using UnityEngine;
using UnityEngine.Rendering;
using CrowdRunner.Core;

namespace CrowdRunner.View
{
    /// <summary>
    /// **총격이 보이는 자리** — 탄환 궤적 · 총구 화염 · 피격 불꽃 · 쓰러진 몸.
    ///
    /// 오너가 완료로 인정하지 않는 것 둘이 있다 (`docs/COMBAT_REWORK.md` §6):
    /// *숫자만 줄어드는 전투* 와 *총알 효과만 표시하는 가짜 사격*. 이 파일은 **뒤쪽이 되지
    /// 않도록** 짜여 있다 — 여기서 만드는 좌표가 하나도 없다. 탄환 자리도, 맞은 자리도,
    /// 쓰러진 자리도 전부 `Core/Battle` 이 계산한 것을 **읽기만** 한다. 그래서 화면에 보이는
    /// 한 발은 실제로 날아가 실제로 HP 를 깎은 그 한 발이다.
    ///
    /// 네 가지를 **인스턴싱 네 번**으로 그린다. `GameObject` 를 하나도 만들지 않는 이유는
    /// 탄환이 초당 240 발 나가기 때문이다 — 개체로 만들면 생성·파괴만으로 예산이 끝난다.
    ///
    /// 머티리얼은 **구운 에셋**을 쓴다. 런타임 `new Material` 은 빌드에 인스턴싱 변형을
    /// 안 남겨서 **웹에서만 통째로 안 그려진다** (`View/RoadMarks.cs` 주석, 2026-10-09).
    /// </summary>
    public sealed class Gunfire
    {
        const int Cap = Battle.BulletCap;
        const int MuzzleCap = 160;
        const int DownCap = 256;
        const int SparkCap = 96;

        Mesh mesh;
        Material matTracer, matMuzzle, matDown;
        RenderParams rpTracer, rpMuzzle, rpDown;

        readonly Matrix4x4[] tracers = new Matrix4x4[Cap];
        readonly Matrix4x4[] muzzles = new Matrix4x4[MuzzleCap];
        readonly Matrix4x4[] downs = new Matrix4x4[DownCap];
        readonly Matrix4x4[] sparks = new Matrix4x4[SparkCap];

        /// <summary>피격 불꽃은 **한 틱짜리 사건**이라 여기서 수명을 들고 있어야 한다</summary>
        readonly float[] sparkX = new float[SparkCap], sparkZ = new float[SparkCap], sparkT = new float[SparkCap];
        int sparkCount;
        const float SparkLife = 0.09f;

        int frame;

        public int LastTracers { get; private set; }
        public int LastMuzzles { get; private set; }

        public void Init()
        {
            var tmp = GameObject.CreatePrimitive(PrimitiveType.Cube);
            mesh = tmp.GetComponent<MeshFilter>().sharedMesh;
            Object.Destroy(tmp);

            matTracer = Resources.Load<Material>("M0/tracer");
            matMuzzle = Resources.Load<Material>("M0/muzzle");
            matDown = Resources.Load<Material>("M0/foedown");
            if (matTracer == null || matMuzzle == null || matDown == null)
            {
                // **조용한 빈칸을 만들지 않는다.** `Resources.Load` 는 없으면 null 을 돌려주고
                // 아무 말도 안 한다 — 그러면 "총을 쏘는데 안 보인다" 가 원인 없이 남는다
                Debug.LogError("[CR] 총격 머티리얼이 없다 (tracer=" + (matTracer != null) +
                               " muzzle=" + (matMuzzle != null) + " foedown=" + (matDown != null) +
                               ") — M0Assets.BakeFlatMaterials 를 먼저 돌려라");
                return;
            }
            rpTracer = Params(matTracer);
            rpMuzzle = Params(matMuzzle);
            rpDown = Params(matDown);
        }

        static RenderParams Params(Material m) => new RenderParams(m)
        {
            worldBounds = new Bounds(Vector3.zero, new Vector3(200f, 20f, 400f)),
            shadowCastingMode = ShadowCastingMode.Off,
            receiveShadows = false,
            lightProbeUsage = LightProbeUsage.Off,
            reflectionProbeUsage = ReflectionProbeUsage.Off,
        };

