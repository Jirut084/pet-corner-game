using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// HUD หลักของเกม สร้างเป็น Canvas (uGUI) ด้วยโค้ดทั้งหมดตอน Awake — ไม่ต้องลาก UI ในฉาก
///
/// แนวทางหน้าตา (อิง PlateUp!): การ์ดสีครีมมุมโค้งบนเงาจางๆ ตัวหนังสือสีเข้ม
/// ใช้สีเฉพาะจุดที่ต้องสื่อความหมาย (ขั้นตอนของสัตว์ / ความอดทน / คำเตือน) ที่เหลือเป็นกลางหมด
/// ไม่มีกรอบ ไม่มีพื้นดำทึบ ไม่มีเส้นขอบรอบทุกอย่าง — ความชัดมาจากคอนทราสต์กับระยะห่าง
///
/// ส่วนประกอบ:
///   1) มุมซ้ายบน — วันที่ + หลอดความคืบหน้าออเดอร์ + จุดร้องเรียน
///   2) มุมขวาบน — เงิน
///   3) วงแหวนความอดทนเหนือหัวลูกค้า (รอสั่ง = วงประจาง, หลังรับออเดอร์ = วงเต็ม) + บับเบิลอีโมต
///   4) ป้ายขั้นตอนเหนือหัวสัตว์ — ไอคอน/จุดสีประจำสถานี + ชื่อ, ทำแล้วจาง
///   5) หลอดกดค้างใต้ตัวผู้เล่น (อ่างอาบน้ำ/แต่งขน)
///   6) บันทึกเหตุการณ์มุมล่างซ้าย — เสียงเบาที่สุดในจอ
///
/// ของที่ลอยตามตัวละคร (3-5) ใช้ object pool วางตำแหน่งจากพิกัดจอทุกเฟรม ไม่สร้าง/ทำลายซ้ำ
/// </summary>
public class GameHudOverlay : MonoBehaviour
{
    [Header("References")]
    [Tooltip("กล้องที่ใช้แปลงตำแหน่งโลกเป็นตำแหน่งจอ ปล่อยว่างจะใช้ Camera.main")]
    [SerializeField] private Camera worldCamera;

    [Header("Layout")]
    [Tooltip("ความละเอียดอ้างอิงของ Canvas — UI จะย่อ/ขยายตามจอจริงโดยอัตโนมัติ")]
    [SerializeField] private Vector2 referenceResolution = new Vector2(1600f, 900f);

    [Tooltip("ระยะห่างจากขอบจอของการ์ดมุมบน")]
    [SerializeField] private float screenMargin = 28f;

    [Header("Patience Ring")]
    [Tooltip("ขนาดวงแหวนความอดทนเหนือหัวลูกค้า")]
    [SerializeField] private float ringSize = 70f;

    [Header("Pet Tags")]
    [SerializeField] private float chipHeight = 34f;
    [SerializeField] private float chipPadding = 12f;
    [SerializeField] private float chipSpacing = 5f;

    [Header("Hold Bar")]
    [Tooltip("ตำแหน่งหลอดกดค้างเทียบกับจุดกลางตัวผู้เล่น")]
    [SerializeField] private Vector3 holdBarWorldOffset = new Vector3(0f, -1.1f, 0f);

    [SerializeField] private float holdBarWidth = 170f;
    [SerializeField] private float holdBarHeight = 12f;

    [Tooltip("หลอดยาวเลยท้ายช่วงที่ต้องปล่อยไปอีกกี่เท่า ให้เห็นว่าเข็มกำลังจะเลยช่วง")]
    [SerializeField] private float holdBarOvershoot = 1.35f;

    [Header("Event Log")]
    [Tooltip("ข้อความจะจางหายหลังผ่านไปกี่วินาที (0 = ไม่จาง)")]
    [SerializeField] private float logFadeSeconds = 7f;

    [Range(8, 40)]
    [SerializeField] private int logFontSize = 19;

    [Tooltip("เก็บข้อความไว้กี่บรรทัด — น้อยไว้ดีกว่า ล็อกไม่ควรแย่งสายตาจากร้าน")]
    [Range(1, 20)]
    [SerializeField] private int logMaxLines = 4;

    [SerializeField] private float logWidth = 560f;

    [Header("Type")]
    [Range(8, 48)]
    [SerializeField] private int titleFontSize = 32;

    [Range(8, 40)]
    [SerializeField] private int bodyFontSize = 20;

    [Range(8, 32)]
    [SerializeField] private int tagFontSize = 19;

