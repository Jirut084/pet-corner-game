using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// ส่วนเล่นหลายคนของเมนูหลัก
///   หน้าปุ่มควบคุม — เลือกอุปกรณ์ของผู้เล่น 1-4 (ไม่เล่น / คีย์บอร์ด / จอยตัวที่ N) แล้วคลิกปุ่มเพื่อตั้งปุ่มใหม่
///                     ผู้เล่นที่เปิดไว้จะเกิดในร้านพร้อมกัน (PlayerSpawner) ค่าเก็บใน PlayerPrefs
///   หน้า LAN        — Host (เลือกสัตว์แล้วเปิดห้อง) / Join (ใส่ IP ของ host) ผ่าน PetCornerNetworkManager
/// </summary>
public partial class MainMenu
{
    private static string EntryLabel(string entry)
    {
        switch (entry)
        {
            case "Up": return Loc.T("ขึ้น", "Up");
            case "Down": return Loc.T("ลง", "Down");
            case "Left": return Loc.T("ซ้าย", "Left");
            case "Right": return Loc.T("ขวา", "Right");
            case "Interact": return Loc.T("โต้ตอบ", "Interact");
            default: return entry;
        }
    }

    private const float ControlsWidth = 1240f;
    private const float ControlsHeight = 640f;
    private const float ColumnWidth = 280f;
    private const float ColumnGap = 16f;
    private const float ColumnHeight = 450f;

    private GameObject controlsRoot;
    private RectTransform controlsContent;
    private int lastGamepadCount = -1;
    private string rebindingKey; // "slot:entry" ที่กำลังรอกดปุ่มใหม่ — ใช้เปลี่ยนป้ายเป็น "กดปุ่ม..."

    private GameObject lanRoot;
    private TMP_InputField addressField;
    private TextMeshProUGUI lanStatus;

    /// <summary>กด Host แล้วไปเลือกสัตว์ก่อน — เลือกเสร็จ StartGame จะเปิดห้องแทนโหลดฉากเอง</summary>
    private bool hostAfterStarter;

    private void Start()
    {
        // กลับมาเมนูระหว่างเล่นออนไลน์ (เช่นกดกลับเมนูจากหน้าหยุดเกม) = ออกจากห้อง
        // ทำใน Start ไม่ใช่ Awake เพราะตอนนี้ฉากเมนูเป็นฉาก active แล้ว Mirror จึงไม่โหลดเมนูซ้ำ
        PetCornerNetworkManager.Leave();
    }

    private void OnEnable() => PetCornerNetworkManager.StatusChanged += OnNetworkStatus;
    private void OnDisable() => PetCornerNetworkManager.StatusChanged -= OnNetworkStatus;

    private void BuildMultiplayerScreens()
    {
        BuildControlsScreen();
        BuildLanScreen();
        HideMultiplayerScreens();
    }

    private void HideMultiplayerScreens()
    {
        PlayerControls.CancelRebind();
        if (controlsRoot != null) controlsRoot.SetActive(false);
        if (lanRoot != null) lanRoot.SetActive(false);
    }

    private void ShowControls(bool show)
    {
        ShowSettings(false); // ซ่อนหน้าอื่น + กลับมาโชว์หน้าแรก
        titleRoot.SetActive(!show);
        controlsRoot.SetActive(show);
        if (show) RefreshControls();
    }

    private void ShowLan(bool show)
    {
        ShowSettings(false);
        titleRoot.SetActive(!show);
        lanRoot.SetActive(show);
        if (show) lanStatus.text = "";
    }

