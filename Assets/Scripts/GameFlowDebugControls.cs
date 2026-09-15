using UnityEngine;

/// <summary>
/// เมนูเริ่มเกม + HUD ชั่วคราว วาดด้วย OnGUI (ยังไม่ใช้ Canvas)
/// ใช้ได้จริงตั้งแต่ตอนนี้ และแทนที่ด้วย UI จริงทีหลังได้โดยไม่ต้องแก้ระบบข้างล่าง
///
///   หน้าเมนู      — คลิกการ์ดสัตว์ (หรือกด 1..9) เพื่อเริ่ม run
///   ช่วงจัดร้าน    — กด Enter เปิดร้าน
///   ท้ายวัน       — คลิกการ์ดรางวัล (หรือกด 1..9)
///   ร้านค้าท้ายวัน — คลิกการ์ดซื้อบัฟ (หรือกด 1..9), ปุ่ม "ไปวันถัดไป" (หรือ Enter)
/// </summary>
public class GameFlowDebugControls : MonoBehaviour
{
    [Header("Main Menu")]
    [Tooltip("สัตว์ที่ให้เลือกเป็นตัวเริ่มต้น — สถานีเริ่มต้นจะมาจาก need ของตัวที่เลือก")]
    [SerializeField] private PetSpecies[] selectableStarters;

    [Header("Keys")]
    [SerializeField] private KeyCode openShopKey = KeyCode.Return;

    [Header("Layout")]
    [SerializeField] private int cardWidth = 240;
    [SerializeField] private int cardHeight = 132;

    /// <summary>สัตว์ทั้งหมดที่ให้เลือกในหน้าเมนู — ร้านค้าท้ายวันใช้เป็นรายการสัตว์ที่ขายได้ ถ้าไม่ได้ตั้งรายการของตัวเอง</summary>
    public PetSpecies[] SelectableStarters => selectableStarters;

    private DayEndRewardSystem rewardSystem;

    // เฟรมที่เพิ่งเข้าช่วงจัดร้าน — Enter ที่ใช้ปิดร้านค้าท้ายวันยังนับเป็น GetKeyDown อยู่ในเฟรมนั้น
    // ถ้าไม่ข้าม ร้านจะเปิดรับลูกค้าทันทีโดยผู้เล่นยังไม่ได้จัดร้าน
    private int setupEnteredFrame = -1;

    private void Awake()
    {
        rewardSystem = FindAnyObjectByType<DayEndRewardSystem>();
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

    private void Update()
    {
        GameManager game = GameManager.Instance;
        if (game == null) return;

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

    private void OnGUI()
    {
        GameManager game = GameManager.Instance;
        if (game == null) return;

        // แถบสถานะ/เกจ/บันทึกเหตุการณ์ ย้ายไปอยู่ที่ GameHudOverlay แล้ว
        // ที่นี่เหลือแค่หน้าจอที่ต้องกดเลือก คือเมนูเริ่มเกม หน้าเลือกรางวัล และร้านค้าท้ายวัน
        switch (game.Phase)
        {
            case GamePhase.MainMenu: DrawMainMenu(game); break;
            case GamePhase.DayEnd: DrawDayEnd(game); break;
            case GamePhase.RunOver: DrawStatusBar("จบ run แล้ว — ดูผลใน Console"); break;
        }
    }

    private void DrawMainMenu(GameManager game)
    {
        GUI.Box(new Rect(0, 0, Screen.width, Screen.height), GUIContent.none);

        GUI.Label(new Rect(40, 40, 600, 30), "PET CORNER — เลือกสัตว์เริ่มต้น");
        GUI.Label(new Rect(40, 66, 700, 24), "สถานีเริ่มต้นในร้านจะมาจากสิ่งที่สัตว์ตัวนี้ต้องการ");

        if (selectableStarters == null || selectableStarters.Length == 0)
        {
            GUI.Label(new Rect(40, 110, 700, 24), "ยังไม่ได้ใส่ selectableStarters ใน Inspector");
            return;
        }

        for (int i = 0; i < selectableStarters.Length; i++)
        {
            PetSpecies species = selectableStarters[i];
            if (species == null) continue;

            Rect rect = new Rect(40 + i * (cardWidth + 14), 110, cardWidth, cardHeight);
            string body = $"[{i + 1}] {species.speciesName}\n\n" +
                          $"ต้องการ: {string.Join(", ", species.requiredSteps)}\n" +
                          $"อดทน: {species.patienceSeconds:F0} วิ\n" +
                          $"ค่าบริการ: {species.baseFee}";

            if (GUI.Button(rect, body))
            {
                StartRunWith(species, game);
            }
        }
    }

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
        GUI.Box(new Rect(0, 0, Screen.width, Screen.height), GUIContent.none);
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
        GUI.Box(new Rect(0, 0, Screen.width, Screen.height), GUIContent.none);

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
}
