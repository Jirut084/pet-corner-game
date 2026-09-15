using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// ช่วยจัดอุปกรณ์บนกริดใน Scene view — ใช้กับ IntakePoint / ReturnZone / สถานีที่วางไว้ในฉาก
///
///   - ลากด้วย Move tool แล้วปล่อยเมาส์ → ดูดเข้ากลางช่องให้เอง (เมื่อเปิด snapToCellCenter)
///   - กรอบช่อง + ป้ายพิกัดลอยเหนือ object: เขียว = ใช้ได้, แดง = นอกกริดหรือทับช่องกับอุปกรณ์ชิ้นอื่น
///   - Inspector บอกช่องที่จะลงทะเบียนตอนกด Play
///
/// ช่องที่เห็นตรงนี้คือช่องเดียวกับที่ LevelInitializer ลงทะเบียนให้ตอนเริ่มเกม
/// ไม่ทำงานตอนกด Play และตอนเปิดแก้ใน Prefab Mode (ไม่มีกริดของฉากให้เทียบ)
/// </summary>
[CustomEditor(typeof(GridOccupant))]
[CanEditMultipleObjects]
public class GridOccupantEditor : Editor
{
    private static readonly Color OkColor = new Color(0.35f, 0.95f, 0.45f);
    private static readonly Color ErrorColor = new Color(1f, 0.35f, 0.35f);

    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        if (targets.Length != 1 || Application.isPlaying) return;

        GridOccupant occupant = (GridOccupant)target;
        if (!IsSceneObject(occupant)) return;

        TileGrid grid = FindGrid(occupant);
        if (grid == null)
        {
            EditorGUILayout.HelpBox("ไม่พบ TileGrid ในฉาก", MessageType.Info);
            return;
        }

        Vector2Int cell = grid.WorldToCell(occupant.transform.position);

        if (!grid.IsInBounds(cell))
        {
            EditorGUILayout.HelpBox($"อยู่นอกกริด (ช่อง {cell.x}, {cell.y}) — ตอนกด Play จะลงทะเบียนไม่ได้", MessageType.Warning);
            return;
        }

        string conflict = FindConflict(occupant, grid, cell);
        if (conflict != null)
        {
            EditorGUILayout.HelpBox($"ช่อง ({cell.x}, {cell.y}) ทับกับ {conflict} — ตอนกด Play จะลงทะเบียนได้แค่ชิ้นเดียว", MessageType.Warning);
            return;
        }

        EditorGUILayout.HelpBox($"จะลงทะเบียนที่ช่อง ({cell.x}, {cell.y}) ตอนกด Play", MessageType.Info);
    }

    private void OnSceneGUI()
    {
        if (Application.isPlaying) return; // ตอนเล่น TilePlacer / LevelInitializer คุมตำแหน่งเอง

        GridOccupant occupant = (GridOccupant)target;
        if (!IsSceneObject(occupant)) return;

        TileGrid grid = FindGrid(occupant);
        if (grid == null) return;

        Transform occupantTransform = occupant.transform;
        Vector2Int cell = grid.WorldToCell(occupantTransform.position);
        bool inBounds = grid.IsInBounds(cell);

        // ดูดเข้ากลางช่องตอนปล่อยเมาส์แล้วเท่านั้น (hotControl == 0) ระหว่างลากจะได้ลื่น ไม่กระตุกเป็นช่องๆ
        if (inBounds && GUIUtility.hotControl == 0 && SnapsToCell(occupant))
        {
            Vector3 center = grid.CellToWorld(cell);
            Vector3 snapped = new Vector3(center.x, occupantTransform.position.y, center.z);

            if ((occupantTransform.position - snapped).sqrMagnitude > 0.000001f)
            {
                Undo.RecordObject(occupantTransform, "Snap To Grid Cell");
                occupantTransform.position = snapped;
            }
        }

        string conflict = inBounds ? FindConflict(occupant, grid, cell) : null;
        Color color = inBounds && conflict == null ? OkColor : ErrorColor;

        Handles.color = color;
        Handles.DrawWireCube(grid.CellToWorld(cell) + Vector3.up * 0.05f, new Vector3(grid.CellSize, 0.1f, grid.CellSize));

        GUIStyle style = new GUIStyle(EditorStyles.boldLabel);
        style.normal.textColor = color;

        string text = !inBounds ? $"{occupant.name}: นอกกริด"
            : conflict != null ? $"{occupant.name}: ทับกับ {conflict}"
            : $"{occupant.name}  ช่อง ({cell.x}, {cell.y})";

        Handles.Label(occupantTransform.position + Vector3.up * 1.5f, text, style);
    }

    /// <summary>เป็น object ในฉากจริง — ไม่ใช่ prefab asset และไม่ได้อยู่ใน Prefab Mode</summary>
    private static bool IsSceneObject(GridOccupant occupant)
    {
        if (occupant == null || EditorUtility.IsPersistent(occupant)) return false;
        return PrefabStageUtility.GetCurrentPrefabStage() == null;
    }

    private static bool SnapsToCell(GridOccupant occupant)
    {
        SerializedProperty snap = new SerializedObject(occupant).FindProperty("snapToCellCenter");
        return snap != null && snap.boolValue;
    }

    private static TileGrid FindGrid(GridOccupant occupant)
    {
        SerializedProperty gridProperty = new SerializedObject(occupant).FindProperty("grid");
        TileGrid grid = gridProperty != null ? gridProperty.objectReferenceValue as TileGrid : null;
        return grid != null ? grid : Object.FindAnyObjectByType<TileGrid>();
    }

    /// <summary>ชื่ออุปกรณ์ชิ้นอื่นในฉากที่อยู่ช่องเดียวกัน (null ถ้าไม่ทับ)</summary>
    private static string FindConflict(GridOccupant occupant, TileGrid grid, Vector2Int cell)
    {
        foreach (GridOccupant other in Object.FindObjectsByType<GridOccupant>())
        {
            if (other == occupant) continue;
            if (grid.WorldToCell(other.transform.position) == cell) return other.name;
        }

        return null;
    }
}
