namespace CrowdFire.Core
{
    /// <summary>
    /// **총격전 — 병사 한 명이 한 명씩 쏜다.**
    ///
    /// 처음 판(2026-10-11 오전)은 `발사율 = 병력 ÷ 간격` 으로 **군단 전체를 한 번에** 계산하고
    /// 탄환을 240 발/초로 **묶어서** 내보냈다. 오너가 그 자리를 정확히 짚었다:
    /// *"300 명의 아군이 실제로 모두 사격 판정을 수행하는지, 아니면 군단 전체의 공격력을 한 번에
    /// 계산하면서 일부 사격 연출만 보여주는지. 이 차이를 확인하지 않고 적 HP 만 올리면 계속
    /// 잘못된 방향으로 개발하게 돼."*
    ///
    /// 그래서 **병사가 개체가 됐다.** 병사마다 자리와 발사 타이머가 있고 자기 차례에 자기가 쏜다.
    /// 300 명이면 **초당 600 번의 판정**이 각각 일어난다.
    ///
    /// **명중 판정은 즉시(히트스캔)이고 탄환은 그중 일부만 그린다.** 오너가 허락한 분리다
    /// (*"실제 발사·피해 판정과 시각적 탄환 수는 성능을 위해 분리해도 된다"*). 거꾸로 하면 —
    /// 피해를 한 번에 계산하고 탄환을 장식으로 뿌리면 — 그것이 가짜 사격이고, 그게 방금 지운 것이다.
    ///
    /// **적은 묶지 않는다.** 개체 하나가 여럿의 HP 를 지면 화면에서 쓰러지는 수가 줄어
    /// *무수히 많은 적을 막아낸다* 와 정면으로 부딪친다 (오너 2026-10-11).
    /// </summary>
    public sealed class Battle
    {
        // ── 상한 — **논리와 렌더링을 나눠서 보고한다** (오너 지시 3) ──────────────
        /// <summary>논리 병사 수의 상한. 이 수까지는 **전원이 각자 쏜다**</summary>
        public const int AllyCap = 1200;
        public const int EnemyCap = 900;
        /// <summary>**그리는** 탄환의 상한. 발사 판정은 이것과 무관하게 전원이 한다</summary>
        public const int BulletCap = 320;
        public const float TracerPerSec = 260f;

        public const float BulletSpeed = 34f;
        public const float Range = 18f;
        public const float Contact = 1.2f;
        public const float DeathTime = 0.6f;
        public const float PressureRange = 9f;
        /// <summary>압력이 최대일 때 깎는 전진 속도 비율 — 바닥은 1 − 이 값이다</summary>
        public const float MaxSlow = 0.8f;
        public const float FlashTime = 0.055f;
        /// <summary>
        /// 적이 **한 번에 쏟아지지 않고 흘러드는 속도**(초당). 오너 지시 2: *"한 무리를 처리하고
        /// 다음 무리가 나오는 방식만으로 끝내지 말고, 전방에서 적군이 지속적으로 접근하며 전선을
        /// 형성해야 한다."* 한 무리를 통째로 깔면 큰 군단은 1 초 안에 지우고 다시 빈 복도를 달린다.
        /// </summary>
        public const float StreamPerSec = 34f;
        /// <summary>
        /// 한 무리를 **다 내보내는 데 걸리는 시간**(초). 속도를 상수 하나로 두었더니 큰 군단의
        /// 화력이 갈 곳이 없었다 — 300 명은 초당 125 명을 지울 수 있는데 34 명만 오니 남는
        /// 화력이 **아무 일도 안 한다**. 무리가 클수록 빨리 쏟아져야 *수가 화력*이 값을 한다.
        /// </summary>
        public const float ReleaseSeconds = 4f;
        /// <summary>흘러드는 적이 생기는 거리 (군단 앞)</summary>
        public const float StreamAhead = 38f;

        // ── 아군 (개체다) ─────────────────────────────────────────────────────────
        public int AllyCount;
        public readonly float[] AX = new float[AllyCap];
        public readonly float[] AZ = new float[AllyCap];
        /// <summary>남은 총구 화염 시간. 표현이 **쏜 병사 자리에** 불을 띄운다</summary>
        public readonly float[] AFlash = new float[AllyCap];
        /// <summary>
        /// 병사가 **마지막으로 겨눈 자리** — 화면이 병사를 그쪽으로 돌린다 (2026-10-20 반응 보충).
        /// 쏘는 판정에는 안 쓰인다. 기록일 뿐이라 시뮬 결과는 한 숫자도 안 바뀐다
        /// </summary>
        public readonly float[] AAimX = new float[AllyCap], AAimZ = new float[AllyCap];

