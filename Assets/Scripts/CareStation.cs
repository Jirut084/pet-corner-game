using UnityEngine;

/// <summary>
/// สถานีดูแลสัตว์ 1 จุด — ครอบคลุมทุกสถานีด้วย class เดียวโดยตั้งค่าใน Inspector
///
/// มี 4 โหมดการทำงาน:
///   Instant  — กดครั้งเดียว สำเร็จทันที (ผู้เล่นอุ้มสัตว์ไว้ตลอด)
///   Hold     — กดค้างให้อยู่ในช่วงเวลาที่กำหนด ค้างนานเกินถือว่าพลาด
///   DropOff  — วางสัตว์ทิ้งไว้แล้วเดินไปทำอย่างอื่น สถานีทำงานเองจนเสร็จ แล้วกลับมาอุ้มไป
///   Holding  — โต๊ะพัก ไม่ทำอะไร แค่เป็นที่วางสัตว์ให้มือว่าง
///
/// DropOff คือหัวใจของความกดดันแบบ PlateUp! — ผู้เล่นถึงจะมีหลายอย่างค้างให้บริหารพร้อมกัน
/// </summary>
public class CareStation : MonoBehaviour, IHoldInteractable
{
    public enum ServiceMode
    {
        Instant,
        Hold,
        DropOff,
        Holding
    }

    public enum StationState
    {
        Idle,     // ว่าง
        Working,  // มีสัตว์อยู่ กำลังทำงาน
        Ready     // ทำเสร็จแล้ว รอผู้เล่นมาอุ้มไป
    }

    [Header("Station")]
    [Tooltip("สถานีนี้ให้บริการขั้นตอนไหน (โหมด Holding ไม่ใช้ค่านี้)")]
    [SerializeField] private CareStep step = CareStep.Feed;

    [SerializeField] private ServiceMode mode = ServiceMode.Instant;

    [Header("Hold Settings (โหมด Hold)")]
    [Tooltip("ต้องกดค้างอย่างน้อยกี่วินาทีถึงจะนับว่าสำเร็จ")]
    [SerializeField] private float minHoldSeconds = 1.5f;

    [Tooltip("ค้างเกินกี่วินาทีถือว่าพลาด ต้องปล่อยแล้วเริ่มใหม่")]
    [SerializeField] private float maxHoldSeconds = 2f;

    [Header("Drop-off Settings (โหมด DropOff)")]
    [Tooltip("สถานีใช้เวลาทำงานเองกี่วินาทีหลังวางสัตว์ลง")]
    [SerializeField] private float processSeconds = 3.5f;

    [Header("Tip")]
    [Tooltip("เงินทิปที่ได้เมื่อพาสัตว์ที่ดูแลครบแล้วมาเข้าสถานีที่ไม่ได้ต้องการ (สถานีละครั้งต่อสัตว์ 1 ตัว)")]
    [SerializeField] private int tipAmount = 10;

    [Header("Visual")]
    [Tooltip("จุดที่สัตว์จะไปวางอยู่บนสถานี ปล่อยว่างจะใช้ตำแหน่งสถานี + สูงขึ้น 1 หน่วย")]
    [SerializeField] private Transform petAnchor;

    [Header("Debug")]
    [SerializeField] private float gizmoRadius = 1f;

    private bool currentHoldFailed;
    private float progress; // 0–1 สำหรับโหมด DropOff

    public CareStep Step => step;
    public ServiceMode Mode => mode;
    public StationState State { get; private set; } = StationState.Idle;

    /// <summary>สัตว์ที่วางอยู่บนสถานีนี้ (null = ว่าง)</summary>
    public Pet OccupyingPet { get; private set; }

    /// <summary>ความคืบหน้า 0–1 ของโหมด DropOff สำหรับวาดหลอดบนหัวสถานี</summary>
    public float Progress => progress;

    /// <summary>โหมด Hold เท่านั้นที่ใช้เส้นทางกดค้างของ InteractionManager</summary>
    public bool UseHold => mode == ServiceMode.Hold;

    // บัฟ "ใช้สถานีเร็วขึ้น" ย่นทั้งช่วงเวลาตามสัดส่วน หน้าต่างเวลาจึงยังกว้างเท่าเดิมเชิงสัดส่วน
    private float InteractSpeed =>
        PlayerProgress.Instance != null ? Mathf.Max(0.1f, PlayerProgress.Instance.InteractSpeedMultiplier) : 1f;

