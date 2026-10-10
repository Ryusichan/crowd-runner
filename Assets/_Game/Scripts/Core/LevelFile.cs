using System;
using System.Collections.Generic;
using System.Globalization;

namespace CrowdRunner.Core
{
    /// <summary>
    /// 레벨을 **글로 적고 읽는다** (기획서 §12 "모든 게이트와 적, 장애물의 위치를 데이터로").
    ///
    /// **JSON 이 아니라 줄 단위 글**인 이유:
    /// · `Core/` 는 `UnityEngine` 을 못 쓴다 → `JsonUtility` 가 없다 (`docs/DESIGN.md` §2)
    /// · `System.Text.Json` 은 Unity 쪽에서 쓸 수 없다
    /// · 직접 짠 JSON 파서는 100 줄인데, 레벨은 **사람이 손으로 적고 눈으로 읽는** 것이라
    ///   그만한 값이 없다. 줄 단위면 **git diff 가 읽힌다** — 레벨 조정은 한 숫자를 바꾸는 일이고,
    ///   그 한 줄이 보이는 것이 JSON 중괄호보다 낫다
    ///
    /// **이 클래스는 문자열만 받는다.** 파일을 여는 것은 부르는 쪽이다 — Unity 는 `TextAsset`,
    /// `tools/sim` 은 디스크. 그래야 `Core/` 가 둘 다에서 돈다.
    ///
    /// 쓰는 모양:
    /// <code>
    /// # 1-1 — 게이트를 지나면 군단이 는다
    /// chapter 1
    /// index 1
    /// units 10
    /// road 7
    /// length 170
    ///
    /// gate 20 commit 90 | L add 30 | R mul 3
    /// zone 30 dps 1.2 len 40 lane R
    /// wall 55 hp 600 lane R
    /// narrow 70 width 3 len 30
    /// enemy 60 count 15 lane L
    /// enemy 160 count 70 final
    /// </code>
    /// </summary>
    public static class LevelFile
    {
        /// <summary>
        /// 읽는다. 못 읽으면 <paramref name="error"/> 에 **줄 번호와 함께** 이유가 들어온다 —
        /// *"레벨이 안 열린다"* 만 알면 50 개 중 어느 줄인지 찾는 데 시간이 다 간다.
        /// </summary>
        public static LevelData Parse(string text, out string error)
        {
            error = null;
            var l = new LevelData();
            if (text == null) { error = "내용이 비었다"; return null; }

            var lines = text.Replace("\r\n", "\n").Split('\n');
            for (int n = 0; n < lines.Length; n++)
            {
                string raw = lines[n];
                int hash = raw.IndexOf('#');
                if (hash >= 0) raw = raw.Substring(0, hash);   // 주석은 줄 어디서든
                string line = raw.Trim();
                if (line.Length == 0) continue;

                string why = ParseLine(l, line);
                if (why != null) { error = (n + 1) + "줄: " + why + "  ← \"" + lines[n].Trim() + "\""; return null; }
            }

            // **읽고 나서 z 로 세운다.** `gates` 가 줄을 펼치므로 두 줄을 번갈아 깔면
            // (왼쪽이 좋은 게이트와 오른쪽이 좋은 게이트를 엇갈리게) 적은 순서와 z 순서가
            // 달라진다. `Validate` 와 `Sim` 둘 다 z 오름차순을 전제하므로 여기서 맞춘다.
            // **안정 정렬**이라 같은 z 의 사건은 적은 순서를 지킨다 — 그 순서가 뜻을 가질 수 있다
            // (벽 먼저, 그다음 지역 같은).
            l.events = StableByZ(l.events);

            string bad = l.Validate();
            if (bad != null) { error = bad; return null; }
            return l;
        }

        /// <summary>z 오름차순으로 **안정** 정렬. 같은 z 는 적은 순서를 지킨다</summary>
        static List<LevelEvent> StableByZ(List<LevelEvent> src)
        {
            var idx = new List<int>();
            for (int i = 0; i < src.Count; i++) idx.Add(i);
            idx.Sort((a, b) =>
            {
                int c = src[a].z.CompareTo(src[b].z);
                return c != 0 ? c : a.CompareTo(b);
            });
            var outp = new List<LevelEvent>(src.Count);
            foreach (var i in idx) outp.Add(src[i]);
            return outp;
        }

