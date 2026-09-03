using UnityEngine;

namespace NasserActiveRagdoll
{
    /// <summary>
    /// Deliberately dumb NPC. Wanders, or chases a target, and grabs what it bumps into.
    /// It exists to prove the driver split works -- replace the Think() body with your
    /// behaviour tree or utility AI and nothing else in the package changes.
    /// </summary>
    public class NpcDriver : CharacterDriver
    {
        public enum Behaviour { Idle, Wander, Follow }

        [Header("Behaviour")]
        public Behaviour behaviour = Behaviour.Wander;
        public Transform followTarget;
        public float wanderRadius = 6f;
        public float repathInterval = 3.5f;
        public float stoppingDistance = 1.4f;
        [Range(0f, 1f)] public float moveSpeedScale = 0.7f;

        [Header("Reaction")]
        [Tooltip("Turn to face whatever just hit us.")]
        public bool faceAttacker = true;

        Vector3 _goal;
        float _repathAt;

        void Start()
        {
            _goal = transform.position;
            if (body) body.Hit += OnHit;
        }

        void OnDestroy() { if (body) body.Hit -= OnHit; }

        void OnHit(Impact impact)
        {
            if (!faceAttacker || impact.instigator == null) return;
            Vector3 to = impact.instigator.transform.position - transform.position;
            to.y = 0f;
            if (to.sqrMagnitude > 0.01f) Face(Quaternion.LookRotation(to).eulerAngles.y);
        }

        void FixedUpdate()
        {
            if (!CanAct) { Move(Vector3.zero); return; }
            Think();
        }

        void Think()
        {
            switch (behaviour)
            {
                case Behaviour.Idle:
                    Move(Vector3.zero);
                    return;

                case Behaviour.Follow:
                    if (!followTarget) { Move(Vector3.zero); return; }
                    _goal = followTarget.position;
                    break;

                case Behaviour.Wander:
                    if (Time.time >= _repathAt)
                    {
                        _repathAt = Time.time + repathInterval;
                        Vector2 r = Random.insideUnitCircle * wanderRadius;
                        _goal = transform.position + new Vector3(r.x, 0f, r.y);
                    }
                    break;
            }

            Vector3 to = _goal - Controller.transform.position;
            to.y = 0f;

            if (to.magnitude < stoppingDistance) { Move(Vector3.zero); return; }

            Vector3 dir = to.normalized;
            Move(dir * moveSpeedScale);
            Face(Quaternion.LookRotation(dir).eulerAngles.y);
        }
    }
}
