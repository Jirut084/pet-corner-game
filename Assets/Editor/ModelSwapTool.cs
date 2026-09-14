using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// เครื่องมือสลับหน้าตา prefab จาก cube ต้นแบบเป็นโมเดลจริง (เปิดจาก Tools → Pet Corner → Model Swap Tool)
///
/// หลักการ: ไม่สร้าง prefab สถานีใหม่ — แก้ prefab เดิมเพื่อให้ GUID คงเดิม
/// StationCatalog / LevelInitializer / scene ที่ผูกไว้จึงไม่ต้องผูกใหม่เลย
///
/// สิ่งที่ทำกับแต่ละ prefab:
///   1) จำระดับฐานของหน้าตาเดิม (เพื่อไม่ให้โมเดลใหม่จมพื้นหรือลอย)
///   2) ลบ MeshFilter + MeshRenderer ที่ root และลูก "Visual" ตัวเก่า (รันซ้ำได้)
///   3) ใส่โมเดลเป็นลูกชื่อ "Visual" แล้วจัดให้อยู่กลางช่อง ฐานแตะพื้นเท่าเดิม
///   4) ปรับ BoxCollider / CapsuleCollider ที่ root ให้ครอบโมเดลใหม่
///   5) ไม่แตะ Transform ของ root เพราะ TilePlacer / LevelInitializer เขียนทับค่านั้นตลอด
///
/// สำหรับสัตว์: คัดลอก PetProto ออกมาเป็น Pet_[ชนิด].prefab แล้วผูกเข้า PetSpecies.petPrefab ให้อัตโนมัติ
/// </summary>
public class ModelSwapTool : EditorWindow
{
    [Serializable]
    public class StationSwap
    {
        public GameObject targetPrefab;
        public GameObject model;
    }

    [Serializable]
    public class PetSwap
    {
        public PetSpecies species;
        public GameObject model;
    }

    private const string VisualChildName = "Visual";

    [SerializeField] private bool fitCollider = true;
    [SerializeField] private bool keepFloorContact = true;

    [SerializeField] private List<StationSwap> stationSwaps = new List<StationSwap>();

    [SerializeField] private GameObject basePetPrefab;
    [SerializeField] private string petOutputFolder = "Assets/Prefabs/Pets";
    [SerializeField] private List<PetSwap> petSwaps = new List<PetSwap>();

    private SerializedObject serializedWindow;
    private Vector2 scroll;

    [MenuItem("Tools/Pet Corner/Model Swap Tool")]
    public static void Open()
    {
        GetWindow<ModelSwapTool>("Model Swap");
    }

    private void OnEnable()
    {
        serializedWindow = new SerializedObject(this);
    }

