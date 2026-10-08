using UnityEngine;
using UnityEngine.UI;
using CrowdRunner.Core;
using CrowdRunner.UI;

namespace CrowdRunner.Meta
{
    /// <summary>
    /// 화면을 **데이터로 짓는 곳.** 상태는 하나도 들지 않는다 — `MetaSave` 와 `LevelCatalog` 를
    /// 읽어 매번 통째로 다시 짓는다 (`MetaFlow` 규칙 ①).
    /// </summary>
    public partial class MetaFlow
    {
        /// <summary>도시 이름. 좀비퀸과 같은 순서다 (지금은 부산만 있다)</summary>
        static readonly string[] CityNames = { "부산" };

        static string CityName(int city) => city >= 1 && city <= CityNames.Length ? CityNames[city - 1] : "도시 " + city;

        /// <summary>☣ 를 글자로. `Grade.Max` 가 늘면 여기도 따라온다</summary>
        static string Bio(int n)
        {
            var s = "";
            for (int i = 0; i < n; i++) s += "☣";
            return s;
        }

        // ── 월드맵 ───────────────────────────────────────────────────────────────
        void BuildWorldMap()
        {
            var levels = LevelCatalog.City(City);
            int clearedUpTo = MetaSave.ClearedUpTo(City);
            float conquest = MetaSave.Conquest(City);

            var bg = UiKit.Panel("Bg", root, UiKit.Hex(0x1b1426));
            UiKit.Stretch(bg.rectTransform);

            // ── 머리 ──
            var head = UiKit.Rect("Head", root);
            UiKit.Place(head, new Vector2(0.5f, 1f), new Vector2(0f, -150f), new Vector2(980f, 260f));

            var city = UiKit.Label("City", head, CityName(City), UiKit.Display, UiKit.Text, TextAnchor.UpperCenter, true);
            UiKit.Place(city.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, 0f), new Vector2(980f, 96f));

            var sub = UiKit.Label("Sub", head, "좀비화된 도시를 쓸어 간다", UiKit.Caption, UiKit.Dim, TextAnchor.UpperCenter);
            UiKit.Place(sub.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -100f), new Vector2(980f, 44f));

