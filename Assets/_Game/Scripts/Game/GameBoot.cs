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

        /// <summary>장면을 세우고 한 판을 띄운다. 테스트도 이것을 쓴다</summary>
        public static LevelRunner Go(string level)
        {
            var go = new GameObject("CrowdRunner");
            Object.DontDestroyOnLoad(go);
            var runner = go.AddComponent<LevelRunner>();
            runner.levelName = level;
            // **`LevelView` 를 `LevelRunner` 뒤에 붙인다.** `LevelRunner.Awake` 가 `Load` 를
            // 부르므로 그때 뷰가 있어야 게이트·벽이 세워진다 — `AddComponent` 가 즉시
            // `Awake` 를 돌리기 때문에 순서가 결과를 바꾼다
            go.AddComponent<LevelView>();
            Runner = runner;
            if (runner.Sim == null)
                Debug.LogError("[CR] 판을 못 띄웠다: " + runner.Error);
            else
                Debug.Log($"[CR] level={level} units={runner.Sim.Units} road={runner.Sim.RoadWidth:F1}m " +
                          $"events={runner.Level.events.Count}");
            return runner;
        }
    }
}
