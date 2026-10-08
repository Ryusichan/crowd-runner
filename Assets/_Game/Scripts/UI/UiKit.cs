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

﻿using UnityEngine;
using UnityEngine.UI;

namespace CrowdRunner.UI
{
    /// <summary>코드로 uGUI 를 짓는 작은 도우미 (프리팹 없음). 한글은 OS 폰트로.</summary>
    public static class UiKit
    {
        static Font font;
        public static Font Font
        {
            get
            {
                if (font != null) return font;
                // 번들 한글 폰트 (OFL — Resources/Fonts). 1순위 Jua(BM 주아, 둥근 굵은 글씨 — 오너 시안 로고/로딩 문구와 같은 결, 2026-09-17 "제공한 것 같은 폰트로"),
                // 없는 글리프(· — → ☣ 등)는 메타의 fallbackFontReferences 로 ZQFallbackGothic(NanumGothic 서브셋) 이 받는다. WebGL 엔 OS 폰트가 없다
                font = Resources.Load<Font>("Fonts/Jua-Regular");
                if (font == null) font = Resources.Load<Font>("Fonts/ZQFallbackGothic");   // NanumGothic 서브셋(OFL 예약 이름 회피로 개명, PENDING §L9)
                if (font != null) return font;
                var names = new[] { "Malgun Gothic", "맑은 고딕", "Apple SD Gothic Neo", "Noto Sans CJK KR", "Noto Sans KR", "NanumGothic", "Droid Sans Fallback" };
                try { font = Font.CreateDynamicFontFromOSFont(names, 24); } catch { font = null; }
                if (font == null) font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                return font;
            }
        }

        static Font iconFont;
        static bool iconFontTried;
        /// <summary>
        /// **아이콘 전용 폰트** (Material Icons, Apache 2.0 — `Resources/Fonts/MaterialIcons-Regular.ttf`).
        ///
        /// 오너 2026-09-24 "현재 아이콘셋 최대한 비슷한 웹상에 있는 아이콘 셋으로 대체해 줘, 이미지로 가려니 깨진다".
        /// 지금까지 작은 글리프(설정 톱니 · 스탯 하트 · 자물쇠)는 시안에서 잘라낸 **래스터**였다. 시안의 그 칸은
        /// 한 변 60~90 px 인데 화면에서는 18~28 units 로 쓰니, 줄이는 쪽에서 획이 뭉개지고 늘리는 쪽에서 번졌다.
        /// 글리프는 벡터라 어느 크기에서도 선명하고, `Text` 라서 색도 그냥 입혀진다.
        ///
        /// **큰 그림은 건드리지 않는다** — 기술 컷 · 코스툼 · 이야기 컷 · 아이템 · 좀비 초상은 그 화풍이 내용이다.
        /// 바뀌는 건 `Glyphs` 표에 든 **기능성 자리**뿐이고, 표에 없는 이름은 예전처럼 그림을 찾는다.
        /// </summary>
        public static Font IconFont
        {
            get
            {
                if (iconFontTried) return iconFont;
                iconFontTried = true;
                // `ZQIcons` = Material Icons + 우리가 그려 넣은 것(왕관 · 보석). `tools/artcut/glyphdraw.py` 가 굽는다.
                // 없으면 받아 온 원본으로 — 그때는 그려 넣은 둘만 빈칸이 되고 나머지 마흔둘은 그대로 나온다
                iconFont = Resources.Load<Font>("Fonts/ZQIcons")
                        ?? Resources.Load<Font>("Fonts/MaterialIcons-Regular");
                return iconFont;
            }
        }

        /// <summary>
        /// 글리프를 쓸지. `false` 면 표를 무시하고 전부 예전 그림으로 돌아간다 —
        /// 오너가 "그림이 낫다" 고 하면 여기 한 줄만 끈다.
        /// </summary>
        public static bool PreferGlyphs = true;

        public static RectTransform Rect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rt = go.GetComponent<RectTransform>();
            rt.SetParent(parent, false);
            return rt;
        }

        /// <summary>앵커 (ax, ay) 기준으로 위치/크기</summary>
        public static RectTransform Place(RectTransform rt, Vector2 anchor, Vector2 pos, Vector2 size)
        {
            rt.anchorMin = rt.anchorMax = anchor;
            rt.pivot = anchor;
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
            return rt;
        }

        public static RectTransform Stretch(RectTransform rt, float pad = 0f)
        {
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(pad, pad); rt.offsetMax = new Vector2(-pad, -pad);
            return rt;
        }

        public static Image Panel(string name, Transform parent, Color color)
        {
            var rt = Rect(name, parent);
            var img = rt.gameObject.AddComponent<Image>();
            img.color = color;
            img.raycastTarget = false;
            return img;
        }

        /// <summary>
        /// 글자 크기 배율 — **모든 글자가 여기를 지난다.** 호출부가 수백 군데라 하나씩 키우면 반드시 빠뜨린다.
        ///
        /// 오너 2026-09-24 "전체적으로 폰트들이 너무 작다". 화면들이 `docs/UI_STYLE.md` §5.2 보다 작은 숫자를
        /// 직접 적고 있어서(문서 Body 16 ↔ 코드 13), 옮기기 전까지 그 화면들을 문서 쪽으로 끌어올리는 **다리**다.
        ///
        /// 위의 이름들(`Body`·`Caption` …)은 이 값으로 **나눠서** 정의되므로 배율과 무관하게 문서 크기로 나온다.
        /// 화면을 전부 이름으로 옮기고 나면 여기를 1 로 되돌린다 — 그때 달라지는 것은 아무것도 없어야 한다.
        /// `Label` 은 넘침을 허용하므로 키워도 글자가 잘리지 않는다. 겹치면 그 화면의 상자를 넓히는 게 맞지,
        /// 글자를 다시 줄이는 게 아니다.
        /// </summary>
        public static float FontScale = 1.25f;

