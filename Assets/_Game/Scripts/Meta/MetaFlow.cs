using UnityEngine;
using UnityEngine.UI;
using CrowdRunner.Core;
using CrowdRunner.Game;
using CrowdRunner.UI;

namespace CrowdRunner.Meta
{
    public enum MetaScreen { Intro, WorldMap, StageCard, Playing, Paused, Result }

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

        /// <summary>
        /// **판이 도는 동안에도 남아 있는 것** — 나가기 단추 하나. `root` 는 판 중에 꺼지므로
        /// 거기 두면 같이 꺼진다.
        ///
        /// 왜 필요한가: 지금 구조에서는 판에 들어가면 **나올 길이 없다.** 끝까지 지거나
        /// 이겨야 한다. 1~2 분이라 견딜 만하지만, 잘못 고른 것을 아는 순간이 10 초쯤이고
        /// 그때 다시 할 수 없으면 **고른 것을 배우는 데 90 초가 든다.**
        /// </summary>
        RectTransform hud;

        MetaScreen screen;
        LevelData current;          // 카드에 띄운 / 플레이 중인 판
        bool resultWon;
        int resultRemaining;
        int resultGrade;

        /// <summary>
        /// **등장 애니메이션을 건너뛴다** — 화면을 찍을 때.
        ///
        /// 세션 B 의 첫 월드맵 스크린샷이 **온 화면 알파 10 %** 로 나왔다. 제목 "부산" 의
        /// 화소가 배경과 **같았다**(27,28,37 vs 25,28,36). 색이 틀린 것이 아니라 `ScreenIn`
        /// 이 띄우는 **중간 프레임**을 찍은 것이다 — 그 그림으로는 대비를 판단할 수 없는데,
        /// 보는 사람은 *"디자인이 어둡다"* 로 읽는다. **잴 수 없는 것으로 판단하게 만드는
        /// 그림이 가장 비싸다.**
        ///
        /// 그래서 끄는 스위치를 둔다. 게임에는 영향이 없다 (기본 false).
        /// </summary>
        public static bool Instant;

        /// <summary>지금 보고 있는 도시 (1 = 부산). 도시가 늘면 월드맵에 탭이 붙는다</summary>
        public int City = 1;

        void Awake()
        {
            I = this;
            // `EventSystem` 은 `GameBoot` 이 세운다 (장면 세우는 일은 한 곳 — 둘이 서면 Unity 가 경고한다)

            root = UiKit.Rect("MetaRoot", GameBoot.Overlay.transform);
            UiKit.Stretch(root);

            hud = UiKit.Rect("MetaHud", GameBoot.Overlay.transform);
            UiKit.Stretch(hud);
            BuildHud();
            hud.gameObject.SetActive(false);

            // 레벨이 하나도 못 읽혔으면 **그것을 화면에 띄운다.** 빈 월드맵이 뜨면
            // "게임이 안 나온다" 로만 보이고 원인이 글 한 줄이라는 것을 못 찾는다
            if (LevelCatalog.All.Count == 0)
            {
                ShowBroken();
                return;
            }
            // 처음 켰으면 **조작을 한 번 알려 준다.** 이 게임의 유일한 조작이 "손가락을
            // 좌우로 끈다" 인데, 아무 안내가 없으면 플레이어는 **가만히 서서 지는 것을**
            // 먼저 본다 (오른쪽에 붙어 있으면 8/10 이 깨지므로 그조차 모르고 지나갈 수 있다)
            Show(MetaSave.Data.introSeen ? MetaScreen.WorldMap : MetaScreen.Intro);
        }

        void OnDestroy() { if (I == this) I = null; }

        // ── 화면 전환 ────────────────────────────────────────────────────────────
        public void Show(MetaScreen s)
        {
            screen = s;
            for (int i = root.childCount - 1; i >= 0; i--) Destroy(root.GetChild(i).gameObject);

            var runner = GameBoot.Runner;
            bool playing = s == MetaScreen.Playing;
            root.gameObject.SetActive(!playing);
            if (runner != null) runner.Paused = !playing;

            // 나가기 띠는 **판이 도는 동안과 멈춰 있는 동안** 보인다 — 멈춤 화면에서
            // 사라지면 "어디를 눌러 멈췄는지" 가 화면에서 없어진다
            hud.gameObject.SetActive(playing || s == MetaScreen.Paused);

            if (playing) return;

            switch (s)
            {
                case MetaScreen.Intro: BuildIntro(); break;
                case MetaScreen.WorldMap: BuildWorldMap(); break;
                case MetaScreen.StageCard: BuildStageCard(); break;
                case MetaScreen.Paused: BuildPaused(); break;
                case MetaScreen.Result: BuildResult(); break;
            }
            // **화면 전환은 각 화면이 아니라 이 길목에서 건다.** 화면마다 손으로 넣으면
            // 새 화면을 만든 사람이 빠뜨리고, 그게 오너가 "끊어진다" 고 한 자리다
            if (!Instant) UiAnim.ScreenIn(root);
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
            // `current` 가 없으면 **기록할 곳이 없다.** 지금 흐름에서는 `StartLevel` 이 먼저
            // 넣으므로 안 일어나지만, 여기서 터지면 판이 끝나는 순간 예외가 나고 그것은
            // *"다 깼는데 화면이 안 넘어간다"* 로만 보인다 — 가장 찾기 싫은 모양이다
            if (current == null)
            {
                Debug.LogError("[CR] 끝난 판이 어느 판인지 모른다 — 기록하지 않고 지도로 돌아간다");
                Show(MetaScreen.WorldMap);
                return;
            }

            resultWon = st == SimState.Won;
            resultRemaining = runner.Sim.Units;
            resultGrade = Grade.Of(resultWon, resultRemaining, current.rating);
            MetaSave.Report(current.Code, resultWon, resultRemaining, current.rating);
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
