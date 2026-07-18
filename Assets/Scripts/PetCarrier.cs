using UnityEngine;

/// <summary>
/// ติดตั้งบน Player GameObject: ทำหน้าที่เก็บสถานะว่าผู้เล่นกำลัง "อุ้ม" สัตว์ตัวไหนอยู่
/// เป็น script เสริมที่จำเป็นสำหรับให้ IntakePoint / ReturnZone รู้ว่าผู้เล่นถืออะไรอยู่
/// (ไม่ได้อยู่ใน 4 script หลักที่ขอมา แต่ต้องมีเพื่อให้ระบบ Pickup/Drop ทำงานได้ครบวงจร
/// ถ้าโปรเจกต์มี PlayerController ของตัวเองอยู่แล้ว สามารถย้าย field/method เหล่านี้เข้าไปรวมได้)
/// </summary>
public class PetCarrier : MonoBehaviour
{
    [Header("Carry Settings")]
    [Tooltip("ตำแหน่งที่สัตว์จะไปอยู่ตอนถูกอุ้ม (ควรเป็นจุดหน้า/บนตัวผู้เล่น)")]
    [SerializeField] private Transform holdPoint;

    /// <summary>สัตว์ที่กำลังถูกอุ้มอยู่ในขณะนี้ (null ถ้าไม่ได้อุ้มอะไร)</summary>
    public Pet CurrentPet { get; private set; }

    /// <summary>ผู้เล่นกำลังอุ้มสัตว์อยู่หรือไม่</summary>
    public bool IsCarrying => CurrentPet != null;

    /// <summary>ตำแหน่งอ้างอิงสำหรับ Pickup (เปิดให้ script อื่นอ่านได้ เผื่อใช้ต่อ)</summary>
    public Transform HoldPoint => holdPoint;

    /// <summary>
    /// สั่งให้ผู้เล่นอุ้มสัตว์ตัวที่ระบุ (คืนค่า false ถ้ามืออุ้มอยู่แล้ว)
    /// </summary>
    public bool Carry(Pet pet)
    {
        if (pet == null || IsCarrying) return false;

        pet.Pickup(holdPoint);
        CurrentPet = pet;
        return true;
    }

    /// <summary>
    /// ปล่อยสัตว์ที่อุ้มอยู่ลงตำแหน่งที่กำหนด และคืนค่า reference ของสัตว์ตัวนั้นออกไป
    /// (ใช้สำหรับ ReturnZone ที่ต้อง Destroy สัตว์ต่อ)
    /// </summary>
    public Pet Release(Vector3 dropPosition)
    {
        if (!IsCarrying) return null;

        Pet released = CurrentPet;
        released.Drop(dropPosition);
        CurrentPet = null;
        return released;
    }
}
