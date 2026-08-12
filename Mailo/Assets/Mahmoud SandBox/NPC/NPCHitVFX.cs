using System.Collections;
using UnityEngine;

public class NPCHitVFX : MonoBehaviour
{
    [Header("Flash")]
    [SerializeField] Material _hitMaterial;
    [SerializeField] float    _flashDuration = 0.15f;

    [Header("Particles")]
    [SerializeField] GameObject _hitParticlePrefab;

    [Header("Camera Shake")]
    [SerializeField] float _shakeDuration  = 0.2f;
    [SerializeField] float _shakeMagnitude = 0.1f;

    SkinnedMeshRenderer[] _renderers;
    Material[][]          _originalMats;
    Coroutine             _flashRoutine;
    Coroutine             _shakeRoutine;
    Camera                _cam;
    Vector3               _camRestPos;

    void Awake()
    {
        _renderers    = GetComponentsInChildren<SkinnedMeshRenderer>();
        _originalMats = new Material[_renderers.Length][];
        for (int i = 0; i < _renderers.Length; i++)
            _originalMats[i] = _renderers[i].materials;

        _cam = Camera.main;
        if (_cam != null) _camRestPos = _cam.transform.localPosition;
    }

    public void PlayHitEffects(Vector3 hitPoint, Vector3 hitDir)
    {
        Vector3 pos = hitPoint != Vector3.zero ? hitPoint : transform.position + Vector3.up;

        if (_flashRoutine != null) StopCoroutine(_flashRoutine);
        if (_shakeRoutine != null)
        {
            StopCoroutine(_shakeRoutine);
            if (_cam != null) _cam.transform.localPosition = _camRestPos;
        }
        _flashRoutine = StartCoroutine(FlashRoutine());
        _shakeRoutine = StartCoroutine(ShakeRoutine());
        SpawnParticle(pos, hitDir);
    }

    void SpawnParticle(Vector3 pos, Vector3 dir)
    {
        if (_hitParticlePrefab == null) return;

        Quaternion rot = dir.sqrMagnitude > 0.01f
            ? Quaternion.LookRotation(dir)
            : Quaternion.identity;

        GameObject fx = Instantiate(_hitParticlePrefab, pos, rot);
        ParticleSystem ps = fx.GetComponent<ParticleSystem>();
        if (ps != null)
        {
            ps.Play();
            Destroy(fx, ps.main.duration + ps.main.startLifetime.constantMax + 0.5f);
        }
        else Destroy(fx, 3f);
    }

    IEnumerator FlashRoutine()
    {
        if (_hitMaterial == null) yield break;

        for (int i = 0; i < _renderers.Length; i++)
        {
            var flash = new Material[_renderers[i].materials.Length];
            System.Array.Fill(flash, _hitMaterial);
            _renderers[i].materials = flash;
        }

        yield return new WaitForSeconds(_flashDuration);

        for (int i = 0; i < _renderers.Length; i++)
            _renderers[i].materials = _originalMats[i];

        _flashRoutine = null;
    }

    IEnumerator ShakeRoutine()
    {
        if (_cam == null) yield break;

        float elapsed = 0f;
        while (elapsed < _shakeDuration)
        {
            float x = (Mathf.PerlinNoise(elapsed * 10f, 0f) * 2f - 1f) * _shakeMagnitude;
            float y = (Mathf.PerlinNoise(0f, elapsed * 10f) * 2f - 1f) * _shakeMagnitude;
            _cam.transform.localPosition = _camRestPos + new Vector3(x, y, 0f);
            elapsed += Time.deltaTime;
            yield return null;
        }
        _cam.transform.localPosition = _camRestPos;
        _shakeRoutine = null;
    }
}
