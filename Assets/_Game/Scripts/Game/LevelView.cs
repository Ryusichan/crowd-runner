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
        Transform road, wall, divider;
        Transform[] gates = new Transform[0];
        Transform[] zones = new Transform[0];
        TextMesh countLabel;
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
            cam.transform.rotation = Quaternion.Euler(34f, 0f, 0f);
            cam.fieldOfView = 52f;
            cam.farClipPlane = 300f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.62f, 0.72f, 0.80f);

            var li = new GameObject("Sun").AddComponent<Light>();
            li.type = LightType.Directional;
            li.intensity = 1.05f;
            li.shadows = LightShadows.None;   // 400 개체의 그림자는 드로우를 두 배로 만든다 (§6b)
            li.transform.rotation = Quaternion.Euler(52f, -28f, 0f);

            road = Box("Road", new Color(0.46f, 0.50f, 0.44f));
            countLabel = Label("Count", 0.5f);
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
            var z = new System.Collections.Generic.List<Transform>();
            foreach (var e in level.events)
            {
                if (e.kind == EventKind.Gate)
                    foreach (var o in e.options) g.Add(Gate(e.z, o));
                else if (e.kind == EventKind.Zone)
                    z.Add(Zone(e));
                else if (e.kind == EventKind.Wall)
                {
                    wall = Box("Wall", new Color(0.52f, 0.46f, 0.40f));
                    wall.localScale = new Vector3(level.roadWidth, 2.2f, 0.9f);
                    wall.position = new Vector3(0f, 1.1f, e.z);
                    wallHp0 = e.wallHp;
                }
            }
            gates = g.ToArray();
            zones = z.ToArray();

            // 차선 칸막이 — 게이트를 고르면 합류 지점까지 **길이 갇힌다** (세션 A `commitUntilZ`).
            // 안 보이면 플레이어는 **자기가 왜 못 넘어가는지 모른다** — 광고 그림처럼 벽이 서야 한다
            divider = Box("Divider", new Color(0.86f, 0.84f, 0.78f));
            divider.gameObject.SetActive(false);
        }

        void Update()
        {
            var sim = runner.Sim;
            if (sim == null) return;

            float w = sim.RoadWidth;
            // 길은 카메라를 따라온다 — 레벨 전체 길이로 한 장 깔면 멀리서 z 정밀도가 깨진다
            road.localScale = new Vector3(w, 0.1f, 240f);
            road.position = new Vector3(0f, -0.05f, sim.Z + 60f);

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

            countLabel.text = sim.Units.ToString();
            countLabel.transform.position = new Vector3(sim.X, 3.4f, sim.Z + 1.5f);
            countLabel.transform.rotation = cam.transform.rotation;
            countLabel.color = sim.State == SimState.Lost ? new Color(0.8f, 0.3f, 0.3f) : Color.white;

            cam.transform.position = new Vector3(0f, 19f, sim.Z - 26f);
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
            if (f.Count != n)
            {
                f.Clear();
                f.Add(n, side, 0f, w * 0.92f);
            }
            // 중심을 옮긴다 — 개체마다의 흩어짐은 `CrowdField` 가 만든 것을 그대로 쓴다
            for (int i = 0; i < f.Count; i++)
            {
                f.X[i] = cx + f.SideTarget[i];
                f.Z[i] = cz + (f.Phase[i] - 0.5f) * 3.2f + (f.SpeedMul[i] - 1f) * 18f;
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
            var t = Box("Gate", col);
            t.localScale = new Vector3(2.6f, 2.0f, 0.2f);
            t.position = new Vector3(o.lane * 2.1f, 1.0f, z);
            var lab = Label("GateText", 0.34f);
            lab.transform.SetParent(t, false);
            lab.transform.localPosition = new Vector3(0f, 0.25f, -0.6f);
            lab.transform.localScale = Vector3.one * 0.4f;
            lab.text = Sign(o.op) + o.value;
            return t;
        }

        static string Sign(GateOp op) =>
            op == GateOp.Add ? "+" : op == GateOp.Multiply ? "×" : op == GateOp.Subtract ? "−" : "÷";

        Transform Zone(LevelEvent e)
        {
            // 지역은 머릿수에 비례해 깎는다 (세션 A) — **큰 군단이 지나갈 때 눈에 보이게 많이
            // 녹아야** 대가가 느껴진다. 그 연출은 다음 단위이고, 지금은 *바닥이 보이는 것*까지다
            var t = Box("Zone", new Color(0.66f, 0.38f, 0.56f, 1f));
            t.localScale = new Vector3(runner.Level != null ? runner.Level.roadWidth : 8f, 0.06f, e.zoneLength);
            t.position = new Vector3(0f, 0.01f, e.z + e.zoneLength * 0.5f);
            return t;
        }

        Transform Box(string name, Color c)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            Destroy(go.GetComponent<Collider>());   // 물리 엔진을 안 쓴다 (`DESIGN.md` §2)
            var sh = Shader.Find("Mobile/Diffuse") ?? Shader.Find("Legacy Shaders/Diffuse") ?? Shader.Find("Standard");
            go.GetComponent<Renderer>().sharedMaterial = new Material(sh) { color = c };
            return go.transform;
        }

        TextMesh Label(string name, float size)
        {
            var go = new GameObject(name);
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