        /// <summary>적이 쓰러진 자리 — 링 버퍼. 화면이 `DeathSerial` 로 새것만 읽어 시체를 눕힌다</summary>
        public const int DeathRing = 512;
        public readonly float[] DeadX = new float[DeathRing], DeadZ = new float[DeathRing];
        public int DeathSerial;
        /// <summary>마지막으로 깨진 통 — 자리 · 나온 병사 · 일련번호 (화면이 병사를 거기서 쏟는다)</summary>
        public float LastBrokeX, LastBrokeZ; public int LastBrokeGives, BrokeSerial;
        readonly float[] aFireT = new float[AllyCap];

        // ── 적 (개체다 — 묶지 않는다) ─────────────────────────────────────────────
        public int EnemyCount;
        public readonly float[] EX = new float[EnemyCap];
        public readonly float[] EZ = new float[EnemyCap];
        public readonly float[] EHp = new float[EnemyCap];
        public readonly float[] EMaxHp = new float[EnemyCap];
        /// <summary>0 달려옴 · 1 때림 · 2 쓰러지는 중</summary>
        public readonly byte[] EState = new byte[EnemyCap];
        public readonly float[] EDieT = new float[EnemyCap];
        /// <summary>맞은 뒤 남은 **피격 반응** 시간 — 표현이 움찔하게 그린다</summary>
        public readonly float[] EHurt = new float[EnemyCap];
        readonly float[] eAtkT = new float[EnemyCap];

        // ── 충돌 기록 (2026-10-20) ─────────────────────────────────────────────
        // 오너: *"인원충돌로 우리쪽 인원 줄어드는 효과등도 좀더 디테일하게."* 손실은 수치로만 깎여
        // **어디서 누가 죽었는지** 가 없었다. 화면이 그 자리를 그리도록 남긴다 — 규칙은 그대로다
        /// <summary>공격 박자 0..1 (1 이 무는 순간). 접촉 중이 아니면 0</summary>
        public readonly float[] EAttack = new float[EnemyCap];
        public const int HitRing = 256;
        /// <summary>좀비가 문 자리 (한 번 물 때마다 하나)</summary>
        public readonly float[] AllyHitX = new float[HitRing], AllyHitZ = new float[HitRing];
        public int AllyHitSerial;
        /// <summary>병사가 **한 명 줄어든** 자리 — 그 순간 문 좀비의 위치 (정수 경계를 넘을 때마다 하나)</summary>
        public readonly float[] AllyDeathX = new float[HitRing], AllyDeathZ = new float[HitRing];
        public int AllyDeathSerial;
        /// <summary>대형 반지름 (가로 · 세로) — 화면이 좀비를 대열 **앞 가장자리**에 세우는 데 쓴다</summary>
        public float FormRx, FormRz;
        /// <summary>소대 옆을 지나쳐 흘려보낸 적 수</summary>
        public int Passed;

        // ── 탄환 (보이는 것만 — 피해는 이미 들어갔다) ─────────────────────────────
        public int BulletCount;
        public readonly float[] BX = new float[BulletCap];
        public readonly float[] BZ = new float[BulletCap];
        public readonly float[] BDX = new float[BulletCap];
        public readonly float[] BDZ = new float[BulletCap];
        readonly float[] bLife = new float[BulletCap];
        readonly float[] bRange = new float[BulletCap];

        // ── 아직 안 나온 적 ───────────────────────────────────────────────────────
        struct Pending { public int left; public float laneX, roadWidth, z, rate; public int seed; public bool band; }
        readonly Pending[] pending = new Pending[8];
        int pendingCount;
        float streamAccum;

        public bool HasWall;
        public float WallZ, WallHp, WallMaxHp;