    // สีและชิ้นส่วนทั้งหมดมาจาก UiKit เพื่อให้ HUD กับหน้าจอเมนูเป็นชุดเดียวกัน
    private static Color Card => UiKit.Card;
    private static Color CardQuiet => UiKit.CardQuiet;
    private static Color Ink => UiKit.Ink;
    private static Color InkSoft => UiKit.InkSoft;
    private static Color Track => UiKit.Track;
    private static Color Money => UiKit.Money;
    private static Color Good => UiKit.Good;
    private static Color Warn => UiKit.Warn;
    private static Color Danger => UiKit.Danger;

    private Canvas canvas;
    private RectTransform floatLayer;

    // มุมบน
    private TextMeshProUGUI dayText;
    private RectTransform orderFill;
    private TextMeshProUGUI orderText;
    private TextMeshProUGUI pressureText;
    private TextMeshProUGUI moneyText;
    private readonly List<Image> complaintDots = new List<Image>();
    private GameObject hintCard;
    private TextMeshProUGUI hintText;

    // บันทึกเหตุการณ์
    private GameObject logPanel;
    private readonly List<TextMeshProUGUI> logRows = new List<TextMeshProUGUI>();

    // ของที่ลอยตามตัวละคร
    private readonly List<Chip> chipPool = new List<Chip>();
    private int chipsUsed;
    private readonly List<Ring> ringPool = new List<Ring>();
    private int ringsUsed;
    private HoldBar holdBar;

    private readonly List<(string text, Color dot, Sprite icon, bool done)> chipBuffer = new List<(string, Color, Sprite, bool)>();

    private InteractionManager playerInteraction;
    private PlayerCarrier playerCarrier;

    /// <summary>ป้ายใบเดียว: การ์ดครีม + ไอคอน/จุดสีบอกประเภท + ข้อความ</summary>
    private class Chip
    {
        public RectTransform rect;
        public Image card;
        public Image dot;
        public Image icon;
        public TextMeshProUGUI label;
    }

    /// <summary>วงแหวนความอดทนเหนือหัวลูกค้า + ชื่อสัตว์ใต้วง + อีโมต</summary>
    private class Ring
    {
        public RectTransform rect;
        public Image track;
        public Image fill;
        public Image badge;   // จุดกลางวง บอกว่าเป็นช่วงรอสั่งหรือรอรับคืน
        public Image emote;   // อีโมตบอกอารมณ์ลูกค้า
        public TextMeshProUGUI label;
    }

    private class HoldBar
    {
        public RectTransform rect;
        public RectTransform zone;
        public RectTransform progress;
        public Image progressImage;
        public RectTransform needle;
    }

    private void Awake()
    {
        if (worldCamera == null) worldCamera = Camera.main;

        GameLog.MaxEntries = logMaxLines;

        BuildCanvas();
    }

    private void OnValidate()
    {
        GameLog.MaxEntries = logMaxLines;
    }

    // ---------- สร้าง UI ----------

    private void BuildCanvas()
    {
        // sortingOrder 10 = อยู่เหนือฉากแต่ต่ำกว่าหน้าจอเมนู (50) และไม่รับคลิก เพราะ HUD ไม่มีปุ่ม
        canvas = UiKit.MakeCanvas("HUD Canvas", transform, 10, false, referenceResolution);

        BuildDayCard();
        BuildMoneyCard();
        BuildHintCard();
        BuildEventLog();

        floatLayer = UiKit.MakeRect("Float Layer", canvas.transform);
        UiKit.Stretch(floatLayer);

        holdBar = BuildHoldBar();
    }

