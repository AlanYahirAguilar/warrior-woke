using UnityEngine;

/// <summary>
/// On the enemy's model (next to its Animator): takes the root motion over (OnAnimatorMove) and hands it to
/// EnemyAnimator, which applies it only while an action asks for it (P40). Without it the Animator would
/// move the model away from the NavMeshAgent's body.
/// </summary>
[RequireComponent(typeof(Animator))]
public class EnemyRootMotion : MonoBehaviour
{
    private Animator _animator;
    private EnemyAnimator _owner;

    private void Awake()
    {
        _animator = GetComponent<Animator>();
        _owner = GetComponentInParent<EnemyAnimator>();
    }

    private void OnAnimatorMove()
    {
        if (_owner != null) _owner.OnRootMotion(_animator.deltaPosition);
    }
}
