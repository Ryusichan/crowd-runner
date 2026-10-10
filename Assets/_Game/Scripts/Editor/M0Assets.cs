using System.IO;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace CrowdRunner.EditorTools
{
    /// <summary>
    /// **대역 캐릭터를 굽는다** — M0 가 재야 하는 비용을 가진, 아직 없는 아트의 대역.
    ///
    /// `docs/M0_CROWD.md` §2 가 *"캡슐로는 재지 않는다"* 로 닫혔는데, 이 저장소에는 **캐릭터 자산이
    /// 아직 하나도 없다.** 아트를 기다리면 M0 가 아트 뒤로 밀리고, 그러면 M0 가 존재하는 이유
    /// (아트 파이프라인을 정하는 것)가 뒤집힌다.
    ///
    /// 그래서 **비용이 대표성 있는 메시를 코드로 만든다.** 스킨드 메시의 비용을 정하는 것은 모양이
    /// 아니라 **정점 수 · 본 수 · 렌더러 수**이고, 셋 다 여기서 숫자로 준다. 모양은 사람 비슷하기만
    /// 하면 된다 — 그래서 모양에 공을 들이지 않았다.
    ///
    /// ⚠ **이것이 캡슐과 다른 점이 그 셋이다.** 캡슐은 본 0 · 정점 ~100 · 스킨닝 없음이라 비용의
    /// **종류**가 달랐다. 여기는 본 `Bones`(20) · 정점 `~Verts`(1,500) · `SkinnedMeshRenderer` 라
    /// 같은 종류의 비용을 낸다. 그리고 **그 두 수가 출하 아트의 상한이 된다** — 측정이 1,500/20 에서
    /// 통과했으면 실제 캐릭터도 그 안에 들어와야 한다. 넘으면 M0 결과가 무효다.
    ///
    /// 배치 모드로 돈다 (오너 규칙: 에디터 창을 띄우지 않는다):
    /// <code>Unity -batchmode -nographics -quit -projectPath . -executeMethod CrowdRunner.EditorTools.M0Assets.Bake</code>
    /// </summary>
    public static class M0Assets
    {
        /// <summary>모바일 군중 캐릭터의 현실적인 값 — 이 둘이 출하 아트의 상한이다</summary>
        public const int Bones = 20;
        public const int Verts = 1500;

        const string Dir = "Assets/_Game/Resources/M0";

        [MenuItem("M0/Bake Stand-in Character")]
        public static void Bake() { Bake(Verts, ""); }

        /// <summary>
        /// **정점 수를 인자로 받는다** — 방식 B 측정이 *정점 수가 병목*이라고 말했기 때문이다.
        /// 1,000 개체 × 1,500 정점 = 150 만 정점이고, VAT 는 본 평가와 드로우 콜만 없애지
        /// **정점을 줄이지 않는다.** 그래서 저폴리 한 벌을 같이 구워 나란히 잰다.
        /// `suffix` 가 자산 이름을 가른다 (빈 문자열 = 기본 1,500).
        /// </summary>
        public static void Bake(int verts, string suffix)
        {
            Directory.CreateDirectory(Dir);

            var mesh = BuildSkinnedMesh(verts, out var boneNames, out var binds);
            AssetDatabase.CreateAsset(mesh, Dir + "/standin" + suffix + ".asset");

            var clip = BuildRunClip(boneNames);
            AssetDatabase.CreateAsset(clip, Dir + "/standin_run" + suffix + ".anim");

            var ctrl = AnimatorController.CreateAnimatorControllerAtPathWithClip(Dir + "/standin" + suffix + ".controller", clip);

            var prefab = BuildPrefab(mesh, binds, boneNames, ctrl, suffix);
            PrefabUtility.SaveAsPrefabAsset(prefab, Dir + "/StandIn" + suffix + ".prefab");
            Object.DestroyImmediate(prefab);

            AssetDatabase.SaveAssets();
            Debug.Log($"[M0] baked stand-in: verts={mesh.vertexCount} bones={boneNames.Length} " +
                      $"tris={mesh.triangles.Length / 3} clip={clip.length:F2}s");
        }

        /// <summary>
        /// 본 하나를 한 층으로 보는 **원통 사슬**. 층마다 고리 하나, 고리의 정점은 **그 층 본에
        /// 100 % 로 묶는다** — 스킨닝 비용은 가중치의 **수**가 정하므로(정점당 1 개) 가중치 1 개가
        /// 가장 싼 쪽이다. 즉 이 측정은 **실제보다 유리하다**: 출하 아트가 정점당 2~4 가중치를 쓰면
        /// 더 비싸진다. 그래서 §4 의 통과선에 여유(절반 예산)를 둔 것이고, 그 여유의 일부가 이것이다.
        /// </summary>
        /// <summary>
        /// **길 표식과 발밑 그림자의 머티리얼을 에셋으로 굽는다.**
        ///
        /// 런타임에 `new Material(Shader.Find(...))` 로 만들고 `enableInstancing = true` 를
        /// 켜면 **에디터에서는 그려지고 빌드에서는 안 그려진다.** 빌드는 쓰이는 셰이더 변형만
        /// 남기는데, 런타임에 켜는 인스턴싱은 빌드 시점에 아무도 모르기 때문이다.
        ///
        /// 2026-10-09 에 그 값을 치렀다: 길 표식도 발밑 원반도 웹 빌드에서 **하나도 안 나왔다.**
        /// 내 PNG 에는 보였다 — 에디터는 모든 변형을 들고 있으니까. **보는 쪽이 달라서 다른 것을
        /// 보고 있던** 오늘 여섯 번째 자리다.
        ///
        /// 군중(`standin_vat*.mat`)이 멀쩡했던 것이 대조군이다. 그쪽은 처음부터 에셋이었다.
        /// </summary>
        public static void BakeFlatMaterials()
        {
            Directory.CreateDirectory(Dir);
            var sh = Shader.Find("Game/FlatInstanced");
            if (sh == null)
            {
                Debug.LogError("[M0] 셰이더 'Game/FlatInstanced' 를 못 찾았다 — 표식과 그림자가 안 그려진다");
                return;
            }
            Save(sh, "roadmark", new Color(0.90f, 0.89f, 0.82f, 1.00f));
            Save(sh, "blobshadow", new Color(0.05f, 0.08f, 0.05f, 0.38f));
            Save(sh, "roadtree", new Color(0.30f, 0.50f, 0.30f, 1.00f));
        }

        static void Save(Shader sh, string name, Color c)
        {
            string path = Dir + "/" + name + ".mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null) { mat = new Material(sh); AssetDatabase.CreateAsset(mat, path); }
            mat.shader = sh;
            mat.color = c;
            // **이 줄이 빌드에 인스턴싱 변형을 남긴다.** 에셋에 켜져 있어야 빌드가 안다
            mat.enableInstancing = true;
            EditorUtility.SetDirty(mat);
            Debug.Log($"[M0] baked material {name}.mat instancing={mat.enableInstancing}");
        }

        static Mesh BuildSkinnedMesh(int want, out string[] boneNames, out Matrix4x4[] binds)
        {
            int ring = Mathf.Max(3, want / Bones);          // 층마다 정점 수
            int rows = Bones;
            var verts = new Vector3[ring * rows];
            var norms = new Vector3[verts.Length];
            var weights = new BoneWeight[verts.Length];
            boneNames = new string[rows];
            binds = new Matrix4x4[rows];

            float height = 1.8f;
            for (int r = 0; r < rows; r++)
            {
                float t = r / (float)(rows - 1);
                float y = t * height;
                float rad = Radius(t);
                boneNames[r] = "b" + r;
                binds[r] = Matrix4x4.TRS(new Vector3(0f, -y, 0f), Quaternion.identity, Vector3.one);
                for (int c = 0; c < ring; c++)
                {
                    float a = c / (float)ring * Mathf.PI * 2f;
                    int k = r * ring + c;
                    verts[k] = new Vector3(Mathf.Cos(a) * rad, y, Mathf.Sin(a) * rad);
                    norms[k] = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));   // 아래에서 다시 센다
                    weights[k] = new BoneWeight { boneIndex0 = r, weight0 = 1f };
                }
            }

            var tris = new System.Collections.Generic.List<int>((rows - 1) * ring * 6);
            for (int r = 0; r < rows - 1; r++)
                for (int c = 0; c < ring; c++)
                {
                    int c2 = (c + 1) % ring;
                    int a = r * ring + c, b = r * ring + c2, d = (r + 1) * ring + c, e = (r + 1) * ring + c2;
                    tris.Add(a); tris.Add(d); tris.Add(b);
                    tris.Add(b); tris.Add(d); tris.Add(e);
                }

            var m = new Mesh { name = "standin" };
            m.indexFormat = UnityEngine.Rendering.IndexFormat.UInt16;
            m.vertices = verts; m.normals = norms; m.boneWeights = weights;
            m.triangles = tris.ToArray();
            // **법선을 다시 센다.** 위에서 넣은 것은 전부 **수평**(`y = 0`)이다. 그런데 셰이더의
            // 빛은 위에서 내려오므로(`(0.3, 1.0, -0.2)`), 수평 법선은 어디서나 `dot ≈ 0.3` 이고
            // **몸 전체가 가장 어두운 값**으로 칠해진다. 첫 게임 화면에서 군중이 커피콩처럼
            // 보인 것의 절반이 이것이었다 — 실루엣만 고쳤으면 모양은 사람인데 여전히 까맸다.
            //
            // 측정에는 영향이 없다: 법선은 굽는 쪽이 아니라 원본 메시의 것을 셰이더가 그대로
            // 쓰고(`CrowdVat` 주석), 정점 수도 본 수도 그대로다
            m.RecalculateNormals();
            m.bindposes = binds;
            m.RecalculateBounds();
            return m;
        }

        /// <summary>
        /// **사람으로 읽히는 옆모습.** 전에는 `Lerp(0.16, 0.26, sin(t·π))` — 허리에서 굵고
        /// 위아래로 가늘어지는 **방추형**이었고, 화면에서 커피콩으로 보였다. 머리가 없으면
        /// 사람으로 안 읽힌다. 그게 전부다.
        ///
        /// **정점 수도 본 수도 안 바뀐다** — 자리만 옮긴다. 그래서 `docs/M0_CROWD.md` 의
        /// 측정치(방식 B · 저폴리 300 정점 · 상한 400)는 **그대로 유효하다.** 모양을 고치면서
        /// 비용을 같이 올리면 그 표를 다시 재야 하고, 그러면 아트 예산의 근거가 사라진다.
        ///
        /// 행이 20 개(= 본 수)라 한 행이 키의 5 % 다. 머리·목·어깨가 그 해상도에 들어가도록
        /// 구간을 잡았다 — 더 잘게 나누면 정점이 늘어난다.
        /// </summary>
        static float Radius(float t)
        {
            if (t < 0.46f) return Mathf.Lerp(0.13f, 0.17f, t / 0.46f);          // 다리
            if (t < 0.54f) return Mathf.Lerp(0.17f, 0.23f, (t - 0.46f) / 0.08f); // 골반
            if (t < 0.74f) return Mathf.Lerp(0.23f, 0.27f, (t - 0.54f) / 0.20f); // 몸통
            if (t < 0.80f) return Mathf.Lerp(0.27f, 0.21f, (t - 0.74f) / 0.06f); // 어깨
            if (t < 0.85f) return 0.10f;                                          // 목
            // 머리 — 구에 가깝게. 정수리에서 0 으로 닫지 않는다 (한 행이 5 % 라 각져 보인다)
            float u = (t - 0.85f) / 0.15f;                                        // 0..1
            return 0.19f * Mathf.Sqrt(Mathf.Max(0.04f, 1f - (u - 0.45f) * (u - 0.45f) / 0.30f));
        }

        /// <summary>
        /// 달리는 클립 — 본마다 **같은 주기, 다른 위상**으로 흔든다. 모양이 달리기처럼 보이는 것은
        /// 중요하지 않다. 중요한 것은 **본 20 개 전부에 커브가 있는 것**이다: 커브가 없는 본은
        /// 평가되지 않아서, 커브를 몇 개만 두면 측정이 **더 싼 클립**을 재게 된다.
        /// </summary>
        static AnimationClip BuildRunClip(string[] boneNames)
        {
            var clip = new AnimationClip { name = "standin_run", frameRate = 30f };
            clip.legacy = false;
            for (int i = 0; i < boneNames.Length; i++)
            {
                string path = Path(boneNames, i);
                float ph = i / (float)boneNames.Length * Mathf.PI * 2f;
                var cx = new AnimationCurve();
                var cz = new AnimationCurve();
                for (int f = 0; f <= 16; f++)
                {
                    float t = f / 16f;
                    float a = t * Mathf.PI * 2f + ph;
                    cx.AddKey(t * 0.8f, Mathf.Sin(a) * 0.06f);
                    cz.AddKey(t * 0.8f, Mathf.Cos(a) * 0.04f);
                }
                clip.SetCurve(path, typeof(Transform), "localEulerAngles.x", cx);
                clip.SetCurve(path, typeof(Transform), "localEulerAngles.z", cz);
            }
            var s = new AnimationClipSettings { loopTime = true };
            AnimationUtility.SetAnimationClipSettings(clip, s);
            return clip;
        }

        /// <summary>본은 사슬이므로 경로가 `b0/b1/b2/...` 로 깊어진다 — 깊이도 평가 비용이다</summary>
        static string Path(string[] names, int i)
        {
            var sb = new System.Text.StringBuilder();
            for (int k = 0; k <= i; k++) { if (k > 0) sb.Append('/'); sb.Append(names[k]); }
            return sb.ToString();
        }

        static GameObject BuildPrefab(Mesh mesh, Matrix4x4[] binds, string[] boneNames, AnimatorController ctrl, string suffix)
        {
            var root = new GameObject("StandIn");
            var bones = new Transform[boneNames.Length];
            Transform parent = root.transform;
            float step = 1.8f / (boneNames.Length - 1);
            for (int i = 0; i < boneNames.Length; i++)
            {
                var go = new GameObject(boneNames[i]);
                go.transform.SetParent(parent, false);
                go.transform.localPosition = i == 0 ? Vector3.zero : new Vector3(0f, step, 0f);
                bones[i] = go.transform;
                parent = go.transform;
            }
            var smr = root.AddComponent<SkinnedMeshRenderer>();
            smr.sharedMesh = mesh;
            smr.bones = bones;
            smr.rootBone = bones[0];
            smr.sharedMaterial = Material(suffix);
            // **품질을 1 가중치로 못 박는다** — 프로젝트 설정이 4 로 되어 있으면 측정이 설정에 끌려간다
            smr.quality = SkinQuality.Bone1;
            smr.updateWhenOffscreen = false;
            var anim = root.AddComponent<Animator>();
            anim.runtimeAnimatorController = ctrl;
            // **컬링을 끈다**: 화면 밖에서 안 돌면 1,000 중 몇이 실제로 평가됐는지 알 수 없고,
            // 그러면 측정이 카메라 각도에 달린다
            anim.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            return root;
        }

        static Material Material(string suffix)
        {
            // **내장 파이프라인의 가장 싼 조명 셰이더.** URP 를 안 쓰는 이유는 `M0_CROWD.md` §2b:
            // 반쯤 설정된 URP 로 재는 것이 내장으로 재는 것보다 나쁘다. M0 가 묻는 것은
            // **A 대 B 의 비율**이고, 파이프라인은 양쪽을 같은 방향으로 움직인다
            var sh = Shader.Find("Mobile/Diffuse") ?? Shader.Find("Legacy Shaders/Diffuse") ?? Shader.Find("Standard");
            var m = new Material(sh) { name = "standin" };
            AssetDatabase.CreateAsset(m, Dir + "/standin" + suffix + ".mat");
            return m;
        }
    }
}
