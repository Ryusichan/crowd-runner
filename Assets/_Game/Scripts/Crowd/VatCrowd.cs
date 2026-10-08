using UnityEngine;
using UnityEngine.Rendering;

namespace CrowdRunner.Crowd
{
    /// <summary>
    /// **방식 B — 인스턴싱 + 정점 애니메이션.** 드로우 **한 번**으로 군중 전체를 그린다.
    ///
    /// 방식 A 가 S21 에서 **60~70 개체**에 8 ms 예산을 다 썼고 (`docs/M0_CROWD.md` §6) 부산 10 판의
    /// 최대 병력은 30~150 이다. 그래서 이것은 최적화가 아니라 **이 군중이 존재하는 방법**이다.
    ///
    /// A 와 다른 점은 셋이다: 유닛당 `GameObject` 가 **없고**(1,000 개의 트랜스폼·`Animator` 가
    /// 사라진다), 본 평가가 **없고**(셰이더가 구운 위치를 읽는다), 드로우가 **하나**다.
    ///
    /// **`RenderMeshInstanced` 를 쓴다** (`DrawMeshInstanced` 아님): 후자는 호출당 1,023 개 상한이
    /// 있어 1,000 을 넘기면 **호출이 둘로 갈라지고** 그 경계가 측정에 섞인다. 전자는 버퍼를 쓴다.
    ///
    /// **프레임당 할당 0 B** 가 예산이다 (M0 §4). 그래서 인스턴스 배열을 한 번만 잡고 매 프레임
    /// **덮어쓴다** — `new` 가 한 번이라도 프레임 안에 있으면 1,000 개 × 60 fps 로 GC 가 깨어난다.
    /// </summary>
    public sealed class VatCrowd : ICrowdRenderer
    {
        /// <summary>
        /// 인스턴스 하나가 셰이더로 보내는 것. **첫 필드가 `Matrix4x4 objectToWorld` 여야 한다** —
        /// `RenderMeshInstanced` 의 규약이고, 나머지 필드는 이름으로 셰이더의 인스턴스 프로퍼티에 붙는다.
        /// </summary>
        struct Inst
        {
            public Matrix4x4 objectToWorld;
            public float _Phase;    // 걸음 위상 — 전부 같으면 1,000 명이 한 몸으로 움직인다
            public float _Side;     // 0 = 아군, 1 = 적 (색만 가른다)
        }

        Mesh mesh;
        Material mat;
        Inst[] inst;
        int live;
        bool on;
        RenderParams rp;

        public int DrawCalls => on && live > 0 ? 1 : 0;

        readonly string suffix;
        /// <summary>`""` = 1,500 정점 · `"_lo"` = 300 정점. **같은 방식, 다른 폴리 예산**을 나란히 잰다</summary>
        public VatCrowd(string suffix = "") { this.suffix = suffix; }

        public void Init(int cap)
        {
            mesh = Resources.Load<Mesh>("M0/standin_vat_mesh" + suffix);
            mat = Resources.Load<Material>("M0/standin_vat" + suffix);
            inst = new Inst[cap];
            if (mesh == null || mat == null)
            {
                // **없는 것을 조용히 0 으로 재지 않는다.** 빈 구현이 `0 ms` 를 돌려주면
                // "B 는 공짜" 로 읽히고, 그게 이 자리에서 가능한 가장 나쁜 결과다
                Debug.LogError("[M0] VAT 자산이 없다 (mesh=" + (mesh != null) + " mat=" + (mat != null) +
                               ") — VatBaker.Bake 를 먼저 돌려라. B 측정은 **무효**다");
                return;
            }
            rp = new RenderParams(mat)
            {
                // **경계 상자를 손으로 준다.** 안 주면 Unity 가 인스턴스마다 컬링을 하려 들고,
                // 군중은 어차피 한 덩어리로 화면에 있으니 그 계산이 순수한 낭비다
                worldBounds = new Bounds(Vector3.zero, new Vector3(200f, 20f, 400f)),
                shadowCastingMode = ShadowCastingMode.Off,
                receiveShadows = false,
                lightProbeUsage = LightProbeUsage.Off,
                reflectionProbeUsage = ReflectionProbeUsage.Off,
            };
        }

        public void SetActive(bool v) { on = v; if (!v) live = 0; }

        public void Sync(CrowdField f)
        {
            if (mesh == null || mat == null) { live = 0; return; }
            int n = Mathf.Min(f.Count, inst.Length);
            live = n;
            for (int i = 0; i < n; i++)
            {
                // 적은 반대쪽을 본다 — 두 군단이 마주 달리는 것이 보여야 겹치는 순간이 읽힌다.
                // `Matrix4x4.TRS` 는 구조체 연산이라 할당이 없다
                inst[i].objectToWorld = Matrix4x4.TRS(
                    new Vector3(f.X[i], 0f, f.Z[i]),
                    f.Side[i] == 0 ? Fwd : Back,
                    Vector3.one);
                inst[i]._Phase = f.Phase[i];
                inst[i]._Side = f.Side[i];
            }
        }

        /// <summary>
        /// 증식 봉우리 — **이 방식에는 봉우리가 없다.** 켜고 끌 `GameObject` 가 없으니 수가 늘어도
        /// 배열에 한 줄 더 쓰는 것이 전부다. 그 **없음이 방식 B 의 값 중 하나**이므로, 아무것도
        /// 안 하는 것이 맞는 구현이다 (A 는 여기서 `SetActive` 240 번을 치렀다).
        /// </summary>
        public void Churn(CrowdField f, int n) { }

        /// <summary>
        /// **`GameManager.Update` 가 아니라 여기서 그린다.** `RenderMeshInstanced` 는 그 프레임에만
        /// 유효한 즉시 호출이라 매 프레임 불러야 한다 — `Sync` 와 나눠 둔 이유는 `Sync` 가 고정
        /// 스텝에 묶이고 그리기는 프레임마다여야 하기 때문이다.
        /// </summary>
        static readonly Quaternion Fwd = Quaternion.identity;
        static readonly Quaternion Back = Quaternion.Euler(0f, 180f, 0f);

        public void Draw()
        {
            if (!on || live == 0 || mesh == null) return;
            Graphics.RenderMeshInstanced(rp, mesh, 0, inst, live);
        }
    }
}
