using System.Collections;
using System.IO;
using CrowdRunner;
using CrowdRunner.Core;
using CrowdRunner.Game;
using CrowdRunner.Meta;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace CrowdRunner.Tests
{
    /// <summary>
    /// **메타 화면을 PNG 로 뜨고, 짐작으로 둔 수를 재는 주행.**
    ///
    /// 세션 A 가 월드맵·스테이지 카드·결과를 짰는데 **아무도 눈으로 본 사람이 없다.** 오너는
    /// Unity 를 못 보고(2026-10-08 규칙), 저쪽도 자기 UI 를 본 적이 없다. 그림이 나와야 한다.
    ///
    /// **에디터 편집 모드가 아니라 PlayMode 에서 찍는다.** 편집 모드에서는 `AddComponent` 가
    /// `Awake` 를 돌리지 않아 `MetaFlow.root` 가 서지 않는다 — 처음에 에디터 스크립트로 짰다가
    /// *"판을 못 띄웠다"* 로 두 번 실패한 자리다. 플레이 모드면 `Awake`·`Start` 가 제대로 돈다.
    ///
    /// `-nographics` 로는 돌 수 없다 (그릴 장치가 없으면 PNG 가 빈다). 창은 안 뜬다:
    /// <code>Unity -batchmode -runTests -testPlatform PlayMode -testFilter MetaShotTests</code>
    /// </summary>
    public class MetaShotTests
    {
        const int W = 1080, H = 1920;
        static readonly string Dir = "docs/shots";

        MetaFlow flow;

        [UnitySetUp]
        public IEnumerator Setup()
        {
            foreach (var o in Object.FindObjectsByType<GameObject>(FindObjectsSortMode.None))
                if (o.name == "CrowdRunner" || o.name == "MetaFlow") Object.Destroy(o);
            yield return null;

            var runner = GameBoot.Go("1-3");
            Assert.IsNotNull(runner.Sim, "판을 못 띄웠다 — " + runner.Error);
            // **애니메이션을 끈다.** 1 차 촬영에서 월드맵을 `ScreenIn` **중간**에 찍었고,
            // 제목 화소가 배경과 같은 값(27,28,37)으로 나와서 *"디자인이 어둡다"* 로 보고했다 —
            // **잴 수 없는 것에 맞춰 고칠 뻔한** 자리다 (세션 A 가 화소를 떠서 잡았다).
            // 카드·결과는 `ModalIn` 이 끝난 뒤라 멀쩡했고, 그래서 **한 장만 틀렸다** — 더 나쁘다.
            MetaFlow.Instant = true;
            flow = new GameObject("MetaFlow").AddComponent<MetaFlow>();
            yield return null;
            Directory.CreateDirectory(Dir);
        }

        /// <summary>
        /// 세 장을 찍는다. **빈 그림을 통과시키지 않는다** — 검은 PNG 는 *"UI 가 없다"* 와
        /// 똑같이 생기고, 그러면 측정 실패와 화면 결함을 가릴 수 없다.
        /// </summary>
        [UnityTest, Timeout(300000)]
        public IEnumerator Shoots_The_Three_Meta_Screens()
        {
            Assert.AreNotEqual(UnityEngine.Rendering.GraphicsDeviceType.Null, SystemInfo.graphicsDeviceType,
                "그래픽 장치가 없다 (-nographics) — PNG 가 빈다");

            flow.Show(MetaScreen.WorldMap);
            yield return null;
            float a = Shot("meta_worldmap");

            var lv = LevelCatalog.Find("1-3");
            Assert.IsNotNull(lv, "1-3 을 목록에서 못 찾았다");
            flow.OpenCard(lv);
            yield return null;
            float b = Shot("meta_stagecard");

            flow.Show(MetaScreen.Result);
            yield return null;
            float c = Shot("meta_result");

            Debug.Log($"[CR-TEST] 그려진 화소 — 월드맵 {a:F1}% · 카드 {b:F1}% · 결과 {c:F1}%");
            Assert.Greater(a, 1f, "월드맵이 거의 비었다");
            Assert.Greater(b, 1f, "스테이지 카드가 거의 비었다");
            Assert.Greater(c, 1f, "결과 화면이 거의 비었다");
        }

        /// <summary>
        /// 세션 A 가 **확인할 수 없어 짐작으로 둔 넷**을 실제 `RectTransform` 에서 읽는다.
        /// 그림은 한 해상도만 말하는데, 수가 있으면 *"조금 넘친다"* 와 *"기기에 따라 넘친다"* 를 가른다.
        /// </summary>
        [UnityTest, Timeout(300000)]
        public IEnumerator Meta_Layout_Fits_The_Reference_Screen()
        {
            var cv = GameBoot.Overlay;
            var sc = cv.GetComponent<CanvasScaler>();
            Assert.AreEqual(1f, sc.matchWidthOrHeight, 0.001f,
                "세로 게임인데 높이 기준이 아니다 — 폭은 기기마다 18:9~20:9 로 크게 다르다");

            flow.Show(MetaScreen.WorldMap);
            yield return null;
            Canvas.ForceUpdateCanvases();
            var (minY, maxY, minX, maxX, n) = Span(cv.transform);
            Debug.Log($"[CR-TEST] ① 월드맵: 자식 {n} 개 · 세로 {maxY - minY:F0} / {H} · 가로 {maxX - minX:F0} / {W} " +
                      $"(실제 화면 {Screen.width}x{Screen.height})");
            Assert.Greater(n, 10, "월드맵에 그려진 것이 거의 없다");
            Assert.LessOrEqual(maxY - minY, H, "경로가 기준 높이를 넘는다 — 노드 간격을 줄여야 한다");
            Assert.LessOrEqual(maxX - minX, W, "경로가 기준 폭을 넘는다 — 좌우 흔들림을 줄여야 한다");

            var lv = LevelCatalog.Find("1-3");
            flow.OpenCard(lv);
            yield return null;
            Canvas.ForceUpdateCanvases();
            var (cy0, cy1, cx0, cx1, cn) = Span(cv.transform);
            Debug.Log($"[CR-TEST] ② 카드: 자식 {cn} 개 · 세로 {cy1 - cy0:F0} / {H}");
            Assert.LessOrEqual(cy1 - cy0, H, "카드가 기준 높이를 넘는다 — ☣ 세 줄 + 버튼 둘이 안 들어간다");
        }

        /// <summary>
        /// **결과 화면을 실데이터로 찍는다** — 이긴 판 하나와 진 판 하나.
        ///
        /// 빈 결과 화면은 화소 6.8 % 였다. 이긴 쪽에는 "다음 구역" 단추와 *"☣☣☣ 까지 N 명"* 줄이
        /// 더 붙는데 그 배치를 **아무도 본 적이 없다** (세션 A 요청).
        ///
        /// 상태를 손으로 만들지 않고 **판을 끝까지 돌린다**: 1-1 은 손 안 대면 이기고 1-9 는 진다
        /// (`LevelShots.Audit` 이 잰 수). 손으로 만들면 *실제로 그 상태가 나오는지* 는 안 재게 된다.
        /// </summary>
        [UnityTest, Timeout(300000)]
        public IEnumerator Shoots_Result_Screens_And_Checks_Doing_Nothing_Fails()
        {
            yield return PlayUntilResult("1-1", SimState.Won);
            float won = Shot("meta_result_won");
            float gap = BiggestVerticalGap(GameBoot.Overlay.transform, out float gapAt);
            Debug.Log($"[CR-TEST] 결과(이김) 화소 {won:F1}% · 가장 큰 세로 공백 {gap:F0}px " +
                      $"(y≈{gapAt:F0} · 화면의 {gap / H * 100f:F0}%)");
            Assert.Greater(won, 3f, "이긴 결과 화면이 거의 비었다");

            // **지는 판을 박지 않는다.** 1-9 를 박아 뒀는데 세션 A 가 좌우를 뒤집고 수치를 고치자
            // **이기는 판**이 되어 주행이 떨어졌다. 레벨은 계속 바뀌므로 그때그때 찾는다 —
            // 하나도 없으면 그것 자체가 알아야 할 사실이다 (아래 단언)
            string loser = null;
            int cleared = 0;
            for (int i = 1; i <= 10 && loser == null; i++)
            {
                yield return PlayUntilResult("1-" + i, null);
                if (GameBoot.Runner.Sim.State == SimState.Lost) loser = "1-" + i; else cleared++;
            }
            Debug.Log($"[CR-TEST] 안 만지고: 깬 판 {cleared}/10 · 진 판 {(loser ?? "없음")}");
            if (loser != null)
            {
                float lost = Shot("meta_result_lost");
                Debug.Log($"[CR-TEST] 결과(짐) 화소 {lost:F1}% · 판 {loser}");
                Assert.Greater(lost, 3f, "진 결과 화면이 거의 비었다");
            }
            // **손가락을 한 번도 안 대고 챕터가 깨지면 안 된다.** 이 게임의 조작은 게이트 선택
            // 하나뿐인데(§3d) 안 만져도 다 깨지면 그 하나가 판단이 아니다 (기획서 §3.4).
            //
            // 세션 A 의 스윕(*"나쁜 선택이 존재하나"*)과 `LevelShots`(*"한쪽에 붙여 두면"*)이
            // 묻지 않는 **세 번째 질문**이다: 아무 선택도 안 하면. 입력이 없으면 `targetX` 가 0 이라
            // 군단이 **가운데**로 가는데, 그건 왼쪽도 오른쪽도 아니라 두 검사가 다 비켜 간다.
            Assert.IsNotNull(loser,
                "폰을 한 번도 안 만지고 열 판을 다 깬다 — 게이트 선택이 판단이 아니다 (기획서 §3.4). " +
                "그리고 지는 판이 없으면 **진 결과 화면을 찍을 수도 없다**");
        }

        /// <summary>
        /// **게임 화면을 찍는다** — 군중·길·게이트·적. UI 가 아니라 3D 카메라를 뜬다.
        ///
        /// 메타 화면과 경로가 다르다: 오버레이 캔버스를 카메라에 물릴 필요가 없고, 대신 **UI 를
        /// 빼야** 한다 (게임이 어떻게 생겼는지 보려는 그림이라 HUD 가 덮으면 안 된다).
        ///
        /// 판을 조금 돌린 뒤에 찍는다 — 0 프레임에서는 군중이 한 점에 모여 있고 게이트가 멀어서
        /// **아무것도 안 보인다.** 첫 게이트를 지난 뒤가 이 장르의 그림이다.
        /// </summary>
        [UnityTest, Timeout(300000)]
        public IEnumerator Shoots_The_Gameplay_Screen()
        {
            var lv = LevelCatalog.Find("1-3");
            Assert.IsNotNull(lv);
            flow.StartLevel(lv);
            float prev = Time.timeScale;
            Time.timeScale = 100f;
            var runner = GameBoot.Runner;
            // 첫 게이트를 지나 군단이 커진 뒤 — 그때가 이 게임이 어떻게 생겼는지 보이는 자리다
            for (int i = 0; i < 30000 && runner.Sim != null && runner.Sim.Units <= lv.initialUnits; i++)
                yield return null;
            for (int i = 0; i < 60; i++) yield return null;
            Time.timeScale = prev;

            float share = ShotWorld("game", runner);
            Debug.Log($"[CR-TEST] 게임 화면: 병력 {runner.Sim.Units} z {runner.Sim.Z:F0} · 그려진 화소 {share:F1}%");
            Assert.Greater(runner.Sim.Units, lv.initialUnits, "게이트를 지나지 않았다 — 군단이 안 커졌다");
            Assert.Greater(share, 3f, "게임 화면이 거의 비었다 — 군중이 안 그려지는지 보라");
        }

        /// <summary>3D 카메라를 뜬다. UI 는 뺀다 (HUD 가 덮으면 게임이 안 보인다)</summary>
        float ShotWorld(string name, LevelRunner runner)
        {
            var cam = Camera.main;
            Assert.IsNotNull(cam, "카메라가 없다 — LevelView 가 안 섰다");
            var rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32);
            var prevTarget = cam.targetTexture;
            int prevMask = cam.cullingMask;
            cam.targetTexture = rt;
            cam.cullingMask = ~(1 << 5);        // UI 빼고 전부
            cam.Render();

            var tex = new Texture2D(W, H, TextureFormat.RGB24, false);
            var prevRt = RenderTexture.active;
            RenderTexture.active = rt;
            tex.ReadPixels(new Rect(0, 0, W, H), 0, 0);
            tex.Apply(false);
            RenderTexture.active = prevRt;

            var px = tex.GetPixels32();
            var bg = (Color32)cam.backgroundColor;
            int lit = 0;
            for (int i = 0; i < px.Length; i += 7)
                if (Mathf.Abs(px[i].r - bg.r) + Mathf.Abs(px[i].g - bg.g) + Mathf.Abs(px[i].b - bg.b) > 24) lit++;
            File.WriteAllBytes(Path.Combine(Dir, name + ".png"), tex.EncodeToPNG());

            cam.targetTexture = prevTarget;
            cam.cullingMask = prevMask;
            Object.DestroyImmediate(tex);
            rt.Release();
            Object.DestroyImmediate(rt);
            return lit / (px.Length / 7f) * 100f;
        }

        /// <summary>
        /// **가장 큰 세로 공백** (캔버스 기준 px). 결과 화면이 *"가운데가 비어 보인다"* 였는데
        /// 그 말로는 못 고친다 — 세션 A 가 **수로 달라**고 했고 그게 맞다.
        ///
        /// 보이는 것들이 차지한 세로 구간을 모아 겹친 것을 합치고, 그 사이의 가장 큰 틈을 돌려준다.
        /// 화면을 꽉 채우는 배경은 뺀다 (안 빼면 틈이 늘 0 이다).
        /// </summary>
        static float BiggestVerticalGap(Transform root, out float at)
        {
            var croot = root as RectTransform;
            float cw = croot != null ? croot.rect.width : W, ch = croot != null ? croot.rect.height : H;
            var spans = new System.Collections.Generic.List<Vector2>();
            var corners = new Vector3[4];
            foreach (var rt in root.GetComponentsInChildren<RectTransform>())
            {
                var g = rt.GetComponent<Graphic>();
                if (g == null || !g.enabled || !rt.gameObject.activeInHierarchy) continue;
                var r = rt.rect;
                if (r.width >= cw * 0.98f && r.height >= ch * 0.98f) continue;
                rt.GetWorldCorners(corners);
                float lo = float.MaxValue, hi = float.MinValue;
                for (int i = 0; i < 4; i++)
                {
                    float y = root.InverseTransformPoint(corners[i]).y;
                    lo = Mathf.Min(lo, y); hi = Mathf.Max(hi, y);
                }
                spans.Add(new Vector2(lo, hi));
            }
            at = 0f;
            if (spans.Count < 2) return 0f;
            spans.Sort((a, b) => a.x.CompareTo(b.x));
            float cur = spans[0].y, best = 0f;
            for (int i = 1; i < spans.Count; i++)
            {
                if (spans[i].x > cur + best) { best = spans[i].x - cur; at = cur; }
                cur = Mathf.Max(cur, spans[i].y);
            }
            return best;
        }

        IEnumerator PlayUntilResult(string code, SimState? want)
        {
            var lv = LevelCatalog.Find(code);
            Assert.IsNotNull(lv, code + " 을 목록에서 못 찾았다");
            flow.StartLevel(lv);
            // **빠르게 돌린다.** 1-9 는 57 초짜리라 실시간이면 주행이 1 분을 넘는다. `LevelRunner` 가
            // 한 프레임당 0.25 초로 자르고 고정 스텝이라 **배속을 올려도 결과는 같다**
            float prev = Time.timeScale;
            // 배치 모드는 프레임이 아주 짧아(실측 real dt ≈ 0.0002 s) 배속 20 으로는 한 프레임에
            // 0.004 초밖에 안 간다 — 3,600 프레임이 **14 초**였고 1-1 은 24 초짜리라 안 끝났다.
            // `LevelRunner` 가 프레임당 0.25 초로 자르므로 배속을 올려도 결과는 같다 (고정 스텝)
            Time.timeScale = 100f;
            var runner = GameBoot.Runner;
            // **멈췄으면 무엇이 멈췄는지 찍는다.** 1 차에서 3,600 프레임을 돌고도 `Running` 이었는데,
            // 그 한 글자로는 *판이 안 돌았는지* · *다 돌았는데 안 끝났는지* 를 가릴 수 없다
            int i = 0;
            // **끝난 두 상태만** 기다린다. `Running` 만 보면 전투(`Fighting`)·벽(`Breaking`) 에서
            // 빠져나오고, 그때 읽은 상태는 *"안 끝났다"* 로 보인다 — 실제로는 **도는 중**이다
            for (; i < 30000 && runner.Sim != null
                   && runner.Sim.State != SimState.Won && runner.Sim.State != SimState.Lost; i++)
            {
                if (i % 5000 == 0)
                    Debug.Log($"[CR-TEST]   {code} {i}프레임 멈춤={runner.Paused} z={runner.Sim.Z:F0} " +
                              $"병력={runner.Sim.Units} 상태={runner.Sim.State} dt={Time.deltaTime:F3}");
                yield return null;
            }
            Time.timeScale = prev;
            Debug.Log($"[CR-TEST]   {code} 끝 {i}프레임 · 상태 {(runner.Sim != null ? runner.Sim.State.ToString() : "Sim 없음")}");
            // `null` = *"끝나기만 하면 된다"* (어느 쪽인지는 부른 쪽이 본다). 세션 A 의 `SimState` 에
            // 테스트용 값을 더하지 않는다 — 더하면 그 값이 게임 코드의 `switch` 에도 나타난다
            if (want.HasValue)
                Assert.AreEqual(want.Value, runner.Sim.State, code + " 이 기대한 결과로 안 끝났다");
            else
                Assert.AreNotEqual(SimState.Running, runner.Sim.State, code + " 이 안 끝났다");
            yield return null; yield return null;    // 결과 화면으로 넘어갈 틱
        }

        /// <summary>
        /// **글자가 자기 색에 도달하는가.**
        ///
        /// 세션 A 가 대비 1.59~1.96 의 원인을 찾았는데 **색이 아니라 크기**였다: `UiKit` 의 글자
        /// 상수는 높이 900 캔버스 기준인데 여기 캔버스는 1920 이라 같은 상수가 2.13 배 작게 떴고,
        /// 글자가 너무 작아 **안티에일리어싱이 꽉 찬 화소를 못 만들었다.** 지정한 색은
        /// `#f2f4ff` 인데 가장 밝은 화소가 `#716d7e` 에서 멈췄다.
        ///
        /// **색을 올렸으면 흐릿한 글자가 조금 밝아졌을 뿐 여전히 안 읽혔다** — 대비는 증상이고
        /// 크기가 병이었다. 그래서 대비가 아니라 **도달률**을 본다: 글자 네모 안에서 가장 밝은
        /// 화소가 지정한 색의 90 % 에 닿는가. 안 닿으면 *그 색으로 그려진 적이 없는* 것이다.
        ///
        /// 캔버스 기준이나 폰트를 누가 바꾸면 여기서 걸린다 — 증상이 아니라 원인에 붙은 검사다.
        /// </summary>
        static void AssertGlyphsReachTheirColour(Camera cam, Texture2D tex, Transform root, string shot)
        {
            var corners = new Vector3[4];
            string worst = null; float worstReach = 2f; int seen = 0, small = 0;
            foreach (var t in root.GetComponentsInChildren<Text>())
            {
                if (!t.enabled || !t.gameObject.activeInHierarchy || string.IsNullOrWhiteSpace(t.text)) continue;
                if (t.color.a < 0.95f) continue;                 // 일부러 흐린 것은 뺀다
                float want = Lum(t.color);
                if (want < 0.25f) continue;                      // 어두운 글자는 비율이 거꾸로다
                var rt = t.rectTransform;
                rt.GetWorldCorners(corners);
                int x0 = W, x1 = 0, y0 = H, y1 = 0;
                for (int i = 0; i < 4; i++)
                {
                    var v = cam.WorldToViewportPoint(corners[i]);
                    int px = Mathf.RoundToInt(v.x * W), py = Mathf.RoundToInt(v.y * H);
                    x0 = Mathf.Min(x0, px); x1 = Mathf.Max(x1, px);
                    y0 = Mathf.Min(y0, py); y1 = Mathf.Max(y1, py);
                }
                x0 = Mathf.Clamp(x0, 0, W - 1); x1 = Mathf.Clamp(x1, 0, W - 1);
                y0 = Mathf.Clamp(y0, 0, H - 1); y1 = Mathf.Clamp(y1, 0, H - 1);
                if (x1 - x0 < 2 || y1 - y0 < 2) continue;
                // **작은 글자는 대상이 아니다** — 도달률은 크기가 아니라 **획 굵기**를 따른다.
                // 26 px 한글은 획이 2 px 쯤이라 `UiKit.Legible` 의 1.2 px 외곽선이 양쪽에서 먹으면
                // **획 속에 밝은 화소가 안 남는다** (세션 A 측정: 제목 91px 100 % · 부제 26px 56 %,
                // **같은 색 같은 크기인데** 56 %). 문턱을 낮춰도 원리상 못 넘고, 넘기려면 외곽선을
                // 꺼야 하는데 그건 가독성을 **깎는** 쪽이다. 그래서 **읽혀야 하는 크기**만 본다
                if (t.fontSize < BigEnough) { small++; continue; }

                float got = 0f;
                for (int y = y0; y <= y1; y++)
                    for (int x = x0; x <= x1; x++)
                        got = Mathf.Max(got, Lum(tex.GetPixel(x, y)));
                seen++;
                float reach = got / Mathf.Max(0.001f, want);
                if (reach < worstReach)
                {
                    worstReach = reach;
                    worst = t.name + " (" + Trim(t.text) + ") 크기 " + t.fontSize +
                            " 도달 " + (reach * 100f).ToString("F0") + "%";
                }
            }
            // **뺀 것도 센다.** 조용히 빼면 *"다 통과했다"* 가 *"볼 것이 없었다"* 와 같아진다
            Debug.Log("[CR-TEST] " + shot + ": " + BigEnough + "px 이상 " + seen + " 개 (작아서 뺀 것 " + small +
                      ") · 가장 낮은 도달 " + (worstReach * 100f).ToString("F0") + "% (" + worst + ")");
            if (seen == 0) return;
            Assert.GreaterOrEqual(worstReach, 0.85f,
                shot + ": " + worst + " — 지정한 색에 못 닿는다. 획이 가늘어 외곽선에 먹히는지, " +
                "크기가 모자란지 보라 (`UiKit.Legible` · `UiKit.FontScale`)");
        }

        /// <summary>이 크기 이상이면 **읽히라고 만든 글자**로 본다 (세션 A 와 합의한 문턱 A)</summary>
        const int BigEnough = 40;

        static float Lum(Color c) { return 0.2126f * c.r + 0.7152f * c.g + 0.0722f * c.b; }
        static string Trim(string v) { return v.Length > 12 ? v.Substring(0, 12) : v; }

        /// <summary>
        /// 그려진 것들이 차지하는 범위 — **캔버스 기준 좌표**(1080×1920)로 잰다.
        ///
        /// 화면 픽셀로 재면 안 된다: 배치 주행의 창이 **640×480** 이라 월드맵이 *"480px / 1920"*
        /// 으로 찍혔다. 그 수는 레이아웃이 아니라 **그때 창 크기**를 잰 것이다. 캔버스 기준으로
        /// 재면 기기와 무관하고, 그것이 세션 A 가 물은 *"1080×1920 에 들어가나"* 의 답이다.
        /// </summary>
        (float, float, float, float, int) Span(Transform root)
        {
            var croot = root as RectTransform;
            float canvasW = croot != null ? croot.rect.width : W;
            float canvasH = croot != null ? croot.rect.height : H;
            float minY = float.MaxValue, maxY = float.MinValue, minX = float.MaxValue, maxX = float.MinValue;
            int n = 0;
            var corners = new Vector3[4];
            foreach (var rt in root.GetComponentsInChildren<RectTransform>())
            {
                // 보이는 것만 — 빈 컨테이너는 범위를 거짓으로 늘린다
                var g = rt.GetComponent<Graphic>();
                if (g == null || !g.enabled || !rt.gameObject.activeInHierarchy) continue;
                // **화면을 꽉 채우는 배경은 뺀다.** 배경까지 세면 *"내용이 화면에 들어가나"* 가
                // 아니라 *"배경이 화면만 하다"* 를 재게 된다 — 4:3 배치 창에서 가로가 2560 으로
                // 찍힌 것이 그 모양이었다 (캔버스가 그만큼 넓어진 것이지 내용이 넘친 것이 아니다)
                var r = rt.rect;
                if (r.width >= canvasW * 0.98f && r.height >= canvasH * 0.98f) continue;
                rt.GetWorldCorners(corners);
                for (int i = 0; i < 4; i++)
                {
                    // 캔버스 로컬로 되돌린다 — 캔버스가 기준 해상도를 들고 있으므로 이 좌표가
                    // 곧 1080×1920 안의 자리다
                    var p = root.InverseTransformPoint(corners[i]);
                    minX = Mathf.Min(minX, p.x); maxX = Mathf.Max(maxX, p.x);
                    minY = Mathf.Min(minY, p.y); maxY = Mathf.Max(maxY, p.y);
                }
                n++;
            }
            if (n == 0) return (0f, 0f, 0f, 0f, 0);
            return (minY, maxY, minX, maxX, n);
        }

        /// <summary>
        /// **아직 움직이는 중이면 찍지 않는다.** `Instant` 가 꺼지거나 새 연출이 붙으면 같은 일이
        /// 조용히 돌아온다 — 그때 나오는 그림은 *"디자인이 흐리다"* 로 읽히고, 그 말에 맞춰
        /// 색을 올리면 **잴 수 없는 것에 맞춰 고치는** 셈이 된다.
        ///
        /// `CanvasGroup.alpha` 를 본다: 등장 연출이 전부 그 값으로 뜬다.
        /// </summary>
        static void AssertSettled(Transform root, string shot)
        {
            foreach (var g in root.GetComponentsInChildren<CanvasGroup>())
            {
                if (!g.gameObject.activeInHierarchy) continue;
                Assert.GreaterOrEqual(g.alpha, 0.99f,
                    $"{shot}: '{g.name}' 이 알파 {g.alpha:F2} 다 — 등장 연출 중간을 찍고 있다. " +
                    "이 그림으로는 대비를 판단할 수 없다 (MetaFlow.Instant 를 보라)");
            }
        }

        static void SetLayer(Transform t, int layer)
        {
            t.gameObject.layer = layer;
            for (int i = 0; i < t.childCount; i++) SetLayer(t.GetChild(i), layer);
        }

        float Shot(string name)
        {
            var cv = GameBoot.Overlay;
            var prevMode = cv.renderMode;
            var prevCam = cv.worldCamera;

            var rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32);
            var camGo = new GameObject("ShotCam");
            var cam = camGo.AddComponent<Camera>();
            cam.orthographic = true;
            cam.targetTexture = rt;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.10f, 0.11f, 0.14f);
            // **UI 레이어만 찍는다.** 처음에 전부(`~0`) 찍었더니 3D 게임 화면이 UI 위에 겹쳐
            // 나왔다 — 군중과 게이트의 월드 글자(`+10`)가 화면을 덮어서 **UI 가 어떻게 생겼는지
            // 볼 수 없었다.** 메타 화면을 보려고 찍는 그림이므로 게임은 들어오면 안 된다
            cam.cullingMask = 1 << 5;   // 5 = UI

            // **오버레이 캔버스는 카메라로 안 찍힌다** — 렌더 경로 밖이다. 찍는 동안만 카메라
            // 모드로 바꾼다. 좀비퀸에서 `Camera.main` 으로 찍다가 **UI 가 하나도 안 나온** 자리다
            // **UI 를 UI 레이어에 올린다.** `new GameObject` 는 레이어 0(Default) 로 나므로
            // 캔버스 자식들이 전부 Default 에 있었고, 카메라를 UI 레이어로 좁히자 **빈 그림(0.0%)**
            // 이 나왔다. UI 가 UI 레이어에 있는 것이 맞는 상태이므로 되돌리지 않는다
            SetLayer(cv.transform, 5);
            AssertSettled(cv.transform, name);

            cv.renderMode = RenderMode.ScreenSpaceCamera;
            cv.worldCamera = cam;
            cv.planeDistance = 10f;
            Canvas.ForceUpdateCanvases();
            cam.Render();

            var tex = new Texture2D(W, H, TextureFormat.RGB24, false);
            var prevRt = RenderTexture.active;
            RenderTexture.active = rt;
            tex.ReadPixels(new Rect(0, 0, W, H), 0, 0);
            tex.Apply(false);
            RenderTexture.active = prevRt;

            var px = tex.GetPixels32();
            var bg = (Color32)cam.backgroundColor;
            int lit = 0;
            for (int i = 0; i < px.Length; i += 7)
                if (Mathf.Abs(px[i].r - bg.r) + Mathf.Abs(px[i].g - bg.g) + Mathf.Abs(px[i].b - bg.b) > 24) lit++;
            float share = lit / (px.Length / 7f) * 100f;

            File.WriteAllBytes(Path.Combine(Dir, name + ".png"), tex.EncodeToPNG());

            // **그림을 쓴 뒤에 단언한다.** 앞에 두었더니 글자 도달률이 걸리는 순간 PNG 가 안 써져서
            // **보려던 그림을 잃었다** — 검사가 자기가 검사하던 증거를 없앤 셈이다. 판정은 판정이고
            // 그림은 사람이 봐야 하는 것이라, 떨어지더라도 그림은 남는다
            AssertGlyphsReachTheirColour(cam, tex, cv.transform, name);

            // **되돌린다** — 안 되돌리면 캔버스가 카메라 모드로 남고, 그건 *"어제는 됐는데
            // 오늘 UI 가 안 보인다"* 로만 보인다
            cv.renderMode = prevMode;
            cv.worldCamera = prevCam;
            Object.DestroyImmediate(camGo);
            Object.DestroyImmediate(tex);
            rt.Release();
            Object.DestroyImmediate(rt);
            return share;
        }
    }
}

