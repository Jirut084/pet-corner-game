using UnityEngine;

/// <summary>
/// จุดส่งคืนสัตว์เลี้ยง (Return Station)
/// ผู้เล่นกด Interact ขณะอุ้มสัตว์อยู่ในระยะเพื่อปิดออร์เดอร์ คำนวณเงิน และทำลาย GameObject ของสัตว์
/// </summary>
public class ReturnZone : MonoBehaviour, IInteractable
{
    [Header("Zone Settings")]
    [Tooltip("รัศมีตรวจจับ (ใช้ประกอบการ debug/อนาคตถ้าต้องการเช็คระยะเพิ่มเติม)")]
    [SerializeField] private float detectionRadius = 2f;

    [Header("Owner Pickup")]
    [Tooltip("จุดที่เจ้าของมายืนรอรับสัตว์ที่ดูแลครบแล้ว หมุนตามทิศของ ReturnZone — " +
             "เลือก ReturnZone แล้วดูเส้นสีเขียวใน Scene view ให้จุดอยู่ฝั่งลูกค้า ไม่จมในเคาน์เตอร์/กำแพง " +
             "(คนต่อมายืนเรียงออกไปตามแนวเส้นนี้)")]
    [SerializeField] private Vector3 ownerStandOffset = new Vector3(0f, 0f, 1.5f);

    /// <summary>จุดยืนรอรับของเจ้าของคนแรก — CustomerManager ใช้เรียงแถวรอรับ</summary>
    public Vector3 OwnerStandPosition => transform.position + transform.rotation * ownerStandOffset;

    [Header("Payout Settings")]
    [Tooltip("ตัวคูณค่าบริการเมื่อส่งคืนทั้งที่ยังทำขั้นตอนไม่ครบ (0.5 = ได้ครึ่งเดียว)")]
    [Range(0f, 1f)]
    [SerializeField] private float incompleteMultiplier = 0.5f;

    // เก็บเงินรวมสะสมข้ามหลายรอบของ loop แบบง่ายที่สุด (static ตามที่ระบุไว้)
    // หมายเหตุ: ถ้าโปรเจกต์ขยายใหญ่ขึ้นในอนาคต แนะนำย้ายไปเป็น EconomyManager แยกต่างหาก
    // เพื่อไม่ให้ ReturnZone ทำหน้าที่เกินขอบเขตของตัวเอง (Single Responsibility)
    public static int TotalWallet { get; private set; } = 0;

    /// <summary>ยิงทุกครั้งที่ TotalWallet เปลี่ยน ส่งยอดรวมล่าสุดไปด้วย ให้ UI subscribe แทนการ poll ทุกเฟรม</summary>
    public static event System.Action<int> OnWalletChanged;

    /// <summary>หักเงินจากกระเป๋า (ร้านค้าท้ายวันใช้) — คืน false และไม่หักเลยถ้าเงินไม่พอ</summary>
    public static bool TrySpend(int amount)
    {
        if (amount < 0 || TotalWallet < amount) return false;

        TotalWallet -= amount;
        OnWalletChanged?.Invoke(TotalWallet);
        return true;
    }

    /// <summary>
    /// ล้างเงินเป็น 0 — GameManager เรียกตอนเริ่ม run ใหม่
    /// จำเป็นเพราะเป็นตัวแปร static จึงค้างข้าม run (และข้ามรอบกด Play ถ้าปิด Domain Reload)
    /// </summary>
    public static void ResetWallet()
    {
        TotalWallet = 0;
        OnWalletChanged?.Invoke(TotalWallet);
    }

