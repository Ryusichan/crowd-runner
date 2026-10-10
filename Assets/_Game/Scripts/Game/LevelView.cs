using CrowdRunner.Core;
using CrowdRunner.Game;
using CrowdRunner.Crowd;
using UnityEngine;

namespace CrowdRunner.View
{
    /// <summary>
    /// **시뮬이 정한 수를 보여 주기만 한다** — `DESIGN.md` §2 의 "표현" 쪽.
    ///
    /// 여기에는 규칙이 없다. 병력이 몇인지, 게이트가 무엇을 하는지, 누가 이겼는지는 전부
    /// `Core/Sim` 이 정하고 이 파일은 **읽는다**. 그래서 이 파일을 통째로 버려도 게임은 돈다
    /// (`tools/sim` 이 그 증거다) — 그것이 그 선을 그은 이유다.
    ///
    /// 읽는 것: `sim.Units`(그릴 수) · `sim.Z`·`sim.X`(어디) · `sim.Side`(갇힌 차선) ·
    /// `sim.State` · `sim.BlockingEnemies`·`sim.BlockingWallHp`·`sim.BlockingZ`(막은 것) ·
    /// `sim.Last`(이 틱의 사건 — **한 틱만 유효**하므로 `LevelRunner.Consume` 이 틱마다 넘겨준다).
    ///
    /// 군중은 **방식 B 저폴리**로 그린다 (`docs/M0_CROWD.md` §6b): 드로우 1 회 · 상한 400.
    /// 방식 A(유닛마다 `Animator`)는 60 개체에서 예산이 끝나 이 장르가 못 된다.
    /// </summary>
    [RequireComponent(typeof(LevelRunner))]
    public sealed class LevelView : MonoBehaviour
    {
        /// <summary>
        /// 화면에 그릴 상한 (`LevelData.RenderCap` 과 같은 수). 병력이 이보다 많아도 **수는 맞게
        /// 보여 주고 몸만 상한까지** 그린다 — 400 과 600 의 차이는 화면에서 구분되지 않고,
        /// 구분되지 않는 것에 프레임을 쓰면 **구분되는 것이 사라진다**.
        /// </summary>
        public int renderCap = 400;

        LevelRunner runner;
        CrowdField allies, foes;
        VatCrowd allyView, foeView;
        Transform road, wall, divider, ground, curbL, curbR, wallL, wallR;
        Transform[] gates = new Transform[0];
        /// <summary>게이트 네모마다의 **z 와 차선** — 지났을 때 *어느 것이 고른 것인지* 를
        /// 찾으려면 필요하다. `Transform.position` 에서 되읽지 않는 이유: 연출이 그 위치를
        /// 움직이므로 연출 중에 되읽으면 **같은 게이트를 두 번 고르거나 못 찾는다**</summary>
        float[] gateZ = new float[0];
        int[] gateLane = new int[0];
        readonly Transform[] refusedBuf = new Transform[4];
        GatePassFx fxGate;

        /// <summary>
        /// 게이트 응답을 몇 번 띄웠나 — **주행이 화면 대신 묻는다.** 게이트를 네 번 지났는데
        /// 응답이 두 번이면 플레이어는 *어떤 선택에는 반응이 있고 어떤 선택에는 없는* 게임을
        /// 본다. 그 증상은 PNG 한 장으로는 절대 안 잡힌다 (한 순간만 담기므로).
        /// </summary>
        public int GatePops => fxGate != null ? fxGate.Popped : 0;
        Transform[] zones = new Transform[0];
        UnityEngine.UI.Text countUi;
        CasualtyFx fx;
        RoadMarks marks;
        float spreadNow = -1f;
        BlobShadows shadows;
        Camera cam;
        float wallHp0;

        void Awake()
        {
            runner = GetComponent<LevelRunner>();
            allies = new CrowdField(Sim.MaxUnits, 20261009);
            foes = new CrowdField(Sim.MaxUnits, 77215);
            allyView = new VatCrowd("_lo");
            foeView = new VatCrowd("_lo");
            allyView.Init(renderCap); foeView.Init(renderCap);
            allyView.SetActive(true); foeView.SetActive(true);
            fx = new CasualtyFx(); fx.Init();
            marks = new RoadMarks();
            shadows = new BlobShadows(); shadows.Init();
            fxGate = new GatePassFx(); fxGate.Init(Label);
            runner.OnTick += OnTick;
            BuildScene();
            // **늦게 붙었을 수도 있다.** `LevelRunner.Awake` 가 먼저 돌면 그때 뷰가 없어서
            // `OnLevelLoaded` 를 놓친다 — 이미 떠 있는 판이 있으면 여기서 받아 간다
            if (runner.Level != null) OnLevelLoaded(runner.Level);
        }

