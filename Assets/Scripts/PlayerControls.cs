using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>อุปกรณ์ที่ผู้เล่นแต่ละช่องใช้ — Off = ไม่เข้าร่วม</summary>
public enum ControlDevice
{
    Off,
    Keyboard,
    Gamepad
}

/// <summary>
/// ปุ่มของผู้เล่นแต่ละคน (สูงสุด 4 คน) สำหรับเล่นหลายคนในเครื่องเดียว
///
/// แต่ละช่องมี InputActionMap ของตัวเอง ผูกกับอุปกรณ์เดียว (คีย์บอร์ด หรือจอยตัวที่ N)
/// จึงเล่นได้ทั้ง คีย์บอร์ดคู่ (แบ่งปุ่มกันคนละฝั่ง) / จอยคู่ / คีย์บอร์ด + จอย
/// ใช้แค่ 2 action ที่เกมใช้จริง: Move (เดิน) และ Interact (ปุ่ม E เดิม กดครั้งเดียว/กดค้าง)
///
/// ตั้งค่าที่หน้า "ปุ่มควบคุม" ในเมนูหลัก — อุปกรณ์และปุ่มที่เปลี่ยนเก็บไว้ใน PlayerPrefs
/// ปุ่มรวมของเกม (Enter เปิดร้าน, Esc หยุดเกม, 1-9 เลือกของ) ยังอ่านผ่าน GameInput เหมือนเดิม
/// </summary>
public static class PlayerControls
{
    public const int MaxPlayers = 4;

    /// <summary>สีประจำผู้เล่น ใช้กับวงใต้เท้าและหัวคอลัมน์ในหน้าตั้งปุ่ม</summary>
    public static readonly Color[] PlayerColors =
    {
        new Color(0.95f, 0.55f, 0.20f),
        new Color(0.30f, 0.62f, 0.95f),
        new Color(0.40f, 0.78f, 0.40f),
        new Color(0.85f, 0.40f, 0.80f)
    };

    /// <summary>ชื่อช่องที่เปลี่ยนปุ่มได้ เรียงตามที่โชว์ในหน้าตั้งปุ่ม</summary>
    public static readonly string[] KeyboardEntries = { "Up", "Down", "Left", "Right", "Interact" };
    public static readonly string[] GamepadEntries = { "Interact" };

    // ปุ่มเริ่มต้นของคีย์บอร์ดแต่ละช่อง แบ่งคนละโซนของคีย์บอร์ดให้ 2-4 คนกดพร้อมกันได้
    private static readonly string[][] KeyboardDefaults =
    {
        new[] { "<Keyboard>/w", "<Keyboard>/s", "<Keyboard>/a", "<Keyboard>/d", "<Keyboard>/e" },
        new[] { "<Keyboard>/upArrow", "<Keyboard>/downArrow", "<Keyboard>/leftArrow", "<Keyboard>/rightArrow", "<Keyboard>/slash" },
        new[] { "<Keyboard>/i", "<Keyboard>/k", "<Keyboard>/j", "<Keyboard>/l", "<Keyboard>/o" },
        new[] { "<Keyboard>/numpad8", "<Keyboard>/numpad5", "<Keyboard>/numpad4", "<Keyboard>/numpad6", "<Keyboard>/numpad0" }
    };

    private sealed class Slot
    {
        public InputActionMap map;
        public InputAction move;
        public InputAction interact;
        public ControlDevice builtFor;
        public int builtGamepad = -1;
        public InputDevice boundDevice;
    }

    private static readonly Slot[] Slots = new Slot[MaxPlayers];
    private static InputActionRebindingExtensions.RebindingOperation rebind;

    /// <summary>กำลังรอให้ผู้เล่นกดปุ่มใหม่อยู่ — เมนูใช้กันไม่ให้ Esc ไปปิดหน้าจอ</summary>
    public static bool IsRebinding => rebind != null;

    // ---------- ตั้งค่าอุปกรณ์ ----------

    public static ControlDevice GetDevice(int slot)
    {
        // ผู้เล่น 1 เปิดไว้เสมอ ค่าเริ่มต้นคือคีย์บอร์ด
        int fallback = slot == 0 ? (int)ControlDevice.Keyboard : (int)ControlDevice.Off;
        ControlDevice device = (ControlDevice)PlayerPrefs.GetInt($"pc.device.{slot}", fallback);
        return slot == 0 && device == ControlDevice.Off ? ControlDevice.Keyboard : device;
    }

