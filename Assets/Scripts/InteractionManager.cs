using UnityEngine;

/// <summary>
/// Implement this on any object the player should be able to interact with
/// (pet cages, food bowl, bath point, intake/return point, etc.).
/// </summary>
public interface IInteractable
{
    void Interact(GameObject interactor);
}

/// <summary>
/// สำหรับวัตถุที่ต้อง "กดค้าง" แทนการกดครั้งเดียว (เช่นสถานี Groom/Bath)
/// ถ้า UseHold คืน false, InteractionManager จะกลับไปเรียก Interact() แบบเดิมให้แทน
/// ทำให้สถานีเดียวสลับโหมดได้ตอน runtime โดยไม่ต้องเปลี่ยน component
/// </summary>
public interface IHoldInteractable : IInteractable
{
    bool UseHold { get; }

    void OnHoldStart(GameObject interactor);
    void OnHoldTick(GameObject interactor, float heldSeconds);
    void OnHoldRelease(GameObject interactor, float heldSeconds);

    /// <summary>เรียกเมื่อการกดค้างถูกขัดจังหวะ เช่น ผู้เล่นเดินออกนอกระยะกลางคัน</summary>
    void OnHoldCancel(GameObject interactor);
}

/// <summary>
/// Handles the player's interaction input (default: E key).
/// Supports two detection modes so the same prototype logic can be tested
/// with a center-screen raycast (Scene A, third-person) or an overlap
/// check in front of the character (Scene B, top-down).
/// </summary>
public class InteractionManager : MonoBehaviour
{
    public enum InteractionType
    {
        Raycast, // Fires from the camera's center point forward (Minecraft-style)
        Overlap  // Checks a sphere in front of the player's current facing direction
    }

    [Header("Mode")]
    [SerializeField] private InteractionType interactionType = InteractionType.Raycast;

    [Header("Common Settings")]
    [SerializeField] private float interactRange = 3f;
    [SerializeField] private LayerMask interactableLayers = ~0; // Default: all layers

    [Header("Raycast Mode")]
    [Tooltip("Camera used as the raycast origin. Defaults to Camera.main if left empty.")]
    [SerializeField] private Camera interactionCamera;

    [Header("Overlap Mode")]
    [SerializeField] private float overlapRadius = 1f;
    [Tooltip("How far in front of the player (along facing direction) the overlap sphere is centered.")]
    [SerializeField] private float overlapForwardOffset = 1f;

    private PlayerController playerController;
    private TilePlacer placer;

    // Last interactable found, exposed for optional UI prompts (e.g. "Press E").
    public IInteractable CurrentTarget { get; private set; }

    // สถานะการกดค้างที่กำลังดำเนินอยู่
    private IHoldInteractable activeHoldTarget;
    private float holdStartTime;

    /// <summary>ระยะเวลาที่กดค้างอยู่ตอนนี้ (0 ถ้าไม่ได้กดค้างอะไรอยู่) เปิดให้ UI เอาไปทำ progress bar ได้</summary>
    public float CurrentHoldSeconds => activeHoldTarget != null ? Time.time - holdStartTime : 0f;

    /// <summary>สิ่งที่กำลังกดค้างอยู่ (null ถ้าไม่ได้กดค้าง) — HUD ใช้วาดหลอดกดค้างให้ถูกสถานี</summary>
    public IHoldInteractable ActiveHoldTarget => activeHoldTarget;

    /// <summary>ชื่อปุ่มโต้ตอบของผู้เล่นคนนี้ ใช้แสดงบนป้ายบอกวิธีกด (ปุ่มตั้งได้ใน PlayerControls)</summary>
    public string InteractLabel => PlayerControls.InteractLabel(Slot);

    private int Slot => playerController != null ? playerController.PlayerSlot : 0;

    private void Awake()
    {
        playerController = GetComponent<PlayerController>();
        placer = GetComponent<TilePlacer>(); // ไม่มีก็ได้ แค่จะไม่มีการยก/วางอุปกรณ์

        if (interactionCamera == null)
        {
            interactionCamera = Camera.main;
        }
    }

    private void Update()
    {
        // หยุดเกมอยู่ = ไม่รับปุ่มโต้ตอบ และตัดการกดค้างที่ค้างอยู่ทิ้ง
        if (GameManager.Instance != null && GameManager.Instance.IsPaused)
        {
            CurrentTarget = null;

            if (activeHoldTarget != null)
            {
                activeHoldTarget.OnHoldCancel(gameObject);
                activeHoldTarget = null;
            }

            return;
        }

        // สัตว์ที่หนีจนมุมอยู่ข้างตัวมาก่อนเสมอ ไม่งั้นถ้ามันไปยืนทับสถานี เรย์จะไปโดนสถานีแทนแล้วจับไม่ได้
        CurrentTarget = FindCorneredPet() ?? (interactionType == InteractionType.Raycast
            ? FindTargetViaRaycast()
            : FindTargetViaOverlap());

        if (activeHoldTarget != null)
        {
            UpdateActiveHold();
            return; // ระหว่างกดค้างอยู่ ไม่รับ input อื่น
        }

        if (!PlayerControls.InteractPressed(Slot)) return;

        // ปุ่ม E ปุ่มเดียวทำทุกอย่าง — ช่วงจัดร้านให้ TilePlacer ชิงไปยก/วางอุปกรณ์ก่อน
        // ถ้าไม่ใช่สถานการณ์ของกริด (เปิดร้านแล้ว / ถือสัตว์ / เล็งที่ว่าง) มันจะคืน false ให้มาทำงานปกติ
        if (placer != null && placer.TryHandleInteract()) return;

        BeginInteraction();
    }

