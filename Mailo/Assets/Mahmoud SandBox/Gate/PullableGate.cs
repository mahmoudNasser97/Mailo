using UnityEngine;

public class PullableGate : MonoBehaviour
{
    [Header("References")]
    [SerializeField] GateNeonMarker _marker;

    [Header("Tuning")]
    [SerializeField] int   _pressesRequired = 10;
    [SerializeField] float _openAngle       = -90f;
    [SerializeField] float _snapBackSpeed   = 45f;
    [SerializeField] float _pullLerpSpeed   = 8f;

    int   _currentPresses;
    float _currentAngle;
    bool  _isPulling;
    bool  _isLocked;

    public bool      IsFullyOpen     => _isLocked;
    public Transform MarkerTransform => _marker != null ? _marker.transform : transform;

    void Update()
    {
        if (_isLocked) return;

        float t = _pressesRequired > 0 ? _currentPresses / (float)_pressesRequired : 0f;

        float targetAngle = _isPulling
            ? t * _openAngle
            : 0f;

        _currentAngle = _isPulling
            ? Mathf.Lerp(_currentAngle, targetAngle, _pullLerpSpeed * Time.deltaTime)
            : Mathf.MoveTowards(_currentAngle, 0f, _snapBackSpeed * Time.deltaTime);

        transform.localRotation = Quaternion.Euler(_currentAngle, 0f, 0f);

        _marker?.SetProgress(t);
    }

    public void StartPull()
    {
        _isPulling = true;
    }

    public void StopPull()
    {
        _isPulling      = false;
        _currentPresses = 0;
    }

    public void RegisterPress()
    {
        if (_isLocked) return;
        _currentPresses = Mathf.Min(_currentPresses + 1, _pressesRequired);
        if (_currentPresses >= _pressesRequired)
            Lock();
    }

    void Lock()
    {
        _isLocked             = true;
        _currentAngle         = _openAngle;
        transform.localRotation = Quaternion.Euler(_openAngle, 0f, 0f);
        _marker?.SetProgress(1f);
        if (_marker != null)
            _marker.gameObject.SetActive(false);
    }
}
