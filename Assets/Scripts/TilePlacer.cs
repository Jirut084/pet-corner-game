using UnityEngine;

/// <summary>
/// ยก/วางอุปกรณ์บน TileGrid แบบ PlateUp! — ใช้ปุ่ม E ร่วมกับการโต้ตอบอื่นทั้งหมด
/// InteractionManager เรียก TryHandleInteract() ก่อนทุกครั้งที่กด E
/// ถ้าสถานการณ์ตรงกับการยก/วางอุปกรณ์ สคริปต์นี้จะรับไปทำและคืน true ให้ระบบหยุดที่นี่
///
///  - ช่วงจัดร้าน + มือว่าง + หันหน้าไปที่ช่องที่มีสถานีย้ายได้  → ยกสถานีนั้นขึ้นมา (ช่องเดิมว่างทันที)
///  - ช่วงจัดร้าน + ถือกล่องอุปกรณ์ + หันไปที่ช่องว่าง          → กล่องแปลงเป็นสถานีจริงลงช่องนั้น
///  - ช่วงจัดร้าน + ถือสถานีอยู่ + หันไปที่ช่องว่าง             → วางสถานีลงช่องใหม่
///  - นอกเหนือจากนี้ (เปิดร้านแล้ว / ถือสัตว์ / เล็งที่ว่าง)     → คืน false ให้ E ไปทำงานปกติ
///
/// สถานีที่ตั้ง GridOccupant.removable = false (เช่น จุดรับ/จุดคืน) จะยกไม่ขึ้น
/// เพื่อกันผู้เล่นรื้อจุดที่ core loop ต้องใช้จนเล่นต่อไม่ได้
/// </summary>
[RequireComponent(typeof(PlayerController))]
[RequireComponent(typeof(PlayerCarrier))]
public class TilePlacer : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private TileGrid grid;

    private PlayerController playerController;
    private PlayerCarrier carrier;

    /// <summary>กริดที่ใช้อยู่ — ระบบพรีวิวอ่านไปคำนวณตำแหน่งช่อง</summary>
    public TileGrid Grid => grid;

    /// <summary>ช่องที่ผู้เล่นเล็งอยู่ตอนนี้ คิดสดทุกครั้งที่เรียก</summary>
    public Vector2Int TargetCell => GetTargetCell();

    private void Awake()
    {
        playerController = GetComponent<PlayerController>();
        carrier = GetComponent<PlayerCarrier>();

        // ผู้เล่นออนไลน์เกิดจาก prefab ซึ่งผูกกริดในฉากไว้ไม่ได้ จึงหาเอง
        if (grid == null) grid = FindAnyObjectByType<TileGrid>();

        // ภาพตัวอย่างตำแหน่งวาง ใส่ให้เองจะได้ไม่ต้องเพิ่ม component ในฉาก
        // อยากปรับสี/ขนาด ให้ Add Component "PlacementPreview" ลงตัวผู้เล่นเองแล้วตั้งใน Inspector
        if (GetComponent<PlacementPreview>() == null)
        {
            gameObject.AddComponent<PlacementPreview>();
        }
    }

    /// <summary>
    /// InteractionManager เรียกตอนกด E — คืน true ถ้ารับไปจัดการเป็นการยก/วางอุปกรณ์แล้ว
    /// คืน false เมื่อสถานการณ์ไม่ใช่งานของกริด เพื่อให้ E ไปทำงานปกติ (รับออเดอร์/ใช้สถานี/ส่งคืน)
    /// </summary>
    public bool TryHandleInteract()
    {
        if (grid == null) return false;

        // จัดสถานีได้เฉพาะช่วง Setup — เปิดร้านแล้ว E เป็นของการดูแลสัตว์ทั้งหมด
        if (GameManager.Instance != null && !GameManager.Instance.CanArrangeStations) return false;

        Vector2Int cell = GetTargetCell();

        if (carrier.IsCarrying)
        {
            // ถือสัตว์หรือของอื่นที่ลงกริดไม่ได้ ปล่อยให้ระบบ interact จัดการต่อ
            bool holdsEquipment = carrier.GetHeld<EquipmentParcel>() != null || carrier.GetHeld<GridOccupant>() != null;
            if (!holdsEquipment) return false;

            TryPlace(cell);
            return true;
        }

        // มือว่าง: ยกได้เฉพาะตอนเล็งช่องที่มีสถานีย้ายได้จริง ไม่งั้นคืนให้ E ไปโต้ตอบตามปกติ
        GameObject target = grid.GetOccupant(cell);
        if (target == null) return false;

        GridOccupant occupant = target.GetComponent<GridOccupant>();
        if (occupant == null || !occupant.Removable) return false;

        TryPickUp(cell);
        return true;
    }

    /// <summary>ช่องติดกันด้านหน้าผู้เล่น (4 ทิศ) — ช่องเดียวกับที่ InteractionManager ใช้โต้ตอบ</summary>
    private Vector2Int GetTargetCell()
    {
        return grid.FacingCell(transform.position, playerController.LastMoveDirection);
    }

    /// <summary>ยกสถานีที่อยู่ในช่องเป้าหมายขึ้นมาถือ เพื่อย้ายไปวางที่อื่น</summary>
    private void TryPickUp(Vector2Int cell)
    {
        GameObject target = grid.GetOccupant(cell);
        if (target == null)
        {
            Debug.Log($"[TilePlacer] ช่อง {cell} ว่างอยู่ ไม่มีอะไรให้ยก");
            return;
        }

        // ต้องมี GridOccupant เท่านั้นถึงจะยกได้ ไม่งั้นยกขึ้นมาแล้วจะวางกลับลงกริดไม่ได้
        // (TryPlace หาของที่วางได้จาก GridOccupant/EquipmentParcel) กลายเป็นถือค้างถาวร
        GridOccupant occupant = target.GetComponent<GridOccupant>();
        if (occupant == null)
        {
            Debug.LogWarning($"[TilePlacer] {target.name} ไม่มี GridOccupant ยกไม่ได้");
            return;
        }

        if (!occupant.Removable)
        {
            Debug.Log($"[TilePlacer] {target.name} เป็นสถานีที่ย้ายไม่ได้");
            return;
        }

        if (!carrier.Carry(target))
        {
            return; // PlayerCarrier log เหตุผลไว้แล้ว
        }

        // ยกสำเร็จแล้วค่อยคืนช่องให้ว่าง เพื่อไม่ให้ช่องว่างค้างถ้ายกไม่ผ่าน
        occupant.Unregister();

        Debug.Log($"[TilePlacer] ยก {target.name} ขึ้นมาจากช่อง {cell} แล้ว");
    }

    /// <summary>วางของที่ถืออยู่ลงช่องเป้าหมาย — รองรับทั้งกล่องอุปกรณ์และสถานีที่ยกมา</summary>
    private void TryPlace(Vector2Int cell)
    {
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

        EquipmentParcel parcel = carrier.GetHeld<EquipmentParcel>();
        if (parcel != null)
        {
            PlaceFromParcel(parcel, cell);
            return;
        }

        GridOccupant carriedStation = carrier.GetHeld<GridOccupant>();
        if (carriedStation != null)
        {
            PlaceCarriedStation(carriedStation, cell);
            return;
        }

        // ของอย่างอื่น (เช่นสัตว์เลี้ยง) วางลงกริดไม่ได้
        Debug.Log($"[TilePlacer] {carrier.HeldObject.name} ไม่ใช่อุปกรณ์ วางลงกริดไม่ได้");
    }

    /// <summary>กล่องอุปกรณ์ → แปลงเป็นสถานีจริงลงช่อง แล้วทำลายกล่องทิ้ง</summary>
    private void PlaceFromParcel(EquipmentParcel parcel, Vector2Int cell)
    {
        // อ่านข้อมูลจากกล่องเก็บไว้ก่อน เพราะเดี๋ยวกล่องจะถูกทำลาย
        GameObject stationPrefab = parcel.StationPrefab;
        string parcelName = parcel.DisplayName;

        GameObject station = Instantiate(stationPrefab, grid.CellToWorld(cell), Quaternion.identity);
        station.name = parcelName;

        GridOccupant occupant = station.GetComponent<GridOccupant>();
        if (occupant == null)
        {
            occupant = station.AddComponent<GridOccupant>();
        }

        // สถานีที่ผู้เล่นวางเองย้ายได้เสมอ (removable = true) ตามกติกาที่ตกลงไว้
        if (!occupant.Register(grid, cell, true))
        {
            // ไม่ควรเกิดเพราะเช็ค IsOccupied ไปแล้ว แต่ถ้าเกิดจริงต้องเก็บกวาดสถานีที่สร้างค้างไว้
            // และ "ไม่" ทำลายกล่อง เพื่อให้ผู้เล่นยังถืออยู่และลองวางใหม่ได้ ไม่ใช่เสียของฟรี
            Debug.LogWarning($"[TilePlacer] ลงทะเบียนสถานีที่ช่อง {cell} ไม่สำเร็จ ยกเลิกการวาง");
            Destroy(station);
            return;
        }

        GameObject consumedParcel = carrier.Consume();
        if (consumedParcel != null)
        {
            Destroy(consumedParcel);
        }

        Debug.Log($"[TilePlacer] แกะกล่อง {parcelName} วางลงช่อง {cell} แล้ว");
    }

    /// <summary>สถานีที่ยกมาอยู่แล้ว → วางลงช่องใหม่ (คงค่า removable เดิมไว้)</summary>
    private void PlaceCarriedStation(GridOccupant station, Vector2Int cell)
    {
        GameObject released = carrier.Release(grid.CellToWorld(cell));
        if (released == null) return;

        // จัดให้ตรงแนวกริดเสมอ — ระหว่างถืออยู่ สถานีเป็นลูกของ holdPoint
        // มุมของมันจึงหมุนตามทิศที่ผู้เล่นหัน ถ้าไม่รีเซ็ตตรงนี้จะวางลงไปเบี้ยวตามตัวผู้เล่น
        released.transform.rotation = Quaternion.identity;

        if (!station.Register(grid, cell))
        {
            // ลงทะเบียนไม่ผ่าน — ยกกลับขึ้นมาถือต่อ ดีกว่าปล่อยสถานีลอยอยู่นอกกริดโดยไม่มีใครรู้จัก
            Debug.LogWarning($"[TilePlacer] ลงทะเบียน {released.name} ที่ช่อง {cell} ไม่สำเร็จ ยกกลับขึ้นมาถือต่อ");
            carrier.Carry(released);
            return;
        }

        Debug.Log($"[TilePlacer] ย้าย {released.name} มาไว้ที่ช่อง {cell} แล้ว");
    }

    // แสดงช่องเป้าหมายปัจจุบันใน Scene view: เขียว = วางได้, แดง = มีของอยู่แล้ว
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
