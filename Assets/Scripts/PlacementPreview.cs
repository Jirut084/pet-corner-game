using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// เงาตัวอย่าง (ghost) บอกตำแหน่งที่สถานีจะไปลงก่อนกดวางจริง — แบบ PlateUp! / Planet Coaster
///
///   ถือกล่องอุปกรณ์อยู่  → โชว์เงาของสถานีที่อยู่ในกล่อง ที่ช่องเป้าหมาย
///   ถือสถานีอยู่        → โชว์เงาของสถานีชิ้นนั้น ที่ช่องเป้าหมาย
///   มือว่าง             → ไฮไลต์สถานีที่ยกได้ในช่องเป้าหมาย (สีฟ้า)
///
///   เขียว = วางได้   แดง = ช่องไม่ว่างหรืออยู่นอกกริด
///
/// TilePlacer ใส่ component นี้ให้อัตโนมัติตอน Awake ไม่ต้องตั้งค่าอะไรในฉาก
/// อยากปรับสี/ความจาง ให้ Add Component "PlacementPreview" ลงตัวผู้เล่นเองแล้วแก้ใน Inspector
///
/// เงาสร้างจาก mesh ของ prefab โดยตรง ไม่ได้ Instantiate ตัว prefab —
/// ถ้า Instantiate จะได้ collider/สคริปต์/GridOccupant ติดมาด้วย แล้วเงาจะไปจองช่องบนกริดเสียเอง
/// </summary>
[DisallowMultipleComponent]
public class PlacementPreview : MonoBehaviour
{
    [Header("Colors")]
    [Tooltip("วางลงช่องนี้ได้")]
    [SerializeField] private Color validColor = new Color(0.35f, 1f, 0.5f);

    [Tooltip("ช่องไม่ว่าง หรืออยู่นอกกริด")]
    [SerializeField] private Color blockedColor = new Color(1f, 0.35f, 0.35f);

    [Tooltip("มือว่างแล้วเล็งไปที่สถานีที่ยกได้")]
    [SerializeField] private Color pickupColor = new Color(0.4f, 0.8f, 1f);

    [Range(0.05f, 1f)]
    [Tooltip("ความทึบของเงาสถานี")]
    [SerializeField] private float ghostAlpha = 0.4f;

    [Range(0.05f, 1f)]
    [Tooltip("ความทึบของแผ่นสี่เหลี่ยมที่พื้น")]
    [SerializeField] private float markerAlpha = 0.3f;

    [Header("Cell Marker")]
    [Tooltip("แสดงแผ่นสี่เหลี่ยมบอกช่องเป้าหมายที่พื้น")]
    [SerializeField] private bool showCellMarker = true;

    [Tooltip("แผ่นเล็กกว่าช่องนิดหน่อยจะได้เห็นเส้นแบ่งช่อง (1 = เต็มช่องพอดี)")]
    [Range(0.5f, 1f)]
    [SerializeField] private float markerScale = 0.92f;

    [Tooltip("ยกแผ่นลอยเหนือพื้นเล็กน้อย กัน z-fighting กับพื้นห้อง")]
    [SerializeField] private float markerLift = 0.02f;

    [Header("Empty Hands")]
    [Tooltip("มือว่างแล้วเล็งไปที่สถานีที่ยกได้ ให้ไฮไลต์ช่องนั้น")]
    [SerializeField] private bool highlightPickupTarget = true;

    [Header("Materials (ไม่บังคับ)")]
    [Tooltip("ปล่อยว่างจะสร้าง material โปร่งแสงให้เองตอนรันจากเชดเดอร์ของ URP")]
    [SerializeField] private Material ghostMaterialOverride;

    private TilePlacer placer;
    private PlayerCarrier carrier;

    // เงาสถานี สร้างใหม่เมื่อของที่ถืออยู่เปลี่ยนชนิดเท่านั้น ไม่ได้สร้างทุกเฟรม
    private GameObject ghostRoot;
    private GameObject ghostSource;
    private float ghostBottomOffset; // ระยะจาก pivot ลงไปถึงก้นโมเดล ใช้วางแผ่นที่พื้น

