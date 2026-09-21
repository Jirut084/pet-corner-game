using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// จัดของลงร้าน:
/// 1) วางเคาน์เตอร์หลัก (จุดรับ/จุดคืน) ลงพิกัดตายตัวตอนเปิดฉาก — ต้องวางก่อนเสมอเพราะจองช่องไว้
/// 2) ฟัง PlayerProgress.OnStationUnlocked แล้ววางสถานี "ทันทีที่ปลดล็อก"
///    ครอบคลุมทั้งตอนเลือกสัตว์เริ่มต้นจากเมนู และตอนเลือกรางวัลท้ายวัน
/// 3) มี sweep ตอนเข้าช่วง Setup เป็นตาข่ายกันพลาด เผื่อมีอะไรหลุดจาก event
///
/// สถานีอัตโนมัติจะไปโผล่ในโซน autoPlaceArea เท่านั้น (กรอบเขียวใน Scene view)
/// ไม่ใช่มุมกริดที่อยู่ไกลจากเคาน์เตอร์จนเล่นไม่ได้
/// </summary>
public class LevelInitializer : MonoBehaviour
{
    /// <summary>เคาน์เตอร์ที่ต้องอยู่พิกัดตายตัว (จุดรับ/จุดคืน)</summary>
    [System.Serializable]
    public class FixedCounter
    {
        [Tooltip("ชื่อไว้ดูใน Inspector เฉยๆ")]
        public string label;

        public GameObject prefab;

        [Tooltip("พิกัดช่องบนกริด — x = แกน X, y = แกน Z")]
        public Vector2Int cell;

        [Tooltip("ปกติปิดไว้ เพราะถ้าผู้เล่นยกจุดรับ/จุดคืนไปทิ้งจะเล่น loop ต่อไม่ได้")]
        public bool movable = false;
    }

    [Header("References")]
    [Tooltip("กริดของด่านนี้ ปล่อยว่างได้ จะค้นหา TileGrid ในฉากให้อัตโนมัติ")]
    [SerializeField] private TileGrid grid;

    [Header("1) เคาน์เตอร์หลัก (พิกัดตายตัว)")]
    [SerializeField] private FixedCounter[] fixedCounters;

    [Header("พื้นที่วางสถานีอัตโนมัติ")]
    [Tooltip("มุมล่างซ้ายของโซนที่สถานีจะไปโผล่ (พิกัดช่อง) ดูกรอบสีเขียวใน Scene view")]
    [SerializeField] private Vector2Int autoPlaceAreaMin = new Vector2Int(2, 4);

    [Tooltip("มุมบนขวาของโซนที่สถานีจะไปโผล่ (พิกัดช่อง)")]
    [SerializeField] private Vector2Int autoPlaceAreaMax = new Vector2Int(12, 9);

    [Header("2) กล่องอุปกรณ์แบบสุ่ม (ทางเลือก)")]
    [Tooltip("Prefab กล่องเปล่า ต้องมี component EquipmentParcel + Collider ติดอยู่")]
    [SerializeField] private GameObject parcelPrefab;

    [Tooltip("จุดที่กล่องอุปกรณ์จะถูกส่งมาวาง")]
    [SerializeField] private Transform deliveryPoint;

    [Tooltip("ส่งกล่องอุปกรณ์สุ่มให้ตอนเริ่มแต่ละวันหรือไม่")]
    [SerializeField] private bool spawnParcelEachDay = false;

    // prefab ที่วางลงร้านไปแล้ว กันวางซ้ำทุกครั้งที่เข้า Setup รอบใหม่
    private readonly HashSet<GameObject> placedPrefabs = new HashSet<GameObject>();