    /// <summary>การ์ดมุมซ้ายบน: วันที่ตัวใหญ่ + หลอดออเดอร์ + จุดร้องเรียน</summary>
    private void BuildDayCard()
    {
        RectTransform card = UiKit.MakeCard("Day Card", canvas.transform, Card);
        card.anchorMin = card.anchorMax = new Vector2(0f, 1f);
        card.pivot = new Vector2(0f, 1f);
        card.anchoredPosition = new Vector2(screenMargin, -screenMargin);
        card.sizeDelta = new Vector2(260f, 132f);

        // จัดกึ่งกลางแนวตั้งในแถวเดียวกับจุดร้องเรียน ฟอนต์ Mitr SemiBold หนาพออยู่แล้ว ไม่ต้องทำตัวหนาซ้ำ
        dayText = UiKit.MakeText("Day", card, titleFontSize, Ink, TextAlignmentOptions.Left);
        UiKit.Place(dayText.rectTransform, new Vector2(18f, -8f), new Vector2(140f, 44f));

        // หลอดความคืบหน้าออเดอร์ของวัน
        RectTransform track = UiKit.MakeImage("Order Track", card, Track).rectTransform;
        UiKit.Place(track, new Vector2(18f, -58f), new Vector2(224f, 8f));

        orderFill = UiKit.MakeImage("Order Fill", track, Good).rectTransform;
        orderFill.anchorMin = new Vector2(0f, 0f);
        orderFill.anchorMax = new Vector2(0f, 1f);
        orderFill.pivot = new Vector2(0f, 0.5f);
        orderFill.anchoredPosition = Vector2.zero;

        orderText = UiKit.MakeText("Order Label", card, bodyFontSize - 2, InkSoft, TextAlignmentOptions.TopLeft);
        UiKit.Place(orderText.rectTransform, new Vector2(18f, -72f), new Vector2(224f, bodyFontSize * 1.4f));

        pressureText = UiKit.MakeText("Pressure", card, bodyFontSize - 2, Warn, TextAlignmentOptions.TopLeft);
        UiKit.Place(pressureText.rectTransform, new Vector2(18f, -98f), new Vector2(224f, bodyFontSize * 1.4f));
        pressureText.gameObject.SetActive(false);

        // จุดร้องเรียน — เห็นปราดเดียวว่าเหลืออีกกี่ครั้งถึงแพ้ ไม่ต้องอ่านตัวเลข
        for (int i = 0; i < 5; i++)
        {
            Image dot = UiKit.MakeImage($"Complaint {i}", card, Track);
            dot.sprite = UiKit.Circle;
            UiKit.Place(dot.rectTransform, new Vector2(165f + i * 16f, -24f), new Vector2(12f, 12f));
            complaintDots.Add(dot);
        }
    }

    private void BuildMoneyCard()
    {
        RectTransform card = UiKit.MakeCard("Money Card", canvas.transform, Card);
        card.anchorMin = card.anchorMax = new Vector2(1f, 1f);
        card.pivot = new Vector2(1f, 1f);
        card.anchoredPosition = new Vector2(-screenMargin, -screenMargin);
        card.sizeDelta = new Vector2(190f, 64f);

        Image coin = UiKit.MakeImage("Coin", card, Money);
        coin.sprite = UiKit.Circle;
        UiKit.Place(coin.rectTransform, new Vector2(18f, -20f), new Vector2(24f, 24f));

        // ยืดเต็มความสูงการ์ดแล้วจัดกลางแนวตั้ง ตัวเลขจะอยู่ระดับเดียวกับเหรียญเสมอ
        moneyText = UiKit.MakeText("Money", card, titleFontSize - 2, Ink, TextAlignmentOptions.Left);
        UiKit.Place(moneyText.rectTransform, new Vector2(52f, 0f), new Vector2(126f, 64f));
    }

    /// <summary>แถบบอกวิธีเล่นช่วงจัดร้าน — ลอยกลางล่าง เสียงเบา ไม่แย่งสายตา</summary>
    private void BuildHintCard()
    {
        RectTransform card = UiKit.MakeCard("Hint", canvas.transform, CardQuiet);
        card.anchorMin = card.anchorMax = new Vector2(0.5f, 0f);
        card.pivot = new Vector2(0.5f, 0f);
        card.anchoredPosition = new Vector2(0f, screenMargin + 6f);
        card.sizeDelta = new Vector2(560f, 46f);
        hintCard = card.gameObject;

        hintText = UiKit.MakeText("Hint Text", card, bodyFontSize, Ink, TextAlignmentOptions.Center);
        UiKit.Stretch(hintText.rectTransform);
    }

    private void BuildEventLog()
    {
        float lineHeight = logFontSize * 1.6f;
        const float padding = 12f;

        // ไม่มีพื้นหลัง — อ่านออกได้ด้วย outline รอบตัวอักษรแทน
        RectTransform panel = UiKit.MakeRect("Event Log", canvas.transform);
        panel.anchorMin = panel.anchorMax = new Vector2(0f, 0f);
        panel.pivot = new Vector2(0f, 0f);
        panel.anchoredPosition = new Vector2(screenMargin, screenMargin);
        panel.sizeDelta = new Vector2(logWidth, logMaxLines * lineHeight + padding * 2f);
        logPanel = panel.gameObject;

        // เรียงจากล่างขึ้นบน: บรรทัดล่าสุดอยู่ติดขอบล่าง เหมือนแชท
        for (int i = 0; i < logMaxLines; i++)
        {
            TextMeshProUGUI row = UiKit.MakeText($"Line {i}", panel, logFontSize, Ink, TextAlignmentOptions.BottomLeft);
            row.outlineColor = new Color32(253, 248, 237, 230);
            row.outlineWidth = 0.2f;

            row.rectTransform.anchorMin = row.rectTransform.anchorMax = new Vector2(0f, 0f);
            row.rectTransform.pivot = new Vector2(0f, 0f);
            row.rectTransform.anchoredPosition = new Vector2(padding, padding + i * lineHeight);
            row.rectTransform.sizeDelta = new Vector2(logWidth - padding * 2f, lineHeight);
            row.gameObject.SetActive(false);
            logRows.Add(row);
        }
    }