    private GameObject markerRoot;

    private Material ghostMaterial;
    private Material markerMaterial;
    private Color currentTint;

    private void Awake()
    {
        placer = GetComponent<TilePlacer>();
        carrier = GetComponent<PlayerCarrier>();
    }

    private void OnDisable()
    {
        HidePreview();
    }

    private void OnDestroy()
    {
        ClearGhost();

        if (markerRoot != null) Destroy(markerRoot);
        if (ghostMaterial != null) Destroy(ghostMaterial);
        if (markerMaterial != null) Destroy(markerMaterial);
    }

    // LateUpdate เพื่อให้ผู้เล่นขยับ/หันตัวเสร็จก่อน เงาจะได้ไม่ตามช้าไป 1 เฟรม
    private void LateUpdate()
    {
        if (!ShouldPreview(out TileGrid grid))
        {
            HidePreview();
            return;
        }

        Vector2Int cell = placer.TargetCell;

        GameObject source = ResolvePlacementSource();
        if (source != null)
        {
            ShowPlacementGhost(grid, cell, source);
            return;
        }

        ShowPickupHighlight(grid, cell);
    }

    /// <summary>ตอนนี้ควรโชว์เงาไหม — จัดร้านได้อยู่ ไม่ได้หยุดเกม และมีกริดให้เล็ง</summary>
    private bool ShouldPreview(out TileGrid grid)
    {
        grid = placer != null ? placer.Grid : null;
        if (grid == null || carrier == null) return false;

        GameManager game = GameManager.Instance;
        if (game == null) return true; // ไม่มี GameManager (ฉากทดสอบ) ให้โชว์ไปเลย

        return game.CanArrangeStations && !game.IsPaused;
    }

    /// <summary>สิ่งที่กำลังจะถูกวาง — prefab ในกล่องอุปกรณ์ หรือสถานีที่ยกมาเอง (null ถ้ามือว่าง/ถือสัตว์อยู่)</summary>
    private GameObject ResolvePlacementSource()
    {
        if (carrier == null || !carrier.IsCarrying) return null;

        EquipmentParcel parcel = carrier.GetHeld<EquipmentParcel>();
        if (parcel != null) return parcel.StationPrefab;

        GridOccupant station = carrier.GetHeld<GridOccupant>();
        return station != null ? station.gameObject : null;
    }

    // ---------- เงาตอนถือของ ----------

    private void ShowPlacementGhost(TileGrid grid, Vector2Int cell, GameObject source)
    {
        EnsureGhost(source);

        if (ghostRoot == null)
        {
            HidePreview();
            return;
        }

        Vector3 center = grid.CellToWorld(cell);

        // วางแบบเดียวกับตอนวางจริงเป๊ะ (TilePlacer รีเซ็ตมุมเป็น identity ก่อนลงทะเบียน)
        // เงาจึงตรงกับผลลัพธ์จริง ไม่ใช่แค่ใกล้เคียง
        ghostRoot.transform.SetPositionAndRotation(center, Quaternion.identity);
        ghostRoot.SetActive(true);

        bool valid = !grid.IsOccupied(cell);
        ApplyTint(valid ? validColor : blockedColor);

        UpdateMarker(grid, center, ghostBottomOffset, valid ? validColor : blockedColor);
    }

    // ---------- ไฮไลต์ตอนมือว่าง ----------

    private void ShowPickupHighlight(TileGrid grid, Vector2Int cell)
    {
        ClearGhost();

        if (!highlightPickupTarget)
        {
            ShowMarker(false);
            return;
        }

        GameObject target = grid.GetOccupant(cell);
        GridOccupant occupant = target != null ? target.GetComponent<GridOccupant>() : null;

        if (occupant == null || !occupant.Removable)
        {
            ShowMarker(false);
            return;
        }

        Vector3 center = grid.CellToWorld(cell);
        UpdateMarker(grid, center, BottomOffsetOf(target), pickupColor);
    }