    /// <summary>เรียกจาก Update — คืน true ถ้าหน้าเล่นหลายคนเปิดอยู่และรับ input เฟรมนี้ไปแล้ว</summary>
    private bool UpdateMultiplayerScreens(bool backPressed)
    {
        // ระหว่างรอกดปุ่มใหม่ Esc เป็นของการยกเลิก ไม่ปิดหน้าจอ
        if (PlayerControls.IsRebinding) return true;

        if (controlsRoot != null && controlsRoot.activeSelf)
        {
            if (backPressed)
            {
                ShowControls(false);
                return true;
            }

            // เสียบ/ถอดจอยระหว่างเปิดหน้านี้ = อัปเดตป้ายสถานะจอย
            if (Gamepad.all.Count != lastGamepadCount) RefreshControls();
            return true;
        }

        if (lanRoot != null && lanRoot.activeSelf)
        {
            if (backPressed) ShowLan(false);
            return true; // กำลังพิมพ์ IP อยู่ ไม่ให้ Enter/เลขไปสั่งเมนู
        }

        return false;
    }

    // ---------- หน้าปุ่มควบคุม ----------

    private void BuildControlsScreen()
    {
        RectTransform panel = MakePanel("Controls Screen", ControlsWidth, ControlsHeight, out controlsRoot);

        TextMeshProUGUI title = UiKit.MakeText("Title", panel, 28f, UiKit.Ink, TextAlignmentOptions.Top);
        title.fontStyle = FontStyles.Bold;
        title.text = Loc.T("เล่นหลายคน · ปุ่มควบคุม", "Multiplayer · Controls");
        GameFlowDebugControls.Fill(title.rectTransform, 22f, 36f);

        TextMeshProUGUI hint = UiKit.MakeText("Hint", panel, 17f, UiKit.InkSoft, TextAlignmentOptions.Top);
        hint.text = Loc.T("เปิดผู้เล่นได้สูงสุด 4 คน ใช้คีย์บอร์ดร่วมกันหรือจอยคนละตัวก็ได้ · คลิกปุ่มแล้วกดปุ่มใหม่ (Esc ยกเลิก)", "Up to 4 players — share the keyboard or use a gamepad each · click a binding, then press the new key (Esc cancels)");
        GameFlowDebugControls.Fill(hint.rectTransform, 62f, 24f);

        controlsContent = UiKit.MakeRect("Columns", panel);
        controlsContent.anchorMin = controlsContent.anchorMax = new Vector2(0f, 1f);
        controlsContent.pivot = new Vector2(0f, 1f);
        controlsContent.anchoredPosition = Vector2.zero;

        Button back = UiKit.MakeButton("Back", panel, 19f, out TextMeshProUGUI backLabel, primary: true);
        backLabel.text = Loc.T($"เสร็จแล้ว  ({UiKit.KeyLabel(backKey)})", $"Done  ({UiKit.KeyLabel(backKey)})");
        UiKit.Place(back.GetComponent<RectTransform>(), new Vector2((ControlsWidth - 240f) * 0.5f, -(ControlsHeight - 70f)), new Vector2(240f, 50f));
        back.onClick.AddListener(() => ShowControls(false));
    }

    private void RefreshControls()
    {
        lastGamepadCount = Gamepad.all.Count;

        for (int i = controlsContent.childCount - 1; i >= 0; i--)
        {
            Destroy(controlsContent.GetChild(i).gameObject);
        }

        float columnsWidth = PlayerControls.MaxPlayers * ColumnWidth + (PlayerControls.MaxPlayers - 1) * ColumnGap;
        float left = (ControlsWidth - columnsWidth) * 0.5f;

        for (int slot = 0; slot < PlayerControls.MaxPlayers; slot++)
        {
            BuildPlayerColumn(slot, new Vector2(left + slot * (ColumnWidth + ColumnGap), -100f));
        }
    }

