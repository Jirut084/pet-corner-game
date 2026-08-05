using UnityEngine;

/// <summary>
/// กริดสำหรับวางอุปกรณ์ในโซนเตรียมงาน (Preparation Phase)
/// แบ่งพื้นห้องเป็นช่อง (cell) สี่เหลี่ยมขนาดเท่ากัน และเป็นแหล่งข้อมูลจริงแหล่งเดียว (single source of truth)
/// ว่าช่องไหนมีอุปกรณ์ชิ้นไหนอยู่ — ครอบคลุมทั้งอุปกรณ์ที่วางมือไว้ใน Scene (ผ่าน GridOccupant)
/// และที่ผู้เล่นวางตอนเล่น (ผ่าน TilePlacer)
/// วางบน GameObject เปล่าที่มุมล่างซ้ายของพื้นห้อง (transform.position = origin ของกริด)
/// </summary>
public class TileGrid : MonoBehaviour
{
    [Header("Grid Size")]
    [SerializeField] private int width = 10;
    [SerializeField] private int height = 10;
    [SerializeField] private float cellSize = 1f;

    // อุปกรณ์ที่อยู่ในแต่ละช่อง (null = ช่องว่าง) index = y * width + x
    // เก็บเป็น reference ของ GameObject ตรงๆ แทน bool เพื่อให้ "ถอดของในช่องนี้" ทำได้
    // โดยไม่ต้องมีตารางจำแยกอีกชุดใน TilePlacer ซึ่งเคยทำให้ของที่วางมือไว้ใน Scene ถูกมองข้าม
    private GameObject[] occupants;

    public int Width => width;
    public int Height => height;
    public float CellSize => cellSize;

    private void Awake()
    {
        EnsureOccupantArray();
    }

    /// <summary>
    /// สร้าง array occupants ถ้ายังไม่มี (หรือขนาดไม่ตรงกับ width/height ปัจจุบัน)
    /// จำเป็นเพราะ OnDrawGizmos ของ TilePlacer อาจเรียก IsOccupied ตอนอยู่ใน Edit Mode
    /// ซึ่ง Awake() ของ MonoBehaviour ยังไม่ถูกเรียก (รันเฉพาะตอนเข้า Play Mode/instantiate)
    /// ถ้าไม่กันไว้ตรงนี้จะเกิด NullReferenceException ตอนอยู่ใน Scene view โดยไม่ได้กด Play
    /// </summary>
    private void EnsureOccupantArray()
    {
        if (occupants == null || occupants.Length != width * height)
        {
            occupants = new GameObject[width * height];
        }
    }

    private int Index(Vector2Int cell)
    {
        return cell.y * width + cell.x;
    }

    /// <summary>แปลงตำแหน่งโลกเป็นพิกัดช่อง (ปัดเศษลง ใช้แกน X/Z)</summary>
    public Vector2Int WorldToCell(Vector3 worldPos)
    {
        Vector3 local = worldPos - transform.position;
        int x = Mathf.FloorToInt(local.x / cellSize);
        int z = Mathf.FloorToInt(local.z / cellSize);
        return new Vector2Int(x, z);
    }

    /// <summary>แปลงพิกัดช่องเป็นตำแหน่งกึ่งกลางช่องในโลก (Y เท่ากับ origin ของกริด)</summary>
    public Vector3 CellToWorld(Vector2Int cell)
    {
        float x = (cell.x + 0.5f) * cellSize;
        float z = (cell.y + 0.5f) * cellSize;
        return transform.position + new Vector3(x, 0f, z);
    }

    public bool IsInBounds(Vector2Int cell)
    {
        return cell.x >= 0 && cell.x < width && cell.y >= 0 && cell.y < height;
    }

    /// <summary>ช่องนอกขอบเขตกริดถือว่า "ถูกใช้งาน" เสมอ เพื่อกันการวางอุปกรณ์หลุดกริด</summary>
    public bool IsOccupied(Vector2Int cell)
    {
        if (!IsInBounds(cell)) return true;
        return GetOccupant(cell) != null;
    }

    /// <summary>คืนอุปกรณ์ที่อยู่ในช่องที่ระบุ (null ถ้าช่องว่างหรืออยู่นอกกริด)</summary>
    public GameObject GetOccupant(Vector2Int cell)
    {
        EnsureOccupantArray();
        if (!IsInBounds(cell)) return null;

        int index = Index(cell);

        // ถ้าของในช่องถูก Destroy จากที่อื่นโดยไม่ผ่าน ClearCell, Unity จะทำให้ reference เทียบเท่า null
        // ล้างให้เป็น null จริงตรงนี้ ช่องจะได้กลับมาว่างเอง ไม่ค้างสถานะ "ไม่ว่าง" ถาวร
        if (occupants[index] == null)
        {
            occupants[index] = null;
        }

        return occupants[index];
    }

    /// <summary>
    /// จองช่องให้อุปกรณ์ชิ้นที่ระบุ
    /// คืน false ถ้าช่องอยู่นอกกริดหรือมีของอยู่แล้ว (ผู้เรียกต้องเช็คผลลัพธ์เสมอ)
    /// </summary>
    public bool SetOccupant(Vector2Int cell, GameObject occupant)
    {
        if (!IsInBounds(cell)) return false;
        if (IsOccupied(cell)) return false;

        occupants[Index(cell)] = occupant;
        return true;
    }

    /// <summary>ล้างช่องให้ว่าง — ไม่ Destroy ตัว GameObject ผู้เรียกต้องจัดการเอง</summary>
    public void ClearCell(Vector2Int cell)
    {
        EnsureOccupantArray();
        if (!IsInBounds(cell)) return;

        occupants[Index(cell)] = null;
    }

    // วาดเส้นกริดใน Scene view เพื่อช่วยจัดตำแหน่งตอน design ห้อง
    // ตอน Play จะระบายช่องที่มีของอยู่เป็นสีแดงด้วย เพื่อเช็คว่าอุปกรณ์ลงทะเบียนครบหรือยัง
    private void OnDrawGizmos()
    {
        Gizmos.color = Color.gray;

        for (int x = 0; x <= width; x++)
        {
            Vector3 from = transform.position + new Vector3(x * cellSize, 0f, 0f);
            Vector3 to = transform.position + new Vector3(x * cellSize, 0f, height * cellSize);
            Gizmos.DrawLine(from, to);
        }

        for (int z = 0; z <= height; z++)
        {
            Vector3 from = transform.position + new Vector3(0f, 0f, z * cellSize);
            Vector3 to = transform.position + new Vector3(width * cellSize, 0f, z * cellSize);
            Gizmos.DrawLine(from, to);
        }

        if (!Application.isPlaying || occupants == null) return;

        Gizmos.color = new Color(1f, 0f, 0f, 0.35f);

        for (int z = 0; z < height; z++)
        {
            for (int x = 0; x < width; x++)
            {
                Vector2Int cell = new Vector2Int(x, z);
                if (!IsOccupied(cell)) continue;

                Gizmos.DrawCube(CellToWorld(cell) + Vector3.up * 0.02f,
                    new Vector3(cellSize * 0.9f, 0.04f, cellSize * 0.9f));
            }
        }
    }
}