    private float EffectiveMinHold => minHoldSeconds / InteractSpeed;
    private float EffectiveMaxHold => maxHoldSeconds / InteractSpeed;

    private Vector3 PetRestPosition => petAnchor != null ? petAnchor.position : transform.position + Vector3.up;

    private void Update()
    {
        // สัตว์บนสถานีถูกทำลายจากที่อื่น (เช่นออเดอร์หลุดเพราะมิเตอร์หมด) — คืนสถานีให้ว่าง
        // ไม่งั้นสถานีจะค้างสถานะ Working/Ready ทั้งที่ไม่มีอะไรอยู่ ใช้ต่อไม่ได้ทั้งวัน
        if (State != StationState.Idle && OccupyingPet == null)
        {
            State = StationState.Idle;
            progress = 0f;
        }

        if (State != StationState.Working) return;
        if (mode != ServiceMode.DropOff) return;
        if (processSeconds <= 0f) return;

        progress += Time.deltaTime * InteractSpeed / processSeconds;
        if (progress < 1f) return;

        progress = 1f;
        State = StationState.Ready;
        FinishDropOffWork();
    }

    // ---------- โหมดกดครั้งเดียว (Instant / DropOff / Holding) ----------

    public void Interact(GameObject interactor)
    {
        if (UseHold) return; // โหมด Hold ใช้เส้นทาง OnHoldRelease แทน

        PlayerCarrier carrier = interactor.GetComponent<PlayerCarrier>();
        if (carrier == null)
        {
            Debug.LogWarning($"[CareStation:{Label}] ผู้เล่นไม่มี PlayerCarrier component");
            return;
        }

        switch (mode)
        {
            case ServiceMode.Instant:
                HandleInstant(carrier);
                break;

            case ServiceMode.DropOff:
            case ServiceMode.Holding:
                HandleDropOffOrHolding(carrier);
                break;
        }
    }

    private void HandleInstant(PlayerCarrier carrier)
    {
        Pet pet = GetServablePet(carrier);
        if (pet == null) return;

        ApplyStep(pet);
    }

    /// <summary>โหมดวางทิ้งไว้: กดตอนมือว่าง = อุ้มสัตว์บนสถานีขึ้นมา / กดตอนถือสัตว์ = วางลง</summary>
    private void HandleDropOffOrHolding(PlayerCarrier carrier)
    {
        // มีสัตว์อยู่บนสถานี → พยายามอุ้มขึ้นมา
        if (OccupyingPet != null)
        {
            if (carrier.IsCarrying)
            {
                Debug.Log($"[CareStation:{Label}] มือไม่ว่าง วางของที่ถืออยู่ก่อน");
                return;
            }

            if (mode == ServiceMode.DropOff && State == StationState.Working)
            {
                Debug.Log($"[CareStation:{Label}] กำลังทำงานอยู่ ({progress:P0}) รออีกนิด");
                return;
            }

            Pet taken = OccupyingPet;
            ReleaseOccupant();

            if (!carrier.Carry(taken.gameObject))
            {
                // ยกไม่ขึ้น → เอากลับไปวางไว้ที่เดิม ไม่ปล่อยสัตว์ลอยไร้เจ้าของ
                PlaceOnStation(taken);
                return;
            }

            Debug.Log($"[CareStation:{Label}] อุ้ม {taken.DisplayName} ขึ้นมาแล้ว → {taken.GetProgressText()}");
            return;
        }

        // สถานีว่าง → พยายามวางสัตว์ที่ถืออยู่ลง
        if (!carrier.IsCarrying)
        {
            Debug.Log($"[CareStation:{Label}] ว่างอยู่ เอาสัตว์มาวางได้");
            return;
        }

        Pet held = carrier.GetHeld<Pet>();
        if (held == null)
        {
            Debug.Log($"[CareStation:{Label}] ของที่ถืออยู่ไม่ใช่สัตว์เลี้ยง");
            return;
        }

        // โต๊ะพักรับได้เสมอ ส่วนสถานีบริการต้องตรงกับที่สัตว์ต้องการ หรือเป็นบริการเสริมเอาทิป
        if (mode == ServiceMode.DropOff && !held.NeedsStep(step) && !CanTip(held))
        {
            Debug.Log(held.IsComplete
                ? $"[CareStation:{Label}] {held.DisplayName} รับบริการเสริมที่นี่ไปแล้ว"
                : $"[CareStation:{Label}] {held.DisplayName} ไม่ได้ต้องการบริการนี้ (ยังต้อง: {string.Join(", ", held.RemainingSteps())})");
            return;
        }

        carrier.Consume();
        PlaceOnStation(held);

        if (mode == ServiceMode.Holding)
        {
            State = StationState.Ready; // โต๊ะพักไม่ต้องทำงาน วางแล้วอุ้มกลับได้เลย
            Debug.Log($"[CareStation:{Label}] วาง {held.DisplayName} พักไว้แล้ว");
            return;
        }

        progress = 0f;
        State = StationState.Working;
        GameLog.Push($"วาง {held.DisplayName} ไว้ที่ {Label} (~{processSeconds / InteractSpeed:F1} วิ) ไปทำอย่างอื่นก่อนได้");
    }

