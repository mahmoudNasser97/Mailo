using UnityEngine;

/// <summary>
/// Applies Perlin-noise wind as a continuous force to a Rigidbody.
/// Attach to any root GameObject that has a Rigidbody.
///
/// F2 = off  |  F3 = medium  |  F4 = strong
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public class WindForce : MonoBehaviour
{
    [Header("Wind Direction")]
    public Vector3 windDirection = Vector3.right;

    [Header("Wind Strength (tune in Inspector)")]
    [Min(0f)] public float baseStrength = 0f;
    [Min(0f)] public float gustStrength = 0f;

    [Header("Noise")]
    public float noiseSpeed = 0.4f;

    Rigidbody _rb;
    float     _noiseOffset;

    void Awake()
    {
        _rb          = GetComponent<Rigidbody>();
        _noiseOffset = Random.Range(0f, 100f);
    }

    void FixedUpdate()
    {
        if (baseStrength <= 0f && gustStrength <= 0f) return;

        float ng = Mathf.PerlinNoise(_noiseOffset * 1.3f, Time.time * noiseSpeed * 0.7f);
        float nx = Mathf.PerlinNoise(_noiseOffset + Time.time * noiseSpeed, 0f) - 0.5f;
        float nz = Mathf.PerlinNoise(0f, _noiseOffset + Time.time * noiseSpeed) - 0.5f;

        Vector3 dir   = (windDirection + new Vector3(nx, 0f, nz) * 0.3f).normalized;
        Vector3 force = dir * (baseStrength + ng * gustStrength);

        _rb.AddForce(force, ForceMode.Acceleration);
    }

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.F2)) { baseStrength = 0f;  gustStrength = 0f;  }
        if (Input.GetKeyDown(KeyCode.F3)) { baseStrength = 3f;  gustStrength = 2f;  }
        if (Input.GetKeyDown(KeyCode.F4)) { baseStrength = 8f;  gustStrength = 5f;  }
    }
}
