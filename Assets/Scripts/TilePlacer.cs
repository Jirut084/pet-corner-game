using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// ให้ผู้เล่นวาง/ถอดอุปกรณ์ลงบน TileGrid (สำหรับช่วง Preparation Phase)
/// กด placeKey เพื่อวางอุปกรณ์ที่เลือกอยู่ลงช่องด้านหน้าผู้เล่น (ถ้าช่องว่าง)
/// กด removeKey เพื่อถอดอุปกรณ์ในช่องนั้นออก (ถ้ามี)
/// </summary>
[RequireComponent(typeof(PlayerController))]
public class TilePlacer : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private TileGrid grid;

    [Header("Placement Settings")]
    [Tooltip("อุปกรณ์ที่จะวาง (prefab ใดก็ได้ ไม่บังคับ component พิเศษ)")]
    [SerializeField] private GameObject equipmentPrefab;
    [Tooltip("ระยะห่างจากผู้เล่นที่จะเล็งวาง/ถอด (หน่วยเมตร)")]
    [SerializeField] private float placeDistance = 1.5f;

    [Header("Input")]
    [SerializeField] private KeyCode placeKey = KeyCode.G;
    [SerializeField] private KeyCode removeKey = KeyCode.H;

    private PlayerController playerController;

    // เก็บ mapping ระหว่างช่องกริดกับ GameObject ที่วางอยู่ เพื่อถอดออกได้ถูกตัว
    private readonly Dictionary<Vector2Int, GameObject> placedObjects = new Dictionary<Vector2Int, GameObject>();

    private void Awake()
    {
        playerController = GetComponent<PlayerController>();
    }

    private void Update()
    {
        if (grid == null) return;

        Vector2Int targetCell = GetTargetCell();

        if (Input.GetKeyDown(placeKey))
        {
            TryPlace(targetCell);
        }
        else if (Input.GetKeyDown(removeKey))
        {
            TryRemove(targetCell);
        }
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
        if (equipmentPrefab == null)
        {
            Debug.LogWarning("[TilePlacer] ยังไม่ได้เลือก equipmentPrefab");
            return;
        }

        if (grid.IsOccupied(cell))
        {
            Debug.Log($"[TilePlacer] ช่อง {cell} ถูกใช้งานแล้ว วางไม่ได้");
            return;
        }

        Vector3 worldPos = grid.CellToWorld(cell);
        GameObject placed = Instantiate(equipmentPrefab, worldPos, Quaternion.identity);

        grid.SetOccupied(cell, true);
        placedObjects[cell] = placed;

        Debug.Log($"[TilePlacer] วางอุปกรณ์ที่ช่อง {cell}");
    }

    private void TryRemove(Vector2Int cell)
    {
        if (!placedObjects.TryGetValue(cell, out GameObject placed) || placed == null)
        {
            Debug.Log($"[TilePlacer] ไม่มีอุปกรณ์ในช่อง {cell}");
            return;
        }

        Destroy(placed);
        placedObjects.Remove(cell);
        grid.SetOccupied(cell, false);

        Debug.Log($"[TilePlacer] ถอดอุปกรณ์ที่ช่อง {cell} แล้ว");
    }

    // แสดงช่องเป้าหมายปัจจุบันใน Scene view: เขียว = วางได้, แดง = ถูกใช้งานแล้ว
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
