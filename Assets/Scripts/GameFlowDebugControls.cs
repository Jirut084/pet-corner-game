using UnityEngine;

/// <summary>
/// หน้าจอเมนูทั้งหมดของเกม วาดด้วย OnGUI (ยังไม่ใช้ Canvas)
/// ใช้ได้จริงตั้งแต่ตอนนี้ และแทนที่ด้วย UI จริงทีหลังได้โดยไม่ต้องแก้ระบบข้างล่าง
///
///   เมนูหลัก      — คลิกการ์ดสัตว์ (หรือกด 1..9) เพื่อเริ่มเล่น / Esc ออกเกม
///   ช่วงจัดร้าน    — กด Enter เปิดร้าน
///   ท้ายวัน       — คลิกการ์ดรางวัล (หรือกด 1..9) แล้วต่อด้วยร้านค้าท้ายวัน
///   จบเกม        — สรุปผล + ปุ่มเล่นใหม่ / ออกเกม
///   ระหว่างเล่น    — Esc เปิดเมนูหยุดเกม (เล่นต่อ / กลับเมนูหลัก / ออกเกม)
///
/// เมนูหยุดเกมตั้ง Time.timeScale = 0 ซึ่งหยุดแค่การเคลื่อนที่ ส่วน Update กับ Input ยังทำงานอยู่
/// ระบบที่รับปุ่ม (เดิน/โต้ตอบ/ยกสถานี/ซื้อของ) จึงเช็ค GameManager.IsPaused เองอีกชั้น
/// </summary>
public class GameFlowDebugControls : MonoBehaviour
{
    [Header("Main Menu")]
    [Tooltip("สัตว์ที่ให้เลือกเป็นตัวเริ่มต้น — สถานีเริ่มต้นจะมาจาก need ของตัวที่เลือก")]
    [SerializeField] private PetSpecies[] selectableStarters;

    [Tooltip("ชื่อเกมบนหน้าเมนู")]
    [SerializeField] private string gameTitle = "PET CORNER";

    [Header("Keys")]
    [SerializeField] private KeyCode openShopKey = KeyCode.Return;

    [Tooltip("ปุ่มเปิดเมนูหยุดเกมระหว่างเล่น และออกเกมเมื่ออยู่หน้าเมนูหลัก")]
    [SerializeField] private KeyCode pauseKey = KeyCode.Escape;

    [Header("Layout")]
    [SerializeField] private int cardWidth = 240;
    [SerializeField] private int cardHeight = 132;

    /// <summary>สัตว์ทั้งหมดที่ให้เลือกในหน้าเมนู — ร้านค้าท้ายวันใช้เป็นรายการสัตว์ที่ขายได้ ถ้าไม่ได้ตั้งรายการของตัวเอง</summary>
    public PetSpecies[] SelectableStarters => selectableStarters;

    private DayEndRewardSystem rewardSystem;

    // เฟรมที่เพิ่งเข้าช่วงจัดร้าน — Enter ที่ใช้ปิดร้านค้าท้ายวันยังนับเป็น GetKeyDown อยู่ในเฟรมนั้น
    // ถ้าไม่ข้าม ร้านจะเปิดรับลูกค้าทันทีโดยผู้เล่นยังไม่ได้จัดร้าน
    private int setupEnteredFrame = -1;

    // พื้นหลังทึบของหน้าจอเมนู สร้างครั้งเดียวใช้ซ้ำทั้งเกม
    private Texture2D pixel;
    private GUIStyle titleStyle;
    private GUIStyle headingStyle;
    private GUIStyle centeredStyle;

    private static readonly Color ScreenDim = new Color(0.04f, 0.05f, 0.07f, 0.92f);
    private static readonly Color PanelColor = new Color(0.12f, 0.14f, 0.18f, 0.97f);
    private static readonly Color WinColor = new Color(0.5f, 0.9f, 0.6f);
    private static readonly Color LoseColor = new Color(0.95f, 0.5f, 0.45f);

    private void Awake()
    {
        rewardSystem = FindAnyObjectByType<DayEndRewardSystem>();

        pixel = new Texture2D(1, 1);
        pixel.SetPixel(0, 0, Color.white);
        pixel.Apply();
    }

