using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// คุมการมาของลูกค้าในช่วงเปิดร้าน — ทยอย spawn ทีละคน เดินมาที่เคาน์เตอร์
/// แล้วจัดจุดรอให้หลังผู้เล่นรับออเดอร์
///
/// กติกาการ spawn (อิงจาก Paw Parlor):
///   - ไม่ spawn เกินโควตาออเดอร์ของวัน (นับรวมคนที่กำลังเดินมาและออเดอร์ที่ค้างอยู่)
///   - ไม่ spawn เกินจำนวนที่ดูแลพร้อมกันได้ของวันนั้น (GameManager.ConcurrentOrderLimit)
///   - ระยะห่างระหว่างคนสั้นลงเรื่อยๆ ตามวัน
///
/// จุดยืนหน้าเคาน์เตอร์อ่านจาก IntakePoint.CustomerStandPosition — ย้ายเคาน์เตอร์แล้วลูกค้าเดินตามไปเอง
/// </summary>
public class CustomerManager : MonoBehaviour
{
    public static CustomerManager Instance { get; private set; }

    [Header("References")]
    [Tooltip("Prefab ลูกค้า ต้องมี component Customer ติดอยู่")]
    [SerializeField] private GameObject customerPrefab;

    [Tooltip("ประตูร้าน — ลูกค้าเกิดที่นี่และเดินออกทางนี้")]
    [SerializeField] private Transform doorPoint;

    [Tooltip("(สำรอง) จุดยืนหน้าเคาน์เตอร์ — ใช้เฉพาะเมื่อในฉากไม่มี IntakePoint " +
             "ปกติจุดยืนมาจากช่อง Customer Stand Point ของ IntakePoint")]
    [SerializeField] private Transform counterPoint;

    [Tooltip("จุดนั่งรอ — ควรมีอย่างน้อยเท่ากับจำนวนออเดอร์พร้อมกันสูงสุด")]
    [SerializeField] private Transform[] waitSeats;

    [Header("Counter Queue")]
    [Tooltip("ระยะห่างระหว่างลูกค้าที่ต่อแถวหน้าเคาน์เตอร์ (เมตร) — เลือก CustomerManager แล้วดูจุดสีส้มใน Scene view")]
    [SerializeField] private float queueSpacing = 1.3f;

    [Tooltip("ทิศที่แถวทอดยาวออกไป ปล่อยเป็น (0,0,0) จะคิดจากแนวเคาน์เตอร์→จุดยืนลูกค้าให้เอง")]
    [SerializeField] private Vector3 queueDirection = Vector3.zero;

    [Tooltip("ลูกค้ารอสั่งนานจนเกจหมดแล้วเดินออก ให้นับเป็นร้องเรียน 1 ครั้งด้วยไหม " +
             "(ความยาวเกจตั้งที่ Counter Patience Seconds ของแต่ละ PetSpecies)")]
    [SerializeField] private bool complainOnCounterTimeout = true;

    /// <summary>ลูกค้าที่รอสั่งจนหมดความอดทนต้องร้องเรียนไหม</summary>
    public bool ComplainOnCounterTimeout => complainOnCounterTimeout;

    [Header("Outside Queue")]
    [Tooltip("ร้านเต็ม (ดูแลครบจำนวนพร้อมกัน/ที่นั่งเต็ม) แล้วลูกค้ายังมาตามจังหวะ — ยืนต่อคิวนอกร้านได้สูงสุดกี่คน (0 = ปิดระบบ)")]
    [Min(0)]
    [SerializeField] private int maxOutsideQueue = 3;

    [Tooltip("เกจรอสั่งของคนนอกร้านลดเร็วกว่าในร้านกี่เท่า (ใช้เกจเดียวกัน เข้าร้านแล้วกลับมาลดปกติ ไม่รีเซ็ต)")]
    [Min(1f)]
    [SerializeField] private float outsideDrainMultiplier = 1.5f;

