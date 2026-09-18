using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Gestor global de Object Pooling. 
/// Sigue la regla #1 de buenas prácticas: evitar Garbage Collection instanciando todo al inicio.
/// </summary>
public class ObjectPoolManager : MonoBehaviour
{
    [System.Serializable]
    public class PoolConfig
    {
        public string poolId;
        public GameObject prefab;
        public int initialSize = 10;
    }

    [Header("Configuración de Piscinas")]
    [SerializeField] private List<PoolConfig> pools;

    // Singleton simplificado para acceso global
    public static ObjectPoolManager Instance { get; private set; }

    private Dictionary<string, Queue<GameObject>> _poolDictionary;
    private Dictionary<string, GameObject> _prefabDictionary;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        InitializePools();
    }

    private void InitializePools()
    {
        _poolDictionary = new Dictionary<string, Queue<GameObject>>();
        _prefabDictionary = new Dictionary<string, GameObject>();

        foreach (var pool in pools)
        {
            Queue<GameObject> objectPool = new Queue<GameObject>();

            for (int i = 0; i < pool.initialSize; i++)
            {
                GameObject obj = Instantiate(pool.prefab);
                obj.SetActive(false);
                obj.transform.SetParent(transform);
                
                if (obj.TryGetComponent(out IPoolable poolable))
                {
                    poolable.PoolId = pool.poolId;
                }

                objectPool.Enqueue(obj);
            }

            _poolDictionary.Add(pool.poolId, objectPool);
            _prefabDictionary.Add(pool.poolId, pool.prefab);
        }
    }

    public GameObject Spawn(string id, Vector3 position, Quaternion rotation)
    {
        if (!_poolDictionary.ContainsKey(id))
        {
            Debug.LogWarning($"[ObjectPoolManager] No se encontró el pool con ID: {id}");
            return null;
        }

        GameObject objectToSpawn;

        if (_poolDictionary[id].Count > 0)
        {
            objectToSpawn = _poolDictionary[id].Dequeue();
        }
        else
        {
            // Expandir el pool si está vacío dinámicamente
            objectToSpawn = Instantiate(_prefabDictionary[id]);
            if (objectToSpawn.TryGetComponent(out IPoolable poolableComponent))
            {
                poolableComponent.PoolId = id;
            }
        }

        objectToSpawn.SetActive(true);
        objectToSpawn.transform.position = position;
        objectToSpawn.transform.rotation = rotation;
        objectToSpawn.transform.SetParent(null);

        if (objectToSpawn.TryGetComponent(out IPoolable poolable))
        {
            poolable.OnSpawn();
        }

        return objectToSpawn;
    }

    public void ReturnToPool(GameObject obj, string id)
    {
        if (!_poolDictionary.ContainsKey(id))
        {
            Debug.LogWarning($"[ObjectPoolManager] Intentando devolver a un pool inexistente: {id}");
            Destroy(obj);
            return;
        }

        if (obj.TryGetComponent(out IPoolable poolable))
        {
            poolable.OnDespawn();
        }

        obj.SetActive(false);
        obj.transform.SetParent(transform);
        _poolDictionary[id].Enqueue(obj);
    }
}
