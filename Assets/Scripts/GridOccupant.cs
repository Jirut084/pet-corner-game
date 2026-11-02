using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// ติดบนอุปกรณ์ทุกชิ้นที่ "กินพื้นที่" บนกริด (ชามอาหาร/อ่างอาบน้ำ/จุดรับ/จุดคืน ฯลฯ)
/// ทำหน้าที่ลงทะเบียนตัวเองเข้ากับ TileGrid ตอนเริ่มฉาก เพื่อให้กริดรู้จักของที่วางมือไว้ใน Scene ด้วย
/// ไม่ใช่รู้จักแค่ของที่ผู้เล่นวางตอนเล่น
///
/// ถ้าอุปกรณ์ชิ้นไหนไม่มี component นี้ TilePlacer จะมองไม่เห็น → ถอดไม่ได้ และวางทับซ้ำได้ไม่จำกัด
/// </summary>
[DisallowMultipleComponent]
public class GridOccupant : MonoBehaviour
{
    [Header("References")]
    [Tooltip("กริดที่อุปกรณ์ชิ้นนี้อยู่ ปล่อยว่างได้ จะค้นหา TileGrid ในฉากให้อัตโนมัติ")]
    [SerializeField] private TileGrid grid;

    [Header("Settings")]
    [Tooltip("ให้ผู้เล่นถอดอุปกรณ์ชิ้นนี้ออกได้หรือไม่ — ปิดไว้สำหรับจุดรับ/จุดคืน กันผู้เล่นถอดจนเล่น loop ต่อไม่ได้")]
    [SerializeField] private bool removable = true;

    [Tooltip("ย้ายตัวเองไปกึ่งกลางช่องตอนลงทะเบียน (ขยับเฉพาะแกน X/Z ความสูงเดิมคงไว้)")]
    [SerializeField] private bool snapToCellCenter = false;

    public bool Removable => removable;
    public Vector2Int Cell { get; private set; }
    public bool IsRegistered { get; private set; }

    private void Awake()
    {
        EnsureNavMeshObstacle();
    }

    /// <summary>
    /// ของทุกชิ้นที่กินช่องบนกริดเป็นสิ่งกีดขวางของ NavMesh ด้วย — สัตว์ที่หนีจะอ้อมแทนที่จะมุดทะลุ
    /// ใช้ carve เพราะผู้เล่นยก/ย้ายสถานีได้ตอนจัดร้าน พื้นที่เดินได้จึงต้องอัปเดตตามทันที
    /// (ทำที่นี่ที่เดียว ไม่ต้องไปใส่ component ทีละ prefab)
    /// </summary>
    private void EnsureNavMeshObstacle()
    {
        if (GetComponent<NavMeshObstacle>() != null) return;

        NavMeshObstacle obstacle = gameObject.AddComponent<NavMeshObstacle>();
        obstacle.shape = NavMeshObstacleShape.Box;
        obstacle.carving = true;
        obstacle.carveOnlyStationary = true;

        // ขนาดตามของจริงถ้ามี collider ไม่งั้นใช้ขนาดช่องกริด 1 ช่อง
        Collider self = GetComponent<Collider>();
        if (self != null)
        {
            obstacle.center = transform.InverseTransformPoint(self.bounds.center);
            obstacle.size = new Vector3(
                Mathf.Max(0.4f, self.bounds.size.x * 0.9f),
                Mathf.Max(0.4f, self.bounds.size.y),
                Mathf.Max(0.4f, self.bounds.size.z * 0.9f));
            return;
        }

        obstacle.center = new Vector3(0f, 0.3f, 0f);
        obstacle.size = new Vector3(0.9f, 0.8f, 0.9f);
    }