    // ---------- แผ่นบอกช่องที่พื้น ----------

    private void UpdateMarker(TileGrid grid, Vector3 cellCenter, float bottomOffset, Color color)
    {
        if (!showCellMarker)
        {
            ShowMarker(false);
            return;
        }

        EnsureMarker();

        float size = grid.CellSize * markerScale;
        markerRoot.transform.position = new Vector3(cellCenter.x, cellCenter.y + bottomOffset + markerLift, cellCenter.z);
        markerRoot.transform.localScale = new Vector3(size, 1f, size);

        markerMaterial.color = new Color(color.r, color.g, color.b, markerAlpha);
        ShowMarker(true);
    }

    private void ShowMarker(bool visible)
    {
        if (markerRoot != null && markerRoot.activeSelf != visible)
        {
            markerRoot.SetActive(visible);
        }
    }

    /// <summary>ซ่อนทั้งเงาและแผ่นบอกช่อง (ไม่ทำลายทิ้ง จะได้ไม่ต้องสร้างใหม่ตอนกลับมาจัดร้านรอบหน้า)</summary>
    private void HidePreview()
    {
        if (ghostRoot != null && ghostRoot.activeSelf) ghostRoot.SetActive(false);
        ShowMarker(false);
    }

    // ---------- สร้างเงา ----------

    private void EnsureGhost(GameObject source)
    {
        // จำไว้ด้วยว่าเคยสร้างให้ของชิ้นนี้แล้ว ถึงผลลัพธ์จะเป็น null (prefab ไม่มี mesh)
        // ไม่งั้นจะวนสร้างใหม่ทุกเฟรมแล้วยิง warning รัวจนอ่าน Console ไม่ออก
        if (ghostSource == source) return;

        DestroyGhostObject();
        ghostSource = source;

        ghostRoot = new GameObject($"PlacementGhost ({source.name})");
        ghostRoot.SetActive(false);

        Material material = GhostMaterial();
        bool hasBounds = false;
        Bounds localBounds = new Bounds();

        // เดินเก็บเฉพาะ mesh — ไม่เอา collider/สคริปต์/แสง/เสียงที่ติดมากับ prefab
        foreach (MeshFilter filter in source.GetComponentsInChildren<MeshFilter>(true))
        {
            MeshRenderer sourceRenderer = filter.GetComponent<MeshRenderer>();
            if (filter.sharedMesh == null || sourceRenderer == null || !sourceRenderer.enabled) continue;

            AddGhostPart(source.transform, filter.transform, filter.sharedMesh, material, ref localBounds, ref hasBounds);
        }

        foreach (SkinnedMeshRenderer skinned in source.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            if (skinned.sharedMesh == null || !skinned.enabled) continue;

            AddGhostPart(source.transform, skinned.transform, skinned.sharedMesh, material, ref localBounds, ref hasBounds);
        }

        if (!hasBounds)
        {
            Debug.LogWarning($"[PlacementPreview] {source.name} ไม่มี mesh ให้ทำเงา — แสดงแค่แผ่นบอกช่อง");
            Destroy(ghostRoot);
            ghostRoot = null;
            ghostBottomOffset = 0f;
            return;
        }

        ghostBottomOffset = localBounds.min.y;
    }

