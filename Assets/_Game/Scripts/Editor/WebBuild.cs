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
            AssertTheCrowdExists();
            EnsureScene();

            PlayerSettings.companyName = "Ryusichan";
            PlayerSettings.productName = "Crowd Runner";
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.Portrait;

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
        /// **구운 것이 정말 거기 있는가.**
        ///
        /// 굽기를 빌드 안에 둔 것만으로는 부족하다는 지적이 있었다 (세션 A): *"굽는 코드가
        /// 깨지면 빌드가 조용히 빈 게임을 낸다."* 맞는 말인데, 그건 **굽기를 빼야 할 이유가
        /// 아니라 구운 뒤에 확인하지 않는 것**이 문제다.
        ///
        /// 없으면 `Resources.Load` 가 null 을 돌려주고, 게임은 **길과 게이트만 있고 사람이
        /// 하나도 없는 채로** 멀쩡히 돈다 — 예외도 안 난다. 그 모양은 *"오늘따라 군중이 안
        /// 보인다"* 로만 보이고, 원인은 빌드 **한참 전**에 있다.
        ///
        /// 여기서 보는 두 경로는 `LevelView`/`CasualtyFx` 가 **실제로 부르는 그 문자열**이어야
        /// 한다. 다른 이름을 보면 이 검사는 통과하면서 게임은 빈다 — 검사가 재는 것과 코드가
        /// 쓰는 것이 어긋나는, 오늘 여러 번 나온 그 자리다.
        /// </summary>
        static void AssertTheCrowdExists()
        {
            foreach (var path in new[] { "Assets/_Game/Resources/M0/standin_vat_mesh_lo.asset",
                                         "Assets/_Game/Resources/M0/standin_vat_lo.mat" })
            {
                if (File.Exists(path)) { Debug.Log($"[CR] 군중 자산 있다: {Path.GetFileName(path)}"); continue; }
                Debug.LogError($"[CR] 굽기가 {path} 를 안 만들었다 — 이대로 빌드하면 " +
                               "**사람이 하나도 없는 게임**이 나간다 (예외는 안 난다). 빌드를 멈춘다");
                EditorApplication.Exit(1);
            }
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
