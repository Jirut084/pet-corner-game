using System.Collections.Generic;
using UnityEngine;

/// <summary>ความสำคัญของข้อความ ใช้เลือกสีตอนวาดบนจอ</summary>
public enum LogTone
{
    Normal,
    Good,   // สำเร็จ/ได้เงิน
    Warn,   // เตือน/ทำไม่ได้
    Bad     // ร้องเรียน/ออเดอร์หลุด
}

/// <summary>
/// บันทึกเหตุการณ์กลางของเกม — เก็บไว้ท้ายสุด N รายการเพื่อวาดบนหน้าจอ
/// และส่งต่อเข้า Debug.Log ให้ด้วย จะได้ดูย้อนหลังใน Console ได้เหมือนเดิม
///
/// ทุกระบบเรียก GameLog.Push(...) แทน Debug.Log สำหรับเหตุการณ์ที่ผู้เล่นควรเห็น
/// ส่วน Debug.Log ตรงๆ เก็บไว้ใช้กับข้อความ debug ภายในที่ผู้เล่นไม่ต้องรู้
/// </summary>
public static class GameLog
{
    public struct Entry
    {
        public string text;
        public LogTone tone;
        public float time; // เวลาที่บันทึก ใช้ทำ fade
    }

    private const int MaxEntries = 8;

    private static readonly List<Entry> entries = new List<Entry>();

    public static IReadOnlyList<Entry> Entries => entries;

    /// <summary>ยิงทุกครั้งที่มีข้อความใหม่ เผื่อ UI อยากอัปเดตแบบ event-driven</summary>
    public static event System.Action<Entry> OnEntryAdded;

    public static void Push(string text, LogTone tone = LogTone.Normal)
    {
        Entry entry = new Entry { text = text, tone = tone, time = Time.unscaledTime };

        entries.Add(entry);
        if (entries.Count > MaxEntries)
        {
            entries.RemoveAt(0);
        }

        OnEntryAdded?.Invoke(entry);

        if (tone == LogTone.Bad || tone == LogTone.Warn)
        {
            Debug.LogWarning(text);
        }
        else
        {
            Debug.Log(text);
        }
    }

    /// <summary>ล้างบันทึก — เรียกตอนเริ่ม run ใหม่</summary>
    public static void Clear()
    {
        entries.Clear();
    }

    public static Color ColorFor(LogTone tone)
    {
        switch (tone)
        {
            case LogTone.Good: return new Color(0.55f, 0.9f, 0.68f);
            case LogTone.Warn: return new Color(1f, 0.83f, 0.42f);
            case LogTone.Bad: return new Color(1f, 0.5f, 0.5f);
            default: return Color.white;
        }
    }
}
