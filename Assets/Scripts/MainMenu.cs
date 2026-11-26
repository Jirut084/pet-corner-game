using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// ฉากเมนูหลัก (MainMenu.unity) — หน้าแรก + หน้าตั้งค่าเสียง + หน้าเลือกสัตว์เริ่มต้น สร้าง Canvas ด้วยโค้ดเหมือนหน้าจออื่น
///
/// เลือกสัตว์แล้วจำไว้ใน ChosenStarter (static ข้ามฉากได้) แล้วโหลดฉากร้าน
/// GameManager ในฉากร้านอ่านค่านี้ตอน Start แล้วเริ่ม run ให้เอง
///
///   หน้าแรก      — Enter / ปุ่มเริ่มเกม ไปหน้าเลือกสัตว์ · ปุ่มตั้งค่าเสียง · Esc ออกเกม
///   หน้าตั้งค่าเสียง — แถบเลื่อนปรับเสียง BGM และ SFX พร้อมบันทึกค่าลง PlayerPrefs
///   หน้าเลือกสัตว์ — คลิกการ์ด หรือกด 1..9 · Esc ย้อนกลับ
/// </summary>
public partial class MainMenu : MonoBehaviour
{
    /// <summary>สัตว์ที่เลือกจากเมนู — GameManager ใช้เริ่ม run ตอนฉากร้านโหลดเสร็จ</summary>
    public static PetSpecies ChosenStarter { get; private set; }

    [Tooltip("สัตว์ที่ให้เลือกเป็นตัวเริ่มต้น — สถานีเริ่มต้นจะมาจาก need ของตัวที่เลือก")]
    [SerializeField] private PetSpecies[] selectableStarters;

    [Tooltip("ชื่อฉากร้าน ต้องอยู่ใน Build Profiles / Scene List ด้วย")]
    [SerializeField] private string gameScene = "Shop";

    [SerializeField] private string gameTitle = "PET CORNER";

    [SerializeField] private KeyCode startKey = KeyCode.Return;
    [SerializeField] private KeyCode backKey = KeyCode.Escape;

    [SerializeField] private int cardWidth = 270;
    [SerializeField] private int cardHeight = 240;
    [SerializeField] private Vector2 referenceResolution = new Vector2(1600f, 900f);

    private Canvas canvas;
    private GameObject titleRoot;
    private GameObject starterRoot;
    private GameObject settingsRoot;

    private UiKit.VolumeSliderRow settingsMusicSlider;
    private UiKit.VolumeSliderRow settingsSfxSlider;

    private void Awake()
    {
        // กลับมาหน้าเมนูจากเกมที่หยุดไว้ timeScale อาจค้างเป็น 0
        Time.timeScale = 1f;

        // ตรวจสอบ AudioListener สำหรับฉากเมนู
        UiKit.EnsureAudioListener();

        canvas = UiKit.MakeCanvas("Menu Canvas", transform, 50, true, referenceResolution);

        Image dim = UiKit.MakeImage("Dim", canvas.transform, UiKit.Dim);
        UiKit.Stretch(dim.rectTransform);

        BuildTitleScreen();
        BuildSettingsScreen();
        BuildStarterSelect();
        BuildMultiplayerScreens(); // MainMenu.Multiplayer.cs — หน้าปุ่มควบคุม + หน้า LAN

        ShowStarterSelect(false);
        ShowSettings(false);
    }

    private void Update()
    {
        bool selecting = starterRoot.activeSelf;
        bool inSettings = settingsRoot != null && settingsRoot.activeSelf;

        // หน้าปุ่มควบคุม / LAN จัดการปุ่มของตัวเอง (รวมถึงตอนรอกดปุ่มใหม่ที่ Esc = ยกเลิก)
        if (UpdateMultiplayerScreens(GameInput.Pressed(backKey))) return;

        if (GameInput.Pressed(backKey))
        {
            if (inSettings)
            {
                ShowSettings(false);
                return;
            }

            // หน้าเลือกสัตว์ = ถอยกลับหน้าแรกก่อน ต้องกดซ้ำถึงจะออกเกมจริง
            if (selecting)
            {
                ShowStarterSelect(false);
                return;
            }

            GameFlowDebugControls.QuitGame();
            return;
        }

        if (inSettings) return;

        if (!selecting)
        {
            if (GameInput.Pressed(startKey)) ShowStarterSelect(true);
            return;
        }

        for (int i = 0; i < selectableStarters.Length && i < 9; i++)
        {
            if (GameInput.DigitPressed(i + 1))
            {
                UiKit.PlayClickSound();
                StartGame(selectableStarters[i]);
                return;
            }
        }
    }

