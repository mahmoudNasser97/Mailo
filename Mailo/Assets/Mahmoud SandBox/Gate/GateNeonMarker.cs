using UnityEngine;

public class GateNeonMarker : MonoBehaviour
{
    [SerializeField] Renderer _renderer;
    [SerializeField] Color    _emissionColor  = Color.cyan;
    [SerializeField] float    _idlePulseSpeed = 2f;
    [SerializeField] float    _idlePulseMin   = 0.3f;
    [SerializeField] float    _idlePulseMax   = 1.0f;
    [SerializeField] float    _maxScale       = 2f;

    static readonly int _emissionPropId = Shader.PropertyToID("_EmissionColor");

    Vector3 _baseScale;
    bool    _progressMode;
    Material _mat;

    void Awake()
    {
        _baseScale = transform.localScale;
        if (_renderer != null)
        {
            _renderer.material.EnableKeyword("_EMISSION");
            _mat = _renderer.material;
        }
    }

    void Update()
    {
        if (_renderer == null || _progressMode) return;
        float t         = (Mathf.Sin(Time.time * _idlePulseSpeed * Mathf.PI * 2f) + 1f) * 0.5f;
        float intensity = Mathf.Lerp(_idlePulseMin, _idlePulseMax, t);
        _mat.SetColor(_emissionPropId, _emissionColor * intensity);
    }

    public void SetProgress(float t)
    {
        t             = Mathf.Clamp01(t);
        _progressMode = t > 0f;
        transform.localScale = Vector3.Lerp(_baseScale, _baseScale * _maxScale, t);
        if (_renderer != null)
        {
            float intensity = Mathf.Lerp(_idlePulseMin, _idlePulseMax * 2f, t);
            _mat.SetColor(_emissionPropId, _emissionColor * intensity);
        }
    }
}