    private void Start()
    {
        if (GameManager.Instance != null)
        {
            GameManager.Instance.OnPhaseChanged += HandlePhaseChanged;
        }
    }

    private void OnDestroy()
    {
        if (GameManager.Instance != null)
        {
            GameManager.Instance.OnPhaseChanged -= HandlePhaseChanged;
        }

        if (pixel != null) Destroy(pixel);
    }

    private void HandlePhaseChanged(GamePhase phase)
    {
        if (phase == GamePhase.Setup)
        {
            setupEnteredFrame = Time.frameCount;
        }
    }

    private void Update()
    {
        GameManager game = GameManager.Instance;
        if (game == null) return;

        if (Input.GetKeyDown(pauseKey))
        {
            HandlePauseKey(game);
            return;
        }

        // เมนูหยุดเกมเปิดอยู่ = ไม่รับปุ่มอื่นเลย
        if (game.IsPaused) return;

        if (game.Phase == GamePhase.MainMenu)
        {
            HandleStarterHotkeys(game);
            return;
        }

        if (game.Phase == GamePhase.Setup && Time.frameCount != setupEnteredFrame && Input.GetKeyDown(openShopKey))
        {
            game.BeginServicePhase();
        }
    }

    /// <summary>
    /// Esc ทำหน้าที่ต่างกันตามหน้าจอ:
    /// เมนูหลัก/จบเกม = ออกเกมเลย (ไม่มีอะไรให้เสีย) ระหว่างเล่น = สลับเปิด/ปิดเมนูหยุดเกม
    /// </summary>
    private void HandlePauseKey(GameManager game)
    {
        if (game.Phase == GamePhase.MainMenu || game.Phase == GamePhase.RunOver)
        {
            QuitGame();
            return;
        }

        game.SetPaused(!game.IsPaused);
    }

    private void HandleStarterHotkeys(GameManager game)
    {
        if (selectableStarters == null) return;

        for (int i = 0; i < selectableStarters.Length && i < 9; i++)
        {
            if (Input.GetKeyDown(KeyCode.Alpha1 + i))
            {
                StartRunWith(selectableStarters[i], game);
                return;
            }
        }
    }

    private void StartRunWith(PetSpecies starter, GameManager game)
    {
        if (starter == null) return;

        // ต้อง ResetForNewRun ก่อน StartNewRun เสมอ เพราะ StartNewRun จะเข้า Setup ทันที
        // แล้ว LevelInitializer จะอ่านสถานีที่ปลดล็อกไปวางในจังหวะนั้นเลย
        PlayerProgress.Instance?.ResetForNewRun(starter);
        game.StartNewRun();

        Debug.Log($"[GameFlow] เริ่ม run ด้วย {starter.speciesName}");
    }

