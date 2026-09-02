using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// เก็บความคืบหน้าของ run ปัจจุบัน — สัตว์/สถานีที่ปลดล็อกแล้ว และค่าบัฟ passive ที่สะสมได้
/// ระบบอื่นอ่านตัวคูณจากที่นี่จุดเดียว จะได้ไม่ต้องกระจาย logic บัฟไปทั่วโค้ด
///
/// เป็นข้อมูลของ run ไม่ใช่ meta-progression ข้ามรอบ (ตัดออกจากสโคปแล้ว)
/// เริ่ม run ใหม่ = รีเซ็ตทั้งหมดผ่าน ResetForNewRun()
/// </summary>
public class PlayerProgress : MonoBehaviour
{
    public static PlayerProgress Instance { get; private set; }

    [Header("Starting Loadout")]
    [Tooltip("สถานีที่มีติดตัวตั้งแต่เริ่ม (นอกเหนือจากที่มากับสัตว์เริ่มต้น)")]
    [SerializeField] private List<GameObject> startingStations = new List<GameObject>();

    // ---------- ตัวคูณจากบัฟ passive ----------
    // ทุกค่าเริ่มที่ 1 = ไม่มีผล การ์ด Perk จะคูณเพิ่มเข้าไป

    public float MoveSpeedMultiplier { get; private set; } = 1f;
    public float InteractSpeedMultiplier { get; private set; } = 1f;
    public float PatienceDrainMultiplier { get; private set; } = 1f;
    public float FeeMultiplier { get; private set; } = 1f;
    public float CatchRadiusMultiplier { get; private set; } = 1f;

    /// <summary>สัตว์ที่ปลดล็อกแล้ว — IntakePoint สุ่มออเดอร์จากลิสต์นี้</summary>
    public List<PetSpecies> UnlockedSpecies { get; private set; } = new List<PetSpecies>();

    /// <summary>สถานีที่ปลดล็อกแล้ว — ใช้เป็นคลังสุ่มกล่องอุปกรณ์</summary>
    public List<GameObject> UnlockedStations { get; private set; } = new List<GameObject>();