        // ── 드럼통 ───────────────────────────────────────────────────────────────
        /// <summary>
        /// **숫자는 걸어서 지나가는 문이 아니라 쏴서 깨는 통이다** (오너 2026-10-12, 레퍼런스 3 장).
        ///
        /// 통은 차선에 서 있고 **그 앞에 선 병사만** 통을 쏜다. 그동안 그 병사들은 적을 안 막는다 —
        /// 키우는 일과 버티는 일이 **같은 화력을 놓고 다툰다.** 산수 게이트에는 이 다툼이 없었다
        /// (걸어서 지나가면 공짜였다). 벽과 달리 여럿이 동시에 서고, 지나쳐 버리면 그걸로 끝이다.
        /// </summary>
        public const int BarrelCap = 8;
        public int BarrelCount;
        public readonly float[] RX = new float[BarrelCap];
        public readonly float[] RZ = new float[BarrelCap];
        public readonly float[] RHalf = new float[BarrelCap];
        public readonly float[] RHp = new float[BarrelCap];
        public readonly float[] RMaxHp = new float[BarrelCap];
        public readonly int[] RGives = new int[BarrelCap];
        /// <summary>이번 틱에 깨진 통이 내놓은 병사 — `Sim` 이 군단에 더한다</summary>
        public int BrokenThisTick;
        /// <summary>이번 틱에 깨진 통 수 (연출·로그용)</summary>
        public int BrokeCountThisTick;
        /// <summary>지나쳐 버린 통 수 — 못 먹은 것이 몇 개인지</summary>
        public int MissedBarrels;

        // ── 이번 틱의 사건 ────────────────────────────────────────────────────────
        /// <summary>이번 틱에 **실제로 일어난 발사 판정** 수. 연출 수가 아니다</summary>
        public int ShotsThisTick;
        public int HitsThisTick, KilledThisTick;
        /// <summary>이번 틱에 **총을 쏜 병사** 수 — 오너 지시 5 의 측정 항목</summary>
        public int FiringThisTick;
        public float LostThisTick;
        public float AlliesLost, EnemiesKilled;

        public const int HitCap = 96;
        public int HitCount;
        public readonly float[] HitX = new float[HitCap];
        public readonly float[] HitZ = new float[HitCap];

        public int Alive
        {
            get { int n = 0; for (int i = 0; i < EnemyCount; i++) if (EState[i] != 2) n++; return n; }
        }

        /// <summary>아직 안 나온 적까지 센다 — 전투가 끝났는지는 이것으로 본다</summary>
        public int Remaining
        {
            get { int n = Alive; for (int i = 0; i < pendingCount; i++) n += pending[i].left; return n; }
        }

        /// <summary>사거리 안의 적 후보 — 틱마다 한 번 모아서 **모든 병사가 나눠 쓴다**</summary>
        const int CandCap = 160;
        readonly int[] cand = new int[CandCap];
        int candCount;
        int frame;
        float tracerAccum;

        /// <summary>
        /// **대형을 로직이 들고 있다.** 전에는 표현이 따로 깔았는데, 그러면 총구 화염이
        /// *그려진 병사*가 아니라 아무 데서나 터진다. 오너 지시 1(*"300 명이면 300 명이 총을
        /// 쏘는 모습이 보여야 한다"*)은 로직과 표현이 **같은 자리**를 써야 성립한다.
        ///
        /// 황금각(137.5°) 해바라기 배치 — 같은 수를 가장 고르게 둥글게 채운다.
        /// </summary>
        public void SetArmy(float allies, float cx, float cz, float roadWidth, float dt)
        {
            int n = (int)(allies + 0.999f);
            if (n > AllyCap) n = AllyCap;
            if (n < 0) n = 0;

            // 2026-10-18: 0.19×0.78² (피치 0.34 m) 는 진짜 모델(어깨 0.5 m)에서 **헬멧이 겹쳐 파란 돔**이
            // 됐다 (`docs/shots`). 라스트워는 병사 하나하나가 읽힌다 — 피치 0.55 m. 109 명이면 반지름 3.2 m 로
            // 9 m 길에 들어간다
            // 1.25² (피치 0.55) 도 헬멧이 맞닿았다 — 이 모델은 머리가 크다. 피치 0.7 (1.6²)
            const float PerPerson = 0.19f * 1.6f * 1.6f;
            float r = (float)System.Math.Sqrt(n * PerPerson / 3.14159265f);
            FormRx = r * 0.78f; FormRz = r * 1.55f;
            float lim = roadWidth * 0.5f - 0.3f;
            const float Golden = 2.39996323f;

            for (int i = 0; i < n; i++)
            {
                float rad = n > 1 ? r * (float)System.Math.Sqrt((i + 0.5f) / n) : 0f;
                float a = i * Golden;
                // **길을 따라 길쭉한 타원.** 원반이면 300 명이 길 폭(9 m)에 눌려 헬멧이 맞닿은 돔이
                // 된다 (z234 사진). 라스트워 소대는 앞뒤로 긴 대열이다 — 폭 0.78 배, 길이 1.55 배
                const float ElongX = 0.78f, ElongZ = 1.55f;
                float x = cx + (float)System.Math.Cos(a) * rad * ElongX;
                if (x < -lim) x = -lim;
                if (x > lim) x = lim;
                AX[i] = x;
                AZ[i] = cz + (float)System.Math.Sin(a) * rad * ElongZ;
                // **새로 들어온 병사는 발사 시점을 흩어 둔다.** 전부 0 이면 600 발이 한 프레임에
                // 몰려 나가고 그다음 반 초는 아무도 안 쏜다 — 화면이 깜빡인다
                if (i >= AllyCount) { aFireT[i] = (i % 31) / 31f * UnitStats.Ally.interval; AFlash[i] = 0f; }
                if (AFlash[i] > 0f) AFlash[i] -= dt;
            }
            AllyCount = n;
        }