    // subscribe ใน Start ไม่ใช่ OnEnable เพราะ OnEnable อาจรันก่อน Awake ของ manager ตัวอื่น
    // ทำให้ Instance ยังเป็น null แล้ว subscribe ไม่ติดเลยทั้งเกม
    private void Start()
    {
        if (grid == null) grid = FindAnyObjectByType<TileGrid>();
        if (grid == null)
        {
            Debug.LogError("[LevelInitializer] หา TileGrid ในฉากไม่เจอ วางสถานีไม่ได้");
            return;
        }

        // ลงทะเบียนของที่วางไว้ในฉากตั้งแต่ใน Editor ก่อน (เช่น IntakePoint/ReturnZone ที่ย้ายมาอยู่ในฉาก)
        // แล้วค่อยวางเคาน์เตอร์จากรายการ — ทั้งสองอย่างต้องมาก่อนสถานีอัตโนมัติ
        // ไม่งั้นสถานีอัตโนมัติอาจไปกินช่องของเคาน์เตอร์จนวางไม่ลง
        RegisterSceneOccupants();
        SpawnFixedCounters();

        if (PlayerProgress.Instance != null)
        {
            PlayerProgress.Instance.OnStationUnlocked += HandleStationUnlocked;

            // เผื่อมีการปลดล็อกไปแล้วก่อนที่เราจะ subscribe ทัน (เช่น skipMainMenu)
            SpawnUnlockedStations();
        }
        else
        {
            Debug.LogWarning("[LevelInitializer] ไม่มี PlayerProgress ในฉาก สถานีจะไม่ถูกวางอัตโนมัติ");
        }

        if (GameManager.Instance != null)
        {
            GameManager.Instance.OnPhaseChanged += HandlePhaseChanged;
        }
    }

    private void OnDestroy()
    {
        if (PlayerProgress.Instance != null)
        {
            PlayerProgress.Instance.OnStationUnlocked -= HandleStationUnlocked;
        }

        if (GameManager.Instance != null)
        {
            GameManager.Instance.OnPhaseChanged -= HandlePhaseChanged;
        }
    }

    /// <summary>
    /// ปลดล็อกสถานีใหม่ = วางลงร้านทันทีในจังหวะเดียวกับที่ผู้เล่นกดเลือก
    /// ครอบคลุมทั้งการเลือกสัตว์เริ่มต้นจากเมนู และการเลือกรางวัลท้ายวัน
    /// </summary>
    private void HandleStationUnlocked(GameObject prefab)
    {
        PlaceStationCopies(prefab);
    }

    private void HandlePhaseChanged(GamePhase phase)
    {
        if (phase == GamePhase.MainMenu)
        {
            ClearShopForNewRun();
            return;
        }

        if (phase != GamePhase.Setup) return;

        // ตาข่ายกันพลาด: ถ้ามีอะไรหลุดจาก event ไป ให้ตามวางตอนเข้าช่วงจัดร้าน
        SpawnUnlockedStations();

        if (spawnParcelEachDay)
        {
            SpawnRandomParcel();
        }
    }

    /// <summary>
    /// ล้างร้านให้ว่างก่อนเริ่มรันใหม่ — ลบทุกอย่างที่ผู้เล่นยกย้ายได้ (สถานี + กล่องอุปกรณ์)
    /// จุดรับ/จุดคืนตั้ง removable = false ไว้จึงรอด ไม่ต้องวางใหม่
    ///
    /// ต้องล้าง placedPrefabs ด้วย ไม่งั้นรันถัดไปจะคิดว่าวางสถานีชนิดนั้นไปแล้วและไม่วางให้อีก
    /// </summary>
    private void ClearShopForNewRun()
    {
        int removed = 0;

        foreach (GridOccupant occupant in FindObjectsByType<GridOccupant>())
        {
            if (occupant == null || !occupant.Removable) continue;

            occupant.Unregister();
            Destroy(occupant.gameObject);
            removed++;
        }

        // กล่องอุปกรณ์ที่ยังไม่ได้แกะไม่มี GridOccupant จึงต้องเก็บแยก
        foreach (EquipmentParcel parcel in FindObjectsByType<EquipmentParcel>())
        {
            if (parcel == null) continue;

            Destroy(parcel.gameObject);
            removed++;
        }

        placedPrefabs.Clear();

        Debug.Log($"[LevelInitializer] ล้างร้านก่อนเริ่มรันใหม่ — ลบของออก {removed} ชิ้น");
    }

    private void SpawnFixedCounters()
    {
        if (fixedCounters == null) return;

        foreach (FixedCounter counter in fixedCounters)
        {
            if (counter == null || counter.prefab == null)
            {
                Debug.LogWarning("[LevelInitializer] มีช่อง Fixed Counter ที่ยังไม่ได้ใส่ prefab ข้ามไป");
                continue;
            }

            // มีเคาน์เตอร์ชนิดเดียวกันวางไว้ในฉากแล้ว (ย้ายได้ใน Scene view) ใช้ตัวในฉาก ไม่สร้างซ้ำ
            if (SceneHasCounterLike(counter.prefab))
            {
                Debug.Log($"[LevelInitializer] {counter.label}: มีวางไว้ในฉากแล้ว ใช้ตัวในฉากแทน");
                continue;
            }

            SpawnStationAt(counter.prefab, counter.cell, counter.label, counter.movable);
        }
    }