    private HoldBar BuildHoldBar()
    {
        RectTransform rect = UiKit.MakeCard("Hold Bar", floatLayer, Card);
        rect.pivot = new Vector2(0.5f, 1f);
        rect.sizeDelta = new Vector2(holdBarWidth + 12f, holdBarHeight + 12f);

        RectTransform track = UiKit.MakeImage("Track", rect, Track).rectTransform;
        UiKit.Stretch(track, 6f);

        RectTransform zone = UiKit.MakeImage("Zone", track, new Color(Good.r, Good.g, Good.b, 0.35f)).rectTransform;
        zone.anchorMin = new Vector2(0f, 0f);
        zone.anchorMax = new Vector2(0f, 1f);
        zone.pivot = new Vector2(0f, 0.5f);

        Image progressImage = UiKit.MakeImage("Progress", track, Warn);
        RectTransform progress = progressImage.rectTransform;
        progress.anchorMin = new Vector2(0f, 0f);
        progress.anchorMax = new Vector2(0f, 1f);
        progress.pivot = new Vector2(0f, 0.5f);

        RectTransform needle = UiKit.MakeImage("Needle", track, Ink).rectTransform;
        needle.anchorMin = new Vector2(0f, 0f);
        needle.anchorMax = new Vector2(0f, 1f);
        needle.pivot = new Vector2(0.5f, 0.5f);
        needle.sizeDelta = new Vector2(3f, 6f);

        rect.gameObject.SetActive(false);

        return new HoldBar { rect = rect, zone = zone, progress = progress, progressImage = progressImage, needle = needle };
    }

    // ---------- อัปเดตทุกเฟรม ----------

    private void LateUpdate()
    {
        GameManager game = GameManager.Instance;

        // หน้าเมนู/หน้าเลือกรางวัลมี UI ของตัวเองอยู่แล้ว ซ่อน HUD ทั้งก้อน
        bool visible = game != null && game.Phase != GamePhase.MainMenu && game.Phase != GamePhase.DayEnd;
        if (canvas.enabled != visible) canvas.enabled = visible;
        if (!visible) return;

        if (worldCamera == null) worldCamera = Camera.main;

        UpdateTopCards(game);
        UpdateEventLog();

        chipsUsed = 0;
        ringsUsed = 0;

        UpdateCustomerRings();
        UpdatePetTags();

        if (game.Phase == GamePhase.Service) UpdateHoldPrompt();
        else holdBar.rect.gameObject.SetActive(false);

        HideUnused(chipPool, chipsUsed, c => c.rect.gameObject);
        HideUnused(ringPool, ringsUsed, r => r.rect.gameObject);
    }

    private void UpdateTopCards(GameManager game)
    {
        dayText.text = $"วันที่ {game.Day}";
        moneyText.text = ReturnZone.TotalWallet.ToString();

        bool service = game.Phase == GamePhase.Service;

        float progress = game.OrdersTarget > 0 ? Mathf.Clamp01((float)game.OrdersCompleted / game.OrdersTarget) : 0f;
        orderFill.sizeDelta = new Vector2(224f * progress, 0f);
        orderText.text = service
            ? $"ออเดอร์ {game.OrdersCompleted}/{game.OrdersTarget}   ·   ดูแลอยู่ {game.ActiveOrderCount}/{game.ConcurrentOrderLimit}"
            : $"วันนี้ {game.OrdersTarget} ออเดอร์   ·   ชื่อเสียง {game.Reputation}";

        float pressure = CustomerManager.PatiencePressure;
        bool underPressure = service && pressure > 1.05f;
        string eventLabel = game.CurrentEvent != null ? game.CurrentEvent.label : null;
        bool showLine = underPressure || eventLabel != null;

        if (pressureText.gameObject.activeSelf != showLine) pressureText.gameObject.SetActive(showLine);
        if (showLine)
        {
            string crowd = underPressure
                ? (game.IsLateDay ? $"ใจร้อน ×{pressure:0.0}" : $"แน่นร้าน ×{pressure:0.0}")
                : null;

            pressureText.text = eventLabel != null && crowd != null ? $"{eventLabel} · {crowd}"
                : eventLabel ?? (game.IsLateDay ? $"ท้ายวัน · ลูกค้าใจร้อน ×{pressure:0.0}" : $"ลูกค้าแน่นร้าน ×{pressure:0.0}");
            pressureText.color = pressure >= 2f ? Danger : Warn;
        }

        for (int i = 0; i < complaintDots.Count; i++)
        {
            bool used = i < game.Complaints;
            complaintDots[i].color = used ? Danger : Track;
        }

        bool setup = game.Phase == GamePhase.Setup;
        if (hintCard.activeSelf != setup) hintCard.SetActive(setup);
        if (setup) hintText.text = "จัดร้าน — E ยก/วางสถานี   ·   Enter เปิดร้าน";
    }

