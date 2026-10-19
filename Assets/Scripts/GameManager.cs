using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>ช่วงเวลาของเกม — ทุกระบบอ่านค่านี้เพื่อรู้ว่าตอนนี้ทำอะไรได้บ้าง</summary>
public enum GamePhase
{
    MainMenu, // หน้าเมนู เลือกสัตว์เริ่มต้น
    Setup,    // จัดร้าน ย้าย/วางสถานีได้อิสระ เวลาไม่เดิน
    Service,  // เปิดร้าน รับออเดอร์ จัดสถานีไม่ได้
    DayEnd,   // จบวัน เลือกรางวัล + ร้านค้าท้ายวัน
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

    /// <summary>ร้องเรียนได้สูงสุดกี่ครั้งก่อนแพ้ — หน้าจอสรุปผลใช้แสดง</summary>
    public int MaxComplaints => maxComplaints;

    /// <summary>ต้องอยู่ให้ครบกี่วันถึงชนะ</summary>
    public int TotalDays => totalDays;

    /// <summary>รันล่าสุดจบแบบชนะหรือแพ้ (อ่านได้ตอน Phase == RunOver)</summary>
    public bool LastRunWon { get; private set; }

    /// <summary>
    /// เกมถูกหยุดชั่วคราวอยู่ (กด Esc) — ระบบที่รับ input ต้องเช็คค่านี้เอง
    /// เพราะ Time.timeScale = 0 หยุดแค่การเคลื่อนที่ ส่วน Update และ Input ยังทำงานตามปกติ
    /// </summary>
    public bool IsPaused { get; private set; }

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

    /// <summary>ยิงตอนเริ่ม run ใหม่ก่อนเข้าวันที่ 1 — ระบบที่เก็บค่าข้ามวัน (เช่นระดับสินค้าในร้านค้า) ใช้รีเซ็ตตัวเอง</summary>
    public event Action OnRunStarted;

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
        // เผื่อออกจากโหมด Play ขณะยังหยุดเกมอยู่ ไม่งั้นค่า timeScale จะค้างเป็น 0
        Time.timeScale = 1f;

