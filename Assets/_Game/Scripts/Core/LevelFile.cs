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

            string bad = l.Validate();
            if (bad != null) { error = bad; return null; }
            return l;
        }

        static string ParseLine(LevelData l, string line)
        {
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

                case "wall":
                {
                    float z, hp;
                    string e = Pos(head, out z); if (e != null) return e;
                    if (!TryKeyNum(head, "hp", out hp)) return "`hp <수>` 가 없다";
                    int lane; TryKeyLane(head, out lane);
                    l.events.Add(LevelEvent.Wall(z, hp, lane));
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