    private void UpdateEventLog()
    {
        var entries = GameLog.Entries;
        if (logPanel.activeSelf != (entries.Count > 0)) logPanel.SetActive(entries.Count > 0);

        for (int row = 0; row < logRows.Count; row++)
        {
            int entryIndex = entries.Count - 1 - row;
            TextMeshProUGUI line = logRows[row];

            if (entryIndex < 0)
            {
                if (line.gameObject.activeSelf) line.gameObject.SetActive(false);
                continue;
            }

            GameLog.Entry entry = entries[entryIndex];

            float alpha = 1f;
            if (logFadeSeconds > 0f)
            {
                float age = Time.unscaledTime - entry.time;
                alpha = Mathf.Clamp01(1f - (age - logFadeSeconds) / 2.5f);
            }

            alpha *= Mathf.Lerp(1f, 0.55f, row / Mathf.Max(1f, logRows.Count - 1f));

            if (alpha <= 0.02f)
            {
                if (line.gameObject.activeSelf) line.gameObject.SetActive(false);
                continue;
            }

            if (!line.gameObject.activeSelf) line.gameObject.SetActive(true);

            Color tone = GameLog.ColorFor(entry.tone);
            line.text = entry.text;
            Color readable = Color.Lerp(Ink, tone * 0.75f, 0.55f);
            line.color = new Color(readable.r, readable.g, readable.b, alpha);
            line.outlineColor = new Color(0.99f, 0.97f, 0.93f, 0.9f * alpha);
        }
    }

    private void UpdateCustomerRings()
    {
        CustomerManager manager = CustomerManager.Instance;
        if (manager == null || worldCamera == null) return;

        foreach (Customer customer in manager.Customers)
        {
            if (customer == null) continue;

            bool orderGauge = customer.ShowGauge;
            bool counterGauge = !orderGauge && customer.ShowCounterGauge;
            if (!orderGauge && !counterGauge) continue;

            if (!TryGetScreenPoint(customer.transform.position + Vector3.up * customer.GaugeHeight, out Vector2 point)) continue;

            float fill = Mathf.Clamp01(orderGauge ? customer.PatienceNormalized : customer.CounterPatienceNormalized);
            string label = orderGauge
                ? (customer.AssignedPet == null ? string.Empty
                    : customer.IsAwaitingPickup ? $"รอรับ {customer.AssignedPet.DisplayName}"
                    : customer.AssignedPet.DisplayName)
                : customer.IsOutside ? "รอนอกร้าน" : "รอสั่ง";

            ShowRing(point, fill, label, orderGauge, customer.Mood);

            if (customer.WantsGood)
            {
                chipBuffer.Clear();
                chipBuffer.Add(($"อยากได้ {customer.WantedGood.label}  +{customer.WantedGood.bonusPay}", Money, null, false));
                ShowChipRow(point + new Vector2(0f, -ringSize * 0.55f), chipBuffer);
            }
        }
    }

    /// <summary>
    /// ป้ายเหนือหัวสัตว์: 1 ใบต่อ 1 ขั้นตอน — ไอคอน/จุดสีประจำสถานี + ชื่อขั้นตอน ทำแล้วจางลง
    /// ครบทุกขั้นตอนเหลือใบเขียว "ส่งคืนได้" สัตว์ที่หนีอยู่มีใบแดงนำหน้า
    /// </summary>
    private void UpdatePetTags()
    {
        if (worldCamera == null) return;

        foreach (Pet pet in Pet.All)
        {
            if (pet == null || pet.Species == null || pet.Species.requiredSteps == null) continue;
            if (!TryGetScreenPoint(pet.OverheadWorldPosition, out Vector2 point)) continue;

            chipBuffer.Clear();

            PetEscape escape = pet.GetComponent<PetEscape>();
            if (escape != null && escape.IsEscaping)
            {
                chipBuffer.Add((escape.IsCornered ? "กด E จับ" : "หนีแล้ว!", Danger, null, false));
            }

            if (pet.TraitLabel != null) chipBuffer.Add((pet.TraitLabel, Ink, null, false));

            if (pet.IsComplete)
            {
                string tip = pet.CollectedTip > 0 ? $"ส่งคืนได้  +{pet.CollectedTip}" : "ส่งคืนได้";
                chipBuffer.Add((tip, Good, null, false));
            }
            else
            {
                foreach (CareStep step in pet.OrderedSteps)
                {
                    bool done = pet.HasCompleted(step);
                    Sprite stepIcon = UiKit.StepIcon(step);
                    chipBuffer.Add((CareStepStyle.LabelOf(step), CareStepStyle.ColorOf(step), stepIcon, done));
                }
            }

            ShowChipRow(point, chipBuffer);
        }
    }