    /// <summary>สถานี DropOff ทำงานเสร็จ — เช็คขั้นตอนหรือจ่ายทิป</summary>
    private void FinishDropOffWork()
    {
        Pet pet = OccupyingPet;
        if (pet == null) return;

        if (pet.TryComplete(step))
        {
            OnStepApplied(pet);
            GameLog.Push($"{Label} เสร็จแล้ว → {pet.GetProgressText()}", LogTone.Good);
            return;
        }

        if (pet.TryCollectTip(step, tipAmount))
        {
            GameLog.Push($"บริการเสริมให้ {pet.DisplayName} → ทิป +{tipAmount} (รวม {pet.CollectedTip})", LogTone.Good);
        }
    }

    // ---------- โหมด Hold ----------

    public void OnHoldStart(GameObject interactor)
    {
        currentHoldFailed = false;

        PlayerCarrier carrier = interactor.GetComponent<PlayerCarrier>();
        if (carrier == null) return;

        // เช็คตั้งแต่ต้นว่าใช้สถานีนี้ได้ไหม จะได้ไม่ค้างฟรีจนครบเวลาแล้วค่อยรู้ว่าผิดสถานี
        if (GetServablePet(carrier) == null) return;

        Debug.Log($"[CareStation:{Label}] เริ่มกดค้าง... (ปล่อยระหว่าง {EffectiveMinHold:F2}–{EffectiveMaxHold:F2} วิ)");
    }

    public void OnHoldTick(GameObject interactor, float heldSeconds)
    {
        if (currentHoldFailed) return;
        if (heldSeconds <= EffectiveMaxHold) return;

        currentHoldFailed = true;
        OnHoldOvertime();
        Debug.Log($"[CareStation:{Label}] ค้างนานเกิน {EffectiveMaxHold:F2} วิ — พลาด ปล่อยแล้วเริ่มใหม่");
    }

    public void OnHoldRelease(GameObject interactor, float heldSeconds)
    {
        if (currentHoldFailed)
        {
            currentHoldFailed = false;
            return;
        }

        if (heldSeconds < EffectiveMinHold)
        {
            Debug.Log($"[CareStation:{Label}] ปล่อยเร็วไป ({heldSeconds:F2} วิ) ต้องค้างอย่างน้อย {EffectiveMinHold:F2} วิ");
            return;
        }

        PlayerCarrier carrier = interactor.GetComponent<PlayerCarrier>();
        if (carrier == null) return;

        Pet pet = GetServablePet(carrier);
        if (pet == null) return;

        ApplyStep(pet);
    }

    public void OnHoldCancel(GameObject interactor)
    {
        currentHoldFailed = false;
        Debug.Log($"[CareStation:{Label}] ออกนอกระยะกลางคัน ยกเลิกการกดค้าง");
    }

    // ---------- ส่วนกลาง ----------

    private string Label => mode == ServiceMode.Holding ? "โต๊ะพัก" : step.ToString();

    private bool CanTip(Pet pet)
    {
        return pet.IsComplete && tipAmount > 0;
    }

