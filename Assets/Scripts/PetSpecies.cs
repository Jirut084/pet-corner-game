using UnityEngine;

/// <summary>
/// ขั้นตอนการดูแล 1 อย่าง — ใช้ร่วมกันระหว่าง PetSpecies (สัตว์ต้องการอะไร)
/// กับ CareStation (สถานีให้บริการอะไร)
/// </summary>
public enum CareStep
{
    Feed,
    Bath,
    Groom,
    Play,
    Rest
}

/// <summary>
/// ข้อมูลสัตว์ 1 ชนิด เก็บเป็น ScriptableObject เพื่อให้จูนตัวเลขใน Inspector ได้
/// โดยไม่ต้องแก้โค้ด (สำคัญมากช่วง Sprint C ที่ต้องจูน balance ซ้ำๆ)
///
/// ค่าอ้างอิงที่แนะนำ (เทียบสัดส่วนจาก Paw Parlor แล้ว):
///   cat     — Feed + Rest          , อดทน 56 วิ
///   dog     — Bath + Feed          , อดทน 60 วิ
///   rabbit  — Groom + Feed         , อดทน 62 วิ
///   hamster — Play + Feed + Rest   , อดทน 86 วิ
/// </summary>
[CreateAssetMenu(fileName = "PetSpecies", menuName = "Pet Corner/Pet Species")]
public class PetSpecies : ScriptableObject
{
    [Header("Identity")]
    public string speciesName = "Cat";

    [Tooltip("Prefab หน้าตาของสัตว์ชนิดนี้ (ต้องมี component Pet) — ปล่อยว่างจะใช้ petPrefab สำรองของ IntakePoint")]
    public GameObject petPrefab;

    [Header("Order")]
    [Tooltip("รายการขั้นตอนที่ต้องทำให้ครบ — เป็น checklist ไม่มีลำดับ ทำอันไหนก่อนก็ได้")]
    public CareStep[] requiredSteps = new CareStep[] { CareStep.Feed };

    [Header("Patience")]
    [Tooltip("เวลาที่ลูกค้ารอได้ (วินาที) เริ่มนับตั้งแต่ผู้เล่นกดรับออเดอร์ที่จุด Intake " +
             "ช่วงที่เหมาะสมคือ 70–90 วิ (Paw Parlor 56–86 วิ, PlateUp เล่นคนเดียวให้ถึง 120 วิ)")]
    public float patienceSeconds = 75f;

    [Tooltip("เวลาที่ลูกค้ายอมยืนรอหน้าเคาน์เตอร์ก่อนผู้เล่นรับออเดอร์ (วินาที) — นับแยกจากค่าด้านบน " +
             "เริ่มนับตอนเดินมาถึงแถว (หรือตอนเริ่มต่อคิวนอกร้าน ซึ่งลดเร็วกว่า) หมดแล้วลูกค้าเดินออกพร้อมร้องเรียน " +
             "(0 = รอได้ไม่จำกัด)")]
    [Min(0f)]
    public float counterPatienceSeconds = 35f;

    [Tooltip("ดูแลครบแล้วเจ้าของลุกมายืนรอรับที่จุดส่งคืน มีเวลาให้ส่งคืนกี่วินาที (นับใหม่แทนมิเตอร์เดิม) " +
             "หมดแล้วร้องเรียนเหมือนมิเตอร์หมด (0 = ปิดเฟสนี้ มิเตอร์เดิมเดินต่อจนส่งคืน)")]
    [Min(0f)]
    public float pickupPatienceSeconds = 20f;

    [Header("Economy")]
    [Tooltip("ค่าบริการฐานของชนิดนี้ ค่าจริงจะบวกเพิ่มตามจำนวนขั้นตอนที่ทำสำเร็จ " +
             "(จูนคู่กับราคาในร้านค้าท้ายวัน: ออเดอร์ละ ~20–30 = จบวันซื้อได้ 1–2 ชิ้น ไม่ใช่ทุกชิ้น)")]
    public int baseFee = 15;

    [Tooltip("ค่าบริการที่ได้เพิ่มต่อ 1 ขั้นตอนที่ทำสำเร็จ")]
    public int feePerStep = 8;

    /// <summary>จำนวนขั้นตอนทั้งหมดของสัตว์ชนิดนี้</summary>
    public int StepCount => requiredSteps != null ? requiredSteps.Length : 0;

    /// <summary>สัตว์ชนิดนี้ต้องใช้สถานีชนิดนี้หรือไม่</summary>
    public bool Requires(CareStep step)
    {
        if (requiredSteps == null) return false;

        foreach (CareStep s in requiredSteps)
        {
            if (s == step) return true;
        }

        return false;
    }
}
