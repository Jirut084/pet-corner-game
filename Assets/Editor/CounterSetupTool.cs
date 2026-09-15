using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// ย้ายเคาน์เตอร์หลัก (IntakePoint / ReturnZone) จาก "สร้างตอนกด Play ตามพิกัดใน LevelInitializer"
/// มาเป็น object จริงในฉาก ที่มองเห็นและลากย้ายได้ใน Scene view
/// เปิดจาก Tools → Pet Corner → Move Counters Into Scene
///
/// สิ่งที่ทำ (Ctrl+Z ครั้งเดียวย้อนได้ทั้งหมด):
///   1) วาง prefab ของแต่ละรายการใน Fixed Counters ลงฉากที่ช่องเดิม (ข้ามถ้าในฉากมีชนิดนั้นอยู่แล้ว)
///   2) ใส่ GridOccupant ตั้ง removable ตามค่า movable เดิม + เปิด snapToCellCenter
///   3) สร้างลูก "CustomerStandPoint" ใต้ IntakePoint ที่ตำแหน่ง counterPoint เดิมของ CustomerManager
///      แล้วผูกเข้า IntakePoint — ลูกค้ายืนที่เดิมเป๊ะ และย้ายตามเคาน์เตอร์ตั้งแต่นี้ไป
///   4) ล้างรายการ Fixed Counters ใน LevelInitializer ให้ Inspector ตรงกับของที่อยู่ในฉากจริง
///
/// รันซ้ำได้ปลอดภัย ของที่มีในฉากแล้วจะถูกข้าม — ต้องกด Save ฉากเอง (Ctrl+S)
/// </summary>
public static class CounterSetupTool
{
    private const string StandPointName = "CustomerStandPoint";

    [MenuItem("Tools/Pet Corner/Move Counters Into Scene")]
    public static void MoveCountersIntoScene()
    {
        if (Application.isPlaying)
        {
            EditorUtility.DisplayDialog("Pet Corner", "ออกจาก Play Mode ก่อน แล้วค่อยรันเครื่องมือนี้", "ตกลง");
            return;
        }

        LevelInitializer initializer = Object.FindAnyObjectByType<LevelInitializer>();
        if (initializer == null)
        {
            EditorUtility.DisplayDialog("Pet Corner", "ไม่พบ LevelInitializer ในฉากที่เปิดอยู่ (เปิดฉาก Topdown ก่อน)", "ตกลง");
            return;
        }

        SerializedObject initializerObject = new SerializedObject(initializer);
        TileGrid grid = initializerObject.FindProperty("grid").objectReferenceValue as TileGrid;
        if (grid == null) grid = Object.FindAnyObjectByType<TileGrid>();

        if (grid == null)
        {
            EditorUtility.DisplayDialog("Pet Corner", "ไม่พบ TileGrid ในฉาก", "ตกลง");
            return;
        }

        Undo.IncrementCurrentGroup();
        int undoGroup = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("Move Counters Into Scene");

        List<string> report = new List<string>();
        SerializedProperty counters = initializerObject.FindProperty("fixedCounters");

        for (int i = 0; i < counters.arraySize; i++)
        {
            SerializedProperty entry = counters.GetArrayElementAtIndex(i);
            GameObject prefab = entry.FindPropertyRelative("prefab").objectReferenceValue as GameObject;
            string label = entry.FindPropertyRelative("label").stringValue;
            Vector2Int cell = entry.FindPropertyRelative("cell").vector2IntValue;
            bool movable = entry.FindPropertyRelative("movable").boolValue;

            if (prefab == null)
            {
                report.Add($"• รายการที่ {i + 1}: ไม่มี prefab ข้าม");
                continue;
            }

            string displayName = string.IsNullOrEmpty(label) ? prefab.name : label;

            if (SceneHasCounterLike(prefab))
            {
                report.Add($"• {displayName}: มีในฉากอยู่แล้ว ข้าม");
                continue;
            }

            PlaceCounter(prefab, displayName, grid, cell, movable, initializer.gameObject.scene);
            report.Add($"• {displayName}: วางลงฉากที่ช่อง ({cell.x}, {cell.y})");
        }

        counters.arraySize = 0;
        initializerObject.ApplyModifiedProperties();

        IntakePoint intake = Object.FindAnyObjectByType<IntakePoint>();
        report.Add(intake != null
            ? BindCustomerStandPoint(intake)
            : "• ไม่พบ IntakePoint ในฉาก — ข้ามการผูกจุดยืนลูกค้า");

        EditorSceneManager.MarkSceneDirty(initializer.gameObject.scene);
        Undo.CollapseUndoOperations(undoGroup);

        if (intake != null) Selection.activeGameObject = intake.gameObject;

        EditorUtility.DisplayDialog("Pet Corner — ย้ายเคาน์เตอร์เสร็จแล้ว",
            string.Join("\n", report) +
            "\n\nลากย้าย IntakePoint / ReturnZone ใน Scene view ได้เลย แล้วกด Save ฉาก (Ctrl+S)",
            "ตกลง");
    }

