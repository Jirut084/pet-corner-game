using UnityEngine;

/// <summary>
/// เอฟเฟกต์ตอนสัตว์อยู่บนสถานี — CareStation ใส่ component นี้ให้อัตโนมัติ ไม่ต้องแก้ prefab
///
///   กำลังทำงาน (Working) — สัตว์หมุนรอบตัวเอง + เด้งเบาๆ, แสงไฟสีตามสถานีกระพริบ, ประกายสีลอยขึ้น
///   ทำเสร็จ   (Ready)   — ระเบิดประกายหลากสี, หมุนช้าลง, แสงเปลี่ยนเป็นสีเขียว = มาอุ้มไปได้แล้ว
///   ยกสัตว์ออก          — ดับแสง หยุดปล่อยประกาย (ประกายที่ลอยอยู่ค่อยๆ จางหายเอง)
///
/// สีมาจาก CareStepStyle ตัวเดียวกับป้ายเหนือหัวสัตว์ ผู้เล่นจึงจับคู่สีกับสถานีได้ทันที
/// ไม่แก้สถานะเกมใดๆ แค่อ่าน CareStation แล้วขยับ transform ของสัตว์
/// ตำแหน่ง/มุมคำนวณใหม่จากค่าตอนวางทุกเฟรม (ไม่บวกสะสม) โมเดลจึงไม่ค่อยๆ ไหลออกจากจุดวาง
///
/// อยากปรับค่า: Add Component "CareStationEffects" ลง prefab สถานีเองแล้วแก้ใน Inspector
/// (CareStation จะเห็นว่ามีอยู่แล้วและไม่ใส่ซ้ำ)
/// </summary>
[DefaultExecutionOrder(-10)] // ขยับสัตว์ให้เสร็จก่อน StationCloseupOverlay เล็งกล้องซูมใน LateUpdate
[RequireComponent(typeof(CareStation))]
public class CareStationEffects : MonoBehaviour
{
    [Header("Spin")]
    [SerializeField] private bool spinPet = true;

    [Tooltip("ความเร็วหมุน (องศาต่อวินาที) ระหว่างสถานีกำลังทำงาน")]
    [SerializeField] private float spinDegreesPerSecond = 120f;

    [Tooltip("ตัวคูณความเร็วหมุนตอนทำเสร็จแล้วรอผู้เล่นมาอุ้ม (0 = หยุดหมุน)")]
    [Range(0f, 1f)]
    [SerializeField] private float readySpinMultiplier = 0.3f;

    [Tooltip("ความสูงที่เด้งขึ้นลง (หน่วยโลก) 0 = ไม่เด้ง")]
    [SerializeField] private float bobHeight = 0.08f;

    [Tooltip("จำนวนครั้งที่เด้งต่อวินาที")]
    [SerializeField] private float bobFrequency = 1.5f;

    [Tooltip("ใส่เอฟเฟกต์ให้สัตว์ที่พักบนโต๊ะพักด้วยหรือไม่ (ปกติโต๊ะพักเป็นแค่ที่วาง จึงปิดไว้)")]
    [SerializeField] private bool affectHoldingTables = false;

    [Header("Light")]
    [SerializeField] private bool useLight = true;
    [SerializeField] private float lightRange = 2.5f;
    [SerializeField] private float lightIntensityMin = 1.5f;
    [SerializeField] private float lightIntensityMax = 4f;

    [Tooltip("ความเร็วกระพริบของแสง")]
    [SerializeField] private float lightPulseSpeed = 3f;

    [Tooltip("สีแสง/ประกายตอนทำเสร็จแล้ว")]
    [SerializeField] private Color readyColor = new Color(0.45f, 0.95f, 0.55f);

    [Header("Sparkles")]
    [SerializeField] private bool useSparkles = true;

    [Tooltip("Material ของประกาย — แนะนำให้ใส่ (URP → Particles → Unlit, Surface Type = Transparent) " +
             "เพื่อให้ขึ้นใน build แน่นอน ปล่อยว่างจะใช้ shader ในตัว Unity แทน")]
    [SerializeField] private Material sparkleMaterial;

    [Tooltip("จำนวนประกายต่อวินาทีระหว่างทำงาน")]
    [SerializeField] private float sparklesPerSecond = 14f;

    [Tooltip("จำนวนประกายที่ระเบิดออกตอนทำเสร็จ")]
    [SerializeField] private int readyBurstCount = 30;

    private static readonly Color HoldingColor = new Color(0.85f, 0.85f, 0.85f);

    // material สำรองใช้ร่วมกันทุกสถานี สร้างครั้งเดียวทั้งเกม
    private static Material fallbackMaterial;
    private static bool fallbackSearched;

