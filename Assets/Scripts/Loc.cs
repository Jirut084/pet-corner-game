using System.Collections.Generic;
using UnityEngine;

public enum GameLanguage
{
    English,
    Thai
}

/// <summary>
/// ภาษาของข้อความที่ผู้เล่นเห็น — ค่าเริ่มต้นคือ English เปลี่ยนได้ที่หน้าตั้งค่าในเมนูหลัก (เก็บใน PlayerPrefs)
///
/// ใช้แบบคู่ข้อความในบรรทัดเดียวกัน: Loc.T("ภาษาไทย", "English") — เห็นทั้งสองภาษาตรงจุดที่ใช้ แก้ง่ายไม่ต้องไปหาในตาราง
/// ข้อมูลที่บันทึกอยู่ในฉาก/asset เป็นภาษาไทย (ชื่อสินค้า บัฟ สถานะสัตว์) แปลตอนแสดงผลด้วย Loc.Data
///
/// เปลี่ยนภาษาแล้วข้อความที่สร้างไปแล้วไม่เปลี่ยนตาม — เมนูหลักจึงโหลดฉากใหม่หลังกดเปลี่ยน
/// </summary>
public static class Loc
{
    private const string PrefKey = "lang";

    // ข้อมูลที่ถูก serialize ไว้เป็นภาษาไทยในฉาก Shop / PetStates/*.asset
    private static readonly Dictionary<string, string> DataEnglish = new Dictionary<string, string>
    {
        { "รองเท้าวิ่ง", "Running shoes" },
        { "อุปกรณ์โปร", "Pro gear" },
        { "เพลงผ่อนคลาย", "Relaxing music" },
        { "บัตรสมาชิก", "Membership card" },
        { "บัฟ", "Buff" },
        { "ขนมสัตว์", "Pet treats" },
        { "ของเล่น", "Toy" },
        { "แชมพู", "Shampoo" },
        { "สงบ", "Calm" },
        { "หนี", "Fleeing" },
        { "จนมุม", "Cornered" },
        { "สถานะ", "State" }
    };

    /// <summary>ภาษาปัจจุบัน — เป็นแค่ static field ไม่อ่าน PlayerPrefs ตรงนี้ เพราะ field initializer ของ MonoBehaviour เรียก PlayerPrefs ไม่ได้</summary>
    public static GameLanguage Current { get; private set; } = GameLanguage.English;

    public static bool IsThai => Current == GameLanguage.Thai;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Load()
    {
        Current = (GameLanguage)PlayerPrefs.GetInt(PrefKey, (int)GameLanguage.English);
    }

    public static void Set(GameLanguage language)
    {
        Current = language;
        PlayerPrefs.SetInt(PrefKey, (int)language);
        PlayerPrefs.Save();
    }

    /// <summary>เลือกข้อความตามภาษาปัจจุบัน</summary>
    public static string T(string thai, string english) => IsThai ? thai : english;

    /// <summary>แปลข้อมูลที่บันทึกเป็นภาษาไทย — ไม่มีในตารางก็คืนค่าเดิม</summary>
    public static string Data(string thai)
    {
        if (IsThai || string.IsNullOrEmpty(thai)) return thai;
        return DataEnglish.TryGetValue(thai, out string english) ? english : thai;
    }
}
