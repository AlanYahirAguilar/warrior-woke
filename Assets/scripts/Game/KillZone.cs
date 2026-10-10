using UnityEngine;

/// <summary>
/// Fatal fall (GDD §5.11–5.12: falling from a great height, into a ravine, is the only instant death): a
/// trigger volume that kills whatever enters it. The Parkour Test Area has one under its floor, so a body
/// that walks off the edge dies and the player reappears (PlayerDeadState). Works with the player's
/// CharacterController and with the enemies' kinematic bodies.
/// </summary>
[RequireComponent(typeof(Collider))]
public class KillZone : MonoBehaviour
{
    private void Reset()
    {
        GetComponent<Collider>().isTrigger = true;
    }

    private void OnTriggerEnter(Collider other)
    {
        HealthSystem health = other.GetComponentInParent<HealthSystem>();
        if (health != null && !health.IsDead) health.InstantKill();
    }
}
