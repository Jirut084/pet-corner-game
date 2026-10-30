using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// เหตุการณ์ประจำวัน — สุ่ม 1 อย่างตอนเริ่มวัน (GameManager) แล้วปรับค่าเดิมของระบบอื่นทั้งวัน
/// run สั้นแค่ 3 วัน แต่ละวันจึงควรรู้สึกต่างกัน ไม่ใช่แค่ลูกค้าเยอะขึ้น
///
/// ทุกค่าเริ่มที่ 1 / 0 = ไม่มีผล ระบบที่อ่าน:
///   patienceDrainMultiplier → CustomerManager.PatiencePressure (ทุกมิเตอร์)
///   feeMultiplier           → ReturnZone (ค่าบริการตอนส่งคืน ไม่รวมทิป)
///   spawnGapMultiplier      → CustomerManager (ระยะห่างระหว่างลูกค้า)
///   extraOrders             → GameManager (โควตาออเดอร์ของวัน)
///   addStep / extraStep     → Pet.Initialize (ทุกตัวต้องทำขั้นตอนนี้เพิ่ม ถ้ายังไม่มีอยู่แล้ว)
/// </summary>
[Serializable]
public class DayEvent
{
    [Tooltip("ชื่อสั้นๆ โชว์บน HUD มุมซ้ายบน (ไม่เกิน ~10 ตัวอักษร)")]
    public string label = "วันปกติ";

    [Tooltip("คำอธิบายผลของเหตุการณ์ โชว์ใน log ตอนเริ่มวัน")]
    public string description = "";

    [Tooltip("มิเตอร์ความอดทนทุกตัวลดเร็วขึ้นกี่เท่า (1 = ปกติ, 1.5 = ใจร้อนขึ้น 50%)")]
    [Min(0.1f)]
    public float patienceDrainMultiplier = 1f;

    [Tooltip("ค่าบริการตอนส่งคืนคูณกี่เท่า (ไม่รวมทิป)")]
    [Min(0f)]
    public float feeMultiplier = 1f;

    [Tooltip("ระยะห่างระหว่างลูกค้าคูณกี่เท่า (0.5 = มาถี่ขึ้น 2 เท่า)")]
    [Min(0.1f)]
    public float spawnGapMultiplier = 1f;

    [Tooltip("ออเดอร์ของวันนี้ ± เท่านี้")]
    public int extraOrders = 0;

    [Tooltip("ทุกตัวต้องทำขั้นตอน Extra Step เพิ่ม — สุ่มได้เฉพาะเมื่อร้านมีสถานีขั้นตอนนั้นแล้ว")]
    public bool addStep = false;

    public CareStep extraStep = CareStep.Bath;

    /// <summary>
    /// คลังเหตุการณ์เริ่มต้น ใช้เมื่อยังไม่ได้ตั้งอะไรใน Inspector ของ GameManager
    /// ทุกเหตุการณ์มีทั้งได้และเสีย ไม่มีวันที่แย่ล้วน
    /// </summary>
    public static List<DayEvent> BuildDefaultPool()
    {
        return new List<DayEvent>
        {
            new DayEvent
            {
                label = "ฝนตก", description = "ทุกตัวต้องอาบน้ำเพิ่ม (ได้ค่าบริการขั้นตอนเพิ่มด้วย)",
                addStep = true, extraStep = CareStep.Bath
            },
            new DayEvent
            {
                label = "ชั่วโมงเร่งด่วน", description = "ลูกค้ามาถี่ขึ้น 2 เท่า แต่จ่าย +20%",
                spawnGapMultiplier = 0.5f, feeMultiplier = 1.2f
            },
            new DayEvent
            {
                label = "ลูกค้า VIP", description = "ใจร้อนขึ้น 50% แต่จ่าย +75%",
                patienceDrainMultiplier = 1.5f, feeMultiplier = 1.75f
            },
            new DayEvent
            {
                label = "วันหยุดยาว", description = "ออเดอร์ +2 แต่ลูกค้าใจเย็นขึ้น 25%",
                extraOrders = 2, patienceDrainMultiplier = 0.75f
            }
        };
    }
}