    /// <summary>
    /// เลือกผู้เล่นที่จะโชว์ป้าย/หลอดกดค้าง แล้วเก็บไว้ใน playerInteraction / playerCarrier
    /// คืนสถานีที่ควรโชว์ (null = ไม่มีใครหันหาสถานีกดค้างที่ใช้ได้)
    /// </summary>
    private CareStation PickHoldPromptPlayer()
    {
        CareStation facing = null;
        InteractionManager facingPlayer = null;

        foreach (PlayerController player in PlayerController.All)
        {
            // ผู้เล่นออนไลน์ของเครื่องอื่นปิด PlayerController ไว้ จึงไม่ถูกนับ
            if (player == null || !player.isActiveAndEnabled) continue;

            InteractionManager interaction = player.GetComponent<InteractionManager>();
            if (interaction == null) continue;

            CareStation station = interaction.CurrentTarget as CareStation;
            PlayerCarrier carrier = player.GetComponent<PlayerCarrier>();
            if (station == null || !station.UseHold || !station.CanServeByHold(carrier)) continue;

            if (ReferenceEquals(interaction.ActiveHoldTarget, station))
            {
                playerInteraction = interaction;
                playerCarrier = carrier;
                return station;
            }

            if (facing == null)
            {
                facing = station;
                facingPlayer = interaction;
            }
        }

        if (facing != null)
        {
            playerInteraction = facingPlayer;
            playerCarrier = facingPlayer.GetComponent<PlayerCarrier>();
        }

        return facing;
    }

    /// <summary>
    /// ป้ายกับหลอดสำหรับสถานีโหมดกดค้าง — โชว์เฉพาะตอนหันไปหาสถานีนั้นและกดแล้วมีผลจริง
    /// </summary>
    private void UpdateHoldPrompt()
    {
        if (worldCamera == null) return;

        // ponytail: หลอดกดค้างมีชุดเดียว เล่นหลายคนจะโชว์ของคนที่กำลังกดค้างก่อน ไม่งั้นคนแรกที่หันหาสถานีกดค้าง
        // ถ้าต้องโชว์พร้อมกันหลายคน ค่อยแยก holdBar เป็นรายผู้เล่น
        CareStation station = PickHoldPromptPlayer();
        bool show = station != null;

        if (!show || !TryGetScreenPoint(playerInteraction.transform.position + holdBarWorldOffset, out Vector2 point))
        {
            holdBar.rect.gameObject.SetActive(false);
            return;
        }

        string stationLabel = CareStepStyle.LabelOf(station.Step);
        bool holding = ReferenceEquals(playerInteraction.ActiveHoldTarget, station);

        if (!holding)
        {
            holdBar.rect.gameObject.SetActive(false);

            chipBuffer.Clear();
            chipBuffer.Add(($"กด {playerInteraction.InteractLabel} ค้าง · {stationLabel}", CareStepStyle.ColorOf(station.Step), UiKit.StepIcon(station.Step), false));
            ShowChipRow(point, chipBuffer);
            return;
        }

        float held = playerInteraction.CurrentHoldSeconds;
        float windowStart = station.HoldWindowStart;
        float windowEnd = station.HoldWindowEnd;
        float barSeconds = Mathf.Max(0.01f, windowEnd * holdBarOvershoot);

        bool failed = station.HoldFailed;
        bool inWindow = !failed && held >= windowStart && held <= windowEnd;
        Color stateColor = failed ? Danger : inWindow ? Good : Warn;

        holdBar.rect.gameObject.SetActive(true);
        holdBar.rect.sizeDelta = new Vector2(holdBarWidth + 12f, holdBarHeight + 12f);
        holdBar.rect.anchoredPosition = point + new Vector2(0f, -ChipHeight - 8f);

        holdBar.zone.anchoredPosition = new Vector2(holdBarWidth * (windowStart / barSeconds), 0f);
        holdBar.zone.sizeDelta = new Vector2(holdBarWidth * ((windowEnd - windowStart) / barSeconds), 0f);

        float fill = Mathf.Clamp01(held / barSeconds);
        holdBar.progress.sizeDelta = new Vector2(holdBarWidth * fill, 0f);
        holdBar.progressImage.color = stateColor;
        holdBar.needle.anchoredPosition = new Vector2(holdBarWidth * fill, 0f);

        string caption = failed ? "ค้างนานไป — ปล่อยแล้วเริ่มใหม่"
            : inWindow ? "ปล่อยเลย!"
            : stationLabel;

        chipBuffer.Clear();
        chipBuffer.Add((caption, stateColor, null, false));
        ShowChipRow(point, chipBuffer);
    }

