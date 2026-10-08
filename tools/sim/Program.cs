using System;
using System.Collections.Generic;
using CrowdRunner.Core;

namespace CrowdRunner.Tools
{
    /// <summary>
    /// **M1 의 끝 조건**: 레벨 하나를 Unity 없이 끝까지 돌려 잔여 병력이 나온다.
    ///
    /// 그리고 그 위에 **M4 가 바로 얹힌다** — 게이트가 n 개면 경로가 2ⁿ 이므로, 전부 돌려서
    /// *최적 / 일반 / 실패* 를 뽑는다 (기획서 §6.3 이 요구한 세 경로 검증). 사람이 손으로 열 번
    /// 플레이해서 감으로 맞추는 대신 **숫자로** 본다.
    ///
    ///     cd tools\sim ; dotnet run -c Release
    /// </summary>
    static class Program
    {
        static int Main(string[] args)
        {
            Console.OutputEncoding = System.Text.Encoding.UTF8;
            var level = Sample();

            string bad = level.Validate();
            if (bad != null) { Console.WriteLine("레벨 " + level.Code + " 이 틀렸다: " + bad); return 1; }

            int gates = 0;
            foreach (var e in level.events) if (e.kind == EventKind.Gate) gates++;
            Console.WriteLine($"레벨 {level.Code} · 시작 {level.initialUnits} 명 · 길이 {level.length} m · 게이트 {gates} 개 · 경로 {1 << gates} 가지");
            Console.WriteLine();

            var results = new List<(string path, Run r)>();
            for (int mask = 0; mask < (1 << gates); mask++)
                results.Add((PathName(mask, gates), RunOne(level, mask)));

            Console.WriteLine("| 경로 | 결과 | 잔여 | 전투 전사 | 지역에서 녹음 | 적 처치 | k | 시간 |");
            Console.WriteLine("|---|---|---|---|---|---|---|---|");
            foreach (var (path, r) in results)
                Console.WriteLine($"| {path} | {(r.won ? "클리어" : "실패")} | {r.units} | {r.lost:0} | {r.zone:0} | {r.killed:0} | {r.k:0.00} | {r.time:0.0}s |");

            // **세 경로가 다 있어야 레벨이다** (기획서 §6.3). 전부 클리어면 선택이 없는 것이고,
            // 전부 실패면 못 깨는 판이다 — 둘 다 "레벨을 안 만든 것" 과 같다
            int won = 0;
            foreach (var (_, r) in results) if (r.won) won++;
            Console.WriteLine();
            Console.WriteLine($"클리어 가능한 경로 {won} / {results.Count}");
            if (won == 0) { Console.WriteLine("** 못 깨는 레벨이다 — 어떤 선택으로도 최종 방어선을 넘지 못한다 **"); return 1; }
            if (won == results.Count) Console.WriteLine("** 모든 경로가 클리어된다 — 게이트가 선택이 아니라 장식이다 **");
            return 0;
        }

        struct Run { public bool won; public int units; public float lost, zone, killed, k, time; }

        /// <summary>
        /// 경로 하나. `mask` 의 비트가 게이트마다 **왼쪽(0) / 오른쪽(1)** 을 정한다 —
        /// 사람이 드래그하는 것과 **같은 문**(`Step(desiredX)`)으로 들어간다. 다른 문을 쓰면
        /// 도구가 잰 것과 사람이 한 것이 다른 게임이 된다.
        /// </summary>
        static Run RunOne(LevelData level, int mask)
        {
            var sim = new Sim(level);
            float half = level.roadWidth * 0.5f;

            // 다음 게이트를 **미리 보고** 그쪽으로 붙어 선다 — 사람도 게이트를 읽고 미리 옮긴다
            for (int step = 0; step < 60 * 600 && sim.State != SimState.Won && sim.State != SimState.Lost; step++)
            {
                int lane = 0;
                int idx = NextGate(level, sim.Z);
                if (idx >= 0)
                {
                    int bit = CountGatesBefore(level, idx);
                    lane = ((mask >> bit) & 1) == 0 ? -1 : +1;
                }
                sim.Step(lane * half * 0.5f);
            }

            return new Run
            {
                won = sim.State == SimState.Won,
                units = sim.Units,
                lost = sim.AlliesLost,
                zone = sim.ZoneLost,
                killed = sim.EnemiesKilled,
                k = sim.LossCoefficient,
                time = sim.Time,
            };
        }

        static int NextGate(LevelData level, float z)
        {
            for (int i = 0; i < level.events.Count; i++)
                if (level.events[i].kind == EventKind.Gate && level.events[i].z >= z) return i;
            return -1;
        }

        static int CountGatesBefore(LevelData level, int index)
        {
            int n = 0;
            for (int i = 0; i < index; i++) if (level.events[i].kind == EventKind.Gate) n++;
            return n;
        }

        static string PathName(int mask, int gates)
        {
            var s = new System.Text.StringBuilder();
            for (int i = 0; i < gates; i++) s.Append(((mask >> i) & 1) == 0 ? 'L' : 'R');
            return s.Length == 0 ? "(게이트 없음)" : s.ToString();
        }

        /// <summary>
        /// 손으로 적은 1-1. **데이터 모양이 쓸 만한지 보려고** 둔 것이고, 진짜 레벨은
        /// `ScriptableObject` / JSON 에서 온다 (기획서 §12).
        /// </summary>
        static LevelData Sample()
        {
            // **대가를 다른 축에 둔다** (세션 B 지적 2026-10-08). *"많이 주지만 적도 많다"* 는
            // 여전히 수 대 수라 **산수 한 번이면 끝**이고, 한 번 풀면 매번 같은 답이다.
            //
            // 그래서 오른쪽(×3)은 **지속 피해 지역 + 벽**을 지나게 한다. 둘 다 전선 폭에 안 막힌다:
            //  · 지역은 **머릿수에 비례**해 깎으므로 많이 받을수록 많이 녹는다
            //  · 벽은 **시간**을 먹는데, 그 시간 동안 지역이 계속 깎는다
            // 왼쪽(+30)은 적게 받지만 **깨끗하다.** 어느 쪽이 나은지는 *지금 병력이 얼마인가*에
            // 달리고, 그래서 매 판 다시 판단한다.
            var l = new LevelData { chapter = 1, index = 1, initialUnits = 10, roadWidth = 7f, length = 170f };

            l.events.Add(LevelEvent.Gate(20f, commitUntilZ: 90f,
                new GateOption(-1, GateOp.Add, 30),
                new GateOption(+1, GateOp.Multiply, 3)));

            // 오른쪽만: 녹는 길 + 그 안의 벽
            l.events.Add(LevelEvent.Zone(30f, dps: 1.2f, length: 40f, lane: +1));
            l.events.Add(LevelEvent.Wall(55f, 600f, lane: +1));
            // 왼쪽만: 작은 적 떼
            l.events.Add(LevelEvent.Enemy(60f, 15, lane: -1));

            l.events.Add(LevelEvent.Gate(95f, commitUntilZ: 0f,
                new GateOption(-1, GateOp.Add, 40),
                new GateOption(+1, GateOp.Multiply, 2)));

            l.events.Add(LevelEvent.Enemy(160f, 70, isFinal: true));
            return l;
        }
    }
}
