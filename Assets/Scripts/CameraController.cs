using UnityEngine;

/// <summary>
/// Versatile camera for the Pet Corner prototype.
/// Switch CameraMode to reuse the same script for different setups:
///   ThirdPerson  — follows behind the target, supports mouse look (Scene A)
///   TopDown2_5D  — follows the target at a fixed downward angle (Scene B, old behaviour)
///   FixedArena   — แบบ PlateUp! / Overcooked: กล้องมุมสูงคงที่ เห็นทั้งร้านในจอเดียว
///
/// FixedArena คำนวณระยะกล้องเองทุกเฟรมให้พื้นที่ร้านทั้งหมดอยู่ในจอพอดี (รองรับจอทุกสัดส่วน)
/// ปรับเร็ว: คลิกขวาที่หัว component → Camera Preset: PlateUp / Camera Preset: Overcooked
/// </summary>
public class CameraController : MonoBehaviour
{
    public enum CameraMode
    {
        ThirdPerson,   // Follows behind the target, supports mouse look.
        TopDown2_5D,   // Follows the target at a fixed downward angle, no rotation input.
        FixedArena     // มุมสูงคงที่ เห็นทั้งร้าน (PlateUp! / Overcooked)
    }

    [Header("Mode")]
    [SerializeField] private CameraMode cameraMode = CameraMode.ThirdPerson;

    [Header("Target")]
    [Tooltip("ผู้เล่น — โหมด FixedArena ไม่จำเป็นต้องใส่ ใช้แค่ตอนตั้ง Follow Strength มากกว่า 0")]
    [SerializeField] private Transform target;

    [Header("Third Person Settings")]
    [SerializeField] private Vector3 thirdPersonOffset = new Vector3(0f, 2.5f, -4f);
    [SerializeField] private float mouseSensitivity = 3f;
    [SerializeField] private float minPitch = -20f;
    [SerializeField] private float maxPitch = 60f;

    [Header("Top-Down 2.5D Settings")]
    [SerializeField] private Vector3 topDownOffset = new Vector3(0f, 8f, -6f);
    [SerializeField] private float topDownPitch = 45f;

    [Header("Fixed Arena Settings (PlateUp! / Overcooked)")]
    [Tooltip("กริดของร้าน ปล่อยว่างจะหา TileGrid ในฉากให้เอง")]
    [SerializeField] private TileGrid arenaGrid;

    [Tooltip("ใช้ขนาดกริดทั้งหมดเป็นพื้นที่ที่ต้องเห็น — ปิดไว้ถ้าร้านจริงเล็กกว่ากริด แล้วกำหนด Arena Center / Arena Size เอง " +
             "(เลือก Main Camera แล้วดูกรอบสีเหลืองใน Scene view)")]
    [SerializeField] private bool frameWholeGrid = true;

    [Tooltip("กลางพื้นที่ที่ต้องเห็น (พิกัดโลก ระดับพื้น) — ใช้เมื่อปิด Frame Whole Grid")]
    [SerializeField] private Vector3 arenaCenter = new Vector3(0.5f, 0.5f, -2.5f);

    [Tooltip("ขนาดพื้นที่ที่ต้องเห็น: X = กว้าง, Y = ความสูงของของในร้าน (กันหัวตัวละครแถวหลังหลุดจอ), Z = ลึก")]
    [SerializeField] private Vector3 arenaSize = new Vector3(25f, 2f, 25f);

    [Tooltip("เว้นขอบรอบพื้นที่ (หน่วยโลก) กันของชิดขอบจอหรือโดน HUD บัง")]
    [SerializeField] private float arenaPadding = 0.5f;

    [Tooltip("มุมก้ม — PlateUp! ≈ 50°, Overcooked ≈ 60° (มากขึ้น = มองลงมาตรงขึ้น เห็นพื้นมากขึ้น)")]
    [Range(20f, 89f)]
    [SerializeField] private float arenaPitch = 55f;

    [Tooltip("หมุนกล้องรอบแกนตั้ง — แนะนำให้คง 0 เพราะการเดินแบบ TopDown ของ PlayerController อิงแกนโลก")]
    [SerializeField] private float arenaYaw = 0f;

    [Tooltip("มุมกว้างของเลนส์ — ค่าน้อยภาพจะแบนคล้าย isometric แบบสองเกมนี้ (ค่าเดิม 60 ขอบจอจะเบี้ยว)")]
    [Range(10f, 90f)]
    [SerializeField] private float arenaFieldOfView = 32f;

    [Tooltip("กล้องเลื่อนตามผู้เล่นแค่ไหน — 0 = นิ่งสนิทแบบ PlateUp!, 0.15 = เลื่อนตามเบาๆ แบบ Overcooked")]
    [Range(0f, 1f)]
    [SerializeField] private float arenaFollowStrength = 0f;

