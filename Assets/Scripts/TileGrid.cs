using UnityEngine;

/// <summary>
/// กริดสำหรับวางอุปกรณ์ในโซนเตรียมงาน (Preparation Phase)
/// แบ่งพื้นห้องเป็นช่อง (cell) สี่เหลี่ยมขนาดเท่ากัน เก็บสถานะว่าช่องไหนถูกใช้งานแล้ว
/// วางบน GameObject เปล่าที่มุมล่างซ้ายของพื้นห้อง (transform.position = origin ของกริด)
/// </summary>
public class TileGrid : MonoBehaviour
{
    [Header("Grid Size")]
    [SerializeField] private int width = 10;
    [SerializeField] private int height = 10;
    [SerializeField] private float cellSize = 1f;

    // เก็บว่าช่องไหนถูกจองแล้ว (index = y * width + x)
    private bool[] occupied;

    public int Width => width;
    public int Height => height;
    public float CellSize => cellSize;

    private void Awake()
    {
        EnsureOccupiedArray();
    }

    /// <summary>
    /// สร้าง array occupied ถ้ายังไม่มี (หรือขนาดไม่ตรงกับ width/height ปัจจุบัน)
    /// จำเป็นเพราะ OnDrawGizmos ของ TilePlacer อาจเรียก IsOccupied ตอนอยู่ใน Edit Mode
    /// ซึ่ง Awake() ของ MonoBehaviour ยังไม่ถูกเรียก (รันเฉพาะตอนเข้า Play Mode/instantiate)
    /// ถ้าไม่กันไว้ตรงนี้จะเกิด NullReferenceException ตอนอยู่ใน Scene view โดยไม่ได้กด Play
    /// </summary>
    private void EnsureOccupiedArray()
    {
        if (occupied == null || occupied.Length != width * height)
        {
            occupied = new bool[width * height];
        }
    }

    /// <summary>แปลงตำแหน่งโลกเป็นพิกัดช่อง (ปัดเศษลง ใช้แกน X/Z)</summary>
    public Vector2Int WorldToCell(Vector3 worldPos)
    {
        Vector3 local = worldPos - transform.position;
        int x = Mathf.FloorToInt(local.x / cellSize);
        int z = Mathf.FloorToInt(local.z / cellSize);
        return new Vector2Int(x, z);
    }

    /// <summary>แปลงพิกัดช่องเป็นตำแหน่งกึ่งกลางช่องในโลก (Y เท่ากับ origin)</summary>
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
        EnsureOccupiedArray();
        if (!IsInBounds(cell)) return true;
        return occupied[cell.y * width + cell.x];
    }

    public void SetOccupied(Vector2Int cell, bool value)
    {
        EnsureOccupiedArray();
        if (!IsInBounds(cell)) return;
        occupied[cell.y * width + cell.x] = value;
    }

    // วาดเส้นกริดใน Scene view เพื่อช่วยจัดตำแหน่งตอน design ห้อง
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
    }
}
