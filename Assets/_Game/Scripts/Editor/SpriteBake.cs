using System.IO;
using UnityEditor;
using UnityEngine;

namespace CrowdFire.EditorTools
{
    /// <summary>
    /// **3D 모델을 2D 스프라이트 시트로 굽는다** — Cocos 판(`crowd-fire-cocos`)의 재료.
    ///
    /// 오너 2026-10-20: 무거운 3D 대신 탑워처럼 **가벼운 2D**. 그런데 그림을 새로 그릴 사람이 없으니
    /// 지금 쓰는 CC0 모델(좀비 · 군인+소총 · 좀비퀸)을 **게임 카메라 각도(50°)** 에서 프레임마다 찍는다.
    /// 게임에서 보는 모습 그대로가 그림이 된다. 한 장 = 8 프레임 가로 줄, 프레임 128 px, 배경 투명.
    ///
    /// <code>Unity -batchmode -quit -projectPath . -executeMethod CrowdFire.EditorTools.SpriteBake.All</code>
    /// (`-nographics` 를 붙이면 그림이 안 나온다)
    /// </summary>
    public static class SpriteBake
    {
        const string Root = "Assets/_Game/ThirdParty/Quaternius";
        const string Out = "D:/ryu/portfolio/crowd-fire-cocos/art";
        // 8 프레임 · 128 px 에서는 달리는 동작이 작아 거의 멈춰 보였다 — 12 프레임 · 160 px
        const int Frames = 12, Px = 160;

        [MenuItem("CR/Bake Sprites for Cocos")]
        public static void All()
        {
            Directory.CreateDirectory(Out);
            var zombieClip = AssetDatabase.LoadAssetAtPath<AnimationClip>(Root + "/Clips/ZombieRun.anim");
            // `ZombieRun.anim` 은 좀비퀸 저장소에서 다른 리그로 구운 것이라 이 모델의 뼈를 거의 못 움직였다 —
            // 12 프레임이 한 장처럼 똑같았다. 같은 리그(UACP)의 기본 `Run` 을 쓴다
            Sheet("zombie_run", Root + "/Characters/Zombie_Male.fbx", Clip(Root + "/Characters/BaseCharacter.fbx", "Run"), false, true);
            Sheet("human_shoot", Root + "/Characters/BlueSoldier_Male.fbx", Clip(Root + "/Characters/BaseCharacter.fbx", "Shoot_OneHanded"), true, false);
            Sheet("human_down", Root + "/Characters/BlueSoldier_Male.fbx", Clip(Root + "/Characters/BaseCharacter.fbx", "Death"), true, false, loop: false);
            Sheet("zombie_down", Root + "/Characters/Zombie_Male.fbx", Clip(Root + "/Characters/BaseCharacter.fbx", "Death"), false, true, loop: false);
            Sheet("queen_run", "Assets/_Game/Resources/Queen/ZombieQueen.fbx", Clip("Assets/_Game/Resources/Queen/ZombieQueen.fbx", "Run"), false, false, queen: true);
            Debug.Log("[CR] 스프라이트 → " + Out);
        }

        static AnimationClip Clip(string fbx, string name)
        {
            foreach (var o in AssetDatabase.LoadAllAssetRepresentationsAtPath(fbx))
                if (o is AnimationClip c && (c.name == name || c.name.EndsWith("|" + name))) return c;
            Debug.LogError("[CR] 클립 없음: " + fbx + " " + name);
            return null;
        }

