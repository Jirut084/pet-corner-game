using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>ชนิดของบัฟ passive ที่การ์ด Perk ให้ได้</summary>
public enum PerkType
{
    MoveSpeed,      // เดินเร็วขึ้น
    InteractSpeed,  // ใช้สถานีเร็วขึ้น
    PatienceDrain,  // มิเตอร์ความอดทนลดช้าลง
    ServiceFee,     // ค่าบริการเพิ่ม
    CatchRadius     // ไล่จับสัตว์หนีง่ายขึ้น
}

/// <summary>รางวัลท้ายวัน 1 ตัวเลือก — เป็นบัฟ, ปลดล็อกสถานี, หรือปลดล็อกสัตว์ชนิดใหม่</summary>
public enum RewardKind
{
    Perk,
    UnlockStation,
    UnlockSpecies
}

[Serializable]
public class RewardOption
{
    [Tooltip("ข้อความที่จะโชว์บนการ์ด")]
    public string label;

    public RewardKind kind;

    [Header("ใช้เมื่อ kind = Perk")]
    public PerkType perk;

    [Tooltip("ขนาดของบัฟ เช่น 0.15 = 15%")]
    public float amount = 0.15f;

    [Header("ใช้เมื่อ kind = UnlockStation")]
    public GameObject stationPrefab;

    [Header("ใช้เมื่อ kind = UnlockSpecies")]
    public PetSpecies species;

    [Header("ราคาที่ต้องจ่าย (ไม่บังคับ)")]
    [Tooltip("บัฟที่จะถูกหักกลับ เช่น PatienceDrain 0.1 = ลูกค้าใจร้อนขึ้น 10%")]
    public PerkType penaltyPerk = PerkType.PatienceDrain;

    [Tooltip("ขนาดของโทษ (0 = การ์ดใบนี้ไม่มีข้อเสีย)")]
    [Min(0f)]
    public float penaltyAmount = 0f;

    [Tooltip("ออเดอร์ต่อวันที่เพิ่มขึ้นถาวรจากการ์ดใบนี้")]
    [Min(0)]
    public int extraOrdersPerDay = 0;
}

/// <summary>
/// ระบบเลือกรางวัลท้ายวัน — สุ่มตัวเลือกมาให้ผู้เล่นเลือก 1 อัน (ฟรี)
/// เลือกเสร็จ (หรือไม่มีรางวัลในคลัง) → ส่งต่อให้ร้านค้าท้ายวัน DayEndShop → ร้านค้าพาเข้าวันถัดไป
///
/// ตัวระบบไม่ยุ่งกับ UI เลย: มันแค่ประกาศ "นี่คือตัวเลือกของรอบนี้" ผ่าน OnRewardsOffered
/// แล้วรอให้ UI (หรือปุ่ม debug) เรียก Choose(index) กลับมา
/// </summary>
public class DayEndRewardSystem : MonoBehaviour
{
    [Header("Reward Pool")]
    [Tooltip("คลังรางวัลทั้งหมด ระบบจะสุ่มมาจากที่นี่ โดยข้ามอันที่ปลดล็อกไปแล้ว")]
    [SerializeField] private List<RewardOption> rewardPool = new List<RewardOption>();

    [Tooltip("จำนวนตัวเลือกที่ยื่นให้ผู้เล่นเลือกในแต่ละวัน")]
    [SerializeField] private int optionsToOffer = 3;

    [Header("Flow")]
    [Tooltip("เลือกรางวัลฟรีเสร็จแล้วเปิดร้านค้าท้ายวัน (DayEndShop) ต่อหรือไม่ — ถ้าในฉากไม่มีจะสร้างให้อัตโนมัติ")]
    [SerializeField] private bool openShopAfterReward = true;

    [Header("Debug")]
    [Tooltip("กดปุ่ม 1/2/3 เลือกรางวัลได้เลยโดยไม่ต้องมี UI (สะดวกตอนเทส)")]
    [SerializeField] private bool allowKeyboardChoice = true;

