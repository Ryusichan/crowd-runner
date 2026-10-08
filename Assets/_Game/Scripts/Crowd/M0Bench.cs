using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Profiling;

namespace Game.Crowd
{
    /// <summary>
    /// **M0 측정기** — 군중 렌더링 방식의 비용을 기기에서 숫자로 뽑는다 (`docs/M0_CROWD.md`).
    ///
    /// 화면을 보지 않고 답이 나와야 한다 (오너 규칙: 에디터 창 금지). 그래서 모든 결과는
    /// `[M0]` 로 시작하는 **로그 한 줄**이고, `adb logcat -s Unity` 로 거둔다.
    ///
    /// **한 빌드 안에서 방식을 바꿔 가며 번갈아 잰다.** 빌드를 둘로 나누면 그 사이에 기기 온도와
    /// 배경 작업이 변하고, 그 차이가 **방식의 차이로 보인다** (§2).
    ///
    /// **구간마다 한 줄을 찍는다.** 한 줄에 중간값·p99·할당·메모리가 같이 있어야, 나중에 그 줄만
    /// 보고 *무엇을 재고 있었는지* 알 수 있다 — 숫자만 있고 조건이 없는 줄은 며칠 뒤에 못 읽는다.
    /// </summary>
    public sealed class M0Bench : MonoBehaviour
    {
        public enum Method { AnimatorPerUnit, InstancedVat }

        /// <summary>한 구간: 이 방식 · 이 개체 수로 `seconds` 초 돌고 한 줄 찍는다</summary>
        struct Seg
        {
            public Method method; public int units; public float seconds; public string label;
            public Seg(Method m, int u, float s, string l) { method = m; units = u; seconds = s; label = l; }
        }

        /// <summary>
        /// **곡선을 뽑는다, 점 하나가 아니라.** 1,000 에서 떨어졌다는 사실만으로는 설계를 못 정한다 —
        /// *어디서* 떨어지는지를 알아야 절충안(가까운 것만 스킨드, 먼 것은 인스턴싱)이 얼마를
        /// 쓸 수 있는지가 나온다. 그래서 A 를 네 점에서 잰다.
        ///
        /// 앞의 워밍업 구간은 **버리는 구간**이다 (§3 함정 3: 셰이더·텍스처 업로드·JIT 가 섞인다).
        /// </summary>
        static readonly Seg[] Plan =
        {
            new Seg(Method.AnimatorPerUnit, 250,  30f, "warmup(버림)"),
            new Seg(Method.AnimatorPerUnit, 100,  20f, "A-100"),
            new Seg(Method.AnimatorPerUnit, 250,  20f, "A-250"),
            new Seg(Method.AnimatorPerUnit, 500,  20f, "A-500"),
            new Seg(Method.AnimatorPerUnit, 1000, 20f, "A-1000"),
            new Seg(Method.InstancedVat,    1000, 20f, "B-1000"),
            new Seg(Method.AnimatorPerUnit, 1000, 20f, "A-1000(재측정)"),   // 스로틀링 — 같은 조건을 뒤에 한 번 더
        };

        const float Dt = 1f / 60f;
        const float Speed = 4.5f;
        const float Lane = 5.5f;
        /// <summary>증식 봉우리를 일부러 만든다 — 안 만들면 p99 가 쉬운 쪽만 잰다 (§3)</summary>
        const float BurstEvery = 5f;

        CrowdField field;
        ICrowdRenderer[] renderers;
        Method method = Method.AnimatorPerUnit;

        int seg = -1;
        float segLeft, nextBurst;
        readonly List<float> frames = new List<float>(4096);
        long allocAtSegStart;
        long monoAtSegStart;
        float segStartRealtime;
        bool dummyPass = true;
        float dummySink;

        void Awake()
        {
            // **vsync 를 푼다.** S21 은 120 Hz 라 켜 두면 프레임 시간이 8.3 ms 로 양자화되고,
            // 4 ms 를 쓰는 군중이 8.3 으로 읽혀 **두 방식이 같은 수로 나온다** (§3 함정 1)
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = -1;
            FrameTimingManager.CaptureFrameTimings();   // 첫 호출로 수집을 켠다

            field = new CrowdField(1000, 20261008);
            renderers = new ICrowdRenderer[2];
            renderers[(int)Method.AnimatorPerUnit] = new AnimatorCrowd();
            renderers[(int)Method.InstancedVat] = new VatCrowd();
            foreach (var r in renderers) r.Init(1000);

            Debug.Log($"[M0] start device={SystemInfo.deviceModel} gpu={SystemInfo.graphicsDeviceName} " +
                      $"os={SystemInfo.operatingSystem} verts={StandInInfo.Verts} bones={StandInInfo.Bones} " +
                      $"vsync={QualitySettings.vSyncCount} target={Application.targetFrameRate}");
            NextSeg();
        }

