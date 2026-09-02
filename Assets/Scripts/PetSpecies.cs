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

    [Header("Order")]
    [Tooltip("รายการขั้นตอนที่ต้องทำให้ครบ — เป็น checklist ไม่มีลำดับ ทำอันไหนก่อนก็ได้")]
    public CareStep[] requiredSteps = new CareStep[] { CareStep.Feed };

    [Header("Patience")]
    [Tooltip("เวลาที่ลูกค้ารอได้ (วินาที) เริ่มนับตั้งแต่ผู้เล่นกดรับออเดอร์ที่จุด Intake " +
             "ช่วงที่เหมาะสมคือ 56–86 วิ ตามที่เทียบกับ Paw Parlor แล้ว")]
    public float patienceSeconds = 60f;

    [Header("Economy")]
    [Tooltip("ค่าบริการฐานของชนิดนี้ ค่าจริงจะบวกเพิ่มตามจำนวนขั้นตอนที่ทำสำเร็จ")]
    public int baseFee = 40;

    [Tooltip("ค่าบริการที่ได้เพิ่มต่อ 1 ขั้นตอนที่ทำสำเร็จ")]
    public int feePerStep = 15;

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