    private CareStation station;
    private Pet trackedPet;
    private CareStation.StationState lastState;

    // ค่าตอนวางสัตว์ลงสถานี ใช้คำนวณตำแหน่ง/มุมใหม่ทุกเฟรม
    private Vector3 basePosition;
    private Quaternion baseRotation;
    private Vector3 pivot;
    private float spinAngle;
    private float effectTime;

    private Light glow;
    private ParticleSystem sparkles;
    private bool sparklesUnavailable;

    private Color StepColor =>
        station.Mode == CareStation.ServiceMode.Holding ? HoldingColor : CareStepStyle.ColorOf(station.Step);

    private void Awake()
    {
        station = GetComponent<CareStation>();
    }

    private void OnDisable()
    {
        StopEffects();
        trackedPet = null;
    }

    private void LateUpdate()
    {
        Pet pet = station.OccupyingPet;
        bool eligible = pet != null &&
                        (affectHoldingTables || station.Mode != CareStation.ServiceMode.Holding);

        if (!eligible)
        {
            // สัตว์ถูกยกออก/ถูกทำลาย — ไม่แตะ transform ของสัตว์อีก (PlayerCarrier จัดตำแหน่งให้แล้ว)
            if (trackedPet != null) StopEffects();
            trackedPet = null;
            return;
        }

        if (pet != trackedPet) BeginTracking(pet);

        CareStation.StationState state = station.State;
        if (state == CareStation.StationState.Ready && lastState == CareStation.StationState.Working)
        {
            PlayReadyBurst();
        }

        lastState = state;
        effectTime += Time.deltaTime;

        bool ready = state == CareStation.StationState.Ready;
        Color tint = ready ? readyColor : StepColor;

        UpdateSpin(pet, ready);
        UpdateLight(tint);
        UpdateSparkles(tint, ready);
    }

    private void BeginTracking(Pet pet)
    {
        trackedPet = pet;
        basePosition = pet.transform.position;
        baseRotation = pet.transform.rotation;

        // หมุนรอบกลางโมเดล ไม่ใช่ pivot ของ prefab ที่อาจอยู่ปลายเท้าหรือมุมตัว
        Bounds bounds = pet.VisualBounds;
        pivot = bounds.center;

        spinAngle = 0f;
        effectTime = 0f;
        lastState = station.State;

        if (useSparkles && EnsureSparkles())
        {
            var shape = sparkles.shape;
            shape.radius = Mathf.Max(0.2f, bounds.extents.magnitude * 0.6f);
        }
    }

    private void UpdateSpin(Pet pet, bool ready)
    {
        if (!spinPet) return;

        float speed = ready ? spinDegreesPerSecond * readySpinMultiplier : spinDegreesPerSecond;
        spinAngle = Mathf.Repeat(spinAngle + speed * Time.deltaTime, 360f);

        // เด้งขึ้นอย่างเดียว (0 ถึง bobHeight) กันโมเดลจมลงไปในสถานี
        float bob = bobHeight > 0f
            ? (Mathf.Sin(effectTime * bobFrequency * Mathf.PI * 2f) * 0.5f + 0.5f) * bobHeight
            : 0f;

        Quaternion spin = Quaternion.AngleAxis(spinAngle, Vector3.up);
        Transform petTransform = pet.transform;
        petTransform.position = pivot + spin * (basePosition - pivot) + Vector3.up * bob;
        petTransform.rotation = spin * baseRotation;
    }

    private void UpdateLight(Color tint)
    {
        if (!useLight) return;

        if (glow == null)
        {
            GameObject lightObject = new GameObject("StationGlow");
            lightObject.transform.SetParent(transform, false);

            glow = lightObject.AddComponent<Light>();
            glow.type = LightType.Point;
            glow.shadows = LightShadows.None;
        }

        float pulse = Mathf.Sin(effectTime * lightPulseSpeed) * 0.5f + 0.5f;

        glow.enabled = true;
        glow.range = lightRange;
        glow.color = tint;
        glow.intensity = Mathf.Lerp(lightIntensityMin, lightIntensityMax, pulse);
        glow.transform.position = pivot + Vector3.up * 0.6f;
    }

    private void UpdateSparkles(Color tint, bool ready)
    {
        if (!useSparkles || !EnsureSparkles()) return;

        sparkles.transform.position = pivot;

        var main = sparkles.main;
        main.startColor = new ParticleSystem.MinMaxGradient(tint, Color.white);

        // ทำเสร็จแล้วยังมีประกายเบาๆ ต่อ สถานีที่รออุ้มจะได้ยังสะดุดตา
        var emission = sparkles.emission;
        emission.rateOverTime = ready ? sparklesPerSecond * 0.3f : sparklesPerSecond;

        if (!sparkles.isEmitting) sparkles.Play();
    }

