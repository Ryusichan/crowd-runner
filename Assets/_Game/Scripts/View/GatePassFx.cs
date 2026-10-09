using UnityEngine;

namespace CrowdRunner.View
{
    /// <summary>
    /// **고른 것이 고른 것처럼 보이게 한다** — 이 게임의 유일한 조작에 붙는 유일한 피드백.
    ///
    /// 이 게임에서 플레이어가 하는 일은 **게이트 하나를 고르는 것**뿐이다 (`DESIGN.md` §3d).
    /// 그런데 지금까지 그 하나에 **아무 응답이 없었다**: 고른 게이트는 그냥 `SetActive(false)` 로
    /// 사라지고, 병력 수는 소리 없이 바뀐다. 조작이 하나인 게임에서 그 하나에 응답이 없으면
    /// 플레이어는 *자기가 무엇을 했는지* 를 추론해야 한다 — 그러면 게이트는 선택이 아니라
    /// **지나가는 지형**이 된다.
    ///
    /// 세 가지를 말한다. 순서가 중요도다:
    /// ① **무엇을 골랐나** — 고른 쪽이 터지고, 안 고른 쪽은 가라앉는다. 둘을 다르게 지우는 것이
    ///    핵심이다: 둘이 같이 사라지면 *어느 쪽으로 들어갔는지* 가 화면에 안 남는다.
    /// ② **그게 무슨 짓을 했나** — `×3` 과 그 아래 **실제 변화량** `+28`. 규칙만 띄우면
    ///    *×3 인데 8 명밖에 안 늘었다* 를 못 배운다. 곱셈은 **가져온 것에 비례**하고, 그 사실이
    ///    이 게임의 유일한 전략이다.
    /// ③ **좋았나 나빴나** — 색. `after > before` 로만 가른다 (연산 종류가 아니라 **결과**로:
    ///    `÷1` 은 나눗셈인데 손해가 아니고, 작은 군단에 `×1` 도 그렇다).
    ///
    /// 움직임은 오너 규격을 따른다: **100 → 110 → 100 오버슈트, 선형 Lerp 금지.**
    /// 예산: 드로우는 `TextMesh` 넷(풀)뿐이고 프레임당 할당 0 B — 풀과 배열은 한 번만 잡는다.
    /// </summary>
    public sealed class GatePassFx
    {
        const int Pool = 4;
        const float PopLife = 0.95f;
        const float GateLife = 0.42f;

        struct Pop { public TextMesh tm; public Vector3 p0; public float t; public bool live; }
        struct Anim { public Transform t; public Vector3 s0, p0; public float k; public bool chosen, live; }

        readonly Pop[] pops = new Pop[Pool];
        readonly Anim[] anims = new Anim[8];

        /// <summary>지금까지 띄운 응답의 수 — **주행이 묻는다.** 화면을 볼 수 없는 검증이
        /// *게이트를 지날 때마다 응답이 있었나* 를 이 수로 본다 (게이트 수와 같아야 한다)</summary>
        public int Popped { get; private set; }

        /// <summary>`Label` 을 만드는 일은 `LevelView` 에 하나만 둔다 — 글꼴을 못 찾는 경우의
        /// 로그가 두 군데로 갈라지면 *"어떤 글자만 안 나온다"* 가 된다</summary>
        public void Init(System.Func<string, float, TextMesh> label)
        {
            for (int i = 0; i < Pool; i++)
            {
                pops[i].tm = label("GatePop", 0.42f);
                pops[i].tm.gameObject.SetActive(false);
            }
        }

        /// <summary>
        /// 게이트를 지났다. `chosen` 이 들어간 쪽, `refused` 가 버린 쪽(없을 수도 있다 — 외길).
        /// </summary>
        public void Pass(Transform chosen, Transform[] refused, int refusedCount,
                         string rule, float before, float after, Camera cam)
        {
            bool good = after > before + 0.01f;
            if (chosen != null) Push(chosen, true);
            for (int i = 0; i < refusedCount; i++) Push(refused[i], false);

            int slot = -1;
            for (int i = 0; i < Pool; i++) if (!pops[i].live) { slot = i; break; }
            // 풀이 다 찼으면 **가장 오래된 것을 뺏는다.** 띄우지 않는 쪽을 고르면 게이트를
            // 연달아 지날 때 *응답이 있는 게이트와 없는 게이트* 가 생기고, 그게 더 나쁘다
            if (slot < 0) { slot = 0; for (int i = 1; i < Pool; i++) if (pops[i].t > pops[slot].t) slot = i; }

            int d = Mathf.RoundToInt(after) - Mathf.RoundToInt(before);
            var tm = pops[slot].tm;
            string delta = (d >= 0 ? "+" : "") + d;
            // **같은 수를 두 번 쓰지 않는다.** 덧셈 게이트는 규칙과 변화량이 같은 수라
            // (`+35` / `+35`) 두 줄이 같은 말을 하고, 그러면 두 줄짜리 형식 자체가
            // *장식* 으로 읽혀서 **곱셈에서 그 둘이 다를 때도 안 읽힌다**
            tm.text = rule == delta ? delta : rule + "\n" + delta;
            tm.color = good ? new Color(0.58f, 0.94f, 0.62f) : new Color(0.96f, 0.52f, 0.48f);
            tm.gameObject.SetActive(true);
            var at = chosen != null ? chosen.position : Vector3.zero;
            // **군중 위로 띄운다.** 2.2 m 는 사람 키 높이라 숫자가 떼 한가운데에 묻혔다 —
            // 첫 연출 사진에서 게이트 글자와도 겹쳤다
            // 게이트 글자(문에 붙어 있다)와 **겹치지 않게** 위로 더, 그리고 카메라 쪽으로.
            // 4.6 m 로도 사진에서 문짝 글자와 붙어 보였다 — 둘 다 숫자라 겹치면 어느 쪽이
            // 규칙이고 어느 쪽이 결과인지 못 읽는다
            pops[slot].p0 = new Vector3(at.x, 5.8f, at.z - 1.6f);
            pops[slot].t = 0f;
            pops[slot].live = true;
            tm.transform.position = pops[slot].p0;
            if (cam != null) tm.transform.rotation = cam.transform.rotation;
            Popped++;
        }