    [Tooltip("ทิศจากประตูออกไปนอกร้าน (แถวทอดยาวไปทางนี้) — เลือก CustomerManager แล้วดูจุดสีแดงใน Scene view")]
    [SerializeField] private Vector3 outsideQueueDirection = Vector3.forward;

    [Tooltip("ระยะจากประตูถึงคนแรกในคิวนอกร้าน")]
    [SerializeField] private float outsideQueueOffset = 1.2f;

    [SerializeField] private float outsideQueueSpacing = 1.0f;

    /// <summary>เกจรอสั่งตอนยืนนอกร้านลดเร็วกว่าในร้านกี่เท่า</summary>
    public float OutsideDrainMultiplier => outsideDrainMultiplier;

    /// <summary>จุดที่คนนอกร้านเดินจากไปเมื่อหมดความอดทน (ปลายคิวออกไปอีก)</summary>
    public Vector3 OutsideLeavePosition => OutsideSlotPosition(maxOutsideQueue + 3);

    [Header("Pressure")]
    [Tooltip("ลูกค้าที่รออยู่พร้อมกันแต่ละคนทำให้ทุกมิเตอร์ลดเร็วขึ้นกี่เท่า (ทบต้น) — 1 = ปิดระบบนี้")]
    [SerializeField] private float crowdPressurePerCustomer = 1.12f;

    [Tooltip("เพดานของความกดดันจากจำนวนลูกค้า กันกรณีคิวยาวจนมิเตอร์หมดในพริบตา")]
    [SerializeField] private float maxCrowdPressure = 2.5f;

    [Tooltip("ตัวคูณช่วงท้ายวัน (เทียบได้กับกลางคืนของ PlateUp)")]
    [SerializeField] private float lateDayPressure = 1.5f;

    /// <summary>
    /// ตัวคูณความเร็วที่มิเตอร์ความอดทนทุกตัวใช้ร่วมกัน (1 = ปกติ)
    /// อ่านได้จาก Pet / Customer / HUD — คำนวณใหม่ทุกเฟรมในช่วงเปิดร้าน
    /// </summary>
    public static float PatiencePressure { get; private set; } = 1f;

    [Header("Spawn Pacing")]
    [Tooltip("ระยะห่างระหว่างลูกค้าในวันแรก (วินาที)")]
    [SerializeField] private float spawnGapFirstDay = 6f;

    [Tooltip("ระยะห่างต่ำสุด ไม่ว่าจะวันที่เท่าไหร่")]
    [SerializeField] private float spawnGapMin = 2.5f;

    [Tooltip("ระยะห่างที่หดลงต่อ 1 วัน")]
    [SerializeField] private float spawnGapStepPerDay = 0.8f;

    [Tooltip("หน่วงก่อนลูกค้าคนแรกจะเข้าร้านหลังเปิดร้าน")]
    [SerializeField] private float firstSpawnDelay = 1.5f;

    /// <summary>ตำแหน่งประตู ใช้เป็นทางออกของลูกค้า</summary>
    public Vector3 ExitPosition => doorPoint != null ? doorPoint.position : transform.position;

    private readonly List<Customer> customers = new List<Customer>();

    // แถวหน้าเคาน์เตอร์ เรียงตามลำดับมาก่อน-หลัง index 0 คือคนที่ผู้เล่นกดรับได้
    private readonly List<Customer> counterQueue = new List<Customer>();

    // คิวนอกร้าน index 0 = คนที่จะได้เข้าร้านก่อน
    private readonly List<Customer> outsideQueue = new List<Customer>();

    private readonly Dictionary<int, Customer> seatOwners = new Dictionary<int, Customer>();
    private float spawnTimer;

    // แถวรอรับคืนที่จุดส่งคืน เรียงตามลำดับที่มาถึง index 0 = ยืนใกล้จุดส่งคืนที่สุด
    private readonly List<Customer> pickupLine = new List<Customer>();