        void BuildScene()
        {
            cam = Camera.main;
            if (cam == null)
            {
                var go = new GameObject("MainCamera");
                go.tag = "MainCamera";
                cam = go.AddComponent<Camera>();
            }
            // 세로형 러너 — 뒤에서 내려다본다. 각도가 **겹쳐 그리기**를 정하므로 (§6b 마지막 경고)
            // 더 눕히면 군중이 화면에 더 넓게 퍼져 GPU 가 싸진다. 지금은 보이는 쪽을 먼저 맞춘다
            // 34° 였는데 군단이 화면 **중간**에 앉고 그 아래가 빈 아스팔트였다. 조금 세우면
            // (= 숙이는 각을 줄이면) 화면의 것들이 전부 아래로 내려오고, 앞쪽이 더 보인다
            cam.transform.rotation = Quaternion.Euler(30f, 0f, 0f);
            cam.fieldOfView = 52f;
            cam.farClipPlane = 300f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.62f, 0.72f, 0.80f);

            // **주변광을 못 박는다.** 기본값은 하늘(skybox) 기반인데 이 장면에는 하늘이 없어
            // 엔진 기본 하늘을 샘플링한다 — 그 결과 **모든 것이 밝아지고 하늘색으로 물든다.**
            // 첫 촬영에서 길(올리브 0.46)이 **흰색에 가까운 하늘빛**으로 나온 것이 그것이다.
            // 코드로 세우는 장면은 하늘이 없으므로 평평한 주변광이 맞다 — 그래야 준 색이 그 색으로 나온다
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.22f, 0.23f, 0.26f);

            var li = new GameObject("Sun").AddComponent<Light>();
            li.transform.SetParent(transform, false);
            li.type = LightType.Directional;
            li.intensity = 0.85f;
            li.shadows = LightShadows.None;   // 400 개체의 그림자는 드로우를 두 배로 만든다 (§6b)
            li.transform.rotation = Quaternion.Euler(52f, -28f, 0f);

            // 색은 **대비로** 고른다. 전에는 땅 0.30/0.38/0.28, 길 0.46/0.50/0.44 로 둘 다
            // 어두운 회녹색이라 화면이 한 톤이었고, 그 위의 군중(어두운 몸)도 묻혔다.
            // 지금은 **풀(녹) · 아스팔트(회) · 연석(흰)** 셋이 밝기로 갈린다 — 색상환이
            // 아니라 **밝기**로 가르는 이유: 작은 화면에서 먼저 읽히는 것이 밝기다
            // **땅이 있어야 길이 길로 보인다.** 길만 그리면 하늘색 위에 떠 있는 리본이고,
            // 그러면 군단이 *어디를* 달리는지가 없다. 땅 한 장 + 연석 둘이면 끝난다 —
            // 광고형 러너가 옆을 비워 두지 않는 이유다
            ground = Box("Ground", new Color(0.42f, 0.55f, 0.35f));
            road = Box("Road", new Color(0.52f, 0.52f, 0.55f));
            // 연석은 길의 **가장자리를 말한다**. 갇힌 차선 칸막이(`divider`)와 달리 늘 서 있고,
            // 밝아서 길의 폭이 한눈에 읽힌다 — 폭이 좁아지는 구간이 눈에 걸려야 한다
            curbL = Box("Curb", new Color(0.92f, 0.90f, 0.84f));
            curbR = Box("Curb", new Color(0.92f, 0.90f, 0.84f));

