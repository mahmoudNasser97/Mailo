using System.Collections.Generic;
using UnityEngine;

public class NPCRVOAgent : MonoBehaviour
{
    [SerializeField] public float agentRadius    = 0.5f;
    [SerializeField] float _neighborRadius       = 5f;
    [SerializeField] float _timeHorizon          = 2f;
    [SerializeField] float _maxSpeed             = 5f;

    static readonly List<NPCRVOAgent> s_All = new List<NPCRVOAgent>();

    Vector3 _velocity;

    public Vector3 Velocity => _velocity;
    public Vector3 Position => transform.position;

    void OnEnable()  => s_All.Add(this);
    void OnDisable() => s_All.Remove(this);

    public Vector3 ComputeAvoidanceVelocity(Vector3 desiredVelocity)
    {
        _velocity = desiredVelocity;

        var lines = new List<OrcaLine>();

        foreach (NPCRVOAgent other in s_All)
        {
            if (other == this) continue;

            Vector2 relPos      = XZ(other.Position - Position);
            float   combinedRad = agentRadius + other.agentRadius;
            if (relPos.magnitude > _neighborRadius + combinedRad) continue;

            Vector2 relVel = XZ(desiredVelocity) - XZ(other._velocity);
            float   dist   = relPos.magnitude;

            Vector2 u, n;

            if (dist > combinedRad)
            {
                Vector2 w    = relVel - relPos / _timeHorizon;
                float   wLen = w.magnitude;
                if (wLen < 1e-5f) continue;
                Vector2 unitW = w / wLen;

                if (Vector2.Dot(w, relPos) < combinedRad / _timeHorizon * wLen)
                {
                    u = (combinedRad / _timeHorizon - wLen) * unitW;
                    n = unitW;
                }
                else
                {
                    float leg = Mathf.Sqrt(Mathf.Max(0f, dist * dist - combinedRad * combinedRad));
                    if (Cross2D(relPos, w) > 0f)
                        n = new Vector2(relPos.x * leg - relPos.y * combinedRad,
                                        relPos.x * combinedRad + relPos.y * leg) / (dist * dist);
                    else
                        n = -(new Vector2( relPos.x * leg + relPos.y * combinedRad,
                                          -relPos.x * combinedRad + relPos.y * leg)) / (dist * dist);
                    u = (Vector2.Dot(relVel, n) - Vector2.Dot(relPos / _timeHorizon, n)) * n;
                }
            }
            else
            {
                float   inv  = 1f / Time.deltaTime;
                Vector2 w    = relVel - relPos * inv;
                float   wLen = w.magnitude;
                n = wLen > 1e-5f ? w / wLen : Vector2.up;
                u = (combinedRad * inv - wLen) * n;
            }

            lines.Add(new OrcaLine
            {
                point     = XZ(desiredVelocity) + 0.5f * u,
                direction = new Vector2(-n.y, n.x)
            });
        }

        Vector2 result = LP2(XZ(desiredVelocity), _maxSpeed, lines);
        return new Vector3(result.x, 0f, result.y);
    }

    // ---- LP solver (RVO2 algorithm) ----

    static Vector2 LP2(Vector2 preferred, float maxSpeed, List<OrcaLine> lines)
    {
        Vector2 result = Vector2.ClampMagnitude(preferred, maxSpeed);
        for (int i = 0; i < lines.Count; i++)
        {
            if (Cross2D(lines[i].direction, lines[i].point - result) > 0f)
            {
                if (!LP1(preferred, maxSpeed, lines, i, out Vector2 candidate))
                {
                    result = LP3(preferred, maxSpeed, lines, i);
                    break;
                }
                result = candidate;
            }
        }
        return result;
    }

    static bool LP1(Vector2 preferred, float maxSpeed,
        List<OrcaLine> lines, int lineNo, out Vector2 result)
    {
        OrcaLine line     = lines[lineNo];
        float    dotProd  = Vector2.Dot(line.point, line.direction);
        float    disc     = dotProd * dotProd + maxSpeed * maxSpeed - line.point.sqrMagnitude;

        if (disc < 0f) { result = default; return false; }

        float sqrt   = Mathf.Sqrt(disc);
        float tLeft  = -dotProd - sqrt;
        float tRight = -dotProd + sqrt;

        for (int i = 0; i < lineNo; i++)
        {
            float denom = Cross2D(line.direction, lines[i].direction);
            float num   = Cross2D(lines[i].direction, line.point - lines[i].point);

            if (Mathf.Abs(denom) < 1e-6f)
            {
                if (num < 0f) { result = default; return false; }
                continue;
            }
            float t = num / denom;
            if (denom > 0f) tRight = Mathf.Min(tRight, t);
            else             tLeft  = Mathf.Max(tLeft,  t);
            if (tLeft > tRight) { result = default; return false; }
        }

        float tPref = Vector2.Dot(line.direction, preferred - line.point);
        result = line.point + Mathf.Clamp(tPref, tLeft, tRight) * line.direction;
        return true;
    }

    static Vector2 LP3(Vector2 preferred, float maxSpeed,
        List<OrcaLine> lines, int numLines)
    {
        float   distance = 0f;
        Vector2 result   = Vector2.ClampMagnitude(preferred, maxSpeed);

        for (int i = numLines; i < lines.Count; i++)
        {
            if (Cross2D(lines[i].direction, lines[i].point - result) <= distance) continue;

            var proj = new List<OrcaLine>();
            for (int j = 0; j < i; j++)
            {
                float denom = Cross2D(lines[i].direction, lines[j].direction);
                if (Mathf.Abs(denom) < 1e-6f)
                {
                    if (Vector2.Dot(lines[i].direction, lines[j].direction) > 0f) continue;
                    proj.Add(new OrcaLine
                    {
                        point     = 0.5f * (lines[i].point + lines[j].point),
                        direction = (lines[i].direction + lines[j].direction).normalized
                    });
                    continue;
                }
                float t = Cross2D(lines[j].direction, lines[i].point - lines[j].point) / denom;
                proj.Add(new OrcaLine
                {
                    point     = lines[i].point + t * lines[i].direction,
                    direction = (lines[i].direction - lines[j].direction).normalized
                });
            }
            proj.Add(new OrcaLine
            {
                point     = lines[i].point,
                direction = new Vector2(-lines[i].direction.y, lines[i].direction.x)
            });

            Vector2 preferred3 = new Vector2(-lines[i].direction.y, lines[i].direction.x) * maxSpeed;
            result   = LP2(preferred3, maxSpeed, proj);
            distance = Cross2D(lines[i].direction, lines[i].point - result);
        }
        return result;
    }

    static float   Cross2D(Vector2 a, Vector2 b) => a.x * b.y - a.y * b.x;
    static Vector2 XZ(Vector3 v)                 => new Vector2(v.x, v.z);

    struct OrcaLine { public Vector2 point, direction; }
}
