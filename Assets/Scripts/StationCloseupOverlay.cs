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

    /// <summary>กล้องซูมมองสัตว์จากทิศไหน — ทั้งสองแบบเป็นมุมคงที่ ไม่หมุนตามตัวสัตว์</summary>
    public enum ViewAngleMode
    {
        MainCamera, // มองจากฝั่งเดียวกับกล้องหลักของเกม ภาพในหน้าต่างจึงหันทางเดียวกับที่ผู้เล่นเห็นในฉาก
        FixedYaw    // มองไปทางมุม viewYaw ในโลก (0 = มองไปทาง +Z)
    }

    [Header("Camera")]
    [SerializeField] private float fieldOfView = 30f;

    [Tooltip("MainCamera = มองจากฝั่งเดียวกับกล้องเกม / FixedYaw = มองตามมุม View Yaw")]
    [SerializeField] private ViewAngleMode angleMode = ViewAngleMode.MainCamera;

    [Tooltip("ทิศที่กล้องมอง (องศารอบแกนตั้ง) ใช้เมื่อ Angle Mode = FixedYaw — 0 = มองไปทาง +Z, 180 = มองไปทาง -Z")]
    [SerializeField] private float viewYaw = 0f;

    [Tooltip("มุมก้มมองลงมาที่สัตว์ (องศา) 0 = ระดับสายตา")]
    [Range(0f, 89f)]
    [SerializeField] private float viewPitch = 25f;

    [Tooltip("ระยะกล้องจากกลางตัวสัตว์ คิดเป็นกี่เท่าของขนาดสัตว์ — น้อย = ซูมใกล้ขึ้น")]
    [SerializeField] private float distanceScale = 2.2f;

    [SerializeField] private Color backgroundColor = new Color(0.90f, 0.87f, 0.82f);

    [Header("Filter")]
    [Tooltip("แสดงหน้าต่างซูมตอนวางสัตว์บนโต๊ะพักด้วยหรือไม่ (ปกติโต๊ะพักไม่ได้ทำอะไร จึงปิดไว้)")]
    [SerializeField] private bool includeHoldingTables = false;

    private class View
    {
        public CareStation station;
        public Pet pet;
        public Camera camera;
        public RenderTexture texture;

        // จุดโฟกัสและขนาดวัดครั้งเดียวตอนวางสัตว์ลงสถานี
        // ถ้าวัดใหม่ทุกเฟรม กล่องขอบเขตจะเปลี่ยนตามมุมที่สัตว์หมุนอยู่ ภาพจะสั่นและซูมเข้าออกเอง
        public Vector3 focusPoint;
        public float frameSize;
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

        // วัดตอนนี้ — CareStation เพิ่งวางสัตว์ลงตำแหน่งพักพอดี ยังไม่เริ่มหมุน/เด้ง
        Bounds bounds = pet.VisualBounds;

        View view = new View
        {
            station = station,
            pet = pet,
            camera = closeup,
            texture = texture,
            focusPoint = bounds.center,
            frameSize = Mathf.Max(0.2f, Mathf.Max(bounds.size.x, Mathf.Max(bounds.size.y, bounds.size.z)))
        };

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
    /// เล็งกล้องจากมุมคงที่ไปที่กลางตัวสัตว์ — ไม่หมุนตามทิศที่สัตว์หันอีกแล้ว
    /// สัตว์หมุนอยู่บนสถานี (CareStationEffects) ผู้เล่นจึงเห็นครบทุกด้านเองโดยภาพไม่หมุนตาม
    /// </summary>
    private void AimCamera(View view)
    {
        Quaternion viewRotation = Quaternion.Euler(viewPitch, CurrentViewYaw(), 0f);
        Vector3 viewForward = viewRotation * Vector3.forward;

        Transform cameraTransform = view.camera.transform;
        cameraTransform.SetPositionAndRotation(
            view.focusPoint - viewForward * view.frameSize * distanceScale,
            viewRotation);
    }

    /// <summary>มุมรอบแกนตั้งที่กล้องซูมมอง — MainCamera ใช้ทิศของกล้องเกม (ถ้าหากล้องไม่เจอใช้ viewYaw แทน)</summary>
    private float CurrentViewYaw()
    {
        if (angleMode == ViewAngleMode.MainCamera)
        {
            Camera main = Camera.main;
            if (main != null)
            {
                Vector3 forward = main.transform.forward;
                forward.y = 0f;

                if (forward.sqrMagnitude > 0.0001f)
                {
                    return Mathf.Atan2(forward.x, forward.z) * Mathf.Rad2Deg;
                }
            }
        }

        return viewYaw;
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
            DrawBox(frame, new Color(0.99f, 0.97f, 0.93f, 0.97f));

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

        DrawBox(bar, new Color(0.22f, 0.19f, 0.16f, 0.12f));
        DrawBox(new Rect(bar.x, bar.y, bar.width * Mathf.Clamp01(station.Progress), bar.height),
            ready ? new Color(0.40f, 0.72f, 0.45f) : CareStepStyle.ColorOf(station.Step));

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
        titleStyle.normal.textColor = new Color(0.22f, 0.19f, 0.16f);

        captionStyle = new GUIStyle(GUI.skin.label)
        {
            alignment = TextAnchor.UpperCenter,
            fontSize = 12,
            clipping = TextClipping.Clip
        };
        captionStyle.normal.textColor = new Color(0.22f, 0.19f, 0.16f, 0.75f);
    }

    private void DrawBox(Rect rect, Color color)
    {
        Color previous = GUI.color;
        GUI.color = color;
        GUI.DrawTexture(rect, pixel);
        GUI.color = previous;
    }
}
