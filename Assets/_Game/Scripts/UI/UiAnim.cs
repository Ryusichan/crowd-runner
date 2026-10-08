// ⚠ **좀비퀸에서 그대로 가져온 파일이다** (`../zombie-queen-unity`, 37836840).
//
// 오너 지시가 *"맵 선택 화면을 재활용한다"* 이고 (`docs/DESIGN.md` §3b), 그러려면 같은 부품과
// 같은 **움직임 규격**(100 → 110 → 100 오버슈트, 선형 Lerp 금지 — 오너 2026-09-30)을 써야 한다.
// 그 규격은 오너가 화면을 보고 몇 번 돌려보낸 끝에 자리 잡은 것이라 다시 만들면 다시 돌아온다.
//
// **두 벌이 되는 비용을 알고 복사했다.** Unity 는 `Assets/` 밖의 파일을 안 보고, 두 PC + OneDrive
// 에서 링크는 깨진다. 그래서 **한쪽을 고치면 다른 쪽도 고친다** — 특히 오너가 움직임을 지적하면
// 양쪽에 같이 들어가야 한다. 갈라지기 시작하면 *"좀비퀸에서는 되는데 여기선 안 된다"* 가 된다.
//
// 바꾼 것: 이름 공간 `ZQ.UI` → `CrowdRunner.UI`, `ZQ.Core.MathUtil.Hex` 를 안으로 옮김.
// 그 밖에는 **한 줄도 고치지 않았다** — 고치면 어디가 다른지 아무도 모르게 된다.