        static void Sheet(string name, string fbxPath, AnimationClip clip, bool rifle, bool zombie, bool loop = true, bool queen = false)
        {
            var fbx = AssetDatabase.LoadAssetAtPath<GameObject>(fbxPath);
            if (fbx == null || clip == null) { Debug.LogError("[CR] 없음: " + fbxPath); return; }
            var go = Object.Instantiate(fbx);
            go.transform.position = new Vector3(0f, 0f, 1000f);   // 장면의 다른 것과 떨어진 곳
            // 인간은 군단 쪽(−z)을 본다 — 게임과 같은 방향에서 찍는다
            // 소총 든 인간은 30° 비껴 세운다 — 정면이면 총이 카메라를 향해 줄어 아래로 늘어진 막대로만 보였다
            go.transform.rotation = zombie || queen ? Quaternion.identity : Quaternion.Euler(0f, rifle ? 150f : 180f, 0f);

            // 색 — 군중 VAT 와 같은 팔레트 (머티리얼 이름으로)
            var sh = Shader.Find("Legacy Shaders/Diffuse");
            var qtex = queen ? AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/_Game/Resources/Queen/queen_basecolor.png") : null;
            foreach (var r in go.GetComponentsInChildren<Renderer>())
            {
                var ms = r.sharedMaterials;
                for (int i = 0; i < ms.Length; i++)
                {
                    var c = queen ? Color.white : Palette(ms[i] != null ? ms[i].name : "", zombie);
                    // 퀸은 **조명 없이** 텍스처 색 그대로 — 빛을 받으면 하얗게 날아갔다 (첫 굽기)
                    ms[i] = queen ? new Material(Shader.Find("Unlit/Texture")) { mainTexture = qtex }
                                  : new Material(sh) { color = c, mainTexture = qtex };
                }
                r.sharedMaterials = ms;
            }

            Transform rifleT = rifle ? AttachRifle(go) : null;
            Transform hand = rifle ? FindBone(go, "Fist.R") : null;

            // 빛 · 카메라 — 게임과 같은 50° 로 내려다본다
            var lightGo = new GameObject("BakeLight");
            var li = lightGo.AddComponent<Light>();
            li.type = LightType.Directional; li.intensity = 1.15f; li.color = new Color(1f, 0.95f, 0.86f);
            lightGo.transform.rotation = Quaternion.Euler(52f, -28f, 0f);
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.46f, 0.48f, 0.54f);
            var camGo = new GameObject("BakeCam");
            var cam = camGo.AddComponent<Camera>();
            cam.orthographic = true;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0f, 0f, 0f, 0f);
            cam.transform.rotation = Quaternion.Euler(50f, 0f, 0f);

            // 0 프레임 크기로 화면을 정한다 — 모든 프레임이 같은 틀에 들어가야 한다
            clip.SampleAnimation(go, 0f);
            var b = Bounds(go);
            float size = Mathf.Max(b.size.y, b.size.x, b.size.z) * 0.62f;
            cam.orthographicSize = size;
            cam.transform.position = b.center - cam.transform.forward * 20f;
            cam.nearClipPlane = 0.1f; cam.farClipPlane = 60f;