    /// <summary>ตัวเลือกของรอบปัจจุบัน (ว่างถ้ายังไม่ถึงท้ายวัน)</summary>
    public IReadOnlyList<RewardOption> CurrentOffer => currentOffer;

    /// <summary>ยิงเมื่อมีตัวเลือกรางวัลใหม่ให้ผู้เล่นเลือก — UI มา subscribe ตรงนี้</summary>
    public event Action<IReadOnlyList<RewardOption>> OnRewardsOffered;

    /// <summary>ยิงหลังผู้เล่นเลือกแล้ว UI ใช้ปิด popup</summary>
    public event Action<RewardOption> OnRewardChosen;

    private readonly List<RewardOption> currentOffer = new List<RewardOption>();

    // subscribe ใน Start ไม่ใช่ OnEnable เพราะ OnEnable อาจรันก่อน GameManager.Awake
    // ทำให้ Instance ยังเป็น null แล้ว subscribe ไม่ติดเลยทั้งเกม
    private void Start()
    {
        if (rewardPool.Count == 0) rewardPool = BuildDefaultPool();

        if (GameManager.Instance == null)
        {
            Debug.LogError("[DayEndRewardSystem] ไม่มี GameManager ในฉาก จะไม่มีรางวัลท้ายวัน");
            return;
        }

        GameManager.Instance.OnDayEnded += HandleDayEnded;
    }

    /// <summary>
    /// คลังรางวัลเริ่มต้น ใช้เมื่อยังไม่ได้ตั้งอะไรใน Inspector — ฉากใหม่จึงมีรางวัลให้เลือกทันที
    /// ครึ่งหนึ่งเป็นการ์ดที่แรงกว่าแต่มีราคาต้องจ่าย ตามหลักของ PlateUp ที่การ์ดทุกใบมีทั้งได้และเสีย
    /// (การ์ดปลดล็อกสถานี/สัตว์ต้องอ้างอิง prefab จึงตั้งเองใน Inspector เท่านั้น ที่นี่มีแต่บัฟ)
    /// </summary>
    private static List<RewardOption> BuildDefaultPool()
    {
        return new List<RewardOption>
        {
            new RewardOption { label = "รองเท้าใหม่\nเดินเร็วขึ้น 20%", kind = RewardKind.Perk, perk = PerkType.MoveSpeed, amount = 0.2f },
            new RewardOption { label = "มือฉมัง\nใช้สถานีไวขึ้น 20%", kind = RewardKind.Perk, perk = PerkType.InteractSpeed, amount = 0.2f },
            new RewardOption { label = "มุมนั่งสบาย\nลูกค้าใจเย็นขึ้น 15%", kind = RewardKind.Perk, perk = PerkType.PatienceDrain, amount = 0.15f },
            new RewardOption { label = "ถุงมือจับสัตว์\nไล่จับง่ายขึ้น 60%", kind = RewardKind.Perk, perk = PerkType.CatchRadius, amount = 0.6f },

            new RewardOption
            {
                label = "ป้ายโฆษณา\nค่าบริการ +35%\nแต่ออเดอร์ต่อวัน +1",
                kind = RewardKind.Perk, perk = PerkType.ServiceFee, amount = 0.35f,
                extraOrdersPerDay = 1
            },
            new RewardOption
            {
                label = "เครื่องมือมืออาชีพ\nใช้สถานีไวขึ้น 40%\nแต่ลูกค้าใจร้อนขึ้น 15%",
                kind = RewardKind.Perk, perk = PerkType.InteractSpeed, amount = 0.4f,
                penaltyPerk = PerkType.PatienceDrain, penaltyAmount = 0.15f
            },
            new RewardOption
            {
                label = "เปิดร้านเช้าขึ้น\nเดินเร็วขึ้น 30%\nแต่ออเดอร์ต่อวัน +1",
                kind = RewardKind.Perk, perk = PerkType.MoveSpeed, amount = 0.3f,
                extraOrdersPerDay = 1
            }
        };
    }

