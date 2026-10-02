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

    private readonly Dictionary<int, Customer> seatOwners = new Dictionary<int, Customer>();
    private float spawnTimer;

    // หาเมื่อต้องใช้ครั้งแรก — IntakePoint อาจถูก LevelInitializer สร้างหลัง Start ของเรา
    private IntakePoint intake;

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
        if (game == null || game.Phase != GamePhase.Service) return;

        customers.RemoveAll(c => c == null);
        RefreshCounterQueue();

        if (!ShouldSpawnMore(game)) return;

        spawnTimer -= Time.deltaTime;
        if (spawnTimer > 0f) return;

        SpawnCustomer();
        spawnTimer = SpawnGapForDay(game.Day);
    }

    private bool ShouldSpawnMore(GameManager game)
    {
        if (customerPrefab == null || doorPoint == null) return false;
        if (!TryGetCounterPosition(out _)) return false;

        // คนที่เข้ามาแล้วแต่ยังไม่ได้ส่งออเดอร์ ก็ถือว่ากินโควตาไปแล้ว
        int incoming = CountIncoming();
        int busy = game.ActiveOrderCount + incoming;

        if (game.OrdersCompleted + busy >= game.OrdersTarget) return false;
        if (busy >= game.ConcurrentOrderLimit) return false;

        // ต้องมีจุดรอว่างรองรับด้วย ไม่งั้นรับออเดอร์แล้วลูกค้าไม่มีที่ไป
        return FreeSeatIndex() >= 0;
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

    private void SpawnCustomer()
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

        // ต่อท้ายแถวทันที ไม่งั้นทุกคนจะเดินไปจุดเดียวกันแล้วยืนซ้อนกันหน้าเคาน์เตอร์
        int slot = counterQueue.Count;
        Vector3 slotPosition = QueueSlotPosition(slot);

        customer.Initialize(this, species, slotPosition);
        customer.SetQueueSlot(slot, slotPosition);

        counterQueue.Add(customer);
        customers.Add(customer);
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

        if (waitSeats == null) return;

        Gizmos.color = Color.cyan;
        foreach (Transform seat in waitSeats)
        {
            if (seat != null) Gizmos.DrawWireSphere(seat.position, 0.35f);
        }
    }
}