    /// <summary>
    /// ลงทะเบียนอุปกรณ์ที่วางไว้ในฉากตั้งแต่ใน Editor เข้ากริด (ช่องคำนวณจากตำแหน่งจริง)
    /// เรียกเองแทนการรอ GridOccupant.Start เพราะลำดับ Start ของแต่ละ object ไม่แน่นอน —
    /// ถ้า Start ของเคาน์เตอร์มาทีหลัง ช่องของมันอาจโดนสถานีอัตโนมัติจองไปก่อน
    /// </summary>
    private void RegisterSceneOccupants()
    {
        foreach (GridOccupant occupant in FindObjectsByType<GridOccupant>())
        {
            if (occupant == null || occupant.IsRegistered) continue;

            occupant.Register(grid, grid.WorldToCell(occupant.transform.position));
        }
    }

    /// <summary>ในฉากมีเคาน์เตอร์ชนิดเดียวกับ prefab นี้อยู่แล้วหรือยัง (เทียบจาก component หลักของมัน)</summary>
    private static bool SceneHasCounterLike(GameObject prefab)
    {
        if (prefab.GetComponent<IntakePoint>() != null) return FindAnyObjectByType<IntakePoint>() != null;
        if (prefab.GetComponent<ReturnZone>() != null) return FindAnyObjectByType<ReturnZone>() != null;
        return false;
    }

    /// <summary>วางสถานีที่ปลดล็อกแล้วแต่ยังไม่มีตัวจริงในร้าน ลงช่องว่างอัตโนมัติ</summary>
    private void SpawnUnlockedStations()
    {
        PlayerProgress progress = PlayerProgress.Instance;
        if (progress == null)
        {
            Debug.LogWarning("[LevelInitializer] ไม่มี PlayerProgress ในฉาก วางสถานีเริ่มต้นไม่ได้");
            return;
        }

        // วนสำเนาลิสต์ เพราะ PlaceStationCopies อาจทำให้ลิสต์ต้นทางถูกแก้ระหว่างวน
        List<GameObject> snapshot = new List<GameObject>(progress.UnlockedStations);
        foreach (GameObject prefab in snapshot)
        {
            PlaceStationCopies(prefab);
        }
    }

    /// <summary>วางสถานีชนิดนี้ตามจำนวนที่กำหนดใน StationCatalog (ข้ามถ้าวางไปแล้ว)</summary>
    private void PlaceStationCopies(GameObject prefab)
    {
        if (prefab == null || placedPrefabs.Contains(prefab)) return;
        if (grid == null) return;

        placedPrefabs.Add(prefab); // ใส่ก่อนวาง กันการเรียกซ้ำซ้อนจาก event + sweep พร้อมกัน

        int copies = CopiesFor(prefab);
        for (int i = 0; i < copies; i++)
        {
            AutoPlaceStation(prefab, prefab.name);
        }
    }

    /// <summary>จำนวนชิ้นที่ได้ตอนปลดล็อกสถานีนี้ครั้งแรก อ่านจาก StationCatalog</summary>
    private int CopiesFor(GameObject prefab)
    {
        StationCatalog catalog = StationCatalog.Instance;
        if (catalog == null) return 1;

        if (prefab == catalog.HoldingTablePrefab) return catalog.HoldingTableCopies;

        CareStation station = prefab.GetComponent<CareStation>();
        if (station == null) return 1;

        // โต๊ะพักที่ไม่ได้ผูกไว้ในช่อง HoldingTablePrefab ก็ต้องไม่ไปอ่านจำนวนจาก step ของมัน
        // เพราะค่า step ของโต๊ะพักไม่มีความหมาย (มักติดมาจากตอน duplicate prefab อื่น)
        if (station.Mode == CareStation.ServiceMode.Holding) return catalog.HoldingTableCopies;

        return catalog.GetCopiesOnUnlock(station.Step);
    }

    /// <summary>วางสถานีลงช่องว่างช่องแรกที่หาเจอ — สถานีที่วางแบบนี้ผู้เล่นย้ายได้เสมอ</summary>
    public GameObject AutoPlaceStation(GameObject prefab, string label = null)
    {
        if (grid == null || prefab == null) return null;

        // หาในโซนที่กำหนดก่อน ถ้าเต็มค่อยขยายไปทั้งกริด ดีกว่าวางไม่ได้เลย
        if (!grid.TryFindFreeCell(autoPlaceAreaMin, autoPlaceAreaMax, out Vector2Int cell) &&
            !grid.TryFindFreeCell(out cell))
        {
            Debug.LogWarning($"[LevelInitializer] กริดเต็ม วาง {prefab.name} ไม่ได้");
            return null;
        }

        return SpawnStationAt(prefab, cell, label, true);
    }

