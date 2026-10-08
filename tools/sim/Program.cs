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

            // 레벨은 **파일에서** 온다 — 코드에 박으면 10 개를 못 만든다 (기획서 §12)
            string dir = System.IO.Path.GetFullPath(System.IO.Path.Combine(
                AppContext.BaseDirectory, "../../../../../Assets/_Game/Resources/Levels"));
            if (args.Length > 0) dir = args[0];
            if (!System.IO.Directory.Exists(dir)) { Console.WriteLine("레벨 폴더가 없다: " + dir); return 1; }

            var files = System.IO.Directory.GetFiles(dir, "*.txt");
            Array.Sort(files, StringComparer.Ordinal);
            if (files.Length == 0) { Console.WriteLine("레벨이 하나도 없다: " + dir); return 1; }

            int bad = 0;
            foreach (var f in files)
            {
                string err;
                var level = LevelFile.Parse(System.IO.File.ReadAllText(f), out err);
                if (level == null)
                {
                    Console.WriteLine($"## {System.IO.Path.GetFileName(f)}  ** 못 읽는다 **");
                    Console.WriteLine("   " + err);
                    Console.WriteLine();
                    bad++;
                    continue;
                }
                if (Report(level) != 0) bad++;
                Console.WriteLine();
            }

            Console.WriteLine(bad == 0
                ? $"레벨 {files.Length} 개 — 전부 통과"
                : $"레벨 {files.Length} 개 중 **{bad} 개가 걸렸다**");
            return bad > 0 ? 1 : 0;
        }

        static int Report(LevelData level)
        {
            int gates = 0;
            foreach (var e in level.events) if (e.kind == EventKind.Gate) gates++;
            Console.WriteLine($"## {level.Code} · 시작 {level.initialUnits} · 길이 {level.length} m · 게이트 {gates} · 경로 {1 << gates} 가지");

            var results = new List<(string path, Run r)>();
            for (int mask = 0; mask < (1 << gates); mask++)
                results.Add((PathName(mask, gates), RunOne(level, mask)));

            Console.WriteLine("| 경로 | 결과 | 잔여 | 최대 | 전투 | 지역 | 처치 | k | 시간 |");
            Console.WriteLine("|---|---|---|---|---|---|---|---|---|");
            foreach (var (path, r) in results)
                Console.WriteLine($"| {path} | {(r.won ? "클리어" : "실패")} | {r.units} | {r.peak} | {r.lost:0} | {r.zone:0} | {r.killed:0} | {r.k:0.00} | {r.time:0.0}s |");

            return Judge(results, gates);
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

        // ── 레벨 판정 ────────────────────────────────────────────────────────────
        //
        // **숫자를 찍는 것과 판정하는 것은 다르다.** 표만 찍으면 사람이 매번 읽어야 하고,
        // 레벨이 50 개가 되면 아무도 안 읽는다. 그래서 **규칙을 적어 두고 기계가 본다.**
        //
        // 값은 전부 `[WORKING]` 이다 — 기획서 §6.3 의 "세 경로" 를 숫자로 옮긴 것이고, 실제 값은
        // 플레이해 보고 정한다. 지금 중요한 것은 값이 아니라 **그 자리가 있는 것**이다.

        /// <summary>그릴 수 있는 최대 병력. **M0 가 정한다** — 방식 A 는 60~70, B 는 측정 중</summary>
        const int RenderCap = 0;   // 0 = 아직 모른다 → 그 검사를 건너뛴다

        /// <summary>
        /// 잘 고른 것과 못 고른 것의 **잔여 병력 차이** 하한. `[WORKING]` 25 % —
        /// 그보다 작으면 플레이어가 **차이를 못 느낀다**. 실제 값은 사람이 해 보고 정한다
        /// </summary>
        const float MinSpread = 0.25f;

        static int Judge(List<(string path, Run r)> results, int gates)
        {
            int won = 0, best = 0, worst = int.MaxValue, peak = 0;
            foreach (var (_, r) in results)
            {
                if (r.won) { won++; if (r.units > best) best = r.units; if (r.units < worst) worst = r.units; }
                if (r.peak > peak) peak = r.peak;
            }
            if (won == 0) worst = 0;

            Console.WriteLine($"   클리어 {won}/{results.Count} · 최대 병력 {peak} · 클리어 잔여 {worst}~{best}");

            var fail = new List<string>();
            var warn = new List<string>();

            // ① 깰 수 있어야 한다
            if (won == 0) fail.Add("어떤 선택으로도 최종 방어선을 넘지 못한다");

            // ② **고른 것이 결과를 바꿔야 한다.** 이것 하나가 ②③ 을 겸한다.
            //
            // 처음에는 *"모든 경로가 클리어되면 실패"* 로 두었는데 **너무 뭉툭했다** — 기획서 §6.2 의
            // 1~3 판은 *가르치는* 판이라 어느 쪽을 골라도 깨지는 것이 맞고, 거기까지 떨어뜨리면
            // 튜토리얼을 만들 수 없다. 실패한 첫 주행이 그 둘을 걸었다.
            //
            // 바꿔야 하는 것은 **승패가 아니라 결과**다: 어떤 경로는 지거나, **남는 병력이 뚜렷이
            // 달라야** 한다. 둘 다 아니면 게이트는 장식이고, 이 장르에서 조작은 그것 하나뿐이다.
            float spread = best > 0 ? (best - worst) / (float)best : 0f;
            if (gates >= 2 && won == results.Count && spread < MinSpread)
                fail.Add($"모든 경로가 클리어되는데 잔여도 {worst}~{best} ({spread * 100f:0}% 차이)로 비슷하다 — 고를 이유가 없다");
            else if (gates >= 2 && won == results.Count && spread < MinSpread * 1.6f)
                warn.Add($"지는 경로가 없고 잔여 차이도 {spread * 100f:0}% 뿐이다 — 뒤 판에서는 더 갈라야 한다");

            // ④ 그릴 수 있어야 한다. **최악이 기준이다** — 한 번만 넘어도 그 프레임이 무너지고,
            //    그 순간이 보통 증식 직후라 **가장 보여 주고 싶은 장면**이다
            if (RenderCap > 0 && peak > RenderCap)
                fail.Add($"최대 병력 {peak} 가 그릴 수 있는 수({RenderCap})를 넘는다");

            foreach (var w in warn) Console.WriteLine("   경고: " + w);
            foreach (var f in fail) Console.WriteLine("   ** 실패: " + f + " **");
            return fail.Count > 0 ? 1 : 0;
        }
    }
}