    private void AddGhostPart(Transform sourceRoot, Transform part, Mesh mesh, Material material,
        ref Bounds localBounds, ref bool hasBounds)
    {
        GameObject piece = new GameObject(part.name);
        piece.transform.SetParent(ghostRoot.transform, false);

        // มุม/สเกลของชิ้นส่วนเทียบกับ root ของ prefab — ใช้เมทริกซ์เพราะบางชิ้นซ้อนกันหลายชั้นและสเกลไม่เท่ากัน
        Matrix4x4 relative = sourceRoot.worldToLocalMatrix * part.localToWorldMatrix;
        piece.transform.localPosition = relative.GetColumn(3);
        piece.transform.localRotation = relative.rotation;
        piece.transform.localScale = relative.lossyScale;

        piece.AddComponent<MeshFilter>().sharedMesh = mesh;

        MeshRenderer renderer = piece.AddComponent<MeshRenderer>();
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;

        // mesh หลาย submesh ต้องใส่ material ให้ครบทุกช่อง ไม่งั้นช่องที่เหลือจะขึ้นสีชมพู
        Material[] materials = new Material[Mathf.Max(1, mesh.subMeshCount)];
        for (int i = 0; i < materials.Length; i++) materials[i] = material;
        renderer.sharedMaterials = materials;

        // piece เป็นลูกตรงของ ghostRoot อยู่แล้ว เมทริกซ์ local ของมันจึงแปลงเข้าพิกัดของ ghostRoot ได้เลย
        Matrix4x4 toGhostSpace = Matrix4x4.TRS(
            piece.transform.localPosition, piece.transform.localRotation, piece.transform.localScale);

        Bounds partBounds = TransformBounds(mesh.bounds, toGhostSpace);

        if (!hasBounds)
        {
            localBounds = partBounds;
            hasBounds = true;
        }
        else
        {
            localBounds.Encapsulate(partBounds);
        }
    }

    /// <summary>ย้ายกล่องขอบเขตไปอีกพิกัดหนึ่ง โดยครอบมุมทั้ง 8 ไว้ทั้งหมด</summary>
    private static Bounds TransformBounds(Bounds source, Matrix4x4 matrix)
    {
        Vector3 center = matrix.MultiplyPoint3x4(source.center);
        Vector3 extents = source.extents;

        Vector3 axisX = matrix.MultiplyVector(new Vector3(extents.x, 0f, 0f));
        Vector3 axisY = matrix.MultiplyVector(new Vector3(0f, extents.y, 0f));
        Vector3 axisZ = matrix.MultiplyVector(new Vector3(0f, 0f, extents.z));

        Vector3 size = new Vector3(
            Mathf.Abs(axisX.x) + Mathf.Abs(axisY.x) + Mathf.Abs(axisZ.x),
            Mathf.Abs(axisX.y) + Mathf.Abs(axisY.y) + Mathf.Abs(axisZ.y),
            Mathf.Abs(axisX.z) + Mathf.Abs(axisY.z) + Mathf.Abs(axisZ.z)) * 2f;

        return new Bounds(center, size);
    }

    /// <summary>ก้นของโมเดลเทียบกับ pivot ใช้วางแผ่นบอกช่องให้แนบพื้นแทนที่จะลอยกลางตัว</summary>
    private static float BottomOffsetOf(GameObject target)
    {
        float lowest = 0f;
        bool found = false;

        foreach (Renderer renderer in target.GetComponentsInChildren<Renderer>())
        {
            if (renderer == null || !renderer.enabled || renderer is ParticleSystemRenderer) continue;

            float min = renderer.bounds.min.y - target.transform.position.y;
            if (!found || min < lowest)
            {
                lowest = min;
                found = true;
            }
        }

        return lowest;
    }

    /// <summary>ล้างทั้งเงาและตัวจำว่าเคยสร้างให้ของชิ้นไหน — ของชิ้นเดิมจะถูกสร้างใหม่เมื่อหยิบขึ้นมาอีกครั้ง</summary>
    private void ClearGhost()
    {
        DestroyGhostObject();
        ghostSource = null;
        ghostBottomOffset = 0f;
    }

    private void DestroyGhostObject()
    {
        if (ghostRoot == null) return;

        // ปิดก่อนสั่งลบ เพราะ Destroy ทำงานปลายเฟรม ถ้าไม่ปิดจะเห็นเงาเก่าซ้อนเงาใหม่ 1 เฟรม
        ghostRoot.SetActive(false);
        Destroy(ghostRoot);
        ghostRoot = null;
    }

