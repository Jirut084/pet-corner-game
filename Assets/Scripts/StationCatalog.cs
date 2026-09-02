using System;
using UnityEngine;

/// <summary>
/// ทะเบียนกลางว่าขั้นตอนแต่ละอย่างใช้ prefab สถานีตัวไหน
/// มีไว้เพื่อให้ระบบปลดล็อกหาสถานีที่สัตว์ชนิดใหม่ต้องใช้ได้อัตโนมัติ
/// (ตามกติกา: สัตว์ใหม่ต้องใช้สถานีที่ยังไม่มี → ปลดล็อกสถานีนั้นทันที)
/// </summary>
public class StationCatalog : MonoBehaviour
{
    public static StationCatalog Instance { get; private set; }

    [Serializable]
    public class Entry
    {
        public CareStep step;
        public GameObject stationPrefab;

        [Tooltip("ปลดล็อกครั้งแรกได้กี่ชิ้น — สถานียอดฮิต (Feed/Rest/Bath) ควรให้ 2 ชิ้น " +
                 "เพื่อไม่ให้สัตว์ตัวเดียวบล็อกคิวทั้งร้าน")]
        [Min(1)]
        public int copiesOnUnlock = 1;
    }

    [Tooltip("ใส่ให้ครบทุก CareStep ที่เกมนี้ใช้ ไม่งั้นระบบปลดล็อกอัตโนมัติจะหาสถานีไม่เจอ")]
    [SerializeField] private Entry[] entries;

    [Header("Holding Table")]
    [Tooltip("โต๊ะพัก — ไม่ผูกกับ CareStep ใด ผู้เล่นได้ติดตัวตั้งแต่เริ่มเสมอเพื่อให้มีที่วางสัตว์")]
    [SerializeField] private GameObject holdingTablePrefab;

    [Min(1)]
    [SerializeField] private int holdingTableCopies = 2;

    public GameObject HoldingTablePrefab => holdingTablePrefab;
    public int HoldingTableCopies => holdingTableCopies;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning("[StationCatalog] มีมากกว่า 1 ตัวในฉาก ลบตัวที่ซ้ำทิ้ง");
            Destroy(gameObject);
            return;
        }

        Instance = this;
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    public GameObject GetStationPrefab(CareStep step)
    {
        Entry entry = GetEntry(step);
        return entry != null ? entry.stationPrefab : null;
    }

    /// <summary>ปลดล็อกสถานีชนิดนี้ครั้งแรกได้กี่ชิ้น</summary>
    public int GetCopiesOnUnlock(CareStep step)
    {
        Entry entry = GetEntry(step);
        return entry != null ? Mathf.Max(1, entry.copiesOnUnlock) : 1;
    }

    private Entry GetEntry(CareStep step)
    {
        if (entries == null) return null;

        foreach (Entry entry in entries)
        {
            if (entry != null && entry.step == step) return entry;
        }

        return null;
    }
}
