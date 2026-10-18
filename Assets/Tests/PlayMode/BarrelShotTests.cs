using System.Collections;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using CrowdFire.Core;
using CrowdFire.Game;
using CrowdFire.Meta;

namespace CrowdFire.Tests
{
    /// <summary>
    /// **1-1 을 찍는다.** 오너의 완료 기준이 숫자가 아니라 *"화면을 보았을 때 수백 명의 병사가
    /// 실제로 전투하는 게임으로 느껴지는가"* 라서, 통이 화면에 어떻게 나오는지는 **사진으로만**
    /// 확인할 수 있다.
    ///
    /// `MetaShotTests` 는 1-3 을 찍는데 그 판에는 통이 없다 — 그래서 따로 둔다.
    /// 그 파일은 세션 B 것이라 건드리지 않았다 (`HANDOFF` §0).
    ///
    /// <code>Unity -batchmode -runTests -testPlatform PlayMode -testFilter BarrelShotTests</code>
    /// </summary>
    public class BarrelShotTests
    {
        const int W = 540, H = 960;
        static string Dir => Path.Combine(Directory.GetCurrentDirectory(), "docs/shots");

        [UnityTearDown]
        public IEnumerator Down()
        {
            GameBoot.Reset();
            yield return null;
        }

        [UnityTest]
        public IEnumerator Shoots_1_1_Barrels()
        {
            var lv = LevelCatalog.Find("1-1");
            Assert.IsNotNull(lv, "1-1 을 못 찾았다");
            var play = GameBoot.Go("1-1");
            Assert.IsNotNull(play.Sim, "판을 못 띄웠다 — " + play.Error);
            Directory.CreateDirectory(Dir);

            float prev = Time.timeScale;
            Time.timeScale = 40f;

            // 찍을 자리 넷 — 통을 만나기 직전 · 쏘는 중 · 깬 직후 · 최종 방어선
            float[] marks = { 36f, 48f, 108f, 173f, 234f, 250f, 258f };
            int next = 0;
            int guard = 0;
            while (play.Sim != null && next < marks.Length && guard++ < 200000)
            {
                play.TargetX = BiggestBarrelLane(play.Sim) * play.Sim.RoadWidth * 0.25f;
                yield return null;
                if (play.Sim.Z >= marks[next])
                {
                    Shot("barrel_" + next + "_z" + Mathf.RoundToInt(play.Sim.Z), play);
                    var c0 = Camera.main;
                    Debug.Log($"[CR-TEST] cam {(c0 != null ? c0.transform.position.ToString("F0") : "none")}"
                            + $" · 그린 아군 {Mathf.Min(play.Sim.Battle.AllyCount, 400)} · 적개체 {play.Sim.Battle.EnemyCount}");
                    Debug.Log($"[CR-TEST] z {play.Sim.Z:F0} · 병력 {play.Sim.Units} · 통 {play.Sim.Battle.BarrelCount}"
                            + $" · 번 것 {play.Sim.BarrelGained} · 놓친 통 {play.Sim.BarrelsMissed}"
                            + $" · 적 {play.Sim.Battle.Alive} · 지운 적 {play.Sim.EnemiesKilled:F0}");
                    next++;
                }
            }
            Time.timeScale = prev;
            Assert.Greater(play.Sim.BarrelGained, 0, "통을 하나도 못 깼다 — 그러면 찍을 것이 없다");
        }

        /// <summary>그 순간 가장 큰 통이 있는 쪽. 사람이 하는 선택을 흉내 낸 것뿐이다</summary>
        static int BiggestBarrelLane(Sim sim)
        {
            var b = sim.Battle;
            int lane = 0; float best = 0f;
            for (int i = 0; i < b.BarrelCount; i++)
            {
                if (b.RHp[i] <= 0f || b.RZ[i] < sim.Z) continue;
                if (b.RGives[i] <= best) continue;
                best = b.RGives[i];
                lane = b.RX[i] < 0f ? -1 : +1;
            }
            return lane;
        }

        static void Shot(string name, LevelPlay play)
        {
            var cam = Camera.main;
            Assert.IsNotNull(cam, "카메라가 없다 — LevelView 가 안 섰다");
            var rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32);
            var keepTarget = cam.targetTexture;
            int keepMask = cam.cullingMask;
            cam.targetTexture = rt;
            cam.cullingMask = ~0;
            cam.Render();
            var prevActive = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(W, H, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, W, H), 0, 0);
            tex.Apply();
            RenderTexture.active = prevActive;
            cam.targetTexture = keepTarget;
            cam.cullingMask = keepMask;
            File.WriteAllBytes(Path.Combine(Dir, name + ".png"), tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            rt.Release();
            Object.DestroyImmediate(rt);
        }
    }
}