        /// <summary>
        /// 적 한 뭉치를 **예약한다** — 그 자리에 통째로 깔지 않는다. `StreamPerSec` 로 흘러나와
        /// 군단 앞 `StreamAhead` 에서 계속 생기고, 그래서 **전선이 생긴다** (오너 지시 2).
        /// </summary>
        public void Queue(int count, float z, float laneX, float roadWidth, int seed, bool band = false)
        {
            if (pendingCount >= pending.Length) { pending[pendingCount - 1].left += count; return; }
            float rate = count / ReleaseSeconds;
            if (rate < StreamPerSec) rate = StreamPerSec;
            pending[pendingCount++] = new Pending
            { left = count, laneX = laneX, roadWidth = roadWidth, z = z, rate = rate, seed = seed, band = band };
        }

        /// <summary>통 하나를 세운다. `gives` 가 깨졌을 때 나오는 병사 수(통에 적힌 숫자)다</summary>
        public void AddBarrel(float z, float x, float half, float hp, int gives)
        {
            if (BarrelCount >= BarrelCap) return;
            int b = BarrelCount++;
            RZ[b] = z; RX[b] = x; RHalf[b] = half; RHp[b] = RMaxHp[b] = hp; RGives[b] = gives;
        }

        /// <summary>그 병사가 지금 쏠 통 — **앞에 있고 사거리 안이고 정면이어야 한다**</summary>
        int BarrelFor(float x, float z)
        {
            for (int b = 0; b < BarrelCount; b++)
            {
                if (RHp[b] <= 0f) continue;
                float dz = RZ[b] - z;
                if (dz <= 0f || dz > Range) continue;
                if (x < RX[b] - RHalf[b] || x > RX[b] + RHalf[b]) continue;
                return b;
            }
            return -1;
        }

        /// <summary>군단 뒤로 넘어간 통은 **놓친 것**이다 — 되돌아갈 수 없다</summary>
        void PruneBarrels(float allyZ)
        {
            for (int b = BarrelCount - 1; b >= 0; b--)
            {
                if (RZ[b] >= allyZ - 2f) continue;
                if (RHp[b] > 0f) MissedBarrels++;
                int last = --BarrelCount;
                if (b != last)
                {
                    RX[b] = RX[last]; RZ[b] = RZ[last]; RHalf[b] = RHalf[last];
                    RHp[b] = RHp[last]; RMaxHp[b] = RMaxHp[last]; RGives[b] = RGives[last];
                }
            }
        }

        public void SetWall(float z, float hp) { HasWall = true; WallZ = z; WallHp = WallMaxHp = hp; }
        public void ClearWall() { HasWall = false; WallHp = 0f; }

