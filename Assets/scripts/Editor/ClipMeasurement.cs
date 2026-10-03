using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace WarriorWoke.EditorTools
{
    /// <summary>
    /// Measures the player's animation clips on the real model (Ch45 through its Humanoid Avatar), so
    /// speeds, contact moments and heights come from the clips and not from guesses (T23):
    ///  - ground velocity of a locomotion clip (local right/forward, m/s): the hips travel when the
    ///    clip carries it in the pose, otherwise the planted foot sliding under the hips;
    ///  - per-sample heights of hips, hands and feet (contact moments for MatchTarget and IK).
    /// PlayerAnimationSetup places each locomotion clip in the directional blend at its measured
    /// velocity. The report writes Logs/ClipMeasurement.csv (one row per sample) and a console summary.
    /// Menu: Tools → Warrior Woke → Medir Clips. Batch: ClipMeasurement.RunBatch.
    /// </summary>
    internal static class ClipMeasurement
    {
        private const string PrefabPath = "Assets/Prefabs/Player.prefab";
        private const int Samples = 60;

        /// <summary>
        /// Samples of one clip in the frame of its root as the game plays it: AnimationMode samples the
        /// raw clip, so the samples are turned by the clip's root rotation settings — the original root
        /// plus its rotation offset ("Original"), or the body's orientation at the start ("Body
        /// Orientation"). x points to the root's right, z where it faces.
        /// </summary>
        private struct ClipSamples
        {
            public Vector3[] Hips, HandL, HandR, FootL, FootR;
        }

        [MenuItem("Tools/Warrior Woke/Medir Clips")]
        private static void RunMenu() => Run();

        public static void RunBatch()
        {
            Run();
            EditorApplication.Exit(0);
        }

        /// <summary>Measured ground velocity (local x = right, y = forward, m/s) of each clip.</summary>
        public static Dictionary<AnimationClip, Vector2> GroundVelocities(IEnumerable<AnimationClip> clips)
        {
            var result = new Dictionary<AnimationClip, Vector2>();
            WithModel((model, animator) =>
            {
                foreach (AnimationClip clip in clips)
                {
                    if (clip == null || result.ContainsKey(clip)) continue;
                    result[clip] = GroundVelocity(clip, Sample(model, animator, clip));
                }
            });
            return result;
        }

        /// <summary>Report of every humanoid clip the player draws from.</summary>
        public static void Run()
        {
            var csv = new StringBuilder("clip,length,t,hipsX,hipsY,hipsZ,handLX,handLY,handLZ,handRX,handRY,handRZ,footLX,footLY,footLZ,footRX,footRY,footRZ\n");
            var ci = CultureInfo.InvariantCulture;
            WithModel((model, animator) =>
            {
                foreach (AnimationClip clip in Clips())
                {
                    if (!clip.humanMotion) continue;
                    ClipSamples s = Sample(model, animator, clip);
                    float minHand = float.MaxValue, maxHand = float.MinValue;
                    for (int i = 0; i <= Samples; i++)
                    {
                        minHand = Mathf.Min(minHand, s.HandL[i].y, s.HandR[i].y);
                        maxHand = Mathf.Max(maxHand, s.HandL[i].y, s.HandR[i].y);
                        csv.Append(clip.name.Replace(',', ' ')).Append(',').Append(clip.length.ToString("F3", ci)).Append(',')
                           .Append((i / (float)Samples).ToString("F3", ci));
                        foreach (Vector3 v in new[] { s.Hips[i], s.HandL[i], s.HandR[i], s.FootL[i], s.FootR[i] })
                            csv.Append(',').Append(v.x.ToString("F3", ci)).Append(',').Append(v.y.ToString("F3", ci)).Append(',').Append(v.z.ToString("F3", ci));
                        csv.Append('\n');
                    }
                    Vector2 ground = GroundVelocity(clip, s);
                    Vector3 travel = s.Hips[Samples] - s.Hips[0];
                    Debug.Log(string.Format(ci,
                        "[ClipMeasurement] {0} | {1:F2} s | loop {2} | velocidad ({3:F2}, {4:F2}) = {5:F2} m/s | recorrido de la cadera ({6:F2}, {7:F2}, {8:F2}) | manos {9:F2}–{10:F2} m",
                        clip.name, clip.length, clip.isLooping, ground.x, ground.y, ground.magnitude, travel.x, travel.y, travel.z, minHand, maxHand));
                }
            });
            Directory.CreateDirectory("Logs");
            File.WriteAllText("Logs/ClipMeasurement.csv", csv.ToString());
            Debug.Log("[ClipMeasurement] Muestras en Logs/ClipMeasurement.csv");
        }

        // ─── Sampling ────────────────────────────────────────────────────────────────

        private static void WithModel(System.Action<GameObject, Animator> body)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            instance.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            AnimationMode.StartAnimationMode();
            try
            {
                GameObject model = instance.transform.Find("Model").gameObject;
                body(model, model.GetComponent<Animator>());
            }
            finally
            {
                AnimationMode.StopAnimationMode();
                Object.DestroyImmediate(instance);
            }
        }

        private static ClipSamples Sample(GameObject model, Animator animator, AnimationClip clip)
        {
            Transform root = model.transform;
            Transform hips  = animator.GetBoneTransform(HumanBodyBones.Hips);
            Transform handL = animator.GetBoneTransform(HumanBodyBones.LeftHand);
            Transform handR = animator.GetBoneTransform(HumanBodyBones.RightHand);
            Transform footL = animator.GetBoneTransform(HumanBodyBones.LeftFoot);
            Transform footR = animator.GetBoneTransform(HumanBodyBones.RightFoot);
            Transform legL  = animator.GetBoneTransform(HumanBodyBones.LeftUpperLeg);
            Transform legR  = animator.GetBoneTransform(HumanBodyBones.RightUpperLeg);
            Quaternion frame = Quaternion.identity;
            var s = new ClipSamples
            {
                Hips = new Vector3[Samples + 1], HandL = new Vector3[Samples + 1], HandR = new Vector3[Samples + 1],
                FootL = new Vector3[Samples + 1], FootR = new Vector3[Samples + 1],
            };
            for (int i = 0; i <= Samples; i++)
            {
                AnimationMode.BeginSampling();
                AnimationMode.SampleAnimationClip(model, clip, clip.length * i / Samples);
                AnimationMode.EndSampling();
                if (i == 0)
                {
                    AnimationClipSettings settings = AnimationUtility.GetAnimationClipSettings(clip);
                    if (settings.keepOriginalOrientation)
                    {
                        frame = Quaternion.Inverse(Quaternion.Euler(0f, settings.orientationOffsetY, 0f));
                    }
                    else
                    {
                        // Body orientation: right = from the left hip to the right hip, forward = right × up
                        Vector3 right = root.InverseTransformDirection(legR.position - legL.position);
                        right.y = 0f;
                        Vector3 forward = Vector3.Cross(right.normalized, Vector3.up);
                        frame = Quaternion.Inverse(Quaternion.LookRotation(forward, Vector3.up) * Quaternion.Euler(0f, settings.orientationOffsetY, 0f));
                    }
                }
                s.Hips[i]  = frame * root.InverseTransformPoint(hips.position);
                s.HandL[i] = frame * root.InverseTransformPoint(handL.position);
                s.HandR[i] = frame * root.InverseTransformPoint(handR.position);
                s.FootL[i] = frame * root.InverseTransformPoint(footL.position);
                s.FootR[i] = frame * root.InverseTransformPoint(footR.position);
            }
            return s;
        }

        /// <summary>
        /// Ground velocity (local x, z): the hips travel over the clip when the clip carries it in the
        /// pose (more than 0.3 m), otherwise the planted (lowest) foot sliding backward under the body —
        /// it moves at the speed the body would travel.
        /// </summary>
        private static Vector2 GroundVelocity(AnimationClip clip, ClipSamples s)
        {
            Vector3 travel = s.Hips[Samples] - s.Hips[0];
            travel.y = 0f;
            if (travel.magnitude > 0.3f)
                return new Vector2(travel.x, travel.z) / clip.length;

            float minFoot = float.MaxValue;
            var low = new Vector3[Samples + 1];
            for (int i = 0; i <= Samples; i++)
            {
                low[i] = s.FootL[i].y <= s.FootR[i].y ? s.FootL[i] : s.FootR[i];
                minFoot = Mathf.Min(minFoot, low[i].y);
            }
            Vector3 sum = Vector3.zero;
            int n = 0;
            float dt = clip.length / Samples;
            for (int i = 1; i <= Samples; i++)
            {
                if (low[i].y > minFoot + 0.03f || low[i - 1].y > minFoot + 0.03f) continue;
                Vector3 d = low[i] - low[i - 1];
                d.y = 0f;
                if (d.magnitude / dt > 15f) continue; // the planted foot switched
                sum += -d / dt;
                n++;
            }
            return n > 0 ? new Vector2(sum.x, sum.z) / n : Vector2.zero;
        }

        /// <summary>All clips in the project folders the player draws from.</summary>
        private static IEnumerable<AnimationClip> Clips()
        {
            string[] folders = { "Assets/ThirdParty", "Assets/LowPoly/Animations/Movement", "Assets/Characters/Player/Animations" };
            var seen = new HashSet<string>();
            foreach (string guid in AssetDatabase.FindAssets("t:AnimationClip", folders))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                foreach (Object o in AssetDatabase.LoadAllAssetsAtPath(path))
                {
                    if (o is AnimationClip clip && !clip.name.StartsWith("__preview__") && clip.name != "tpose" &&
                        seen.Add(path + "|" + clip.name))
                        yield return clip;
                }
            }
        }
    }
}
