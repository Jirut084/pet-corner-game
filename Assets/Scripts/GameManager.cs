using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>ช่วงเวลาของเกม — ทุกระบบอ่านค่านี้เพื่อรู้ว่าตอนนี้ทำอะไรได้บ้าง</summary>
public enum GamePhase
{
    MainMenu, // หน้าเมนู เลือกสัตว์เริ่มต้น
    Setup,    // จัดร้าน ย้าย/วางสถานีได้อิสระ เวลาไม่เดิน
    Service,  // เปิดร้าน รับออเดอร์ จัดสถานีไม่ได้
    DayEnd,   // จบวัน เลือกรางวัล
    RunOver   // จบ run (ชนะหรือแพ้)
}

/// <summary>
/// แกนกลางของเกม คุม phase, วงจรวัน, จำนวนออเดอร์ และการร้องเรียน
/// ระบบอื่นอ่าน phase จากที่นี่แทนที่จะเก็บ state ของตัวเองแยก จะได้ไม่หลุด sync กัน
///
/// วงจร: Setup → Service → DayEnd → Setup → ... จนกว่าจะครบจำนวนวัน (ชนะ) หรือร้องเรียนเต็ม (แพ้)
/// </summary>
public class GameManager : MonoBehaviour
{
    public static GameManager Instance { get; private set; }

    [Header("Run Settings")]
    [Tooltip("จำนวนวันที่ต้องอยู่ให้ครบถึงจะชนะ")]
    [SerializeField] private int totalDays = 3;

    [Tooltip("จำนวนออเดอร์ของวันแรก")]
    [SerializeField] private int ordersOnFirstDay = 3;

    [Tooltip("จำนวนออเดอร์ที่เพิ่มขึ้นในแต่ละวันถัดไป (วัน 1/2/3 = 3/5/7)")]
    [SerializeField] private int ordersAddedPerDay = 2;

    [Tooltip("ร้องเรียนสะสมถึงจำนวนนี้เมื่อไหร่ = แพ้ทันที")]
    [SerializeField] private int maxComplaints = 5;

    [Header("Concurrent Orders")]
    [Tooltip("จำนวนออเดอร์ที่รับค้างไว้พร้อมกันได้ ไล่ตามวัน (วัน 1 = ช่องแรก) วันที่เกินลิสต์ใช้ค่าสุดท้าย")]
    [SerializeField] private int[] concurrentOrdersPerDay = new int[] { 1, 2, 3 };

    [Header("Debug")]
    [Tooltip("ข้ามหน้าเมนูแล้วเริ่มที่ Setup เลย สะดวกตอนเทสใน Editor")]
    [SerializeField] private bool skipMainMenu = false;

    [Tooltip("สัตว์เริ่มต้นที่ใช้ตอน skipMainMenu — ต้องใส่ ไม่งั้นจะไม่มีสถานีใดถูกปลดล็อกเลย")]
    [SerializeField] private PetSpecies debugStarterSpecies;

    public GamePhase Phase { get; private set; } = GamePhase.MainMenu;
    public int Day { get; private set; }
    public int OrdersCompleted { get; private set; }
    public int OrdersTarget { get; private set; }

    /// <summary>ออเดอร์ที่ปล่อยออกไปแล้วในวันนี้ (นับรวมตัวที่ยังดูแลอยู่) กันไม่ให้รับเกินโควตา</summary>
    public int OrdersIssued { get; private set; }

    public int Complaints { get; private set; }

    /// <summary>จัดวาง/ย้ายสถานีได้เฉพาะตอน Setup เท่านั้น</summary>
    public bool CanArrangeStations => Phase == GamePhase.Setup;

    /// <summary>จำนวนออเดอร์ที่ค้างอยู่พร้อมกันได้ในวันนี้</summary>
    public int ConcurrentOrderLimit
    {
        get
        {
            if (concurrentOrdersPerDay == null || concurrentOrdersPerDay.Length == 0) return 1;

            int index = Mathf.Clamp(Day - 1, 0, concurrentOrdersPerDay.Length - 1);
            return Mathf.Max(1, concurrentOrdersPerDay[index]);
        }
    }

    /// <summary>ออเดอร์ที่รับมาแล้วและยังไม่ได้ส่งคืน</summary>
    public int ActiveOrderCount => activeOrders.Count;

    /// <summary>
    /// รับออเดอร์ใหม่ได้เมื่อ: เปิดร้านอยู่ + ยังไม่ครบโควตาของวัน + ยังไม่เต็มจำนวนที่ดูแลพร้อมกันได้
    /// </summary>
    public bool CanTakeNewOrder =>
        Phase == GamePhase.Service &&
        OrdersIssued < OrdersTarget &&
        activeOrders.Count < ConcurrentOrderLimit;

    private readonly List<Pet> activeOrders = new List<Pet>();

