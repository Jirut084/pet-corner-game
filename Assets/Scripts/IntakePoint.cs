using UnityEngine;

/// <summary>
/// จุดรับออร์เดอร์สัตว์เลี้ยง (Intake Station)
/// ผู้เล่นกด Interact ที่จุดนี้เพื่อสุ่มสัตว์ตัวใหม่และ "อุ้ม" ขึ้นมาทันที
/// </summary>
public class IntakePoint : MonoBehaviour, IInteractable
{
    [Header("Spawn Settings")]
    [Tooltip("Prefab ของสัตว์เลี้ยง (ต้องมี component Pet ติดอยู่)")]
    [SerializeField] private GameObject petPrefab;

    [Tooltip("ตำแหน่งที่สัตว์ตัวใหม่จะปรากฏ ก่อนถูกอุ้มไปกับผู้เล่น")]
    [SerializeField] private Transform spawnPoint;

    public void Interact(GameObject interactor)
    {
        if (petPrefab == null || spawnPoint == null)
        {
            Debug.LogWarning("[IntakePoint] ยังไม่ได้ตั้งค่า petPrefab หรือ spawnPoint");
            return;
        }

        // ผู้เล่นต้องมี PetCarrier ถึงจะรับสัตว์ได้
        PetCarrier carrier = interactor.GetComponent<PetCarrier>();
        if (carrier == null)
        {
            Debug.LogWarning("[IntakePoint] ผู้เล่นไม่มี PetCarrier component");
            return;
        }

        // ถ้ามือไม่ว่าง (อุ้มสัตว์ตัวอื่นอยู่) ห้ามรับตัวใหม่
        if (carrier.IsCarrying)
        {
            Debug.Log("[IntakePoint] มือไม่ว่าง กรุณาส่งคืนสัตว์ที่ถืออยู่ก่อน");
            return;
        }

        // Spawn สัตว์ตัวใหม่
        GameObject petObj = Instantiate(petPrefab, spawnPoint.position, spawnPoint.rotation);
        Pet pet = petObj.GetComponent<Pet>();

        if (pet == null)
        {
            Debug.LogError("[IntakePoint] petPrefab ไม่มี component Pet ติดอยู่");
            Destroy(petObj);
            return;
        }

        // สุ่ม Need 50/50 ระหว่าง Food กับ Bath
        Pet.PetNeed randomNeed = Random.value < 0.5f ? Pet.PetNeed.Food : Pet.PetNeed.Bath;
        pet.SetNeed(randomNeed);

        // ให้ผู้เล่นอุ้มสัตว์ตัวนี้ทันที ปิด loop ขั้นตอนรับสัตว์
        carrier.Carry(pet);

        Debug.Log($"[IntakePoint] รับสัตว์ตัวใหม่แล้ว ต้องการ: {randomNeed}");
    }
}
