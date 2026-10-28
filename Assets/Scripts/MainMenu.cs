using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// ฉากเมนูหลัก (MainMenu.unity) — หน้าแรก + หน้าเลือกสัตว์เริ่มต้น สร้าง Canvas ด้วยโค้ดเหมือนหน้าจออื่น
///
/// เลือกสัตว์แล้วจำไว้ใน ChosenStarter (static ข้ามฉากได้) แล้วโหลดฉากร้าน
/// GameManager ในฉากร้านอ่านค่านี้ตอน Start แล้วเริ่ม run ให้เอง
///
///   หน้าแรก      — Enter / ปุ่มเริ่มเกม ไปหน้าเลือกสัตว์ · Esc ออกเกม
///   หน้าเลือกสัตว์ — คลิกการ์ด หรือกด 1..9 · Esc ย้อนกลับ
/// </summary>
public class MainMenu : MonoBehaviour
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

    [SerializeField] private int cardWidth = 260;
    [SerializeField] private int cardHeight = 190;
    [SerializeField] private Vector2 referenceResolution = new Vector2(1600f, 900f);

    private Canvas canvas;
    private GameObject titleRoot;
    private GameObject starterRoot;

    private void Awake()
    {
        // กลับมาหน้าเมนูจากเกมที่หยุดไว้ timeScale อาจค้างเป็น 0
        Time.timeScale = 1f;

        canvas = UiKit.MakeCanvas("Menu Canvas", transform, 50, true, referenceResolution);

        Image dim = UiKit.MakeImage("Dim", canvas.transform, UiKit.Dim);
        UiKit.Stretch(dim.rectTransform);

        BuildTitleScreen();
        BuildStarterSelect();
        ShowStarterSelect(false);
    }

    private void Update()
    {
        bool selecting = starterRoot.activeSelf;

        if (GameInput.Pressed(backKey))
        {
            // หน้าเลือกสัตว์ = ถอยกลับหน้าแรกก่อน ต้องกดซ้ำถึงจะออกเกมจริง
            if (selecting) ShowStarterSelect(false);
            else GameFlowDebugControls.QuitGame();
            return;
        }

        if (!selecting)
        {
            if (GameInput.Pressed(startKey)) ShowStarterSelect(true);
            return;
        }

        for (int i = 0; i < selectableStarters.Length && i < 9; i++)
        {
            if (GameInput.DigitPressed(i + 1))
            {
                StartGame(selectableStarters[i]);
                return;
            }
        }
    }

    private void ShowStarterSelect(bool show)
    {
        titleRoot.SetActive(!show);
        starterRoot.SetActive(show);
    }

    private void StartGame(PetSpecies starter)
    {
        if (starter == null) return;

        ChosenStarter = starter;
        Debug.Log($"[MainMenu] เริ่ม run ด้วย {starter.speciesName}");
        SceneManager.LoadScene(gameScene);
    }

    private void BuildTitleScreen()
    {
        RectTransform panel = MakePanel("Title Screen", 640f, 430f, out titleRoot);

        Text title = UiKit.MakeText("Title", panel, 58, UiKit.Ink, TextAnchor.UpperCenter);
        title.fontStyle = FontStyle.Bold;
        title.text = gameTitle;
        GameFlowDebugControls.Fill(title.rectTransform, 62f, 70f);

        Text subtitle = UiKit.MakeText("Subtitle", panel, 21, UiKit.InkSoft, TextAnchor.UpperCenter);
        subtitle.text = "ร้านรับฝากดูแลสัตว์เลี้ยง";
        GameFlowDebugControls.Fill(subtitle.rectTransform, 136f, 28f);

        Text tagline = UiKit.MakeText("Tagline", panel, 19, UiKit.InkSoft, TextAnchor.UpperCenter);
        tagline.text = "เปิดร้านให้รอดจนครบทุกวัน";
        GameFlowDebugControls.Fill(tagline.rectTransform, 168f, 26f);

        Button start = UiKit.MakeButton("Start", panel, 22, out Text startLabel);
        startLabel.text = $"เริ่มเกม  ({startKey})";
        UiKit.Place(start.GetComponent<RectTransform>(), new Vector2((640f - 300f) * 0.5f, -232f), new Vector2(300f, 60f));
        start.onClick.AddListener(() => ShowStarterSelect(true));

        Button quit = UiKit.MakeButton("Quit", panel, 19, out Text quitLabel);
        quitLabel.text = $"ออกจากเกม  ({backKey})";
        UiKit.Place(quit.GetComponent<RectTransform>(), new Vector2((640f - 300f) * 0.5f, -304f), new Vector2(300f, 52f));
        quit.onClick.AddListener(GameFlowDebugControls.QuitGame);

        Text hint = UiKit.MakeText("Hint", panel, 18, UiKit.InkSoft, TextAnchor.UpperCenter);
        hint.text = "WASD เดิน   ·   E ทำทุกอย่าง   ·   Enter เปิดร้าน   ·   Esc เมนู";
        GameFlowDebugControls.Fill(hint.rectTransform, 380f, 26f);
    }

    private void BuildStarterSelect()
    {
        int count = selectableStarters != null ? selectableStarters.Length : 0;
        float cardsWidth = count > 0 ? count * cardWidth + (count - 1) * 16f : 520f;

        RectTransform panel = MakePanel("Starter Select", Mathf.Max(760f, cardsWidth + 120f), cardHeight + 330f, out starterRoot);

        Text title = UiKit.MakeText("Title", panel, 46, UiKit.Ink, TextAnchor.UpperCenter);
        title.fontStyle = FontStyle.Bold;
        title.text = gameTitle;
        GameFlowDebugControls.Fill(title.rectTransform, 40f, 56f);

        Text subtitle = UiKit.MakeText("Subtitle", panel, 21, UiKit.InkSoft, TextAnchor.UpperCenter);
        subtitle.text = "ร้านรับฝากดูแลสัตว์เลี้ยง";
        GameFlowDebugControls.Fill(subtitle.rectTransform, 98f, 26f);

        Text brief = UiKit.MakeText("Brief", panel, 19, UiKit.InkSoft, TextAnchor.UpperCenter);
        brief.text = "เลือกสัตว์ที่จะเปิดรับ — สถานีเริ่มต้นมาจากสิ่งที่สัตว์ตัวนั้นต้องการ";
        GameFlowDebugControls.Fill(brief.rectTransform, 128f, 26f);

        float cardsLeft = (panel.sizeDelta.x - cardsWidth) * 0.5f;

        for (int i = 0; i < count; i++)
        {
            PetSpecies species = selectableStarters[i];
            if (species == null) continue;

            Button button = UiKit.MakeButton($"Starter {i}", panel, 20, out Text label);
            UiKit.Place(button.GetComponent<RectTransform>(),
                new Vector2(cardsLeft + i * (cardWidth + 16f), -174f), new Vector2(cardWidth, cardHeight));
            button.onClick.AddListener(() => StartGame(species));

            string needs = string.Join(" · ", System.Array.ConvertAll(species.requiredSteps, CareStepStyle.LabelOf));
            label.text = $"{species.speciesName}\n\n{needs}\n\nอดทน {species.patienceSeconds:F0} วิ\n" +
                         $"ค่าบริการ {species.baseFee} + {species.feePerStep}/ขั้น\n\n({i + 1})";
        }

        Text hint = UiKit.MakeText("Hint", panel, 18, UiKit.InkSoft, TextAnchor.UpperCenter);
        hint.text = "WASD เดิน   ·   E ทำทุกอย่าง (ยก / วาง / ดูแล)   ·   Enter เปิดร้าน";
        GameFlowDebugControls.Fill(hint.rectTransform, 174f + cardHeight + 26f, 24f);

        Button back = UiKit.MakeButton("Back", panel, 19, out Text backLabel);
        backLabel.text = "ย้อนกลับ";
        UiKit.Place(back.GetComponent<RectTransform>(),
            new Vector2((panel.sizeDelta.x - 220f) * 0.5f, -(174f + cardHeight + 62f)), new Vector2(220f, 46f));
        back.onClick.AddListener(() => ShowStarterSelect(false));
    }

    private RectTransform MakePanel(string name, float width, float height, out GameObject root)
    {
        RectTransform panel = UiKit.MakeCard(name, canvas.transform, UiKit.Card);
        UiKit.Center(panel, new Vector2(width, height));
        root = panel.gameObject;
        return panel;
    }
}
