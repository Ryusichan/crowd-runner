namespace CrowdRunner.Core
{
    /// <summary>
    /// 양측 유닛의 수치. 기획서 §4.2 의 초기값에서 출발하되 **좀비는 느리고 많다** (`DESIGN.md` §3b).
    /// </summary>
    public struct UnitStats
    {
        public float hp;
        public float damage;
        /// <summary>공격 간격(초)</summary>
        public float interval;
        public float speed;

        public static UnitStats Ally => new UnitStats { hp = 10f, damage = 10f, interval = 0.8f, speed = 4.5f };
        /// <summary>
        /// 좀비: **느리고 하나하나는 약하다. 대신 많다.** `[WORKING]` — 오너가 플레이하고 정한다.
        ///
        /// 처음에 아군과 **똑같이** 두었더니 첫 주행에서 모든 경로의 손실 계수가 **정확히 1.00** 이
        /// 나왔다. 수치가 같으면 양쪽이 같은 속도로 죽어서 **생존자 = 아군 − 적** 이 되고, 그러면
        /// 전선 폭도 공격 간격도 **결과에 아무 영향이 없다** (싸우는 시간만 바뀐다). 모든 레버가
        /// 죽은 채로 밸런스를 잡게 된다 — 이 수를 다르게 두는 것이 모델이 일하기 시작하는 조건이다.
        /// </summary>
        public static UnitStats Zombie => new UnitStats { hp = 8f, damage = 6f, interval = 0.9f, speed = 2.0f };
    }

    /// <summary>
    /// **전선 소모 모델** — 병력을 개체가 아니라 **수**로 두고 깎는다.
    ///
    /// 기획서는 두 가지를 적었다: §4.3 개별 유닛 전투와 §4.5 추상식 `P − ⌈E×k⌉`. 그리고
    /// *"두 방식이 크게 어긋나면 보정한다"* 고 했는데, **어긋났다는 것을 말해 주는 것이 없으면
    /// 아무도 안 한다** (`DESIGN.md` §3.1).
    ///
    /// 그래서 **모델을 하나로 만든다.** 추상식의 `k` 를 입력으로 받지 않고, **전선 폭과 공격 간격에서
    /// 소모를 유도한다**:
    ///
    /// ```
    /// 한 번에 싸우는 수 = min(병력, 전선 폭)        ← 뒤에 선 병력은 때리지 못한다
    /// 초당 피해        = 싸우는 수 × 공격력 ÷ 간격
    /// 초당 전사        = 초당 피해 ÷ 상대 HP
    /// ```
    ///
    /// 이러면 `k` 는 **입력이 아니라 출력**이 된다 — 판이 끝난 뒤 *"적 1 명당 아군 몇을 잃었나"* 로
    /// 계산해서 볼 수 있고, 그 수가 설계 감각과 다르면 **전선 폭이나 간격**을 고치면 된다.
    /// 두 모델을 나란히 두고 어긋나는지 지키는 장치가 **필요 없어진다.**
    ///
    /// 왜 이 모델이 그럴듯한가: 1,000 명이 서로를 동시에 때리는 것은 **실제로 일어나지 않는다**.
    /// 길이 7 m 면 앞줄에 설 수 있는 수가 정해져 있고, 뒤는 앞이 죽어야 올라온다. 기획서 §4.4 가
    /// *"300 대 300 에서 앞 30 명씩만 교전"* 으로 적은 것이 같은 이야기다 — 그걸 **성능 대책이 아니라
    /// 전투 규칙 자체**로 삼는다.
    ///
    /// **개체를 하나도 만들지 않는다** (`DESIGN.md` §2 보험). 병력은 수이고 전투는 그 수의 감소다.
    /// </summary>
    public static class Combat
    {
        /// <summary>유닛 하나가 전선에서 차지하는 폭(m). 너무 좁게 잡으면 길 폭이 뜻을 잃는다</summary>
        public const float UnitWidth = 0.9f;

        /// <summary>그 길에서 한 번에 맞붙을 수 있는 최대 수. 적어도 1 — 0 이면 전투가 끝나지 않는다</summary>
        public static int FrontWidth(float roadWidth)
        {
            int n = (int)(roadWidth / UnitWidth);
            return n < 1 ? 1 : n;
        }

        /// <summary>
        /// 한 틱의 교전. **양측이 동시에 때린다** — 선공을 주면 먼저 닿은 쪽이 거의 항상 이겨서
        /// 병력 차이가 결과를 정하지 못한다.
        /// </summary>
        /// <param name="allies">아군 수 (소수점 — 전사자가 틱마다 조금씩 쌓인다)</param>
        /// <param name="enemies">적 수</param>
        public static void Tick(ref float allies, ref float enemies,
                                UnitStats a, UnitStats e, float roadWidth, float dt)
        {
            if (allies <= 0f || enemies <= 0f) return;
            int front = FrontWidth(roadWidth);

            float aFighting = allies < front ? allies : front;
            float eFighting = enemies < front ? enemies : front;

            // 양쪽 피해를 **먼저 다 구하고** 그다음에 적용한다. 하나씩 적용하면 먼저 계산한 쪽이
            // 덜 죽어서, 코드를 쓴 순서가 전투 결과를 바꾼다
            float toEnemies = aFighting * a.damage / a.interval * dt;
            float toAllies = eFighting * e.damage / e.interval * dt;

            enemies -= toEnemies / e.hp;
            allies -= toAllies / a.hp;

            if (enemies < 0f) enemies = 0f;
            if (allies < 0f) allies = 0f;
        }

        /// <summary>
        /// 벽 때리기. 벽은 **되받아치지 않으므로** 병력을 잃지 않는다 — **잃는 것은 시간**이고,
        /// 병력이 많을수록 빨리 지난다 (기획서 §5 "높은 병력을 유지할수록 더 빠르게 파괴").
        /// 병력을 깎는 장애물은 다른 종류다 (Unit Cost — MVP 제외).
        /// </summary>
        public static void TickWall(float allies, ref float wallHp, UnitStats a, float roadWidth, float dt)
        {
            if (allies <= 0f || wallHp <= 0f) return;
            float front = allies < FrontWidth(roadWidth) ? allies : FrontWidth(roadWidth);
            wallHp -= front * a.damage / a.interval * dt;
            if (wallHp < 0f) wallHp = 0f;
        }
    }
}