        public static Text Label(string name, Transform parent, string text, int size, Color color, TextAnchor align = TextAnchor.MiddleLeft, bool bold = false)
        {
            var rt = Rect(name, parent);
            var t = rt.gameObject.AddComponent<Text>();
            t.font = Font;
            // `Px()` 가 나눠 둔 것을 여기서 **되곱는다** — 둘은 서로를 지운다 (그쪽 주석 참고)
            t.fontSize = Mathf.Max(1, Mathf.RoundToInt(size * FontScale));
            t.text = text;
            t.color = color;
            t.alignment = align;
            t.fontStyle = bold ? FontStyle.Bold : FontStyle.Normal;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.raycastTarget = false;
            Legible(t);
            return t;
        }

        /// <summary>
        /// **밝은 글자에만** 어두운 테두리를 붙여 그림 위에서도 읽히게 한다
        /// (오너 2026-09-24 "글자가 묻힌다").
        ///
        /// 도시 지도·시안 배경은 한 장 안에서 밝은 곳과 어두운 곳이 섞여 있다. 그래서 같은 흰 글자가
        /// 어떤 자리에서는 읽히고 어떤 자리에서는 사라진다 — **글자 색을 바꿔서는 못 고친다.**
        /// 어느 바탕 위에 놓이든 테두리가 글자를 바탕에서 떼어 낸다.
        ///
        /// 어두운 글자에는 붙이지 않는다: 크림색 카드 위의 검은 글자에 검은 테두리를 두르면 획이
        /// 두꺼워져 오히려 뭉갠다. 그쪽은 이미 대비가 충분하다.
        /// `on = false` 로 끌 수 있다 — 이미 바탕을 깔아 둔 자리라면 테두리가 없는 편이 깔끔하다.
        /// </summary>
        public static void Legible(Text t, bool on = true)
        {
            if (t == null) return;
            var c = t.color;
            float lum = 0.299f * c.r + 0.587f * c.g + 0.114f * c.b;
            if (!on || lum < 0.55f) return;
            var o = t.gameObject.GetComponent<Outline>();
            if (o == null) o = t.gameObject.AddComponent<Outline>();
            o.effectColor = new Color(0.04f, 0.03f, 0.07f, 0.85f);
            o.effectDistance = new Vector2(1.2f, -1.2f);
            o.useGraphicAlpha = true;
        }

        public static Button Button(string name, Transform parent, string text, int size, Color bg, Color fg, System.Action onClick)
        {
            var img = Panel(name, parent, bg);
            img.sprite = Rounded; img.type = Image.Type.Sliced; SetRadius(img, RadiusButton);
            img.raycastTarget = true;
            var b = img.gameObject.AddComponent<Button>();
            b.targetGraphic = img;
            b.onClick.AddListener(() => onClick?.Invoke());
            var t = Label("text", img.transform, text, size, fg, TextAnchor.MiddleCenter, true);
            Stretch(t.rectTransform);
            // 누르면 눌린다 — **모든 버튼이 여기를 지나므로** 화면마다 따로 붙일 필요가 없다
            // (오너 2026-09-24 "클릭했을 때 나타나는 요소들 움직임도 쭉 적용돼 있어야 한다").
            // 반응이 없으면 안 눌린 줄 안다 — 오늘 클릭 버그를 오래 못 찾은 이유 중 하나이기도 했다.
            UiAnim.Press(b);
            return b;
        }

