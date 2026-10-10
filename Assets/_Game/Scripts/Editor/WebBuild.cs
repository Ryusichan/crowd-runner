using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace CrowdRunner.EditorTools
{
    /// <summary>
    /// **웹에 올릴 빌드** — 오너가 직접 해 보실 수 있는 유일한 형태.
    ///
    /// `M0Build` 는 측정용 APK 라 `CR_M0` 를 박아 넣는다 (그래야 `GameBoot` 가 비켜난다).
    /// 이쪽은 **게임**이므로 그 정의를 넣지 않는다. 한 빌드가 둘을 다 하려 들면 측정 장면에
    /// 게임이 섞이고, 그러면 M0 의 수가 거짓이 된다.
    ///
    /// <code>Unity -batchmode -quit -projectPath . -buildTarget WebGL -executeMethod CrowdRunner.EditorTools.WebBuild.Web</code>
    ///
    /// 출력은 `Builds/WebGL` — 배포 스크립트가 거기를 본다.
    ///
    /// ## 한 호출 안에 네 가지가 있는 이유
    ///
    /// ① 셰이더 등록 ② 군중 자산 굽기 ③ 빈 씬 ④ 빌드. 나눠 두면 사람이 넷을 순서대로 불러야
    /// 하고 Unity 콜드 스타트가 네 번이다 (한 번에 3~8 분). 오너 2026-10-08: *"유니티 돌릴 때
    /// 컴퓨터 부하가 심해서."*
    ///
    /// **순서가 결과를 바꾼다.** 셰이더 등록을 **맨 앞**에 두는 이유: `GraphicsSettings` 를
    /// `SerializedObject` 로 건드린 **직후에** `BuildPlayer` 를 부르면 `unity_builtin_extra`
    /// 쓰기가 잠긴다 (좀비퀸에서 겪은 자리 — 그쪽은 아예 씬 만들 때만 건드린다). 사이에
    /// 굽기가 들어가면 그 사이에 `SaveAssets`/`Refresh` 가 여러 번 돈다.
    /// </summary>
    public static class WebBuild
    {
        const string SceneDir = "Assets/_Game/Scenes";
        const string ScenePath = SceneDir + "/Game.unity";
        const string Out = "Builds/WebGL";

        [MenuItem("CR/Build WebGL")]
        public static void Web()
        {
            EnsureShaders();
            AssetDatabase.SaveAssets();

            // **군중 자산은 저장소에 없다** (`Assets/_Game/Resources/M0` 는 커밋 안 됨 — 구운
            // 결과물이라). 안 구우면 `LevelView` 와 `CasualtyFx` 가 `Resources.Load` 에서
            // null 을 받고, 그러면 **길과 게이트만 있고 사람이 하나도 없는** 게임이 나간다.
            // 에디터에서는 지난번에 구운 것이 남아 있어 멀쩡해 보인다 — 그게 이 줄의 전부다
            M0Assets.Bake(300, "_lo");
            VatBaker.Bake("_lo");
            M0Assets.BakeFlatMaterials();
            AssetDatabase.SaveAssets();
            AssertEverythingLoadedByNameExists();
            EnsureScene();

            PlayerSettings.companyName = "Ryusichan";
            PlayerSettings.productName = "Crowd Runner";
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.Portrait;
            // **세로 캔버스.** 기본값은 960×600 **가로**다 — 폰에서 열면 세로 화면에 가로
            // 상자가 앉고 가로 스크롤바가 생긴다. 세션 A 가 배포 쪽 CSS 로 덮어 두었지만
            // 근본은 여기다: CSS 로만 고치면 **다음에 다른 곳에 올릴 때 또 가로로 나간다**
            PlayerSettings.defaultWebScreenWidth = 540;
            PlayerSettings.defaultWebScreenHeight = 960;

            // **Gzip + 로더 쪽 해제.** 정적 호스팅(GitHub Pages)은 `Content-Encoding` 헤더를 못
            // 주므로 `decompressionFallback` 이 켜져 있어야 로더가 JS 로 직접 푼다.
            // **Brotli 로 하면 안 된다**: 받는 크기는 작지만 JS 브로틀리 해제가 수십 배 느려
            // 로딩이 멈춘 것처럼 보인다 (좀비퀸, 오너 2026-09-18 "로딩이 멈췄어").
            // 판단 기준은 **받는 크기가 아니라 첫 화면까지 걸리는 시간**이다
            PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Gzip;
            PlayerSettings.WebGL.decompressionFallback = true;
            PlayerSettings.WebGL.exceptionSupport = WebGLExceptionSupport.ExplicitlyThrownExceptionsOnly;
            PlayerSettings.WebGL.initialMemorySize = 256;
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.WebGL, ScriptingImplementation.IL2CPP);
            PlayerSettings.SetIl2CppCompilerConfiguration(NamedBuildTarget.WebGL, Il2CppCompilerConfiguration.Release);

            Directory.CreateDirectory("Builds");
            var r = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { ScenePath },
                locationPathName = Out,
                target = BuildTarget.WebGL,
                options = BuildOptions.None,
            });
            var s = r.summary;
            Debug.Log($"[CR] web build {s.result} size={s.totalSize / 1024 / 1024}MB " +
                      $"errors={s.totalErrors} time={s.totalTime.TotalMinutes:F1}min out={Out}");
            if (s.result != BuildResult.Succeeded) EditorApplication.Exit(1);
        }

        /// <summary>
        /// **이름으로 찾는 것이 정말 거기 있는가.**
        ///
        /// `Resources.Load("...")` 는 없으면 **null 을 돌려주고 끝난다.** 예외도 경고도 없다.
        /// 그래서 빠진 자산은 *오류*가 아니라 **조용한 빈칸**으로 나타난다:
        /// 군중이 없으면 길과 게이트만 달리고, 글꼴이 없으면 단추가 빈 네모다.
        ///
        /// 그리고 **둘 다 에디터에서는 멀쩡해 보인다** — 군중은 지난번에 구운 것이 남아 있고,
        /// 글꼴은 OS 폰트로 떨어진다. **WebGL 엔 OS 폰트가 없다.** 그래서 이 결함류는
        /// *웹에 올려야만* 드러나고, 그때는 이미 오너가 열어 본 뒤다.
        ///
        /// 실제로 두 번 그랬다. 군중은 빌드에 굽기를 넣어 막았고, 한글 글꼴은 **오너가
        /// *"플레이 할 수가 없어"* 라고 하신 뒤에** 찾았다 (2026-10-09). 그래서 하나씩 막는
        /// 대신 **이름으로 찾는 것 전부**를 여기서 센다.
        ///
        /// 있어야 하는 것은 빌드를 **멈추고**, `??` 로 대안이 적힌 것은 **경고만** 한다 —
        /// 코드가 없어도 된다고 말하는 것을 검사가 반대로 말하면 그 검사는 곧 꺼진다.
        /// </summary>
        static void AssertEverythingLoadedByNameExists()
        {
            // 없으면 게임이 **조용히 빈다**
            var must = new (string path, string what)[]
            {
                ("Assets/_Game/Resources/M0/standin_vat_mesh_lo.asset", "군중의 몸 — 없으면 길과 게이트만 달린다"),
                ("Assets/_Game/Resources/M0/standin_vat_lo.mat",        "군중의 재질 — 같은 결과"),
                ("Assets/_Game/Resources/M0/standin_vat_lo_foe.mat",    "적의 재질 — 없으면 적이 아군 색으로 나온다"),
                ("Assets/_Game/Resources/Fonts/Jua-Regular.ttf",        "본문 글꼴 — 없으면 웹에서 글자가 통째로 안 나온다 (OS 폰트가 없다)"),
                ("Assets/_Game/Resources/Fonts/ZQFallbackGothic.ttf",   "대체 글꼴 — `·` `—` 같은 글리프가 여기로 넘어간다"),
                ("Assets/_Game/Resources/M0/roadmark.mat",              "길 표식 — 없으면 달리는 것이 안 보인다"),
                ("Assets/_Game/Resources/M0/blobshadow.mat",            "발밑 그림자 — 없으면 군중이 떠 보인다"),
                ("Assets/_Game/Resources/M0/roadtree.mat",              "길가 나무 — 없으면 길 밖이 초록 허공이다"),
            };
            bool bad = false;
            foreach (var (path, what) in must)
            {
                if (File.Exists(path)) { Debug.Log($"[CR] 있다: {Path.GetFileName(path)}"); continue; }
                Debug.LogError($"[CR] **{path} 가 없다** — {what}. 이대로 빌드하면 예외 없이 " +
                               "그 부분만 비어서 나간다. 빌드를 멈춘다");
                bad = true;
            }

            // `UiKit` 이 `??` 로 대안을 적어 둔 것들 — 없어도 돌지만, 없는 줄 모르고 쓰면
            // *"아이콘이 왜 네모지"* 가 된다
            foreach (var (path, what) in new (string, string)[]
            {
                ("Assets/_Game/Resources/Fonts/ZQIcons.ttf", "아이콘 글꼴 (UiKit.Icon)"),
                ("Assets/_Game/Resources/UI",                "UI/icon · UI/frame 그림"),
                ("Assets/_Game/Resources/Owner",             "오너 시안 그림 (UiKit 의 Owner/UI/*)"),
            })
            {
                if (File.Exists(path) || Directory.Exists(path)) continue;
                Debug.LogWarning($"[CR] {path} 가 없다 — {what}. `UiKit` 이 그 경로를 부르는데 " +
                                 "대안이 적혀 있어 돌기는 한다 (좀비퀸에서 코드만 가져온 자리)");
            }

            // 열 판 — 하나라도 없으면 그 판에서 멈춘다
            for (int i = 1; i <= 10; i++)
            {
                string p = $"Assets/_Game/Resources/Levels/1-{i}.txt";
                if (File.Exists(p)) continue;
                Debug.LogError($"[CR] **{p} 가 없다** — 그 판을 못 연다");
                bad = true;
            }

            if (bad) EditorApplication.Exit(1);
        }

        /// <summary>
        /// **런타임에 `Shader.Find` 로 찾는 셰이더는 빌드에서 잘린다.** 아무도 참조하지 않으니
        /// 빌드가 안 쓰는 것으로 보기 때문이다. 증상은 **에디터에서는 멀쩡하고 웹에서만**
        /// 분홍색이거나 흰색이다 — 가장 늦게 발견되는 모양이다.
        ///
        /// `Game/CrowdVat` 은 넣지 않아도 된다: `Resources/M0` 의 머티리얼 자산이 참조하므로
        /// 그 의존성으로 따라 들어간다. 잘리는 것은 **머티리얼 없이 이름으로만 찾는** 쪽이다.
        ///
        /// `GUI/Text Shader` 같은 *unity default resources* 는 **넣지 않는다** — 넣으면
        /// `unity_builtin_extra` 쓰기가 실패한다 (좀비퀸에서 겪음).
        /// </summary>
        static void EnsureShaders()
        {
            var gs = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/GraphicsSettings.asset");
            if (gs == null || gs.Length == 0) { Debug.LogError("[CR] GraphicsSettings 를 못 읽었다"); return; }
            var so = new SerializedObject(gs[0]);
            var arr = so.FindProperty("m_AlwaysIncludedShaders");
            if (arr == null) { Debug.LogError("[CR] m_AlwaysIncludedShaders 가 없다"); return; }

            foreach (var name in new[] { "Legacy Shaders/Diffuse", "Standard" })
            {
                var sh = Shader.Find(name);
                if (sh == null) { Debug.LogError($"[CR] 셰이더 '{name}' 를 못 찾았다 — 웹에서 그 네모들이 안 보인다"); continue; }
                bool has = false;
                for (int i = 0; i < arr.arraySize; i++)
                    if (arr.GetArrayElementAtIndex(i).objectReferenceValue == sh) { has = true; break; }
                Debug.Log($"[CR] 셰이더 '{name}' {(has ? "이미 들어 있다" : "추가한다")}");
                if (has) continue;
                arr.InsertArrayElementAtIndex(arr.arraySize);
                arr.GetArrayElementAtIndex(arr.arraySize - 1).objectReferenceValue = sh;
            }
            so.ApplyModifiedProperties();
        }

        /// <summary>
        /// **빈 씬 하나.** 장면은 `GameBoot` 가 코드로 세운다 (`DESIGN.md` §0) — 씬 파일에 담으면
        /// 그것을 고치려고 에디터를 열어야 하고, 지금 그게 금지다 (오너 2026-10-08).
        ///
        /// `M0.unity` 를 재사용하지 않는 이유: 이름이 측정용이라 다음 사람이 그 씬을 고치면
        /// 어느 빌드가 바뀌는지 헷갈린다.
        /// </summary>
        static void EnsureScene()
        {
            Directory.CreateDirectory(SceneDir);
            if (File.Exists(ScenePath)) return;
            var sc = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            EditorSceneManager.SaveScene(sc, ScenePath);
            Debug.Log("[CR] created empty scene " + ScenePath);
        }
    }
}
