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
/// </summary>
public class CustomerManager : MonoBehaviour
{
    public static CustomerManager Instance { get; private set; }

    [Header("References")]
    [Tooltip("Prefab ลูกค้า ต้องมี component Customer ติดอยู่")]
    [SerializeField] private GameObject customerPrefab;

    [Tooltip("ประตูร้าน — ลูกค้าเกิดที่นี่และเดินออกทางนี้")]
    [SerializeField] private Transform doorPoint;

    [Tooltip("จุดยืนหน้าเคาน์เตอร์รับออเดอร์ (ควรอยู่ติดกับ IntakePoint)")]
    [SerializeField] private Transform counterPoint;

    [Tooltip("จุดนั่งรอ — ควรมีอย่างน้อยเท่ากับจำนวนออเดอร์พร้อมกันสูงสุด")]
    [SerializeField] private Transform[] waitSeats;

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
    private readonly Dictionary<int, Customer> seatOwners = new Dictionary<int, Customer>();
    private float spawnTimer;

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

        if (!ShouldSpawnMore(game)) return;

        spawnTimer -= Time.deltaTime;
        if (spawnTimer > 0f) return;

        SpawnCustomer();
        spawnTimer = SpawnGapForDay(game.Day);
    }

    private bool ShouldSpawnMore(GameManager game)
    {
        if (customerPrefab == null || counterPoint == null || doorPoint == null) return false;

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

    private void SpawnCustomer()
    {
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
        customer.Initialize(this, species, counterPoint.position);
        customers.Add(customer);
    }

    private PetSpecies PickSpecies()
    {
        PlayerProgress progress = PlayerProgress.Instance;
        if (progress == null || progress.UnlockedSpecies.Count == 0) return null;

        return progress.UnlockedSpecies[Random.Range(0, progress.UnlockedSpecies.Count)];
    }

    /// <summary>ลูกค้าที่ยืนรออยู่ที่เคาน์เตอร์ตอนนี้ (null ถ้ายังไม่มีใครถึง)</summary>
    public Customer CustomerAtCounter()
    {
        foreach (Customer c in customers)
        {
            if (c != null && c.State == Customer.CustomerState.AtCounter) return c;
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
        seatOwners.Clear();
    }

    private void OnDrawGizmos()
    {
        if (doorPoint != null)
        {
            Gizmos.color = Color.white;
            Gizmos.DrawWireSphere(doorPoint.position, 0.4f);
        }

        if (counterPoint != null)
        {
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(counterPoint.position, 0.4f);
        }

        if (waitSeats == null) return;

        Gizmos.color = Color.cyan;
        foreach (Transform seat in waitSeats)
        {
            if (seat != null) Gizmos.DrawWireSphere(seat.position, 0.35f);
        }
    }
}
