using System.Collections;
using UnityEngine;

// Place on the Player root alongside HitReactor.
// Routes HitReactor.OnImpact to PhysicsCharacterController.ForceRagdoll (knockdown)
// or a flinch animation (light hit).
public class PlayerHitReaction : MonoBehaviour
{
    [SerializeField] float  _flinchImpulse    = 20f;   // minimum impulse to play flinch
    [SerializeField] float  _knockdownImpulse = 80f;   // impulse that triggers full ragdoll
    [SerializeField] float  _hitRecoverTime   = 0.5f;
    [SerializeField] string _hitReactStateName = "HitReact";
    [SerializeField] float  _crossFadeDuration = 0.1f;

    static readonly int _hitReactHash = Animator.StringToHash("HitReact");

    HitReactor                 _hitReactor;
    PhysicsCharacterController _physics;
    Animator                   _animator;
    bool                       _inHitReact;

    void Awake()
    {
        _hitReactor = GetComponent<HitReactor>();
        if (_hitReactor == null)
        {
            _hitReactor = gameObject.AddComponent<HitReactor>();
        }

        _physics = GetComponent<PhysicsCharacterController>()
                ?? GetComponentInParent<PhysicsCharacterController>()
                ?? GetComponentInChildren<PhysicsCharacterController>();

        _animator = GetComponentInChildren<Animator>();
    }

    void OnEnable()  { if (_hitReactor != null) _hitReactor.OnImpact += HandleImpact; }
    void OnDisable() { if (_hitReactor != null) _hitReactor.OnImpact -= HandleImpact; }

    void HandleImpact(float impulse, Vector3 direction, Vector3 hitPoint)
    {
        if (impulse < _flinchImpulse) return;

        // Heavy hit — trigger full ragdoll fall
        if (impulse >= _knockdownImpulse && _physics != null)
        {
            _physics.ForceRagdoll();
            return;
        }

        // Light hit — flinch animation only
        if (_inHitReact) return;
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
