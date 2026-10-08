using System.Collections;
using System.IO;
using CrowdRunner;
using CrowdRunner.Core;
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

