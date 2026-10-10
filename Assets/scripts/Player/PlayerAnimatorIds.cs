using UnityEngine;

/// <summary>
/// Names shared by PlayerAnimator and the Editor tool that builds PlayerAnimator.controller.
/// Hashes are computed once (Animator.StringToHash), never per frame.
/// </summary>
public static class PlayerAnimatorIds
{
    // Parameters
    public const string MoveX  = "MoveX";  // velocity under the body, local right (m/s): the directional locomotion blend
    public const string MoveZ  = "MoveZ";  // velocity under the body, local forward (m/s)
    public const string LocomotionRate = "LocomotionRate"; // playback of the locomotion beyond its fastest clip (sprint)
    public const string DodgeX = "DodgeX";
    public const string DodgeY = "DodgeY";
    public const string ParkourSpeed = "ParkourSpeed"; // playback rate of the vault clip (PlayerVaultState.PlaybackRate)
    public const string SlideEnterRate = "SlideEnterRate"; // playback of the drop into the slide: a faster entry drops faster

    // Clips whose length PlayerAnimator needs to start them at an offset (the vault clips: VaultCatalog)
    public const string LedgeGrabClip = "Idle To Braced Hang";
    public const string MantleClip    = "ClimbUp_1m"; // Quaternius UAL2 "ClimbUp_1m_RM"

    // States (Base Layer)
    public const string LocomotionName       = "Locomotion"; // 2D directional blend: idle, walk, jog, run, backward, strafe, diagonals
    public const string CrouchName           = "Crouch";
    public const string JumpName             = "Jump";
    public const string FallName             = "Fall";
    public const string LandName             = "Land";
    public const string LandRunName          = "LandRun";
    public const string LandHardName         = "LandHard";
    public const string LandRollName         = "LandRoll";   // heavy landing at speed absorbed with a roll
    public const string LedgeHangName        = "LedgeHang";
    public const string SlideName            = "Slide";      // drop into the slide ("Slide_Start")
    public const string SlideLoopName        = "SlideLoop";  // sliding ("Slide_Loop", a true loop)
    public const string SlideExitName        = "SlideExit";  // get up ("Slide_Exit")
    public const string MantleName           = "Mantle";
    public const string LedgeGrabName        = "LedgeGrab";
    public const string LedgeClimbName       = "LedgeClimb";
    public const string LedgeDropName        = "LedgeDrop";  // "Braced Hang To Crouch" reversed
    public const string LightAttack1Name     = "LightAttack1";  // jab
    public const string LightAttack2Name     = "LightAttack2";  // cross
    public const string LightAttack3Name     = "LightAttack3";  // hook (CMU)
    public const string HeavyAttackName      = "HeavyAttack";   // front kick (CMU)
    public const string HurtName             = "Hurt";          // hit to the body
    public const string HurtHeadName         = "HurtHead";      // hit to the head (a heavy hit)
    public const string DeathName            = "Death";         // dies (Quaternius Death01)
    public const string DeathClip            = "Death01";
    public const string BlockEnterName       = "BlockEnter";
    public const string BlockLoopName        = "BlockLoop";
    public const string BlockExitName        = "BlockExit";
    public const string DodgeName            = "Dodge";

    public static readonly int MoveXParam  = Animator.StringToHash(MoveX);
    public static readonly int MoveZParam  = Animator.StringToHash(MoveZ);
    public static readonly int LocomotionRateParam = Animator.StringToHash(LocomotionRate);
    public static readonly int DodgeXParam = Animator.StringToHash(DodgeX);
    public static readonly int DodgeYParam = Animator.StringToHash(DodgeY);
    public static readonly int ParkourSpeedParam = Animator.StringToHash(ParkourSpeed);
    public static readonly int SlideEnterRateParam = Animator.StringToHash(SlideEnterRate);

    public static readonly int Locomotion       = Animator.StringToHash(LocomotionName);
    public static readonly int Crouch           = Animator.StringToHash(CrouchName);
    public static readonly int Jump             = Animator.StringToHash(JumpName);
    public static readonly int Fall             = Animator.StringToHash(FallName);
    public static readonly int Land             = Animator.StringToHash(LandName);
    public static readonly int LandRun          = Animator.StringToHash(LandRunName);
    public static readonly int LandHard         = Animator.StringToHash(LandHardName);
    public static readonly int LandRoll         = Animator.StringToHash(LandRollName);
    public static readonly int LedgeHang        = Animator.StringToHash(LedgeHangName);
    public static readonly int Slide            = Animator.StringToHash(SlideName);
    public static readonly int SlideExit        = Animator.StringToHash(SlideExitName);
    public static readonly int SlideLoop        = Animator.StringToHash(SlideLoopName);
    public static readonly int Mantle           = Animator.StringToHash(MantleName);
    public static readonly int LedgeGrab        = Animator.StringToHash(LedgeGrabName);
    public static readonly int LedgeClimb       = Animator.StringToHash(LedgeClimbName);
    public static readonly int LedgeDrop        = Animator.StringToHash(LedgeDropName);
    public static readonly int LightAttack1     = Animator.StringToHash(LightAttack1Name);
    public static readonly int LightAttack2     = Animator.StringToHash(LightAttack2Name);
    public static readonly int LightAttack3     = Animator.StringToHash(LightAttack3Name);
    public static readonly int HeavyAttack      = Animator.StringToHash(HeavyAttackName);
    public static readonly int Hurt             = Animator.StringToHash(HurtName);
    public static readonly int HurtHead         = Animator.StringToHash(HurtHeadName);
    public static readonly int Death            = Animator.StringToHash(DeathName);
    public static readonly int BlockEnter       = Animator.StringToHash(BlockEnterName);
    public static readonly int BlockExit        = Animator.StringToHash(BlockExitName);
    public static readonly int Dodge            = Animator.StringToHash(DodgeName);
}