        /// <summary>한 틱. 전진 속도 배수(20~100 %)를 돌려준다</summary>
        public float Step(float dt, ref float allies, float allyZ, float allyX, UnitStats a, UnitStats e)
        {
            ShotsThisTick = 0; HitsThisTick = 0; KilledThisTick = 0; LostThisTick = 0f;
            HitCount = 0; FiringThisTick = 0;
            BrokenThisTick = 0; BrokeCountThisTick = 0;
            frame++;

            PruneBarrels(allyZ);
            Stream(dt, allyZ);
            StepEnemies(dt, ref allies, allyZ, allyX, a, e);
            Candidates(allyZ);
            FirePerSoldier(dt, a);
            StepBullets(dt);

            int near = 0;
            for (int i = 0; i < EnemyCount; i++)
            {
                // **소대와 겹친 적만** 발을 묶는다 — 옆을 지나가는 적까지 세면 1-1 이 99 초로 늘어졌다
                if (EState[i] == 2 || EZ[i] - allyZ >= PressureRange) continue;
                float pu = (EX[i] - allyX) / (FormRx > 0.6f ? FormRx : 0.6f);
                if (pu > -1.15f && pu < 1.15f) near++;
            }
            float cap = allies * 0.5f + 4f;
            float p = near / cap; if (p > 1f) p = 1f; if (p < 0f) p = 0f;
            // ⚠ **이 바닥을 올려도 판은 안 짧아진다.** 45 % 로 올려 봤더니 1-1 이 60 초에서
            // **71 초로 늘었다** — 이 판의 길이를 정하는 것은 걷는 시간이 아니라 **적을 다
            // 지우는 시간**이고(2,200 명 ÷ 초당 40 명), 빨리 가면 최종 방어선 앞에서 그만큼 더
            // 서 있을 뿐이다. 게다가 다른 판 2 개가 깨졌다. 멈춰 보이는 문제(오너 지시 6)는
            // 여기가 아니라 **적 수와 흘려보내는 속도**에서 풀어야 한다.
            return 1f - MaxSlow * p;
        }

        /// <summary>예약된 적을 **조금씩** 내보낸다 — 전방에서 끊임없이 다가오게</summary>
        void Stream(float dt, float allyZ)
        {
            if (pendingCount == 0) return;
            streamAccum += pending[0].rate * dt;
            while (streamAccum >= 1f && pendingCount > 0 && EnemyCount < EnemyCap)
            {
                streamAccum -= 1f;
                int left = pending[0].left;
                float laneX = pending[0].laneX, roadWidth = pending[0].roadWidth, pz = pending[0].z;
                bool band = pending[0].band;
                var rng = new System.Random(pending[0].seed + left);

                int i = EnemyCount++;
                float half = roadWidth * 0.5f;
                if (band)
                {
                    // 마지막 무리는 **길을 가로로 막는 줄**이다 — 뚫어야 하는 것으로 읽힌다
                    int perRow = (int)(roadWidth / 0.85f); if (perRow < 4) perRow = 4;
                    float z0 = allyZ + StreamAhead * 0.8f; if (z0 < pz) z0 = pz;
                    EX[i] = -half + 0.45f + (roadWidth - 0.9f) * (left % perRow) / (perRow - 1);
                    EZ[i] = z0 + (left / perRow) * 0.85f;
                }
                else
                {
                    EX[i] = laneX + (float)(rng.NextDouble() - 0.5) * roadWidth * 0.85f;
                    if (EX[i] < -half) EX[i] = -half;
                    if (EX[i] > half) EX[i] = half;
                    // **군단 앞에서 생긴다.** 사건의 z 에 고정하면 군단이 지나친 뒤에 나와
                    // 뒤에서 쫓아오는 꼴이 된다
                    float z0 = allyZ + StreamAhead; if (z0 < pz) z0 = pz;
                    EZ[i] = z0 + (float)rng.NextDouble() * 6f;
                }
                EHp[i] = EMaxHp[i] = UnitStats.Zombie.hp * 3f;
                EState[i] = 0; EDieT[i] = 0f; eAtkT[i] = 0f; EHurt[i] = 0f; EAttack[i] = 0f;

                pending[0].left = left - 1;
                if (pending[0].left <= 0)
                {
                    for (int k = 1; k < pendingCount; k++) pending[k - 1] = pending[k];
                    pendingCount--;
                }
            }
        }

