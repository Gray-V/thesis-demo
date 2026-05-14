using UnityEngine;

public class ThirdPersonCamera : MonoBehaviour
{
    [Header("Target")]
    public Transform target;

    [Header("Camera")]
    public Vector3 offset = new Vector3(0f, 2f, -5f);

    public float sensitivity = 2f;
    public float smoothSpeed = 10f;

    [Header("Clamp")]
    public float minPitch = -30f;
    public float maxPitch = 70f;

    private InputSystem_Actions inputActions;

    private Vector2 lookInput;

    private float yaw;
    private float pitch;

    private void Awake()
    {
        inputActions = new InputSystem_Actions();

        inputActions.Player.Look.performed += ctx =>
        {
            lookInput = ctx.ReadValue<Vector2>();
        };

        inputActions.Player.Look.canceled += ctx =>
        {
            lookInput = Vector2.zero;
        };
    }

    private void OnEnable()
    {
        inputActions.Enable();

        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    private void OnDisable()
    {
        inputActions.Disable();
    }

    private void LateUpdate()
    {
        RotateCamera();
        FollowTarget();
    }

    private void RotateCamera()
    {
        yaw += lookInput.x * sensitivity * Time.deltaTime;
        pitch -= lookInput.y * sensitivity * Time.deltaTime;

        pitch = Mathf.Clamp(pitch, minPitch, maxPitch);
    }

    private void FollowTarget()
    {
        Quaternion rotation = Quaternion.Euler(pitch, yaw, 0f);

        Vector3 desiredPosition =
            target.position + rotation * offset;

        transform.position = Vector3.Lerp(
            transform.position,
            desiredPosition,
            smoothSpeed * Time.deltaTime
        );

        transform.LookAt(target.position + Vector3.up * 0.3f);
    }
}