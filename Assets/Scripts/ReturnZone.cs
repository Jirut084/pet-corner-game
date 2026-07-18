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

    [Header("Payout Settings")]
    [Tooltip("เงินที่ได้เมื่อสัตว์ถูกตอบสนอง Need แล้ว (Satisfied)")]
    [SerializeField] private int fullPayout = 100;

    [Tooltip("เงินที่ได้เมื่อสัตว์ยังไม่ถูกตอบสนอง Need (Unsatisfied) - หักครึ่งเป็น penalty")]
    [SerializeField] private int penaltyPayout = 50;

    // เก็บเงินรวมสะสมข้ามหลายรอบของ loop แบบง่ายที่สุด (static ตามที่ระบุไว้)
    // หมายเหตุ: ถ้าโปรเจกต์ขยายใหญ่ขึ้นในอนาคต แนะนำย้ายไปเป็น EconomyManager แยกต่างหาก
    // เพื่อไม่ให้ ReturnZone ทำหน้าที่เกินขอบเขตของตัวเอง (Single Responsibility)
    public static int TotalWallet { get; private set; } = 0;

    public void Interact(GameObject interactor)
    {
        PetCarrier carrier = interactor.GetComponent<PetCarrier>();
        if (carrier == null || !carrier.IsCarrying)
        {
            Debug.Log("[ReturnZone] ไม่มีสัตว์ในมือ ไม่สามารถส่งคืนได้");
            return;
        }

        Pet pet = carrier.CurrentPet;

        // คำนวณ payout ตามสถานะของสัตว์ ณ ตอนส่งคืน
        int earned = pet.State == Pet.PetState.Satisfied ? fullPayout : penaltyPayout;
        TotalWallet += earned;

        Debug.Log($"Earned: {earned} | Total Wallet: {TotalWallet}");

        // ปล่อยสัตว์ออกจากมือผู้เล่นแล้วทำลายทิ้ง เพื่อปิดวงจร 1 รอบของ Core Loop
        Pet releasedPet = carrier.Release(transform.position);
        if (releasedPet != null)
        {
            Destroy(releasedPet.gameObject);
        }
    }

    // แสดง wire sphere ใน Scene view เพื่อ debug ตำแหน่ง/รัศมีจุดส่งคืน
    private void OnDrawGizmos()
    {
        Gizmos.color = Color.green;
        Gizmos.DrawWireSphere(transform.position, detectionRadius);
    }
}