        void Push(Transform t, bool chosen)
        {
            if (t == null) return;
            int slot = -1;
            for (int i = 0; i < anims.Length; i++) if (!anims[i].live) { slot = i; break; }
            if (slot < 0) { Finish(ref anims[0]); slot = 0; }
            anims[slot] = new Anim { t = t, s0 = t.localScale, p0 = t.position, k = 0f, chosen = chosen, live = true };
        }

        /// <summary>그 게이트가 지금 연출 중인가 — `LevelView` 의 "앞에 있나" 숨김 규칙이
        /// 연출과 **싸우지 않게** 하려고 묻는다. 둘이 같은 `activeSelf` 를 매 프레임 반대로
        /// 쓰면 게이트가 깜빡인다</summary>
        public bool Animating(Transform t)
        {
            for (int i = 0; i < anims.Length; i++) if (anims[i].live && anims[i].t == t) return true;
            return false;
        }

        public void Tick(float dt, Camera cam)
        {
            for (int i = 0; i < anims.Length; i++)
            {
                if (!anims[i].live) continue;
                anims[i].k += dt / GateLife;
                float k = anims[i].k;
                if (k >= 1f) { Finish(ref anims[i]); continue; }
                var t = anims[i].t;
                if (t == null) { anims[i].live = false; continue; }
                if (anims[i].chosen)
                {
                    // 고른 쪽은 **터진다** — 좌우로 벌어지며 얇아지고 밝아진다. 군단이 그
                    // 자리를 통과하는 중이므로 위로 솟으면 머리 위에서 벌어져 안 읽힌다
                    float s = Overshoot(k);
                    t.localScale = new Vector3(anims[i].s0.x * (1f + 1.9f * k * k),
                                               anims[i].s0.y * s * Mathf.Max(0.02f, 1f - k),
                                               anims[i].s0.z);
                }
                else
                {
                    // 버린 쪽은 **가라앉는다.** 같이 터지면 *어느 쪽으로 들어갔는지* 가 안 남는다
                    t.position = anims[i].p0 + new Vector3(0f, -2.6f * k * k, 0f);
                    float s = Mathf.Max(0.02f, 1f - k * 0.8f);
                    t.localScale = new Vector3(anims[i].s0.x * s, anims[i].s0.y, anims[i].s0.z);
                }
            }

            for (int i = 0; i < Pool; i++)
            {
                if (!pops[i].live) continue;
                pops[i].t += dt;
                float k = pops[i].t / PopLife;
                if (k >= 1f)
                {
                    pops[i].live = false;
                    pops[i].tm.gameObject.SetActive(false);
                    continue;
                }
                var tr = pops[i].tm.transform;
                // 올라가며 느려진다 (1-(1-k)² ) — 등속으로 올리면 **영수증이 날아가는 것**처럼
                // 보이고, 숫자를 읽을 시간이 안 생긴다
                tr.position = pops[i].p0 + new Vector3(0f, 2.4f * (1f - (1f - k) * (1f - k)), 0f);
                float sc = Overshoot(Mathf.Min(1f, k * 3.2f)) * 0.42f;
                tr.localScale = new Vector3(sc, sc, sc);
                if (cam != null) tr.rotation = cam.transform.rotation;
                var c = pops[i].tm.color;
                // 마지막 3 할에서만 사라진다 — 처음부터 흐려지면 가장 읽어야 할 순간이 가장 옅다
                c.a = k < 0.7f ? 1f : 1f - (k - 0.7f) / 0.3f;
                pops[i].tm.color = c;
            }
        }

        /// <summary>오너 규격의 100 → 110 → 100. **선형 Lerp 금지** — 커졌다 돌아오는
        /// 그 한 번이 "일어났다" 를 만든다</summary>
        static float Overshoot(float k)
        {
            k = Mathf.Clamp01(k);
            return 1f + 0.10f * Mathf.Sin(k * Mathf.PI) * (1f - k * 0.35f);
        }

        void Finish(ref Anim a)
        {
            if (a.t != null)
            {
                a.t.localScale = a.s0;
                a.t.position = a.p0;
                a.t.gameObject.SetActive(false);
            }
            a.live = false;
        }
    }
}
