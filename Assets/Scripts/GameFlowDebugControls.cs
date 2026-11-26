using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// หน้าจอเมนูในฉากร้าน สร้างเป็น Canvas ด้วยโค้ดตอน Awake (ชิ้นส่วนมาจาก UiKit ชุดเดียวกับ HUD)
/// หน้าแรก/เลือกสัตว์อยู่อีกฉาก (MainMenu.unity ดู MainMenu.cs)
///
///   ช่วงจัดร้าน    — กด Enter เปิดร้าน (ไม่มีหน้าจอ มีแค่ป้ายบอกใน HUD)
///   ท้ายวัน       — เลือกรางวัล 1 ใบ แล้วต่อด้วยร้านค้าท้ายวัน
///   จบเกม        — สรุปผล + ปุ่มเล่นใหม่ / ออกเกม
///   ระหว่างเล่น    — Esc เปิดเมนูหยุดเกม (เล่นต่อ / ตั้งค่าเสียง / กลับเมนูหลัก / ออกเกม)
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
    [SerializeField] private int cardWidth = 270;
    [SerializeField] private int cardHeight = 220;

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
    private TextMeshProUGUI rewardTitle;
    private readonly List<UiKit.ChoiceCard> rewardCards = new List<UiKit.ChoiceCard>();

    // หน้าร้านค้าท้ายวัน
    private GameObject shopRoot;
    private TextMeshProUGUI shopTitle;
    private TextMeshProUGUI shopMoney;
    private readonly List<UiKit.ChoiceCard> shopCards = new List<UiKit.ChoiceCard>();
    private TextMeshProUGUI shopContinueLabel;

    // หน้าหยุดเกม / จบเกม
    private GameObject pauseRoot;
    private TextMeshProUGUI pauseInfo;
    private UiKit.VolumeSliderRow pauseMusicSlider;
    private UiKit.VolumeSliderRow pauseSfxSlider;
    private bool lastPausedState = false;

    private GameObject runOverRoot;
    private TextMeshProUGUI runOverTitle;
    private TextMeshProUGUI runOverSummary;
    private TextMeshProUGUI runOverStats;
    private TextMeshProUGUI runOverCause;

    // ข้อความสำรองตอนท้ายวันที่ยังไม่มีอะไรให้เลือก
    private GameObject waitingRoot;
    private TextMeshProUGUI waitingText;

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
        float panelWidth = 3 * cardWidth + 2 * 18f + 100f;
        float panelHeight = cardHeight + 210f;
        RectTransform panel = MakePanel("Rewards", panelWidth, panelHeight, out rewardRoot);

        rewardTitle = UiKit.MakeText("Title", panel, 32f, UiKit.Ink, TextAlignmentOptions.Top);
        rewardTitle.fontStyle = FontStyles.Bold;
        Fill(rewardTitle.rectTransform, 32f, 40f);

        TextMeshProUGUI subtitle = UiKit.MakeText("Subtitle", panel, 19f, UiKit.InkSoft, TextAlignmentOptions.Top);
        subtitle.text = "เลือกได้ 1 ใบ — ใบที่แรงกว่ามักมีราคาที่ต้องจ่าย";
        Fill(subtitle.rectTransform, 76f, 24f);

        float left = (panelWidth - (3 * cardWidth + 2 * 18f)) * 0.5f;

        for (int i = 0; i < 3; i++)
        {
            int index = i;
            UiKit.ChoiceCard card = UiKit.MakeChoiceCard($"Reward {i}", panel, new Vector2(cardWidth, cardHeight));
            UiKit.Place(card.holder,
                new Vector2(left + i * (cardWidth + 18f), -116f), new Vector2(cardWidth, cardHeight));

            card.button.onClick.AddListener(() => rewardSystem?.Choose(index));
            rewardCards.Add(card);
        }
    }

    private void BuildShopScreen()
    {
        const int columns = 4;
        int rows = Mathf.CeilToInt(maxShopSlots / (float)columns);

        float width = columns * cardWidth + (columns - 1) * 16f + 90f;
        float height = rows * (cardHeight + 14f) + 220f;

        RectTransform panel = MakePanel("Shop", width, height, out shopRoot);

        shopTitle = UiKit.MakeText("Title", panel, 30f, UiKit.Ink, TextAlignmentOptions.Top);
        shopTitle.fontStyle = FontStyles.Bold;
        Fill(shopTitle.rectTransform, 30f, 38f);

        TextMeshProUGUI subtitle = UiKit.MakeText("Subtitle", panel, 18f, UiKit.InkSoft, TextAlignmentOptions.Top);
        subtitle.text = "ใช้เงินจากลูกค้าซื้อบัฟ / สถานีเพิ่ม / สัตว์ชนิดใหม่ — ชิ้นละครั้งต่อวัน";
        Fill(subtitle.rectTransform, 70f, 24f);

        // ป้ายเงินมุมขวาบนของแผง
        RectTransform moneyCard = UiKit.MakeCard("Money", panel, UiKit.Card);
        UiKit.Place(moneyCard, new Vector2(width - 180f, -24f), new Vector2(150f, 44f));

        Image coin = UiKit.MakeImage("Coin", moneyCard, UiKit.Money);
        coin.sprite = UiKit.Circle;
        UiKit.Place(coin.rectTransform, new Vector2(14f, -13f), new Vector2(18f, 18f));

        shopMoney = UiKit.MakeText("Amount", moneyCard, 24f, UiKit.Ink, TextAlignmentOptions.Left);
        shopMoney.fontStyle = FontStyles.Bold;
        UiKit.Place(shopMoney.rectTransform, new Vector2(40f, 0f), new Vector2(100f, 44f));

        float left = (width - (columns * cardWidth + (columns - 1) * 16f)) * 0.5f;

        for (int i = 0; i < maxShopSlots; i++)
        {
            int index = i;
            UiKit.ChoiceCard card = UiKit.MakeChoiceCard($"Offer {i}", panel, new Vector2(cardWidth, cardHeight));
            UiKit.Place(card.holder,
                new Vector2(left + (i % columns) * (cardWidth + 16f), -(108f + (i / columns) * (cardHeight + 14f))),
                new Vector2(cardWidth, cardHeight));

            card.button.onClick.AddListener(() => DayEndShop.Instance?.TryBuy(index));
            shopCards.Add(card);
        }

        Button continueButton = UiKit.MakeButton("Continue", panel, 20f, out shopContinueLabel, primary: true);
        UiKit.Place(continueButton.GetComponent<RectTransform>(),
            new Vector2((width - 280f) * 0.5f, -(height - 68f)), new Vector2(280f, 50f));
        continueButton.onClick.AddListener(() => DayEndShop.Instance?.Close());
    }

    private void BuildPauseMenu()
    {
        float panelWidth = 460f;
        float panelHeight = 440f;
        RectTransform panel = MakePanel("Pause", panelWidth, panelHeight, out pauseRoot);

        TextMeshProUGUI title = UiKit.MakeText("Title", panel, 28f, UiKit.Ink, TextAlignmentOptions.Top);
        title.fontStyle = FontStyles.Bold;
        title.text = "หยุดเกมชั่วคราว";
        Fill(title.rectTransform, 26f, 36f);

        pauseInfo = UiKit.MakeText("Info", panel, 19f, UiKit.InkSoft, TextAlignmentOptions.Top);
        Fill(pauseInfo.rectTransform, 66f, 24f);

        // แถบปรับระดับเสียงดนตรีและเสียงเอฟเฟกต์
        float sliderWidth = 390f;
        float sliderLeft = (panelWidth - sliderWidth) * 0.5f;

        pauseMusicSlider = UiKit.MakeSliderRow("MusicSlider", panel, "เสียงเพลง",
            AudioManager.GetMusicVolumeGlobal(), (v) => AudioManager.SetMusicVolumeGlobal(v), sliderWidth);
        UiKit.Place(pauseMusicSlider.holder, new Vector2(sliderLeft, -106f), new Vector2(sliderWidth, 36f));

        pauseSfxSlider = UiKit.MakeSliderRow("SfxSlider", panel, "เสียงเอฟเฟกต์",
            AudioManager.GetSfxVolumeGlobal(), (v) => AudioManager.SetSfxVolumeGlobal(v), sliderWidth);
        UiKit.Place(pauseSfxSlider.holder, new Vector2(sliderLeft, -148f), new Vector2(sliderWidth, 36f));

        // ปุ่มเล่นต่อ, กลับเมนูหลัก, ออกจากเกม
        AddStackedButton(panel, 0, $"เล่นต่อ  ({UiKit.KeyLabel(pauseKey)})", () => GameManager.Instance?.SetPaused(false), primary: true, startY: 202f);
        AddStackedButton(panel, 1, "กลับเมนูหลัก (เลิกรันนี้)", () => GameManager.Instance?.ReturnToMainMenu(), primary: false, startY: 202f);
        AddStackedButton(panel, 2, "ออกจากเกม", QuitGame, primary: false, startY: 202f);
    }

    private void BuildRunOverScreen()
    {
        RectTransform panel = MakePanel("Run Over", 720f, 540f, out runOverRoot);

        runOverTitle = UiKit.MakeText("Title", panel, 38f, UiKit.Ink, TextAlignmentOptions.Top);
        runOverTitle.fontStyle = FontStyles.Bold;
        Fill(runOverTitle.rectTransform, 30f, 46f);

        runOverSummary = UiKit.MakeText("Summary", panel, 20f, UiKit.InkSoft, TextAlignmentOptions.Top);
        Fill(runOverSummary.rectTransform, 82f, 26f);

        runOverStats = UiKit.MakeText("Stats", panel, 19f, UiKit.Ink, TextAlignmentOptions.Top);
        runOverStats.textWrappingMode = TextWrappingModes.Normal;
        runOverStats.lineSpacing = 1.35f;
        Fill(runOverStats.rectTransform, 122f, 240f);

        runOverCause = UiKit.MakeText("Cause", panel, 18f, UiKit.Danger, TextAlignmentOptions.Center);
        runOverCause.textWrappingMode = TextWrappingModes.Normal;
        Fill(runOverCause.rectTransform, 372f, 44f);

        Button again = UiKit.MakeButton("Again", panel, 20f, out TextMeshProUGUI againLabel, primary: true);
        againLabel.text = "เล่นใหม่";
        UiKit.Place(again.GetComponent<RectTransform>(), new Vector2((720f - 520f) * 0.5f, -440f), new Vector2(240f, 56f));
        again.onClick.AddListener(() => GameManager.Instance?.ReturnToMainMenu());

        Button quit = UiKit.MakeButton("Quit", panel, 20f, out TextMeshProUGUI quitLabel);
        quitLabel.text = $"ออกจากเกม  ({UiKit.KeyLabel(pauseKey)})";
        UiKit.Place(quit.GetComponent<RectTransform>(), new Vector2((720f - 520f) * 0.5f + 280f, -440f), new Vector2(240f, 56f));
        quit.onClick.AddListener(QuitGame);
    }

    private void BuildWaitingScreen()
    {
        RectTransform panel = MakePanel("Waiting", 520f, 120f, out waitingRoot);

        waitingText = UiKit.MakeText("Text", panel, 21f, UiKit.Ink, TextAlignmentOptions.Center);
        waitingText.textWrappingMode = TextWrappingModes.Normal;
        UiKit.Stretch(waitingText.rectTransform, 24f);
    }

    /// <summary>แผงกลางจอพร้อมเงา — โครงร่วมของทุกหน้าจอ</summary>
    private RectTransform MakePanel(string name, float width, float height, out GameObject root)
    {
        RectTransform panel = UiKit.MakeCard(name, canvas.transform, UiKit.Panel);
        UiKit.Center(panel, new Vector2(width, height));

        root = panel.gameObject;
        root.SetActive(false);
        return panel;
    }

    private void AddStackedButton(RectTransform panel, int index, string text, UnityEngine.Events.UnityAction action, bool primary = false, float startY = 116f)
    {
        Button button = UiKit.MakeButton($"Button {index}", panel, 19f, out TextMeshProUGUI label, primary: primary);
        label.text = text;

        UiKit.Place(button.GetComponent<RectTransform>(),
            new Vector2((panel.sizeDelta.x - 300f) * 0.5f, -(startY + index * 56f)), new Vector2(300f, 48f));

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

        // ซิงค์ตำแหน่งแถบเลื่อนเสียงตอนเปิด Pause Menu
        if (paused && !lastPausedState)
        {
            SyncPauseSliders();
        }
        lastPausedState = paused;

        if (rewards) RefreshRewards(game, offer);
        if (shopOpen) RefreshShop(game, shop);
        if (paused) pauseInfo.text = $"วันที่ {game.Day} / {game.TotalDays}   ·   เงิน {ReturnZone.TotalWallet}   ·   ชื่อเสียง {game.Reputation}";
        if (runOver) RefreshRunOver(game);
        if (waiting) waitingText.text = rewardSystem == null
            ? "จบวันแล้ว — แต่ไม่มี DayEndRewardSystem ในฉาก"
            : "จบวันแล้ว — กำลังสรุปผล";
    }

    private void SyncPauseSliders()
    {
        if (pauseMusicSlider != null)
        {
            float m = AudioManager.GetMusicVolumeGlobal();
            pauseMusicSlider.slider.SetValueWithoutNotify(m);
            pauseMusicSlider.valueText.text = $"{Mathf.RoundToInt(m * 100f)}%";
        }

        if (pauseSfxSlider != null)
        {
            float s = AudioManager.GetSfxVolumeGlobal();
            pauseSfxSlider.slider.SetValueWithoutNotify(s);
            pauseSfxSlider.valueText.text = $"{Mathf.RoundToInt(s * 100f)}%";
        }
    }

    private void RefreshRewards(GameManager game, IReadOnlyList<RewardOption> offer)
    {
        rewardTitle.text = $"จบวันที่ {game.Day} — เลือกรางวัล";

        for (int i = 0; i < rewardCards.Count; i++)
        {
            bool has = i < offer.Count;
            if (rewardCards[i].holder.gameObject.activeSelf != has) rewardCards[i].holder.gameObject.SetActive(has);
            if (!has) continue;

            RewardOption opt = offer[i];
            SplitRewardText(opt, out string title, out string detail);

            rewardCards[i].header.text = title;
            rewardCards[i].body.text = detail;
            rewardCards[i].footer.text = $"({i + 1})";
        }
    }

    private void SplitRewardText(RewardOption option, out string title, out string detail)
    {
        if (option == null)
        {
            title = "-";
            detail = string.Empty;
            return;
        }

        string full = DescribeReward(option);
        int newline = full.IndexOf('\n');
        if (newline >= 0)
        {
            title = full.Substring(0, newline).Trim();
            detail = full.Substring(newline + 1).Trim();
        }
        else
        {
            title = full;
            detail = string.Empty;
        }
    }

    private void RefreshShop(GameManager game, DayEndShop shop)
    {
        shopTitle.text = game.LastDayCost > 0
            ? $"จบวันที่ {game.Day} — ร้านค้าท้ายวัน (หักค่าเช่าแล้ว {game.LastDayCost})"
            : $"จบวันที่ {game.Day} — ร้านค้าท้ายวัน";
        shopMoney.text = shop.Money.ToString();
        shopContinueLabel.text = $"ไปวันถัดไป  ({UiKit.KeyLabel(shop.ContinueKey)})";

        var offers = shop.CurrentOffers;

        for (int i = 0; i < shopCards.Count; i++)
        {
            bool has = i < offers.Count;
            if (shopCards[i].holder.gameObject.activeSelf != has) shopCards[i].holder.gameObject.SetActive(has);
            if (!has) continue;

            DayEndShop.Offer item = offers[i];

            string status = item.sold ? "ซื้อแล้ว"
                : shop.LockedReason(item) != null ? $"ราคา {item.price} — ชื่อเสียงไม่พอ"
                : shop.Money >= item.price ? $"ราคา {item.price}"
                : $"ราคา {item.price} — เงินไม่พอ";

            shopCards[i].header.text = shop.GetTitle(item);
            shopCards[i].body.text = shop.GetDetail(item);
            shopCards[i].footer.text = $"{status}   ({i + 1})";

            bool canBuy = shop.CanBuy(item);
            shopCards[i].body.color = canBuy ? UiKit.Ink : UiKit.InkSoft;
            shopCards[i].button.interactable = canBuy;
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

        runOverStats.text =
            $"ไปถึงวันที่ {game.Day} / {game.TotalDays}\n" +
            $"ส่งคืนสำเร็จ {RunStats.OrdersReturned} ตัว (ครบทุกขั้นตอน {RunStats.OrdersComplete}){goods}\n" +
            $"รายได้รวม {RunStats.TotalEarned}  (ค่าบริการ {RunStats.FeesEarned} · ทิป {RunStats.TipsEarned} · คอมโบ {RunStats.Combos} ครั้ง)\n" +
            $"ค่าเช่ารวม −{RunStats.CostsPaid}   ·   เงินที่เหลือ {ReturnZone.TotalWallet}   ·   ชื่อเสียง {game.Reputation}\n" +
            escapes +
            $"ร้องเรียน {game.Complaints} / {game.MaxComplaints}";

        bool hasCause = !won && !string.IsNullOrEmpty(RunStats.LastComplaint);
        runOverCause.gameObject.SetActive(hasCause);
        if (hasCause)
        {
            runOverCause.text = $"ฟางเส้นสุดท้าย: {RunStats.LastComplaint}";
        }
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
