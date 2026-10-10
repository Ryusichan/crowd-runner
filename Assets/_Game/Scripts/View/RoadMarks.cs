using UnityEngine;
using UnityEngine.Rendering;

namespace CrowdRunner.View
{
    /// <summary>
    /// **달리고 있다는 것을 보이게 한다** — 지금까지 없던 것.
    ///
    /// 길이 **균일한 판**이면 앞으로 가는지 서 있는지 알 수가 없다. 군단은 화면 가운데에
    /// 고정돼 있고(카메라가 따라간다) 배경도 단색이라, 첫 플레이 화면에서 움직임을 말해 주는
    /// 것이 **하나도 없었다.** 러너에서 속도감은 장식이 아니라 **장르 그 자체**다.
    ///
    /// 두 가지를 둔다. 둘이 하는 일이 다르다:
    /// - **중앙선 점선** — 화면 가운데라 눈이 늘 보고 있다. 다만 원근 때문에 멀리서는 느리게
    ///   보인다 (소실점으로 모인다)
    /// - **길가 기둥** — 화면 **가장자리**를 스쳐 지나간다. 가장자리는 각속도가 커서 같은
    ///   속도가 훨씬 빠르게 읽힌다. 속도감의 대부분이 여기서 온다
    ///
    /// ## 드로우 한 번
    ///
    /// 표식은 판마다 50 개쯤이다. 오브젝트로 두면 **드로우가 50 번** 나는데, 군중 400 명이
    /// 한 번인 것에 비하면 터무니없다 (`docs/M0_CROWD.md` §6b: 예산은 평균이 아니라 최악에
    /// 걸린다). 그래서 위치를 한 번만 계산해 두고 `RenderMeshInstanced` 로 한 번에 그린다.
    ///
    /// 위치는 **판 전체에 미리 깔아 둔다** — 매 프레임 카메라를 따라 옮기지 않는다. 옮기면
    /// 표식이 군단과 **같이 움직여서** 속도가 0 으로 보인다. 그게 이 효과가 막으려는 바로 그것이다.
    /// </summary>
    public sealed class RoadMarks
    {
        /// <summary>중앙선 간격 (m). 좁으면 멀리서 한 줄로 뭉치고, 넓으면 띄엄띄엄해 속도가 안 읽힌다</summary>
        const float DashEvery = 7f;
        /// <summary>길가 기둥 간격 (m). 중앙선보다 **촘촘하지 않게** — 둘이 같은 주기면 하나로 보인다</summary>
        const float PostEvery = 11f;

        Mesh mesh;
        Material mat, treeMat;
        RenderParams rp, treeRp;
        Matrix4x4[] mats = new Matrix4x4[0];
        Matrix4x4[] trees = new Matrix4x4[0];
        int count, treeCount;

        public int Count => count;

