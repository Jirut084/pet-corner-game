using UnityEngine;

/// <summary>
/// ตั้งค่าเริ่มต้นของด่านตอนเริ่มฉาก:
/// 1) วางสถานีหลักที่ขาดไม่ได้ (จุดรับ / จุดคืน / จุดสนอง need) ลงพิกัดกริดที่กำหนดไว้ตายตัว
/// 2) ส่งกล่องอุปกรณ์แบบสุ่มมาที่จุดรับของ ให้ผู้เล่นเดินไปยกมาวางเองในช่วง Preparation Phase
///
/// สถานีที่วางจากที่นี่จะถูกล็อกถาวรทั้งหมด (removable = false) ผู้เล่นย้ายหรือถอดไม่ได้
/// </summary>
public class LevelInitializer : MonoBehaviour
{
    /// <summary>สถานี 1 ชิ้นที่ต้องมีตั้งแต่เริ่มเกม พร้อมพิกัดช่องที่จะวาง</summary>
    [System.Serializable]
    public class DefaultStation
    {
        [Tooltip("ชื่อไว้ดูใน Inspector เฉยๆ ไม่มีผลกับ logic")]
        public string label;

        [Tooltip("Prefab ของสถานี (IntakePoint / ReturnZone / NeedZone ฯลฯ)")]
        public GameObject prefab;

        [Tooltip("พิกัดช่องบนกริดที่จะวาง — x = แกน X, y = แกน Z")]
        public Vector2Int cell;
    }

    [Header("References")]
    [Tooltip("กริดของด่านนี้ ปล่อยว่างได้ จะค้นหา TileGrid ในฉากให้อัตโนมัติ")]
    [SerializeField] private TileGrid grid;

    [Header("1) สถานีเริ่มต้น (วางตายตัวตอนเริ่มเกม)")]
    [SerializeField] private DefaultStation[] defaultStations;

    [Header("2) กล่องอุปกรณ์แบบสุ่ม")]
    [Tooltip("Prefab กล่องเปล่า ต้องมี component EquipmentParcel + Collider ติดอยู่")]
    [SerializeField] private GameObject parcelPrefab;

    [Tooltip("คลังอุปกรณ์ที่สุ่มได้ — กล่องจะสุ่มหยิบ 1 ชิ้นจากลิสต์นี้")]
    [SerializeField] private GameObject[] equipmentPool;

    [Tooltip("จุดที่กล่องอุปกรณ์จะถูกส่งมาวาง")]
    [SerializeField] private Transform deliveryPoint;

    [Tooltip("ส่งกล่องแรกให้ทันทีตอนเริ่มฉากหรือไม่")]
    [SerializeField] private bool spawnParcelOnStart = true;

    private void Start()
    {
        if (grid == null)
        {
            grid = FindFirstObjectByType<TileGrid>();
        }

        if (grid == null)
        {
            Debug.LogError("[LevelInitializer] หา TileGrid ในฉากไม่เจอ วางสถานีเริ่มต้นไม่ได้");
            return;
        }

        SpawnDefaultStations();

        if (spawnParcelOnStart)
        {
            SpawnRandomParcel();
        }
    }

    private void SpawnDefaultStations()
    {
        if (defaultStations == null) return;

        foreach (DefaultStation entry in defaultStations)
        {
            if (entry == null || entry.prefab == null)
            {
                Debug.LogWarning("[LevelInitializer] มีช่อง Default Station ที่ยังไม่ได้ใส่ prefab ข้ามไป");
                continue;
            }

            SpawnStationAt(entry.prefab, entry.cell, entry.label);
        }
    }

    /// <summary>
    /// วางสถานีลงช่องที่กำหนดแล้วล็อกถาวร คืน GameObject ที่สร้าง (null ถ้าวางไม่สำเร็จ)
    /// เปิดเป็น public ไว้ให้ระบบอื่นเรียกใช้ต่อได้ เช่น ตอนเริ่มวันใหม่ใน Day Cycle
    /// </summary>
    public GameObject SpawnStationAt(GameObject prefab, Vector2Int cell, string label = null)
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

        // สถานีหลักล็อกถาวรเสมอ ผู้เล่นถอดทิ้งจนเล่น loop ต่อไม่ได้ไม่ได้
        if (!occupant.Register(grid, cell, false))
        {
            Debug.LogWarning($"[LevelInitializer] {stationName}: ลงทะเบียนช่อง {cell} ไม่สำเร็จ ยกเลิก");
            Destroy(station);
            return null;
        }

        Debug.Log($"[LevelInitializer] วาง {stationName} ที่ช่อง {cell} แล้ว");
        return station;
    }

    /// <summary>
    /// สุ่มอุปกรณ์ 1 ชิ้นจากคลัง แล้วส่งมาเป็นกล่องที่จุดรับของ
    /// คืน EquipmentParcel ที่สร้าง (null ถ้าตั้งค่าไม่ครบ)
    /// </summary>
    public EquipmentParcel SpawnRandomParcel()
    {
        if (parcelPrefab == null)
        {
            Debug.LogWarning("[LevelInitializer] ยังไม่ได้ตั้งค่า parcelPrefab");
            return null;
        }

        if (equipmentPool == null || equipmentPool.Length == 0)
        {
            Debug.LogWarning("[LevelInitializer] equipmentPool ว่างเปล่า ไม่มีอุปกรณ์ให้สุ่ม");
            return null;
        }

        if (deliveryPoint == null)
        {
            Debug.LogWarning("[LevelInitializer] ยังไม่ได้ตั้งค่า deliveryPoint");
            return null;
        }

        GameObject picked = equipmentPool[Random.Range(0, equipmentPool.Length)];
        if (picked == null)
        {
            Debug.LogWarning("[LevelInitializer] สุ่มได้ช่องว่างใน equipmentPool ตรวจสอบลิสต์ใน Inspector");
            return null;
        }

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
