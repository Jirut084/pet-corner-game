using UnityEngine;

/// <summary>
/// แทนสัตว์เลี้ยง 1 ตัวในโลกเกม (ใน Prototype ใช้ Cube/Capsule แทนโมเดลจริง)
/// เก็บ State (Unsatisfied/Satisfied), Need (Food/Bath) และจัดการสีตามสถานะ
/// รวมถึง logic การถูก "อุ้ม/วาง" โดยผู้เล่น
/// </summary>
[RequireComponent(typeof(Renderer))]
public class Pet : MonoBehaviour
{
    public enum PetState
    {
        Unsatisfied,
        Satisfied
    }

    public enum PetNeed
    {
        Food,
        Bath
    }

    [Header("Pet Data")]
    [Tooltip("ความต้องการของสัตว์ตัวนี้ ถูกกำหนดตอน Spawn จาก IntakePoint")]
    [SerializeField] private PetNeed need;

    [Tooltip("สถานะปัจจุบันของสัตว์ เริ่มต้นเป็น Unsatisfied เสมอ")]
    [SerializeField] private PetState state = PetState.Unsatisfied;

    [Header("Visual")]
    [Tooltip("Renderer ของสัตว์ ใช้เปลี่ยนสีตาม Need/State")]
    [SerializeField] private Renderer petRenderer;

    // สีสำหรับแต่ละ Need x State
    // Food: เหลือง (ยังไม่พอใจ) -> เหลืองสว่าง (พอใจแล้ว)
    // Bath: ฟ้า (ยังไม่พอใจ) -> Cyan/ฟ้าสว่าง (พอใจแล้ว)
    private static readonly Color FoodUnsatisfiedColor = Color.yellow;
    private static readonly Color FoodSatisfiedColor = new Color(1f, 1f, 0.4f); // เหลืองสว่าง
    private static readonly Color BathUnsatisfiedColor = Color.blue;
    private static readonly Color BathSatisfiedColor = Color.cyan;

    private Collider petCollider;
    private Rigidbody petRigidbody;

    /// <summary>ความต้องการปัจจุบันของสัตว์ (อ่านอย่างเดียวจากภายนอก)</summary>
    public PetNeed Need => need;

    /// <summary>สถานะปัจจุบันของสัตว์ (อ่านอย่างเดียวจากภายนอก)</summary>
    public PetState State => state;

    /// <summary>สัตว์กำลังถูกอุ้มอยู่หรือไม่</summary>
    public bool IsCarried { get; private set; }

    private void Awake()
    {
        // ดึง component ที่จำเป็นถ้ายังไม่ได้ผูกใน Inspector
        if (petRenderer == null)
        {
            petRenderer = GetComponent<Renderer>();
        }

        petCollider = GetComponent<Collider>();
        petRigidbody = GetComponent<Rigidbody>();
    }

    /// <summary>
    /// เรียกจาก IntakePoint ทันทีหลัง Spawn เพื่อสุ่ม/กำหนด Need ให้สัตว์ตัวนี้
    /// อัปเดตสีตาม Need ที่กำหนดทันที (สถานะเริ่มต้นเป็น Unsatisfied เสมอ)
    /// </summary>
    public void SetNeed(PetNeed newNeed)
    {
        need = newNeed;
        state = PetState.Unsatisfied;
        UpdateVisual();
    }

    /// <summary>
    /// เรียกจาก NeedZone เมื่อสัตว์ได้รับการตอบสนอง Need ตรงกับโซนแล้ว
    /// เปลี่ยน State เป็น Satisfied และอัปเดตสีเป็นสีสว่าง
    /// </summary>
    public void SetSatisfied()
    {
        if (state == PetState.Satisfied) return; // กันการเรียกซ้ำ/เปลี่ยนสีซ้ำโดยไม่จำเป็น

        state = PetState.Satisfied;
        UpdateVisual();
    }

    /// <summary>
    /// ผู้เล่น "อุ้ม" สัตว์: ทำให้เป็นลูกของ holdPoint, ปิด collider/rigidbody
    /// เพื่อไม่ให้เกิด physics glitch ระหว่างถูกถือ
    /// </summary>
    public void Pickup(Transform holdPoint)
    {
        if (holdPoint == null)
        {
            Debug.LogWarning("[Pet] Pickup ถูกเรียกโดยไม่มี holdPoint");
            return;
        }

        transform.SetParent(holdPoint);
        transform.localPosition = Vector3.zero;
        transform.localRotation = Quaternion.identity;

        if (petCollider != null) petCollider.enabled = false;

        if (petRigidbody != null)
        {
            petRigidbody.isKinematic = true;
            petRigidbody.linearVelocity = Vector3.zero; // Unity 6 ใช้ linearVelocity แทน velocity เดิม
        }

        IsCarried = true;
    }

    /// <summary>
    /// ผู้เล่น "วาง" สัตว์ลงตำแหน่งที่กำหนด: ถอดออกจาก parent
    /// และเปิด collider/rigidbody กลับคืน
    /// </summary>
    public void Drop(Vector3 position)
    {
        transform.SetParent(null);
        transform.position = position;

        if (petCollider != null) petCollider.enabled = true;

        if (petRigidbody != null)
        {
            petRigidbody.isKinematic = false;
        }

        IsCarried = false;
    }

    /// <summary>อัปเดตสี Renderer ตาม Need + State ปัจจุบัน</summary>
    private void UpdateVisual()
    {
        if (petRenderer == null) return;

        Color targetColor;

        switch (need)
        {
            case PetNeed.Food:
                targetColor = state == PetState.Satisfied ? FoodSatisfiedColor : FoodUnsatisfiedColor;
                break;
            case PetNeed.Bath:
                targetColor = state == PetState.Satisfied ? BathSatisfiedColor : BathUnsatisfiedColor;
                break;
            default:
                targetColor = Color.white;
                break;
        }

        // ใช้ MaterialPropertyBlock เพื่อเลี่ยงการสร้าง material instance ใหม่ทุกครั้ง (ดีต่อ performance)
        var block = new MaterialPropertyBlock();
        petRenderer.GetPropertyBlock(block);
        block.SetColor("_BaseColor", targetColor); // URP Lit/Unlit ใช้ property ชื่อ _BaseColor
        petRenderer.SetPropertyBlock(block);
    }
}
