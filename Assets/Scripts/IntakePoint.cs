using UnityEngine;

/// <summary>
/// เคาน์เตอร์รับออเดอร์ (Intake Station)
/// ผู้เล่นกด Interact ตอนมีลูกค้ายืนรออยู่ที่เคาน์เตอร์ เพื่อรับสัตว์ของลูกค้าคนนั้น
/// สัตว์จะปรากฏขึ้นและถูกอุ้มทันที ส่วนลูกค้าจะเดินไปนั่งรอที่จุดรอ
///
/// ชนิดสัตว์มาจากลูกค้า ไม่ได้สุ่มที่นี่แล้ว (CustomerManager เป็นคนสุ่มตอน spawn ลูกค้า)
/// จุดยืนของลูกค้าผูกกับเคาน์เตอร์นี้ (CustomerStandPosition) ย้ายเคาน์เตอร์แล้วลูกค้าเดินตามไปเอง
/// </summary>
public class IntakePoint : MonoBehaviour, IInteractable
{
    [Header("Spawn Settings")]
    [Tooltip("Prefab สัตว์สำรอง — ใช้เฉพาะเมื่อ PetSpecies ของลูกค้ายังไม่ได้ใส่ petPrefab ของตัวเอง")]
    [SerializeField] private GameObject petPrefab;

    [Tooltip("ตำแหน่งที่สัตว์ตัวใหม่จะปรากฏ ก่อนถูกอุ้มไปกับผู้เล่น")]
    [SerializeField] private Transform spawnPoint;

    [Header("Customer Counter")]
    [Tooltip("จุดที่ลูกค้ายืนรอหน้าเคาน์เตอร์ — ควรเป็นลูกของ IntakePoint จะได้ย้ายตามกันเสมอ " +
             "ปล่อยว่างจะใช้ตำแหน่ง IntakePoint + Customer Stand Offset แทน")]
    [SerializeField] private Transform customerStandPoint;

    [Tooltip("ระยะจากเคาน์เตอร์ไปจุดยืนของลูกค้า หมุนตามทิศของ IntakePoint (ใช้เมื่อไม่ได้ใส่ Customer Stand Point)")]
    [SerializeField] private Vector3 customerStandOffset = new Vector3(0f, 0f, 1.5f);

    /// <summary>ตำแหน่งยืนหน้าเคาน์เตอร์ของลูกค้า — CustomerManager อ่านค่านี้ทุกครั้งที่ส่งลูกค้าเข้าร้าน</summary>
    public Vector3 CustomerStandPosition =>
        customerStandPoint != null
            ? customerStandPoint.position
            : transform.position + transform.rotation * customerStandOffset;

    public void Interact(GameObject interactor)
    {
        if (spawnPoint == null)
        {
            Debug.LogWarning("[IntakePoint] ยังไม่ได้ตั้งค่า spawnPoint");
            return;
        }

        GameManager game = GameManager.Instance;
        if (game != null && !game.CanTakeNewOrder)
        {
            if (game.Phase != GamePhase.Service)
            {
                GameLog.Push("ยังไม่ได้เปิดร้าน รับออเดอร์ไม่ได้", LogTone.Warn);
            }
            else if (game.ActiveOrderCount >= game.ConcurrentOrderLimit)
            {
                GameLog.Push($"ดูแลพร้อมกันได้แค่ {game.ConcurrentOrderLimit} ตัว ส่งคืนสักตัวก่อน", LogTone.Warn);
            }
            else
            {
                GameLog.Push("รับออเดอร์ครบโควตาของวันนี้แล้ว", LogTone.Warn);
            }

            return;
        }

        CustomerManager customers = CustomerManager.Instance;
        if (customers == null)
        {
            Debug.LogError("[IntakePoint] ไม่มี CustomerManager ในฉาก รับออเดอร์ไม่ได้");
            return;
        }

        Customer customer = customers.CustomerAtCounter();
        if (customer == null)
        {
            GameLog.Push("ยังไม่มีลูกค้าที่เคาน์เตอร์", LogTone.Warn);
            return;
        }

        PlayerCarrier carrier = interactor.GetComponent<PlayerCarrier>();
        if (carrier == null)
        {
            Debug.LogWarning("[IntakePoint] ผู้เล่นไม่มี PlayerCarrier component");
            return;
        }

        if (carrier.IsCarrying)
        {
            GameLog.Push("มือไม่ว่าง วางของที่ถืออยู่ก่อน (โต๊ะพักรับได้)", LogTone.Warn);
            return;
        }

        // ใช้หน้าตาของชนิดที่ลูกค้าสั่งมาก่อน ถ้ายังไม่ได้ใส่ค่อยใช้ prefab สำรอง
        PetSpecies requested = customer.RequestedSpecies;
        GameObject prefabToSpawn = requested != null && requested.petPrefab != null ? requested.petPrefab : petPrefab;

        if (prefabToSpawn == null)
        {
            Debug.LogWarning($"[IntakePoint] ไม่มี prefab ให้ spawn — ใส่ petPrefab ใน {requested?.name} หรือช่องสำรองของ IntakePoint");
            return;
        }

        GameObject petObj = Instantiate(prefabToSpawn, spawnPoint.position, spawnPoint.rotation);
        Pet pet = petObj.GetComponent<Pet>();

        if (pet == null)
        {
            Debug.LogError("[IntakePoint] petPrefab ไม่มี component Pet ติดอยู่");
            Destroy(petObj);
            return;
        }

        pet.Initialize(customer.RequestedSpecies);

        // จองจุดรอให้ลูกค้าก่อน ถ้าไม่มีที่นั่งว่างต้องยกเลิกทั้งออเดอร์
        // ไม่งั้นลูกค้าจะยืนค้างที่เคาน์เตอร์บังคนถัดไปตลอดวัน
        if (!customers.AssignSeat(customer, pet))
        {
            GameLog.Push("ไม่มีจุดรอว่าง รับออเดอร์ไม่ได้", LogTone.Warn);
            Destroy(petObj);
            return;
        }

        if (!carrier.Carry(petObj))
        {
            Debug.LogWarning("[IntakePoint] ยกสัตว์ขึ้นมาไม่สำเร็จ ยกเลิกการรับออเดอร์");
            Destroy(petObj);
            return;
        }

        // มิเตอร์เริ่มนับตรงนี้ — ตอนรับออเดอร์ ไม่ใช่ตอนลูกค้าเดินเข้าร้าน
        pet.StartPatience();
        game?.NotifyOrderIssued(pet);

        GameLog.Push($"รับออเดอร์ {pet.DisplayName} — ต้องทำ: {string.Join(", ", pet.RemainingSteps())} " +
                     $"(อดทน {customer.RequestedSpecies.patienceSeconds:F0} วิ)", LogTone.Good);
    }

    // เส้นสีเหลืองจากเคาน์เตอร์ไปจุดยืนลูกค้า ใช้เช็คตำแหน่งตอนย้าย IntakePoint ใน Scene view
    private void OnDrawGizmos()
    {
        Vector3 stand = CustomerStandPosition;

        Gizmos.color = Color.yellow;
        Gizmos.DrawLine(transform.position, stand);
        Gizmos.DrawWireSphere(stand, 0.4f);
    }
}
