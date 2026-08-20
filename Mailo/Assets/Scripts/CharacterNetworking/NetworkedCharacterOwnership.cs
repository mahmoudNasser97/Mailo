using FishNet.Object;
using RootMotion.Demos;
using UnityEngine;

// Bridges a networked character prefab variant to the existing (untouched) single-player
// control/camera scripts: enables the full stack only for the owning client, and hands
// locomotion authority to NetworkTransform (by disabling movement + making the Rigidbody
// kinematic) on every other client's copy. Deliberately lives outside any .asmdef, in the
// same compile tier as the Mahmoud SandBox/RootMotion scripts it references, so it can wire
// to them directly without moving, wrapping, or modifying a single one of those files.
public class NetworkedCharacterOwnership : NetworkBehaviour
{
    [Header("Camera")]
    [SerializeField] private CameraController _cameraController;
    [SerializeField] private GameObject _characterCameraObject;

    [Header("Movement")]
    [SerializeField] private UserControlThirdPerson _userControl;
    [SerializeField] private CharacterPuppet _characterPuppet;
    [SerializeField] private Rigidbody _characterRigidbody;

    [Header("Animation")]
    // CharacterAnimationThirdPerson runs its own independent Update() that reads
    // CharacterPuppet.animState and writes Animator parameters every frame, regardless of
    // anything else's enabled state. Left enabled on a non-owner, it keeps re-applying the
    // now-frozen animState from the disabled CharacterPuppet, fighting NetworkAnimator's
    // correct incoming values every tick - this is what was breaking remote animation sync.
    [SerializeField] private CharacterAnimationThirdPerson _characterAnimation;

    [Header("Interactions")]
    [SerializeField] private GrappleController _grappleController;
    [SerializeField] private ObjectGrabController _objectGrabController;
    [SerializeField] private GatePullController _gatePullController;
    [SerializeField] private SeesawParticipant _seesawParticipant;

    public override void OnStartClient()
    {
        base.OnStartClient();

        bool owner = IsOwner;

        if (_cameraController != null)
            _cameraController.enabled = owner;

        // Only ever switched on for the owner - stays at its prefab-default inactive state
        // for everyone else, never explicitly forced off here.
        if (owner && _characterCameraObject != null)
            _characterCameraObject.SetActive(true);

        if (_userControl != null)
            _userControl.enabled = owner;

        if (_characterPuppet != null)
            _characterPuppet.enabled = owner;

        if (_characterAnimation != null)
            _characterAnimation.enabled = owner;

        // Non-owner instances hand full movement authority to the incoming NetworkTransform
        // data; leaving the Rigidbody non-kinematic here would fight those writes every tick.
        if (_characterRigidbody != null)
            _characterRigidbody.isKinematic = !owner;

        if (_grappleController != null)
            _grappleController.enabled = owner;

        if (_objectGrabController != null)
            _objectGrabController.enabled = owner;

        if (_gatePullController != null)
            _gatePullController.enabled = owner;

        if (_seesawParticipant != null)
            _seesawParticipant.enabled = owner;
    }
}
