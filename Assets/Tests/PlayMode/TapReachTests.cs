using System.Collections;
using System.Collections.Generic;
using System.Text;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using UnityEngine.TestTools;
using CrowdFire.Game;
using CrowdFire.Meta;

namespace CrowdFire.Tests
{
    /// <summary>
    /// **월드맵의 1-1 칸이 안 눌린다.** 웹에 올린 빌드를 터치로도 마우스로도 눌러 봤는데
    /// 인트로의 "시작" 은 눌리고 그 칸만 안 된다 (2026-10-18). 같은 증상이 2026-10-09 에도
    /// 났고 그때는 `UiKit.Panel` 의 `raycastTarget = false` 가 원인이었다.
    ///
    /// 그래서 **추측 대신 그 자리를 레이캐스트한다** — 그 좌표에서 맨 위에 잡히는 것이
    /// 무엇인지 찍으면 "누가 덮고 있나" 가 이름으로 나온다.
    /// </summary>
    public class TapReachTests
    {
        [UnityTearDown]
        public IEnumerator Down()
        {
            GameBoot.Reset();
            yield return null;
        }

        [UnityTest]
        public IEnumerator The_World_Map_Tile_Is_Actually_Reachable()
        {
            GameBoot.Go("1-1");
            yield return null;
            var flow = Object.FindFirstObjectByType<MetaFlow>();
            Assert.IsNotNull(flow, "MetaFlow 가 없다");
            flow.Show(MetaScreen.WorldMap);
            for (int i = 0; i < 5; i++) yield return null;

            var es = Object.FindFirstObjectByType<EventSystem>();
            Assert.IsNotNull(es, "EventSystem 이 없다 — 그러면 어떤 단추도 안 눌린다");

            // 화면에 있는 단추를 전부 적는다
            var buttons = Object.FindObjectsByType<Button>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            var sb = new StringBuilder();
            Button target = null;
            foreach (var b in buttons)
            {
                var rt = b.transform as RectTransform;
                if (rt == null) continue;
                var label = b.GetComponentInChildren<Text>();
                string name = label != null ? label.text : b.name;
                sb.Append($"\n  · {b.name} [{name}] 화면 {Screen.width}x{Screen.height}"
                        + $" 중심 {RectTransformUtility.WorldToScreenPoint(null, rt.position)}"
                        + $" raycast={(b.targetGraphic != null ? b.targetGraphic.raycastTarget.ToString() : "타깃없음")}"
                        + $" 켜짐={b.IsInteractable()}");
                if (b.name.Contains("Card") || (label != null && label.text.Contains("1-1"))) target = b;
            }
            Debug.Log("[CR-TEST] 단추 " + buttons.Length + " 개" + sb);

            Assert.IsNotNull(target, "월드맵에서 1-1 칸에 해당하는 단추를 못 찾았다");

            // 그 자리를 실제로 쏴 본다 — 맨 위에 잡히는 것이 그 칸인가
            var trt = (RectTransform)target.transform;
            Vector2 at = RectTransformUtility.WorldToScreenPoint(null, trt.position);
            var ped = new PointerEventData(es) { position = at };
            var hits = new List<RaycastResult>();
            es.RaycastAll(ped, hits);

            var who = new StringBuilder();
            foreach (var h in hits) who.Append("\n  · " + Path(h.gameObject));
            Debug.Log($"[CR-TEST] {at} 에서 잡힌 것 {hits.Count} 개{who}");

            Assert.Greater(hits.Count, 0, "그 자리에 아무것도 안 잡힌다 — 레이캐스트가 통째로 막혀 있다");
            bool topIsTarget = hits[0].gameObject == target.gameObject
                            || hits[0].gameObject.transform.IsChildOf(target.transform);
            Assert.IsTrue(topIsTarget,
                "1-1 칸 위에 다른 것이 덮여 있다 — 맨 위: " + Path(hits[0].gameObject));
        }

        static string Path(GameObject go)
        {
            var sb = new StringBuilder(go.name);
            var t = go.transform.parent;
            while (t != null) { sb.Insert(0, t.name + "/"); t = t.parent; }
            return sb.ToString();
        }
    }
}
