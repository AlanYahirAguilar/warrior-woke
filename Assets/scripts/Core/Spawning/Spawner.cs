using System.Collections;
using UnityEngine;

/// <summary>
/// Spawner multi-propósito "tonto". Solo pide entidades al ObjectPoolManager usando un String ID.
/// Cero allocations en el loop principal, WaitForSeconds en caché.
/// </summary>
public class Spawner : MonoBehaviour
{
    public enum SpawnTrigger
    {
        OnStart,
        OnTriggerEnter,
        Timer,
        Manual
    }

    [Header("Configuración Principal")]
    [Tooltip("ID del objeto a spawnear, definido en el ObjectPoolManager.")]
    [SerializeField] private string entityId;

    [Tooltip("ID opcional de un efecto visual (Particle System) para acompañar la aparición. Déjalo vacío si no aplica.")]
    [SerializeField] private string spawnEffectId;

    [Tooltip("¿Qué detona el spawn?")]
    [SerializeField] private SpawnTrigger triggerType = SpawnTrigger.Manual;

    [Header("Configuración de Timer")]
    [Tooltip("Intervalo en segundos si el trigger es Timer.")]
    [SerializeField] private float spawnInterval = 3f;

    [Header("Configuración de Trigger Area")]
    [Tooltip("Tag requerido para activar el OnTriggerEnter.")]
    [SerializeField] private string triggeringTag = "Player";
    
    [Tooltip("Si es true, solo spawneará una vez cuando el jugador entre al área.")]
    [SerializeField] private bool oneShotTrigger = true;

    private bool _hasTriggered = false;
    private WaitForSeconds _timerWait;

    private void Awake()
    {
        if (triggerType == SpawnTrigger.Timer)
        {
            // Cacheamos el WaitForSeconds para evitar allocations durante el coroutine
            _timerWait = new WaitForSeconds(spawnInterval);
        }
    }

    private void Start()
    {
        if (triggerType == SpawnTrigger.OnStart)
        {
            ExecuteSpawn();
        }
        else if (triggerType == SpawnTrigger.Timer)
        {
            StartCoroutine(SpawnTimerRoutine());
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        if (triggerType != SpawnTrigger.OnTriggerEnter) return;

        if (oneShotTrigger && _hasTriggered) return;

        if (other.CompareTag(triggeringTag))
        {
            _hasTriggered = true;
            ExecuteSpawn();
        }
    }

    /// <summary>
    /// Llamado manualmente desde otros scripts o Unity Events.
    /// </summary>
    public void ManualSpawn()
    {
        if (triggerType == SpawnTrigger.Manual)
        {
            ExecuteSpawn();
        }
    }

    private void ExecuteSpawn()
    {
        if (ObjectPoolManager.Instance != null)
        {
            ObjectPoolManager.Instance.Spawn(entityId, transform.position, transform.rotation);

            // Si hay un efecto visual configurado, instanciarlo exactamente en el mismo Transform
            if (!string.IsNullOrEmpty(spawnEffectId))
            {
                ObjectPoolManager.Instance.Spawn(spawnEffectId, transform.position, transform.rotation);
            }
        }
        else
        {
            Debug.LogWarning($"[Spawner] No se encontró instancia de ObjectPoolManager para el ID: {entityId}");
        }
    }

    private IEnumerator SpawnTimerRoutine()
    {
        while (true)
        {
            yield return _timerWait;
            ExecuteSpawn();
        }
    }
}
