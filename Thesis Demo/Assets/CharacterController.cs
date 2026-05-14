using UnityEngine;

[RequireComponent(typeof(CharacterController))]
public class ThirdPersonPlayer : MonoBehaviour
{
    [Header("References")]
    public Transform cameraTransform;
    public Animator animator;

    [Header("Movement")]
    public float walkSpeed = 5f;
    public float sprintSpeed = 8f;
    public float rotationSpeed = 10f;

    [Header("Jumping")]
    public float jumpHeight = 2f;
    public float gravity = -9.81f;

    [Header("Ground Check")]
    public Transform groundCheck;
    public float groundDistance = 0.3f;
    public LayerMask groundMask;

    private CharacterController controller;
    private InputSystem_Actions inputActions;

    private Vector2 moveInput;
    private Vector3 velocity;

    private bool isGrounded;
    private bool isSprinting;

    private void Awake()
    {
        controller = GetComponent<CharacterController>();
        inputActions = new InputSystem_Actions();

        inputActions.Player.Move.performed += ctx =>
            moveInput = ctx.ReadValue<Vector2>();

        inputActions.Player.Move.canceled += ctx =>
            moveInput = Vector2.zero;

        inputActions.Player.Jump.performed += ctx =>
            Jump();

        inputActions.Player.Sprint.performed += ctx =>
            isSprinting = true;

        inputActions.Player.Sprint.canceled += ctx =>
            isSprinting = false;
    }

    private void OnEnable()
    {
        inputActions.Enable();
    }

    private void OnDisable()
    {
        inputActions.Disable();
    }

    private void Update()
    {
        GroundCheck();
        Move();
        ApplyGravity();
        UpdateAnimations();
    }

    private void Move()
    {
        Vector3 forward = cameraTransform.forward;
        Vector3 right = cameraTransform.right;

        forward.y = 0f;
        right.y = 0f;

        forward.Normalize();
        right.Normalize();

        Vector3 moveDirection =
            forward * moveInput.y +
            right * moveInput.x;

        if (moveDirection.magnitude > 0.1f)
        {
            Quaternion targetRotation = Quaternion.LookRotation(moveDirection);

            transform.rotation = Quaternion.Slerp(
                transform.rotation,
                targetRotation,
                rotationSpeed * Time.deltaTime
            );
        }

        float currentSpeed = isSprinting ? sprintSpeed : walkSpeed;

        controller.Move(moveDirection * currentSpeed * Time.deltaTime);
    }

    private void ApplyGravity()
    {
        if (isGrounded && velocity.y < 0)
            velocity.y = -2f;

        velocity.y += gravity * Time.deltaTime;
        controller.Move(velocity * Time.deltaTime);
    }

    private void Jump()
    {
        if (!isGrounded)
            return;

        velocity.y = Mathf.Sqrt(jumpHeight * -2f * gravity);
    }

    private void GroundCheck()
    {
        isGrounded = controller.isGrounded || Physics.CheckSphere(
            groundCheck.position,
            groundDistance,
            groundMask
        );
    }

    private void UpdateAnimations()
    {
        float inputMagnitude = moveInput.magnitude;

        if (isSprinting && inputMagnitude > 0.1f)
            inputMagnitude = 2f;

        animator.SetFloat("InputHorizontal", moveInput.x, 0.1f, Time.deltaTime);
        animator.SetFloat("InputVertical", moveInput.y, 0.1f, Time.deltaTime);
        animator.SetFloat("InputMagnitude", inputMagnitude, 0.1f, Time.deltaTime);

        animator.SetBool("IsGrounded", isGrounded);
        animator.SetBool("IsStrafing", false);
        animator.SetBool("IsSprinting", isSprinting && moveInput.magnitude > 0.1f);

        animator.SetFloat("GroundDistance", isGrounded ? 0f : 1f);
    }
}