    [Tooltip("ระยะสูงสุดที่กล้องเลื่อนตามผู้เล่นได้ (หน่วยโลก)")]
    [SerializeField] private float arenaMaxFollowOffset = 2f;

    [Header("Follow Smoothing")]
    [SerializeField] private float positionSmoothTime = 0.15f;

    private Vector3 velocityRef; // Used internally by SmoothDamp.
    private float yaw;
    private float pitch;

    private Camera cachedCamera;
    private bool arenaSnapped;

    private void Awake()
    {
        cachedCamera = GetComponent<Camera>();
    }

    private void Start()
    {
        if (cameraMode == CameraMode.ThirdPerson)
        {
            Cursor.lockState = CursorLockMode.Locked;
            yaw = target != null ? target.eulerAngles.y : 0f;
            pitch = 15f;
        }
    }

    private void LateUpdate()
    {
        switch (cameraMode)
        {
            case CameraMode.ThirdPerson:
                if (target != null) UpdateThirdPerson();
                break;

            case CameraMode.TopDown2_5D:
                if (target != null) UpdateTopDown();
                break;

            case CameraMode.FixedArena:
                UpdateFixedArena();
                break;
        }
    }

    private void UpdateThirdPerson()
    {
        // Mouse look input controls yaw/pitch around the target.
        Vector2 look = GameInput.Look;
        yaw += look.x * mouseSensitivity;
        pitch -= look.y * mouseSensitivity;
        pitch = Mathf.Clamp(pitch, minPitch, maxPitch);

        Quaternion rotation = Quaternion.Euler(pitch, yaw, 0f);
        Vector3 desiredPosition = target.position + rotation * thirdPersonOffset;

        transform.position = Vector3.SmoothDamp(transform.position, desiredPosition, ref velocityRef, positionSmoothTime);
        transform.LookAt(target.position + Vector3.up * 1.5f);
    }

    private void UpdateTopDown()
    {
        // Fixed rotation looking down at the target, offset stays constant relative to world axes.
        Vector3 desiredPosition = target.position + topDownOffset;

        transform.position = Vector3.SmoothDamp(transform.position, desiredPosition, ref velocityRef, positionSmoothTime);
        transform.rotation = Quaternion.Euler(topDownPitch, 0f, 0f);
    }

    // ---------- Fixed Arena ----------

    private void UpdateFixedArena()
    {
        ComputeArenaPose(out Vector3 desiredPosition, out Quaternion rotation);

        // เฟรมแรกวางกล้องที่ตำแหน่งจริงทันที ไม่ให้ค่อยๆ ไหลมาจากจุดที่วางไว้ในฉาก
        if (!arenaSnapped)
        {
            transform.SetPositionAndRotation(desiredPosition, rotation);
            velocityRef = Vector3.zero;
            arenaSnapped = true;
            return;
        }

        transform.position = Vector3.SmoothDamp(transform.position, desiredPosition, ref velocityRef, positionSmoothTime);
        transform.rotation = rotation;
    }

    /// <summary>
    /// หาตำแหน่ง/มุมกล้องที่ทำให้พื้นที่ร้านทั้งหมดอยู่ในจอ
    /// วิธีคิด: หมุนมุมทั้ง 8 ของกล่องพื้นที่เข้าพิกัดกล้อง แล้วหาระยะที่น้อยที่สุดที่ทุกมุมยังอยู่ในกรวยมองเห็น
    /// คิดใหม่ทุกเฟรม สัดส่วนจอเปลี่ยน (เช่นย่อหน้าต่าง Game) ภาพก็ยังพอดี
    /// </summary>
    private void ComputeArenaPose(out Vector3 position, out Quaternion rotation)
    {
        Camera cam = cachedCamera != null ? cachedCamera : GetComponent<Camera>();

        GetArenaArea(out Vector3 center, out Vector3 size);
        rotation = Quaternion.Euler(arenaPitch, arenaYaw, 0f);

        float aspect = cam != null && cam.aspect > 0f ? cam.aspect : 16f / 9f;
        bool orthographic = cam != null && cam.orthographic;

        if (cam != null && !orthographic)
        {
            cam.fieldOfView = arenaFieldOfView;
        }

        float tanVertical = Mathf.Tan(arenaFieldOfView * 0.5f * Mathf.Deg2Rad);
        float tanHorizontal = tanVertical * aspect;

        Vector3 half = new Vector3(size.x * 0.5f + arenaPadding, size.y, size.z * 0.5f + arenaPadding);
        Quaternion worldToCamera = Quaternion.Inverse(rotation);

        float distance = 0f;
        float orthoHalfHeight = 0f;

        for (int i = 0; i < 8; i++)
        {
            // มุมกล่อง: ระดับพื้น (y = 0) และระดับความสูงของของในร้าน (y = size.y)
            Vector3 corner = new Vector3(
                (i & 1) == 0 ? -half.x : half.x,
                (i & 2) == 0 ? 0f : half.y,
                (i & 4) == 0 ? -half.z : half.z);

            Vector3 local = worldToCamera * corner; // x = ขวา, y = บน, z = ลึกเข้าไปในจอ

            distance = Mathf.Max(distance,
                Mathf.Abs(local.y) / tanVertical - local.z,
                Mathf.Abs(local.x) / tanHorizontal - local.z);

            orthoHalfHeight = Mathf.Max(orthoHalfHeight, Mathf.Abs(local.y), Mathf.Abs(local.x) / aspect);
        }

        if (orthographic)
        {
            // กล้อง orthographic ขนาดภาพไม่ขึ้นกับระยะ วางไกลพอไม่ให้ของโดนตัดด้วย near clip ก็พอ
            cam.orthographicSize = orthoHalfHeight;
            distance = Mathf.Max(half.x, half.z) * 3f;
        }

        Vector3 focus = center + ArenaFollowOffset(center);
        position = focus - rotation * Vector3.forward * distance;
    }

