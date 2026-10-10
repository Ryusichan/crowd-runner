using System.IO;
using UnityEditor;
using UnityEngine;

namespace CrowdRunner.EditorTools
{
    /// <summary>
    /// **정점 애니메이션을 텍스처에 굽는다** — 방식 B 의 절반 (나머지 절반은 `Shaders/CrowdVat.shader`).
    ///
    /// 왜 이것이 필요한가: 방식 A 가 S21 에서 **60~70 개체**에 8 ms 예산을 다 썼고
    /// (`docs/M0_CROWD.md` §6) 부산 10 판의 최대 병력은 30~150 이다. 선택이 아니라 유일한 길이다.
    ///
    /// 하는 일: 클립을 `Frames` 개 시점에서 샘플링하고, 그때마다 `SkinnedMeshRenderer.BakeMesh` 로
    /// **그 순간의 정점 위치**를 떠서 텍스처 한 줄에 적는다. 가로 = 정점, 세로 = 프레임.
    ///
    /// 그리고 **정적 메시** 하나를 같이 만든다 — 같은 삼각형, `uv2.x` 에 정점 번호를 담은 것.
    /// 셰이더가 그 번호로 자기 픽셀을 찾는다. 이 메시에는 본도 가중치도 없다.
    ///
    /// 배치 모드로 돈다 (오너 규칙: 에디터 창을 띄우지 않는다). `M0Build` 가 대역을 구운 **뒤에** 부른다.
    /// </summary>
    public static class VatBaker
    {
        /// <summary>
        /// 구울 프레임 수. 32 는 걸음 한 주기에 충분하고 **2 의 거듭제곱**이라 텍스처가 깔끔하다.
        ///
        /// 텍스처 크기 = 정점 1,500 × 32 × RGBAHalf(8 B) = **384 KB**. 이 값을 올리면 선형으로 는다 —
        /// 64 로 하면 768 KB 이고, 그 차이를 화면에서 구분할 수 없다 (걸음은 0.8 초짜리 루프다).
        /// </summary>
        public const int Frames = 32;

        const string Dir = "Assets/_Game/Resources/M0";

        [MenuItem("M0/Bake Vertex Animation")]
        public static void Bake() { Bake(""); }

        /// <summary>`suffix` 로 어느 대역을 구울지 가른다 (저폴리 비교용 — `M0Assets.Bake` 주석)</summary>
        public static void Bake(string suffix)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Dir + "/StandIn" + suffix + ".prefab");
            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(Dir + "/standin_run" + suffix + ".anim");
            if (prefab == null || clip == null)
            {
                Debug.LogError("[M0] 대역이 없다 — M0Assets.Bake 를 먼저 돌려라. VAT 를 구울 수 없다");
                return;
            }

            var inst = Object.Instantiate(prefab);
            var smr = inst.GetComponentInChildren<SkinnedMeshRenderer>();
            var src = smr.sharedMesh;
            int n = src.vertexCount;

            // **`RGBAHalf` 를 쓴다.** 위치가 ±2 m 안이라 half(약 3 자리)로 **밀리미터** 정밀도가 나온다.
            // `RGBAFloat` 면 텍스처가 두 배이고 그 정밀도는 눈에 안 보인다.
            var tex = new Texture2D(n, Frames, TextureFormat.RGBAHalf, false, true)
            {
                name = "standin_vat" + suffix,
                // **점 필터 · 반복 없음.** 쌍선형이면 이웃 프레임·이웃 정점이 섞여 몸이 떨린다 —
                // 셰이더가 픽셀 중심을 집는 것과 한 쌍이다
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
            };

            var px = new Color[n * Frames];
            var baked = new Mesh();
            var buf = new System.Collections.Generic.List<Vector3>(n);
            for (int f = 0; f < Frames; f++)
            {
                // **첫 프레임과 마지막이 이어져야 한다.** `f / Frames` 로 나누면 마지막 프레임이
                // 루프의 끝에 닿지 않아 `frac` 으로 돌 때 **점프가 보인다**. 클립이 루프이므로
                // `length * f / Frames` 가 맞는 자리다 (f=Frames 는 f=0 과 같은 자세다)
                clip.SampleAnimation(inst, clip.length * f / Frames);
                smr.BakeMesh(baked, true);
                baked.GetVertices(buf);
                for (int i = 0; i < n; i++)
                {
                    var v = buf[i];
                    px[f * n + i] = new Color(v.x, v.y, v.z, 1f);
                }
            }
            tex.SetPixels(px);
            tex.Apply(false, false);