            // **벽으로 닫는다** (`docs/REF_TOPWAR.md` §2③). 들판 가운데 열린 길은 *어디로
            // 가야 하는지* 는 말하지만 **갇혀 있다** 는 말을 못 한다. 벽이 서면 좌우가 끝이라는
            // 것이 모양으로 읽히고, 군중이 벽에 닿는 것이 *꽉 찼다* 를 만든다 — 참고한 광고에서
            // 군중이 **벽에서 벽까지** 차 있는 것이 그 장르의 핵심 그림이다.
            //
            // 길보다 **밝게** 둔다: 어두우면 화면 양끝이 검은 띠가 되어 세로 화면이 더 좁아 보인다
            wallL = Box("Wall", new Color(0.62f, 0.60f, 0.58f));
            wallR = Box("Wall", new Color(0.62f, 0.60f, 0.58f));
        }

        /// <summary>`LevelRunner` 가 레벨을 띄운 뒤 부른다 — 게이트·지역·벽을 레벨대로 세운다</summary>
        public void OnLevelLoaded(LevelData level)
        {
            foreach (var t in gates) if (t) Destroy(t.gameObject);
            foreach (var t in zones) if (t) Destroy(t.gameObject);
            if (wall) Destroy(wall.gameObject);
            if (divider) Destroy(divider.gameObject);
            wall = null; divider = null;

            var g = new System.Collections.Generic.List<Transform>();
            var gz = new System.Collections.Generic.List<float>();
            var gl = new System.Collections.Generic.List<int>();
            var z = new System.Collections.Generic.List<Transform>();
            foreach (var e in level.events)
            {
                if (e.kind == EventKind.Gate)
                    foreach (var o in e.options) { g.Add(Gate(e.z, o)); gz.Add(e.z); gl.Add(o.lane); }
                else if (e.kind == EventKind.Zone)
                {
                    z.Add(Zone(e));
                    ZoneMarks(e, z);
                }
                else if (e.kind == EventKind.Wall)
                {
                    wall = Box("Wall", new Color(0.52f, 0.46f, 0.40f));
                    wall.localScale = new Vector3(level.roadWidth, 2.2f, 0.9f);
                    wall.position = new Vector3(0f, 1.1f, e.z);
                    wallHp0 = e.wallHp;
                }
            }
            gates = g.ToArray();
            gateZ = gz.ToArray();
            gateLane = gl.ToArray();
            zones = z.ToArray();

            // 차선 칸막이 — 게이트를 고르면 합류 지점까지 **길이 갇힌다** (세션 A `commitUntilZ`).
            // 안 보이면 플레이어는 **자기가 왜 못 넘어가는지 모른다** — 광고 그림처럼 벽이 서야 한다
            divider = Box("Divider", new Color(0.86f, 0.84f, 0.78f));
            divider.gameObject.SetActive(false);

            marks.Build(level.length, level.roadWidth);
        }

        void Update()
        {
            var sim = runner.Sim;
            if (sim == null) return;

            float w = sim.RoadWidth;
            // 길은 카메라를 따라온다 — 레벨 전체 길이로 한 장 깔면 멀리서 z 정밀도가 깨진다
            road.localScale = new Vector3(w, 0.1f, 190f);
            road.position = new Vector3(0f, -0.05f, sim.Z + 36f);
            // 땅은 길보다 **살짝 아래**에 둔다 — 같은 높이면 z-파이팅으로 얼룩진다
            // **지평선을 남긴다.** 땅을 끝없이 깔면 화면 위까지 전부 땅이고, 그러면 그림이
            // 세상이 아니라 **질감**으로 보인다. 보이는 끝(게이트가 들어오는 102 m)보다 조금
            // 뒤에서 끊어 하늘이 띠로 남게 한다 — 달리는 방향이 어디인지가 그 띠로 읽힌다
            ground.localScale = new Vector3(320f, 0.1f, 190f);
            ground.position = new Vector3(0f, -0.22f, sim.Z + 36f);
            float half = w * 0.5f;
            curbL.localScale = new Vector3(0.35f, 0.34f, 190f);
            curbR.localScale = curbL.localScale;
            curbL.position = new Vector3(-half, 0.12f, sim.Z + 36f);
            curbR.position = new Vector3(+half, 0.12f, sim.Z + 36f);

            // 벽은 연석 **바깥**에 선다. 높이 2.6 m 는 사람(1.86)보다 높아 *넘을 수 없다* 로
            // 읽히고, 카메라가 10 m 높이라 복도 안이 다 보인다 — 더 높이면 길을 가린다
            wallL.localScale = new Vector3(0.6f, 2.6f, 190f);
            wallR.localScale = wallL.localScale;
            wallL.position = new Vector3(-half - 0.45f, 1.3f, sim.Z + 36f);
            wallR.position = new Vector3(+half + 0.45f, 1.3f, sim.Z + 36f);

            Formation(allies, sim.Units, sim.X, sim.Z, w, 0);
            allyView.Sync(allies); allyView.Draw();

            // 막고 있는 적 — 수는 `BlockingEnemies`, 자리는 `BlockingZ`. 둘 다 시뮬이 준다
            int fn = Mathf.RoundToInt(sim.BlockingEnemies);
            Formation(foes, fn, 0f, sim.BlockingZ, w, 1);
            foeView.Sync(foes); foeView.Draw();

            // **벽은 비율을 정직하게 따라가야 한다.** 세션 A 측정: 벽 HP 2400 과 3200 이 네 경로
            // 모두 **한 숫자도 안 달랐다** — 벽은 밸런스가 아니라 *"병력이 많으면 빨리 부순다"* 가
            // 눈에 보이는 자리다. 그 자리가 값을 하려면 금 가는 정도가 HP 를 그대로 따라야 한다
            if (wall)
            {
                bool up = sim.BlockingWallHp > 0.01f;
                wall.gameObject.SetActive(up);
                if (up && wallHp0 > 0f)
                {
                    float k = Mathf.Clamp01(sim.BlockingWallHp / wallHp0);
                    wall.localScale = new Vector3(w * Mathf.Lerp(0.82f, 1f, k), 2.2f * Mathf.Lerp(0.55f, 1f, k), 0.9f);
                    wall.position = new Vector3(0f, wall.localScale.y * 0.5f, sim.BlockingZ);
                }
            }

            // 갇힌 동안 반대쪽을 막는다
            bool locked = sim.Side != 0 && sim.Z < CommitZ(sim);
            divider.gameObject.SetActive(locked);
            if (locked)
            {
                divider.localScale = new Vector3(0.25f, 1.4f, 60f);
                divider.position = new Vector3(0f, 0.7f, sim.Z + 28f);
            }

            fx.Tick(Time.deltaTime);
            fx.Draw();
            fxGate.Tick(Time.deltaTime, cam);
            marks.Draw();
            // 그림자는 **몸을 그린 뒤**에 — 땅 바로 위라 순서가 바뀌면 z 싸움이 보인다
            shadows.Draw(allies, foes, fx);

            // **지나친 게이트를 가린다.** 안 가리면 뒤에 남은 게이트가 카메라에 가까워져
            // 글자가 거대해지고 서로 겹친다 — 첫 촬영에서 `×135` 와 `+30` 이 화면 아래를 덮었다.
            // 지나친 선택지를 계속 보여 줄 이유도 없다: 이 게임의 조작은 **다음** 게이트 하나다
            for (int i = 0; i < gates.Length; i++)
            {
                if (gates[i] == null) continue;
                // 연출 중인 게이트는 연출이 들고 있다 — 여기서 같은 `activeSelf` 를 매 프레임
                // 반대로 쓰면 **깜빡인다**
                if (fxGate.Animating(gates[i])) continue;
                bool ahead = gateZ[i] > sim.Z - 2f;
                if (gates[i].gameObject.activeSelf != ahead) gates[i].gameObject.SetActive(ahead);
            }

            // **있을 때 만든다.** `GameBoot.Go` 는 `LevelView` 를 붙인 **뒤에** 오버레이를
            // 세우고(`AddComponent` 가 즉시 `Awake` 를 돌린다), `GameBoot.Reset` 은 오버레이를
            // 통째로 지운다. 둘 다 "만드는 시점"을 고정할 수 없게 만든다 — 그래서 **없으면
            // 만든다**로 둔다. 순서에 기대면 그 순서를 바꾸는 사람이 조용히 깨뜨린다
            if (countUi == null) BuildCountUi();
            if (countUi != null)
            {
                // **판이 도는 동안에만 보인다.** 오버레이 캔버스에 바로 붙어 있어서
                // `MetaFlow` 가 자기 `root` 를 꺼도 이것은 남는다 — 월드맵 1-1 칸 위에
                // 멈춰 있는 `10` 이 떠 있었다.
                //
                // 숨는 조건을 **메타가 아니라 여기서** 본다: *판이 도는 동안에만 보인다* 는
                // 이 물건 자신의 성질이고, 메타가 알아야 할 일이 아니다. 메타 쪽에 참조를
                // 내주면 **끄는 자리가 둘**이 되고, 그 둘이 어긋나는 날이 온다.
                //
                // `Paused` 는 멈춤 화면에서도 참이다 — 멈춤 중에 병력 수를 보여 줄지는
                // 따로 정할 일이고, 지금은 **안 보인다**로 둔다 (멈춤 화면이 자기 수를 띄운다)
                bool playing = !runner.Paused;
                if (countUi.gameObject.activeSelf != playing) countUi.gameObject.SetActive(playing);

                countUi.text = sim.Units.ToString();
                countUi.color = sim.State == SimState.Lost ? new Color(0.92f, 0.46f, 0.44f) : Color.white;
            }

            // **당긴다.** 19 m 높이 · 26 m 뒤였는데, 시작 지점(10 명)에서 화면의 80 % 가
            // 빈 아스팔트였다 — 군단이 점처럼 작고 길이 화면을 지배했다. 1.5 배 가까이
            // 오면 사람이 1.5 배 커지고 길이 차지하는 비율이 줄어든다.
            //
            // 각도는 그대로 34°다. 각도를 눕히면 겹쳐 그리기가 늘어 GPU 가 비싸진다 (§6b)
            cam.transform.position = new Vector3(0f, 9.2f, sim.Z - 10.5f);
        }

        /// <summary>
        /// **병력 수는 화면 UI 다 — 3D 안이 아니다.**
        ///
        /// 처음에는 군단 머리 위에 `TextMesh` 로 띄웠다. 그랬더니 **게이트 글자와 화면에서
        /// 겹쳤다** — 세계 좌표 높이는 달랐지만(4.4 m vs 1.6 m) 게이트가 더 멀리 있어 화면에서는
        /// 같은 높이로 올라온다. 이 구도에서 **'앞'과 '위'는 화면에서 같은 방향**이다.
        ///
        /// 그래서 군단 **뒤로** 옮겼더니 이번엔 **거대해졌다.** 카메라가 군단 뒤 26 m 에 있으니
        /// 뒤로 보내는 것은 **카메라에 가까워지는 것**이고, 가까우면 크다. 3D 안에는
        /// *작으면서 게이트를 피하는 자리가 없다* — 두 번 옮겨 보고 알았다.
        ///
        /// 화면 UI 면 **원근이 없다.** 크기가 거리와 무관하고, 자리도 고정이라 무엇과도
        /// 안 겹친다. 이 장르가 전부 이렇게 하는 데는 이유가 있었다.
        ///
        /// 캔버스는 `GameBoot.Overlay` 를 쓴다 — 판보다 오래 사는 것이라, 이 뷰가 죽을 때
        /// **자식만 지운다**(`OnDestroy`). 안 지우면 판을 바꿀 때마다 숫자가 하나씩 쌓인다.
        /// </summary>
        void BuildCountUi()
        {
            var canvas = GameBoot.Overlay;
            if (canvas == null) return;    // 아직 안 섰다 — 다음 프레임에 다시 본다

            var go = new GameObject("CountUi");
            go.transform.SetParent(canvas.transform, false);
            var rt = go.AddComponent<RectTransform>();
            // 아래 가운데. 게이트는 늘 화면 위쪽에 있으므로 여기면 만날 일이 없다
            rt.anchorMin = new Vector2(0.5f, 0f);
            rt.anchorMax = new Vector2(0.5f, 0f);
            rt.pivot = new Vector2(0.5f, 0f);
            rt.anchoredPosition = new Vector2(0f, 110f);
            rt.sizeDelta = new Vector2(600f, 190f);

            countUi = go.AddComponent<UnityEngine.UI.Text>();
            countUi.font = Resources.Load<Font>("Fonts/Jua-Regular")
                        ?? Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (countUi.font == null) Debug.LogError("[CR] 글꼴을 못 찾았다 — 병력 수가 안 나온다");
            countUi.fontSize = 150;
            countUi.alignment = TextAnchor.LowerCenter;
            countUi.color = Color.white;
            countUi.raycastTarget = false;   // 손가락은 이 아래 길까지 닿아야 한다
            // **테두리.** 길도 군중도 밝기가 제각각이라, 흰 글자 하나로는 어딘가에서 묻힌다
            var o = go.AddComponent<UnityEngine.UI.Outline>();
            o.effectColor = new Color(0.05f, 0.04f, 0.08f, 0.92f);
            o.effectDistance = new Vector2(3.5f, -3.5f);
        }

        void OnDestroy()
        {
            // 캔버스는 판보다 오래 산다 — 내 자식만 치운다
            if (countUi != null) Destroy(countUi.gameObject);
        }

        /// <summary>
        /// 틱마다 온다 (`LevelRunner.OnTick`). 여기서 쓰러지는 몸을 띄운다 — **틱마다** 받아야
        /// 프레임이 긴 기기에서도 손실이 같은 양으로 보인다.
        /// </summary>
        void OnTick(Sim.Tick t)
        {
            var sim = runner.Sim;
            if (sim == null) return;
            // 전투사는 **막은 것이 서 있는 z**(앞줄)에서, 지역사는 군중 가운데에서
            fx.Take(t, sim.X, sim.Z, sim.RoadWidth, sim.BlockingZ > 0.1f ? sim.BlockingZ : sim.Z + 2f);
            if (t.gateFired) GatePassed(t);
        }

        /// <summary>
        /// **고른 것에 응답한다.** 이 게임의 조작은 게이트 하나뿐인데 지금까지 그 하나에
        /// 아무 응답이 없었다 — 고른 쪽도 버린 쪽도 그냥 사라졌다 (`GatePassFx` 주석).
        ///
        /// 어느 것이 고른 것인지는 **`Tick.gateLane` 으로만** 판단한다. 화면상의 X 거리로
        /// 다시 고르지 않는 이유: 세션 A 가 `Sim` 에서 바로 그 두 기준이 갈라져 **한가운데가
        /// 제3의 길**이 된 것을 고쳤다 (`Sim.cs` §221 주석). 표현이 자기 기준으로 다시
        /// 고르면 그 갈라짐이 **화면 쪽에 되살아난다** — 시뮬은 왼쪽으로 넣었는데 화면은
        /// 오른쪽이 터지는 것은, 틀린 것 중에서도 가장 알아채기 어려운 쪽이다.
        /// </summary>
        void GatePassed(in Sim.Tick t)
        {
            var lv = runner.Level;
            if (lv == null) return;
            // 방금 터진 게이트 = 지나온 게이트 중 **가장 앞의 것**
            float zBest = float.NegativeInfinity;
            LevelEvent ev = default; bool found = false;
            foreach (var e in lv.events)
                if (e.kind == EventKind.Gate && e.z <= runner.Sim.Z && e.z > zBest) { zBest = e.z; ev = e; found = true; }
            if (!found) return;

            Transform chosen = null; int nRef = 0;
            for (int i = 0; i < gates.Length; i++)
            {
                if (gates[i] == null || !Mathf.Approximately(gateZ[i], zBest)) continue;
                if (gateLane[i] == t.gateLane || ev.options.Length == 1) chosen = gates[i];
                else if (nRef < refusedBuf.Length) refusedBuf[nRef++] = gates[i];
            }

            string rule = "?";
            foreach (var o in ev.options)
                if (o.lane == t.gateLane || ev.options.Length == 1) { rule = Sign(o.op) + o.value; break; }
            fxGate.Pass(chosen, refusedBuf, nRef, rule, t.gateBefore, t.gateAfter, cam);
        }

        /// <summary>갇힘이 끝나는 z — 시뮬이 공개하지 않으므로 레벨에서 읽는다 (게이트가 들고 있다)</summary>
        float CommitZ(Sim sim)
        {
            var lv = runner.Level;
            if (lv == null) return 0f;
            float best = 0f;
            foreach (var e in lv.events)
                if (e.kind == EventKind.Gate && e.z <= sim.Z && e.commitUntilZ > best) best = e.commitUntilZ;
            return best;
        }

        /// <summary>
        /// 군중을 대형으로 세운다 — **격자가 아니라 흩어서**. 격자면 1,000 명이 군대 퍼레이드로
        /// 보이고, 이 장르의 군중은 *몰려 달리는 떼*다. `CrowdField` 가 개체마다 다른 위상과
        /// 속도 배수를 들고 있으므로 줄이 저절로 흐트러진다.
        /// </summary>
        void Formation(CrowdField f, int n, float cx, float cz, float w, byte side)
        {
            n = Mathf.Min(n, renderCap);
            // **머릿수에 맞춰 뭉친다.** 전에는 몇 명이든 길 폭(6.4 m)에 흩었다 — 10 명이면
            // 사람 사이가 2 m 씩 벌어져 **떼가 아니라 흩어진 점**으로 보인다. 시작 지점이
            // 늘 그 모양이었고, 그게 *"퀄리티가 심각하다"* 의 절반이었다.
            //
            // `sqrt(n)` 에 비례시키는 이유: 사람이 **면적**을 차지하므로 폭은 수의 제곱근으로
            // 자란다. 10 명 → 3.6 m · 40 명 → 7.2 m(길 폭에서 잘림) 으로, 적을 때 뭉치고
            // 많을 때 길을 꽉 채운다
            float spread = Mathf.Min(w * 0.92f, 1.15f * Mathf.Sqrt(Mathf.Max(1, n)));
            if (f.Count != n || Mathf.Abs(spreadNow - spread) > 0.25f)
            {
                f.Clear();
                f.Add(n, side, 0f, spread);
                spreadNow = spread;
            }
            float depth = Mathf.Min(8.5f, 0.95f * Mathf.Sqrt(Mathf.Max(1, n)));
            // 중심을 옮긴다 — 개체마다의 흩어짐은 `CrowdField` 가 만든 것을 그대로 쓴다
            for (int i = 0; i < f.Count; i++)
            {
                // **길 안에 가둔다.** 시뮬의 `X` 는 군단의 **중심**이고 ±길폭/2 까지 간다.
                // 대형은 거기서 또 퍼지므로 가장자리 사람은 길 밖으로 나간다 — 벽을 세우기
                // 전에는 안 보였고(풀밭 위를 걸을 뿐이었다), 벽을 세우니 **벽을 뚫고 지나갔다**.
                //
                // 시뮬은 안 고친다: 규칙상 중심이 ±길폭/2 인 것이 맞다. **보이는 몸만** 길 안으로
                // 당긴다 — 표현이 규칙보다 넓게 말하지 않게 하는 쪽이다
                float lim = w * 0.5f - 0.35f;
                f.X[i] = Mathf.Clamp(cx + f.SideTarget[i], -lim, lim);
                // **떼는 깊이가 있어야 떼다.** 3.2 m 로는 한 줄로 서 있고, 그러면 42 명이
                // 42 명으로 안 읽힌다 (앞줄만 보인다). 뒤로 늘리면 수가 눈에 쌓인다
                f.Z[i] = cz + (f.Phase[i] - 0.5f) * depth + (f.SpeedMul[i] - 1f) * depth * 2.1f;
                f.Phase[i] += Time.deltaTime * 1.4f;
                if (f.Phase[i] >= 1f) f.Phase[i] -= 1f;
            }
        }

        Transform Gate(float z, GateOption o)
        {
            // 색으로 종류를, 글자로 값을 말한다. 색만으로는 `+10` 과 `×3` 이 구별되지 않고,
            // 그 둘의 차이가 이 게임의 **유일한 조작**이다
            var col = o.op == GateOp.Multiply ? new Color(0.40f, 0.72f, 0.42f)
                    : o.op == GateOp.Add ? new Color(0.44f, 0.62f, 0.82f)
                    : new Color(0.78f, 0.42f, 0.40f);
            // **문이어야 문으로 보인다.** 2.6 × 2.0 은 군중(67 명이 길을 꽉 채운다)보다 작아서
            // 지나가는 표지판처럼 보였다 — 첫 연출 사진에서 그게 그대로 드러났다. 차선 폭을
            // 거의 채우고 사람 키보다 높게 두면 **통과한다**는 것이 모양만으로 읽힌다
            float lane = (runner.Level != null ? runner.Level.roadWidth : 8f) * 0.5f;
            // **둘 사이를 벌린다.** 전에는 폭 0.90 · 중심 0.50 이라 틈이 0.35 m 뿐이었고,
            // 멀리서 보면 **두 문이 아니라 간판 하나**로 읽혔다 (오너 폰 사진). 이 게임의
            // 조작이 *둘 중 하나* 라서, 둘로 안 보이면 조작이 화면에서 사라진 것이다
            var t = Box("Gate", col);
            t.localScale = new Vector3(lane * 0.78f, 3.2f, 0.25f);
            t.position = new Vector3(o.lane * lane * 0.60f, 1.6f, z);
            // **테두리.** 색판만 있으면 공중에 뜬 종이로 보인다. 뒤에 조금 큰 어두운 판을
            // 깔면 가장자리가 생겨 *문틀* 로 읽힌다 — 드로우 하나 값이다
            //
            // **문의 자식으로 붙인다.** 따로 두면 `gates[]` 가 모르니 지나간 뒤에도 남고,
            // 연출(`GatePassFx`)이 문을 터뜨려도 테두리만 제자리에 서 있다 — 문이 사라진
            // 자리에 틀만 남는 그림이다
            var frame = Box("GateFrame", new Color(0.16f, 0.17f, 0.22f));
            frame.SetParent(t, false);
            frame.localScale = new Vector3(1f + 0.34f / t.localScale.x,
                                           1f + 0.30f / t.localScale.y,
                                           0.16f / t.localScale.z);
            frame.localPosition = new Vector3(0f, 0.01f, 0.4f);
            var lab = Label("GateText", 0.34f);
            lab.transform.SetParent(t, false);
            lab.transform.localPosition = new Vector3(0f, 0.06f, -0.6f);
            // 부모가 차선만큼 넓어졌으므로 **자식의 지역 배율로 돌려놓는다** — 안 그러면
            // 글자가 네모와 같이 늘어나 길쭉해진다
            lab.transform.localScale = new Vector3(0.55f / Mathf.Max(0.1f, t.localScale.x),
                                                   0.55f / t.localScale.y, 1f);
            lab.text = Sign(o.op) + o.value;
            FitLabel(lab, t.localScale.x * 0.80f);
            return t;
        }

        /// <summary>
        /// **글자를 자기 문 안에 맞춘다** — 배수 하나를 고르지 않는 이유.
        ///
        /// 읽히는 거리(43 m)는 글자 크기가 정하므로 키우고 싶다. 그런데 한 게이트의 두 선택지는
        /// 몇 m 밖에 안 떨어져 있고, **붙으면 둘 다 못 읽는다** — 멀리서 읽히게 만든 대가로
        /// 가까이서 못 읽게 되는 것이다.
        ///
        /// 처음에는 "1.88 배까지 안 붙는다" 를 재서 1.5 배를 쓰려 했다. **그 방식이 틀렸다.**
        /// 그 1.88 은 `×2`/`+35`(두세 글자, 길 7 m)에서 나온 수인데, 세션 A 가 전 경로를 돌려
        /// 잰 최대 증감은 **`+100`(네 글자, 1-5)** 이고 길 폭도 판마다 다르다. 상수 하나는
        /// *어느 판에서는* 붙고, 그 판이 어디인지는 아무도 모른다.
        ///
        /// 그래서 **글자가 자기 네모의 80 % 안에 들어가게** 한다. 그러면 두 글자가 겹치는 일이
        /// **구조적으로 없다** — 네모끼리 안 겹치니까. 글자 수가 늘면 저절로 작아지고 길이
        /// 좁아져도 저절로 맞는다. 짧은 글자(`×2`)는 커져서 더 멀리서 읽힌다.
        ///
        /// 글자당 폭 `0.68 m` 는 **잰 수**다 (`+35` 세 글자 = 2.04 m, `MetaShotTests` 가
        /// `Renderer.bounds` 로 다시 확인한다). 글꼴을 바꾸면 이 수도 다시 재야 한다.
        /// </summary>
        const float GateCharWidth = 0.68f;

        static void FitLabel(TextMesh lab, float maxWidth)
        {
            int chars = Mathf.Max(1, lab.text.Length);
            float want = chars * GateCharWidth;
            // 키우는 쪽은 1.8 배에서 멈춘다 — `×2` 가 문을 넘칠 만큼 커지면 숫자가 아니라
            // 무늬로 보이고, 두 선택지의 **크기 차이**가 값의 차이로 오해된다
            float k = Mathf.Clamp(maxWidth / Mathf.Max(0.01f, want), 0.45f, 1.8f);
            var sc = lab.transform.localScale;
            lab.transform.localScale = new Vector3(sc.x * k, sc.y * k, sc.z);
        }

        static string Sign(GateOp op) =>
            op == GateOp.Add ? "+" : op == GateOp.Multiply ? "×" : op == GateOp.Subtract ? "−" : "÷";

        Transform Zone(LevelEvent e)
        {
            // 지역은 머릿수에 비례해 깎는다 (세션 A) — **큰 군단이 지나갈 때 눈에 보이게 많이
            // 녹아야** 대가가 느껴진다. 그 연출은 다음 단위이고, 지금은 *바닥이 보이는 것*까지다
            // **차선이 정해진 지역은 그 차선만 칠한다.** 길 전체를 칠하면 화면이
            // *"어느 쪽으로 가도 녹는다"* 라고 말하는데, 규칙은 `Side != e.lane` 이면
            // 안 녹는다 (`Sim`). 1-7 이 바로 그 판이다: `zone 35 … lane L` 이라
            // **`×3` 쪽만 녹는데** 양쪽을 칠해서 그 선택이 화면에서 지워져 있었다.
            //
            // 표현이 규칙보다 **넓게** 말하면 플레이어는 있지도 않은 위험을 피한다. 그건
            // 틀린 그림이 아니라 **없어진 선택**이다 — 이 게임의 조작이 그 하나뿐이라 더 그렇다.
            float rw = runner.Level != null ? runner.Level.roadWidth : 8f;
            float zw = e.lane != 0 ? rw * 0.5f : rw;
            float zx = e.lane != 0 ? e.lane * rw * 0.25f : 0f;
            var t = Box("Zone", new Color(0.66f, 0.38f, 0.56f, 1f));
            t.localScale = new Vector3(zw, 0.06f, e.zoneLength);
            t.position = new Vector3(zx, 0.01f, e.z + e.zoneLength * 0.5f);
            return t;
        }

        /// <summary>
        /// **녹는 구간을 멀리서 보이게 한다** — 바닥 색판은 가까이 와야 길과 구분된다.
        ///
        /// 1-7 은 게이트를 고르고 **2.2 초 뒤**에 지역이 시작한다 (세션 A 측정). 그 판이
        /// 가르치려는 것은 *녹는 길* 인데, **가르쳐지기 전에 벌어진다** — 플레이어는 고를 때
        /// 앞에 무엇이 있는지 모르고, 그러면 손해가 *선택의 결과*가 아니라 *사고*가 된다.
        ///
        /// 세워 두면 고르는 **그 순간에** 보인다. 2.2 초는 그대로지만 *예고 없이 당한다* 가
        /// *알고 들어간다* 로 바뀐다 — 페이싱을 안 건드리고 그 몫을 줄이는 길이다.
        /// 지역과 **같은 색**을 쓴다: 표식과 바닥이 다른 색이면 둘이 같은 것임을 못 배운다.
        /// </summary>
        void ZoneMarks(LevelEvent e, System.Collections.Generic.List<Transform> into)
        {
            float rw = runner.Level != null ? runner.Level.roadWidth : 8f;
            // 바닥과 **같은 폭·같은 자리**에 선다 — 표식이 바닥보다 넓으면 표식 쪽이 거짓말을
            // 하고, 플레이어는 멀리서 표식부터 본다
            float w = e.lane != 0 ? rw * 0.5f : rw;
            float cx = e.lane != 0 ? e.lane * rw * 0.25f : 0f;
            var col = new Color(0.66f, 0.38f, 0.56f, 1f);
            for (int i = 0; i < 2; i++)
            {
                var post = Box("ZoneMark", col);
                post.localScale = new Vector3(0.30f, 3.4f, 0.30f);
                post.position = new Vector3(cx + (i == 0 ? -1f : 1f) * w * 0.5f, 1.7f, e.z);
                into.Add(post);
            }
            // 가로대 — 그 차선 위를 가로지르므로 **거기로 들어간다**는 것이 모양으로 읽힌다
            var bar = Box("ZoneMark", col);
            bar.localScale = new Vector3(w, 0.40f, 0.30f);
            bar.position = new Vector3(cx, 3.2f, e.z);
            into.Add(bar);
        }

        Transform Box(string name, Color c)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            // **자기 밑에 단다.** 부모가 없으면 판 오브젝트를 지워도 길·게이트·벽이 장면에
            // 남는다 — 판을 바꿀 때마다 쌓이고, 해가 넷이면 화면이 하얗게 날아간다.
            // `GameBoot.AssertOneWorld` 가 바로 이 자리를 가리켰다
            go.transform.SetParent(transform, false);
            Destroy(go.GetComponent<Collider>());   // 물리 엔진을 안 쓴다 (`DESIGN.md` §2)
            // **`Mobile/Diffuse` 에는 `_Color` 가 없다.** 그 셰이더는 `_MainTex` 만 받으므로
            // `material.color = c` 가 **아무 일도 안 하고**, 길·게이트·벽·지역·칸막이가 전부
            // **새하얗게** 나왔다 (첫 게임 화면 촬영에서 길이 화면을 하얗게 덮었다).
            // 색을 주는 셰이더를 고르고, **실제로 먹었는지 확인**한다 — 조용히 흰색으로
            // 떨어지면 "색을 줬는데 왜 하얗지" 가 된다
            var sh = Shader.Find("Legacy Shaders/Diffuse") ?? Shader.Find("Standard");
            var m = new Material(sh);
            if (!m.HasProperty("_Color"))
                Debug.LogError("[CR] 셰이더 '" + sh.name + "' 에 _Color 가 없다 — 모든 네모가 하얗게 나온다");
            m.color = c;
            // **광택을 끈다.** `Legacy Shaders/Diffuse` 를 못 찾으면 `Standard` 로 떨어지는데
            // 그쪽 기본 광택은 0.5 다. 길처럼 **넓고 평평한 면**은 정반사 로브가 화면 전체에
            // 걸쳐 퍼져서 **면 하나가 통째로 하얘진다** — 색을 제대로 줬는데도 그렇다.
            // 이 게임에 반짝이는 것은 하나도 없으므로 그냥 끈다
            if (m.HasProperty("_Glossiness")) m.SetFloat("_Glossiness", 0f);
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 0f);
            if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", 0f);
            if (m.HasProperty("_SpecColor")) m.SetColor("_SpecColor", Color.black);
            go.GetComponent<Renderer>().sharedMaterial = m;
            return go.transform;
        }

        TextMesh Label(string name, float size)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);   // 위와 같은 이유 — 남으면 글자가 겹쳐 쌓인다
            var tm = go.AddComponent<TextMesh>();
            // **없으면 조용히 비는 대신 로그를 남긴다** — 숫자가 안 보이면 이 게임은 읽을 수 없다
            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf")
                    ?? Resources.GetBuiltinResource<Font>("Arial.ttf");
            if (font == null) Debug.LogError("[CR] 내장 글꼴을 못 찾았다 — 병력 수가 화면에 안 나온다");
            tm.font = font;
            if (font != null) go.GetComponent<MeshRenderer>().sharedMaterial = font.material;
            tm.fontSize = 64;
            tm.characterSize = size;
            tm.anchor = TextAnchor.MiddleCenter;
            tm.alignment = TextAlignment.Center;
            return tm;
        }
    }
}
