using CrowdRunner.Core;
using UnityEngine;
using UnityEngine.Rendering;

namespace CrowdRunner.View
{
    /// <summary>
    /// **쓰러지는 것을 보여 준다** — 손실이 **머릿수에 비례한다**는 사실이 화면에 있어야 한다.
    ///
    /// 세션 A 측정: 지역(zone)은 머릿수에 비례해 깎는다. 그래서 **큰 군단이 지나갈 때 많이 녹아야**
    /// 플레이어가 대가를 느낀다 — 비율인데 *"조금씩 꾸준히"* 로 그리면 **많이 가진 것에 대가가
    /// 없는 것처럼 보이고**, 그러면 큰 숫자가 늘 정답이라는 결론으로 되돌아간다.
    ///
    /// 그리고 **두 손실을 다르게 그린다** (세션 A 가 `lostToCombat`/`lostToZone` 을 나눠 준 이유):
    /// 전투사는 **앞줄에서** 쓰러지고, 지역사는 **군단 전체에 흩어져** 녹는다. 한 수로 받으면
    /// 그 구분이 표현 쪽에서 사라진다.
    ///
    /// 예산: **드로우 한 번** · 프레임당 할당 0 B. 몸은 군중과 같은 메시·머티리얼을 쓰고
    /// (같은 머티리얼이라 상태 변경도 없다) 배열은 한 번만 잡는다.
    /// </summary>
    public sealed class CasualtyFx
    {
        /// <summary>한 번에 보여 줄 상한. 넘치는 손실은 **수가 아니라 밀도로** 읽힌다</summary>
        const int Cap = 300;
        /// <summary>쓰러지는 데 걸리는 시간 (초). 짧으면 안 보이고 길면 시체가 쌓인 것처럼 보인다</summary>
        const float Life = 0.75f;

        struct Body { public float x, z, t, spin; public bool front; }

        readonly Body[] bodies = new Body[Cap];
        readonly Matrix4x4[] mats = new Matrix4x4[Cap];
        readonly float[] phase = new float[Cap];
        readonly float[] side = new float[Cap];
        int live;

        Mesh mesh;
        Material mat;
        RenderParams rp;
        System.Random rng = new System.Random(911);

        public int Live => live;
        /// <summary>
        /// 지금까지 띄운 몸의 수 — **주행이 묻는다.** 화면은 주행이 볼 수 없으므로, *손실만큼
        /// 띄웠는가* 를 이 수로 본다. 손실이 74 인데 몸이 3 개면 비례가 깨진 것이고, 그건
        /// *"많이 가진 것에 대가가 없다"* 로 보인다 — 이 효과가 막으려던 바로 그것이다.
        /// </summary>
        public int SpawnedCombat { get; private set; }
        public int SpawnedZone { get; private set; }

        public void Init()
        {
            mesh = Resources.Load<Mesh>("M0/standin_vat_mesh_lo");
            mat = Resources.Load<Material>("M0/standin_vat_lo");
            if (mesh == null || mat == null)
            {
                Debug.LogError("[CR] 쓰러지는 몸의 자산이 없다 — 손실이 화면에 안 보인다 (VatBaker.Bake(\"_lo\"))");
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
        /// 이 틱의 손실을 받는다. **`sim.Last` 는 한 틱만 유효**하므로 `LevelRunner.Consume` 이
        /// 틱마다 넘겨주는 그 자리에서 불러야 한다 — 프레임 끝에 한 번만 읽으면 그 프레임의
        /// 앞 틱 손실이 사라지고, 그러면 **빠른 기기에서 손실이 덜 보인다**.
        /// </summary>
        public void Take(in Sim.Tick t, float cx, float cz, float width, float frontZ)
        {
            Spawn(t.lostToCombat, cx, frontZ, width, true);
            Spawn(t.lostToZone, cx, cz, width, false);
        }

        void Spawn(float n, float cx, float cz, float width, bool front)
        {
            // **소수점을 버리지 않는다.** 지역 손실은 틱마다 0.3 명 같은 수로 오는데, 버리면
            // 작은 군단에서 **아무도 안 녹는 것처럼** 보인다. 쌓아서 1 이 될 때 한 명을 띄운다
            if (front) debtCombat += n; else debtZone += n;
            ref float debt = ref front ? ref debtCombat : ref debtZone;
            while (debt >= 1f && live < Cap)
            {
                debt -= 1f;
                int i = live++;
                // 전투사는 **앞줄에** 좁게, 지역사는 **군단 전체에** 흩어진다
                float spread = front ? width * 0.45f : width * 0.5f;
                float depth = front ? 1.2f : 5.0f;
                bodies[i] = new Body
                {
                    x = cx + (float)(rng.NextDouble() - 0.5) * spread * 2f,
                    z = cz + (float)(rng.NextDouble() - 0.5) * depth * 2f,
                    t = 0f,
                    spin = (float)(rng.NextDouble() - 0.5) * 220f,
                    front = front,
                };
                if (front) SpawnedCombat++; else SpawnedZone++;
                phase[i] = (float)rng.NextDouble();
                side[i] = front ? 0f : 1f;    // 색으로도 갈라 둔다 — 두 손실이 다른 일이라서
            }
            if (debt > 40f) debt = 40f;   // 밀린 빚이 쌓여 영원히 토해내지 않게
        }

        float debtCombat, debtZone;

        public void Tick(float dt)
        {
            for (int i = live - 1; i >= 0; i--)
            {
                bodies[i].t += dt;
                if (bodies[i].t >= Life) { bodies[i] = bodies[live - 1]; phase[i] = phase[live - 1]; side[i] = side[live - 1]; live--; }
            }
        }

        public void Draw()
        {
            if (mesh == null || live == 0) return;
            for (int i = 0; i < live; i++)
            {
                float k = bodies[i].t / Life;            // 0 → 1
                // **가라앉으며 작아진다.** 터뜨리지 않는 이유: 1,000 명이 터지면 화면이 덮이고,
                // 그러면 *얼마나 녹았나* 가 안 읽힌다. 가라앉는 것은 수가 많아도 읽힌다
                float y = -1.4f * k * k;
                float s = Mathf.Max(0.05f, 1f - k);
                mats[i] = Matrix4x4.TRS(
                    new Vector3(bodies[i].x, y, bodies[i].z),
                    Quaternion.Euler(k * 78f, bodies[i].spin * k, 0f),
                    new Vector3(s, s, s));
            }
            Graphics.RenderMeshInstanced(rp, mesh, 0, mats, live);
        }
    }
}
