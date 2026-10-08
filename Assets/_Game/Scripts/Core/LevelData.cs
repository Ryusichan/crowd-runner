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

    public enum EventKind { Gate, Enemy, Wall, Zone, Narrow }

    /// <summary>
    /// 길 위의 한 사건. **`z` 하나로 줄 세운다** — 이 게임은 선형이고, 그래서 위치는 전진 거리다.
    /// </summary>
    public struct LevelEvent
    {
        public EventKind kind;
        /// <summary>출발선에서 몇 m</summary>
        public float z;

        /// <summary>
        /// **어느 쪽 길에 있는가** — 0 이면 길 전체를 막고, −1/+1 이면 그쪽에 선 군단만 만난다.
        ///
        /// 이게 있어야 게이트가 **선택**이 된다. 없으면 어느 쪽을 골라도 뒤에 오는 것이 같아서
        /// *"큰 숫자를 고른다"* 가 늘 정답이고, 기획서 §3.4 가 금지한 상태가 된다
        /// (`docs/DESIGN.md` §3c 에서 시뮬이 그걸 찍었다).
        /// </summary>
        public int lane;

        // ---- Gate ----
        /// <summary>한 짝(보통 둘). **리더 중심**이 어느 쪽에 있느냐로 하나만 고른다</summary>
        public GateOption[] options;
        /// <summary>
        /// **고른 길에 갇히는 지점**(이 z 까지). 광고의 그림처럼 두 길이 **칸막이로 갈려** 있다가
        /// 여기서 다시 합쳐진다.
        ///
        /// 없으면 게이트 선택에 **결과가 안 붙는다** — 첫 주행에서 실제로 그랬다: 오른쪽 게이트를
        /// 고른 뒤 **곧바로 왼쪽으로 비켜서** 오른쪽 길의 지역을 안 밟고 지나갔다. 선택은 했는데
        /// 대가는 안 치른 것이고, 그러면 *"큰 쪽을 고르고 피한다"* 가 늘 정답이 된다
        /// (`docs/DESIGN.md` §3c 가 고치려던 그 상태로 되돌아간다).
        /// </summary>
        public float commitUntilZ;

        // ---- Enemy ----
        public int enemyCount;
        /// <summary>최종 방어선인가 — 이것을 넘으면 판이 끝난다</summary>
        public bool isFinal;

        // ---- Wall ----
        /// <summary>장애물 체력. 병력이 때려서 0 으로 만들면 지나간다</summary>
        public float wallHp;

        // ---- Zone ----
        /// <summary>
        /// **병력 하나당** 초당 피해. 이 게임에서 **큰 군단에 처음으로 대가가 붙는 자리**다.
        ///
        /// 전투는 전선 폭에 막혀 *"많아도 덜 죽지 않고 버틸 뿐"* 이라 병력이 많은 것에 손해가 없다
        /// (§3c). 지역 피해는 **머릿수에 비례**하므로 — 100 명이 지나면 100 명분이 깎인다 —
        /// 처음으로 *"지금 더 받는 것"* 에 값이 생긴다.
        /// </summary>
        public float dps;
        /// <summary>구간 길이(m). 지나는 **시간**이 곧 비용이라, 느릴수록·길수록 비싸다</summary>
        public float zoneLength;

        // ---- Narrow ----
        /// <summary>
        /// 이 구간의 길 폭(m). **전선이 좁아진다** — 한 번에 싸우는 수가 줄어든다.
        ///
        /// 좁은 것 **자체로는 손실이 안 는다** (전투 손실은 적 수와 수치로 정해진다, §3c).
        /// 느는 것은 **시간**이고, 그래서 **지역과 겹칠 때** 비로소 아프다 — 좁은 길에서 오래
        /// 싸우는 동안 계속 녹는다. 기획서 §6.2 의 9 판("제한된 공간에서 대군 운영")이 그 조합이다.
        /// </summary>
        public float narrowWidth;

        public static LevelEvent Gate(float z, float commitUntilZ, params GateOption[] options)
            => new LevelEvent { kind = EventKind.Gate, z = z, commitUntilZ = commitUntilZ, options = options };

        public static LevelEvent Enemy(float z, int count, bool isFinal = false, int lane = 0)
            => new LevelEvent { kind = EventKind.Enemy, z = z, enemyCount = count, isFinal = isFinal, lane = lane };

        public static LevelEvent Wall(float z, float hp, int lane = 0)
            => new LevelEvent { kind = EventKind.Wall, z = z, wallHp = hp, lane = lane };

        /// <summary>좁아지는 구간. `zoneLength` 를 길이로 쓴다 (같은 뜻이라 칸을 또 만들지 않는다)</summary>
        public static LevelEvent Narrow(float z, float width, float length, int lane = 0)
            => new LevelEvent { kind = EventKind.Narrow, z = z, narrowWidth = width, zoneLength = length, lane = lane };

        /// <summary>지속 피해 지역 (기획서 §5). `lane` 을 주면 그쪽 길에만 깔린다</summary>
        public static LevelEvent Zone(float z, float dps, float length, int lane = 0)
            => new LevelEvent { kind = EventKind.Zone, z = z, dps = dps, zoneLength = length, lane = lane };
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
        /// **☣ 등급 기준** — 끝났을 때 남은 병력이 이 수 이상이면 그 등급. 좀비퀸 월드맵이
        /// 구역마다 ☣ 셋을 그리므로 (`docs/DESIGN.md` §3b) 그 자리에 들어갈 수가 필요하다.
        ///
        /// **눈대중으로 적지 않는다.** 이 수는 `tools/sim` 이 전 경로를 돌려 잰 잔여에서 나오고,
        /// 검증기가 **닿을 수 있는지**를 본다: ☣☣☣ 는 어떤 경로로든 나와야 하고, **모든**
        /// 경로로 나오면 안 된다(그러면 등급이 아니라 참가상이다). ☣ 하나는 이기면 받는다.
        ///
        /// 좀비퀸에서 **맞출 수 없는 목표**를 세워 놓고 한참 원인을 찾은 적이 있다
        /// (`HANDOFF` §0c-15: 지표가 0 을 읽을 수 없는 구조였다). 목표를 세우는 쪽이
        /// 그 목표가 닿는지 확인하지 않으면, 못 맞추는 것이 **플레이어의 일**이 된다.
        /// </summary>
        public int[] rating = null;

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

            if (rating != null)
            {
                if (rating.Length != 3) return "rating 이 " + rating.Length + " 개다 — ☣ 셋이므로 세 수를 적는다";
                for (int r = 0; r < 3; r++)
                    if (rating[r] <= 0) return "rating[" + r + "] 가 " + rating[r] + " 다 — 0 명으로 받는 등급은 없다";
                if (rating[1] <= rating[0] || rating[2] <= rating[1])
                    return "rating 이 오름차순이 아니다 (" + rating[0] + "/" + rating[1] + "/" + rating[2] + ") — 뒤 등급이 더 쉬우면 등급이 아니다";
            }

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
                    if (e.commitUntilZ > 0f && e.commitUntilZ <= e.z)
                        return "게이트 #" + i + " 의 합류 지점이 게이트보다 앞이다 — 고르자마자 풀린다";
                    for (int a = 0; a < e.options.Length; a++)
                        for (int b = a + 1; b < e.options.Length; b++)
                            if (e.options[a].lane == e.options[b].lane)
                                return "게이트 #" + i + " 의 선택지 둘이 같은 쪽(lane " + e.options[a].lane + ")에 있다";
                }
                else if (e.kind == EventKind.Enemy && e.enemyCount <= 0)
                    return "적 #" + i + " 의 수가 " + e.enemyCount + " 다";
                else if (e.kind == EventKind.Wall && e.wallHp <= 0f)
                    return "벽 #" + i + " 의 체력이 " + e.wallHp + " 다";
                else if (e.kind == EventKind.Zone && (e.dps <= 0f || e.zoneLength <= 0f))
                    return "지역 #" + i + " 의 dps/길이가 " + e.dps + "/" + e.zoneLength + " 다";
                else if (e.kind == EventKind.Narrow)
                {
                    if (e.zoneLength <= 0f) return "좁은 구간 #" + i + " 의 길이가 " + e.zoneLength + " 다";
                    // 넓어지는 것은 좁은 구간이 아니다 — 그렇게 쓰면 읽는 사람이 반대로 이해한다
                    if (e.narrowWidth <= 0f || e.narrowWidth >= roadWidth)
                        return "좁은 구간 #" + i + " 의 폭이 " + e.narrowWidth + " 로 길 폭(" + roadWidth + ") 이상이다";
                    // 한 명도 못 서면 전투가 끝나지 않는다
                    if (e.narrowWidth < 1.0f) return "좁은 구간 #" + i + " 이 " + e.narrowWidth + " m 다 — 한 명도 못 선다";
                }

                if (e.lane < -1 || e.lane > 1) return "사건 #" + i + " 의 lane 이 " + e.lane + " 다 (−1/0/+1)";
                // **최종 방어선을 한쪽에만 두면 반대쪽은 그냥 지나간다** — 이기는 조건이 사라진다
                if (e.kind == EventKind.Enemy && e.isFinal && e.lane != 0)
                    return "최종 방어선 #" + i + " 이 한쪽 길(lane " + e.lane + ")에만 있다 — 반대로 가면 그냥 통과한다";
            }

            bool hasFinal = false;
            foreach (var e in events) if (e.kind == EventKind.Enemy && e.isFinal) hasFinal = true;
            if (!hasFinal) return "최종 방어선(isFinal)이 없다 — 이 판은 이기는 방법이 없다";
            return null;
        }
    }
}
