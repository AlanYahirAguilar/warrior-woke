using UnityEngine;

/// <summary>
/// Names shared by PlayerAnimator and the Editor tool that builds PlayerAnimator.controller.
/// Hashes are computed once (Animator.StringToHash), never per frame.
/// </summary>
public static class PlayerAnimatorIds
{
    // Parameters
    public const string Speed  = "Speed";
    public const string DodgeX = "DodgeX";
    public const string DodgeY = "DodgeY";
    public const string LHandCurve = "LHandCurve"; // driven by the curve of the VaultFence clip (Dynamic Parkour System)
    public const string ParkourSpeed = "ParkourSpeed"; // playback speed of the vault (approach speed / clip speed)

    // Clips whose length PlayerAnimator needs to start them at an offset
    public const string VaultClip     = "Vault1";
    public const string LedgeGrabClip = "Idle To Braced Hang";

    // States (Base Layer)
    public const string LocomotionName       = "Locomotion";
    public const string JumpName             = "Jump";
    public const string FallName             = "Fall";
    public const string LandName             = "Land";
    public const string LandRunName          = "LandRun";
    public const string LandHardName         = "LandHard";
    public const string LedgeHangName        = "LedgeHang";
    public const string SlideName            = "Slide";      // drop to the ground ("Slide Down")
    public const string SlideLoopName        = "SlideLoop";  // sliding ("Slide", loop)
    public const string SlideExitName        = "SlideExit";  // get up ("Slide Up")
    public const string VaultName            = "Vault";
    public const string LedgeGrabName        = "LedgeGrab";
    public const string LedgeClimbName       = "LedgeClimb";
    public const string LightAttackRightName = "LightAttackRight";
    public const string LightAttackLeftName  = "LightAttackLeft";
    public const string HeavyAttackName      = "HeavyAttack";
    public const string BlockEnterName       = "BlockEnter";
    public const string BlockLoopName        = "BlockLoop";
    public const string BlockExitName        = "BlockExit";
    public const string DodgeName            = "Dodge";

    public static readonly int SpeedParam  = Animator.StringToHash(Speed);
    public static readonly int DodgeXParam = Animator.StringToHash(DodgeX);
    public static readonly int DodgeYParam = Animator.StringToHash(DodgeY);
    public static readonly int LHandCurveParam = Animator.StringToHash(LHandCurve);
    public static readonly int ParkourSpeedParam = Animator.StringToHash(ParkourSpeed);

    public static readonly int Locomotion       = Animator.StringToHash(LocomotionName);
    public static readonly int Jump             = Animator.StringToHash(JumpName);
    public static readonly int Fall             = Animator.StringToHash(FallName);
    public static readonly int Land             = Animator.StringToHash(LandName);
    public static readonly int LandRun          = Animator.StringToHash(LandRunName);
    public static readonly int LandHard         = Animator.StringToHash(LandHardName);
    public static readonly int LedgeHang        = Animator.StringToHash(LedgeHangName);
    public static readonly int Slide            = Animator.StringToHash(SlideName);
    public static readonly int SlideExit        = Animator.StringToHash(SlideExitName);
    public static readonly int Vault            = Animator.StringToHash(VaultName);
    public static readonly int LedgeGrab        = Animator.StringToHash(LedgeGrabName);
    public static readonly int LedgeClimb       = Animator.StringToHash(LedgeClimbName);
    public static readonly int LightAttackRight = Animator.StringToHash(LightAttackRightName);
    public static readonly int LightAttackLeft  = Animator.StringToHash(LightAttackLeftName);
    public static readonly int HeavyAttack      = Animator.StringToHash(HeavyAttackName);
    public static readonly int BlockEnter       = Animator.StringToHash(BlockEnterName);
    public static readonly int BlockExit        = Animator.StringToHash(BlockExitName);
    public static readonly int Dodge            = Animator.StringToHash(DodgeName);
}
