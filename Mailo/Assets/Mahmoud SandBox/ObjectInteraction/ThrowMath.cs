using UnityEngine;

public static class ThrowMath
{
    public static bool TryCalculateVelocity(Vector3 from, Vector3 to, float angleDeg, out Vector3 velocity)
    {
        velocity    = Vector3.zero;
        Vector3 dir = to - from;
        float   h   = dir.y;
        dir.y       = 0f;
        float dist  = dir.magnitude;
        if (dist < 0.01f) return false;

        float angle       = angleDeg * Mathf.Deg2Rad;
        float denominator = dist * Mathf.Sin(2f * angle) - 2f * h * Mathf.Cos(angle) * Mathf.Cos(angle);
        if (denominator <= 0f) return false;

        float speed = Mathf.Sqrt(Physics.gravity.magnitude * dist * dist / denominator);
        velocity    = dir.normalized * speed * Mathf.Cos(angle)
                    + Vector3.up     * speed * Mathf.Sin(angle);
        return true;
    }
}
