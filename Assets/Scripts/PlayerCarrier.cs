using UnityEngine;

/// <summary>
/// ให้ของที่ถูกยกรู้ตัวว่าถูกยก/ถูกวางแล้ว (optional — ของที่ไม่ต้องรู้ก็ไม่ต้องใส่)
/// PlayerCarrier จัดการ parent + physics ให้เองอยู่แล้ว interface นี้มีไว้ให้ของแต่ละชนิด
/// อัปเดตสถานะภายในของตัวเอง เช่น Pet ใช้ปรับค่า IsCarried
/// </summary>
public interface ICarryable
{
    void OnPickedUp(PlayerCarrier carrier);
    void OnDropped(PlayerCarrier carrier);
}

/// <summary>
/// ติดตั้งบน Player: เก็บว่าผู้เล่นกำลังถืออะไรอยู่ — ถือได้ครั้งละ 1 ชิ้นเท่านั้น
/// ใช้ร่วมกันทั้งสัตว์เลี้ยง (Pet) และกล่องอุปกรณ์ (EquipmentParcel)
///
/// แทนที่ PetCarrier เดิมที่รับได้เฉพาะ Pet โดยย้ายงาน parent/ปิด physics
/// มารวมไว้ที่เดียวแทนที่จะกระจายอยู่ในตัวของที่ถูกถือแต่ละชนิด
/// </summary>
public class PlayerCarrier : MonoBehaviour
{
    [Header("Carry Settings")]
    [Tooltip("ตำแหน่งที่ของจะไปอยู่ตอนถูกถือ (ควรเป็นจุดหน้า/บนตัวผู้เล่น)")]
    [SerializeField] private Transform holdPoint;

    /// <summary>ของที่กำลังถืออยู่ (null ถ้ามือว่าง)</summary>
    public GameObject HeldObject { get; private set; }

    /// <summary>ผู้เล่นมีของอยู่ในมือหรือไม่</summary>
    public bool IsCarrying => HeldObject != null;

    public Transform HoldPoint => holdPoint;

    // เก็บสถานะ physics เดิมของที่ถืออยู่ ไว้คืนค่าตอนวางลง
    private Collider[] heldColliders;
    private Rigidbody heldRigidbody;
    private bool cachedIsKinematic;
    private bool cachedUseGravity;

    /// <summary>
    /// ยกของขึ้นมาถือ คืน false ถ้ามือไม่ว่าง / ไม่ได้ตั้ง holdPoint / target เป็น null
    /// ผู้เรียกต้องเช็คค่าที่คืนกลับเสมอ อย่าถือว่าสำเร็จโดยอัตโนมัติ
    /// </summary>
    public bool Carry(GameObject target)
    {
        if (target == null) return false;

        if (holdPoint == null)
        {
            Debug.LogError("[PlayerCarrier] ยังไม่ได้ผูก holdPoint ใน Inspector จึงยกของไม่ได้");
            return false;
        }

        if (IsCarrying)
        {
            Debug.Log($"[PlayerCarrier] มือไม่ว่าง กำลังถือ {HeldObject.name} อยู่");
            return false;
        }

        HeldObject = target;

        // ปิด collider ทั้งหมด (รวม child) กันไม่ให้ชนกับตัวผู้เล่น/พื้นจนกระเด็นระหว่างถือ
        heldColliders = target.GetComponentsInChildren<Collider>();
        foreach (Collider col in heldColliders)
        {
            col.enabled = false;
        }

        heldRigidbody = target.GetComponent<Rigidbody>();
        if (heldRigidbody != null)
        {
            cachedIsKinematic = heldRigidbody.isKinematic;
            cachedUseGravity = heldRigidbody.useGravity;

            heldRigidbody.linearVelocity = Vector3.zero; // Unity 6 ใช้ linearVelocity แทน velocity เดิม
            heldRigidbody.angularVelocity = Vector3.zero;
            heldRigidbody.isKinematic = true;
            heldRigidbody.useGravity = false;
        }

        target.transform.SetParent(holdPoint);
        target.transform.localPosition = Vector3.zero;
        target.transform.localRotation = Quaternion.identity;

        target.GetComponent<ICarryable>()?.OnPickedUp(this);

        PlayCarryAudio(target);
        return true;
    }

    /// <summary>
    /// วางของที่ถืออยู่ลงตำแหน่งที่กำหนด พร้อมคืนค่า physics กลับเป็นเหมือนเดิม
    /// คืน reference ของสิ่งที่วาง (null ถ้ามือว่างอยู่แล้ว)
    /// </summary>
    public GameObject Release(Vector3 dropPosition)
    {
        if (!IsCarrying) return null;

        GameObject released = HeldObject;

        released.transform.SetParent(null);
        released.transform.position = dropPosition;

        if (heldColliders != null)
        {
            foreach (Collider col in heldColliders)
            {
                if (col != null) col.enabled = true;
            }
        }

        if (heldRigidbody != null)
        {
            heldRigidbody.isKinematic = cachedIsKinematic;
            heldRigidbody.useGravity = cachedUseGravity;
        }

        released.GetComponent<ICarryable>()?.OnDropped(this);

        PlayCarryAudio(released);

        ClearHeldState();
        return released;
    }

    /// <summary>
    /// เอาของออกจากมือโดย "ไม่" คืนค่า physics — ใช้ตอนของชิ้นนั้นกำลังจะถูกทำลายหรือแปลงเป็นอย่างอื่น
    /// เช่น กล่องอุปกรณ์ที่ถูกวางลงกริดแล้วกลายเป็นสถานีจริง (ดู TilePlacer)
    /// </summary>
    public GameObject Consume()
    {
        if (!IsCarrying) return null;

        GameObject taken = HeldObject;
        taken.transform.SetParent(null);
        taken.GetComponent<ICarryable>()?.OnDropped(this);

        ClearHeldState();
        return taken;
    }

    /// <summary>อ่าน component ที่ต้องการจากของที่ถืออยู่ (null ถ้ามือว่างหรือของชิ้นนั้นไม่มี component นี้)</summary>
    public T GetHeld<T>() where T : Component
    {
        return HeldObject != null ? HeldObject.GetComponent<T>() : null;
    }

    /// <summary>
    /// เสียงตอนหยิบ/วาง แยกเป็นสัตว์เลี้ยงกับเฟอร์นิเจอร์
    /// ใช้ GetComponentInChildren เพราะบาง prefab วาง script Pet ไว้ในโมเดลลูก ไม่ได้อยู่ที่ root
    /// </summary>
    private void PlayCarryAudio(GameObject target)
    {
        if (AudioManager.Instance == null || target == null) return;

        if (target.GetComponentInChildren<Pet>() != null)
        {
            AudioManager.Instance.PlayPickupPet();
        }
        else
        {
            AudioManager.Instance.PlayPickupFurniture();
        }
    }

    private void ClearHeldState()
    {
        HeldObject = null;
        heldColliders = null;
        heldRigidbody = null;
    }
}