    public static void SetDevice(int slot, ControlDevice device)
    {
        PlayerPrefs.SetInt($"pc.device.{slot}", (int)device);
        PlayerPrefs.Save();
    }

    /// <summary>จอยตัวที่เท่าไหร่ (0 = ตัวแรกใน Gamepad.all)</summary>
    public static int GetGamepadIndex(int slot) => PlayerPrefs.GetInt($"pc.pad.{slot}", slot);

    public static void SetGamepadIndex(int slot, int index)
    {
        PlayerPrefs.SetInt($"pc.pad.{slot}", Mathf.Max(0, index));
        PlayerPrefs.Save();
    }

    /// <summary>ช่องนี้เข้าร่วมและมีอุปกรณ์เสียบอยู่จริงหรือไม่</summary>
    public static bool IsAvailable(int slot) => ResolveDevice(slot) != null;

    /// <summary>ช่องที่พร้อมเล่น เรียงจากผู้เล่น 1 — ผู้เล่น 1 อยู่ในรายการเสมอแม้จอยยังไม่เสียบ</summary>
    public static List<int> ActiveSlots()
    {
        var list = new List<int> { 0 };
        for (int i = 1; i < MaxPlayers; i++)
        {
            if (IsAvailable(i)) list.Add(i);
        }
        return list;
    }

    // ---------- อ่านปุ่มในเกม ----------

    public static Vector2 Move(int slot)
    {
        Slot s = Get(slot);
        return s != null ? Vector2.ClampMagnitude(s.move.ReadValue<Vector2>(), 1f) : Vector2.zero;
    }

    public static bool InteractPressed(int slot)
    {
        Slot s = Get(slot);
        return s != null && s.interact.WasPressedThisFrame();
    }

    public static bool InteractHeld(int slot)
    {
        Slot s = Get(slot);
        return s != null && s.interact.IsPressed();
    }

    /// <summary>ชื่อปุ่มโต้ตอบของผู้เล่นคนนี้ สำหรับป้าย "กด E ค้าง"</summary>
    public static string InteractLabel(int slot) => BindingLabel(slot, "Interact");

    // ---------- เปลี่ยนปุ่ม ----------

    public static string[] Entries(int slot) =>
        GetDevice(slot) == ControlDevice.Gamepad ? GamepadEntries : KeyboardEntries;

    public static string BindingLabel(int slot, string entry)
    {
        Slot s = Build(slot);
        if (s == null) return "-";

        InputAction action = Resolve(s, entry, out int bindingIndex);
        return action != null ? action.GetBindingDisplayString(bindingIndex) : "-";
    }

    /// <summary>รอให้ผู้เล่นกดปุ่มใหม่ (Esc = ยกเลิก) แล้วบันทึกลง PlayerPrefs — onDone ถูกเรียกเสมอไม่ว่าจะสำเร็จหรือยกเลิก</summary>
    public static void StartRebind(int slot, string entry, Action onDone)
    {
        CancelRebind();

        Slot s = Build(slot);
        int bindingIndex = -1;
        InputAction action = s != null ? Resolve(s, entry, out bindingIndex) : null;
        if (action == null)
        {
            onDone?.Invoke();
            return;
        }

        bool gamepad = s.builtFor == ControlDevice.Gamepad;
        s.map.Disable();

        rebind = action.PerformInteractiveRebinding(bindingIndex)
            .WithControlsHavingToMatchPath(gamepad ? "<Gamepad>" : "<Keyboard>")
            .WithCancelingThrough("<Keyboard>/escape")
            .OnMatchWaitForAnother(0.1f)
            .OnComplete(op =>
            {
                PlayerPrefs.SetString(OverrideKey(slot, s.builtFor, entry), action.bindings[bindingIndex].effectivePath);
                PlayerPrefs.Save();
                FinishRebind(s, onDone);
            })
            .OnCancel(op => FinishRebind(s, onDone));

        rebind.Start();
    }

    public static void CancelRebind()
    {
        rebind?.Cancel();
    }

    /// <summary>คืนปุ่มทั้งหมดของผู้เล่นคนนี้ (เฉพาะอุปกรณ์ที่เลือกอยู่) กลับเป็นค่าเริ่มต้น</summary>
    public static void ResetBindings(int slot)
    {
        ControlDevice device = GetDevice(slot);
        foreach (string entry in Entries(slot))
        {
            PlayerPrefs.DeleteKey(OverrideKey(slot, device, entry));
        }
        PlayerPrefs.Save();
        Dispose(slot); // สร้างใหม่ครั้งหน้าที่ถูกอ่าน
    }