        if (Instance == this) Instance = null;
    }

    /// <summary>หยุด/เล่นต่อ — เมนู Esc เรียกใช้</summary>
    public void SetPaused(bool paused)
    {
        if (IsPaused == paused) return;

        IsPaused = paused;
        Time.timeScale = paused ? 0f : 1f;
    }

    /// <summary>
    /// เลิกรันปัจจุบันแล้วกลับหน้าเมนูหลัก — ใช้ทั้งปุ่ม "เล่นใหม่" ตอนจบเกม และ "กลับเมนูหลัก" ในเมนู Esc
    ///
    /// ต้องเก็บกวาดของที่ค้างจากรันเดิมให้หมดก่อน ไม่งั้นรันถัดไปจะเริ่มด้วยร้านที่ยังมีสถานีเก่าเต็มไปหมด
    /// สัตว์เก็บที่นี่ ส่วนสถานี/กล่องอุปกรณ์ LevelInitializer เก็บเองตอนเห็น phase เปลี่ยนเป็น MainMenu
    /// </summary>
    public void ReturnToMainMenu()
    {
        SetPaused(false);

        ClearActivePets();

        Day = 0;
        OrdersCompleted = 0;
        OrdersIssued = 0;
        OrdersTarget = 0;
        Complaints = 0;
        activeOrders.Clear();

        OnOrderProgressChanged?.Invoke(OrdersCompleted, OrdersTarget);
        OnComplaintsChanged?.Invoke(Complaints, maxComplaints);

        SetPhase(GamePhase.MainMenu);
    }

    /// <summary>ทำลายสัตว์ที่ยังค้างอยู่ในฉากทั้งหมด (บนสถานี ในมือผู้เล่น หรือวางอยู่กับพื้น)</summary>
    private static void ClearActivePets()
    {
        // Pet.All เป็นทะเบียนสดที่หดลงทุกครั้งที่สัตว์ถูกทำลาย วนตรงๆ ไม่ได้ ต้องคัดลอกออกมาก่อน
        List<Pet> pets = new List<Pet>(Pet.All);

        foreach (Pet pet in pets)
        {
            if (pet == null) continue;

            // อยู่ในมือผู้เล่น ต้องเอาออกจากมือก่อน ไม่งั้นมือจะค้างว่าถืออะไรอยู่ทั้งที่ของหายไปแล้ว
            PlayerCarrier holder = pet.GetComponentInParent<PlayerCarrier>();
            if (holder != null && holder.HeldObject == pet.gameObject)
            {
                holder.Consume();
            }

            Destroy(pet.gameObject);
        }
    }

    /// <summary>เริ่ม run ใหม่ — เรียกจากหน้าเมนูหลังผู้เล่นเลือกสัตว์เริ่มต้นแล้ว</summary>
    public void StartNewRun()
    {
        Day = 0;
        Complaints = 0;
        OnComplaintsChanged?.Invoke(Complaints, maxComplaints);

        // เงินเป็นตัวแปร static จึงค้างข้าม run ต้องล้างเองทุกครั้งที่เริ่มใหม่
        ReturnZone.ResetWallet();
        OnRunStarted?.Invoke();

        AdvanceToNextDay();
    }

    /// <summary>
    /// จบช่วงท้ายวันแล้วเข้าช่วงจัดร้านของวันถัดไป — ระบบรางวัล/ร้านค้าท้ายวันเป็นคนเรียก
    /// เรียกได้เฉพาะตอนอยู่ใน DayEnd กันระบบใดเรียกหลังจบ run ไปแล้วจนเกมเล่นต่อเกินวันสุดท้าย
    /// </summary>
    public void BeginNextDay()
    {
        if (Phase != GamePhase.DayEnd)
        {
            Debug.LogWarning($"[GameManager] BeginNextDay เรียกได้เฉพาะตอนจบวัน (ตอนนี้ {Phase}) — ข้าม");
            return;
        }

        AdvanceToNextDay();
    }

    /// <summary>เข้าสู่ช่วงจัดร้านของวันถัดไป (เวลายังไม่เดิน ลูกค้ายังไม่มา)</summary>
    private void AdvanceToNextDay()
    {
        Day++;
        OrdersCompleted = 0;
        OrdersIssued = 0;
        activeOrders.Clear();
        // การ์ดรางวัลบางใบแลกพลังมาด้วยลูกค้าที่มากขึ้น จึงบวกโควตาเพิ่มจาก PlayerProgress ด้วย
        int extraOrders = PlayerProgress.Instance != null ? PlayerProgress.Instance.ExtraOrdersPerDay : 0;
        OrdersTarget = ordersOnFirstDay + (ordersAddedPerDay * (Day - 1)) + extraOrders;

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
    /// ออเดอร์หลุด (มิเตอร์หมด) — ปลดออกจากคิวที่ดูแลอยู่ คืนโควตา แล้วนับร้องเรียน
    /// ต้องปลดออกจาก activeOrders ด้วย ไม่งั้นช่องที่ดูแลพร้อมกันได้จะโดนกินถาวร
    /// </summary>
    public void FailOrder(Pet pet, string reason)
    {
        // คืนโควตาที่ปล่อยไปแล้วด้วย — ออเดอร์นี้จะไม่มีวันถูกนับเป็น OrdersCompleted
        // ถ้าไม่คืน ลูกค้าคนใหม่ที่มาแทนจะรับออเดอร์ไม่ได้ (โควตาเต็ม) แต่วันก็ไม่มีวันครบ = ค้างถาวร
        // เช็คผลของ Remove กันลดซ้ำถ้าถูกเรียกกับสัตว์ตัวเดิมมากกว่า 1 ครั้ง
        if (activeOrders.Remove(pet) && OrdersIssued > 0)
        {
            OrdersIssued--;
        }

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
        // วันสุดท้าย: จบ run ก่อน แล้วค่อยยิง OnDayEnded
        // ระบบที่ฟัง OnDayEnded (รางวัล/ร้านค้า) จะเห็น Phase == RunOver แล้วไม่เปิดหน้าของตัวเอง
        // (เดิมยิง event ก่อน ระบบรางวัลที่ไม่มีของให้เลือกเลยเรียก BeginNextDay ไปก่อนเช็ควัน
        //  Day เลยเลื่อนเป็นวันถัดไปแล้วเกมตัดสินว่าชนะตั้งแต่จบวันที่ 2)
        if (Day >= totalDays)
        {
            EndRun(true);
            OnDayEnded?.Invoke(Day);
            return;
        }

        SetPhase(GamePhase.DayEnd);
        GameLog.Push($"จบวันที่ {Day} — เลือกรางวัลได้", LogTone.Good);

        OnDayEnded?.Invoke(Day);
    }

    private void EndRun(bool won)
    {
        LastRunWon = won;
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
