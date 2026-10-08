using System.IO;
using CrowdRunner.Core;
using CrowdRunner.Meta;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace CrowdRunner.EditorTools
{
    /// <summary>
    /// **메타 화면을 PNG 로 뜬다** — 눈으로 봐야 아는 것을 보는 유일한 길.
    ///
    /// 세션 A 가 월드맵·스테이지 카드·결과를 짰는데 **아무도 눈으로 본 사람이 없다.** 오너는
    /// Unity 를 못 보고(2026-10-08 규칙: 모니터에 띄우지 않는다), 배치 주행은 아껴야 한다.
    /// 그래서 **한 번 돌려 세 장을 찍고**, 그 PNG 를 사람이 본다.
    ///
    /// ## 오버레이 캔버스는 카메라로 안 찍힌다
    ///
    /// `ScreenSpaceOverlay` 캔버스는 카메라 렌더 경로 **밖**에 있어서 `RenderTexture` 에 안 담긴다.
    /// 좀비퀸에서 `ClassLayerShots` 가 `Camera.main` 으로 찍다가 **UI 가 하나도 안 나온** 자리다.
    /// 그래서 찍는 동안만 `ScreenSpaceCamera` 로 바꿔 전용 카메라에 물리고, 끝나면 되돌린다.
    ///
    /// **되돌리는 것이 중요하다**: 안 되돌리면 그 프로젝트가 저장되는 순간 캔버스 모드가 바뀌어
    /// 있고, 그건 *"어제는 됐는데 오늘 UI 가 안 보인다"* 로만 보인다.
    ///
    /// 배치 모드로 돈다. **`-nographics` 는 안 된다** — 그릴 장치가 없으면 PNG 가 빈다:
    /// <code>Unity -batchmode -quit -executeMethod CrowdRunner.EditorTools.MetaShots.Shoot</code>
    /// </summary>
    public static class MetaShots
    {
        const int W = 1080, H = 1920;
        static readonly string Dir = "docs/shots";

        [MenuItem("CR/Shoot Meta Screens")]
        public static void Shoot()
        {
            if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null)
            {
                // **빈 PNG 를 내놓지 않는다.** 검은 그림은 "UI 가 안 보인다" 와 똑같이 생겼고,
                // 원인이 측정 쪽이라는 것을 아무도 모른다
                Debug.LogError("[CR] 그래픽 장치가 없다 (-nographics) — 화면을 찍을 수 없다. 그 플래그를 빼고 돌려라");
                if (Application.isBatchMode) EditorApplication.Exit(1);
                return;
            }
            Directory.CreateDirectory(Dir);

            var runner = GameBoot.Go("1-3");
            if (runner == null || runner.Sim == null)
            {
                Debug.LogError("[CR] 판을 못 띄웠다 — 화면을 찍을 수 없다: " + (runner != null ? runner.Error : "runner 없음"));
                if (Application.isBatchMode) EditorApplication.Exit(1);
                return;
            }
            var flow = new GameObject("MetaFlow").AddComponent<MetaFlow>();

            Measure();

            flow.Show(MetaScreen.WorldMap); Shot("meta_worldmap");
            var lv = LevelCatalog.Find("1-3");
            if (lv != null) { flow.OpenCard(lv); Shot("meta_stagecard"); }
            else Debug.LogError("[CR] 1-3 을 목록에서 못 찾았다 — 카드를 못 찍는다");
            flow.Show(MetaScreen.Result); Shot("meta_result");

            Debug.Log("[CR] shots → " + Dir);
        }

        /// <summary>
        /// **세션 A 가 짐작으로 둔 넷을 숫자로 읽는다.** PNG 로도 보지만, 숫자가 있으면
        /// *"조금 넘친다"* 와 *"기기에 따라 넘친다"* 를 가를 수 있다 — 그림은 한 해상도만 말한다.
        /// </summary>
        static void Measure()
        {
            var cv = GameBoot.Overlay;
            var sc = cv.GetComponent<CanvasScaler>();
            Debug.Log($"[CR] 캔버스 기준 {sc.referenceResolution.x}x{sc.referenceResolution.y} " +
                      $"match={sc.matchWidthOrHeight} · 실제 {Screen.width}x{Screen.height}");

            // ① 경로 노드가 화면 높이에 들어가는가 — 10 개 × 간격 + 흔들림
            const float gap = 140f, sway = 95f;
            float need = gap * 9f;
            Debug.Log($"[CR] ① 경로: 노드 10 개 × 간격 {gap} = {need}px (높이 {H}px 중) · " +
                      $"좌우 ±{sway} → 폭 {sway * 2f + 120f}px (폭 {W}px 중) · " +
                      $"{(need < H - 300f ? "들어간다" : "⚠ 넘친다")}");

            // ②③④ 는 실제로 세운 뒤에 재야 한다 — 짐작이 아니라 `RectTransform` 을 읽는다
        }

        static void Shot(string name)
        {
            var cv = GameBoot.Overlay;
            var prevMode = cv.renderMode;
            var prevCam = cv.worldCamera;
            var prevPlane = cv.planeDistance;

            var rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32) { antiAliasing = 1 };
            var camGo = new GameObject("ShotCam");
            var cam = camGo.AddComponent<Camera>();
            cam.orthographic = true;
            cam.targetTexture = rt;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.10f, 0.11f, 0.14f);
            cam.cullingMask = ~0;

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

            // **배경이 아닌 화소를 센다.** 검은 PNG 는 "UI 가 없다" 와 똑같이 생겨서, 그림만
            // 넘기면 *측정이 실패한 것*과 *화면이 비어 있는 것*을 가릴 수 없다. 0 %면 측정 실패다
            var px = tex.GetPixels32();
            var bg = (Color32)cam.backgroundColor;
            int lit = 0;
            for (int i = 0; i < px.Length; i += 7)   // 7 화소마다 — 비율만 알면 된다
                if (Mathf.Abs(px[i].r - bg.r) + Mathf.Abs(px[i].g - bg.g) + Mathf.Abs(px[i].b - bg.b) > 24) lit++;
            float share = lit / (px.Length / 7f) * 100f;

            var bytes = tex.EncodeToPNG();
            File.WriteAllBytes(Path.Combine(Dir, name + ".png"), bytes);

            // **되돌린다** — 안 되돌리면 캔버스가 카메라 모드로 저장되고, 그건
            // *"어제는 됐는데 오늘 UI 가 안 보인다"* 로만 보인다
            cv.renderMode = prevMode;
            cv.worldCamera = prevCam;
            cv.planeDistance = prevPlane;
            Object.DestroyImmediate(camGo);
            Object.DestroyImmediate(tex);
            rt.Release();
            Object.DestroyImmediate(rt);

            // **들어 있는 것이 있는지 센다.** 검은 PNG 는 "UI 가 없다" 와 똑같이 생긴다 —
            // 그래서 배경색과 다른 화소의 비율을 같이 찍는다. 0 %면 아무것도 안 찍힌 것이다
            Debug.Log($"[CR] shot {name}.png {bytes.Length / 1024}KB · 그려진 화소 {share:F1}%" +
                      (share < 1f ? "  ⚠ **거의 빈 그림이다** — 측정이 실패했는지 보라" : ""));
        }
    }
}
