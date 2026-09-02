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

    [Header("Gauge")]
    [Tooltip("ความสูงของเกจเหนือหัวลูกค้า")]
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
                    GameLog.Push($"ลูกค้า {RequestedSpecies.speciesName} รออยู่ที่เคาน์เตอร์ — กด E เพื่อรับออเดอร์", LogTone.Warn);
                }
                break;

            case CustomerState.WalkingToSeat:
                if (MoveTowardTarget())
                {
                    State = CustomerState.Waiting;
                }
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
        transform.rotation = Quaternion.LookRotation(delta.normalized, Vector3.up);
        return false;
    }
}