        // ---- 고정 라운드 반경 (오너 2026-09-23 "border radius 가 이상하다 — 고정된 px 로 하는 게 좋겠다") ----
        //
        // `Rounded` 는 64 px 안에 24 px 코너를 넣은 9-slice 다. Sliced 로 늘리면 **코너는 언제나 24 단위**라,
        // 높이 14 짜리 볼륨 트랙에서는 코너(24)가 높이의 반(7)을 넘어 서로 겹치며 모양이 뭉개진다 — 그게 "이상한" 이유다.
        // `pixelsPerUnitMultiplier` 로 그려지는 코너 크기를 정해 준다: 배수 = 스프라이트 코너 / 원하는 px.
        public const float SpriteCorner = 24f;
        /// <summary>화면 단위 패널·다이얼로그</summary>
        // ==== 글자 크기 — `docs/UI_STYLE.md` §5.2 가 정본이다 ====
        //
        // 정책은 이미 문서에 있었다. **코드가 그걸 안 지키고 있었을 뿐이다** — 문서는 Body 16 인데
        // 화면들은 13 을, Caption 12 인데 10 을 쓰고 있었다. 그래서 "폰트가 작다" 가 나온다
        // (오너 2026-09-24). 손으로 고른 숫자가 화면마다 달라 같은 성격의 글자가 10·11·12·13 으로
        // 흩어졌고, 그러면 무엇이 더 중요한지 눈으로 안 읽힌다.
        //
        // **새 코드는 숫자를 직접 적지 않는다.** 이 이름들을 쓴다. 문서 값이 그대로 화면에 나온다 —
        // `FontScale` 로 나눠 두었기 때문에 배율을 어떻게 두든 결과는 문서와 같다.
        public static int Display => Px(34);   // 화면에 하나뿐인 제목
        public static int Title   => Px(22);   // 다이얼로그 제목 · 여왕 이름
        public static int Value   => Px(20);   // 칩 안의 숫자
        public static int Body    => Px(16);   // 본문 · 버튼 글씨
        public static int LabelPt => Px(14);   // 카드 라벨 · 스탯 이름
        public static int Caption => Px(12);   // 보조 설명 · 단위
        public static int Tagline => Px(11);   // 제목 밑 한 줄
        public static int Micro   => Px(10);   // 버전 · 각주. **이보다 작게 쓰지 않는다**
        /// <summary>
        /// 문서의 크기(px)를 **`FontScale` 로 나눠** 둔다. 그리고 <see cref="Label"/> 이 그릴 때
        /// **다시 곱한다** — 즉 **둘은 서로를 지운다.** `FontScale` 을 어떻게 두든 최종 크기는
        /// 문서의 수 그대로다.
        ///
        /// ⚠ **그래서 `FontScale` 을 올려도 글자가 커지지 않는다.** 이 프로젝트의 캔버스는
        /// 1080×1920 이고 좀비퀸은 420×900 이라 같은 상수가 2.13 배 작게 뜨는데, 여기서
        /// `FontScale` 을 올려 고치려다 **`Body` 가 16 → 6 으로 줄어든 값만 로그에 찍혔다**
        /// (2026-10-09). 키우는 자리는 `Meta/MetaFlow.F()` 다 — <see cref="Label"/> 에
        /// **넘기는 크기 자체**를 키운다.
        /// </summary>
        static int Px(int docSize) => Mathf.Max(1, Mathf.RoundToInt(docSize / Mathf.Max(0.01f, FontScale)));

        // ==== 여백 단계 ====
        // 4 의 배수로만 띄운다. 6·7·9 px 이 섞이면 줄이 안 맞는 게 눈에 딱 보이지는 않으면서 지저분해진다.
        public const float Sp1 = 4f, Sp2 = 8f, Sp3 = 12f, Sp4 = 16f, Sp5 = 24f, Sp6 = 32f;

        public const float RadiusPanel = 20f;
        /// <summary>카드 (UI_STYLE 4)</summary>
        public const float RadiusCard = 14f;
        /// <summary>버튼·알약</summary>
        public const float RadiusButton = 16f;
        /// <summary>정사각 타일 (하단 탭·뒤로)</summary>
        public const float RadiusTile = 12f;
        /// <summary>칩·배지</summary>
        public const float RadiusChip = 12f;
        /// <summary>바·트랙처럼 얇은 것</summary>
        public const float RadiusBar = 7f;

        /// <summary>고정 px 반경으로. `height` 를 주면 그 절반을 넘지 않게 깎는다 (얇은 요소에서 코너가 겹치지 않게)</summary>
        public static Image SetRadius(Image img, float px, float height = 0f)
        {
            if (img == null) return img;
            if (height > 0f) px = Mathf.Min(px, height * 0.5f);
            img.pixelsPerUnitMultiplier = SpriteCorner / Mathf.Max(1f, px);
            return img;
        }

        static Sprite circle;
        /// <summary>런타임 원 스프라이트 (조이스틱·둥근 점) — 이미지 에셋 없이</summary>
        public static Sprite Circle
        {
            get
            {
                if (circle != null) return circle;
                const int N = 128;
                var tex = new Texture2D(N, N, TextureFormat.RGBA32, false);
                var px = new Color32[N * N];
                float r = N / 2f - 1f;
                for (int y = 0; y < N; y++) for (int x = 0; x < N; x++)
                {
                    float d = Mathf.Sqrt((x + 0.5f - N / 2f) * (x + 0.5f - N / 2f) + (y + 0.5f - N / 2f) * (y + 0.5f - N / 2f));
                    float a = Mathf.Clamp01(r - d + 0.5f); // 1px 안티에일리어싱
                    px[y * N + x] = new Color32(255, 255, 255, (byte)(a * 255));
                }
                tex.SetPixels32(px); tex.Apply(false, true);
                circle = Sprite.Create(tex, new Rect(0, 0, N, N), new Vector2(0.5f, 0.5f), 100f);
                return circle;
            }
        }

        static Sprite rounded;
        /// <summary>런타임 둥근 사각형 (9-slice, 반지름 24px @64) — 캡슐 HUD (VISUAL §8 스타일 참고)</summary>
        public static Sprite Rounded
        {
            get
            {
                if (rounded != null) return rounded;
                const int N = 64; const float R = 24f;
                var tex = new Texture2D(N, N, TextureFormat.RGBA32, false);
                var px = new Color32[N * N];
                for (int y = 0; y < N; y++) for (int x = 0; x < N; x++)
                {
                    float cx = Mathf.Clamp(x + 0.5f, R, N - R), cy = Mathf.Clamp(y + 0.5f, R, N - R);
                    float d = Mathf.Sqrt((x + 0.5f - cx) * (x + 0.5f - cx) + (y + 0.5f - cy) * (y + 0.5f - cy));
                    float a = Mathf.Clamp01(R - d + 0.5f);
                    px[y * N + x] = new Color32(255, 255, 255, (byte)(a * 255));
                }
                tex.SetPixels32(px); tex.Apply(false, true);
                rounded = Sprite.Create(tex, new Rect(0, 0, N, N), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, new Vector4(R, R, R, R));
                return rounded;
            }
        }

