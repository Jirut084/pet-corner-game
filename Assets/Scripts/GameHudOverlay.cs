using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// HUD หลักของเกม วาดด้วย OnGUI (ยังไม่ใช้ Canvas)
/// แสดง 5 อย่าง:
///   1) แถบสถานะ — เงิน / วัน / ออเดอร์ / ร้องเรียน
///   2) เกจความอดทนลอยเหนือหัวลูกค้าที่กำลังรออยู่
///   3) ป้ายเหนือหัวสัตว์ บอกว่ายังต้องไปสถานีไหน (แทนการย้อมสีตัวสัตว์แบบเดิม)
///   4) ป้าย "กดค้าง" + หลอดเวลาใต้ตัวผู้เล่น สำหรับสถานีโหมดกดค้าง (เช่นอ่างอาบน้ำ)
///   5) บันทึกเหตุการณ์ล่าสุดจาก GameLog มุมล่างซ้าย
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

    [Header("Hold Bar")]
    [Tooltip("ตำแหน่งหลอดกดค้างเทียบกับจุดกลางตัวผู้เล่น — ค่าเริ่มต้นอยู่แถวเท้า ไม่ทับป้ายเหนือหัวสัตว์ที่อุ้มอยู่")]
    [SerializeField] private Vector3 holdBarWorldOffset = new Vector3(0f, -1.1f, 0f);

    [SerializeField] private float holdBarWidth = 130f;
    [SerializeField] private float holdBarHeight = 12f;

    [Tooltip("หลอดยาวเลยท้ายช่วงสีเขียวไปอีกกี่เท่า ให้เห็นว่าเข็มกำลังจะเลยช่วง")]
    [SerializeField] private float holdBarOvershoot = 1.35f;

    [Header("Event Log")]
    [Tooltip("ข้อความจะจางหายหลังผ่านไปกี่วินาที (0 = ไม่จาง)")]
    [SerializeField] private float logFadeSeconds = 9f;

    private static readonly Color DoneChipColor = new Color(0.45f, 0.47f, 0.46f, 0.9f);
    private static readonly Color ReadyChipColor = new Color(0.45f, 0.85f, 0.55f);

    private static readonly Color HoldZoneColor = new Color(0.35f, 0.85f, 0.45f, 0.55f);
    private static readonly Color HoldChargingColor = new Color(0.98f, 0.82f, 0.35f);
    private static readonly Color HoldReadyColor = new Color(0.45f, 0.95f, 0.55f);
    private static readonly Color HoldFailColor = new Color(0.95f, 0.4f, 0.4f);

    private Texture2D pixel;
    private GUIStyle chipStyle;

    // ใช้ซ้ำทุกเฟรม ไม่สร้างลิสต์ใหม่ทุกครั้งที่วาด (OnGUI ถูกเรียกหลายรอบต่อเฟรม)
    private readonly List<(string text, Color color)> chipBuffer = new List<(string, Color)>();

    // ผู้เล่น — หาเมื่อต้องใช้ครั้งแรก
    private InteractionManager playerInteraction;
    private PlayerCarrier playerCarrier;

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

        if (game.Phase == GamePhase.Service)
        {
            DrawHoldPrompt();
        }

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

    /// <summary>
    /// ป้ายกับหลอดสำหรับสถานีโหมดกดค้าง — โชว์เฉพาะตอนหันไปหาสถานีนั้นและกดแล้วจะมีผลจริง
    ///   ยังไม่กด:   ป้าย "กด E ค้าง: อาบน้ำ"
    ///   กดค้างอยู่: หลอดมีโซนสีเขียว = ช่วงที่ต้องปล่อย, เข็มขาววิ่งตามเวลา
    ///               สีเหลือง = ยังไม่ถึง / เขียว = ปล่อยได้ / แดง = เลยช่วงแล้ว
    /// </summary>
    private void DrawHoldPrompt()
    {
        if (worldCamera == null) return;

        if (playerInteraction == null)
        {
            playerInteraction = FindAnyObjectByType<InteractionManager>();
            if (playerInteraction == null) return;

            playerCarrier = playerInteraction.GetComponent<PlayerCarrier>();
        }

        CareStation station = playerInteraction.CurrentTarget as CareStation;
        if (station == null || !station.UseHold || !station.CanServeByHold(playerCarrier)) return;

        Vector3 screen = worldCamera.WorldToScreenPoint(playerInteraction.transform.position + holdBarWorldOffset);
        if (screen.z <= 0f) return;

        EnsureStyles();

        float centerX = screen.x;
        float top = Screen.height - screen.y;
        string stationLabel = CareStepStyle.LabelOf(station.Step);

        bool holding = ReferenceEquals(playerInteraction.ActiveHoldTarget, station);
        if (!holding)
        {
            chipBuffer.Clear();
            chipBuffer.Add(($"กด {playerInteraction.InteractKey} ค้าง: {stationLabel}", CareStepStyle.ColorOf(station.Step)));
            DrawChipRow(centerX, top, chipBuffer);
            return;
        }

        float held = playerInteraction.CurrentHoldSeconds;
        float windowStart = station.HoldWindowStart;
        float windowEnd = station.HoldWindowEnd;
        float barSeconds = Mathf.Max(0.01f, windowEnd * holdBarOvershoot);

        bool failed = station.HoldFailed;
        bool inWindow = !failed && held >= windowStart && held <= windowEnd;
        Color stateColor = failed ? HoldFailColor : inWindow ? HoldReadyColor : HoldChargingColor;

        Rect bar = new Rect(centerX - holdBarWidth * 0.5f, top + chipHeight + 4f, holdBarWidth, holdBarHeight);

        DrawBox(new Rect(bar.x - 2f, bar.y - 2f, bar.width + 4f, bar.height + 4f), new Color(0f, 0f, 0f, 0.75f));
        DrawBox(bar, new Color(0.18f, 0.18f, 0.18f, 0.9f));

        // โซนสีเขียว = ช่วงที่ต้องปล่อยปุ่ม
        DrawBox(new Rect(
            bar.x + bar.width * (windowStart / barSeconds),
            bar.y,
            bar.width * ((windowEnd - windowStart) / barSeconds),
            bar.height), HoldZoneColor);

        // แถบเวลาที่กดค้างมาแล้ว + เข็มขาวตรงปลาย ให้เห็นชัดว่าอยู่ก่อน/ในหรือเลยโซนเขียว
        float fill = Mathf.Clamp01(held / barSeconds);
        DrawBox(new Rect(bar.x, bar.y + bar.height * 0.3f, bar.width * fill, bar.height * 0.4f), stateColor);
        DrawBox(new Rect(bar.x + bar.width * fill - 1.5f, bar.y - 4f, 3f, bar.height + 8f), Color.white);

        string caption = failed ? "ค้างนานเกิน — ปล่อยแล้วลองใหม่"
            : inWindow ? "ปล่อยเลย!"
            : $"ค้างไว้... ({stationLabel})";

        chipBuffer.Clear();
        chipBuffer.Add((caption, stateColor));
        DrawChipRow(centerX, top, chipBuffer);
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
