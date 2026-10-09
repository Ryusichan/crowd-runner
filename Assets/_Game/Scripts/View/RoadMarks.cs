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
        Material mat;
        RenderParams rp;
        Matrix4x4[] mats = new Matrix4x4[0];
        int count;

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
                var sh = Shader.Find("Legacy Shaders/Diffuse") ?? Shader.Find("Standard");
                mat = new Material(sh) { name = "RoadMark" };
                if (mat.HasProperty("_Color")) mat.color = new Color(0.86f, 0.85f, 0.78f);
                if (mat.HasProperty("_Glossiness")) mat.SetFloat("_Glossiness", 0f);
                // **인스턴싱을 켜지 않으면 `RenderMeshInstanced` 가 조용히 한 개만 그린다**
                mat.enableInstancing = true;
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

        public void Draw()
        {
            if (mesh == null || count == 0) return;
            Graphics.RenderMeshInstanced(rp, mesh, 0, mats, count);
        }
    }
}