    // ---------- pool ของที่ลอยตามตัวละคร ----------

    private float ChipHeight => Mathf.Max(chipHeight, tagFontSize * 1.8f);

    /// <summary>วางป้ายเรียงแถวเดียว จัดกึ่งกลางที่พิกัดจอที่ให้มา</summary>
    private void ShowChipRow(Vector2 point, List<(string text, Color dot, Sprite icon, bool done)> chips)
    {
        if (chips.Count == 0) return;

        const float dotSize = 10f;
        const float iconSize = 22f;

        float totalWidth = -chipSpacing;
        for (int i = 0; i < chips.Count; i++)
        {
            Chip chip = TakeChip(chipsUsed + i);
            chip.label.text = chips[i].text;
            float symbolSize = chips[i].icon != null ? iconSize : dotSize;
            totalWidth += chip.label.preferredWidth + chipPadding * 2f + symbolSize + 6f + chipSpacing;
        }

        float x = point.x - totalWidth * 0.5f;

        for (int i = 0; i < chips.Count; i++)
        {
            Chip chip = TakeChip(chipsUsed + i);
            bool hasIcon = chips[i].icon != null;
            float symbolSize = hasIcon ? iconSize : dotSize;
            float width = chip.label.preferredWidth + chipPadding * 2f + symbolSize + 6f;

            chip.rect.sizeDelta = new Vector2(width, ChipHeight);
            chip.rect.anchoredPosition = new Vector2(x + width * 0.5f, point.y);

            bool done = chips[i].done;
            chip.card.color = done ? CardQuiet : Card;
            chip.label.color = done ? new Color(Ink.r, Ink.g, Ink.b, 0.62f) : Ink;

            if (hasIcon)
            {
                chip.icon.gameObject.SetActive(true);
                chip.dot.gameObject.SetActive(false);
                chip.icon.sprite = chips[i].icon;
                chip.icon.color = done ? new Color(1f, 1f, 1f, 0.45f) : Color.white;
                UiKit.Place(chip.icon.rectTransform, new Vector2(chipPadding, -(ChipHeight - iconSize) * 0.5f), new Vector2(iconSize, iconSize));
            }
            else
            {
                chip.icon.gameObject.SetActive(false);
                chip.dot.gameObject.SetActive(true);
                chip.dot.color = done ? Track : chips[i].dot;
                UiKit.Place(chip.dot.rectTransform, new Vector2(chipPadding, -(ChipHeight - dotSize) * 0.5f), new Vector2(dotSize, dotSize));
            }

            UiKit.Place(chip.label.rectTransform, new Vector2(chipPadding + symbolSize + 6f, 0f), new Vector2(width, ChipHeight));

            x += width + chipSpacing;
        }

        chipsUsed += chips.Count;
    }

    private Chip TakeChip(int index)
    {
        while (chipPool.Count <= index) chipPool.Add(BuildChip());

        Chip chip = chipPool[index];
        if (!chip.rect.gameObject.activeSelf) chip.rect.gameObject.SetActive(true);
        return chip;
    }

    private Chip BuildChip()
    {
        RectTransform rect = UiKit.MakeCard($"Chip {chipPool.Count}", floatLayer, Card, out Image card);
        rect.pivot = new Vector2(0.5f, 1f);

        Image dot = UiKit.MakeImage("Dot", rect, Ink);
        dot.sprite = UiKit.Circle;

        Image icon = UiKit.MakeImage("Icon", rect, Color.white);
        icon.preserveAspect = true;
        icon.gameObject.SetActive(false);

        TextMeshProUGUI label = UiKit.MakeText("Label", rect, tagFontSize, Ink, TextAlignmentOptions.Left);
        label.fontStyle = FontStyles.Bold;

        return new Chip { rect = rect, card = card, dot = dot, icon = icon, label = label };
    }