    private void BuildPlayerColumn(int slot, Vector2 topLeft)
    {
        RectTransform column = UiKit.MakeCard($"Player {slot + 1}", controlsContent, UiKit.CardQuiet);
        UiKit.Place(column, topLeft, new Vector2(ColumnWidth, ColumnHeight));

        Image stripe = UiKit.MakeImage("Color", column, PlayerControls.PlayerColors[slot]);
        UiKit.Place(stripe.rectTransform, Vector2.zero, new Vector2(ColumnWidth, 8f));

        TextMeshProUGUI header = UiKit.MakeText("Header", column, 22f, UiKit.Ink, TextAlignmentOptions.Top);
        header.fontStyle = FontStyles.Bold;
        header.text = Loc.T($"ผู้เล่น {slot + 1}", $"Player {slot + 1}");
        UiKit.Place(header.rectTransform, new Vector2(0f, -18f), new Vector2(ColumnWidth, 30f));

        ControlDevice device = PlayerControls.GetDevice(slot);

        Button deviceBtn = UiKit.MakeButton("Device", column, 18f, out TextMeshProUGUI deviceLabel, primary: device != ControlDevice.Off);
        deviceLabel.text = DeviceLabel(slot);
        UiKit.Place(deviceBtn.GetComponent<RectTransform>(), new Vector2(16f, -56f), new Vector2(ColumnWidth - 32f, 46f));
        deviceBtn.onClick.AddListener(() =>
        {
            CycleDevice(slot);
            RefreshControls();
        });

        if (device == ControlDevice.Off)
        {
            TextMeshProUGUI off = UiKit.MakeText("Off", column, 17f, UiKit.InkSoft, TextAlignmentOptions.Top);
            off.textWrappingMode = TextWrappingModes.Normal;
            off.text = Loc.T("ไม่เข้าร่วม\nคลิกปุ่มด้านบนเพื่อเลือกคีย์บอร์ดหรือจอย", "Not playing\nClick the button above to pick a keyboard or gamepad");
            UiKit.Place(off.rectTransform, new Vector2(16f, -130f), new Vector2(ColumnWidth - 32f, 80f));
            return;
        }

        float y = -118f;
        if (device == ControlDevice.Gamepad)
        {
            TextMeshProUGUI move = UiKit.MakeText("Move", column, 16f, UiKit.InkSoft, TextAlignmentOptions.Left);
            move.text = Loc.T("เดิน: ก้านซ้าย / D-Pad", "Move: left stick / D-pad");
            UiKit.Place(move.rectTransform, new Vector2(20f, y), new Vector2(ColumnWidth - 40f, 40f));
            y -= 48f;
        }

        foreach (string entry in PlayerControls.Entries(slot))
        {
            BuildBindingRow(column, slot, entry, y);
            y -= 50f;
        }

        Button reset = UiKit.MakeButton("Reset", column, 16f, out TextMeshProUGUI resetLabel);
        resetLabel.text = Loc.T("คืนค่าเริ่มต้น", "Reset to defaults");
        UiKit.Place(reset.GetComponent<RectTransform>(), new Vector2(16f, -(ColumnHeight - 60f)), new Vector2(ColumnWidth - 32f, 42f));
        reset.onClick.AddListener(() =>
        {
            PlayerControls.ResetBindings(slot);
            RefreshControls();
        });
    }

    private void BuildBindingRow(RectTransform column, int slot, string entry, float y)
    {
        TextMeshProUGUI label = UiKit.MakeText($"{entry} Label", column, 18f, UiKit.Ink, TextAlignmentOptions.Left);
        label.text = EntryLabel(entry);
        UiKit.Place(label.rectTransform, new Vector2(20f, y), new Vector2(100f, 42f));

        string key = $"{slot}:{entry}";
        bool waiting = rebindingKey == key;

        Button bind = UiKit.MakeButton($"{entry} Binding", column, 18f, out TextMeshProUGUI bindLabel, primary: waiting);
        bindLabel.text = waiting ? Loc.T("กดปุ่ม...", "Press a key...") : PlayerControls.BindingLabel(slot, entry);
        UiKit.Place(bind.GetComponent<RectTransform>(), new Vector2(ColumnWidth - 156f, y), new Vector2(140f, 42f));
        bind.onClick.AddListener(() =>
        {
            rebindingKey = key;
            RefreshControls();
            PlayerControls.StartRebind(slot, entry, () =>
            {
                rebindingKey = null;
                if (controlsRoot != null && controlsRoot.activeSelf) RefreshControls();
            });
        });
    }