    /// <summary>
    /// ปิดเกม — ในโหมด Play ของ Editor ปิดโปรแกรมไม่ได้ จึงสั่งหยุดเล่นแทนเพื่อให้เทสปุ่มนี้ได้จริง
    /// </summary>
    private static void QuitGame()
    {
        Debug.Log("[GameFlow] ออกจากเกม");

#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    private void OnGUI()
    {
        GameManager game = GameManager.Instance;
        if (game == null) return;

        // ค่าติดลบ = วาดทับของคนอื่น เมนูจึงอยู่หน้า HUD เสมอไม่ว่าสคริปต์ไหนจะรันก่อน
        GUI.depth = -50;

        EnsureStyles();

        // หยุดเกมอยู่ = วาดแค่เมนูหยุดเกม ไม่วาดหน้าจออื่นทับไว้ข้างหลัง
        // เพราะ IMGUI ไม่ได้กันคลิกตามลำดับการวาด ปุ่มร้านค้าที่โผล่พ้นกรอบเมนูจะยังกดได้อยู่
        if (game.IsPaused)
        {
            DrawPauseMenu(game);
            return;
        }

        // แถบสถานะ/เกจ/บันทึกเหตุการณ์ ย้ายไปอยู่ที่ GameHudOverlay แล้ว
        // ที่นี่เหลือแค่หน้าจอที่ต้องกดเลือก
        switch (game.Phase)
        {
            case GamePhase.MainMenu: DrawMainMenu(game); break;
            case GamePhase.DayEnd: DrawDayEnd(game); break;
            case GamePhase.RunOver: DrawRunOver(game); break;
        }
    }

    // ---------- เมนูหลัก ----------

    private void DrawMainMenu(GameManager game)
    {
        DrawFullScreenDim();

        float panelWidth = Mathf.Min(Screen.width - 80f, selectableStarters != null && selectableStarters.Length > 0
            ? selectableStarters.Length * (cardWidth + 14) + 60f
            : 640f);

        float panelHeight = cardHeight + 230f;
        Rect panel = CenteredRect(panelWidth, panelHeight);

        DrawBox(panel, PanelColor);

        GUI.Label(new Rect(panel.x, panel.y + 26f, panel.width, 46f), gameTitle, titleStyle);
        GUI.Label(new Rect(panel.x, panel.y + 74f, panel.width, 24f), "ร้านรับฝากดูแลสัตว์เลี้ยง", centeredStyle);
        GUI.Label(new Rect(panel.x, panel.y + 104f, panel.width, 24f),
            $"เลือกสัตว์ที่จะเปิดรับ — สถานีเริ่มต้นในร้านมาจากสิ่งที่สัตว์ตัวนั้นต้องการ  |  อยู่ให้ครบ {game.TotalDays} วัน",
            centeredStyle);

        if (selectableStarters == null || selectableStarters.Length == 0)
        {
            GUI.Label(new Rect(panel.x, panel.y + 150f, panel.width, 24f),
                "ยังไม่ได้ใส่ Selectable Starters ใน Inspector", centeredStyle);
            return;
        }

        float cardsWidth = selectableStarters.Length * cardWidth + (selectableStarters.Length - 1) * 14f;
        float cardsLeft = panel.x + (panel.width - cardsWidth) * 0.5f;
        float cardsTop = panel.y + 142f;

        for (int i = 0; i < selectableStarters.Length; i++)
        {
            PetSpecies species = selectableStarters[i];
            if (species == null) continue;

            Rect rect = new Rect(cardsLeft + i * (cardWidth + 14f), cardsTop, cardWidth, cardHeight);
            string needs = string.Join(", ", System.Array.ConvertAll(species.requiredSteps, CareStepStyle.LabelOf));

            string body = $"[{i + 1}]  {species.speciesName}\n\n" +
                          $"ต้องการ: {needs}\n" +
                          $"อดทน: {species.patienceSeconds:F0} วิ\n" +
                          $"ค่าบริการ: {species.baseFee} + {species.feePerStep}/ขั้นตอน";

            if (GUI.Button(rect, body))
            {
                StartRunWith(species, game);
            }
        }

        float bottom = cardsTop + cardHeight + 22f;
        GUI.Label(new Rect(panel.x, bottom, panel.width, 22f),
            "WASD เดิน  |  E โต้ตอบ  |  G ยก/วางสถานี  |  Enter เปิดร้าน", centeredStyle);

        Rect quit = new Rect(panel.x + (panel.width - 200f) * 0.5f, bottom + 28f, 200f, 34f);
        if (GUI.Button(quit, $"ออกจากเกม  [{pauseKey}]"))
        {
            QuitGame();
        }
    }

    // ---------- หน้าจบเกม ----------

    private void DrawRunOver(GameManager game)
    {
        DrawFullScreenDim();

        Rect panel = CenteredRect(520f, 300f);
        DrawBox(panel, PanelColor);

        bool won = game.LastRunWon;

        Color previous = GUI.color;
        GUI.color = won ? WinColor : LoseColor;
        GUI.Label(new Rect(panel.x, panel.y + 26f, panel.width, 46f), won ? "ร้านอยู่รอด!" : "ร้านเจ๊ง", titleStyle);
        GUI.color = previous;

        string summary = won
            ? $"อยู่ครบ {game.TotalDays} วันตามเป้า"
            : $"ร้องเรียนครบ {game.MaxComplaints} ครั้งในวันที่ {game.Day}";

        GUI.Label(new Rect(panel.x, panel.y + 80f, panel.width, 24f), summary, centeredStyle);

        GUI.Label(new Rect(panel.x, panel.y + 120f, panel.width, 24f),
            $"ไปถึงวันที่ {game.Day} / {game.TotalDays}", centeredStyle);
        GUI.Label(new Rect(panel.x, panel.y + 146f, panel.width, 24f),
            $"เงินที่เหลือ {ReturnZone.TotalWallet}", centeredStyle);
        GUI.Label(new Rect(panel.x, panel.y + 172f, panel.width, 24f),
            $"ร้องเรียนสะสม {game.Complaints} / {game.MaxComplaints}", centeredStyle);

        float buttonWidth = 200f;
        float gap = 16f;
        float left = panel.x + (panel.width - (buttonWidth * 2f + gap)) * 0.5f;
        float top = panel.y + panel.height - 62f;

        if (GUI.Button(new Rect(left, top, buttonWidth, 38f), "เล่นใหม่"))
        {
            game.ReturnToMainMenu();
        }

        if (GUI.Button(new Rect(left + buttonWidth + gap, top, buttonWidth, 38f), $"ออกจากเกม  [{pauseKey}]"))
        {
            QuitGame();
        }
    }

    // ---------- เมนูหยุดเกม ----------

    private void DrawPauseMenu(GameManager game)
    {
        DrawFullScreenDim();

        Rect panel = CenteredRect(380f, 280f);
        DrawBox(panel, PanelColor);

        GUI.Label(new Rect(panel.x, panel.y + 24f, panel.width, 34f), "หยุดเกมชั่วคราว", headingStyle);
        GUI.Label(new Rect(panel.x, panel.y + 64f, panel.width, 22f),
            $"วันที่ {game.Day} / {game.TotalDays}   เงิน {ReturnZone.TotalWallet}", centeredStyle);

        float buttonWidth = 260f;
        float left = panel.x + (panel.width - buttonWidth) * 0.5f;
        float top = panel.y + 104f;

        if (GUI.Button(new Rect(left, top, buttonWidth, 38f), $"เล่นต่อ  [{pauseKey}]"))
        {
            game.SetPaused(false);
        }

        if (GUI.Button(new Rect(left, top + 48f, buttonWidth, 38f), "กลับเมนูหลัก (เลิกรันนี้)"))
        {
            game.ReturnToMainMenu();
        }

        if (GUI.Button(new Rect(left, top + 96f, buttonWidth, 38f), "ออกจากเกม"))
        {
            QuitGame();
        }
    }

    // ---------- ท้ายวัน ----------

    /// <summary>ท้ายวันมี 2 ขั้น: เลือกรางวัลฟรี (ถ้ามีในคลัง) → ร้านค้าท้ายวัน</summary>
    private void DrawDayEnd(GameManager game)
    {
        if (rewardSystem == null)
        {
            DrawStatusBar("จบวันแล้ว — แต่ไม่มี DayEndRewardSystem ในฉาก");
            return;
        }

        var offer = rewardSystem.CurrentOffer;
        if (offer != null && offer.Count > 0)
        {
            DrawRewards(game, offer);
            return;
        }

        DayEndShop shop = DayEndShop.Instance;
        if (shop != null && shop.IsOpen)
        {
            DrawShop(game, shop);
            return;
        }

        DrawStatusBar("จบวันแล้ว — กำลังรอรางวัล");
    }

    private void DrawRewards(GameManager game, System.Collections.Generic.IReadOnlyList<RewardOption> offer)
    {
        DrawFullScreenDim();
        GUI.Label(new Rect(40, 40, 600, 30), $"จบวันที่ {game.Day} — เลือกรางวัล 1 อย่าง");

        for (int i = 0; i < offer.Count; i++)
        {
            Rect rect = new Rect(40 + i * (cardWidth + 14), 90, cardWidth, cardHeight);
            if (GUI.Button(rect, $"[{i + 1}]\n\n{DescribeReward(offer[i])}"))
            {
                rewardSystem.Choose(i);
                return;
            }
        }
    }

    private void DrawShop(GameManager game, DayEndShop shop)
    {
        DrawFullScreenDim();

        GUI.Label(new Rect(40, 40, 900, 26), $"จบวันที่ {game.Day} — ร้านค้าท้ายวัน: ใช้เงินจากลูกค้าซื้อบัฟ / สถานีเพิ่ม / ลูกค้าชนิดใหม่ (ชิ้นละครั้งต่อวัน)");
        GUI.Label(new Rect(40, 64, 900, 24), $"เงินที่มี: {shop.Money}");

        var offers = shop.CurrentOffers;
        int columns = Mathf.Max(1, (Screen.width - 80) / (cardWidth + 14));
        const float top = 100f;

        for (int i = 0; i < offers.Count; i++)
        {
            DayEndShop.Offer item = offers[i];
            Rect rect = new Rect(
                40 + (i % columns) * (cardWidth + 14),
                top + (i / columns) * (cardHeight + 14),
                cardWidth,
                cardHeight);

            // ปุ่มจางลงเมื่อซื้อไปแล้วหรือเงินไม่พอ แต่ยังอ่านราคาได้
            GUI.enabled = shop.CanBuy(item);
            if (GUI.Button(rect, DescribeOffer(shop, item, i)))
            {
                shop.TryBuy(i);
            }
            GUI.enabled = true;
        }

        int rows = Mathf.CeilToInt(offers.Count / (float)columns);
        Rect continueRect = new Rect(40, top + rows * (cardHeight + 14) + 6, 260, 40);

        if (GUI.Button(continueRect, $"ไปวันถัดไป  [{shop.ContinueKey}]"))
        {
            shop.Close();
        }
    }

    private static string DescribeOffer(DayEndShop shop, DayEndShop.Offer offer, int index)
    {
        string status = offer.sold ? "✓ ซื้อแล้ว"
            : shop.Money >= offer.price ? $"ราคา {offer.price}"
            : $"ราคา {offer.price}  (เงินไม่พอ)";

        return $"[{index + 1}] {shop.GetTitle(offer)}\n\n{shop.GetDetail(offer)}\n\n{status}";
    }

    private string DescribeReward(RewardOption option)
    {
        if (option == null) return "-";
        if (!string.IsNullOrEmpty(option.label)) return option.label;

        return option.kind switch
        {
            RewardKind.Perk => $"{option.perk} +{option.amount:P0}",
            RewardKind.UnlockStation => $"ปลดล็อกสถานี\n{(option.stationPrefab != null ? option.stationPrefab.name : "?")}",
            RewardKind.UnlockSpecies => $"ปลดล็อกสัตว์\n{(option.species != null ? option.species.speciesName : "?")}",
            _ => "-"
        };
    }

    private void DrawStatusBar(string text)
    {
        GUI.Label(new Rect(12, 10, 700, 70), text);
    }

    // ---------- ตัวช่วยวาด ----------

    /// <summary>สร้าง style ครั้งแรกที่ OnGUI ทำงาน — GUI.skin ยังไม่พร้อมตอน Awake</summary>
    private void EnsureStyles()
    {
        if (titleStyle != null) return;

        titleStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 34,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleCenter
        };

        headingStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 20,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleCenter
        };

        centeredStyle = new GUIStyle(GUI.skin.label)
        {
            alignment = TextAnchor.MiddleCenter,
            wordWrap = true
        };
    }

    private Rect CenteredRect(float width, float height)
    {
        return new Rect((Screen.width - width) * 0.5f, (Screen.height - height) * 0.5f, width, height);
    }

    private void DrawFullScreenDim()
    {
        DrawBox(new Rect(0, 0, Screen.width, Screen.height), ScreenDim);
    }

    private void DrawBox(Rect rect, Color color)
    {
        if (pixel == null) return;

        Color previous = GUI.color;
        GUI.color = color;
        GUI.DrawTexture(rect, pixel);
        GUI.color = previous;
    }
}
