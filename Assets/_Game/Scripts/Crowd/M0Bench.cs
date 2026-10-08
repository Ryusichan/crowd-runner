using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Profiling;

namespace Game.Crowd
{
    /// <summary>
    /// **M0 측정기** — 군중 렌더링 방식의 비용을 기기에서 숫자로 뽑는다 (`docs/M0_CROWD.md`).
    ///
    /// 화면을 보지 않고 답이 나와야 한다 (오너 규칙: 에디터 창 금지). 모든 결과는 `[M0]` 로 시작하는
    /// 로그 한 줄이고 `adb logcat` 으로 거둔다. 두 방식은 **한 빌드 · 한 장면**에서 번갈아 잰다 —
    /// 빌드를 나누면 그 사이 기기 온도가 변하고 그 차이가 방식 차이로 보인다.
    ///
    /// ## 이 측정기가 틀렸던 자리 넷 (2026-10-08, 세 번의 주행으로)
    ///
    /// 1. **정지를 프레임으로 셌다** — 폰이 잠겨 Unity 가 멈췄고 재개된 한 프레임이 `117,267 ms` 로
    ///    찍혀 그것이 `p99` 가 됐다. 게다가 `남은 초 -= 그 값` 이라 **일곱 구간을 한 프레임이 삼켜**
    ///    한 줄만 나왔다. → 구간은 **프레임 수**로 세고, `SuspendMs` 넘는 프레임은 분포에서 빼고
    ///    **뺀 수를 찍는다**. `MinFrames` 미만이면 그 구간은 **무효**라고 말한다.
    /// 2. **벽시계 프레임 시간을 쟀다** — `vSyncCount=0`·`targetFrameRate=-1` 로도 안드로이드는
    ///    vsync 를 끌 수 없다. 그래서 **아무것도 안 그리는 B 까지 `p50=33.35ms`**(정확히 30 fps)로
    ///    나왔다. 묶인 수는 비용이 아니라 **대기 시간**이다. → `FrameTimingManager` 의
    ///    **일한 시간**(메인스레드 − present 대기)과 **GPU 시간**을 쓴다. 묶여 있어도 일한 양은 보인다.
    /// 3. **구간당 타이밍을 한 번만 샘플링했다** — `Report()` 에서 `GetLatestTimings(1, …)` 한 번.
    ///    900 프레임을 돌고 **마지막 한 프레임**으로 그 구간을 말하고 있었다. → 매 프레임 모아
    ///    중간값·p99 를 낸다.
    /// 4. **증식 봉우리가 독립변수를 바꿨다** — 5 초마다 120 명을 *더하니* `A-100` 구간이 실제로는
    ///    **820 명**이었고, 네 점 스윕이 전부 1,000 근처로 뭉쳐 **곡선이 사라졌다**. → 봉우리는
    ///    **수를 바꾸지 않는다**: 120 명을 끄고 같은 프레임에 다시 켠다 (봉우리는 그대로, 수는 고정).
    /// </summary>
    public sealed class M0Bench : MonoBehaviour
    {
        public enum Method { AnimatorPerUnit, InstancedVat }

        struct Seg
        {
            public Method method; public int units; public int frames; public string label;
            public Seg(Method m, int u, int f, string l) { method = m; units = u; frames = f; label = l; }
        }

        /// <summary>
        /// **곡선을 뽑는다, 점 하나가 아니라.** 1,000 에서 떨어졌다는 사실만으로는 설계를 못 정한다 —
        /// *어디서* 떨어지는지를 알아야 절충안(가까운 것만 스킨드, 먼 것은 인스턴싱)이 얼마를
        /// 쓸 수 있는지 나온다. 마지막 줄은 **같은 조건을 뒤에 한 번 더** — 스로틀링을 본다.
        /// </summary>
        static readonly Seg[] Plan =
        {
            new Seg(Method.AnimatorPerUnit,  250,  600, "warmup(버림)"),
            new Seg(Method.InstancedVat,    1000,  600, "baseline(빈 장면)"),
            // **절편을 가른다.** 4 차에서 `A-100` 의 GPU 가 벌써 14.1 ms 였는데 빈 장면은 0.72 다 —
            // 100 개(정점 15 만)로 14 ms 는 Mali-G78 에서 말이 안 되는 수다. 그러면 **개체 수와
            // 무관한 고정 비용**이 섞여 있다는 뜻이고, 그걸 안 가르면 *"몇을 그릴 수 있나"* 에
            // 답할 수 없다 — 절편이 14 면 100 이든 250 이든 이미 예산 밖이다.
            // `A-0` 은 방식 A 를 켜 두고 **아무도 안 세운다**: 남는 수가 곧 고정 비용이다
            new Seg(Method.AnimatorPerUnit,    0,  600, "A-0(고정비용)"),
            new Seg(Method.AnimatorPerUnit,   50,  600, "A-50"),
            new Seg(Method.AnimatorPerUnit,  100,  600, "A-100"),
            new Seg(Method.AnimatorPerUnit,  250,  600, "A-250"),
            new Seg(Method.AnimatorPerUnit,  500,  600, "A-500"),
            new Seg(Method.AnimatorPerUnit, 1000,  600, "A-1000"),
            new Seg(Method.AnimatorPerUnit, 1000,  600, "A-1000(재측정)"),
        };