        void StepEnemies(float dt, ref float allies, float allyZ, float allyX, UnitStats a, UnitStats e)
        {
            for (int i = EnemyCount - 1; i >= 0; i--)
            {
                if (EHurt[i] > 0f) EHurt[i] -= dt;
                if (EState[i] == 2)
                {
                    EDieT[i] += dt;
                    if (EDieT[i] >= DeathTime) Remove(i);
                    continue;
                }

                float dz = EZ[i] - allyZ;
                // **대열 가장자리에서 문다** (2026-10-20). 전에는 소대 *중심* z 기준이라 좀비가 대열 한가운데까지
                // 들어와 섰고, 총알이 앞으로만 나가게 되자 앞줄이 등 뒤의 좀비를 못 쏴 소대가 안에서 녹았다.
                // 타원 대형의 그 x 에서의 가장자리까지가 몸이다 — 그 밖 `Contact` 안에 들면 문다
                float u = (EX[i] - allyX) / (FormRx > 0.6f ? FormRx : 0.6f);
                float edge = u > -1f && u < 1f ? FormRz * (float)System.Math.Sqrt(1f - u * u) : 0f;
                float gap = (dz >= 0f ? dz : -dz) - edge;
                // **좌우로 겹친 적만 부딪힌다.** 옆을 지나가는 적은 소대를 못 문다 — 라스트워처럼
                // 좌우 이동이 곧 *어느 적과 싸울지* 를 고르는 일이다 (2026-10-20 곧은 총알과 한 짝)
                bool overlaps = u > -1.15f && u < 1.15f;
                if (overlaps && gap <= Contact)
                {
                    // 가장자리 안으로는 못 들어온다 — 소대가 전진하면 **밀린다** (전선이 움직인다)
                    if (gap < 0.3f) EZ[i] = allyZ + (dz >= 0f ? edge + 0.3f : -(edge + 0.3f));
                    // 닿은 적은 **전부** 때린다 — 전선 폭 제한이 없다. 둘러싸이면 빨리 녹는다
                    EState[i] = 1;
                    eAtkT[i] += dt;
                    EAttack[i] = e.interval > 0f ? eAtkT[i] / e.interval : 0f;
                    if (eAtkT[i] >= e.interval)
                    {
                        eAtkT[i] -= e.interval;
                        float kill = e.damage / a.hp;
                        if (kill > allies) kill = allies;
                        float before = allies;
                        allies -= kill;
                        LostThisTick += kill; AlliesLost += kill;
                        int hk = AllyHitSerial++ & (HitRing - 1);
                        AllyHitX[hk] = EX[i]; AllyHitZ[hk] = EZ[i];
                        // 화면에 보이는 수(올림)가 줄어든 만큼 — 그 한 명 한 명이 **이 좀비 앞에서** 죽었다
                        int crossed = (int)(before + 0.999f) - (int)(allies + 0.999f);
                        for (int c = 0; c < crossed; c++)
                        {
                            int dk = AllyDeathSerial++ & (HitRing - 1);
                            AllyDeathX[dk] = EX[i]; AllyDeathZ[dk] = EZ[i];
                        }
                    }
                }
                else
                {
                    // **군단을 쫓는다.** 전에는 늘 −z 로만 걸어서, 군단이 빠르게 지나치면
                    // 적이 **뒤에 남아 영원히 멀어졌다** (측정: 최근접 −117 m). 뒤에 남은 적은
                    // 표적도 아니고 죽지도 않아서, 화면에서는 *지나쳐 버린 것*으로 보이고
                    // 판정에서는 *아직 안 끝난 전투*로 남는다
                    EState[i] = 0; EAttack[i] = 0f;
                    // **곧게 내려온다.** 전에는 소대를 쫓아 가로로 모여들었다 — 그러면 옆구리에 붙어
                    // 아무 병사의 앞 줄에도 없이 물기만 했다 (탐침: 병사 50 명이 초당 4~13 발만 쐈다).
                    // 지나쳐 뒤로 8 m 넘게 빠진 적은 **흘려보낸 것**이다 — 사라진다
                    EZ[i] -= e.speed * dt;
                    if (dz < -8f) { Remove(i); Passed++; continue; }
                }
            }
        }

        /// <summary>
        /// 사거리 안의 적을 **틱마다 한 번** 모은다. 병사마다 전체를 훑으면 300 × 900 이 매 틱
        /// 돌아간다 — 같은 답을 300 번 구하는 셈이다.
        /// </summary>
        void Candidates(float allyZ)
        {
            candCount = 0;
            for (int i = 0; i < EnemyCount && candCount < CandCap; i++)
            {
                if (EState[i] == 2) continue;
                float dz = EZ[i] - allyZ;
                // 뒤로 흘러간 적도 **조금은** 쏜다 — 지나친 적이 그대로 쌓이면 전투가 안 끝난다
                if (dz < -6f || dz > Range) continue;
                cand[candCount++] = i;
            }
        }

