using UnityEngine;

namespace Game.Crowd
{
    /// <summary>
    /// **그릴 것의 자리** — 군중 1,000 개체의 좌표·위상을 들고 고정 스텝으로 옮긴다.
    ///
    /// 로직이 아니다. `Scripts/Core/` 가 병력을 **수**로 들고 전투를 **수의 감소**로 계산하는 쪽이고
    /// (세션 A · `DESIGN.md` §2), 여기 있는 좌표는 **화면에 뭔가를 그리려고** 존재한다. 그래서 이
    /// 배열에 HP 도 id 도 없다 — M0 가 떨어져서 화면을 추상화하게 되면 이 파일만 버린다.
    ///
    /// **구조체 배열(AoS)이 아니라 배열의 묶음(SoA)이다.** 1,000 개를 매 프레임 훑으므로 쓰는 것만
    /// 연속으로 읽혀야 한다. 그리고 **할당이 프레임당 0 이어야** 하므로(§3) 배열은 한 번만 잡고
    /// 늘리지 않는다 — `Cap` 이 상한이고 `Count` 가 지금 산 수다.
    /// </summary>
    public sealed class CrowdField
    {
        public readonly int Cap;
        public int Count;

        /// <summary>xz 평면. y 는 안 쓴다 — 지면 위를 달리는 군중이라 높이가 없다</summary>
        public readonly float[] X, Z;
        /// <summary>진행 방향 기준 좌우 오프셋의 목표값 — 대형이 벌어지고 모이는 것</summary>
        public readonly float[] SideTarget;
        /// <summary>걸음 위상 (0~1). 전부 같으면 1,000 명이 한 몸처럼 움직여 군중으로 안 읽힌다</summary>
        public readonly float[] Phase;
        /// <summary>개체마다 조금씩 다른 속도 배수 — 줄이 자연히 흐트러진다</summary>
        public readonly float[] SpeedMul;
        /// <summary>0 = 아군, 1 = 적. 색만 가른다 (전투는 `Core` 의 일)</summary>
        public readonly byte[] Side;

        readonly System.Random rng;

        public CrowdField(int cap, int seed)
        {
            Cap = cap;
            X = new float[cap]; Z = new float[cap];
            SideTarget = new float[cap]; Phase = new float[cap];
            SpeedMul = new float[cap]; Side = new byte[cap];
            rng = new System.Random(seed);
        }

        float Rand(float a, float b) => a + (float)rng.NextDouble() * (b - a);

        /// <summary>
        /// 한 덩어리를 채운다. `side` 는 색, `z0` 는 앞뒤 자리, `width` 는 좌우 폭.
        ///
        /// **증식 순간이 최악값**이므로(§3 p99) 이 함수는 **한 프레임에 수백 개**가 들어오는 것을
        /// 그대로 재현해야 한다 — 미리 다 채워 두면 M0 가 그 봉우리를 영원히 못 본다.
        /// </summary>
        public int Add(int n, byte side, float z0, float width)
        {
            int added = 0;
            for (int i = 0; i < n && Count < Cap; i++, added++)
            {
                int k = Count++;
                X[k] = Rand(-width * 0.5f, width * 0.5f);
                Z[k] = z0 + Rand(-2f, 2f);
                SideTarget[k] = X[k];
                Phase[k] = (float)rng.NextDouble();
                SpeedMul[k] = Rand(0.92f, 1.08f);
                Side[k] = side;
            }
            return added;
        }

        public void Clear() { Count = 0; }

        /// <summary>
        /// 고정 스텝 한 틱. **프레임률과 무관하게 같은 결과**가 나와야 하므로 `dt` 는 호출하는 쪽이
        /// 1/60 으로 고정해 준다 (`DESIGN.md` §2 "선을 지키는 규칙").
        ///
        /// 아군은 `+z`, 적은 `-z` 로 간다. 서로 지나치게 두는 것이 중요하다 — **두 군단이 겹치는
        /// 순간**이 이 장르의 최악 프레임이고, 그 순간을 안 만들면 M0 가 쉬운 쪽만 잰다.
        /// </summary>
        public void Step(float dt, float speed, float lane)
        {
            for (int i = 0; i < Count; i++)
            {
                float dir = Side[i] == 0 ? 1f : -1f;
                Z[i] += speed * SpeedMul[i] * dir * dt;
                // 좌우로 목표 자리까지 부드럽게 — 대형이 숨 쉬는 것처럼 보이게 하는 최소치
                float want = Mathf.Clamp(SideTarget[i], -lane, lane);
                X[i] += (want - X[i]) * Mathf.Min(1f, dt * 3f);
                Phase[i] += dt * SpeedMul[i] * 1.6f;
                if (Phase[i] >= 1f) Phase[i] -= 1f;
            }
        }

        /// <summary>
        /// **유닛당 더미 계산** — M1 의 전투가 들어올 자리를 지금 예산에 포함시킨다 (`M0_CROWD.md` §3).
        ///
        /// 비워 두면 예산을 전부 렌더링에 쓰고, M1 이 붙을 때 다시 떨어진다. 하는 일은 *가장 가까운
        /// 반대편을 찾는 것* — 전투가 실제로 할 일의 모양이고, O(n²) 가 아니라 **격자 없이 O(n)**
        /// 한 번으로 잡아 둔다 (진짜 전투는 `Core` 가 수로 계산하므로 이보다 싸다).
        /// </summary>
        public float NearestOpponentPass(float band)
        {
            float acc = 0f;
            for (int i = 0; i < Count; i++)
            {
                // 같은 띠에 있는 반대편과의 z 거리만 본다 — 전선은 한 줄이라 x 는 덜 중요하다
                float best = band;
                byte me = Side[i];
                for (int j = i + 1; j < Count && j < i + 17; j++)   // 16 명만 본다 — O(n·16)
                {
                    if (Side[j] == me) continue;
                    float d = Z[j] - Z[i]; if (d < 0f) d = -d;
                    if (d < best) best = d;
                }
                acc += best;
            }
            return acc;
        }
    }
}