    private void BeginInteraction()
    {
        if (CurrentTarget == null) return;

        // ถ้าเป้าหมายรองรับการกดค้างและกำลังตั้งเป็นโหมดค้างอยู่ ให้เข้าสู่โหมดค้าง
        if (CurrentTarget is IHoldInteractable holdTarget && holdTarget.UseHold)
        {
            // สถานีเก็บสถานะกดค้างได้ทีละคน เล่นหลายคนจึงห้ามคนที่สองกดค้างแทรกสถานีเดียวกัน
            if (IsHeldByOtherPlayer(holdTarget)) return;

            activeHoldTarget = holdTarget;
            holdStartTime = Time.time;
            holdTarget.OnHoldStart(gameObject);
            return;
        }

        CurrentTarget.Interact(gameObject);
    }

    private bool IsHeldByOtherPlayer(IHoldInteractable target)
    {
        foreach (PlayerController other in PlayerController.All)
        {
            if (other == null || other.gameObject == gameObject) continue;

            InteractionManager interaction = other.GetComponent<InteractionManager>();
            if (interaction != null && ReferenceEquals(interaction.ActiveHoldTarget, target)) return true;
        }

        return false;
    }

    private void UpdateActiveHold()
    {
        float held = Time.time - holdStartTime;

        // เดินออกนอกระยะ/เล็งไปทางอื่นกลางคัน = ยกเลิก
        // เทียบด้วย ReferenceEquals เพราะ CurrentTarget เป็น interface ที่อาจชี้ไปคนละ component
        if (!ReferenceEquals(CurrentTarget, activeHoldTarget))
        {
            activeHoldTarget.OnHoldCancel(gameObject);
            activeHoldTarget = null;
            return;
        }

        if (PlayerControls.InteractHeld(Slot))
        {
            activeHoldTarget.OnHoldTick(gameObject, held);
            return;
        }

        activeHoldTarget.OnHoldRelease(gameObject, held);
        activeHoldTarget = null;
    }

    /// <summary>
    /// สัตว์ที่กำลังหนีและผู้เล่นเข้าไปใกล้จนมันหยุดให้จับแล้ว (PetEscape.IsCornered)
    /// เช็คจากระยะห่างตรงๆ ไม่ต้องพึ่ง collider จึงจับได้แม้สัตว์ยืนทับสถานีหรือ collider ถูกปิดอยู่
    /// </summary>
    private IInteractable FindCorneredPet()
    {
        foreach (Pet pet in Pet.All)
        {
            if (pet == null) continue;

            PetEscape escape = pet.GetComponent<PetEscape>();
            if (escape != null && escape.IsEscaping && escape.IsCornered) return escape;
        }

        return null;
    }

    private IInteractable FindTargetViaRaycast()
    {
        if (interactionCamera == null)
        {
            return null;
        }

        Ray ray = new Ray(interactionCamera.transform.position, interactionCamera.transform.forward);

        if (Physics.Raycast(ray, out RaycastHit hit, interactRange, interactableLayers))
        {
            return hit.collider.GetComponentInParent<IInteractable>();
        }

        return null;
    }

    /// <summary>
    /// แบบ PlateUp: ของบนกริด (สถานี/เคาน์เตอร์) โต้ตอบได้เฉพาะชิ้นที่อยู่ในช่องติดกันด้านหน้าเท่านั้น
    /// ช่องหน้าว่างค่อยใช้ทรงกลมหาของที่ไม่ได้ลงกริด (สัตว์ที่หนีอยู่บนพื้น ฯลฯ) — ของบนกริดถูกข้ามในขั้นนี้
    /// ไม่งั้นทรงกลมจะไปโดนสถานีแนวทแยงหรือห่างไปสองช่อง
    /// </summary>
    private IInteractable FindTargetViaOverlap()
    {
        Vector3 facing = playerController != null ? playerController.LastMoveDirection : transform.forward;

        TileGrid grid = placer != null ? placer.Grid : null;
        if (grid != null)
        {
            GameObject occupant = grid.GetOccupant(grid.FacingCell(transform.position, facing));
            IInteractable onTile = occupant != null ? occupant.GetComponentInChildren<IInteractable>() : null;
            if (onTile != null) return onTile;
        }

        Vector3 checkCenter = transform.position + facing.normalized * overlapForwardOffset;

        Collider[] hits = Physics.OverlapSphere(checkCenter, overlapRadius, interactableLayers);

        // Pick the closest valid IInteractable within the overlap.
        IInteractable closest = null;
        float closestDistSqr = float.MaxValue;

        foreach (Collider col in hits)
        {
            IInteractable interactable = col.GetComponentInParent<IInteractable>();
            if (interactable == null)
            {
                continue;
            }

            if (grid != null && col.GetComponentInParent<GridOccupant>() is GridOccupant onGrid && onGrid.IsRegistered)
            {
                continue;
            }

            float distSqr = (col.transform.position - checkCenter).sqrMagnitude;
            if (distSqr < closestDistSqr)
            {
                closestDistSqr = distSqr;
                closest = interactable;
            }
        }

        return closest;
    }

    private void OnDrawGizmosSelected()
    {
        // Visual debug aid in the Scene view.
        if (interactionType == InteractionType.Overlap)
        {
            Vector3 facing = Application.isPlaying && playerController != null
                ? playerController.LastMoveDirection
                : transform.forward;
            Vector3 checkCenter = transform.position + facing.normalized * overlapForwardOffset;

            Gizmos.color = Color.cyan;
            Gizmos.DrawWireSphere(checkCenter, overlapRadius);
        }
        else if (interactionCamera != null)
        {
            Gizmos.color = Color.yellow;
            Gizmos.DrawLine(interactionCamera.transform.position,
                interactionCamera.transform.position + interactionCamera.transform.forward * interactRange);
        }
    }
}
