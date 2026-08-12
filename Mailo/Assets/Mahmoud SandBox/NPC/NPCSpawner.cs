using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[System.Serializable]
public class WaveConfig
{
    public int        npcCount           = 3;
    public GameObject npcPrefab;
    public float      delayBetweenSpawns = 0.5f;
    public float      delayAfterWave     = 5f;
}

public class NPCSpawner : MonoBehaviour
{
    [Header("Waves")]
    [SerializeField] WaveConfig[] _waves;
    [SerializeField] bool         _autoAdvanceWaves        = true;
    [SerializeField] bool         _triggerNextWaveOnAllDead = false;

    [Header("Spawn Locations")]
    [SerializeField] Transform[] _spawnPoints;
    [SerializeField] float       _spawnRadius     = 10f;
    [SerializeField] float       _spawnSeparation = 2f;

    readonly List<GameObject> _living = new List<GameObject>();

    void Start() => StartWaves();

    public void StartWaves() => StartCoroutine(RunWaves());

    public void SpawnWave(int index)
    {
        if (index >= 0 && index < _waves.Length)
            StartCoroutine(SpawnWaveCoroutine(_waves[index]));
    }

    public int GetLivingNPCCount()
    {
        _living.RemoveAll(n => n == null);
        return _living.Count;
    }

    IEnumerator RunWaves()
    {
        for (int i = 0; i < _waves.Length; i++)
        {
            yield return StartCoroutine(SpawnWaveCoroutine(_waves[i]));

            if (_triggerNextWaveOnAllDead)
                yield return new WaitUntil(() => GetLivingNPCCount() == 0);

            if (_autoAdvanceWaves && i < _waves.Length - 1)
                yield return new WaitForSeconds(_waves[i].delayAfterWave);
        }
    }

    IEnumerator SpawnWaveCoroutine(WaveConfig wave)
    {
        if (wave.npcPrefab == null) yield break;

        var used = new List<Vector3>();

        for (int i = 0; i < wave.npcCount; i++)
        {
            Vector3    pos = ChooseSpawnPosition(used);
            used.Add(pos);
            GameObject npc = Instantiate(wave.npcPrefab, pos, Quaternion.Euler(0f, Random.Range(0f, 360f), 0f));
            _living.Add(npc);
            yield return new WaitForSeconds(wave.delayBetweenSpawns);
        }
    }

    Vector3 ChooseSpawnPosition(List<Vector3> used)
    {
        if (_spawnPoints != null && _spawnPoints.Length > 0)
        {
            foreach (Transform sp in _spawnPoints)
            {
                if (sp == null) continue;
                if (IsClear(sp.position, used)) return sp.position;
            }
        }

        for (int attempt = 0; attempt < 30; attempt++)
        {
            Vector2 rand = Random.insideUnitCircle * _spawnRadius;
            Vector3 pos  = transform.position + new Vector3(rand.x, 0f, rand.y);
            if (IsClear(pos, used)) return pos;
        }

        Vector2 fallback = Random.insideUnitCircle * _spawnRadius;
        return transform.position + new Vector3(fallback.x, 0f, fallback.y);
    }

    bool IsClear(Vector3 pos, List<Vector3> used)
    {
        foreach (Vector3 u in used)
            if (Vector3.Distance(pos, u) < _spawnSeparation)
                return false;
        return true;
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(transform.position, _spawnRadius);

        if (_spawnPoints == null) return;
        Gizmos.color = Color.blue;
        foreach (Transform sp in _spawnPoints)
            if (sp != null) Gizmos.DrawSphere(sp.position, 0.3f);
    }
}
