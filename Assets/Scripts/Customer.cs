using UnityEngine;

/// <summary>
/// ลูกค้า 1 คน — เดินเข้าร้านมาส่งออเดอร์ที่เคาน์เตอร์ แล้วไปนั่งรอที่จุดรอ
/// จนกว่าผู้เล่นจะดูแลสัตว์เสร็จและเอามาส่งคืน
///
/// สถานะ: WalkingToCounter → AtCounter (รอผู้เล่นกดรับออเดอร์)
///        → WalkingToSeat → Waiting (มิเตอร์เดิน) → Leaving → ทำลายตัวเอง
///
/// มิเตอร์ความอดทนอยู่ที่ Pet (แหล่งข้อมูลเดียว) ลูกค้าแค่อ่านมาโชว์เป็นเกจ
/// ตามที่ตกลงไว้: มิเตอร์เริ่มนับตอนผู้เล่นรับออเดอร์ ไม่ใช่ตอนลูกค้าเดินเข้าร้าน
///
/// การหันหน้า: ระหว่างเดินหันตามทิศที่เดิน / ถึงเคาน์เตอร์หันหา IntakePoint /
/// นั่งรอหันตามแกนสีฟ้า (Z) ของ object จุดรอ — หมุนจุดรอใน Scene view เพื่อตั้งทิศ
/// โมเดล + อนิเมชันอยู่ที่ลูกของ prefab ส่วน CharacterAnimationDriver ส่งค่า Speed / IsSitting ให้เอง
/// </summary>
public class Customer : MonoBehaviour
{
    public enum CustomerState
    {
        WalkingToCounter,
        AtCounter,
        WalkingToSeat,
        Waiting,
        Leaving
    }

    [Header("Movement")]
    [SerializeField] private float moveSpeed = 3.2f;

    [Tooltip("ถือว่าถึงที่หมายเมื่อเข้าใกล้กว่าระยะนี้")]
    [SerializeField] private float arriveThreshold = 0.15f;

    [Tooltip("ความเร็วหันตัว — ยิ่งมากยิ่งหันไว (ใช้ทั้งตอนเดินและตอนหันหาเคาน์เตอร์/ทิศของจุดรอ)")]
    [SerializeField] private float turnSpeed = 10f;

    [Tooltip("นั่งรอแล้วหันตามแกนสีฟ้า (Z) ของ object จุดรอ — ปิดไว้จะหันค้างตามทิศที่เดินมาถึง")]
    [SerializeField] private bool faceSeatDirection = true;

    [Header("Gauge")]
    [Tooltip("ความสูงของเกจเหนือหัวลูกค้า (วัดจาก pivot ของ root) — ปรับให้ลอยเหนือหัวโมเดลพอดี")]
    [SerializeField] private float gaugeHeight = 2.2f;

    public CustomerState State { get; private set; } = CustomerState.WalkingToCounter;

    /// <summary>ชนิดสัตว์ที่ลูกค้าคนนี้เอามาฝาก</summary>
    public PetSpecies RequestedSpecies { get; private set; }

    /// <summary>สัตว์ที่เกิดขึ้นจริงหลังผู้เล่นรับออเดอร์แล้ว (null ถ้ายังไม่รับ)</summary>
    public Pet AssignedPet { get; private set; }

    /// <summary>จุดรอที่ลูกค้าคนนี้จองไว้ ให้ CustomerManager เอาไปคืนตอนลูกค้าออก</summary>
    public int SeatIndex { get; private set; } = -1;

    public float GaugeHeight => gaugeHeight;

    /// <summary>มิเตอร์เหลือกี่ส่วน 0–1 (คืน 1 ถ้ายังไม่ได้รับออเดอร์)</summary>
    public float PatienceNormalized => AssignedPet != null ? AssignedPet.PatienceNormalized : 1f;

    /// <summary>ควรวาดเกจไหม — วาดเฉพาะตอนมิเตอร์เดินอยู่จริง</summary>
    public bool ShowGauge => AssignedPet != null && AssignedPet.IsPatienceRunning;

    private Vector3 targetPosition;
    private CustomerManager manager;

    // มุมที่จะค่อยๆ หันไปตอนยืน/นั่งอยู่กับที่ (null = ไม่ต้องหัน)
    private Quaternion? idleFacing;

    /// <summary>เรียกจาก CustomerManager ทันทีหลัง spawn</summary>
    public void Initialize(CustomerManager owner, PetSpecies species, Vector3 counterPosition)
    {
        manager = owner;
        RequestedSpecies = species;

        SetTarget(counterPosition);
        State = CustomerState.WalkingToCounter;

        GameLog.Push($"ลูกค้า {species.speciesName} เดินเข้าร้านแล้ว กำลังไปที่เคาน์เตอร์");
    }

    private void Update()
    {
        switch (State)
        {
            case CustomerState.WalkingToCounter:
                if (MoveTowardTarget())
                {
                    State = CustomerState.AtCounter;
                    idleFacing = FacingTowardCounter();
                    GameLog.Push($"ลูกค้า {RequestedSpecies.speciesName} รออยู่ที่เคาน์เตอร์ — กด E เพื่อรับออเดอร์", LogTone.Warn);
                }
                break;

            case CustomerState.WalkingToSeat:
                if (MoveTowardTarget())
                {
                    State = CustomerState.Waiting;
                    idleFacing = faceSeatDirection ? FacingOfSeat() : null;
                }
                break;

            case CustomerState.AtCounter:
            case CustomerState.Waiting:
                TurnTowardIdleFacing();
                break;

            case CustomerState.Leaving:
                if (MoveTowardTarget())
                {
                    Destroy(gameObject);
                }
                break;
        }
    }