        static string ParseLine(LevelData l, string line)
        {
            // **글 두 줄은 낱말로 쪼개지 않는다.** 이름에 띄어쓰기가 들어가므로
            // (`name Choryang Alley`) 낱말 단위로 읽으면 첫 낱말만 남는다
            if (line.StartsWith("name ")) { l.name = line.Substring(5).Trim(); return null; }
            if (line.StartsWith("teaches ")) { l.teaches = line.Substring(8).Trim(); return null; }

            var bar = line.Split('|');
            var head = Words(bar[0]);
            if (head.Count == 0) return "빈 줄이 아닌데 낱말이 없다";

            switch (head[0])
            {
                case "chapter": return Int(head, 1, v => l.chapter = v);
                case "index": return Int(head, 1, v => l.index = v);
                case "units": return Int(head, 1, v => l.initialUnits = v);
                case "road": return Num(head, 1, v => l.roadWidth = v);
                case "speed": return Num(head, 1, v => l.forwardSpeed = v);
                case "length": return Num(head, 1, v => l.length = v);

                case "rating":
                {
                    // `rating 12 20 30` — 남은 병력 기준 ☣ / ☣☣ / ☣☣☣
                    if (head.Count < 4) return "rating 에 수가 " + (head.Count - 1) + " 개다 — `rating <☣> <☣☣> <☣☣☣>` 처럼 셋";
                    var r = new int[3];
                    for (int k = 0; k < 3; k++)
                        if (!int.TryParse(head[k + 1], out r[k])) return "rating 의 수가 숫자가 아니다: " + head[k + 1];
                    l.rating = r;
                    return null;
                }

                case "gate":
                {
                    if (bar.Length < 3) return "게이트에 선택지가 " + (bar.Length - 1) + " 개다 — `| L add 30 | R mul 3` 처럼 둘 이상";
                    if (head.Count < 2) return "게이트에 위치(z)가 없다";
                    float z; if (!TryNum(head[1], out z)) return "위치가 숫자가 아니다: " + head[1];
                    float commit = 0f;
                    int ci = head.IndexOf("commit");
                    if (ci >= 0)
                    {
                        if (ci + 1 >= head.Count || !TryNum(head[ci + 1], out commit)) return "`commit` 뒤에 합류 지점(z)이 없다";
                    }
                    var opts = new List<GateOption>();
                    for (int b = 1; b < bar.Length; b++)
                    {
                        var w = Words(bar[b]);
                        if (w.Count < 3) return "선택지는 `<쪽> <연산> <값>` 이다: \"" + bar[b].Trim() + "\"";
                        int lane; if (!TryLane(w[0], out lane) || lane == 0) return "선택지의 쪽은 L 이나 R 이다: " + w[0];
                        GateOp op; if (!TryOp(w[1], out op)) return "연산은 add·mul·sub·div 다: " + w[1];
                        int val; if (!int.TryParse(w[2], out val)) return "값이 숫자가 아니다: " + w[2];
                        opts.Add(new GateOption(lane, op, val));
                    }
                    l.events.Add(LevelEvent.Gate(z, commit, opts.ToArray()));
                    return null;
                }

                case "gates":
                {
                    // **줄지어 선 게이트** — `gates 40..160 every 20 | L add 1 | R add 9`
                    //
                    // 탑워 광고의 핵심 구조다 (`docs/REF_TOPWAR.md` §2②): 한 지점에서 한 번
                    // 고르는 것이 아니라, **복도를 달리는 내내 한쪽에 붙어 있으면 그쪽이 계속
                    // 걸린다.** 그래서 *고르는 순간* 이 아니라 **머무는 시간**이 보상을 정한다.
                    //
                    // ⚠ **간판만 늘리지 않는다.** 여기서 **진짜 게이트 여러 개로 펼친다** —
                    // 화면이 규칙보다 넓게 말하면 선택이 죽는다 (오늘 1-7 지역에서 고친 그것).
                    // 펼친 뒤에는 `Sim` 도 검증기도 **평범한 게이트 여럿**으로 본다. 새 개념이
                    // 들어가지 않는 것이 요점이다.
                    if (bar.Length < 3) return "줄 게이트에 선택지가 " + (bar.Length - 1) + " 개다 — `| L add 1 | R add 9` 처럼 둘 이상";
                    if (head.Count < 2) return "줄 게이트에 구간이 없다 — `gates 40..160 every 20`";
                    var span = head[1].Split(new[] { ".." }, StringSplitOptions.None);
                    float from, to, step;
                    if (span.Length != 2 || !TryNum(span[0], out from) || !TryNum(span[1], out to))
                        return "구간은 `처음..끝` 이다: " + head[1];
                    int ei = head.IndexOf("every");
                    if (ei < 0 || ei + 1 >= head.Count || !TryNum(head[ei + 1], out step))
                        return "`every <간격>` 이 없다";
                    if (step <= 0f) return "간격이 " + step + " 다 — 0 이하면 끝없이 펼쳐진다";
                    if (to < from) return "구간이 거꾸로다 (" + from + ".." + to + ")";
                    if ((to - from) / step > 200f) return "한 줄이 게이트 " + (int)((to - from) / step) + " 개로 펼쳐진다 — 200 개가 상한이다";

                    float commitTo = 0f;
                    int ci2 = head.IndexOf("commit");
                    if (ci2 >= 0 && (ci2 + 1 >= head.Count || !TryNum(head[ci2 + 1], out commitTo)))
                        return "`commit` 뒤에 합류 지점(z)이 없다";

                    var opts2 = new List<GateOption>();
                    for (int b = 1; b < bar.Length; b++)
                    {
                        var w = Words(bar[b]);
                        if (w.Count < 3) return "선택지는 `<쪽> <연산> <값>` 이다: \"" + bar[b].Trim() + "\"";
                        int lane2; if (!TryLane(w[0], out lane2) || lane2 == 0) return "선택지의 쪽은 L 이나 R 이다: " + w[0];
                        GateOp op2; if (!TryOp(w[1], out op2)) return "연산은 add·mul·sub·div 다: " + w[1];
                        int val2; if (!int.TryParse(w[2], out val2)) return "값이 숫자가 아니다: " + w[2];
                        opts2.Add(new GateOption(lane2, op2, val2));
                    }
                    for (float z2 = from; z2 <= to + 0.001f; z2 += step)
                        l.events.Add(LevelEvent.Gate(z2, commitTo, (GateOption[])opts2.ToArray().Clone()));
                    return null;
                }

                case "enemy":
                {
                    float z; int count;
                    string e = Pos(head, out z); if (e != null) return e;
                    if (!TryKeyInt(head, "count", out count)) return "`count <수>` 가 없다";
                    int lane; TryKeyLane(head, out lane);
                    bool final = head.Contains("final");
                    l.events.Add(LevelEvent.Enemy(z, count, final, lane));
                    return null;
                }

                case "enemies":
                {
                    // **끊임없이 오는 적** — `enemies 60..200 every 20 count 15`
                    //
                    // 오너 2026-10-10: *"무수히 많은 적을 막아내는 게 게임의 원칙인데"*.
                    // 지금까지는 적이 **끝에 한 덩어리**였다. 그건 *치우는* 게임이지
                    // *막아내는* 게임이 아니다.
                    //
                    // `gates` 와 같은 수다 — 한 줄이 **진짜 적 무리 여럿**으로 펼쳐진다.
                    // 무리를 작게·촘촘하게 두면 **짧게 여러 번** 부딪히고, 그 리듬이
                    // "밀고 나간다" 가 된다. 크게·드물게 두면 지금처럼 *벽을 치우는* 느낌이다.
                    //
                    // ⚠ 전투 중에는 전진이 멈춘다(`Sim.TickBlocking`). 그래서 무리 크기가
                    // **멈춰 서는 시간**을 정한다 — 15 명이면 1.4 초, 100 명이면 9 초다.
                    if (head.Count < 2) return "줄 적에 구간이 없다 — `enemies 60..200 every 20 count 15`";
                    var sp = head[1].Split(new[] { ".." }, StringSplitOptions.None);
                    float f2, t2, st2;
                    if (sp.Length != 2 || !TryNum(sp[0], out f2) || !TryNum(sp[1], out t2))
                        return "구간은 `처음..끝` 이다: " + head[1];
                    int e2 = head.IndexOf("every");
                    if (e2 < 0 || e2 + 1 >= head.Count || !TryNum(head[e2 + 1], out st2)) return "`every <간격>` 이 없다";
                    if (st2 <= 0f) return "간격이 " + st2 + " 다 — 0 이하면 끝없이 펼쳐진다";
                    if (t2 < f2) return "구간이 거꾸로다 (" + f2 + ".." + t2 + ")";
                    if ((t2 - f2) / st2 > 200f) return "한 줄이 무리 " + (int)((t2 - f2) / st2) + " 개로 펼쳐진다 — 200 개가 상한이다";
                    int cnt2;
                    if (!TryKeyInt(head, "count", out cnt2)) return "`count <수>` 가 없다";
                    if (cnt2 <= 0) return "무리의 수가 " + cnt2 + " 다";
                    int lane3; TryKeyLane(head, out lane3);
                    // **`final` 은 줄에 못 쓴다.** 최종 방어선은 하나여야 한다 — 줄이 전부
                    // 최종이면 첫 무리를 넘는 순간 판이 끝난다
                    if (head.Contains("final")) return "줄 적에는 `final` 을 못 쓴다 — 최종 방어선은 `enemy` 한 줄로 따로 둔다";
                    for (float z3 = f2; z3 <= t2 + 0.001f; z3 += st2)
                        l.events.Add(LevelEvent.Enemy(z3, cnt2, false, lane3));
                    return null;
                }

                case "wall":
                {
                    float z, hp;
                    string e = Pos(head, out z); if (e != null) return e;
                    if (!TryKeyNum(head, "hp", out hp)) return "`hp <수>` 가 없다";
                    int lane; TryKeyLane(head, out lane);
                    l.events.Add(LevelEvent.Wall(z, hp, lane));
                    return null;
                }

                case "narrow":
                {
                    float z, w, len;
                    string e = Pos(head, out z); if (e != null) return e;
                    if (!TryKeyNum(head, "width", out w)) return "`width <수>` 가 없다";
                    if (!TryKeyNum(head, "len", out len)) return "`len <수>` 가 없다";
                    int lane; TryKeyLane(head, out lane);
                    l.events.Add(LevelEvent.Narrow(z, w, len, lane));
                    return null;
                }

                case "zone":
                {
                    float z, dps, len;
                    string e = Pos(head, out z); if (e != null) return e;
                    if (!TryKeyNum(head, "dps", out dps)) return "`dps <수>` 가 없다";
                    if (!TryKeyNum(head, "len", out len)) return "`len <수>` 가 없다";
                    int lane; TryKeyLane(head, out lane);
                    l.events.Add(LevelEvent.Zone(z, dps, len, lane));
                    return null;
                }
            }
            return "모르는 낱말: " + head[0];
        }

