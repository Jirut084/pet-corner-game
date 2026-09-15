using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// หน้าต่างซูมดูสัตว์ขณะอยู่ในสถานี — วางสัตว์ลงสถานีเมื่อไหร่ จะมีกรอบภาพสดมุมขวาของจอ
/// ผู้เล่นจึงเห็นว่าสัตว์ตัวไหนกำลังถูกดูแลอยู่ที่ไหน และงานคืบหน้าไปเท่าไหร่ ขณะเดินไปทำอย่างอื่น
///
/// ทำงานโดยสร้างกล้องเล็ก 1 ตัวต่อสถานีที่มีสัตว์ ถ่ายลง RenderTexture แล้ววาดด้วย OnGUI
/// ไม่ต้องตั้งค่าใน Editor — CareStation เรียก Show/Hide เอง และจะสร้างตัวเองขึ้นมาถ้ายังไม่มีในฉาก
/// (ถ้าอยากปรับขนาดหน้าต่าง/มุมกล้อง ให้ใส่ component นี้ลงฉากเองแล้วปรับใน Inspector)
/// </summary>
public class StationCloseupOverlay : MonoBehaviour
{
    public static StationCloseupOverlay Instance { get; private set; }

    [Header("Window")]
    [Tooltip("ขนาดกรอบภาพบนจอ (พิกเซล)")]
    [SerializeField] private float windowSize = 180f;

    [Tooltip("ความละเอียดของภาพจากกล้องซูม")]
    [SerializeField] private int textureSize = 256;

    [Tooltip("ระยะจากขอบบนจอ เว้นที่ให้แถบสถานะของ HUD")]
    [SerializeField] private float topMargin = 44f;

    [Header("Camera")]
    [SerializeField] private float fieldOfView = 30f;

    [Tooltip("ระยะกล้องจากหน้าสัตว์ คิดเป็นกี่เท่าของขนาดสัตว์ — น้อย = ซูมใกล้หน้ามากขึ้น")]
    [SerializeField] private float faceDistanceScale = 1.2f;

    [Tooltip("ยกกล้องสูงกว่าระดับหน้าเล็กน้อย คิดเป็นกี่เท่าของขนาดสัตว์ (0 = ระดับสายตาพอดี)")]
    [SerializeField] private float faceHeightScale = 0.15f;

    [SerializeField] private Color backgroundColor = new Color(0.13f, 0.16f, 0.15f);

    [Header("Filter")]
    [Tooltip("แสดงหน้าต่างซูมตอนวางสัตว์บนโต๊ะพักด้วยหรือไม่ (ปกติโต๊ะพักไม่ได้ทำอะไร จึงปิดไว้)")]
    [SerializeField] private bool includeHoldingTables = false;

    private class View
    {
        public CareStation station;
        public Pet pet;
        public Camera camera;
        public RenderTexture texture;
    }

    private readonly List<View> views = new List<View>();
    private Texture2D pixel;
    private GUIStyle titleStyle;
    private GUIStyle captionStyle;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;