    private void OnGUI()
    {
        serializedWindow.Update();
        scroll = EditorGUILayout.BeginScrollView(scroll);

        EditorGUILayout.HelpBox(
            "กด 'เติมรายการอัตโนมัติ' ก่อน แล้วตรวจว่าจับคู่ถูก จากนั้นกด 'สลับโมเดลทั้งหมด'\n" +
            "เครื่องมือนี้แก้ไฟล์ prefab จริงและ Undo ไม่ได้ — ย้อนกลับด้วย git แทน",
            MessageType.Info);

        EditorGUILayout.LabelField("ตัวเลือก", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(serializedWindow.FindProperty(nameof(fitCollider)),
            new GUIContent("ปรับ Collider ให้ครอบโมเดล"));
        EditorGUILayout.PropertyField(serializedWindow.FindProperty(nameof(keepFloorContact)),
            new GUIContent("วางฐานโมเดลให้แตะพื้นเท่าของเดิม"));

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("1) สถานี / เคาน์เตอร์ — แก้ prefab เดิม", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(serializedWindow.FindProperty(nameof(stationSwaps)), true);

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("2) สัตว์เลี้ยง — สร้าง prefab แยกต่อชนิด", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(serializedWindow.FindProperty(nameof(basePetPrefab)),
            new GUIContent("Prefab ต้นแบบ (PetProto)"));
        EditorGUILayout.PropertyField(serializedWindow.FindProperty(nameof(petOutputFolder)),
            new GUIContent("โฟลเดอร์ปลายทาง"));
        EditorGUILayout.PropertyField(serializedWindow.FindProperty(nameof(petSwaps)), true);

        serializedWindow.ApplyModifiedProperties();

        EditorGUILayout.Space();

        if (GUILayout.Button("เติมรายการอัตโนมัติจากชื่อไฟล์"))
        {
            AutoFill();
            GUIUtility.ExitGUI(); // ขนาดลิสต์เปลี่ยนกลางเฟรม ออกจาก OnGUI รอบนี้ไปก่อนกัน layout เพี้ยน
        }

        if (GUILayout.Button("สลับโมเดลทั้งหมด", GUILayout.Height(32)))
        {
            SwapAll();
            GUIUtility.ExitGUI();
        }

        EditorGUILayout.EndScrollView();
    }

    // ---------- เติมรายการอัตโนมัติ ----------

    private void AutoFill()
    {
        stationSwaps.Clear();
        AddStation("Assets/Prefabs/Food.prefab", "feeding bowl");
        AddStation("Assets/Prefabs/Bath.prefab", "Bath-Colored");
        AddStation("Assets/Prefabs/IntakePoint.prefab", "Cashier Machine");

        basePetPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Object/PetProto.prefab");

        petSwaps.Clear();
        AddPet("Cat", "Cat1");
        AddPet("Dog", "dog2");
        AddPet("Pig", "pig1");

        serializedWindow.Update();
        Repaint();
    }

    private void AddStation(string prefabPath, string modelName)
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
        GameObject model = FindModel(modelName);

        if (prefab == null || model == null)
        {
            Debug.LogWarning($"[ModelSwap] ข้าม {prefabPath} ← {modelName} (หาไฟล์ไม่เจอ)");
            return;
        }

        stationSwaps.Add(new StationSwap { targetPrefab = prefab, model = model });
    }

    private void AddPet(string speciesName, string modelName)
    {
        PetSpecies species = FindSpecies(speciesName);
        GameObject model = FindModel(modelName);

        if (species == null || model == null)
        {
            Debug.LogWarning($"[ModelSwap] ข้ามสัตว์ {speciesName} ← {modelName} (หา species หรือโมเดลไม่เจอ)");
            return;
        }

        petSwaps.Add(new PetSwap { species = species, model = model });
    }

    private static GameObject FindModel(string fileName)
    {
        foreach (string guid in AssetDatabase.FindAssets("t:Model", new[] { "Assets/Models" }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (Path.GetFileNameWithoutExtension(path).Equals(fileName, StringComparison.OrdinalIgnoreCase))
            {
                return AssetDatabase.LoadAssetAtPath<GameObject>(path);
            }
        }

        return null;
    }

    private static PetSpecies FindSpecies(string speciesName)
    {
        foreach (string guid in AssetDatabase.FindAssets("t:PetSpecies"))
        {
            PetSpecies species = AssetDatabase.LoadAssetAtPath<PetSpecies>(AssetDatabase.GUIDToAssetPath(guid));
            if (species != null && species.speciesName.Equals(speciesName, StringComparison.OrdinalIgnoreCase))
            {
                return species;
            }
        }

        return null;
    }

    // ---------- สลับทั้งหมด ----------

    private void SwapAll()
    {
        bool confirmed = EditorUtility.DisplayDialog(
            "สลับโมเดล",
            "จะแก้ไฟล์ prefab จริงและ Undo ไม่ได้ (ย้อนกลับด้วย git ได้)\nดำเนินการต่อ?",
            "สลับเลย", "ยกเลิก");

        if (!confirmed) return;

        int succeeded = 0;

        foreach (StationSwap swap in stationSwaps)
        {
            if (swap == null || swap.targetPrefab == null || swap.model == null) continue;

            if (SwapVisual(AssetDatabase.GetAssetPath(swap.targetPrefab), swap.model))
            {
                succeeded++;
            }
        }

        succeeded += SwapPets();

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log($"[ModelSwap] เสร็จ {succeeded} รายการ — กด Play เช็คการยก/วาง/กด E ได้เลย");
    }

    private int SwapPets()
    {
        if (petSwaps.Count == 0) return 0;

        if (basePetPrefab == null)
        {
            Debug.LogError("[ModelSwap] ยังไม่ได้ใส่ Prefab ต้นแบบของสัตว์ (PetProto)");
            return 0;
        }

        EnsureFolder(petOutputFolder);

        int succeeded = 0;
        string basePath = AssetDatabase.GetAssetPath(basePetPrefab);

        foreach (PetSwap swap in petSwaps)
        {
            if (swap == null || swap.species == null || swap.model == null) continue;

            string newPath = $"{petOutputFolder}/Pet_{swap.species.speciesName}.prefab";

            // รันซ้ำได้ — มีไฟล์อยู่แล้วก็แค่สลับหน้าตาใหม่ ไม่คัดลอกทับ
            if (AssetDatabase.LoadAssetAtPath<GameObject>(newPath) == null &&
                !AssetDatabase.CopyAsset(basePath, newPath))
            {
                Debug.LogError($"[ModelSwap] คัดลอก {basePath} → {newPath} ไม่สำเร็จ");
                continue;
            }

            if (!SwapVisual(newPath, swap.model)) continue;

            swap.species.petPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(newPath);
            EditorUtility.SetDirty(swap.species);

            Debug.Log($"[ModelSwap] {swap.species.speciesName} ใช้ prefab {newPath} แล้ว");
            succeeded++;
        }

        return succeeded;
    }

    /// <summary>สลับหน้าตาของ prefab ไฟล์เดียว คืน true ถ้าบันทึกสำเร็จ</summary>
    private bool SwapVisual(string prefabPath, GameObject model)
    {
        GameObject root = PrefabUtility.LoadPrefabContents(prefabPath);

        try
        {
            Transform rootTransform = root.transform;

            // การจัดตำแหน่งด้านล่างคิดใน world space แล้วลบตำแหน่ง root ออก
            // ใช้ได้ถูกต้องเฉพาะ root ที่ไม่หมุนและ scale = 1 (prefab ทุกตัวในโปรเจกต์นี้เป็นแบบนั้น)
            bool rootIsPlain = rootTransform.rotation == Quaternion.identity && rootTransform.lossyScale == Vector3.one;
            if (!rootIsPlain)
            {
                Debug.LogWarning($"[ModelSwap] {prefabPath}: root มีการหมุนหรือ scale ไม่เท่ากับ 1 จะข้ามการจัดตำแหน่งอัตโนมัติ");
            }

            // 1) จำระดับฐานของหน้าตาเดิมก่อนลบ
            bool hasOldBounds = TryGetWorldBounds(root, out Bounds oldBounds);

            // 2) ลบหน้าตาเดิม (ลูก Visual เก่าจากการรันครั้งก่อน + mesh ที่ root)
            Transform oldVisual = rootTransform.Find(VisualChildName);
            if (oldVisual != null) DestroyImmediate(oldVisual.gameObject);

            MeshRenderer oldRenderer = root.GetComponent<MeshRenderer>();
            if (oldRenderer != null) DestroyImmediate(oldRenderer);

            MeshFilter oldFilter = root.GetComponent<MeshFilter>();
            if (oldFilter != null) DestroyImmediate(oldFilter);

            // 3) ใส่โมเดลเป็นลูก (เป็น prefab instance ของไฟล์ fbx แก้ Scale Factor ทีหลังจะอัปเดตตาม)
            GameObject visual = (GameObject)PrefabUtility.InstantiatePrefab(model, rootTransform);
            visual.name = VisualChildName;
            visual.transform.localPosition = Vector3.zero;
            visual.transform.localRotation = Quaternion.identity;
            visual.transform.localScale = Vector3.one;

            if (rootIsPlain && TryGetWorldBounds(visual, out Bounds newBounds))
            {
                Vector3 rootPos = rootTransform.position;

                // จัดกึ่งกลางช่องบนแกน X/Z เพราะ pivot ของโมเดลมักไม่ได้อยู่กลางตัว
                Vector3 offset = new Vector3(rootPos.x - newBounds.center.x, 0f, rootPos.z - newBounds.center.z);

                // ให้ฐานโมเดลอยู่ระดับเดียวกับฐานของ cube เดิม จะได้ไม่จมพื้นหรือลอย
                if (keepFloorContact && hasOldBounds)
                {
                    offset.y = oldBounds.min.y - newBounds.min.y;
                }

                visual.transform.localPosition += offset;
            }

            // 4) ปรับ collider ที่ root ให้ครอบโมเดลใหม่ (InteractionManager ใช้ collider หาสถานี)
            if (fitCollider && rootIsPlain && TryGetWorldBounds(visual, out Bounds fitBounds))
            {
                FitRootCollider(root, fitBounds);
            }

            WarnAboutModelColliders(visual, prefabPath);

            PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
            Debug.Log($"[ModelSwap] {prefabPath} ← {model.name} เรียบร้อย");
            return true;
        }
        catch (Exception e)
        {
            Debug.LogError($"[ModelSwap] สลับ {prefabPath} ไม่สำเร็จ: {e.Message}");
            return false;
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    private static void FitRootCollider(GameObject root, Bounds worldBounds)
    {
        Vector3 localCenter = worldBounds.center - root.transform.position;

        BoxCollider box = root.GetComponent<BoxCollider>();
        if (box != null)
        {
            box.center = localCenter;
            box.size = worldBounds.size;
        }

        CapsuleCollider capsule = root.GetComponent<CapsuleCollider>();
        if (capsule != null)
        {
            capsule.direction = 1; // แกน Y
            capsule.center = localCenter;
            capsule.height = worldBounds.size.y;
            capsule.radius = Mathf.Max(worldBounds.size.x, worldBounds.size.z) * 0.5f;
        }
    }

    /// <summary>
    /// ถ้าตอน import ติ๊ก Generate Colliders ไว้ โมเดลจะมี MeshCollider ติดมา
    /// ซ้อนกับ collider ที่ root และอาจขวางทางเดินผู้เล่น — เตือนให้ไปปิดที่ไฟล์ fbx
    /// </summary>
    private static void WarnAboutModelColliders(GameObject visual, string prefabPath)
    {
        if (visual.GetComponentsInChildren<Collider>().Length == 0) return;

        Debug.LogWarning($"[ModelSwap] {prefabPath}: โมเดลมี Collider ติดมาด้วย — " +
                         "ไปที่ไฟล์ .fbx → แท็บ Model → ปิด Generate Colliders → Apply");
    }

    private static bool TryGetWorldBounds(GameObject target, out Bounds bounds)
    {
        bounds = default;
        bool found = false;

        foreach (Renderer renderer in target.GetComponentsInChildren<Renderer>())
        {
            if (!found)
            {
                bounds = renderer.bounds;
                found = true;
            }
            else
            {
                bounds.Encapsulate(renderer.bounds);
            }
        }

        return found;
    }

    private static void EnsureFolder(string folder)
    {
        if (AssetDatabase.IsValidFolder(folder)) return;

        string parent = Path.GetDirectoryName(folder).Replace('\\', '/');
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, Path.GetFileName(folder));
    }
}
