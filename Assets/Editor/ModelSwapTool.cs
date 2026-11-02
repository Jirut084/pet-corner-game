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
///   1) หาระดับฐาน — ถ้ายังมี mesh ต้นแบบที่ root ใช้ฐานของมัน ไม่งั้นใช้ค่า floorLocalY
///   2) ลบ MeshFilter + MeshRenderer ที่ root และลูก "Visual" ตัวเก่า (รันซ้ำได้)
///   3) ใส่โมเดลเป็นลูกชื่อ "Visual" จัดกึ่งกลางช่อง ฐานแตะระดับที่หาได้ในข้อ 1
///   4) ปรับ BoxCollider / CapsuleCollider ที่ root ให้ครอบโมเดลใหม่
///      สถานีจะได้ BoxCollider สูงอย่างน้อย minStationColliderHeight (ยืดขึ้นด้านบน ฐานอยู่ที่พื้นเท่าเดิม)
///   5) ไม่แตะ Transform ของ root เพราะ TilePlacer / LevelInitializer เขียนทับค่านั้นตลอด
///
/// ขนาดคำนวณจาก vertex bounds ของ mesh × matrix ของ transform เอง ไม่ใช้ Renderer.bounds
/// เพราะใน prefab preview scene ค่า Renderer.bounds ยังไม่อัปเดตทันทีหลัง instantiate
/// โมเดลจาก Blender ที่ node มี scale 100 จึงถูกวัดเล็กไป 100 เท่า (อาการ: โมเดลจมพื้น, collider เหลือ 1 ซม.)
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

    [Tooltip("ระดับฐานของโมเดลในพิกัดของ root ใช้เมื่อไม่มี mesh ต้นแบบเหลือให้วัด (เช่นตอนรันซ้ำ) " +
             "-0.5 = ฐานของ cube 1 หน่วยที่ pivot อยู่กลางตัว ซึ่งเป็นแบบของ prefab ทุกตัวในโปรเจกต์นี้")]
    [SerializeField] private float floorLocalY = -0.5f;

    [Tooltip("ความสูงขั้นต่ำของ BoxCollider สถานี นับจากฐานโมเดลขึ้นไป (0 = ไม่บังคับ)\n" +
             "โมเดลเตี้ยกว่า Step Offset ของ CharacterController (0.3) ผู้เล่นจะก้าวขึ้นไปยืนหรือเดินข้ามได้ " +
             "collider ที่สูงกว่าตัวโมเดลยังช่วยให้กด E เจอสถานีง่ายขึ้นด้วย")]
    [SerializeField] private float minStationColliderHeight = 1.2f;

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
            new GUIContent("วางฐานโมเดลให้แตะพื้น"));
        EditorGUILayout.PropertyField(serializedWindow.FindProperty(nameof(floorLocalY)),
            new GUIContent("ระดับฐาน (พิกัด root)"));
        EditorGUILayout.PropertyField(serializedWindow.FindProperty(nameof(minStationColliderHeight)),
            new GUIContent("Collider สถานีสูงอย่างน้อย"));

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

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("3) แก้ prefab ที่สลับโมเดลไปแล้ว", EditorStyles.boldLabel);

        if (GUILayout.Button("ยืด Collider สถานีทุกตัวให้สูงอย่างน้อยค่าด้านบน"))
        {
            RaiseExistingStationColliders();
            GUIUtility.ExitGUI();
        }

        EditorGUILayout.EndScrollView();
    }

    // ---------- เติมรายการอัตโนมัติ ----------

    private void AutoFill()
    {
        stationSwaps.Clear();
        // ใช้เวอร์ชันลงสีก่อน ถ้าไม่มีค่อยถอยไปใช้ไฟล์ชุดแรก
        AddStation("Assets/Prefabs/Food.prefab", "feed bowl colored 2", "feeding bowl");
        AddStation("Assets/Prefabs/Bath.prefab", "Bath-Colored");
        AddStation("Assets/Prefabs/IntakePoint.prefab", "cashier colored", "Cashier Machine");
        AddStation("Assets/Prefabs/HoldingTable.prefab", "tableCoffeeSquare");
        AddStation("Assets/Prefabs/EquipmentParcel.prefab", "cardboardBoxClosed");
        AddStation("Assets/Prefabs/ReturnZone.prefab", "return_counter");

        basePetPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Object/PetProto.prefab");

        petSwaps.Clear();
        AddPet("Cat", "Cat1");
        AddPet("Dog", "dog2");
        AddPet("Pig", "pig1");

        serializedWindow.Update();
        Repaint();
    }

    private void AddStation(string prefabPath, params string[] modelNames)
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
        GameObject model = null;

        foreach (string candidate in modelNames)
        {
            model = FindModel(candidate);
            if (model != null) break;
        }

        if (prefab == null || model == null)
        {
            Debug.LogWarning($"[ModelSwap] ข้าม {prefabPath} ← {string.Join(" / ", modelNames)} (หาไฟล์ไม่เจอ)");
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
        // ThirdParty = ชุดโมเดล CC0 ที่โหลดมา (Kenney Furniture Kit ตั้ง Scale Factor 0.25 ไว้แล้วให้พอดีกริด)
        foreach (string guid in AssetDatabase.FindAssets("t:Model", new[] { "Assets/Models", "Assets/ThirdParty", "Assets/Art" }))
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

            if (SwapVisual(AssetDatabase.GetAssetPath(swap.targetPrefab), swap.model, minStationColliderHeight))
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

            // สัตว์ไม่ต้องมี collider สูง — ผู้เล่นไม่ได้เดินชนสัตว์ และ collider ถูกปิดตอนอุ้มอยู่แล้ว
            if (!SwapVisual(newPath, swap.model, 0f)) continue;

            swap.species.petPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(newPath);
            EditorUtility.SetDirty(swap.species);

            Debug.Log($"[ModelSwap] {swap.species.speciesName} ใช้ prefab {newPath} แล้ว");
            succeeded++;
        }

        return succeeded;
    }

    /// <summary>สลับหน้าตาของ prefab ไฟล์เดียว คืน true ถ้าบันทึกสำเร็จ (public ให้สคริปต์ editor อื่นเรียกได้โดยไม่ต้องเปิดหน้าต่าง)</summary>
    public bool SwapVisual(string prefabPath, GameObject model, float minColliderHeight)
    {
        GameObject root = PrefabUtility.LoadPrefabContents(prefabPath);

        try
        {
            Transform rootTransform = root.transform;

            // ทุกค่าด้านล่างคิดใน "พิกัดของ root" จึงใช้ได้แม้ root จะมี scale (เช่น PetProto = 0.5)
            // และตรงกับพิกัดที่ใช้ตั้ง localPosition ของลูก Visual และ center/size ของ collider พอดี

            // 1) หาระดับฐาน — ใช้ mesh ต้นแบบที่ root ถ้ายังมี (รันครั้งแรก) ไม่งั้นใช้ค่าที่ตั้งไว้
            //    ห้ามวัดจาก Visual ตัวเก่า เพราะถ้ารอบก่อนวางผิด จะพาความผิดนั้นมาด้วย
            float baseY = floorLocalY;
            MeshRenderer oldRenderer = root.GetComponent<MeshRenderer>();
            if (oldRenderer != null && TryGetLocalBounds(rootTransform, new Renderer[] { oldRenderer }, out Bounds oldBounds))
            {
                baseY = oldBounds.min.y;
            }

            // 2) ลบหน้าตาเดิม (ลูก Visual เก่าจากการรันครั้งก่อน + mesh ที่ root)
            Transform oldVisual = rootTransform.Find(VisualChildName);
            if (oldVisual != null) DestroyImmediate(oldVisual.gameObject);

            if (oldRenderer != null) DestroyImmediate(oldRenderer);

            MeshFilter oldFilter = root.GetComponent<MeshFilter>();
            if (oldFilter != null) DestroyImmediate(oldFilter);

            // 3) ใส่โมเดลเป็นลูก (เป็น prefab instance ของไฟล์ fbx แก้ Scale Factor ทีหลังจะอัปเดตตาม)
            // ห้ามรีเซ็ต rotation/scale ของโมเดล — fbx ที่มี mesh ชิ้นเดียว Unity จะยุบ node นั้นขึ้นมาเป็น root
            // พร้อม scale 100 และหมุน -90 จาก Blender ติดมาด้วย ถ้าตั้งเป็น 1/identity โมเดลจะเล็กลง 100 เท่า
            // (เหลือ ~1 ซม. จนมองไม่เห็น) และนอนตะแคง — ใช้ค่าจากไฟล์ต้นทางตามเดิม แล้วค่อยเลื่อนตำแหน่งด้านล่าง
            GameObject visual = (GameObject)PrefabUtility.InstantiatePrefab(model, rootTransform);
            visual.name = VisualChildName;

            if (!TryGetLocalBounds(rootTransform, visual.GetComponentsInChildren<Renderer>(), out Bounds modelBounds))
            {
                Debug.LogWarning($"[ModelSwap] {prefabPath}: โมเดลไม่มี mesh ให้วัดขนาด ข้ามการจัดตำแหน่งและ collider");
            }
            else
            {
                // จัดกึ่งกลางช่องบนแกน X/Z เพราะ pivot ของโมเดลมักไม่ได้อยู่กลางตัว
                Vector3 offset = new Vector3(-modelBounds.center.x, 0f, -modelBounds.center.z);

                // ให้ฐานโมเดลอยู่ระดับเดียวกับฐานของ cube เดิม จะได้ไม่จมพื้นหรือลอย
                if (keepFloorContact)
                {
                    offset.y = baseY - modelBounds.min.y;
                }

                visual.transform.localPosition += offset;
                modelBounds.center += offset;

                // 4) ปรับ collider ที่ root ให้ครอบโมเดลใหม่ (InteractionManager ใช้ collider หาสถานี)
                if (fitCollider)
                {
                    FitRootCollider(root, modelBounds, minColliderHeight);
                }

                Debug.Log($"[ModelSwap] {prefabPath}: ขนาดโมเดล {FormatSize(modelBounds.size, rootTransform.lossyScale)}");
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

    private static void FitRootCollider(GameObject root, Bounds localBounds, float minBoxHeight)
    {
        BoxCollider box = root.GetComponent<BoxCollider>();
        if (box != null)
        {
            Vector3 center = localBounds.center;
            Vector3 size = localBounds.size;

            // โมเดลเตี้ยเกิน — ยืดขึ้นด้านบนอย่างเดียว ฐาน collider ยังแตะพื้นที่ฐานโมเดลเท่าเดิม
            if (size.y < minBoxHeight)
            {
                center.y = localBounds.min.y + minBoxHeight * 0.5f;
                size.y = minBoxHeight;
            }

            box.center = center;
            box.size = size;
        }

        CapsuleCollider capsule = root.GetComponent<CapsuleCollider>();
        if (capsule != null)
        {
            capsule.direction = 1; // แกน Y
            capsule.center = localBounds.center;
            capsule.height = localBounds.size.y;
            capsule.radius = Mathf.Max(localBounds.size.x, localBounds.size.z) * 0.5f;
        }
    }

    // ---------- แก้ prefab ที่สลับไปแล้ว ----------

    /// <summary>
    /// ยืด BoxCollider ของ prefab สถานีที่มีอยู่แล้ว (ทุก prefab ใน Assets/Prefabs ที่มี CareStation / IntakePoint / ReturnZone)
    /// ให้สูงอย่างน้อย minStationColliderHeight — ฐาน collider อยู่ที่เดิม ไม่แตะโมเดลหรือตำแหน่งใดๆ
    /// ใช้แทนการสลับโมเดลซ้ำ เมื่อสลับโมเดลไปก่อนที่เครื่องมือนี้จะมีค่าความสูงขั้นต่ำ
    /// </summary>
    private void RaiseExistingStationColliders()
    {
        if (minStationColliderHeight <= 0f)
        {
            EditorUtility.DisplayDialog("ยืดความสูง Collider", "ตั้งค่า 'Collider สถานีสูงอย่างน้อย' ให้มากกว่า 0 ก่อน", "ตกลง");
            return;
        }

        bool confirmed = EditorUtility.DisplayDialog(
            "ยืดความสูง Collider",
            $"จะแก้ BoxCollider ของ prefab สถานีทุกตัวใน Assets/Prefabs ที่เตี้ยกว่า {minStationColliderHeight:0.##} หน่วย\n" +
            "Undo ไม่ได้ (ย้อนกลับด้วย git ได้) ดำเนินการต่อ?",
            "ยืดเลย", "ยกเลิก");

        if (!confirmed) return;

        List<string> changed = new List<string>();

        foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/Prefabs" }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            GameObject asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (asset == null || !IsStationPrefab(asset)) continue;

            BoxCollider assetBox = asset.GetComponent<BoxCollider>();
            if (assetBox == null || assetBox.size.y >= minStationColliderHeight) continue;

            GameObject root = PrefabUtility.LoadPrefabContents(path);

            try
            {
                BoxCollider box = root.GetComponent<BoxCollider>();
                float bottom = box.center.y - box.size.y * 0.5f;
                float oldHeight = box.size.y;

                box.center = new Vector3(box.center.x, bottom + minStationColliderHeight * 0.5f, box.center.z);
                box.size = new Vector3(box.size.x, minStationColliderHeight, box.size.z);

                PrefabUtility.SaveAsPrefabAsset(root, path);
                changed.Add($"{Path.GetFileNameWithoutExtension(path)}: {oldHeight:0.00} → {minStationColliderHeight:0.00}");
            }
            catch (Exception e)
            {
                Debug.LogError($"[ModelSwap] ยืด collider {path} ไม่สำเร็จ: {e.Message}");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        AssetDatabase.SaveAssets();

        string report = changed.Count > 0 ? string.Join("\n", changed) : "ทุกสถานีสูงพอแล้ว ไม่มีอะไรต้องแก้";
        Debug.Log($"[ModelSwap] ยืด collider สถานี:\n{report}");
        EditorUtility.DisplayDialog("ยืดความสูง Collider เสร็จแล้ว", report, "ตกลง");
    }

    private static bool IsStationPrefab(GameObject prefab)
    {
        return prefab.GetComponent<CareStation>() != null ||
               prefab.GetComponent<IntakePoint>() != null ||
               prefab.GetComponent<ReturnZone>() != null;
    }

    /// <summary>
    /// วัดขอบเขตของ renderer ทั้งหมดในพิกัดของ space
    /// คิดจาก vertex bounds ของ mesh × matrix ของ transform ซึ่งอัปเดตทันทีเสมอ
    /// (ต่างจาก Renderer.bounds ที่อาจยังเป็นค่าเก่าใน prefab preview scene)
    /// </summary>
    private static bool TryGetLocalBounds(Transform space, IEnumerable<Renderer> renderers, out Bounds bounds)
    {
        bounds = default;
        bool found = false;
        Matrix4x4 worldToSpace = space.worldToLocalMatrix;

        foreach (Renderer renderer in renderers)
        {
            Mesh mesh = GetMesh(renderer);
            if (mesh == null) continue;

            Matrix4x4 meshToSpace = worldToSpace * renderer.transform.localToWorldMatrix;

            // วัดจาก vertex จริงของ mesh ก่อน — แม่นกว่าแปลงมุม 8 มุมของกล่องขอบเขต
            // ถ้า node ใน fbx หมุนเอียง (ไม่ใช่ 90° พอดี) กล่องที่แปลงจากมุมจะใหญ่เกินจริงทุกด้านรวมถึงด้านล่าง
            // ฐานที่วัดได้จึงต่ำกว่าผิวล่างจริง → วางแล้วโมเดลลอยเหนือพื้น (อาการของกรง cage)
            // SkinnedMeshRenderer ข้ามไปใช้มุมกล่อง เพราะ vertex ของมันอิงกระดูก ไม่ได้อิง transform ของ renderer
            // mesh ที่อ่าน vertex ไม่ได้จะได้ array ว่าง ก็ถอยไปใช้มุมกล่องเช่นกัน
            Vector3[] vertices = renderer is SkinnedMeshRenderer ? null : mesh.vertices;
            if (vertices != null && vertices.Length > 0)
            {
                foreach (Vector3 vertex in vertices)
                {
                    Vector3 point = meshToSpace.MultiplyPoint3x4(vertex);

                    if (!found)
                    {
                        bounds = new Bounds(point, Vector3.zero);
                        found = true;
                    }
                    else
                    {
                        bounds.Encapsulate(point);
                    }
                }

                continue;
            }

            Bounds meshBounds = mesh.bounds;

            // แปลงมุมทั้ง 8 ของกล่อง เพราะ node ในไฟล์ fbx มักหมุน (Blender หมุน -90 รอบแกน X)
            for (int i = 0; i < 8; i++)
            {
                Vector3 sign = new Vector3((i & 1) == 0 ? -1f : 1f, (i & 2) == 0 ? -1f : 1f, (i & 4) == 0 ? -1f : 1f);
                Vector3 corner = meshToSpace.MultiplyPoint3x4(meshBounds.center + Vector3.Scale(meshBounds.extents, sign));

                if (!found)
                {
                    bounds = new Bounds(corner, Vector3.zero);
                    found = true;
                }
                else
                {
                    bounds.Encapsulate(corner);
                }
            }
        }

        return found;
    }

    private static Mesh GetMesh(Renderer renderer)
    {
        if (renderer is SkinnedMeshRenderer skinned) return skinned.sharedMesh;

        MeshFilter filter = renderer.GetComponent<MeshFilter>();
        return filter != null ? filter.sharedMesh : null;
    }

    /// <summary>ขนาดจริงในโลก (คูณ scale ของ root แล้ว) เอาไว้ให้ดูใน Console ว่าพอดี 1 ช่องไหม</summary>
    private static string FormatSize(Vector3 localSize, Vector3 rootScale)
    {
        Vector3 world = Vector3.Scale(localSize, rootScale);
        return $"กว้าง {world.x:F2} × สูง {world.y:F2} × ลึก {world.z:F2} หน่วย";
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

    private static void EnsureFolder(string folder)
    {
        if (AssetDatabase.IsValidFolder(folder)) return;

        string parent = Path.GetDirectoryName(folder).Replace('\\', '/');
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, Path.GetFileName(folder));
    }
}
