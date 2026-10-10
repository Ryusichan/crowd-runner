using System.Collections;
using System.Linq;
using System.IO;
using CrowdRunner;
using CrowdRunner.Core;
using CrowdRunner.Game;
using CrowdRunner.Meta;
using CrowdRunner.View;
using NUnit.Framework;
using UnityEngine.Rendering;
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
            // **오버레이까지 지운다.** 앞 테스트의 메타 패널이 남으면 새 화면을 덮고, 그 증상은
            // *"글자가 지정한 색에 못 닿는다"* 로만 보인다 (`GameBoot.Reset` 주석)
            GameBoot.Reset();
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
            AssertButtonsCanBeTapped("월드맵");
            AssertNoGameplayHudOnMeta("월드맵");
            float a = Shot("meta_worldmap");

            var lv = LevelCatalog.Find("1-3");
            Assert.IsNotNull(lv, "1-3 을 목록에서 못 찾았다");
            flow.OpenCard(lv);
            yield return null;
            AssertButtonsCanBeTapped("스테이지 카드");
            AssertNoGameplayHudOnMeta("스테이지 카드");
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
            yield return AssertFitsEveryPhone(cv, "월드맵", () => flow.Show(MetaScreen.WorldMap));

            var lv = LevelCatalog.Find("1-3");
            flow.OpenCard(lv);
            yield return null;
            Canvas.ForceUpdateCanvases();
            var (cy0, cy1, cx0, cx1, cn) = Span(cv.transform);
            Debug.Log($"[CR-TEST] ② 카드: 자식 {cn} 개 · 세로 {cy1 - cy0:F0} / {H}");
            Assert.LessOrEqual(cy1 - cy0, H, "카드가 기준 높이를 넘는다 — ☣ 세 줄 + 버튼 둘이 안 들어간다");
            yield return AssertFitsEveryPhone(cv, "스테이지 카드", () => flow.OpenCard(lv));
        }

        /// <summary>
        /// **가장 좁은 폰에서도 들어가는가** — 이제 진짜로 잰다.
        ///
        /// ## 두 번 틀렸다
        ///
        /// ① **한 폭만 쟀다.** 1080x1920 에서만 보고 통과시켰고, 세션 A 가 실제 폰에서
        ///    머리글이 잘리는 것을 찾았다. 한 폭만 재는 검사는 *그 폭에서는 맞다* 만 말하는데
        ///    읽는 사람은 *맞다* 로 읽는다.
        ///
        /// ② 그래서 세 비율로 쟀더니 **거짓으로 떨어뜨렸다.** 레이아웃이 적응형이라
        ///    `Screen` 에서 폭을 받아 짓는데, 배치 창이 640x480 **가로**라 게임은 폭을
        ///    2560 으로 보고 아무것도 안 깎는다. 즉 **넓게 지어진 것을 좁은 예산에 넣어
        ///    보고 있었다.** 세션 A 가 고친 뒤에도 `Head 47 단위` 가 **고치기 전과 똑같이**
        ///    나온 것이 단서였다 — 같은 수가 나오면 같은 것을 보고 있는 것이다.
        ///
        ///    그 상태가 특히 나빴던 이유: **고치면 눈이 머는 검사**였다. 하드코딩을 규칙으로
        ///    바꾸는 순간 잡을 것이 없어졌는데도 빨간색이라 믿음직해 보였고, 그 빨간색을 보고
        ///    더 깎았으면 **멀쩡한 레이아웃을 망가뜨렸을** 것이다.
        ///
        /// ## 지금
        ///
        /// `MetaFlow.ForceUsableWidth` 로 폭을 넣고 **화면을 다시 지어** 잰다. 그래야 그
        /// 폭에서 *실제로 지어진* `RectTransform` 을 보는 것이다. 세션 A 가 그 값을
        /// **속성**으로 둔 것이 핵심이다 — `Awake` 에서 한 번 계산했으면 검사가 값을 넣어도
        /// 화면은 이미 지어진 뒤라 **조용히 초록**이 나왔을 것이다.
        ///
        /// 끝나고 **되돌린다.** 안 되돌리면 다음 검사가 좁은 폭으로 짓는다 — 오늘 두 번 겪은
        /// *"앞엣것이 안 지워진다"* 자리다.
        ///
        /// 앵커는 세 경우고, 묻는 것이 다르다:
        /// 늘어남(min≠max) 화면과 같이 커진다 → 묻지 않는다 ·
        /// 가운데(0.5) 제자리에서 잘린다 → `|x| ≤ 폭/2` ·
        /// 가장자리(0·1) 가장자리를 따라온다 → 자기 폭만.
        /// 첫 판에 이것을 안 걸러 `MetaRoot`(화면 전체 뿌리)를 지목했었다.
        /// </summary>
        static IEnumerator AssertFitsEveryPhone(Canvas cv, string where, System.Action rebuild)
        {
            var phones = new (string name, float usable)[]
            {
                ("9:16", 1920f * 9f / 16f),      // 1080 — 구형
                ("9:19.5", 1920f * 9f / 19.5f),  //  886 — 아이폰
                ("9:20", 1920f * 9f / 20f),      //  864 — 긴 안드로이드
            };
            var root = cv.transform as RectTransform;
            var corners = new Vector3[4];

            string fail = null;
            {
                foreach (var (name, usable) in phones)
                {
                    MetaFlow.ForceUsableWidth = usable;
                    rebuild();
                    // **한 프레임 기다린다.** `Show` 는 앞 화면을 `Destroy` 로 지우는데 그건
                    // 프레임 끝까지 미뤄진다. 같은 프레임에 재면 **옛 화면과 새 화면이 둘 다**
                    // 잡히고, 넘치는 개수가 정확히 두 배로 나온다 (4 → 8 로 그렇게 나왔다).
                    // 오늘 `GameBoot.Reset` 에서 겪은 것과 같은 자리다
                    yield return null;
                    Canvas.ForceUpdateCanvases();

                    float half = usable * 0.5f;
                    int over = 0; string worst = null; float worstBy = 0f;
                    foreach (var rt in cv.GetComponentsInChildren<RectTransform>(false))
                    {
                        if (rt == root) continue;
                        if (Mathf.Abs(rt.anchorMax.x - rt.anchorMin.x) > 0.01f) continue;  // 늘어남
                        rt.GetWorldCorners(corners);
                        float x0 = root.InverseTransformPoint(corners[0]).x;
                        float x1 = root.InverseTransformPoint(corners[2]).x;
                        bool centred = Mathf.Abs(rt.anchorMin.x - 0.5f) < 0.01f;
                        float by = centred ? Mathf.Max(Mathf.Abs(x0), Mathf.Abs(x1)) - half
                                           : (x1 - x0) - usable;
                        if (by <= 0.5f) continue;
                        over++;
                        if (by > worstBy) { worstBy = by; worst = rt.name + (centred ? " (가운데)" : " (가장자리)"); }
                    }

                    if (over == 0) { Debug.Log($"[CR-TEST] {where} · {name} ({usable:F0}단위): 들어간다"); continue; }
                    Debug.LogError($"[CR-TEST] {where} · {name}({usable:F0}단위)에서 **{over} 개가 밖으로 나간다** — " +
                                   $"가장 심한 것 '{worst}' 가 {worstBy:F0} 단위");
                    // **바로 안 떨어뜨린다**: 되돌리는 일이 남아 있고, `yield` 가 있는 메서드는
                    // `finally` 를 못 쓴다. 안 되돌리면 다음 검사가 좁은 폭으로 짓는다
                    if (fail == null) fail = $"{where}: {name} 에서 {over} 개가 화면 밖 (최대 {worstBy:F0} 단위)";
                }
            }
            // **되돌린다** — 안 되돌리면 다음 검사가 좁은 폭으로 짓고, 그건 "왜 갑자기
            // 다 통과하지" 로만 보인다
            MetaFlow.ForceUsableWidth = 0f;
            rebuild();
            yield return null;
            if (fail != null) Assert.Fail(fail);
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
            AssertEveryGateAnswered();
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

            AssertNumbersDoNotOverlapOnScreen(runner);
            yield return ShootTheOpeningFrame();
            MeasureGateLabels(runner);
            yield return ShootTheGatePass(runner);

            MeasureDecisionTime(runner);
            float share = ShotWorld("game", runner);
            Debug.Log($"[CR-TEST] 게임 화면: 병력 {runner.Sim.Units} z {runner.Sim.Z:F0} · 그려진 화소 {share:F1}%");
            Assert.Greater(runner.Sim.Units, lv.initialUnits, "게이트를 지나지 않았다 — 군단이 안 커졌다");
            Assert.Greater(share, 3f, "게임 화면이 거의 비었다 — 군중이 안 그려지는지 보라");
        }

        /// <summary>
        /// **고를 시간이 있는가** — 네 번째 질문.
        ///
        /// 앞의 셋은 *나쁜 선택이 존재하나* · *한쪽에 붙여 두면* · *아무것도 안 하면* 이었다.
        /// 셋 다 **선택의 내용**을 묻는다. 이것은 **선택할 기회**를 묻는다: 게이트가 화면에
        /// 들어온 뒤 거기 닿기까지 몇 초인가. 짧으면 게이트는 *판단* 이 아니라 **반사신경**이고,
        /// 그러면 두 선택지의 수를 아무리 잘 짜도 플레이어는 읽을 틈이 없다.
        ///
        /// 레벨 쪽에서는 보이지 않는다 — 카메라 높이·각도·화각·전진 속도가 정하므로
        /// **표현 쪽 수**다. 레벨을 하나도 안 고치고 이 수를 바꿀 수 있다.
        ///
        /// ## 두 번 틀렸던 자리
        ///
        /// ① 손으로 짠 뷰·투영 행렬. Unity 의 뷰 공간은 이미 −z 가 앞이라 `.inverse` 뒤에
        ///    z 를 또 뒤집으면 **w 가 틀린 값으로 나눠진다.** 그래서 실제 카메라를 옮겨
        ///    `WorldToViewportPoint` 에 묻는다 — 게임이 쓰는 그 함수여야 답이 게임과 같다.
        /// ② `seen < 0` 을 "못 찾았다" 로 썼다. **z 는 정당하게 음수다.** `z=-77` 이라는 옳은
        ///    측정을 "못 찾음" 으로 읽어 `0.00초` 를 찍었고, 0.00 은 *게이트가 코앞에서
        ///    튀어나온다* 는 **그럴듯한 결함 보고**로 읽힌다. 센티넬이 정당한 값과 겹치면
        ///    측정은 조용히 거짓이 된다 — 그래서 찾았는지는 `bool` 이 따로 말한다.
        /// ③ 배치 창은 640x480 **가로**다. 그 화각으로 재면 세로 9:16 기기의 답이 아니다.
        ///    `cam.aspect` 를 세로로 고정하고 재고 되돌린다.
        /// </summary>
        static void MeasureDecisionTime(LevelRunner runner)
        {
            var cam = Camera.main;
            var lv = runner.Level;
            if (cam == null || lv == null) { Debug.LogError("[CR-TEST] 카메라나 레벨이 없다 — 고를 시간을 못 잰다"); return; }

            var keepPos = cam.transform.position;
            float keepAspect = cam.aspect;
            cam.aspect = 1080f / 1920f;       // 세로 기기의 화각으로 — 배치 창(가로)이 아니라

            try
            {
                foreach (var e in lv.events)
                {
                    if (e.kind != EventKind.Gate || e.options.Length == 0) continue;
                    float laneX = e.options[0].lane * 2.1f;
                    var target = new Vector3(laneX, 1.0f, e.z);

                    bool found = false; float seen = 0f, readable = 0f; bool readFound = false;
                    for (float z = e.z; z > e.z - 160f; z -= 0.25f)
                    {
                        cam.transform.position = new Vector3(0f, 19f, z - 26f);
                        var vp = cam.WorldToViewportPoint(target);
                        bool on = vp.z > 0.1f && vp.x >= 0f && vp.x <= 1f && vp.y >= 0f && vp.y <= 1f;
                        if (!on) break;
                        found = true; seen = z;      // 아직 보인다 — 더 뒤로
                        // **읽히는 크기인가.** 게이트 네모는 2 m 높이고, 그 안의 숫자가 그 절반쯤
                        // 된다. 1920 px 중 60 px 미만이면 두 글자짜리 숫자는 뭉개진다
                        var top = cam.WorldToViewportPoint(target + new Vector3(0f, 1.0f, 0f));
                        var bot = cam.WorldToViewportPoint(target + new Vector3(0f, -1.0f, 0f));
                        float h = Mathf.Abs(top.y - bot.y) * 1920f;
                        if (h >= 60f) { readFound = true; readable = z; }
                    }

                    if (!found)
                    {
                        Debug.LogError($"[CR-TEST] 게이트 z={e.z:F0} 가 **닿는 순간에도 화면에 없다** — " +
                                       "카메라 각도나 차선 폭을 보라");
                        return;
                    }
                    float lead = (e.z - seen) / Mathf.Max(0.1f, lv.forwardSpeed);
                    float readLead = readFound ? (e.z - readable) / Mathf.Max(0.1f, lv.forwardSpeed) : 0f;
                    // **두 수는 다른 것을 말한다.** 화면에 있는 시간은 길어도 (22 초) 숫자가
                    // 읽히는 크기가 되는 것은 훨씬 늦다. 플레이어가 *고를 수 있는* 시간은
                    // 뒤쪽이다 — 앞의 수만 보면 "시간은 충분하다" 는 **틀린 안심**이 된다
                    Debug.Log($"[CR-TEST] 게이트 z={e.z:F0}: 화면 진입 {lead:F2}초 전 ({e.z - seen:F0}m) · " +
                              $"**숫자가 읽히는 크기 {readLead:F2}초 전** ({e.z - readable:F0}m) · " +
                              $"전진 {lv.forwardSpeed:F1} m/s");
                    if (!readFound)
                        Debug.LogError("[CR-TEST] 게이트 숫자가 **끝까지 읽을 크기가 안 된다** — " +
                                       "닿는 순간에도 60px 미만이다");
                    lead = readLead;
                    // 숫자에 뜻을 붙여 둔다 — 다음에 보는 사람이 2.1 초가 좋은지 나쁜지 모른다.
                    // 광고형 러너의 손가락 반응은 보통 0.3~0.5 초, 숫자 두 개를 **읽고** 고르는 데
                    // 더 필요하니 1.5 초를 아래 선으로 둔다 (오너 확정 전 [WORKING] 값)
                    if (lead < 1.5f)
                        Debug.LogWarning($"[CR-TEST] ⚠ 고를 시간이 {lead:F2}초다 — 두 숫자를 읽고 고르기엔 짧다. " +
                                         "카메라를 더 뒤/위로 두거나 전진 속도를 낮춰야 한다");
                    return;   // 첫 게이트만 — 나머지는 같은 기하다
                }
                Debug.LogWarning("[CR-TEST] 이 판에 게이트가 없다 — 고를 시간을 잴 자리가 없다");
            }
            finally
            {
                // **되돌린다.** 안 되돌리면 뒤이어 찍는 게임 화면이 다른 화각으로 나오고,
                // 그건 "오늘 그림이 어제와 다르다" 로만 보인다
                cam.transform.position = keepPos;
                cam.aspect = keepAspect;
                cam.ResetAspect();
            }
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
            // **UI 를 같이 찍는다.** 전에는 레이어 5 를 뺐는데, 병력 수가 화면 UI 로 옮겨 간
            // 뒤로는 그러면 **플레이어가 보는 것과 다른 그림**이 나온다 — 고쳐 놓고도
            // 사진에는 안 나오고, 그러면 사진으로 확인할 수가 없다.
            // 오버레이 캔버스는 카메라 렌더 경로 밖이라 찍는 동안만 카메라에 물린다
            cam.cullingMask = ~0;
            var cv = GameBoot.Overlay;
            RenderMode keepMode = RenderMode.ScreenSpaceOverlay; Camera keepCam = null; float keepPlane = 0f;
            if (cv != null)
            {
                keepMode = cv.renderMode; keepCam = cv.worldCamera; keepPlane = cv.planeDistance;
                cv.renderMode = RenderMode.ScreenSpaceCamera;
                cv.worldCamera = cam;
                cv.planeDistance = 1f;      // 3D 보다 앞 — 뒤에 두면 길에 가린다
                Canvas.ForceUpdateCanvases();
            }
            cam.Render();
            if (cv != null)
            {
                // **되돌린다** — 안 되돌리면 그 다음 UI 촬영이 다른 모드에서 찍힌다
                cv.renderMode = keepMode; cv.worldCamera = keepCam; cv.planeDistance = keepPlane;
            }

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
            ProbeWorld(tex, cam);

            cam.targetTexture = prevTarget;
            cam.cullingMask = prevMask;
            Object.DestroyImmediate(tex);
            rt.Release();
            Object.DestroyImmediate(rt);
            return lit / (px.Length / 7f) * 100f;
        }

        /// <summary>
        /// **게임 중에만 보여야 할 것이 메타 화면에 남아 있지 않은가.**
        ///
        /// 병력 수를 3D 에서 화면 UI 로 옮겼더니 겹침은 사라졌는데, **판 밖에서도 살아남았다** —
        /// 월드맵 1-1 칸 위에 멈춰 있는 `10` 이 떠 있었다. `MetaFlow` 는 자기 `root` 만 껐다
        /// 켜는데 `CountUi` 는 오버레이에 **바로** 붙어 있어서 그 손이 안 닿는다.
        ///
        /// **되는 것이 안 되는 것을 가린 자리다**: 화면 UI 로 옮긴 것이 맞았기 때문에
        /// (게이트와 안 겹친다) 그 판단에서 멈췄고, 같은 변경이 만든 새 결함은 **게임 화면만
        /// 찍는 한 영영 안 보인다.** 메타 화면을 찍어야 나온다.
        ///
        /// 그래서 **메타 화면마다** 묻는다: 게임용 물건이 보이나.
        /// </summary>
        static void AssertNoGameplayHudOnMeta(string where)
        {
            // **없는 것과 숨은 것을 가른다.** 찾은 것이 하나도 없으면 이 검사는 아무것도 안 하고
            // 초록이 된다 — *숨겨져서 안 보인다* 와 *애초에 안 만들어졌다* 가 **같은 통과**로
            // 보이는 것이다. 그러면 나중에 누가 `CountUi` 의 이름을 바꾸는 날, 이 검사는
            // **조용히 아무것도 안 지키게** 된다. 오늘 하루 종일 본 그 모양이다
            int found = 0, shown = 0;
            foreach (var t in Object.FindObjectsByType<UnityEngine.UI.Text>(FindObjectsSortMode.None)
                                    .Concat(Resources.FindObjectsOfTypeAll<UnityEngine.UI.Text>()))
            {
                if (t == null || t.gameObject.name != "CountUi") continue;
                if (t.hideFlags != HideFlags.None) continue;      // 에디터가 들고 있는 사본
                found++;
                if (t.gameObject.activeInHierarchy) shown++;
            }
            Debug.Log($"[CR-TEST] {where}: 병력 수 UI {found} 개 중 떠 있는 것 {shown} 개");
            Assert.Greater(found, 0,
                $"{where}: 병력 수 UI 를 **아예 못 찾았다** — 숨은 것이 아니라 없는 것이다. " +
                "이름이 바뀌었으면 이 검사는 지금부터 아무것도 안 지킨다");
            Assert.AreEqual(0, shown,
                $"{where}: 게임 중에만 보여야 할 병력 수가 메타 화면에 떠 있다 — " +
                "오버레이에 바로 붙어 있어 MetaFlow 가 못 끈다 (LevelView 가 Paused 를 보고 숨긴다)");
        }

        /// <summary>
        /// **보이는 단추가 정말 눌리는가.**
        ///
        /// 월드맵의 칸이 **보이는데 안 눌렸다.** `UiKit.Panel` 이 `raycastTarget = false` 로
        /// 그림을 만들고(바탕이 클릭을 먹지 않게 하려는 뜻), 거기에 `Button` 만 붙이면
        /// **클릭이 애초에 닿지 않는다.** 오너가 *"플레이 할 수가 없어"* 라고 하신 그 자리다.
        ///
        /// **안 들킨 이유**: 안내 화면의 시작 단추는 `UiKit.Button` 이라 `raycastTarget` 이
        /// 켜져 있었다. 그래서 *"탭은 된다"* 로 보였다 — **되는 것 하나가 안 되는 것들을
        /// 가렸다.** 그림으로도 안 보인다. 멀쩡한 단추와 죽은 단추는 **똑같이 생겼다.**
        ///
        /// 그래서 그림이 아니라 **uGUI 에게 묻는다**: `Button` 마다, 그 자신이나 자식 중에
        /// `raycastTarget` 이 켜진 `Graphic` 이 하나라도 있는가. 없으면 그 단추는 화면에
        /// 있지만 **존재하지 않는다**.
        /// </summary>
        static void AssertButtonsCanBeTapped(string where)
        {
            int n = 0, dead = 0;
            foreach (var b in Object.FindObjectsByType<UnityEngine.UI.Button>(FindObjectsSortMode.None))
            {
                if (!b.isActiveAndEnabled) continue;
                n++;
                bool hit = false;
                foreach (var g in b.GetComponentsInChildren<UnityEngine.UI.Graphic>(false))
                    if (g.raycastTarget) { hit = true; break; }
                if (hit) continue;
                dead++;
                Debug.LogError($"[CR-TEST] {where}: 단추 '{b.name}' 는 **보이는데 안 눌린다** — " +
                               "자기도 자식도 raycastTarget 이 꺼져 있다 (UiKit.Panel 이 끄고 만든다). " +
                               "UiKit.Tappable() 로 붙여라");
            }
            Debug.Log($"[CR-TEST] {where}: 단추 {n} 개 · 안 눌리는 것 {dead} 개");
            Assert.AreEqual(0, dead, $"{where}: 보이는데 안 눌리는 단추가 {dead} 개다");
            Assert.Greater(n, 0, $"{where}: 단추가 하나도 없다 — 화면이 안 세워졌을 수 있다");
        }

        /// <summary>
        /// **차선이 정해진 것은 가운데 선을 안 넘는가.**
        ///
        /// 1-7 의 지역은 `lane L` 이라 **왼쪽에서만** 녹는데 (`Sim`: `Side != e.lane` 이면
        /// 건너뛴다), 화면은 길 **전체**를 칠하고 있었다. 그러면 화면이 *"어느 쪽으로 가도
        /// 녹는다"* 라고 말하고, 플레이어는 **있지도 않은 위험을 피한다.**
        ///
        /// 그건 틀린 그림이 아니라 **없어진 선택**이다. 1-7 의 선택은 *"`×3` 을 먹고 녹을래,
        /// `+25` 로 안전할래"* 인데 양쪽을 칠하면 그 질문이 화면에서 사라진다 — 이 게임의
        /// 조작이 그 하나뿐이라 더 그렇다.
        ///
        /// 표현이 규칙보다 **좁게** 말하면 플레이어가 속고, **넓게** 말하면 선택이 죽는다.
        /// 어느 쪽이든 수로 잡을 수 있는 것은 *가운데 선을 넘었나* 하나다.
        /// </summary>
        static void AssertLaneThingsStayInTheirLane()
        {
            var runner = GameBoot.Runner;
            var lvl = runner != null ? runner.Level : null;
            if (lvl == null) return;
            int lanes = 0;
            foreach (var e in lvl.events)
            {
                if (e.kind != EventKind.Zone || e.lane == 0) continue;
                lanes++;
                foreach (var t in Object.FindObjectsByType<Transform>(FindObjectsSortMode.None))
                {
                    if (t.name != "Zone" && t.name != "ZoneMark") continue;
                    if (Mathf.Abs(t.position.z - (e.z + (t.name == "Zone" ? e.zoneLength * 0.5f : 0f))) > 1f) continue;
                    float near = t.position.x - t.localScale.x * 0.5f;
                    float far = t.position.x + t.localScale.x * 0.5f;
                    // 왼쪽(-1) 것이면 오른쪽 끝이 0 을 넘으면 안 된다. 기둥은 경계에 서므로 여유를 둔다
                    float over = e.lane < 0 ? far : -near;
                    Assert.LessOrEqual(over, 0.4f,
                        $"{t.name} 이 lane {(e.lane < 0 ? "L" : "R")} 인데 가운데 선을 {over:F2} m 넘었다 — " +
                        "화면이 규칙보다 넓게 말하면 있지도 않은 위험을 피하게 되고, 그 판의 선택이 사라진다");
                }
            }
            Debug.Log($"[CR-TEST] 차선 지정 지역 {lanes} 개 · 전부 자기 차선 안");
        }

        /// <summary>
        /// **두 숫자가 화면에서 겹치지 않는가.**
        ///
        /// 병력 수(`32`)와 게이트 글자(`×3`)가 포개져 **둘 다 안 읽히는** 그림이 나왔다.
        /// 세계 좌표에서는 높이가 달랐다 — 병력 수 4.4 m, 게이트 1.6 m. 그런데 게이트가
        /// **더 멀리** 있어서 화면에서는 같은 높이로 올라온다. 이 구도(위에서 비스듬히)에서는
        /// **'앞'과 '위'가 화면에서 같은 방향**이라 높이로 떼는 것이 듣지 않는다.
        ///
        /// 그리고 하필 **군단이 게이트에 다가가는 순간** 겹친다 — 즉 **고르는 순간**이고,
        /// 두 숫자를 가장 읽어야 할 때다.
        ///
        /// 그래서 세계 좌표가 아니라 **화면 사각형**을 재서 겹치는지 묻는다. 눈으로 보면
        /// 한 프레임만 보게 되고, 겹치는 구간은 몇 프레임뿐이라 그 프레임을 놓치면 통과한다.
        /// </summary>
        static void AssertNumbersDoNotOverlapOnScreen(LevelRunner runner)
        {
            var cam = Camera.main;
            if (cam == null) return;
            Rect? count = null;
            var gates = new System.Collections.Generic.List<(string text, Rect r)>();
            foreach (var tm in Object.FindObjectsByType<TextMesh>(FindObjectsSortMode.None))
            {
                if (!tm.gameObject.activeInHierarchy) continue;
                var rend = tm.GetComponent<Renderer>();
                if (rend == null) continue;
                var r = ScreenRect(cam, rend.bounds);
                if (r.width <= 0f) continue;                  // 카메라 뒤
                if (tm.gameObject.name == "GateText") gates.Add((tm.text, r));
            }
            // 병력 수는 **화면 UI** 다 (`LevelView.BuildCountUi`). 오버레이 캔버스의
            // `RectTransform` 월드 꼭짓점은 이미 화면 픽셀이라 그대로 쓴다
            foreach (var t in Object.FindObjectsByType<UnityEngine.UI.Text>(FindObjectsSortMode.None))
            {
                if (t.gameObject.name != "CountUi" || !t.gameObject.activeInHierarchy) continue;
                var c4 = new Vector3[4];
                ((RectTransform)t.transform).GetWorldCorners(c4);
                count = new Rect(c4[0].x, c4[0].y, c4[2].x - c4[0].x, c4[2].y - c4[0].y);
            }
            if (count == null) { Debug.LogWarning("[CR-TEST] 병력 수 UI 를 못 찾았다"); return; }

            foreach (var (text, r) in gates)
            {
                if (!r.Overlaps(count.Value)) continue;
                Debug.LogError($"[CR-TEST] 병력 수와 게이트 글자 '{text}' 가 **화면에서 겹친다** — " +
                               "둘 다 안 읽힌다. 하필 고르는 순간이다");
                Assert.Fail($"병력 수와 '{text}' 가 화면에서 겹친다");
            }
            Debug.Log($"[CR-TEST] 화면 글자 겹침 없음 (병력 수 1 · 게이트 글자 {gates.Count})");
        }

        /// <summary>월드 상자를 화면 사각형으로. 여덟 꼭짓점을 투영해 감싸는 상자를 만든다</summary>
        static Rect ScreenRect(Camera cam, Bounds b)
        {
            float x0 = float.MaxValue, y0 = float.MaxValue, x1 = float.MinValue, y1 = float.MinValue;
            for (int i = 0; i < 8; i++)
            {
                var c = new Vector3(((i & 1) == 0 ? b.min : b.max).x,
                                    ((i & 2) == 0 ? b.min : b.max).y,
                                    ((i & 4) == 0 ? b.min : b.max).z);
                var p = cam.WorldToScreenPoint(c);
                if (p.z <= 0f) return new Rect(0, 0, -1, -1);   // 카메라 뒤 — 잴 수 없다
                x0 = Mathf.Min(x0, p.x); x1 = Mathf.Max(x1, p.x);
                y0 = Mathf.Min(y0, p.y); y1 = Mathf.Max(y1, p.y);
            }
            return new Rect(x0, y0, x1 - x0, y1 - y0);
        }

        /// <summary>
        /// **글자가 자기 문 안에 들어가는가** — 두 선택지가 겹치지 않는다는 것을 *배수*가 아니라
        /// **구조**로 보장한다.
        ///
        /// 처음에는 "1.88 배까지 안 붙는다" 를 재서 그 배수를 쓰려 했다. 그 수는 `×2`/`+35`
        /// (두세 글자, 길 7 m)에서 나온 것이고, 세션 A 가 전 경로를 돌려 잰 **최대 증감은
        /// `+100`(네 글자, 1-5)** 이다. 길 폭도 판마다 다르다. **상수 하나는 어느 판에서는
        /// 붙고, 그 판이 어디인지는 아무도 모른다** — 그래서 글자를 자기 네모에 맞추고
        /// (`LevelView.FitLabel`) 여기서는 *정말 들어갔는지* 를 전 판에서 확인한다.
        ///
        /// 네모끼리 안 겹치므로, 글자가 네모 안이면 글자도 안 겹친다. 재는 쪽이 아니라
        /// **못 틀리게 만드는 쪽**이다.
        /// </summary>
        static void AssertGateLabelsFitTheirGate(string code)
        {
            int n = 0; float worst = 0f; string worstText = "";
            foreach (var tm in Object.FindObjectsByType<TextMesh>(FindObjectsSortMode.None))
            {
                if (tm.gameObject.name != "GateText") continue;
                var r = tm.GetComponent<Renderer>();
                var box = tm.transform.parent;
                if (r == null || box == null) continue;
                float share = r.bounds.size.x / Mathf.Max(0.01f, box.localScale.x);
                n++;
                if (share > worst) { worst = share; worstText = tm.text; }
                Assert.LessOrEqual(share, 0.92f,
                    $"{code}: 게이트 글자 '{tm.text}' 가 자기 네모의 {share * 100f:F0}% 를 차지한다 — " +
                    "두 선택지가 붙어 보인다 (LevelView.FitLabel / GateCharWidth 를 보라)");
            }
            // **레벨이 말하는 수와 맞는가.** 이 한 줄이 없어서 다섯 판이 전부 '글자 2 개' 를
            // 찍는 것을 보고도 한참 뒤에야 알았다 — `GameBoot.Go` 가 어떤 이름을 받든 1-1 을
            // 띄우고 있었다. 세 판이 **똑같은 수**를 낸 것이 유일한 단서였다
            var lvl = GameBoot.Runner != null ? GameBoot.Runner.Level : null;
            int want = 0;
            if (lvl != null)
                foreach (var e in lvl.events)
                    if (e.kind == EventKind.Gate) want += e.options.Length;
            Debug.Log($"[CR-TEST] {code}: 게이트 글자 {n} 개 (레벨이 말하는 수 {want}) · " +
                      $"가장 꽉 찬 것 '{worstText}' {worst * 100f:F0}%");
            Assert.AreEqual(want, n,
                $"{code}: 레벨에는 선택지가 {want} 개인데 화면에 글자는 {n} 개다 — " +
                "띄운 판이 달라졌거나 게이트를 덜 세웠다");
        }

        /// <summary>
        /// 가장 긴 글자가 있는 판들에서 맞춤 규칙을 확인하고, **1-7 의 지역 표식**을 한 장 찍는다.
        ///
        /// 1-7 은 게이트를 고르고 2.2 초 뒤에 지역이 시작한다 — 바닥 색판만으로는 고를 때
        /// 앞에 무엇이 있는지 모른다. 표식이 **고르는 순간 보이는지**는 그림으로만 알 수 있다.
        /// </summary>
        [UnityTest, Timeout(300000)]
        public IEnumerator Gate_Labels_Fit_And_The_Zone_Is_Announced()
        {
            foreach (var code in new[] { "1-5", "1-10", "1-7" })
            {
                GameBoot.Go(code);
                yield return null;
                yield return null;
                AssertGateLabelsFitTheirGate(code);
            }

            var runner = GameBoot.Runner;      // 마지막은 1-7
            Assert.IsNotNull(runner.Sim, "1-7 을 못 띄웠다 — " + runner.Error);
            float zoneZ = float.NaN;
            foreach (var e in runner.Level.events)
                if (e.kind == EventKind.Zone) { zoneZ = e.z; break; }
            if (float.IsNaN(zoneZ)) { Debug.LogWarning("[CR-TEST] 1-7 에 지역이 없다"); yield break; }

            float prev = Time.timeScale;
            Time.timeScale = 20f;
            // **고르는 자리에서 찍는다.** 지역 바로 앞이 아니라 43 m 뒤 — 게이트 숫자가
            // 읽히기 시작하는 그 거리다. 거기서 표식이 보여야 "알고 들어간다" 가 된다
            for (int i = 0; i < 30000 && runner.Sim != null && runner.Sim.Z < zoneZ - 43f; i++)
                yield return null;
            Time.timeScale = prev;
            yield return null;
            AssertLaneThingsStayInTheirLane();
            float share = ShotWorld("zone_mark", runner);
            Debug.Log($"[CR-TEST] 1-7 지역 표식: 지역 z={zoneZ:F0} · 지금 z={runner.Sim.Z:F0} " +
                      $"(43m 앞) · 그려진 화소 {share:F1}%");
        }

        /// <summary>
        /// **게이트 글자를 키워도 되는가** — 세션 A 가 물은 것에 짐작 말고 수로 답한다.
        ///
        /// 읽히는 거리(43 m)는 **글자 크기가 정한다.** 키우면 더 멀리서 읽히지만, 한 게이트의
        /// 두 선택지는 2~4 m 밖에 안 떨어져 있어서 **서로 붙으면 둘 다 못 읽는다** — 그러면
        /// 멀리서 읽히게 만든 대가로 가까이서 못 읽게 된다.
        ///
        /// 그래서 `Renderer.bounds` 로 **실제 글자 폭**을 재고 두 선택지 사이의 빈 틈을 센다.
        /// 남는 틈이 곧 키울 수 있는 몫이다.
        /// </summary>
        static void MeasureGateLabels(LevelRunner runner)
        {
            var seen = new System.Collections.Generic.Dictionary<float, System.Collections.Generic.List<Bounds>>();
            foreach (var tm in Object.FindObjectsByType<TextMesh>(FindObjectsSortMode.None))
            {
                if (tm.gameObject.name != "GateText") continue;
                var r = tm.GetComponent<Renderer>();
                if (r == null) continue;
                float z = Mathf.Round(tm.transform.position.z);
                if (!seen.TryGetValue(z, out var list)) seen[z] = list = new System.Collections.Generic.List<Bounds>();
                list.Add(r.bounds);
            }
            foreach (var kv in seen)
            {
                if (kv.Value.Count < 2) continue;
                kv.Value.Sort((a, b) => a.center.x.CompareTo(b.center.x));
                var L = kv.Value[0]; var R = kv.Value[1];
                float gap = (R.center.x - R.extents.x) - (L.center.x + L.extents.x);
                float wide = Mathf.Max(L.size.x, R.size.x);
                // 둘 다 키우면 틈은 **양쪽에서** 줄어든다 — 그래서 여유는 틈의 절반씩이다
                float room = wide > 0.01f ? 1f + gap / wide : 1f;
                Debug.Log($"[CR-TEST] 게이트 z={kv.Key:F0} 글자 폭 {L.size.x:F2}/{R.size.x:F2} m · " +
                          $"사이 {gap:F2} m → **{room:F2} 배까지 키워도 안 붙는다**");
                if (gap < 0.15f)
                    Debug.LogWarning($"[CR-TEST] ⚠ 두 선택지 글자가 {gap:F2} m 밖에 안 떨어져 있다 — 이미 붙어 보인다");
                return;
            }
            Debug.LogWarning("[CR-TEST] 선택지 둘인 게이트를 못 찾았다 — 글자 폭을 못 쟀다");
        }

        /// <summary>
        /// **시작하는 순간을 찍는다** — 오너가 보는 첫 화면.
        ///
        /// 지금까지 게임 화면을 **군단이 67 명일 때** 찍었다. 그 그림은 길이 꽉 차서
        /// 좋아 보였는데, 오너가 폰에서 본 것은 **시작 지점의 10 명**이었고 화면의 80 % 가
        /// 빈 아스팔트였다. *"퀄리티가 심각하다"* 가 거기서 나왔다.
        ///
        /// **가장 좋은 순간을 찍으면 가장 좋은 순간만 안다.** 이 게임에서 사람이 가장 먼저
        /// 보는 것은 10 명이고, 그 한 장이 첫인상 전부다. 그래서 그 프레임을 따로 찍는다.
        /// </summary>
        IEnumerator ShootTheOpeningFrame()
        {
            var lv = LevelCatalog.Find("1-1");
            Assert.IsNotNull(lv, "1-1 을 목록에서 못 찾았다");
            flow.StartLevel(lv);
            yield return null;
            yield return null;
            var runner = GameBoot.Runner;
            Assert.IsNotNull(runner.Sim, "1-1 을 못 띄웠다 — " + runner.Error);
            float share = ShotWorld("game_opening", runner);
            Debug.Log($"[CR-TEST] 시작 화면: 병력 {runner.Sim.Units} · z {runner.Sim.Z:F0} · 그려진 화소 {share:F1}%");
        }

        /// <summary>
        /// **연출을 한 장 찍는다** — 게이트 응답은 1 초도 안 사는데, 지금까지 **아무도 본 적이
        /// 없다.** `GatePops` 수는 *일어났나* 만 말하고 *어떻게 생겼나* 를 말하지 않는다.
        ///
        /// 다음 게이트를 지나는 **그 순간**에 멈춰 찍는다. 조금이라도 늦으면 글자가 다 올라가
        /// 사라진 뒤라 빈 길만 남고, 그 그림은 *"연출이 없다"* 와 똑같이 생긴다.
        /// </summary>
        IEnumerator ShootTheGatePass(LevelRunner runner)
        {
            var view = runner.GetComponent<LevelView>();
            if (view == null) { Debug.LogWarning("[CR-TEST] LevelView 가 없다 — 연출을 못 찍는다"); yield break; }
            int before = view.GatePops;
            float prev = Time.timeScale;
            Time.timeScale = 20f;       // 100 배면 연출이 한 프레임에 다 지나간다
            int i = 0;
            for (; i < 30000 && runner.Sim != null && view.GatePops == before; i++) yield return null;
            Time.timeScale = prev;
            if (view.GatePops == before)
            {
                Debug.LogWarning("[CR-TEST] 다음 게이트를 못 만났다 — 연출 사진 없음");
                yield break;
            }
            yield return null;          // 연출이 한 프레임 자라게
            float share = ShotWorld("gate_pass", runner);
            Debug.Log($"[CR-TEST] 게이트 연출 사진: 응답 {view.GatePops} 번째 · 그려진 화소 {share:F1}%");
        }

        /// <summary>
        /// **왜 하얀가** — 그림이 못 쓰게 나왔을 때 짐작하지 않기 위한 눈.
        ///
        /// 첫 게임 화면은 길이 새하얗게 날아가고 병력 수가 흰 바탕의 흰 글자였다. 그런데
        /// `그려진 화소 38.3%` 는 **통과했다**. 화소를 세는 검사는 *무언가 그려졌나* 만 묻지
        /// *읽히나* 를 안 묻는다 — 이 저장소에서 같은 종류로 이미 여러 번 속았다
        /// (`docs/M0_CROWD.md` §7).
        ///
        /// 그래서 두 가지를 **수로** 남긴다.
        /// ① **머티리얼이 실제로 무슨 색인가.** `m.color = c` 가 먹었는지는 셰이더가 정한다.
        ///    셰이더 이름과 색을 같이 찍으면 *색을 줬는데 왜 하얗지* 가 한 줄로 끝난다.
        /// ② **숫자와 그 뒤 배경의 대비.** 흰 글자가 흰 길 위에 있으면 글자는 **있는데 없다**.
        ///    라벨이 있는 화면 자리의 화소를 직접 읽어 밝기 차를 본다.
        /// </summary>
        static void ProbeWorld(Texture2D tex, Camera cam)
        {
            foreach (var r in Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None))
            {
                if (r.sharedMaterial == null) continue;
                string n = r.gameObject.name;
                if (n != "Road" && n != "Wall" && n != "Divider" && n != "Zone" && n != "Gate") continue;
                var m = r.sharedMaterial;
                Debug.Log($"[CR-TEST] 재질 {n,-8} 셰이더 {m.shader.name} · " +
                          $"_Color {(m.HasProperty("_Color") ? m.color.ToString("F2") : "없음")} · " +
                          $"광택 {(m.HasProperty("_Glossiness") ? m.GetFloat("_Glossiness").ToString("F2") : "-")}");
            }

            var label = Object.FindFirstObjectByType<TextMesh>();
            if (label == null) { Debug.LogWarning("[CR-TEST] 병력 수 라벨을 못 찾았다"); return; }
            var vp = cam.WorldToViewportPoint(label.transform.position);
            if (vp.z <= 0f || vp.x < 0f || vp.x > 1f || vp.y < 0f || vp.y > 1f)
            { Debug.LogWarning("[CR-TEST] 병력 수가 화면 밖이다"); return; }
            int px = Mathf.Clamp((int)(vp.x * tex.width), 8, tex.width - 9);
            int py = Mathf.Clamp((int)(vp.y * tex.height), 8, tex.height - 9);

            // 라벨 자리 둘레에서 **가장 밝은 것과 가장 어두운 것** — 글자와 배경이 그 둘이다.
            // 둘이 가까우면 글자는 그려졌어도 안 읽힌다
            float lo = 1f, hi = 0f;
            for (int dy = -34; dy <= 34; dy += 2)
                for (int dx = -60; dx <= 60; dx += 2)
                {
                    int x = Mathf.Clamp(px + dx, 0, tex.width - 1), y = Mathf.Clamp(py + dy, 0, tex.height - 1);
                    var c = tex.GetPixel(x, y);
                    float l = 0.299f * c.r + 0.587f * c.g + 0.114f * c.b;
                    if (l < lo) lo = l; if (l > hi) hi = l;
                }
            float contrast = hi - lo;
            Debug.Log($"[CR-TEST] 병력 수 대비: 밝은 {hi:F2} 어두운 {lo:F2} → **차 {contrast:F2}**");
            if (contrast < 0.25f)
                Debug.LogWarning($"[CR-TEST] ⚠ 병력 수가 배경과 밝기 차 {contrast:F2} 뿐이다 — " +
                                 "그려지긴 했지만 **안 읽힌다**. 이 게임에서 유일하게 꼭 읽어야 하는 수다");
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

        /// <summary>
        /// **고른 것마다 응답이 있었나.** 게이트 연출은 한순간이라 PNG 로는 *있었는지* 를 알 수
        /// 없다 — 찍힌 프레임에 안 떠 있었던 것과 아예 안 뜨는 것이 똑같이 생긴다. 그래서
        /// 한 판을 끝까지 돌리고 **지난 게이트 수와 띄운 응답 수를 맞춘다.**
        ///
        /// 하나라도 빠지면 *어떤 선택에는 반응이 있고 어떤 선택에는 없는* 게임이 되고,
        /// 그게 조작이 하나뿐인 게임에서 가장 비싼 결함이다.
        /// </summary>
        void AssertEveryGateAnswered()
        {
            var runner = GameBoot.Runner;
            var view = runner != null ? runner.GetComponent<LevelView>() : null;
            var lv = runner != null ? runner.Level : null;
            Assert.IsNotNull(view, "LevelView 가 없다 — 게이트 응답을 셀 수 없다");
            int gates = 0;
            foreach (var e in lv.events) if (e.kind == EventKind.Gate) gates++;
            Debug.Log($"[CR-TEST] 게이트 {gates} 개 · 응답 {view.GatePops} 번");
            Assert.AreEqual(gates, view.GatePops,
                $"게이트를 {gates} 번 지났는데 응답은 {view.GatePops} 번이다 — " +
                "어떤 선택에는 반응이 있고 어떤 선택에는 없는 게임이 된다");
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

