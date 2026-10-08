using System.Collections.Generic;

namespace CrowdRunner.Core
{
    public enum SimState { Running, Fighting, Breaking, Won, Lost }

    /// <summary>
    /// **한 판 전체의 로직.** `UnityEngine` 을 쓰지 않는다 — 그래서 `tools/sim` 이 에디터 없이
    /// 레벨을 끝까지 돌릴 수 있고, 그것이 레벨 검증 도구(M4)의 전제다 (`docs/DESIGN.md` §2).
    ///
    /// **고정 스텝**으로 돈다. 같은 레벨 + 같은 입력 = 같은 결과 — 프레임이 떨어져도, 기기가 달라도.
    /// 그 성질이 없으면 "모든 게이트 경로를 밀어 본다" 가 성립하지 않는다.
    ///
    /// **개체를 하나도 만들지 않는다.** 병력은 `Allies` 하나의 수이고, 개별 유닛은 **표현 쪽에만**
    /// 있다 (`DESIGN.md` §2 보험 — M0 가 떨어져 군중이 추상화돼도 이 파일은 그대로 선다).
    /// </summary>
    public class Sim
    {
        public const float FixedStep = 1f / 60f;
        /// <summary>논리 병력 상한 (기획서 §3.3). 넘기면 수가 커지는 쾌감 대신 숫자만 늘어난다</summary>
        public const int MaxUnits = 2000;

        readonly LevelData level;
        readonly UnitStats ally = UnitStats.Ally;
        readonly UnitStats zombie = UnitStats.Zombie;

        /// <summary>리더(군단 중심)의 전진 거리. **게이트 판정도 이 값으로 한다** — 개별 병력이 아니라</summary>
        public float Z { get; private set; }
        /// <summary>리더의 좌우 위치. 길 가운데가 0</summary>
        public float X { get; private set; }
        /// <summary>병력. 소수점으로 들고 **보여 줄 때만 내린다** — 틱마다 반올림하면 전사가 묻힌다</summary>
        public float Allies { get; private set; }
        public int Units => (int)Allies;
        public SimState State { get; private set; } = SimState.Running;
        public float Time { get; private set; }

        /// <summary>지금 막고 있는 사건의 번호 (없으면 -1). 전투·벽 중에는 전진하지 않는다</summary>
        public int BlockingIndex { get; private set; } = -1;
        /// <summary>그 사건에 남은 적 / 벽 체력</summary>
        // 프로퍼티가 아니라 **필드**다 — `Combat.TickWall` 이 `ref` 로 받기 때문이다.
        // 처음에 프로퍼티로 두고 임시 변수에 복사해 `ref` 로 넘겼는데, 그러면 깎인 값이
        // **돌아오지 않아 벽이 영원히 안 부서진다**. 컴파일은 된다
        float blockingEnemies, blockingWallHp;
        public float BlockingEnemies => blockingEnemies;
        public float BlockingWallHp => blockingWallHp;

        /// <summary>고른 게이트 기록 — 경로를 되짚을 때 쓴다 (`"L"`/`"R"`)</summary>
        public readonly List<int> ChosenLanes = new List<int>();
        /// <summary>전사 누계 — 끝난 뒤 `k`(적 1 명당 잃은 아군)를 **출력으로** 계산할 때 쓴다</summary>
        public float AlliesLost { get; private set; }
        public float EnemiesKilled { get; private set; }

        readonly bool[] fired;

