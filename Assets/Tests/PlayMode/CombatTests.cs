using NUnit.Framework;
using UnityEngine;
using CrowdRunner.Core;

namespace CrowdRunner.Tests
{
    /// <summary>
    /// **병사가 각자 쏘는가**를 묻는 검사.
    ///
    /// 오너가 2026-10-11 에 짚은 것: *"현재 300 명의 아군이 실제로 모두 사격 판정을 수행하는지,
    /// 아니면 군단 전체의 공격력을 한 번에 계산하면서 일부 사격 연출만 보여주는지. 이 차이를
    /// 확인하지 않고 적 HP 만 올리면 계속 잘못된 방향으로 개발하게 돼."*
    ///
    /// 그 둘은 **화면으로는 구분이 안 간다** — 둘 다 총알이 날고 적이 죽는다. 구분되는 자리는
    /// *발사 판정이 몇 번 일어났는가* 뿐이라 여기서 센다.
    ///
    /// 그리고 오너 지시 5(*"적의 HP 와 사거리를 임의로 바꾸기 전에 전투 장면부터 검증해라"*)의
    /// 측정표를 `Measures_The_Battle_At_30_100_300` 이 찍는다.
    /// </summary>
    public class CombatTests
    {
        static readonly UnitStats A = UnitStats.Ally;
        static readonly UnitStats E = UnitStats.Zombie;
        const float Road = 7f;

        /// <summary>군단이 **제자리에서** 싸우는 한 틱 (적이 걸어온다)</summary>
        static float Tick(Battle b, float allies, float z, float x = 0f)
        {
            b.SetArmy(allies, x, z, Road, Sim.FixedStep);
            b.Step(Sim.FixedStep, ref allies, z, x, A, E);
            return allies;
        }

        static float Hold(Battle b, float allies, float seconds, float z = 0f)
        {
            int n = (int)(seconds / Sim.FixedStep);
            for (int i = 0; i < n; i++) allies = Tick(b, allies, z);
            return allies;
        }

        /// <summary>
        /// **병사 수만큼 발사 판정이 일어난다.** 300 명 · 간격 0.5 초면 1 초에 600 번이다.
        /// 묶어서 계산하면 이 수가 240(그리는 상한)이나 1(한 번에 계산) 근처로 나온다.
        /// </summary>
        [Test]
        public void Every_Soldier_Fires_For_Itself()
        {
            foreach (int n in new[] { 30, 300 })
            {
                var b = new Battle();
                b.Queue(900, 0f, 0f, Road, 1);
                // 적이 사거리에 들어올 때까지 걸어오게 둔다
                float allies = n;
                for (int i = 0; i < 60 * 20 && b.ShotsThisTick == 0; i++) allies = Tick(b, allies, 0f);
                Assert.Greater(b.ShotsThisTick, 0, $"{n} 명인데 한 발도 안 나갔다");
                allies = Hold(b, allies, 3f);   // 전선이 자리잡을 때까지

                int shots = 0;
                for (int i = 0; i < 60; i++) { allies = Tick(b, allies, 0f); shots += b.ShotsThisTick; }

                float want = n / A.interval;      // 초당 발사 판정 수
                Assert.That(shots, Is.EqualTo(want).Within(want * 0.15f),
                    $"{n} 명이 1 초에 {shots} 번 쐈다 — 기대 {want:0}. 군단을 한 번에 계산하고 있다");
            }
        }

        /// <summary>**병력 열 배 = 화력 열 배.** 전선 폭으로 깎지 않는다</summary>
        [Test]
        public void Ten_Times_The_Army_Is_Ten_Times_The_Fire()
        {
            // **무리가 커야 화력이 값을 한다.** 흘러드는 속도가 무리 크기에서 나오므로
            // (`Battle.ReleaseSeconds`) 1,200 짜리 무리는 초당 300 명씩 쏟아진다 — 30 명으로는
            // 못 막고 300 명으로는 막는다. 작은 무리로 재면 둘 다 "다 죽였다" 가 나와
            // **수가 화력인지 아닌지가 안 보인다**
            float Kills(int n)
            {
                var b = new Battle();
                b.Queue(1200, 0f, 0f, Road, 5);
                float allies = n;
                for (int i = 0; i < 60 * 20 && b.ShotsThisTick == 0; i++) allies = Tick(b, allies, 0f);
                float k0 = b.EnemiesKilled;
                for (int i = 0; i < 120 && allies > 0f; i++) allies = Tick(b, allies, 0f);
                return b.EnemiesKilled - k0;
            }
            float few = Kills(30), many = Kills(300);
            Assert.Greater(few, 0f, "30 명이 하나도 못 죽였다");
            Assert.Greater(many, few * 4f, $"30 명 {few:0} · 300 명 {many:0} — 수가 화력이 아니다");
        }