        const float Dt = 1f / 60f;
        const float Speed = 4.5f;
        const float Lane = 5.5f;
        /// <summary>이보다 긴 프레임은 **프레임이 아니라 정지**다 — 분포에서 뺀다 (뺀 수는 찍는다)</summary>
        const float SuspendMs = 400f;
        /// <summary>이보다 적게 모였으면 그 구간은 **무효**다 — 9 프레임으로 p99 를 말할 수 없다</summary>
        const int MinFrames = 300;
        /// <summary>봉우리 간격 (프레임) — 증식 순간이 p99 를 만든다</summary>
        const int BurstEvery = 150;
        const int BurstN = 120;

        CrowdField field;
        ICrowdRenderer[] renderers;
        Method method = Method.AnimatorPerUnit;

        int seg = -1, segFrames, suspended, burstIn;
        readonly List<float> wall = new List<float>(1024);
        readonly List<float> cpuWork = new List<float>(1024);
        readonly List<float> gpu = new List<float>(1024);
        readonly FrameTiming[] timing = new FrameTiming[1];
        long monoAtStart;
        float startRealtime, dummySink;

        void Awake()
        {
            // vsync 는 안드로이드에서 **끌 수 없다** — 그래서 아래 §2 대로 벽시계가 아니라
            // `FrameTimingManager` 의 일한 시간을 쓴다. 그래도 요청은 해 둔다 (묶임이 느슨해질 수 있다)
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = 300;
            // **초점을 잃어도 계속 돈다** — 잠금화면 뒤로 밀렸을 때 측정기가 정지를 재던 자리
            Application.runInBackground = true;
            Screen.sleepTimeout = SleepTimeout.NeverSleep;
            FrameTimingManager.CaptureFrameTimings();

            field = new CrowdField(1000, 20261008);
            renderers = new ICrowdRenderer[2];
            renderers[(int)Method.AnimatorPerUnit] = new AnimatorCrowd();
            renderers[(int)Method.InstancedVat] = new VatCrowd();
            foreach (var r in renderers) r.Init(1000);

            // **측정 조건을 같이 찍는다.** 특히 새로고침률과 절전 — 폰이 절전 모드면 클럭이
            // 제한되고, 그걸 모른 채 읽은 수는 *기기의 한계* 가 아니라 *그때 설정* 이다
            Debug.Log($"[M0] start device={SystemInfo.deviceModel} gpu={SystemInfo.graphicsDeviceName} " +
                      $"api={SystemInfo.graphicsDeviceType} os={SystemInfo.operatingSystem} " +
                      $"refresh={Screen.currentResolution.refreshRateRatio.value:F1}Hz " +
                      $"verts={StandInInfo.Verts} bones={StandInInfo.Bones} " +
                      $"gpuSkinning={SystemInfo.supportsComputeShaders} vsync={QualitySettings.vSyncCount}");
            NextSeg();
        }

        void NextSeg()
        {
            if (seg >= 0) Report();
            seg++;
            if (seg >= Plan.Length)
            {
                Debug.Log("[M0] done");
                Application.Quit();     // 끝나면 스스로 나간다 — 사람이 폰을 들여다보지 않아도 되게
                enabled = false;
                return;
            }
            var p = Plan[seg];
            method = p.method;
            Rebuild(p.units);
            segFrames = 0; suspended = 0; burstIn = BurstEvery;
            wall.Clear(); cpuWork.Clear(); gpu.Clear();
            monoAtStart = Profiler.GetMonoUsedSizeLong();
            startRealtime = Time.realtimeSinceStartup;
        }