    /// <summary>
    /// หาสัตว์ในมือผู้เล่นที่สถานีนี้ให้บริการได้ (ใช้เฉพาะโหมด Instant/Hold ที่ผู้เล่นอุ้มไว้ตลอด)
    /// ถ้าสัตว์ดูแลครบแล้วจะจ่ายทิปให้ตรงนี้เลยแล้วคืน null
    /// </summary>
    private Pet GetServablePet(PlayerCarrier carrier)
    {
        if (!carrier.IsCarrying)
        {
            Debug.Log($"[CareStation:{Label}] ต้องอุ้มสัตว์มาด้วยถึงจะใช้สถานีได้");
            return null;
        }

        Pet pet = carrier.GetHeld<Pet>();
        if (pet == null)
        {
            Debug.Log($"[CareStation:{Label}] ของที่ถืออยู่ไม่ใช่สัตว์เลี้ยง");
            return null;
        }

        if (pet.NeedsStep(step)) return pet;

        // ดูแลครบแล้วแต่ยังพามาเข้าสถานีเพิ่ม = ลูกค้าประทับใจ ให้ทิป (สถานีละครั้ง)
        if (pet.IsComplete)
        {
            if (pet.TryCollectTip(step, tipAmount))
            {
                Debug.Log($"[CareStation:{Label}] บริการเสริมให้ {pet.DisplayName} → ทิป +{tipAmount} (รวม {pet.CollectedTip})");
            }
            else
            {
                Debug.Log($"[CareStation:{Label}] {pet.DisplayName} รับบริการเสริมที่นี่ไปแล้ว พาไปส่งคืนได้เลย");
            }

            return null;
        }

        Debug.Log($"[CareStation:{Label}] {pet.DisplayName} ไม่ได้ต้องการบริการนี้ (ยังต้อง: {string.Join(", ", pet.RemainingSteps())})");
        return null;
    }

    private void ApplyStep(Pet pet)
    {
        if (!pet.TryComplete(step))
        {
            Debug.LogWarning($"[CareStation:{Label}] TryComplete ไม่ผ่านทั้งที่เช็คแล้ว");
            return;
        }

        OnStepApplied(pet);
        GameLog.Push($"{Label} สำเร็จ → {pet.GetProgressText()}", LogTone.Good);
    }

    private void PlaceOnStation(Pet pet)
    {
        OccupyingPet = pet;
        pet.transform.SetParent(transform);
        pet.transform.position = PetRestPosition;

        // สัตว์ที่เพิ่งถูกอุ้มมาจะติดมุมของผู้เล่นมาด้วย จัดให้ตรงแนวสถานีเสมอ
        pet.transform.rotation = transform.rotation;
    }

    private void ReleaseOccupant()
    {
        if (OccupyingPet != null)
        {
            OccupyingPet.transform.SetParent(null);
        }

        OccupyingPet = null;
        State = StationState.Idle;
        progress = 0f;
    }

    /// <summary>
    /// ถ้าสถานีถูกทำลาย/ยกย้ายตอนมีสัตว์อยู่ ต้องคืนสัตว์ลงพื้น ไม่งั้นสัตว์หายไปพร้อมสถานี
    /// แล้วออเดอร์นั้นจะค้างจนมิเตอร์หมดโดยแก้อะไรไม่ได้
    /// </summary>
    private void OnDestroy()
    {
        if (OccupyingPet == null) return;

        Pet stranded = OccupyingPet;
        OccupyingPet = null;

        stranded.transform.SetParent(null);
        stranded.transform.position = transform.position;
        Debug.LogWarning($"[CareStation:{Label}] ถูกทำลายขณะมี {stranded.DisplayName} อยู่ — วางสัตว์คืนลงพื้น");
    }

    /// <summary>hook ให้สถานีเฉพาะทาง override เพิ่มเอฟเฟกต์/เสียงตอนทำสำเร็จ</summary>
    protected virtual void OnStepApplied(Pet pet) { }

    /// <summary>hook ให้สถานีเฉพาะทาง override เพิ่มบทลงโทษตอนกดค้างนานเกิน</summary>
    protected virtual void OnHoldOvertime() { }

    private void OnDrawGizmos()
    {
        if (mode == ServiceMode.Holding)
        {
            Gizmos.color = Color.gray;
        }
        else
        {
            switch (step)
            {
                case CareStep.Feed: Gizmos.color = Color.yellow; break;
                case CareStep.Bath: Gizmos.color = new Color(0.3f, 0.6f, 1f); break;
                case CareStep.Groom: Gizmos.color = Color.magenta; break;
                case CareStep.Play: Gizmos.color = Color.green; break;
                case CareStep.Rest: Gizmos.color = new Color(1f, 0.6f, 0.2f); break;
                default: Gizmos.color = Color.white; break;
            }
        }

        Gizmos.DrawWireSphere(transform.position, gizmoRadius);
    }
}
