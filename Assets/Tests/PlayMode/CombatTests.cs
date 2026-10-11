using NUnit.Framework;
using CrowdRunner.Core;

namespace CrowdRunner.Tests
{
    /// <summary>
    /// **총격전이 진짜인가**를 묻는 검사 (`docs/COMBAT_REWORK.md` §6).
    ///
    /// 오너가 완료로 인정하지 않는 것이 둘이다 — *숫자만 줄어드는 전투* 와 *총알 효과만
    /// 표시하는 가짜 사격*. 두 가지는 **화면을 봐도 구분이 안 간다**: 둘 다 총알이 날고
    /// 숫자가 준다. 구분되는 자리는 *총알이 HP 를 깎는가* 하나뿐이고, 그래서 여기서 잰다.
    ///
    /// 장면을 세우지 않는다 (`[Test]`, `[UnityTest]` 아님) — `Core` 는 `UnityEngine` 을
    /// 안 쓰므로 전투 전체가 **프레임 없이** 돌아간다. 초 단위로 끝난다.
    /// </summary>
    public class CombatTests
    {
        static readonly UnitStats A = UnitStats.Ally;
        static readonly UnitStats E = UnitStats.Zombie;

        static float Run(Battle b, float allies, float seconds, float allyZ = 0f)
        {
            int n = (int)(seconds / Sim.FixedStep);
            for (int i = 0; i < n; i++) b.Step(Sim.FixedStep, ref allies, allyZ, 0f, A, E);
            return allies;
        }

        /// <summary>**총알이 HP 를 깎는다.** 이것이 아니면 전부 연출이다</summary>
        [Test]
        public void Bullets_Are_What_Kills()
        {
            var b = new Battle();
            b.Spawn(20, 10f, 0f, 7f, 1);
            Assert.AreEqual(20, b.Alive, "깔린 수");

            float allies = 30f;
            Run(b, allies, 3f);

            Assert.Greater(b.ShotsThisTick + (int)b.EnemiesKilled, 0, "한 발도 안 나갔다");
            Assert.Greater(b.EnemiesKilled, 0f, "맞아 죽은 적이 없다 — 탄환이 HP 를 안 깎는다");
            Assert.Less(b.Alive, 20, "적 수가 그대로다");
        }

        /// <summary>
        /// **병력 두 배 = 화력 두 배.** 폐기한 전선 소모 모델에서는 이것이 성립하지 않았다 —
        /// 아군 수가 결과에 안 들어가서 적 하나당 잃는 아군이 늘 0.427 이었다.
        /// </summary>
        [Test]
        public void More_Units_Means_More_Firepower()
        {
            float KillsIn(float allies, float sec)
            {
                var b = new Battle();
                b.Spawn(400, 12f, 0f, 7f, 5);
                Run(b, allies, sec);
                return b.EnemiesKilled;
            }

            float few = KillsIn(10f, 2f);
            float many = KillsIn(100f, 2f);
            Assert.Greater(few, 0f, "10 명이 아무것도 못 죽였다");
            // 정확히 10 배를 요구하지 않는다: 사거리 안에 선 적 수와 탄환 비행 시간이 걸린다.
            // 요구하는 것은 **수가 화력이라는 성질**이고, 4 배면 그 성질이 산 것이다
            Assert.Greater(many, few * 4f, $"10 명 {few:0} · 100 명 {many:0} — 수가 화력이 아니다");
        }

        /// <summary>
        /// **그려야 할 탄환은 유한하다.** 300 명이면 600 발/초인데, 그걸 다 들면 WebGL 이 선다.
        /// 묶어도 **피해 총량은 같아야** 하고 — 그 둘을 한 번에 본다.
        /// </summary>
        [Test]
        public void Bullets_Are_Packed_But_Damage_Is_Not_Lost()
        {
            var b = new Battle();
            b.Spawn(Battle.EnemyCap, 12f, 0f, 7f, 9);
            float allies = 300f;
            int peak = 0;
            for (int i = 0; i < 120; i++)
            {
                b.Step(Sim.FixedStep, ref allies, 0f, 0f, A, E);
                if (b.BulletCount > peak) peak = b.BulletCount;
            }
            Assert.LessOrEqual(peak, Battle.BulletCap, "탄환이 상한을 넘었다");
            // 300 명 × 10 ÷ 0.5 = 6,000 dps. 2 초면 12,000 피해 = 적 400 마리.
            // 사거리 안에 있는 만큼만 맞으므로 절반만 요구한다 — 묶음이 피해를 **버리지** 않았는가
            Assert.Greater(b.EnemiesKilled, 100f, $"2 초에 {b.EnemiesKilled:0} 마리 — 묶음이 피해를 버렸다");
        }

