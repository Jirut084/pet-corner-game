using UnityEngine;

/// <summary>
/// กล่องอุปกรณ์ที่ถูกส่งมาถึงร้าน — เป็นวัตถุจริงในฉากที่ผู้เล่นต้องเดินไปยกเอง
/// ข้างในเก็บ prefab ของ "สถานีจริง" ที่จะเกิดขึ้นเมื่อนำกล่องไปวางลงช่องกริด (ดู TilePlacer)
///
/// GameObject ที่ใช้ต้องมี Collider ติดอยู่ ไม่งั้น InteractionManager จะตรวจไม่เจอ
/// (ไม่ใส่ RequireComponent เพราะ Collider เป็น abstract type ที่ Unity เพิ่มให้อัตโนมัติไม่ได้)
/// </summary>
public class EquipmentParcel : MonoBehaviour, IInteractable
{
    [Header("Contents")]
    [Tooltip("สถานีจริงที่จะเกิดขึ้นเมื่อวางกล่องนี้ลงบนกริด")]
    [SerializeField] private GameObject stationPrefab;

    [Tooltip("ชื่อที่ใช้แสดงใน Console ตอน debug (ปล่อยว่างจะใช้ชื่อ prefab แทน)")]
    [SerializeField] private string displayName;

    /// <summary>Prefab ของสถานีข้างในกล่อง (TilePlacer อ่านค่านี้ไปใช้ตอนวาง)</summary>
    public GameObject StationPrefab => stationPrefab;

    public string DisplayName => string.IsNullOrEmpty(displayName)
        ? (stationPrefab != null ? stationPrefab.name : name)
        : displayName;

    /// <summary>กำหนดของข้างในกล่องตอน spawn — LevelInitializer เรียกหลังสุ่มได้อุปกรณ์แล้ว</summary>
    public void SetContents(GameObject prefab, string label = null)
    {
        stationPrefab = prefab;
        displayName = label;
    }

    public void Interact(GameObject interactor)
    {
        PlayerCarrier carrier = interactor.GetComponent<PlayerCarrier>();
        if (carrier == null)
        {
            Debug.LogWarning("[EquipmentParcel] ผู้เล่นไม่มี PlayerCarrier component");
            return;
        }

        // กันไม่ให้ยกกล่องเปล่าที่ไม่มีของข้างใน เพราะยกไปก็วางลงกริดไม่ได้อยู่ดี
        if (stationPrefab == null)
        {
            Debug.LogWarning($"[EquipmentParcel] {name} ยังไม่ได้ตั้งค่า stationPrefab ยกไปก็วางไม่ได้");
            return;
        }

        if (carrier.Carry(gameObject))
        {
            Debug.Log($"[EquipmentParcel] ยกกล่อง {DisplayName} ขึ้นมาแล้ว");
        }
    }
}