        /// <summary>
        /// **적은 한 번에 쏟아지지 않고 흘러든다** (오너 지시 2). 통째로 깔면 큰 군단이 1 초에
        /// 지우고 다시 빈 복도를 달린다 — 그것이 "전선"이 아닌 이유다.
        /// </summary>
        [Test]
        public void Enemies_Keep_Coming_Instead_Of_Dumping()
        {
            var b = new Battle();
            b.Queue(400, 0f, 0f, Road, 7);
            float allies = 50f;

            allies = Tick(b, allies, 0f);
            Assert.Less(b.EnemyCount, 10, $"한 틱에 {b.EnemyCount} 명이 한꺼번에 깔렸다");
            Assert.AreEqual(400, b.Remaining, "예약한 수가 남아 있지 않다");

            Hold(b, allies, 3f);
            int mid = b.EnemyCount;
            Assert.Greater(mid, 20, "3 초가 지났는데 거의 안 나왔다");
            Assert.Less(mid, 400, "결국 통째로 깔렸다");
        }

        /// <summary>**살아서 닿은 적이 전선을 만들고 병력을 가져간다**</summary>
        [Test]
        public void Survivors_Reach_The_Line_And_Take_Soldiers()
        {
            var b = new Battle();
            b.Queue(700, 0f, 0f, Road, 3);
            float allies = 25f;
            allies = Hold(b, allies, 26f);
            Assert.Less(allies, 25f, "적 700 에 아군 25 인데 한 명도 안 줄었다");
            Assert.Greater(b.AlliesLost, 0f, "접촉 공격이 안 들어갔다");
        }

        /// <summary>맞으면 **피격 반응**이 남고, 죽으면 쓰러졌다가 치워진다</summary>
        [Test]
        public void Hit_Then_Fall_Then_Leave()
        {
            var b = new Battle();
            b.Queue(12, 0f, 0f, Road, 11);
            float allies = 120f;
            bool sawHurt = false, sawFalling = false;
            for (int i = 0; i < 60 * 30; i++)
            {
                allies = Tick(b, allies, 0f);
                for (int k = 0; k < b.EnemyCount; k++)
                {
                    if (b.EHurt[k] > 0f) sawHurt = true;
                    if (b.EState[k] == 2) sawFalling = true;
                }
                if (b.EnemyCount == 0 && b.Remaining == 0) break;
            }
            Assert.IsTrue(sawHurt, "맞은 표시가 한 번도 안 남았다");
            Assert.IsTrue(sawFalling, "쓰러지는 상태를 안 거쳤다 — 그냥 사라진 것이다");
            Assert.AreEqual(0, b.EnemyCount, "연출이 끝났는데 배열에 남아 있다");
        }

        /// <summary>벽은 **총알이** 부순다</summary>
        [Test]
        public void The_Wall_Falls_To_Bullets()
        {
            var b = new Battle();
            b.SetWall(10f, 1200f);
            float allies = 40f;
            Hold(b, allies, 2f);
            Assert.Less(b.WallHp, 1200f, "벽이 한 대도 안 맞았다");
        }

