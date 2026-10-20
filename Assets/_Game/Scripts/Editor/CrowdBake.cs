using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace CrowdFire.EditorTools
{
    /// <summary>
    /// **진짜 캐릭터를 VAT 로 굽는다** — 파란 군인(+소총)과 좀비.
    ///
    /// 오너 2026-10-18: *"퀄리티가 처참하다. 라스트워처럼."* 화면의 사람이 회색 캡슐(`StandIn`)
    /// 이었다. 좀비퀸에 CC0 모델이 있었다 — Quaternius `BlueSoldier`·`Zombie` (`docs/THIRD_PARTY_ASSETS.md`).
    /// `VatBaker` 는 대역 프리팹만 알았으므로, FBX 에서 바로 굽는 길을 여기 둔다. 출력 이름은
    /// `VatBaker` 와 같은 규칙(`standin_vat{suffix}`)이라 `VatCrowd` 가 접미사만 바꿔 읽는다.
    ///
    /// 대역과 다른 셋:
    /// 1. **색.** UACP 는 텍스처가 없고 **머티리얼마다 단색**이다. 서브메시를 하나로 합치면서
    ///    각 서브메시의 머티리얼 색을 **정점색**으로 옮긴다. 셰이더가 정점색 × 팀 틴트를 쓴다.
    /// 2. **소총.** 군인 모델에 총이 없다. 강체 메시(`Rifle.fbx`)를 손 뼈(`Fist.R`)의 프레임별
    ///    행렬로 변환해 정점을 **뒤에 덧붙인다** — 본 없이 같은 텍스처에 들어간다.
    /// 3. **클립이 다른 FBX 에 있다.** UACP 의 클립은 `BaseCharacter.fbx` 에만 임포트된다(좀비퀸
    ///    규칙). 리그 경로가 같아서 `SampleAnimation` 이 그대로 먹는다.
    ///
    /// <code>Unity -batchmode -quit -projectPath . -executeMethod CrowdFire.EditorTools.CrowdBake.All</code>
    /// </summary>
    public static class CrowdBake
    {
        const string Root = "Assets/_Game/ThirdParty/Quaternius";
        const string Out = "Assets/_Game/Resources/M0";
        const int Frames = VatBaker.Frames;

        [MenuItem("CR/Bake Crowd Characters")]
        public static void All()
        {
            Soldier();
            Zombie();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        public static void Soldier()
        {
            // **들고 달리는 클립.** `Run` 은 팔을 흔들어 소총이 몸 뒤로 숨었다 (2026-10-18 사진).
            // `Run_Carry` 는 두 손을 앞에 모은 자세라 라스트워의 "총 들고 전진" 이 된다
            var clip = FindClip(Root + "/Characters/BaseCharacter.fbx", "Run_Carry");
            Bake("_soldier",
                 Root + "/Characters/BlueSoldier_Male.fbx", clip,
                 Root + "/Weapons/Rifle.fbx", "Fist.R",
                 // 소총 메시의 긴 축은 Y (경계 0.00 × 0.02 × 0.01). X 로 +90° 돌리면 Y → 앞(Z).
                 // 손에서 조금 앞·위로 — 사진 보고 다듬는다
                 // 잰 값(2026-10-18): 소총 긴 축은 메시 Y, 손 뼈의 Y 축이 세계 앞쪽. 그래서 회전 없음,
                 // 손 뼈 Y 로 0.28 m 밀어 개머리판은 손에, 총구는 앞에
                 new Vector3(0f, 0.28f, 0f), Quaternion.identity, 1.0f,
                 new Color(0.46f, 0.76f, 0.86f), 1.80f);
        }

        public static void Zombie()
        {
            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(Root + "/Clips/ZombieRun.anim");
            Bake("_zombie",
                 Root + "/Characters/Zombie_Male.fbx", clip,
                 null, null, Vector3.zero, Quaternion.identity, 1f,
                 new Color(0.90f, 0.36f, 0.33f), 1.85f);
        }

        /// <summary>
        /// **머티리얼 이름으로 색을 입힌다.** FBX 에서 온 색은 선형값이라 거의 0 이다 (군인 `Main`
        /// 이 (0.019, 0.030, 0.056) 로 읽혔다 — 그대로 쓰면 검다). 좀비퀸도 같은 이유로
        /// `ActorVisual.Normalize` 가 이름(Skin · Main · …)으로 팔레트를 준다. 라스트워의
        /// 파란 헬멧 군인과 회녹색 좀비는 여기서 결정된다 (`docs/REF_LASTWAR.md` §2a)
        /// </summary>
        /// <summary>뼈의 **위치·회전만** 루트 공간으로. 배율은 버린다 — FBX 뼈 배율(×100)이 소총을 15 m 밖으로 보냈다</summary>
        static Matrix4x4 BoneFrame(Transform root, Transform bone)
        {
            var pos = root.InverseTransformPoint(bone.position);
            var rot = Quaternion.Inverse(root.rotation) * bone.rotation;
            return Matrix4x4.TRS(pos, rot, Vector3.one);
        }

        static Color MatColor(Material m)
        {
            if (m == null) return Color.white;
            switch (m.name.Replace(" (Instance)", ""))
            {
                // 군인
                case "Main":   return new Color(0.22f, 0.40f, 0.78f);   // 파란 군복
                case "Helmet": return new Color(0.17f, 0.32f, 0.68f);   // 파란 헬멧 (조금 어둡게)
                case "Skin":
                case "Face":   return new Color(0.95f, 0.80f, 0.66f);
                case "Grey":   return new Color(0.62f, 0.62f, 0.66f);   // 장구
                // 몸통이 검으면 검은 소총이 묻힌다 (z36 사진) — 라스트워 군인은 몸통도 파랗다
                case "Black":  return new Color(0.16f, 0.26f, 0.52f);
                // 좀비 (Skin 은 위와 겹치므로 Clothes/Pants 가 있는 모델에서만 따로 — 아래 Zombie() 가 덮어쓴다)
                case "Clothes": return new Color(0.36f, 0.30f, 0.42f);  // 찢어진 옷
                case "Pants":   return new Color(0.30f, 0.26f, 0.20f);
            }
            Color c = Color.white;
            if (m.HasProperty("_BaseColor")) c = m.GetColor("_BaseColor");
            else if (m.HasProperty("_Color")) c = m.GetColor("_Color");
            if (c.r + c.g + c.b < 0.06f) c = Color.white;
            c.a = 1f;
            return c;
        }

        static void EnsureRig(string fbxPath)
        {
            var imp = AssetImporter.GetAtPath(fbxPath) as ModelImporter;
            if (imp == null) return;
            bool dirty = false;
            if (imp.animationType != ModelImporterAnimationType.Generic) { imp.animationType = ModelImporterAnimationType.Generic; dirty = true; }
            if (imp.optimizeGameObjects) { imp.optimizeGameObjects = false; dirty = true; }   // 뼈 이름으로 찾는다(`Fist.R`)
            if (!imp.isReadable) { imp.isReadable = true; dirty = true; }                   // BakeMesh · 삼각형 읽기
            if (imp.materialImportMode == ModelImporterMaterialImportMode.None) { imp.materialImportMode = ModelImporterMaterialImportMode.ImportViaMaterialDescription; dirty = true; }
            if (dirty) { imp.SaveAndReimport(); Debug.Log("[CR] 임포트 설정을 맞춰 다시 읽었다: " + fbxPath); }
        }

        static AnimationClip FindClip(string fbx, string name)
        {
            foreach (var o in AssetDatabase.LoadAllAssetRepresentationsAtPath(fbx))
                if (o is AnimationClip c && (c.name == name || c.name.EndsWith("|" + name))) return c;
            Debug.LogError("[CR] 클립 " + name + " 을 " + fbx + " 에서 못 찾았다");
            return null;
        }

        static void Bake(string suffix, string fbxPath, AnimationClip clip, string weaponPath, string boneName,
                         Vector3 weaponOffset, Quaternion weaponRot, float weaponScale, Color teamTint, float targetHeight)
        {
            // **리그가 꺼져 있으면 켠다.** 좀비 FBX 는 좀비퀸의 `Unused/` 에서 왔고 거기서는 임포트를
            // 끊어 둬서 `SkinnedMeshRenderer` 없이(정적 메시로) 들어왔다 — 첫 굽기가 그래서 비었다.
            // 굽는 쪽이 필요한 임포트 설정을 **스스로 맞추고** 다시 읽는다. 사람이 인스펙터에서
            // 맞추는 설정은 다른 PC 에서 또 틀린다
            EnsureRig(fbxPath);
            var fbx = AssetDatabase.LoadAssetAtPath<GameObject>(fbxPath);
            if (fbx == null || clip == null) { Debug.LogError("[CR] 모델이나 클립이 없다: " + fbxPath); return; }

            var inst = Object.Instantiate(fbx);
            inst.transform.position = Vector3.zero; inst.transform.rotation = Quaternion.identity;
            inst.transform.localScale = Vector3.one;
            var smr = inst.GetComponentInChildren<SkinnedMeshRenderer>();
            if (smr == null) { Debug.LogError("[CR] SkinnedMeshRenderer 가 없다: " + fbxPath); Object.DestroyImmediate(inst); return; }
            var src = smr.sharedMesh;
            var root = inst.transform;
            int nBody = src.vertexCount;
            Debug.Log($"[CR] {fbxPath} 몸 경계 {smr.bounds.size} (키가 1.6~2.0 m 여야 한다)");

            // ── 무기 (강체) ──────────────────────────────────────────────────────
            Mesh wMesh = null; Transform bone = null; Matrix4x4 wLocal = Matrix4x4.identity; float weaponFit = 1f; Color wColor = new Color(0.52f, 0.46f, 0.38f);   // 소총 — 나무 개머리판 느낌, 파란 몸에서 읽힌다
            if (!string.IsNullOrEmpty(weaponPath))
            {
                var w = AssetDatabase.LoadAssetAtPath<GameObject>(weaponPath);
                var mf = w != null ? w.GetComponentInChildren<MeshFilter>() : null;
                if (mf != null) wMesh = mf.sharedMesh;
                var mr = w != null ? w.GetComponentInChildren<MeshRenderer>() : null;
                if (mr != null && mr.sharedMaterial != null && mr.sharedMaterial.HasProperty("_Color")) wColor = mr.sharedMaterial.color;
                foreach (var t in inst.GetComponentsInChildren<Transform>())
                    if (t.name == boneName) { bone = t; break; }
                if (wMesh == null || bone == null)
                {
                    Debug.LogWarning("[CR] 무기(" + weaponPath + ") 또는 뼈(" + boneName + ") 를 못 찾았다 — 총 없이 굽는다");
                    wMesh = null;
                }
                else
                {
                    // **총 크기는 경계 상자로 맞춘다.** 첫 굽기에서 소총이 화면을 덮는 검은 덩어리로
                    // 나왔다 (`docs/shots`, 2026-10-18) — FBX 단위가 달라 100 배였다. 긴 축을
                    // 0.8 m 로 맞추고, `weaponScale` 은 그 위에 곱하는 보정값으로만 쓴다
                    var ws = wMesh.bounds.size;
                    float longest = Mathf.Max(ws.x, Mathf.Max(ws.y, ws.z));
                    float fit = longest > 1e-4f ? 0.8f / longest : 1f;
                    Debug.Log($"[CR] 무기 {weaponPath} 경계 {ws} → 배율 {fit * weaponScale:F4}");
                    wLocal = Matrix4x4.TRS(weaponOffset, weaponRot, Vector3.one * (fit * weaponScale));
                    weaponFit = fit * weaponScale;
                }
            }
            int nWeapon = wMesh != null ? wMesh.vertexCount : 0;
            int n = nBody + nWeapon;

            // ── 정점색: 서브메시의 머티리얼 색 ──────────────────────────────────
            var colors = new Color[n];
            var mats = smr.sharedMaterials;
            for (int s = 0; s < src.subMeshCount; s++)
            {
                // 군인이 **검게** 구워졌다 (2026-10-18 사진). 좀비는 색이 나왔으니 모델마다 머티리얼
                // 속성 이름이 다르다 — URP 는 `_BaseColor`, 내장은 `_Color`. 둘 다 보고, 그래도
                // 어두우면(= 못 읽은 것) 흰색으로 둔다. 무엇을 읽었는지는 **로그로 남긴다**
                var c = MatColor(s < mats.Length ? mats[s] : null);
                if (suffix == "_zombie" && s < mats.Length && mats[s] != null && mats[s].name.StartsWith("Skin"))
                    c = new Color(0.56f, 0.68f, 0.46f);   // 회녹색 좀비 피부
                Debug.Log($"[CR] {suffix} 서브메시 {s}: {(s < mats.Length && mats[s] ? mats[s].name : "(없음)")} → {c}");
                foreach (int i in src.GetTriangles(s)) colors[i] = c;
            }
            for (int i = 0; i < nWeapon; i++) colors[nBody + i] = wColor;

            // ── 키 정규화 ─────────────────────────────────────────────────────────
            // 첫 굽기에서 군인이 **3.3 m** 였다 (`smr.bounds` 로 쟀다). 좀비퀸은 캐릭터를 코드에서
            // 배율로 맞춰 쓰고 있었고, 이 FBX 는 루트 공간에서 그 크기다. 모델마다 다르니
            // **재서** 맞춘다 — 사람 키가 길 폭(9 m)의 1/5 이어야 군중이 군중으로 읽힌다
            float fitK = 1f;
            {
                clip.SampleAnimation(inst, 0f);
                var probe = new Mesh(); smr.BakeMesh(probe, true);
                var toRoot0 = root.worldToLocalMatrix * smr.transform.localToWorldMatrix;
                float lo = float.MaxValue, hi = float.MinValue;
                foreach (var v in probe.vertices) { float y = toRoot0.MultiplyPoint3x4(v).y; if (y < lo) lo = y; if (y > hi) hi = y; }
                float h = hi - lo;
                if (h > 1e-3f) fitK = targetHeight / h;
                Debug.Log($"[CR] {suffix} 루트 공간 키 {h:F2} m → ×{fitK:F3} 로 {targetHeight:F2} m");
                Object.DestroyImmediate(probe);
            }
            if (wMesh != null)
                wLocal = Matrix4x4.TRS(weaponOffset / fitK, weaponRot, Vector3.one * (weaponFit / fitK));

            // ── 프레임 샘플링 ───────────────────────────────────────────────────
            var tex = new Texture2D(n, Frames, TextureFormat.RGBAHalf, false, true)
            { name = "standin_vat" + suffix, filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            var px = new Color[n * Frames];
            var baked = new Mesh();
            var buf = new List<Vector3>(nBody);
            var wv = wMesh != null ? wMesh.vertices : null;
            for (int f = 0; f < Frames; f++)
            {
                clip.SampleAnimation(inst, clip.length * f / Frames);
                smr.BakeMesh(baked, true);
                baked.GetVertices(buf);
                // BakeMesh 는 렌더러 로컬 좌표다 — 루트 기준으로 옮겨 뼈 행렬과 같은 공간에 둔다
                var toRoot = root.worldToLocalMatrix * smr.transform.localToWorldMatrix;
                for (int i = 0; i < nBody; i++)
                {
                    var v = toRoot.MultiplyPoint3x4(buf[i]) * fitK;
                    px[f * n + i] = new Color(v.x, v.y, v.z, 1f);
                }
                if (wMesh != null)
                {
                    var m = BoneFrame(root, bone) * wLocal;
                    for (int i = 0; i < nWeapon; i++)
                    {
                        var v = m.MultiplyPoint3x4(wv[i]) * fitK;
                        px[f * n + nBody + i] = new Color(v.x, v.y, v.z, 1f);
                    }
                }
            }
            // **구운 데이터를 검사한다.** 화면에 거대한 삼각형이 나타나면 십중팔구 정점 하나가
            // NaN 이거나 수백 m 밖이다 — 인스턴싱 드로우는 렌더러 진단에 안 잡히니 여기서 센다
            {
                int bad = 0; var lo = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue); var hi = -lo;
                for (int i = 0; i < px.Length; i++)
                {
                    var c = px[i];
                    bool nan = float.IsNaN(c.r) || float.IsNaN(c.g) || float.IsNaN(c.b) || float.IsInfinity(c.r) || float.IsInfinity(c.g) || float.IsInfinity(c.b);
                    bool far = Mathf.Abs(c.r) > 6f || Mathf.Abs(c.g) > 6f || Mathf.Abs(c.b) > 6f;
                    if (nan || far) { bad++; if (bad <= 3) Debug.LogWarning($"[CR] {suffix} 나쁜 정점 #{i % n} 프레임 {i / n}: ({c.r}, {c.g}, {c.b})"); c = new Color(0f, 0f, 0f, 1f); px[i] = c; }
                    lo = Vector3.Min(lo, new Vector3(c.r, c.g, c.b)); hi = Vector3.Max(hi, new Vector3(c.r, c.g, c.b));
                }
                Debug.Log($"[CR] {suffix} 정점 범위 {lo} ~ {hi} · 나쁜 정점 {bad} 개 (0 으로 눌렀다)");
            }
            tex.SetPixels(px); tex.Apply(false, false);

            // ── 정적 메시 (0 프레임 자세) ────────────────────────────────────────
            clip.SampleAnimation(inst, 0f);
            smr.BakeMesh(baked, true);
            var verts = new Vector3[n]; var norms = new Vector3[n];
            {
                var toRoot = root.worldToLocalMatrix * smr.transform.localToWorldMatrix;
                var bv = baked.vertices; var bn = baked.normals;
                for (int i = 0; i < nBody; i++) { verts[i] = toRoot.MultiplyPoint3x4(bv[i]) * fitK; norms[i] = bn != null && i < bn.Length ? toRoot.MultiplyVector(bn[i]).normalized : Vector3.up; }
                if (wMesh != null)
                {
                    var m = BoneFrame(root, bone) * wLocal;
                    var wn = wMesh.normals;
                    for (int i = 0; i < nWeapon; i++) { verts[nBody + i] = m.MultiplyPoint3x4(wv[i]) * fitK; norms[nBody + i] = wn != null && i < wn.Length ? m.MultiplyVector(wn[i]).normalized : Vector3.up; }
                }
            }
            // **소총이 어디에 어떻게 놓였는지 수치로 찍는다.** 사진으로는 "안 보인다" 까지만 알 수 있다 —
            // 손 뼈의 축이 어느 쪽인지 모른 채 회전을 돌려 맞추면 한 번에 10 분씩 든다
            if (wMesh != null)
            {
                var lo = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue); var hi = -lo;
                for (int i = 0; i < nWeapon; i++) { var v = verts[nBody + i]; lo = Vector3.Min(lo, v); hi = Vector3.Max(hi, v); }
                var handPos = BoneFrame(root, bone).MultiplyPoint3x4(Vector3.zero) * fitK;
                var boneFwd = BoneFrame(root, bone).MultiplyVector(Vector3.forward);
                var boneUp = BoneFrame(root, bone).MultiplyVector(Vector3.up);
                Debug.Log($"[CR] {suffix} 소총 범위 {lo} ~ {hi} (크기 {hi - lo}) · 손 {handPos} · 손뼈 Z축 {boneFwd} Y축 {boneUp}");
            }
            var tris = new List<int>(src.triangles);
            if (wMesh != null) foreach (int t in wMesh.triangles) tris.Add(nBody + t);

            // ── 단순화 (2026-10-20) ────────────────────────────────────────────
            // 오너: *"인물들도 훨씬 lite 하게 — 많은 수의 적이 등장할 때 느려진다."* 좀비 한 명이
            // 정점 5,971, 군인이 4,475(소총만 2,706)였다. 400 명이면 매 프레임 수백만 정점이다.
            // **정점 군집**으로 줄인다: 0 프레임 자세에서 같은 칸(+ 같은 색)에 든 정점을 하나로 합치고,
            // 그 대표의 프레임별 위치만 텍스처에 남긴다. 칸 크기가 곧 디테일이다 — 화면의 사람이
            // 1 m 남짓이라 6 cm 밑의 굴곡은 안 보인다
            {
                // 6.5/7.5 cm → 10/11 cm (2026-10-20 끊김). 카메라가 28 m 로 멀어져 사람이 화면 높이의
                // 1/25 쯤이다 — 10 cm 밑은 한 화소도 안 된다
                float cell = suffix == "_zombie" ? 0.11f : 0.10f;
                int n0 = n;
                var colorId = new Dictionary<Color, int>();
                var keyTo = new Dictionary<long, int>();
                var remap = new int[n0]; var rep = new List<int>();
                for (int i = 0; i < n0; i++)
                {
                    int cid; if (!colorId.TryGetValue(colors[i], out cid)) { cid = colorId.Count; colorId[colors[i]] = cid; }
                    var p = verts[i];
                    long kx = Mathf.FloorToInt(p.x / cell) + 4096, ky = Mathf.FloorToInt(p.y / cell) + 4096, kz = Mathf.FloorToInt(p.z / cell) + 4096;
                    long key = ((kx * 8192 + ky) * 8192 + kz) * 64 + cid;
                    int j; if (!keyTo.TryGetValue(key, out j)) { j = rep.Count; rep.Add(i); keyTo[key] = j; }
                    remap[i] = j;
                }
                int m2 = rep.Count;
                var v2 = new Vector3[m2]; var nm2 = new Vector3[m2]; var c2 = new Color[m2];
                for (int j = 0; j < m2; j++) { v2[j] = verts[rep[j]]; nm2[j] = norms[rep[j]]; c2[j] = colors[rep[j]]; }
                var t2 = new List<int>(tris.Count);
                for (int t = 0; t + 2 < tris.Count; t += 3)
                {
                    int a = remap[tris[t]], bb = remap[tris[t + 1]], cc = remap[tris[t + 2]];
                    if (a == bb || bb == cc || a == cc) continue;
                    t2.Add(a); t2.Add(bb); t2.Add(cc);
                }
                var px2 = new Color[m2 * Frames];
                for (int f = 0; f < Frames; f++)
                    for (int j = 0; j < m2; j++) px2[f * m2 + j] = px[f * n0 + rep[j]];
                var tex2 = new Texture2D(m2, Frames, TextureFormat.RGBAHalf, false, true)
                { name = tex.name, filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
                tex2.SetPixels(px2); tex2.Apply(false, false);
                Object.DestroyImmediate(tex); tex = tex2;
                Debug.Log($"[CR] {suffix} 단순화: 정점 {n0} → {m2} · 삼각형 {tris.Count / 3} → {t2.Count / 3} (칸 {cell * 100:F1} cm)");
                n = m2; verts = v2; norms = nm2; colors = c2; tris = t2;
            }

            var mesh = new Mesh { name = "standin_vat_mesh" + suffix, indexFormat = n > 65000 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16 };
            mesh.SetVertices(verts); mesh.SetNormals(norms); mesh.SetColors(colors);
            mesh.SetTriangles(tris, 0);
            var ids = new Vector2[n];
            for (int i = 0; i < n; i++) ids[i] = new Vector2(i / (float)(n - 1), 0f);
            mesh.SetUVs(1, ids);
            var b = mesh.bounds; b.Expand(1.2f); mesh.bounds = b;

            Object.DestroyImmediate(inst); Object.DestroyImmediate(baked);

            Directory.CreateDirectory(Out);
            Replace(Out + "/standin_vat" + suffix + ".asset", tex);
            Replace(Out + "/standin_vat_mesh" + suffix + ".asset", mesh);

            var sh = Shader.Find("Game/CrowdVat");
            if (sh == null) { Debug.LogError("[CR] Game/CrowdVat 셰이더가 없다"); return; }
            // 아군·적 머티리얼 둘 다 만든다 — `VatCrowd(suffix, foe)` 가 `_foe` 를 붙여 읽는다.
            // 정점색이 있으니 팀 틴트는 **살짝만** (흰색에 가깝게) — 모델의 색이 주인이다
            MakeMat(sh, Out + "/standin_vat" + suffix + ".mat", tex, n, clip, Color.Lerp(Color.white, teamTint, 0.25f));
            MakeMat(sh, Out + "/standin_vat" + suffix + "_foe.mat", tex, n, clip, Color.Lerp(Color.white, teamTint, 0.25f));
            Debug.Log($"[CR] baked VAT{suffix}: body={nBody} weapon={nWeapon} frames={Frames} tex={n}x{Frames} clip={clip.name} {clip.length:F2}s");
        }

        static void MakeMat(Shader sh, string path, Texture2D tex, int n, AnimationClip clip, Color tint)
        {
            var mat = new Material(sh) { name = Path.GetFileNameWithoutExtension(path), enableInstancing = true };
            mat.SetColor("_Color", tint);
            mat.SetColor("_EnemyColor", tint);
            mat.SetTexture("_Vat", tex);
            mat.SetFloat("_Frames", Frames);
            mat.SetFloat("_Verts", n);
            mat.SetFloat("_ClipSpeed", 1f / Mathf.Max(0.1f, clip.length));
            mat.SetFloat("_UseVertexColor", 1f);
            Replace(path, mat);
        }

        static void Replace(string path, Object asset)
        {
            if (AssetDatabase.LoadAssetAtPath<Object>(path) != null) AssetDatabase.DeleteAsset(path);
            AssetDatabase.CreateAsset(asset, path);
        }
    }
}
