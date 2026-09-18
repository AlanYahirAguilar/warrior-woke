using UnityEngine;
using System.Collections;

/// <summary>
/// Componente de conveniencia para devolver un objeto a la piscina después de un tiempo.
/// Útil para partículas, balas o efectos efímeros. Implementa IPoolable.
/// </summary>
public class ReturnToPoolDelay : MonoBehaviour, IPoolable
{
    [Tooltip("Tiempo en segundos antes de regresar al pool.")]
    [SerializeField] private float delay = 2f;
    
    private WaitForSeconds _wait;
    
    public string PoolId { get; set; }

    private void Awake()
    {
        // Cacheamos para cero allocations en tiempo de ejecución
        _wait = new WaitForSeconds(delay);
    }

    public void OnSpawn()
    {
        StartCoroutine(ReturnRoutine());
    }

    public void OnDespawn()
    {
        StopAllCoroutines();
    }

    private IEnumerator ReturnRoutine()
    {
        yield return _wait;
        
        if (ObjectPoolManager.Instance != null)
        {
            ObjectPoolManager.Instance.ReturnToPool(gameObject, PoolId);
        }
        else
        {
            // Fallback en caso de que el Manager sea destruido
            gameObject.SetActive(false);
        }
    }
}
