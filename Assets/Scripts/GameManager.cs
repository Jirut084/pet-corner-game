using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>ช่วงเวลาของเกม — ทุกระบบอ่านค่านี้เพื่อรู้ว่าตอนนี้ทำอะไรได้บ้าง</summary>
public enum GamePhase
{
    MainMenu, // ฉากร้านเพิ่งโหลด ยังไม่เริ่ม run (หน้าเมนูจริงอยู่ฉาก MainMenu.unity)
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

    [Tooltip("ออเดอร์ของวันออกไปแล้วกี่ส่วนถึงจะนับว่าเข้าช่วงท้ายวัน (ลูกค้าใจร้อนขึ้น เทียบได้กับกลางคืนของ PlateUp)")]
    [Range(0.1f, 1f)]
    [SerializeField] private float lateDayStart = 0.75f;

    [Header("Reputation")]
    [Tooltip("ชื่อเสียงทุกๆ กี่แต้ม = ออเดอร์วันถัดไป ±1 (ติดลบก็ลดออเดอร์ได้) " +
             "ได้แต้มจาก: ส่งคืนครบทุกขั้น +1 · ส่งสินค้า +1 · ร้องเรียน −2")]
    [Min(1)]
    [SerializeField] private int reputationPerOrder = 3;

    [Header("Daily Costs")]
    [Tooltip("ค่าเช่าที่หักตอนจบวันแรก — ทางออกของเงินที่โตตามรายได้ กันเงินล้นจนของในร้านค้าไม่มีความหมาย")]
    [Min(0)]
    [SerializeField] private int rentFirstDay = 30;

    [Tooltip("ค่าเช่าที่เพิ่มขึ้นต่อวัน (วัน 1/2/3 = 30/50/70)")]
    [Min(0)]
    [SerializeField] private int rentIncreasePerDay = 20;

    [Tooltip("ค่าดูแลต่อสถานี 1 ชิ้นต่อวัน — ซื้อสถานีเพิ่มจึงมีต้นทุนต่อเนื่อง ไม่ใช่ซื้อครั้งเดียวจบ")]
    [Min(0)]
    [SerializeField] private int upkeepPerStation = 5;

    [Header("Concurrent Orders")]
    [Tooltip("จำนวนออเดอร์ที่รับค้างไว้พร้อมกันได้ ไล่ตามวัน (วัน 1 = ช่องแรก) วันที่เกินลิสต์ใช้ค่าสุดท้าย")]
    [SerializeField] private int[] concurrentOrdersPerDay = new int[] { 1, 2, 3 };

    [Header("Scenes")]
    [Tooltip("ชื่อฉากเมนูหลัก ต้องอยู่ใน Build Profiles / Scene List ด้วย")]
    [SerializeField] private string mainMenuScene = "MainMenu";

    [Header("Debug")]
    [Tooltip("สัตว์เริ่มต้นตอนกด Play ที่ฉากร้านตรงๆ (ไม่ผ่านเมนู) — ปล่อยว่าง = เด้งไปฉากเมนูหลัก")]
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

    /// <summary>
    /// ชื่อเสียงของร้าน แยกจากเงิน — ทำงานดีลูกค้าวันถัดไปเยอะขึ้น (เงินเยอะแต่ยากขึ้น) ทำพังลูกค้าน้อยลง
    /// ให้แกนตัดสินใจที่สอง: รีบคืนทั้งที่ไม่ครบเพื่อเคลียร์คิว vs ทำให้ครบเพื่อชื่อเสียง
    /// </summary>
    public int Reputation { get; private set; }

    /// <summary>ออเดอร์ที่บวก/ลบจากชื่อเสียง คิดตอนเริ่มวันถัดไป (ปัดเข้าหาศูนย์ −2 แต้มยังไม่ลด)</summary>
    public int ReputationOrders => Reputation / reputationPerOrder;

    /// <summary>ค่าเช่า + ค่าดูแลสถานีที่หักไปตอนจบวันล่าสุด — หน้าร้านค้าท้ายวันเอาไปโชว์</summary>
    public int LastDayCost { get; private set; }

    /// <summary>
    /// เข้าช่วงท้ายวันแล้วหรือยัง — ลูกค้าส่วนใหญ่ของวันเข้าร้านไปแล้ว
    /// CustomerManager ใช้เร่งมิเตอร์ความอดทนทุกตัวในช่วงนี้ ให้ท้ายวันมีจังหวะพีค
    /// </summary>
    public bool IsLateDay =>
        Phase == GamePhase.Service &&
        OrdersTarget > 0 &&
        OrdersIssued >= OrdersTarget * lateDayStart;

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

