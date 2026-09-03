using UnityEngine;

namespace NasserActiveRagdoll
{
    /// <summary>
    /// First-person camera for a body that can fall over, plus the hand reach targets.
    ///
    /// The camera rides the HEAD BONE at all times -- standing and ragdolled -- so a
    /// knockdown reads as your own head hitting the floor. Look input never touches the
    /// camera transform directly; it drives a yaw/pitch pair that the capsule and the
    /// hand targets both read from. That is what keeps aiming stable while your actual
    /// skull is bouncing off a seat.
    ///
    /// Only players get one of these. NPCs have no rig at all -- their hands are driven
    /// by targets parented to the chest instead.
    /// </summary>
    [DefaultExecutionOrder(200)]
    public class FirstPersonRig : MonoBehaviour
    {
        [Header("References")]
        public Transform headBone;
        public FloatingCapsuleController controller;
        public CharacterBody body;
        public Camera cam;

        [Header("Camera")]
        public Vector3 headOffset = new Vector3(0f, 0.1f, 0.09f);
        public float positionLerp = 25f;
        public float lookSensitivity = 2f;
        public float minPitch = -80f, maxPitch = 80f;

        [Tooltip("How much the camera inherits head tilt while standing. 0 = always level.")]
        [Range(0f, 1f)] public float standingTiltFollow = 0.15f;
        [Tooltip("Tilt inheritance while down. High is funny and also nauseating -- test it on someone else.")]
        [Range(0f, 1f)] public float ragdolledTiltFollow = 0.9f;

        [Header("Body turn")]
        [Tooltip("The body follows the CAMERA yaw: moving the mouse rotates the character, so the camera " +
                 "steers and all locomotion is forward-facing (no strafe). This is how fast the body yaw " +
                 "catches up to the camera — high feels tight, low feels heavy.")]
        public float bodyTurnLerp = 14f;

        bool _yawInit;

        /// <summary>Keyboard turn (A/D): rotates the view (and therefore the body) without strafing.</summary>
        public void AddYaw(float degrees) => Yaw += degrees;

        [Header("Hand targets")]
        [Tooltip("Moved in camera space. The hands are spring-dragged toward these.")]
        public Transform leftHandTarget;
        public Transform rightHandTarget;
        public Vector3 leftRestLocal = new Vector3(-0.32f, -0.32f, 0.52f);
        public Vector3 rightRestLocal = new Vector3(0.32f, -0.32f, 0.52f);
        [Tooltip("Hands follow pitch this much. 1 = fully aim-linked, 0 = always level.")]
        [Range(0f, 1f)] public float handPitchFollow = 0.75f;

        public float Yaw { get; private set; }
        public float Pitch { get; private set; }
        public Vector3 FlatForward => Quaternion.Euler(0f, Yaw, 0f) * Vector3.forward;

        void Reset() => cam = GetComponentInChildren<Camera>();

        public void AddLook(Vector2 delta)
        {
            // Mouse drives the camera yaw/pitch. UpdateBodyYaw then turns the whole body to match the
            // yaw, so moving the mouse rotates the character -- the camera steers.
            Yaw += delta.x * lookSensitivity;
            Pitch = Mathf.Clamp(Pitch - delta.y * lookSensitivity, minPitch, maxPitch);
        }

        /// <summary>
        /// The body follows the camera yaw, so turning the view turns the character. All locomotion is
        /// therefore forward-facing (no strafe); the driver moves forward/back and turns with mouse or A/D.
        /// </summary>
        void UpdateBodyYaw()
        {
            if (!controller) return;
            if (!_yawInit)
            {
                Yaw = controller.transform.eulerAngles.y;
                _yawInit = true;
            }

            // The body faces the camera DIRECTLY -- no extra lerp here, so it does not trail the mouse.
            // The controller's own yawLerp is the only smoothing (raise it there for an even tighter turn).
            controller.SetYaw(Yaw);
        }

        void LateUpdate()
        {
            if (!headBone) return;

            UpdateBodyYaw();

            Vector3 want = headBone.TransformPoint(headOffset);
            transform.position = Vector3.Lerp(transform.position, want, positionLerp * Time.deltaTime);

            bool down = body && body.Current != CharacterBody.State.Standing;
            float follow = down ? ragdolledTiltFollow : standingTiltFollow;

            Quaternion look = Quaternion.Euler(Pitch, Yaw, 0f);
            transform.rotation = Quaternion.Slerp(look, headBone.rotation, follow);

            PlaceHandTargets();
        }

        void PlaceHandTargets()
        {
            // Anchored to the look direction, not the camera transform -- otherwise your
            // hands inherit every ragdoll head wobble and the game is unplayable.
            Quaternion aim = Quaternion.Euler(Pitch * handPitchFollow, Yaw, 0f);
            Vector3 origin = transform.position;

            if (leftHandTarget)
            {
                leftHandTarget.SetPositionAndRotation(origin + aim * leftRestLocal, aim);
            }
            if (rightHandTarget)
            {
                rightHandTarget.SetPositionAndRotation(origin + aim * rightRestLocal, aim);
            }
        }

        public void Apply(RagdollProfile p)
        {
            if (!p) return;
            lookSensitivity = p.lookSensitivity;
            standingTiltFollow = p.standingTiltFollow;
            ragdolledTiltFollow = p.ragdolledTiltFollow;
        }
    }
}