        /// <param name="frontZ">군단 앞줄. 총구가 거기서 나온다</param>
        public void Draw(Battle b, float frontX, float frontZ, float dt)
        {
            if (matTracer == null) return;
            frame++;

            // ── 탄환 ──────────────────────────────────────────────────────────────
            // 길쭉한 막대로 그린다. 점으로 그리면 30 m/s 에서 **프레임 사이를 건너뛰어** 점선이
            // 되고, 길이를 주면 그 건너뜀이 궤적으로 읽힌다 (실제 총알 사진이 그렇게 찍힌다)
            int n = b.BulletCount; if (n > Cap) n = Cap;
            for (int i = 0; i < n; i++)
            {
                var dir = new Vector3(b.BDX[i], 0f, b.BDZ[i]);
                if (dir.sqrMagnitude < 1e-6f) dir = Vector3.forward;
                tracers[i] = Matrix4x4.TRS(
                    new Vector3(b.BX[i], 1.05f, b.BZ[i]),
                    Quaternion.LookRotation(dir, Vector3.up),
                    new Vector3(0.07f, 0.07f, 0.85f));
            }
            LastTracers = n;
            if (n > 0) Graphics.RenderMeshInstanced(rpTracer, mesh, 0, tracers, n);

            // ── 총구 화염 ─────────────────────────────────────────────────────────
            // **쏜 병사 자리에서 터진다.** 전에는 발사율로 개수만 맞춰 아무 데나 뿌렸는데,
            // 그러면 *군단이 쏘는 그림*이 아니라 *앞쪽에서 불이 난 그림*이다. 로직이 병사마다
            // `AFlash` 를 켜 주므로(`Battle.FirePerSoldier`) 여기서는 그것만 읽는다 —
            // 화면에 보이는 불 하나가 **실제로 일어난 발사 판정 하나**다
            int m = 0;
            for (int i = 0; i < b.AllyCount && m < MuzzleCap; i++)
            {
                if (b.AFlash[i] <= 0f) continue;
                float k = b.AFlash[i] / Battle.FlashTime;
                float s = 0.13f + k * 0.22f;
                muzzles[m++] = Matrix4x4.TRS(
                    new Vector3(b.AX[i], 1.06f, b.AZ[i] + 0.6f),
                    Quaternion.identity,
                    new Vector3(s, s, s * 1.7f));
            }
            LastMuzzles = m;
            if (m > 0) Graphics.RenderMeshInstanced(rpMuzzle, mesh, 0, muzzles, m);

            // ── 피격 불꽃 ─────────────────────────────────────────────────────────
            for (int i = 0; i < b.HitCount && sparkCount < SparkCap; i++)
            {
                int k = sparkCount++;
                sparkX[k] = b.HitX[i]; sparkZ[k] = b.HitZ[i]; sparkT[k] = 0f;
            }
            for (int i = sparkCount - 1; i >= 0; i--)
            {
                sparkT[i] += dt;
                if (sparkT[i] >= SparkLife)
                {
                    int last = --sparkCount;
                    sparkX[i] = sparkX[last]; sparkZ[i] = sparkZ[last]; sparkT[i] = sparkT[last];
                    continue;
                }
                float k = 1f - sparkT[i] / SparkLife;
                float s = 0.14f + k * 0.34f;
                sparks[i] = Matrix4x4.TRS(new Vector3(sparkX[i], 1.0f, sparkZ[i]), Quaternion.identity,
                                          new Vector3(s, s, s));
            }
            if (sparkCount > 0) Graphics.RenderMeshInstanced(rpMuzzle, mesh, 0, sparks, sparkCount);

            // ── 쓰러진 몸 ─────────────────────────────────────────────────────────
            // 군중 렌더러는 선 자세 하나뿐이라(VAT 한 클립) **눕힐 수가 없다.** 그래서 죽은
            // 적은 군중에서 빼고 여기서 **바닥에 누운 납작한 몸**으로 그린다. 사라지는 것과
            // 쓰러지는 것은 다르다 — 사라지면 *맞았는지* 가 화면에 안 남는다
            int d = 0;
            for (int i = 0; i < b.EnemyCount && d < DownCap; i++)
            {
                if (b.EState[i] != 2) continue;
                float t = Mathf.Clamp01(b.EDieT[i] / Battle.DeathTime);
                // 넘어가는 동안 높이가 줄고 길이가 는다 — 서 있던 것이 눕는 모양
                float h = Mathf.Lerp(0.9f, 0.14f, t);
                float len = Mathf.Lerp(0.5f, 1.25f, t);
                downs[d++] = Matrix4x4.TRS(
                    new Vector3(b.EX[i], h * 0.5f, b.EZ[i]),
                    Quaternion.identity,
                    new Vector3(0.52f, h, len));
            }
            if (d > 0) Graphics.RenderMeshInstanced(rpDown, mesh, 0, downs, d);
        }

        public void Clear() { sparkCount = 0; }
    }
}