        // ── 표현이 읽는 문 ────────────────────────────────────────────────────────
        // **이번 틱에 무슨 일이 있었나.** 표현(파티클·소리·카메라)은 *상태*가 아니라 *사건*을
        // 알아야 한다 — "병력이 10 에서 40 이 됐다" 가 아니라 "게이트가 터졌다" 여야
        // 생성 연출을 한 번만 낸다.
        //
        // **대리자(delegate)나 리스트를 쓰지 않는다.** 표현 쪽 예산이 프레임당 **관리 힙 할당
        // 0 B** 라서(`docs/DESIGN.md` M0), 이벤트를 객체로 넘기면 그 예산을 로직이 먼저 쓴다.
        // 그래서 **구조체 하나를 덮어쓰고** 표현이 `Step` 직후에 읽는다.
        public struct Tick
        {
            /// <summary>이번 틱에 게이트가 터졌나 — 터졌으면 어느 쪽(−1/+1)과 그 전후 병력</summary>
            public bool gateFired; public int gateLane; public float gateBefore, gateAfter;
            /// <summary>전투·벽이 시작/끝났나 — 카메라가 붙고 떨어지는 지점</summary>
            public bool fightBegan, fightEnded, wallBegan, wallEnded;
            /// <summary>이번 틱에 잃은 병력 (전투 / 지역) — 사망 연출의 양을 정한다</summary>
            public float lostToCombat, lostToZone;
            /// <summary>이번 틱에 지운 적 수</summary>
            public float killed;
        }
        /// <summary>**`Step` 직후에만 유효하다.** 다음 `Step` 이 덮어쓴다</summary>
        public Tick Last;

        /// <summary>지금 밟고 있는 지속 피해 지역들 (겹칠 수 있다)</summary>
        readonly List<int> zones = new List<int>();
        /// <summary>지금 지나는 좁은 구간들. **가장 좁은 것**이 이긴다 — 겹치면 더 답답한 쪽이 맞다</summary>
        readonly List<int> narrows = new List<int>();
        /// <summary>지역에서 녹은 누계 — 전투 손실과 **따로** 센다. 섞으면 어느 쪽이 비쌌는지 못 본다</summary>
        public float ZoneLost { get; private set; }

        /// <summary>
        /// 이 판에서 **동시에 존재한 최대 병력**. 레벨 검증이 이 수로 *"그릴 수 있는 레벨인가"* 를 본다.
        ///
        /// 왜 로직이 이걸 세나: 렌더링 예산은 **평균이 아니라 최악**에 걸린다 — 한 번이라도 넘으면
        /// 그 순간 프레임이 무너지고, 그 순간이 보통 **증식 직후**라 가장 보여 주고 싶은 장면이다.
        /// M0 측정(2026-10-08)에서 방식 A 가 **60~70 개체**에서 8 ms 예산을 다 썼다 — 즉 이 수가
        /// 레벨 설계의 **실제 상한**이고, 그 상한이 정해지기 전에 레벨을 많이 만들면 다시 짜게 된다.
        /// </summary>
        public int PeakUnits { get; private set; }

        public Sim(LevelData level)
        {
            this.level = level;
            Allies = level.initialUnits;
            fired = new bool[level.events.Count];
        }

        /// <param name="desiredX">
        /// 플레이어가 가려는 좌우 위치. **정책이 넣는다** — 사람이 드래그하면 그 값이고,
        /// 검증 도구는 "이 게이트에서 왼쪽" 같은 규칙으로 넣는다. 둘이 **같은 문**을 쓰므로
        /// 도구가 잰 경로와 사람이 한 플레이가 비교된다.
        /// </param>
        public void Step(float desiredX)
        {
            if (Units > PeakUnits) PeakUnits = Units;
            Last = default;   // 매 틱 비운다 — 안 비우면 지난 틱 연출이 계속 다시 난다
            if (State == SimState.Won || State == SimState.Lost) return;
            float dt = FixedStep;
            Time += dt;

            // 좌우는 **막혀 있어도** 움직인다 — 싸우는 중에 옆으로 못 비키면 조작이 끊긴 느낌이 난다
            float half = RoadWidth * 0.5f;
            if (desiredX > half) desiredX = half;
            if (desiredX < -half) desiredX = -half;
            X = desiredX;
            // 갇혀 있으면 **그 길 안에서만** 움직인다 — 칸막이 너머로는 못 간다
            if (Z < commitUntil)
            {
                float lo = commitLane < 0 ? -half : 0f, hi = commitLane < 0 ? 0f : half;
                if (X < lo) X = lo;
                if (X > hi) X = hi;
            }

            // **막혀 있어도 돈다.** 지역 안에서 벽을 때리고 있으면 그동안 계속 녹는 것이 맞다 —
            // 그 자리가 이 게임에서 제일 비싼 자리이고, 레벨이 그걸 노리고 짜일 수 있어야 한다
            for (int k = narrows.Count - 1; k >= 0; k--)
            {
                var nv = level.events[narrows[k]];
                if (Z > nv.z + nv.zoneLength) narrows.RemoveAt(k);
            }
            if (zones.Count > 0) { TickZones(dt); if (State == SimState.Lost) return; }

            if (BlockingIndex >= 0) { TickBlocking(dt); return; }

            Z += level.forwardSpeed * dt;

            // 지나친 사건을 **순서대로** 처리한다. 한 틱에 둘을 지나칠 수 있고(빠른 속도·긴 dt),
            // 그때 뒤엣것을 건너뛰면 레벨이 조용히 쉬워진다
            for (int i = 0; i < level.events.Count; i++)
            {
                if (fired[i]) continue;
                var e = level.events[i];
                if (Z < e.z) break;          // z 오름차순이 보장돼 있다 (`LevelData.Validate`)
                fired[i] = true;
                // **그쪽 길에 선 군단만 만난다.** 반대쪽으로 갔으면 그냥 지나간다 —
                // 이것이 게이트 선택에 뒤따르는 결과를 만든다 (`LevelEvent.lane`)
                if (e.lane != 0 && Side != e.lane) continue;
                Enter(i, e);
                if (BlockingIndex >= 0) return;
            }

            if (Z >= level.length) Finish();
        }

