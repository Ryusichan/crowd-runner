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

            Console.WriteLine("| 경로 | 결과 | 잔여 | 최대 병력 | 전투 전사 | 지역에서 녹음 | 적 처치 | k | 시간 |");
            Console.WriteLine("|---|---|---|---|---|---|---|---|---|");
            foreach (var (path, r) in results)
                Console.WriteLine($"| {path} | {(r.won ? "클리어" : "실패")} | {r.units} | {r.peak} | {r.lost:0} | {r.zone:0} | {r.killed:0} | {r.k:0.00} | {r.time:0.0}s |");

            // **세 경로가 다 있어야 레벨이다** (기획서 §6.3). 전부 클리어면 선택이 없는 것이고,
            // 전부 실패면 못 깨는 판이다 — 둘 다 "레벨을 안 만든 것" 과 같다
            return Judge(results);
        }

        // ── 레벨 판정 ────────────────────────────────────────────────────────────
        //
        // **숫자를 찍는 것과 판정하는 것은 다르다.** 표만 찍으면 사람이 매번 읽고 판단해야 하고,
        // 레벨이 50 개가 되면 아무도 안 읽는다. 그래서 **규칙을 적어 두고 기계가 본다.**
        //
        // 규칙 값은 전부 `[WORKING]` 이다 — 기획서 §6.3 의 "세 경로" 를 숫자로 옮긴 것이고,
        // 실제 값은 플레이해 보고 정한다. 지금 중요한 것은 **값이 아니라 그 자리가 있는 것**이다.

        /// <summary>그릴 수 있는 최대 병력. **M0 가 정한다** — 방식 A 는 60~70 이었고 B 는 측정 중이다</summary>
        const int RenderCap = 0;   // 0 = 아직 모른다 → 그 검사를 건너뛴다

        static int Judge(List<(string path, Run r)> results)
        {
            int won = 0, best = 0, worst = int.MaxValue, peak = 0;
            foreach (var (_, r) in results)
            {
                if (r.won) { won++; if (r.units > best) best = r.units; if (r.units < worst) worst = r.units; }
                if (r.peak > peak) peak = r.peak;
            }

            Console.WriteLine();
            Console.WriteLine($"클리어 {won}/{results.Count} · 최대 병력 {peak} · 클리어 경로의 잔여 {worst}~{best}");

            var fail = new List<string>();
            var warn = new List<string>();

            // ① 깰 수 있어야 한다
            if (won == 0) fail.Add("어떤 선택으로도 최종 방어선을 넘지 못한다");

            // ② 고르는 뜻이 있어야 한다. **전부 클리어 = 게이트가 장식**이고,
            //    그건 이 장르의 **유일한 조작**이 아무 일도 안 한다는 뜻이다 (기획서 §3.4)
            if (won == results.Count && results.Count > 1)
                fail.Add("모든 경로가 클리어된다 — 게이트가 선택이 아니라 장식이다");

            // ③ 클리어되는 경로끼리도 **갈려야** 한다. 다 이기는데 결과가 같으면 고른 보람이 없다
            if (won > 1 && best > 0 && (best - worst) < best * 0.2f)
                warn.Add($"클리어 경로의 잔여가 {worst}~{best} 로 거의 같다 — 잘 고른 값이 작다");

            // ④ 그릴 수 있어야 한다. **최악이 기준이다** — 한 번만 넘어도 그 프레임이 무너지고,
            //    그 순간이 보통 증식 직후라 **가장 보여 주고 싶은 장면**이다
            if (RenderCap > 0 && peak > RenderCap)
                fail.Add($"최대 병력 {peak} 가 그릴 수 있는 수({RenderCap})를 넘는다");

            foreach (var w in warn) Console.WriteLine("  경고: " + w);
            foreach (var f in fail) Console.WriteLine("  ** 실패: " + f + " **");

            if (RenderCap == 0)
                Console.WriteLine("  (렌더 상한이 아직 없다 — M0 방식 B 측정 뒤 `RenderCap` 을 채운다)");

            return fail.Count > 0 ? 1 : 0;
        }

        struct Run { public bool won; public int units, peak; public float lost, zone, killed, k, time; }

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
                peak = sim.PeakUnits,
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
