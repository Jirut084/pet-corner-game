using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// HUD หลักของเกม วาดด้วย OnGUI (ยังไม่ใช้ Canvas)
/// แสดง 4 อย่าง:
///   1) แถบสถานะ — เงิน / วัน / ออเดอร์ / ร้องเรียน
///   2) เกจความอดทนลอยเหนือหัวลูกค้าที่กำลังรออยู่
///   3) ป้ายเหนือหัวสัตว์ บอกว่ายังต้องไปสถานีไหน (แทนการย้อมสีตัวสัตว์แบบเดิม)
///   4) บันทึกเหตุการณ์ล่าสุดจาก GameLog มุมล่างซ้าย
///
/// หน้าต่างซูมดูสัตว์ในสถานีแยกไปอยู่ที่ StationCloseupOverlay
/// แยกจาก GameFlowDebugControls ที่ดูแลเฉพาะหน้าเมนูกับหน้าเลือกรางวัล
/// </summary>
public class GameHudOverlay : MonoBehaviour
{
    [Header("References")]
    [Tooltip("กล้องที่ใช้แปลงตำแหน่งโลกเป็นตำแหน่งจอ ปล่อยว่างจะใช้ Camera.main")]
    [SerializeField] private Camera worldCamera;

    [Header("Gauge")]
    [SerializeField] private float gaugeWidth = 74f;
    [SerializeField] private float gaugeHeight = 9f;

    [Header("Pet Need Tags")]
    [SerializeField] private float chipHeight = 20f;
    [SerializeField] private float chipPadding = 8f;
    [SerializeField] private float chipSpacing = 3f;

    [Header("Event Log")]
    [Tooltip("ข้อความจะจางหายหลังผ่านไปกี่วินาที (0 = ไม่จาง)")]
    [SerializeField] private float logFadeSeconds = 9f;

    private static readonly Color DoneChipColor = new Color(0.45f, 0.47f, 0.46f, 0.9f);
    private static readonly Color ReadyChipColor = new Color(0.45f, 0.85f, 0.55f);

    private Texture2D pixel;
    private GUIStyle chipStyle;

    // ใช้ซ้ำทุกเฟรม ไม่สร้างลิสต์ใหม่ทุกครั้งที่วาด (OnGUI ถูกเรียกหลายรอบต่อเฟรม)
    private readonly List<(string text, Color color)> chipBuffer = new List<(string, Color)>();

    private void Awake()
    {
        if (worldCamera == null) worldCamera = Camera.main;

        // เท็กซ์เจอร์ 1x1 ไว้ระบายสีสี่เหลี่ยม ใช้ซ้ำทั้งเกม ไม่สร้างใหม่ทุกเฟรม
        pixel = new Texture2D(1, 1);
        pixel.SetPixel(0, 0, Color.white);
        pixel.Apply();
    }

    private void OnDestroy()
    {
        if (pixel != null) Destroy(pixel);
    }

    private void OnGUI()
    {
        GameManager game = GameManager.Instance;
        if (game == null) return;

        // หน้าเมนู/หน้าเลือกรางวัลมี UI ของตัวเองอยู่แล้ว ไม่ต้องวาดทับ
        if (game.Phase == GamePhase.MainMenu || game.Phase == GamePhase.DayEnd) return;

        if (worldCamera == null) worldCamera = Camera.main;

        DrawStatusBar(game);
        DrawCustomerGauges();
        DrawPetNeedTags();
        DrawEventLog();
    }

    private void DrawStatusBar(GameManager game)
    {
        string line = $"เงิน {ReturnZone.TotalWallet}    วันที่ {game.Day}";

        if (game.Phase == GamePhase.Service)
        {
            line += $"    ออเดอร์ {game.OrdersCompleted}/{game.OrdersTarget}" +
                    $"    ดูแลอยู่ {game.ActiveOrderCount}/{game.ConcurrentOrderLimit}" +
                    $"    ร้องเรียน {game.Complaints}";
        }
        else if (game.Phase == GamePhase.Setup)
        {
            line += "    ช่วงจัดร้าน — G ยก/วางสถานี, Enter เปิดร้าน";
        }

        DrawBox(new Rect(8, 8, 720, 26), new Color(0f, 0f, 0f, 0.55f));
        GUI.Label(new Rect(16, 12, 710, 22), line);
    }

    /// <summary>วาดเกจความอดทนลอยเหนือหัวลูกค้าที่นั่งรออยู่</summary>
    private void DrawCustomerGauges()
    {
        CustomerManager manager = CustomerManager.Instance;
        if (manager == null || worldCamera == null) return;

        foreach (Customer customer in manager.Customers)
        {
            if (customer == null || !customer.ShowGauge) continue;

            Vector3 worldPos = customer.transform.position + Vector3.up * customer.GaugeHeight;
            Vector3 screen = worldCamera.WorldToScreenPoint(worldPos);

            // อยู่หลังกล้อง = ไม่ต้องวาด ไม่งั้นเกจจะไปโผล่กลับด้านบนจอ
            if (screen.z <= 0f) continue;

            float x = screen.x - gaugeWidth * 0.5f;
            float y = Screen.height - screen.y;

            float fill = Mathf.Clamp01(customer.PatienceNormalized);

            DrawBox(new Rect(x - 1, y - 1, gaugeWidth + 2, gaugeHeight + 2), new Color(0f, 0f, 0f, 0.7f));
            DrawBox(new Rect(x, y, gaugeWidth * fill, gaugeHeight), PatienceColor(fill));

            string label = customer.AssignedPet != null ? customer.AssignedPet.DisplayName : string.Empty;
            GUI.Label(new Rect(x - 20, y - 19, gaugeWidth + 40, 18), label);
        }
    }