    /// <summary>
    /// ผู้เล่นรับออเดอร์แล้ว — ผูกสัตว์ที่เกิดขึ้นเข้ากับลูกค้าคนนี้ แล้วเดินไปนั่งรอ
    /// </summary>
    public void OnOrderTaken(Pet pet, int seatIndex, Vector3 seatPosition)
    {
        AssignedPet = pet;
        SeatIndex = seatIndex;

        // ลูกค้าเป็นคนเฝ้ามิเตอร์ของตัวเอง เพราะเป็นคนที่ต้องเดินออกเมื่อหมดอดทน
        pet.OnPatienceExpired += HandlePatienceExpired;

        SetTarget(seatPosition);
        idleFacing = null;
        State = CustomerState.WalkingToSeat;
    }

    /// <summary>ส่งคืนสัตว์สำเร็จ — ลูกค้าพอใจแล้วเดินออก</summary>
    public void OnOrderDelivered()
    {
        UnsubscribeFromPet();
        AssignedPet = null;
        LeaveShop();
    }

    private void HandlePatienceExpired(Pet pet)
    {
        UnsubscribeFromPet();

        GameLog.Push($"{pet.DisplayName} รอนานเกินไป เจ้าของเดินออกจากร้าน!", LogTone.Bad);
        GameManager.Instance?.FailOrder(pet, $"{pet.DisplayName} รอนานเกินไป");

        AssignedPet = null;
        Destroy(pet.gameObject);

        LeaveShop();
    }

    private void LeaveShop()
    {
        // คืนจุดรอให้คนอื่นใช้ต่อทันที ไม่ต้องรอเดินออกจากร้านจริงๆ
        if (manager != null)
        {
            manager.ReleaseSeat(this);
            SetTarget(manager.ExitPosition);
        }

        SeatIndex = -1;
        idleFacing = null;
        State = CustomerState.Leaving;
    }

    private void UnsubscribeFromPet()
    {
        if (AssignedPet != null)
        {
            AssignedPet.OnPatienceExpired -= HandlePatienceExpired;
        }
    }

    private void OnDestroy()
    {
        UnsubscribeFromPet();

        // ถ้าถูกทำลายกลางคัน (เช่นจบวัน) ต้องคืนจุดรอด้วย ไม่งั้นจุดนั้นจะโดนจองค้าง
        if (manager != null && SeatIndex >= 0)
        {
            manager.ReleaseSeat(this);
        }
    }

    private void SetTarget(Vector3 position)
    {
        targetPosition = new Vector3(position.x, transform.position.y, position.z);
    }

    /// <summary>เดินเข้าหาเป้าหมาย คืน true เมื่อถึงแล้ว</summary>
    private bool MoveTowardTarget()
    {
        Vector3 flatTarget = new Vector3(targetPosition.x, transform.position.y, targetPosition.z);
        Vector3 delta = flatTarget - transform.position;

        if (delta.sqrMagnitude <= arriveThreshold * arriveThreshold)
        {
            return true;
        }

        transform.position = Vector3.MoveTowards(transform.position, flatTarget, moveSpeed * Time.deltaTime);

        // หันแบบนุ่มแทนการหันทันที โมเดลจะได้ไม่สะบัดตอนเปลี่ยนทิศ
        Quaternion walkFacing = Quaternion.LookRotation(delta.normalized, Vector3.up);
        transform.rotation = Quaternion.Slerp(transform.rotation, walkFacing, turnSpeed * Time.deltaTime);
        return false;
    }

    private void TurnTowardIdleFacing()
    {
        if (!idleFacing.HasValue) return;

        transform.rotation = Quaternion.Slerp(transform.rotation, idleFacing.Value, turnSpeed * Time.deltaTime);
    }

    /// <summary>มุมที่หันหาเคาน์เตอร์รับออเดอร์ (null ถ้าไม่มี IntakePoint)</summary>
    private Quaternion? FacingTowardCounter()
    {
        if (manager == null || !manager.TryGetCounterLookPoint(out Vector3 lookPoint)) return null;

        Vector3 direction = lookPoint - transform.position;
        direction.y = 0f;

        return direction.sqrMagnitude > 0.0001f ? Quaternion.LookRotation(direction, Vector3.up) : (Quaternion?)null;
    }

    /// <summary>มุมตามแกนสีฟ้า (Z) ของ object จุดรอ — ใช้แค่การหมุนแนวราบ</summary>
    private Quaternion? FacingOfSeat()
    {
        if (manager == null || SeatIndex < 0) return null;

        Vector3 forward = manager.GetSeatRotation(SeatIndex) * Vector3.forward;
        forward.y = 0f;

        return forward.sqrMagnitude > 0.0001f ? Quaternion.LookRotation(forward, Vector3.up) : (Quaternion?)null;
    }
}
