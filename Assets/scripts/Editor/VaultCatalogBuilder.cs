using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace WarriorWoke.EditorTools
{
    /// <summary>
    /// Builds the vault clips of the player (decision P36, docs/arquitectura.md §5.17) from the annotated
    /// vaults of the Kinematica Demo mocap (Unity Companion License; annotations of the demo's Unit.asset:
    /// ParkourTag "Ledge" = a thin fence of 0.94 × 0.08 m, "Table" = a table of 0.87 × 1.10 m, the hand
    /// contacts, the landing foot and the escape frame):
    ///  1. Measures every annotated vault on Ch45 (root motion at 60 Hz): approach and exit speed,
    ///     take-off, the hand plant (where the hand is stillest), release, landing, the flight's arc and
    ///     gravity, the stride (lead foot) along the run-up, and the clip's own hand goal (the actor's
    ///     wrist scaled to Ch45: what the hand IK reaches).
    ///  2. Warp lab: for every obstacle height × depth, the vertical lift, horizontal stretch and hand
    ///     inset that put the hand on the top with the skinned body (baked mesh) clear of it, within
    ///     bounds (lift ≤ +0.2 / ≥ −0.4 m, stretch ≤ 0.6 m, shoulder within reach of the plant point); a
    ///     cell without a solution is an obstacle the clip must not be used for.
    ///  3. Imports the selected vaults as sub-clips of their takes (and their mirrors), with root motion
    ///     in XZ, the height baked into the pose and the root's heading free (the warper replaces it),
    ///     and writes Assets/Data/Parkour/VaultCatalog.asset, which PlayerAnimationSetup turns into
    ///     Animator states and the player's VaultPlanner reads.
    /// Reports: Logs/VaultProbe/segments.csv, envelope.csv and storyboards (*.png: the vault over its
    /// mocap obstacle, and warped over the standard ones).
    /// Menu: Tools → Warrior Woke → Construir Catálogo de Vaults. Batch: VaultCatalogBuilder.RunBatch.
    /// </summary>
    internal static class VaultCatalogBuilder
    {
        private const string Ch45Path    = "Assets/Characters/Player/character.fbx";
        private const string UnitPath    = "Assets/ThirdParty/Kinematica/Character/Unit.FBX";
        private const string KinFolder   = "Assets/ThirdParty/Kinematica/Animations/";
        internal const string CatalogPath = "Assets/Data/Parkour/VaultCatalog.asset";
        private const string OutRoot     = "Logs/VaultProbe";
        private const float  Rate        = 60f;
        private const float  PathRate    = 30f;

        internal enum VaultKind { Fence, Table }

        /// <summary>One annotated vault of a take (times in seconds of the take).</summary>
        internal sealed class Segment
        {
            public string Take, Id;
            public VaultKind Kind;
            public float Start, End, Escape;
            /// <summary>Contacts in order: Hand_L / Hand_R (palm on the top), FootBridge_L / _R (landing).</summary>
            public (string Joint, float Time)[] Contacts;
        }

        /// <summary>Size of the obstacle each annotation type was captured on (demo prefabs z_VaultOverFence / z_VaultOverTable).</summary>
        private static (float Height, float Depth) Native(VaultKind kind) => kind == VaultKind.Fence ? (0.94f, 0.08f) : (0.87f, 1.10f);

        private static Segment S(string take, string id, VaultKind kind, float start, float end, float escape, params (string, float)[] contacts) =>
            new Segment { Take = take, Id = id, Kind = kind, Start = start, End = end, Escape = escape, Contacts = contacts };

        /// <summary>
        /// The annotated vaults (Kinematica Demo, Assets/Kinematica/Unit.asset) of the takes in the project.
        /// The lab also measured Vaults_Sliding_Sprint_1/2 and Vaults_Sliding_Stand_Walk_2 (2026-10-08): no
        /// gait needed them (the selected vaults cover every speed band), so the takes were removed (P8).
        /// </summary>
        internal static readonly Segment[] Segments =
        {
            S("Vaults_Over_Ledge_Walk",      "LedgeWalkA",     VaultKind.Fence, 0.000f,  4.800f,  3.100f, ("Hand_L", 2.000f), ("FootBridge_R", 2.333f), ("FootBridge_L", 2.833f)),
            S("Vaults_Over_Ledge_Walk",      "LedgeWalkB",     VaultKind.Fence, 9.233f, 12.400f, 11.010f, ("Hand_L", 10.133f), ("FootBridge_L", 10.833f)),
            S("Vaults_Over_Ledge_Jog",       "LedgeJogA",      VaultKind.Fence, 0.000f,  3.300f,  2.220f, ("Hand_L", 1.500f), ("FootBridge_R", 2.160f)),
            S("Vaults_Over_Ledge_Jog",       "LedgeJogB",      VaultKind.Fence, 9.130f, 11.830f, 10.590f, ("Hand_L", 10.130f), ("FootBridge_L", 10.530f)),
            S("Vaults_Over_Ledge_Sprint",    "LedgeSprintA",   VaultKind.Fence, 0.000f,  2.500f,  1.380f, ("Hand_L", 0.967f), ("FootBridge_L", 1.267f)),
            S("Vaults_Over_Ledge_Sprint",    "LedgeSprintB",   VaultKind.Fence, 8.333f, 11.067f,  9.610f, ("Hand_L", 9.200f), ("FootBridge_L", 9.567f)),
            S("Vaults_Over_Table_1",         "TableA",         VaultKind.Table, 30.267f, 33.133f, 31.900f, ("Hand_L", 31.000f), ("FootBridge_R", 31.400f), ("FootBridge_L", 31.867f)),
            S("Vaults_Over_Table_1",         "TableB",         VaultKind.Table, 48.667f, 51.167f, 50.000f, ("Hand_L", 49.467f), ("FootBridge_R", 50.000f)),
            S("Vaults_Over_Table_2",         "Table2A",        VaultKind.Table, 11.200f, 14.433f, 12.967f, ("Hand_L", 12.100f), ("Hand_R", 12.533f), ("FootBridge_R", 12.733f)),
            S("Vaults_Over_Table_2",         "Table2B",        VaultKind.Table, 22.833f, 25.100f, 24.200f, ("Hand_L", 23.333f), ("Hand_R", 23.667f), ("FootBridge_R", 24.133f)),
            S("Vaults_Sliding_Jog",          "SlidingJogA",    VaultKind.Table, 0.733f,  3.533f,  2.400f, ("Hand_L", 1.633f), ("Hand_R", 1.933f), ("FootBridge_L", 2.300f)),
            S("Vaults_Sliding_Jog",          "SlidingJogB",    VaultKind.Table, 23.933f, 27.133f, 25.933f, ("Hand_L", 24.800f), ("Hand_L", 25.600f), ("FootBridge_L", 25.900f)),
            S("Vaults_Sliding_Stand_Walk_1", "SlidingStand1",  VaultKind.Table, 0.000f,  4.000f,  2.933f, ("Hand_L", 1.867f), ("Hand_L", 2.400f), ("FootBridge_L", 2.833f)),
        };

        /// <summary>
        /// The vaults the game plays, chosen with the envelope (one speed vault and one lazy or dive vault
        /// per gait, docs/arquitectura.md §5.17), plus the dive of Table2B: it takes off 0.66 m from the
        /// hand (the others 1.4–1.8 m running), so a late Space while running still fits a vault. Each
        /// is also imported mirrored (the other hand).
        /// </summary>
        internal static readonly string[] Selected = { "LedgeWalkB", "SlidingStand1", "LedgeJogA", "SlidingJogA", "LedgeSprintB", "TableB", "Table2B" };

        /// <summary>Animator state and sub-clip name of a vault (and of its mirror).</summary>
        internal static string StateName(string id, bool mirror) => "Vault_" + id + (mirror ? "_M" : "");

        [MenuItem("Tools/Warrior Woke/Construir Catálogo de Vaults")]
        private static void RunMenu() => Build();

        public static void RunBatch()
        {
            int code = 1;
            try { code = Build() ? 0 : 1; }
            catch (System.Exception e) { Debug.LogException(e); }
            EditorApplication.Exit(code);
        }

        // ─── Sampling ───────────────────────────────────────────────────────────────

        private enum Bone { Hips, Head, HandL, HandR, FootL, FootR, ToeL, ToeR, KneeL, KneeR, LegL, LegR, ArmL, ArmR, Count }

        private static readonly HumanBodyBones[] BoneMap =
        {
            HumanBodyBones.Hips, HumanBodyBones.Head, HumanBodyBones.LeftHand, HumanBodyBones.RightHand,
            HumanBodyBones.LeftFoot, HumanBodyBones.RightFoot, HumanBodyBones.LeftToes, HumanBodyBones.RightToes,
            HumanBodyBones.LeftLowerLeg, HumanBodyBones.RightLowerLeg, HumanBodyBones.LeftUpperLeg, HumanBodyBones.RightUpperLeg,
            HumanBodyBones.LeftUpperArm, HumanBodyBones.RightUpperArm,
        };

        /// <summary>Bone positions of a clip sampled at Rate (world, model at the origin; root travel in the pose).</summary>
        private sealed class Track
        {
            public float Start, Dt;
            public Vector3[,] P; // [sample, bone]
            /// <summary>The root transform (the model's GameObject, moved by the root motion) per sample: position and yaw (°).</summary>
            public Vector3[] RootPos;
            public float[] RootYaw;
            public int Count => P.GetLength(0);
            public float Time(int i) => Start + i * Dt;
            public int Index(float t) => Mathf.Clamp(Mathf.RoundToInt((t - Start) / Dt), 0, Count - 1);
            public Vector3 At(int i, Bone b) => P[i, (int)b];
        }

        private sealed class Rig
        {
            public GameObject Root;
            public Animator Animator;
            public Transform[] Bones;
            public float SoleL, SoleR;
            public SkinnedMeshRenderer[] Skins;
            /// <summary>Per skin and vertex: 0 = foot (the foot IK lifts it), 1 = pelvis and thighs (may rest on a lazy vault's top), 2 = rest of the body, 3 = arm (ignored: the hands touch the top on purpose).</summary>
            public byte[][] VertexClass;
            public Transform ArmL, ElbowL, WristL;
        }

        private static Rig CreateRig(string path)
        {
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            GameObject go = Object.Instantiate(asset, Vector3.zero, Quaternion.identity);
            Animator animator = go.GetComponent<Animator>() ?? go.AddComponent<Animator>();
            if (animator.avatar == null) animator.avatar = AssetDatabase.LoadAllAssetsAtPath(path).OfType<Avatar>().FirstOrDefault();
            animator.applyRootMotion = true;
            animator.runtimeAnimatorController = null;
            var rig = new Rig { Root = go, Animator = animator, Bones = new Transform[(int)Bone.Count] };
            for (int b = 0; b < (int)Bone.Count; b++) rig.Bones[b] = animator.GetBoneTransform(BoneMap[b]);
            rig.SoleL = animator.leftFeetBottomHeight;
            rig.SoleR = animator.rightFeetBottomHeight;
            rig.ArmL = animator.GetBoneTransform(HumanBodyBones.LeftUpperArm);
            rig.ElbowL = animator.GetBoneTransform(HumanBodyBones.LeftLowerArm);
            rig.WristL = animator.GetBoneTransform(HumanBodyBones.LeftHand);
            ClassifyVertices(rig);
            foreach (Renderer r in go.GetComponentsInChildren<Renderer>()) r.enabled = false;
            return rig;
        }

        private static bool IsUnder(Transform t, Transform ancestor)
        {
            for (; t != null; t = t.parent) if (t == ancestor) return true;
            return false;
        }

        private static void ClassifyVertices(Rig rig)
        {
            Animator a = rig.Animator;
            Transform armL = a.GetBoneTransform(HumanBodyBones.LeftUpperArm), armR = a.GetBoneTransform(HumanBodyBones.RightUpperArm);
            Transform footL = a.GetBoneTransform(HumanBodyBones.LeftFoot), footR = a.GetBoneTransform(HumanBodyBones.RightFoot);
            Transform hips = a.GetBoneTransform(HumanBodyBones.Hips);
            Transform legL = a.GetBoneTransform(HumanBodyBones.LeftUpperLeg), legR = a.GetBoneTransform(HumanBodyBones.RightUpperLeg);
            rig.Skins = rig.Root.GetComponentsInChildren<SkinnedMeshRenderer>();
            rig.VertexClass = new byte[rig.Skins.Length][];
            for (int si = 0; si < rig.Skins.Length; si++)
            {
                SkinnedMeshRenderer smr = rig.Skins[si];
                BoneWeight[] weights = smr.sharedMesh.boneWeights;
                var classes = new byte[weights.Length];
                for (int v = 0; v < weights.Length; v++)
                {
                    Transform bone = smr.bones[weights[v].boneIndex0];
                    classes[v] = IsUnder(bone, armL) || IsUnder(bone, armR) ? (byte)3
                               : IsUnder(bone, footL) || IsUnder(bone, footR) ? (byte)0
                               : bone == hips || bone == legL || bone == legR ? (byte)1
                               : (byte)2;
                }
                rig.VertexClass[si] = classes;
            }
        }

        private static void Pose(Rig rig, AnimationClip clip, float time, Vector3 offset)
        {
            rig.Root.transform.SetPositionAndRotation(offset, Quaternion.identity);
            AnimationMode.BeginSampling();
            AnimationMode.SampleAnimationClip(rig.Root, clip, time);
            AnimationMode.EndSampling();
        }

        private static Track Sample(Rig rig, AnimationClip clip, float from, float to)
        {
            int n = Mathf.Max(2, Mathf.RoundToInt((to - from) * Rate)) + 1;
            var track = new Track { Start = from, Dt = (to - from) / (n - 1), P = new Vector3[n, (int)Bone.Count], RootPos = new Vector3[n], RootYaw = new float[n] };
            for (int i = 0; i < n; i++)
            {
                Pose(rig, clip, track.Time(i), Vector3.zero);
                for (int b = 0; b < (int)Bone.Count; b++) track.P[i, b] = rig.Bones[b].position;
                track.RootPos[i] = rig.Root.transform.position;
                track.RootYaw[i] = rig.Root.transform.eulerAngles.y;
            }
            return track;
        }

        // ─── Measurement ────────────────────────────────────────────────────────────

        /// <summary>One vault measured on Ch45. Times in seconds from the segment's start.</summary>
        private sealed class Measure
        {
            public Segment Seg;
            public AnimationClip Clip;
            public float ClipOffset;             // where the segment starts in Clip (0 for a sub-clip)
            public Track T;
            public VaultVariant V;                // key times, path, offsets, speeds (not mirrored)
            public Vector3 F, Right, Hand;        // vault frame and the planted wrist (world, FK)
            public float HandGoalY, ArmLength;
            public int Takeoff, Plant, PlantRelease, SecondPlant, SecondRelease, Release, Land, Escape;
            public Bone PlantBone, SecondBone, TakeoffFoot, LandFoot;
            public float Gravity, PlantSpeed;
            public float[,,] Mesh;
            public LabCell[,] Lab;
        }

        private static Measure MeasureSegment(Rig rig, Rig actor, Segment s, AnimationClip clip, float clipOffset)
        {
            float length = s.End - s.Start;
            Track t = Sample(rig, clip, clipOffset, clipOffset + length);
            var m = new Measure { Seg = s, Clip = clip, ClipOffset = clipOffset, T = t };
            float Local(float takeTime) => takeTime - s.Start + clipOffset;

            var hands = s.Contacts.Where(c => c.Joint.StartsWith("Hand")).ToArray();
            var feet  = s.Contacts.Where(c => c.Joint.StartsWith("Foot")).ToArray();
            m.PlantBone = hands[0].Joint.EndsWith("_R") ? Bone.HandR : Bone.HandL;
            m.Land = t.Index(Local(feet[0].Time));
            m.LandFoot = feet[0].Joint.EndsWith("_R") ? Bone.FootR : Bone.FootL;
            m.Escape = t.Index(Local(s.Escape));

            // The annotation marks the plant; the hand is planted where it is stillest around it (±0.25 s)
            m.Plant = Stillest(t, m.PlantBone, Local(hands[0].Time), out m.PlantSpeed);
            float plantT = t.Time(m.Plant);

            Vector3 before = t.At(t.Index(plantT - 0.4f), Bone.Hips), after = t.At(m.Land, Bone.Hips);
            m.F = after - before; m.F.y = 0f; m.F.Normalize();
            m.Right = Vector3.Cross(Vector3.up, m.F);
            m.Hand = t.At(m.Plant, m.PlantBone);

            // Take-off: the last moment before the plant with a foot on the ground
            m.Takeoff = 0;
            for (int i = 0; i <= m.Plant; i++)
                if (SoleY(t, i, rig, true) < 0.04f || SoleY(t, i, rig, false) < 0.04f) m.Takeoff = i;
            m.TakeoffFoot = SoleY(t, m.Takeoff, rig, true) < SoleY(t, m.Takeoff, rig, false) ? Bone.FootL : Bone.FootR;
            // Each hand leaves the top when it rises 5 cm above where it was planted (a hand planted again
            // further along leaves just before); the support ends when the last one leaves
            m.PlantRelease = Rise(t, m.PlantBone, m.Plant, m.Land);
            m.Release = m.PlantRelease;
            m.SecondPlant = -1;
            if (hands.Length > 1)
            {
                m.SecondBone = hands[1].Joint.EndsWith("_R") ? Bone.HandR : Bone.HandL;
                m.SecondPlant = Stillest(t, m.SecondBone, Local(hands[1].Time), out _);
                m.SecondRelease = Rise(t, m.SecondBone, m.SecondPlant, m.Land);
                if (m.SecondBone == m.PlantBone) m.PlantRelease = Mathf.Min(m.PlantRelease, m.SecondPlant - 2);
                m.Release = Mathf.Max(m.PlantRelease, m.SecondRelease);
            }

            // The clip's own hand goal (what the hand IK reaches): the actor's wrist, scaled to Ch45
            Pose(actor, clip, plantT, Vector3.zero);
            float scale = rig.Animator.humanScale / Mathf.Max(0.01f, actor.Animator.humanScale);
            m.HandGoalY = actor.Bones[(int)m.PlantBone].position.y * scale;
            m.ArmLength = Vector3.Distance(rig.ArmL.position, rig.ElbowL.position) + Vector3.Distance(rig.ElbowL.position, rig.WristL.position);

            m.Gravity = FlightGravity(t, m.Release + 2, m.Land - 2);
            m.V = BuildVariant(m, rig, actor, scale);
            return m;
        }

        /// <summary>First sample after <paramref name="from"/> where the hand is 5 cm above where it was then (at most <paramref name="until"/>).</summary>
        private static int Rise(Track t, Bone hand, int from, int until)
        {
            float y = t.At(from, hand).y;
            int i = from;
            while (i < until && t.At(i, hand).y < y + 0.05f) i++;
            return i;
        }

        private static int Stillest(Track t, Bone hand, float around, out float speed)
        {
            int best = t.Index(around);
            speed = float.MaxValue;
            for (int i = t.Index(around - 0.25f); i <= t.Index(around + 0.25f); i++)
            {
                int a = Mathf.Max(0, i - 1), b = Mathf.Min(t.Count - 1, i + 1);
                float v = b > a ? (t.At(b, hand) - t.At(a, hand)).magnitude / ((b - a) * t.Dt) : float.MaxValue;
                if (v < speed) { speed = v; best = i; }
            }
            return best;
        }

        private static VaultVariant BuildVariant(Measure m, Rig rig, Rig actor, float scale)
        {
            Track t = m.T;
            float t0 = t.Time(0);
            // The root transform as the root motion moves it: the warper moves the body along this path and
            // the pose is drawn relative to it, so the game reproduces the sampled motion exactly
            Vector3 Root(int i) { Vector3 r = t.RootPos[i]; return new Vector3(r.x, 0f, r.z); }
            Vector3 origin = Root(0);
            float frameYaw = Vector3.SignedAngle(Vector3.forward, m.F, Vector3.up);
            var v = new VaultVariant
            {
                Id = m.Seg.Id,
                State = StateName(m.Seg.Id, false),
                Style = m.Seg.Id.StartsWith("Sliding") ? VaultStyle.Lazy : m.Seg.Kind == VaultKind.Table ? VaultStyle.Dive : VaultStyle.Speed,
                Length = t.Time(t.Count - 1) - t0,
                Takeoff = t.Time(m.Takeoff) - t0,
                Plant = t.Time(m.Plant) - t0,
                PlantRelease = t.Time(m.PlantRelease) - t0,
                Release = t.Time(m.Release) - t0,
                Land = t.Time(m.Land) - t0,
                Escape = t.Time(m.Escape) - t0,
                RightHand = m.PlantBone == Bone.HandR,
                PathRate = PathRate,
                FrameYaw = frameYaw,
            };

            int samples = Mathf.FloorToInt(v.Length * PathRate) + 1;
            v.PathAlong = new float[samples];
            v.PathLateral = new float[samples];
            v.Heading = new float[samples];
            v.FootPhase = new float[samples];
            Bone ShoulderOf(Bone hand) => hand == Bone.HandL ? Bone.ArmL : Bone.ArmR;
            v.PlantShoulder = new Vector3[samples];
            v.SecondShoulder = m.SecondPlant >= 0 ? new Vector3[samples] : new Vector3[0];
            v.ArmLength = m.ArmLength;
            Vector3 Relative(int i, Bone bone)
            {
                Vector3 p = t.At(i, bone), d = p - Root(i);
                return new Vector3(Vector3.Dot(d, m.Right), p.y, Vector3.Dot(d, m.F));
            }
            for (int k = 0; k < samples; k++)
            {
                int i = t.Index(t0 + k / PathRate);
                Vector3 d = Root(i) - origin;
                v.PathAlong[k] = Vector3.Dot(d, m.F);
                v.PathLateral[k] = Vector3.Dot(d, m.Right);
                v.Heading[k] = Mathf.DeltaAngle(frameYaw, t.RootYaw[i]);
                v.FootPhase[k] = Vector3.Dot(t.At(i, Bone.FootL) - t.At(i, Bone.FootR), m.F);
                v.PlantShoulder[k] = Relative(i, ShoulderOf(m.PlantBone));
                if (m.SecondPlant >= 0) v.SecondShoulder[k] = Relative(i, ShoulderOf(m.SecondBone));
            }

            Vector3 rootAtPlant = Root(m.Plant);
            v.HandOffset = new Vector3(Vector3.Dot(m.Hand - rootAtPlant, m.Right), m.HandGoalY, Vector3.Dot(m.Hand - rootAtPlant, m.F));
            if (m.SecondPlant >= 0)
            {
                int sp = m.SecondPlant;
                v.SecondPlant = t.Time(sp) - t0;
                v.SecondRelease = t.Time(m.SecondRelease) - t0;
                v.SecondRightHand = m.SecondBone == Bone.HandR;
                Pose(actor, m.Clip, t.Time(sp), Vector3.zero);
                Vector3 hand2 = t.At(sp, m.SecondBone), root2 = Root(sp);
                v.SecondHandOffset = new Vector3(Vector3.Dot(hand2 - root2, m.Right), actor.Bones[(int)m.SecondBone].position.y * scale, Vector3.Dot(hand2 - root2, m.F));
            }

            v.ApproachSpeed = HorizontalSpeed(t, t.Index(t.Time(m.Plant) - 0.7f), t.Index(t.Time(m.Plant) - 0.3f));
            v.ExitSpeed = HorizontalSpeed(t, m.Escape, Mathf.Min(t.Count - 1, m.Escape + Mathf.RoundToInt(0.3f * Rate)));
            float peak = float.MinValue;
            for (int i = m.Takeoff; i <= m.Land; i++) peak = Mathf.Max(peak, t.At(i, Bone.Hips).y);
            v.HipsRise = Mathf.Max(0f, peak - t.At(m.Takeoff, Bone.Hips).y);
            v.LandFromHand = Vector3.Dot(t.At(m.Land, m.LandFoot) - m.Hand, m.F);
            return v;
        }

        private static float SoleY(Track t, int i, Rig rig, bool left) =>
            Mathf.Min(t.At(i, left ? Bone.FootL : Bone.FootR).y - (left ? rig.SoleL : rig.SoleR), t.At(i, left ? Bone.ToeL : Bone.ToeR).y - 0.02f);

        private static float HorizontalSpeed(Track t, int from, int to)
        {
            if (to <= from) return 0f;
            Vector3 d = t.At(to, Bone.Hips) - t.At(from, Bone.Hips);
            d.y = 0f;
            return d.magnitude / ((to - from) * t.Dt);
        }

        /// <summary>Apparent gravity of the hips' arc between two samples (least-squares parabola), or NaN if too short.</summary>
        private static float FlightGravity(Track t, int a, int b)
        {
            if (b - a < 4) return float.NaN;
            double s0 = 0, s1 = 0, s2 = 0, s3 = 0, s4 = 0, y0 = 0, y1 = 0, y2 = 0;
            for (int i = a; i <= b; i++)
            {
                double x = (i - a) * t.Dt, y = t.At(i, Bone.Hips).y;
                s0 += 1; s1 += x; s2 += x * x; s3 += x * x * x; s4 += x * x * x * x;
                y0 += y; y1 += x * y; y2 += x * x * y;
            }
            double Det(double a00, double a01, double a02, double a10, double a11, double a12, double a20, double a21, double a22) =>
                a00 * (a11 * a22 - a12 * a21) - a01 * (a10 * a22 - a12 * a20) + a02 * (a10 * a21 - a11 * a20);
            double d = Det(s0, s1, s2, s1, s2, s3, s2, s3, s4);
            if (System.Math.Abs(d) < 1e-12) return float.NaN;
            double c2 = Det(s0, s1, y0, s1, s2, y1, s2, s3, y2) / d;
            return (float)(-2.0 * c2);
        }

        // ─── Warp lab ───────────────────────────────────────────────────────────────
        // What an obstacle (top H, depth D) needs from a clip: the body moved by a vertical lift L (the
        // VaultWarpProfile.Lift profile) and a horizontal stretch E (VaultWarpProfile.Stretch), with the
        // hand planted at an inset past the front edge. The baked mesh must stay above the top while over
        // it (feet may touch: their IK lifts them; a lazy vault rests the pelvis and thighs on it), and the
        // shoulder must reach the plant point when the palm lands (Humanoid IK stretches the arm up to 5 %).

        internal struct LabCell { public bool Ok; public float Lift, Stretch, Inset, Reach; public string Why; }

        private const float WristAboveTop = 0.06f, MaxHandCorrection = 0.22f;
        /// <summary>Largest lift (m): more is an exaggerated flight. Largest drop, for low obstacles, and largest stretch.</summary>
        internal const float MaxLift = 0.2f, MaxDrop = 0.4f, MaxStretch = 0.6f;
        internal static readonly float[] LabHeights = { 0.5f, 0.6f, 0.7f, 0.8f, 0.9f, 1.0f, 1.1f, 1.2f, 1.3f };
        internal static readonly float[] LabDepths  = { 0.1f, 0.2f, 0.3f, 0.4f, 0.5f, 0.6f, 0.8f, 1.0f, 1.2f, 1.4f };

        private const float MeshBinStart = -1.5f, MeshBin = 0.02f;
        private const int   MeshBins = 200;

        /// <summary>Lowest point of the skinned body over each 2-cm bin along the vault (relative to the planted hand), per airborne sample and vertex class.</summary>
        private static float[,,] MeshProfiles(Rig rig, Measure m)
        {
            int n = m.Land - m.Takeoff + 1;
            var prof = new float[n, 3, MeshBins];
            for (int k = 0; k < n; k++) for (int c = 0; c < 3; c++) for (int j = 0; j < MeshBins; j++) prof[k, c, j] = float.PositiveInfinity;
            var mesh = new Mesh();
            var verts = new List<Vector3>();
            foreach (Renderer r in rig.Root.GetComponentsInChildren<Renderer>()) r.enabled = true;
            try
            {
                for (int k = 0; k < n; k++)
                {
                    Pose(rig, m.Clip, m.T.Time(m.Takeoff + k), Vector3.zero);
                    for (int si = 0; si < rig.Skins.Length; si++)
                    {
                        SkinnedMeshRenderer smr = rig.Skins[si];
                        smr.BakeMesh(mesh, true);
                        mesh.GetVertices(verts);
                        byte[] classes = rig.VertexClass[si];
                        Matrix4x4 mat = smr.transform.localToWorldMatrix;
                        for (int v = 0; v < verts.Count && v < classes.Length; v++)
                        {
                            int c = classes[v];
                            if (c == 3) continue;
                            Vector3 w = mat.MultiplyPoint3x4(verts[v]);
                            int j = Mathf.FloorToInt((Vector3.Dot(w - m.Hand, m.F) - MeshBinStart) / MeshBin);
                            if (j < 0 || j >= MeshBins) continue;
                            if (w.y < prof[k, c, j]) prof[k, c, j] = w.y;
                        }
                    }
                }
            }
            finally
            {
                foreach (Renderer r in rig.Root.GetComponentsInChildren<Renderer>()) r.enabled = false;
                Object.DestroyImmediate(mesh);
            }
            return prof;
        }

        private static LabCell[,] WarpLab(Measure m)
        {
            Track t = m.T;
            VaultVariant v = m.V;
            float t0 = t.Time(0);
            bool lazy = v.Style == VaultStyle.Lazy;
            Vector3 hand = new Vector3(m.Hand.x, m.HandGoalY, m.Hand.z);
            float A(Vector3 p) => Vector3.Dot(p - hand, m.F);
            float aTakeoff = A(t.At(m.Takeoff, m.TakeoffFoot)) + 0.12f;   // toes ahead of the ankle
            float aLand = A(t.At(m.Land, m.LandFoot)) - 0.08f;            // heel behind the ankle
            float airTime = Mathf.Max(0.1f, v.Land - v.Takeoff);
            float clipSpeed = Mathf.Max(0.5f, v.ApproachSpeed);
            Bone shoulder = m.PlantBone == Bone.HandL ? Bone.ArmL : Bone.ArmR;

            var grid = new LabCell[LabHeights.Length, LabDepths.Length];
            for (int hi = 0; hi < LabHeights.Length; hi++)
            for (int di = 0; di < LabDepths.Length; di++)
            {
                float H = LabHeights[hi], D = LabDepths[di];
                var best = new LabCell { Ok = false, Why = "sin solución" };
                float bestScore = float.MaxValue;
                for (float inset = Mathf.Min(0.08f, D * 0.5f); inset <= Mathf.Max(D - 0.05f, D * 0.5f) + 1e-4f; inset += 0.04f)
                {
                    if (aTakeoff > -inset - 0.02f) { if (best.Why == "sin solución") best.Why = "despegue dentro"; continue; }
                    float eLand = Mathf.Max(0f, (D - inset + 0.05f) - aLand);
                    for (float extra = 0f; extra <= 0.3f + 1e-4f; extra += 0.1f)
                    {
                        float E = eLand + extra;
                        if (E > MaxStretch || E / airTime > 0.3f * clipSpeed) { if (best.Why == "sin solución") best.Why = "estiramiento"; continue; }
                        float need = float.NegativeInfinity;
                        bool ok = true;
                        for (int k = m.Takeoff + 1; k < m.Land && ok; k++)
                        {
                            float tk = t.Time(k) - t0;
                            float b = VaultWarpProfile.Lift(v, tk);
                            float e = E * VaultWarpProfile.Stretch(v, tk);
                            for (int c = 0; c < 3 && ok; c++)
                            {
                                float target = c == 0 ? H : c == 1 ? (lazy ? H - 0.03f : H + 0.02f) : H + 0.03f;
                                for (int j = 0; j < MeshBins; j++)
                                {
                                    float y = m.Mesh[k - m.Takeoff, c, j];
                                    if (float.IsInfinity(y) || y >= target) continue;
                                    float a = MeshBinStart + (j + 0.5f) * MeshBin + e;
                                    if (a < -inset || a > D - inset) continue; // not over the top
                                    if (b < 0.05f) { ok = false; break; }
                                    need = Mathf.Max(need, (target - y) / b);
                                }
                            }
                        }
                        if (!ok) { if (best.Why == "sin solución") best.Why = "cuerpo sin elevar"; continue; }
                        // Lift bounds: at least what the body needs to clear the top, at most what keeps the
                        // shoulder within reach of the plant point; within them, closest to the clip's own
                        // hand-to-top relation (its hand goal on the top)
                        float ePlant = E * VaultWarpProfile.Stretch(v, v.Plant);
                        Vector3 plantPoint = hand + m.F * Mathf.Clamp(ePlant, -inset + 0.05f, D - inset - 0.05f);
                        plantPoint.y = H + WristAboveTop;
                        Vector3 sh = t.At(m.Plant, shoulder) + m.F * ePlant;
                        Vector3 dxz = sh - plantPoint; dxz.y = 0f;
                        float R = m.ArmLength * 1.05f;
                        if (dxz.magnitude >= R) { if (best.Why == "sin solución" || best.Why == "estiramiento") best.Why = "mano lejos"; continue; }
                        float liftReach = plantPoint.y + Mathf.Sqrt(R * R - dxz.sqrMagnitude) - sh.y;
                        if (need > liftReach) { if (best.Why == "sin solución" || best.Why == "estiramiento") best.Why = "mano lejos"; continue; }
                        float L = Mathf.Clamp(H + WristAboveTop - hand.y, need, liftReach);
                        if (L > MaxLift || L < -MaxDrop) { best.Why = L > 0f ? "demasiado alto" : "demasiado bajo"; continue; }
                        float reach = Vector3.Distance(hand + m.F * ePlant + Vector3.up * L, plantPoint);
                        if (reach > MaxHandCorrection) { if (best.Why == "sin solución" || best.Why == "estiramiento") best.Why = "mano lejos"; continue; }
                        float score = Mathf.Abs(L) + 0.5f * E + reach;
                        if (score < bestScore)
                        {
                            bestScore = score;
                            best = new LabCell { Ok = true, Lift = L, Stretch = E, Inset = inset, Reach = reach, Why = "" };
                        }
                    }
                }
                grid[hi, di] = best;
            }
            return grid;
        }

        // ─── Build ──────────────────────────────────────────────────────────────────

        private static bool Build()
        {
            Directory.CreateDirectory(OutRoot);
            Avatar unit = AssetDatabase.LoadAllAssetsAtPath(UnitPath).OfType<Avatar>().FirstOrDefault();
            if (unit == null || !unit.isHuman) { Debug.LogError("[VaultCatalog] Unit.FBX no tiene Avatar Humanoid: corre 'Probar Retarget del Mocap'."); return false; }

            // The takes: imported whole (the probes' clip) plus the selected vaults as sub-clips and mirrors
            var takeClips = new Dictionary<string, AnimationClip>();
            foreach (string take in Segments.Select(s => s.Take).Distinct())
            {
                string path = KinFolder + take + ".fbx";
                Segment[] chosen = Segments.Where(s => s.Take == take && Selected.Contains(s.Id)).ToArray();
                if (!ConfigureTake(path, unit, chosen)) return false;
                AnimationClip whole = LoadClip(path, take);
                if (whole == null) { Debug.LogError($"[VaultCatalog] {path}: falta la toma completa '{take}'."); return false; }
                takeClips[take] = whole;
            }

            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            Rig rig = CreateRig(Ch45Path), actor = CreateRig(UnitPath);
            var measures = new List<Measure>();
            var sheets = new PoseSheetRenderer(300, 240, 1.5f);
            AnimationMode.StartAnimationMode();
            try
            {
                foreach (Segment s in Segments)
                {
                    bool chosen = Selected.Contains(s.Id);
                    AnimationClip clip = chosen ? LoadClip(KinFolder + s.Take + ".fbx", StateName(s.Id, false)) : takeClips[s.Take];
                    if (clip == null) { Debug.LogError($"[VaultCatalog] Falta el sub-clip {StateName(s.Id, false)}."); return false; }
                    Measure m = MeasureSegment(rig, actor, s, clip, chosen ? 0f : s.Start);
                    m.Mesh = MeshProfiles(rig, m);
                    m.Lab = WarpLab(m);
                    RenderStoryboard(rig, sheets, m, null);
                    foreach ((float H, float D) in WarpedTargets)
                    {
                        int hi = System.Array.IndexOf(LabHeights, H), di = System.Array.IndexOf(LabDepths, D);
                        if (hi >= 0 && di >= 0 && m.Lab[hi, di].Ok) RenderStoryboard(rig, sheets, m, (H, D, m.Lab[hi, di]));
                    }
                    measures.Add(m);
                }
            }
            finally
            {
                AnimationMode.StopAnimationMode();
                sheets.Dispose();
                Object.DestroyImmediate(rig.Root);
                Object.DestroyImmediate(actor.Root);
            }

            Report(measures);
            return WriteCatalog(measures.Where(m => Selected.Contains(m.Seg.Id)).ToList());
        }

        /// <summary>
        /// Imports a take: its whole length as one clip (as MocapRetargetProbe does) plus each chosen vault
        /// as a sub-clip and its mirror, with the vault's root settings: XZ root motion (center of mass),
        /// the height baked into the pose as captured (the floor stays the floor; the warper adds the lift)
        /// and the heading free (the warper replaces it with the obstacle's direction).
        /// </summary>
        private static bool ConfigureTake(string path, Avatar unit, Segment[] chosen)
        {
            if (!(AssetImporter.GetAtPath(path) is ModelImporter importer)) { Debug.LogError($"[VaultCatalog] No se encontró {path}."); return false; }
            string name = Path.GetFileNameWithoutExtension(path);
            if (importer.animationType != ModelImporterAnimationType.Human || importer.sourceAvatar != unit)
            {
                importer.animationType = ModelImporterAnimationType.Human;
                importer.avatarSetup   = ModelImporterAvatarSetup.CopyFromOther;
                importer.sourceAvatar  = unit;
            }
            importer.materialImportMode = ModelImporterMaterialImportMode.None;
            importer.importAnimation    = true;
            importer.importBlendShapes  = false;
            importer.importCameras      = false;
            importer.importLights       = false;
            ModelImporterClipAnimation[] takes = importer.defaultClipAnimations;
            if (takes.Length == 0) { Debug.LogError($"[VaultCatalog] {path} no tiene tomas."); return false; }

            var clips = new List<ModelImporterClipAnimation>();
            ModelImporterClipAnimation whole = takes[0];
            whole.name                    = name;
            whole.loopTime                = false;
            whole.lockRootRotation        = false;
            whole.lockRootHeightY         = MxMLocomotionBuilder.IsGroundLocomotion(path);
            whole.lockRootPositionXZ      = false;
            whole.keepOriginalOrientation = false;
            whole.keepOriginalPositionY   = false;
            whole.keepOriginalPositionXZ  = false;
            whole.heightFromFeet          = true;
            clips.Add(whole);
            float fps = 30f; // the Kinematica takes are 30 fps (MocapRetargetProbe metrics)
            foreach (Segment s in chosen)
            {
                foreach (bool mirror in new[] { false, true })
                {
                    ModelImporterClipAnimation sub = importer.defaultClipAnimations[0];
                    sub.name                    = StateName(s.Id, mirror);
                    sub.firstFrame              = s.Start * fps;
                    sub.lastFrame               = s.End * fps;
                    sub.mirror                  = mirror;
                    sub.loopTime                = false;
                    sub.lockRootRotation        = false;
                    sub.keepOriginalOrientation = false;
                    sub.lockRootHeightY         = true;
                    sub.keepOriginalPositionY   = true;
                    sub.heightFromFeet          = false;
                    sub.lockRootPositionXZ      = false;
                    sub.keepOriginalPositionXZ  = false;
                    clips.Add(sub);
                }
            }
            importer.clipAnimations = clips.ToArray();
            importer.SaveAndReimport();
            return true;
        }

        private static AnimationClip LoadClip(string path, string clipName) =>
            AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>().FirstOrDefault(c => c.name == clipName && !c.name.StartsWith("__preview__"));

        private static bool WriteCatalog(List<Measure> chosen)
        {
            if (!AssetDatabase.IsValidFolder("Assets/Data/Parkour"))
                AssetDatabase.CreateFolder("Assets/Data", "Parkour");
            var catalog = AssetDatabase.LoadAssetAtPath<VaultCatalog>(CatalogPath);
            bool created = catalog == null;
            if (created) catalog = ScriptableObject.CreateInstance<VaultCatalog>();
            catalog.Heights = LabHeights.ToArray();
            catalog.Depths = LabDepths.ToArray();
            var variants = new List<VaultVariant>();
            foreach (Measure m in chosen)
            {
                Fill(m.V, m.Lab);
                variants.Add(m.V);
                variants.Add(Mirrored(m.V));
            }
            catalog.Variants = variants.ToArray();
            if (created) AssetDatabase.CreateAsset(catalog, CatalogPath);
            EditorUtility.SetDirty(catalog);
            AssetDatabase.SaveAssets();
            Debug.Log($"[VaultCatalog] {CatalogPath}: {variants.Count} vaults ({string.Join(", ", variants.Select(v => v.Id))}).");
            return variants.Count > 0;
        }

        private static void Fill(VaultVariant v, LabCell[,] lab)
        {
            int n = LabHeights.Length * LabDepths.Length;
            v.Ok = new bool[n];
            v.Lift = new float[n];
            v.Stretch = new float[n];
            v.Inset = new float[n];
            for (int hi = 0; hi < LabHeights.Length; hi++)
            for (int di = 0; di < LabDepths.Length; di++)
            {
                int i = hi * LabDepths.Length + di;
                LabCell c = lab[hi, di];
                v.Ok[i] = c.Ok;
                v.Lift[i] = c.Lift;
                v.Stretch[i] = c.Stretch;
                v.Inset[i] = c.Inset;
            }
        }

        /// <summary>The same vault with the other hand: everything lateral changes sign, left and right swap.</summary>
        private static VaultVariant Mirrored(VaultVariant v)
        {
            var m = JsonUtility.FromJson<VaultVariant>(JsonUtility.ToJson(v));
            m.Id = v.Id + "_M";
            m.State = StateName(v.Id, true);
            m.Mirror = true;
            m.RightHand = !v.RightHand;
            m.SecondRightHand = !v.SecondRightHand;
            m.FrameYaw = -v.FrameYaw;
            m.HandOffset.x = -v.HandOffset.x;
            m.SecondHandOffset.x = -v.SecondHandOffset.x;
            for (int i = 0; i < m.PathLateral.Length; i++) m.PathLateral[i] = -v.PathLateral[i];
            for (int i = 0; i < m.Heading.Length; i++) m.Heading[i] = -v.Heading[i];
            for (int i = 0; i < m.PlantShoulder.Length; i++) m.PlantShoulder[i].x = -v.PlantShoulder[i].x;
            for (int i = 0; i < m.SecondShoulder.Length; i++) m.SecondShoulder[i].x = -v.SecondShoulder[i].x;
            for (int i = 0; i < m.FootPhase.Length; i++) m.FootPhase[i] = -v.FootPhase[i];
            return m;
        }

        // ─── Reports ────────────────────────────────────────────────────────────────

        private static void Report(List<Measure> measures)
        {
            var ci = CultureInfo.InvariantCulture;
            var csv = new StringBuilder("id,selected,style,hand,approach,exit,handGoalY,handFkY,takeoff,plant,release,land,escape,flight,gFlight,hipsRise,landFromHand,frameYaw,plantSpeed\n");
            var env = new StringBuilder("id,height,depth,ok,lift,stretch,inset,handReach,why\n");
            var log = new StringBuilder("[VaultCatalog] Clips medidos sobre Ch45 (tiempos desde el apoyo de la mano; por altura: fondo máximo que libra con lift de +0.2 a −0.4 m, estiramiento ≤ 0.6 m y la mano al alcance):\n");
            foreach (Measure m in measures)
            {
                VaultVariant v = m.V;
                bool chosen = Selected.Contains(m.Seg.Id);
                float[] vals = { v.ApproachSpeed, v.ExitSpeed, m.HandGoalY, m.Hand.y, v.Takeoff - v.Plant, v.Plant, v.Release - v.Plant, v.Land - v.Plant, v.Escape - v.Plant,
                                 v.Land - v.Takeoff, m.Gravity, v.HipsRise, v.LandFromHand, v.FrameYaw, m.PlantSpeed };
                csv.Append(m.Seg.Id).Append(',').Append(chosen ? 1 : 0).Append(',').Append(v.Style).Append(',').Append(v.RightHand ? "R" : "L");
                foreach (float x in vals) csv.Append(',').Append(x.ToString("F3", ci));
                csv.Append('\n');
                log.Append(string.Format(ci, "  {0}{1,-15} {2,-5} entra {3:F2} sale {4:F2} m/s | mano meta {5:F2} m | despegue {6:+0.00;-0.00} s, suelta {12:+0.00}/{7:+0.00} s, aterriza {8:+0.00} s, escape {9:+0.00} s | vuelo g {10:F1}, arco {11:F2} m | rumbo {13:F0}/{14:F0}/{15:F0}/{16:F0}° |",
                    chosen ? "* " : "  ", m.Seg.Id, v.Style, v.ApproachSpeed, v.ExitSpeed, m.HandGoalY, v.Takeoff - v.Plant, v.Release - v.Plant, v.Land - v.Plant, v.Escape - v.Plant, m.Gravity, v.HipsRise,
                    v.PlantRelease - v.Plant, v.HeadingAt(v.Takeoff), v.HeadingAt(v.Plant), v.HeadingAt(v.Land), v.HeadingAt(v.Escape)));
                for (int hi = 0; hi < LabHeights.Length; hi++)
                {
                    float maxD = 0f;
                    for (int di = 0; di < LabDepths.Length; di++)
                    {
                        LabCell c = m.Lab[hi, di];
                        env.Append(m.Seg.Id).Append(',').Append(LabHeights[hi].ToString("F2", ci)).Append(',').Append(LabDepths[di].ToString("F2", ci)).Append(',')
                           .Append(c.Ok ? 1 : 0).Append(',').Append(c.Lift.ToString("F3", ci)).Append(',').Append(c.Stretch.ToString("F3", ci)).Append(',')
                           .Append(c.Inset.ToString("F2", ci)).Append(',').Append(c.Reach.ToString("F3", ci)).Append(',').Append(c.Why).Append('\n');
                        if (c.Ok) maxD = LabDepths[di];
                    }
                    log.Append(string.Format(ci, " {0:F1}:{1:F1}", LabHeights[hi], maxD));
                }
                log.Append('\n');
            }
            File.WriteAllText(Path.Combine(OutRoot, "segments.csv"), csv.ToString());
            File.WriteAllText(Path.Combine(OutRoot, "envelope.csv"), env.ToString());
            Debug.Log(log.ToString());
        }

        /// <summary>Standard obstacles (top, depth) the warped vaults are rendered over: low, medium, medium deep and the top of the vault range.</summary>
        private static readonly (float, float)[] WarpedTargets = { (0.6f, 0.4f), (1.0f, 0.3f), (1.0f, 0.5f), (1.0f, 1.4f), (1.1f, 0.4f) };

        /// <summary>
        /// Storyboard of a vault: one cell per key moment (approach, take-off, plant, support, release,
        /// flight, landing, escape) with the camera fixed on the obstacle; row 0 from the side (travel to
        /// the left), row 1 three-quarter from ahead on the left. Without a warp, over the mocap obstacle;
        /// with one, warped onto that standard obstacle (the hands are not solved here: the runtime IK puts
        /// the planted palm on the red point).
        /// </summary>
        private static void RenderStoryboard(Rig rig, PoseSheetRenderer sheets, Measure m, (float H, float D, LabCell Cell)? warped)
        {
            Track t = m.T;
            VaultVariant v = m.V;
            float t0 = t.Time(0);
            int span = Mathf.RoundToInt(0.3f * Rate);
            int[] frames =
            {
                Mathf.Max(0, m.Takeoff - span), m.Takeoff, m.Plant, (m.Plant + m.Release) / 2, m.Release,
                (m.Release + m.Land) / 2, m.Land, Mathf.Min(t.Count - 1, m.Escape),
            };
            (float nh, float nd) = Native(m.Seg.Kind);
            float h = warped?.H ?? nh, d = warped?.D ?? nd;
            float inset = warped?.Cell.Inset ?? (m.Seg.Kind == VaultKind.Fence ? nd * 0.5f : 0.15f);
            Vector3 front = new Vector3(m.Hand.x, 0f, m.Hand.z) - m.F * inset;
            Vector3 focus = front + m.F * (d * 0.5f) + Vector3.up * 1.0f;
            Vector3 right = Vector3.Cross(Vector3.up, m.F);
            foreach (Renderer r in rig.Root.GetComponentsInChildren<Renderer>()) r.enabled = true;
            try
            {
                sheets.Begin(frames.Length, 2);
                sheets.SetObstacle(front + m.F * (d * 0.5f) + Vector3.up * (h * 0.5f), new Vector3(1.4f, h, d), Quaternion.LookRotation(m.F, Vector3.up).eulerAngles.y);
                sheets.SetMarker(front + m.F * inset + Vector3.up * h);
                for (int c = 0; c < frames.Length; c++)
                {
                    float tk = t.Time(frames[c]);
                    Vector3 warp = Vector3.zero;
                    if (warped.HasValue)
                        warp = m.F * (warped.Value.Cell.Stretch * VaultWarpProfile.Stretch(v, tk - t0)) + Vector3.up * (warped.Value.Cell.Lift * VaultWarpProfile.Lift(v, tk - t0));
                    Pose(rig, m.Clip, tk, warp);
                    sheets.Capture(0, c, focus, right, 0f);
                    sheets.Capture(1, c, focus, (right - m.F).normalized, 0f);
                }
                sheets.SetObstacle(null, Vector3.one, 0f);
                sheets.SetMarker(null);
                string file = warped.HasValue
                    ? string.Format(CultureInfo.InvariantCulture, "{0}_H{1:F1}_D{2:F1}.png", m.Seg.Id, h, d)
                    : m.Seg.Id + ".png";
                sheets.Save(Path.Combine(OutRoot, file));
            }
            finally
            {
                foreach (Renderer r in rig.Root.GetComponentsInChildren<Renderer>()) r.enabled = false;
            }
        }
    }
}