        void Rebuild(int units)
        {
            foreach (var r in renderers) r.SetActive(false);
            field.Clear();
            int half = units / 2;
            field.Add(half, 0, -20f, Lane * 2f);
            field.Add(units - half, 1, 20f, Lane * 2f);
            renderers[(int)method].SetActive(true);
            renderers[(int)method].Sync(field);
        }

        void Update()
        {
            if (seg < 0 || seg >= Plan.Length) return;

            float ms = Time.unscaledDeltaTime * 1000f;
            if (ms > SuspendMs) suspended++;        // 정지는 프레임이 아니다 — 버리고 **센다**
            else wall.Add(ms);

            if (FrameTimingManager.GetLatestTimings(1, timing) == 1)
            {
                // **일한 시간 = 메인스레드 − present 대기.** 대기를 빼지 않으면 vsync 에 묶인
                // 33.3 ms 가 그대로 찍히고, 그러면 아무것도 안 그리는 구간도 33.3 이 된다
                float work = (float)timing[0].cpuMainThreadFrameTime - (float)timing[0].cpuMainThreadPresentWaitTime;
                if (work > 0f && work < SuspendMs) cpuWork.Add(work);
                float g = (float)timing[0].gpuFrameTime;
                if (g > 0f && g < SuspendMs) gpu.Add(g);
            }

            field.Step(Dt, Speed, Lane);
            dummySink += field.NearestOpponentPass(8f);
            renderers[(int)method].Sync(field);

            if (--burstIn <= 0)
            {
                burstIn = BurstEvery;
                // **수를 바꾸지 않는 봉우리.** 더하면 독립변수가 변해 스윕이 무너진다 (§4) —
                // 꺼고 같은 프레임에 다시 켜면 활성화 비용은 그대로 들고 수는 고정이다
                renderers[(int)method].Churn(field, BurstN);
            }

            segFrames++;
            if (segFrames >= Plan[seg].frames) NextSeg();
        }

        void Report()
        {
            var p = Plan[seg];
            wall.Sort(); cpuWork.Sort(); gpu.Sort();
            long mono = Profiler.GetMonoUsedSizeLong() - monoAtStart;
            float perFrame = wall.Count > 0 ? mono / (float)wall.Count : 0f;
            string verdict = wall.Count < MinFrames ? "  **무효**(프레임 부족)"
                           : gpu.Count < MinFrames ? "  **무효**(GPU 타이밍 부족)"
                           : suspended > 0 ? "  (정지 " + suspended + ")" : "";

            Debug.Log($"[M0] {p.label,-18} {p.method,-16} units={field.Count,4} " +
                      $"cpu일={Q(cpuWork, .5f),6:F2}/{Q(cpuWork, .99f),6:F2}ms " +
                      $"gpu={Q(gpu, .5f),6:F2}/{Q(gpu, .99f),6:F2}ms " +
                      $"벽시계={Q(wall, .5f),6:F2}/{Q(wall, .99f),6:F2}ms " +
                      $"mono/frame={perFrame,6:F0}B draws={renderers[(int)p.method].DrawCalls,5} " +
                      $"frames={wall.Count}/{gpu.Count} {Time.realtimeSinceStartup - startRealtime:F0}s{verdict}");
        }

        static float Q(List<float> v, float q)
        {
            if (v.Count == 0) return -1f;
            return v[Mathf.Clamp(Mathf.RoundToInt(q * (v.Count - 1)), 0, v.Count - 1)];
        }
    }

    /// <summary>대역 캐릭터의 두 수 — 로그에 같이 찍어서 **무엇을 재고 있었는지** 남긴다</summary>
    public static class StandInInfo
    {
        public const int Bones = 20;
        public const int Verts = 1500;
    }

    /// <summary>군중을 그리는 방식 하나. **같은 좌표 배열을 받아** 그리기만 한다</summary>
    public interface ICrowdRenderer
    {
        void Init(int cap);
        void SetActive(bool on);
        void Sync(CrowdField f);
        /// <summary>증식 봉우리 — `n` 개를 끄고 같은 프레임에 다시 켠다 (**수는 안 바뀐다**)</summary>
        void Churn(CrowdField f, int n);
        int DrawCalls { get; }
    }
}
