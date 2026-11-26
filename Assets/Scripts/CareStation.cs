using UnityEngine;

/// <summary>
/// สถานีดูแลสัตว์ 1 จุด — ครอบคลุมทุกสถานีด้วย class เดียวโดยตั้งค่าใน Inspector
///
/// มี 4 โหมดการทำงาน:
///   Instant  — กดครั้งเดียว สำเร็จทันที (ผู้เล่นอุ้มสัตว์ไว้ตลอด)
///   Hold     — กดค้างแล้วปล่อยในช่วงเวลาที่กำหนด ค้างนานเกินถือว่าพลาด (HUD วาดหลอดให้เห็นช่วงที่ต้องปล่อย)
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

    /// <summary>สถานีทำอะไรกับสัตว์ตัวนี้ได้บ้าง</summary>
    private enum ServeResult
    {
        None,     // ใช้ไม่ได้ (มีข้อความบอกเหตุผล)
        DoStep,   // ทำขั้นตอนของสถานีนี้ให้
        GiveTip   // สัตว์ดูแลครบแล้ว — บริการเสริมรับทิป
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
    [SerializeField] private int tipAmount = 4;

    [Header("Audio")]
    [Tooltip("0 = ได้ยินเท่ากันทั้งแผนที่ (2D), 1 = ดังตามระยะห่างจากสถานี (3D)")]
    [Range(0f, 1f)]
    [SerializeField] private float soundSpatialBlend = 0f;

    [Tooltip("ความเร็วของคลิปที่เล่นวนระหว่างสถานีทำงาน — ตั้งเกิน 1 ให้ฟังกระฉับกระเฉงขึ้น")]
    [SerializeField] private float loopPitch = 1.35f;

    [Header("Visual")]
    [Tooltip("จุดที่เท้าสัตว์จะแตะเมื่อวางบนสถานี (เช่นพื้นในกรง) — ปล่อยว่างจะวางบนผิวบนสุดของโมเดลสถานีให้เอง")]
    [SerializeField] private Transform petAnchor;

    [Header("Debug")]
    [SerializeField] private float gizmoRadius = 1f;

    private bool currentHoldFailed;

    // กดค้างรอบนี้มีสัตว์ให้ดูแล/ให้ทิปจริงไหม — ถ้าไม่มี ไม่ต้องแจ้งปล่อยเร็ว/ค้างนานเกินให้รก HUD
    private bool holdHasTarget;

    // ลำโพงประจำสถานี ตั้งให้เองตอน Awake
    private AudioSource stationAudioSource;

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

    /// <summary>เวลาที่เริ่มปล่อยปุ่มได้ (วินาทีนับจากเริ่มกดค้าง) — HUD ใช้วาดโซนสีเขียว</summary>
    public float HoldWindowStart => EffectiveMinHold;

    /// <summary>เวลาสุดท้ายที่ยังปล่อยได้ เลยจากนี้ถือว่าพลาด</summary>
    public float HoldWindowEnd => EffectiveMaxHold;

    /// <summary>กดค้างรอบนี้เลยช่วงไปแล้ว ต้องปล่อยแล้วเริ่มใหม่</summary>
    public bool HoldFailed => currentHoldFailed;

    private float InteractSpeed =>
        PlayerProgress.Instance != null ? Mathf.Max(0.1f, PlayerProgress.Instance.InteractSpeedMultiplier) : 1f;

    // บัฟ "สถานีทำงานเร็วขึ้น" ย่นเฉพาะเวลาขั้นต่ำที่ต้องกดค้าง ความกว้างของช่วงที่ต้องปล่อยคงเดิม
    // (เดิมหารทั้งสองค่า ช่วงปล่อยจึงแคบลงตามบัฟ — ซื้อบัฟแล้วกดยากขึ้นแทนที่จะง่ายขึ้น)
    private float EffectiveMinHold => minHoldSeconds / InteractSpeed;
    private float EffectiveMaxHold => EffectiveMinHold + Mathf.Max(0f, maxHoldSeconds - minHoldSeconds);

    // จุดที่เท้าสัตว์จะแตะเมื่อวางบนสถานี — ไม่ได้ใส่ petAnchor ใช้ผิวบนสุดของโมเดลสถานี
    // (เดิมใช้ตำแหน่งสถานี + 1 หน่วยตายตัว สถานีเตี้ยอย่างชามอาหารสัตว์เลยลอยสูงเหนือชามเกือบ 1 หน่วย)
    private Vector3 PetRestPosition => petAnchor != null ? petAnchor.position : VisualTopCenter();

    /// <summary>กลางผิวด้านบนของโมเดลสถานี — ไม่นับสัตว์ที่วางอยู่และประกายของเอฟเฟกต์</summary>
    private Vector3 VisualTopCenter()
    {
        Bounds bounds = default;
        bool found = false;

        foreach (Renderer part in GetComponentsInChildren<Renderer>())
        {
            if (!part.enabled || part is ParticleSystemRenderer) continue;
            if (OccupyingPet != null && part.transform.IsChildOf(OccupyingPet.transform)) continue;

            if (!found)
            {
                bounds = part.bounds;
                found = true;
            }
            else
            {
                bounds.Encapsulate(part.bounds);
            }
        }

        return found
            ? new Vector3(bounds.center.x, bounds.max.y, bounds.center.z)
            : transform.position + Vector3.up;
    }

    private void Awake()
    {
        // เอฟเฟกต์หมุนตัวสัตว์ + แสง/ประกายสีตามสถานี ใส่ให้อัตโนมัติ ไม่ต้องแก้ prefab ทุกตัว
        // อยากปรับค่า ให้ Add Component "CareStationEffects" ลง prefab เองแล้วตั้งใน Inspector
        if (GetComponent<CareStationEffects>() == null)
        {
            gameObject.AddComponent<CareStationEffects>();
        }

        // ลำโพงประจำสถานี ใช้เล่นเสียงวนระหว่างทำงาน (คนละตัวกับ SFX กลางของ AudioManager)
        // ใส่ให้เองทุกสถานี จะได้ไม่ต้องไล่เพิ่ม AudioSource ทีละ prefab
        stationAudioSource = GetComponent<AudioSource>();
        if (stationAudioSource == null)
        {
            stationAudioSource = gameObject.AddComponent<AudioSource>();
        }

        stationAudioSource.playOnAwake = false;
        stationAudioSource.loop = true;
        stationAudioSource.spatialBlend = soundSpatialBlend;
    }

    private void Update()
    {
        // สัตว์บนสถานีถูกทำลายจากที่อื่น (เช่นออเดอร์หลุดเพราะมิเตอร์หมด) — คืนสถานีให้ว่าง
        // ไม่งั้นสถานีจะค้างสถานะ Working/Ready ทั้งที่ไม่มีอะไรอยู่ ใช้ต่อไม่ได้ทั้งวัน
        if (State != StationState.Idle && OccupyingPet == null)
        {
            State = StationState.Idle;
            progress = 0f;
            StopStationLoop();
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
        ServeResult result = EvaluateCarriedPet(carrier, out Pet pet, out string reason);
        Serve(result, pet, reason);
    }

    /// <summary>โหมดวางทิ้งไว้: กดตอนมือว่าง = อุ้มสัตว์บนสถานีขึ้นมา / กดตอนถือสัตว์ = วางลง</summary>
    private void HandleDropOffOrHolding(PlayerCarrier carrier)
    {
        // มีสัตว์อยู่บนสถานี → พยายามอุ้มขึ้นมา
        if (OccupyingPet != null)
        {
            if (carrier.IsCarrying)
            {
                GameLog.Push($"มือไม่ว่าง — วางของที่ถืออยู่ก่อนถึงจะอุ้ม {OccupyingPet.DisplayName} จาก{Label}ได้", LogTone.Warn);
                return;
            }

            if (mode == ServiceMode.DropOff && State == StationState.Working)
            {
                GameLog.Push($"{Label}กำลังดูแล {OccupyingPet.DisplayName} อยู่ ({progress:P0}) รออีกนิด", LogTone.Warn);
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
            GameLog.Push($"{Label}ว่างอยู่ — อุ้มสัตว์มาวางได้");
            return;
        }

        Pet held = carrier.GetHeld<Pet>();
        if (held == null)
        {
            GameLog.Push("ของที่ถืออยู่ไม่ใช่สัตว์เลี้ยง วางที่สถานีไม่ได้", LogTone.Warn);
            return;
        }

        // โต๊ะพักรับได้เสมอ ส่วนสถานีบริการต้องตรงกับที่สัตว์ต้องการ หรือเป็นบริการเสริมที่ยังไม่เคยได้ทิป
        // (เดิมรับสัตว์ที่ได้ทิปจากสถานีนี้ไปแล้วด้วย ผู้เล่นเสียเวลารอสถานีทำงานฟรีโดยไม่ได้อะไร)
        if (mode == ServiceMode.DropOff && EvaluatePet(held, out string reason) == ServeResult.None)
        {
            GameLog.Push(reason, LogTone.Warn);
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
        StartStationSound();
        GameLog.Push($"วาง {held.DisplayName} ไว้ที่ {Label} (~{processSeconds / InteractSpeed:F1} วิ) ไปทำอย่างอื่นก่อนได้");
    }

    /// <summary>สถานี DropOff ทำงานเสร็จ — เช็คขั้นตอนหรือจ่ายทิป</summary>
    private void FinishDropOffWork()
    {
        StopStationLoop();

        Pet pet = OccupyingPet;
        if (pet == null) return;

        if (pet.TryComplete(step))
        {
            OnStepApplied(pet);
            AudioManager.Instance?.PlayTaskDone();
            GameLog.Push($"{Label} เสร็จแล้ว → {pet.GetProgressText()}", LogTone.Good);
            return;
        }

        if (pet.TryCollectTip(step, tipAmount))
        {
            AudioManager.Instance?.PlayTaskDone();
            GameLog.Push($"บริการเสริมให้ {pet.DisplayName} → ทิป +{tipAmount} (รวม {pet.CollectedTip})", LogTone.Good);
        }
    }

    // ---------- โหมด Hold ----------

    public void OnHoldStart(GameObject interactor)
    {
        currentHoldFailed = false;

        // แค่เช็คว่ากดค้างแล้วจะมีผลไหม — ยังไม่ทำขั้นตอนหรือจ่ายทิปจนกว่าจะปล่อยปุ่มในช่วงที่ถูกต้อง
        // (เดิมจ่ายทิปตั้งแต่ตอนแตะปุ่ม เอาสัตว์ที่ดูแลครบแล้วมาแตะ E ที่อ่างก็ได้ทิปฟรี)
        PlayerCarrier carrier = interactor.GetComponent<PlayerCarrier>();
        holdHasTarget = EvaluateCarriedPet(carrier, out _, out string reason) != ServeResult.None;

        if (!holdHasTarget)
        {
            GameLog.Push(reason, LogTone.Warn);
            return;
        }

        // เริ่มเสียงตั้งแต่กดลง ให้รู้ว่ากำลังทำอยู่ — เสียงไม่เกี่ยวกับการจ่ายทิป จ่ายตอนปล่อยเหมือนเดิม
        StartStationSound();
    }

    public void OnHoldTick(GameObject interactor, float heldSeconds)
    {
        if (!holdHasTarget || currentHoldFailed) return;
        if (heldSeconds <= EffectiveMaxHold) return;

        currentHoldFailed = true;
        OnHoldOvertime();
        StopStationLoop();
        GameLog.Push($"{Label}: ค้างนานเกิน — ปล่อยปุ่มแล้วลองใหม่ (ปล่อยตอนเข็มอยู่ในช่วงสีเขียว)", LogTone.Warn);
    }

    public void OnHoldRelease(GameObject interactor, float heldSeconds)
    {
        StopStationLoop();

        bool hadTarget = holdHasTarget;
        bool failed = currentHoldFailed;

        holdHasTarget = false;
        currentHoldFailed = false;

        if (!hadTarget || failed) return;

        if (heldSeconds < EffectiveMinHold)
        {
            GameLog.Push($"{Label}: ปล่อยเร็วไป — กดปุ่มค้างไว้จนเข็มเข้าช่วงสีเขียวแล้วค่อยปล่อย", LogTone.Warn);
            return;
        }

        // ประเมินใหม่ตอนปล่อย เผื่อของในมือเปลี่ยนไประหว่างกดค้าง
        PlayerCarrier carrier = interactor.GetComponent<PlayerCarrier>();
        ServeResult result = EvaluateCarriedPet(carrier, out Pet pet, out string reason);
        Serve(result, pet, reason);
    }

    public void OnHoldCancel(GameObject interactor)
    {
        StopStationLoop();

        if (holdHasTarget && !currentHoldFailed)
        {
            GameLog.Push($"{Label}: เดินออกห่างระหว่างกดค้าง — ยกเลิก ลองใหม่ได้เลย", LogTone.Warn);
        }

        holdHasTarget = false;
        currentHoldFailed = false;
    }

    /// <summary>
    /// กดค้างที่สถานีนี้ตอนนี้แล้วจะมีผลไหม (สัตว์ในมือต้องการขั้นตอนนี้ หรือยังรับทิปจากสถานีนี้ได้)
    /// ไม่มีผลข้างเคียง — HUD เรียกทุกเฟรมเพื่อตัดสินใจโชว์ป้าย "กดค้าง" และหลอดเวลา
    /// </summary>
    public bool CanServeByHold(PlayerCarrier carrier)
    {
        return UseHold && EvaluateCarriedPet(carrier, out _, out _) != ServeResult.None;
    }

    // ---------- ส่วนกลาง ----------

    private string Label => mode == ServiceMode.Holding ? "โต๊ะพัก" : CareStepStyle.LabelOf(step);

    /// <summary>ชื่อสถานีภาษาไทย ใช้แสดงบนหน้าต่างซูม</summary>
    public string DisplayLabel => Label;

    /// <summary>ประเมินสัตว์ในมือผู้เล่น (ใช้กับโหมด Instant/Hold ที่ผู้เล่นอุ้มไว้ตลอด) — ไม่มีผลข้างเคียง</summary>
    private ServeResult EvaluateCarriedPet(PlayerCarrier carrier, out Pet pet, out string reason)
    {
        pet = null;

        if (carrier == null || !carrier.IsCarrying)
        {
            reason = $"ต้องอุ้มสัตว์มาที่{Label}ถึงจะใช้ได้";
            return ServeResult.None;
        }

        pet = carrier.GetHeld<Pet>();
        if (pet == null)
        {
            reason = "ของที่ถืออยู่ไม่ใช่สัตว์เลี้ยง";
            return ServeResult.None;
        }

        return EvaluatePet(pet, out reason);
    }

    /// <summary>สถานีนี้ทำอะไรให้สัตว์ตัวนี้ได้ — ไม่มีผลข้างเคียง ใช้ร่วมกันทุกโหมด</summary>
    private ServeResult EvaluatePet(Pet pet, out string reason)
    {
        reason = null;

        if (pet.NeedsStep(step)) return ServeResult.DoStep;

        // ดูแลครบแล้วแต่ยังพามาเข้าสถานีเพิ่ม = ลูกค้าประทับใจ ให้ทิป (สถานีละครั้งต่อสัตว์ 1 ตัว)
        if (pet.IsComplete)
        {
            if (tipAmount > 0 && !pet.HasTippedAt(step)) return ServeResult.GiveTip;

            reason = tipAmount > 0
                ? $"{pet.DisplayName} รับบริการเสริมที่{Label}ไปแล้ว — พาไปส่งคืนได้เลย"
                : $"{pet.DisplayName} ดูแลครบแล้ว — พาไปส่งคืนได้เลย";
            return ServeResult.None;
        }

        string remaining = string.Join(", ", pet.RemainingSteps().ConvertAll(CareStepStyle.LabelOf));
        reason = $"{pet.DisplayName} ไม่ได้ต้องการ{Label} (ยังต้อง: {remaining})";
        return ServeResult.None;
    }

    /// <summary>ทำตามผลประเมิน — ทำขั้นตอน / จ่ายทิป / แจ้งเหตุผลที่ใช้ไม่ได้</summary>
    private void Serve(ServeResult result, Pet pet, string reason)
    {
        switch (result)
        {
            case ServeResult.DoStep:
                ApplyStep(pet);
                break;

            case ServeResult.GiveTip:
                if (pet.TryCollectTip(step, tipAmount))
                {
                    AudioManager.Instance?.PlayTaskDone();
                    GameLog.Push($"บริการเสริมให้ {pet.DisplayName} → ทิป +{tipAmount} (รวม {pet.CollectedTip})", LogTone.Good);
                }
                break;

            default:
                GameLog.Push(reason, LogTone.Warn);
                break;
        }
    }

    private void ApplyStep(Pet pet)
    {
        if (!pet.TryComplete(step))
        {
            Debug.LogWarning($"[CareStation:{Label}] TryComplete ไม่ผ่านทั้งที่เช็คแล้ว");
            return;
        }

        OnStepApplied(pet);
        AudioManager.Instance?.PlayTaskDone();
        GameLog.Push($"{Label} สำเร็จ → {pet.GetProgressText()}", LogTone.Good);
    }

    private void PlaceOnStation(Pet pet)
    {
        OccupyingPet = pet;
        pet.transform.SetParent(transform);

        // สัตว์ที่เพิ่งถูกอุ้มมาจะติดมุมของผู้เล่นมาด้วย จัดให้ตรงแนวสถานีเสมอ (หมุนก่อนวัดขอบเขตตัวสัตว์)
        pet.transform.rotation = transform.rotation;

        // วางให้ขอบล่างของโมเดลสัตว์ (เท้า) แตะจุดพัก ไม่ใช่ pivot ของ prefab
        // pivot สัตว์อยู่กลางตัว ถ้าวาง pivot ตรงผิวสถานี ครึ่งล่างของตัวจะจมลงไปในสถานี
        Vector3 rest = PetRestPosition;
        pet.transform.position = rest;
        float pivotAboveFeet = pet.transform.position.y - pet.VisualBounds.min.y;
        pet.transform.position = rest + Vector3.up * pivotAboveFeet;

        // เปิดหน้าต่างซูมให้เห็นสัตว์ขณะอยู่ในสถานี (โต๊ะพักถูกกรองออกที่ตัว overlay)
        StationCloseupOverlay.Show(this, pet);
    }

    /// <summary>
    /// สัตว์หนีออกไปเอง (PetEscape) — เคลียร์ช่องให้สถานีกลับมาว่าง
    /// ไม่เคลียร์ สถานีจะคิดว่ายังมีสัตว์อยู่แล้ววางตัวใหม่ไม่ได้
    /// </summary>
    public void ReleaseForEscape()
    {
        if (OccupyingPet == null) return;
        ReleaseOccupant();
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

        StopStationLoop();
        StationCloseupOverlay.Hide(this);
    }

    /// <summary>
    /// ถ้าสถานีถูกทำลาย/ยกย้ายตอนมีสัตว์อยู่ ต้องคืนสัตว์ลงพื้น ไม่งั้นสัตว์หายไปพร้อมสถานี
    /// แล้วออเดอร์นั้นจะค้างจนมิเตอร์หมดโดยแก้อะไรไม่ได้
    /// </summary>
    private void OnDestroy()
    {
        StationCloseupOverlay.Hide(this);

        if (OccupyingPet == null) return;

        Pet stranded = OccupyingPet;
        OccupyingPet = null;

        stranded.transform.SetParent(null);
        stranded.transform.position = transform.position;
        Debug.LogWarning($"[CareStation:{Label}] ถูกทำลายขณะมี {stranded.DisplayName} อยู่ — วางสัตว์คืนลงพื้น");
    }

    // ---------- เสียงประจำสถานี ----------

    /// <summary>
    /// เริ่มเสียงตอนสถานีลงมือทำงาน
    /// อ่างอาบน้ำยิงคลิปครั้งเดียว (กดค้างแป๊บเดียว เสียงวนจะรัวจนฟังไม่รู้เรื่อง)
    /// สถานีที่เหลือเล่นวนจนกว่าจะเสร็จหรือยกเลิก
    /// </summary>
    private void StartStationSound()
    {
        AudioManager audio = AudioManager.Instance;
        if (audio == null) return;

        if (step == CareStep.Bath)
        {
            audio.PlayShower();
            return;
        }

        AudioClip loopClip = null;
        switch (step)
        {
            case CareStep.Feed: loopClip = audio.EatClip; break;
            case CareStep.Play: loopClip = audio.PlayToyClip; break;
            case CareStep.Groom: loopClip = audio.CuttingHairClip; break;
            case CareStep.Rest: loopClip = audio.SleepClip; break;
        }

        if (loopClip == null || stationAudioSource == null) return;

        // ส่งเข้ากลุ่ม SFX ของ mixer — ตั้งตอนเล่น ไม่ใช่ Awake เพราะสถานีในฉากอาจตื่นก่อน AudioManager
        if (audio.SfxGroup != null) stationAudioSource.outputAudioMixerGroup = audio.SfxGroup;
        else stationAudioSource.volume = audio.SfxVolume;

        // บัฟ "ใช้สถานีเร็วขึ้น" ย่นเวลาทำงาน เร่ง pitch ตามไปด้วยเสียงจะได้จบพร้อมงาน
        stationAudioSource.pitch = loopPitch * InteractSpeed;
        stationAudioSource.clip = loopClip;
        stationAudioSource.Play();
    }

    /// <summary>หยุดเสียงวน เรียกได้เสมอ ไม่ต้องเช็คก่อนว่ากำลังเล่นอยู่ไหม</summary>
    private void StopStationLoop()
    {
        if (stationAudioSource == null || !stationAudioSource.isPlaying) return;

        stationAudioSource.Stop();
        stationAudioSource.pitch = 1f;
    }

    /// <summary>hook ให้สถานีเฉพาะทาง override เพิ่มเอฟเฟกต์/เสียงตอนทำสำเร็จ</summary>
    protected virtual void OnStepApplied(Pet pet) { }

    /// <summary>hook ให้สถานีเฉพาะทาง override เพิ่มบทลงโทษตอนกดค้างนานเกิน</summary>
    protected virtual void OnHoldOvertime() { }

    private void OnDrawGizmos()
    {
        Gizmos.color = mode == ServiceMode.Holding ? Color.gray : CareStepStyle.ColorOf(step);
        Gizmos.DrawWireSphere(transform.position, gizmoRadius);
    }
}
