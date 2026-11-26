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

    [Header("Multiplayer")]
    [Tooltip("ผู้เล่นคนที่เท่าไหร่ (0 = ผู้เล่น 1) — ใช้เลือกชุดปุ่มใน PlayerControls ผู้เล่นที่เกิดเพิ่มถูกตั้งค่าให้เองตอนเริ่มฉาก")]
    [SerializeField] private int playerSlot = 0;

    private CharacterController controller;
    private Vector3 verticalVelocity;

    // เสียงฝีเท้ามีตัวเดียว เล่นเมื่อมีผู้เล่นอย่างน้อย 1 คนกำลังเดิน (ไม่งั้นแต่ละคนจะสั่งเปิด/ปิดแย่งกัน)
    private static int movingCount;
    private bool isMoving;

    /// <summary>ผู้เล่นทุกคนที่อยู่ในฉาก (ทั้ง local หลายคน และผู้เล่นออนไลน์)</summary>
    public static readonly System.Collections.Generic.List<PlayerController> All = new System.Collections.Generic.List<PlayerController>();

    public int PlayerSlot
    {
        get => playerSlot;
        set => playerSlot = Mathf.Clamp(value, 0, PlayerControls.MaxPlayers - 1);
    }

    // Exposed so other systems (e.g. InteractionManager) can query facing direction.
    public Vector3 LastMoveDirection { get; private set; } = Vector3.forward;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        All.Clear();
        movingCount = 0;
    }

    private void OnEnable()
    {
        if (!All.Contains(this)) All.Add(this);
    }

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
        SetMoving(false);
        All.Remove(this);
    }

    private void SetMoving(bool moving)
    {
        if (moving == isMoving) return;

        isMoving = moving;
        movingCount = Mathf.Max(0, movingCount + (moving ? 1 : -1));
        AudioManager.Instance?.SetWalking(movingCount > 0);
    }

    private void HandleMovement()
    {
        Vector2 stick = PlayerControls.Move(playerSlot); // ปุ่มของผู้เล่นคนนี้ (ตั้งได้ในเมนู ปุ่มควบคุม)
        Vector3 input = new Vector3(stick.x, 0f, stick.y);

        // หยุดเกมอยู่ = ไม่รับปุ่มเดิน (timeScale 0 หยุดแค่ระยะทาง ปุ่มยังถูกอ่านตามปกติ)
        if (GameManager.Instance != null && GameManager.Instance.IsPaused)
        {
            input = Vector3.zero;
        }

        bool moving = input.sqrMagnitude > 0.0001f;
        SetMoving(moving);

        if (!moving)
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
