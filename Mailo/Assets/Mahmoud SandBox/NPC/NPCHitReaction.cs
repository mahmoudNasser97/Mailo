using System.Collections;
using UnityEngine;

[RequireComponent(typeof(NPCBrain))]
public class NPCHitReaction : MonoBehaviour
{
    [SerializeField] float _hitThreshold       = 20f;
    [SerializeField] float _knockdownThreshold = 80f;
    [SerializeField] float _hitRecoverTime     = 0.8f;

    static readonly int _hitReactHash = Animator.StringToHash("HitReact");

    NPCBrain  _brain;
    Animator  _animator;
    HitReactor _hitReactor;

    void Awake()
    {
        _brain    = GetComponent<NPCBrain>();
        _animator = GetComponent<Animator>();

        _hitReactor = GetComponentInParent<HitReactor>();
        if (_hitReactor == null)
            _hitReactor = transform.root.GetComponentInChildren<HitReactor>();
    }

    void OnEnable()
    {
        if (_hitReactor != null) _hitReactor.OnImpact += HandleImpact;
    }

    void OnDisable()
    {
        if (_hitReactor != null) _hitReactor.OnImpact -= HandleImpact;
        if (_brain != null && _brain.State == NPCState.HitReact)
            _brain.RecoverFromHit();
    }

    void HandleImpact(float impulse, Vector3 direction, Vector3 hitPoint)
    {
        if (impulse < _hitThreshold)       return; // too small to react
        if (impulse >= _knockdownThreshold) return; // ragdoll handles heavy hits
        if (_brain.State == NPCState.HitReact) return; // already in hit react

        _brain.ReportHit(impulse, hitPoint, direction);
        _animator.SetTrigger(_hitReactHash);
        StartCoroutine(RecoverAfter(_hitRecoverTime));
    }

    IEnumerator RecoverAfter(float delay)
    {
        yield return new WaitForSeconds(delay);
        _brain.RecoverFromHit();
    }
}