            // 정적 메시 — 삼각형은 같고, `uv2.x` 에 정점 번호를 정규화해 담는다.
            // 정점 위치는 0 프레임 것을 넣는다: **경계 상자**가 거기서 나오고, 상자가 틀리면
            // 컬링이 틀려 **화면 안의 군중이 사라진다**
            clip.SampleAnimation(inst, 0f);
            smr.BakeMesh(baked, true);
            var mesh = new Mesh { name = "standin_vat_mesh" + suffix, indexFormat = src.indexFormat };
            mesh.SetVertices(baked.vertices);
            mesh.SetNormals(src.normals);
            mesh.SetTriangles(src.triangles, 0);
            var ids = new Vector2[n];
            for (int i = 0; i < n; i++) ids[i] = new Vector2(i / (float)(n - 1), 0f);
            mesh.SetUVs(1, ids);
            // **상자를 넉넉하게 키운다.** 0 프레임 자세만으로 잡으면 팔을 든 프레임에서 몸이
            // 상자를 넘고, 그러면 화면 가장자리에서 **깜빡이며 사라진다**
            var b = mesh.bounds; b.Expand(1.2f); mesh.bounds = b;

            Object.DestroyImmediate(inst);
            Object.DestroyImmediate(baked);

            Directory.CreateDirectory(Dir);
            AssetDatabase.CreateAsset(tex, Dir + "/standin_vat" + suffix + ".asset");
            AssetDatabase.CreateAsset(mesh, Dir + "/standin_vat_mesh" + suffix + ".asset");

            var sh = Shader.Find("Game/CrowdVat");
            if (sh == null) { Debug.LogError("[M0] Game/CrowdVat 셰이더를 못 찾았다 — VAT 는 안 그려진다"); return; }
            var mat = new Material(sh) { name = "standin_vat" + suffix, enableInstancing = true };
            // **군단과 적이 색으로 갈려야 한다.** 셰이더 기본값은 베이지/붉은색인데, 길이
            // 회색이라 베이지 군중이 **길에 묻혔다** (첫 플레이 화면). 이 게임에서 한눈에
            // 읽어야 하는 것은 *내 군단이 얼마나 큰가* 와 *앞의 것이 적인가* 둘뿐이다.
            //
            // 밝은 청록을 쓰는 이유: 길(회) · 풀(녹) · 연석(흰) 과 **색상으로** 갈리고,
            // 적의 붉은색과는 **보색**이라 섞인 난전에서도 두 편이 구분된다
            if (mat.HasProperty("_Color")) mat.SetColor("_Color", new Color(0.46f, 0.76f, 0.86f));
            if (mat.HasProperty("_EnemyColor")) mat.SetColor("_EnemyColor", new Color(0.88f, 0.42f, 0.38f));
            mat.SetTexture("_Vat", tex);
            mat.SetFloat("_Frames", Frames);
            mat.SetFloat("_Verts", n);
            mat.SetFloat("_ClipSpeed", 1f / Mathf.Max(0.1f, clip.length));
            AssetDatabase.CreateAsset(mat, Dir + "/standin_vat" + suffix + ".mat");

            // **적은 머티리얼을 따로 쓴다.**
            //
            // 셰이더에 인스턴스별 `_Side`(0=아군 1=적)가 있고 `VatCrowd.Sync` 가 그 값을 넣는데,
            // **화면에서는 안 먹는다** — 적 무리를 처음 그려 보고 화소로 재서 알았다:
            // 앞의 무리 평균 RGB (47,74,83) · 내 군단 (52,82,96). 빨강이면 R 이 커야 하는데
            // 둘 다 청록이다. `Graphics.RenderMeshInstanced` 의 구조체 경로에서 그 값이
            // 셰이더까지 안 가는 것으로 보인다.
            //
            // 깊이 파는 대신 **돌아간다**: 색이 다른 머티리얼 하나를 더 굽는다. 이 게임에서
            // *앞의 것이 적인가* 는 한눈에 읽혀야 하는 두 가지 중 하나라, **확실한 쪽**을 쓴다.
            // 런타임에 `new Material` 로 만들지 않는 이유는 오늘 배웠다 — 빌드가 인스턴싱
            // 변형을 안 남긴다 (`RoadMarks` 주석).
            var foe = new Material(sh) { name = "standin_vat" + suffix + "_foe", enableInstancing = true };
            if (foe.HasProperty("_Color")) foe.SetColor("_Color", new Color(0.90f, 0.36f, 0.33f));
            foe.SetTexture("_Vat", tex);
            foe.SetFloat("_Frames", Frames);
            foe.SetFloat("_Verts", n);
            foe.SetFloat("_ClipSpeed", 1f / Mathf.Max(0.1f, clip.length));
            AssetDatabase.CreateAsset(foe, Dir + "/standin_vat" + suffix + "_foe.mat");

            AssetDatabase.SaveAssets();

            Debug.Log($"[M0] baked VAT{suffix}: verts={n} frames={Frames} tex={n}x{Frames} " +
                      $"{(n * Frames * 8) / 1024}KB clip={clip.length:F2}s instancing={mat.enableInstancing}");
        }
    }
}
