using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// หน้าจอเมนูในฉากร้าน สร้างเป็น Canvas ด้วยโค้ดตอน Awake (ชิ้นส่วนมาจาก UiKit ชุดเดียวกับ HUD)
/// หน้าแรก/เลือกสัตว์อยู่อีกฉาก (MainMenu.unity ดู MainMenu.cs)
///
///   ช่วงจัดร้าน    — กด Enter เปิดร้าน (ไม่มีหน้าจอ มีแค่ป้ายบอกใน HUD)
///   ท้ายวัน       — เลือกรางวัล 1 ใบ แล้วต่อด้วยร้านค้าท้ายวัน
///   จบเกม        — สรุปผล + ปุ่มเล่นใหม่ / ออกเกม
///   ระหว่างเล่น    — Esc เปิดเมนูหยุดเกม (เล่นต่อ / กลับเมนูหลัก / ออกเกม)
///
/// เมนูหยุดเกมตั้ง Time.timeScale = 0 ซึ่งหยุดแค่การเคลื่อนที่ ส่วน Update กับ Input ยังทำงานอยู่
/// ระบบที่รับปุ่ม (เดิน/โต้ตอบ/ยกสถานี/ซื้อของ) จึงเช็ค GameManager.IsPaused เองอีกชั้น
///
/// ปุ่มบนจอใช้ EventSystem ของฉาก (InputSystemUIInputModule) ส่วนปุ่มลัด 1..9 / Enter / Esc
/// ยังอ่านผ่าน GameInput เหมือนเดิม ทั้งสองทางทำงานคู่กันได้
/// </summary>
public class GameFlowDebugControls : MonoBehaviour
{
    [Header("Keys")]
    [SerializeField] private KeyCode openShopKey = KeyCode.Return;

    [Tooltip("ปุ่มเปิดเมนูหยุดเกมระหว่างเล่น และออกเกมเมื่ออยู่หน้าจบเกม")]
    [SerializeField] private KeyCode pauseKey = KeyCode.Escape;

    [Header("Layout")]
    [SerializeField] private int cardWidth = 260;
    [SerializeField] private int cardHeight = 190;

    [SerializeField] private Vector2 referenceResolution = new Vector2(1600f, 900f);

    [Tooltip("จำนวนช่องสินค้าสูงสุดที่เตรียมไว้ในหน้าร้านค้า")]
    [SerializeField] private int maxShopSlots = 8;

    private DayEndRewardSystem rewardSystem;

    // เฟรมที่เพิ่งเข้าช่วงจัดร้าน — Enter ที่ใช้ปิดร้านค้าท้ายวันยังนับเป็น GetKeyDown อยู่ในเฟรมนั้น
    // ถ้าไม่ข้าม ร้านจะเปิดรับลูกค้าทันทีโดยผู้เล่นยังไม่ได้จัดร้าน
    private int setupEnteredFrame = -1;

    private Canvas canvas;
    private GameObject dim;

    // หน้าเลือกรางวัล
    private GameObject rewardRoot;
    private Text rewardTitle;
    private readonly List<Button> rewardButtons = new List<Button>();
    private readonly List<Text> rewardLabels = new List<Text>();

    // หน้าร้านค้าท้ายวัน
    private GameObject shopRoot;
    private Text shopTitle;
    private Text shopMoney;
    private readonly List<Button> shopButtons = new List<Button>();
    private readonly List<Text> shopLabels = new List<Text>();
    private Text shopContinueLabel;

    // หน้าหยุดเกม / จบเกม
    private GameObject pauseRoot;
    private Text pauseInfo;
    private GameObject runOverRoot;
    private Text runOverTitle;
    private Text runOverSummary;
    private Text runOverStats;

    // ข้อความสำรองตอนท้ายวันที่ยังไม่มีอะไรให้เลือก
    private GameObject waitingRoot;
    private Text waitingText;

    private void Awake()
    {
        rewardSystem = FindAnyObjectByType<DayEndRewardSystem>();
        BuildCanvas();
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
    }

    private void HandlePhaseChanged(GamePhase phase)
    {
        if (phase == GamePhase.Setup)
        {
            setupEnteredFrame = Time.frameCount;
        }
    }

    // ---------- ปุ่มลัด ----------