    private static string DeviceLabel(int slot)
    {
        switch (PlayerControls.GetDevice(slot))
        {
            case ControlDevice.Keyboard:
                return Loc.T("คีย์บอร์ด", "Keyboard");
            case ControlDevice.Gamepad:
                int pad = PlayerControls.GetGamepadIndex(slot);
                return PlayerControls.IsAvailable(slot) ? Loc.T($"จอย {pad + 1}", $"Gamepad {pad + 1}") : Loc.T($"จอย {pad + 1} (ไม่ได้เสียบ)", $"Gamepad {pad + 1} (not connected)");
            default:
                return Loc.T("ไม่เล่น", "Not playing");
        }
    }

    /// <summary>วนตัวเลือก: ไม่เล่น → คีย์บอร์ด → จอย 1 … จอย 4 → ไม่เล่น (ผู้เล่น 1 ข้าม "ไม่เล่น")</summary>
    private static void CycleDevice(int slot)
    {
        ControlDevice device = PlayerControls.GetDevice(slot);
        int pad = PlayerControls.GetGamepadIndex(slot);

        if (device == ControlDevice.Off)
        {
            PlayerControls.SetDevice(slot, ControlDevice.Keyboard);
        }
        else if (device == ControlDevice.Keyboard)
        {
            PlayerControls.SetDevice(slot, ControlDevice.Gamepad);
            PlayerControls.SetGamepadIndex(slot, 0);
        }
        else if (pad + 1 < PlayerControls.MaxPlayers)
        {
            PlayerControls.SetGamepadIndex(slot, pad + 1);
        }
        else
        {
            PlayerControls.SetDevice(slot, slot == 0 ? ControlDevice.Keyboard : ControlDevice.Off);
        }
    }

    // ---------- หน้า LAN ----------

