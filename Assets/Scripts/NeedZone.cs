using UnityEngine;

/// <summary>
/// จุดสนองความต้องการของสัตว์เลี้ยง (Food Station หรือ Bath Station)
/// ตรวจจับสัตว์ที่ผู้เล่นอุ้มเข้ามาในระยะ ถ้า Need ตรงกับโซนนี้ จะเปลี่ยนสถานะเป็น Satisfied อัตโนมัติ
/// ไม่ต้องกด Interact เพิ่ม เพียงแค่พาสัตว์เข้ามาในระยะก็พอ (ลด friction ของ prototype)
/// </summary>
public class NeedZone : MonoBehaviour
{
    public enum ZoneType
    {
        Food,
        Bath
    }

    [Header("Zone Settings")]
    [Tooltip("โซนนี้ตอบสนอง Need ชนิดไหน")]
    [SerializeField] private ZoneType zoneType;

    [Tooltip("รัศมีตรวจจับสัตว์รอบจุดนี้")]
    [SerializeField] private float detectionRadius = 2f;

    private void Update()
    {
        // ใช้ FindObjectsOfType แทน Physics.OverlapSphere เพราะ Pet ปิด Collider
        // ระหว่างถูกอุ้ม (ดู Pet.Pickup) จึงตรวจจับด้วยระยะห่างธรรมดาแทน
        // หมายเหตุ: วิธีนี้เหมาะกับ prototype ที่มีสัตว์ในฉากไม่กี่ตัว
        // ถ้าจำนวนสัตว์เยอะขึ้นในอนาคต ควรเปลี่ยนไปเก็บ reference จาก GameManager แทนการ Find ทุกเฟรม
        Pet[] allPets = FindObjectsByType<Pet>(FindObjectsSortMode.None);

        foreach (Pet pet in allPets)
        {
            float distance = Vector3.Distance(transform.position, pet.transform.position);
            if (distance > detectionRadius) continue;

            TrySatisfy(pet);
        }
    }

    /// <summary>ตรวจว่า Need ของสัตว์ตรงกับโซนนี้หรือไม่ ถ้าตรงและยังไม่ Satisfied ให้ตอบสนองทันที</summary>
    private void TrySatisfy(Pet pet)
    {
        bool needMatches =
            (zoneType == ZoneType.Food && pet.Need == Pet.PetNeed.Food) ||
            (zoneType == ZoneType.Bath && pet.Need == Pet.PetNeed.Bath);

        if (!needMatches) return;
        if (pet.State == Pet.PetState.Satisfied) return; // ตอบสนองไปแล้ว ไม่ต้องทำซ้ำ

        pet.SetSatisfied();
        Debug.Log($"[NeedZone:{zoneType}] สัตว์ได้รับการตอบสนอง Need เรียบร้อย");
    }

    // แสดง wire sphere สีตาม ZoneType ใน Scene view เพื่อ debug ตำแหน่ง/รัศมีจุดสนองความต้องการ
    private void OnDrawGizmos()
    {
        Gizmos.color = zoneType == ZoneType.Food ? Color.yellow : Color.cyan;
        Gizmos.DrawWireSphere(transform.position, detectionRadius);
    }
}