        pixel = new Texture2D(1, 1);
        pixel.SetPixel(0, 0, Color.white);
        pixel.Apply();
    }

    private void OnDestroy()
    {
        foreach (View view in views) ReleaseView(view);
        views.Clear();

        if (pixel != null) Destroy(pixel);
        if (Instance == this) Instance = null;
    }

    /// <summary>CareStation เรียกตอนวางสัตว์ลงสถานี</summary>
    public static void Show(CareStation station, Pet pet)
    {
        if (station == null || pet == null) return;

        StationCloseupOverlay overlay = Instance != null
            ? Instance
            : new GameObject("StationCloseupOverlay").AddComponent<StationCloseupOverlay>();

        overlay.AddView(station, pet);
    }

    /// <summary>CareStation เรียกตอนสัตว์ถูกยกออกจากสถานี หรือสถานีถูกทำลาย</summary>
    public static void Hide(CareStation station)
    {
        // ไม่สร้าง instance ใหม่ตอนซ่อน — กันการสร้าง GameObject ระหว่างปิดฉาก
        if (Instance != null) Instance.RemoveView(station);
    }

    private void AddView(CareStation station, Pet pet)
    {
        if (station.Mode == CareStation.ServiceMode.Holding && !includeHoldingTables) return;

        RemoveView(station); // สถานีหนึ่งมีได้หน้าต่างเดียว

        GameObject cameraObject = new GameObject($"Closeup Camera ({station.name})");
        cameraObject.transform.SetParent(transform, false);

        Camera closeup = cameraObject.AddComponent<Camera>();
        closeup.clearFlags = CameraClearFlags.SolidColor;
        closeup.backgroundColor = backgroundColor;
        closeup.fieldOfView = fieldOfView;
        closeup.nearClipPlane = 0.03f;
        closeup.farClipPlane = 30f;
        closeup.depth = -50f;

        RenderTexture texture = new RenderTexture(textureSize, textureSize, 24)
        {
            name = $"Closeup ({station.name})",
            antiAliasing = 2
        };
        texture.Create();
        closeup.targetTexture = texture; // มี targetTexture แล้ว กล้องนี้จะไม่วาดทับจอหลัก

        View view = new View { station = station, pet = pet, camera = closeup, texture = texture };
        views.Add(view);
        AimCamera(view);
    }

    private void RemoveView(CareStation station)
    {
        for (int i = views.Count - 1; i >= 0; i--)
        {
            if (views[i].station != station) continue;

            ReleaseView(views[i]);
            views.RemoveAt(i);
        }
    }

    private void LateUpdate()
    {
        for (int i = views.Count - 1; i >= 0; i--)
        {
            View view = views[i];

            // สัตว์ถูกยกไป / ออเดอร์หลุดจนสัตว์ถูกทำลาย / สถานีถูกยกย้าย — ปิดหน้าต่างทิ้ง
            if (view.station == null || view.pet == null || view.station.OccupyingPet != view.pet)
            {
                ReleaseView(view);
                views.RemoveAt(i);
                continue;
            }

            AimCamera(view);
        }
    }

    /// <summary>
    /// เล็งกล้องไปที่หน้าสัตว์ — วางกล้องไว้ด้านหน้าตามทิศที่สัตว์หันหน้า แล้วมองกลับมาที่ใบหน้า
    /// (เดิมมองจากทิศเดียวกับกล้องหลัก ซึ่งอยู่ด้านหลังสัตว์ ภาพเลยเห็นแต่หลัง)
    /// </summary>
    private void AimCamera(View view)
    {
        Bounds bounds = view.pet.VisualBounds;
        float size = Mathf.Max(0.2f, Mathf.Max(bounds.size.x, Mathf.Max(bounds.size.y, bounds.size.z)));

        Vector3 facePoint = view.pet.FaceWorldPoint;
        Vector3 faceDirection = view.pet.FaceWorldDirection;

        Transform cameraTransform = view.camera.transform;
        cameraTransform.position = facePoint + faceDirection * size * faceDistanceScale + Vector3.up * size * faceHeightScale;
        cameraTransform.LookAt(facePoint);
    }

    private static void ReleaseView(View view)
    {
        if (view.camera != null)
        {
            view.camera.targetTexture = null;
            Destroy(view.camera.gameObject);
        }

        if (view.texture != null)
        {
            view.texture.Release();
            Destroy(view.texture);
        }
    }

    private void OnGUI()
    {
        if (views.Count == 0) return;

        EnsureStyles();

        const float headerHeight = 22f;
        const float footerHeight = 26f;
        float x = Screen.width - windowSize - 14f;
        float y = topMargin;

        foreach (View view in views)
        {
            if (view.station == null || view.pet == null || view.texture == null) continue;

            Rect frame = new Rect(x - 5f, y - 5f, windowSize + 10f, headerHeight + windowSize + footerHeight + 10f);
            DrawBox(frame, new Color(0f, 0f, 0f, 0.62f));

            GUI.Label(new Rect(x, y, windowSize, headerHeight),
                $"{view.pet.DisplayName} — {view.station.DisplayLabel}", titleStyle);

            GUI.DrawTexture(new Rect(x, y + headerHeight, windowSize, windowSize), view.texture, ScaleMode.ScaleToFit, false);

            DrawFooter(view, new Rect(x, y + headerHeight + windowSize + 4f, windowSize, footerHeight - 4f));

            y = frame.yMax + 10f;
        }
    }

    private void DrawFooter(View view, Rect area)
    {
        CareStation station = view.station;

        if (station.Mode != CareStation.ServiceMode.DropOff)
        {
            GUI.Label(area, "พักรออยู่", captionStyle);
            return;
        }

        bool ready = station.State == CareStation.StationState.Ready;
        Rect bar = new Rect(area.x, area.y, area.width, 7f);

        DrawBox(bar, new Color(0f, 0f, 0f, 0.7f));
        DrawBox(new Rect(bar.x, bar.y, bar.width * Mathf.Clamp01(station.Progress), bar.height),
            ready ? new Color(0.45f, 0.85f, 0.55f) : CareStepStyle.ColorOf(station.Step));

        string caption = ready ? "เสร็จแล้ว — มาอุ้มไปได้" : $"กำลังดูแล {Mathf.RoundToInt(station.Progress * 100f)}%";
        GUI.Label(new Rect(area.x, bar.yMax + 1f, area.width, area.height - bar.height), caption, captionStyle);
    }

    private void EnsureStyles()
    {
        if (titleStyle != null) return;

        titleStyle = new GUIStyle(GUI.skin.label)
        {
            alignment = TextAnchor.MiddleCenter,
            fontStyle = FontStyle.Bold,
            clipping = TextClipping.Clip
        };

        captionStyle = new GUIStyle(GUI.skin.label)
        {
            alignment = TextAnchor.UpperCenter,
            fontSize = 11,
            clipping = TextClipping.Clip
        };
    }

    private void DrawBox(Rect rect, Color color)
    {
        Color previous = GUI.color;
        GUI.color = color;
        GUI.DrawTexture(rect, pixel);
        GUI.color = previous;
    }
}
