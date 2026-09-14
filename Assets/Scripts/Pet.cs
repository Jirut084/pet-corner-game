using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// แทนสัตว์เลี้ยง 1 ตัวในโลกเกม (ใน Prototype ใช้ Cube/Capsule แทนโมเดลจริง)
///
/// ใบสั่งงานเป็น "checklist ไม่มีลำดับ" — ต้องผ่านทุกสถานีที่ระบุ แต่ทำอันไหนก่อนก็ได้
/// ผู้เล่นจึงเลือกเส้นทางเองได้ ซึ่งเป็นจุดที่ทำให้เกิดการตัดสินใจในเกม
///
/// ย้อมสีตัวสัตว์ตามขั้นตอนที่ยังเหลืออันแรก เพื่อให้เทสได้โดยยังไม่มี UI
/// รองรับทั้ง cube ต้นแบบ และโมเดลจริงที่ mesh อยู่ในลูกหลายชิ้น
/// (ไม่บังคับ MeshRenderer ที่ root แล้ว เพื่อให้สลับเป็นโมเดลได้)
/// </summary>
public class Pet : MonoBehaviour, ICarryable
{
    [Header("Data")]
    [Tooltip("ชนิดของสัตว์ตัวนี้ ปกติถูกกำหนดตอน Spawn จาก IntakePoint")]
    [SerializeField] private PetSpecies species;

    [Header("Visual")]
    [Tooltip("ย้อมสีตัวสัตว์ตามขั้นตอนถัดไป — ปิดได้เมื่อมีไอคอนบอกขั้นตอนแล้ว " +
             "(สีจะคูณกับ texture ของโมเดล ทำให้สีจริงของอาร์ตเพี้ยน)")]
    [SerializeField] private bool tintByNextStep = true;

    [Tooltip("Renderer ที่จะย้อมสี ปล่อยว่างจะเก็บทุกชิ้นข้างใต้ให้อัตโนมัติ")]
    [SerializeField] private Renderer[] petRenderers;

    private static readonly Color FeedColor = Color.yellow;
    private static readonly Color BathColor = new Color(0.3f, 0.6f, 1f);
    private static readonly Color GroomColor = Color.magenta;
    private static readonly Color PlayColor = Color.green;
    private static readonly Color RestColor = new Color(1f, 0.6f, 0.2f);
    private static readonly Color DoneColor = Color.white;

    // ขั้นตอนที่ทำสำเร็จแล้ว และสถานีที่จ่ายทิปไปแล้ว (จำกัดสถานีละครั้งต่อสัตว์ 1 ตัว)
    private readonly HashSet<CareStep> completedSteps = new HashSet<CareStep>();
    private readonly HashSet<CareStep> tippedSteps = new HashSet<CareStep>();

    public PetSpecies Species => species;

    /// <summary>ชื่อไว้แสดงใน log — ปลอดภัยแม้ยังไม่ได้ Initialize (species เป็น null)</summary>
    public string DisplayName => species != null ? species.speciesName : name;

    public bool IsComplete => species == null || completedSteps.Count >= species.StepCount;
    public int StepsCompleted => completedSteps.Count;
    public int TotalSteps => species != null ? species.StepCount : 0;

    /// <summary>สัตว์กำลังถูกอุ้มอยู่หรือไม่</summary>
    public bool IsCarried { get; private set; }

    /// <summary>ทิปที่สะสมได้ระหว่างทาง จ่ายรวมทีเดียวตอนส่งคืน</summary>
    public int CollectedTip { get; private set; }

    public event Action<Pet> OnStepCompleted;
    public event Action<Pet> OnAllStepsCompleted;
    public event Action<Pet> OnPatienceExpired;
    public event Action<Pet, float> OnPatienceChanged;

    // ---------- มิเตอร์ความอดทน ----------
    // เริ่มนับตั้งแต่ผู้เล่นกดรับออเดอร์ที่ Intake (ไม่ใช่ตอน spawn) ตามที่ตกลงไว้

    private bool patienceRunning;
    private float patienceRemaining;

    public float PatienceRemaining => patienceRemaining;
    public bool IsPatienceRunning => patienceRunning;

    public float PatienceNormalized =>
        (species != null && species.patienceSeconds > 0f)
            ? Mathf.Clamp01(patienceRemaining / species.patienceSeconds)
            : 0f;

    private void Awake()
    {
        // โมเดลจริงมักมี mesh อยู่ที่ลูกและมีหลายชิ้น จึงเก็บทุก Renderer ข้างใต้มาย้อมพร้อมกัน
        if (petRenderers == null || petRenderers.Length == 0)
        {
            petRenderers = GetComponentsInChildren<Renderer>();
        }
    }

    /// <summary>เรียกจาก IntakePoint ทันทีหลัง Spawn เพื่อกำหนดชนิดและรีเซ็ตความคืบหน้า</summary>
    public void Initialize(PetSpecies newSpecies)
    {
        species = newSpecies;
        completedSteps.Clear();
        tippedSteps.Clear();
        CollectedTip = 0;
        patienceRunning = false;

        if (species == null)
        {
            Debug.LogError("[Pet] Initialize ถูกเรียกโดยไม่มี PetSpecies");
            return;
        }

        if (species.StepCount == 0)
        {
            Debug.LogWarning($"[Pet] {species.speciesName} ไม่มีขั้นตอนใน requiredSteps เลย จะนับว่าเสร็จทันที");
        }

        UpdateVisual();
    }

