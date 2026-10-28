using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>สินค้า 1 ชนิดในตู้ — ชื่อ ราคาที่ลูกค้าจ่ายเพิ่ม และสีของกล่องที่ผู้เล่นถือ</summary>
[Serializable]
public class GoodsItem
{
    public string label = "ขนมสัตว์";

    [Tooltip("เงินที่ได้เพิ่มเมื่อส่งของให้ลูกค้าที่สั่งไว้")]
    [Min(0)]
    public int bonusPay = 12;

    public Color color = new Color(0.90f, 0.62f, 0.25f);
}

/// <summary>ของที่ผู้เล่นถืออยู่ระหว่างเดินไปส่ง — สร้างตอน runtime ไม่ต้องมี prefab</summary>
public class CarriedGood : MonoBehaviour
{
    public GoodsItem Item { get; private set; }

    public void Initialize(GoodsItem item)
    {
        Item = item;
        name = $"สินค้า: {item.label}";
    }
}

/// <summary>
/// ตู้สินค้าในร้าน — ลูกค้าบางคนสั่งสินค้าเพิ่มตอนรับออเดอร์ ผู้เล่นเลือกได้ว่าจะไปหยิบมาส่งไหม
///
/// เป็น "ทางเลือก" ไม่ใช่ "ภาระ": ได้เงินเพิ่มแลกกับเวลาที่เสียไป ซึ่งไปเร่งมิเตอร์ของทุกคน
/// (อิง Extra Items ของ PlateUp ที่ลูกค้าขอของเพิ่มระหว่างกิน)
///
/// วงจร: ตู้สินค้า (กด E หยิบ) → จุดคืน (กด E ส่ง) → ได้เงินเพิ่มทันที
/// ส่งที่จุดคืนเพราะผู้เล่นข้ามเคาน์เตอร์ไปหาลูกค้าไม่ได้ เหมือนการส่งสัตว์คืน
///
/// วางเป็นสถานีบนกริดได้ (มี GridOccupant) ย้ายที่ได้ตอนจัดร้านเหมือนสถานีอื่น
/// ไม่มีตู้ในฉาก = ระบบนี้ปิดอยู่เฉยๆ ไม่มีลูกค้าสั่งสินค้าเลย
/// </summary>
public class GoodsShelf : MonoBehaviour, IInteractable
{
    public static GoodsShelf Instance { get; private set; }

    [Header("Catalog")]
    [SerializeField]
    private List<GoodsItem> catalog = new List<GoodsItem>
    {
        new GoodsItem { label = "ขนมสัตว์", bonusPay = 12, color = new Color(0.90f, 0.62f, 0.25f) },
        new GoodsItem { label = "ของเล่น", bonusPay = 15, color = new Color(0.45f, 0.70f, 0.90f) },
        new GoodsItem { label = "แชมพู", bonusPay = 18, color = new Color(0.60f, 0.85f, 0.60f) }
    };

    [Header("Demand")]
    [Tooltip("โอกาสที่ลูกค้าแต่ละคนจะสั่งสินค้าเพิ่มตอนรับออเดอร์")]
    [Range(0f, 1f)]
    [SerializeField] private float demandChance = 0.6f;

    [Header("Carried Box")]
    [Tooltip("ขนาดกล่องสินค้าที่ผู้เล่นถือ")]
    [SerializeField] private float boxSize = 0.35f;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning($"[GoodsShelf] มีตู้สินค้ามากกว่าหนึ่งในฉาก ใช้ {Instance.name} ตัวเดิมเป็นตัวสั่งสินค้า");
            return;
        }

        Instance = this;
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    /// <summary>
    /// สุ่มสินค้าให้ลูกค้าหนึ่งคนตอนรับออเดอร์ — คืน null ถ้ารอบนี้ไม่สั่งอะไร
    /// IntakePoint เรียกผ่าน GoodsShelf.Instance ไม่มีตู้ในฉากก็ไม่มีใครสั่ง
    /// </summary>
    public GoodsItem RollDemand()
    {
        if (catalog.Count == 0) return null;
        if (UnityEngine.Random.value > demandChance) return null;

        return catalog[UnityEngine.Random.Range(0, catalog.Count)];
    }

    /// <summary>
    /// กด E ที่ตู้ — หยิบสินค้าที่ "มีคนสั่งค้างอยู่" ให้เลย ไม่ต้องให้ผู้เล่นมานั่งเลือกเอง
    /// (ถ้ามีหลายคนสั่ง หยิบของคนที่มิเตอร์เหลือน้อยที่สุดก่อน)
    /// </summary>
    public void Interact(GameObject interactor)
    {
        PlayerCarrier carrier = interactor != null ? interactor.GetComponent<PlayerCarrier>() : null;
        if (carrier == null) return;

        // ถือสินค้าอยู่ = เก็บคืนเข้าตู้ ไม่งั้นลูกค้าที่สั่งเดินออกไปแล้วจะทำให้มือค้างถือกล่องไปตลอด
        if (carrier.GetHeld<CarriedGood>() != null)
        {
            GameObject held = carrier.Consume();
            if (held != null) Destroy(held);
            GameLog.Push("เก็บสินค้าคืนเข้าตู้แล้ว");
            return;
        }

        if (carrier.IsCarrying)
        {
            GameLog.Push("มือไม่ว่าง วางของที่ถืออยู่ก่อนถึงจะหยิบสินค้าได้", LogTone.Warn);
            return;
        }

        Customer customer = CustomerManager.Instance != null ? CustomerManager.Instance.MostUrgentGoodsRequest() : null;
        if (customer == null)
        {
            GameLog.Push("ยังไม่มีลูกค้าสั่งสินค้า", LogTone.Warn);
            return;
        }

        GameObject box = CreateBox(customer.WantedGood);
        if (!carrier.Carry(box))
        {
            Destroy(box);
            return;
        }

        GameLog.Push($"หยิบ {customer.WantedGood.label} — เอาไปส่งที่จุดคืน (+{customer.WantedGood.bonusPay})");
    }

    private GameObject CreateBox(GoodsItem item)
    {
        GameObject box = GameObject.CreatePrimitive(PrimitiveType.Cube);
        box.transform.position = transform.position + Vector3.up * 0.5f;
        box.transform.localScale = Vector3.one * boxSize;

        Renderer renderer = box.GetComponent<Renderer>();
        Shader lit = Shader.Find("Universal Render Pipeline/Lit");
        if (lit != null) renderer.material = new Material(lit) { color = item.color };

        Rigidbody body = box.AddComponent<Rigidbody>();
        body.isKinematic = true;

        box.AddComponent<CarriedGood>().Initialize(item);
        return box;
    }

    private void OnDrawGizmos()
    {
        Gizmos.color = new Color(0.9f, 0.7f, 0.3f);
        Gizmos.DrawWireCube(transform.position + Vector3.up * 0.3f, new Vector3(0.9f, 0.6f, 0.9f));
    }
}
