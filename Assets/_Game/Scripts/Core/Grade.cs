namespace CrowdRunner.Core
{
    /// <summary>
    /// **등급과 해금의 규칙.** 월드맵이 ☣ 를 그리고 어느 판이 열렸는지 정할 때 쓰는 셈이
    /// 전부 여기 있다 — `UnityEngine` 없이.
    ///
    /// 왜 `Core/` 인가: 이 셈이 틀리면 **진행이 막히거나 공짜로 열린다**. 그런 것을 화면 쪽에
    /// 두면 Unity 를 띄워야 확인할 수 있고, 지금은 그게 규칙으로 막혀 있다 (오너 2026-10-08:
    /// 4 일간 에디터를 모니터에 띄우지 않는다). 여기 있으면 `tools/sim` 이 레벨 열 판 전 경로를
    /// 돌리면서 **매번 같이 확인한다.**
    /// </summary>
    public static class Grade
    {
        /// <summary>한 판에서 받을 수 있는 ☣ 의 수</summary>
        public const int Max = 3;

        /// <summary>
        /// 끝난 판의 ☣ 수 (0~3).
        ///
        /// **이긴 판은 최소 ☣ 하나다.** `rating[0]` 보다 적게 남겨도 그렇다 — 검증기는 모든
        /// 클리어 경로가 `rating[0]` 을 넘도록 지키지만(`LevelData.rating` 설명), 사람은
        /// 시뮬이 돌린 어떤 경로보다 못할 수 있다. 그때 *"깼는데 ☣ 가 0"* 이 되면
        /// **이긴 것이 화면에 안 나타난다** — 좀비퀸의 주 목표 = ☣ 하나와 같은 규칙이다.
        /// </summary>
        public static int Of(bool won, int remaining, int[] rating)
        {
            if (!won) return 0;
            if (rating == null || rating.Length != 3) return 1;
            int n = 1;
            for (int i = 0; i < 3; i++) if (remaining >= rating[i] && i + 1 > n) n = i + 1;
            return n > Max ? Max : n;
        }

        /// <summary>
        /// 그 판이 열렸는가. **1 번은 늘 열려 있고**, 나머지는 **바로 앞 판을 깼으면** 열린다.
        ///
        /// 앞 판의 ☣ 수를 조건으로 걸지 않는다 — 걸면 ☣ 를 못 채운 사람이 **진행 자체를
        /// 못 하게** 되고, 그건 1~2 분짜리 판을 쌓아 가는 이 장르에서 가장 비싼 막힘이다.
        /// ☣ 는 "다시 할 이유" 이고 "진행 조건" 이 아니다.
        /// </summary>
        public static bool Unlocked(int index, int clearedUpTo) => index <= 1 || index <= clearedUpTo + 1;

        /// <summary>
        /// 도시 정복도 0~1 (월드맵의 "부산 72 %"). ☣ 를 다 모아야 100 % 다 — 깨기만 하면
        /// 33 % 다. 좀비퀸의 구역 정복과 같은 셈이라 화면을 그대로 쓸 수 있다.
        /// </summary>
        public static float Conquest(int[] grades, int stagesPerCity)
        {
            if (grades == null || stagesPerCity <= 0) return 0f;
            int sum = 0;
            for (int i = 0; i < grades.Length && i < stagesPerCity; i++) sum += grades[i] < 0 ? 0 : (grades[i] > Max ? Max : grades[i]);
            return sum / (float)(stagesPerCity * Max);
        }
    }
}
