using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;
using UnityEngine.UI;

/// <summary>
/// ชุดชิ้นส่วน UI กลางของเกม — สี ฟอนต์ สไปรต์ และตัวช่วยสร้างการ์ด/ข้อความ/ปุ่ม
/// ใช้ร่วมกันระหว่าง HUD (GameHudOverlay) กับหน้าจอเมนู (GameFlowDebugControls)
/// ทุกหน้าจอจึงหน้าตาเป็นชุดเดียวกันโดยไม่ต้องก๊อปโค้ดสร้าง UI ไปไว้สองที่
///
/// แนวทาง (อิง PlateUp!): การ์ดครีมมุมโค้งบนเงาจาง ตัวหนังสือสีหมึก
/// ใช้สีสดเฉพาะจุดที่สื่อความหมาย (ขั้นตอน/ความอดทน/คำเตือน/เงิน) ที่เหลือเป็นกลาง
///
/// สไปรต์ทั้งหมดวาดด้วยโค้ดตอนเรียกใช้ครั้งแรก เพราะสไปรต์ในตัว Unity (UI/Skin/*.psd)
/// โหลดได้เฉพาะใน Editor พอ build จริงแล้วจะกลายเป็นกล่องเหลี่ยมไม่มีพื้น
/// </summary>
public static class UiKit
{
    // ---------- ชุดสี ----------
    public static readonly Color Panel = new Color(0.95f, 0.93f, 0.89f, 0.98f);    // พื้นแผงเมนูใหญ่ อุ่นนวล
    public static readonly Color Card = new Color(0.995f, 0.985f, 0.965f, 0.99f);   // พื้นการ์ด/ปุ่ม ขาวครีมสว่าง ลอยเด่นจากแผง
    public static readonly Color CardQuiet = new Color(0.97f, 0.95f, 0.92f, 0.94f); // การ์ดเงียบ/ป้ายรอง
    public static readonly Color Border = new Color(0.16f, 0.14f, 0.11f, 0.12f);    // เส้นขอบบางๆ
    public static readonly Color Shadow = new Color(0.12f, 0.10f, 0.08f, 0.16f);    // เงานวลตา
    public static readonly Color Ink = new Color(0.16f, 0.14f, 0.11f);              // สีหมึกหลัก
    public static readonly Color InkSoft = new Color(0.16f, 0.14f, 0.11f, 0.78f);   // สีหมึกรอง
    public static readonly Color Track = new Color(0.16f, 0.14f, 0.11f, 0.18f);

    public static readonly Color Money = new Color(0.85f, 0.62f, 0.15f);
    public static readonly Color Good = new Color(0.35f, 0.68f, 0.40f);
    public static readonly Color Warn = new Color(0.92f, 0.68f, 0.22f);
    public static readonly Color Danger = new Color(0.85f, 0.35f, 0.30f);

    /// <summary>ม่านทึบหลังหน้าจอเมนู — เข้มพอให้อ่านการ์ดได้ แต่ยังเห็นร้านอยู่ข้างหลังจางๆ</summary>
    public static readonly Color Dim = new Color(0.13f, 0.11f, 0.09f, 0.86f);

    // สีปุ่มและการตอบสนองเมื่อชี้เมาส์
    public static readonly Color ButtonHover = new Color(0.95f, 0.91f, 0.83f);
    public static readonly Color ButtonPress = new Color(0.88f, 0.84f, 0.77f);
    public static readonly Color PrimaryBtn = new Color(0.88f, 0.58f, 0.18f);
    public static readonly Color PrimaryBtnHover = new Color(0.94f, 0.65f, 0.24f);
    public static readonly Color PrimaryBtnPress = new Color(0.80f, 0.50f, 0.14f);

    private static Font font;
    private static TMP_FontAsset tmpFont;
    private static Sprite rounded;
    private static Sprite circle;
    private static Sprite ring;

    public static Font Font
    {
        get
        {
            if (font == null) font = Resources.Load<Font>("UI/Mitr-SemiBold");
            if (font == null) font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            return font;
        }
    }

    public static Font DefaultFont => Font;

