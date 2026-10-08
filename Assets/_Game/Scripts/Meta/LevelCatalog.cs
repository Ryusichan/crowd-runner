using System.Collections.Generic;
using UnityEngine;
using CrowdRunner.Core;

namespace CrowdRunner.Meta
{
    /// <summary>
    /// **월드맵이 보는 판 목록.** `Resources/Levels` 의 글 파일을 전부 읽어 도시·번호 순으로 세운다.
    ///
    /// 목록을 코드에 박지 않는다 — 박으면 레벨을 하나 추가할 때 **두 곳**을 고쳐야 하고,
    /// 한 곳을 잊으면 *"만든 판이 월드맵에 안 보인다"* 가 된다. 폴더가 목록이다.
    ///
    /// ⚠ **못 읽은 판을 조용히 빼지 않는다.** 글 한 줄이 틀렸을 때 그 판이 목록에서 그냥
    /// 사라지면 원인을 찾을 수 없다 (`docs/DESIGN.md` §3e 가 적은 결함과 같은 모양 — 지표가
    /// 틀린 것을 말하지 않으면 틀린 것이 없는 것처럼 보인다). 그래서 `Broken` 에 남기고
    /// 로그로 소리친다.
    /// </summary>
    public static class LevelCatalog
    {
        static readonly List<LevelData> all = new List<LevelData>();
        static readonly List<string> broken = new List<string>();
        static bool loaded;

        /// <summary>도시·번호 순으로 정렬된 전체 판</summary>
        public static IReadOnlyList<LevelData> All { get { Ensure(); return all; } }

        /// <summary>못 읽은 판의 이유 (줄 번호 포함). 비어 있어야 정상</summary>
        public static IReadOnlyList<string> Broken { get { Ensure(); return broken; } }

        /// <summary>그 도시의 판들 (번호 순)</summary>
        public static List<LevelData> City(int chapter)
        {
            Ensure();
            var outp = new List<LevelData>();
            foreach (var l in all) if (l.chapter == chapter) outp.Add(l);
            return outp;
        }

        /// <summary>`"1-3"` 으로 찾는다. 없으면 null</summary>
        public static LevelData Find(string code)
        {
            Ensure();
            foreach (var l in all) if (l.Code == code) return l;
            return null;
        }

        /// <summary>도시가 몇 개인가 (월드맵의 탭 수)</summary>
        public static int CityCount
        {
            get
            {
                Ensure();
                int max = 0;
                foreach (var l in all) if (l.chapter > max) max = l.chapter;
                return max;
            }
        }

        /// <summary>레벨 파일을 고쳤을 때 (에디터·테스트) 다시 읽게 한다</summary>
        public static void Reload() { loaded = false; Ensure(); }

        static void Ensure()
        {
            if (loaded) return;
            loaded = true;
            all.Clear(); broken.Clear();

            // `LoadAll` 은 **이름 순**으로 주므로 "1-10" 이 "1-2" 앞에 온다. 그래서 읽고 나서
            // 번호로 다시 세운다 — 월드맵에 1-10 이 두 번째로 뜨면 그게 바로 보인다
            var assets = Resources.LoadAll<TextAsset>("Levels");
            if (assets == null || assets.Length == 0)
            {
                broken.Add("Resources/Levels 에 레벨이 하나도 없다");
                Debug.LogError("[CR] " + broken[0]);
                return;
            }

            foreach (var a in assets)
            {
                string why;
                var l = LevelFile.Parse(a.text, out why);
                if (l == null)
                {
                    broken.Add(a.name + ": " + why);
                    Debug.LogError("[CR] 레벨 " + a.name + " 을 못 읽는다 — " + why);
                    continue;
                }
                all.Add(l);
            }

            all.Sort((x, y) => x.chapter != y.chapter ? x.chapter - y.chapter : x.index - y.index);
        }
    }
}
