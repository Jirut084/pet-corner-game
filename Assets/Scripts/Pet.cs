using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// แทนสัตว์เลี้ยง 1 ตัวในโลกเกม
///
/// ใบสั่งงานเป็น "checklist ไม่มีลำดับ" — ต้องผ่านทุกสถานีที่ระบุ แต่ทำอันไหนก่อนก็ได้
/// ผู้เล่นจึงเลือกเส้นทางเองได้ ซึ่งเป็นจุดที่ทำให้เกิดการตัดสินใจในเกม
///
/// สิ่งที่สัตว์ต้องการแสดงเป็นป้ายลอยเหนือหัว (วาดโดย GameHudOverlay) แทนการย้อมสีตัวสัตว์
/// ซึ่งเคยทำให้สีจริงของโมเดลเพี้ยน — Pet แค่บอกตำแหน่งเหนือหัวและขอบเขตของโมเดลให้
/// </summary>
public class Pet : MonoBehaviour, ICarryable
{
    [Header("Data")]
    [Tooltip("ชนิดของสัตว์ตัวนี้ ปกติถูกกำหนดตอน Spawn จาก IntakePoint")]
    [SerializeField] private PetSpecies species;

    [Header("Visual")]
    [Tooltip("Renderer ของโมเดล ใช้หาขอบเขตตัวสัตว์ ปล่อยว่างจะเก็บทุกชิ้นข้างใต้ให้อัตโนมัติ")]
    [SerializeField] private Renderer[] petRenderers;

    [Tooltip("ระยะป้ายเหนือหัวสัตว์ นับจากจุดสูงสุดของโมเดล")]
    [SerializeField] private float overheadOffset = 0.25f;

    [Header("Face (ใช้กับหน้าต่างซูม)")]
    [Tooltip("ทิศที่หน้าสัตว์หันไป ในพิกัดของตัวสัตว์ — เลือก prefab แล้วดูลูกศรสีฟ้าใน Scene view " +
             "ถ้าชี้ไปทางหาง ให้เปลี่ยนเป็น (0, 0, -1)")]
    [SerializeField] private Vector3 faceLocalDirection = Vector3.forward;

    [Tooltip("จุดกึ่งกลางใบหน้า (ไม่บังคับ) — ปล่อยว่างจะประมาณจากขนาดโมเดลให้เอง")]
    [SerializeField] private Transform faceAnchor;

    [Tooltip("ความสูงของใบหน้า คิดเป็นสัดส่วนความสูงโมเดล (0 = เท้า, 1 = ยอดหัว)")]
    [Range(0f, 1f)]
    [SerializeField] private float faceHeight = 0.65f;

    // ทะเบียนสัตว์ที่อยู่ในฉาก ให้ HUD วนวาดป้ายได้โดยไม่ต้อง Find ทุกเฟรม
    private static readonly List<Pet> activePets = new List<Pet>();

    /// <summary>สัตว์ทุกตัวที่เปิดใช้งานอยู่ในฉากตอนนี้</summary>
    public static IReadOnlyList<Pet> All => activePets;

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

    // ตอนอยู่ใน Edit mode (วาด gizmo ใน prefab) Awake ยังไม่ทำงาน จึงดึง renderer สดๆ เมื่อยังว่าง
    private Renderer[] Renderers =>
        petRenderers != null && petRenderers.Length > 0 ? petRenderers : GetComponentsInChildren<Renderer>();

    /// <summary>ขอบเขตของโมเดลในโลก ใช้วางป้ายเหนือหัวและเล็งกล้องซูม</summary>
    public Bounds VisualBounds
    {
        get
        {
            Bounds bounds = new Bounds(transform.position, Vector3.zero);
            bool found = false;

            foreach (Renderer target in Renderers)
            {
                if (target == null || !target.enabled) continue;

                if (!found)
                {
                    bounds = target.bounds;
                    found = true;
                }
                else
                {
                    bounds.Encapsulate(target.bounds);
                }
            }

            return bounds;
        }
    }

    /// <summary>ตำแหน่งเหนือหัวสัตว์ในโลก สำหรับวาดป้ายบอกสิ่งที่ต้องการ</summary>
    public Vector3 OverheadWorldPosition
    {
        get
        {
            Bounds bounds = VisualBounds;
            return new Vector3(bounds.center.x, bounds.max.y + overheadOffset, bounds.center.z);
        }
    }

    /// <summary>ทิศที่หน้าสัตว์หันไปในโลก (แนวราบเท่านั้น กล้องซูมจะมองระดับสายตา ไม่ก้มเงยตามโมเดล)</summary>
    public Vector3 FaceWorldDirection
    {
        get
        {
            Vector3 direction = transform.TransformDirection(faceLocalDirection);
            direction.y = 0f;

            if (direction.sqrMagnitude < 0.0001f)
            {
                direction = transform.forward;
                direction.y = 0f;
            }

            return direction.sqrMagnitude < 0.0001f ? Vector3.forward : direction.normalized;
        }
    }

