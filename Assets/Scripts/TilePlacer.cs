using UnityEngine;

/// <summary>
/// วางอุปกรณ์ลงบน TileGrid ในช่วง Preparation Phase (อ้างอิงกลไกจาก PlateUp!)
///
/// ผู้เล่นต้องถือ EquipmentParcel อยู่ในมือ แล้วกด placeKey ขณะหันหน้าไปยังช่องว่าง
/// กล่องจะถูกแปลงเป็นสถานีจริงและ "ล็อกถาวร" — ย้าย ถอด หรือยกซ้ำไม่ได้อีก
/// จึงไม่มีปุ่มถอดอุปกรณ์ในระบบนี้แล้วโดยตั้งใจ (ต่างจากเวอร์ชันก่อนหน้า)
/// </summary>
[RequireComponent(typeof(PlayerController))]
[RequireComponent(typeof(PlayerCarrier))]
public class TilePlacer : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private TileGrid grid;

    [Header("Placement Settings")]
    [Tooltip("ระยะห่างจากผู้เล่นที่จะเล็งช่องวาง (หน่วยเมตร)")]
    [SerializeField] private float placeDistance = 1.5f;

    [Header("Input")]
    [SerializeField] private KeyCode placeKey = KeyCode.G;

    private PlayerController playerController;
    private PlayerCarrier carrier;

    private void Awake()
    {
        playerController = GetComponent<PlayerController>();
        carrier = GetComponent<PlayerCarrier>();
    }

    private void Update()
    {
        if (grid == null) return;
        if (!Input.GetKeyDown(placeKey)) return;

        TryPlace(GetTargetCell());
    }

    /// <summary>หาช่องกริดที่อยู่ด้านหน้าผู้เล่นตามทิศที่หันล่าสุด</summary>
    private Vector2Int GetTargetCell()
    {
        Vector3 facing = playerController.LastMoveDirection.normalized;
        Vector3 targetPos = transform.position + facing * placeDistance;
        return grid.WorldToCell(targetPos);
    }

    private void TryPlace(Vector2Int cell)
    {
        EquipmentParcel parcel = carrier.GetHeld<EquipmentParcel>();
        if (parcel == null)
        {
            Debug.Log("[TilePlacer] ต้องถือกล่องอุปกรณ์อยู่ในมือก่อนถึงจะวางได้");
            return;
        }

        if (!grid.IsInBounds(cell))
        {
            Debug.Log($"[TilePlacer] ช่อง {cell} อยู่นอกกริด วางไม่ได้");
            return;
        }

        if (grid.IsOccupied(cell))
        {
            Debug.Log($"[TilePlacer] ช่อง {cell} มีอุปกรณ์อยู่แล้ว วางไม่ได้");
            return;
        }

        // อ่านข้อมูลจากกล่องเก็บไว้ก่อน เพราะเดี๋ยวกล่องจะถูกทำลาย
        GameObject stationPrefab = parcel.StationPrefab;
        string parcelName = parcel.DisplayName;

        GameObject station = Instantiate(stationPrefab, grid.CellToWorld(cell), Quaternion.identity);

        GridOccupant occupant = station.GetComponent<GridOccupant>();
        if (occupant == null)
        {
            occupant = station.AddComponent<GridOccupant>();
        }

        // removable = false คือหัวใจของกติกา "วางได้ครั้งเดียว" — ลงไปแล้วล็อกถาวร
        if (!occupant.Register(grid, cell, false))
        {
            // ไม่ควรเกิดเพราะเช็ค IsOccupied ไปแล้ว แต่ถ้าเกิดจริงต้องเก็บกวาดสถานีที่สร้างค้างไว้
            // และ "ไม่" ทำลายกล่อง เพื่อให้ผู้เล่นยังถืออยู่และลองวางใหม่ได้ ไม่ใช่เสียของฟรี
            Debug.LogWarning($"[TilePlacer] ลงทะเบียนสถานีที่ช่อง {cell} ไม่สำเร็จ ยกเลิกการวาง");
            Destroy(station);
            return;
        }

        // วางสำเร็จแล้วค่อยเอากล่องออกจากมือและทำลายทิ้ง
        GameObject consumedParcel = carrier.Consume();
        if (consumedParcel != null)
        {
            Destroy(consumedParcel);
        }

        Debug.Log($"[TilePlacer] วาง {parcelName} ที่ช่อง {cell} เรียบร้อย (ล็อกถาวร ย้ายไม่ได้อีก)");
    }

    // แสดงช่องเป้าหมายปัจจุบันใน Scene view: เขียว = วางได้, แดง = วางไม่ได้
    private void OnDrawGizmos()
    {
        if (grid == null) return;

        if (playerController == null)
        {
            playerController = GetComponent<PlayerController>();
            if (playerController == null) return;
        }

        Vector2Int cell = GetTargetCell();
        Vector3 center = grid.CellToWorld(cell);

        Gizmos.color = grid.IsOccupied(cell) ? Color.red : Color.green;
        Gizmos.DrawWireCube(center + Vector3.up * 0.05f, new Vector3(grid.CellSize, 0.1f, grid.CellSize));
    }
}
