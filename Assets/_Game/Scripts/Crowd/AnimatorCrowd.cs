using UnityEngine;

namespace Game.Crowd
{
    /// <summary>
    /// **방식 A — 유닛마다 `SkinnedMeshRenderer` + `Animator`.** "그냥 만들면 이렇게 된다" 는 기준선.
    ///
    /// 풀에서 꺼내 쓴다. 증식이 한 프레임에 수백이라 그때 `Instantiate` 를 하면 **p99 가 측정하려던
    /// 것(그리기 비용)이 아니라 생성 비용**을 재게 된다 — 그건 풀로 없앨 수 있는 비용이므로 섞으면
    /// 방식 A 를 억울하게 낮게 평가한다. 상한만큼 **미리** 만들고 켜고 끈다.
    ///
    /// 그래서 이 측정은 A 에게 **유리한 쪽**이다: 생성 비용 없음 · 가중치 1 개 · 컬링 끔.
    /// 그래도 떨어지면 그 결과는 단단하다.
    /// </summary>
    public sealed class AnimatorCrowd : ICrowdRenderer
    {
        GameObject root;
        Transform[] units;
        Renderer[] rends;
        int live;

        public int DrawCalls => live;   // 배칭이 거의 안 된다 — 유닛 하나가 드로우 하나

        public void Init(int cap)
        {
            var prefab = Resources.Load<GameObject>("M0/StandIn");
            root = new GameObject("CrowdA");
            root.SetActive(false);
            units = new Transform[cap];
            rends = new Renderer[cap];
            if (prefab == null)
            {
                // **없는 것을 조용히 0 으로 재지 않는다** — 굽지 않았으면 그 사실이 로그에 남아야 한다
                Debug.LogError("[M0] StandIn 프리팹이 없다 — M0Assets.Bake 를 먼저 돌려라. A 측정은 무효다");
                return;
            }
            for (int i = 0; i < cap; i++)
            {
                var go = Object.Instantiate(prefab, root.transform);
                go.SetActive(false);
                units[i] = go.transform;
                rends[i] = go.GetComponentInChildren<Renderer>();
            }
        }

        /// <summary>
        /// **끌 때 자식도 꺼야 한다.** 전에는 뿌리만 끄고 `live = 0` 만 했다 — 그러면 자식은
        /// **켜진 채로 남고**, 다음 구간이 뿌리를 다시 켜면 **지난 구간의 개체가 그대로 그려진다.**
        /// `Sync` 는 `live` 부터 켜고 `n`~`live` 를 끄는데 `live` 가 0 이라 **끌 것을 못 찾는다.**
        ///
        /// 5 차 측정에서 이것이 잡혔다: `A-0`(아무도 안 세움)이 GPU **14.33 ms** 였고 빈 장면은
        /// 0.70 이었다. 아무것도 안 그리는데 14 ms 가 나올 수는 없으니 **뭔가를 그리고 있었다** —
        /// 앞 구간(250 명)이었다. 그래서 `A-0`~`A-250` 이 전부 250 명을 재고 **곡선이 평평했다.**
        /// 설명이 안 되는 절편 하나가 이 버그를 가리켰다.
        /// </summary>
        public void SetActive(bool on)
        {
            if (!on && units != null)
                for (int i = 0; i < live; i++) if (units[i] != null) units[i].gameObject.SetActive(false);
            if (root != null) root.SetActive(on);
            if (!on) live = 0;
        }

        public void Sync(CrowdField f)
        {
            if (units == null || units[0] == null) return;
            int n = Mathf.Min(f.Count, units.Length);
            // 새로 들어온 만큼만 켠다 — 매 프레임 전부 `SetActive` 를 부르면 그 호출이 측정에 섞인다
            for (int i = live; i < n; i++) units[i].gameObject.SetActive(true);
            for (int i = n; i < live; i++) units[i].gameObject.SetActive(false);
            live = n;
            for (int i = 0; i < n; i++)
            {
                var t = units[i];
                t.localPosition = new Vector3(f.X[i], 0f, f.Z[i]);
                // 적은 반대쪽을 본다 — 두 군단이 마주 달리는 것이 보여야 겹치는 순간이 읽힌다
                t.localRotation = f.Side[i] == 0 ? IdentityFwd : IdentityBack;
            }
        }

        /// <summary>
        /// 증식 봉우리 — 앞의 `n` 개를 끄고 **같은 프레임에** 다시 켠다.
        ///
        /// 전에는 봉우리가 120 명을 *더했다*. 그러면 `A-100` 구간이 실제로는 **820 명**이 되어
        /// 네 점 스윕이 전부 1,000 근처로 뭉치고 **곡선이 사라진다** (`M0Bench` 주석 §4).
        /// 끄고 켜면 활성화 비용은 그대로 들고 수는 고정이다.
        /// </summary>
        public void Churn(CrowdField f, int n)
        {
            if (units == null || units[0] == null) return;
            int k = Mathf.Min(n, live);
            for (int i = 0; i < k; i++) units[i].gameObject.SetActive(false);
            for (int i = 0; i < k; i++) units[i].gameObject.SetActive(true);
        }

        static readonly Quaternion IdentityFwd = Quaternion.identity;
        static readonly Quaternion IdentityBack = Quaternion.Euler(0f, 180f, 0f);
    }

    /// <summary>
    /// **방식 B — 인스턴싱 + 정점 애니메이션 텍스처.** 아직 **안 만들었다.**
    ///
    /// 비어 있는 구현이 `0 ms` 를 돌려주면 **"B 는 공짜" 로 읽힌다** — 그것이 이 자리에서 가능한
    /// 가장 나쁜 결과다. 그래서 켜질 때마다 "무효" 를 로그에 남기고, 아무것도 그리지 않는다.
    /// (`M0_CROWD.md` §2: A 가 통과하면 B 를 만들 이유가 없으니, **A 를 먼저 재고** 결과를 보고 짠다.)
    /// </summary>
    public sealed class VatCrowd : ICrowdRenderer
    {
        public int DrawCalls => -1;
        public void Init(int cap) { }
        public void SetActive(bool on)
        {
            if (on) Debug.LogWarning("[M0] 방식 B(VAT) 미구현 — 이 구간의 수는 **무효**다 (0 을 '공짜' 로 읽지 말 것)");
        }
        public void Sync(CrowdField f) { }
        public void Churn(CrowdField f, int n) { }
    }
}
