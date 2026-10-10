using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace WarriorWoke.EditorTools
{
    /// <summary>
    /// Visual and measured review of the combat clips on Ch45 (P37): for each clip of CombatTimings,
    /// a contact sheet (Logs/CombatClips/&lt;clip&gt;.png, seen from the side and from the front-left) and a
    /// timeline of the striking limbs in the frame of the clip's root: how far each hand and foot reaches
    /// forward from the hips and how high, the head's height and the hips' travel. The impact of a strike
    /// is where its limb reaches farthest forward; CombatTimings keeps those measured moments.
    /// Menu: Tools → Warrior Woke → Revisar Clips de Combate. Batch: CombatClipReview.RunBatch.
    /// </summary>
    internal static class CombatClipReview
    {
        private const string PrefabPath = "Assets/Prefabs/Player.prefab";
        private const string OutFolder  = "Logs/CombatClips";
        private const int Samples = 40, SheetColumns = 8;

        private static readonly (string File, string Clip)[] Clips =
        {
            ("Assets/ThirdParty/Quaternius/Animations/UAL1_Standard.fbx", CombatTimings.JabClip),
            ("Assets/ThirdParty/Quaternius/Animations/UAL1_Standard.fbx", CombatTimings.CrossClip),
            ("Assets/ThirdParty/CMU/Animations/CMU_14_01_Hook.fbx", CombatTimings.HookClip),
            ("Assets/ThirdParty/CMU/Animations/CMU_135_04_FrontKick.fbx", CombatTimings.KickClip),
            ("Assets/ThirdParty/Quaternius/Animations/UAL1_Standard.fbx", CombatTimings.HitChestClip),
            ("Assets/ThirdParty/Quaternius/Animations/UAL1_Standard.fbx", CombatTimings.HitHeadClip),
        };

        [MenuItem("Tools/Warrior Woke/Revisar Clips de Combate")]
        private static void RunMenu() => Run();

        public static void RunBatch()
        {
            bool ok = false;
            try { ok = Run(); }
            catch (System.Exception e) { Debug.LogException(e); }
            EditorApplication.Exit(ok ? 0 : 1);
        }

        private static bool Run()
        {
            Directory.CreateDirectory(OutFolder);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            var sheets = new PoseSheetRenderer(220, 300, 1.25f);
            var ci = CultureInfo.InvariantCulture;
            var csv = new StringBuilder("clip,length,n,handLFwd,handLY,handRFwd,handRY,footLFwd,footLY,footRFwd,footRY,headY,hipsFwd,hipsSide,handLSide,handRSide,rootFwd,rootSide,footLSide,footRSide\n");
            bool ok = true;
            try
            {
                foreach ((string file, string name) in Clips)
                {
                    AnimationClip clip = AssetDatabase.LoadAllAssetsAtPath(file).OfType<AnimationClip>().FirstOrDefault(c => c.name == name);
                    if (clip == null) { Debug.LogError($"[CombatClips] Falta el clip {name} en {file}."); ok = false; continue; }
                    // A fresh model per clip: after sampling a clip, the renders of the next one showed
                    // the previous clip's pose
                    GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                    instance.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                    AnimationMode.StartAnimationMode();
                    try
                    {
                        GameObject model = instance.transform.Find("Model").gameObject;
                        Review(instance.transform, model, model.GetComponent<Animator>(), clip, sheets, csv, ci);
                    }
                    finally
                    {
                        AnimationMode.StopAnimationMode();
                        Object.DestroyImmediate(instance);
                    }
                }
            }
            finally
            {
                sheets.Dispose();
            }
            File.WriteAllText(Path.Combine(OutFolder, "timeline.csv"), csv.ToString());
            Debug.Log($"[CombatClips] Hojas y línea de tiempo en {OutFolder}");
            return ok;
        }

        private static void Review(Transform player, GameObject model, Animator animator, AnimationClip clip, PoseSheetRenderer sheets, StringBuilder csv, CultureInfo ci)
        {
            Transform root = model.transform;
            Transform hips = animator.GetBoneTransform(HumanBodyBones.Hips), head = animator.GetBoneTransform(HumanBodyBones.Head);
            Transform handL = animator.GetBoneTransform(HumanBodyBones.LeftHand), handR = animator.GetBoneTransform(HumanBodyBones.RightHand);
            Transform footL = animator.GetBoneTransform(HumanBodyBones.LeftFoot), footR = animator.GetBoneTransform(HumanBodyBones.RightFoot);
            float floor = player.position.y; // the soles rest at the player's floor (sampling moves the model's root)

            // The root's frame at the start: where the clip faces (its root orientation is baked). The
            // root's own travel (root motion: the step the clip takes) is measured in that frame too
            Sample(model, clip, 0f);
            Vector3 hips0 = root.InverseTransformPoint(hips.position);
            Vector3 root0 = root.position, forward0 = root.forward, right0 = root.right;

            float best = float.MinValue; string bestLimb = ""; float bestN = 0f;
            float minHead = float.MaxValue, maxHead = float.MinValue;
            for (int i = 0; i <= Samples; i++)
            {
                float n = i / (float)Samples;
                Sample(model, clip, clip.length * n);
                Vector3 h = root.InverseTransformPoint(hips.position);
                float Fwd(Transform t) => root.InverseTransformPoint(t.position).z - h.z;
                float Y(Transform t) => t.position.y - floor;
                float[] fwd = { Fwd(handL), Fwd(handR), Fwd(footL), Fwd(footR) };
                string[] names = { "mano izq", "mano der", "pie izq", "pie der" };
                for (int k = 0; k < 4; k++)
                    if (fwd[k] > best) { best = fwd[k]; bestLimb = names[k]; bestN = n; }
                minHead = Mathf.Min(minHead, Y(head));
                maxHead = Mathf.Max(maxHead, Y(head));
                csv.Append(clip.name).Append(',').Append(clip.length.ToString("F3", ci)).Append(',').Append(n.ToString("F3", ci));
                float Side(Transform t) => root.InverseTransformPoint(t.position).x - h.x;
                foreach (float v in new[] { Fwd(handL), Y(handL), Fwd(handR), Y(handR), Fwd(footL), Y(footL), Fwd(footR), Y(footR), Y(head), h.z - hips0.z, h.x - hips0.x,
                                            Side(handL), Side(handR), Vector3.Dot(root.position - root0, forward0), Vector3.Dot(root.position - root0, right0),
                                            Side(footL), Side(footR) })
                    csv.Append(',').Append(v.ToString("F3", ci));
                csv.Append('\n');
            }
            Sample(model, clip, clip.length);
            Vector3 travel = root.InverseTransformPoint(hips.position) - hips0;
            Debug.Log(string.Format(ci, "[CombatClips] {0}: {1:F2} s, alcance máximo {2:F2} m ({3}) en n = {4:F2} ({5:F2} s), cabeza {6:F2}–{7:F2} m, cadera recorre ({8:F2}, {9:F2}) m",
                clip.name, clip.length, best, bestLimb, bestN, bestN * clip.length, minHead, maxHead, travel.x, travel.z));

            sheets.Begin(SheetColumns, 2);
            for (int c = 0; c < SheetColumns; c++)
            {
                Sample(model, clip, clip.length * c / (SheetColumns - 1));
                Vector3 focus = new Vector3(hips.position.x, floor + 0.95f, hips.position.z);
                sheets.Capture(0, c, focus, root.right, floor);
                sheets.Capture(1, c, focus, (root.right - root.forward).normalized, floor);
            }
            sheets.Save(Path.Combine(OutFolder, clip.name + ".png"));
        }

        private static void Sample(GameObject model, AnimationClip clip, float time)
        {
            AnimationMode.BeginSampling();
            AnimationMode.SampleAnimationClip(model, clip, time);
            AnimationMode.EndSampling();
        }
    }
}
