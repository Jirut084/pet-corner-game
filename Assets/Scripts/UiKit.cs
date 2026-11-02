using System.Collections.Generic;
using UnityEngine;
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
    public static readonly Color Card = new Color(0.99f, 0.97f, 0.93f, 0.97f);
    public static readonly Color CardQuiet = new Color(0.99f, 0.97f, 0.93f, 0.93f);
    public static readonly Color Shadow = new Color(0.12f, 0.10f, 0.08f, 0.13f);
    public static readonly Color Ink = new Color(0.16f, 0.14f, 0.11f);
    public static readonly Color InkSoft = new Color(0.16f, 0.14f, 0.11f, 0.78f);
    public static readonly Color Track = new Color(0.16f, 0.14f, 0.11f, 0.18f);

    public static readonly Color Money = new Color(0.85f, 0.62f, 0.15f);
    public static readonly Color Good = new Color(0.40f, 0.72f, 0.45f);
    public static readonly Color Warn = new Color(0.93f, 0.72f, 0.26f);
    public static readonly Color Danger = new Color(0.85f, 0.35f, 0.30f);

    /// <summary>ม่านทึบหลังหน้าจอเมนู — เข้มพอให้อ่านการ์ดได้ แต่ยังเห็นร้านอยู่ข้างหลังจางๆ</summary>
    public static readonly Color Dim = new Color(0.13f, 0.11f, 0.09f, 0.86f);

    private static Font font;
    private static Sprite rounded;
    private static Sprite circle;
    private static Sprite ring;

    public static Font Font
    {
        get
        {
            // ฟอนต์ไทย Mitr (OFL) อยู่ใน Resources/UI จึงติดไปกับ build ด้วย ถ้าหาไม่เจอค่อยถอยไปใช้ฟอนต์ในตัว Unity
            if (font == null) font = Resources.Load<Font>("UI/Mitr-SemiBold");
            if (font == null) font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            return font;
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
        canvas.pixelPerfect = true; // ขอบและตัวหนังสือตกบนพิกเซลพอดี ไม่เบลอครึ่งพิกเซล

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
    /// การ์ดครีมพร้อมเงาจางด้านหลัง — ชิ้นส่วนหลักของหน้าตาทั้งเกม
    /// คืน RectTransform ของตัวห่อ: จัดตำแหน่ง/ขนาดที่ตัวนี้ตัวเดียว เงากับพื้นการ์ดยืดตามเอง
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
        shadow.rectTransform.offsetMin = new Vector2(-1f, -4f);
        shadow.rectTransform.offsetMax = new Vector2(1f, 0f);

        card = MakeImage("Card", holder, color);
        Stretch(card.rectTransform);

        return holder;
    }

    public static Text MakeText(string name, Transform parent, int fontSize, Color color, TextAnchor anchor)
    {
        Text text = MakeRect(name, parent).gameObject.AddComponent<Text>();
        text.font = Font;
        text.fontSize = fontSize;
        text.fontStyle = FontStyle.Normal; // Mitr SemiBold หนาในตัวอยู่แล้ว ถ้าสั่ง Bold ซ้ำ Unity จะปั้นตัวหนาปลอมจนตัวไทยตัวเล็กแตกเป็นก้อน
        text.color = color;
        text.alignment = anchor;
        text.horizontalOverflow = HorizontalWrapMode.Overflow;
        text.verticalOverflow = VerticalWrapMode.Overflow;
        text.raycastTarget = false;
        return text;
    }

    /// <summary>
    /// ปุ่มหน้าตาเดียวกับการ์ด — ชี้แล้วสว่างขึ้น กดแล้วเข้มลง กดไม่ได้แล้วจาง
    /// คืน Button ส่วนข้อความอยู่ใน label ให้เปลี่ยนทีหลังได้
    /// </summary>
    public static Button MakeButton(string name, Transform parent, int fontSize, out Text label)
    {
        RectTransform holder = MakeCard(name, parent, Card, out Image card);
        card.raycastTarget = true;

        label = MakeText("Label", holder, fontSize, Ink, TextAnchor.MiddleCenter);
        label.horizontalOverflow = HorizontalWrapMode.Wrap;
        Stretch(label.rectTransform, 14f);

        Button button = holder.gameObject.AddComponent<Button>();
        button.targetGraphic = card;
        button.transition = Selectable.Transition.ColorTint;
        button.colors = new ColorBlock
        {
            normalColor = Color.white,
            highlightedColor = new Color(1.03f, 1.02f, 1f),
            pressedColor = new Color(0.90f, 0.88f, 0.84f),
            selectedColor = Color.white,
            disabledColor = new Color(1f, 1f, 1f, 0.45f),
            colorMultiplier = 1f,
            fadeDuration = 0.08f
        };

        return button;
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