            // **스킨 메시는 직접 굽는다.** 배치 모드에서는 `SampleAnimation` 으로 뼈를 옮겨도 스킨이 다시 계산되지 않은
            // 채 찍혀, 12 프레임이 한 장처럼 똑같았다. 매 프레임 `BakeMesh` 한 결과를 대역 메시로 그린다
            var smrs = go.GetComponentsInChildren<SkinnedMeshRenderer>();
            var proxies = new MeshFilter[smrs.Length];
            for (int k = 0; k < smrs.Length; k++)
            {
                var pgo = new GameObject("Proxy" + k);
                proxies[k] = pgo.AddComponent<MeshFilter>();
                proxies[k].sharedMesh = new Mesh();
                pgo.AddComponent<MeshRenderer>().sharedMaterials = smrs[k].sharedMaterials;
                smrs[k].enabled = false;
            }
            // 쓰러지는 판은 **모든 프레임을 굽어 본 합친 상자**로 틀을 정한다 — 0 프레임 틀이면 넘어진 몸이 잘렸다
            if (!loop)
            {
                var all = b;
                for (int f = 0; f < Frames; f++)
                {
                    clip.SampleAnimation(go, clip.length * f / (Frames - 1));
                    for (int k = 0; k < smrs.Length; k++)
                    {
                        smrs[k].BakeMesh(proxies[k].sharedMesh, true);
                        var st = smrs[k].transform;
                        proxies[k].transform.SetPositionAndRotation(st.position, st.rotation);
                        proxies[k].transform.localScale = st.lossyScale;
                        all.Encapsulate(proxies[k].GetComponent<MeshRenderer>().bounds);
                    }
                }
                // 틀의 중심은 서 있을 때 그대로 (그려지는 자리가 튀지 않게), 크기만 넘어진 몸까지 넓힌다
                var lo = all.min - b.center; var hi = all.max - b.center;
                float half = Mathf.Max(Mathf.Max(-lo.x, hi.x), Mathf.Max(Mathf.Max(-lo.y, hi.y), Mathf.Max(-lo.z, hi.z)));
                cam.orthographicSize = Mathf.Max(size, half * 1.05f);
            }
            var rt = new RenderTexture(Px, Px, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
            cam.targetTexture = rt;
            var sheet = new Texture2D(Px * Frames, Px, TextureFormat.RGBA32, false);
            var tmp = new Texture2D(Px, Px, TextureFormat.RGBA32, false);
            for (int f = 0; f < Frames; f++)
            {
                float t = loop ? clip.length * f / Frames : clip.length * f / (Frames - 1);
                clip.SampleAnimation(go, t);
                if (queen) go.transform.position = new Vector3(0f, 0f, 1000f);
                // **소총은 몸이 향한 쪽으로** — 손 뼈의 축에 맡기면 한 손 사격 자세에서 아래를 향했다
                // 쓰러질 때는 절반쯤에서 총을 놓친다 — 누운 몸 위로 총구가 하늘을 향했다
                if (rifleT != null) rifleT.gameObject.SetActive(loop || f < Frames / 2);
                if (rifleT != null && hand != null)
                {
                    var fwd = go.transform.forward;
                    // 메시의 **실제 긴 축**을 몸 앞으로 — 축을 짐작해 돌렸더니 총구가 땅을 향했다
                    rifleT.rotation = Quaternion.FromToRotation(RifleAxis, (fwd + Vector3.up * 0.08f).normalized);
                    rifleT.position = hand.position + fwd * 0.40f + Vector3.up * 0.05f;
                }
                for (int k = 0; k < smrs.Length; k++)
                {
                    // 배율 없이 굽고 대역이 렌더러의 배율을 그대로 갖는다 (배율 포함으로 구우면 점처럼 작게 나왔다)
                    smrs[k].BakeMesh(proxies[k].sharedMesh, true);   // 군중 VAT 와 같은 식: 세상 = 렌더러 행렬 × BakeMesh(true)
                    var st = smrs[k].transform;
                    proxies[k].transform.SetPositionAndRotation(st.position, st.rotation);
                    proxies[k].transform.localScale = st.lossyScale;
                }
                cam.Render();
                RenderTexture.active = rt;
                tmp.ReadPixels(new Rect(0, 0, Px, Px), 0, 0);
                tmp.Apply();
                sheet.SetPixels(f * Px, 0, Px, Px, tmp.GetPixels());
            }
            RenderTexture.active = null;
            sheet.Apply();
            File.WriteAllBytes(Out + "/" + name + ".png", sheet.EncodeToPNG());
            // 1 m 가 몇 px 인지 — Cocos 쪽이 크기를 맞추는 데 쓴다 (정사영이라 화면 높이 = 2·size)
            float pxPerM = Px / (2f * cam.orthographicSize);   // 쓰러짐 판은 틀을 다시 정한다
            File.WriteAllText(Out + "/" + name + ".json",
                "{ \"frames\": " + Frames + ", \"px\": " + Px + ", \"pxPerMeter\": " + pxPerM.ToString("F3", System.Globalization.CultureInfo.InvariantCulture) +
                ", \"clip\": \"" + clip.name + "\", \"seconds\": " + clip.length.ToString("F3", System.Globalization.CultureInfo.InvariantCulture) + " }\n");
            Debug.Log($"[CR] {name}: {Frames} 프레임 · {Px}px · {pxPerM:F1} px/m · 상자 {b.size}");

            rt.Release();
            Object.DestroyImmediate(rt); Object.DestroyImmediate(sheet); Object.DestroyImmediate(tmp);
            Object.DestroyImmediate(camGo); Object.DestroyImmediate(lightGo); Object.DestroyImmediate(go);
            foreach (var pf in proxies) if (pf != null) { Object.DestroyImmediate(pf.sharedMesh); Object.DestroyImmediate(pf.gameObject); }
            if (rifleT != null) Object.DestroyImmediate(rifleT.gameObject);   // 남겨 두면 다음 판(쓰러짐)에 총이 하나 더 찍혔다
        }

