using UnityEngine;

/// <summary>
/// Handles environment detection (walls, ledges, low obstacles) using Raycasts.
/// Complies with SRP by isolating collision detection from movement logic.
/// </summary>
public class EnvironmentChecker : MonoBehaviour
{
    [Header("Layer Masks")]
    [SerializeField] private LayerMask obstacleLayer;

    [Header("Wall Check")]
    [SerializeField] private float wallCheckDistance = 0.6f;
    [SerializeField] private Transform centerPoint;

    [Header("Vault Check")]
    [SerializeField] private float vaultHeightCheck = 1.2f;
    [SerializeField] private Transform headPoint;

    [Header("Ledge Check")]
    [SerializeField] private float ledgeTopCheckDistance = 0.5f;

    public bool IsTouchingWall(float facingDirection)
    {
        if (centerPoint == null) return false;
        Vector3 direction = facingDirection > 0 ? Vector3.right : Vector3.left;
        return Physics.Raycast(centerPoint.position, direction, wallCheckDistance, obstacleLayer);
    }

    public bool IsObstacleVaultable(float facingDirection)
    {
        // Si tocamos pared en el centro, pero NO tocamos pared a la altura de la cabeza, es un vault
        if (headPoint == null) return false;
        
        bool touchingMid = IsTouchingWall(facingDirection);
        Vector3 direction = facingDirection > 0 ? Vector3.right : Vector3.left;
        bool touchingHigh = Physics.Raycast(headPoint.position, direction, wallCheckDistance, obstacleLayer);

        return touchingMid && !touchingHigh;
    }

    public bool IsLedgeDetected(float facingDirection, out Vector3 ledgeCorner)
    {
        ledgeCorner = Vector3.zero;
        if (headPoint == null || centerPoint == null) return false;

        Vector3 direction = facingDirection > 0 ? Vector3.right : Vector3.left;
        
        // Si choca la cabeza y el centro, es una pared alta
        bool touchingHigh = Physics.Raycast(headPoint.position, direction, wallCheckDistance, obstacleLayer);
        bool touchingMid = Physics.Raycast(centerPoint.position, direction, wallCheckDistance, obstacleLayer);

        if (touchingHigh && touchingMid)
        {
            // Tirar un rayo desde un poco más arriba y hacia adelante, luego hacia abajo para encontrar la esquina
            Vector3 topCheckOrigin = headPoint.position + Vector3.up * 0.5f + direction * wallCheckDistance;
            if (Physics.Raycast(topCheckOrigin, Vector3.down, out RaycastHit hit, ledgeTopCheckDistance, obstacleLayer))
            {
                ledgeCorner = hit.point;
                return true;
            }
        }
        return false;
    }

    private void OnDrawGizmosSelected()
    {
        if (centerPoint != null)
        {
            Gizmos.color = Color.blue;
            Gizmos.DrawLine(centerPoint.position, centerPoint.position + transform.right * wallCheckDistance);
        }
        if (headPoint != null)
        {
            Gizmos.color = Color.cyan;
            Gizmos.DrawLine(headPoint.position, headPoint.position + transform.right * wallCheckDistance);
        }
    }
}