        /// <summary>
        /// 리더가 선 쪽 (−1 왼쪽 / +1 오른쪽). **갇혀 있는 동안은 고른 쪽으로 고정된다** —
        /// 두 길이 칸막이로 갈려 있기 때문이고, 그래야 게이트 선택에 결과가 붙는다.
        /// </summary>
        /// <summary>
        /// 지금 자리의 길 폭. 좁은 구간 안이면 **가장 좁은 것**이 이긴다 — 겹치면 더 답답한 쪽이 맞다.
        /// 전투·벽·좌우 제한이 전부 이 값을 쓴다. `level.roadWidth` 를 직접 읽는 곳이 남아 있으면
        /// **좁은 구간이 그 자리에서만 안 걸린다** — 조용히 틀리는 종류다.
        /// </summary>
        public float RoadWidth
        {
            get
            {
                float w = level.roadWidth;
                for (int k = 0; k < narrows.Count; k++)
                {
                    float nw = level.events[narrows[k]].narrowWidth;
                    if (nw < w) w = nw;
                }
                return w;
            }
        }

        public int Side => Z < commitUntil ? commitLane : (X < 0f ? -1 : +1);

        float commitUntil = -1f;
        int commitLane;

        /// <summary>
        /// 밟고 있는 지역이 **머릿수에 비례해** 깎는다. 전투와 달리 전선 폭에 안 막히므로,
        /// **병력이 많을수록 더 잃는다** — 이 게임에서 큰 군단에 붙는 유일한 대가다 (§3c).
        /// </summary>
        void TickZones(float dt)
        {
            for (int k = zones.Count - 1; k >= 0; k--)
            {
                var z = level.events[zones[k]];
                if (Z > z.z + z.zoneLength) { zones.RemoveAt(k); continue; }
                float before = Allies;
                Allies -= Allies * (z.dps / ally.hp) * dt;
                if (Allies < 0f) Allies = 0f;
                ZoneLost += before - Allies;
                Last.lostToZone += before - Allies;
            }
            if (Allies <= 0f) State = SimState.Lost;
        }

