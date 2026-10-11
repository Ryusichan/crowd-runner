namespace CrowdRunner.Core
{
    /// <summary>
    /// **총격전.** 적은 개체이고, 아군은 수이고, 그 수가 곧 발사율이다 (`docs/COMBAT_REWORK.md`).
    ///
    /// 이것이 `Core/Combat.cs` 의 전선 소모 모델을 **대체한다.** 그 모델은 양쪽을 전선 폭으로
    /// 깎아서 **아군 수가 결과에 안 들어갔다** — 적 하나당 잃는 아군이 병력과 무관하게 0.427 로
    /// 고정됐다. 즉 *"많으면 덜 죽는다"* 가 아니라 *"같은 수를 잃고 더 오래 버틴다"* 였고,
    /// 그 성질이 이 장르와 정반대다 (오너 2026-10-11).
    ///
    /// **`UnityEngine` 을 쓰지 않는다** — `Core/` 의 규칙이고, 그래야 `tools/sim` 이 에디터 없이
    /// 같은 전투를 돌린다. 오너 완료 조건의 마지막 줄(*"시뮬레이터의 전투 결과와 실제 게임
    /// 결과가 일치한다"*)이 그 성질 위에 선다.
    ///
    /// **개체 배열은 SoA 이고 한 번만 잡는다.** 프레임당 관리 힙 할당 0 B 예산 (`DESIGN.md` §3).
    /// </summary>
    public sealed class Battle
    {
        // ── 상수 (수치의 출처는 COMBAT_REWORK §3 — 오너 제시 임시값) ──────────────
        public const int EnemyCap = 512;
        /// <summary>
        /// 동시에 날아다닐 수 있는 탄환. 240 발/초 × 사거리 18 m ÷ 30 m/s = 144 발이 최악이고,
        /// 유도가 빗나가 수명을 채우는 몫까지 봐서 256 을 잡는다.
        /// </summary>
        public const int BulletCap = 256;

        public const float BulletSpeed = 30f;
        public const float Range = 18f;
        /// <summary>
        /// **화면에 나갈 수 있는 초당 발사 수.** 넘으면 묶는다 — 탄환 하나가 여러 발의 피해를 진다.
        /// 피해 총량은 묶어도 같고, 바뀌는 것은 *몇 개를 그리느냐* 뿐이다 (COMBAT_REWORK §2c).
        ///
        /// 240 인 이유: 10 명(20 발/초)과 300 명(240 발/초)이 **12 배** 차이로 보여야 한다는
        /// 오너 조건 때문이다. 이 값을 낮추면 큰 군단과 작은 군단이 같은 양으로 쏘는 것처럼 보인다.
        /// </summary>
        public const float MaxShotsPerSec = 240f;

        /// <summary>적이 멈춰서 때리기 시작하는 거리</summary>
        public const float Contact = 1.2f;
        /// <summary>탄환이 표적에 닿았다고 보는 거리. 너무 작으면 빠른 탄환이 한 틱에 지나친다</summary>
        public const float HitRadius = 0.9f;
        /// <summary>쓰러지는 연출에 주는 시간. 이 동안 개체는 배열에 남아 있고 표적이 되지 않는다</summary>
        public const float DeathTime = 0.6f;
        /// <summary>전진 감속을 계산할 때 "가까운 적" 으로 세는 거리</summary>
        public const float PressureRange = 8f;

        // ── 적 ────────────────────────────────────────────────────────────────────
        public int EnemyCount;
        public readonly float[] EX = new float[EnemyCap];
        public readonly float[] EZ = new float[EnemyCap];
        public readonly float[] EHp = new float[EnemyCap];
        public readonly float[] EMaxHp = new float[EnemyCap];
        /// <summary>0 달려옴 · 1 때림 · 2 쓰러지는 중</summary>
        public readonly byte[] EState = new byte[EnemyCap];
        /// <summary>쓰러진 뒤 지난 시간. 연출 길이는 표현이 정하고, 여기서는 치우는 시점만 센다</summary>
        public readonly float[] EDieT = new float[EnemyCap];
        readonly float[] eAtkT = new float[EnemyCap];

        // ── 탄환 ──────────────────────────────────────────────────────────────────
        public int BulletCount;
        public readonly float[] BX = new float[BulletCap];
        public readonly float[] BZ = new float[BulletCap];
        /// <summary>진행 방향(단위 벡터). 궤적을 그리려면 자리만으로는 모자란다</summary>
        public readonly float[] BDX = new float[BulletCap];
        public readonly float[] BDZ = new float[BulletCap];
        readonly float[] bDmg = new float[BulletCap];
        readonly float[] bLife = new float[BulletCap];
        /// <summary>표적 번호. −1 이면 표적을 잃은 것(다음 틱에 다시 고른다), −2 는 벽</summary>
        readonly int[] bTarget = new int[BulletCap];

        // ── 벽 (총알이 부순다 — COMBAT_REWORK §2g) ────────────────────────────────
        public bool HasWall;
        public float WallZ, WallHp, WallMaxHp;

        // ── 이번 틱의 사건 (표현이 `Step` 직후에 읽는다) ──────────────────────────
        /// <summary>지금 초당 몇 발 나가는가. 총구 화염의 **양**을 이 수로 정한다</summary>
        public float FireRate;
        public int ShotsThisTick, HitsThisTick;
        /// <summary>
        /// **이번 틱에 맞은 자리.** 표현이 거기에 불꽃을 띄운다. 좌표를 표현이 다시 계산하면
        /// 그것이 오너가 말한 *"총알 효과만 표시하는 가짜 사격"* 이 된다 — 맞은 자리는
        /// 맞춘 쪽만 안다.
        /// </summary>
        public const int HitCap = 64;
        public int HitCount;
        public readonly float[] HitX = new float[HitCap];
        public readonly float[] HitZ = new float[HitCap];
        public int KilledThisTick;
        public float LostThisTick;

        /// <summary>누계 — 판이 끝난 뒤 되짚을 때</summary>
        public float AlliesLost, EnemiesKilled;

        /// <summary>쓰러지는 중이 아닌 적. 전투가 끝났는지를 이 수로 본다</summary>
        public int Alive
        {
            get
            {
                int n = 0;
                for (int i = 0; i < EnemyCount; i++) if (EState[i] != 2) n++;
                return n;
            }
        }

        float shotAccum;
        /// <summary>총구 자리를 좌우로 흩는 counter. 난수를 안 쓰는 이유는 **결정성**이다</summary>
        int muzzle;

        /// <summary>
        /// 적 한 무리를 깐다. **막는 사건이 아니라 웨이브**다 — 군단이 아직 멀리 있을 때 깔아 두면
        /// 적이 **화면에 보이고 나서** 닿는다. 닿는 순간에 생기면 그 전까지는 아무것도 없는 길이다.
        /// </summary>
        /// <param name="band">
        /// **마지막 무리는 덩어리가 아니라 벽이다.** 장르의 끝은 보스와 최후의 일전인데
        /// (`docs/REF_TOPWAR.md` §4) 둥근 덩어리로 두면 *더 큰 덩어리*일 뿐이다. 길을 가로로
        /// 꽉 막는 줄로 세우면 **뚫어야 하는 것**으로 읽힌다 — 같은 수로 뜻을 바꾸므로 비용이 0 이다.
        /// </param>
        public void Spawn(int count, float z, float laneX, float roadWidth, int seed, bool band = false)
        {
            // 결정적인 배치 — `System.Random` 을 쓰되 **씨앗을 받는다**. 시뮬과 게임이 같은
            // 자리를 깔아야 오너 완료 조건의 마지막 줄이 선다
            var rng = new System.Random(seed);
            float half = roadWidth * 0.5f;
            for (int k = 0; k < count && EnemyCount < EnemyCap; k++)
            {
                int i = EnemyCount++;
                // 수가 많을수록 뒤로 깊어진다 — 한 줄로 서면 "밀려온다" 가 아니라 "벽" 이 된다
                if (band)
                {
                    int perRow = (int)(roadWidth / 0.85f); if (perRow < 4) perRow = 4;
                    int col = k % perRow, row = k / perRow;
                    float span = roadWidth - 0.9f;
                    EX[i] = -half + 0.45f + (perRow > 1 ? span * col / (perRow - 1) : span * 0.5f);
                    EZ[i] = z + row * 0.85f;
                }
                else
                {
                    // 수가 많을수록 뒤로 깊어진다 — 한 줄로 서면 "밀려온다" 가 아니라 "벽" 이 된다
                    float depth = 2f + count * 0.12f;
                    EX[i] = laneX + (float)(rng.NextDouble() - 0.5) * roadWidth * 0.8f;
                    if (EX[i] < -half) EX[i] = -half;
                    if (EX[i] > half) EX[i] = half;
                    EZ[i] = z + (float)rng.NextDouble() * depth;
                }
                EHp[i] = EMaxHp[i] = UnitStats.Zombie.hp * 3f;   // 30 — COMBAT_REWORK §3
                EState[i] = 0; EDieT[i] = 0f; eAtkT[i] = 0f;
            }
        }

        public void SetWall(float z, float hp) { HasWall = true; WallZ = z; WallHp = WallMaxHp = hp; }
        public void ClearWall() { HasWall = false; WallHp = 0f; }

        /// <summary>
        /// 한 틱. **전진 속도 배수를 돌려준다** — 압력에 따라 20~100 % (COMBAT_REWORK §2f).
        /// 멈추지 않는 것이 요점이다: 멈추면 화면이 *막아내는 것*이 아니라 **멈춰 있는 것**이 된다.
        /// </summary>
        /// <param name="allies">병력. 줄어든 만큼 여기서 깎는다</param>
        public float Step(float dt, ref float allies, float allyZ, float allyX, UnitStats a, UnitStats e)
        {
            ShotsThisTick = 0; HitsThisTick = 0; KilledThisTick = 0; LostThisTick = 0f; HitCount = 0;

            StepEnemies(dt, ref allies, allyZ, allyX, a, e);
            Fire(dt, allies, allyZ, allyX, a);
            StepBullets(dt);

            // 압력 — 가까이 온 적이 많을수록 느려지고, **병력이 많을수록 덜 느려진다**
            int near = 0;
            for (int i = 0; i < EnemyCount; i++)
                if (EState[i] != 2 && EZ[i] - allyZ < PressureRange) near++;
            float cap = allies * 0.5f + 4f;
            float p = near / cap; if (p > 1f) p = 1f; if (p < 0f) p = 0f;
            return 1f - 0.8f * p;
        }

        void StepEnemies(float dt, ref float allies, float allyZ, float allyX, UnitStats a, UnitStats e)
        {
            for (int i = EnemyCount - 1; i >= 0; i--)
            {
                if (EState[i] == 2)
                {
                    EDieT[i] += dt;
                    if (EDieT[i] >= DeathTime) Remove(i);
                    continue;
                }

                float dz = EZ[i] - allyZ;
                if (dz <= Contact)
                {
                    // 닿았다 — 멈춰서 때린다. **전선 폭 제한이 없다**: 닿은 적은 전부 때린다.
                    // 그래서 둘러싸이면 빨리 녹고, 그것이 "막아내지 못했다" 의 뜻이다
                    EState[i] = 1;
                    eAtkT[i] += dt;
                    if (eAtkT[i] >= e.interval)
                    {
                        eAtkT[i] -= e.interval;
                        float kill = e.damage / a.hp;
                        if (kill > allies) kill = allies;
                        allies -= kill;
                        LostThisTick += kill; AlliesLost += kill;
                    }
                }
                else
                {
                    EState[i] = 0;
                    EZ[i] -= e.speed * dt;
                    // 좌우로도 군단 쪽으로 붙는다 — 안 그러면 길 가장자리의 적이 영원히 안 닿는다
                    float dx = allyX - EX[i];
                    float step = e.speed * 0.4f * dt;
                    if (dx > step) dx = step; else if (dx < -step) dx = -step;
                    EX[i] += dx;
                }
            }
        }

        /// <summary>
        /// **발사.** 발사율은 병력에 비례하고 전선 폭에 안 막힌다 — 뒤에 선 병사도 앞으로 쏜다.
        /// 그것이 §1 에서 폐기한 성질을 뒤집는 자리다 (병력 두 배 = 화력 두 배).
        /// </summary>
        void Fire(float dt, float allies, float allyZ, float allyX, UnitStats a)
        {
            FireRate = 0f;
            if (allies <= 0f) { shotAccum = 0f; return; }
            if (!AnyTarget(allyZ)) { shotAccum = 0f; return; }

            FireRate = allies / a.interval;

            // 묶음 — 그릴 수 있는 수를 넘는 몫은 **탄환 하나의 피해**로 들어간다
            float pack = FireRate / MaxShotsPerSec;
            if (pack < 1f) pack = 1f;
            int packN = (int)pack; if (packN < pack) packN++;     // ceil, 할당 없이

            float visual = FireRate / packN;
            shotAccum += visual * dt;
            while (shotAccum >= 1f)
            {
                shotAccum -= 1f;
                if (!SpawnBullet(allyZ, allyX, a.damage * packN)) { shotAccum = 0f; break; }
                ShotsThisTick++;
            }
        }

        bool AnyTarget(float allyZ)
        {
            for (int i = 0; i < EnemyCount; i++)
                if (EState[i] != 2 && EZ[i] > allyZ - Contact && EZ[i] - allyZ <= Range) return true;
            return HasWall && WallHp > 0f && WallZ - allyZ <= Range && WallZ > allyZ - Contact;
        }

        bool SpawnBullet(float allyZ, float allyX, float dmg)
        {
            if (BulletCount >= BulletCap) return false;
            int t = PickTarget(allyZ, allyX);
            if (t == -1) return false;

            int b = BulletCount++;
            // 총구는 군단 앞줄의 **여러 자리**에서 나온다 — 한 점에서 나오면 분수처럼 보인다
            muzzle = (muzzle + 7) & 15;
            BX[b] = allyX + (muzzle - 7.5f) * 0.22f;
            BZ[b] = allyZ + 1.2f;
            bDmg[b] = dmg; bLife[b] = 0f; bTarget[b] = t;
            Aim(b);
            return true;
        }

        /// <summary>사거리 안에서 **가장 가까운** 산 적. 없으면 벽(−2), 둘 다 없으면 −1</summary>
        int PickTarget(float allyZ, float allyX)
        {
            int best = -1; float bestD = float.MaxValue;
            for (int i = 0; i < EnemyCount; i++)
            {
                if (EState[i] == 2) continue;
                float dz = EZ[i] - allyZ;
                if (dz < -Contact || dz > Range) continue;
                float dx = EX[i] - allyX;
                float d = dz * dz + dx * dx;
                if (d < bestD) { bestD = d; best = i; }
            }
            if (best >= 0) return best;
            if (HasWall && WallHp > 0f && WallZ - allyZ <= Range && WallZ > allyZ - Contact) return -2;
            return -1;
        }

        void Aim(int b)
        {
            float tx, tz;
            if (bTarget[b] == -2) { tx = BX[b]; tz = WallZ; }
            else { tx = EX[bTarget[b]]; tz = EZ[bTarget[b]]; }
            float dx = tx - BX[b], dz = tz - BZ[b];
            float len = (float)System.Math.Sqrt(dx * dx + dz * dz);
            if (len < 1e-4f) { BDX[b] = 0f; BDZ[b] = 1f; return; }
            BDX[b] = dx / len; BDZ[b] = dz / len;
        }

        void StepBullets(float dt)
        {
            for (int b = BulletCount - 1; b >= 0; b--)
            {
                int t = bTarget[b];
                // 표적이 죽었으면 **가장 가까운 산 적으로 옮겨 탄다** — 쏜 피해를 버리지 않는다
                if (t >= 0 && (t >= EnemyCount || EState[t] == 2)) t = bTarget[b] = Retarget(b);
                if (t == -1) { RemoveBullet(b); continue; }
                if (t == -2 && (!HasWall || WallHp <= 0f)) { RemoveBullet(b); continue; }

                Aim(b);
                BX[b] += BDX[b] * BulletSpeed * dt;
                BZ[b] += BDZ[b] * BulletSpeed * dt;
                bLife[b] += dt;

                float tx = t == -2 ? BX[b] : EX[t];
                float tz = t == -2 ? WallZ : EZ[t];
                float dx = tx - BX[b], dz = tz - BZ[b];
                if (dx * dx + dz * dz <= HitRadius * HitRadius)
                {
                    HitsThisTick++;
                    if (HitCount < HitCap) { HitX[HitCount] = BX[b]; HitZ[HitCount] = BZ[b]; HitCount++; }
                    if (t == -2)
                    {
                        WallHp -= bDmg[b];
                        if (WallHp < 0f) WallHp = 0f;
                    }
                    else
                    {
                        EHp[t] -= bDmg[b];
                        if (EHp[t] <= 0f)
                        {
                            EHp[t] = 0f; EState[t] = 2; EDieT[t] = 0f;
                            KilledThisTick++; EnemiesKilled += 1f;
                        }
                    }
                    RemoveBullet(b);
                    continue;
                }

                // 수명 — 사거리의 두 배를 날면 버린다. 유도라도 못 따라잡는 경우가 있다
                if (bLife[b] > Range * 2f / BulletSpeed) RemoveBullet(b);
            }
        }

        int Retarget(int b)
        {
            int best = -1; float bestD = float.MaxValue;
            for (int i = 0; i < EnemyCount; i++)
            {
                if (EState[i] == 2) continue;
                float dx = EX[i] - BX[b], dz = EZ[i] - BZ[b];
                float d = dx * dx + dz * dz;
                if (d < bestD) { bestD = d; best = i; }
            }
            if (best >= 0) return best;
            return HasWall && WallHp > 0f ? -2 : -1;
        }

        /// <summary>
        /// 빈자리를 **맨 뒤로 메운다**(swap-remove). 그런데 그러면 맨 뒤를 **가리키던 탄환**이
        /// 엉뚱한 적을 때린다 — 옮긴 번호를 따라가게 고쳐 준다. 조용히 틀리는 종류라 적어 둔다.
        /// </summary>
        void Remove(int i)
        {
            int last = --EnemyCount;
            if (i != last)
            {
                EX[i] = EX[last]; EZ[i] = EZ[last]; EHp[i] = EHp[last]; EMaxHp[i] = EMaxHp[last];
                EState[i] = EState[last]; EDieT[i] = EDieT[last]; eAtkT[i] = eAtkT[last];
            }
            for (int b = 0; b < BulletCount; b++)
            {
                if (bTarget[b] == last) bTarget[b] = i;
                else if (bTarget[b] == i) bTarget[b] = -1;   // 다음 틱에 다시 고른다
            }
        }

        void RemoveBullet(int b)
        {
            int last = --BulletCount;
            if (b == last) return;
            BX[b] = BX[last]; BZ[b] = BZ[last]; BDX[b] = BDX[last]; BDZ[b] = BDZ[last];
            bDmg[b] = bDmg[last]; bLife[b] = bLife[last]; bTarget[b] = bTarget[last];
        }

        public void Clear()
        {
            EnemyCount = 0; BulletCount = 0; shotAccum = 0f; HitCount = 0;
            HasWall = false; WallHp = 0f;
            FireRate = 0f; ShotsThisTick = HitsThisTick = KilledThisTick = 0; LostThisTick = 0f;
            AlliesLost = 0f; EnemiesKilled = 0f;
        }
    }
}
