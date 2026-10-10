using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Animations.Rigging;

namespace WarriorWoke.EditorTools
{
    /// <summary>
    /// Builds the Animation Rigging rig of Ch45 (P31, docs/arquitectura.md §5.10): a RigBuilder on the
    /// model's Animator with one rig, "ContactRig", holding the feet's ground contact
    /// (<see cref="GroundContactConstraint"/>) and, on the player, the head look
    /// (<see cref="HeadLookConstraint"/>). Used by PlayerAnimationSetup on Player.prefab and by
    /// MxMLocomotionProbe on its test model, so both measure the same rig. Idempotent.
    /// </summary>
    internal static class PlayerRigSetup
    {
        public const string RigName = "ContactRig", FeetName = "FeetContact", LookName = "HeadLook";

        /// <param name="animator">The Humanoid Animator of the model.</param>
        /// <param name="rootAboveFloor">Height (m) of the model root above the floor its animation stands on.</param>
        /// <param name="withLook">Also build the head look (the player; the probe only measures the feet).</param>
        public static GroundContactConstraint Build(Animator animator, float rootAboveFloor, bool withLook)
        {
            Transform model = animator.transform;
            Transform rigTransform = Child(model, RigName);
            if (!rigTransform.TryGetComponent(out Rig rig)) rig = rigTransform.gameObject.AddComponent<Rig>();
            rig.weight = 1f;

            if (!model.TryGetComponent(out RigBuilder builder)) builder = model.gameObject.AddComponent<RigBuilder>();
            builder.layers = new List<RigLayer> { new RigLayer(rig, true) };

            Transform feetTransform = Child(rigTransform, FeetName);
            if (!feetTransform.TryGetComponent(out GroundContactConstraint feet)) feet = feetTransform.gameObject.AddComponent<GroundContactConstraint>();
            feet.Reset(); // default tuning (GroundContactData.SetDefaultValues)
            ref GroundContactData d = ref feet.data;
            d.root = model;
            d.leftFoot = animator.GetBoneTransform(HumanBodyBones.LeftFoot);
            d.rightFoot = animator.GetBoneTransform(HumanBodyBones.RightFoot);
            d.leftToes = animator.GetBoneTransform(HumanBodyBones.LeftToes);
            d.rightToes = animator.GetBoneTransform(HumanBodyBones.RightToes);
            d.leftSole = animator.leftFeetBottomHeight;
            d.rightSole = animator.rightFeetBottomHeight;
            d.rootAboveFloor = rootAboveFloor;
            d.groundLayers = LayerMask.GetMask("Ground", "Obstacle");
            if (d.groundLayers.value == 0) d.groundLayers = ~0;

            Transform lookTransform = rigTransform.Find(LookName);
            if (withLook)
            {
                if (lookTransform == null) lookTransform = Child(rigTransform, LookName);
                if (!lookTransform.TryGetComponent(out HeadLookConstraint look)) look = lookTransform.gameObject.AddComponent<HeadLookConstraint>();
                look.Reset();
                ref HeadLookData l = ref look.data;
                l.root = model;
                l.chest = animator.GetBoneTransform(HumanBodyBones.UpperChest);
                if (l.chest == null) l.chest = animator.GetBoneTransform(HumanBodyBones.Chest);
                l.neck = animator.GetBoneTransform(HumanBodyBones.Neck);
                l.head = animator.GetBoneTransform(HumanBodyBones.Head);
                // The face's axis on the model's default pose (it stands facing the model's forward)
                if (l.head != null) l.headForward = Quaternion.Inverse(l.head.rotation) * model.forward;
                look.weight = 0f; // PlayerRig fades it in
            }
            else if (lookTransform != null)
                Object.DestroyImmediate(lookTransform.gameObject, true);

            return feet;
        }

        private static Transform Child(Transform parent, string name)
        {
            Transform t = parent.Find(name);
            if (t != null) return t;
            t = new GameObject(name).transform;
            t.SetParent(parent, false);
            return t;
        }
    }
}