    private void ShowStarterSelect(bool show)
    {
        if (!show) hostAfterStarter = false; // ถอยออกจากหน้าเลือกสัตว์ = ยกเลิกการ Host ที่รออยู่
        HideMultiplayerScreens();
        titleRoot.SetActive(!show);
        starterRoot.SetActive(show);
        if (settingsRoot != null) settingsRoot.SetActive(false);
    }

    private void ShowSettings(bool show)
    {
        HideMultiplayerScreens();
        titleRoot.SetActive(!show);
        if (settingsRoot != null)
        {
            settingsRoot.SetActive(show);
            if (show) SyncSettingsSliders();
        }
        starterRoot.SetActive(false);
    }

    private void SyncSettingsSliders()
    {
        if (settingsMusicSlider != null)
        {
            float m = AudioManager.GetMusicVolumeGlobal();
            settingsMusicSlider.slider.SetValueWithoutNotify(m);
            settingsMusicSlider.valueText.text = $"{Mathf.RoundToInt(m * 100f)}%";
        }

        if (settingsSfxSlider != null)
        {
            float s = AudioManager.GetSfxVolumeGlobal();
            settingsSfxSlider.slider.SetValueWithoutNotify(s);
            settingsSfxSlider.valueText.text = $"{Mathf.RoundToInt(s * 100f)}%";
        }
    }

    private void StartGame(PetSpecies starter)
    {
        if (starter == null) return;

        ChosenStarter = starter;
        Debug.Log($"[MainMenu] เริ่ม run ด้วย {starter.speciesName}");

        // มาจากปุ่ม Host ในหน้า LAN — Mirror โหลดฉากร้าน (Online Scene) ให้ทุกเครื่องเอง
        if (hostAfterStarter)
        {
            hostAfterStarter = false;
            PetCornerNetworkManager.Host();
            return;
        }

        SceneManager.LoadScene(gameScene);
    }