    private void OnDestroy()
    {
        if (GameManager.Instance != null)
        {
            GameManager.Instance.OnDayEnded -= HandleDayEnded;
        }
    }

    private void Update()
    {
        if (!allowKeyboardChoice) return;
        if (currentOffer.Count == 0) return;

        // รับปุ่มเฉพาะช่วงจบวัน — กันกดเลือกรางวัลที่ค้างอยู่หลังจบ run แล้วเกมเล่นต่อเกินวันสุดท้าย
        if (GameManager.Instance == null || GameManager.Instance.Phase != GamePhase.DayEnd) return;
        if (GameManager.Instance.IsPaused) return; // เมนู Esc เปิดอยู่ ไม่รับปุ่มเลือกรางวัล

        for (int i = 0; i < currentOffer.Count && i < 9; i++)
        {
            if (GameInput.DigitPressed(i + 1))
            {
                Choose(i);
                return;
            }
        }
    }

    private void HandleDayEnded(int day)
    {
        // วันสุดท้าย GameManager จบ run ก่อนยิง OnDayEnded แล้ว — เห็น RunOver = ไม่ต้องยื่นรางวัล
        if (GameManager.Instance != null && GameManager.Instance.Phase == GamePhase.RunOver) return;

        RollOffer();
    }

    /// <summary>
    /// จบขั้นเลือกรางวัลฟรี → เปิดร้านค้าท้ายวัน (DayEndShop) ต่อ ไม่งั้นเข้าวันถัดไปเลย
    /// ร้านค้าจะเป็นคนเรียก BeginNextDay เองตอนผู้เล่นกดไปต่อ
    /// </summary>
    private void FinishRewardStage()
    {
        if (openShopAfterReward)
        {
            DayEndShop shop = DayEndShop.Instance;
            if (shop == null)
            {
                // ยังไม่ได้ใส่ร้านค้าลงฉาก — สร้างให้ด้วยค่าเริ่มต้นจะได้เล่นได้ทันที
                // อยากปรับราคา/สินค้า ให้ Add Component "DayEndShop" ลงฉากเองแล้วแก้ใน Inspector
                shop = gameObject.AddComponent<DayEndShop>();
                Debug.Log("[DayEndRewardSystem] ไม่พบ DayEndShop ในฉาก สร้างให้อัตโนมัติด้วยค่าเริ่มต้น");
            }

            if (shop.Open()) return;
        }

        GameManager.Instance?.BeginNextDay();
    }

    /// <summary>สุ่มตัวเลือกรางวัลใหม่ ข้ามอันที่ปลดล็อกไปแล้ว</summary>
    public void RollOffer()
    {
        currentOffer.Clear();

        List<RewardOption> candidates = new List<RewardOption>();
        foreach (RewardOption option in rewardPool)
        {
            if (option != null && IsStillRelevant(option)) candidates.Add(option);
        }

        int count = Mathf.Min(optionsToOffer, candidates.Count);
        for (int i = 0; i < count; i++)
        {
            int pick = UnityEngine.Random.Range(0, candidates.Count);
            currentOffer.Add(candidates[pick]);
            candidates.RemoveAt(pick); // กันสุ่มซ้ำใบเดิมในรอบเดียวกัน
        }

        if (currentOffer.Count == 0)
        {
            Debug.Log("[DayEndRewardSystem] ไม่มีรางวัลฟรีเหลือให้เลือก ไปต่อที่ร้านค้าท้ายวัน");
            FinishRewardStage();
            return;
        }

        Debug.Log($"[DayEndRewardSystem] เลือกรางวัล 1 ใน {currentOffer.Count} (กด 1–{currentOffer.Count}):");
        for (int i = 0; i < currentOffer.Count; i++)
        {
            Debug.Log($"   [{i + 1}] {DescribeOption(currentOffer[i])}");
        }

        OnRewardsOffered?.Invoke(currentOffer);
    }