    public void Interact(GameObject interactor)
    {
        PlayerCarrier carrier = interactor.GetComponent<PlayerCarrier>();
        if (carrier == null || !carrier.IsCarrying)
        {
            GameLog.Push("ไม่มีอะไรในมือ ส่งคืนไม่ได้", LogTone.Warn);
            return;
        }

        // ถือสินค้ามาส่ง = คนละเส้นทางกับการส่งสัตว์คืน จัดการให้จบตรงนี้เลย
        CarriedGood good = carrier.GetHeld<CarriedGood>();
        if (good != null)
        {
            DeliverGood(carrier, good);
            return;
        }

        // ตอนนี้ผู้เล่นถือได้ทั้งสัตว์และกล่องอุปกรณ์ จึงต้องเช็คว่าของในมือเป็นสัตว์จริงๆ
        Pet pet = carrier.GetHeld<Pet>();
        if (pet == null)
        {
            GameLog.Push($"{carrier.HeldObject.name} ไม่ใช่สัตว์เลี้ยง ส่งคืนไม่ได้", LogTone.Warn);
            return;
        }

        // ค่าบริการ = ค่าฐานของชนิดสัตว์ + ค่าต่อขั้นตอนที่ทำสำเร็จจริง
        // ส่งคืนทั้งที่ทำไม่ครบก็ยังรับได้ แต่โดนหักตาม incompleteMultiplier (ไม่ block การส่งคืน)
        PetSpecies species = pet.Species;
        if (species == null)
        {
            Debug.LogWarning($"[ReturnZone] {pet.DisplayName} ยังไม่ได้ Initialize ชนิดสัตว์ คิดค่าบริการไม่ได้");
            return;
        }

        int earned = species.baseFee + (species.feePerStep * pet.StepsCompleted);

        if (!pet.IsComplete)
        {
            earned = Mathf.RoundToInt(earned * incompleteMultiplier);
        }

        // บัฟ "ค่าบริการ +x%" คูณหลังหัก penalty แล้ว
        if (PlayerProgress.Instance != null)
        {
            earned = Mathf.RoundToInt(earned * PlayerProgress.Instance.FeeMultiplier);
        }

        // เหตุการณ์ประจำวัน (ลูกค้า VIP / ชั่วโมงเร่งด่วน) คูณค่าบริการอีกชั้น
        DayEvent dayEvent = GameManager.Instance != null ? GameManager.Instance.CurrentEvent : null;
        if (dayEvent != null) earned = Mathf.RoundToInt(earned * dayEvent.feeMultiplier);

        // ทิปที่สะสมไว้ระหว่างทางไม่โดนหัก penalty และไม่โดนคูณบัฟ เป็นค่าคงที่ตรงๆ
        earned += pet.CollectedTip;

        AddMoney(earned);
        RunStats.NotePetReturned(earned, pet.CollectedTip, pet.IsComplete);
        AudioManager.Instance?.PlayGetMoney();

        pet.StopPatience();

        // บอกเจ้าของว่าสัตว์กลับมาแล้ว จะได้ลุกจากจุดรอแล้วเดินออกจากร้าน
        CustomerManager.Instance?.NotifyOrderDelivered(pet);

        string status = pet.IsComplete ? "ครบทุกขั้นตอน" : $"ไม่ครบ ({pet.StepsCompleted}/{pet.TotalSteps})";
        string tipText = pet.CollectedTip > 0 ? $" + ทิป {pet.CollectedTip}" : string.Empty;
        GameLog.Push($"ส่งคืน {species.speciesName} ({status}) → ได้ {earned}{tipText} | เงินรวม {TotalWallet}",
            pet.IsComplete ? LogTone.Good : LogTone.Warn);

        // ส่งคืนทั้งที่ทำขั้นตอนไม่ครบ = ร้องเรียน 1 ครั้ง (ไม่ block การส่งคืน) ครบ = ชื่อเสียง +1
        if (!pet.IsComplete)
        {
            GameManager.Instance?.AddComplaint($"{species.speciesName} ดูแลไม่ครบขั้นตอน");
        }
        else
        {
            GameManager.Instance?.AddReputation(1);
        }

        GameManager.Instance?.CompleteOrder(pet);

        // ปล่อยสัตว์ออกจากมือผู้เล่นแล้วทำลายทิ้ง เพื่อปิดวงจร 1 รอบของ Core Loop
        GameObject releasedPet = carrier.Consume();
        if (releasedPet != null)
        {
            Destroy(releasedPet);
        }
    }

    /// <summary>ส่งสินค้าให้ลูกค้าที่สั่งไว้ — ได้เงินทันที ไม่กระทบออเดอร์สัตว์ที่ยังค้างอยู่</summary>
    private void DeliverGood(PlayerCarrier carrier, CarriedGood good)
    {
        Customer customer = CustomerManager.Instance != null
            ? CustomerManager.Instance.MostUrgentGoodsRequest(good.Item)
            : null;

        if (customer == null)
        {
            GameLog.Push($"ไม่มีลูกค้าที่รอ {good.Item.label} อยู่แล้ว — เอาไปเก็บคืนที่ตู้สินค้า", LogTone.Warn);
            return;
        }

        customer.ReceiveGood();

        GameObject box = carrier.Consume();
        if (box != null) Destroy(box);

        AddMoney(good.Item.bonusPay);
        RunStats.NoteGoodsDelivered(good.Item.bonusPay);
        GameManager.Instance?.AddReputation(1);
        AudioManager.Instance?.PlayGetMoney();

        GameLog.Push($"ส่ง {good.Item.label} ให้ลูกค้า → ได้เพิ่ม {good.Item.bonusPay} | เงินรวม {TotalWallet}", LogTone.Good);
    }

    /// <summary>ทางเข้า-ออกของเงินทางเดียวของเกม — ระบบอื่นเพิ่ม/หักเงินผ่านตรงนี้เท่านั้น (ค่าติดลบ = หัก)</summary>
    public static void AddMoney(int amount)
    {
        if (amount == 0) return;

        TotalWallet += amount;
        OnWalletChanged?.Invoke(TotalWallet);
    }

    // แสดง wire sphere ใน Scene view เพื่อ debug ตำแหน่ง/รัศมีจุดส่งคืน
    private void OnDrawGizmos()
    {
        Gizmos.color = Color.green;
        Gizmos.DrawWireSphere(transform.position, detectionRadius);

        Vector3 stand = OwnerStandPosition;
        Gizmos.DrawLine(transform.position, stand);
        Gizmos.DrawWireSphere(stand, 0.4f);
    }
}