        /// <summary>
        /// **병사마다 자기 타이머로 자기가 쏜다.** 300 명이면 초당 600 번의 판정이 각각 일어난다.
        /// 표적은 틱마다 모은 후보를 번호로 나눠 가진다 — 앞에 선 적부터 고르게 맞는다.
        /// </summary>
        void FirePerSoldier(float dt, UnitStats a)
        {
            for (int i = 0; i < AllyCount; i++)
            {
                aFireT[i] -= dt;
                if (aFireT[i] > 0f) continue;

                float tx, tz; int t = -1;
                // **통 앞에 선 병사는 통을 쏜다 — 적이 와 있어도.** 이것이 대가가 생기는 자리다
                int b = BarrelCount > 0 ? BarrelFor(AX[i], AZ[i]) : -1;
                if (b >= 0)
                {
                    tx = RX[b]; tz = RZ[b];
                }
                else if (candCount > 0 && (t = LaneTarget(AX[i], AZ[i])) >= 0)
                {
                    // **총알은 곧게만 간다** (오너 2026-10-20: *"좌우로 움직이는 만큼 총은 직선으로만
                    // 날라가야 해"*). 전에는 사거리 안 아무 적이나 나눠 맡아 비스듬히 쐈다. 이제 병사는
                    // **자기 앞 줄**(좌우 ±LaneHalf)에 든 가장 가까운 적만 맞힌다 — 손가락으로 옮기는
                    // 것이 곧 조준이고, 대열 폭 밖으로 비켜 온 적은 아무도 못 쏜다
                    tx = AX[i]; tz = EZ[t];
                }
                else if (HasWall && WallHp > 0f && WallZ - AZ[i] <= Range)
                {
                    tx = AX[i]; tz = WallZ;
                }
                else
                {
                    // 쏠 것이 없다 — **타이머를 돌리지 않는다.** 돌리면 적이 나타나는 순간
                    // 전원이 한 프레임에 쏘고 그다음 반 초가 비어, 첫 교전이 깜빡인다
                    aFireT[i] = 0f;
                    continue;
                }

                aFireT[i] += a.interval;
                AAimX[i] = tx; AAimZ[i] = tz;
                FiringThisTick++; ShotsThisTick++; HitsThisTick++;
                AFlash[i] = FlashTime;

                // **명중은 즉시 들어간다** (히트스캔). 보이는 탄환은 그중 일부일 뿐이고,
                // 그 분리는 오너가 허락한 것이다 — 거꾸로(피해를 한 번에 계산하고 탄환을
                // 장식으로 뿌리는 것)가 가짜 사격이고, 그게 이 파일이 방금 지운 것이다
                if (t >= 0)
                {
                    EHp[t] -= a.damage;
                    EHurt[t] = 0.12f;
                    if (HitCount < HitCap) { HitX[HitCount] = EX[t]; HitZ[HitCount] = EZ[t]; HitCount++; }
                    if (EHp[t] <= 0f)
                    {
                        EHp[t] = 0f; EState[t] = 2; EDieT[t] = 0f;
                        int dk = DeathSerial++ & (DeathRing - 1);
                        DeadX[dk] = EX[t]; DeadZ[dk] = EZ[t];
                        KilledThisTick++; EnemiesKilled += 1f;
                        // 죽은 것은 후보에서 뺀다 — 안 그러면 이번 틱의 뒷 병사들이 시체에 쏜다
                        for (int c = 0; c < candCount; c++)
                            if (cand[c] == t) { cand[c] = cand[--candCount]; break; }
                    }
                }
                else if (b >= 0)
                {
                    RHp[b] -= a.damage;
                    if (HitCount < HitCap) { HitX[HitCount] = tx; HitZ[HitCount] = tz; HitCount++; }
                    if (RHp[b] <= 0f)
                    {
                        RHp[b] = 0f;
                        BrokenThisTick += RGives[b]; BrokeCountThisTick++;
                        LastBrokeX = RX[b]; LastBrokeZ = RZ[b]; LastBrokeGives = RGives[b]; BrokeSerial++;
                    }
                }
                else
                {
                    WallHp -= a.damage;
                    if (WallHp < 0f) WallHp = 0f;
                    if (HitCount < HitCap) { HitX[HitCount] = tx; HitZ[HitCount] = tz; HitCount++; }
                }

                Tracer(dt, AX[i], AZ[i], tx, tz);
            }
        }