        public void Build(float length, float roadWidth)
        {
            if (mesh == null)
            {
                var tmp = GameObject.CreatePrimitive(PrimitiveType.Cube);
                mesh = tmp.GetComponent<MeshFilter>().sharedMesh;
                Object.DestroyImmediate(tmp);
            }
            if (mat == null)
            {
                // **구워 둔 에셋을 쓴다.** 런타임에 `new Material(Shader.Find(...))` 로 만들고
                // `enableInstancing` 을 켜면 **에디터에서는 그려지고 빌드에서는 안 그려진다** —
                // 빌드는 쓰이는 셰이더 변형만 남기는데, 런타임에 켜는 인스턴싱은 빌드 시점에
                // 아무도 모른다. 2026-10-09 웹 빌드에서 표식이 통째로 사라졌던 자리다
                mat = Resources.Load<Material>("M0/roadmark");
                if (mat == null)
                {
                    Debug.LogError("[CR] M0/roadmark.mat 이 없다 — 길 표식이 안 그려지고, " +
                                   "그러면 달리고 있다는 것이 화면에 없다 (M0Assets.BakeFlatMaterials)");
                    return;
                }
                rp = new RenderParams(mat)
                {
                    worldBounds = new Bounds(new Vector3(0f, 0f, length * 0.5f),
                                             new Vector3(200f, 20f, length + 120f)),
                    shadowCastingMode = ShadowCastingMode.Off,
                    receiveShadows = false,
                    lightProbeUsage = LightProbeUsage.Off,
                    reflectionProbeUsage = ReflectionProbeUsage.Off,
                };
            }

            if (treeMat == null)
            {
                treeMat = Resources.Load<Material>("M0/roadtree");
                if (treeMat != null)
                    treeRp = new RenderParams(treeMat)
                    {
                        worldBounds = new Bounds(new Vector3(0f, 0f, length * 0.5f),
                                                 new Vector3(240f, 30f, length + 120f)),
                        shadowCastingMode = ShadowCastingMode.Off,
                        receiveShadows = false,
                        lightProbeUsage = LightProbeUsage.Off,
                        reflectionProbeUsage = ReflectionProbeUsage.Off,
                    };
            }

            // 판 앞뒤로 조금 넘겨 깐다 — 시작 지점 뒤가 비어 있으면 **출발하는 순간** 표식이
            // 없어서 그 몇 초가 가장 안 움직이는 것처럼 보인다
            float from = -40f, to = length + 60f;
            int dashes = Mathf.Max(0, Mathf.CeilToInt((to - from) / DashEvery));
            int posts = Mathf.Max(0, Mathf.CeilToInt((to - from) / PostEvery)) * 2;
            if (mats.Length < dashes + posts) mats = new Matrix4x4[dashes + posts];

            count = 0;
            for (int i = 0; i < dashes; i++)
                mats[count++] = Matrix4x4.TRS(new Vector3(0f, 0.03f, from + i * DashEvery),
                                              Quaternion.identity, new Vector3(0.22f, 0.04f, 2.6f));
            BuildTrees(from, to, roadWidth);
            float side = roadWidth * 0.5f + 1.35f;
            for (int i = 0; i < posts / 2; i++)
            {
                float z = from + i * PostEvery;
                mats[count++] = Matrix4x4.TRS(new Vector3(-side, 0.85f, z), Quaternion.identity,
                                              new Vector3(0.22f, 1.7f, 0.22f));
                mats[count++] = Matrix4x4.TRS(new Vector3(+side, 0.85f, z), Quaternion.identity,
                                              new Vector3(0.22f, 1.7f, 0.22f));
            }
        }

        /// <summary>
        /// **길 밖이 허공이면 세상이 아니다.** 초록 벌판만 있으면 달리는 곳에 *바깥* 이 없고,
        /// 그러면 길이 공중에 떠 있는 띠로 돌아간다 (땅을 깔기 전과 같은 증상이 색만 바뀐 것).
        ///
        /// 나무는 **기둥보다 띄엄띄엄, 더 멀리, 더 크게** 둔다. 가까이 촘촘히 두면 길을
        /// 가리고, 기둥과 주기가 같으면 하나로 보인다 (기둥 11 m · 나무 19 m).
        /// 드로우 **한 번** 더 — 색이 달라 같은 묶음에 못 넣는다.
        /// </summary>
        void BuildTrees(float from, float to, float roadWidth)
        {
            const float Every = 19f;
            int rows = Mathf.Max(0, Mathf.CeilToInt((to - from) / Every));
            if (trees.Length < rows * 2) trees = new Matrix4x4[rows * 2];
            treeCount = 0;
            var rng = new System.Random(4711);
            for (int i = 0; i < rows; i++)
            {
                float z = from + i * Every;
                for (int sgn = -1; sgn <= 1; sgn += 2)
                {
                    // 자리와 크기를 조금씩 흔든다 — 같은 간격·같은 크기면 **울타리**로 보인다
                    // **멀리, 낮게.** 처음엔 길에서 5.5 m 에 최대 6 m 높이로 뒀더니 화면 위
                    // 가장자리에서 **잘린 검은 덩어리**로 보였고, 일시정지 단추까지 덮었다.
                    // 배경은 배경으로 읽혀야지 앞에 나서면 안 된다
                    float off = roadWidth * 0.5f + 8.5f + (float)rng.NextDouble() * 6f;
                    float h = 2.6f + (float)rng.NextDouble() * 1.8f;
                    float wdt = 1.6f + (float)rng.NextDouble() * 1.2f;
                    trees[treeCount++] = Matrix4x4.TRS(
                        new Vector3(sgn * off, h * 0.5f, z + (float)rng.NextDouble() * 8f),
                        Quaternion.Euler(0f, (float)rng.NextDouble() * 90f, 0f),
                        new Vector3(wdt, h, wdt));
                }
            }
        }

        public void Draw()
        {
            if (mesh == null) return;
            if (count > 0) Graphics.RenderMeshInstanced(rp, mesh, 0, mats, count);
            if (treeCount > 0 && treeMat != null) Graphics.RenderMeshInstanced(treeRp, mesh, 0, trees, treeCount);
        }
    }
}
