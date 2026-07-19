using UnityEngine;
using RootMotion.Demos;

[RequireComponent(typeof(CharacterController))]
public class GrappleController : MonoBehaviour
{
    [Header("Rope Visual")]
    [SerializeField] Transform _ropeOrigin;
    [SerializeField] float     _ropeWidth = 0.05f;
    [SerializeField] Color     _ropeColor = Color.gray;

    [Header("Pull Settings")]
    [SerializeField] float _maxGrappleRange    = 15f;
    [SerializeField] float _springAcceleration = 20f;
    [SerializeField] float _maxPullSpeed       = 12f;
    [SerializeField] float _detachDistance     = 1.5f;
    [SerializeField] float _pullGravity        = 9.8f;

    CharacterController    _cc;
    UserControlThirdPerson _userControl;
    CharacterThirdPerson   _charMotor;
    LineRenderer           _rope;

    bool    _grappling;
    Transform _anchor;
    Vector3 _grappleVelocity;

    void Awake()
    {
        _cc          = GetComponent<CharacterController>();
        _userControl = GetComponent<UserControlThirdPerson>();
        _charMotor   = GetComponent<CharacterThirdPerson>();

        _rope                = gameObject.AddComponent<LineRenderer>();
        _rope.positionCount  = 2;
        _rope.startWidth     = _ropeWidth;
        _rope.endWidth       = _ropeWidth;
        _rope.material       = new Material(Shader.Find("Sprites/Default"));
        _rope.startColor     = _ropeColor;
        _rope.endColor       = _ropeColor;
        _rope.enabled        = false;
    }

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.E))
        {
            if (_grappling) Detach();
            else            TryAttach();
        }

        if (_grappling && Input.GetKeyDown(KeyCode.Space))
            Detach();

        if (_grappling)
            Pull();
    }

    void TryAttach()
    {
        GrappleAnchor nearest = FindNearest();
        if (nearest == null) return;

        _anchor          = nearest.transform;
        _grappleVelocity = Vector3.zero;
        _grappling       = true;
        _rope.enabled    = true;

        if (_userControl != null) _userControl.enabled = false;
        if (_charMotor   != null) _charMotor.enabled   = false;
    }

    void Detach()
    {
        _grappling    = false;
        _rope.enabled = false;
        _anchor       = null;

        if (_userControl != null) _userControl.enabled = true;
        if (_charMotor   != null) _charMotor.enabled   = true;
    }

    void Pull()
    {
        Vector3 dir  = _anchor.position - transform.position;
        float   dist = dir.magnitude;

        _grappleVelocity += dir.normalized * _springAcceleration * Time.deltaTime;
        _grappleVelocity  = Vector3.ClampMagnitude(_grappleVelocity, _maxPullSpeed);
        _grappleVelocity.y -= _pullGravity * Time.deltaTime;

        _cc.Move(_grappleVelocity * Time.deltaTime);

        Vector3 ropeStart = _ropeOrigin != null ? _ropeOrigin.position : transform.position;
        _rope.SetPosition(0, ropeStart);
        _rope.SetPosition(1, _anchor.position);

        if (dist < _detachDistance)
            Detach();
    }

    GrappleAnchor FindNearest()
    {
        GrappleAnchor[] all      = Object.FindObjectsByType<GrappleAnchor>(FindObjectsSortMode.None);
        GrappleAnchor   nearest  = null;
        float           bestDist = _maxGrappleRange;

        foreach (GrappleAnchor a in all)
        {
            float d = Vector3.Distance(transform.position, a.transform.position);
            if (d < bestDist) { nearest = a; bestDist = d; }
        }
        return nearest;
    }
}
