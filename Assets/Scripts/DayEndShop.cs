using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// ร้านค้าท้ายวัน — ใช้เงินที่ได้จากลูกค้าซื้อของหลังจบวัน มี 3 หมวด:
///   บัฟ          — ตัวคูณ passive (เดินเร็ว / สถานีเร็ว / มิเตอร์ลดช้า / ค่าบริการ) ราคาขึ้นตามระดับ
///   สถานีเพิ่ม    — ได้สถานีชนิดที่มีอยู่แล้วเพิ่มอีก 1 ชิ้น (วางลงร้านทันที) ช่วยให้คิวไม่ตัน
///   สัตว์ชนิดใหม่ — ลูกค้าชนิดใหม่เริ่มมาตั้งแต่วันถัดไป และได้สถานีที่สัตว์ต้องใช้แต่ยังไม่มีมาด้วย
///
/// วงจร: DayEndRewardSystem (เลือกรางวัลฟรีเสร็จ) เรียก Open() → สุ่มสินค้ามาวางขาย
///       (การันตีสัตว์ใหม่ 1 + สถานีเพิ่ม 1 ถ้ามีให้ขาย ที่เหลือสุ่ม)
///       → ผู้เล่นซื้อได้หลายชิ้นเท่าที่เงินพอ (ชิ้นละครั้งต่อวัน)
///       → กด "ไปวันถัดไป" → Close() เรียก GameManager.BeginNextDay()
///
/// ระดับบัฟ/จำนวนสถานีที่ซื้อ รีเซ็ตทุกครั้งที่เริ่ม run ใหม่ (GameManager.OnRunStarted)
/// ตัวระบบไม่วาด UI เอง — GameFlowDebugControls อ่าน CurrentOffers + GetTitle/GetDetail แล้ววาดการ์ด
/// ถ้าไม่ได้ใส่ลงฉาก DayEndRewardSystem จะสร้างให้อัตโนมัติด้วยค่าเริ่มต้น
/// </summary>
public class DayEndShop : MonoBehaviour
{
    public enum OfferKind
    {
        Buff,
        ExtraStation,
        NewSpecies
    }

    [Serializable]
    public class ShopItem
    {
        [Tooltip("ชื่อบนการ์ด")]
        public string label = "บัฟ";

        public PerkType perk = PerkType.MoveSpeed;

        [Tooltip("ขนาดบัฟต่อการซื้อ 1 ครั้ง เช่น 0.15 = 15%")]
        public float amount = 0.15f;

        [Tooltip("ราคาตอนซื้อครั้งแรก")]
        [Min(0)] public int basePrice = 60;

        [Tooltip("ราคาที่เพิ่มขึ้นทุกครั้งที่ซื้อชิ้นนี้ซ้ำใน run เดียวกัน")]
        [Min(0)] public int priceIncreasePerLevel = 40;

        [Tooltip("ซื้อได้สูงสุดกี่ครั้งต่อ run (0 = ไม่จำกัด)")]
        [Min(0)] public int maxLevel = 3;

        public ShopItem() { }

        public ShopItem(string label, PerkType perk, float amount, int basePrice, int priceIncreasePerLevel, int maxLevel)
        {
            this.label = label;
            this.perk = perk;
            this.amount = amount;
            this.basePrice = basePrice;
            this.priceIncreasePerLevel = priceIncreasePerLevel;
            this.maxLevel = maxLevel;
        }
    }

    /// <summary>สินค้า 1 ชิ้นที่วางขายในวันนี้ — ราคาล็อกไว้ตั้งแต่ตอนเปิดร้าน</summary>
    public class Offer
    {
        public OfferKind kind;
        public int price;
        public bool sold;

        public int buffIndex = -1;        // Buff: index ใน catalog
        public GameObject stationPrefab;  // ExtraStation
        public PetSpecies species;        // NewSpecies
    }

    public static DayEndShop Instance { get; private set; }

    [Header("Buffs")]
    [Tooltip("บัฟที่ขายในร้าน — ไม่ใส่ CatchRadius เพราะยังไม่มีระบบสัตว์หนี ซื้อไปก็ไม่มีผล")]
    [SerializeField] private List<ShopItem> catalog = new List<ShopItem>
    {
        new ShopItem("รองเท้าวิ่ง", PerkType.MoveSpeed, 0.15f, 50, 35, 3),
        new ShopItem("อุปกรณ์โปร", PerkType.InteractSpeed, 0.15f, 60, 40, 3),
        new ShopItem("เพลงผ่อนคลาย", PerkType.PatienceDrain, 0.15f, 60, 40, 3),
        new ShopItem("บัตรสมาชิก", PerkType.ServiceFee, 0.25f, 70, 45, 3)
    };

