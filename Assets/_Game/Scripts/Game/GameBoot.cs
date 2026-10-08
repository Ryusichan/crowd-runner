using CrowdRunner.Game;
using CrowdRunner.View;
using UnityEngine;

namespace CrowdRunner
{
    /// <summary>
    /// **장면을 코드로 세운다** — 씬 파일에는 아무것도 담지 않는다.
    ///
    /// 좀비퀸에서 가져오는 "방법" 하나다 (`DESIGN.md` §0): 장면이 코드면 에디터를 열지 않고
    /// 바꿀 수 있다. 그리고 지금은 그것이 규칙이기도 하다 (오너 2026-10-08: Unity 를 모니터에
    /// 띄우지 않는다). 씬에 직렬화된 값이 있으면 그 값을 고치려고 에디터를 열어야 한다.
    ///
    /// **진입은 하나다**: `LevelRunner` + `LevelView` 를 한 오브젝트에 얹고, 판은
    /// `LevelRunner.Load(name)` 으로 넣는다 (세션 A 가 그 문을 하나로 뒀다 — 문이 둘이면
    /// 한쪽만 초기화를 빼먹고 *"두 번째 판부터 이상하다"* 가 된다).
    ///
    /// `CR_M0` 정의가 있으면 **측정용 빌드**라 여기는 비켜난다 (`Crowd/M0Boot` 가 대신 선다).
    /// 한 빌드가 둘을 다 하려 들면 측정 장면에 게임이 섞이고, 그러면 M0 의 수가 거짓이 된다.
    /// </summary>
    public static class GameBoot
    {
        /// <summary>바깥(테스트·메타 화면)이 이 하나를 들고 판을 바꾼다</summary>
        public static LevelRunner Runner { get; private set; }

