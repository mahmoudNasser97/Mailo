using UnityEngine;
using RootMotion.Demos;

[RequireComponent(typeof(CharacterController))]
public class GrappleController : MonoBehaviour
{
    [Header("Rope Visual")]
    [SerializeField] Transform _ropeOrigin;
    [SerializeField] float     _ropeWidth = 0.05f;
    [SerializeField] Color     _ropeColor = Color.gray;

    [Header("Grapple Settings")]
    [SerializeField] float _maxGrappleRange = 15f;
    [SerializeField] float _gravity         = 20f;
    [SerializeField] float _maxSwingSpeed   = 20f;
    [SerializeField] float _reelSpeed       = 3f;
    [SerializeField] float _detachDistance  = 1.2f;

    [Header("Animation")]
    [SerializeField] Animator _animator;
    [SerializeField] string   _swingStateName    = "Swing";
    [SerializeField] string   _returnStateName   = "Locomotion";
    [SerializeField] float    _crossFadeDuration = 0.2f;

    CharacterController    _cc;
    UserControlThirdPerson _userControl;
    CharacterThirdPerson   _charMotor;
    LineRenderer           _rope;

    bool      _grappling;
    Transform _anchor;
    float     _ropeLength;
    Vector3   _swingVelocity;

    void Awake()
    {
        _cc          = GetComponent<CharacterController>();
        _userControl = GetComponent<UserControlThirdPerson>();
        _charMotor   = GetComponent<CharacterThirdPerson>();

        if (_animator == null)
            _animator = GetComponentInParent<Animator>();

        GameObject ropeObj  = new GameObject("GrappleRope");
        ropeObj.transform.SetParent(transform);
        _rope               = ropeObj.AddComponent<LineRenderer>();
        _rope.positionCount = 2;
        _rope.startWidth    = _ropeWidth;
        _rope.endWidth      = _ropeWidth;
        _rope.useWorldSpace = true;
        _rope.material      = new Material(Shader.Find("Sprites/Default"));
        _rope.startColor    = _ropeColor;
        _rope.endColor      = _ropeColor;
        _rope.enabled       = false;
    }

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.E) && !_grappling)
            TryAttach();

        if (Input.GetKeyUp(KeyCode.E) && _grappling)
            Detach();

        if (_grappling && Input.GetKeyDown(KeyCode.Space))
            Detach();

        if (_grappling)
            Swing();
    }

    void TryAttach()
    {
        GrappleAnchor nearest = FindNearest();
        if (nearest == null) return;

        _anchor       = nearest.transform;
        _ropeLength   = Vector3.Distance(transform.position, _anchor.position);
        _grappling    = true;
        _rope.enabled = true;

        if (_userControl != null) _userControl.enabled = false;
        if (_charMotor   != null) _charMotor.enabled   = false;

        if (_animator != null)
            _animator.CrossFade(_swingStateName, _crossFadeDuration);
    }

    void Detach()
    {
        _grappling    = false;
        _rope.enabled = false;
        _anchor       = null;

        if (_userControl != null) _userControl.enabled = true;
        if (_charMotor   != null) _charMotor.enabled   = true;

        if (_animator != null)
            _animator.CrossFade(_returnStateName, _crossFadeDuration);
    }

    void Swing()
    {
        // Pure pendulum: only gravity drives motion
        _swingVelocity.y -= _gravity * Time.deltaTime;
        _swingVelocity    = Vector3.ClampMagnitude(_swingVelocity, _maxSwingSpeed);

        Vector3 toAnchor = _anchor.position - transform.position;
        float   dist     = toAnchor.magnitude;
        Vector3 ropeDir  = toAnchor / dist;

        // Rope constraint: remove velocity pulling away from anchor when taut
        float awaySpeed = -Vector3.Dot(_swingVelocity, ropeDir);
        if (dist >= _ropeLength && awaySpeed > 0f)
            _swingVelocity += ropeDir * awaySpeed;

        _cc.Move(_swingVelocity * Time.deltaTime);

        // Stop downward velocity accumulation when grounded — prevents tunneling on repeated impacts
        if (_cc.isGrounded && _swingVelocity.y < 0f)
            _swingVelocity.y = 0f;

        // Reel in: rope shortens over time so character reaches the anchor
        _ropeLength -= _reelSpeed * Time.deltaTime;

        // Positional correction pulls character in as rope shortens
        // Capped per-frame so a large correction can never skip through a collider
        Vector3 post       = _anchor.position - transform.position;
        float   postDist   = post.magnitude;
        if (postDist > _ropeLength)
        {
            float correction = Mathf.Min(postDist - _ropeLength, _maxSwingSpeed * Time.deltaTime);
            _cc.Move(post.normalized * correction);
        }

        Vector3 ropeStart = _ropeOrigin != null ? _ropeOrigin.position : transform.position;
        _rope.SetPosition(0, ropeStart);
        _rope.SetPosition(1, _anchor.position);

        if (_ropeLength <= _detachDistance)
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