    /// <summary>
    /// ยิงทันทีที่ปลดล็อกสถานีชนิดใหม่ — LevelInitializer มา subscribe เพื่อวางลงร้านให้ทันที
    /// ไม่ต้องรอเปลี่ยน phase ผู้เล่นจึงเห็นของที่เพิ่งเลือกโผล่มาเลย
    /// </summary>
    public event System.Action<GameObject> OnStationUnlocked;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning("[PlayerProgress] มีมากกว่า 1 ตัวในฉาก ลบตัวที่ซ้ำทิ้ง");
            Destroy(gameObject);
            return;
        }

        Instance = this;
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    /// <summary>เริ่ม run ใหม่ด้วยสัตว์ที่ผู้เล่นเลือกจากหน้าเมนู</summary>
    public void ResetForNewRun(PetSpecies startingSpecies)
    {
        MoveSpeedMultiplier = 1f;
        InteractSpeedMultiplier = 1f;
        PatienceDrainMultiplier = 1f;
        FeeMultiplier = 1f;
        CatchRadiusMultiplier = 1f;

        UnlockedSpecies.Clear();
        UnlockedStations.Clear();
        UnlockedStations.AddRange(startingStations);

        // โต๊ะพักต้องมีตั้งแต่เริ่มเสมอ ไม่งั้นผู้เล่นไม่มีที่วางสัตว์ = มือไม่ว่างแล้วตัน
        StationCatalog catalog = StationCatalog.Instance;
        if (catalog != null && catalog.HoldingTablePrefab != null)
        {
            UnlockStation(catalog.HoldingTablePrefab);
        }

        // สถานีเริ่มต้นมาจาก need ของสัตว์ที่เลือก (UnlockSpecies ปลดล็อกให้เอง)
        if (startingSpecies != null)
        {
            UnlockSpecies(startingSpecies);
        }
    }

    /// <summary>
    /// ปลดล็อกสัตว์ชนิดใหม่ พร้อมปลดล็อกสถานีที่ชนิดนั้นต้องใช้ให้อัตโนมัติ
    /// (ตามกติกา: ถ้าสัตว์ใหม่ต้องใช้สถานีที่ยังไม่มี ให้ปลดล็อกสถานีนั้นทันที)
    /// </summary>
    public void UnlockSpecies(PetSpecies species)
    {
        if (species == null) return;

        if (!UnlockedSpecies.Contains(species))
        {
            UnlockedSpecies.Add(species);
            Debug.Log($"[PlayerProgress] ปลดล็อกสัตว์ใหม่: {species.speciesName}");
        }

        UnlockStationsRequiredBy(species);
    }

    /// <summary>ปลดล็อกสถานี 1 ชิ้น (คืน false ถ้ามีอยู่แล้ว)</summary>
    public bool UnlockStation(GameObject stationPrefab)
    {
        if (stationPrefab == null) return false;
        if (UnlockedStations.Contains(stationPrefab)) return false;

        UnlockedStations.Add(stationPrefab);
        Debug.Log($"[PlayerProgress] ปลดล็อกสถานีใหม่: {stationPrefab.name}");

        OnStationUnlocked?.Invoke(stationPrefab);
        return true;
    }

    /// <summary>ตรวจว่าคลังสถานีที่มีตอนนี้ครอบคลุมขั้นตอนนี้แล้วหรือยัง</summary>
    public bool HasStationFor(CareStep step)
    {
        foreach (GameObject prefab in UnlockedStations)
        {
            if (prefab == null) continue;

            CareStation station = prefab.GetComponent<CareStation>();
            if (station == null) continue;

            // โต๊ะพักไม่ได้ให้บริการขั้นตอนไหนเลย ต้องข้ามเสมอ
            // ไม่งั้นถ้า prefab โต๊ะพักถูก duplicate มาจากสถานีอื่นจนติดค่า step มาด้วย
            // มันจะไปหลอกว่ามีสถานีนั้นแล้ว ทำให้สถานีจริงไม่ถูกปลดล็อก
            if (station.Mode == CareStation.ServiceMode.Holding) continue;

            if (station.Step == step) return true;
        }

        return false;
    }

    public void ApplyBuff(PerkType perk, float amount)
    {
        switch (perk)
        {
            case PerkType.MoveSpeed: MoveSpeedMultiplier += amount; break;
            case PerkType.InteractSpeed: InteractSpeedMultiplier += amount; break;
            case PerkType.PatienceDrain: PatienceDrainMultiplier -= amount; break; // ลดช้าลง = ตัวคูณน้อยลง
            case PerkType.ServiceFee: FeeMultiplier += amount; break;
            case PerkType.CatchRadius: CatchRadiusMultiplier += amount; break;
        }

        // กันค่าติดลบ/ศูนย์ ซึ่งจะทำให้มิเตอร์ไม่เดินเลยหรือเดินถอยหลัง
        PatienceDrainMultiplier = Mathf.Max(0.1f, PatienceDrainMultiplier);

        Debug.Log($"[PlayerProgress] ได้บัฟ {perk} +{amount:P0}");
    }

    /// <summary>
    /// หาสถานีที่สัตว์ชนิดนี้ต้องใช้แต่ยังไม่ได้ปลดล็อก แล้วปลดล็อกให้ทันที
    /// ต้องมี stationCatalog ครบทุกชนิดถึงจะหาเจอ
    /// </summary>
    private void UnlockStationsRequiredBy(PetSpecies species)
    {
        if (species.requiredSteps == null) return;

        StationCatalog catalog = StationCatalog.Instance;
        if (catalog == null)
        {
            Debug.LogWarning("[PlayerProgress] ไม่มี StationCatalog ในฉาก ปลดล็อกสถานีอัตโนมัติไม่ได้");
            return;
        }

        foreach (CareStep step in species.requiredSteps)
        {
            if (HasStationFor(step)) continue;

            GameObject prefab = catalog.GetStationPrefab(step);
            if (prefab == null)
            {
                Debug.LogWarning($"[PlayerProgress] {species.speciesName} ต้องใช้สถานี {step} แต่ StationCatalog ไม่มี prefab ให้");
                continue;
            }

            UnlockStation(prefab);
            Debug.Log($"[PlayerProgress] → ปลดล็อก {step} อัตโนมัติเพราะ {species.speciesName} ต้องใช้");
        }
    }
}
