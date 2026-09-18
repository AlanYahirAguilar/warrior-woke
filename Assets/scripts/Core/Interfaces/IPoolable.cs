using UnityEngine;

/// <summary>
/// Interfaz para objetos que pueden ser manejados por el ObjectPoolManager.
/// </summary>
public interface IPoolable
{
    string PoolId { get; set; }
    void OnSpawn();
    void OnDespawn();
}