    private void BuildLanScreen()
    {
        float width = 640f;
        float height = 520f;
        RectTransform panel = MakePanel("LAN Screen", width, height, out lanRoot);

        TextMeshProUGUI title = UiKit.MakeText("Title", panel, 28f, UiKit.Ink, TextAlignmentOptions.Top);
        title.fontStyle = FontStyles.Bold;
        title.text = Loc.T("เล่นผ่าน LAN", "LAN play");
        GameFlowDebugControls.Fill(title.rectTransform, 22f, 36f);

        List<string> ips = PetCornerNetworkManager.LocalAddresses();
        TextMeshProUGUI mine = UiKit.MakeText("My IP", panel, 18f, UiKit.InkSoft, TextAlignmentOptions.Top);
        mine.text = ips.Count > 0
            ? Loc.T($"IP เครื่องนี้: {string.Join(" / ", ips)}   ·   พอร์ต 7777", $"This machine's IP: {string.Join(" / ", ips)}   ·   port 7777")
            : Loc.T("ไม่พบเครือข่าย LAN — ทดสอบในเครื่องเดียวด้วย localhost ได้", "No LAN found — you can still test on one machine with localhost");
        GameFlowDebugControls.Fill(mine.rectTransform, 66f, 26f);

        float buttonWidth = 380f;
        float x = (width - buttonWidth) * 0.5f;

        Button host = UiKit.MakeButton("Host", panel, 20f, out TextMeshProUGUI hostLabel, primary: true);
        hostLabel.text = Loc.T("เปิดห้อง (Host)", "Host a game");
        UiKit.Place(host.GetComponent<RectTransform>(), new Vector2(x, -110f), new Vector2(buttonWidth, 52f));
        host.onClick.AddListener(() =>
        {
            hostAfterStarter = true;
            ShowStarterSelect(true);
        });

        addressField = MakeInputField(panel, PlayerPrefs.GetString("net.lastAddress", "localhost"));
        UiKit.Place((RectTransform)addressField.transform, new Vector2(x, -190f), new Vector2(buttonWidth, 48f));

        Button join = UiKit.MakeButton("Join", panel, 20f, out TextMeshProUGUI joinLabel);
        joinLabel.text = Loc.T("เข้าร่วม (Join)", "Join a game");
        UiKit.Place(join.GetComponent<RectTransform>(), new Vector2(x, -248f), new Vector2(buttonWidth, 52f));
        join.onClick.AddListener(() =>
        {
            PlayerPrefs.SetString("net.lastAddress", addressField.text.Trim());
            PlayerPrefs.Save();
            PetCornerNetworkManager.Join(addressField.text);
        });

        lanStatus = UiKit.MakeText("Status", panel, 18f, UiKit.Ink, TextAlignmentOptions.Top);
        GameFlowDebugControls.Fill(lanStatus.rectTransform, 318f, 26f);

        TextMeshProUGUI note = UiKit.MakeText("Note", panel, 15f, UiKit.InkSoft, TextAlignmentOptions.Top);
        note.textWrappingMode = TextWrappingModes.Normal;
        note.text = Loc.T("1 คนต่อ 1 เครื่อง ใช้ปุ่มของผู้เล่น 1 · ทุกเครื่องเห็นกันเดิน แต่ระบบร้าน (ลูกค้า สัตว์ เงิน) ยังรันแยกแต่ละเครื่อง", "One player per machine, using Player 1's controls · everyone sees each other move, but the shop (customers, pets, money) still runs separately on each machine");
        UiKit.Place(note.rectTransform, new Vector2(40f, -354f), new Vector2(width - 80f, 50f));

        Button back = UiKit.MakeButton("Back", panel, 19f, out TextMeshProUGUI backLabel);
        backLabel.text = Loc.T($"ย้อนกลับ  ({UiKit.KeyLabel(backKey)})", $"Back  ({UiKit.KeyLabel(backKey)})");
        UiKit.Place(back.GetComponent<RectTransform>(), new Vector2((width - 220f) * 0.5f, -(height - 74f)), new Vector2(220f, 48f));
        back.onClick.AddListener(() =>
        {
            PetCornerNetworkManager.Leave(); // ยกเลิกการเชื่อมต่อที่ค้างอยู่
            ShowLan(false);
        });
    }

    private void OnNetworkStatus(string message)
    {
        if (lanStatus != null) lanStatus.text = message;
    }

    /// <summary>ช่องพิมพ์ข้อความแบบ TMP (UiKit ยังไม่มีตัวช่วยนี้)</summary>
    private static TMP_InputField MakeInputField(RectTransform parent, string initial)
    {
        RectTransform holder = UiKit.MakeCard("Address", parent, UiKit.Card, out Image background);
        background.raycastTarget = true;

        RectTransform area = UiKit.MakeRect("Text Area", holder);
        UiKit.Stretch(area, 12f);
        area.gameObject.AddComponent<RectMask2D>();

        TextMeshProUGUI text = UiKit.MakeText("Text", area, 20f, UiKit.Ink, TextAlignmentOptions.Left);
        text.textPreprocessor = null; // ตัวจัดสระไทยทำให้ตำแหน่งเคอร์เซอร์เพี้ยน IP เป็นตัวเลขอยู่แล้ว
        UiKit.Stretch(text.rectTransform);

        TextMeshProUGUI placeholder = UiKit.MakeText("Placeholder", area, 18f, UiKit.InkSoft, TextAlignmentOptions.Left);
        placeholder.text = Loc.T("IP ของ host เช่น 192.168.1.10", "Host's IP, e.g. 192.168.1.10");
        UiKit.Stretch(placeholder.rectTransform);

        TMP_InputField field = holder.gameObject.AddComponent<TMP_InputField>();
        field.textViewport = area;
        field.textComponent = text;
        field.placeholder = placeholder;
        field.targetGraphic = background;
        field.characterLimit = 64;
        field.text = initial;
        return field;
    }
}