        /// <summary>캡슐 패널: 어두운 반투명 + 밝은 외곽선 (두 장 겹침)</summary>
        public static Image Capsule(string name, Transform parent, Color fill, Color outline, float border = 2f)
        {
            var o = Panel(name, parent, outline);
            o.sprite = Rounded; o.type = Image.Type.Sliced; SetRadius(o, RadiusCard);
            var f = Panel("fill", o.transform, fill);
            f.sprite = Rounded; f.type = Image.Type.Sliced; SetRadius(f, RadiusCard);
            Stretch(f.rectTransform, border);
            return o;
        }

        /// <summary>오너 시안에서 잘라낸 HUD 아트 (Resources/Owner/UI/hud_*.png) — 없으면 null → 글리프 폴백</summary>
        public static RawImage Art(string name, Transform parent, string file, Vector2 anchor, Vector2 pos, Vector2 size)
        {
            var tex = Resources.Load<Texture2D>("Owner/UI/" + file);
            if (tex == null) return null;
            var rt = Rect(name, parent);
            var ri = rt.gameObject.AddComponent<RawImage>();
            ri.texture = tex; ri.raycastTarget = false;
            Place(rt, anchor, pos, size);
            return ri;
        }

        static Sprite star;
        /// <summary>5각 별 (오너 2026-09-18 클리어 표시 · 2026-09-30 모양 교정) — 골이 깊고 꼭짓점은 둥근 형태</summary>
        public static Sprite Star
        {
            get
            {
                if (star != null) return star;
                const int N = 128;
                var tex = new Texture2D(N, N, TextureFormat.RGBA32, false);
                var px = new Color32[N * N];
                float cx = N / 2f, cy = N / 2f;
                for (int y = 0; y < N; y++) for (int x = 0; x < N; x++)
                {
                    float dx = x + 0.5f - cx, dy = y + 0.5f - cy;
                    float r = Mathf.Sqrt(dx * dx + dy * dy) / (N / 2f - 2f);
                    float a = Mathf.Atan2(dy, dx) - Mathf.PI / 2f;          // 꼭짓점이 위로
                    float wave = Mathf.Cos(5f * a);                          // 5각
                    // **골이 깊어야 별이다** (오너 2026-09-30 "별이 클로버 모양이다").
                    // 예전 값(안쪽 0.62 · 지수 0.55)은 골을 메워 꽃잎 다섯 장이 됐다 — 지수가 1 보다 작으면
                    // 파형이 위로 부풀어 골이 얕아진다. 안쪽을 0.45 로 내리고 지수를 2.0 으로 올려 골을 판다.
                    // 뾰족해지지 않는 이유는 아래 가장자리 흐림(0.10)이 꼭짓점을 둥글게 깎기 때문이다 — 라운드된 별.
                    // 지수가 **1 보다 커야** 골이 파인다. 0.55 였을 때는 파형이 위로 부풀어 골이 메워졌다.
                    // 2.0 까지 올리면 별이 되긴 하는데 꼭짓점이 바늘처럼 가늘어진다 — 1.2 가 굵은 다섯 갈래다.
                    // 안쪽 0.55 는 가운데를 채워 "라운드된 별" 로 읽히게 한다 (뾰족한 별은 안쪽이 0.38 근처).
                    float t = (wave + 1f) * 0.5f;                             // 0 = 골, 1 = 꼭짓점
                    float edge = 0.55f + 0.45f * Mathf.Pow(t, 1.2f);
                    float alpha = Mathf.Clamp01((edge - r) * (N / 2f) * 0.11f);
                    px[y * N + x] = new Color32(255, 255, 255, (byte)(alpha * 255));
                }
                tex.SetPixels32(px); tex.Apply(false, true);
                star = Sprite.Create(tex, new Rect(0, 0, N, N), new Vector2(0.5f, 0.5f), 100f);
                return star;
            }
        }