    /// <summary>พื้นที่ที่ต้องเห็น — กลางพื้นที่ระดับพื้น + ขนาด (X กว้าง, Y สูง, Z ลึก)</summary>
    private void GetArenaArea(out Vector3 center, out Vector3 size)
    {
        if (frameWholeGrid)
        {
            if (arenaGrid == null) arenaGrid = FindAnyObjectByType<TileGrid>();

            if (arenaGrid != null)
            {
                float width = arenaGrid.Width * arenaGrid.CellSize;
                float depth = arenaGrid.Height * arenaGrid.CellSize;

                center = arenaGrid.transform.position + new Vector3(width * 0.5f, 0f, depth * 0.5f);
                size = new Vector3(width, arenaSize.y, depth);
                return;
            }
        }

        center = arenaCenter;
        size = arenaSize;
    }

    /// <summary>ระยะที่กล้องเลื่อนตามผู้เล่น (แนวราบเท่านั้น และไม่เกิน arenaMaxFollowOffset)</summary>
    private Vector3 ArenaFollowOffset(Vector3 center)
    {
        if (target == null || arenaFollowStrength <= 0f) return Vector3.zero;

        Vector3 offset = target.position - center;
        offset.y = 0f;

        return Vector3.ClampMagnitude(offset * arenaFollowStrength, arenaMaxFollowOffset);
    }

    // ---------- เมนูคลิกขวาที่หัว component ----------

    [ContextMenu("Camera Preset: PlateUp")]
    private void PresetPlateUp()
    {
        ApplyPreset(50f, 35f, 0f);
    }

    [ContextMenu("Camera Preset: Overcooked")]
    private void PresetOvercooked()
    {
        ApplyPreset(60f, 30f, 0.15f);
    }

    /// <summary>จัดกล้องตามค่า Fixed Arena ตอนนี้เลย ใช้ดูผลใน Scene/Game view โดยไม่ต้องกด Play</summary>
    [ContextMenu("Apply Fixed Arena Framing Now")]
    private void ApplyArenaNow()
    {
        Camera cam = GetComponent<Camera>();

#if UNITY_EDITOR
        UnityEditor.Undo.RecordObject(transform, "Apply Fixed Arena Framing");
        if (cam != null) UnityEditor.Undo.RecordObject(cam, "Apply Fixed Arena Framing");
#endif

        cachedCamera = cam;
        ComputeArenaPose(out Vector3 position, out Quaternion rotation);
        transform.SetPositionAndRotation(position, rotation);
    }

    private void ApplyPreset(float presetPitch, float presetFieldOfView, float presetFollow)
    {
#if UNITY_EDITOR
        UnityEditor.Undo.RecordObject(this, "Camera Preset");
#endif

        cameraMode = CameraMode.FixedArena;
        arenaPitch = presetPitch;
        arenaFieldOfView = presetFieldOfView;
        arenaFollowStrength = presetFollow;

        ApplyArenaNow();
    }

    // เลือก Main Camera แล้วจะเห็นกรอบสีเหลือง = พื้นที่ที่กล้องโหมด FixedArena พยายามให้อยู่ในจอ
    private void OnDrawGizmosSelected()
    {
        if (cameraMode != CameraMode.FixedArena) return;

        GetArenaArea(out Vector3 center, out Vector3 size);

        Gizmos.color = Color.yellow;
        Gizmos.DrawWireCube(center + Vector3.up * size.y * 0.5f,
            new Vector3(size.x + arenaPadding * 2f, size.y, size.z + arenaPadding * 2f));
    }
}
