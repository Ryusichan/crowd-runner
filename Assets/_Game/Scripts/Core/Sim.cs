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
            if (State == SimState.Won || State == SimState.Lost) return;
            float dt = FixedStep;
            Time += dt;

            // 좌우는 **막혀 있어도** 움직인다 — 싸우는 중에 옆으로 못 비키면 조작이 끊긴 느낌이 난다
            float half = level.roadWidth * 0.5f;
            if (desiredX > half) desiredX = half;
            if (desiredX < -half) desiredX = -half;
            X = desiredX;

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
                Enter(i, e);
                if (BlockingIndex >= 0) return;
            }

            if (Z >= level.length) Finish();
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
                        float lx = e.options[k].lane * (level.roadWidth * 0.25f);
                        float d = X - lx; if (d < 0f) d = -d;
                        if (d < bestD) { bestD = d; best = k; }
                    }
                    var pick = e.options[best];
                    ChosenLanes.Add(pick.lane);
                    Allies = Apply(Allies, pick.op, pick.value);
                    if (Allies <= 0f) { State = SimState.Lost; }
                    break;
                }
                case EventKind.Enemy:
                    BlockingIndex = i;
                    blockingEnemies = e.enemyCount;
                    State = SimState.Fighting;
                    break;
                case EventKind.Wall:
                    BlockingIndex = i;
                    blockingWallHp = e.wallHp;
                    State = SimState.Breaking;
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
                Combat.Tick(ref al, ref blockingEnemies, ally, zombie, level.roadWidth, dt);
                Allies = al;
                AlliesLost += a0 - Allies;
                EnemiesKilled += e0 - blockingEnemies;

                if (Allies <= 0f) { State = SimState.Lost; BlockingIndex = -1; return; }
                if (blockingEnemies <= 0f)
                {
                    bool wasFinal = e.isFinal;
                    BlockingIndex = -1;
                    State = SimState.Running;
                    // **최종 방어선을 넘으면 거기서 끝난다** — 남은 길을 걸어가게 두면 판이 늘어지고,
                    // 그 사이에 아무 일도 없다
                    if (wasFinal) Finish();
                }
            }
            else // Wall
            {
                Combat.TickWall(Allies, ref blockingWallHp, ally, level.roadWidth, dt);
                if (blockingWallHp <= 0f) { BlockingIndex = -1; State = SimState.Running; }
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