    [Header("Extra Station")]
    [Tooltip("ขายสถานีชนิดที่ปลดล็อกแล้วเพิ่มอีก 1 ชิ้น")]
    [SerializeField] private bool sellExtraStations = true;

    [Tooltip("ราคาสถานีเพิ่มครั้งแรกของแต่ละชนิด")]
    [Min(0)]
    [SerializeField] private int extraStationPrice = 45;

    [Tooltip("ราคาที่เพิ่มขึ้นทุกครั้งที่ซื้อสถานีชนิดเดิมเพิ่มใน run เดียวกัน")]
    [Min(0)]
    [SerializeField] private int extraStationPriceIncrease = 30;

    [Tooltip("จำนวนสูงสุดของสถานีแต่ละชนิดในร้าน (นับรวมที่ได้มาตอนปลดล็อก) ครบแล้วจะไม่ขายชนิดนั้นอีก")]
    [Min(1)]
    [SerializeField] private int maxCopiesPerStation = 4;

    [Header("New Species")]
    [Tooltip("ขายสัตว์ชนิดใหม่ (ได้สถานีที่ยังไม่มีมาด้วย)")]
    [SerializeField] private bool sellNewSpecies = true;

    [Tooltip("สัตว์ที่ขายในร้าน (ตัวที่ปลดล็อกแล้วจะไม่ขายซ้ำ)")]
    [SerializeField] private List<PetSpecies> speciesForSale = new List<PetSpecies>();

    [Tooltip("ราคาพื้นฐานของสัตว์ชนิดใหม่")]
    [Min(0)]
    [SerializeField] private int newSpeciesPrice = 70;

    [Tooltip("ราคาเพิ่มต่อสถานีใหม่ 1 ชนิดที่จะได้มาพร้อมสัตว์ตัวนั้น")]
    [Min(0)]
    [SerializeField] private int pricePerNewStation = 25;

    [Header("Offers")]
    [Tooltip("จำนวนสินค้าที่วางขายต่อวัน — การันตีสัตว์ใหม่ 1 + สถานีเพิ่ม 1 (ถ้ามีให้ขาย) ที่เหลือสุ่มจากทุกหมวด")]
    [Min(1)]
    [SerializeField] private int itemsPerDay = 4;

    [Header("Input")]
    [Tooltip("กด 1..9 ซื้อสินค้า และกด continueKey ไปวันถัดไปได้โดยไม่ต้องคลิก")]
    [SerializeField] private bool allowKeyboard = true;

    [SerializeField] private KeyCode continueKey = KeyCode.Return;

    /// <summary>ร้านกำลังเปิดอยู่ (อยู่ในช่วง DayEnd หลังเลือกรางวัลฟรีแล้ว)</summary>
    public bool IsOpen { get; private set; }

    /// <summary>สินค้าที่วางขายวันนี้</summary>
    public IReadOnlyList<Offer> CurrentOffers => offers;

    /// <summary>เงินที่มีตอนนี้ (อ่านจากกระเป๋ากลางใน ReturnZone)</summary>
    public int Money => ReturnZone.TotalWallet;

    public KeyCode ContinueKey => continueKey;

    public event Action OnShopOpened;
    public event Action<Offer> OnItemPurchased;
    public event Action OnShopClosed;

    private readonly List<Offer> offers = new List<Offer>();

    // จำนวนครั้งที่ซื้อบัฟแต่ละชิ้นไปแล้วใน run นี้ — index ตรงกับ catalog
    private int[] buffLevels = Array.Empty<int>();

    // จำนวนสถานีที่ซื้อเพิ่มไปแล้วของแต่ละ prefab ใน run นี้ ใช้คิดราคาที่ขึ้นเรื่อยๆ
    private readonly Dictionary<GameObject, int> extraStationsBought = new Dictionary<GameObject, int>();

