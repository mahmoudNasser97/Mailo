using System.Collections;
using UnityEngine;

// Add this to the Player root alongside HitReactor.
// Plays a flinch animation on light hits; heavy hits let PuppetRagdollController handle knockdown.
public class PlayerHitReaction : MonoBehaviour
{
    [SerializeField] float  _hitThreshold       = 20f;
    [SerializeField] float  _knockdownThreshold = 80f;
    [SerializeField] float  _hitRecoverTime     = 0.5f;
    [SerializeField] string _hitReactStateName  = "HitReact";
    [SerializeField] float  _crossFadeDuration  = 0.1f;

    static readonly int _hitReactHash = Animator.StringToHash("HitReact");

    HitReactor _hitReactor;
    Animator   _animator;
    bool       _inHitReact;

    void Awake()
    {
        _hitReactor = GetComponent<HitReactor>();
        if (_hitReactor == null)
            _hitReactor = GetComponentInChildren<HitReactor>();

        _animator = GetComponentInChildren<Animator>();
    }

    void OnEnable()
    {
        if (_hitReactor != null) _hitReactor.OnImpact += HandleImpact;
    }

    void OnDisable()
    {
        if (_hitReactor != null) _hitReactor.OnImpact -= HandleImpact;
    }

    void HandleImpact(float impulse, Vector3 direction, Vector3 hitPoint)
    {
        if (impulse < _hitThreshold)        return; // too weak to react
        if (impulse >= _knockdownThreshold)  return; // ragdoll handles heavy hits
        if (_inHitReact)                     return; // already reacting

        if (_animator != null)
        {
            if (_animator.HasState(0, _hitReactHash))
                _animator.CrossFade(_hitReactStateName, _crossFadeDuration);
            else
                _animator.SetTrigger(_hitReactHash);
        }

        StartCoroutine(RecoverAfter(_hitRecoverTime));
    }

    IEnumerator RecoverAfter(float delay)
    {
        _inHitReact = true;
        yield return new WaitForSeconds(delay);
        _inHitReact = false;
    }
}
