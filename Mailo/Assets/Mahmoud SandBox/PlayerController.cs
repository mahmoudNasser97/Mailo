using UnityEngine;

public class PlayerController : MonoBehaviour
{
    [Header("Movement")]
    public float moveSpeed = 6f;
    private Vector2 moveInput;
    private Rigidbody2D rb;
    private Animator animator;

    [Header("Attack")]
    public float attackCooldown = 0.4f;
    private bool canAttack = true;

    void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        animator = GetComponent<Animator>();

        rb.gravityScale = 0; // Top-down: no gravity
        rb.freezeRotation = true;
    }

    void Update()
    {
        // Movement Input
        moveInput = new Vector2(
            Input.GetAxisRaw("Horizontal"),
            Input.GetAxisRaw("Vertical")
        ).normalized;

        // Animation Parameters
        animator.SetFloat("MoveX", moveInput.x);
        animator.SetFloat("MoveY", moveInput.y);
        animator.SetFloat("Speed", moveInput.sqrMagnitude);

        // Attack
        if (Input.GetMouseButtonDown(1) && canAttack)
            Attack();
    }

    void FixedUpdate()
    {
        rb.MovePosition(rb.position + moveInput * moveSpeed * Time.fixedDeltaTime);
    }

    void Attack()
    {
        animator.SetTrigger("Attack");
        canAttack = false;
        Invoke(nameof(ResetAttack), attackCooldown);
    }

    void ResetAttack() => canAttack = true;

}