    /// <summary>ผู้เล่นเลือกรางวัลตาม index ของตัวเลือกที่ยื่นให้</summary>
    public void Choose(int index)
    {
        if (GameManager.Instance != null && GameManager.Instance.Phase != GamePhase.DayEnd)
        {
            Debug.LogWarning($"[DayEndRewardSystem] เลือกรางวัลได้เฉพาะตอนจบวัน (ตอนนี้ {GameManager.Instance.Phase})");
            return;
        }

        if (index < 0 || index >= currentOffer.Count)
        {
            Debug.LogWarning($"[DayEndRewardSystem] index {index} ไม่อยู่ในช่วงตัวเลือก");
            return;
        }

        RewardOption chosen = currentOffer[index];
        Apply(chosen);

        currentOffer.Clear();
        OnRewardChosen?.Invoke(chosen);

        // เลือกเสร็จแล้วไปร้านค้าท้ายวันต่อ (ร้านค้าจะพาเข้าวันถัดไปเอง)
        FinishRewardStage();
    }

    private void Apply(RewardOption option)
    {
        PlayerProgress progress = PlayerProgress.Instance;
        if (progress == null)
        {
            Debug.LogError("[DayEndRewardSystem] ไม่มี PlayerProgress ในฉาก ให้รางวัลไม่ได้");
            return;
        }

        switch (option.kind)
        {
            case RewardKind.Perk:
                progress.ApplyBuff(option.perk, option.amount);
                break;

            case RewardKind.UnlockStation:
                progress.UnlockStation(option.stationPrefab);
                break;

            case RewardKind.UnlockSpecies:
                // UnlockSpecies จะปลดล็อกสถานีที่สัตว์ชนิดนี้ต้องใช้ให้เองถ้ายังไม่มี
                progress.UnlockSpecies(option.species);
                break;
        }

        // ราคาที่ต้องจ่าย — การ์ดแรงๆ ต้องมีข้อเสียถ่วงไว้ ไม่งั้นเล่นไปยิ่งง่าย
        if (option.penaltyAmount > 0f) progress.ApplyBuff(option.penaltyPerk, -option.penaltyAmount);
        if (option.extraOrdersPerDay > 0) progress.AddDailyOrders(option.extraOrdersPerDay);

        GameLog.Push($"ได้รับรางวัล: {DescribeOption(option)}", LogTone.Good);
    }

    /// <summary>รางวัลที่ปลดล็อกไปแล้วไม่ต้องเอามาเสนอซ้ำ ส่วนการ์ดบัฟเสนอซ้ำได้เรื่อยๆ</summary>
    private bool IsStillRelevant(RewardOption option)
    {
        PlayerProgress progress = PlayerProgress.Instance;
        if (progress == null) return true;

        switch (option.kind)
        {
            case RewardKind.UnlockStation:
                return option.stationPrefab != null && !progress.UnlockedStations.Contains(option.stationPrefab);

            case RewardKind.UnlockSpecies:
                return option.species != null && !progress.UnlockedSpecies.Contains(option.species);

            default:
                return true;
        }
    }

    private string DescribeOption(RewardOption option)
    {
        if (!string.IsNullOrEmpty(option.label)) return option.label;

        switch (option.kind)
        {
            case RewardKind.Perk: return $"{option.perk} +{option.amount:P0}";
            case RewardKind.UnlockStation: return $"ปลดล็อกสถานี {(option.stationPrefab != null ? option.stationPrefab.name : "?")}";
            case RewardKind.UnlockSpecies: return $"ปลดล็อกสัตว์ {(option.species != null ? option.species.speciesName : "?")}";
            default: return "(ไม่มีชื่อ)";
        }
    }
}