    private void BuildTitleScreen()
    {
        float panelWidth = 660f;
        float panelHeight = 590f;
        RectTransform panel = MakePanel("Title Screen", panelWidth, panelHeight, out titleRoot);

        MakeTitle(panel, 26f, 104f, 54);

        TextMeshProUGUI subtitle = UiKit.MakeText("Subtitle", panel, 21f, UiKit.InkSoft, TextAlignmentOptions.Top);
        subtitle.text = "บ้านของคุณ · บ้านหลังที่สองของพวกเขา";
        GameFlowDebugControls.Fill(subtitle.rectTransform, 136f, 28f);

        TextMeshProUGUI tagline = UiKit.MakeText("Tagline", panel, 19f, UiKit.InkSoft, TextAlignmentOptions.Top);
        tagline.text = "What home means to you — เปิดบ้านรับฝากสัตว์ให้รอดครบทุกวัน";
        GameFlowDebugControls.Fill(tagline.rectTransform, 168f, 26f);

        // ปุ่มเริ่มเกม
        Button start = UiKit.MakeButton("Start", panel, 21f, out TextMeshProUGUI startLabel, primary: true);
        startLabel.text = $"เริ่มเกม  ({UiKit.KeyLabel(startKey)})";
        UiKit.Place(start.GetComponent<RectTransform>(), new Vector2((panelWidth - 320f) * 0.5f, -214f), new Vector2(320f, 52f));
        start.onClick.AddListener(() => ShowStarterSelect(true));

        // ปุ่มตั้งค่าเสียง
        Button settingsBtn = UiKit.MakeButton("Settings", panel, 19f, out TextMeshProUGUI settingsLabel);
        settingsLabel.text = "ตั้งค่าเสียง";
        UiKit.Place(settingsBtn.GetComponent<RectTransform>(), new Vector2((panelWidth - 320f) * 0.5f, -274f), new Vector2(320f, 48f));
        settingsBtn.onClick.AddListener(() => ShowSettings(true));

        // ปุ่มเล่นหลายคน — ตั้งจำนวนผู้เล่น/อุปกรณ์/ปุ่ม (MainMenu.Multiplayer.cs)
        Button controlsBtn = UiKit.MakeButton("Controls", panel, 19f, out TextMeshProUGUI controlsLabel);
        controlsLabel.text = "เล่นหลายคน · ปุ่มควบคุม";
        UiKit.Place(controlsBtn.GetComponent<RectTransform>(), new Vector2((panelWidth - 320f) * 0.5f, -328f), new Vector2(320f, 48f));
        controlsBtn.onClick.AddListener(() => ShowControls(true));

        Button lanBtn = UiKit.MakeButton("LAN", panel, 19f, out TextMeshProUGUI lanLabel);
        lanLabel.text = "เล่นผ่าน LAN";
        UiKit.Place(lanBtn.GetComponent<RectTransform>(), new Vector2((panelWidth - 320f) * 0.5f, -382f), new Vector2(320f, 48f));
        lanBtn.onClick.AddListener(() => ShowLan(true));

        // ปุ่มออกจากเกม
        Button quit = UiKit.MakeButton("Quit", panel, 19f, out TextMeshProUGUI quitLabel);
        quitLabel.text = $"ออกจากเกม  ({UiKit.KeyLabel(backKey)})";
        UiKit.Place(quit.GetComponent<RectTransform>(), new Vector2((panelWidth - 320f) * 0.5f, -436f), new Vector2(320f, 48f));
        quit.onClick.AddListener(GameFlowDebugControls.QuitGame);

        TextMeshProUGUI hint = UiKit.MakeText("Hint", panel, 18f, UiKit.InkSoft, TextAlignmentOptions.Top);
        hint.text = "WASD เดิน   ·   E ทำทุกอย่าง   ·   Enter เปิดร้าน   ·   Esc เมนู";
        GameFlowDebugControls.Fill(hint.rectTransform, 512f, 26f);
    }

    private void BuildSettingsScreen()
    {
        float panelWidth = 460f;
        float panelHeight = 290f;
        RectTransform panel = MakePanel("Settings Screen", panelWidth, panelHeight, out settingsRoot);

        TextMeshProUGUI title = UiKit.MakeText("Title", panel, 28f, UiKit.Ink, TextAlignmentOptions.Top);
        title.fontStyle = FontStyles.Bold;
        title.text = "ตั้งค่าระดับเสียง";
        GameFlowDebugControls.Fill(title.rectTransform, 26f, 36f);

        float sliderWidth = 390f;
        float sliderLeft = (panelWidth - sliderWidth) * 0.5f;

        settingsMusicSlider = UiKit.MakeSliderRow("MusicSlider", panel, "เสียงเพลง",
            AudioManager.GetMusicVolumeGlobal(), (v) => AudioManager.SetMusicVolumeGlobal(v), sliderWidth);
        UiKit.Place(settingsMusicSlider.holder, new Vector2(sliderLeft, -84f), new Vector2(sliderWidth, 36f));

        settingsSfxSlider = UiKit.MakeSliderRow("SfxSlider", panel, "เสียงเอฟเฟกต์",
            AudioManager.GetSfxVolumeGlobal(), (v) => AudioManager.SetSfxVolumeGlobal(v), sliderWidth);
        UiKit.Place(settingsSfxSlider.holder, new Vector2(sliderLeft, -132f), new Vector2(sliderWidth, 36f));

        Button back = UiKit.MakeButton("Back", panel, 19f, out TextMeshProUGUI backLabel, primary: true);
        backLabel.text = $"ย้อนกลับ  ({UiKit.KeyLabel(backKey)})";
        UiKit.Place(back.GetComponent<RectTransform>(), new Vector2((panelWidth - 220f) * 0.5f, -198f), new Vector2(220f, 48f));
        back.onClick.AddListener(() => ShowSettings(false));
    }