    // วาดกรอบโซนวางสถานีอัตโนมัติใน Scene view เพื่อให้ปรับตำแหน่งได้เห็นภาพ
    private void OnDrawGizmosSelected()
    {
        TileGrid target = grid != null ? grid : FindAnyObjectByType<TileGrid>();
        if (target == null) return;

        Vector3 a = target.CellToWorld(autoPlaceAreaMin);
        Vector3 b = target.CellToWorld(autoPlaceAreaMax);
        Vector3 center = (a + b) * 0.5f;
        Vector3 size = new Vector3(
            Mathf.Abs(b.x - a.x) + target.CellSize,
            0.1f,
            Mathf.Abs(b.z - a.z) + target.CellSize);

        Gizmos.color = Color.green;
        Gizmos.DrawWireCube(center + Vector3.up * 0.05f, size);
    }

    /// <summary>
    /// วางสถานีลงช่องที่กำหนด คืน GameObject ที่สร้าง (null ถ้าวางไม่สำเร็จ)
    /// movable = false จะล็อกไม่ให้ผู้เล่นยกย้าย เหมาะกับจุดรับ/จุดคืนที่ core loop ต้องใช้
    /// </summary>
    public GameObject SpawnStationAt(GameObject prefab, Vector2Int cell, string label = null, bool movable = false)
    {
        if (grid == null || prefab == null) return null;

        string stationName = string.IsNullOrEmpty(label) ? prefab.name : label;

        if (!grid.IsInBounds(cell))
        {
            Debug.LogWarning($"[LevelInitializer] {stationName}: ช่อง {cell} อยู่นอกกริด (ขนาด {grid.Width}x{grid.Height}) ข้ามไป");
            return null;
        }

        if (grid.IsOccupied(cell))
        {
            Debug.LogWarning($"[LevelInitializer] {stationName}: ช่อง {cell} มีของอยู่แล้ว ข้ามไป");
            return null;
        }

        GameObject station = Instantiate(prefab, grid.CellToWorld(cell), Quaternion.identity);
        station.name = stationName;

        GridOccupant occupant = station.GetComponent<GridOccupant>();
        if (occupant == null)
        {
            occupant = station.AddComponent<GridOccupant>();
        }

        if (!occupant.Register(grid, cell, movable))
        {
            Debug.LogWarning($"[LevelInitializer] {stationName}: ลงทะเบียนช่อง {cell} ไม่สำเร็จ ยกเลิก");
            Destroy(station);
            return null;
        }

        Debug.Log($"[LevelInitializer] วาง {stationName} ที่ช่อง {cell}");
        return station;
    }

    /// <summary>สุ่มอุปกรณ์ 1 ชิ้นจากคลังที่ปลดล็อกแล้ว แล้วส่งมาเป็นกล่องที่จุดรับของ</summary>
    public EquipmentParcel SpawnRandomParcel()
    {
        if (parcelPrefab == null || deliveryPoint == null)
        {
            Debug.LogWarning("[LevelInitializer] ยังไม่ได้ตั้งค่า parcelPrefab หรือ deliveryPoint");
            return null;
        }

        PlayerProgress progress = PlayerProgress.Instance;
        if (progress == null || progress.UnlockedStations.Count == 0)
        {
            Debug.LogWarning("[LevelInitializer] ยังไม่มีสถานีที่ปลดล็อกให้สุ่มใส่กล่อง");
            return null;
        }

        GameObject picked = progress.UnlockedStations[Random.Range(0, progress.UnlockedStations.Count)];
        if (picked == null) return null;

        GameObject parcelObj = Instantiate(parcelPrefab, deliveryPoint.position, deliveryPoint.rotation);

        EquipmentParcel parcel = parcelObj.GetComponent<EquipmentParcel>();
        if (parcel == null)
        {
            Debug.LogError("[LevelInitializer] parcelPrefab ไม่มี component EquipmentParcel ติดอยู่");
            Destroy(parcelObj);
            return null;
        }

        parcel.SetContents(picked, picked.name);
        parcelObj.name = $"Parcel ({picked.name})";

        Debug.Log($"[LevelInitializer] ส่งกล่อง {picked.name} มาที่จุดรับของแล้ว");
        return parcel;
    }
}