    private static void PlaceCounter(GameObject prefab, string displayName, TileGrid grid, Vector2Int cell, bool movable, Scene scene)
    {
        GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
        Undo.RegisterCreatedObjectUndo(instance, "Place Counter");

        instance.name = displayName;

        // ตำแหน่งเดียวกับที่ LevelInitializer เคยสร้างตอนกด Play (กลางช่อง, ความสูงเท่ากริด, ไม่หมุน)
        instance.transform.SetPositionAndRotation(grid.CellToWorld(cell), Quaternion.identity);

        GridOccupant occupant = instance.GetComponent<GridOccupant>();
        if (occupant == null) occupant = Undo.AddComponent<GridOccupant>(instance);

        SerializedObject occupantObject = new SerializedObject(occupant);
        occupantObject.FindProperty("removable").boolValue = movable;
        occupantObject.FindProperty("snapToCellCenter").boolValue = true;
        occupantObject.ApplyModifiedProperties();
    }

    /// <summary>ในฉากมีเคาน์เตอร์ชนิดเดียวกับ prefab นี้อยู่แล้วหรือยัง (ใช้กฎเดียวกับ LevelInitializer)</summary>
    private static bool SceneHasCounterLike(GameObject prefab)
    {
        if (prefab.GetComponent<IntakePoint>() != null) return Object.FindAnyObjectByType<IntakePoint>() != null;
        if (prefab.GetComponent<ReturnZone>() != null) return Object.FindAnyObjectByType<ReturnZone>() != null;
        return false;
    }

    private static string BindCustomerStandPoint(IntakePoint intake)
    {
        SerializedObject intakeObject = new SerializedObject(intake);
        SerializedProperty standProperty = intakeObject.FindProperty("customerStandPoint");

        if (standProperty.objectReferenceValue != null)
        {
            return "• IntakePoint มีจุดยืนลูกค้าผูกไว้แล้ว ไม่แตะ";
        }

        // ใช้ตำแหน่ง counterPoint เดิมของ CustomerManager ลูกค้าจะยืนที่เดิมเป๊ะ
        // ถ้าไม่มี ใช้ตำแหน่งจากระยะ customerStandOffset ของ IntakePoint แทน
        Vector3 position = intake.CustomerStandPosition;
        Transform oldCounter = null;

        CustomerManager manager = Object.FindAnyObjectByType<CustomerManager>();
        if (manager != null)
        {
            oldCounter = new SerializedObject(manager).FindProperty("counterPoint").objectReferenceValue as Transform;
            if (oldCounter != null) position = oldCounter.position;
        }

        GameObject standPoint = new GameObject(StandPointName);
        Undo.RegisterCreatedObjectUndo(standPoint, "Create Customer Stand Point");
        Undo.SetTransformParent(standPoint.transform, intake.transform, "Parent Customer Stand Point");
        standPoint.transform.position = position;

        standProperty.objectReferenceValue = standPoint.transform;
        intakeObject.ApplyModifiedProperties();

        return oldCounter != null
            ? $"• สร้าง {StandPointName} ใต้ IntakePoint ที่ตำแหน่ง '{oldCounter.name}' เดิม — ลบ object '{oldCounter.name}' ทิ้งได้"
            : $"• สร้าง {StandPointName} ใต้ IntakePoint (ปรับตำแหน่งได้ใน Scene view)";
    }
}