    /// <summary>
    /// ป้ายเหนือหัวสัตว์: 1 ชิ้นต่อ 1 ขั้นตอน — สีตามสถานีถ้ายังไม่ได้ทำ, สีเทามีเครื่องหมายถูกถ้าทำแล้ว
    /// ทำครบทุกขั้นตอนแล้วเหลือป้ายเขียว "ส่งคืนได้" อันเดียว
    /// </summary>
    private void DrawPetNeedTags()
    {
        if (worldCamera == null) return;

        EnsureStyles();

        foreach (Pet pet in Pet.All)
        {
            if (pet == null || pet.Species == null || pet.Species.requiredSteps == null) continue;

            Vector3 screen = worldCamera.WorldToScreenPoint(pet.OverheadWorldPosition);
            if (screen.z <= 0f) continue;

            chipBuffer.Clear();

            if (pet.IsComplete)
            {
                string tip = pet.CollectedTip > 0 ? $"  +ทิป {pet.CollectedTip}" : string.Empty;
                chipBuffer.Add(($"✓ ส่งคืนได้{tip}", ReadyChipColor));
            }
            else
            {
                foreach (CareStep step in pet.Species.requiredSteps)
                {
                    bool done = pet.HasCompleted(step);
                    string text = done ? $"✓ {CareStepStyle.LabelOf(step)}" : CareStepStyle.LabelOf(step);
                    chipBuffer.Add((text, done ? DoneChipColor : CareStepStyle.ColorOf(step)));
                }
            }

            DrawChipRow(screen.x, Screen.height - screen.y - chipHeight, chipBuffer);
        }
    }

    private void DrawChipRow(float centerX, float top, List<(string text, Color color)> chips)
    {
        float totalWidth = -chipSpacing;
        foreach (var chip in chips)
        {
            totalWidth += chipStyle.CalcSize(new GUIContent(chip.text)).x + chipPadding * 2f + chipSpacing;
        }

        float x = centerX - totalWidth * 0.5f;

        foreach (var chip in chips)
        {
            float width = chipStyle.CalcSize(new GUIContent(chip.text)).x + chipPadding * 2f;
            Rect rect = new Rect(x, top, width, chipHeight);

            DrawBox(new Rect(rect.x - 1f, rect.y - 1f, rect.width + 2f, rect.height + 2f), new Color(0f, 0f, 0f, 0.6f));
            DrawBox(rect, chip.color);
            GUI.Label(rect, chip.text, chipStyle);

            x += width + chipSpacing;
        }
    }

    private static Color PatienceColor(float fill)
    {
        if (fill > 0.5f) return new Color(0.45f, 0.85f, 0.55f);
        if (fill > 0.25f) return new Color(0.95f, 0.8f, 0.35f);
        return new Color(0.9f, 0.35f, 0.35f);
    }

    private void DrawEventLog()
    {
        var entries = GameLog.Entries;
        if (entries.Count == 0) return;

        const float lineHeight = 20f;
        float boxHeight = entries.Count * lineHeight + 10f;
        float top = Screen.height - boxHeight - 10f;

        DrawBox(new Rect(8, top, 640, boxHeight), new Color(0f, 0f, 0f, 0.45f));

        Color previous = GUI.color;

        for (int i = 0; i < entries.Count; i++)
        {
            GameLog.Entry entry = entries[i];

            float alpha = 1f;
            if (logFadeSeconds > 0f)
            {
                float age = Time.unscaledTime - entry.time;
                alpha = Mathf.Clamp01(1f - (age - logFadeSeconds) / 3f);
                if (alpha <= 0f) continue;
            }

            Color tone = GameLog.ColorFor(entry.tone);
            GUI.color = new Color(tone.r, tone.g, tone.b, alpha);
            GUI.Label(new Rect(16, top + 5 + i * lineHeight, 626, lineHeight), entry.text);
        }

        GUI.color = previous;
    }

    private void EnsureStyles()
    {
        if (chipStyle != null) return;

        // GUI.skin ใช้ได้เฉพาะใน OnGUI จึงสร้าง style ตอนวาดครั้งแรก
        chipStyle = new GUIStyle(GUI.skin.label)
        {
            alignment = TextAnchor.MiddleCenter,
            fontStyle = FontStyle.Bold,
            fontSize = 12
        };
        chipStyle.normal.textColor = new Color(0.08f, 0.08f, 0.08f);
    }

    private void DrawBox(Rect rect, Color color)
    {
        Color previous = GUI.color;
        GUI.color = color;
        GUI.DrawTexture(rect, pixel);
        GUI.color = previous;
    }
}