        void Enter(int i, LevelEvent e)
        {
            switch (e.kind)
            {
                case EventKind.Gate:
                {
                    // **리더 중심 하나로 고른다** (기획서 §3.3). 병력마다 판정하면 왼쪽 병력은 +50,
                    // 오른쪽 병력은 ×3 에 닿아 규칙이 무너진다. 고른 쪽과 **가장 가까운** 선택지가
                    // 당첨이고, 나머지는 그 순간 죽는다 — 한 짝에서 한 번만 발동한다
                    int best = 0; float bestD = float.MaxValue;
                    for (int k = 0; k < e.options.Length; k++)
                    {
                        float lx = e.options[k].lane * (RoadWidth * 0.25f);
                        float d = X - lx; if (d < 0f) d = -d;
                        if (d < bestD) { bestD = d; best = k; }
                    }
                    var pick = e.options[best];
                    Last.gateFired = true; Last.gateLane = pick.lane; Last.gateBefore = Allies;
                    ChosenLanes.Add(pick.lane);
                    if (e.commitUntilZ > Z) { commitUntil = e.commitUntilZ; commitLane = pick.lane; }
                    Allies = Apply(Allies, pick.op, pick.value);
                    Last.gateAfter = Allies;
                    // **게이트 직후에 바로 잰다.** 다음 틱까지 기다리면 그 사이 전투가 깎아서
                    // 최고점을 놓친다 — 그런데 화면에는 그 최고점이 **실제로 한 번 그려진다**
                    if (Units > PeakUnits) PeakUnits = Units;
                    if (Allies <= 0f) { State = SimState.Lost; }
                    break;
                }
                case EventKind.Enemy:
                    BlockingIndex = i;
                    blockingEnemies = e.enemyCount;
                    State = SimState.Fighting; Last.fightBegan = true;
                    break;
                case EventKind.Wall:
                    BlockingIndex = i;
                    blockingWallHp = e.wallHp;
                    State = SimState.Breaking; Last.wallBegan = true;
                    break;
                case EventKind.Narrow:
                    narrows.Add(i);
                    break;
                case EventKind.Zone:
                    // 막지 않는다 — **지나가면서** 깎인다. 그래서 전진이 멈추는 전투·벽과 다르다
                    zones.Add(i);
                    break;
            }
        }

        void TickBlocking(float dt)
        {
            var e = level.events[BlockingIndex];
            if (e.kind == EventKind.Enemy)
            {
                float a0 = Allies, e0 = blockingEnemies;
                float al = Allies;
                Combat.Tick(ref al, ref blockingEnemies, ally, zombie, RoadWidth, dt);
                Allies = al;
                AlliesLost += a0 - Allies;
                EnemiesKilled += e0 - blockingEnemies;
                Last.lostToCombat = a0 - Allies;
                Last.killed = e0 - blockingEnemies;

                if (Allies <= 0f) { State = SimState.Lost; BlockingIndex = -1; return; }
                if (blockingEnemies <= 0f)
                {
                    bool wasFinal = e.isFinal;
                    BlockingIndex = -1;
                    State = SimState.Running; Last.fightEnded = true;
                    // **최종 방어선을 넘으면 거기서 끝난다** — 남은 길을 걸어가게 두면 판이 늘어지고,
                    // 그 사이에 아무 일도 없다
                    if (wasFinal) Finish();
                }
            }
            else // Wall
            {
                Combat.TickWall(Allies, ref blockingWallHp, ally, RoadWidth, dt);
                if (blockingWallHp <= 0f) { BlockingIndex = -1; State = SimState.Running; Last.wallEnded = true; }
            }
        }

        void Finish()
        {
            // 최종 방어선을 깬 적이 있어야 이긴 것이다. 길 끝까지 갔는데 그걸 안 만났다면
            // **레벨 데이터가 틀린 것**이고 (`Validate` 가 막는다), 여기서는 졌다고 본다
            bool clearedFinal = false;
            for (int i = 0; i < level.events.Count; i++)
                if (fired[i] && level.events[i].kind == EventKind.Enemy && level.events[i].isFinal) clearedFinal = true;
            State = clearedFinal && Allies > 0f ? SimState.Won : SimState.Lost;
        }

        public static float Apply(float count, GateOp op, int value)
        {
            float r;
            switch (op)
            {
                case GateOp.Add: r = count + value; break;
                case GateOp.Multiply: r = count * value; break;
                case GateOp.Subtract: r = count - value; break;
                case GateOp.Divide: r = value > 0 ? count / value : count; break;
                default: r = count; break;
            }
            if (r < 0f) r = 0f;
            if (r > MaxUnits) r = MaxUnits;
            return r;
        }

        /// <summary>
        /// 판이 끝난 뒤의 **전투 손실 계수** — 기획서 §4.5 의 `k` 다. 여기서는 **입력이 아니라 출력**이다
        /// (`Combat` 주석). 적 하나를 지우는 데 아군 몇을 썼는가. 0 이면 손실 없이 이긴 것.
        /// </summary>
        public float LossCoefficient => EnemiesKilled > 0f ? AlliesLost / EnemiesKilled : 0f;
    }
}