    /// <summary>
    /// จุดกึ่งกลางใบหน้าในโลก — ใช้ faceAnchor ถ้ามี ไม่งั้นประมาณจากกล่องขอบเขตของโมเดล:
    /// ค่อนไปทางด้านหน้าตามทิศที่หันหน้า และสูงตามสัดส่วน faceHeight
    /// </summary>
    public Vector3 FaceWorldPoint
    {
        get
        {
            if (faceAnchor != null) return faceAnchor.position;

            Bounds bounds = VisualBounds;
            Vector3 direction = FaceWorldDirection;

            // ระยะจากกลางตัวไปถึงผิวด้านหน้าตามทิศที่หันหน้า
            float reach = Mathf.Abs(direction.x) * bounds.extents.x + Mathf.Abs(direction.z) * bounds.extents.z;

            Vector3 point = bounds.center + direction * reach * 0.8f;
            point.y = bounds.min.y + bounds.size.y * faceHeight;
            return point;
        }
    }

    // เลือก prefab สัตว์แล้วจะเห็นจุดหน้า + ลูกศรสีฟ้าบอกทิศที่หันหน้า ใช้เช็คก่อนกด Play
    private void OnDrawGizmosSelected()
    {
        Vector3 face = FaceWorldPoint;
        Vector3 direction = FaceWorldDirection;
        float size = Mathf.Max(0.2f, VisualBounds.size.magnitude * 0.5f);

        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(face, size * 0.08f);
        Gizmos.DrawLine(face, face + direction * size);
        Gizmos.DrawWireSphere(face + direction * size, size * 0.04f);
    }

    private void Awake()
    {
        // โมเดลจริงมักมี mesh อยู่ที่ลูกและมีหลายชิ้น จึงเก็บทุก Renderer ข้างใต้มาคิดขอบเขตรวม
        if (petRenderers == null || petRenderers.Length == 0)
        {
            petRenderers = GetComponentsInChildren<Renderer>();
        }

        // ระบบหนี/ไล่จับ ใส่ให้อัตโนมัติ ไม่ต้องแก้ prefab สัตว์ทุกตัว
        // อยากปรับค่า ให้ Add Component "PetEscape" ลง prefab เองแล้วตั้งใน Inspector
        if (GetComponent<PetEscape>() == null)
        {
            gameObject.AddComponent<PetEscape>();
        }
    }

    private void OnEnable()
    {
        if (!activePets.Contains(this)) activePets.Add(this);
    }

    private void OnDisable()
    {
        activePets.Remove(this);
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
    }

    /// <summary>ยังต้องผ่านสถานีชนิดนี้อยู่หรือไม่ (อยู่ใน checklist และยังไม่ได้ทำ)</summary>
    public bool NeedsStep(CareStep step)
    {
        if (species == null) return false;
        return species.Requires(step) && !completedSteps.Contains(step);
    }

    /// <summary>ทำขั้นตอนนี้ไปแล้วหรือยัง ใช้วาดเครื่องหมายถูกบนป้าย</summary>
    public bool HasCompleted(CareStep step)
    {
        return completedSteps.Contains(step);
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

    /// <summary>สถานีชนิดนี้เคยจ่ายทิปให้สัตว์ตัวนี้ไปแล้วหรือยัง — สถานีใช้กันรับสัตว์ซ้ำแบบไม่ได้อะไร</summary>
    public bool HasTippedAt(CareStep step)
    {
        return tippedSteps.Contains(step);
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

        // ความกดดันร่วมของร้าน: ลูกค้าค้างหลายคนพร้อมกัน / ช่วงท้ายวัน = ทุกมิเตอร์ลดเร็วขึ้นพร้อมกัน
        drainRate *= CustomerManager.PatiencePressure;

        patienceRemaining -= Time.deltaTime * drainRate;
        OnPatienceChanged?.Invoke(this, PatienceNormalized);

        if (patienceRemaining > 0f) return;

        patienceRemaining = 0f;
        patienceRunning = false; // ตั้งก่อนยิง event กัน subscriber เรียกกลับมาซ้ำ
        OnPatienceExpired?.Invoke(this);
    }

    /// <summary>ข้อความสรุปความคืบหน้าไว้แสดงใน log</summary>
    public string GetProgressText()
    {
        if (species == null) return "(ไม่มีข้อมูลสัตว์)";

        if (IsComplete) return $"{species.speciesName} [{StepsCompleted}/{TotalSteps}] พร้อมส่งคืน";

        List<string> labels = RemainingSteps().ConvertAll(CareStepStyle.LabelOf);
        return $"{species.speciesName} [{StepsCompleted}/{TotalSteps}] ยังต้อง: {string.Join(", ", labels)}";
    }

    public void OnPickedUp(PlayerCarrier carrier)
    {
        IsCarried = true;
    }

    public void OnDropped(PlayerCarrier carrier)
    {
        IsCarried = false;
    }
}