    private void ShowRing(Vector2 point, float fill, string label, bool afterOrder, Customer.CustomerMood mood)
    {
        Ring ring = TakeRing(ringsUsed);
        ringsUsed++;

        ring.rect.sizeDelta = new Vector2(ringSize, ringSize);
        ring.rect.anchoredPosition = point;

        ring.fill.fillAmount = fill;
        ring.fill.color = PatienceColor(fill);

        ring.track.color = afterOrder ? Track : new Color(Ink.r, Ink.g, Ink.b, 0.07f);
        ring.badge.color = afterOrder ? Card : new Color(0.55f, 0.72f, 0.95f, 0.95f);

        Sprite emoteSprite = UiKit.MoodEmote(mood);
        if (emoteSprite != null)
        {
            ring.emote.gameObject.SetActive(true);
            ring.emote.sprite = emoteSprite;
        }
        else
        {
            ring.emote.gameObject.SetActive(false);
        }

        ring.label.text = label;
        ring.label.color = afterOrder ? Ink : InkSoft;
    }

    private Ring TakeRing(int index)
    {
        while (ringPool.Count <= index) ringPool.Add(BuildRing());

        Ring ring = ringPool[index];
        if (!ring.rect.gameObject.activeSelf) ring.rect.gameObject.SetActive(true);
        return ring;
    }

    private Ring BuildRing()
    {
        RectTransform rect = UiKit.MakeRect($"Ring {ringPool.Count}", floatLayer);

        Image track = UiKit.MakeImage("Track", rect, Track);
        track.sprite = UiKit.Ring;
        UiKit.Stretch(track.rectTransform);

        Image fill = UiKit.MakeImage("Fill", rect, Good);
        fill.sprite = UiKit.Ring;
        fill.type = Image.Type.Filled;
        fill.fillMethod = Image.FillMethod.Radial360;
        fill.fillOrigin = (int)Image.Origin360.Top;
        fill.fillClockwise = true;
        UiKit.Stretch(fill.rectTransform);

        Image badge = UiKit.MakeImage("Badge", rect, Card);
        badge.sprite = UiKit.Circle;
        UiKit.Stretch(badge.rectTransform, ringSize * 0.22f);

        // อีโมตเหนือวงแหวน
        Image emote = UiKit.MakeImage("Emote", rect, Color.white);
        emote.preserveAspect = true;
        UiKit.Place(emote.rectTransform, new Vector2((ringSize - 26f) * 0.5f, 26f), new Vector2(26f, 26f));
        emote.gameObject.SetActive(false);

        TextMeshProUGUI label = UiKit.MakeText("Label", rect, tagFontSize - 1, Ink, TextAlignmentOptions.Center);
        label.fontStyle = FontStyles.Bold;
        label.enableAutoSizing = true;
        label.fontSizeMin = 10;
        label.fontSizeMax = tagFontSize - 1;
        label.textWrappingMode = TextWrappingModes.Normal;
        label.overflowMode = TextOverflowModes.Truncate;

        RectTransform labelRect = label.rectTransform;
        labelRect.anchorMin = labelRect.anchorMax = new Vector2(0.5f, 0.5f);
        labelRect.pivot = new Vector2(0.5f, 0.5f);
        labelRect.sizeDelta = new Vector2(ringSize * 0.58f, ringSize * 0.58f);
        labelRect.anchoredPosition = Vector2.zero;

        return new Ring { rect = rect, track = track, fill = fill, badge = badge, emote = emote, label = label };
    }

    private static void HideUnused<T>(List<T> pool, int used, System.Func<T, GameObject> selector)
    {
        for (int i = used; i < pool.Count; i++)
        {
            GameObject go = selector(pool[i]);
            if (go.activeSelf) go.SetActive(false);
        }
    }

    // ---------- ตัวช่วยสร้าง UI ----------

    /// <summary>แปลงตำแหน่งโลกเป็นพิกัดใน Canvas — คืน false ถ้าอยู่หลังกล้อง</summary>
    private bool TryGetScreenPoint(Vector3 worldPosition, out Vector2 point)
    {
        Vector3 screen = worldCamera.WorldToScreenPoint(worldPosition);
        if (screen.z <= 0f)
        {
            point = Vector2.zero;
            return false;
        }

        float scale = canvas.scaleFactor > 0f ? canvas.scaleFactor : 1f;
        point = new Vector2(screen.x / scale, screen.y / scale);
        return true;
    }

    /// <summary>สีของมิเตอร์ความอดทน: เขียว → เหลือง → แดง ตามเวลาที่เหลือ</summary>
    private static Color PatienceColor(float fill)
    {
        if (fill > 0.5f) return Good;
        if (fill > 0.25f) return Warn;
        return Danger;
    }
}