    /// <summary>
    /// TMP_FontAsset พร้อม OpenType features ภาษาไทย (mark, mkmk, liga, kern)
    /// สร้างแบบ Dynamic SDF ที่ runtime ทำให้ไม่ต้องพึ่งพา prefab หรือ meta
    /// </summary>
    public static TMP_FontAsset TmpFont
    {
        get
        {
            if (tmpFont == null)
            {
                tmpFont = Resources.Load<TMP_FontAsset>("UI/Mitr-SemiBold SDF");
                if (tmpFont == null)
                {
                    Font baseFont = Font;
                    if (baseFont != null)
                    {
                        tmpFont = TMP_FontAsset.CreateFontAsset(
                            baseFont,
                            samplingPointSize: 44,
                            atlasPadding: 6,
                            renderMode: GlyphRenderMode.SDFAA,
                            atlasWidth: 1024,
                            atlasHeight: 1024,
                            atlasPopulationMode: AtlasPopulationMode.Dynamic,
                            enableMultiAtlasSupport: true);
                    }
                }

                if (tmpFont != null)
                {
                    tmpFont.getFontFeatures = true;
                }
            }
            return tmpFont;
        }
    }

    /// <summary>
    /// ตัวช่วยจัดลำดับตัวอักษรภาษาไทยสำหรับสระอำ (ำ) ที่มีวรรณยุกต์ซ้อน
    /// เปลี่ยนเป็น นิคหิต (ํ) + วรรณยุกต์ + สระอา (า) เพื่อให้ OpenType แสดงผลซ้อนกันถูกต้อง
    /// </summary>
    public class ThaiTextPreprocessor : ITextPreprocessor
    {
        public static readonly ThaiTextPreprocessor Instance = new ThaiTextPreprocessor();

        public string PreprocessText(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;

            System.Text.StringBuilder sb = null;
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (i + 1 < text.Length && (c >= '\u0E48' && c <= '\u0E4B') && text[i + 1] == '\u0E33')
                {
                    if (sb == null)
                    {
                        sb = new System.Text.StringBuilder(text.Length + 4);
                        sb.Append(text, 0, i);
                    }
                    sb.Append('\u0E4D'); // นิคหิต
                    sb.Append(c);        // วรรณยุกต์
                    sb.Append('\u0E32'); // สระอา
                    i++; // ข้ามสระอำเดิม
                }
                else
                {
                    sb?.Append(c);
                }
            }