        /// <summary>
        /// 보이는 탄환. **실제로 일어난 발사에서만** 나온다 — 예산을 넘으면 그 발은 안 그려질
        /// 뿐이고 피해는 이미 들어갔다.
        /// </summary>
        void Tracer(float dt, float sx, float sz, float tx, float tz)
        {
            tracerAccum += TracerPerSec * dt;
            if (tracerAccum < 1f || BulletCount >= BulletCap) return;
            tracerAccum -= 1f;

            // 곧게 앞으로 — 표적까지의 **z 거리**만큼 날아간다
            float len = tz - sz;
            if (len < 1e-3f) return;
            int b = BulletCount++;
            BX[b] = sx; BZ[b] = sz;
            BDX[b] = 0f; BDZ[b] = 1f;
            bLife[b] = 0f; bRange[b] = len;
        }

        /// <summary>병사 한 줄의 반폭 — 좀비 어깨(0.5 m)와 병사 간격(0.7 m) 사이라 줄이 빈틈없이 이어진다</summary>
        public const float LaneHalf = 0.42f;

        /// <summary>그 병사 **바로 앞 줄**에서 가장 가까운 적. 없으면 −1</summary>
        int LaneTarget(float x, float z)
        {
            int best = -1; float bd = float.MaxValue;
            for (int c = 0; c < candCount; c++)
            {
                int e = cand[c];
                float dz = EZ[e] - z;
                if (dz <= 0f || dz > Range) continue;
                float dx = EX[e] - x;
                if (dx > LaneHalf || dx < -LaneHalf) continue;
                if (dz < bd) { bd = dz; best = e; }
            }
            return best;
        }

        void StepBullets(float dt)
        {
            for (int b = BulletCount - 1; b >= 0; b--)
            {
                BX[b] += BDX[b] * BulletSpeed * dt;
                BZ[b] += BDZ[b] * BulletSpeed * dt;
                bLife[b] += dt;
                if (bLife[b] * BulletSpeed >= bRange[b])
                {
                    int last = --BulletCount;
                    if (b != last)
                    {
                        BX[b] = BX[last]; BZ[b] = BZ[last]; BDX[b] = BDX[last]; BDZ[b] = BDZ[last];
                        bLife[b] = bLife[last]; bRange[b] = bRange[last];
                    }
                }
            }
        }

        /// <summary>
        /// 빈자리를 맨 뒤로 메운다. **후보 배열이 그 번호를 들고 있으면 따라가게** 고친다 —
        /// 안 그러면 이번 틱의 남은 병사들이 엉뚱한 적을 쏜다. 조용히 틀리는 종류다.
        /// </summary>
        void Remove(int i)
        {
            int last = --EnemyCount;
            if (i != last)
            {
                EX[i] = EX[last]; EZ[i] = EZ[last]; EHp[i] = EHp[last]; EMaxHp[i] = EMaxHp[last];
                EState[i] = EState[last]; EDieT[i] = EDieT[last]; eAtkT[i] = eAtkT[last]; EHurt[i] = EHurt[last];
                EAttack[i] = EAttack[last];
            }
            for (int c = 0; c < candCount; c++)
            {
                if (cand[c] == last) { cand[c] = i; break; }
                if (cand[c] == i) { cand[c] = cand[--candCount]; break; }
            }
        }

        public void Clear()
        {
            AllyCount = 0; EnemyCount = 0; BulletCount = 0; pendingCount = 0;
            streamAccum = 0f; tracerAccum = 0f; candCount = 0;
            HasWall = false; WallHp = 0f;
            BarrelCount = 0; BrokenThisTick = 0; BrokeCountThisTick = 0; MissedBarrels = 0;
            ShotsThisTick = HitsThisTick = KilledThisTick = FiringThisTick = HitCount = 0;
            LostThisTick = 0f; AlliesLost = 0f; EnemiesKilled = 0f;
        }
    }
}