    private void Start()
    {
        // ใช้ Start ไม่ใช่ Awake เพื่อให้แน่ใจว่า TileGrid.Awake ทำงานไปก่อนแล้ว
        // ถ้า TilePlacer ลงทะเบียนให้ตั้งแต่ตอน Instantiate แล้ว ตรงนี้จะข้ามไปเอง
        if (IsRegistered) return;

        TileGrid targetGrid = grid != null ? grid : FindAnyObjectByType<TileGrid>();
        if (targetGrid == null)
        {
            Debug.LogWarning($"[GridOccupant] {name} หา TileGrid ในฉากไม่เจอ จะไม่ถูกนับว่ากินช่องบนกริด");
            return;
        }

        Register(targetGrid, targetGrid.WorldToCell(transform.position));
    }

    /// <summary>
    /// ลงทะเบียนตัวเองลงช่องที่ระบุ โดยใช้ค่า removable ที่ตั้งไว้ใน Inspector
    /// </summary>
    public bool Register(TileGrid targetGrid, Vector2Int cell)
    {
        return RegisterInternal(targetGrid, cell, null);
    }

    /// <summary>
    /// ลงทะเบียนพร้อมบังคับค่า removable ทับค่าใน Inspector
    /// TilePlacer/LevelInitializer เรียกด้วย canBeRemoved = false เพื่อล็อกสถานีถาวรตอนวางลงกริด
    /// เรียกทันทีหลัง Instantiate เพื่อให้ช่องถูกจองในเฟรมเดียวกัน ไม่ต้องรอ Start()
    /// </summary>
    public bool Register(TileGrid targetGrid, Vector2Int cell, bool canBeRemoved)
    {
        return RegisterInternal(targetGrid, cell, canBeRemoved);
    }

    private bool RegisterInternal(TileGrid targetGrid, Vector2Int cell, bool? overrideRemovable)
    {
        if (targetGrid == null) return false;

        if (!targetGrid.SetOccupant(cell, gameObject))
        {
            Debug.LogWarning($"[GridOccupant] {name} ลงทะเบียนช่อง {cell} ไม่สำเร็จ (อยู่นอกกริด หรือช่องมีของอยู่แล้ว)");
            return false;
        }

        grid = targetGrid;
        Cell = cell;
        IsRegistered = true;

        // เขียนทับ removable เฉพาะตอนลงทะเบียนสำเร็จ เพื่อไม่ให้ค่าใน Inspector เพี้ยนถ้าวางไม่ผ่าน
        if (overrideRemovable.HasValue)
        {
            removable = overrideRemovable.Value;
        }

        if (snapToCellCenter)
        {
            Vector3 center = targetGrid.CellToWorld(cell);
            transform.position = new Vector3(center.x, transform.position.y, center.z);
        }

        return true;
    }

    /// <summary>
    /// ถอนการลงทะเบียนออกจากกริด ทำให้ช่องเดิมว่างลง
    /// ใช้ตอนผู้เล่นยกสถานีขึ้นมาเพื่อย้ายไปวางที่อื่น (ดู TilePlacer)
    /// </summary>
    public void Unregister()
    {
        if (!IsRegistered) return;

        // ล้างเฉพาะกรณีที่ช่องนั้นยังเป็นของเราอยู่จริง กันไปล้างช่องของคนอื่นที่มาจองต่อ
        if (grid != null && grid.GetOccupant(Cell) == gameObject)
        {
            grid.ClearCell(Cell);
        }

        IsRegistered = false;
    }

    private void OnDestroy()
    {
        if (!IsRegistered || grid == null) return;

        // ล้างช่องเฉพาะกรณีที่ช่องนั้นยังเป็นของเราอยู่จริง
        // กันเคสที่ TilePlacer เคลียร์ช่องแล้ววางของชิ้นใหม่ทับไปก่อนที่ OnDestroy ของเราจะถูกเรียก
        // (Destroy ทำงานปลายเฟรม จึงมีช่วงคาบเกี่ยวได้)
        if (grid.GetOccupant(Cell) == gameObject)
        {
            grid.ClearCell(Cell);
        }

        IsRegistered = false;
    }
}
