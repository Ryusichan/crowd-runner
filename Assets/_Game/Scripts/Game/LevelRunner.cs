using UnityEngine;
using CrowdRunner.Core;

namespace CrowdRunner.Game
{
    /// <summary>
    /// **로직과 화면을 잇는 한 곳.** 레벨을 읽고, 고정 스텝으로 `Sim` 을 돌리고, 손가락을 받는다.
    ///
    /// 여기 말고 어디서도 `Sim` 을 돌리지 않는다. 둘이 돌리면 **한 프레임에 두 번 전진**하고,
    /// 그건 "가끔 빨라진다" 로만 보여서 찾기 어렵다.
    ///
    /// **`Sim` 은 `Core/` 에 있고 `UnityEngine` 을 모른다** (`docs/DESIGN.md` §2). 그래서 이 파일이
    /// 그 경계다 — 위로는 Unity, 아래로는 순수 C#. 군중을 어떻게 그리든(M0 방식 A/B/추상화)
    /// 이 아래는 안 바뀐다.
    /// </summary>
    public class LevelRunner : MonoBehaviour
    {
        [Tooltip("Resources/Levels 안의 이름 — 예: \"1-1\"")]
        public string levelName = "1-1";

        [Tooltip("손가락 한 번 움직인 거리가 길 폭의 몇 배로 환산되나")]
        public float dragGain = 2.2f;

        public Sim Sim { get; private set; }
        public LevelData Level { get; private set; }
        /// <summary>왜 못 떴나 (레벨 파일이 틀렸을 때). 비어 있으면 정상</summary>
        public string Error { get; private set; }

        float targetX;
        float accumulator;
        bool dragging;
        float lastPointerX;

        void Awake()
        {
            var asset = Resources.Load<TextAsset>("Levels/" + levelName);
            if (asset == null) { Error = "레벨을 못 찾았다: Resources/Levels/" + levelName; Debug.LogError("[CR] " + Error); return; }

            string why;
            Level = LevelFile.Parse(asset.text, out why);
            if (Level == null)
            {
                // **조용히 넘어가지 않는다.** 레벨이 틀렸는데 빈 판이 뜨면 *"게임이 안 나온다"* 로만
                // 보이고, 원인이 레벨 글 한 줄이라는 것을 못 찾는다. 줄 번호가 `why` 에 들어 있다
                Error = "레벨 " + levelName + " 을 못 읽는다 — " + why;
                Debug.LogError("[CR] " + Error);
                return;
            }
            Sim = new Sim(Level);
        }

        void Update()
        {
            if (Sim == null) return;
            ReadDrag();

            // **고정 스텝으로 밀어 넣는다.** `Time.deltaTime` 을 그대로 주면 기기마다 결과가 달라지고,
            // 그러면 `tools/sim` 이 잰 경로와 사람이 한 플레이가 **다른 게임**이 된다.
            // 한 프레임에 여러 틱이 들어갈 수 있다 — 프레임이 길어도 전진 거리는 같아야 한다.
            accumulator += Time.deltaTime;
            // 긴 멈춤(앱 복귀·로딩) 뒤에 수백 틱을 몰아 돌리면 **그 프레임이 통째로 멈춘다**.
            // 따라잡기를 포기하는 쪽이 낫다 — 잃는 것은 시간이고, 지키는 것은 조작이다
            if (accumulator > 0.25f) accumulator = 0.25f;
            while (accumulator >= Sim.FixedStep)
            {
                accumulator -= Sim.FixedStep;
                Sim.Step(targetX);
                Consume(Sim.Last);
                if (Sim.State == SimState.Won || Sim.State == SimState.Lost) break;
            }
        }

        /// <summary>
        /// 손가락 **이동량**으로 움직인다 (기획서 §3.1 Drag Relative). 화면 어디를 잡아도 되고,
        /// 절대 위치 방식과 달리 **UI 를 가린 손가락 위치에 군단이 끌려가지 않는다.**
        /// </summary>
        void ReadDrag()
        {
            float half = Level.roadWidth * 0.5f;
#if UNITY_EDITOR || UNITY_STANDALONE || UNITY_WEBGL
            if (Input.GetMouseButtonDown(0)) { dragging = true; lastPointerX = Input.mousePosition.x; }
            if (Input.GetMouseButtonUp(0)) dragging = false;
            if (dragging)
            {
                float dx = (Input.mousePosition.x - lastPointerX) / Mathf.Max(1, Screen.width);
                lastPointerX = Input.mousePosition.x;
                targetX = Mathf.Clamp(targetX + dx * Level.roadWidth * dragGain, -half, half);
            }
#endif
            if (Input.touchCount > 0)
            {
                var t = Input.GetTouch(0);
                if (t.phase == TouchPhase.Began) lastPointerX = t.position.x;
                else if (t.phase == TouchPhase.Moved)
                {
                    // **화면 폭으로 나눈다.** 픽셀을 그대로 쓰면 해상도가 높은 기기에서 더 느리게
                    // 움직이고, 그건 "이 폰에서는 조작이 둔하다" 로 나타난다
                    float dx = (t.position.x - lastPointerX) / Mathf.Max(1, Screen.width);
                    lastPointerX = t.position.x;
                    targetX = Mathf.Clamp(targetX + dx * Level.roadWidth * dragGain, -half, half);
                }
            }
        }

        /// <summary>
        /// 이번 틱의 사건을 화면 쪽에 넘긴다. **지금은 로그뿐** — 군중·파티클·소리가 서면 여기서 부른다.
        /// 비워 두지 않고 찍는 이유: 연결이 끊겨 있으면 *"아무 일도 안 일어난다"* 로만 보이고,
        /// 사건이 **나긴 났는지**를 확인할 길이 없다.
        /// </summary>
        void Consume(Sim.Tick t)
        {
            if (t.gateFired) Debug.Log($"[CR] 게이트 {(t.gateLane < 0 ? "L" : "R")} · {t.gateBefore:0} → {t.gateAfter:0}");
            if (t.fightBegan) Debug.Log($"[CR] 전투 시작 · 적 {Sim.BlockingEnemies:0}");
            if (t.fightEnded) Debug.Log($"[CR] 전투 끝 · 남은 병력 {Sim.Units}");
            if (t.wallBegan) Debug.Log($"[CR] 벽 {Sim.BlockingWallHp:0}");
            if (t.wallEnded) Debug.Log("[CR] 벽 부숨");
        }
    }
}
