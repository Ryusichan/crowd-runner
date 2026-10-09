using CrowdRunner.Crowd;
using UnityEngine;
using UnityEngine.Rendering;

namespace CrowdRunner.View
{
    /// <summary>
    /// **발이 땅에 닿아 보이게 한다** — 지금까지 군중이 공중에 떠 있었다.
    ///
    /// 그림자가 없으면 눈이 **깊이를 못 읽는다.** 세로 화면을 위에서 비스듬히 보는 구도라
    /// 특히 그렇다: 화면 위쪽에 있는 몸이 *멀리 있는 것*인지 *떠 있는 것*인지 단서가 없다.
    /// 첫 플레이 화면에서 군중이 길 위에 **붙어 있지 않아** 보인 것이 이것이다.
    ///
    /// **진짜 그림자는 안 쓴다.** 400 개체의 그림자 맵은 드로우를 두 배로 만든다
    /// (`docs/M0_CROWD.md` §6b — 그래서 `VatCrowd` 도 `shadowCastingMode.Off` 다).
    /// 대신 발밑에 **납작한 원반**을 하나씩 깐다: 드로우 **한 번**, 조명 계산 없음.
    ///
    /// 광고형 러너가 전부 이렇게 한다. 비용 때문만이 아니라 **이쪽이 더 잘 읽히기** 때문이다 —
    /// 진짜 그림자는 해 방향으로 길게 늘어져 *어느 발 아래인지*가 흐려진다.
    /// </summary>
    public sealed class BlobShadows
    {
        const int Cap = 900;

        Mesh mesh;
        Material mat;
        RenderParams rp;
        readonly Matrix4x4[] mats = new Matrix4x4[Cap];

        public void Init()
        {
            // 원반은 실린더 메시를 납작하게 눌러서 쓴다 — 원반 메시를 따로 만들 이유가 없다
            var tmp = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            mesh = tmp.GetComponent<MeshFilter>().sharedMesh;
            Object.DestroyImmediate(tmp);

            // 위 `RoadMarks` 와 같은 이유로 **구워 둔 에셋**을 쓴다 (런타임 머티리얼은
            // 빌드에서 인스턴싱 변형이 없어 조용히 안 그려진다)
            mat = Resources.Load<Material>("M0/blobshadow");
            if (mat == null)
            {
                Debug.LogError("[CR] M0/blobshadow.mat 이 없다 — 발밑 그림자가 안 그려지고, " +
                               "그러면 군중이 땅에서 떠 보인다 (M0Assets.BakeFlatMaterials)");
                return;
            }
            rp = new RenderParams(mat)
            {
                worldBounds = new Bounds(Vector3.zero, new Vector3(200f, 20f, 400f)),
                shadowCastingMode = ShadowCastingMode.Off,
                receiveShadows = false,
                lightProbeUsage = LightProbeUsage.Off,
                reflectionProbeUsage = ReflectionProbeUsage.Off,
            };
        }

        /// <summary>
        /// 살아 있는 몸 아래에만 깐다. **쓰러지는 몸(`CasualtyFx`)에는 안 깐다** — 그쪽은
        /// 가라앉으면서 사라지는데 그림자가 남아 있으면 *없는 사람의 그림자*가 보인다.
        /// </summary>
        public void Draw(CrowdField a, CrowdField b, CasualtyFx fx)
        {
            if (mesh == null) return;
            int n = 0;
            n = Add(a, n);
            n = Add(b, n);
            if (n > 0) Graphics.RenderMeshInstanced(rp, mesh, 0, mats, n);
        }

        int Add(CrowdField f, int n)
        {
            if (f == null) return n;
            for (int i = 0; i < f.Count && n < Cap; i++)
                // 실린더는 높이 2 짜리라 y 배율 0.01 이면 두께 0.02 m 다. 0 으로 두면
                // 땅과 **같은 평면**이 되어 z 싸움으로 지직거린다
                mats[n++] = Matrix4x4.TRS(new Vector3(f.X[i], 0.02f, f.Z[i]),
                                          Quaternion.identity, new Vector3(0.58f, 0.01f, 0.58f));
            return n;
        }
    }
}
