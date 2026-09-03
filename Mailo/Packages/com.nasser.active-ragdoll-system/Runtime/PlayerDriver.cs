using UnityEngine;

namespace NasserActiveRagdoll
{
    /// <summary>
    /// Local player. Reads the legacy Input axes so the package has no dependency on
    /// the Input System package -- swap the three Read* methods if you use it.
    /// </summary>
    public class PlayerDriver : CharacterDriver
    {
        [Header("Rig")]
        public FirstPersonRig rig;

        [Header("Bindings")]
        public string horizontalAxis = "Horizontal";
        public string verticalAxis = "Vertical";
        public string mouseX = "Mouse X";
        public string mouseY = "Mouse Y";
        public KeyCode jumpKey = KeyCode.Space;
        public KeyCode grabLeftKey = KeyCode.Mouse1;
        public KeyCode grabRightKey = KeyCode.Mouse0;
        [Tooltip("Held while releasing to throw instead of drop.")]
        public KeyCode throwModifier = KeyCode.None;

        [Header("Cursor")]
        public bool lockCursor = true;

        [Header("Steering")]
        [Tooltip("A/D turn the view (and the body) at this many degrees/second, instead of strafing. " +
                 "The mouse also turns. W/S move forward/back only, so there is never any strafe.")]
        public float keyboardTurnSpeed = 130f;
        [Tooltip("Backward (S) top speed as a fraction of forward, so back-pedalling matches the slower " +
                 "back clip instead of skating.")]
        [Range(0.2f, 1f)] public float backwardSpeedFactor = 0.6f;

        void Start()
        {
            if (lockCursor) { Cursor.lockState = CursorLockMode.Locked; Cursor.visible = false; }
        }

        void Update()
        {
            if (rig) rig.AddLook(new Vector2(Input.GetAxisRaw(mouseX), Input.GetAxisRaw(mouseY)));

            HandleHand(body.rightHand, grabRightKey);
            HandleHand(body.leftHand, grabLeftKey);

            if (CanAct && Input.GetKeyDown(jumpKey)) Jump();
        }

        void FixedUpdate()
        {
            if (!CanAct) { Move(Vector3.zero); return; }

            float h = Input.GetAxisRaw(horizontalAxis);
            float v = Input.GetAxisRaw(verticalAxis);

            // A/D turn the view (which turns the body); the mouse does the same. This replaces strafe.
            if (rig && Mathf.Abs(h) > 0.01f)
                rig.AddYaw(h * keyboardTurnSpeed * Time.fixedDeltaTime);

            // Move forward/back only, along the body's facing (which equals the camera). No lateral
            // component means there is never a strafe -- turning the camera is how you change heading.
            float yaw = rig ? rig.Yaw : transform.eulerAngles.y;
            float fwd = v * (v < 0f ? backwardSpeedFactor : 1f);
            Vector3 dir = Quaternion.Euler(0f, yaw, 0f) * new Vector3(0f, 0f, fwd);
            Move(dir);
        }

        void HandleHand(PhysicsHand hand, KeyCode key)
        {
            if (!hand) return;

            // Hold to reach: the arm raises toward its target while the button is down and hangs
            // (following the animation) when released -- so the character stands naturally instead
            // of permanently reaching. Keep trying to grab while reaching, so it grabs on contact
            // once the hand arrives, not only on the first frame.
            bool holding = CanAct && Input.GetKey(key);
            hand.SetReaching(holding);
            if (holding && hand.Held == null) hand.TryGrab();

            if (Input.GetKeyUp(key))
            {
                bool throwIt = throwModifier == KeyCode.None || Input.GetKey(throwModifier);
                hand.Release(throwIt);
            }
        }
    }
}