        // ── 낱말 다루기 ─────────────────────────────────────────────────────────
        static List<string> Words(string s)
        {
            var outp = new List<string>();
            foreach (var w in s.Split(' ', '\t')) if (w.Length > 0) outp.Add(w);
            return outp;
        }

        static bool TryNum(string s, out float v)
            => float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out v);

        static string Pos(List<string> w, out float z)
        {
            z = 0f;
            if (w.Count < 2) return "위치(z)가 없다";
            return TryNum(w[1], out z) ? null : "위치가 숫자가 아니다: " + w[1];
        }

        static string Int(List<string> w, int at, Action<int> set)
        {
            int v;
            if (w.Count <= at || !int.TryParse(w[at], out v)) return "숫자가 와야 한다";
            set(v); return null;
        }

        static string Num(List<string> w, int at, Action<float> set)
        {
            float v;
            if (w.Count <= at || !TryNum(w[at], out v)) return "숫자가 와야 한다";
            set(v); return null;
        }

        static bool TryKeyNum(List<string> w, string key, out float v)
        {
            v = 0f;
            int i = w.IndexOf(key);
            return i >= 0 && i + 1 < w.Count && TryNum(w[i + 1], out v);
        }

        static bool TryKeyInt(List<string> w, string key, out int v)
        {
            v = 0;
            int i = w.IndexOf(key);
            return i >= 0 && i + 1 < w.Count && int.TryParse(w[i + 1], out v);
        }

        static void TryKeyLane(List<string> w, out int lane)
        {
            lane = 0;
            int i = w.IndexOf("lane");
            if (i >= 0 && i + 1 < w.Count) TryLane(w[i + 1], out lane);
        }

        static bool TryLane(string s, out int lane)
        {
            switch (s.ToUpperInvariant())
            {
                case "L": lane = -1; return true;
                case "R": lane = +1; return true;
                case "BOTH": case "ALL": lane = 0; return true;
            }
            lane = 0; return false;
        }

        static bool TryOp(string s, out GateOp op)
        {
            switch (s.ToLowerInvariant())
            {
                case "add": case "+": op = GateOp.Add; return true;
                case "mul": case "x": case "*": op = GateOp.Multiply; return true;
                case "sub": case "-": op = GateOp.Subtract; return true;
                case "div": case "/": op = GateOp.Divide; return true;
            }
            op = GateOp.Add; return false;
        }
    }
}