        /// <summary>
        /// **메타 화면이 붙을 캔버스** — 월드맵·스테이지 카드·결과가 여기 자식으로 들어온다.
        ///
        /// 세션 A 가 *"메타가 자기 캔버스를 들어도 되나"* 라고 물었는데, **여기서 든다.**
        /// 이유는 둘이다. ① 장면을 세우는 일이 한 곳이어야 한다 (차선 규칙이자 `DESIGN.md` §0:
        /// 씬에 직렬화된 값을 두지 않는다). ② 캔버스가 둘이면 **정렬 순서와 안전 영역이 두 벌**이
        /// 되고, 그 둘이 어긋나면 *"어떤 기기에서만 버튼이 노치에 가린다"* 로만 보인다 —
        /// 좀비퀸에서 한 번 치른 값이다.
        ///
        /// `sortingOrder 100` 은 게임 위다. 메타가 떠 있는 동안 게임은 멈추는 것이 아니라
        /// **가려진다** — 멈추는 것은 `LevelRunner` 쪽 결정이고 표현이 정할 일이 아니다.
        /// </summary>
        public static Canvas Overlay { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Boot()
        {
#if CR_M0
            return;     // 측정용 빌드 — `M0Boot` 가 선다
#else
            if (!Application.isPlaying) return;
            Go("1-1");
#endif
        }

        /// <summary>
        /// **장면을 통째로 지운다** — 테스트가 다음 것을 깨끗하게 시작할 때.
        ///
        /// `Go` 는 **판**만 지우고 오버레이는 살려 둔다 (판이 바뀌어도 메타 화면은 살아야 하니까).
        /// 그런데 테스트는 판이 아니라 **처음부터** 다시 시작한다. 오버레이가 남으면 앞 테스트의
        /// 메타 화면 패널이 자식으로 남아 새 화면을 **가린다** — 글자는 그려지는데 위에 덮인 판
        /// 때문에 안 보이고, 검사는 *글자가 지정한 색에 못 닿는다* 로 읽는다.
        ///
        /// 실제로 그렇게 나왔다: 서로 다른 글자 둘이 **소수점 일곱 자리까지 같은 도달값**(9.3%)을
        /// 냈다. 같은 것에 덮여 있었던 것이다. 월드 쪽 샘을 막다가 **UI 쪽에 같은 샘**을 만든
        /// 셈이고, 그래서 지우는 일은 두 곳이 아니라 **여기 한 곳**에 둔다.
        /// </summary>
        public static void Reset()
        {
            // **이미 죽은 것을 건너뛴다.** 뿌리 하나를 즉시 지우면 그 자식들도 같이 죽는데,
            // 배열은 그 자식들의 **죽은 참조**를 그대로 들고 있다 — 거기에 `.transform` 을
            // 물으면 `MissingReferenceException` 이다. Unity 의 `== null` 은 파괴된 것도 참이다
            var all = Object.FindObjectsByType<GameObject>(FindObjectsSortMode.None);
            foreach (var o in all)
            {
                if (o == null) continue;
                if (o.transform.parent != null) continue;
                if (o.name != "CrowdRunner" && o.name != "MetaFlow" && o.name != "Overlay") continue;
                Object.DestroyImmediate(o);
            }
            Runner = null;
            Overlay = null;
        }

        /// <summary>장면을 세우고 한 판을 띄운다. 테스트도 이것을 쓴다</summary>
        public static LevelRunner Go(string level)
        {
            // **앞 판을 지운다.** 안 지우면 판마다 길·게이트·`Sun` 이 **쌓인다.** 조명이 넷이면
            // 0.85 × 4 라 화면이 통째로 하얗게 날아가고, 그 증상은 *"길 색이 안 먹는다"* 로만
            // 보인다 — 머티리얼은 멀쩡한데. 네 번째 판부터 그림이 못 쓰게 되는데 첫 판은
            // 멀쩡하므로 **처음 보는 사람은 레벨 탓을 한다**.
            //
            // M0 에서 같은 종류를 이미 한 번 치렀다 (`docs/M0_CROWD.md` §7: 렌더러가 구간
            // 사이로 유닛을 흘려 빈 장면이 14.33 ms 를 찍었다). 남은 것이 다음 측정에 섞이는
            // 결함은 **그럴듯한 수**를 내놓기 때문에 조용히 오래 간다
            if (Runner != null && Runner.gameObject != null)
            {
                // **즉시** 지운다. `Destroy` 는 프레임 끝까지 미뤄지는데, 그 사이에 새 판이
                // 서면 한 프레임 동안 길과 해가 둘이고 — 그 프레임에 화면을 찍으면 하얗게
                // 나온다. 바로 그 한 프레임을 찍는 것이 이 저장소의 촬영 기계가 하는 일이다
                Object.DestroyImmediate(Runner.gameObject);
                Runner = null;
            }
            var go = new GameObject("CrowdRunner");
            // **편집 모드에서는 부르지 않는다.** `DontDestroyOnLoad` 는 플레이 모드 전용이고
            // 에디터 스크립트에서 부르면 예외가 난다 — 화면 찍는 기계(`MetaShots`)가 바로
            // 그 자리에서 죽었다. 플레이 중에는 장면이 바뀌어도 살아야 하므로 조건으로 둔다
            if (Application.isPlaying) Object.DontDestroyOnLoad(go);
            var runner = go.AddComponent<LevelRunner>();
            runner.levelName = level;
            // **`LevelView` 를 `LevelRunner` 뒤에 붙인다.** `LevelRunner.Awake` 가 `Load` 를
            // 부르므로 그때 뷰가 있어야 게이트·벽이 세워진다 — `AddComponent` 가 즉시
            // `Awake` 를 돌리기 때문에 순서가 결과를 바꾼다
            go.AddComponent<LevelView>();
            // **오버레이는 판보다 오래 산다.** 판 오브젝트의 자식으로 두면 다음 판을 띄울 때
            // 같이 죽고, 메타 화면(월드맵·카드·결과)은 **그 캔버스의 자식**이라 통째로 사라진다.
            // 증상은 "결과 화면이 빈다" 인데 원인은 레벨을 바꾼 쪽에 있다 — 가장 찾기 어려운 모양
            if (Overlay == null) Overlay = BuildOverlay();
            EnsureEventSystem(Overlay.transform);
            Runner = runner;
            AssertOneWorld();
            if (runner.Sim == null)
                Debug.LogError("[CR] 판을 못 띄웠다: " + runner.Error);
            else
                Debug.Log($"[CR] level={level} units={runner.Sim.Units} road={runner.Sim.RoadWidth:F1}m " +
                          $"events={runner.Level.events.Count}");
            return runner;
        }

        /// <summary>
        /// **세상이 하나인지 센다.** 길이 둘이면 그림이 틀리는데, 틀린 그림은 *레벨이 이상하다*
        /// 로 보인다. 지우는 쪽을 고쳐 놓았어도 **세는 쪽을 같이 둔다** — 다음에 누가 다른 문으로
        /// 장면을 세우면 그 문에서 다시 샐 것이고, 그때 이 줄이 자리를 바로 가리킨다.
        /// </summary>
        static void AssertOneWorld()
        {
            int suns = 0, roads = 0;
            foreach (var l in Object.FindObjectsByType<Light>(FindObjectsSortMode.None))
                if (l.type == LightType.Directional) suns++;
            foreach (var t in Object.FindObjectsByType<Transform>(FindObjectsSortMode.None))
                if (t.name == "Road") roads++;
            if (suns > 1 || roads > 1)
                Debug.LogError($"[CR] 앞 판이 안 지워졌다 — 해 {suns} 개 · 길 {roads} 개. " +
                               "조명이 겹쳐 화면이 하얗게 날아가고, 그 증상은 '길 색이 안 먹는다' 로만 보인다");
        }

        /// <summary>
        /// **클릭을 받는 장치.** 없으면 uGUI 가 입력을 하나도 안 받고, 버튼이 **보이는데 안 눌린다** —
        /// 그 증상은 "버튼이 안 먹는다" 로만 보여서 원인을 찾기 어렵다.
        ///
        /// 세션 A 가 `MetaFlow` 쪽에 임시로 두었던 것을 여기로 옮겼다 (장면 세우는 일은 한 곳).
        /// **둘이 서면 Unity 가 경고한다** — 그래서 `MetaFlow.EnsureEventSystem` 은 지웠다.
        /// </summary>
        static void EnsureEventSystem(Transform parent)
        {
            if (UnityEngine.EventSystems.EventSystem.current != null) return;
            var go = new GameObject("EventSystem");
            go.transform.SetParent(parent, false);
            go.AddComponent<UnityEngine.EventSystems.EventSystem>();
            go.AddComponent<UnityEngine.EventSystems.StandaloneInputModule>();
        }

        static Canvas BuildOverlay()
        {
            var go = new GameObject("Overlay");
            if (Application.isPlaying) Object.DontDestroyOnLoad(go);
            var c = go.AddComponent<Canvas>();
            c.renderMode = RenderMode.ScreenSpaceOverlay;
            c.sortingOrder = 100;
            var sc = go.AddComponent<UnityEngine.UI.CanvasScaler>();
            // 세로형 기준 해상도. **높이로 맞춘다**: 세로 게임에서 폭은 기기마다 크게 다르고
            // (18:9 ~ 20:9) 높이를 기준으로 하면 위아래 배치가 기기마다 안 움직인다
            sc.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
            sc.referenceResolution = new Vector2(1080f, 1920f);
            sc.matchWidthOrHeight = 1f;
            go.AddComponent<UnityEngine.UI.GraphicRaycaster>();
            return c;
        }
    }
}
