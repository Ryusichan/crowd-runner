using System.Collections.Generic;
using CrowdFire.Core;
using CrowdFire.Game;
using CrowdFire.Crowd;
using UnityEngine;

namespace CrowdFire.View
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
    /// `sim.Last`(이 틱의 사건 — **한 틱만 유효**하므로 `LevelPlay.Consume` 이 틱마다 넘겨준다).
    ///
    /// 군중은 **방식 B 저폴리**로 그린다 (`docs/M0_CROWD.md` §6b): 드로우 1 회 · 상한 400.
    /// 방식 A(유닛마다 `Animator`)는 60 개체에서 예산이 끝나 이 장르가 못 된다.
    /// </summary>
    [RequireComponent(typeof(LevelPlay))]
    public sealed class LevelView : MonoBehaviour
    {
        /// <summary>
        /// 화면에 그릴 상한 (`LevelData.RenderCap` 과 같은 수). 병력이 이보다 많아도 **수는 맞게
        /// 보여 주고 몸만 상한까지** 그린다 — 400 과 600 의 차이는 화면에서 구분되지 않고,
        /// 구분되지 않는 것에 프레임을 쓰면 **구분되는 것이 사라진다**.
        /// </summary>
        public int renderCap = 400;

        LevelPlay play;
        CrowdField allies, foes;
        readonly Gunfire gunfire = new Gunfire();
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

        /// <summary>
        /// **길가가 비면 황야다.** 라스트워의 레인은 양옆에 콘크리트 방호벽 토막이 줄지어 있고
        /// 그 밖으로 폐건물이 지나간다 (`docs/REF_LASTWAR.md` §2e). 전부 상자이지만 **간격·높이가
        /// 제각각**이면 거리감이 생긴다. 개수를 고정하고 앞으로 보내는 풀이라 판 길이와 무관하다
        /// </summary>
        readonly List<Transform> sideProps = new List<Transform>();
        const int BarrierPerSide = 14, BuildingPerSide = 6;
        const float BarrierStep = 6f, BuildingStep = 16f;

        // ── 드럼통 ───────────────────────────────────────────────────────────
        /// <summary>
        /// 통은 **레벨 글에서 한 번** 세우고, 매 프레임 `Battle` 에서 체력만 읽어 온다.
        /// `Battle` 쪽 배열은 지나가면 지워지므로(`PruneBarrels`) 거기 맞춰 만들었다 지웠다
        /// 하면 깜빡인다 — 그래서 **화면 쪽은 레벨이 주인**이고 상태만 빌려 온다.
        /// </summary>
        Transform[] barrels = new Transform[0];
        float[] barrelZ = new float[0];
        int[] barrelLane = new int[0];
        Transform[] barrelBar = new Transform[0];
        Material[] barrelMat = new Material[0];
        float[] barrelPop = new float[0];
        static readonly Color BarrelWood = new Color(0.62f, 0.44f, 0.26f);
        float barrelUpright; bool barrelLabelLoose; Transform lastBarrelLabel;
        static readonly Color BarrelHit = new Color(0.86f, 0.30f, 0.22f);
        UnityEngine.UI.Text countUi;
        UnityEngine.UI.Text foeCountUi;
        CasualtyFx fx;
        RoadMarks marks;
        float spreadNow = -1f;
        BlobShadows shadows;
        Camera cam;
        float camK;   // 병력에 따른 카메라 거리 배율 (1 = 20 명 기준)
        float wallHp0;

        void Awake()
        {
            play = GetComponent<LevelPlay>();
            allies = new CrowdField(Sim.MaxUnits, 20261009);
            foes = new CrowdField(Sim.MaxUnits, 77215);
            // 2026-10-18: 회색 캡슐 대역(`_lo`) 대신 **파란 군인(+소총)** 과 **좀비** —
            // `Editor/CrowdBake` 가 굽는다. 오너: *"라스트워처럼."* 구운 것이 없으면 `VatCrowd`
            // 가 에러를 찍고 안 그리므로, 조용히 대역으로 떨어지지 않는다 (그게 맞다)
            allyView = new VatCrowd("_soldier");
            foeView = new VatCrowd("_zombie", foe: true);
            allyView.Init(renderCap); foeView.Init(renderCap);
            allyView.SetActive(true); foeView.SetActive(true);
            gunfire.Init();
            fx = new CasualtyFx(); fx.Init();
            marks = new RoadMarks();
            shadows = new BlobShadows(); shadows.Init();
            fxGate = new GatePassFx(); fxGate.Init(Label);
            play.OnTick += OnTick;
            BuildScene();
            // **늦게 붙었을 수도 있다.** `LevelPlay.Awake` 가 먼저 돌면 그때 뷰가 없어서
            // `OnLevelLoaded` 를 놓친다 — 이미 떠 있는 판이 있으면 여기서 받아 간다
            if (play.Level != null) OnLevelLoaded(play.Level);
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
            // 세로형 군단 — 뒤에서 내려다본다. 각도가 **겹쳐 그리기**를 정하므로 (§6b 마지막 경고)
            // 더 눕히면 군중이 화면에 더 넓게 퍼져 GPU 가 싸진다. 지금은 보이는 쪽을 먼저 맞춘다
            // 34° 였는데 군단이 화면 **중간**에 앉고 그 아래가 빈 아스팔트였다. 조금 세우면
            // (= 숙이는 각을 줄이면) 화면의 것들이 전부 아래로 내려오고, 앞쪽이 더 보인다
            // 2026-10-18 라스트워 프레이밍 (`docs/REF_LASTWAR.md` §2b): 소대가 화면 아래 1/3 에,
            // 사람 키가 화면 높이의 1/8 쯤. 9.2 m 위 · 10.5 m 뒤 · 30° 에서는 사람이 점이었다
            // 34° 에서는 헬멧 윗면만 보여 군단이 **파란 돔** 이었다 (z108 사진). 라스트워는 더 낮게 본다 —
            // 몸통·소총이 보이는 28°, 대신 뒤로 조금 더 물러난다
            cam.transform.rotation = Quaternion.Euler(28f, 0f, 0f);
            cam.fieldOfView = 46f;
            cam.farClipPlane = 300f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.74f, 0.80f, 0.86f);
            // **안개.** 멀리가 하늘색으로 녹아야 길이 지평선으로 사라진다 — 없으면 끝이 칼로 자른 듯 끊긴다
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = cam.backgroundColor;
            RenderSettings.fogStartDistance = 55f;
            RenderSettings.fogEndDistance = 130f;

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
            li.color = new Color(1.0f, 0.95f, 0.86f);   // 오후 햇빛 — 흰 해는 플라스틱처럼 보인다
            li.intensity = 1.15f;
            li.transform.rotation = Quaternion.Euler(52f, -28f, 0f);
            RenderSettings.ambientLight = new Color(0.46f, 0.48f, 0.54f);   // 그늘이 검지 않게
            li.transform.rotation = Quaternion.Euler(52f, -28f, 0f);

            // 색은 **대비로** 고른다. 전에는 땅 0.30/0.38/0.28, 길 0.46/0.50/0.44 로 둘 다
            // 어두운 회녹색이라 화면이 한 톤이었고, 그 위의 군중(어두운 몸)도 묻혔다.
            // 지금은 **풀(녹) · 아스팔트(회) · 연석(흰)** 셋이 밝기로 갈린다 — 색상환이
            // 아니라 **밝기**로 가르는 이유: 작은 화면에서 먼저 읽히는 것이 밝기다
            // **땅이 있어야 길이 길로 보인다.** 길만 그리면 하늘색 위에 떠 있는 리본이고,
            // 그러면 군단이 *어디를* 달리는지가 없다. 땅 한 장 + 연석 둘이면 끝난다 —
            // 광고형 군단가 옆을 비워 두지 않는 이유다
            ground = Box("Ground", new Color(0.56f, 0.50f, 0.40f));   // 마른 땅 — 라스트워의 황야
            road = Box("Road", new Color(0.33f, 0.34f, 0.37f));      // 아스팔트는 어둡다 — 밝으면 군중이 묻힌다
            // 연석은 길의 **가장자리를 말한다**. 갇힌 차선 칸막이(`divider`)와 달리 늘 서 있고,
            // 밝아서 길의 폭이 한눈에 읽힌다 — 폭이 좁아지는 구간이 눈에 걸려야 한다
            curbL = Box("Curb", new Color(0.93f, 0.78f, 0.22f));   // 노란 가장자리 선
            curbR = Box("Curb", new Color(0.93f, 0.78f, 0.22f));

            // **벽으로 닫는다** (`docs/REF_TOPWAR.md` §2③). 들판 가운데 열린 길은 *어디로
            // 가야 하는지* 는 말하지만 **갇혀 있다** 는 말을 못 한다. 벽이 서면 좌우가 끝이라는
            // 것이 모양으로 읽히고, 군중이 벽에 닿는 것이 *꽉 찼다* 를 만든다 — 참고한 광고에서
            // 군중이 **벽에서 벽까지** 차 있는 것이 그 장르의 핵심 그림이다.
            //
            // 길보다 **밝게** 둔다: 어두우면 화면 양끝이 검은 띠가 되어 세로 화면이 더 좁아 보인다
            wallL = Box("Wall", new Color(0.74f, 0.72f, 0.68f));   // 콘크리트 방호벽
            wallR = Box("Wall", new Color(0.74f, 0.72f, 0.68f));
            BuildSideProps();
        }

        void BuildSideProps()
        {
            foreach (var t in sideProps) if (t) Destroy(t.gameObject);
            sideProps.Clear();
            var concrete = new Color(0.80f, 0.78f, 0.74f);
            var stripe = new Color(0.90f, 0.72f, 0.20f);
            for (int side = -1; side <= 1; side += 2)
            {
                // 방호벽 토막 — 사이사이 틈을 두고, 하나 걸러 노란 줄
                for (int i = 0; i < BarrierPerSide; i++)
                {
                    var b = Box("Barrier", (i & 1) == 0 ? concrete : Color.Lerp(concrete, stripe, 0.35f));
                    b.localScale = new Vector3(0.55f, 0.95f, 3.6f);
                    b.name = "Barrier" + (side < 0 ? "L" : "R") + i;
                    sideProps.Add(b);
                }
                // 폐건물 — 높이·폭·색이 제각각인 상자. 멀리 서서 안개에 녹는다
                for (int i = 0; i < BuildingPerSide; i++)
                {
                    float h = 6f + ((i * 7 + (side + 1) * 3) % 5) * 2.2f;
                    float w = 7f + ((i * 3) % 3) * 2.5f;
                    float g = 0.42f + ((i * 5 + side + 1) % 4) * 0.05f;
                    var b = Box("Building", new Color(g, g * 0.97f, g * 0.93f));
                    b.localScale = new Vector3(w, h, 9f + (i % 2) * 4f);
                    b.name = "Building" + (side < 0 ? "L" : "R") + i;
                    sideProps.Add(b);
                }
            }
        }

        /// <summary>풀을 군단 앞뒤로 깐다 — 지나간 것은 앞으로 보낸다</summary>
        void TickSideProps(float z, float half)
        {
            int k = 0;
            for (int side = -1; side <= 1; side += 2)
            {
                float x = side * (half + 0.9f);
                for (int i = 0; i < BarrierPerSide; i++, k++)
                {
                    float bz = Mathf.Floor((z - 18f) / BarrierStep) * BarrierStep + i * BarrierStep;
                    sideProps[k].position = new Vector3(x, 0.48f, bz);
                }
                float bx = side * (half + 7.5f);
                for (int i = 0; i < BuildingPerSide; i++, k++)
                {
                    var t = sideProps[k];
                    float bz = Mathf.Floor((z - 24f) / BuildingStep) * BuildingStep + i * BuildingStep;
                    // 좌우 번갈아 조금 더 멀리 — 일직선이면 담장처럼 보인다
                    float off = ((i + (side + 1)) % 3) * 3.5f;
                    t.position = new Vector3(bx + side * off, t.localScale.y * 0.5f - 0.05f, bz);
                }
            }
        }

        /// <summary>`LevelPlay` 가 레벨을 띄운 뒤 부른다 — 게이트·지역·벽을 레벨대로 세운다</summary>
        public void OnLevelLoaded(LevelData level)
        {
            foreach (var t in gates) if (t) Destroy(t.gameObject);
            foreach (var t in barrels) if (t) Destroy(t.gameObject);
            foreach (var t in sideProps) if (t) Destroy(t.gameObject);
            sideProps.Clear();
            BuildSideProps();   // 판을 바꿀 때 길가 소품도 새로 세운다
            barrels = new Transform[0]; barrelZ = new float[0]; barrelLane = new int[0];
            barrelBar = new Transform[0]; barrelMat = new Material[0]; barrelPop = new float[0];
            foreach (var t in zones) if (t) Destroy(t.gameObject);
            if (wall) Destroy(wall.gameObject);
            if (divider) Destroy(divider.gameObject);
            wall = null; divider = null;

            var g = new System.Collections.Generic.List<Transform>();
            var gz = new System.Collections.Generic.List<float>();
            var gl = new System.Collections.Generic.List<int>();
            var z = new System.Collections.Generic.List<Transform>();
            var b = new System.Collections.Generic.List<Transform>();
            var bz = new System.Collections.Generic.List<float>();
            var bl = new System.Collections.Generic.List<int>();
            var bbar = new System.Collections.Generic.List<Transform>();
            var bmat = new System.Collections.Generic.List<Material>();
            foreach (var e in level.events)
            {
                if (e.kind == EventKind.Barrel)
                {
                    Transform bar, mark;
                    barrelLabelLoose = false;
                    var bt = Barrel(e, level, out bar, out mark);
                    b.Add(bt);
                    bz.Add(e.z); bl.Add(e.lane); bbar.Add(bar);
                    bmat.Add(mark.GetComponent<Renderer>().sharedMaterial);
                    // 글자가 통의 자식이 아니면(진짜 메시) 통이 사라질 때 같이 사라지게 부모를 통으로 — 월드 자세 유지
                    if (barrelLabelLoose && lastBarrelLabel != null) lastBarrelLabel.SetParent(bt, true);
                }
                else if (e.kind == EventKind.Gate)
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
            Goal(level, z);

            gates = g.ToArray();
            gateZ = gz.ToArray();
            gateLane = gl.ToArray();
            zones = z.ToArray();
            barrels = b.ToArray();
            barrelZ = bz.ToArray();
            barrelLane = bl.ToArray();
            barrelBar = bbar.ToArray();
            barrelMat = bmat.ToArray();
            barrelPop = new float[barrels.Length];

            // 차선 칸막이 — 게이트를 고르면 합류 지점까지 **길이 갇힌다** (세션 A `commitUntilZ`).
            // 안 보이면 플레이어는 **자기가 왜 못 넘어가는지 모른다** — 광고 그림처럼 벽이 서야 한다
            divider = Box("Divider", new Color(0.86f, 0.84f, 0.78f));
            divider.gameObject.SetActive(false);

            marks.Build(level.length, level.roadWidth);
        }

        void Update()
        {
            var sim = play.Sim;
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
            if (sideProps.Count > 0) TickSideProps(sim.Z, half);
            curbR.position = new Vector3(+half, 0.12f, sim.Z + 36f);

            // 벽은 연석 **바깥**에 선다. 높이 2.6 m 는 사람(1.86)보다 높아 *넘을 수 없다* 로
            // 읽히고, 카메라가 10 m 높이라 복도 안이 다 보인다 — 더 높이면 길을 가린다
            wallL.localScale = new Vector3(0.6f, 2.6f, 190f);
            wallR.localScale = wallL.localScale;
            wallL.position = new Vector3(-half - 0.45f, 1.3f, sim.Z + 36f);
            wallR.position = new Vector3(+half + 0.45f, 1.3f, sim.Z + 36f);

            SyncAllies(sim);
            allyView.Sync(allies); allyView.Draw();

            DrawFoes(sim, w);
            gunfire.Draw(sim.Battle, sim.X, sim.Z, Time.deltaTime);

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

            TickBarrels(sim);

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
                bool playing = !play.Paused;
                if (countUi.gameObject.activeSelf != playing) countUi.gameObject.SetActive(playing);

                countUi.text = sim.Units.ToString();
                if (squadHpRt != null)
                {
                    float k = Mathf.Clamp01(sim.Units / (float)Mathf.Max(1, sim.PeakUnits));
                    squadHpRt.sizeDelta = new Vector2(414f * k, -6f);
                    squadHp.color = Color.Lerp(new Color(0.90f, 0.32f, 0.26f), new Color(0.36f, 0.86f, 0.42f), k);
                }
                countUi.color = sim.State == SimState.Lost ? new Color(0.92f, 0.46f, 0.44f) : Color.white;
            }

            if (foeCountUi == null) BuildFoeCountUi();
            if (foeCountUi != null) ShowFoeCount(sim);

            // **당긴다.** 19 m 높이 · 26 m 뒤였는데, 시작 지점(10 명)에서 화면의 80 % 가
            // 빈 아스팔트였다 — 군단이 점처럼 작고 길이 화면을 지배했다. 1.5 배 가까이
            // 오면 사람이 1.5 배 커지고 길이 차지하는 비율이 줄어든다.
            //
            // 각도는 그대로 34°다. 각도를 눕히면 겹쳐 그리기가 늘어 GPU 가 비싸진다 (§6b)
            // **군단이 크면 카메라가 물러난다** (라스트워가 그렇다). 20 명일 때 6.8 m 위 · 7.6 m 뒤가
            // 300 명이 되면 화면이 파란 헬멧으로만 찬다 (2026-10-18 z234 사진). 대형 반지름은
            // sqrt(병력)에 비례하므로 거리도 그렇게 — 급하게 튀지 않게 한 프레임에 조금씩 따라간다
            float wantK = Mathf.Clamp(Mathf.Sqrt(Mathf.Max(1f, sim.Units) / 40f), 1f, 1.9f);
            camK = camK <= 0f ? wantK : Mathf.Lerp(camK, wantK, 1f - Mathf.Exp(-Time.deltaTime * 2.5f));
            cam.transform.position = new Vector3(0f, 6.0f * camK, sim.Z - 9.2f * camK);
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

            // **소대 HP 바** (라스트워: 소대 위 얇은 초록 띠). 값은 병력 ÷ 최고 병력 — 통으로 키운 뒤
            // 깎이는 것이 **길이로** 보여야 "밀리고 있다" 가 숫자를 읽기 전에 들어온다
            var bgGo = new GameObject("SquadHpBg");
            bgGo.transform.SetParent(canvas.transform, false);
            var bgRt = bgGo.AddComponent<RectTransform>();
            bgRt.anchorMin = bgRt.anchorMax = new Vector2(0.5f, 0f);
            bgRt.pivot = new Vector2(0.5f, 0f);
            bgRt.anchoredPosition = new Vector2(0f, 300f);
            bgRt.sizeDelta = new Vector2(420f, 22f);
            var bg = bgGo.AddComponent<UnityEngine.UI.Image>();
            bg.color = new Color(0.05f, 0.04f, 0.08f, 0.75f); bg.raycastTarget = false;
            var fgGo = new GameObject("SquadHp");
            fgGo.transform.SetParent(bgGo.transform, false);
            var fgRt = fgGo.AddComponent<RectTransform>();
            fgRt.anchorMin = new Vector2(0f, 0f); fgRt.anchorMax = new Vector2(0f, 1f);
            fgRt.pivot = new Vector2(0f, 0.5f);
            fgRt.anchoredPosition = new Vector2(3f, 0f);
            fgRt.sizeDelta = new Vector2(414f, -6f);
            squadHp = fgGo.AddComponent<UnityEngine.UI.Image>();
            squadHp.color = new Color(0.36f, 0.86f, 0.42f); squadHp.raycastTarget = false;
            squadHpRt = fgRt;
        }

        UnityEngine.UI.Image squadHp; RectTransform squadHpRt;

        /// <summary>
        /// **앞의 무리가 몇인지 글자로 말한다.**
        ///
        /// 세션 A 가 짚은 자리(`HANDOFF` §9): 1-1 의 방어선은 **620 명**인데 `renderCap` 이
        /// 400 이라 화면에는 400 만 선다. 숫자가 없으면 플레이어는 **400 이 전부**라고 읽고,
        /// 그 수로 *지금 더 모아야 하나*를 판단한다 — 틀린 수로 내리는 옳은 판단이다.
        ///
        /// 세는 것은 `Battle.Remaining` 이다: 살아 있는 적 + **아직 안 나온 적**. 적이
        /// 흘러드는 구조라(`Battle.Stream`) 화면에 선 수는 늘 전체보다 적고, 플레이어가
        /// 알아야 하는 것은 *앞으로 몇이 오는가*다.
        ///
        /// 자리는 **적 무리 위**다 — 화면 한구석에 두면 *저 앞의 저것*과 이 수가 같은
        /// 것인지 알 수 없다. 레퍼런스(탑워 광고)도 무리마다 머리 위에 수를 띄운다.
        /// </summary>
        void ShowFoeCount(Sim sim)
        {
            var b = sim.Battle;
            int n = b.Remaining;
            bool show = n > 0 && !play.Paused && sim.State != SimState.Won && sim.State != SimState.Lost;
            if (foeCountUi.gameObject.activeSelf != show) foeCountUi.gameObject.SetActive(show);
            if (!show) return;

            // **가장 가까운 무리의 앞머리**를 가리킨다. 평균을 쓰면 뒤로 길게 늘어선 줄의
            // 한가운데가 되어 *지금 부딪히는 것*에서 멀어진다
            float cz = sim.Z + 24f, cx = 0f;
            float best = float.MaxValue; int cnt = 0;
            for (int i = 0; i < b.EnemyCount; i++)
            {
                if (b.EState[i] == 2) continue;
                float d = b.EZ[i] - sim.Z;
                if (d < -2f) continue;
                if (d < best) { best = d; cz = b.EZ[i]; }
                cx += b.EX[i]; cnt++;
            }
            if (cnt > 0) cx /= cnt;

            var sp = cam.WorldToScreenPoint(new Vector3(cx, 2.6f, cz + 1.2f));
            // 카메라 뒤로 넘어가면 `WorldToScreenPoint` 가 **뒤집힌 좌표**를 준다 —
            // 그대로 쓰면 글자가 반대쪽 구석에서 번쩍인다
            if (sp.z < 0f) { sp.x = Screen.width * 0.5f; sp.y = Screen.height * 0.92f; }

            float scale = foeCountUi.canvas != null ? foeCountUi.canvas.scaleFactor : 1f;
            if (scale <= 0f) scale = 1f;
            float x = Mathf.Clamp(sp.x, Screen.width * 0.12f, Screen.width * 0.88f) / scale;
            float y = Mathf.Clamp(sp.y, Screen.height * 0.30f, Screen.height * 0.93f) / scale;
            foeCountUi.rectTransform.anchoredPosition = new Vector2(x, y);
            foeCountUi.text = n.ToString();
        }

        void BuildFoeCountUi()
        {
            var canvas = GameBoot.Overlay;
            if (canvas == null) return;

            var go = new GameObject("FoeCountUi");
            go.transform.SetParent(canvas.transform, false);
            var rt = go.AddComponent<RectTransform>();
            // 화면 좌표를 그대로 쓰므로 **왼쪽 아래 기준**이다 (`ShowFoeCount` 가 매 프레임 옮긴다)
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.zero;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(360f, 110f);

            foeCountUi = go.AddComponent<UnityEngine.UI.Text>();
            foeCountUi.font = Resources.Load<Font>("Fonts/Jua-Regular")
                           ?? Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            foeCountUi.fontSize = 86;
            foeCountUi.alignment = TextAnchor.MiddleCenter;
            // 적은 붉은색이다 — 군단(흰색)과 **한눈에** 갈려야 한다
            foeCountUi.color = new Color(0.98f, 0.42f, 0.38f);
            foeCountUi.raycastTarget = false;
            var o = go.AddComponent<UnityEngine.UI.Outline>();
            o.effectColor = new Color(0.05f, 0.04f, 0.08f, 0.95f);
            o.effectDistance = new Vector2(3f, -3f);
        }

        void OnDestroy()
        {
            // 캔버스는 판보다 오래 산다 — 내 자식만 치운다
            if (countUi != null) Destroy(countUi.gameObject);
            if (foeCountUi != null) Destroy(foeCountUi.gameObject);
        }

        /// <summary>
        /// 틱마다 온다 (`LevelPlay.OnTick`). 여기서 쓰러지는 몸을 띄운다 — **틱마다** 받아야
        /// 프레임이 긴 기기에서도 손실이 같은 양으로 보인다.
        /// </summary>
        void OnTick(Sim.Tick t)
        {
            var sim = play.Sim;
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
            var lv = play.Level;
            if (lv == null) return;
            // 방금 터진 게이트 = 지나온 게이트 중 **가장 앞의 것**
            float zBest = float.NegativeInfinity;
            LevelEvent ev = default; bool found = false;
            foreach (var e in lv.events)
                if (e.kind == EventKind.Gate && e.z <= play.Sim.Z && e.z > zBest) { zBest = e.z; ev = e; found = true; }
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
            var lv = play.Level;
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
        /// <summary>
        /// **앞에 있는 적을 미리 그린다.**
        ///
        /// 전에는 `BlockingEnemies` 로만 그렸다 — 그건 **몸이 닿는 순간에야 0 이 아니다.**
        /// 그래서 플레이어는 빈 복도를 달리다가 적이 **갑자기 생기는** 것을 본다. 오너:
        /// *"적도 없잖어."* 화면에 없었던 것이 맞다.
        ///
        /// 이 장르에서 앞의 적은 **긴장 그 자체**다 — 저 끝에 무엇이 얼마나 있는지가 보여야
        /// *지금 더 모아야 하나* 를 묻게 된다. 안 보이면 게이트는 그냥 숫자 놀이다.
        ///
        /// 아직 안 닿은 무리는 **레벨이 적은 수 그대로** 그린다 (손대지 않았으니 그 수가 맞다).
        /// 지금 막고 있는 무리만 `BlockingEnemies` 로 바꿔 그린다 — 그래야 **줄어드는 것이
        /// 보인다**. 둘을 같은 식으로 그리면 싸우는데 수가 안 줄어 보인다.
        /// </summary>
        /// <summary>
        /// **아군 자리는 로직이 정한다** (`Battle.SetArmy`). 전에는 여기서 따로 깔았는데,
        /// 그러면 총구 화염이 *그려진 병사*가 아니라 아무 데서나 터진다. 오너 지시 1
        /// (*"300 명이면 300 명이 총을 쏘는 모습이 보여야 한다"*)은 둘이 같은 자리를 써야 선다.
        ///
        /// 논리 병사 수는 1,200 까지 가고 **그리는 수는 `renderCap`(400) 이다** — 그 차이는
        /// 숨기지 않고 `Gunfire.LastMuzzles` 와 측정표에 나온다 (오너 지시 3).
        /// </summary>
        void SyncAllies(Sim sim)
        {
            var b = sim.Battle;
            allies.Clear();
            int n = Mathf.Min(b.AllyCount, renderCap);
            for (int i = 0; i < n; i++)
            {
                int k = allies.Count;
                allies.Add(1, 0, 0f, 1f);
                allies.X[k] = b.AX[i]; allies.Z[k] = b.AZ[i];
                allies.SideTarget[k] = b.AX[i];
                float ph = i * 0.113f + Time.time * 1.7f;
                allies.Phase[k] = ph - Mathf.Floor(ph);
            }
        }

        void DrawFoes(Sim sim, float w)
        {
            var b = sim.Battle;
            foes.Clear();
            int cap = renderCap;
            for (int i = 0; i < b.EnemyCount && foes.Count < cap; i++)
            {
                // 쓰러진 것은 여기서 빼고 `Gunfire` 가 **바닥에 누워** 그린다 — 군중 렌더러는
                // 선 자세 하나뿐이라 눕힐 수가 없다
                if (b.EState[i] == 2) continue;
                int k = foes.Count;
                foes.Add(1, 1, 0f, 1f);
                foes.X[k] = b.EX[i]; foes.Z[k] = b.EZ[i];
                foes.SideTarget[k] = b.EX[i];
                // **위상을 번호에서 만든다.** `CrowdField.Add` 는 매번 새 난수를 넣는데 이 배열은
                // 프레임마다 다시 채워지므로, 그대로 두면 걸음이 **매 프레임 튄다**. 번호 + 시간은
                // 개체마다 다르면서 프레임 사이에 이어진다
                float ph = i * 0.137f + Time.time * 1.6f;
                foes.Phase[k] = ph - Mathf.Floor(ph);
            }
            foeView.Sync(foes); foeView.Draw();
        }

        /// <summary>
        /// **빽빽한 원**으로 세운다 (오너 2026-10-10: *"인물들 최대한 모아줘야해 · 둥그런
        /// 현상이되게"*).
        ///
        /// 전에는 폭 안에 **무작위로** 뿌렸다. 그러면 가장자리가 들쭉날쭉한 **네모 얼룩**이
        /// 되고, 같은 인원이라도 **덜 많아 보인다** — 빈 자리가 사이사이에 생기기 때문이다.
        /// 이 장르의 군중이 둥근 덩어리인 데는 이유가 있다: **원이 같은 면적에 가장 짧은
        /// 둘레**를 가져서, 같은 수가 가장 꽉 차 보인다.
        ///
        /// 황금각(137.5°) 나선으로 놓는다 — 해바라기 씨앗이 그렇게 박히는 방식이고,
        /// **격자가 아니면서도 빈 자리가 안 생긴다.** 격자로 놓으면 줄이 보여 군대 사열이 된다.
        ///
        /// 반지름은 `sqrt(n)` 에 비례한다(사람이 면적을 차지하므로). 한 사람당 0.42 m² 는
        /// 어깨가 거의 닿는 밀도다 — *"최대한 모아줘"* 의 수치다.
        /// </summary>
        void Disc(CrowdField f, int from, int to, float cx, float cz, float w)
        {
            int n = Mathf.Max(1, to - from);
            // **작아진 몸에 맞춰 더 빽빽하게.** 캐릭터가 키 1.86 m → 1.2 m 로 작아졌으니
            // (오너: *"아주 작고 귀엽게"*) 한 사람이 차지하는 면적도 그만큼 준다. 전 수치를
            // 그대로 두면 **작아진 만큼 덩어리에 빈 틈이 생겨** 오히려 덜 많아 보인다
            const float PerPerson = 0.19f * 0.78f * 0.78f;   // 배율의 **제곱** — 면적이라서                    // m² — 어깨가 거의 닿는다
            float r = Mathf.Sqrt(n * PerPerson / Mathf.PI);
            float lim = w * 0.5f - 0.35f;
            const float Golden = 2.39996323f;                 // 라디안 — 황금각
            for (int i = from; i < to; i++)
            {
                int k = i - from;
                // `sqrt` 를 쓰면 **바깥쪽이 안 성기다** — 그냥 k/n 으로 하면 가운데만 빽빽하다
                float rad = r * Mathf.Sqrt((k + 0.5f) / n);
                float a = k * Golden;
                // 아주 약한 흔들림만 — 없으면 나선이 눈에 보이고, 크면 다시 얼룩이 된다
                float j = (f.Phase[i] - 0.5f) * 0.18f;
                f.X[i] = Mathf.Clamp(cx + Mathf.Cos(a) * rad + j, -lim, lim);
                f.Z[i] = cz + Mathf.Sin(a) * rad + (f.SpeedMul[i] - 1f) * 0.9f;
                f.Phase[i] += Time.deltaTime * 1.4f;
                if (f.Phase[i] >= 1f) f.Phase[i] -= 1f;
            }
        }

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
            if (f.Count != n)
            {
                f.Clear();
                f.Add(n, side, 0f, 1f);
            }
            // **빽빽한 원.** 길 안으로 가두는 것은 `Disc` 가 한다 — 시뮬의 `X` 는 군단의
            // **중심**이고 ±길폭/2 까지 가는데, 대형이 거기서 또 퍼지면 가장자리 사람이
            // 벽을 뚫는다. 시뮬은 안 고친다(규칙상 중심이 ±길폭/2 인 것이 맞다) —
            // **보이는 몸만** 당긴다
            Disc(f, 0, f.Count, cx, cz, w);
        }

        /// <summary>
        /// 통 하나. **숫자가 통에 적혀 있는 것**이 이 게임의 간판이라(오너 레퍼런스 3 장)
        /// 글자를 통 지름만큼 크게 쓰고, 그 아래 체력 띠를 둔다 — 쏘는 동안 줄어드는 것이
        /// 보여야 *"지금 이걸 쏘고 있다"* 가 읽힌다.
        /// </summary>
        Transform Barrel(in LevelEvent e, LevelData level, out Transform bar, out Transform mark)
        {
            float half = level.roadWidth * 0.17f;
            float lane = level.roadWidth * 0.25f;

            // **진짜 드럼통** (Quaternius ZA `Barrel.fbx`, CC0 — `THIRD_PARTY_ASSETS` CF_PROP_002).
            // 없으면 원통 + 쇠테로 떨어진다. 모델 크기는 모르니 **경계 상자로 지름을 맞춘다**
            Transform t;
            var barrelMesh = BarrelMesh();
            if (barrelMesh != null)
            {
                var go = new GameObject("Barrel");
                go.transform.SetParent(transform, false);
                go.AddComponent<MeshFilter>().sharedMesh = barrelMesh;
                var mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterial = Box("BarrelMat", BarrelWood).GetComponent<Renderer>().sharedMaterial;
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                t = go.transform;
                // **세운다.** 모델의 긴 축이 Y 가 아닐 수 있다 — 첫 사진에서 통이 옆으로 누워 있었다.
                // 긴 축을 보고 돌려 세우고, 돌린 뒤의 상자로 지름과 바닥을 맞춘다
                var bs0 = barrelMesh.bounds.size;
                Quaternion up = Quaternion.identity;
                if (bs0.x >= bs0.y && bs0.x >= bs0.z) up = Quaternion.Euler(0f, 0f, 90f);
                else if (bs0.z >= bs0.y && bs0.z >= bs0.x) up = Quaternion.Euler(90f, 0f, 0f);
                t.rotation = up;
                // 돌린 상자: 축을 바꿔 치환 (균등 배율이라 크기만 섞이면 된다)
                Vector3 bs = up == Quaternion.identity ? bs0
                           : (bs0.x >= bs0.y && bs0.x >= bs0.z ? new Vector3(bs0.y, bs0.x, bs0.z) : new Vector3(bs0.x, bs0.z, bs0.y));
                float k = (half * 2f) / Mathf.Max(0.01f, Mathf.Max(bs.x, bs.z));
                t.localScale = new Vector3(k, k, k);
                // 바닥: 돌린 뒤 중심이 상자 중심이라 가정하고 높이 절반만큼 띄운다
                t.position = new Vector3(e.lane * lane, bs.y * k * 0.5f, e.z);
                mark = t;
                barrelUpright = bs.y * k;
                Debug.Log($"[CR] 통 세움: 원상자 {bs0} → 회전 {up.eulerAngles} 배율 {k:F3} 높이 {bs.y * k:F2}");
            }
            else
            {
                t = Prim(PrimitiveType.Cylinder, "Barrel", BarrelWood);
                t.localScale = new Vector3(half * 2f, 1.1f, half * 2f);
                t.position = new Vector3(e.lane * lane, 1.1f, e.z);
                mark = t;
                for (int k = 0; k < 2; k++)
                {
                    var hoop = Prim(PrimitiveType.Cylinder, "Hoop", new Color(0.30f, 0.24f, 0.18f));
                    hoop.SetParent(t, false);
                    hoop.localScale = new Vector3(1.04f, 0.07f, 1.04f);
                    hoop.localPosition = new Vector3(0f, k == 0 ? 0.45f : -0.45f, 0f);
                }
            }

            // **숫자.** 부모가 지름만큼 늘어나 있으므로 지역 배율로 되돌린다
            var lab = OutlinedLabel("BarrelText", 0.34f);
            lab.transform.SetParent(t, false);
            lastBarrelLabel = lab.transform;
            lab.transform.localPosition = new Vector3(0f, 0.08f, -0.62f);
            // **글자 폭을 통 지름에서 역산한다.** 기본값을 1.55 → 0.80 → 0.45 로 세 번 줄였는데
            // 웹 사진에서 숫자는 계속 통만 했다. 이유는 FitLabel 이었다: 넘긴 최대 폭이 커서
            // 늘 **키우는 쪽 상한(1.8 배)** 에 걸렸고, 기본값을 줄인 만큼 1.8 배가 도로 붙었다.
            // 줄인 수가 줄지 않는데 세 번을 줄인 것이다.
            //
            // 그래서 짐작을 버리고 **사진에서 쟀다**: 통 지름 3.06 m 옆에서 두 자리 45 가
            // 3.33 m 였고 그때 월드 배율이 0.81 이었으므로, 한 자리는 배율 1 당 2.06 m 다.
            // 지름의 72 % 안에 글자 수만큼 들어가게 맞춘다 — 110 처럼 세 자리면 저절로 좁아진다.
            SetOutlined(lab, e.gives.ToString());
            const float WidthPerDigit = 2.06f;
            float diameter = barrelMesh != null ? half * 2f : t.localScale.x;
            float want = diameter * 0.72f;
            float world = want / (Mathf.Max(1, lab.text.Length) * WidthPerDigit);
            lab.transform.localScale = new Vector3(world / Mathf.Max(0.1f, t.localScale.x),
                                                   world / Mathf.Max(0.1f, t.localScale.y), 1f);
            if (barrelMesh != null)
            {
                // 통이 돌아가 있을 수 있으니 **월드 좌표**로 앞면 가운데에 둔다 (카메라는 -Z 에서 본다)
                lab.transform.SetParent(transform, false);
                lab.transform.rotation = Quaternion.identity;
                lab.transform.position = new Vector3(e.lane * lane, barrelUpright * 0.55f, e.z - half - 0.06f);
                lab.transform.localScale = new Vector3(world, world, 1f);
                barrelLabelLoose = true;
            }

            // 체력 띠 — 가운데에서 줄어들지 않고 **왼쪽에서부터** 줄게 부모를 하나 둔다
            var pivot = new GameObject("BarrelBarPivot").transform;
            pivot.SetParent(transform, false);
            pivot.position = new Vector3(e.lane * lane - half, 2.45f, e.z - half * 0.9f);
            bar = Box("BarrelBar", new Color(0.95f, 0.86f, 0.35f));
            bar.SetParent(pivot, false);
            bar.localScale = new Vector3(half * 2f, 0.14f, 0.12f);
            bar.localPosition = new Vector3(half, 0f, 0f);
            return t;
        }

        /// <summary>
        /// 통의 상태는 **`Battle` 이 들고 있다.** z 로 맞춰 읽는다 — 화면 쪽에서 따로 세면
        /// 둘이 어긋나고, 어긋난 쪽이 플레이어에게 보이는 쪽이다.
        /// </summary>
        void TickBarrels(Sim sim)
        {
            var bt = sim.Battle;
            for (int i = 0; i < barrels.Length; i++)
            {
                var t = barrels[i];
                if (t == null) continue;

                float hp01 = -1f;
                for (int k = 0; k < bt.BarrelCount; k++)
                {
                    if (Mathf.Abs(bt.RZ[k] - barrelZ[i]) > 0.5f) continue;
                    if (barrelLane[i] != 0 && Mathf.Sign(bt.RX[k]) != Mathf.Sign(barrelLane[i])) continue;
                    hp01 = bt.RMaxHp[k] > 0f ? bt.RHp[k] / bt.RMaxHp[k] : 0f;
                    break;
                }
                // 아직 `Battle` 에 안 올라왔으면(사거리 밖) 멀쩡한 통이다
                if (hp01 < 0f) hp01 = barrelZ[i] > sim.Z ? 1f : 0f;

                bool alive = hp01 > 0f;
                if (alive && barrelZ[i] < sim.Z - 2f) alive = false;
                // **다음 줄은 아직 안 보여 준다.** 50 m 밖의 통까지 그리면 그 숫자가 지금 줄
                // 위에 겹쳐서 `45` 와 `14` 가 `4514` 로 읽힌다 (docs/shots/barrel_1_z48.png)
                if (alive && barrelZ[i] > sim.Z + 50f) alive = false;

                // 깨지는 순간 한 번 부푼다 — 보상이 어디서 나왔는지가 보여야 한다
                if (!alive && t.gameObject.activeSelf && barrelPop[i] <= 0f) barrelPop[i] = 0.22f;
                if (barrelPop[i] > 0f)
                {
                    barrelPop[i] -= Time.deltaTime;
                    float k = Mathf.Clamp01(barrelPop[i] / 0.22f);
                    t.localScale = new Vector3(t.localScale.x, 1.1f * (1f + (1f - k) * 0.6f), t.localScale.z);
                    alive = true;
                    if (barrelPop[i] <= 0f) alive = false;
                }

                if (t.gameObject.activeSelf != alive) t.gameObject.SetActive(alive);
                var pivot = barrelBar[i] != null ? barrelBar[i].parent : null;
                if (pivot != null && pivot.gameObject.activeSelf != alive) pivot.gameObject.SetActive(alive);
                if (!alive) continue;

                // **맞고 있는 통은 붉어진다** — 레퍼런스에서 지금 쏘는 통만 빨갛게 물려 있다
                if (barrelMat[i] != null) barrelMat[i].color = Color.Lerp(BarrelHit, BarrelWood, hp01);
                // 띠는 **왼쪽 끝을 축으로** 줄인다 — 부모만 눌러 주면 된다
                if (barrelBar[i] != null && barrelBar[i].parent != null)
                    barrelBar[i].parent.localScale = new Vector3(Mathf.Max(0.001f, hp01), 1f, 1f);
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
            float lane = (play.Level != null ? play.Level.roadWidth : 8f) * 0.5f;
            // **둘 사이를 벌린다.** 전에는 폭 0.90 · 중심 0.50 이라 틈이 0.35 m 뿐이었고,
            // 멀리서 보면 **두 문이 아니라 간판 하나**로 읽혔다 (오너 폰 사진). 이 게임의
            // 조작이 *둘 중 하나* 라서, 둘로 안 보이면 조작이 화면에서 사라진 것이다
            // **반투명 판.** 라스트워의 게이트는 유리 같은 판이라 뒤의 적이 비친다 — 불투명 네모는
            // 벽으로 읽혀 *지나가도 되나* 를 망설이게 한다
            var t = Pane("Gate", new Color(col.r, col.g, col.b, 0.55f));
            t.localScale = new Vector3(lane * 0.78f, 3.2f, 0.25f);
            t.position = new Vector3(o.lane * lane * 0.60f, 1.6f, z);
            // **테두리.** 색판만 있으면 공중에 뜬 종이로 보인다. 뒤에 조금 큰 어두운 판을
            // 깔면 가장자리가 생겨 *문틀* 로 읽힌다 — 드로우 하나 값이다
            //
            // **문의 자식으로 붙인다.** 따로 두면 `gates[]` 가 모르니 지나간 뒤에도 남고,
            // 연출(`GatePassFx`)이 문을 터뜨려도 테두리만 제자리에 서 있다 — 문이 사라진
            // 자리에 틀만 남는 그림이다
            // 테두리는 **밝게** — 유리판의 가장자리는 빛을 받는다. 어두운 틀은 창살처럼 보였다
            var frame = Box("GateFrame", Color.Lerp(col, Color.white, 0.55f));
            frame.SetParent(t, false);
            frame.localScale = new Vector3(1f + 0.34f / t.localScale.x,
                                           1f + 0.30f / t.localScale.y,
                                           0.16f / t.localScale.z);
            frame.localPosition = new Vector3(0f, 0.01f, 0.4f);
            var lab = OutlinedLabel("GateText", 0.34f);
            lab.transform.SetParent(t, false);
            lab.transform.localPosition = new Vector3(0f, 0.06f, -0.6f);
            // 부모가 차선만큼 넓어졌으므로 **자식의 지역 배율로 돌려놓는다** — 안 그러면
            // 글자가 네모와 같이 늘어나 길쭉해진다
            lab.transform.localScale = new Vector3(0.55f / Mathf.Max(0.1f, t.localScale.x),
                                                   0.55f / t.localScale.y, 1f);
            SetOutlined(lab, Sign(o.op) + o.value);
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
            float rw = play.Level != null ? play.Level.roadWidth : 8f;
            float zw = e.lane != 0 ? rw * 0.5f : rw;
            float zx = e.lane != 0 ? e.lane * rw * 0.25f : 0f;
            var t = Box("Zone", new Color(0.66f, 0.38f, 0.56f, 1f));
            t.localScale = new Vector3(zw, 0.06f, e.zoneLength);
            t.position = new Vector3(zx, 0.01f, e.z + e.zoneLength * 0.5f);
            return t;
        }

        /// <summary>
        /// **끝이 보여야 달릴 이유가 있다.**
        ///
        /// 이 장르는 *"가능한 많은 병력을 결승선까지 데려가, 끝에서 보스와 붙는다"* 가 한 판의
        /// 모양이다 (`docs/REF_TOPWAR.md` §4 — 인터넷에서 확인한 장르 규칙). 그런데 우리 복도는
        /// **그냥 끝났다**: 목표가 화면에 없으니 달리는 것이 *어디로* 가 아니라 *그냥 앞으로* 였다.
        ///
        /// 문 모양으로 세우는 이유: 게이트와 같은 어휘다. 플레이어는 이미 *문은 지나가는 것*
        /// 으로 배웠으니, 끝의 큰 문은 **"저기까지 가면 된다"** 로 바로 읽힌다.
        ///
        /// 색은 게이트 어느 쪽과도 안 겹치게 금색으로 둔다 — 파랑(+)·초록(×)·빨강(−÷)은
        /// 이미 뜻이 있다.
        /// </summary>
        void Goal(LevelData level, System.Collections.Generic.List<Transform> into)
        {
            float w = level.roadWidth;
            var gold = new Color(0.92f, 0.76f, 0.30f);
            float z = level.length;

            // 기둥 둘 + 가로대 — 멀리서 **문 하나**로 읽힌다
            for (int i = 0; i < 2; i++)
            {
                var post = Box("Goal", gold);
                post.localScale = new Vector3(0.55f, 4.2f, 0.55f);
                post.position = new Vector3((i == 0 ? -1f : 1f) * (w * 0.5f - 0.28f), 2.1f, z);
                into.Add(post);
            }
            var bar = Box("Goal", gold);
            bar.localScale = new Vector3(w, 0.9f, 0.55f);
            bar.position = new Vector3(0f, 4.2f, z);
            into.Add(bar);

            // 바닥의 결승선 — 가로대만으로는 **어디서 끝나는지**가 땅에 안 적힌다
            var line = Box("Goal", new Color(0.96f, 0.93f, 0.82f));
            line.localScale = new Vector3(w, 0.06f, 0.9f);
            line.position = new Vector3(0f, 0.02f, z);
            into.Add(line);
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
            float rw = play.Level != null ? play.Level.roadWidth : 8f;
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

        Transform Box(string name, Color c) { return Prim(PrimitiveType.Cube, name, c); }

        static Mesh barrelMeshCache; static bool barrelMeshTried;
        static Mesh BarrelMesh()
        {
            if (barrelMeshTried) return barrelMeshCache;
            barrelMeshTried = true;
            var go = Resources.Load<GameObject>("Props/Barrel");
            // **정점이 가장 많은 메시를 고른다.** FBX 에 메시가 여럿이면(충돌용 상자 등) 첫 번째가
            // 에디터와 빌드에서 다를 수 있다 — 웹에서 통이 네모 상자로 나왔다 (2026-10-18)
            Mesh best = null;
            if (go != null)
                foreach (var f in go.GetComponentsInChildren<MeshFilter>(true))
                    if (f.sharedMesh != null && (best == null || f.sharedMesh.vertexCount > best.vertexCount)) best = f.sharedMesh;
            barrelMeshCache = best;
            if (best != null)
                Debug.Log($"[CR] 통 메시 {best.name} 정점 {best.vertexCount} 서브메시 {best.subMeshCount} 상자 {best.bounds.size} 중심 {best.bounds.center}");
            if (barrelMeshCache == null) Debug.LogWarning("[CR] Props/Barrel 을 못 읽었다 — 원통으로 그린다");
            return barrelMeshCache;
        }

        /// <summary>반투명 판. `Legacy Shaders/Transparent/Diffuse` — `WebBuild.EnsureShaders` 에 올려 둔 것</summary>
        Transform Pane(string name, Color c)
        {
            var t = Prim(PrimitiveType.Cube, name, c);
            var sh = Shader.Find("Legacy Shaders/Transparent/Diffuse");
            if (sh != null)
            {
                var m = new Material(sh) { color = c };
                t.GetComponent<Renderer>().sharedMaterial = m;
            }
            return t;
        }

        Transform Prim(PrimitiveType kind, string name, Color c)
        {
            var go = GameObject.CreatePrimitive(kind);
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

        /// <summary>
        /// **흰 글자 + 짙은 외곽선** (라스트워의 숫자). `TextMesh` 에는 외곽선이 없으니 같은 글자를
        /// 어둡게 한 장 더, 살짝 뒤·아래에 둔다 — 드로우 하나 값으로 글자가 어디서나 읽힌다.
        /// 돌려주는 것은 앞 글자이고, 뒷 글자는 자식이라 같이 움직인다. `text` 는 `SetOutlined` 로 넣는다.
        /// </summary>
        TextMesh OutlinedLabel(string name, float size)
        {
            var front = Label(name, size);
            var back = Label(name + "Outline", size);
            back.transform.SetParent(front.transform, false);
            back.transform.localPosition = new Vector3(0.035f, -0.035f, 0.02f);
            back.color = new Color(0.06f, 0.05f, 0.08f, 1f);
            back.fontStyle = FontStyle.Bold;
            return front;
        }

        static void SetOutlined(TextMesh front, string text)
        {
            front.text = text;
            front.fontStyle = FontStyle.Bold;
            var back = front.transform.childCount > 0 ? front.transform.GetChild(0).GetComponent<TextMesh>() : null;
            if (back != null) back.text = text;
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