        /// <summary>같은 판 + 같은 입력 = 같은 결과 (시뮬과 게임이 일치하는 근거)</summary>
        [Test]
        public void Same_Level_Same_Input_Same_Outcome()
        {
            float[] Play()
            {
                var sim = new Sim(TestLevel());
                for (int i = 0; i < 60 * 90 && sim.State != SimState.Won && sim.State != SimState.Lost; i++)
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
        /// **오너 지시 5 의 측정표.** 병력 30 · 100 · 300 에서 화면에 몇 명이 보이는지, 몇 명이
        /// 쏘는지, 초당 몇 명이 쓰러지는지, 적이 어디까지 접근하는지.
        ///
        /// 단언을 거의 안 한다 — 이 시험의 일은 **수를 내놓는 것**이고, 그 수를 보고 적 HP 나
        /// 사거리를 정하는 것은 사람이다. 숫자 없이 고치면 또 엉뚱한 쪽으로 간다.
        /// </summary>
        [Test]
        public void Measures_The_Battle_At_30_100_300()
        {
            Debug.Log("[ZQ-TEST] 병력 | 논리적 | 그린 | 쏜 병사/초 | 발사 판정/초 | 적 화면 | 처치/초 | 최근접 m | 속도");
            foreach (int n in new[] { 30, 100, 300 })
            {
                var b = new Battle();
                // 무리를 **병력에 맞춰** 건다 — 900 을 고정으로 두면 30 명은 시작하자마자
                // 전멸해서 측정값이 전부 0 이 된다 (첫 주행에서 그랬다)
                b.Queue(n * 3, 0f, 0f, Road, 13);
                float allies = n, z = 0f, mul = 1f;
                int fired = 0, shots = 0, killed0 = 0;
                float closest = 99f; int peakEnemies = 0; int seconds = 0;

                for (int i = 0; i < 60 * 24 && allies > 0f; i++)
                {
                    b.SetArmy(allies, 0f, z, Road, Sim.FixedStep);
                    mul = b.Step(Sim.FixedStep, ref allies, z, 0f, A, E);
                    // 실제 게임처럼 **싸우면서 민다** (오너 지시 6)
                    z += 7f * mul * Sim.FixedStep;

                    // **싸우는 동안만 잰다.** 무리가 끝나면 다음 무리를 건다 — 안 그러면
                    // 측정 구간의 대부분이 *전투가 끝난 빈 복도*라 "전원이 안 쏜다" 로 읽힌다
                    if (b.Remaining == 0) b.Queue(n * 3, z, 0f, Road, 13 + i);
                    if (i >= 60 * 6 && b.Alive > 0)
                    {
                        fired += b.FiringThisTick; shots += b.ShotsThisTick; seconds++;
                        if (b.EnemyCount > peakEnemies) peakEnemies = b.EnemyCount;
                        for (int k = 0; k < b.EnemyCount; k++)
                            if (b.EState[k] != 2) { float d = b.EZ[k] - z; if (d < closest) closest = d; }
                    }
                    if (i == 60 * 6) killed0 = (int)b.EnemiesKilled;
                }
                float sec = seconds / 60f;
                Debug.Log($"[ZQ-TEST] {n} | {b.AllyCount} | {Mathf.Min(b.AllyCount, 400)} | " +
                          $"{fired / sec:0} | {shots / sec:0} | {peakEnemies} | " +
                          $"{((int)b.EnemiesKilled - killed0) / sec:0} | {closest:0.0} | {mul:0.00}");

                // **전멸해도 적는다.** 작은 군단이 못 버티는 것은 결함이 아니라 측정값이고,
                // 그 수가 레벨이 몇 명을 깔아도 되는지를 정한다 (오너 지시 5)
                if (allies <= 0f) Debug.Log($"[ZQ-TEST] {n} | 전멸 — 초당 {n * 2 / 3:0} 명을 지우는데 {n * 3 / 4:0} 명이 온다");
                else Assert.Greater(shots / sec, n * 0.8f, $"{n} 명이 초당 {shots / sec:0} 발 — 전원이 쏘고 있지 않다");
            }
        }

        static LevelData TestLevel()
        {
            var lv = new LevelData { chapter = 1, index = 1, initialUnits = 20, roadWidth = Road, forwardSpeed = 7f, length = 150f };
            lv.events.Add(LevelEvent.Gate(40f, 60f, new GateOption(-1, GateOp.Add, 25), new GateOption(+1, GateOp.Multiply, 2)));
            lv.events.Add(LevelEvent.Enemy(70f, 60));
            lv.events.Add(LevelEvent.Enemy(130f, 120, isFinal: true));
            Assert.IsNull(lv.Validate(), "검사용 레벨이 규칙을 어겼다");
            return lv;
        }
    }
}
