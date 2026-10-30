using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// ตัวตรวจผังร้าน (GDD ก้าว 5.1) — เช็คว่าผู้เล่นเดินไปถึงหน้าสถานี/เคาน์เตอร์ทุกชิ้นได้จริงไหม
/// ผู้เล่นจัดร้านอิสระในช่วง Setup จึงวางสถานีล้อมจนบางชิ้นเดินไปไม่ถึงได้ ถ้าเปิดร้านไปแบบนั้นจะทำออเดอร์ไม่ได้ทั้งวัน
///
/// วิธีคิด: flood fill บนกริดจากช่องที่ผู้เล่นยืน เดินได้เฉพาะช่องว่าง (ไม่ถูกบล็อก ไม่มีของ) ไปทาง 4 ทิศ
/// (ไม่นับแนวทแยง ตัวละครมุดผ่านมุมของสองชิ้นที่ชนกันไม่ได้)
/// ของที่ใช้งานได้ (IInteractable) ต้องมีช่องข้างๆ อย่างน้อย 1 ช่องที่เดินถึง ส่วนของตกแต่งไม่ต้องเช็ค
/// ponytail: ดูแค่กริด ไม่ได้ยิง physics — ถ้ามีสิ่งกีดขวางที่ไม่ได้ลงกริด (เช่นผนังที่ไม่ได้ใส่ blockedCells) จะมองไม่เห็น
/// </summary>
public static class LayoutValidator
{
    private static readonly Vector2Int[] Directions =
    {
        Vector2Int.up, Vector2Int.down, Vector2Int.left, Vector2Int.right
    };

    /// <summary>
    /// ของที่ใช้งานได้แต่เดินไปไม่ถึงจากตำแหน่ง start — ลิสต์ว่าง = ผังผ่าน
    /// คืน null ถ้าตรวจไม่ได้ (ตำแหน่งเริ่มอยู่นอกกริดหรือยืนทับของ) ผู้เรียกควรปล่อยผ่าน ไม่ใช่ล็อกเกม
    /// </summary>
    public static List<GameObject> FindUnreachable(TileGrid grid, Vector3 start)
    {
        Vector2Int origin = grid.WorldToCell(start);
        if (grid.IsOccupied(origin)) return null;

        HashSet<Vector2Int> reached = new HashSet<Vector2Int> { origin };
        Queue<Vector2Int> frontier = new Queue<Vector2Int>();
        frontier.Enqueue(origin);

        while (frontier.Count > 0)
        {
            Vector2Int cell = frontier.Dequeue();
            foreach (Vector2Int step in Directions)
            {
                Vector2Int next = cell + step;
                if (grid.IsOccupied(next) || !reached.Add(next)) continue;
                frontier.Enqueue(next);
            }
        }

        List<GameObject> unreachable = new List<GameObject>();
        for (int y = 0; y < grid.Height; y++)
        {
            for (int x = 0; x < grid.Width; x++)
            {
                Vector2Int cell = new Vector2Int(x, y);
                GameObject occupant = grid.GetOccupant(cell);
                if (occupant == null || occupant.GetComponentInChildren<IInteractable>() == null) continue;

                if (!HasReachableNeighbour(cell, reached)) unreachable.Add(occupant);
            }
        }

        return unreachable;
    }

    private static bool HasReachableNeighbour(Vector2Int cell, HashSet<Vector2Int> reached)
    {
        foreach (Vector2Int step in Directions)
        {
            if (reached.Contains(cell + step)) return true;
        }

        return false;
    }
}
