using UnityEngine;

/// <summary>
/// Handles WASD movement for the Pet Corner prototype.
/// Supports two movement modes so the same character can be tested
/// in a Third-Person scene (Scene A) or a 2.5D Top-Down scene (Scene B).
/// </summary>
[RequireComponent(typeof(CharacterController))]
public class PlayerController : MonoBehaviour
{
    public enum MovementMode
    {
        ThirdPerson, // Movement is relative to the camera's forward/right (camera-relative)
        TopDown      // Movement is relative to world axes (X/Z), camera does not rotate
    }

    [Header("Mode")]
    [SerializeField] private MovementMode movementMode = MovementMode.ThirdPerson;

    [Header("Movement")]
    [SerializeField] private float moveSpeed = 5f;
    [SerializeField] private float rotationSpeed = 10f; // deg-lerp speed factor for facing direction
    [SerializeField] private float gravity = -9.81f;

    [Header("References")]
    [Tooltip("Camera used to compute camera-relative movement in ThirdPerson mode. Defaults to Camera.main if left empty.")]
    [SerializeField] private Transform cameraTransform;

    private CharacterController controller;
    private Vector3 verticalVelocity;

    // Exposed so other systems (e.g. InteractionManager) can query facing direction.
    public Vector3 LastMoveDirection { get; private set; } = Vector3.forward;

    private void Awake()
    {
        controller = GetComponent<CharacterController>();

        if (cameraTransform == null && Camera.main != null)
        {
            cameraTransform = Camera.main.transform;
        }
    }

    private void Update()
    {
        HandleMovement();
        HandleGravity();
    }

    private void OnDisable()
    {
        // กันเสียงฝีเท้าค้างเวลาตัวละครถูกปิดกะทันหัน (จบวัน / เปิดเมนู)
        AudioManager.Instance?.SetWalking(false);
    }

    private void HandleMovement()
    {
        Vector2 stick = GameInput.Move; // WASD / ลูกศร / จอย
        Vector3 input = new Vector3(stick.x, 0f, stick.y);

        // หยุดเกมอยู่ = ไม่รับปุ่มเดิน (timeScale 0 หยุดแค่ระยะทาง ปุ่มยังถูกอ่านตามปกติ)
        if (GameManager.Instance != null && GameManager.Instance.IsPaused)
        {
            input = Vector3.zero;
        }

        // ส่งสถานะ "กำลังเดินอยู่ไหม" ให้ AudioManager ทุกเฟรม (มันเช็คซ้ำให้เอง ไม่เล่นทับ)
        bool isMoving = input.sqrMagnitude > 0.0001f;
        AudioManager.Instance?.SetWalking(isMoving);

        if (!isMoving)
        {
            return; // No input, skip movement/rotation this frame.
        }

        input.Normalize();
        Vector3 moveDirection;

        switch (movementMode)
        {
            case MovementMode.ThirdPerson:
                moveDirection = GetCameraRelativeDirection(input);
                break;

            case MovementMode.TopDown:
            default:
                // World-axis movement, no camera dependency needed.
                moveDirection = input;
                break;
        }

        if (moveDirection.sqrMagnitude > 0.0001f)
        {
            // บัฟ "เดินเร็วขึ้น" จาก PlayerProgress คูณตรงนี้จุดเดียว
            float speed = moveSpeed;
            if (PlayerProgress.Instance != null)
            {
                speed *= PlayerProgress.Instance.MoveSpeedMultiplier;
            }

            controller.Move(moveDirection * speed * Time.deltaTime);
            LastMoveDirection = moveDirection;
            FaceDirection(moveDirection);
        }
    }

    private Vector3 GetCameraRelativeDirection(Vector3 input)
    {
        if (cameraTransform == null)
        {
            return input; // Fallback to world-space if no camera assigned.
        }

        Vector3 camForward = cameraTransform.forward;
        Vector3 camRight = cameraTransform.right;
        camForward.y = 0f;
        camRight.y = 0f;
        camForward.Normalize();
        camRight.Normalize();

        return (camForward * input.z + camRight * input.x).normalized;
    }

    private void FaceDirection(Vector3 direction)
    {
        Quaternion targetRotation = Quaternion.LookRotation(direction, Vector3.up);
        transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, rotationSpeed * Time.deltaTime);
    }

    private void HandleGravity()
    {
        if (controller.isGrounded && verticalVelocity.y < 0f)
        {
            verticalVelocity.y = -2f; // Small downward force to keep grounded flag stable.
        }

        verticalVelocity.y += gravity * Time.deltaTime;
        controller.Move(verticalVelocity * Time.deltaTime);
    }
}