        void NextSeg()
        {
            if (seg >= 0) Report();
            seg++;
            if (seg >= Plan.Length)
            {
                Debug.Log("[M0] done");
                // **끝나면 스스로 나간다** — 사람이 폰을 들여다보지 않아도 되게
                Application.Quit();
                enabled = false;
                return;
            }
            var p = Plan[seg];
            method = p.method;
            Rebuild(p.units);
            segLeft = p.seconds;
            nextBurst = BurstEvery;
            frames.Clear();
            allocAtSegStart = Profiler.GetTotalAllocatedMemoryLong();
            monoAtSegStart = Profiler.GetMonoUsedSizeLong();
            segStartRealtime = Time.realtimeSinceStartup;
        }

        /// <summary>구간을 바꿀 때 군중을 다시 세운다. 절반은 아군, 절반은 적 — 서로 지나치게 둔다</summary>
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
            float real = Time.unscaledDeltaTime;
            frames.Add(real * 1000f);

            field.Step(Dt, Speed, Lane);
            if (dummyPass) dummySink += field.NearestOpponentPass(8f);
            renderers[(int)method].Sync(field);

            nextBurst -= real;
            if (nextBurst <= 0f)
            {
                nextBurst = BurstEvery;
                // 한 프레임에 수백 — 이것이 p99 를 만드는 순간이다
                int before = field.Count;
                field.Add(120, (byte)(Random.value < 0.5f ? 0 : 1), Random.Range(-15f, 15f), Lane * 2f);
                if (field.Count > before) renderers[(int)method].Sync(field);
            }

            segLeft -= real;
            if (segLeft <= 0f) NextSeg();
        }

        void Report()
        {
            var p = Plan[seg];
            frames.Sort();
            float p50 = Pick(0.50f), p95 = Pick(0.95f), p99 = Pick(0.99f);
            long alloc = Profiler.GetTotalAllocatedMemoryLong() - allocAtSegStart;
            long mono = Profiler.GetMonoUsedSizeLong() - monoAtSegStart;
            float dur = Time.realtimeSinceStartup - segStartRealtime;
            float perFrameMono = frames.Count > 0 ? mono / (float)frames.Count : 0f;

            // CPU/GPU 를 **갈라서** 찍는다 — 어느 쪽을 고칠지가 그 두 수에서 정해진다 (§3)
            float cpu = -1f, gpu = -1f;
            var t = new FrameTiming[1];
            if (FrameTimingManager.GetLatestTimings(1, t) == 1)
            { cpu = (float)t[0].cpuFrameTime; gpu = (float)t[0].gpuFrameTime; }

            Debug.Log($"[M0] {p.label,-16} method={p.method} units={field.Count,4} " +
                      $"p50={p50,6:F2}ms p95={p95,6:F2}ms p99={p99,6:F2}ms " +
                      $"cpu={cpu,6:F2} gpu={gpu,6:F2} " +
                      $"mono/frame={perFrameMono,8:F0}B total={alloc / 1024 / 1024,4}MB " +
                      $"frames={frames.Count} dur={dur:F1}s draws={renderers[(int)p.method].DrawCalls}");
        }

        float Pick(float q)
        {
            if (frames.Count == 0) return -1f;
            int i = Mathf.Clamp(Mathf.RoundToInt(q * (frames.Count - 1)), 0, frames.Count - 1);
            return frames[i];
        }
    }

    /// <summary>대역 캐릭터의 두 수 — 측정 로그에 같이 찍어서 **무엇을 재고 있었는지** 남긴다</summary>
    public static class StandInInfo
    {
        public const int Bones = 20;
        public const int Verts = 1500;
    }

    /// <summary>
    /// 군중을 그리는 방식 하나. **같은 좌표 배열을 받아** 그리기만 한다 — 두 방식이 다른 것을
    /// 그리면 비교가 아니다.
    /// </summary>
    public interface ICrowdRenderer
    {
        void Init(int cap);
        void SetActive(bool on);
        /// <summary>좌표를 화면으로. 개체 수가 늘었으면 그만큼 더 그린다</summary>
        void Sync(CrowdField f);
        int DrawCalls { get; }
    }
}