            return sb != null ? sb.ToString() : text;
        }
    }

    /// <summary>สี่เหลี่ยมมุมโค้งสำหรับพื้นการ์ด (ใช้แบบ Sliced มุมจึงคมเสมอไม่ว่ายืดเท่าไหร่)</summary>
    public static Sprite Rounded
    {
        get
        {
            if (rounded == null) rounded = CreateRoundedSprite(32, 10);
            return rounded;
        }
    }

    /// <summary>วงกลมทึบ สำหรับจุดบอกสถานะและไอคอนเหรียญ</summary>
    public static Sprite Circle
    {
        get
        {
            if (circle == null) circle = CreateRingSprite(64, 32);
            return circle;
        }
    }

    /// <summary>วงแหวน สำหรับมิเตอร์ความอดทนแบบ Radial360</summary>
    public static Sprite Ring
    {
        get
        {
            if (ring == null) ring = CreateRingSprite(128, 11);
            return ring;
        }
    }

    // ---------- ตัวช่วยสร้าง ----------

    /// <summary>Canvas เต็มจอที่ย่อ/ขยายตามความละเอียดจอ — interactive = รับคลิกปุ่มหรือไม่</summary>
    public static Canvas MakeCanvas(string name, Transform parent, int sortingOrder, bool interactive, Vector2 reference)
    {
        GameObject go = new GameObject(name, typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        go.transform.SetParent(parent, false);

        Canvas canvas = go.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = sortingOrder;
        canvas.pixelPerfect = false; // SDF คมชัดอยู่แล้ว การปิด pixelPerfect ช่วยให้สเกลลื่นไหลและไม่สั่น

        CanvasScaler scaler = go.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = reference;
        scaler.matchWidthOrHeight = 0.5f;

        go.GetComponent<GraphicRaycaster>().enabled = interactive;
        return canvas;
    }

    public static RectTransform MakeRect(string name, Transform parent)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);

        RectTransform rect = (RectTransform)go.transform;
        rect.anchorMin = rect.anchorMax = Vector2.zero;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = Vector2.zero;
        return rect;
    }

    // ---------- ชื่อปุ่มที่โชว์ผู้เล่น ----------
    /// <summary>
    /// ชื่อปุ่มแบบที่ผู้เล่นคุ้นเคย ใช้แทน KeyCode.ToString() ที่ได้คำแบบ Return / Escape
    /// ซึ่งไม่ตรงกับตัวหนังสือบนคีย์บอร์ดและคำแนะนำบนจอ (Enter / Esc)
    /// </summary>
    public static string KeyLabel(KeyCode key)
    {
        switch (key)
        {
            case KeyCode.Return:
            case KeyCode.KeypadEnter: return "Enter";
            case KeyCode.Escape: return "Esc";
            case KeyCode.Space: return "Space";
            case KeyCode.Backspace: return "Backspace";
            case KeyCode.LeftShift:
            case KeyCode.RightShift: return "Shift";
            default:
                if (key >= KeyCode.Alpha0 && key <= KeyCode.Alpha9) return ((int)(key - KeyCode.Alpha0)).ToString();
                return key.ToString();
        }
    }

    // ---------- ภาพที่วาดไว้ใน Assets/Resources/UI ----------
    private static readonly Dictionary<string, Sprite> spriteCache = new Dictionary<string, Sprite>();

    /// <summary>โหลดสไปรต์จาก Resources/UI ตามชื่อไฟล์ (ไม่ต้องใส่ .png) จำไว้ใช้ซ้ำ คืน null ถ้าไม่มีไฟล์</summary>
    public static Sprite LoadSprite(string fileName)
    {
        if (spriteCache.TryGetValue(fileName, out Sprite cached)) return cached;
        Sprite sprite = Resources.Load<Sprite>("UI/" + fileName);
        spriteCache[fileName] = sprite;
        return sprite;
    }

    /// <summary>ไอคอนขั้นตอนดูแล icon_feed / bath / groom / play / rest (256 px สีตรงกับ CareStepStyle)</summary>
    public static Sprite StepIcon(CareStep step) => LoadSprite("icon_" + step.ToString().ToLowerInvariant());

    /// <summary>บับเบิลอีโมตเหนือหัวลูกค้า emote_wait / impatient / happy / angry (128 px)</summary>
    public static Sprite MoodEmote(Customer.CustomerMood mood) => LoadSprite("emote_" + mood.ToString().ToLowerInvariant());

    /// <summary>Image แสดงสไปรต์ทั้งรูปโดยรักษาสัดส่วน ใช้กับไอคอน อีโมต และโลโก้</summary>
    public static Image MakeSpriteImage(string name, Transform parent, Sprite sprite)
    {
        Image image = MakeRect(name, parent).gameObject.AddComponent<Image>();
        image.sprite = sprite;
        image.type = Image.Type.Simple;
        image.preserveAspect = true;
        image.raycastTarget = false;
        return image;
    }

    public static Image MakeImage(string name, Transform parent, Color color)
    {
        Image image = MakeRect(name, parent).gameObject.AddComponent<Image>();
        image.sprite = Rounded;
        image.type = Image.Type.Sliced;
        image.color = color;
        image.raycastTarget = false;
        return image;
    }

    /// <summary>
    /// การ์ดครีมพร้อมเงาด้านหลัง — ชิ้นส่วนหลักของหน้าตาทั้งเกม
    /// </summary>
    public static RectTransform MakeCard(string name, Transform parent, Color color)
    {
        return MakeCard(name, parent, color, out _);
    }

    public static RectTransform MakeCard(string name, Transform parent, Color color, out Image card)
    {
        RectTransform holder = MakeRect(name, parent);

        Image shadow = MakeImage("Shadow", holder, Shadow);
        shadow.rectTransform.anchorMin = Vector2.zero;
        shadow.rectTransform.anchorMax = Vector2.one;
        shadow.rectTransform.offsetMin = new Vector2(-2f, -5f);
        shadow.rectTransform.offsetMax = new Vector2(2f, 1f);

        card = MakeImage("Card", holder, color);
        Stretch(card.rectTransform);

        return holder;
    }

    /// <summary>สร้างข้อความ TextMeshProUGUI รองรับภาษาไทยและ OpenType mark positioning</summary>
    public static TextMeshProUGUI MakeText(string name, Transform parent, float fontSize, Color color, TextAlignmentOptions alignment = TextAlignmentOptions.Center)
    {
        GameObject go = MakeRect(name, parent).gameObject;
        TextMeshProUGUI text = go.AddComponent<TextMeshProUGUI>();
        text.font = TmpFont;
        text.fontSize = fontSize;
        text.color = color;
        text.alignment = alignment;
        text.textWrappingMode = TextWrappingModes.NoWrap;
        text.overflowMode = TextOverflowModes.Overflow;
        text.raycastTarget = false;
        text.extraPadding = true;
        text.textPreprocessor = ThaiTextPreprocessor.Instance;

        return text;
    }

    public static TextMeshProUGUI MakeText(string name, Transform parent, float fontSize, Color color, TextAnchor anchor)
    {
        return MakeText(name, parent, fontSize, color, ConvertAnchor(anchor));
    }

    public static TextAlignmentOptions ConvertAnchor(TextAnchor anchor)
    {
        switch (anchor)
        {
            case TextAnchor.UpperLeft: return TextAlignmentOptions.TopLeft;
            case TextAnchor.UpperCenter: return TextAlignmentOptions.Top;
            case TextAnchor.UpperRight: return TextAlignmentOptions.TopRight;
            case TextAnchor.MiddleLeft: return TextAlignmentOptions.Left;
            case TextAnchor.MiddleCenter: return TextAlignmentOptions.Center;
            case TextAnchor.MiddleRight: return TextAlignmentOptions.Right;
            case TextAnchor.LowerLeft: return TextAlignmentOptions.BottomLeft;
            case TextAnchor.LowerCenter: return TextAlignmentOptions.Bottom;
            case TextAnchor.LowerRight: return TextAlignmentOptions.BottomRight;
            default: return TextAlignmentOptions.Center;
        }
    }

    private static AudioClip clickClip;
    private static AudioSource uiAudioSource;

    /// <summary>เล่นเสียงคลิก UI ผ่าน AudioManager หรือเล่นตรงถ้ายังไม่มี AudioManager ในฉาก</summary>
    public static void PlayClickSound()
    {
        // ตรวจสอบ AudioListener ในฉากเสมอ ถ้าไม่มีให้ติดบน Main Camera หรือสร้างใหม่ทันที
        EnsureAudioListener();

        if (AudioManager.Instance != null)
        {
            AudioManager.Instance.PlayClick();
            return;
        }

        if (clickClip == null) clickClip = Resources.Load<AudioClip>("UI/ui_click");
        if (clickClip == null) return;

        if (uiAudioSource == null)
        {
            GameObject go = new GameObject("UiAudioSource", typeof(AudioSource));
            Object.DontDestroyOnLoad(go);
            uiAudioSource = go.GetComponent<AudioSource>();
            uiAudioSource.playOnAwake = false;
            uiAudioSource.spatialBlend = 0f; // 2D เต็มรูปแบบ ได้ยินชัดเจน ไม่ขึ้นกับระยะกล้อง
        }

        float sfxVol = AudioManager.GetSfxVolumeGlobal();
        if (sfxVol > 0.001f)
        {
            uiAudioSource.PlayOneShot(clickClip, sfxVol);
        }
    }

    /// <summary>ตรวจสอบให้แน่ใจว่าในฉากมี AudioListener รับฟังเสียง</summary>
    public static void EnsureAudioListener()
    {
        if (Object.FindAnyObjectByType<AudioListener>() != null) return;

        Camera cam = Camera.main != null ? Camera.main : Object.FindAnyObjectByType<Camera>();
        if (cam != null)
        {
            cam.gameObject.AddComponent<AudioListener>();
        }
        else
        {
            GameObject listenerObj = new GameObject("AudioListener", typeof(AudioListener));
            Object.DontDestroyOnLoad(listenerObj);
        }
    }

    /// <summary>
    /// ปุ่มหน้าตาเดียวกับการ์ด — ชี้แล้วสว่างขึ้น กดแล้วเข้มลง กดไม่ได้แล้วจาง
    /// มีตัวเลือก primary สำหรับปุ่มเด่น (เช่น เริ่มเกม, ไปวันถัดไป, เล่นใหม่)
    /// </summary>
    public static Button MakeButton(string name, Transform parent, float fontSize, out TextMeshProUGUI label, bool primary = false)
    {
        Color btnBg = primary ? PrimaryBtn : Card;
        RectTransform holder = MakeCard(name, parent, btnBg, out Image card);
        card.raycastTarget = true;

        Color labelColor = primary ? Color.white : Ink;
        label = MakeText("Label", holder, fontSize, labelColor, TextAlignmentOptions.Center);
        label.textWrappingMode = TextWrappingModes.Normal;
        Stretch(label.rectTransform, 10f);

        Button button = holder.gameObject.AddComponent<Button>();
        button.targetGraphic = card;
        button.transition = Selectable.Transition.ColorTint;
        button.colors = new ColorBlock
        {
            normalColor = Color.white,
            highlightedColor = primary ? PrimaryBtnHover : ButtonHover,
            pressedColor = primary ? PrimaryBtnPress : ButtonPress,
            selectedColor = Color.white,
            disabledColor = new Color(1f, 1f, 1f, 0.45f),
            colorMultiplier = 1f,
            fadeDuration = 0.08f
        };

        button.onClick.AddListener(PlayClickSound);

        return button;
    }

    /// <summary>
    /// การ์ดตัวเลือกแบบ 3 ส่วน (Header ชื่อ / Body รายละเอียด / Footer ราคาหรือเลขปุ่มลัด)
    /// ช่วยจัดวางเนื้อหาไม่ให้ล้นการ์ดและให้ตำแหน่งหัวกับท้ายตรงกันทุกใบ
    /// </summary>
    public class ChoiceCard
    {
        public RectTransform holder;
        public Button button;
        public Image background;
        public TextMeshProUGUI header;
        public TextMeshProUGUI body;
        public TextMeshProUGUI footer;
    }

    public static ChoiceCard MakeChoiceCard(string name, Transform parent, Vector2 size)
    {
        RectTransform holder = MakeCard(name, parent, Card, out Image card);
        card.raycastTarget = true;
        holder.sizeDelta = size;

        Button button = holder.gameObject.AddComponent<Button>();
        button.targetGraphic = card;
        button.transition = Selectable.Transition.ColorTint;
        button.colors = new ColorBlock
        {
            normalColor = Color.white,
            highlightedColor = ButtonHover,
            pressedColor = ButtonPress,
            selectedColor = Color.white,
            disabledColor = new Color(1f, 1f, 1f, 0.45f),
            colorMultiplier = 1f,
            fadeDuration = 0.08f
        };

        button.onClick.AddListener(PlayClickSound);

        // Header: บนสุด
        TextMeshProUGUI header = MakeText("Header", holder, 22f, Ink, TextAlignmentOptions.Top);
        header.fontStyle = FontStyles.Bold;
        header.rectTransform.anchorMin = new Vector2(0f, 1f);
        header.rectTransform.anchorMax = new Vector2(1f, 1f);
        header.rectTransform.pivot = new Vector2(0.5f, 1f);
        header.rectTransform.offsetMin = new Vector2(12f, -42f);
        header.rectTransform.offsetMax = new Vector2(-12f, -14f);

        // Footer: ล่างสุด
        TextMeshProUGUI footer = MakeText("Footer", holder, 19f, InkSoft, TextAlignmentOptions.Bottom);
        footer.rectTransform.anchorMin = new Vector2(0f, 0f);
        footer.rectTransform.anchorMax = new Vector2(1f, 0f);
        footer.rectTransform.pivot = new Vector2(0.5f, 0f);
        footer.rectTransform.offsetMin = new Vector2(12f, 12f);
        footer.rectTransform.offsetMax = new Vector2(-12f, 44f);

        // Body: ตรงกลาง
        TextMeshProUGUI body = MakeText("Body", holder, 18f, Ink, TextAlignmentOptions.Center);
        body.textWrappingMode = TextWrappingModes.Normal;
        body.lineSpacing = 1.3f;
        body.rectTransform.anchorMin = Vector2.zero;
        body.rectTransform.anchorMax = Vector2.one;
        body.rectTransform.offsetMin = new Vector2(14f, 46f);
        body.rectTransform.offsetMax = new Vector2(-14f, -44f);

        return new ChoiceCard
        {
            holder = holder,
            button = button,
            background = card,
            header = header,
            body = body,
            footer = footer
        };
    }

    /// <summary>
    /// แถวตัวเลือกปรับค่าระดับเสียง (หรือค่า 0–1 ใดๆ) ประกอบด้วย ชื่อหมวด, แถบเลื่อน (Slider), และตัวเลข %
    /// </summary>
    public class VolumeSliderRow
    {
        public RectTransform holder;
        public TextMeshProUGUI label;
        public Slider slider;
        public TextMeshProUGUI valueText;
    }

    public static VolumeSliderRow MakeSliderRow(string name, Transform parent, string labelText, float initialValue, System.Action<float> onValueChanged, float width = 380f)
    {
        RectTransform holder = MakeRect(name, parent);
        holder.sizeDelta = new Vector2(width, 36f);

        // ข้อความชื่อหมวดทางซ้าย
        float labelWidth = 130f;
        float valueWidth = 54f;
        float sliderWidth = width - labelWidth - valueWidth - 16f;

        TextMeshProUGUI label = MakeText("Label", holder, 18f, Ink, TextAlignmentOptions.Left);
        label.text = labelText;
        Place(label.rectTransform, new Vector2(0f, -4f), new Vector2(labelWidth, 28f));

        // ข้อความตัวเลข % ทางขวาสุด
        TextMeshProUGUI valText = MakeText("Value", holder, 18f, InkSoft, TextAlignmentOptions.Right);
        valText.text = $"{Mathf.RoundToInt(initialValue * 100f)}%";
        Place(valText.rectTransform, new Vector2(width - valueWidth, -4f), new Vector2(valueWidth, 28f));

        // ตัว Slider ตรงกลาง
        GameObject sliderGo = new GameObject("Slider", typeof(RectTransform), typeof(Slider));
        sliderGo.transform.SetParent(holder, false);
        Slider slider = sliderGo.GetComponent<Slider>();
        Place(slider.GetComponent<RectTransform>(), new Vector2(labelWidth + 8f, -10f), new Vector2(sliderWidth, 16f));

        // รางพื้นหลัง (Track)
        GameObject bgGo = new GameObject("Background", typeof(RectTransform), typeof(Image));
        bgGo.transform.SetParent(sliderGo.transform, false);
        Image bgImage = bgGo.GetComponent<Image>();
        bgImage.sprite = Rounded;
        bgImage.type = Image.Type.Sliced;
        bgImage.color = Track;
        Stretch(bgImage.rectTransform);

        // พื้นที่เติมสี (Fill Area)
        GameObject fillArea = new GameObject("Fill Area", typeof(RectTransform));
        fillArea.transform.SetParent(sliderGo.transform, false);
        RectTransform fillAreaRect = fillArea.GetComponent<RectTransform>();
        Stretch(fillAreaRect);
        fillAreaRect.offsetMin = new Vector2(0f, 0f);
        fillAreaRect.offsetMax = new Vector2(0f, 0f);

        GameObject fill = new GameObject("Fill", typeof(RectTransform), typeof(Image));
        fill.transform.SetParent(fillArea.transform, false);
        Image fillImage = fill.GetComponent<Image>();
        fillImage.sprite = Rounded;
        fillImage.type = Image.Type.Sliced;
        fillImage.color = PrimaryBtn;
        fillImage.rectTransform.sizeDelta = Vector2.zero;

        // ปุ่มเลื่อน (Handle)
        GameObject handleArea = new GameObject("Handle Slide Area", typeof(RectTransform));
        handleArea.transform.SetParent(sliderGo.transform, false);
        RectTransform handleAreaRect = handleArea.GetComponent<RectTransform>();
        Stretch(handleAreaRect);

        GameObject handle = new GameObject("Handle", typeof(RectTransform), typeof(Image));
        handle.transform.SetParent(handleArea.transform, false);
        Image handleImage = handle.GetComponent<Image>();
        handleImage.sprite = Circle;
        handleImage.color = Card;
        handleImage.rectTransform.sizeDelta = new Vector2(20f, 20f);

        slider.fillRect = fillImage.rectTransform;
        slider.handleRect = handleImage.rectTransform;
        slider.targetGraphic = handleImage;
        slider.direction = Slider.Direction.LeftToRight;
        slider.minValue = 0f;
        slider.maxValue = 1f;
        slider.value = initialValue;

        slider.onValueChanged.AddListener((val) =>
        {
            valText.text = $"{Mathf.RoundToInt(val * 100f)}%";
            onValueChanged?.Invoke(val);
        });

        return new VolumeSliderRow
        {
            holder = holder,
            label = label,
            slider = slider,
            valueText = valText
        };
    }

    public static void Stretch(RectTransform rect, float padding = 0f)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = new Vector2(padding, padding);
        rect.offsetMax = new Vector2(-padding, -padding);
    }

    /// <summary>วางของโดยอิงมุมซ้ายบนของพ่อ อ่านง่ายกว่าคิด anchor ใหม่ทุกครั้ง</summary>
    public static void Place(RectTransform rect, Vector2 topLeftOffset, Vector2 size)
    {
        rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition = topLeftOffset;
        rect.sizeDelta = size;
    }

    /// <summary>จัดของไว้กลางพ่อ ตามขนาดที่กำหนด</summary>
    public static void Center(RectTransform rect, Vector2 size, Vector2 offset = default)
    {
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = offset;
        rect.sizeDelta = size;
    }

    // ---------- สไปรต์ที่วาดเอง ----------

    private static Sprite CreateRoundedSprite(int size, int radius)
    {
        Texture2D texture = NewTexture(size);

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                // ระยะจากจุดศูนย์กลางของมุมที่ใกล้ที่สุด — นอกรัศมี = โปร่งใส ไล่ขอบ 1 พิกเซลกันหยัก
                float dx = Mathf.Max(radius - x - 0.5f, x + 0.5f - (size - radius), 0f);
                float dy = Mathf.Max(radius - y - 0.5f, y + 0.5f - (size - radius), 0f);
                float alpha = Mathf.Clamp01(radius - Mathf.Sqrt(dx * dx + dy * dy));

                texture.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
            }
        }

        texture.Apply();

        return Sprite.Create(texture, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), 100f, 0,
            SpriteMeshType.FullRect, new Vector4(radius, radius, radius, radius));
    }

    /// <summary>วงแหวน หรือวงกลมทึบถ้า thickness เท่ารัศมี</summary>
    private static Sprite CreateRingSprite(int size, int thickness)
    {
        Texture2D texture = NewTexture(size);

        float center = size * 0.5f;
        float outer = center - 0.5f;
        float inner = outer - thickness;

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float distance = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(center, center));
                float alpha = Mathf.Clamp01(outer - distance) * (inner <= 0f ? 1f : Mathf.Clamp01(distance - inner));

                texture.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
            }
        }

        texture.Apply();

        return Sprite.Create(texture, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect);
    }

    private static Texture2D NewTexture(int size)
    {
        return new Texture2D(size, size, TextureFormat.RGBA32, false)
        {
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear
        };
    }
}