    // ---------- ภายใน ----------

    private static void FinishRebind(Slot s, Action onDone)
    {
        rebind?.Dispose();
        rebind = null;
        s.map.Enable();
        onDone?.Invoke();
    }

    private static string OverrideKey(int slot, ControlDevice device, string entry) => $"pc.bind.{slot}.{device}.{entry}";

    private static InputDevice ResolveDevice(int slot)
    {
        switch (GetDevice(slot))
        {
            case ControlDevice.Keyboard:
                return Keyboard.current;
            case ControlDevice.Gamepad:
                int index = GetGamepadIndex(slot);
                return index < Gamepad.all.Count ? Gamepad.all[index] : null;
            default:
                return null;
        }
    }

    /// <summary>map ที่พร้อมอ่านค่า — null ถ้าช่องปิดอยู่หรือไม่มีอุปกรณ์</summary>
    private static Slot Get(int slot)
    {
        Slot s = Build(slot);
        return s != null && s.boundDevice != null && s.boundDevice.added ? s : null;
    }

    /// <summary>สร้าง map ใหม่เมื่ออุปกรณ์ที่ตั้งไว้เปลี่ยน (เปลี่ยนในเมนู / เสียบ-ถอดจอย)</summary>
    private static Slot Build(int slot)
    {
        if (slot < 0 || slot >= MaxPlayers) return null;

        ControlDevice device = GetDevice(slot);
        if (device == ControlDevice.Off)
        {
            Dispose(slot);
            return null;
        }

        InputDevice target = ResolveDevice(slot);
        int pad = device == ControlDevice.Gamepad ? GetGamepadIndex(slot) : -1;

        Slot s = Slots[slot];
        if (s != null && s.builtFor == device && s.builtGamepad == pad && s.boundDevice == target) return s;

        Dispose(slot);
        s = new Slot { builtFor = device, builtGamepad = pad, boundDevice = target };
        s.map = new InputActionMap($"Player{slot + 1}");
        s.move = s.map.AddAction("Move", InputActionType.Value);
        s.interact = s.map.AddAction("Interact", InputActionType.Button);

        if (device == ControlDevice.Keyboard)
        {
            string[] keys = KeyboardDefaults[slot];
            s.move.AddCompositeBinding("2DVector")
                .With("Up", Saved(slot, device, "Up", keys[0]))
                .With("Down", Saved(slot, device, "Down", keys[1]))
                .With("Left", Saved(slot, device, "Left", keys[2]))
                .With("Right", Saved(slot, device, "Right", keys[3]));
            s.interact.AddBinding(Saved(slot, device, "Interact", keys[4]));
        }
        else
        {
            s.move.AddBinding("<Gamepad>/leftStick");
            s.move.AddCompositeBinding("2DVector")
                .With("Up", "<Gamepad>/dpad/up")
                .With("Down", "<Gamepad>/dpad/down")
                .With("Left", "<Gamepad>/dpad/left")
                .With("Right", "<Gamepad>/dpad/right");
            s.interact.AddBinding(Saved(slot, device, "Interact", "<Gamepad>/buttonSouth"));
        }

        // ผูก map กับอุปกรณ์ตัวเดียว คีย์บอร์ดหลายคนยังแชร์คีย์บอร์ดเดียวกันได้เพราะแยกปุ่มกัน
        if (target != null) s.map.devices = new[] { target };
        s.map.Enable();

        Slots[slot] = s;
        return s;
    }

    private static string Saved(int slot, ControlDevice device, string entry, string fallback) =>
        PlayerPrefs.GetString(OverrideKey(slot, device, entry), fallback);

    /// <summary>หา action + ลำดับ binding ของช่องที่เปลี่ยนได้ (Up/Down/Left/Right คือส่วนของ composite)</summary>
    private static InputAction Resolve(Slot s, string entry, out int bindingIndex)
    {
        bindingIndex = -1;
        if (entry == "Interact")
        {
            bindingIndex = 0;
            return s.interact;
        }

        for (int i = 0; i < s.move.bindings.Count; i++)
        {
            InputBinding b = s.move.bindings[i];
            if (b.isPartOfComposite && string.Equals(b.name, entry, StringComparison.OrdinalIgnoreCase))
            {
                bindingIndex = i;
                return s.move;
            }
        }

        return null;
    }

    private static void Dispose(int slot)
    {
        Slot s = Slots[slot];
        if (s == null) return;

        s.map.Disable();
        s.map.Dispose();
        Slots[slot] = null;
    }
}