        static readonly System.Collections.Generic.Dictionary<string, Sprite> artSprites = new System.Collections.Generic.Dictionary<string, Sprite>();
        /// <summary>오너 HUD 아트를 Image(스프라이트)로 — Filled(HP·충전 바) / Button 타깃이 필요할 때. 없으면 null</summary>
        public static Image ArtImage(string name, Transform parent, string file, Vector2 anchor, Vector2 pos, Vector2 size)
        {
            var sp = ArtSprite(file);
            if (sp == null) return null;
            var img = Panel(name, parent, Color.white);
            img.sprite = sp; img.type = Image.Type.Simple;
            Place(img.rectTransform, anchor, pos, size);
            return img;
        }
        public static Sprite ArtSprite(string file)
        {
            if (artSprites.TryGetValue(file, out var cached)) return cached;
            var tex = Resources.Load<Texture2D>("Owner/UI/" + file);
            var sp = tex == null ? null : Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f);
            artSprites[file] = sp;
            return sp;
        }
        public static bool HasArt(string file) => ArtSprite(file) != null;

        /// <summary>한 줄 체이닝: rt.Let(r => Place(r, …))</summary>
        public static RectTransform Let(this RectTransform rt, System.Action<RectTransform> f) { f(rt); return rt; }

        /// <summary>
        /// **그림 위에 글자를 얹을 때** 깔아 주는 어두운 띠. 도시 지도·시안 배경은 밝은 곳과 어두운 곳이
        /// 섞여 있어서, 같은 흰 글자가 어떤 자리에서는 읽히고 어떤 자리에서는 사라진다.
        /// 글자 색을 바꾸는 대신 **바탕을 눌러** 어디서나 같게 읽히게 한다 (오너 2026-09-24 "구분이 쉬워야 한다").
        /// </summary>
        public static Image Scrim(string name, Transform parent, float alpha = 0.55f)
        {
            var img = Panel(name, parent, new Color(0.06f, 0.05f, 0.09f, alpha));
            img.sprite = Rounded; img.type = Image.Type.Sliced; SetRadius(img, RadiusCard);
            img.raycastTarget = false;
            img.transform.SetAsFirstSibling();
            return img;
        }

        /// <summary>좀비퀸의 `Core.MathUtil.Hex` 를 여기로 옮겼다 — 그것 하나 때문에 Core 를 끌고 오지 않는다</summary>
        static Color HexToColor(int hex)
            => new Color(((hex >> 16) & 0xFF) / 255f, ((hex >> 8) & 0xFF) / 255f, (hex & 0xFF) / 255f, 1f);

        public static Color Hex(int hex, float a = 1f)
        {
            var c = HexToColor(hex); c.a = a; return c;
        }

        /// <summary>둥근 모서리 흉내: 스프라이트 없이 그냥 색판 (나중에 9-slice 로 교체)</summary>
        public static readonly Color PanelBg = new Color(46f / 255f, 36f / 255f, 58f / 255f, 0.8f);
        public static readonly Color Text = Hex(0xf2f4ff);
        public static readonly Color Dim = Hex(0x9aa3c7);
        public static readonly Color Green = Hex(0x8fe0a0);
        public static readonly Color Red = Hex(0xf29ab0);   // 위험 표시도 핑크 — 붉은색 금지
        public static readonly Color Yellow = Hex(0xf6d97a);
        public static readonly Color Purple = Hex(0xc9a8f0);
        public static readonly Color Pink = Hex(0xf48fb1);

        // ---- 시안 CUTE APOCALYPSE 팔레트 (docs/UI_STYLE.md 2 · zombieQ_imgset8 에서 직접 추출) ----
        //
        // 지금 화면은 어두운 보라 위에 밝은 글자다. 시안은 **반대**다 — 따뜻한 크림 판 위에 검은 글자,
        // 어두운 것은 상태를 읽는 작은 칩뿐. 그래서 글자색도 한 쌍이 아니라 **표면마다 한 쌍**이다:
        // 크림 위에서는 `OnCream`/`OnCreamDim`, 어두운 칩 위에서는 `OnChip`/`OnChipDim`.
        // 화면을 한꺼번에 뒤집지 않으려고 기존 `Text`/`Dim` 은 그대로 둔다 (UI_STYLE 12 의 단계 순서).

        /// <summary>카드 바탕 — 화면에서 가장 밝은 면</summary>
        public static readonly Color Cream = Hex(0xf6e6d5);
        /// <summary>카드 안쪽 홈 (‹ 값 › 같은 인셋)</summary>
        public static readonly Color CreamDeep = Hex(0xe6d7c8);
        /// <summary>카드가 올라앉는 바닥 — 카드보다 **어둡다**. 이 대비가 시안의 뼈대다</summary>
        public static readonly Color CreamPanel = Hex(0xd2c0af);
        /// <summary>종이·간판</summary>
        public static readonly Color Paper = Hex(0xfde7d4);
        /// <summary>상태 칩 (ARMY·HUMANS·ALERT)</summary>
        public static readonly Color Chip = Hex(0x232026);
        /// <summary>넓은 어두운 면 (여왕 스탯 패널) — 순검정보다 따뜻하게</summary>
        public static readonly Color ChipSoft = Hex(0x372c29);

        /// <summary>
        /// 간다 — PLAY·출격·다음. **초록이다.**
        ///
        /// 시안(imgset8·9)은 이 자리에 분홍을 쓰지만 오너가 두 번 못 박았다:
        /// 2026-09-23 오전 "모든 걸 좀비화 시키는 게 목적이니 primary 버튼들도 좀비 초록으로",
        /// 같은 날 imgset9 를 주면서 "**하지만 초록 색감은 유지해야 한다** — 이 이미지는 핑크로 되어 있어".
        /// 그래서 시안에서 분홍인 자리는 전부 이 색으로 바꿔 읽는다 (강조 알약·선택된 탭·채움 게이지 포함).
        /// </summary>
        public static readonly Color Go = Hex(0x4fb63f);
        /// <summary>주 버튼 아래 그림자 테두리 — 입체감은 그림자가 아니라 이 한 줄에서 나온다 (오너가 보낸 UPGRADE 버튼)</summary>
        public static readonly Color GoEdge = Hex(0x2e7f26);
        /// <summary>초록 위의 글자 — 흰색은 초록에서 안 읽힌다 (대비 2:1). 오너 시안도 진한 잉크다</summary>
        public static readonly Color OnGo = Hex(0x14320f);

        // ---- 여왕의 방 (시안 imgset9) — 크림 마을과 달리 **어둡고 빛난다** ----
        // 크림은 바깥(설정·월드맵·군단), 이 어두운 한 벌은 퀸 영역(프로필·스킬·기술·외형·이야기) 전용이다.
        /// <summary>여왕 영역 바닥</summary>
        public static readonly Color Night = Hex(0x1a1720);
        /// <summary>여왕 영역 카드</summary>
        public static readonly Color NightCard = Hex(0x26222f);
        /// <summary>여왕 영역 카드 안쪽 홈 · 비활성 행</summary>
        public static readonly Color NightWell = Hex(0x322c3d);
        public static readonly Color NightLine = Hex(0x3a3346);
        /// <summary>어두운 바탕에서 빛나는 강조 — 시안의 분홍 자리 (오너 지시로 초록)</summary>
        public static readonly Color Accent = Hex(0x7fd96a);
        /// <summary>좋아진다 — 강화·구매·확인·토글 ON</summary>
        public static readonly Color Confirm = Hex(0x61bc4f);
        /// <summary>보조 버튼</summary>
        public static readonly Color Normal = Hex(0xefcdac);
        public static readonly Color DisabledBg = Hex(0x6c6366);
        public static readonly Color DisabledFg = Hex(0x9a9397);
        /// <summary>위험 — **아이콘과 한 자리 숫자에만**. 넓은 면 금지 (UI_STYLE 0 C)</summary>
        public static readonly Color Alert = Hex(0xe2453c);
        public static readonly Color Gold = Hex(0xf2c14e);
        public static readonly Color Gem = Hex(0xb65fd6);

        public static readonly Color OnCream = Hex(0x231f1d);
        public static readonly Color OnCreamDim = Hex(0x7a6a63);
        public static readonly Color OnChip = Hex(0xfbf4ec);
        public static readonly Color OnChipDim = Hex(0xb8ada6);

        static readonly System.Collections.Generic.Dictionary<string, Sprite> icons = new System.Collections.Generic.Dictionary<string, Sprite>();
        /// <summary>
        /// 시안 아이콘 (`Resources/UI/icon/<name>`). 없으면 **null** — 부르는 쪽이 글자로 물러선다.
        ///
        /// `Sprite` 가 아니라 `Texture2D` 로 읽고 런타임에 스프라이트를 만든다: 텍스처 임포트 설정(.meta)에
        /// 의존하지 않으려는 것이다. 유니티 에디터를 다른 세션이 잡고 있어도 파일만 넣으면 바로 보인다.
        /// </summary>
        public static Sprite IconSprite(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            if (icons.TryGetValue(name, out var cached)) return cached;
            var sp = LoadIcon(name);
            if (sp == null && Alias.TryGetValue(name, out var older)) sp = LoadIcon(older);
            icons[name] = sp;
            return sp;
        }

        /// <summary>
        /// `UI/icon` 을 먼저, 없으면 `UI/img` 를 본다. 작은 글리프와 큰 그림(연출 컷·코스툼·이야기)을
        /// 폴더로 나눠 두되 **부르는 쪽은 이름만 알면 되게** 한다 — 어느 폴더에 들어갔는지까지 외울 이유가 없다.
        /// </summary>
        static Sprite LoadIcon(string file)
        {
            var tex = Resources.Load<Texture2D>("UI/icon/" + file) ?? Resources.Load<Texture2D>("UI/img/" + file);
            return tex == null ? null : Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f);
        }

        /// <summary>
        /// 새 이름 -> 예전 이름. 2026-09-23 오너가 `zombieQ_imgset10`("UI ASSET PACK") 을 주면서 이름 체계가 바뀌었다.
        /// 화면은 **새 이름만** 부르고, 새 그림이 아직 안 들어온 자리는 imgset8 에서 잘라 둔 옛 그림으로 버틴다 —
        /// 이름을 한꺼번에 바꾸면 도착 전까지 화면이 통째로 도형으로 주저앉는다.
        /// 새 팩이 다 들어오면 이 표는 지운다.
        /// </summary>
        static readonly System.Collections.Generic.Dictionary<string, string> Alias = new System.Collections.Generic.Dictionary<string, string>
        {
            { "nav_world", "worldmap" }, { "nav_horde", "zombie" }, { "nav_queen", "queen" }, { "nav_shop", "shop" },
            { "nav_map", "worldmap" }, { "nav_mission", "mission" },
            { "res_brain", "skill" }, { "res_gem", "gem" }, { "res_gold", "coin" }, { "sys_skillpoint", "skill" },
            { "sys_achieve", "rank" }, { "set_gear", "settings" },
            { "tab_profile", "queen" }, { "tab_skill", "skill" }, { "tab_tech", "technique" },
            { "tab_looks", "costume" }, { "tab_story", "mission" },
            { "br_queen", "queen" }, { "br_horde", "zombie" }, { "br_instinct", "infect" },
            { "br_command", "rank" }, { "br_royal", "queen" },
            { "st_hp", "hp" }, { "st_atk", "atk" }, { "st_spd", "spd" }, { "st_range", "range" },
            { "st_cmd", "range" }, { "st_def", "def" },
            { "z_normal", "human" }, { "z_brute", "zombie" }, { "z_runner", "spd" },
            { "z_medic", "heal" }, { "z_armored", "def" }, { "z_special", "infect" },
            { "ui_alert", "alert" }, { "ui_like", "hp" }, { "ui_music2", "ui_music" },
            { "ui_lock", "lock" }, { "ui_check", "heal" },
            { "set_sfx", "ui_sfx" }, { "set_music", "ui_music" }, { "set_haptic", "ui_vibration" },
            { "set_control", "ui_control" }, { "set_graphics", "ui_graphics" },
            { "set_lang", "ui_language" }, { "set_account", "ui_account" }, { "set_notify", "ui_notify" },
            { "map_lock", "lock" }, { "map_infect", "infect" },
            { "ic_mission", "mission" }, { "ic_rank", "rank" }, { "ic_mail", "mail" },
            { "ic_attend", "attendance" }, { "ic_book", "skill" },
            { "res_gift", "reward" }, { "res_meat", "heal" },
        };

        /// <summary>
        /// **글리프로 대체하는 이름들** (Material Icons 코드포인트).
        ///
        /// 고른 기준은 "작고 기능적인 것" 이다 — 설정 줄 · 스탯 · 스킬 계통 · 하단 탭 · 자물쇠 · 별 · 경고.
        /// 이 자리들은 화면에서 18~30 units 라 래스터가 가장 심하게 깨지고, 동시에 **화풍을 지고 있지 않다**.
        /// 여기 없는 이름(`tech_*` `techcut_*` `look_*` `story_*` `it_*` `z_*` `fx_*` `map_<도시>`)은
        /// 오너 팩 그림 그대로다.
        ///
        /// **글꼴에 없는 코드포인트는 쓸 수 없다.** 받아 온 `codepoints` 목록은 2,234 개인데
        /// 채워진 기본판에 실제로 든 건 1,552 개다 — `diamond` `workspace_premium` `hub` `paid` 는
        /// 목록에만 있고 글꼴에는 없어서, 그대로 쓰면 화면에 **빈칸**이 나온다(그림이 깨지는 것보다
        /// 나쁘다 — 자리조차 안 보인다). `tools/docsync/glyphs.py` 가 이걸 막는다.
        ///
        /// 넷 중 둘(`hub` `paid`)은 **같은 그림이 옛 이름으로 들어 있어서** 그쪽을 쓴다.
        /// 나머지 둘 — **왕관과 보석** — 은 비슷한 것도 없어서 **직접 그려 넣었다**
        /// (오너 2026-09-24 "없는 부분들은 네가 그릴 수도 있잖아, 아이콘셋 유지하면서").
        /// `tools/artcut/glyphdraw.py` 가 같은 규격(24 dp 격자 · upem 512 · 전진폭 512)으로 그려
        /// `ZQIcons.ttf` 를 굽는다. 코드포인트는 **원래 그 아이콘이 가졌을 번호**를 그대로 쓰므로,
        /// 나중에 최신판 글꼴로 갈아도 자리가 맞는다.
        ///
        /// 한 아이콘을 두 이름이 같은 코드포인트로 쓰는 건 일부러다 — `res_brain` 과 `sys_skillpoint` 는
        /// 게임 안에서 같은 것(뇌)이고, 다른 그림을 주면 다른 자원으로 읽힌다.
        /// </summary>
        static readonly System.Collections.Generic.Dictionary<string, int> Glyphs = new System.Collections.Generic.Dictionary<string, int>
        {
            // 설정 (UI_UX §9 설정 화면의 여덟 줄)
            { "set_gear", 0xe8b8 }, { "set_sfx", 0xe050 }, { "set_music", 0xe405 }, { "set_haptic", 0xe62d },
            { "set_control", 0xe30f }, { "set_graphics", 0xe3f4 }, { "set_lang", 0xe894 },
            { "set_notify", 0xe7f4 }, { "set_account", 0xe7fd },
            // 스탯 (프로필 · 레벨업 화면)
            { "st_hp", 0xe87d }, { "st_atk", 0xea0b }, { "st_spd", 0xe9e4 },
            { "st_range", 0xe55c }, { "st_cmd", 0xe1e2 }, { "st_def", 0xe9e0 },
            // 스킬 계통 다섯
            { "br_queen", 0xe7af }, { "br_horde", 0xf233 }, { "br_instinct", 0xf221 },
            { "br_command", 0xe335 }, { "br_royal", 0xea3f },
            // 하단 탭 · 퀸 탭
            { "nav_world", 0xe80b }, { "nav_horde", 0xf233 }, { "nav_queen", 0xe7fd },
            { "nav_shop", 0xea12 }, { "nav_map", 0xe55b }, { "nav_mission", 0xe85d },
            { "tab_profile", 0xe7fd }, { "tab_skill", 0xea4a }, { "tab_tech", 0xe65f },
            { "tab_looks", 0xf19e }, { "tab_story", 0xea19 },
            // 자원 · 시스템
            { "res_brain", 0xea4a }, { "res_gem", 0xead5 }, { "res_gold", 0xe263 },
            { "sys_skillpoint", 0xea4a }, { "sys_achieve", 0xea23 },
            // 상태
            { "ui_lock", 0xe897 }, { "map_lock", 0xe897 }, { "ui_alert", 0xef49 },
            { "map_infect", 0xf221 }, { "ui_check", 0xe5ca },
            { "star_on", 0xe838 }, { "star_off", 0xe83a }, { "star_half", 0xe839 },
        };

        /// <summary>이 이름이 글리프로 나가는가 (폰트가 안 들어왔으면 아니다 — 그림으로 물러선다)</summary>
        public static bool IsGlyph(string name) =>
            PreferGlyphs && !string.IsNullOrEmpty(name) && Glyphs.ContainsKey(name) && IconFont != null;

        /// <summary>
        /// 아이콘 한 장. 없으면 null — 부르는 쪽이 점(Dot)이나 머리글자로 물러선다.
        ///
        /// `tint` 는 **그림에 곱해지는 색**이라 그림을 그대로 쓰려는 자리는 흰색을 넘긴다.
        /// 그런데 같은 흰색이 글리프에는 "흰 글자" 라서, 크림색 종이 위에서는 사라진다.
        /// 그래서 `glyphTint` 가 따로 있다 — **글리프일 때만** 쓰는 색. 안 주면 `tint` 를 쓴다.
        /// (크림 위에 올라가는 네 자리만 이걸 넘긴다: 결과 별·보상 칩, 지도 진행 칩·스테이지 칩)
        /// </summary>
        public static Graphic Icon(string name, Transform parent, float size, Vector2 anchor, Vector2 pos,
                                   Color? tint = null, Color? glyphTint = null)
        {
            // **그림이 먼저, 글리프는 대체.** 예전엔 글리프를 먼저 봐서 팩과 글리프 양쪽에 있는 43 개 이름(별·탭·설정 줄·스탯…)이
            // 전부 각진 Material 글리프로 나왔다 — 오너 2026-09-27 "전체 아이콘 둥글둥글하게, 별은 뚱뚱하게": 그 그림이 팩에 있었다
            var early = IconSprite(name);
            if (early != null)
            {
                var im = Panel("icon", parent, tint ?? Color.white);
                im.sprite = early; im.type = Image.Type.Simple; im.preserveAspect = true;
                Place(im.rectTransform, anchor, pos, new Vector2(size, size));
                return im;
            }
            if (IsGlyph(name))
            {
                var rt = Rect("icon", parent);
                var t = rt.gameObject.AddComponent<Text>();
                t.font = IconFont;
                // 글리프는 칸을 가득 채우므로 FontScale(글자용 여백 보정)을 타지 않는다
                t.fontSize = Mathf.Max(8, Mathf.RoundToInt(size));
                t.text = char.ConvertFromUtf32(Glyphs[name]);
                t.color = glyphTint ?? tint ?? Color.white;
                t.alignment = TextAnchor.MiddleCenter;
                t.horizontalOverflow = HorizontalWrapMode.Overflow;
                t.verticalOverflow = VerticalWrapMode.Overflow;
                t.raycastTarget = false;
                // 테두리는 안 붙인다 — 채워진 도형에 1.2 px 그림자를 두르면 작은 크기에서 획이 엉킨다
                Place(rt, anchor, pos, new Vector2(size, size));
                return t;
            }
            var sp = IconSprite(name);
            if (sp == null) return null;
            var img = Panel("icon", parent, tint ?? Color.white);
            img.sprite = sp; img.type = Image.Type.Simple; img.preserveAspect = true;
            Place(img.rectTransform, anchor, pos, new Vector2(size, size));
            return img;
        }
        public static bool HasIcon(string name) => IsGlyph(name) || IconSprite(name) != null;

        static readonly System.Collections.Generic.Dictionary<string, Sprite> frames = new System.Collections.Generic.Dictionary<string, Sprite>();
        /// <summary>
        /// 9-slice 프레임 (`Resources/UI/frame/<name>`) — 오너 팩의 패널·말풍선·버튼 배경.
        ///
        /// `.meta` 에 border 가 박혀 있으면 그걸 쓰고, 없으면 **가장자리 1/4 을 테두리로 가정**한다.
        /// 프레임이 아직 없으면 null — 부르는 쪽이 예전 캡슐로 물러선다. 그림이 도착하면 **코드 수정 없이** 바뀐다.
        /// </summary>
        public static Sprite FrameSprite(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            if (frames.TryGetValue(name, out var cached)) return cached;
            var sp = Resources.Load<Sprite>("UI/frame/" + name);
            if (sp == null)
            {
                var tex = Resources.Load<Texture2D>("UI/frame/" + name);
                if (tex != null)
                {
                    float b = Mathf.Min(tex.width, tex.height) * 0.25f;
                    sp = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f,
                                       0, SpriteMeshType.FullRect, new Vector4(b, b, b, b));
                }
            }
            frames[name] = sp;
            return sp;
        }

        /// <summary>
        /// 패널 한 장. `file` 프레임이 있으면 그 그림으로, 없으면 `fill` + `outline` 캡슐로.
        /// 화면 코드는 둘을 구분하지 않는다 — 그래서 아트가 도착해도 레이아웃을 다시 안 짠다.
        /// </summary>
        public static Image Frame(string name, Transform parent, string file, Color fill, Color outline, float border = 1.5f, float radius = RadiusCard)
        {
            var sp = FrameSprite(file);
            if (sp == null)
            {
                var cap = Capsule(name, parent, fill, outline, border);
                SetRadius(cap, radius);
                var f = cap.transform.Find("fill");
                if (f != null) SetRadius(f.GetComponent<Image>(), radius);
                return cap;
            }
            var img = Panel(name, parent, Color.white);
            img.sprite = sp; img.type = Image.Type.Sliced;
            return img;
        }
    }
}
