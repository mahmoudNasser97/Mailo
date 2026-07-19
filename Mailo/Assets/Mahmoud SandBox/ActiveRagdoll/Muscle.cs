using UnityEngine;

/// <summary>
/// Sits on each physical ragdoll bone. Drives its ConfigurableJoint toward the matching
/// animated-rig bone via a strength-scaled slerp drive.
/// Call Init() from ActiveRagdollController.Awake() BEFORE physics runs.
/// </summary>
[RequireComponent(typeof(ConfigurableJoint))]
public class Muscle : MonoBehaviour
{
    [Tooltip("Matching bone Transform on the hidden animated reference rig.")]
    [HideInInspector] public Transform animatedTarget;

    // Captured at Init() — the local rotation when the joint was created.
    [HideInInspector] public Quaternion initialLocalRotation;

    [Range(0f, 1f)]
    [Tooltip("Per-bone strength multiplier. Reduce for intentionally floppy limbs.")]
    public float strengthMultiplier = 1f;

    ConfigurableJoint _joint;
    float _maxSpring;
    float _maxDamper;
    float _currentStrength;
    bool  _locked;

    public ConfigurableJoint Joint => _joint;

    /// <summary>Called once by ActiveRagdollController before any FixedUpdate.</summary>
    public void Init(float maxSpring, float maxDamper)
    {
        _joint              = GetComponent<ConfigurableJoint>();
        _maxSpring          = maxSpring;
        _maxDamper          = maxDamper;
        initialLocalRotation = transform.localRotation;
        ApplyDrive(_maxSpring, _maxDamper);
    }

    /// <summary>Set by the controller every fixed frame. 0 = ragdoll limp, 1 = fully rigid.</summary>
    public void SetStrength(float globalStrength)
    {
        _currentStrength = globalStrength * strengthMultiplier;
        if (!_locked)
            ApplyDrive(_maxSpring * _currentStrength, _maxDamper * _currentStrength);
    }

    /// <summary>
    /// Stance-foot lock: cranks drive to maximum regardless of global muscle strength.
    /// Unlock during swing phase so the foot can lift freely.
    /// </summary>
    public void SetLocked(bool locked)
    {
        _locked = locked;
        ApplyDrive(
            _locked ? _maxSpring * 2f : _maxSpring * _currentStrength,
            _locked ? _maxDamper * 2f : _maxDamper * _currentStrength
        );
    }

    /// <summary>Copy the animated bone's current local rotation into the joint target.</summary>
    public void UpdateTarget()
    {
        if (animatedTarget == null || _joint == null) return;
        _joint.SetTargetRotationLocal(animatedTarget.localRotation, initialLocalRotation);
    }

    void ApplyDrive(float spring, float damper)
    {
        var drive = new JointDrive
        {
            positionSpring = spring,
            positionDamper = damper,
            maximumForce   = float.MaxValue
        };
        _joint.slerpDrive = drive;
    }
}