    /// <summary>ยังต้องผ่านสถานีชนิดนี้อยู่หรือไม่ (อยู่ใน checklist และยังไม่ได้ทำ)</summary>
    public bool NeedsStep(CareStep step)
    {
        if (species == null) return false;
        return species.Requires(step) && !completedSteps.Contains(step);
    }

    /// <summary>ขั้นตอนที่ยังเหลือทั้งหมด</summary>
    public List<CareStep> RemainingSteps()
    {
        List<CareStep> remaining = new List<CareStep>();
        if (species == null || species.requiredSteps == null) return remaining;

        foreach (CareStep step in species.requiredSteps)
        {
            if (!completedSteps.Contains(step)) remaining.Add(step);
        }

        return remaining;
    }

    /// <summary>
    /// ทำขั้นตอนที่ระบุให้สำเร็จ — สำเร็จถ้าอยู่ใน checklist และยังไม่เคยทำ (ไม่สนลำดับ)
    /// </summary>
    public bool TryComplete(CareStep step)
    {
        if (!NeedsStep(step)) return false;

        completedSteps.Add(step);
        UpdateVisual();

        OnStepCompleted?.Invoke(this);

        if (IsComplete)
        {
            OnAllStepsCompleted?.Invoke(this);
        }

        return true;
    }

    /// <summary>
    /// พยายามรับทิปจากสถานีที่สัตว์ไม่ได้ต้องการ
    /// ได้เฉพาะเมื่อทำครบทุกขั้นตอนแล้ว และสถานีชนิดนั้นยังไม่เคยให้ทิปกับสัตว์ตัวนี้
    /// </summary>
    public bool TryCollectTip(CareStep step, int amount)
    {
        if (!IsComplete) return false;
        if (!tippedSteps.Add(step)) return false; // Add คืน false ถ้ามีอยู่แล้ว

        CollectedTip += amount;
        return true;
    }

    // ---------- มิเตอร์ ----------

    public void StartPatience()
    {
        if (species == null)
        {
            Debug.LogWarning("[Pet] StartPatience ถูกเรียกก่อน Initialize");
            return;
        }

        patienceRemaining = species.patienceSeconds;
        patienceRunning = true;
    }

    public void StopPatience()
    {
        patienceRunning = false;
    }

    private void Update()
    {
        if (!patienceRunning) return;

        // บัฟ "มิเตอร์ลดช้าลง" คูณตรงนี้จุดเดียว
        float drainRate = PlayerProgress.Instance != null
            ? PlayerProgress.Instance.PatienceDrainMultiplier
            : 1f;

        patienceRemaining -= Time.deltaTime * drainRate;
        OnPatienceChanged?.Invoke(this, PatienceNormalized);

        if (patienceRemaining > 0f) return;

        patienceRemaining = 0f;
        patienceRunning = false; // ตั้งก่อนยิง event กัน subscriber เรียกกลับมาซ้ำ
        OnPatienceExpired?.Invoke(this);
    }

    /// <summary>ข้อความสรุปความคืบหน้าไว้ debug/แสดงบน ticket UI ชั่วคราว</summary>
    public string GetProgressText()
    {
        if (species == null) return "(ไม่มีข้อมูลสัตว์)";

        if (IsComplete) return $"{species.speciesName} [{StepsCompleted}/{TotalSteps}] พร้อมส่งคืน";

        return $"{species.speciesName} [{StepsCompleted}/{TotalSteps}] ยังต้อง: {string.Join(", ", RemainingSteps())}";
    }

    public void OnPickedUp(PlayerCarrier carrier)
    {
        IsCarried = true;
    }

    public void OnDropped(PlayerCarrier carrier)
    {
        IsCarried = false;
    }

    /// <summary>เปลี่ยนสีตามขั้นตอนที่เหลืออันแรก เพื่อให้ผู้เล่นรู้คร่าวๆ ว่ายังต้องไปไหน</summary>
    private void UpdateVisual()
    {
        if (!tintByNextStep || petRenderers == null || petRenderers.Length == 0) return;

        Color targetColor = DoneColor;
        List<CareStep> remaining = RemainingSteps();

        if (remaining.Count > 0)
        {
            switch (remaining[0])
            {
                case CareStep.Feed: targetColor = FeedColor; break;
                case CareStep.Bath: targetColor = BathColor; break;
                case CareStep.Groom: targetColor = GroomColor; break;
                case CareStep.Play: targetColor = PlayColor; break;
                case CareStep.Rest: targetColor = RestColor; break;
            }
        }

        // ใช้ MaterialPropertyBlock เพื่อเลี่ยงการสร้าง material instance ใหม่ทุกครั้ง (ดีต่อ performance)
        // URP Lit คูณ _BaseColor กับ texture — ขาว = สีจริงของอาร์ต, สีอื่น = ย้อมทับ
        var block = new MaterialPropertyBlock();
        foreach (Renderer target in petRenderers)
        {
            if (target == null) continue;

            target.GetPropertyBlock(block);
            block.SetColor("_BaseColor", targetColor);
            target.SetPropertyBlock(block);
        }
    }
}