        /// <summary>
        /// **살아서 닿은 적만 때린다.** 화력이 모자라면 실제로 병력을 잃어야 한다 —
        /// 안 잃으면 적은 장식이고, 그것이 오너가 말한 *숫자만 줄어드는 전투*의 반대쪽 실패다.
        /// </summary>
        [Test]
        public void Enemies_That_Survive_Actually_Hit_Back()
        {
            var b = new Battle();
            b.Spawn(300, 6f, 0f, 7f, 3);
            float allies = 8f;
            float left = Run(b, allies, 6f);
            Assert.Less(left, allies, "적 300 에 아군 8 인데 한 명도 안 줄었다");
            Assert.Greater(b.AlliesLost, 0f, "접촉 공격이 안 들어갔다");
        }

        /// <summary>쓰러지는 연출 시간이 지나면 치운다 — 남으면 배열이 찬다</summary>
        [Test]
        public void The_Dead_Fall_Then_Leave()
        {
            var b = new Battle();
            b.Spawn(6, 8f, 0f, 7f, 11);
            float allies = 200f;
            bool sawFalling = false;
            for (int i = 0; i < 180; i++)
            {
                b.Step(Sim.FixedStep, ref allies, 0f, 0f, A, E);
                for (int k = 0; k < b.EnemyCount; k++) if (b.EState[k] == 2) sawFalling = true;
            }
            Assert.IsTrue(sawFalling, "쓰러지는 상태를 한 번도 안 거쳤다 — 그냥 사라진 것이다");
            Assert.AreEqual(0, b.EnemyCount, "연출이 끝났는데 배열에 남아 있다");
        }

        /// <summary>벽은 **총알이** 부순다 (COMBAT_REWORK §2g)</summary>
        [Test]
        public void The_Wall_Falls_To_Bullets()
        {
            var b = new Battle();
            b.SetWall(10f, 900f);
            float allies = 40f;
            Run(b, allies, 2f);
            Assert.Less(b.WallHp, 900f, "벽이 한 대도 안 맞았다");
        }

        /// <summary>
        /// **같은 판 + 같은 입력 = 같은 결과.** 오너 완료 조건의 마지막 줄(*"시뮬레이터의 전투
        /// 결과와 실제 게임 결과가 일치한다"*)이 이 성질 위에 선다. 적 배치에 난수를 쓰므로
        /// 씨앗이 새면 여기서만 드러난다.
        /// </summary>
        [Test]
        public void Same_Level_Same_Input_Same_Outcome()
        {
            float[] Play()
            {
                var sim = new Sim(TestLevel());
                for (int i = 0; i < 60 * 60 && sim.State != SimState.Won && sim.State != SimState.Lost; i++)
                    sim.Step(0f);
                return new[] { sim.Allies, sim.Z, sim.EnemiesKilled, (float)(int)sim.State };
            }
            var a = Play(); var c = Play();
            Assert.AreEqual(a[0], c[0], 1e-4f, "남은 병력이 다르다");
            Assert.AreEqual(a[1], c[1], 1e-4f, "간 거리가 다르다");
            Assert.AreEqual(a[2], c[2], 1e-4f, "지운 적 수가 다르다");
            Assert.AreEqual(a[3], c[3], "승패가 다르다");
        }

        /// <summary>
        /// **적은 닿기 전에 보여야 한다.** 오너가 본 화면이 빈 복도였다 (*"그리구 적도 없잖어"*).
        /// 닿는 순간에 깔면 그 전까지 길에는 아무것도 없다.
        /// </summary>
        [Test]
        public void Enemies_Are_On_Screen_Before_They_Arrive()
        {
            var sim = new Sim(TestLevel());
            float firstSeenGap = -1f;
            for (int i = 0; i < 60 * 60; i++)
            {
                sim.Step(0f);
                if (sim.Battle.EnemyCount > 0) { firstSeenGap = sim.Battle.EZ[0] - sim.Z; break; }
            }
            Assert.Greater(firstSeenGap, 15f,
                $"적이 {firstSeenGap:0.0} m 앞에서 생겼다 — 갑자기 나타나는 것으로 보인다");
        }

        static LevelData TestLevel()
        {
            var lv = new LevelData { chapter = 1, index = 1, initialUnits = 20, roadWidth = 7f, forwardSpeed = 7f, length = 120f };
            lv.events.Add(LevelEvent.Gate(40f, 60f, new GateOption(-1, GateOp.Add, 25), new GateOption(+1, GateOp.Multiply, 2)));
            lv.events.Add(LevelEvent.Enemy(70f, 30));
            lv.events.Add(LevelEvent.Enemy(110f, 60, isFinal: true));
            Assert.IsNull(lv.Validate(), "검사용 레벨이 규칙을 어겼다");
            return lv;
        }
    }
}
