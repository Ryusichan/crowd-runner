using System.Collections.Generic;

namespace CrowdRunner.Core
{
    /// <summary>
    /// 한 판의 **데이터**. 레벨은 코드가 아니라 이 구조로 적는다 — 하드코딩하면 레벨이 늘수록
    /// 고치기 어려워진다 (기획서 §12).
    ///
    /// **`UnityEngine` 을 쓰지 않는다.** 이 폴더(`Core/`)는 Unity 없이 컴파일돼야 하고,
    /// 그래야 `tools/sim` 이 에디터를 켜지 않고 레벨을 끝까지 돌릴 수 있다 (`docs/DESIGN.md` §2).
    /// 컴파일러가 이 규칙을 지킨다 — 사람이 지키는 규칙은 지켜지지 않는다.
    /// </summary>
    public enum GateOp { Add, Multiply, Subtract, Divide }

    /// <summary>게이트 한 짝의 한쪽. `lane` 은 −1(왼쪽) / +1(오른쪽)</summary>
    public struct GateOption
    {
        public int lane;
        public GateOp op;
        public int value;

        public GateOption(int lane, GateOp op, int value) { this.lane = lane; this.op = op; this.value = value; }
    }

    public enum EventKind { Gate, Enemy, Wall }

    /// <summary>
    /// 길 위의 한 사건. **`z` 하나로 줄 세운다** — 이 게임은 선형이고, 그래서 위치는 전진 거리다.
    /// </summary>
    public struct LevelEvent
    {
        public EventKind kind;
        /// <summary>출발선에서 몇 m</summary>
        public float z;

        // ---- Gate ----
        /// <summary>한 짝(보통 둘). **리더 중심**이 어느 쪽에 있느냐로 하나만 고른다</summary>
        public GateOption[] options;

        // ---- Enemy ----
        public int enemyCount;
        /// <summary>최종 방어선인가 — 이것을 넘으면 판이 끝난다</summary>
        public bool isFinal;

        // ---- Wall ----
        /// <summary>장애물 체력. 병력이 때려서 0 으로 만들면 지나간다</summary>
        public float wallHp;

        public static LevelEvent Gate(float z, params GateOption[] options)
            => new LevelEvent { kind = EventKind.Gate, z = z, options = options };

        public static LevelEvent Enemy(float z, int count, bool isFinal = false)
            => new LevelEvent { kind = EventKind.Enemy, z = z, enemyCount = count, isFinal = isFinal };

        public static LevelEvent Wall(float z, float hp)
            => new LevelEvent { kind = EventKind.Wall, z = z, wallHp = hp };
    }

    /// <summary>
    /// 한 레벨 전체. `events` 는 **`z` 오름차순**이어야 한다 — `Validate()` 가 본다.
    /// </summary>
    public class LevelData
    {
        /// <summary>
        /// **도시 번호** (1 = 부산). 월드맵이 좀비퀸 것 그대로라 **그 번호 체계를 따른다** —
        /// 도시 하나 = 10 판 (`docs/DESIGN.md` §3b). 번호가 다르면 월드맵이 이 레벨을 못 찾고,
        /// 그러면 메타를 통째로 가져온 이득이 사라진다.
        /// </summary>
        public int chapter = 1;
        /// <summary>도시 안에서 몇 번째 판인가 (1~10). `chapter` 와 함께 "1-3" 을 만든다</summary>
        public int index = 1;

        /// <summary>`"1-3"` — 화면·로그·검사기가 판을 가리키는 이름</summary>
        public string Code => chapter + "-" + index;

        public int initialUnits = 10;
        public float roadWidth = 7f;
        public float forwardSpeed = 4.5f;
        public float length = 160f;
        public List<LevelEvent> events = new List<LevelEvent>();

        /// <summary>
        /// 데이터가 **돌려 볼 수 있는 모양인가.** 틀린 레벨을 시뮬에 넣으면 시뮬이 거짓말을 한다 —
        /// 그래서 돌리기 전에 본다. 반환값이 null 이면 괜찮고, 아니면 그게 이유다.
        /// </summary>
        public string Validate()
        {
            // 번호부터 본다 — 월드맵이 이 둘로 판을 찾는다. 틀리면 **판이 목록에서 사라지고**,
            // 그건 "레벨이 안 보인다" 로만 나타나서 원인을 찾기 어렵다
            if (chapter < 1) return "chapter 가 " + chapter + " 다 — 도시 번호는 1 부터다 (1 = 부산)";
            if (index < 1 || index > 10) return "index 가 " + index + " 다 — 도시 하나는 10 판이다";
            if (initialUnits <= 0) return "initialUnits 가 " + initialUnits + " 다 — 0 명으로는 시작할 수 없다";
            if (forwardSpeed <= 0f) return "forwardSpeed 가 0 이하다 — 전진하지 않는 판은 끝나지 않는다";
            if (events.Count == 0) return "사건이 하나도 없다 — 그냥 길이다";

            float last = -1f;
            for (int i = 0; i < events.Count; i++)
            {
                var e = events[i];
                if (e.z < last) return "사건이 z 순서가 아니다 (#" + i + " 가 " + e.z + ", 앞은 " + last + ")";
                if (e.z > length) return "사건 #" + i + " 가 레벨 길이(" + length + ") 밖에 있다 — 영원히 안 만난다";
                last = e.z;

                if (e.kind == EventKind.Gate)
                {
                    if (e.options == null || e.options.Length < 2)
                        return "게이트 #" + i + " 에 선택지가 " + (e.options?.Length ?? 0) + " 개다 — 고를 것이 없으면 게이트가 아니다";
                    // 같은 lane 에 둘을 두면 **어느 쪽을 골라도 같은 것**이 되어 선택이 사라진다
                    for (int a = 0; a < e.options.Length; a++)
                        for (int b = a + 1; b < e.options.Length; b++)
                            if (e.options[a].lane == e.options[b].lane)
                                return "게이트 #" + i + " 의 선택지 둘이 같은 쪽(lane " + e.options[a].lane + ")에 있다";
                }
                else if (e.kind == EventKind.Enemy && e.enemyCount <= 0)
                    return "적 #" + i + " 의 수가 " + e.enemyCount + " 다";
                else if (e.kind == EventKind.Wall && e.wallHp <= 0f)
                    return "벽 #" + i + " 의 체력이 " + e.wallHp + " 다";
            }

            bool hasFinal = false;
            foreach (var e in events) if (e.kind == EventKind.Enemy && e.isFinal) hasFinal = true;
            if (!hasFinal) return "최종 방어선(isFinal)이 없다 — 이 판은 이기는 방법이 없다";
            return null;
        }
    }
}