    // หาเมื่อต้องใช้ครั้งแรก — IntakePoint อาจถูก LevelInitializer สร้างหลัง Start ของเรา
    private IntakePoint intake;
    private ReturnZone returnZone;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning("[CustomerManager] มีมากกว่า 1 ตัวในฉาก ลบตัวที่ซ้ำทิ้ง");
            Destroy(gameObject);
            return;
        }

        Instance = this;
    }

    private void Start()
    {
        if (GameManager.Instance != null)
        {
            GameManager.Instance.OnPhaseChanged += HandlePhaseChanged;
        }
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;

        if (GameManager.Instance != null)
        {
            GameManager.Instance.OnPhaseChanged -= HandlePhaseChanged;
        }
    }

    private void HandlePhaseChanged(GamePhase phase)
    {
        if (phase == GamePhase.Service)
        {
            spawnTimer = firstSpawnDelay;
            return;
        }

        // ออกจากช่วงเปิดร้านแล้ว เก็บกวาดลูกค้าที่ค้างอยู่ให้หมด
        ClearAllCustomers();
    }

    private void Update()
    {
        GameManager game = GameManager.Instance;
        if (game == null || game.Phase != GamePhase.Service)
        {
            PatiencePressure = 1f;
            return;
        }

        customers.RemoveAll(c => c == null);
        AdmitFromOutside(game);
        RefreshCounterQueue();
        RefreshOutsideQueue();
        RefreshPickupLine();
        UpdatePatiencePressure(game);

        if (!ShouldSpawnMore(game)) return;

        spawnTimer -= Time.deltaTime;
        if (spawnTimer > 0f) return;

        // มีคนต่อคิวนอกร้านอยู่แล้ว คนใหม่ต้องต่อท้าย ห้ามแซงเข้าร้านไปก่อน
        bool inside = outsideQueue.Count == 0 && HasRoomInside(game);
        SpawnCustomer(inside);
        spawnTimer = SpawnGapForDay(game.Day);
    }

    /// <summary>ร้านมีที่ว่างให้ลูกค้าเข้ามาต่อแถวหน้าเคาน์เตอร์อีกคนไหม (ดูแลไม่เต็ม + มีที่นั่งรอว่าง)</summary>
    private bool HasRoomInside(GameManager game)
    {
        return game.ActiveOrderCount + CountIncoming() < game.ConcurrentOrderLimit && FreeSeatIndex() >= 0;
    }

    /// <summary>ร้านว่างเมื่อไหร่ ให้หัวคิวนอกร้านเดินเข้ามาต่อแถวหน้าเคาน์เตอร์ (ทีละคนต่อเฟรม)</summary>
    private void AdmitFromOutside(GameManager game)
    {
        outsideQueue.RemoveAll(c => c == null || !c.IsOutside);
        if (outsideQueue.Count == 0 || !HasRoomInside(game)) return;

        Customer next = outsideQueue[0];
        outsideQueue.RemoveAt(0);

        next.EnterShop();
        int slot = counterQueue.Count;
        next.SetQueueSlot(slot, QueueSlotPosition(slot));
        counterQueue.Add(next);
    }

    private void RefreshOutsideQueue()
    {
        outsideQueue.RemoveAll(c => c == null || !c.IsOutside);

        for (int i = 0; i < outsideQueue.Count; i++)
        {
            outsideQueue[i].SetOutsideSlot(OutsideSlotPosition(i));
        }
    }

    /// <summary>
    /// จัดแถวเจ้าของที่ยืนรอรับคืน — เช็คจาก State ทุกเฟรมเหมือนแถวหน้าเคาน์เตอร์
    /// คนที่เพิ่งลุกมาต่อท้าย ส่งคืนคนไหนไปแล้วคนข้างหลังขยับเข้ามาแทน
    /// </summary>
    private void RefreshPickupLine()
    {
        pickupLine.RemoveAll(c => c == null || !c.IsAwaitingPickup);

        foreach (Customer customer in customers)
        {
            if (customer != null && customer.IsAwaitingPickup && !pickupLine.Contains(customer)) pickupLine.Add(customer);
        }

        if (pickupLine.Count == 0) return;
        if (returnZone == null) returnZone = FindAnyObjectByType<ReturnZone>();
        if (returnZone == null) return; // ไม่มีจุดส่งคืนในฉาก เจ้าของยืนรอที่เดิม

        Vector3 stand = returnZone.OwnerStandPosition;
        Vector3 away = stand - returnZone.transform.position;
        away.y = 0f;
        away = away.sqrMagnitude > 0.0001f ? away.normalized : Vector3.forward;

        for (int i = 0; i < pickupLine.Count; i++)
        {
            pickupLine[i].SetPickupSlot(stand + away * (i * queueSpacing), returnZone.transform.position);
        }
    }

    /// <summary>ตำแหน่งช่องที่ n ในคิวนอกร้าน (0 = ติดประตู)</summary>
    private Vector3 OutsideSlotPosition(int index)
    {
        Vector3 direction = outsideQueueDirection;
        direction.y = 0f;
        direction = direction.sqrMagnitude > 0.0001f ? direction.normalized : Vector3.forward;

        return ExitPosition + direction * (outsideQueueOffset + index * outsideQueueSpacing);
    }

    /// <summary>
    /// ความกดดันร่วมของร้าน ณ ตอนนี้ — ตัวคูณที่ไปเร่งทุกมิเตอร์ความอดทนพร้อมกัน
    ///
    /// อิง PlateUp: ลูกค้าทุกกลุ่มที่รออยู่ใช้ "ความกดดัน" ร่วมกัน ยิ่งรอหลายคนยิ่งลดเร็วแบบทบต้น
    /// และช่วงท้ายวัน (กลางคืนของ PlateUp) ลดเร็วขึ้นอีกชั้น
    ///
    /// ความยากจึงมาจากการปล่อยให้งานค้างพร้อมกันหลายอย่าง ไม่ใช่จากการตั้งเวลาให้สั้นจนไม่มีใครทัน
    /// </summary>
    private void UpdatePatiencePressure(GameManager game)
    {
        int waiting = 0;
        foreach (Customer customer in customers)
        {
            if (customer == null) continue;
            if (customer.ShowGauge || customer.ShowCounterGauge) waiting++;
        }

        // คนแรกไม่คิดโทษ คนที่สองเป็นต้นไปถึงจะเริ่มเร่ง
        float crowd = Mathf.Min(Mathf.Pow(crowdPressurePerCustomer, Mathf.Max(0, waiting - 1)), maxCrowdPressure);

        PatiencePressure = crowd * (game.IsLateDay ? lateDayPressure : 1f);
    }

    /// <summary>
    /// ลูกค้าที่สั่งสินค้าค้างอยู่และมิเตอร์เหลือน้อยที่สุด — ตู้สินค้าใช้เลือกว่าจะหยิบอะไรให้ผู้เล่น
    /// ส่ง item มาด้วย = หาเฉพาะคนที่สั่งของชนิดนั้น (จุดคืนใช้ตอนผู้เล่นเอาของมาส่ง)
    /// คืน null ถ้าไม่มีใครสั่งอะไรค้างไว้
    /// </summary>
    public Customer MostUrgentGoodsRequest(GoodsItem item = null)
    {
        Customer best = null;
        float lowest = float.MaxValue;

        foreach (Customer customer in customers)
        {
            if (customer == null || !customer.WantsGood) continue;
            if (item != null && customer.WantedGood != item) continue;

            float remaining = customer.PatienceNormalized;
            if (remaining >= lowest) continue;

            lowest = remaining;
            best = customer;
        }

        return best;
    }

    private bool ShouldSpawnMore(GameManager game)
    {
        if (customerPrefab == null || doorPoint == null) return false;
        if (!TryGetCounterPosition(out _)) return false;

        // คนที่เข้ามาแล้ว/ยืนคิวนอกร้านแต่ยังไม่ได้ส่งออเดอร์ ก็ถือว่ากินโควตาไปแล้ว
        int busy = game.ActiveOrderCount + CountIncoming() + outsideQueue.Count;
        if (game.OrdersCompleted + busy >= game.OrdersTarget) return false;

        // ร้านเต็ม (ดูแลครบจำนวน / ที่นั่งเต็ม) ลูกค้ายังมาตามจังหวะ แต่ต้องไปยืนรอนอกร้าน
        return (outsideQueue.Count == 0 && HasRoomInside(game)) || outsideQueue.Count < maxOutsideQueue;
    }

    private int CountIncoming()
    {
        int count = 0;

        foreach (Customer c in customers)
        {
            if (c == null) continue;
            if (c.State == Customer.CustomerState.WalkingToCounter || c.State == Customer.CustomerState.AtCounter)
            {
                count++;
            }
        }

        return count;
    }

    private float SpawnGapForDay(int day)
    {
        return Mathf.Max(spawnGapMin, spawnGapFirstDay - (day - 1) * spawnGapStepPerDay);
    }

    /// <summary>
    /// ตำแหน่งยืนหน้าเคาน์เตอร์ — อ่านจาก IntakePoint ก่อนเสมอ ย้ายเคาน์เตอร์แล้วลูกค้าตามไปเอง
    /// ใช้ counterPoint เป็นทางสำรองเฉพาะตอนในฉากไม่มี IntakePoint
    /// </summary>
    private bool TryGetCounterPosition(out Vector3 position)
    {
        if (intake == null)
        {
            intake = FindAnyObjectByType<IntakePoint>();
        }

        if (intake != null)
        {
            position = intake.CustomerStandPosition;
            return true;
        }

        if (counterPoint != null)
        {
            position = counterPoint.position;
            return true;
        }

        position = default;
        return false;
    }

    /// <summary>จุดที่ลูกค้าหันหน้าไปหาตอนยืนรอที่เคาน์เตอร์ (ตำแหน่ง IntakePoint) — false ถ้าไม่มี IntakePoint</summary>
    public bool TryGetCounterLookPoint(out Vector3 point)
    {
        if (intake == null)
        {
            intake = FindAnyObjectByType<IntakePoint>();
        }

        point = intake != null ? intake.transform.position : default;
        return intake != null;
    }

    /// <summary>มุมของ object จุดรอ — ลูกค้าจะนั่งหันตามแกนสีฟ้า (Z) ของมัน หมุนจุดรอใน Scene view เพื่อตั้งทิศ</summary>
    public Quaternion GetSeatRotation(int seatIndex)
    {
        if (waitSeats == null || seatIndex < 0 || seatIndex >= waitSeats.Length || waitSeats[seatIndex] == null)
        {
            return Quaternion.identity;
        }

        return waitSeats[seatIndex].rotation;
    }

    // เลือก CustomerManager แล้วจะเห็นลูกศรสีฟ้าจากจุดรอ = ทิศที่ลูกค้าจะหันตอนนั่งรอ
    private void OnDrawGizmosSelected()
    {
        if (waitSeats == null) return;

        Gizmos.color = Color.cyan;
        foreach (Transform seat in waitSeats)
        {
            if (seat == null) continue;

            Vector3 forward = seat.forward;
            forward.y = 0f;
            if (forward.sqrMagnitude < 0.0001f) continue;

            Vector3 tip = seat.position + forward.normalized * 0.9f;
            Gizmos.DrawLine(seat.position, tip);
            Gizmos.DrawWireSphere(tip, 0.08f);
        }
    }

    /// <summary>
    /// จัดตำแหน่งทุกคนในแถวใหม่ — คนที่ถูกรับออเดอร์ไปแล้วจะหลุดออกจากแถวเอง
    /// (เช็คจาก State ไม่ต้องให้ใครมาสั่งถอดออก) แล้วคนข้างหลังขยับขึ้นมาแทน
    /// </summary>
    private void RefreshCounterQueue()
    {
        counterQueue.RemoveAll(c => c == null || !c.IsWaitingInLine);

        if (counterQueue.Count == 0) return;
        if (!TryGetCounterPosition(out Vector3 stand)) return;

        Vector3 direction = QueueDirection();

        for (int i = 0; i < counterQueue.Count; i++)
        {
            counterQueue[i].SetQueueSlot(i, stand + direction * (i * queueSpacing));
        }
    }

    /// <summary>
    /// ทิศที่แถวทอดยาวออกจากเคาน์เตอร์ — ใช้แนวเดียวกับที่จุดยืนลูกค้าเยื้องออกมาจาก IntakePoint
    /// ย้าย/หมุนเคาน์เตอร์เมื่อไหร่ แถวก็หันตามเอง ไม่ต้องมาตั้งค่าใหม่
    /// </summary>
    private Vector3 QueueDirection()
    {
        if (queueDirection.sqrMagnitude > 0.0001f)
        {
            Vector3 manual = queueDirection;
            manual.y = 0f;
            if (manual.sqrMagnitude > 0.0001f) return manual.normalized;
        }

        if (intake != null)
        {
            Vector3 away = intake.CustomerStandPosition - intake.transform.position;
            away.y = 0f;
            if (away.sqrMagnitude > 0.0001f) return away.normalized;
        }

        return Vector3.forward;
    }

    /// <summary>ตำแหน่งยืนของช่องที่ n ในแถว (0 = ติดเคาน์เตอร์)</summary>
    private Vector3 QueueSlotPosition(int index)
    {
        TryGetCounterPosition(out Vector3 stand);
        return stand + QueueDirection() * (index * queueSpacing);
    }

    private void SpawnCustomer(bool inside)
    {
        if (!TryGetCounterPosition(out Vector3 counterPosition)) return;

        PetSpecies species = PickSpecies();
        if (species == null)
        {
            Debug.LogWarning("[CustomerManager] ยังไม่มีสัตว์ที่ปลดล็อกให้สุ่มออเดอร์");
            return;
        }

        GameObject obj = Instantiate(customerPrefab, doorPoint.position, doorPoint.rotation);
        Customer customer = obj.GetComponent<Customer>();

        if (customer == null)
        {
            Debug.LogError("[CustomerManager] customerPrefab ไม่มี component Customer ติดอยู่");
            Destroy(obj);
            return;
        }

        obj.name = $"Customer ({species.speciesName})";
        customers.Add(customer);

        if (!inside)
        {
            customer.InitializeOutside(this, species, OutsideSlotPosition(outsideQueue.Count));
            outsideQueue.Add(customer);
            return;
        }

        // ต่อท้ายแถวทันที ไม่งั้นทุกคนจะเดินไปจุดเดียวกันแล้วยืนซ้อนกันหน้าเคาน์เตอร์
        int slot = counterQueue.Count;
        Vector3 slotPosition = QueueSlotPosition(slot);

        customer.Initialize(this, species, slotPosition);
        customer.SetQueueSlot(slot, slotPosition);

        counterQueue.Add(customer);
    }

    private PetSpecies PickSpecies()
    {
        PlayerProgress progress = PlayerProgress.Instance;
        if (progress == null || progress.UnlockedSpecies.Count == 0) return null;

        return progress.UnlockedSpecies[Random.Range(0, progress.UnlockedSpecies.Count)];
    }

    /// <summary>
    /// ลูกค้าที่ผู้เล่นกดรับออเดอร์ได้ตอนนี้ — หัวแถวที่ยืนถึงที่แล้วเท่านั้น
    /// หัวแถวยังเดินอยู่ก็คืน null ไม่ข้ามไปรับคนที่ต่อท้าย (ไม่งั้นแซงคิวกันได้)
    /// </summary>
    public Customer CustomerAtCounter()
    {
        foreach (Customer c in counterQueue)
        {
            if (c == null || !c.IsWaitingInLine) continue;

            return c.State == Customer.CustomerState.AtCounter ? c : null;
        }

        return null;
    }

    /// <summary>
    /// IntakePoint เรียกหลังสร้าง Pet สำเร็จ — จัดจุดรอให้ลูกค้าแล้วส่งไปนั่ง
    /// คืน false ถ้าไม่มีจุดรอว่าง (ผู้เรียกต้องยกเลิกการรับออเดอร์)
    /// </summary>
    public bool AssignSeat(Customer customer, Pet pet)
    {
        int seat = FreeSeatIndex();
        if (seat < 0)
        {
            Debug.LogWarning("[CustomerManager] ไม่มีจุดรอว่าง รับออเดอร์ไม่ได้");
            return false;
        }

        seatOwners[seat] = customer;
        customer.OnOrderTaken(pet, seat, waitSeats[seat].position);
        return true;
    }

    /// <summary>ReturnZone เรียกเมื่อส่งคืนสัตว์ตัวนี้สำเร็จ เพื่อให้เจ้าของเดินออกอย่างพอใจ</summary>
    public void NotifyOrderDelivered(Pet pet)
    {
        foreach (Customer c in customers)
        {
            if (c == null || c.AssignedPet != pet) continue;

            c.OnOrderDelivered();
            return;
        }
    }

    public void ReleaseSeat(Customer customer)
    {
        if (customer == null || customer.SeatIndex < 0) return;

        if (seatOwners.TryGetValue(customer.SeatIndex, out Customer owner) && owner == customer)
        {
            seatOwners.Remove(customer.SeatIndex);
        }
    }

    private int FreeSeatIndex()
    {
        if (waitSeats == null) return -1;

        for (int i = 0; i < waitSeats.Length; i++)
        {
            if (waitSeats[i] == null) continue;
            if (seatOwners.TryGetValue(i, out Customer owner) && owner != null) continue;

            return i;
        }

        return -1;
    }

    /// <summary>ลูกค้าทุกคนในฉากตอนนี้ ใช้โดย HUD เพื่อวาดเกจความอดทน</summary>
    public IReadOnlyList<Customer> Customers => customers;

    private void ClearAllCustomers()
    {
        foreach (Customer c in customers)
        {
            if (c != null) Destroy(c.gameObject);
        }

        customers.Clear();
        counterQueue.Clear();
        outsideQueue.Clear();
        pickupLine.Clear();
        seatOwners.Clear();
    }

    /// <summary>จุดส้มเรียงกัน = ช่องยืนของแต่ละคนในแถว ใช้เช็คระยะห่างกับทิศแถวก่อนกด Play</summary>
    private void DrawQueueGizmo()
    {
        if (!TryGetCounterPosition(out Vector3 stand)) return;

        Vector3 direction = QueueDirection();
        Gizmos.color = new Color(1f, 0.6f, 0.15f);

        for (int i = 0; i < 4; i++)
        {
            Vector3 slot = stand + direction * (i * queueSpacing);
            Gizmos.DrawWireSphere(slot, 0.3f);

            if (i > 0)
            {
                Gizmos.DrawLine(slot - direction * queueSpacing, slot);
            }
        }
    }

    private void OnDrawGizmos()
    {
        if (doorPoint != null)
        {
            Gizmos.color = Color.white;
            Gizmos.DrawWireSphere(doorPoint.position, 0.4f);
        }

        // จุดยืนหน้าเคาน์เตอร์ตัวจริง IntakePoint วาดเองแล้ว (สีเหลือง) ตรงนี้วาดแค่จุดสำรองเป็นสีเทา
        if (counterPoint != null)
        {
            Gizmos.color = Color.gray;
            Gizmos.DrawWireSphere(counterPoint.position, 0.4f);
        }

        DrawQueueGizmo();

        // จุดแดง = ช่องยืนของคิวนอกร้าน ควรอยู่นอกกำแพงฝั่งประตู
        if (doorPoint != null)
        {
            Gizmos.color = new Color(0.9f, 0.25f, 0.25f);
            for (int i = 0; i < maxOutsideQueue; i++) Gizmos.DrawWireSphere(OutsideSlotPosition(i), 0.3f);
        }

        if (waitSeats == null) return;

        Gizmos.color = Color.cyan;
        foreach (Transform seat in waitSeats)
        {
            if (seat != null) Gizmos.DrawWireSphere(seat.position, 0.35f);
        }
    }
}