        static Bounds Bounds(GameObject go)
        {
            var rs = go.GetComponentsInChildren<Renderer>();
            var b = rs[0].bounds;
            foreach (var r in rs) b.Encapsulate(r.bounds);
            return b;
        }

        /// <summary>군중 VAT 와 같은 자리 — 손 뼈 Y 축으로 모델 공간 0.45 m, 길이 1.3 m (키 2.92 m 기준)</summary>
        static Transform FindBone(GameObject go, string name)
        {
            foreach (var t in go.GetComponentsInChildren<Transform>()) if (t.name == name) return t;
            return null;
        }

        static Vector3 RifleAxis = Vector3.up;

        static Transform AttachRifle(GameObject go)
        {
            Transform bone = FindBone(go, "Fist.R");
            var w = AssetDatabase.LoadAssetAtPath<GameObject>(Root + "/Weapons/Rifle.fbx");
            if (bone == null || w == null) return null;
            var rifle = Object.Instantiate(w);
            var mf = rifle.GetComponentInChildren<MeshFilter>();
            var ws = mf.sharedMesh.bounds.size;
            float longest = Mathf.Max(ws.x, Mathf.Max(ws.y, ws.z));
            foreach (var r in rifle.GetComponentsInChildren<Renderer>())
                r.sharedMaterial = new Material(Shader.Find("Legacy Shaders/Diffuse")) { color = new Color(0.52f, 0.46f, 0.38f) };
            rifle.transform.rotation = bone.rotation;
            rifle.transform.position = bone.position + bone.up * 0.45f;
            float k = 1.3f / Mathf.Max(1e-4f, longest);
            // 메시 축 = 소총 뿌리 축 (FBX 자식의 회전·위치를 지운다), 긴 축 1.3 m, 굵기 1.8 배 — 160 px 에서 실처럼 가늘었다
            foreach (Transform c in rifle.transform) { c.localPosition = -mf.sharedMesh.bounds.center; c.localRotation = Quaternion.identity; c.localScale = Vector3.one; }
            RifleAxis = ws.x >= ws.y && ws.x >= ws.z ? Vector3.right : ws.y >= ws.z ? Vector3.up : Vector3.forward;
            rifle.transform.localScale = (Vector3.one * 1.8f - RifleAxis * 0.8f) * k;
            Debug.Log($"[CR] 소총 메시 {ws} · 긴 축 {RifleAxis} · 자식 {rifle.transform.childCount}");
            return rifle.transform;
        }

        static Color Palette(string mat, bool zombie)
        {
            switch (mat.Replace(" (Instance)", ""))
            {
                case "Main": return new Color(0.22f, 0.40f, 0.78f);
                case "Helmet": return new Color(0.17f, 0.32f, 0.68f);
                case "Skin": return zombie ? new Color(0.56f, 0.68f, 0.46f) : new Color(0.95f, 0.80f, 0.66f);
                case "Face": return new Color(0.95f, 0.80f, 0.66f);
                case "Grey": return new Color(0.62f, 0.62f, 0.66f);
                case "Black": return new Color(0.16f, 0.26f, 0.52f);
                case "Clothes": return new Color(0.36f, 0.30f, 0.42f);
                case "Pants": return new Color(0.30f, 0.26f, 0.20f);
            }
            return Color.white;
        }
    }
}
