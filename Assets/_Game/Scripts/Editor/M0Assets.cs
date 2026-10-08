using System.IO;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace Game.EditorTools
{
    /// <summary>
    /// **대역 캐릭터를 굽는다** — M0 가 재야 하는 비용을 가진, 아직 없는 아트의 대역.
    ///
    /// `docs/M0_CROWD.md` §2 가 *"캡슐로는 재지 않는다"* 로 닫혔는데, 이 저장소에는 **캐릭터 자산이
    /// 아직 하나도 없다.** 아트를 기다리면 M0 가 아트 뒤로 밀리고, 그러면 M0 가 존재하는 이유
    /// (아트 파이프라인을 정하는 것)가 뒤집힌다.
    ///
    /// 그래서 **비용이 대표성 있는 메시를 코드로 만든다.** 스킨드 메시의 비용을 정하는 것은 모양이
    /// 아니라 **정점 수 · 본 수 · 렌더러 수**이고, 셋 다 여기서 숫자로 준다. 모양은 사람 비슷하기만
    /// 하면 된다 — 그래서 모양에 공을 들이지 않았다.
    ///
    /// ⚠ **이것이 캡슐과 다른 점이 그 셋이다.** 캡슐은 본 0 · 정점 ~100 · 스킨닝 없음이라 비용의
    /// **종류**가 달랐다. 여기는 본 `Bones`(20) · 정점 `~Verts`(1,500) · `SkinnedMeshRenderer` 라
    /// 같은 종류의 비용을 낸다. 그리고 **그 두 수가 출하 아트의 상한이 된다** — 측정이 1,500/20 에서
    /// 통과했으면 실제 캐릭터도 그 안에 들어와야 한다. 넘으면 M0 결과가 무효다.
    ///
    /// 배치 모드로 돈다 (오너 규칙: 에디터 창을 띄우지 않는다):
    /// <code>Unity -batchmode -nographics -quit -projectPath . -executeMethod Game.EditorTools.M0Assets.Bake</code>
    /// </summary>
    public static class M0Assets
    {
        /// <summary>모바일 군중 캐릭터의 현실적인 값 — 이 둘이 출하 아트의 상한이다</summary>
        public const int Bones = 20;
        public const int Verts = 1500;

        const string Dir = "Assets/_Game/Resources/M0";

        [MenuItem("M0/Bake Stand-in Character")]
        public static void Bake()
        {
            Directory.CreateDirectory(Dir);

            var mesh = BuildSkinnedMesh(out var boneNames, out var binds);
            AssetDatabase.CreateAsset(mesh, Dir + "/standin.asset");

            var clip = BuildRunClip(boneNames);
            AssetDatabase.CreateAsset(clip, Dir + "/standin_run.anim");

            var ctrl = AnimatorController.CreateAnimatorControllerAtPathWithClip(Dir + "/standin.controller", clip);

            var prefab = BuildPrefab(mesh, binds, boneNames, ctrl);
            PrefabUtility.SaveAsPrefabAsset(prefab, Dir + "/StandIn.prefab");
            Object.DestroyImmediate(prefab);

            AssetDatabase.SaveAssets();
            Debug.Log($"[M0] baked stand-in: verts={mesh.vertexCount} bones={boneNames.Length} " +
                      $"tris={mesh.triangles.Length / 3} clip={clip.length:F2}s");
        }

        /// <summary>
        /// 본 하나를 한 층으로 보는 **원통 사슬**. 층마다 고리 하나, 고리의 정점은 **그 층 본에
        /// 100 % 로 묶는다** — 스킨닝 비용은 가중치의 **수**가 정하므로(정점당 1 개) 가중치 1 개가
        /// 가장 싼 쪽이다. 즉 이 측정은 **실제보다 유리하다**: 출하 아트가 정점당 2~4 가중치를 쓰면
        /// 더 비싸진다. 그래서 §4 의 통과선에 여유(절반 예산)를 둔 것이고, 그 여유의 일부가 이것이다.
        /// </summary>
        static Mesh BuildSkinnedMesh(out string[] boneNames, out Matrix4x4[] binds)
        {
            int ring = Mathf.Max(3, Verts / Bones);          // 층마다 정점 수
            int rows = Bones;
            var verts = new Vector3[ring * rows];
            var norms = new Vector3[verts.Length];
            var weights = new BoneWeight[verts.Length];
            boneNames = new string[rows];
            binds = new Matrix4x4[rows];

            float height = 1.8f;
            for (int r = 0; r < rows; r++)
            {
                float t = r / (float)(rows - 1);
                float y = t * height;
                // 허리에서 가장 굵고 위아래로 가늘어진다 — 사람 실루엣의 최소치
                float rad = Mathf.Lerp(0.16f, 0.26f, Mathf.Sin(t * Mathf.PI));
                boneNames[r] = "b" + r;
                binds[r] = Matrix4x4.TRS(new Vector3(0f, -y, 0f), Quaternion.identity, Vector3.one);
                for (int c = 0; c < ring; c++)
                {
                    float a = c / (float)ring * Mathf.PI * 2f;
                    int k = r * ring + c;
                    verts[k] = new Vector3(Mathf.Cos(a) * rad, y, Mathf.Sin(a) * rad);
                    norms[k] = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                    weights[k] = new BoneWeight { boneIndex0 = r, weight0 = 1f };
                }
            }

            var tris = new System.Collections.Generic.List<int>((rows - 1) * ring * 6);
            for (int r = 0; r < rows - 1; r++)
                for (int c = 0; c < ring; c++)
                {
                    int c2 = (c + 1) % ring;
                    int a = r * ring + c, b = r * ring + c2, d = (r + 1) * ring + c, e = (r + 1) * ring + c2;
                    tris.Add(a); tris.Add(d); tris.Add(b);
                    tris.Add(b); tris.Add(d); tris.Add(e);
                }

            var m = new Mesh { name = "standin" };
            m.indexFormat = UnityEngine.Rendering.IndexFormat.UInt16;
            m.vertices = verts; m.normals = norms; m.boneWeights = weights;
            m.triangles = tris.ToArray();
            m.bindposes = binds;
            m.RecalculateBounds();
            return m;
        }

        /// <summary>
        /// 달리는 클립 — 본마다 **같은 주기, 다른 위상**으로 흔든다. 모양이 달리기처럼 보이는 것은
        /// 중요하지 않다. 중요한 것은 **본 20 개 전부에 커브가 있는 것**이다: 커브가 없는 본은
        /// 평가되지 않아서, 커브를 몇 개만 두면 측정이 **더 싼 클립**을 재게 된다.
        /// </summary>
        static AnimationClip BuildRunClip(string[] boneNames)
        {
            var clip = new AnimationClip { name = "standin_run", frameRate = 30f };
            clip.legacy = false;
            for (int i = 0; i < boneNames.Length; i++)
            {
                string path = Path(boneNames, i);
                float ph = i / (float)boneNames.Length * Mathf.PI * 2f;
                var cx = new AnimationCurve();
                var cz = new AnimationCurve();
                for (int f = 0; f <= 16; f++)
                {
                    float t = f / 16f;
                    float a = t * Mathf.PI * 2f + ph;
                    cx.AddKey(t * 0.8f, Mathf.Sin(a) * 0.06f);
                    cz.AddKey(t * 0.8f, Mathf.Cos(a) * 0.04f);
                }
                clip.SetCurve(path, typeof(Transform), "localEulerAngles.x", cx);
                clip.SetCurve(path, typeof(Transform), "localEulerAngles.z", cz);
            }
            var s = new AnimationClipSettings { loopTime = true };
            AnimationUtility.SetAnimationClipSettings(clip, s);
            return clip;
        }

        /// <summary>본은 사슬이므로 경로가 `b0/b1/b2/...` 로 깊어진다 — 깊이도 평가 비용이다</summary>
        static string Path(string[] names, int i)
        {
            var sb = new System.Text.StringBuilder();
            for (int k = 0; k <= i; k++) { if (k > 0) sb.Append('/'); sb.Append(names[k]); }
            return sb.ToString();
        }

        static GameObject BuildPrefab(Mesh mesh, Matrix4x4[] binds, string[] boneNames, AnimatorController ctrl)
        {
            var root = new GameObject("StandIn");
            var bones = new Transform[boneNames.Length];
            Transform parent = root.transform;
            float step = 1.8f / (boneNames.Length - 1);
            for (int i = 0; i < boneNames.Length; i++)
            {
                var go = new GameObject(boneNames[i]);
                go.transform.SetParent(parent, false);
                go.transform.localPosition = i == 0 ? Vector3.zero : new Vector3(0f, step, 0f);
                bones[i] = go.transform;
                parent = go.transform;
            }
            var smr = root.AddComponent<SkinnedMeshRenderer>();
            smr.sharedMesh = mesh;
            smr.bones = bones;
            smr.rootBone = bones[0];
            smr.sharedMaterial = Material();
            // **품질을 1 가중치로 못 박는다** — 프로젝트 설정이 4 로 되어 있으면 측정이 설정에 끌려간다
            smr.quality = SkinQuality.Bone1;
            smr.updateWhenOffscreen = false;
            var anim = root.AddComponent<Animator>();
            anim.runtimeAnimatorController = ctrl;
            // **컬링을 끈다**: 화면 밖에서 안 돌면 1,000 중 몇이 실제로 평가됐는지 알 수 없고,
            // 그러면 측정이 카메라 각도에 달린다
            anim.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            return root;
        }

        static Material Material()
        {
            var sh = Shader.Find("Universal Render Pipeline/Simple Lit") ?? Shader.Find("Sprites/Default");
            var m = new Material(sh) { name = "standin" };
            AssetDatabase.CreateAsset(m, Dir + "/standin.mat");
            return m;
        }
    }
}
