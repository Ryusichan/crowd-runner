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

        /// <summary>
        /// **골격.** 전에는 본 20 개가 **한 줄 사슬**(b0→b1→…)이었다 — 그래서 각 본을 조금씩
        /// 돌리면 *흔들리는 기둥*이 나왔고, **팔다리를 붙일 자리가 구조적으로 없었다.**
        /// 세션 A 가 한 명을 6 배로 확대해서 잡았다: *"걷는 게 안 보이는 게 아니라 걸을 수가 없다."*
        ///
        /// 이제 가지를 친다 — 척추 · 다리 둘 · 팔 둘. **본 수는 20 그대로**라
        /// `docs/M0_CROWD.md` 의 상한(정점 300/1,500 · 본 20)이 그대로 유효하다. 모양을 고치면서
        /// 비용을 올리면 그 표를 다시 재야 하고, 그러면 아트 예산의 근거가 사라진다.
        ///
        /// 17~19 는 **일부러 남긴 여분**이다. 정점이 하나도 안 묶이지만 커브는 준다 — §7 의
        /// 규칙이 *"커브 없는 본은 평가되지 않아 측정이 더 싼 클립을 잰다"* 이기 때문이다.
        /// </summary>
        struct BoneDef { public string name; public int parent; public Vector3 off; }

        static readonly BoneDef[] Rig =
        {
            // **작고 귀엽게** (오너 2026-10-10). 전에는 키 1.86 m 의 8 등신이었다 —
            // 사람 비율이라 군중 속에서 *작은 어른*으로 보였지, 귀엽지는 않았다.
            //
            // 지금은 **키 1.2 m · 3 등신**이다. 귀여움은 장식이 아니라 **비율**이다:
            // 머리가 몸에 비해 크고(아기 비율), 팔다리가 짧고 뭉툭하다. 그리고 작아진 만큼
            // **더 빽빽하게 모을 수 있어서** 같은 수가 더 많아 보인다 (`LevelView.Disc`).
            new BoneDef { name = "hips",   parent = -1, off = new Vector3(0f, 0.40f, 0f) },
            new BoneDef { name = "spine",  parent = 0,  off = new Vector3(0f, 0.10f, 0f) },
            new BoneDef { name = "chest",  parent = 1,  off = new Vector3(0f, 0.12f, 0f) },
            new BoneDef { name = "neck",   parent = 2,  off = new Vector3(0f, 0.09f, 0f) },
            new BoneDef { name = "head",   parent = 3,  off = new Vector3(0f, 0.11f, 0f) },

            // 다리는 **짧고 통통하게**. 전에는 키의 46 % 였는데 지금은 33 % 다
            new BoneDef { name = "lhip",   parent = 0,  off = new Vector3(-0.08f, -0.03f, 0f) },
            new BoneDef { name = "lknee",  parent = 5,  off = new Vector3(0f, -0.18f, 0f) },
            new BoneDef { name = "lfoot",  parent = 6,  off = new Vector3(0f, -0.16f, 0f) },
            new BoneDef { name = "rhip",   parent = 0,  off = new Vector3(0.08f, -0.03f, 0f) },
            new BoneDef { name = "rknee",  parent = 8,  off = new Vector3(0f, -0.18f, 0f) },
            new BoneDef { name = "rfoot",  parent = 9,  off = new Vector3(0f, -0.16f, 0f) },

            // 어깨는 **몸통 반지름 밖**에 둔다. 전에 가슴 반지름보다 안쪽이라 팔이 몸에
            // 묻혔고, 화면에서는 삐져나온 조각이 *망토* 처럼 보였다
            new BoneDef { name = "lsh",    parent = 2,  off = new Vector3(-0.17f, 0.05f, 0f) },
            new BoneDef { name = "lelb",   parent = 11, off = new Vector3(0f, -0.13f, 0f) },
            new BoneDef { name = "lhand",  parent = 12, off = new Vector3(0f, -0.12f, 0f) },
            new BoneDef { name = "rsh",    parent = 2,  off = new Vector3(0.17f, 0.05f, 0f) },
            new BoneDef { name = "relb",   parent = 14, off = new Vector3(0f, -0.13f, 0f) },
            new BoneDef { name = "rhand",  parent = 15, off = new Vector3(0f, -0.12f, 0f) },

            new BoneDef { name = "x0",     parent = 0,  off = Vector3.zero },
            new BoneDef { name = "x1",     parent = 17, off = Vector3.zero },
            new BoneDef { name = "x2",     parent = 18, off = Vector3.zero },
        };

        /// <summary>
        /// **전체 배율.** 오너 2026-10-10: *"아주 작고 귀엽게"* → *"더 작아도 돼"*.
        /// 위 표의 수를 하나씩 고치는 대신 여기 한 곳에서 줄인다 — 비율(3 등신)은 그대로
        /// 두고 크기만 바뀌어야 하기 때문이다. 표를 직접 만지면 **줄이면서 비율이 어긋난다**.
        ///
        /// 따라 줄여야 하는 것 둘이 밖에 있다: `LevelView.Disc` 의 한 사람당 면적(**배율의
        /// 제곱**으로 — 면적이니까)과 `BlobShadows` 의 원반 크기. 안 줄이면 작아진 몸 사이에
        /// 빈 틈이 생겨 **오히려 덜 많아 보인다**.
        /// </summary>
        public const float Scale = 0.78f;

        /// <summary>쉬는 자세에서 본이 서 있는 **월드 좌표**. 바인드포즈와 메시가 둘 다 이것을 쓴다</summary>
        static Vector3[] RestPositions()
        {
            var p = new Vector3[Rig.Length];
            for (int i = 0; i < Rig.Length; i++)
                p[i] = Rig[i].parent < 0 ? Rig[i].off * Scale : p[Rig[i].parent] + Rig[i].off * Scale;
            return p;
        }

        /// <summary>반지름 표를 배율에 맞춰 줄인다 — 비율은 그대로, 크기만</summary>
        static float[] S(params float[] r)
        {
            var o = new float[r.Length];
            for (int i = 0; i < r.Length; i++) o[i] = r[i] * Scale;
            return o;
        }

        /// <summary>
        /// 몸을 **다섯 조각**으로 짓는다 — 몸통 · 머리 · 다리 둘 · 팔 둘. 조각마다 고리를 쌓은
        /// 원통이고, 고리의 정점은 **그 높이에 해당하는 본에 100 % 로** 묶는다 (정점당 가중치 1 —
        /// 스킨닝 비용은 가중치의 수가 정한다).
        ///
        /// 목표 정점 수(`want`)에 맞춰 고리 수와 층 수를 **같은 비율로** 키운다. 정점은 둘의
        /// 곱이므로 배율은 `sqrt(want / 기준)` 이다.
        /// </summary>
        static Mesh BuildSkinnedMesh(int want, out string[] boneNames, out Matrix4x4[] binds)
        {
            boneNames = new string[Rig.Length];
            for (int i = 0; i < Rig.Length; i++) boneNames[i] = Rig[i].name;
            var rest = RestPositions();
            binds = new Matrix4x4[Rig.Length];
            for (int i = 0; i < Rig.Length; i++) binds[i] = Matrix4x4.Translate(-rest[i]);

            var verts = new System.Collections.Generic.List<Vector3>(want + 64);
            var weights = new System.Collections.Generic.List<BoneWeight>(want + 64);
            var tris = new System.Collections.Generic.List<int>(want * 6);

            // **예산을 넘기지 않는다.** 반올림 때문에 한 번에 안 맞는다 — 300 을 달라고 했는데
            // 320 이 나왔고, `M0_CROWD` 의 상한(300/1,500)이 그 수에 걸려 있다. 넘으면 **줄여
            // 다시 짓는다**: 짐작한 배율을 쓰는 것보다 세어 보는 쪽이 확실하다
            float k = Mathf.Clamp(Mathf.Sqrt(want / 226f), 0.5f, 6f);
            for (int tries = 0; tries < 20; tries++)
            {
                verts.Clear(); weights.Clear(); tris.Clear();
                float kk = k;
                int R(int n) => Mathf.Max(4, Mathf.RoundToInt(n * kk));

                // 몸통 — 짧고 **통통하게**. 귀여움은 비율이다: 허리를 거의 안 잘록하게 둔다
                Part(verts, weights, tris, R(12), R(5), rest[0], rest[3],
                     new[] { 0, 0, 1, 2, 3 }, S(0.135f, 0.130f, 0.140f, 0.150f, 0.080f));
                // 머리 — **크다.** 키의 3 분의 1 쯤 (3 등신). 목에서 시작해야 머리 아래로
                // 뒤 배경이 비치지 않는다 — 전에 8 cm 가 비어서 확대하면 나무가 보였다
                Part(verts, weights, tris, R(11), R(6), rest[3] + new Vector3(0f, -0.01f * Scale, 0f),
                     rest[4] + new Vector3(0f, 0.22f * Scale, 0f),
                     new[] { 3, 4, 4, 4, 4, 4 }, S(0.070f, 0.062f, 0.150f, 0.175f, 0.150f, 0.055f));
                // 다리 둘 — **짧고 뭉툭하게**
                for (int sgn = 0; sgn < 2; sgn++)
                {
                    int h = sgn == 0 ? 5 : 8;
                    Part(verts, weights, tris, R(7), R(5), rest[h], rest[h + 2] + new Vector3(0f, -0.03f * Scale, 0f),
                         new[] { h, h, h + 1, h + 1, h + 2 }, S(0.070f, 0.062f, 0.058f, 0.055f, 0.052f));
                }
                // 팔 둘 — **짧고 뭉툭하게**. 6 각은 옆에서 보면 평평한 판 두 장이라 8 각으로
                for (int sgn = 0; sgn < 2; sgn++)
                {
                    int sh = sgn == 0 ? 11 : 14;
                    Part(verts, weights, tris, R(8), R(4), rest[sh], rest[sh + 2] + new Vector3(0f, -0.03f * Scale, 0f),
                         new[] { sh, sh, sh + 1, sh + 2 }, S(0.058f, 0.052f, 0.048f, 0.044f));
                }

                if (verts.Count <= want) break;
                k *= 0.96f;
            }
            if (verts.Count > want)
                Debug.LogError($"[M0] 정점이 {verts.Count} 로 예산 {want} 를 넘는다 — M0 표의 상한이 깨진다");

            var m = new Mesh { name = "standin" };
            m.indexFormat = UnityEngine.Rendering.IndexFormat.UInt16;
            m.SetVertices(verts);
            m.boneWeights = weights.ToArray();
            m.SetTriangles(tris, 0);
            // 법선은 **수평이 아니라** 실제 면에서 센다. 전부 수평이면 위에서 내려오는 빛과
            // 늘 같은 각이라 **몸 전체가 가장 어두운 값**으로 칠해진다 (2026-10-09 에 겪었다)
            m.RecalculateNormals();
            m.bindposes = binds;
            m.RecalculateBounds();
            return m;
        }

        /// <summary>원통 한 조각. `boneOf`/`radius` 는 층을 **비율로** 읽으므로 길이가 달라도 된다</summary>
        static void Part(System.Collections.Generic.List<Vector3> verts,
                         System.Collections.Generic.List<BoneWeight> weights,
                         System.Collections.Generic.List<int> tris,
                         int ring, int rows, Vector3 from, Vector3 to, int[] boneOf, float[] radius)
        {
            ring = Mathf.Max(4, ring);
            rows = Mathf.Max(2, rows);
            int baseIndex = verts.Count;
            for (int r = 0; r < rows; r++)
            {
                float t = rows == 1 ? 0f : r / (float)(rows - 1);
                var c = Vector3.Lerp(from, to, t);
                int b = boneOf[Mathf.Clamp(Mathf.RoundToInt(t * (boneOf.Length - 1)), 0, boneOf.Length - 1)];
                float rad = radius[Mathf.Clamp(Mathf.RoundToInt(t * (radius.Length - 1)), 0, radius.Length - 1)];
                for (int i = 0; i < ring; i++)
                {
                    float a = i / (float)ring * Mathf.PI * 2f;
                    verts.Add(c + new Vector3(Mathf.Cos(a) * rad, 0f, Mathf.Sin(a) * rad));
                    weights.Add(new BoneWeight { boneIndex0 = b, weight0 = 1f });
                }
            }
            for (int r = 0; r < rows - 1; r++)
                for (int i = 0; i < ring; i++)
                {
                    int i2 = (i + 1) % ring;
                    int a = baseIndex + r * ring + i, b2 = baseIndex + r * ring + i2;
                    int d = baseIndex + (r + 1) * ring + i, e = baseIndex + (r + 1) * ring + i2;
                    tris.Add(a); tris.Add(d); tris.Add(b2);
                    tris.Add(b2); tris.Add(d); tris.Add(e);
                }
        }

        /// <summary>
        /// 달리는 클립. 전에는 본 20 개를 **같은 주기, 다른 위상**으로 흔들었다 — 모양이
        /// 달리기처럼 보이는 것은 중요하지 않다고 적어 뒀는데, **보이는 쪽이 중요해졌다.**
        ///
        /// 이제 팔다리가 **엇갈린다**: 왼다리가 앞이면 오른팔이 앞이다. 그 교차 하나가
        /// *걷는 것*과 *미끄러지는 것*을 가른다.
        ///
        /// **본 20 개 전부에 커브를 준다** (여분 셋 포함) — 커브가 없는 본은 평가되지 않아
        /// 측정이 **더 싼 클립**을 재게 된다 (`M0_CROWD` §7).
        /// </summary>
        static AnimationClip BuildRunClip(string[] boneNames)
        {
            var clip = new AnimationClip { name = "standin_run", frameRate = 30f };
            clip.legacy = false;
            const float Len = 0.8f;

            void Curve(int bone, System.Func<float, float> fx, System.Func<float, float> fz)
            {
                var cx = new AnimationCurve();
                var cz = new AnimationCurve();
                for (int f = 0; f <= 16; f++)
                {
                    float t = f / 16f;
                    cx.AddKey(t * Len, fx(t));
                    cz.AddKey(t * Len, fz(t));
                }
                string path = Path(bone);
                clip.SetCurve(path, typeof(Transform), "localEulerAngles.x", cx);
                clip.SetCurve(path, typeof(Transform), "localEulerAngles.z", cz);
            }

            float Sin(float t, float off) => Mathf.Sin((t + off) * Mathf.PI * 2f);

            Curve(0, t => Sin(t, 0f) * 2.5f, t => 0f);                      // 골반이 조금 흔들린다
            Curve(1, t => 3f, t => Sin(t, 0.25f) * 1.5f);                   // 살짝 앞으로 기운다
            Curve(2, t => 0f, t => Sin(t, 0.5f) * 2f);
            Curve(3, t => Sin(t, 0.5f) * 2f, t => 0f);
            Curve(4, t => Sin(t, 0.5f) * 2f, t => 0f);

            // 다리 — 반 주기 엇갈림. 무릎은 **뒤로만** 접힌다
            Curve(5, t => Sin(t, 0f) * 26f, t => 0f);
            Curve(6, t => Mathf.Max(0f, -Sin(t, 0.15f)) * 42f, t => 0f);
            Curve(7, t => Sin(t, 0.5f) * 10f, t => 0f);
            Curve(8, t => Sin(t, 0.5f) * 26f, t => 0f);
            Curve(9, t => Mathf.Max(0f, -Sin(t, 0.65f)) * 42f, t => 0f);
            Curve(10, t => Sin(t, 0f) * 10f, t => 0f);

            // 팔 — 다리와 **반대쪽**. 왼다리가 앞이면 오른팔이 앞이다
            Curve(11, t => Sin(t, 0.5f) * 22f, t => Sin(t, 0f) * 4f);
            Curve(12, t => -18f - Mathf.Max(0f, Sin(t, 0.5f)) * 14f, t => 0f);
            Curve(13, t => 0f, t => 0f);
            Curve(14, t => Sin(t, 0f) * 22f, t => Sin(t, 0.5f) * 4f);
            Curve(15, t => -18f - Mathf.Max(0f, Sin(t, 0f)) * 14f, t => 0f);
            Curve(16, t => 0f, t => 0f);

            // 여분 셋 — 정점은 안 묶였지만 **평가는 된다**
            for (int i = 17; i < Rig.Length; i++)
            {
                int bi = i;
                Curve(bi, t => Sin(t, bi * 0.1f) * 1f, t => 0f);
            }

            var set = new AnimationClipSettings { loopTime = true };
            AnimationUtility.SetAnimationClipSettings(clip, set);
            return clip;
        }

        /// <summary>본까지의 경로. 가지가 생겼으므로 **부모를 거슬러** 만든다 (전에는 한 줄이라 0..i 였다)</summary>
        static string Path(int i)
        {
            var parts = new System.Collections.Generic.List<string>();
            for (int k = i; k >= 0; k = Rig[k].parent) parts.Add(Rig[k].name);
            parts.Reverse();
            return string.Join("/", parts);
        }

        static GameObject BuildPrefab(Mesh mesh, Matrix4x4[] binds, string[] boneNames, AnimatorController ctrl, string suffix)
        {
            var root = new GameObject("StandIn");
            var bones = new Transform[Rig.Length];
            for (int i = 0; i < Rig.Length; i++)
            {
                var go = new GameObject(Rig[i].name);
                // **표가 정한 자리에 둔다.** 메시의 바인드포즈가 같은 표에서 나왔으므로
                // 둘이 어긋날 수가 없다 — 전에는 본이 일정 간격으로 쌓이고 메시는 따로 계산했다
                go.transform.SetParent(Rig[i].parent < 0 ? root.transform : bones[Rig[i].parent], false);
                go.transform.localPosition = Rig[i].off;
                bones[i] = go.transform;
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