    private void BuildStarterSelect()
    {
        int count = selectableStarters != null ? selectableStarters.Length : 0;
        float spacing = 18f;
        float cardsWidth = count > 0 ? count * cardWidth + (count - 1) * spacing : 540f;

        float panelWidth = Mathf.Max(840f, cardsWidth + 100f);
        float panelHeight = cardHeight + 300f;

        RectTransform panel = MakePanel("Starter Select", panelWidth, panelHeight, out starterRoot);

        MakeTitle(panel, 26f, 64f, 44);

        TextMeshProUGUI subtitle = UiKit.MakeText("Subtitle", panel, 20f, UiKit.InkSoft, TextAlignmentOptions.Top);
        subtitle.text = "บ้านของคุณ · บ้านหลังที่สองของพวกเขา";
        GameFlowDebugControls.Fill(subtitle.rectTransform, 96f, 26f);

        TextMeshProUGUI brief = UiKit.MakeText("Brief", panel, 18f, UiKit.InkSoft, TextAlignmentOptions.Top);
        brief.text = "เลือกสัตว์ที่จะเปิดรับ — สถานีเริ่มต้นมาจากสิ่งที่สัตว์ตัวนั้นต้องการ";
        GameFlowDebugControls.Fill(brief.rectTransform, 124f, 26f);

        float cardsLeft = (panelWidth - cardsWidth) * 0.5f;

        for (int i = 0; i < count; i++)
        {
            PetSpecies species = selectableStarters[i];
            if (species == null) continue;

            UiKit.ChoiceCard card = UiKit.MakeChoiceCard($"Starter {i}", panel, new Vector2(cardWidth, cardHeight));
            UiKit.Place(card.holder,
                new Vector2(cardsLeft + i * (cardWidth + spacing), -160f), new Vector2(cardWidth, cardHeight));
            card.button.onClick.AddListener(() => StartGame(species));

            card.header.text = species.speciesName;
            card.header.fontSize = 24f;

            string needs = string.Join(" · ", System.Array.ConvertAll(species.requiredSteps, CareStepStyle.LabelOf));
            card.body.text = $"{needs}\n\nอดทน {species.patienceSeconds:F0} วิ\nค่าบริการ {species.baseFee} + {species.feePerStep}/ขั้น";

            card.footer.text = $"({i + 1})";
            card.footer.fontSize = 19f;
        }

        TextMeshProUGUI hint = UiKit.MakeText("Hint", panel, 18f, UiKit.InkSoft, TextAlignmentOptions.Top);
        hint.text = "WASD เดิน   ·   E ทำทุกอย่าง (ยก / วาง / ดูแล)   ·   Enter เปิดร้าน";
        GameFlowDebugControls.Fill(hint.rectTransform, 160f + cardHeight + 20f, 24f);

        Button back = UiKit.MakeButton("Back", panel, 19f, out TextMeshProUGUI backLabel);
        backLabel.text = "ย้อนกลับ";
        UiKit.Place(back.GetComponent<RectTransform>(),
            new Vector2((panelWidth - 220f) * 0.5f, -(160f + cardHeight + 54f)), new Vector2(220f, 48f));
        back.onClick.AddListener(() => ShowStarterSelect(false));
    }

    /// <summary>โลโก้เกมจาก Resources/UI/logo.png ถ้าหาไม่เจอค่อยใช้ชื่อเกมเป็นตัวหนังสือแทน</summary>
    private void MakeTitle(RectTransform panel, float top, float height, int fallbackFontSize)
    {
        Sprite logo = UiKit.LoadSprite("logo");
        if (logo != null)
        {
            Image logoImage = UiKit.MakeSpriteImage("Logo", panel, logo);
            GameFlowDebugControls.Fill(logoImage.rectTransform, top, height);
            return;
        }

        TextMeshProUGUI title = UiKit.MakeText("Title", panel, fallbackFontSize, UiKit.Ink, TextAlignmentOptions.Center);
        title.text = gameTitle;
        GameFlowDebugControls.Fill(title.rectTransform, top, height);
    }

    private RectTransform MakePanel(string name, float width, float height, out GameObject root)
    {
        RectTransform panel = UiKit.MakeCard(name, canvas.transform, UiKit.Panel);
        UiKit.Center(panel, new Vector2(width, height));
        root = panel.gameObject;
        return panel;
    }
}