using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace CrowdRunner.UI
{
    /// <summary>
    /// UI 움직임 — **화면마다 손으로 넣지 않는다** (오너 2026-09-24: "클릭했을 때 나타나는 요소들
    /// 움직임도 애니메이션들 쭉 적용돼 있어야 한다").
    ///
    /// 우리 화면은 `Show()` 가 통째로 다시 짓는 구조라, 지금까지는 모든 것이 **툭 하고 나타났다가
    /// 툭 하고 사라졌다.** 눌렀는데 아무 반응이 없으면 안 눌린 줄 안다 — 오늘 클릭 버그를 찾는 데
    /// 오래 걸린 이유 중 하나이기도 하다.
    ///
    /// **움직임의 기준은 하나다: 100 으로 가다 110 까지 지나쳤다가 100 에 앉는다** (오너 2026-09-30).
    /// 선형 `Lerp` 를 계단처럼 이어 붙이면 꺾이는 자리가 그대로 보이고, 게임이 아니라 문서처럼 움직인다.
    /// 그래서 곡선을 손으로 쓰지 말고 여기 있는 것을 쓴다 — `Back`(넘쳤다 앉는다) · `Punch`(100→110→100) ·
    /// `Ease`(튀면 안 되는 이동).
    ///
    /// 쓰는 쪽은 이 목록에서 고른다. 새 연출이 필요하면 **여기에 이름을 붙여 더한다** — 화면 코드에서
    /// 직접 크기를 매 프레임 만지면 다음 사람이 그걸 또 베낀다:
    ///   · `Press`    — 누르는 동안 살짝 눌린다. `UiKit.Button` 이 전부 자동으로 붙인다
    ///   · `PopIn`    — 나타날 때 아주 짧게 커지며 뜬다. 카드·패널처럼 **새로 나타나는 것**에만
    ///   · `Bump`     — 이미 떠 있는 것이 **반응**한다(수가 늘었다·값이 바뀌었다). 바뀌는 순간 한 줄
    ///   · `Glide`    — 붙어 있던 것이 **새 자리로** 미끄러진다(목록이 다시 쌓일 때)
    ///   · `Hide`     — 줄어들며 흐려진 뒤 꺼진다. `SetActive(false)` 를 직접 부르지 않는다
    ///   · `Burst`    — 터지듯 커지며 나타난다(결과 화면의 별처럼 "얻었다")
    ///   · `ScreenIn` — 화면이 바뀔 때. `GameFlow.Show()` **한 곳**이 부르므로 모든 화면이 자동으로 탄다
    ///   · `ModalIn`  — 막(scrim)이 깔리고 카드가 떠오른다. 모달을 여는 자리마다 한 줄
    ///
    /// **화면 전환과 모달은 각 화면이 아니라 여는 길목에 건다.** 화면마다 손으로 넣으면
    /// 새 화면을 만든 사람이 빠뜨리고, 그게 오너가 "끊어진다" 고 한 자리다 (2026-09-25).
    ///
    /// 시간은 `unscaledDeltaTime` 을 쓴다 — 메뉴에서는 게임이 멈춰 있어서 `deltaTime` 이 0 이다.
    /// </summary>
    public static class UiAnim
    {
        /// <summary>눌림 깊이와 시간. 0.96 보다 더 눌리면 글자가 흔들려 보인다</summary>
        public const float PressScale = 0.92f, PressTime = 0.06f, ReleaseBounce = 0.05f, ReleaseTime = 0.16f;
        /// <summary>등장: 짧을수록 좋다. 0.2 초를 넘기면 화면 넘김이 굼떠진다</summary>
        public const float PopTime = 0.2f, PopFrom = 0.86f;
        /// <summary>화면 요소가 위에서 아래로 차례로 뜨는 간격 · 최대 개수 (그 뒤는 한꺼번에) — 게임 화면은 "짜잔" 이 있어야 한다 (오너 2026-09-26)</summary>
        public const float CascadeStep = 0.035f; public const int CascadeMax = 14;
        /// <summary>
        /// 화면 전환. 들어오는 쪽 0.22 s · 나가는 쪽 0.14 s. 탭은 **옆으로**(56), 아래 화면으로 들어갈 땐 **아래에서**(48).
        /// 12 px·0.16 s 였을 때 오너 2026-09-25: "이동이 전혀 자연스럽지 않다 · 뜨고 사라지는 애니메이션이 전혀 없다" — 너무 작아서
        /// 안 보였고, 나가는 화면은 아예 없이 그냥 사라졌다. 그리고 **같은 화면을 다시 그릴 때**(설정 토글)도 이걸 태워서
        /// "버튼 누를 때마다 죄다 깜박거린다" — 새로고침은 전환이 아니다. `GameFlow.Show` 가 그 둘을 가른다.
        /// </summary>
        public const float ScreenTime = 0.28f, ScreenOutTime = 0.14f, TabSlide = 88f, PushSlide = 72f, ScreenRise = 64f;
        /// <summary>하단 탭 초록 알약이 옆 탭으로 미끄러지는 시간</summary>
        public const float NavSlideTime = 0.28f;
        /// <summary>모달: 막이 깔리는 동안 카드가 떠오른다. 화면 전환보다 아주 조금 느리게 — 위에 얹히는 것이라</summary>
        public const float ModalTime = 0.26f, ModalFrom = 0.78f, ModalRise = 32f;

        /// <summary>
        /// **반응(Bump)의 기본 폭과 시간** — 오너 2026-09-30: "점점 커지다 100 을 지나 110 정도로 커졌다가 다시 100 으로".
        /// 이 프로젝트의 UI 움직임은 전부 이 느낌을 기준으로 잡는다. `BumpSmall` 은 배지처럼 작은 것 전용:
        /// 20 px 짜리에 10 % 는 2 px 라 아예 안 보인다 — **같은 비율이 같은 크기로 보이지 않는다.**
        /// </summary>
        public const float BumpAmount = 0.10f, BumpSmall = 0.22f, BumpTime = 0.26f;
        /// <summary>목록이 다시 쌓일 때 줄이 제 자리로 미끄러지는 시간</summary>
        public const float GlideTime = 0.22f;
        /// <summary>꺼질 때: 줄어들며 흐려진다. 짧아야 한다 — 사라지는 것을 오래 보고 있을 이유가 없다</summary>
        public const float HideTime = 0.14f, HideTo = 0.82f;

        /// <summary>끝에서 살짝 넘쳤다 돌아오는 곡선 (ease-out-back). `k` 가 클수록 더 튄다 — 1.7 이 표준, 0.8 이면 은근하다</summary>
        public static float Back(float c, float k = 1.70158f) { c = Mathf.Clamp01(c) - 1f; return 1f + (k + 1f) * c * c * c + k * c * c; }

        /// <summary>양 끝이 부드러운 가속·감속 (ease-in-out). 자리를 옮기는 것처럼 **튀면 안 되는** 움직임에</summary>
        public static float Ease(float c) { c = Mathf.Clamp01(c); return c * c * (3f - 2f * c); }

        /// <summary>
        /// **100 → 110 → 100** — 이미 떠 있는 것이 "반응" 할 때의 곡선 (오너 2026-09-30).
        /// 앞 40 % 는 ease-out 으로 부풀고, 남은 60 % 는 ease-in-out 으로 제자리에 앉는다.
        /// 계단식 `Lerp` 를 이어 붙이면 꺾이는 자리가 눈에 보인다 — 게임의 숫자는 **튀어야** 한다.
        /// 돌려주는 값은 0..1 의 **넘침 비율**이라 `1f + Punch(c) * BumpAmount` 처럼 곱해 쓴다.
        /// </summary>
        public static float Punch(float c)
        {
            c = Mathf.Clamp01(c);
            const float up = 0.4f;
            return c < up ? 1f - Mathf.Pow(1f - c / up, 3f) : 1f - Ease((c - up) / (1f - up));
        }

        public static void Press(Selectable target)
        {
            if (target == null) return;
            var rt = target.transform as RectTransform;
            if (rt == null || rt.GetComponent<PressFx>() != null) return;
            rt.gameObject.AddComponent<PressFx>();
        }

        public static void PopIn(RectTransform rt, float delay = 0f)
        {
            if (rt == null || rt.GetComponent<PopFx>() != null) return;
            var fx = rt.gameObject.AddComponent<PopFx>();
            fx.delay = delay;
        }

        /// <summary>
        /// 잠깐 떴다 사라지는 것(토스트). 뜰 때 `PopIn`, `hold` 초 뒤 스르르 사라지고 스스로 없어진다.
        /// `Destroy(go, t)` 만 쓰면 **글자가 툭 하고 없어진다** — 사라지는 것도 나타나는 것만큼 눈에 띈다.
        /// </summary>
        public static void Toast(RectTransform rt, float hold, float fade = 0.25f)
        {
            if (rt == null) return;
            PopIn(rt);
            var fx = rt.gameObject.AddComponent<FadeOutFx>();
            fx.wait = hold; fx.fade = fade;
        }

        /// <summary>
        /// **터지듯 커지며 나타난다** — 결과 화면의 별처럼 "얻었다" 를 보여 줄 때. 0 에서 1.35 배까지 부풀었다가 제자리로.
        /// `PopIn`(0.94→1, 카드용)보다 훨씬 크게 움직인다. `delay` 로 차례를 준다 — 별 셋이 1·2·3 순서로 흘러가게.
        /// </summary>
        public static void Burst(RectTransform rt, float delay = 0f, float dur = 0.42f)
        {
            if (rt == null) return;
            var fx = rt.gameObject.AddComponent<BurstFx>();
            fx.delay = delay; fx.dur = dur;
        }

        /// <summary>
        /// 화면의 직계 자식들을 위에서부터 **차례로** 띄운다. 바탕·지도·탭 바처럼 "무대" 인 것은 빼고 내용만.
        /// `GameFlow.Show()` 가 전환 때만 부른다 — 새로고침에 걸면 깜박인다.
        /// </summary>
        public static void Cascade(RectTransform root)
        {
            if (root == null) return;
            int n = 0;
            for (int i = 0; i < root.childCount; i++)
            {
                var c = root.GetChild(i) as RectTransform; if (c == null) continue;
                string nm = c.name;
                if (nm == "nav" || nm == "qnav" || nm == "map" || nm == "bg" || nm == "bgart" || nm == "shade" || nm == "scrim" || nm == "dim" || nm == "mapholder" || nm == "sky" || nm == "viewport" || nm == "skip") continue;
                PopIn(c, 0.06f + Mathf.Min(n, CascadeMax) * CascadeStep);
                n++;
            }
        }

        /// <summary>화면 하나가 들어온다. `GameFlow.Show()` 가 부른다 — 화면마다 넣지 않는다. `from` = 어디서 미끄러져 오는가(px)</summary>
        public static void ScreenIn(RectTransform rt) => ScreenIn(rt, new Vector2(0f, -ScreenRise));
        public static void ScreenIn(RectTransform rt, Vector2 from)
        {
            if (rt == null || rt.GetComponent<ScreenFx>() != null) return;
            var fx = rt.gameObject.AddComponent<ScreenFx>();
            fx.from = from;
        }

        /// <summary>
        /// 화면 하나가 나간다: 입력을 즉시 끊고(버튼 컴포넌트를 떼어 낸다 — 나가는 화면은 눌리면 안 되고, 테스트가 라벨로 버튼을 찾을 때
        /// 잡히면 안 된다), `to` 쪽으로 미끄러지며 사라진 뒤 스스로 파괴된다. 예전엔 `Destroy` 한 줄이라 그냥 툭 사라졌다.
        /// </summary>
        public static void ScreenOut(RectTransform rt, Vector2 to)
        {
            if (rt == null) return;
            rt.name = "exit:" + rt.name;
            var entering = rt.GetComponent<ScreenFx>();
            if (entering != null) Object.DestroyImmediate(entering);   // still sliding in: it would Destroy the CanvasGroup the exit uses (MissingReferenceException, FlowTests 2026-09-25)
            foreach (var b in rt.GetComponentsInChildren<Selectable>(true)) Object.Destroy(b);
            foreach (var g in rt.GetComponentsInChildren<Graphic>(true)) g.raycastTarget = false;
            if (rt.GetComponent<ScreenOutFx>() != null) return;
            var fx = rt.gameObject.AddComponent<ScreenOutFx>();
            fx.to = to;
        }

        /// <summary>붙어 있던 것을 `from` 에서 제자리로 미끄러뜨린다 (하단 탭의 초록 알약)</summary>
        public static void SlideFrom(RectTransform rt, Vector2 from, float time)
        {
            if (rt == null) return;
            var fx = rt.gameObject.AddComponent<SlideFx>();
            fx.from = from; fx.time = time;
        }

        /// <summary>부모 화면이 미끄러져도 **제자리에 남는다** — 하단 탭 바. 바까지 같이 움직이면 앱 바가 아니라 내용의 일부로 보인다</summary>
        public static void StayPut(RectTransform rt)
        {
            if (rt == null || rt.GetComponent<StayPutFx>() != null) return;
            rt.gameObject.AddComponent<StayPutFx>();
        }

        /// <summary>덮는 패널을 닫는다: 스르르 사라진 뒤 `SetActive(false)`. `SetActive(false)` 만 하면 툭 꺼진다</summary>
        public static void OverlayOut(RectTransform ov)
        {
            if (ov == null || !ov.gameObject.activeSelf) return;
            if (ov.GetComponent<OverlayOutFx>() != null) return;
            foreach (var g in ov.GetComponentsInChildren<Graphic>(true)) g.raycastTarget = false;   // 닫히는 동안 눌리지 않게
            ov.gameObject.AddComponent<OverlayOutFx>();
        }

        /// <summary>
        /// 이미 떠 있는 것을 **한 번 튀게** 한다 — 수가 늘어난 배지, 갱신된 숫자, 값이 바뀐 칸.
        /// 같은 대상에 다시 부르면 처음부터 다시 튄다(연달아 늘어날 때마다 반응해야 한다).
        /// 매 프레임 크기를 직접 만지는 코드를 쓰지 말고 **바뀌는 순간 이 한 줄**을 부른다 (오너 2026-09-30).
        /// </summary>
        public static void Bump(Transform t, float amount = BumpAmount, float dur = BumpTime)
        {
            if (t == null) return;
            var fx = t.GetComponent<BumpFx>();
            if (fx == null) fx = t.gameObject.AddComponent<BumpFx>();
            fx.Play(amount, dur);
        }

        /// <summary>
        /// 붙어 있는 것을 새 자리로 **미끄러뜨린다** — 목록에서 윗줄이 사라져 아랫줄이 올라올 때.
        /// 자리를 바로 찍으면 줄이 순간이동한다. 매 프레임 불러도 안전하다: 목표가 그대로면 아무 일도 안 한다.
        /// </summary>
        public static void Glide(RectTransform rt, Vector2 to, float dur = GlideTime)
        {
            if (rt == null) return;
            var fx = rt.GetComponent<GlideFx>();
            if (fx == null) fx = rt.gameObject.AddComponent<GlideFx>();
            fx.To(to, dur);
        }

        /// <summary>
        /// 보이던 것을 **줄어들며 흐려지게** 끈다. `SetActive(false)` 만 하면 툭 꺼진다 — 다음에 켤 때를 위해 되돌려 놓는다.
        /// 매 프레임 불려도 안전하다: 이미 사라지는 중이면 처음부터 다시 시작하지 않는다
        /// (다시 시작하면 **영원히 사라지지 않는다** — 조건을 보고 매 프레임 부르는 쪽이 있다).
        /// </summary>
        public static void Hide(RectTransform rt)
        {
            if (rt == null || !rt.gameObject.activeSelf) return;
            var fx = rt.GetComponent<HideFx>();
            if (fx == null) fx = rt.gameObject.AddComponent<HideFx>();
            fx.Play();
        }

        /// <summary>
        /// `Hide` 의 짝. 켜면서 제자리에서 튀어 오른다 — 사라지는 중이었다면 **되돌리고** 다시 띄운다.
        /// `SetActive(true)` 만 하면 흐려지던 상태 그대로 굳거나, 곧바로 다시 꺼진다.
        ///
        /// 매 프레임 불려도 안전하다. **이미 떠 있으면 아무 일도 안 한다** — `PopFx` 는 끝나면 스스로 떨어지므로
        /// 무턱대고 `PopIn` 을 다시 부르면 매 프레임 새로 붙어 영원히 튄다.
        /// </summary>
        public static void Show(RectTransform rt)
        {
            if (rt == null) return;
            var fx = rt.GetComponent<HideFx>();
            bool revived = fx != null && fx.Cancel();
            if (rt.gameObject.activeSelf && !revived) return;
            rt.gameObject.SetActive(true);
            PopIn(rt);
        }

        /// <summary>
        /// 모달: `scrim` 은 투명에서 제 색까지, `card` 는 살짝 작고 아래에서 떠오른다.
        /// `card` 가 null 이면 막만 깐다 (토스트처럼 카드가 없는 것도 있다).
        /// </summary>
        public static void ModalIn(Graphic scrim, RectTransform card)
        {
            // **다시 열 때는 다시 돌린다.** 같은 패널을 껐다 켜는 자리(Hud 의 일시정지·설정)에서 그냥 건너뛰면,
            // 연출 도중에 꺼진 패널은 흐린 채로 굳는다 — 컴포넌트가 남아 있어 "이미 했다" 로 읽히기 때문이다
            if (scrim != null)
            {
                var fx = scrim.GetComponent<ScrimFx>();
                if (fx != null) fx.Restart(); else scrim.gameObject.AddComponent<ScrimFx>();
            }
            if (card != null)
            {
                var fx = card.GetComponent<ModalFx>();
                if (fx != null) fx.Restart(); else card.gameObject.AddComponent<ModalFx>();
            }
        }

        /// <summary>누르는 동안 살짝 줄었다가 떼면 돌아온다. 손가락 밑에서 일어나는 일이라 짧아야 한다</summary>
        public class PressFx : MonoBehaviour, IPointerDownHandler, IPointerUpHandler
        {
            Vector3 baseScale = Vector3.one;
            float t = 1f;   // 1 = 원래, 0 = 눌림
            float bounce;   // 떼고 난 뒤 튀는 시간 (남은 초)
            bool down;

            void Awake() { baseScale = transform.localScale; }
            public void OnPointerDown(PointerEventData e) { down = true; bounce = 0f; }
            public void OnPointerUp(PointerEventData e) { down = false; bounce = ReleaseTime; }

            void Update()
            {
                float target = down ? 0f : 1f;
                bool settled = Mathf.Approximately(t, target) && bounce <= 0f;
                if (settled) return;
                t = Mathf.MoveTowards(t, target, Time.unscaledDeltaTime / Mathf.Max(0.001f, PressTime));
                float s = Mathf.Lerp(PressScale, 1f, t);
                if (bounce > 0f)
                {
                    bounce -= Time.unscaledDeltaTime;
                    float k = Mathf.Clamp01(1f - bounce / ReleaseTime);   // 0→1
                    s += ReleaseBounce * Mathf.Sin(k * Mathf.PI) * t;     // 떼는 순간 부풀었다 가라앉는다 — 눌렀다는 확신
                }
                transform.localScale = baseScale * s;
            }
        }

        /// <summary>
        /// 화면 전환. 투명도와 함께 **아주 조금** 떠오른다 — 방향이 있어야 "바뀌었다" 로 읽힌다.
        /// `blocksRaycasts` 는 건드리지 않는다: 연출 중에 막으면 빨리 누르는 사람의 첫 탭이 먹힌다.
        /// </summary>
        public class ScreenFx : MonoBehaviour
        {
            public Vector2 from = new Vector2(0f, -ScreenRise);
            /// <summary>지금 제자리에서 얼마나 벗어나 있는가 — `StayPutFx` 가 이걸 상쇄한다</summary>
            public Vector2 Offset { get; private set; }
            CanvasGroup group;
            RectTransform rt;
            Vector2 home;
            float t;

            void Awake()
            {
                rt = (RectTransform)transform;
                home = rt.anchoredPosition;
                group = GetComponent<CanvasGroup>();
                if (group == null) group = gameObject.AddComponent<CanvasGroup>();
                group.alpha = 0f;
            }

            void Start() { Offset = from; rt.anchoredPosition = home + from; }   // from 은 AddComponent 뒤에 들어온다

            void Update()
            {
                if (group == null || rt == null) { Destroy(this); return; }
                t += Time.unscaledDeltaTime / Mathf.Max(0.001f, ScreenTime);
                float c = Mathf.Clamp01(t);
                float e = Back(c, 0.9f);                          // 살짝 넘쳤다 자리 잡는다 — 미끄러지는 게 아니라 "도착한다"
                group.alpha = Mathf.Clamp01(c * 1.6f);            // 글자는 먼저 보이고 자리는 조금 더 미끄러진다
                Offset = from * (1f - e);
                rt.anchoredPosition = home + Offset;
                if (t < 1f) return;
                group.alpha = 1f;
                Offset = Vector2.zero;
                rt.anchoredPosition = home;
                Destroy(group);   // 화면 위에 CanvasGroup 을 남겨 두면 나중에 alpha 를 만지는 코드와 부딪힌다
                Destroy(this);
            }
        }

        /// <summary>나가는 화면: 바로 흐려지면서 `to` 쪽으로 밀려나고, 끝나면 오브젝트째 사라진다</summary>
        public class ScreenOutFx : MonoBehaviour
        {
            public Vector2 to;
            public Vector2 Offset { get; private set; }
            CanvasGroup group;
            RectTransform rt;
            Vector2 home;
            float t;

            void Awake()
            {
                rt = (RectTransform)transform;
                home = rt.anchoredPosition;
                group = GetComponent<CanvasGroup>();
                if (group == null) group = gameObject.AddComponent<CanvasGroup>();
                group.blocksRaycasts = false; group.interactable = false;
            }

            void Update()
            {
                if (group == null || rt == null) { Destroy(gameObject); return; }
                t += Time.unscaledDeltaTime / Mathf.Max(0.001f, ScreenOutTime);
                float c = Mathf.Clamp01(t);
                float e = c * c;                                   // ease-in — 떠날 땐 가속
                group.alpha = 1f - c;
                Offset = to * e;
                rt.anchoredPosition = home + Offset;
                if (t >= 1f) Destroy(gameObject);
            }
        }

        /// <summary>부모의 `ScreenFx`/`ScreenOutFx` 오프셋을 매 프레임 되돌려 제자리에 선다</summary>
        public class StayPutFx : MonoBehaviour
        {
            RectTransform rt, parent;
            Vector2 home;
            void Awake() { rt = (RectTransform)transform; parent = rt.parent as RectTransform; home = rt.anchoredPosition; }
            void LateUpdate()
            {
                if (parent == null) return;
                Vector2 off = Vector2.zero;
                var i = parent.GetComponent<ScreenFx>(); if (i != null) off = i.Offset;
                var o = parent.GetComponent<ScreenOutFx>(); if (o != null) off = o.Offset;
                rt.anchoredPosition = home - off;
            }
        }

        /// <summary>`from` 에서 제자리로. 끝나면 스스로 떨어진다</summary>
        public class SlideFx : MonoBehaviour
        {
            public Vector2 from; public float time = NavSlideTime;
            RectTransform rt; Vector2 home; float t;
            void Awake() { rt = (RectTransform)transform; home = rt.anchoredPosition; }
            void Start() { rt.anchoredPosition = from; }
            void Update()
            {
                t += Time.unscaledDeltaTime / Mathf.Max(0.001f, time);
                float c = Mathf.Clamp01(t); float e = Back(c, 1.2f);
                rt.anchoredPosition = Vector2.LerpUnclamped(from, home, e);
                if (t >= 1f) { rt.anchoredPosition = home; Destroy(this); }
            }
        }

        /// <summary>덮는 패널이 닫힌다: 카드가 살짝 내려앉으며 막과 함께 흐려지고, 끝나면 꺼진다. 다음에 열릴 때를 위해 원상 복구한다</summary>
        public class OverlayOutFx : MonoBehaviour
        {
            CanvasGroup group; float t;
            RectTransform card; Vector2 cardHome; Vector3 cardScale;
            void Awake()
            {
                group = GetComponent<CanvasGroup>();
                if (group == null) group = gameObject.AddComponent<CanvasGroup>();
                card = transform.Find("panel") as RectTransform;
                if (card != null) { cardHome = card.anchoredPosition; cardScale = card.localScale; }
            }
            void Update()
            {
                if (group == null) { gameObject.SetActive(false); Destroy(this); return; }
                t += Time.unscaledDeltaTime / Mathf.Max(0.001f, ModalTime);
                float c = Mathf.Clamp01(t); float e = c * c;
                group.alpha = 1f - c;
                if (card != null) { card.anchoredPosition = cardHome + new Vector2(0f, -ModalRise * 0.5f * e); card.localScale = cardScale * Mathf.Lerp(1f, 0.96f, e); }
                if (t < 1f) return;
                if (card != null) { card.anchoredPosition = cardHome; card.localScale = cardScale; }
                foreach (var g in GetComponentsInChildren<Graphic>(true)) g.raycastTarget = true;   // 다시 열리면 눌려야 한다
                group.alpha = 1f;
                Destroy(group);
                gameObject.SetActive(false);
                Destroy(this);
            }
        }

        /// <summary>막이 깔린다. 제 색의 알파까지만 올린다 — 어디에 쓰이든 원래 농도를 지킨다</summary>
        public class ScrimFx : MonoBehaviour
        {
            Graphic g;
            float target, t;

            void Awake()
            {
                g = GetComponent<Graphic>();
                if (g == null) { Destroy(this); return; }
                target = g.color.a;
                g.color = new Color(g.color.r, g.color.g, g.color.b, 0f);
            }

            /// <summary>목표 농도는 처음 한 번만 읽는다 — 연출 중에 다시 읽으면 그때의 흐린 값이 목표가 된다</summary>
            public void Restart() { t = 0f; }

            void Update()
            {
                if (g == null) { Destroy(this); return; }
                t += Time.unscaledDeltaTime / Mathf.Max(0.001f, ModalTime * 0.6f);   // 막은 카드보다 먼저 깔린다
                float e = Mathf.Clamp01(t);
                g.color = new Color(g.color.r, g.color.g, g.color.b, target * e);
                if (t >= 1f) Destroy(this);
            }
        }

        /// <summary>모달 카드: 조금 작게, 조금 아래에서 떠오른다. `PopFx` 보다 크게 움직인다 — 위에 얹히는 것이라</summary>
        public class ModalFx : MonoBehaviour
        {
            CanvasGroup group;
            RectTransform rt;
            Vector3 baseScale;
            Vector2 home;
            float t;

            void Awake()
            {
                rt = (RectTransform)transform;
                baseScale = rt.localScale;
                home = rt.anchoredPosition;
                group = GetComponent<CanvasGroup>();
                if (group == null) group = gameObject.AddComponent<CanvasGroup>();
                group.alpha = 0f;
                rt.localScale = baseScale * ModalFrom;
                rt.anchoredPosition = home + new Vector2(0f, -ModalRise);
            }

            public void Restart() { t = 0f; }

            void Update()
            {
                t += Time.unscaledDeltaTime / Mathf.Max(0.001f, ModalTime);
                float c = Mathf.Clamp01(t);
                float e = Back(c);                                 // 스프링: 1.1 배까지 넘쳤다가 앉는다 — 게임의 팝업은 "튀어나와야" 한다
                group.alpha = Mathf.Clamp01(c * 2.5f);
                rt.localScale = baseScale * Mathf.Lerp(ModalFrom, 1f, e);
                rt.anchoredPosition = Vector2.Lerp(home + new Vector2(0f, -ModalRise), home, e);
                if (t < 1f) return;
                group.alpha = 1f;
                rt.localScale = baseScale;
                rt.anchoredPosition = home;
                Destroy(group);
                Destroy(this);
            }
        }

        /// <summary>기다렸다가 스르르 사라지고 자기 오브젝트를 지운다</summary>
        public class FadeOutFx : MonoBehaviour
        {
            public float wait, fade = 0.25f;
            CanvasGroup group;
            float t;

            void Update()
            {
                if (wait > 0f) { wait -= Time.unscaledDeltaTime; return; }
                if (group == null)
                {
                    group = GetComponent<CanvasGroup>();
                    if (group == null) group = gameObject.AddComponent<CanvasGroup>();
                }
                t += Time.unscaledDeltaTime / Mathf.Max(0.001f, fade);
                group.alpha = 1f - Mathf.Clamp01(t);
                if (t >= 1f) Destroy(gameObject);
            }
        }

        /// <summary>0 → 1.35 → 1 로 부풀며 나타난다. 시작 전엔 안 보인다(alpha 0). 끝나면 스스로 떨어진다</summary>
        public class BurstFx : MonoBehaviour
        {
            public float delay, dur = 0.42f;
            CanvasGroup group; Vector3 baseScale = Vector3.one; float t;
            void Awake()
            {
                baseScale = transform.localScale;
                group = GetComponent<CanvasGroup>(); if (group == null) group = gameObject.AddComponent<CanvasGroup>();
                group.alpha = 0f; transform.localScale = Vector3.zero;
            }
            void Update()
            {
                if (delay > 0f) { delay -= Time.unscaledDeltaTime; return; }
                t += Time.unscaledDeltaTime / Mathf.Max(0.001f, dur);
                float c = Mathf.Clamp01(t);
                // 0→0.6: 0 에서 1.35 로 (ease-out) · 0.6→1: 1.35 에서 1 로 (ease-in-out)
                float s = c < 0.6f ? Mathf.Lerp(0f, 1.35f, 1f - Mathf.Pow(1f - c / 0.6f, 3f))
                                   : Mathf.Lerp(1.35f, 1f, Mathf.SmoothStep(0f, 1f, (c - 0.6f) / 0.4f));
                group.alpha = Mathf.Clamp01(c * 4f);
                transform.localScale = baseScale * s;
                if (t < 1f) return;
                group.alpha = 1f; transform.localScale = baseScale;
                Destroy(group); Destroy(this);
            }
        }

        /// <summary>
        /// `Punch` 곡선으로 한 번 부풀었다 앉는다. **붙은 채로 남는다** — 다시 튈 일이 잦은 자리(수·점수)라
        /// 매번 컴포넌트를 붙였다 떼는 것이 더 비싸다. 쉴 때는 `Update` 첫 줄에서 바로 빠진다.
        /// </summary>
        public class BumpFx : MonoBehaviour
        {
            Vector3 baseScale = Vector3.one;
            float amount = BumpAmount, dur = BumpTime, t = 2f;
            bool captured;

            /// <summary>기준 크기는 **처음 한 번만** 읽는다 — 연출 중에 다시 읽으면 부푼 값이 기준이 되어 점점 커진다</summary>
            public void Play(float a, float d)
            {
                if (!captured) { baseScale = transform.localScale; captured = true; }
                amount = a; dur = Mathf.Max(0.001f, d); t = 0f;
            }

            void Update()
            {
                if (t > 1f) return;
                t += Time.unscaledDeltaTime / dur;
                float c = Mathf.Clamp01(t);
                transform.localScale = baseScale * (1f + Punch(c) * amount);
                if (t >= 1f) { transform.localScale = baseScale; t = 2f; }
            }
        }

        /// <summary>새 자리로 미끄러진다. 살짝 지나쳤다 앉는다(`Back`) — 목록이 "다시 쌓였다" 로 읽히게</summary>
        public class GlideFx : MonoBehaviour
        {
            RectTransform rt;
            Vector2 from, to;
            float dur = GlideTime, t = 2f;
            bool has;

            void Awake() { rt = (RectTransform)transform; to = rt.anchoredPosition; }

            public void To(Vector2 target, float d)
            {
                if (rt == null) rt = (RectTransform)transform;
                if (has && (target - to).sqrMagnitude < 0.01f) return;      // 매 프레임 불려도 목표가 같으면 건드리지 않는다
                // 처음 자리를 잡을 때와 꺼져 있던 줄이 켜질 때는 **미끄러지지 않는다.** 화면 밖에서부터 날아오면
                // 방금 생긴 줄이 어디서 온 것인지 읽히지 않는다 — 그 줄은 `PopIn` 으로 제자리에서 뜬다
                if (!has || !gameObject.activeInHierarchy) { rt.anchoredPosition = target; to = target; has = true; t = 2f; return; }
                from = rt.anchoredPosition; to = target; dur = Mathf.Max(0.001f, d); t = 0f;
            }

            void Update()
            {
                if (t > 1f) return;
                t += Time.unscaledDeltaTime / dur;
                rt.anchoredPosition = Vector2.LerpUnclamped(from, to, Back(Mathf.Clamp01(t), 0.9f));
                if (t >= 1f) { rt.anchoredPosition = to; t = 2f; }
            }
        }

        /// <summary>줄어들며 흐려진 뒤 꺼진다. 크기와 투명도를 되돌려 놓아야 다음에 켤 때 멀쩡하다</summary>
        public class HideFx : MonoBehaviour
        {
            CanvasGroup group;
            Vector3 baseScale = Vector3.one;
            float t = 2f;
            bool captured;

            public void Play()
            {
                if (t <= 1f) return;                                // 이미 사라지는 중 — 다시 감으면 끝나지 않는다
                if (!captured) { baseScale = transform.localScale; captured = true; }
                if (group == null) { group = GetComponent<CanvasGroup>(); if (group == null) group = gameObject.AddComponent<CanvasGroup>(); }
                t = 0f;
            }

            /// <summary>사라지던 것을 도로 세운다 — 크기·투명도를 원래대로. **되돌릴 것이 있었는지**를 돌려준다</summary>
            public bool Cancel()
            {
                if (t > 1f) return false;
                t = 2f;
                if (captured) transform.localScale = baseScale;
                if (group != null) group.alpha = 1f;
                return true;
            }

            void Update()
            {
                if (t > 1f) return;
                t += Time.unscaledDeltaTime / HideTime;
                float c = Mathf.Clamp01(t);
                if (group != null) group.alpha = 1f - c;
                transform.localScale = baseScale * Mathf.Lerp(1f, HideTo, Ease(c));
                if (t < 1f) return;
                transform.localScale = baseScale;
                if (group != null) group.alpha = 1f;
                gameObject.SetActive(false);
                t = 2f;
            }
        }

        /// <summary>나타날 때 한 번. 끝나면 스스로 사라진다 — 남아서 매 프레임 도는 것이 없게</summary>
        public class PopFx : MonoBehaviour
        {
            public float delay;
            CanvasGroup group;
            Vector3 baseScale = Vector3.one;
            float t;

            void Awake()
            {
                baseScale = transform.localScale;
                group = GetComponent<CanvasGroup>();
                if (group == null) group = gameObject.AddComponent<CanvasGroup>();
                group.alpha = 0f;
                transform.localScale = baseScale * PopFrom;
            }

            void Update()
            {
                if (delay > 0f) { delay -= Time.unscaledDeltaTime; return; }
                t += Time.unscaledDeltaTime / Mathf.Max(0.001f, PopTime);
                float c = Mathf.Clamp01(t);
                float e = Back(c, 1.4f);                                              // 살짝 튀며 자리 잡는다
                group.alpha = Mathf.Clamp01(c * 2f);
                transform.localScale = baseScale * Mathf.LerpUnclamped(PopFrom, 1f, e);
                if (t >= 1f)
                {
                    group.alpha = 1f;
                    transform.localScale = baseScale;
                    Destroy(this);
                }
            }
        }
    }
}
