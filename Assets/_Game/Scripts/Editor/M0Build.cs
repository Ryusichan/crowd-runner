using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Game.EditorTools
{
    /// <summary>
    /// **M0 APK — 한 번의 Unity 주행으로 끝낸다.**
    ///
    /// 오너 2026-10-08: *"유니티 돌릴 때 컴퓨터 부하가 심해서 필요할 때 간략히 확인하는 구조로."*
    /// 그래서 임포트 · 대역 캐릭터 굽기 · 씬 만들기 · APK 빌드가 **한 호출** 안에 있다. 나눠 두면
    /// 사람이 셋을 순서대로 불러야 하고, 그러면 왕복이 세 번이다.
    ///
    /// <code>Unity -batchmode -nographics -quit -projectPath . -executeMethod Game.EditorTools.M0Build.Android</code>
    ///
    /// 에디터 창을 띄우지 않는다 (같은 날 규칙). 측정은 폰에서 돌고 결과는 `adb logcat` 으로 나온다.
    /// </summary>
    public static class M0Build
    {
        const string Pkg = "com.ryusichan.crowdrunner.m0";
        const string SceneDir = "Assets/_Game/Scenes";
        const string ScenePath = SceneDir + "/M0.unity";
        const string Apk = "Builds/M0.apk";

        public static void Android()
        {
            M0Assets.Bake();
            EnsureScene();

            PlayerSettings.companyName = "Ryusichan";
            PlayerSettings.productName = "CrowdRunner M0";
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.Portrait;
            // **측정에 필요한 둘을 켠다.** `FrameTimingManager` 는 이 설정 없이는 늘 0 을 돌려주고,
            // 0 은 *"빠르다"* 와 똑같이 생긴다 (`M0Bench.Report` 가 cpu/gpu 를 찍는 근거)
            PlayerSettings.enableFrameTimingStats = true;
            PlayerSettings.gpuSkinning = true;   // 출하 기본값 — 끄면 방식 A 의 수가 통째로 달라진다
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, Pkg);
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
            PlayerSettings.SetIl2CppCompilerConfiguration(NamedBuildTarget.Android, Il2CppCompilerConfiguration.Release);
            // **ARM64 만.** 측정 기기가 ARM64 라 ARMv7 를 같이 넣으면 빌드 시간만 두 배가 된다 —
            // 지금 아끼는 것이 Unity 주행 시간이다 (오너 2026-10-08 "컴퓨터 부하가 심해서")
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel25;
            // Auto 는 설치된 모듈에 따라 바뀌어 **같은 커밋이 다른 APK** 를 낸다 — 고정한다
            PlayerSettings.Android.targetSdkVersion = AndroidSdkVersions.AndroidApiLevel35;
            PlayerSettings.Android.useCustomKeystore = false;   // 측정용 — 디버그 키스토어
            // 60 fps 로 묶지 않는다 — 묶으면 프레임 시간이 양자화돼 비용이 안 보인다 (`M0Bench.Awake`)
            QualitySettings.vSyncCount = 0;

            EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Android, BuildTarget.Android);
            EditorUserBuildSettings.buildAppBundle = false;
            Directory.CreateDirectory("Builds");

            var opt = new BuildPlayerOptions
            {
                scenes = new[] { ScenePath },
                locationPathName = Apk,
                target = BuildTarget.Android,
                options = BuildOptions.None,
            };
            var report = BuildPipeline.BuildPlayer(opt);
            var s = report.summary;
            Debug.Log($"[M0] build {s.result} size={s.totalSize / 1024 / 1024}MB errors={s.totalErrors} " +
                      $"time={s.totalTime.TotalMinutes:F1}min out={Apk}");
            if (s.result != BuildResult.Succeeded)
                EditorApplication.Exit(1);
        }

        /// <summary>
        /// **빈 씬 하나.** 장면은 `M0Boot` 가 코드로 세우므로(`RuntimeInitializeOnLoadMethod`) 씬 파일에
        /// 담을 것이 없다 — 담으면 그것을 고치려고 에디터를 열어야 하고, 지금 그게 금지다.
        /// </summary>
        static void EnsureScene()
        {
            Directory.CreateDirectory(SceneDir);
            if (File.Exists(ScenePath)) return;
            var sc = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            EditorSceneManager.SaveScene(sc, ScenePath);
            Debug.Log("[M0] created empty scene " + ScenePath);
        }
    }
}
