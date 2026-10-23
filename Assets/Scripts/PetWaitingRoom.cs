using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// ห้องรอรับสัตว์ข้างเคาน์เตอร์ — ที่พักของสัตว์ที่รับออเดอร์มาแล้วแต่ผู้เล่นยังว่างไม่พอจะอุ้ม
///
/// แก้คอขวดที่ใหญ่ที่สุดของ loop เดิม: มือไม่ว่าง = รับออเดอร์ไม่ได้ = ลูกค้ายืนค้างบังคิวจนหมดความอดทน
/// ตอนนี้รับออเดอร์ได้เสมอถ้าห้องยังมีที่ว่าง สัตว์ไปรออยู่ในห้อง ผู้เล่นค่อยมาอุ้มตามลำดับที่ตัวเองไหว
///
/// ต่างจากโต๊ะพัก (CareStation โหมด Holding) ตรงที่ห้องนี้ "มีคนดูแล" — สัตว์ในห้องจะไม่หนี
/// ส่วนโต๊ะพักกลางร้านเป็นที่วางแบบไม่มีคนดู ทิ้งไว้นานแล้วสัตว์หนีได้ (ดู PetEscape)
/// มิเตอร์ความอดทนยังเดินอยู่ทั้งสองที่ ห้องนี้จึงไม่ใช่ที่ดองสัตว์ฟรีๆ
///
/// ไม่มีในฉากก็ได้ — IntakePoint จะสร้างให้อัตโนมัติข้างเคาน์เตอร์ฝั่งผู้เล่น
/// อยากได้โมเดลจริง: ใส่ component นี้ลง GameObject ที่มีโมเดลเอง แล้ววางไว้ในฉาก
/// </summary>
public class PetWaitingRoom : MonoBehaviour, IInteractable
{
    public static PetWaitingRoom Instance { get; private set; }

    [Header("Slots")]
    [Tooltip("รับสัตว์รอได้กี่ตัวพร้อมกัน")]
    [Min(1)]
    [SerializeField] private int capacity = 3;

    [Tooltip("ระยะห่างระหว่างช่อง (เรียงตามแกน X ของตัวเอง)")]
    [SerializeField] private float slotSpacing = 0.9f;

    /// <summary>สัตว์ที่รออยู่ตอนนี้ เรียงตามลำดับที่เข้ามา</summary>
    private readonly List<Pet> waiting = new List<Pet>();

    public int Count => waiting.Count;
    public int Capacity => capacity;
    public bool HasSpace => waiting.Count < capacity;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning($"[PetWaitingRoom] มีห้องรอมากกว่าหนึ่งในฉาก ใช้ {Instance.name} ตัวเดิมต่อ");
            return;
        }

        Instance = this;
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    private void Update()
    {
        // สัตว์ถูกอุ้มไป / ออเดอร์หลุดจนสัตว์ถูกทำลาย — คืนช่องให้ว่าง
        waiting.RemoveAll(pet => pet == null || pet.IsCarried || pet.transform.parent != transform);
    }

    /// <summary>ตำแหน่งกลางของช่องที่ index (เรียงจากซ้ายไปขวาโดยมีจุดกึ่งกลางที่ตัวห้อง)</summary>
    public Vector3 SlotPosition(int index)
    {
        float offset = (index - (capacity - 1) * 0.5f) * slotSpacing;
        return transform.position + transform.rotation * new Vector3(offset, 0f, 0f);
    }

    /// <summary>
    /// รับสัตว์เข้ามารอในห้อง — คืน false ถ้าเต็ม
    /// เรียกจาก IntakePoint ตอนผู้เล่นมือไม่ว่าง
    /// </summary>
    public bool Accept(Pet pet)
    {
        if (pet == null || !HasSpace) return false;

        Vector3 slot = SlotPosition(waiting.Count);

        pet.transform.SetParent(transform);
        pet.transform.rotation = transform.rotation;

        // วางให้เท้าแตะพื้นห้อง ไม่ใช่เอา pivot (ซึ่งอยู่กลางตัว) ไปวางทับพื้น
        pet.transform.position = slot;
        float pivotAboveFeet = pet.transform.position.y - pet.VisualBounds.min.y;
        pet.transform.position = slot + Vector3.up * pivotAboveFeet;

        Rigidbody body = pet.GetComponent<Rigidbody>();
        if (body != null) body.isKinematic = true;

        waiting.Add(pet);
        return true;
    }

    /// <summary>ผู้เล่นกด E ที่ห้อง — อุ้มสัตว์ตัวที่ใกล้ที่สุดออกไป</summary>
    public void Interact(GameObject interactor)
    {
        PlayerCarrier carrier = interactor != null ? interactor.GetComponent<PlayerCarrier>() : null;
        if (carrier == null) return;

        if (waiting.Count == 0)
        {
            GameLog.Push("ห้องรอว่างอยู่ ไม่มีสัตว์ให้อุ้ม", LogTone.Warn);
            return;
        }

        if (carrier.IsCarrying)
        {
            GameLog.Push("มือไม่ว่าง วางของที่ถืออยู่ก่อนถึงจะอุ้มสัตว์จากห้องรอได้", LogTone.Warn);
            return;
        }

        Pet nearest = NearestTo(interactor.transform.position);
        if (nearest == null) return;

        nearest.transform.SetParent(null);

        if (!carrier.Carry(nearest.gameObject)) return;

        waiting.Remove(nearest);
        GameLog.Push($"อุ้ม {nearest.DisplayName} ออกจากห้องรอ — เหลือรออีก {waiting.Count} ตัว");
    }

    private Pet NearestTo(Vector3 position)
    {
        Pet best = null;
        float bestDistance = float.MaxValue;

        foreach (Pet pet in waiting)
        {
            if (pet == null) continue;

            float distance = (pet.transform.position - position).sqrMagnitude;
            if (distance >= bestDistance) continue;

            bestDistance = distance;
            best = pet;
        }

        return best;
    }

    private void OnDrawGizmos()
    {
        // ช่องว่าง = เขียว, ช่องที่มีสัตว์รออยู่ = ส้ม
        for (int i = 0; i < capacity; i++)
        {
            Gizmos.color = i < waiting.Count ? new Color(1f, 0.6f, 0.2f) : new Color(0.4f, 0.85f, 0.5f);
            Gizmos.DrawWireCube(SlotPosition(i) + Vector3.up * 0.1f, new Vector3(slotSpacing * 0.8f, 0.2f, 0.8f));
        }
    }
}
