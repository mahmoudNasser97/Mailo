using UnityEngine;

/// <summary>
/// MUST live on the same GameObject as the reference rig's Animator.
///
/// SETUP CHECKLIST:
///  1. Reference rig uses a Humanoid avatar (animationType: 3 in the .fbx importer).
///  2. IK Pass is ENABLED on the Animator's Base Layer (Layer 0) — without this
///     OnAnimatorIK never fires and all IK calls silently do nothing.
///  3. Animator.updateMode = AnimatePhysics so IK runs in sync with FixedUpdate.
///
/// GaitController writes the foot data each FixedUpdate; this component delivers
/// it to Unity's built-in foot IK during OnAnimatorIK.
/// </summary>
public class GaitIKDriver : MonoBehaviour
{
    // Written by GaitController every FixedUpdate.
    [HideInInspector] public Vector3    leftFootPos;
    [HideInInspector] public Quaternion leftFootRot  = Quaternion.identity;
    [HideInInspector] public float      leftFootPosW;
    [HideInInspector] public float      leftFootRotW;

    [HideInInspector] public Vector3    rightFootPos;
    [HideInInspector] public Quaternion rightFootRot  = Quaternion.identity;
    [HideInInspector] public float      rightFootPosW;
    [HideInInspector] public float      rightFootRotW;

    Animator _animator;

    void Awake()
    {
        _animator = GetComponent<Animator>();
        if (_animator == null)
            Debug.LogError("[GaitIKDriver] No Animator found on this GameObject. "
                         + "GaitIKDriver MUST be on the same GO as the reference rig Animator.", this);
    }

    void OnAnimatorIK(int layerIndex)
    {
        if (_animator == null) return;

        _animator.SetIKPositionWeight(AvatarIKGoal.LeftFoot,  leftFootPosW);
        _animator.SetIKRotationWeight(AvatarIKGoal.LeftFoot,  leftFootRotW);
        _animator.SetIKPosition(AvatarIKGoal.LeftFoot,        leftFootPos);
        _animator.SetIKRotation(AvatarIKGoal.LeftFoot,        leftFootRot);

        _animator.SetIKPositionWeight(AvatarIKGoal.RightFoot, rightFootPosW);
        _animator.SetIKRotationWeight(AvatarIKGoal.RightFoot, rightFootRotW);
        _animator.SetIKPosition(AvatarIKGoal.RightFoot,       rightFootPos);
        _animator.SetIKRotation(AvatarIKGoal.RightFoot,       rightFootRot);
    }
}
