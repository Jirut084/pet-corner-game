using UnityEngine;

/// <summary>
/// สถานะหนึ่งของเครื่องจักรสถานะ (FSM) ของสัตว์เลี้ยง — เก็บเป็น ScriptableObject
///
/// ทำไมเป็น ScriptableObject: พฤติกรรมกับค่าที่ใช้จูนอยู่ในไฟล์ asset ไม่ได้ฝังในโค้ด
/// นักออกแบบปรับ "หนีเร็วแค่ไหน / ยอมให้เข้าใกล้เท่าไหร่" ได้โดยไม่ต้องคอมไพล์ใหม่
/// และสัตว์คนละชนิดใช้ชุดสถานะคนละชุดได้ (เช่นแมวขี้ตกใจใช้ FleeState ที่หนีไกลกว่า)
///
/// สัญญาของสถานะ:
///   Enter(brain)      — เรียกครั้งเดียวตอนเข้าสถานะ (ตั้งค่าเริ่มต้น / สั่ง agent)
///   Tick(brain, dt)   — เรียกทุกเฟรม คืน "สถานะถัดไป" (คืนตัวเอง = อยู่ต่อ)
///   Exit(brain)       — เรียกครั้งเดียวตอนออกจากสถานะ (เก็บกวาด)
///
/// ตัวรัน FSM คือ PetEscape (ดู FSM diagram ใน Claude outputs/)
/// </summary>
public abstract class PetState : ScriptableObject
{
    [Tooltip("ชื่อที่โชว์บน HUD และใน log ตอนดีบัก")]
    public string displayName = "สถานะ";

    public virtual void Enter(PetEscape brain) { }

    public abstract PetState Tick(PetEscape brain, float deltaTime);

    public virtual void Exit(PetEscape brain) { }
}
