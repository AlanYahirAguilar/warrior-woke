/// <summary>
/// Optional hook that lets the owner of a HealthSystem change incoming damage before it is
/// applied (e.g. the player's block: −70 % from the front, GDD §5.8).
/// HealthSystem looks for it on the same GameObject in Awake; without one, damage is unchanged.
/// </summary>
public interface IDamageModifier
{
    /// <summary>Returns the damage to apply.</summary>
    /// <param name="amount">Incoming damage (always positive).</param>
    /// <param name="source">World position of the damage source.</param>
    int ModifyIncomingDamage(int amount, UnityEngine.Vector3 source);
}
