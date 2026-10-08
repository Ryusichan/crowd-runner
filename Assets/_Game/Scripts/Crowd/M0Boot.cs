using UnityEngine;

namespace Game.Crowd
{
    /// <summary>
    /// **M0 장면을 코드로 세운다** — 씬 파일에 아무것도 담지 않는다.
    ///
    /// 좀비퀸에서 가져오는 "방법" 하나다 (`DESIGN.md` §0): 장면이 코드면 **에디터를 열지 않고**
    /// 바꿀 수 있고, 지금 그것이 규칙이기도 하다 (오너 2026-10-08: 모니터에 Unity 를 띄우지 않는다).
    /// 씬에 직렬화된 설정이 있으면 그 설정을 고치려고 에디터를 열어야 한다.
    /// </summary>
    public static class M0Boot
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Boot()
        {
            if (!Application.isPlaying) return;
#if !UNITY_EDITOR
            Go();
#endif
        }

        /// <summary>에디터에서도 같은 장면을 만들 수 있게 따로 둔다 (테스트가 부른다)</summary>
        public static GameObject Go()
        {
            var camGo = new GameObject("M0Camera");
            var cam = camGo.AddComponent<Camera>();
            // 세로형 러너의 시점 — 뒤에서 내려다본다. 1,000 개체가 **한 화면에 들어와야** 측정이
            // 의미가 있다: 화면 밖은 그려지지 않으므로, 카메라가 좁으면 측정이 쉬워진다
            camGo.transform.position = new Vector3(0f, 22f, -34f);
            camGo.transform.rotation = Quaternion.Euler(28f, 0f, 0f);
            cam.fieldOfView = 55f;
            cam.farClipPlane = 220f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.55f, 0.62f, 0.52f);

            var lightGo = new GameObject("M0Light");
            var li = lightGo.AddComponent<Light>();
            li.type = LightType.Directional;
            li.intensity = 1.1f;
            // **그림자를 끈다**: 1,000 개체의 그림자는 드로우를 두 배로 만들고, 그러면 측정이
            // *군중 비용* 이 아니라 *그림자 정책* 을 재게 된다. 그림자는 M0 뒤에 따로 잴 자리다
            li.shadows = LightShadows.None;
            lightGo.transform.rotation = Quaternion.Euler(50f, -30f, 0f);

            var bench = new GameObject("M0Bench");
            bench.AddComponent<M0Bench>();
            Object.DontDestroyOnLoad(bench);
            return bench;
        }
    }
}
