using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

/// <summary>
/// ตัวกลางอ่าน input ด้วย Input System ใหม่ แทน Input.GetKey/GetAxis ของ Input Manager เดิมที่ Unity เตือนว่าจะเลิกรองรับ
///
/// แกนเดิน/กล้องอ่านจาก Project-Wide Actions (Assets/InputSystem_Actions) จึงรองรับจอยเกมให้ฟรี
/// ปุ่มรายตัว (E, G, Enter, Esc, 1-9) อ่านจากคีย์บอร์ดตรงๆ โดยยังรับค่าเป็น KeyCode เหมือนเดิม
/// สคริปต์อื่นจึงเก็บช่อง KeyCode ใน Inspector ไว้ได้ ไม่ต้องตั้งปุ่มใหม่ทั้งฉาก
///
/// ไม่มี MonoBehaviour ให้ใส่ในฉาก เรียกใช้ได้เลย เช่น GameInput.Pressed(interactKey)
/// </summary>
public static class GameInput
{
    private static readonly Dictionary<KeyCode, Key> KeyMap = new Dictionary<KeyCode, Key>();
    private static InputAction moveAction;
    private static InputAction lookAction;

    /// <summary>แกนเดิน (x = ซ้าย/ขวา, y = หน้า/หลัง) ค่าเท่ากับ Horizontal/Vertical เดิม</summary>
    public static Vector2 Move => Read(ref moveAction, "Move");

    /// <summary>
    /// ระยะที่เมาส์ขยับเฟรมนี้ ปรับสเกลให้ใกล้เคียง Input.GetAxis("Mouse X") เดิม
    /// ค่า mouseSensitivity ที่ตั้งไว้ใน Inspector จึงให้ความรู้สึกเท่าเดิม
    /// </summary>
    public static Vector2 Look => Read(ref lookAction, "Look") * 0.1f;

    /// <summary>ปุ่มถูกกดในเฟรมนี้ (แทน Input.GetKeyDown)</summary>
    public static bool Pressed(KeyCode key)
    {
        ButtonControl control = Control(key);
        return control != null && control.wasPressedThisFrame;
    }

    /// <summary>ปุ่มกำลังถูกกดค้าง (แทน Input.GetKey)</summary>
    public static bool Held(KeyCode key)
    {
        ButtonControl control = Control(key);
        return control != null && control.isPressed;
    }

    /// <summary>เลขแถวบน 1-9 ถูกกดในเฟรมนี้ (แทน Input.GetKeyDown(KeyCode.Alpha1 + i))</summary>
    public static bool DigitPressed(int digit)
    {
        if (digit < 1 || digit > 9) return false;

        // Key.Digit1 ถึง Key.Digit9 เรียงติดกันใน enum ของ Input System
        return Pressed(KeyCode.Alpha1 + (digit - 1));
    }

    private static Vector2 Read(ref InputAction cached, string actionName)
    {
        if (cached == null)
        {
            InputActionAsset actions = InputSystem.actions;
            if (actions == null) return Vector2.zero; // ยังไม่ได้ตั้ง Project-Wide Actions ใน Project Settings

            cached = actions.FindAction(actionName);
            if (cached == null)
            {
                Debug.LogWarning($"[GameInput] ไม่พบ action \"{actionName}\" ใน Project-Wide Actions");
                return Vector2.zero;
            }
        }

        return cached.ReadValue<Vector2>();
    }

    private static ButtonControl Control(KeyCode code)
    {
        Keyboard keyboard = Keyboard.current;
        if (keyboard == null) return null; // ไม่มีคีย์บอร์ด (มือถือ/คอนโซล)

        if (!KeyMap.TryGetValue(code, out Key key))
        {
            key = Translate(code);
            KeyMap[code] = key;
        }

        return key == Key.None ? null : keyboard[key];
    }

    /// <summary>แปลง KeyCode ของระบบเก่าเป็น Key ของ Input System ชื่อส่วนใหญ่ตรงกัน เหลือไม่กี่ตัวที่ต่าง</summary>
    private static Key Translate(KeyCode code)
    {
        switch (code)
        {
            case KeyCode.Return: return Key.Enter;
            case KeyCode.KeypadEnter: return Key.NumpadEnter;
            case KeyCode.LeftControl: return Key.LeftCtrl;
            case KeyCode.RightControl: return Key.RightCtrl;
            case KeyCode.LeftCommand: return Key.LeftCommand;
            case KeyCode.RightCommand: return Key.RightCommand;
        }

        if (code >= KeyCode.Alpha0 && code <= KeyCode.Alpha9)
        {
            return code == KeyCode.Alpha0 ? Key.Digit0 : Key.Digit1 + (code - KeyCode.Alpha1);
        }

        if (System.Enum.TryParse(code.ToString(), out Key parsed)) return parsed;

        Debug.LogWarning($"[GameInput] ยังไม่รองรับปุ่ม {code} — เพิ่มใน GameInput.Translate");
        return Key.None;
    }
}
