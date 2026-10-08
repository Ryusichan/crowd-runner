using System;
using System.IO;
using System.Runtime.InteropServices;
using UnityEngine;
using CrowdRunner.Core;

namespace CrowdRunner.Meta
{
    /// <summary>한 판의 기록. ☣ 와 **가장 잘했을 때 남은 수**를 같이 둔다</summary>
    [Serializable]
    public class LevelRecord
    {
        public string code;
        public int grade;       // 0~3
        public int best;        // 가장 잘했을 때의 잔여 병력
        public int plays;
    }

    [Serializable]
    public class MetaData
    {
        public int version = 1;
        public LevelRecord[] levels = new LevelRecord[0];
        /// <summary>처음 켰을 때의 안내를 봤나 (한 번만 보여 준다)</summary>
        public bool introSeen;
    }

    /// <summary>
    /// **진행을 파일에 남긴다.** 좀비퀸의 `Save/SaveService` 와 같은 모양이고, 거기서 비싸게
    /// 배운 것 셋을 처음부터 넣는다:
    ///
    /// ① **PlayerPrefs 가 아니라 JSON 파일.** 키가 늘면 PlayerPrefs 는 읽을 수 없는 더미가 되고,
    ///    옮길 때 키마다 이사 코드를 써야 한다. 좀비퀸은 나중에 옮기면서 그 값을 치렀다.
    /// ② **임시 파일에 쓰고 바꿔 끼운다 + `.bak`.** 쓰는 중에 앱이 죽으면 반쯤 쓴 파일이
    ///    남는데, 그 파일은 **열리지도 않으면서 지워지지도 않아** 진행이 통째로 사라진다.
    /// ③ **WebGL 은 쓰고 나서 `syncfs` 를 불러야 남는다.** 안 부르면 새로고침에 사라지고,
    ///    오너는 웹으로 확인하므로 그게 곧 *"진행이 안 저장된다"* 로 보고된다.
    /// </summary>
    public static class MetaSave
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")] static extern void CRSyncFs();
#endif

        const string FileName = "crowdrunner_save.json";

        static MetaData data;
        static string dirOverride;      // 테스트가 격리할 때 쓴다

        /// <summary>테스트가 자기 폴더를 쓰게 한다 — 진짜 저장을 건드리지 않도록</summary>
        public static void UseDirectory(string dir) { dirOverride = dir; data = null; }

        static string Dir => dirOverride ?? Application.persistentDataPath;
        static string Path0 => System.IO.Path.Combine(Dir, FileName);
        static string PathBak => Path0 + ".bak";
        static string PathTmp => Path0 + ".tmp";

        public static MetaData Data
        {
            get
            {
                if (data == null) Load();
                return data;
            }
        }

        public static void Load()
        {
            data = Read(Path0) ?? Read(PathBak) ?? new MetaData();
        }

        static MetaData Read(string path)
        {
            try
            {
                if (!File.Exists(path)) return null;
                var d = JsonUtility.FromJson<MetaData>(File.ReadAllText(path));
                // **빈 객체도 역직렬화는 성공한다.** 그래서 모양을 한 번 본다 —
                // 안 보면 깨진 파일을 "정상인 새 저장" 으로 읽고 진행을 덮어쓴다
                if (d == null || d.version <= 0) return null;
                if (d.levels == null) d.levels = new LevelRecord[0];
                return d;
            }
            catch (Exception e)
            {
                Debug.LogWarning("[CR] 저장을 못 읽었다 (" + path + "): " + e.Message);
                return null;
            }
        }

        public static void Save()
        {
            if (data == null) return;
            try
            {
                Directory.CreateDirectory(Dir);
                File.WriteAllText(PathTmp, JsonUtility.ToJson(data));
                if (File.Exists(Path0))
                {
                    if (File.Exists(PathBak)) File.Delete(PathBak);
                    File.Move(Path0, PathBak);
                }
                File.Move(PathTmp, Path0);
#if UNITY_WEBGL && !UNITY_EDITOR
                CRSyncFs();
#endif
            }
            catch (Exception e)
            {
                // **조용히 넘어가지 않는다.** 저장이 안 되는 것은 다음에 켤 때 알게 되고,
                // 그때는 무엇을 잃었는지 알 수 없다
                Debug.LogError("[CR] 저장 실패: " + e.Message);
            }
        }

        // ── 읽기 ────────────────────────────────────────────────────────────────
        public static LevelRecord Record(string code)
        {
            foreach (var r in Data.levels) if (r.code == code) return r;
            return null;
        }

        public static int GradeOf(string code) { var r = Record(code); return r == null ? 0 : r.grade; }
        public static int BestOf(string code) { var r = Record(code); return r == null ? 0 : r.best; }
        public static bool Cleared(string code) => GradeOf(code) > 0;

        /// <summary>그 도시에서 **연속으로** 깬 마지막 판 번호. 해금이 이 수를 본다</summary>
        public static int ClearedUpTo(int chapter)
        {
            int n = 0;
            for (int i = 1; i <= 10; i++)
            {
                if (!Cleared(chapter + "-" + i)) break;
                n = i;
            }
            return n;
        }

        public static bool Unlocked(int chapter, int index) => Grade.Unlocked(index, ClearedUpTo(chapter));

        /// <summary>그 도시 정복도 0~1 (월드맵의 "부산 72 %")</summary>
        public static float Conquest(int chapter)
        {
            var g = new int[10];
            for (int i = 1; i <= 10; i++) g[i - 1] = GradeOf(chapter + "-" + i);
            return Grade.Conquest(g, 10);
        }

        // ── 쓰기 ────────────────────────────────────────────────────────────────
        /// <summary>
        /// 판이 끝났다. **내려가지 않는다** — 다시 해서 더 못했어도 ☣ 와 최고 기록은 유지된다.
        /// 내려가면 플레이어가 **다시 하기를 두려워하고**, 그러면 ☣ 가 "다시 할 이유" 를 못 한다.
        /// </summary>
        public static void Report(string code, bool won, int remaining, int[] rating)
        {
            var d = Data;
            var r = Record(code);
            if (r == null)
            {
                r = new LevelRecord { code = code };
                var arr = new LevelRecord[d.levels.Length + 1];
                Array.Copy(d.levels, arr, d.levels.Length);
                arr[d.levels.Length] = r;
                d.levels = arr;
            }
            r.plays++;
            int g = Grade.Of(won, remaining, rating);
            if (g > r.grade) r.grade = g;
            if (won && remaining > r.best) r.best = remaining;
            Save();
        }

        /// <summary>QA·테스트용. 진짜 저장을 지운다</summary>
        public static void Wipe()
        {
            data = new MetaData();
            try { if (File.Exists(Path0)) File.Delete(Path0); if (File.Exists(PathBak)) File.Delete(PathBak); }
            catch (Exception e) { Debug.LogWarning("[CR] 저장 삭제 실패: " + e.Message); }
        }
    }
}
