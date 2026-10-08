using System.IO;
using CrowdRunner.Core;
using UnityEditor;
using UnityEngine;

namespace CrowdRunner.EditorTools
{
    /// <summary>
    /// **열 판이 실제로 뜨는지 숫자로 본다** — 에디터 창도, 폰도 없이.
    ///
    /// 컴파일이 통과한 것은 아무것도 보장하지 않는다. 이 저장소에서 그걸 세 번 배웠다
    /// (`docs/M0_CROWD.md` §7). 그래서 열 판을 **하나씩 띄워** 레벨이 읽히는지 · 사건이 몇 개인지 ·
    /// 길 폭과 시작 병력이 얼마인지를 찍는다. 뜨지 않는 판이 있으면 **이름과 이유**가 나온다.
    ///
    /// `Sim` 은 Unity 없이도 도므로(`tools/sim`) 여기서 새로 재는 것은 **레벨 파일이 실제로
    /// `Resources` 에 들어갔는지** 와 **`LevelRunner.Load` 의 문이 열리는지** 다 — 둘 다
    /// `tools/sim` 이 못 보는 자리다 (저쪽은 파일을 직접 읽는다).
    ///
    /// 배치 모드로 돈다 (오너 규칙: 에디터 창을 띄우지 않는다):
    /// <code>Unity -batchmode -nographics -quit -executeMethod CrowdRunner.EditorTools.LevelShots.Audit</code>
    /// </summary>
    public static class LevelShots
    {
        [MenuItem("CR/Audit Chapter 1")]
        public static void Audit()
        {
            int ok = 0, bad = 0, wonR = 0, wonL = 0;
            for (int i = 1; i <= 10; i++)
            {
                string name = "1-" + i;
                var asset = Resources.Load<TextAsset>("Levels/" + name);
                if (asset == null)
                {
                    Debug.LogError($"[CR] {name}: Resources/Levels 에 없다 — 파일이 그 폴더 밖인지 보라");
                    bad++; continue;
                }
                string why;
                var level = LevelFile.Parse(asset.text, out why);
                if (level == null)
                {
                    Debug.LogError($"[CR] {name}: 레벨 글이 틀렸다 — {why}");
                    bad++; continue;
                }

                // 판을 **끝까지 돌려** 본다 — 뜨는 것과 끝나는 것은 다르다.
                // 아무 입력도 주지 않는다(`desiredX = 0`): 가만히 두면 어떻게 끝나는지가
                // 그 판의 **바탕**이고, 바탕이 이미 클리어면 선택이 없는 판이다
                int gates = 0, zonesN = 0, walls = 0, enemies = 0;
                foreach (var e in level.events)
                {
                    if (e.kind == EventKind.Gate) gates++;
                    else if (e.kind == EventKind.Zone) zonesN++;
                    else if (e.kind == EventKind.Wall) walls++;
                    else if (e.kind == EventKind.Enemy) enemies++;
                }
                // **손을 안 대면 어떻게 되는가** — 양쪽 극단을 둘 다 본다.
                //
                // `desiredX = 0` 은 *가만히 둔 것*이다 (`Sim.Side` 가 X≥0 에서 +1 이라 늘 오른쪽).
                // 이것이 이기면 **플레이어가 폰을 내려놔도 깬다** — 설계가 금지한 자리다.
                // 왼쪽 고정도 같이 재는 이유: 한쪽만 이기면 *"기본값이 정답"* 이고 둘 다 이기면
                // **난이도가 아예 없다.** 그 둘은 뜻이 다르고 고치는 자리도 다르다.
                var (sR, stR) = Run(level, +1f);
                var (sL, stL) = Run(level, -1f);

                Debug.Log($"[CR] {name,-5} 시작 {level.initialUnits,3} · 길 {level.roadWidth,4:F1}m · " +
                          $"사건 {level.events.Count,2} (게이트 {gates} 적 {enemies} 벽 {walls} 지역 {zonesN}) · " +
                          $"가만히(오른쪽) {sR.State} 잔여 {sR.Units,3} 최고 {sR.PeakUnits,3} {stR / 60f:F0}s · " +
                          $"왼쪽고정 {sL.State} 잔여 {sL.Units,3} 최고 {sL.PeakUnits,3} {stL / 60f:F0}s");
                if (sR.State == SimState.Won) wonR++;
                if (sL.State == SimState.Won) wonL++;
                ok++;
            }
            Debug.Log($"[CR] audit: 통과 {ok} · 실패 {bad} · " +
                      $"오른쪽고정 클리어 {wonR}/{ok} · 왼쪽고정 클리어 {wonL}/{ok}");
            // **고정 전략 하나가 챕터를 깨면 조작이 없는 것이다.**
            //
            // 이 게임의 조작은 게이트 선택 하나뿐이다 (`DESIGN.md` §3d). 손가락을 한쪽에 붙여 둔
            // 채로 전부 클리어된다면 그 하나가 판단이 아니라 **방향 하나**이고, 플레이어가
            // *고를 것*이 없다 — 기획서 §3.4 가 금지한 자리다.
            //
            // 떨어뜨리지 않고 **크게 적는다**: 어느 판을 어떻게 고칠지는 레벨 쪽 판단이고(세션 A),
            // 여기서 할 수 있는 것은 *그 사실이 눈에 걸리게 만드는 것*이다. 조용한 경고는 안 읽힌다.
            if (wonR >= ok || wonL >= ok)
                Debug.LogWarning($"[CR] ⚠ **고정 전략이 챕터를 깬다** — " +
                                 $"{(wonL >= ok ? "왼쪽" : "오른쪽")}으로만 붙여 두면 {ok}/{ok} 클리어다. " +
                                 $"게이트 선택이 판단이 아니라 방향 하나가 된다 (기획서 §3.4)");
            if (bad > 0 && Application.isBatchMode) EditorApplication.Exit(1);
        }

        /// <summary>한 판을 끝까지 돌린다. `dir` 은 손가락을 그쪽 끝으로 붙여 둔 것 (−1 왼쪽 · +1 오른쪽)</summary>
        static (Sim, int) Run(LevelData level, float dir)
        {
            var sim = new Sim(level);
            int steps = 0;
            while (sim.State != SimState.Won && sim.State != SimState.Lost && steps < 60 * 60 * 5)
            {
                // 길 폭의 절반까지 — 시뮬이 알아서 자른다 (갇힌 구간에서는 반쪽으로)
                sim.Step(dir * level.roadWidth);
                steps++;
            }
            return (sim, steps);
        }
    }
}
