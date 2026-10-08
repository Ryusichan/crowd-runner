using System.Xml;
using UnityEditor.Android;

namespace CrowdRunner.EditorTools
{
    /// <summary>
    /// **측정 앱이 잠금화면 위에 뜨게 한다.**
    ///
    /// 오너 폰에 보안 잠금이 걸려 있어서 `adb` 로 깨워도 앱이 1.2 초 만에 잠금화면에 밀렸다
    /// (`APP_CMD_GAINED_FOCUS` → 1.2 s → `APP_CMD_PAUSE`). 그러면 측정기가 **정지를 잰다** —
    /// 1 차 주행이 117 초에 9 프레임이었던 두 번째 이유다. `wm dismiss-keyguard` 는 보안 잠금을
    /// 못 넘는다.
    ///
    /// 사람이 폰을 풀어 줘야 하는 구조로 두면 **측정할 때마다 사람이 필요하다.** 오너는 원격으로
    /// 일하므로 그건 측정이 아니라 약속이 된다. 그래서 앱 쪽에서 해결한다.
    ///
    /// **매니페스트를 통째로 쓰지 않고 두 속성만 덧붙인다.** `Assets/Plugins/Android/AndroidManifest.xml`
    /// 로 대체하면 Unity 가 쓰던 테마·`configChanges`·GameActivity 이름을 내가 다시 적어야 하고,
    /// 그중 하나만 틀려도 **앱이 안 뜨는데 이유는 안 보인다.** Unity 가 만든 것을 고치는 쪽이 싸다.
    ///
    /// 이것은 **측정용 빌드에만** 의미가 있는 설정이다 — 출하 앱이 잠금화면을 넘을 이유는 없다.
    /// `M0Build` 가 만드는 APK 에만 붙으므로 따로 끄는 장치를 두지 않았다.
    /// </summary>
    public sealed class M0ManifestPatch : IPostGenerateGradleAndroidProject
    {
        public int callbackOrder => 0;

        public void OnPostGenerateGradleAndroidProject(string path)
        {
            string file = System.IO.Path.Combine(path, "src/main/AndroidManifest.xml");
            if (!System.IO.File.Exists(file))
            {
                UnityEngine.Debug.LogWarning("[M0] 매니페스트를 못 찾았다: " + file + " — 잠금화면 위로 안 뜬다");
                return;
            }
            var doc = new XmlDocument();
            doc.Load(file);
            const string ns = "http://schemas.android.com/apk/res/android";
            int patched = 0;
            foreach (XmlNode a in doc.GetElementsByTagName("activity"))
            {
                var el = a as XmlElement;
                if (el == null) continue;
                el.SetAttribute("showWhenLocked", ns, "true");
                el.SetAttribute("turnScreenOn", ns, "true");
                patched++;
            }
            doc.Save(file);
            // **몇 개를 고쳤는지 찍는다.** 0 이면 액티비티를 못 찾은 것이고, 그 0 은 조용히
            // 지나가면 다음 측정에서 또 정지를 재게 된다
            UnityEngine.Debug.Log($"[M0] manifest patched activities={patched} (showWhenLocked · turnScreenOn)");
        }
    }
}