    private void PlayReadyBurst()
    {
        if (!useSparkles || readyBurstCount <= 0 || !EnsureSparkles()) return;

        sparkles.transform.position = pivot;

        var emit = new ParticleSystem.EmitParams
        {
            applyShapeToPosition = true,
            startSize = 0.2f,
            startLifetime = 1.2f
        };

        Color[] palette = { readyColor, StepColor, Color.white };
        int perColor = Mathf.Max(1, readyBurstCount / palette.Length);

        foreach (Color color in palette)
        {
            emit.startColor = color;
            sparkles.Emit(emit, perColor);
        }
    }

    private void StopEffects()
    {
        if (glow != null) glow.enabled = false;

        if (sparkles != null && sparkles.isEmitting)
        {
            sparkles.Stop(true, ParticleSystemStopBehavior.StopEmitting);
        }
    }

    /// <summary>สร้าง ParticleSystem ครั้งแรกที่ต้องใช้ คืน false ถ้าหา material มาวาดไม่ได้</summary>
    private bool EnsureSparkles()
    {
        if (sparkles != null) return true;
        if (sparklesUnavailable) return false;

        Material material = sparkleMaterial != null ? sparkleMaterial : GetFallbackMaterial();
        if (material == null)
        {
            sparklesUnavailable = true;
            Debug.LogWarning($"[CareStationEffects] {name}: หา shader สำหรับประกายไม่เจอ — ใส่ Sparkle Material ใน Inspector");
            return false;
        }

        GameObject sparkleObject = new GameObject("StationSparkles");
        sparkleObject.transform.SetParent(transform, false);

        sparkles = sparkleObject.AddComponent<ParticleSystem>();

        // AddComponent แล้วระบบเริ่มเล่นทันที ต้องหยุดก่อนตั้งค่า
        sparkles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        var main = sparkles.main;
        main.loop = true;
        main.playOnAwake = false;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.7f, 1.2f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.3f, 0.9f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.06f, 0.16f);
        main.gravityModifier = -0.15f; // ติดลบ = ลอยขึ้น
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = 80;

        var emission = sparkles.emission;
        emission.rateOverTime = 0f;

        var shape = sparkles.shape;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = 0.35f;

        // ค่อยๆ ปรากฏแล้วจางหาย ไม่โผล่/หายวูบ
        Gradient fade = new Gradient();
        fade.SetKeys(
            new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.15f), new GradientAlphaKey(0f, 1f) });

        var colorOverLifetime = sparkles.colorOverLifetime;
        colorOverLifetime.enabled = true;
        colorOverLifetime.color = fade;

        var sizeOverLifetime = sparkles.sizeOverLifetime;
        sizeOverLifetime.enabled = true;
        sizeOverLifetime.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 1f, 1f, 0.2f));

        ParticleSystemRenderer particleRenderer = sparkleObject.GetComponent<ParticleSystemRenderer>();
        particleRenderer.renderMode = ParticleSystemRenderMode.Billboard;
        particleRenderer.sharedMaterial = material;

        return true;
    }

    /// <summary>
    /// material สำรองตอนไม่ได้ใส่ sparkleMaterial — Sprites/Default อยู่ใน Always Included Shaders ของโปรเจกต์
    /// รองรับสีต่อ particle + ความโปร่งใส และ URP วาดให้ได้ ใส่ texture จุดกลมขอบนุ่มให้ประกายไม่เป็นสี่เหลี่ยม
    /// </summary>
    private static Material GetFallbackMaterial()
    {
        if (fallbackSearched) return fallbackMaterial;
        fallbackSearched = true;

        Shader shader = Shader.Find("Sprites/Default");
        if (shader == null) shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
        if (shader == null) return null;

        fallbackMaterial = new Material(shader)
        {
            name = "StationSparkles (runtime)",
            mainTexture = CreateSoftDotTexture()
        };

        return fallbackMaterial;
    }

    private static Texture2D CreateSoftDotTexture()
    {
        const int size = 32;
        Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
        {
            name = "SparkleDot (runtime)",
            wrapMode = TextureWrapMode.Clamp
        };

        float radius = (size - 1) * 0.5f;
        Vector2 center = new Vector2(radius, radius);

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float distance = Vector2.Distance(new Vector2(x, y), center) / radius;
                float alpha = Mathf.Clamp01(1f - distance);
                texture.SetPixel(x, y, new Color(1f, 1f, 1f, alpha * alpha));
            }
        }

        texture.Apply();
        return texture;
    }
}