    private void ApplyTint(Color color)
    {
        if (ghostMaterial == null || currentTint == color) return;

        currentTint = color;

        Color tint = new Color(color.r, color.g, color.b, ghostAlpha);
        ghostMaterial.color = tint;

        // URP/Lit ใช้ _BaseColor ไม่ใช่ _Color — ตั้งทั้งคู่จะได้ครอบคลุมทุกเชดเดอร์ที่อาจถูกเลือกมา
        if (ghostMaterial.HasProperty("_BaseColor")) ghostMaterial.SetColor("_BaseColor", tint);
    }

    // ---------- แผ่นสี่เหลี่ยมที่พื้น ----------

    private void EnsureMarker()
    {
        if (markerRoot != null) return;

        markerRoot = new GameObject("PlacementCellMarker");
        markerRoot.SetActive(false);

        MeshFilter filter = markerRoot.AddComponent<MeshFilter>();
        filter.sharedMesh = BuildFlatQuad();

        MeshRenderer renderer = markerRoot.AddComponent<MeshRenderer>();
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;

        markerMaterial = CreateTransparentMaterial();
        renderer.sharedMaterial = markerMaterial;
    }

    /// <summary>สี่เหลี่ยมขนาด 1x1 นอนราบบนระนาบ XZ — สร้างเองแทน CreatePrimitive ที่แถม collider มาด้วย</summary>
    private static Mesh BuildFlatQuad()
    {
        Mesh mesh = new Mesh { name = "PlacementQuad" };

        mesh.vertices = new[]
        {
            new Vector3(-0.5f, 0f, -0.5f),
            new Vector3(0.5f, 0f, -0.5f),
            new Vector3(0.5f, 0f, 0.5f),
            new Vector3(-0.5f, 0f, 0.5f)
        };

        mesh.normals = new[] { Vector3.up, Vector3.up, Vector3.up, Vector3.up };
        mesh.uv = new[] { Vector2.zero, Vector2.right, Vector2.one, Vector2.up };
        mesh.triangles = new[] { 0, 2, 1, 0, 3, 2 };

        mesh.RecalculateBounds();
        return mesh;
    }

    // ---------- material ----------

    private Material GhostMaterial()
    {
        if (ghostMaterial == null)
        {
            // ใส่ material เองก็ยังย้อมเขียว/แดงได้ เพราะทำสำเนามาใช้ ไม่ได้แก้ตัวต้นฉบับใน Project
            ghostMaterial = ghostMaterialOverride != null
                ? new Material(ghostMaterialOverride) { name = "PlacementPreview (copy)" }
                : CreateTransparentMaterial();

            currentTint = Color.clear; // บังคับให้ ApplyTint ครั้งแรกทำงานจริง
        }

        return ghostMaterial;
    }

    /// <summary>
    /// สร้าง material โปร่งแสงตอนรัน — URP ไม่ได้ทำให้โปร่งแค่ตั้งค่า alpha
    /// ต้องสลับ Surface Type เป็น Transparent, ปิด ZWrite และย้าย render queue ด้วย
    /// </summary>
    private static Material CreateTransparentMaterial()
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Lit")
                        ?? Shader.Find("Universal Render Pipeline/Unlit")
                        ?? Shader.Find("Sprites/Default")
                        ?? Shader.Find("Unlit/Color");

        Material material = new Material(shader) { name = "PlacementPreview (runtime)" };

        if (material.HasProperty("_Surface")) material.SetFloat("_Surface", 1f); // 1 = Transparent
        if (material.HasProperty("_Blend")) material.SetFloat("_Blend", 0f);     // 0 = Alpha
        if (material.HasProperty("_AlphaClip")) material.SetFloat("_AlphaClip", 0f);
        if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", 0f);
        if (material.HasProperty("_SrcBlend")) material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
        if (material.HasProperty("_DstBlend")) material.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
        if (material.HasProperty("_ZWrite")) material.SetFloat("_ZWrite", 0f);

        material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        material.DisableKeyword("_ALPHATEST_ON");
        material.DisableKeyword("_ALPHAPREMULTIPLY_ON");

        material.renderQueue = (int)RenderQueue.Transparent;
        return material;
    }
}
