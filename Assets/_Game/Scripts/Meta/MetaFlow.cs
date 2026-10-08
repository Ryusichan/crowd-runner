using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using CrowdRunner.Core;
using CrowdRunner.Game;
using CrowdRunner.UI;

namespace CrowdRunner.Meta
{
    public enum MetaScreen { WorldMap, StageCard, Playing, Result }

    /// <summary>
    /// **메타 껍데기** — 월드맵 → 스테이지 카드 → 한 판 → 결과 → 월드맵.
    ///
    /// 오너 지시로 좀비퀸의 그 흐름을 그대로 가져온다 (`docs/DESIGN.md` §3b): *"부산부터
    /// 시작하는 우리가 만들어 놓은 좀비퀸 화면"*. 이 장르의 약점은 기획서 §14 가 짚은
    /// *"건설과 RPG 를 빼면 장기 동기가 약해진다"* 인데 **정복 지도가 그 자리를 메운다.**
    ///
    /// 구조 규칙 셋 (좀비퀸 `GameFlow` 에서 그대로):
    /// ① **화면마다 상태를 들지 않는다.** `Show()` 가 데이터로 통째로 다시 짓는다 —
    ///    화면이 자기 상태를 들면 두 곳의 진실이 갈라지고, 그게 *"돌아오면 ☣ 가 안 맞는다"* 가 된다.
    /// ② **`GameManager`/`LevelRunner` 를 만들거나 지우지 않는다.** 멈추고(`Paused`) 가린다.
    ///    판을 만들고 지우면 그 사이에 뷰·군중이 다시 서느라 화면이 끊긴다.
    /// ③ **움직임은 `UiAnim` 에서만 고른다.** 손으로 `Lerp` 를 쓰면 꺾이는 자리가 보인다
    ///    (오너 2026-09-30: 100 → 110 → 100).
    /// </summary>
    public partial class MetaFlow : MonoBehaviour
    {
        public static MetaFlow I { get; private set; }

        /// <summary>
        /// `AfterSceneLoad` 다 — `GameBoot` 가 `BeforeSceneLoad` 에서 캔버스와 러너를 세우므로
        /// 그 뒤에 서야 `GameBoot.Overlay` 가 있다. 순서를 뒤집으면 **부모가 없어서 화면이
        /// 안 보이고**, 그건 "아무 일도 안 일어난다" 로만 나타난다.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install()
        {
#if CR_M0
            return;     // 측정용 빌드에는 메타가 없다
#else
            if (!Application.isPlaying || I != null) return;
            if (GameBoot.Overlay == null) { Debug.LogError("[CR] 오버레이 캔버스가 없다 — GameBoot 가 안 섰다"); return; }
            var go = new GameObject("MetaFlow");
            go.transform.SetParent(GameBoot.Overlay.transform.parent, false);
            go.AddComponent<MetaFlow>();
#endif
        }

        RectTransform root;
        MetaScreen screen;
        LevelData current;          // 카드에 띄운 / 플레이 중인 판
        bool resultWon;
        int resultRemaining;
        int resultGrade;

        /// <summary>지금 보고 있는 도시 (1 = 부산). 도시가 늘면 월드맵에 탭이 붙는다</summary>
        public int City = 1;

        void Awake()
        {
            I = this;
            EnsureEventSystem();

            root = UiKit.Rect("MetaRoot", GameBoot.Overlay.transform);
            UiKit.Stretch(root);

            // 레벨이 하나도 못 읽혔으면 **그것을 화면에 띄운다.** 빈 월드맵이 뜨면
            // "게임이 안 나온다" 로만 보이고 원인이 글 한 줄이라는 것을 못 찾는다
            if (LevelCatalog.All.Count == 0)
            {
                ShowBroken();
                return;
            }
            Show(MetaScreen.WorldMap);
        }

        void OnDestroy() { if (I == this) I = null; }

        /// <summary>
        /// uGUI 는 `EventSystem` 이 없으면 **클릭을 하나도 받지 않는다.** 버튼이 보이는데
        /// 눌리지 않으므로 *"버튼이 안 먹는다"* 로만 보인다 — 좀비퀸에서 한 번 겪었다.
        /// `GameBoot` 가 캔버스를 세우므로 원래 그쪽 일이지만, 없으면 **내 화면이 죽는다.**
        /// 그래서 없을 때만 세우고 로그를 남긴다 (두 벌이 서면 Unity 가 경고한다).
        /// </summary>
        static void EnsureEventSystem()
        {
            if (EventSystem.current != null) return;
            var go = new GameObject("EventSystem");
            Object.DontDestroyOnLoad(go);
            go.AddComponent<EventSystem>();
            go.AddComponent<StandaloneInputModule>();
            Debug.Log("[CR] EventSystem 이 없어서 MetaFlow 가 세웠다");
        }

        // ── 화면 전환 ────────────────────────────────────────────────────────────
        public void Show(MetaScreen s)
        {
            screen = s;
            for (int i = root.childCount - 1; i >= 0; i--) Destroy(root.GetChild(i).gameObject);

            var runner = GameBoot.Runner;
            bool playing = s == MetaScreen.Playing;
            root.gameObject.SetActive(!playing);
            if (runner != null) runner.Paused = !playing;

            if (playing) return;

            switch (s)
            {
                case MetaScreen.WorldMap: BuildWorldMap(); break;
                case MetaScreen.StageCard: BuildStageCard(); break;
                case MetaScreen.Result: BuildResult(); break;
            }
            // **화면 전환은 각 화면이 아니라 이 길목에서 건다.** 화면마다 손으로 넣으면
            // 새 화면을 만든 사람이 빠뜨리고, 그게 오너가 "끊어진다" 고 한 자리다
            UiAnim.ScreenIn(root);
        }

        /// <summary>스테이지 카드를 띄운다 (월드맵에서 탭)</summary>
        public void OpenCard(LevelData level)
        {
            current = level;
            Show(MetaScreen.StageCard);
        }

        /// <summary>한 판을 시작한다. **판을 만들지 않고 이름만 바꿔 끼운다**</summary>
        public void StartLevel(LevelData level)
        {
            current = level;
            var runner = GameBoot.Runner;
            if (runner == null) { Debug.LogError("[CR] 러너가 없다 — GameBoot 가 안 섰다"); return; }
            if (!runner.Load(level.Code))
            {
                Debug.LogError("[CR] " + runner.Error);
                Show(MetaScreen.WorldMap);
                return;
            }
            Show(MetaScreen.Playing);
        }

        void Update()
        {
            if (screen != MetaScreen.Playing) return;
            var runner = GameBoot.Runner;
            if (runner == null || runner.Sim == null) return;

            var st = runner.Sim.State;
            if (st != SimState.Won && st != SimState.Lost) return;

            // **끝난 판을 한 번만 집는다.** 여기서 바로 `Show(Result)` 로 나가므로
            // `screen` 이 바뀌어 다음 프레임에 다시 들어오지 않는다
            resultWon = st == SimState.Won;
            resultRemaining = runner.Sim.Units;
            resultGrade = Grade.Of(resultWon, resultRemaining, current?.rating);
            MetaSave.Report(current.Code, resultWon, resultRemaining, current?.rating);
            Show(MetaScreen.Result);
        }

        /// <summary>결과 화면의 "다음 판" — 없으면 null</summary>
        LevelData NextLevel()
        {
            if (current == null) return null;
            return LevelCatalog.Find(current.chapter + "-" + (current.index + 1));
        }
    }
}