    private IEnumerator Start()
    {
        PetSpecies starter = MainMenu.ChosenStarter != null ? MainMenu.ChosenStarter : debugStarterSpecies;
        if (starter == null)
        {
            SceneManager.LoadScene(mainMenuScene);
            yield break;
        }

        // รอ 1 เฟรมให้ทุกระบบ Start/subscribe event ครบก่อน — เหมือนตอนเมนูอยู่ฉากเดียวกันที่ run เริ่มหลังทุกอย่างพร้อมแล้ว
        yield return null;

        // ต้องปลดล็อกสัตว์/สถานีก่อนเข้า Setup ไม่งั้น LevelInitializer จะไม่มีอะไรให้วาง
        PlayerProgress.Instance?.ResetForNewRun(starter);
        StartNewRun();
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
    /// โหลดฉากเมนูใหม่ทั้งฉาก ของในร้าน (สัตว์/สถานี/ลูกค้า) หายไปกับฉากเอง ไม่ต้องเก็บกวาดทีละชิ้น
    /// เหลือแค่ค่า static ที่ StartNewRun ล้างให้ตอนเริ่ม run ถัดไป
    /// </summary>
    public void ReturnToMainMenu()
    {
        SetPaused(false);
        SceneManager.LoadScene(mainMenuScene);
    }

    /// <summary>เริ่ม run ใหม่ — Start เรียกให้เองหลังฉากร้านโหลด โดยใช้สัตว์ที่เลือกจากฉากเมนู</summary>
    private void StartNewRun()
    {
        Day = 0;
        Complaints = 0;
        Reputation = 0;
        OnComplaintsChanged?.Invoke(Complaints, maxComplaints);

        // ค่า static ค้างข้ามฉาก/ข้าม run ต้องล้างเองทุกครั้งที่เริ่มใหม่
        ReturnZone.ResetWallet();
        RunStats.Reset();
        GameLog.Clear();
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
        OrdersTarget = Mathf.Max(1, ordersOnFirstDay + (ordersAddedPerDay * (Day - 1)) + extraOrders + ReputationOrders);

        OnOrderProgressChanged?.Invoke(OrdersCompleted, OrdersTarget);
        SetPhase(GamePhase.Setup);

        string fromReputation = ReputationOrders != 0 ? $" — ชื่อเสียง {ReputationOrders:+0;-0} ออเดอร์" : string.Empty;
        GameLog.Push($"เริ่มวันที่ {Day}/{totalDays} — จัดร้านให้เรียบร้อยแล้วกดเปิดร้าน (ออเดอร์วันนี้ {OrdersTarget}{fromReputation})");
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

    /// <summary>ชื่อเสียง +/− — ReturnZone เรียกตอนส่งคืนครบ/ส่งสินค้า ส่วนร้องเรียนหักใน AddComplaint เอง</summary>
    public void AddReputation(int amount)
    {
        Reputation += amount;
    }

    /// <summary>
    /// เพิ่มการร้องเรียน 1 ครั้ง (มิเตอร์หมด หรือส่งคืนทั้งที่ทำขั้นตอนไม่ครบ) — หักชื่อเสียง 2 ด้วย
    /// ส่งคืนไม่ครบจึงโดนแค่ −2 จากตรงนี้ ไม่หักซ้ำอีกชั้น
    /// </summary>
    public void AddComplaint(string reason)
    {
        Complaints++;
        Reputation -= 2;
        OnComplaintsChanged?.Invoke(Complaints, maxComplaints);
        RunStats.NoteComplaint(reason); // ครั้งสุดท้าย = สาเหตุที่แพ้ เอาไปโชว์หน้าสรุป

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

        // จ่ายไม่ครบ = ร้องเรียน ซึ่งอาจทำให้แพ้ตรงนี้เลย ถ้าจบ run แล้วไม่ต้องเข้าช่วงท้ายวัน
        PayDailyCosts();
        if (Phase == GamePhase.RunOver) return;

        SetPhase(GamePhase.DayEnd);
        GameLog.Push($"จบวันที่ {Day} — เลือกรางวัลได้", LogTone.Good);

        OnDayEnded?.Invoke(Day);
    }

    /// <summary>
    /// หักค่าเช่า + ค่าดูแลสถานีตอนจบวัน (ไม่หักวันสุดท้าย เพราะ run จบแล้วเงินไม่มีที่ใช้)
    /// ค่าเช่าโตตามวัน เงินที่ได้มากขึ้นในวันหลังๆ จึงถูกดูดออกตามไปด้วย ไม่สะสมจนล้น
    /// </summary>
    private void PayDailyCosts()
    {
        int stations = FindObjectsByType<CareStation>().Length;
        int bill = rentFirstDay + rentIncreasePerDay * (Day - 1) + upkeepPerStation * stations;
        int paid = Mathf.Min(bill, ReturnZone.TotalWallet);

        ReturnZone.AddMoney(-paid);
        RunStats.NoteCostsPaid(paid);
        LastDayCost = paid;

        GameLog.Push($"จ่ายค่าเช่า {rentFirstDay + rentIncreasePerDay * (Day - 1)} + ค่าดูแลสถานี {stations} ชิ้น = {bill} | เหลือ {ReturnZone.TotalWallet}",
            paid < bill ? LogTone.Bad : LogTone.Warn);

        if (paid < bill) AddComplaint($"ค้างค่าเช่า {bill - paid}");
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