    private int openedFrame = -1;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            // ลบแค่ component ไม่ลบทั้ง GameObject เพราะอาจถูกสร้างอัตโนมัติบน object ของระบบอื่น
            Debug.LogWarning("[DayEndShop] มีมากกว่า 1 ตัวในฉาก ลบตัวที่ซ้ำทิ้ง");
            Destroy(this);
            return;
        }

        Instance = this;
        buffLevels = new int[catalog.Count];
    }

    private void Start()
    {
        if (GameManager.Instance != null)
        {
            GameManager.Instance.OnRunStarted += HandleRunStarted;
        }
    }

    private void OnDestroy()
    {
        if (GameManager.Instance != null)
        {
            GameManager.Instance.OnRunStarted -= HandleRunStarted;
        }

        if (Instance == this) Instance = null;
    }

    private void HandleRunStarted()
    {
        buffLevels = new int[catalog.Count];
        extraStationsBought.Clear();
        offers.Clear();
        IsOpen = false;
    }

    private void Update()
    {
        if (!IsOpen) return;

        // phase ถูกเปลี่ยนจากที่อื่นระหว่างร้านเปิด — ปิดร้านเงียบๆ โดยไม่พาไปวันถัดไป
        GameManager game = GameManager.Instance;
        if (game != null && game.Phase != GamePhase.DayEnd)
        {
            IsOpen = false;
            offers.Clear();
            return;
        }

        if (!allowKeyboard) return;
        if (game != null && game.IsPaused) return; // เมนู Esc เปิดอยู่ ไม่รับปุ่มซื้อของ

        // เฟรมเดียวกับที่เปิดร้าน ปุ่มเลขที่เพิ่งกดเลือกรางวัลฟรียังนับเป็น GetKeyDown อยู่
        // ถ้าไม่ข้ามจะซื้อสินค้าชิ้นแรกไปทันทีโดยผู้เล่นไม่ได้ตั้งใจ
        if (Time.frameCount == openedFrame) return;

        for (int i = 0; i < offers.Count && i < 9; i++)
        {
            if (GameInput.DigitPressed(i + 1))
            {
                TryBuy(i);
                return;
            }
        }

        if (GameInput.Pressed(continueKey))
        {
            Close();
        }
    }

    /// <summary>
    /// เปิดร้านค้าท้ายวัน — คืน false ถ้าเปิดไม่ได้ (ไม่ใช่ช่วงจบวัน หรือไม่มีอะไรเหลือให้ขาย)
    /// ถ้าคืน false ผู้เรียกต้องพาไปวันถัดไปเอง
    /// </summary>
    public bool Open()
    {
        GameManager game = GameManager.Instance;
        if (game != null && game.Phase != GamePhase.DayEnd) return false;

        RollOffers();
        if (offers.Count == 0)
        {
            GameLog.Push("ร้านค้าท้ายวัน: ไม่มีสินค้าเหลือให้ซื้อแล้ว");
            return false;
        }

        IsOpen = true;
        openedFrame = Time.frameCount;

        GameLog.Push($"ร้านค้าท้ายวันเปิดแล้ว — มีเงิน {Money} (กด 1–{offers.Count} ซื้อ, {continueKey} ไปวันถัดไป)", LogTone.Good);
        OnShopOpened?.Invoke();
        return true;
    }

    /// <summary>ซื้อสินค้าตาม index ของ CurrentOffers — คืน false ถ้าซื้อไม่ได้ (ขายไปแล้ว/เงินไม่พอ/วางสถานีไม่ลง)</summary>
    public bool TryBuy(int offerIndex)
    {
        if (!IsOpen || offerIndex < 0 || offerIndex >= offers.Count) return false;

        Offer offer = offers[offerIndex];
        string title = GetTitle(offer);

        if (offer.sold)
        {
            GameLog.Push($"{title} ซื้อไปแล้ววันนี้", LogTone.Warn);
            return false;
        }

        if (Money < offer.price)
        {
            GameLog.Push($"เงินไม่พอซื้อ {title} — ต้องใช้ {offer.price} มีอยู่ {Money}", LogTone.Warn);
            return false;
        }

        PlayerProgress progress = PlayerProgress.Instance;
        if (progress == null)
        {
            Debug.LogError("[DayEndShop] ไม่มี PlayerProgress ในฉาก ขายของไม่ได้");
            return false;
        }

        // ให้ของก่อนแล้วค่อยหักเงิน — ถ้าให้ของไม่สำเร็จ (เช่นร้านเต็มวางสถานีไม่ลง) จะไม่เสียเงินฟรี
        // เช็คเงินพอไปแล้วด้านบน TrySpend ตรงนี้จึงผ่านเสมอ
        string result;
        switch (offer.kind)
        {
            case OfferKind.Buff:
                result = GrantBuff(offer, progress);
                break;

            case OfferKind.ExtraStation:
                result = GrantExtraStation(offer);
                break;

            case OfferKind.NewSpecies:
                result = GrantSpecies(offer, progress);
                break;

            default:
                result = null;
                break;
        }

        if (result == null) return false;

        ReturnZone.TrySpend(offer.price);
        offer.sold = true;

        GameLog.Push($"ซื้อ {title} ({result}) -{offer.price} | เหลือเงิน {Money}", LogTone.Good);
        OnItemPurchased?.Invoke(offer);
        return true;
    }

    /// <summary>ปิดร้านแล้วเข้าช่วงจัดร้านของวันถัดไป</summary>
    public void Close()
    {
        if (!IsOpen) return;

        IsOpen = false;
        offers.Clear();
        OnShopClosed?.Invoke();

        GameManager.Instance?.BeginNextDay();
    }

    /// <summary>กดซื้อได้ไหม — ใช้เปิด/ปิดปุ่มบน UI</summary>
    public bool CanBuy(Offer offer)
    {
        return IsOpen && offer != null && !offer.sold && Money >= offer.price;
    }

    /// <summary>ชื่อบนการ์ด</summary>
    public string GetTitle(Offer offer)
    {
        if (offer == null) return string.Empty;

        switch (offer.kind)
        {
            case OfferKind.Buff:
                return catalog[offer.buffIndex].label;

            case OfferKind.ExtraStation:
                return IsHoldingTable(offer.stationPrefab)
                    ? "โต๊ะพัก +1"
                    : $"สถานี{StationLabel(offer.stationPrefab)} +1";

            case OfferKind.NewSpecies:
                return $"ลูกค้าใหม่: {offer.species.speciesName}";

            default:
                return string.Empty;
        }
    }

    /// <summary>รายละเอียดบนการ์ด 2 บรรทัด — คำนวณสดทุกครั้ง จำนวน/ระดับจึงอัปเดตทันทีหลังซื้อ</summary>
    public string GetDetail(Offer offer)
    {
        if (offer == null) return string.Empty;

        switch (offer.kind)
        {
            case OfferKind.Buff:
            {
                ShopItem item = catalog[offer.buffIndex];
                int level = BuffLevelOf(offer.buffIndex);
                string levelText = item.maxLevel > 0 ? $"ระดับ {level}/{item.maxLevel}" : $"ระดับ {level}";
                return $"{DescribeEffect(item)}\n{levelText}";
            }

            case OfferKind.ExtraStation:
                return $"ดูแลได้หลายตัวพร้อมกันขึ้น\nมีอยู่ {CountInShop(offer.stationPrefab)}/{maxCopiesPerStation} ชิ้น";

            case OfferKind.NewSpecies:
            {
                List<CareStep> missing = MissingSteps(offer.species);
                string needs = string.Join(" + ", Array.ConvertAll(offer.species.requiredSteps, CareStepStyle.LabelOf));
                string bonus = missing.Count > 0
                    ? $"ได้สถานีใหม่: {string.Join(", ", missing.ConvertAll(CareStepStyle.LabelOf))}"
                    : "มีสถานีครบแล้ว";
                return $"ต้องการ: {needs}\n{bonus}";
            }

            default:
                return string.Empty;
        }
    }

    public static string DescribeEffect(ShopItem item)
    {
        if (item == null) return string.Empty;

        return item.perk switch
        {
            PerkType.MoveSpeed => $"เดินเร็วขึ้น +{item.amount:P0}",
            PerkType.InteractSpeed => $"สถานีทำงานเร็วขึ้น +{item.amount:P0}",
            PerkType.PatienceDrain => $"มิเตอร์ความอดทนลดช้าลง {item.amount:P0}",
            PerkType.ServiceFee => $"ค่าบริการ +{item.amount:P0}",
            PerkType.CatchRadius => $"ไล่จับสัตว์ง่ายขึ้น +{item.amount:P0} (ยังไม่มีผล)",
            _ => item.perk.ToString()
        };
    }

    // ---------- ให้ของ (คืนข้อความสรุปผล หรือ null ถ้าให้ไม่สำเร็จ) ----------

    private string GrantBuff(Offer offer, PlayerProgress progress)
    {
        ShopItem item = catalog[offer.buffIndex];
        progress.ApplyBuff(item.perk, item.amount);

        EnsureBuffLevels();
        buffLevels[offer.buffIndex]++;

        return DescribeEffect(item);
    }

    private string GrantExtraStation(Offer offer)
    {
        LevelInitializer level = FindAnyObjectByType<LevelInitializer>();
        if (level == null)
        {
            Debug.LogError("[DayEndShop] ไม่มี LevelInitializer ในฉาก วางสถานีเพิ่มไม่ได้");
            return null;
        }

        // วางลงโซนวางอัตโนมัติทันที ผู้เล่นยกย้ายได้ในช่วงจัดร้านของวันถัดไป
        if (level.AutoPlaceStation(offer.stationPrefab, offer.stationPrefab.name) == null)
        {
            GameLog.Push("ร้านเต็ม ไม่มีช่องว่างให้วางสถานีเพิ่ม — ยังไม่เสียเงิน", LogTone.Warn);
            return null;
        }

        extraStationsBought.TryGetValue(offer.stationPrefab, out int bought);
        extraStationsBought[offer.stationPrefab] = bought + 1;

        return $"มีอยู่ {CountInShop(offer.stationPrefab)} ชิ้น";
    }

    private string GrantSpecies(Offer offer, PlayerProgress progress)
    {
        // เก็บรายการสถานีที่ขาดไว้ก่อนปลดล็อก เพราะหลังปลดล็อกจะไม่ขาดแล้ว
        List<CareStep> missing = MissingSteps(offer.species);

        // UnlockSpecies ปลดล็อกสถานีที่ขาดให้เอง → LevelInitializer วางลงร้านทันที
        progress.UnlockSpecies(offer.species);

        return missing.Count > 0
            ? $"ได้สถานีใหม่: {string.Join(", ", missing.ConvertAll(CareStepStyle.LabelOf))}"
            : "ลูกค้าชนิดนี้เริ่มมาวันถัดไป";
    }

    // ---------- สุ่มสินค้า ----------

    private void RollOffers()
    {
        offers.Clear();
        EnsureBuffLevels();

        List<Offer> species = BuildSpeciesOffers();
        List<Offer> stations = BuildStationOffers();
        List<Offer> buffs = BuildBuffOffers();

        // การันตีหมวดละ 1 ก่อน ผู้เล่นจะได้เห็นทางเลือกครบทุกแบบ แล้วค่อยสุ่มเติมจากที่เหลือทุกหมวด
        TakeRandom(species, 1);
        TakeRandom(stations, 1);

        List<Offer> rest = new List<Offer>();
        rest.AddRange(species);
        rest.AddRange(stations);
        rest.AddRange(buffs);
        TakeRandom(rest, itemsPerDay);

        // จัดกลุ่มตามหมวด (บัฟ → สถานี → สัตว์) การ์ดหมวดเดียวกันจะอยู่ติดกันทุกวัน
        offers.Sort((a, b) => a.kind.CompareTo(b.kind));
    }

    private void TakeRandom(List<Offer> pool, int count)
    {
        for (int n = 0; n < count && pool.Count > 0 && offers.Count < itemsPerDay; n++)
        {
            int pick = UnityEngine.Random.Range(0, pool.Count);
            offers.Add(pool[pick]);
            pool.RemoveAt(pick);
        }
    }

    private List<Offer> BuildBuffOffers()
    {
        List<Offer> result = new List<Offer>();

        for (int i = 0; i < catalog.Count; i++)
        {
            ShopItem item = catalog[i];
            if (item == null) continue;

            int level = BuffLevelOf(i);
            if (item.maxLevel > 0 && level >= item.maxLevel) continue;

            result.Add(new Offer
            {
                kind = OfferKind.Buff,
                buffIndex = i,
                price = item.basePrice + item.priceIncreasePerLevel * level
            });
        }

        return result;
    }

    private List<Offer> BuildStationOffers()
    {
        List<Offer> result = new List<Offer>();

        PlayerProgress progress = PlayerProgress.Instance;
        if (!sellExtraStations || progress == null) return result;

        foreach (GameObject prefab in progress.UnlockedStations)
        {
            if (prefab == null || prefab.GetComponent<CareStation>() == null) continue;
            if (CountInShop(prefab) >= maxCopiesPerStation) continue;

            extraStationsBought.TryGetValue(prefab, out int bought);

            result.Add(new Offer
            {
                kind = OfferKind.ExtraStation,
                stationPrefab = prefab,
                price = extraStationPrice + extraStationPriceIncrease * bought
            });
        }

        return result;
    }

    private List<Offer> BuildSpeciesOffers()
    {
        List<Offer> result = new List<Offer>();

        PlayerProgress progress = PlayerProgress.Instance;
        if (!sellNewSpecies || progress == null) return result;

        foreach (PetSpecies species in SpeciesPool())
        {
            if (species == null || progress.UnlockedSpecies.Contains(species)) continue;
            if (result.Exists(o => o.species == species)) continue; // กันชนิดซ้ำในลิสต์

            result.Add(new Offer
            {
                kind = OfferKind.NewSpecies,
                species = species,
                price = newSpeciesPrice + pricePerNewStation * MissingSteps(species).Count
            });
        }

        return result;
    }

    // ---------- ตัวช่วย ----------

    /// <summary>สัตว์ที่ขายได้ — เมนูหลักอยู่คนละฉากแล้ว จึงต้องใส่รายการใน speciesForSale เอง</summary>
    private IEnumerable<PetSpecies> SpeciesPool()
    {
        return speciesForSale;
    }

    /// <summary>ขั้นตอนที่สัตว์ชนิดนี้ต้องใช้ แต่ร้านยังไม่มีสถานีรองรับ (ไม่ซ้ำ)</summary>
    private static List<CareStep> MissingSteps(PetSpecies species)
    {
        List<CareStep> missing = new List<CareStep>();

        PlayerProgress progress = PlayerProgress.Instance;
        if (species == null || species.requiredSteps == null || progress == null) return missing;

        foreach (CareStep step in species.requiredSteps)
        {
            if (!progress.HasStationFor(step) && !missing.Contains(step)) missing.Add(step);
        }

        return missing;
    }

    /// <summary>
    /// จำนวนสถานีชนิดเดียวกับ prefab นี้ที่อยู่ในร้านตอนนี้
    /// เทียบจากขั้นตอน/โหมดของ CareStation ไม่เทียบชื่อ เพราะชื่อ object เปลี่ยนได้
    /// </summary>
    public static int CountInShop(GameObject prefab)
    {
        CareStation template = prefab != null ? prefab.GetComponent<CareStation>() : null;
        if (template == null) return 0;

        bool templateHolding = template.Mode == CareStation.ServiceMode.Holding;
        int count = 0;

        foreach (CareStation station in FindObjectsByType<CareStation>())
        {
            bool holding = station.Mode == CareStation.ServiceMode.Holding;
            if (holding != templateHolding) continue;

            // โต๊ะพักทุกตัวถือเป็นชนิดเดียวกัน ส่วนสถานีบริการต้องให้บริการขั้นตอนเดียวกัน
            if (holding || station.Step == template.Step) count++;
        }

        return count;
    }

    private static bool IsHoldingTable(GameObject prefab)
    {
        CareStation station = prefab != null ? prefab.GetComponent<CareStation>() : null;
        return station != null && station.Mode == CareStation.ServiceMode.Holding;
    }

    private static string StationLabel(GameObject prefab)
    {
        CareStation station = prefab != null ? prefab.GetComponent<CareStation>() : null;
        if (station == null) return prefab != null ? prefab.name : "?";

        return station.Mode == CareStation.ServiceMode.Holding ? "โต๊ะพัก" : CareStepStyle.LabelOf(station.Step);
    }

    private int BuffLevelOf(int buffIndex)
    {
        EnsureBuffLevels();
        return buffIndex >= 0 && buffIndex < buffLevels.Length ? buffLevels[buffIndex] : 0;
    }

    /// <summary>catalog อาจถูกเพิ่ม/ลดใน Inspector ระหว่างกด Play — ปรับขนาด array ให้ตรงโดยคงระดับเดิมไว้</summary>
    private void EnsureBuffLevels()
    {
        if (buffLevels.Length != catalog.Count)
        {
            Array.Resize(ref buffLevels, catalog.Count);
        }
    }
}