    public event Action<GamePhase> OnPhaseChanged;
    public event Action<int, int> OnOrderProgressChanged; // (completed, target)
    public event Action<int, int> OnComplaintsChanged;    // (complaints, max)
    public event Action<int> OnDayStarted;
    public event Action<int> OnDayEnded;
    public event Action<bool> OnRunEnded;                 // true = ชนะ

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning("[GameManager] มี GameManager มากกว่า 1 ตัวในฉาก ลบตัวที่ซ้ำทิ้ง");
            Destroy(gameObject);
            return;
        }

        Instance = this;
    }

    private void Start()
    {
        if (skipMainMenu)
        {
            // ต้องปลดล็อกสัตว์/สถานีก่อนเข้า Setup ไม่งั้น LevelInitializer จะไม่มีอะไรให้วาง
            if (debugStarterSpecies == null)
            {
                Debug.LogWarning("[GameManager] skipMainMenu เปิดอยู่แต่ไม่ได้ใส่ debugStarterSpecies — ร้านจะไม่มีสถานีเลย");
            }

            PlayerProgress.Instance?.ResetForNewRun(debugStarterSpecies);
            StartNewRun();
        }
        else
        {
            SetPhase(GamePhase.MainMenu);
        }
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    /// <summary>เริ่ม run ใหม่ — เรียกจากหน้าเมนูหลังผู้เล่นเลือกสัตว์เริ่มต้นแล้ว</summary>
    public void StartNewRun()
    {
        Day = 0;
        Complaints = 0;
        OnComplaintsChanged?.Invoke(Complaints, maxComplaints);

        BeginNextDay();
    }

    /// <summary>เข้าสู่ช่วงจัดร้านของวันถัดไป (เวลายังไม่เดิน ลูกค้ายังไม่มา)</summary>
    public void BeginNextDay()
    {
        Day++;
        OrdersCompleted = 0;
        OrdersIssued = 0;
        activeOrders.Clear();
        OrdersTarget = ordersOnFirstDay + (ordersAddedPerDay * (Day - 1));

        OnOrderProgressChanged?.Invoke(OrdersCompleted, OrdersTarget);
        SetPhase(GamePhase.Setup);

        GameLog.Push($"เริ่มวันที่ {Day}/{totalDays} — จัดร้านให้เรียบร้อยแล้วกดเปิดร้าน (ออเดอร์วันนี้ {OrdersTarget})");
    }

    /// <summary>เปิดร้าน — ผู้เล่นกดปุ่มเรียกเมื่อจัดสถานีเสร็จแล้ว</summary>
    public void BeginServicePhase()
    {
        if (Phase != GamePhase.Setup)
        {
            Debug.LogWarning($"[GameManager] เปิดร้านได้เฉพาะตอนอยู่ในช่วง Setup (ตอนนี้ {Phase})");
            return;
        }

        SetPhase(GamePhase.Service);
        OnDayStarted?.Invoke(Day);

        GameLog.Push($"เปิดร้านวันที่ {Day} — ต้องทำให้ครบ {OrdersTarget} ออเดอร์ ลูกค้ากำลังจะเข้ามา", LogTone.Good);
    }

    /// <summary>IntakePoint เรียกตอนปล่อยออเดอร์ใหม่ออกไป</summary>
    public void NotifyOrderIssued(Pet pet)
    {
        OrdersIssued++;

        if (pet != null && !activeOrders.Contains(pet))
        {
            activeOrders.Add(pet);
        }
    }

    /// <summary>ReturnZone เรียกเมื่อส่งคืนสัตว์สำเร็จ 1 ตัว</summary>
    public void CompleteOrder(Pet pet)
    {
        if (Phase != GamePhase.Service) return;

        activeOrders.Remove(pet);
        OrdersCompleted++;
        OnOrderProgressChanged?.Invoke(OrdersCompleted, OrdersTarget);

        GameLog.Push($"ออเดอร์ {OrdersCompleted}/{OrdersTarget} ของวันที่ {Day}", LogTone.Good);

        if (OrdersCompleted >= OrdersTarget)
        {
            EndDay();
        }
    }

    /// <summary>
    /// ออเดอร์หลุด (มิเตอร์หมด) — ปลดออกจากคิวที่ดูแลอยู่แล้วนับร้องเรียน
    /// ต้องปลดออกจาก activeOrders ด้วย ไม่งั้นช่องที่ดูแลพร้อมกันได้จะโดนกินถาวร
    /// </summary>
    public void FailOrder(Pet pet, string reason)
    {
        activeOrders.Remove(pet);
        AddComplaint(reason);
    }

    /// <summary>เพิ่มการร้องเรียน 1 ครั้ง (มิเตอร์หมด หรือส่งคืนทั้งที่ทำขั้นตอนไม่ครบ)</summary>
    public void AddComplaint(string reason)
    {
        Complaints++;
        OnComplaintsChanged?.Invoke(Complaints, maxComplaints);

        GameLog.Push($"ร้องเรียน! ({reason}) สะสม {Complaints}/{maxComplaints}", LogTone.Bad);

        if (Complaints >= maxComplaints)
        {
            EndRun(false);
        }
    }

    private void EndDay()
    {
        SetPhase(GamePhase.DayEnd);
        OnDayEnded?.Invoke(Day);

        GameLog.Push($"จบวันที่ {Day} — เลือกรางวัลได้", LogTone.Good);

        // ครบจำนวนวันแล้ว = ชนะ (เช็คหลังยิง OnDayEnded เพื่อให้ UI สรุปผลวันได้ก่อน)
        if (Day >= totalDays)
        {
            EndRun(true);
        }
    }

    private void EndRun(bool won)
    {
        SetPhase(GamePhase.RunOver);
        OnRunEnded?.Invoke(won);

        GameLog.Push(won
            ? $"ชนะ! อยู่ครบ {totalDays} วัน"
            : $"แพ้ — ร้องเรียนครบ {maxComplaints} ครั้งในวันที่ {Day}",
            won ? LogTone.Good : LogTone.Bad);
    }

    private void SetPhase(GamePhase next)
    {
        if (Phase == next) return;

        Phase = next;
        OnPhaseChanged?.Invoke(Phase);
    }
}