    private void Update()
    {
        GameManager game = GameManager.Instance;
        if (game == null) return;

        if (GameInput.Pressed(pauseKey))
        {
            HandlePauseKey(game);
            return;
        }

        // เมนูหยุดเกมเปิดอยู่ = ไม่รับปุ่มอื่นเลย
        if (game.IsPaused) return;

        if (game.Phase == GamePhase.Setup && Time.frameCount != setupEnteredFrame && GameInput.Pressed(openShopKey))
        {
            game.BeginServicePhase();
        }
    }

    /// <summary>
    /// Esc ทำหน้าที่ต่างกันตามหน้าจอ:
    /// จบเกม = ออกเกมเลย (ไม่มีอะไรให้เสีย) ระหว่างเล่น = สลับเปิด/ปิดเมนูหยุดเกม
    /// </summary>
    private void HandlePauseKey(GameManager game)
    {
        if (game.Phase == GamePhase.RunOver)
        {
            QuitGame();
            return;
        }

        game.SetPaused(!game.IsPaused);
    }

    /// <summary>
    /// ปิดเกม — ในโหมด Play ของ Editor ปิดโปรแกรมไม่ได้ จึงสั่งหยุดเล่นแทนเพื่อให้เทสปุ่มนี้ได้จริง
    /// </summary>
    internal static void QuitGame()
    {
        Debug.Log("[GameFlow] ออกจากเกม");

#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    // ---------- สร้างหน้าจอ ----------

    private void BuildCanvas()
    {
        // sortingOrder สูงกว่า HUD (10) เมนูจึงอยู่หน้าเสมอ และเปิด raycast ไว้เพราะมีปุ่มให้กด
        canvas = UiKit.MakeCanvas("Menu Canvas", transform, 50, true, referenceResolution);

        Image dimImage = UiKit.MakeImage("Dim", canvas.transform, UiKit.Dim);
        dimImage.raycastTarget = true; // กันคลิกทะลุไปโดนของในฉากข้างหลัง
        UiKit.Stretch(dimImage.rectTransform);
        dim = dimImage.gameObject;

        BuildRewardScreen();
        BuildShopScreen();
        BuildPauseMenu();
        BuildRunOverScreen();
        BuildWaitingScreen();
    }

    private void BuildRewardScreen()
    {
        RectTransform panel = MakePanel("Rewards", 3 * cardWidth + 32f + 120f, cardHeight + 200f, out rewardRoot);

        rewardTitle = UiKit.MakeText("Title", panel, 34, UiKit.Ink, TextAnchor.UpperCenter);
        rewardTitle.fontStyle = FontStyle.Bold;
        Fill(rewardTitle.rectTransform, 36f, 40f);

        Text subtitle = UiKit.MakeText("Subtitle", panel, 19, UiKit.InkSoft, TextAnchor.UpperCenter);
        subtitle.text = "เลือกได้ 1 ใบ — ใบที่แรงกว่ามักมีราคาที่ต้องจ่าย";
        Fill(subtitle.rectTransform, 84f, 24f);

        float left = (panel.sizeDelta.x - (3 * cardWidth + 32f)) * 0.5f;

        for (int i = 0; i < 3; i++)
        {
            int index = i;
            Button button = UiKit.MakeButton($"Reward {i}", panel, 20, out Text label);
            UiKit.Place(button.GetComponent<RectTransform>(),
                new Vector2(left + i * (cardWidth + 16f), -124f), new Vector2(cardWidth, cardHeight));

            button.onClick.AddListener(() => rewardSystem?.Choose(index));

            rewardButtons.Add(button);
            rewardLabels.Add(label);
        }
    }

    private void BuildShopScreen()
    {
        const int columns = 4;
        int rows = Mathf.CeilToInt(maxShopSlots / (float)columns);

        float width = columns * cardWidth + (columns - 1) * 16f + 120f;
        float height = rows * (cardHeight + 16f) + 230f;

        RectTransform panel = MakePanel("Shop", width, height, out shopRoot);

        shopTitle = UiKit.MakeText("Title", panel, 34, UiKit.Ink, TextAnchor.UpperCenter);
        shopTitle.fontStyle = FontStyle.Bold;
        Fill(shopTitle.rectTransform, 34f, 40f);

        Text subtitle = UiKit.MakeText("Subtitle", panel, 19, UiKit.InkSoft, TextAnchor.UpperCenter);
        subtitle.text = "ใช้เงินจากลูกค้าซื้อบัฟ / สถานีเพิ่ม / สัตว์ชนิดใหม่ — ชิ้นละครั้งต่อวัน";
        Fill(subtitle.rectTransform, 80f, 24f);

        // ป้ายเงินมุมขวาบนของแผง
        RectTransform moneyCard = UiKit.MakeCard("Money", panel, UiKit.Card);
        UiKit.Place(moneyCard, new Vector2(width - 190f, -30f), new Vector2(150f, 48f));

        Image coin = UiKit.MakeImage("Coin", moneyCard, UiKit.Money);
        coin.sprite = UiKit.Circle;
        UiKit.Place(coin.rectTransform, new Vector2(16f, -16f), new Vector2(18f, 18f));

        shopMoney = UiKit.MakeText("Amount", moneyCard, 26, UiKit.Ink, TextAnchor.MiddleLeft);
        shopMoney.fontStyle = FontStyle.Bold;
        UiKit.Place(shopMoney.rectTransform, new Vector2(42f, 0f), new Vector2(100f, 48f));

        float left = (width - (columns * cardWidth + (columns - 1) * 16f)) * 0.5f;

        for (int i = 0; i < maxShopSlots; i++)
        {
            int index = i;
            Button button = UiKit.MakeButton($"Offer {i}", panel, 18, out Text label);
            UiKit.Place(button.GetComponent<RectTransform>(),
                new Vector2(left + (i % columns) * (cardWidth + 16f), -(118f + (i / columns) * (cardHeight + 16f))),
                new Vector2(cardWidth, cardHeight));

            button.onClick.AddListener(() => DayEndShop.Instance?.TryBuy(index));

            shopButtons.Add(button);
            shopLabels.Add(label);
        }

        Button continueButton = UiKit.MakeButton("Continue", panel, 20, out shopContinueLabel);
        UiKit.Place(continueButton.GetComponent<RectTransform>(),
            new Vector2((width - 260f) * 0.5f, -(height - 74f)), new Vector2(260f, 50f));
        continueButton.onClick.AddListener(() => DayEndShop.Instance?.Close());
    }

    private void BuildPauseMenu()
    {
        RectTransform panel = MakePanel("Pause", 420f, 330f, out pauseRoot);

        Text title = UiKit.MakeText("Title", panel, 28, UiKit.Ink, TextAnchor.UpperCenter);
        title.fontStyle = FontStyle.Bold;
        title.text = "หยุดเกมชั่วคราว";
        Fill(title.rectTransform, 32f, 36f);

        pauseInfo = UiKit.MakeText("Info", panel, 19, UiKit.InkSoft, TextAnchor.UpperCenter);
        Fill(pauseInfo.rectTransform, 76f, 24f);

        AddStackedButton(panel, 0, $"เล่นต่อ  ({pauseKey})", () => GameManager.Instance?.SetPaused(false));
        AddStackedButton(panel, 1, "กลับเมนูหลัก (เลิกรันนี้)", () => GameManager.Instance?.ReturnToMainMenu());
        AddStackedButton(panel, 2, "ออกจากเกม", QuitGame);
    }

    private void BuildRunOverScreen()
    {
        RectTransform panel = MakePanel("Run Over", 620f, 470f, out runOverRoot);

        runOverTitle = UiKit.MakeText("Title", panel, 42, UiKit.Ink, TextAnchor.UpperCenter);
        runOverTitle.fontStyle = FontStyle.Bold;
        Fill(runOverTitle.rectTransform, 40f, 52f);

        runOverSummary = UiKit.MakeText("Summary", panel, 21, UiKit.InkSoft, TextAnchor.UpperCenter);
        Fill(runOverSummary.rectTransform, 100f, 26f);

        runOverStats = UiKit.MakeText("Stats", panel, 20, UiKit.Ink, TextAnchor.UpperCenter);
        runOverStats.horizontalOverflow = HorizontalWrapMode.Wrap;
        Fill(runOverStats.rectTransform, 146f, 190f);

        Button again = UiKit.MakeButton("Again", panel, 20, out Text againLabel);
        againLabel.text = "เล่นใหม่";
        UiKit.Place(again.GetComponent<RectTransform>(), new Vector2(100f, -370f), new Vector2(190f, 50f));
        again.onClick.AddListener(() => GameManager.Instance?.ReturnToMainMenu());

        Button quit = UiKit.MakeButton("Quit", panel, 20, out Text quitLabel);
        quitLabel.text = $"ออกจากเกม  ({pauseKey})";
        UiKit.Place(quit.GetComponent<RectTransform>(), new Vector2(330f, -370f), new Vector2(190f, 50f));
        quit.onClick.AddListener(QuitGame);
    }

    private void BuildWaitingScreen()
    {
        RectTransform panel = MakePanel("Waiting", 520f, 120f, out waitingRoot);

        waitingText = UiKit.MakeText("Text", panel, 21, UiKit.Ink, TextAnchor.MiddleCenter);
        waitingText.horizontalOverflow = HorizontalWrapMode.Wrap;
        UiKit.Stretch(waitingText.rectTransform, 24f);
    }

    /// <summary>แผงกลางจอพร้อมเงา — โครงร่วมของทุกหน้าจอ</summary>
    private RectTransform MakePanel(string name, float width, float height, out GameObject root)
    {
        RectTransform panel = UiKit.MakeCard(name, canvas.transform, UiKit.Card);
        UiKit.Center(panel, new Vector2(width, height));

        root = panel.gameObject;
        root.SetActive(false);
        return panel;
    }

    private void AddStackedButton(RectTransform panel, int index, string text, UnityEngine.Events.UnityAction action)
    {
        Button button = UiKit.MakeButton($"Button {index}", panel, 20, out Text label);
        label.text = text;

        UiKit.Place(button.GetComponent<RectTransform>(),
            new Vector2((panel.sizeDelta.x - 280f) * 0.5f, -(120f + index * 62f)), new Vector2(280f, 50f));

        button.onClick.AddListener(action);
    }

    // ---------- อัปเดตเนื้อหา ----------

    private void LateUpdate()
    {
        GameManager game = GameManager.Instance;
        if (game == null) return;

        bool paused = game.IsPaused;
        bool runOver = !paused && game.Phase == GamePhase.RunOver;
        bool dayEnd = !paused && game.Phase == GamePhase.DayEnd;

        var offer = rewardSystem != null ? rewardSystem.CurrentOffer : null;
        bool rewards = dayEnd && offer != null && offer.Count > 0;

        DayEndShop shop = DayEndShop.Instance;
        bool shopOpen = dayEnd && !rewards && shop != null && shop.IsOpen;
        bool waiting = dayEnd && !rewards && !shopOpen;

        Show(rewardRoot, rewards);
        Show(shopRoot, shopOpen);
        Show(pauseRoot, paused);
        Show(runOverRoot, runOver);
        Show(waitingRoot, waiting);
        Show(dim, rewards || shopOpen || paused || runOver || waiting);

        if (rewards) RefreshRewards(game, offer);
        if (shopOpen) RefreshShop(game, shop);
        if (paused) pauseInfo.text = $"วันที่ {game.Day} / {game.TotalDays}   ·   เงิน {ReturnZone.TotalWallet}   ·   ชื่อเสียง {game.Reputation}";
        if (runOver) RefreshRunOver(game);
        if (waiting) waitingText.text = rewardSystem == null
            ? "จบวันแล้ว — แต่ไม่มี DayEndRewardSystem ในฉาก"
            : "จบวันแล้ว — กำลังสรุปผล";
    }

    private void RefreshRewards(GameManager game, IReadOnlyList<RewardOption> offer)
    {
        rewardTitle.text = $"จบวันที่ {game.Day} — เลือกรางวัล";

        for (int i = 0; i < rewardButtons.Count; i++)
        {
            bool has = i < offer.Count;
            if (rewardButtons[i].gameObject.activeSelf != has) rewardButtons[i].gameObject.SetActive(has);
            if (!has) continue;

            rewardLabels[i].text = $"{DescribeReward(offer[i])}\n\n({i + 1})";
        }
    }

    private void RefreshShop(GameManager game, DayEndShop shop)
    {
        shopTitle.text = game.LastDayCost > 0
            ? $"จบวันที่ {game.Day} — ร้านค้าท้ายวัน (หักค่าเช่าแล้ว {game.LastDayCost})"
            : $"จบวันที่ {game.Day} — ร้านค้าท้ายวัน";
        shopMoney.text = shop.Money.ToString();
        shopContinueLabel.text = $"ไปวันถัดไป  ({shop.ContinueKey})";

        var offers = shop.CurrentOffers;

        for (int i = 0; i < shopButtons.Count; i++)
        {
            bool has = i < offers.Count;
            if (shopButtons[i].gameObject.activeSelf != has) shopButtons[i].gameObject.SetActive(has);
            if (!has) continue;

            DayEndShop.Offer item = offers[i];

            string status = item.sold ? "ซื้อแล้ว"
                : shop.LockedReason(item) != null ? $"ราคา {item.price} — ชื่อเสียงไม่พอ"
                : shop.Money >= item.price ? $"ราคา {item.price}"
                : $"ราคา {item.price} — เงินไม่พอ";

            shopLabels[i].text = $"{shop.GetTitle(item)}\n\n{shop.GetDetail(item)}\n\n{status}   ({i + 1})";
            shopLabels[i].color = shop.CanBuy(item) ? UiKit.Ink : UiKit.InkSoft;

            // ปุ่มกดไม่ได้จะจางลงเอง (ColorBlock.disabledColor) แต่ยังอ่านราคาได้
            shopButtons[i].interactable = shop.CanBuy(item);
        }
    }

    private void RefreshRunOver(GameManager game)
    {
        bool won = game.LastRunWon;

        runOverTitle.text = won ? "ร้านอยู่รอด!" : "ร้านเจ๊ง";
        runOverTitle.color = won ? UiKit.Good : UiKit.Danger;

        runOverSummary.text = won
            ? $"อยู่ครบ {game.TotalDays} วันตามเป้า"
            : $"ร้องเรียนครบ {game.MaxComplaints} ครั้งในวันที่ {game.Day}";

        string goods = RunStats.GoodsDelivered > 0
            ? $"   ·   สินค้า {RunStats.GoodsDelivered} ชิ้น (+{RunStats.GoodsEarned})"
            : string.Empty;

        string escapes = RunStats.Escapes > 0
            ? $"สัตว์หนี {RunStats.Escapes} ครั้ง · จับคืนได้ {RunStats.Catches}\n"
            : string.Empty;

        // บรรทัดนี้สำคัญที่สุดของหน้าสรุป: บอกว่าแพ้เพราะอะไร คนเทสจะได้รู้ว่าต้องแก้อะไรรอบหน้า
        string cause = !won && !string.IsNullOrEmpty(RunStats.LastComplaint)
            ? $"\nฟางเส้นสุดท้าย: {RunStats.LastComplaint}"
            : string.Empty;

        runOverStats.text =
            $"ไปถึงวันที่ {game.Day} / {game.TotalDays}\n" +
            $"ส่งคืนสำเร็จ {RunStats.OrdersReturned} ตัว (ครบทุกขั้นตอน {RunStats.OrdersComplete}){goods}\n" +
            $"รายได้รวม {RunStats.TotalEarned}  (ค่าบริการ {RunStats.FeesEarned} · ทิป {RunStats.TipsEarned} · คอมโบ {RunStats.Combos} ครั้ง)  ·  ค่าเช่ารวม −{RunStats.CostsPaid}\n" +
            $"เงินที่เหลือ {ReturnZone.TotalWallet}   ·   ชื่อเสียง {game.Reputation}\n" +
            escapes +
            $"ร้องเรียน {game.Complaints} / {game.MaxComplaints}" +
            cause;
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

    private static void Show(GameObject target, bool visible)
    {
        if (target != null && target.activeSelf != visible) target.SetActive(visible);
    }

    /// <summary>ข้อความกว้างเต็มแผง วางห่างจากขอบบนตามที่กำหนด</summary>
    internal static void Fill(RectTransform rect, float topOffset, float height)
    {
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(1f, 1f);
        rect.pivot = new Vector2(0.5f, 1f);
        rect.offsetMin = new Vector2(24f, -(topOffset + height));
        rect.offsetMax = new Vector2(-24f, -topOffset);
    }
}