            // 정복도 — **☣ 를 다 모아야 100 %** 다 (`Grade.Conquest`). 깨기만 하면 33 %
            var barBg = UiKit.Panel("BarBg", head, UiKit.Hex(0x2e243a));
            UiKit.Place(barBg.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -156f), new Vector2(760f, 22f));
            UiKit.SetRadius(barBg, 11f, 22f);

            var barFill = UiKit.Panel("BarFill", barBg.rectTransform, UiKit.Purple);
            // ⚠ **앵커가 이미 왼쪽 끝이다.** 처음에 `anchoredPosition` 을 -380 으로 줬는데,
            // `UiKit.Place` 가 `pivot = anchor` 로 두는 것과 겹쳐 막대가 **부모 왼쪽에서 다시
            // 380 px 밖으로** 나갔다. 0 % 라 폭이 0 이어서 화면에는 안 보이는데, 사각형은
            // 거기 있으므로 **레이아웃 폭이 1250 px 로 잡혔다** (세션 B 가 쟀다).
            // 안 보이는 것이 자리를 차지한다.
            barFill.rectTransform.anchorMin = new Vector2(0f, 0.5f);
            barFill.rectTransform.anchorMax = new Vector2(0f, 0.5f);
            barFill.rectTransform.pivot = new Vector2(0f, 0.5f);
            barFill.rectTransform.anchoredPosition = Vector2.zero;
            barFill.rectTransform.sizeDelta = new Vector2(760f * Mathf.Clamp01(conquest), 22f);
            UiKit.SetRadius(barFill, 11f, 22f);

            var pct = UiKit.Label("Pct", head, Mathf.RoundToInt(conquest * 100f) + " % 정복", UiKit.Caption, UiKit.Dim, TextAnchor.UpperCenter);
            UiKit.Place(pct.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -186f), new Vector2(760f, 44f));

            // ── 정복 경로: 아래(1-1) 에서 위(1-10) 로 꺾여 올라간다 ──
            //
            // 목록으로 쌓으면 **설정 화면처럼** 보인다. 꺾인 경로는 아트 없이도 "지도" 로 읽히고,
            // 아래에서 위로 가는 것이 진행 방향과 같아 **다음에 갈 곳이 눈에 먼저 든다.**
            var route = UiKit.Rect("Route", root);
            UiKit.Place(route, new Vector2(0.5f, 0f), new Vector2(0f, 60f), new Vector2(980f, 1480f));

            // 노드 폭 470 · 흔들림 ±110 → 가로 195~885 (1080 안, 양옆 195 여유).
            // 세로는 70 + 9×140 = 1330 이 1480 안에 든다 — 세션 B 가 그림에서 쟀다
            // 글자가 캔버스 기준에 맞춰 2.13 배 커졌으므로(`MatchFontsToCanvas`) 칸도 키운다 —
            // 112 짜리 칸에 53 px 코드 + 37 px 이름을 넣으면 눌린다.
            // 세로: 70 + 9×150 = 1420 (칸 절반 62 를 더해도 1482) · 가로: 195~885 (1080 안)
            const float step = 150f, swing = 220f;
            var pos = new Vector2[levels.Count];
            for (int i = 0; i < levels.Count; i++)
                pos[i] = new Vector2((i % 2 == 0 ? -1f : 1f) * swing * 0.5f, 70f + i * step);

            // 선을 먼저 깔아 노드가 그 위에 온다
            for (int i = 0; i + 1 < levels.Count; i++)
            {
                bool walked = MetaSave.Cleared(levels[i].Code);
                var line = UiKit.Panel("Link" + i, route, walked ? UiKit.Purple : UiKit.Hex(0x3a2f49));
                var a = pos[i];
                var b = pos[i + 1];
                UiKit.Place(line.rectTransform, new Vector2(0.5f, 0f), (a + b) * 0.5f,
                            new Vector2(Vector2.Distance(a, b), 8f));
                line.rectTransform.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(b.y - a.y, b.x - a.x) * Mathf.Rad2Deg);
                UiKit.SetRadius(line, 4f, 8f);
            }

            for (int i = 0; i < levels.Count; i++)
            {
                var lv = levels[i];
                bool open = Grade.Unlocked(lv.index, clearedUpTo);
                bool next = open && !MetaSave.Cleared(lv.Code);
                BuildNode(route, lv, pos[i], open, next, MetaSave.GradeOf(lv.Code), i);
            }

            // 못 읽은 판이 있으면 **아래에 띠로 알린다** — 조용히 빼면 원인을 못 찾는다
            if (LevelCatalog.Broken.Count > 0)
            {
                var warn = UiKit.Panel("Broken", root, UiKit.Red);
                UiKit.Place(warn.rectTransform, new Vector2(0.5f, 0f), new Vector2(0f, 26f), new Vector2(980f, 44f));
                UiKit.SetRadius(warn, 10f, 44f);
                var wt = UiKit.Label("BrokenText", warn.rectTransform,
                                     "못 읽은 판 " + LevelCatalog.Broken.Count + " 개 — 로그를 보라",
                                     UiKit.Caption, UiKit.Hex(0x1b1426), TextAnchor.MiddleCenter, true);
                UiKit.Stretch(wt.rectTransform);
            }
        }

        void BuildNode(RectTransform parent, LevelData lv, Vector2 at, bool open, bool next, int grade, int order)
        {
            var card = UiKit.Capsule("Node" + lv.index,
                                     parent,
                                     next ? UiKit.Hex(0x5a4578) : open ? UiKit.Hex(0x3b2f4d) : UiKit.Hex(0x2f2740),
                                     next ? UiKit.Yellow : open ? UiKit.Hex(0x6b5a85) : UiKit.Hex(0x4b3d60),
                                     next ? 6f : 2f);
            UiKit.Place(card.rectTransform, new Vector2(0.5f, 0f), at, new Vector2(470f, 124f));

            var code = UiKit.Label("Code", card.rectTransform, lv.Code, UiKit.Value,
                                   open ? UiKit.Yellow : UiKit.Dim, TextAnchor.MiddleLeft, true);
            UiKit.Place(code.rectTransform, new Vector2(0f, 1f), new Vector2(22f, -12f), new Vector2(140f, 48f));

            // **잠긴 판도 이름을 보여 준다.** `- - -` 로 가렸더니 열 칸 중 아홉이 빈 줄이 되어
            // 첫 화면이 거의 빈 화면이었다 (세션 B). 이름은 **스포일러가 아니라 지도**다 —
            // 어디로 가는지 보이는 것이 정복 지도의 값이다
            var nm = UiKit.Label("Name", card.rectTransform, lv.name, UiKit.LabelPt,
                                 open ? UiKit.Text : UiKit.Hex(0x8a7aa6), TextAnchor.MiddleLeft);
            UiKit.Place(nm.rectTransform, new Vector2(0f, 1f), new Vector2(22f, -64f), new Vector2(330f, 42f));

            // ☣ 셋 — 받은 것만 채운다
            for (int i = 0; i < Grade.Max; i++)
            {
                var dot = UiKit.Panel("Bio" + i, card.rectTransform, i < grade ? UiKit.Green : UiKit.Hex(0x3f3450));
                dot.sprite = UiKit.Circle;
                UiKit.Place(dot.rectTransform, new Vector2(1f, 1f), new Vector2(-26f - (2 - i) * 38f, -30f), new Vector2(26f, 26f));
            }

            // **다음에 할 판에 표를 붙인다.** 색만으로는 "다음" 이 안 읽힌다 — 한 화면에
            // 열 칸이 있으면 눈이 먼저 갈 곳이 있어야 한다
            if (next)
            {
                var mark = UiKit.Label("Next", card.rectTransform, "▶", UiKit.Value, UiKit.Yellow, TextAnchor.MiddleCenter, true);
                UiKit.Place(mark.rectTransform, new Vector2(0f, 0.5f), new Vector2(-44f, 0f), new Vector2(72f, 72f));
            }

            if (!open)
            {
                var lock0 = UiKit.Label("Lock", card.rectTransform, "잠김", UiKit.Micro, UiKit.Hex(0x7a6b93), TextAnchor.MiddleRight);
                UiKit.Place(lock0.rectTransform, new Vector2(1f, 0f), new Vector2(-24f, 24f), new Vector2(200f, 36f));
                return;
            }

            if (MetaSave.BestOf(lv.Code) > 0)
            {
                var best = UiKit.Label("Best", card.rectTransform, "최고 " + MetaSave.BestOf(lv.Code), UiKit.Micro, UiKit.Dim, TextAnchor.MiddleRight);
                UiKit.Place(best.rectTransform, new Vector2(1f, 0f), new Vector2(-24f, 24f), new Vector2(240f, 36f));
            }

            // 카드 전체가 버튼이다 — 작은 과녁을 겨누게 하면 엄지로 누르기 어렵다
            var btn = card.gameObject.AddComponent<Button>();
            btn.targetGraphic = card;
            var captured = lv;
            btn.onClick.AddListener(delegate { OpenCard(captured); });

            // **"짜잔" 이 있어야 한다** (오너 2026-09-26) — 아래에서부터 차례로 뜬다
            if (!Instant) UiAnim.PopIn(card.rectTransform, Mathf.Min(order, UiAnim.CascadeMax) * UiAnim.CascadeStep);
        }

        // ── 스테이지 카드 ────────────────────────────────────────────────────────
        void BuildStageCard()
        {
            var lv = current;
            if (lv == null) { Show(MetaScreen.WorldMap); return; }

            var scrim = UiKit.Scrim("Scrim", root, 0.7f);
            UiKit.Stretch(scrim.rectTransform);
            var back = scrim.gameObject.AddComponent<Button>();
            back.targetGraphic = scrim;
            back.onClick.AddListener(delegate { Show(MetaScreen.WorldMap); });

            var cardImg = UiKit.Panel("Card", root, UiKit.Hex(0x2a2138));
            var card = cardImg.rectTransform;
            UiKit.Place(card, new Vector2(0.5f, 0.5f), new Vector2(0f, 40f), new Vector2(900f, 1060f));
            UiKit.SetRadius(cardImg, 28f);

            var code = UiKit.Label("Code", card, lv.Code, UiKit.Caption, UiKit.Yellow, TextAnchor.UpperCenter, true);
            UiKit.Place(code.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -40f), new Vector2(820f, 34f));

            var nm = UiKit.Label("Name", card, lv.name, UiKit.Title, UiKit.Text, TextAnchor.UpperCenter, true);
            UiKit.Place(nm.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -82f), new Vector2(820f, 48f));

            var tc = UiKit.Label("Teaches", card, lv.teaches, UiKit.Body, UiKit.Dim, TextAnchor.UpperCenter);
            UiKit.Place(tc.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -140f), new Vector2(820f, 70f));

            // 숫자 셋 — 이 판이 어떤 판인지 한눈에
            int gates = 0, finalEnemies = 0;
            foreach (var e in lv.events)
            {
                if (e.kind == EventKind.Gate) gates++;
                if (e.kind == EventKind.Enemy && e.isFinal) finalEnemies = e.enemyCount;
            }
            Chip(card, 0, "시작 병력", lv.initialUnits.ToString());
            Chip(card, 1, "게이트", gates.ToString());
            Chip(card, 2, "최종 방어선", finalEnemies.ToString());

            // ☣ 기준 — **목표는 숨기지 않는다.** 모르면 다시 할 이유가 생기지 않는다
            var rh = UiKit.Label("RateHead", card, "☣ 기준 (남은 병력)", UiKit.LabelPt, UiKit.Dim, TextAnchor.UpperLeft);
            UiKit.Place(rh.rectTransform, new Vector2(0f, 1f), new Vector2(50f, -420f), new Vector2(500f, 34f));

            int mine = MetaSave.GradeOf(lv.Code);
            for (int i = 0; i < Grade.Max; i++)
            {
                var row = UiKit.Rect("Rate" + i, card);
                UiKit.Place(row, new Vector2(0f, 1f), new Vector2(50f, -466f - i * 62f), new Vector2(800f, 54f));

                var dot = UiKit.Panel("Dot", row, i < mine ? UiKit.Green : UiKit.Hex(0x3f3450));
                dot.sprite = UiKit.Circle;
                UiKit.Place(dot.rectTransform, new Vector2(0f, 0.5f), new Vector2(16f, 0f), new Vector2(26f, 26f));

                string need = lv.rating != null && lv.rating.Length == 3 ? lv.rating[i] + " 이상" : "-";
                var txt = UiKit.Label("Need", row, Bio(i + 1) + "   " + need, UiKit.Body,
                                      i < mine ? UiKit.Text : UiKit.Dim, TextAnchor.MiddleLeft);
                // 동그라미는 x=16, 글자는 x=240 이었다 — 한 줄인데 **224 px 떨어져** 둘로
                // 읽혔다 (세션 B 가 그림에서 봤다). 붙인다
                UiKit.Place(txt.rectTransform, new Vector2(0f, 0.5f), new Vector2(62f, 0f), new Vector2(680f, 40f));
            }

            if (MetaSave.BestOf(lv.Code) > 0)
            {
                var best = UiKit.Label("Best", card, "내 최고 기록 " + MetaSave.BestOf(lv.Code) + " 명", UiKit.Caption, UiKit.Dim, TextAnchor.UpperCenter);
                UiKit.Place(best.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -664f), new Vector2(820f, 34f));
            }

            var startBtn = UiKit.Button("Start", card, "시작", UiKit.Body, UiKit.Purple, UiKit.Hex(0x1b1426),
                                        delegate { StartLevel(lv); });
            UiKit.Place((RectTransform)startBtn.transform, new Vector2(0.5f, 0f), new Vector2(0f, 150f), new Vector2(620f, 112f));

            var backBtn = UiKit.Button("Back", card, "돌아가기", UiKit.Caption, UiKit.Hex(0x3b2f4d), UiKit.Dim,
                                       delegate { Show(MetaScreen.WorldMap); });
            UiKit.Place((RectTransform)backBtn.transform, new Vector2(0.5f, 0f), new Vector2(0f, 62f), new Vector2(620f, 64f));

            if (!Instant) UiAnim.ModalIn(scrim, card);
        }

        void Chip(RectTransform card, int i, string label, string value)
        {
            var chip = UiKit.Panel("Chip" + i, card, UiKit.Hex(0x352a46));
            UiKit.Place(chip.rectTransform, new Vector2(0.5f, 1f), new Vector2((i - 1) * 268f, -290f), new Vector2(252f, 110f));
            UiKit.SetRadius(chip, 16f);

            var v = UiKit.Label("V", chip.rectTransform, value, UiKit.Value, UiKit.Text, TextAnchor.UpperCenter, true);
            UiKit.Place(v.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -16f), new Vector2(230f, 40f));

            // Micro 로 뒀더니 그림에서 **안 읽혔다**. 숫자만 셋 떠 있으면 무슨 숫자인지 모른다
            var l = UiKit.Label("L", chip.rectTransform, label, UiKit.Caption, UiKit.Hex(0xb9b0cf), TextAnchor.UpperCenter);
            UiKit.Place(l.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -60f), new Vector2(230f, 34f));
        }

        // ── 결과 ─────────────────────────────────────────────────────────────────
        //
        // ⚠ 처음에는 내용을 **위에**, 단추를 **아래에** 붙였더니 가운데가 950 px 비었다
        // (세션 B 의 그림). 두 덩어리가 한 화면으로 안 읽히고 *"뭔가 안 떴나"* 로 보인다.
        // 내용은 **화면 가운데 기준**으로 모으고 단추만 아래에 둔다.
        void BuildResult()
        {
            var lv = current;
            var bg = UiKit.Panel("Bg", root, UiKit.Hex(0x1b1426));
            UiKit.Stretch(bg.rectTransform);

            var mid = new Vector2(0.5f, 0.5f);

            var head = UiKit.Label("Head", root, resultWon ? "구역 정복" : "군단 소멸", UiKit.Display,
                                   resultWon ? UiKit.Green : UiKit.Red, TextAnchor.MiddleCenter, true);
            UiKit.Place(head.rectTransform, mid, new Vector2(0f, 330f), new Vector2(900f, 72f));

            var where = UiKit.Label("Where", root, lv != null ? lv.Code + "  " + lv.name : "", UiKit.Body, UiKit.Dim, TextAnchor.MiddleCenter);
            UiKit.Place(where.rectTransform, mid, new Vector2(0f, 262f), new Vector2(900f, 40f));

            // ☣ — 받은 것이 **터지듯** 뜬다 ("얻었다" 가 보여야 한다)
            for (int i = 0; i < Grade.Max; i++)
            {
                var dot = UiKit.Panel("Bio" + i, root, i < resultGrade ? UiKit.Green : UiKit.Hex(0x3f3450));
                dot.sprite = UiKit.Circle;
                UiKit.Place(dot.rectTransform, mid, new Vector2((i - 1) * 118f, 140f), new Vector2(92f, 92f));
                if (i < resultGrade && !Instant) UiAnim.Burst(dot.rectTransform, 0.12f + i * 0.14f);
            }

            var left = UiKit.Label("Left", root, resultWon ? "남은 병력 " + resultRemaining : "최종 방어선을 넘지 못했다",
                                   UiKit.Title, UiKit.Text, TextAnchor.MiddleCenter, true);
            UiKit.Place(left.rectTransform, mid, new Vector2(0f, 10f), new Vector2(900f, 52f));

            // **다음 등급까지 몇 명 부족한지 말해 준다.** 말해 주지 않으면 플레이어는
            // *"더 잘할 수 있었다"* 는 것만 알고 **얼마나** 인지 모른다 — 그러면 다시 하기가
            // 도박이 된다. 숫자가 붙으면 "게이트 하나만 다르게" 가 된다
            if (lv != null && lv.rating != null && lv.rating.Length == 3 && resultGrade < Grade.Max)
            {
                int need = lv.rating[resultGrade] - resultRemaining;
                string more = resultWon && need > 0
                    ? Bio(resultGrade + 1) + " 까지 " + need + " 명"
                    : Bio(resultGrade + 1) + " 기준 " + lv.rating[resultGrade] + " 명";
                var hint = UiKit.Label("Hint", root, more, UiKit.Body, UiKit.Yellow, TextAnchor.MiddleCenter);
                UiKit.Place(hint.rectTransform, mid, new Vector2(0f, -56f), new Vector2(900f, 40f));
            }

            var next = NextLevel();
            bool nextOpen = next != null && Grade.Unlocked(next.index, MetaSave.ClearedUpTo(City));

            if (resultWon && nextOpen)
            {
                var nb = UiKit.Button("Next", root, "다음 구역  " + next.Code, UiKit.Body, UiKit.Purple, UiKit.Hex(0x1b1426),
                                      delegate { OpenCard(next); });
                UiKit.Place((RectTransform)nb.transform, new Vector2(0.5f, 0f), new Vector2(0f, 420f), new Vector2(620f, 112f));
            }

            var rb = UiKit.Button("Retry", root, "다시 하기", UiKit.Body,
                                  resultWon ? UiKit.Hex(0x3b2f4d) : UiKit.Purple,
                                  resultWon ? UiKit.Text : UiKit.Hex(0x1b1426),
                                  delegate { if (lv != null) StartLevel(lv); });
            UiKit.Place((RectTransform)rb.transform, new Vector2(0.5f, 0f), new Vector2(0f, 292f), new Vector2(620f, 104f));

            var mb = UiKit.Button("Map", root, CityName(City) + " 지도", UiKit.Caption, UiKit.Hex(0x2a2138), UiKit.Dim,
                                  delegate { Show(MetaScreen.WorldMap); });
            UiKit.Place((RectTransform)mb.transform, new Vector2(0.5f, 0f), new Vector2(0f, 190f), new Vector2(620f, 76f));
        }

        // ── 처음 한 번: 조작 안내 ────────────────────────────────────────────────
        //
        // 이 게임의 **유일한 조작**이 "손가락을 좌우로 끈다" 다 (기획서 §3.1). 안내가 없으면
        // 플레이어는 가만히 서서 지는 것을 먼저 보고, 더 나쁜 경우 **오른쪽에 붙어 8/10 을
        // 깨면서 고르는 법을 영영 안 배운다** (`DESIGN.md` §3f 가 그 사고를 기록한 자리다).
        void BuildIntro()
        {
            var bg = UiKit.Panel("Bg", root, UiKit.Hex(0x1b1426));
            UiKit.Stretch(bg.rectTransform);

            var head = UiKit.Label("Head", root, "군단을 이끈다", UiKit.Display, UiKit.Text, TextAnchor.UpperCenter, true);
            UiKit.Place(head.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -300f), new Vector2(900f, 70f));

            var sub = UiKit.Label("Sub", root, "좀비화된 부산을 쓸어 간다", UiKit.Caption, UiKit.Dim, TextAnchor.UpperCenter);
            UiKit.Place(sub.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -372f), new Vector2(900f, 34f));

            string[] lines =
            {
                "손가락을 좌우로 끌어 군단을 옮긴다",
                "게이트를 지나면 군단이 늘거나 준다",
                "+10 과 ×3 중 어느 쪽이 큰지는 지금 병력에 따라 다르다",
                "최종 방어선을 넘으면 그 구역을 정복한다",
            };
            for (int i = 0; i < lines.Length; i++)
            {
                var row = UiKit.Panel("Line" + i, root, UiKit.Hex(0x2a2138));
                UiKit.Place(row.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -480f - i * 130f), new Vector2(880f, 112f));
                UiKit.SetRadius(row, 18f);

                var num = UiKit.Label("N", row.rectTransform, (i + 1).ToString(), UiKit.Value, UiKit.Yellow, TextAnchor.MiddleCenter, true);
                UiKit.Place(num.rectTransform, new Vector2(0f, 0.5f), new Vector2(56f, 0f), new Vector2(60f, 60f));

                var txt = UiKit.Label("T", row.rectTransform, lines[i], UiKit.Body, UiKit.Text, TextAnchor.MiddleLeft);
                UiKit.Place(txt.rectTransform, new Vector2(0f, 0.5f), new Vector2(120f, 0f), new Vector2(700f, 80f));

                if (!Instant) UiAnim.PopIn(row.rectTransform, i * UiAnim.CascadeStep * 2f);
            }

            var go = UiKit.Button("Go", root, "시작", UiKit.Body, UiKit.Purple, UiKit.Hex(0x1b1426), delegate
            {
                MetaSave.Data.introSeen = true;
                MetaSave.Save();
                Show(MetaScreen.WorldMap);
            });
            UiKit.Place((RectTransform)go.transform, new Vector2(0.5f, 0f), new Vector2(0f, 260f), new Vector2(620f, 112f));
        }

        // ── 판이 도는 동안 남아 있는 띠 ──────────────────────────────────────────
        void BuildHud()
        {
            var btn = UiKit.Button("Leave", hud, "||", UiKit.Body, UiKit.Hex(0x2a2138), UiKit.Text, delegate
            {
                if (screen == MetaScreen.Playing) Show(MetaScreen.Paused);
            });
            // 왼쪽 위 — 엄지가 닿는 아래쪽은 **조작 영역**이다. 거기 두면 끌다가 눌린다
            UiKit.Place((RectTransform)btn.transform, new Vector2(0f, 1f), new Vector2(96f, -96f), new Vector2(104f, 104f));
        }

        // ── 멈춤 ─────────────────────────────────────────────────────────────────
        void BuildPaused()
        {
            var scrim = UiKit.Scrim("Scrim", root, 0.72f);
            UiKit.Stretch(scrim.rectTransform);

            var cardImg = UiKit.Panel("Card", root, UiKit.Hex(0x2a2138));
            var card = cardImg.rectTransform;
            UiKit.Place(card, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(820f, 560f));
            UiKit.SetRadius(cardImg, 28f);

            var head = UiKit.Label("Head", card, "멈췄다", UiKit.Title, UiKit.Text, TextAnchor.UpperCenter, true);
            UiKit.Place(head.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -48f), new Vector2(740f, 48f));

            var runner = GameBoot.Runner;
            string line = runner != null && runner.Sim != null
                ? "병력 " + runner.Sim.Units + " · " + Mathf.RoundToInt(runner.Sim.Z) + " m"
                : "";
            var sub = UiKit.Label("Sub", card, line, UiKit.Caption, UiKit.Dim, TextAnchor.UpperCenter);
            UiKit.Place(sub.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -104f), new Vector2(740f, 34f));

            var cont = UiKit.Button("Continue", card, "계속", UiKit.Body, UiKit.Purple, UiKit.Hex(0x1b1426),
                                    delegate { Show(MetaScreen.Playing); });
            UiKit.Place((RectTransform)cont.transform, new Vector2(0.5f, 0f), new Vector2(0f, 290f), new Vector2(620f, 104f));

            var again = UiKit.Button("Again", card, "처음부터", UiKit.Body, UiKit.Hex(0x3b2f4d), UiKit.Text,
                                     delegate { if (current != null) StartLevel(current); });
            UiKit.Place((RectTransform)again.transform, new Vector2(0.5f, 0f), new Vector2(0f, 172f), new Vector2(620f, 96f));

            var quit = UiKit.Button("Quit", card, CityName(City) + " 지도로", UiKit.Caption, UiKit.Hex(0x352a46), UiKit.Dim,
                                    delegate { Show(MetaScreen.WorldMap); });
            UiKit.Place((RectTransform)quit.transform, new Vector2(0.5f, 0f), new Vector2(0f, 64f), new Vector2(620f, 76f));

            if (!Instant) UiAnim.ModalIn(scrim, card);
        }

        // ── 레벨을 하나도 못 읽었을 때 ───────────────────────────────────────────
        void ShowBroken()
        {
            var bg = UiKit.Panel("Bg", root, UiKit.Hex(0x1b1426));
            UiKit.Stretch(bg.rectTransform);

            var head = UiKit.Label("Head", root, "레벨을 못 읽었다", UiKit.Title, UiKit.Red, TextAnchor.UpperCenter, true);
            UiKit.Place(head.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -400f), new Vector2(900f, 60f));

            string why = LevelCatalog.Broken.Count > 0 ? LevelCatalog.Broken[0] : "Resources/Levels 가 비었다";
            var w = UiKit.Label("Why", root, why, UiKit.Caption, UiKit.Dim, TextAnchor.UpperCenter);
            UiKit.Place(w.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -470f), new Vector2(900f, 200f));
        }
    }
}
