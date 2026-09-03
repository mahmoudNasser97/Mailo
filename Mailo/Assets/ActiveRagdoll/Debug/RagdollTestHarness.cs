using UnityEngine;

namespace ActiveRagdoll
{
    /// <summary>
    /// The manual test rig for every phase (spec §Phase 0 test scene + §6 slow-mo).
    /// Spawns crates on the character, fires projectiles at it from the camera, and
    /// toggles slow motion. Projectiles get continuous collision detection so a light
    /// fast ball can't tunnel and produce the "launched into orbit" explosion (spec §7).
    ///
    /// Drop this on an empty GameObject in the test scene. It finds the RagdollRig
    /// automatically once you build a character with the setup wizard.
    /// </summary>
    public class RagdollTestHarness : MonoBehaviour
    {
        [Header("Target (auto-found if empty)")]
        public RagdollRig target;

        [Header("Keys")]
        public KeyCode spawnCrateKey = KeyCode.C;
        public KeyCode fireChestKey = KeyCode.F; // chest-height shot
        public KeyCode fireShinKey = KeyCode.G;  // low shot → backward takedown (spin)
        public KeyCode fireHeadKey = KeyCode.H;  // head-height shot
        public KeyCode grabCrateKey = KeyCode.B; // spawn a small crate in the right hand (then E to grab)
        public KeyCode slowMoKey = KeyCode.T;

        [Header("Crate (dropped above the character)")]
        public float crateMass = 15f;
        public Vector3 crateSize = new Vector3(0.4f, 0.4f, 0.4f);
        public float crateDropHeight = 2.5f;

        [Header("Projectile (fired from the camera at the CoM)")]
        public float projectileMass = 3f;
        public float projectileRadius = 0.15f;
        public float projectileSpeed = 14f;

        [Header("Slow motion")]
        [Range(0.02f, 1f)] public float slowMoScale = 0.15f;

        float _lifetime = 8f;
        bool _slow;

        void Start()
        {
            if (target == null) target = FindFirstObjectByType<RagdollRig>();
        }

        void Update()
        {
            if (Input.GetKeyDown(spawnCrateKey)) SpawnCrate();
            if (Input.GetKeyDown(fireChestKey)) FireProjectile(0.6f);
            if (Input.GetKeyDown(fireShinKey)) FireProjectile(0.18f);
            if (Input.GetKeyDown(fireHeadKey)) FireProjectile(1f);
            if (Input.GetKeyDown(grabCrateKey)) SpawnCrateInHand();
            if (Input.GetKeyDown(slowMoKey)) ToggleSlowMo();
        }

        void SpawnCrateInHand()
        {
            if (target == null || !target.TryGetBone(BodyPart.HandR, out var h) || h.physical == null) return;
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = "GrabCrate";
            go.transform.position = h.physical.position;
            go.transform.localScale = Vector3.one * 0.18f;
            Colorize(go, new Color(0.3f, 0.6f, 0.8f));
            var rb = go.AddComponent<Rigidbody>();
            rb.mass = 4f;
            Destroy(go, 30f);
        }

        Vector3 AimPoint() => AimAt(0.6f);

        // Point on the character at height fraction (0 = feet, 1 = head).
        Vector3 AimAt(float frac)
        {
            if (target == null) return transform.position;
            Vector3 com = target.TotalMass > 0f ? target.CenterOfMass : target.transform.position + Vector3.up;
            float lowY = com.y, highY = com.y;
            if (target.TryGetBone(BodyPart.FootL, out var f) && f.physical != null) lowY = f.physical.position.y;
            if (target.TryGetBone(BodyPart.Head, out var h) && h.physical != null) highY = h.physical.position.y;
            return new Vector3(com.x, Mathf.Lerp(lowY, highY, frac), com.z);
        }

        void SpawnCrate()
        {
            Vector3 at = AimPoint() + Vector3.up * crateDropHeight;
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = "TestCrate";
            go.transform.position = at;
            go.transform.localScale = crateSize;
            Colorize(go, new Color(0.75f, 0.55f, 0.25f));
            var rb = go.AddComponent<Rigidbody>();
            rb.mass = crateMass;
            Destroy(go, _lifetime * 2f);
        }

        void FireProjectile(float heightFrac)
        {
            Camera cam = Camera.main;
            Vector3 aim = AimAt(heightFrac);
            Vector3 from = cam != null ? cam.transform.position : aim + Vector3.up * 2f + Vector3.back * 4f;
            Vector3 dir = (aim - from).normalized;

            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = "TestProjectile";
            go.transform.position = from + dir * 0.5f;
            go.transform.localScale = Vector3.one * (projectileRadius * 2f);
            Colorize(go, new Color(0.9f, 0.25f, 0.2f));

            var rb = go.AddComponent<Rigidbody>();
            rb.mass = projectileMass;
            // CCD on the projectile only — never on the character bones (spec §7).
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            rb.linearVelocity = dir * projectileSpeed;

            Destroy(go, _lifetime);
        }

        void ToggleSlowMo()
        {
            _slow = !_slow;
            Time.timeScale = _slow ? slowMoScale : 1f;
        }

        static void Colorize(GameObject go, Color color)
        {
            var r = go.GetComponent<Renderer>();
            if (r == null) return;
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) return; // leave default material rather than guess
            var mat = new Material(shader);
            mat.SetColor("_BaseColor", color);
            r.sharedMaterial = mat;
        }

        void OnGUI()
        {
            const int h = 22;
            var r = new Rect(10, Screen.height - h - 8, 1100, h);
            GUI.Label(r, $"[{spawnCrateKey}] drop crate   [{fireChestKey}] chest [{fireShinKey}] shin [{fireHeadKey}] head   " +
                         $"[{grabCrateKey}] crate-in-hand [E] grab   [{slowMoKey}] slow-mo ({(_slow ? "on" : "off")})   [F1] panel");
        }
    }
}
