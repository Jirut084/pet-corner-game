using UnityEngine;

/// <summary>
/// ลูกค้า 1 คน — เดินเข้าร้านมาส่งออเดอร์ที่เคาน์เตอร์ แล้วไปนั่งรอที่จุดรอ
/// จนกว่าผู้เล่นจะดูแลสัตว์เสร็จและเอามาส่งคืน
///
/// สถานะ: (WaitingOutside — ร้านเต็ม ต่อคิวนอกร้าน) → WalkingToCounter → AtCounter (รอผู้เล่นกดรับออเดอร์)
///        → WalkingToSeat → Waiting (มิเตอร์เดิน) → (AwaitingPickup — ดูแลครบ ลุกมายืนรอรับที่จุดส่งคืน)
///        → Leaving → ทำลายตัวเอง
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
    /// <summary>อารมณ์ที่โชว์เป็นอีโมตเหนือหัว (GameHudOverlay อ่าน Mood แล้วเลือกรูป emote_happy/wait/impatient/angry)</summary>
    public enum CustomerMood
    {
        Wait,       // รออยู่ เกจยังเหลือเยอะ
        Impatient,  // เกจเหลือน้อยกว่า ImpatientThreshold
        Happy,      // ได้สัตว์คืนครบทุกขั้นตอน กำลังเดินออก
        Angry       // ร้องเรียนแล้วเดินออก (รอนานเกิน / ได้คืนไม่ครบ)
    }

    /// <summary>เกจเหลือน้อยกว่านี้ = Impatient (ตรงกับที่ T2-5 ตกลงไว้ 30%)</summary>
    public const float ImpatientThreshold = 0.3f;

    public enum CustomerState
    {
        WaitingOutside,
        WalkingToCounter,
        AtCounter,
        WalkingToSeat,
        Waiting,
        AwaitingPickup,
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

    /// <summary>ลำดับในแถวหน้าเคาน์เตอร์ (0 = หัวแถว, -1 = ไม่ได้อยู่ในแถวแล้ว)</summary>
    public int QueueIndex { get; private set; } = -1;

    /// <summary>ยังต่อแถวรอส่งออเดอร์อยู่ไหม (กำลังเดินมา หรือยืนรออยู่แล้ว)</summary>
    public bool IsWaitingInLine =>
        State == CustomerState.WalkingToCounter || State == CustomerState.AtCounter;

    public float GaugeHeight => gaugeHeight;

    // อารมณ์ตอนเดินออก ตั้งครั้งเดียวตอนเริ่มเดินออก (Wait = ยังไม่ออก)
    private CustomerMood leaveMood = CustomerMood.Wait;

    /// <summary>
    /// อารมณ์ตอนนี้ — เดินออกแล้วใช้อารมณ์ตอนออก ไม่งั้นดูเกจที่กำลังเดินอยู่ (รอสั่ง / มิเตอร์สัตว์ / รอรับ)
    /// อ่านอย่างเดียว ไม่เปลี่ยนพฤติกรรมของลูกค้า
    /// </summary>
    public CustomerMood Mood
    {
        get
        {
            if (State == CustomerState.Leaving) return leaveMood;

            float fill = ShowGauge ? PatienceNormalized : ShowCounterGauge ? CounterPatienceNormalized : 1f;
            return fill < ImpatientThreshold ? CustomerMood.Impatient : CustomerMood.Wait;
        }
    }

    /// <summary>เดินออกอย่างพอใจ (ได้สัตว์คืนครบ)</summary>
    public bool IsLeavingHappy => State == CustomerState.Leaving && leaveMood == CustomerMood.Happy;

    /// <summary>เดินออกแบบร้องเรียน</summary>
    public bool IsLeavingAngry => State == CustomerState.Leaving && leaveMood == CustomerMood.Angry;

    /// <summary>ลุกจากที่นั่งมายืนรอรับสัตว์ที่ดูแลครบแล้ว (กำลังเดินไป หรือยืนรออยู่แล้ว)</summary>
    public bool IsAwaitingPickup => State == CustomerState.AwaitingPickup;

    /// <summary>มิเตอร์เหลือกี่ส่วน 0–1 (คืน 1 ถ้ายังไม่ได้รับออเดอร์)</summary>
    public float PatienceNormalized => AssignedPet != null ? AssignedPet.PatienceNormalized : 1f;

    /// <summary>ควรวาดเกจไหม — วาดเฉพาะตอนมิเตอร์เดินอยู่จริง</summary>
    public bool ShowGauge => AssignedPet != null && AssignedPet.IsPatienceRunning;

    // ---------- เกจรอ (ก่อนผู้เล่นรับออเดอร์) ----------
    // แยกจากมิเตอร์ของสัตว์โดยสิ้นเชิง: อันนี้อยู่ที่ลูกค้า เดินตอนต่อคิว หยุดทันทีที่ผู้เล่นกดรับออเดอร์
    // จากนั้นมิเตอร์ของสัตว์ (Pet) ค่อยเริ่มนับต่อ — สองเกจจึงไม่มีวันเดินพร้อมกัน
    // ใช้ตัวแปรชุดเดียวแต่เป็นคนละเกจ: นอกร้าน (CustomerManager.OutsidePatienceSeconds) กับหน้าเคาน์เตอร์
    // (PetSpecies.counterPatienceSeconds) — เข้าร้านแล้วเกจหน้าเคาน์เตอร์เริ่มเต็มใหม่ตอนถึงแถว

    private bool counterPatienceRunning;
    private float counterPatienceRemaining;
    private float counterPatienceTotal;
    private bool hasJoinedLine;

    /// <summary>เกจรอที่กำลังเดินอยู่ (นอกร้านหรือหน้าเคาน์เตอร์) เหลือกี่ส่วน 0–1</summary>
    public float CounterPatienceNormalized =>
        counterPatienceTotal > 0f ? Mathf.Clamp01(counterPatienceRemaining / counterPatienceTotal) : 1f;

    /// <summary>ยืนต่อคิวอยู่นอกร้าน (แถวหน้าเคาน์เตอร์เต็ม) — ใช้เกจนอกร้านของตัวเอง</summary>
    public bool IsOutside => State == CustomerState.WaitingOutside;

    /// <summary>ควรวาดเกจรอสั่งไหม — เฉพาะตอนต่อแถว (ในหรือนอกร้าน) และเกจกำลังเดิน</summary>
    public bool ShowCounterGauge => counterPatienceRunning && (IsWaitingInLine || IsOutside);

    /// <summary>สินค้าที่ลูกค้าคนนี้สั่งเพิ่มไว้ (null = ไม่ได้สั่ง หรือส่งให้แล้ว)</summary>
    public GoodsItem WantedGood { get; private set; }

    /// <summary>ยังรอสินค้าอยู่ไหม — ต้องนั่งรออยู่ในร้านเท่านั้น คนที่เดินออกไปแล้วไม่นับ</summary>
    public bool WantsGood => WantedGood != null && State != CustomerState.Leaving;

    /// <summary>IntakePoint เรียกตอนรับออเดอร์ ถ้าตู้สินค้าสุ่มได้ว่าคนนี้สั่งของเพิ่ม</summary>
    public void RequestGood(GoodsItem item)
    {
        WantedGood = item;
    }

    /// <summary>ส่งสินค้าให้แล้ว — เคลียร์คำสั่งซื้อทิ้ง</summary>
    public void ReceiveGood()
    {
        WantedGood = null;
    }

    private Vector3 targetPosition;
    private CustomerManager manager;

    // มุมที่จะค่อยๆ หันไปตอนยืน/นั่งอยู่กับที่ (null = ไม่ต้องหัน)
    private Quaternion? idleFacing;

    /// <summary>เรียกจาก CustomerManager ทันทีหลัง spawn</summary>
    public void Initialize(CustomerManager owner, PetSpecies species, Vector3 counterPosition)
    {
        manager = owner;
        RequestedSpecies = species;

        // ลูกค้าไม่มี Rigidbody และเดินแบบคงค่า y เดิมไว้ ถ้า spawn มาสูง/ต่ำกว่าพื้นจะลอยหรือจมอยู่อย่างนั้นทั้งวัน
        SnapToGround();

        SetTarget(counterPosition);
        State = CustomerState.WalkingToCounter;

        GameLog.Push(Loc.T($"ลูกค้า {species.speciesName} เดินเข้าร้านแล้ว กำลังไปที่เคาน์เตอร์", $"A {species.speciesName} customer walked in and is heading to the counter"));
    }

    /// <summary>
    /// แถวหน้าเคาน์เตอร์เต็ม — ต่อคิวนอกร้านแทน เกจนอกร้านเริ่มทันที (CustomerManager.OutsidePatienceSeconds)
    /// เกิดที่ประตูก่อนเพื่อให้ SnapToGround เจอพื้นในร้าน แล้วค่อยย้ายไปยืนที่ช่องคิว (นอกร้านอาจไม่มีพื้นให้ยิงเรย์)
    /// </summary>
    public void InitializeOutside(CustomerManager owner, PetSpecies species, Vector3 outsidePosition)
    {
        manager = owner;
        RequestedSpecies = species;

        SnapToGround();
        transform.position = new Vector3(outsidePosition.x, transform.position.y, outsidePosition.z);

        SetTarget(outsidePosition);
        State = CustomerState.WaitingOutside;
        idleFacing = FacingTowardDoor();

        StartCounterPatience(owner.OutsidePatienceSeconds);

        GameLog.Push(Loc.T($"แถวในร้านเต็ม! ลูกค้า {species.speciesName} ต่อคิวรอนอกร้าน", $"The line is full! A {species.speciesName} customer is queuing outside"), LogTone.Warn);
    }

    /// <summary>ย้ายไปช่องที่ระบุในคิวนอกร้าน — คนหน้าเข้าร้านไปเมื่อไหร่ทุกคนขยับขึ้นมาเอง</summary>
    public void SetOutsideSlot(Vector3 position)
    {
        if (IsOutside) SetTarget(position);
    }

    /// <summary>แถวหน้าเคาน์เตอร์มีที่ว่างแล้ว — ปิดเกจนอกร้าน เดินเข้าไปต่อแถว เกจหน้าเคาน์เตอร์เริ่มใหม่ตอนถึงแถว</summary>
    public void EnterShop()
    {
        if (!IsOutside) return;

        counterPatienceRunning = false;
        State = CustomerState.WalkingToCounter;
        idleFacing = null;
        GameLog.Push(Loc.T($"ลูกค้า {RequestedSpecies.speciesName} เข้าร้านมาต่อแถวแล้ว", $"A {RequestedSpecies.speciesName} customer joined the line"));
    }

    private void Update()
    {
        TickCounterPatience();

        switch (State)
        {
            case CustomerState.WaitingOutside:
                if (!ReachedTarget()) MoveTowardTarget();
                else TurnTowardIdleFacing();
                break;

            case CustomerState.WalkingToCounter:
                if (MoveTowardTarget())
                {
                    State = CustomerState.AtCounter;
                    idleFacing = FacingTowardCounter();

                    // เกจรอสั่งเริ่มตอนมาถึงแถวครั้งแรก ไม่นับช่วงเดินจากประตู
                    // ขยับขึ้นในแถวทีหลังก็ไม่รีเซ็ต — รอมานานเท่าไหร่ก็นับต่อจากเดิม
                    if (!hasJoinedLine)
                    {
                        hasJoinedLine = true;
                        StartCounterPatience(RequestedSpecies != null ? RequestedSpecies.counterPatienceSeconds : 0f);
                    }

                    // บอกเฉพาะหัวแถว คนที่ยืนต่อท้ายยังกดรับไม่ได้ ไม่ต้องรก log
                    if (QueueIndex <= 0)
                    {
                        GameLog.Push(Loc.T($"ลูกค้า {RequestedSpecies.speciesName} รออยู่ที่เคาน์เตอร์ — กด E เพื่อรับออเดอร์", $"A {RequestedSpecies.speciesName} customer is waiting at the counter — press E to take the order"), LogTone.Warn);
                    }
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

            case CustomerState.AwaitingPickup:
                if (!ReachedTarget()) MoveTowardTarget();
                else TurnTowardIdleFacing();
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
    /// ย้ายไปยืนช่องที่ระบุในแถว — CustomerManager เรียกทุกเฟรมระหว่างเปิดร้าน
    /// คนข้างหน้าถูกรับออเดอร์ไปเมื่อไหร่ ทุกคนขยับขึ้นมาหนึ่งช่องเอง
    /// </summary>
    public void SetQueueSlot(int index, Vector3 position)
    {
        if (!IsWaitingInLine) return;

        QueueIndex = index;
        SetTarget(position);

        // ยืนรออยู่แล้วแต่แถวขยับ ต้องกลับไปเดินต่อ ไม่ใช่ยืนค้างที่เดิม
        if (State == CustomerState.AtCounter && !ReachedTarget())
        {
            State = CustomerState.WalkingToCounter;
            idleFacing = null;
        }
    }

    /// <summary>
    /// ผู้เล่นรับออเดอร์แล้ว — ผูกสัตว์ที่เกิดขึ้นเข้ากับลูกค้าคนนี้ แล้วเดินไปนั่งรอ
    /// </summary>
    public void OnOrderTaken(Pet pet, int seatIndex, Vector3 seatPosition)
    {
        AssignedPet = pet;
        SeatIndex = seatIndex;
        QueueIndex = -1; // ออกจากแถวแล้ว คนข้างหลังจะขยับขึ้นมาแทนในเฟรมถัดไป

        // ส่งออเดอร์ทันแล้ว หยุดเกจรอสั่ง — จากนี้ไปใช้มิเตอร์ของสัตว์แทน
        counterPatienceRunning = false;

        // ลูกค้าเป็นคนเฝ้ามิเตอร์ของตัวเอง เพราะเป็นคนที่ต้องเดินออกเมื่อหมดอดทน
        pet.OnPatienceExpired += HandlePatienceExpired;
        pet.OnAllStepsCompleted += HandlePetReady;

        SetTarget(seatPosition);
        idleFacing = null;
        State = CustomerState.WalkingToSeat;
    }

    /// <summary>
    /// สัตว์ดูแลครบแล้ว — ถ้าเปิดเฟสรอรับ (pet.IsAwaitingPickup) เจ้าของลุกจากที่นั่งมายืนรอที่จุดส่งคืน
    /// คืนที่นั่งทันทีให้ลูกค้าคนใหม่ใช้ ส่วนตำแหน่งในแถวรอรับ CustomerManager จัดให้ทุกเฟรม
    /// </summary>
    private void HandlePetReady(Pet pet)
    {
        if (!pet.IsAwaitingPickup || State == CustomerState.Leaving) return;

        if (manager != null) manager.ReleaseSeat(this);
        SeatIndex = -1;

        idleFacing = null;
        State = CustomerState.AwaitingPickup;
        GameLog.Push(Loc.T($"เจ้าของ {pet.DisplayName} มารอรับที่จุดส่งคืนแล้ว — ส่งคืนภายใน {pet.PatienceRemaining:F0} วิ", $"{pet.DisplayName}'s owner is at the pickup point — return it within {pet.PatienceRemaining:F0}s"), LogTone.Warn);
    }

    /// <summary>CustomerManager เรียกทุกเฟรม — ย้ายไปช่องที่ระบุในแถวรอรับ แล้วหันหน้าเข้าหาจุดส่งคืน</summary>
    public void SetPickupSlot(Vector3 position, Vector3 lookPoint)
    {
        if (!IsAwaitingPickup) return;

        SetTarget(position);

        Vector3 direction = lookPoint - position;
        direction.y = 0f;
        idleFacing = direction.sqrMagnitude > 0.0001f ? Quaternion.LookRotation(direction, Vector3.up) : (Quaternion?)null;
    }

    /// <summary>ส่งคืนสัตว์สำเร็จ — ลูกค้าพอใจแล้วเดินออก</summary>
    public void OnOrderDelivered()
    {
        // ส่งคืนไม่ครบ ReturnZone นับเป็นร้องเรียน ลูกค้าจึงเดินออกแบบไม่พอใจ
        leaveMood = AssignedPet != null && AssignedPet.IsComplete ? CustomerMood.Happy : CustomerMood.Angry;
        UnsubscribeFromPet();
        AssignedPet = null;
        LeaveShop();
    }

    private void StartCounterPatience(float total)
    {
        if (total <= 0f) return; // ตั้งเป็น 0 = รอได้ไม่จำกัด ไม่ต้องมีเกจ

        counterPatienceTotal = total;
        counterPatienceRemaining = total;
        counterPatienceRunning = true;
    }

    /// <summary>
    /// ลดเกจรอสั่งระหว่างต่อแถว — เดินทั้งตอนยืนนิ่งและตอนขยับขึ้นในแถว
    /// Time.deltaTime เป็น 0 ตอนหยุดเกม เกจจึงหยุดตามไปด้วยเอง
    /// </summary>
    private void TickCounterPatience()
    {
        if (!counterPatienceRunning) return;

        if (!IsWaitingInLine && !IsOutside)
        {
            counterPatienceRunning = false;
            return;
        }

        // บัฟ "มิเตอร์ลดช้าลง" ใช้กับเกจนี้ด้วย ผู้เล่นที่ซื้อมาจะรู้สึกได้ทั้งสองช่วง
        float drainRate = PlayerProgress.Instance != null
            ? PlayerProgress.Instance.PatienceDrainMultiplier
            : 1f;

        // คิวหน้าเคาน์เตอร์โดนความกดดันร่วม — ปล่อยคิวยาวแล้วทุกคนใจร้อนขึ้นพร้อมกัน
        // เกจนอกร้านไม่โดน: คนนอกร้านมีได้ก็ต่อเมื่อแถวในร้านเต็มอยู่แล้ว ถ้าคูณซ้ำจะหมดก่อนได้เข้าร้าน
        if (!IsOutside) drainRate *= CustomerManager.PatiencePressure;

        counterPatienceRemaining -= Time.deltaTime * drainRate;
        if (counterPatienceRemaining > 0f) return;

        counterPatienceRemaining = 0f;
        HandleCounterPatienceExpired();
    }

    /// <summary>รอสั่งนานเกิน — ยังไม่มีออเดอร์ให้ยกเลิก แค่ร้องเรียนแล้วเดินออก ลูกค้าคนใหม่จะมาแทนที่เอง</summary>
    private void HandleCounterPatienceExpired()
    {
        counterPatienceRunning = false;
        leaveMood = CustomerMood.Angry;

        string speciesName = RequestedSpecies != null ? RequestedSpecies.speciesName : name;
        bool outside = IsOutside;
        GameLog.Push(outside
            ? Loc.T($"ลูกค้า {speciesName} รอคิวนอกร้านนานเกินไป เดินกลับไปแล้ว!", $"The {speciesName} customer waited outside too long and went home!")
            : Loc.T($"ลูกค้า {speciesName} รอสั่งนานเกินไป เดินออกจากร้าน!", $"The {speciesName} customer waited too long to order and left!"), LogTone.Bad);

        if (manager == null || manager.ComplainOnCounterTimeout)
        {
            GameManager.Instance?.AddComplaint(outside
                ? Loc.T($"ลูกค้า {speciesName} รอคิวนอกร้านนานเกินไป", $"{speciesName} customer waited outside too long")
                : Loc.T($"ลูกค้า {speciesName} รอหน้าเคาน์เตอร์นานเกินไป", $"{speciesName} customer waited at the counter too long"));
        }

        LeaveShop();

        // คนนอกร้านเดินจากไปทางถนน ไม่ต้องเดินผ่านประตูเข้าร้านก่อนหายไป
        if (outside && manager != null) SetTarget(manager.OutsideLeavePosition);
    }

    private void HandlePatienceExpired(Pet pet)
    {
        leaveMood = CustomerMood.Angry;
        UnsubscribeFromPet();

        // หมดเวลาตอนรอรับ = ดูแลเสร็จแล้วแต่ส่งคืนไม่ทัน แยกข้อความให้คนเทสรู้ว่าแพ้ตรงจุดไหน
        string reason = pet.IsAwaitingPickup
            ? Loc.T($"เจ้าของรอรับ {pet.DisplayName} นานเกินไป", $"The owner waited too long to collect {pet.DisplayName}")
            : Loc.T($"{pet.DisplayName} รอนานเกินไป", $"{pet.DisplayName} waited too long");

        GameLog.Push(Loc.T($"{reason} เจ้าของเดินออกจากร้าน!", $"{reason} — the owner left!"), LogTone.Bad);
        GameManager.Instance?.FailOrder(pet, reason);

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
            AssignedPet.OnAllStepsCompleted -= HandlePetReady;
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

    /// <summary>
    /// ยกตัวลูกค้าให้เท้าแตะพื้นที่อยู่ใต้จุด spawn
    /// จุดประตู/จุดนั่งในฉากจะตั้ง y ไว้เท่าไหร่ก็ได้ ไม่ต้องคอยปรับให้พอดีความสูงโมเดล
    /// </summary>
    private void SnapToGround()
    {
        // คำนวณเท้าจากขนาด capsule เอง ห้ามใช้ collider.bounds — เฟรมแรกหลัง Instantiate ค่า bounds ยังเป็นจุดเดียวตรง root
        // (ยังไม่ได้ลงทะเบียนกับ physics) ทำให้คิดว่าเท้าอยู่ที่ root แล้วกดตัวจมพื้นลงไปครึ่งตัว
        CapsuleCollider self = GetComponent<CapsuleCollider>();
        float feet = self != null
            ? transform.TransformPoint(self.center + Vector3.down * (self.height * 0.5f)).y
            : transform.position.y;

        // RaycastAll เพราะเรย์ออกจากในตัวลูกค้าเอง ต้องข้าม collider ตัวเองก่อนถึงจะเจอพื้นจริง
        RaycastHit[] hits = Physics.RaycastAll(transform.position + Vector3.up * 2f, Vector3.down, 20f);

        float floorY = float.NegativeInfinity;
        foreach (RaycastHit hit in hits)
        {
            if (hit.collider.transform.IsChildOf(transform)) continue;
            if (hit.point.y > floorY) floorY = hit.point.y;
        }

        if (float.IsNegativeInfinity(floorY)) return;

        transform.position += Vector3.up * (floorY - feet);
    }

    private void SetTarget(Vector3 position)
    {
        targetPosition = new Vector3(position.x, transform.position.y, position.z);
    }

    /// <summary>ถึงเป้าหมายปัจจุบันแล้วหรือยัง (วัดเฉพาะแนวราบ)</summary>
    private bool ReachedTarget()
    {
        Vector3 flatTarget = new Vector3(targetPosition.x, transform.position.y, targetPosition.z);
        return (flatTarget - transform.position).sqrMagnitude <= arriveThreshold * arriveThreshold;
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

    /// <summary>คนต่อคิวนอกร้านหันหน้าเข้าหาประตู</summary>
    private Quaternion? FacingTowardDoor()
    {
        if (manager == null) return null;

        Vector3 direction = manager.ExitPosition - transform.position;
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
