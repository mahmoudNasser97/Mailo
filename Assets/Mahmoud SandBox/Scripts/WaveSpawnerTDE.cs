using MoreMountains.TopDownEngine;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class WaveSpawnerTDE : MonoBehaviour
{
    [System.Serializable]
    public class Wave
    {
        public GameObject enemyPrefab;
        public int count;
        public float spawnInterval = 0.3f; 
    }

    public List<Wave> waves = new List<Wave>();
    public Transform[] spawnPoints;

    private int _currentWave = 0;
    private int _aliveEnemies = 0;

    private void Start()
    {
        StartWave();
    }

    private void StartWave()
    {
        if (_currentWave >= waves.Count)
        {
            Debug.Log("All waves finished!");
            return;
        }

        StartCoroutine(SpawnWaveCoroutine(waves[_currentWave]));
    }

    private IEnumerator SpawnWaveCoroutine(Wave wave)
    {
        for (int i = 0; i < wave.count; i++)
        {
            Transform spawn = spawnPoints[Random.Range(0, spawnPoints.Length)];

            GameObject obj = Instantiate(wave.enemyPrefab, spawn.position, spawn.rotation);

            var health = obj.GetComponent<Health>();
            health.OnDeath += OnEnemyDeath;

            _aliveEnemies++;

            yield return new WaitForSeconds(wave.spawnInterval);
        }
    }

    private void OnEnemyDeath()
    {
        _aliveEnemies--;

        if (_aliveEnemies <= 0)
        {
            _currentWave++;
            Invoke(nameof(StartWave), 2f);
        }
    }
}
