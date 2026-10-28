using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class QuantityField : MonoBehaviour
{
    public int Value { get; private set; } = 1;

    public event Action<int> Changed;

    private TMP_InputField input;
    private Func<int> getMax;

    private int Max => Mathf.Max(1, getMax != null ? getMax() : 1);

    public static QuantityField Create(Transform parent, Func<int> getMax, float height = 22f, float fontSize = 11f, bool compact = false)
    {
        var row = new GameObject("Quantity", typeof(RectTransform));
        row.transform.SetParent(parent, false);
        var le = row.AddComponent<LayoutElement>();
        le.preferredHeight = height;
        le.flexibleWidth   = 1f;
        le.flexibleHeight  = 0f;
        var hlg = row.AddComponent<HorizontalLayoutGroup>();
        hlg.spacing                = 3f;
        hlg.childControlWidth      = true;
        hlg.childControlHeight     = true;
        hlg.childForceExpandWidth  = false;
        hlg.childForceExpandHeight = true;

        var q = row.AddComponent<QuantityField>();
        q.getMax = getMax;

        float stepW = compact ? 18f : 22f, wordW = compact ? 28f : 34f;
        CuteUIKit.Button(row.transform, "-", CuteUIKit.ButtonLatte, () => q.Set(q.Value - Step()), stepW, height, fontSize)
            .gameObject.AddComponent<GamepadHoldRepeat>();
        q.input = BuildInput(row.transform, fontSize + 1f);
        CuteUIKit.Button(row.transform, "+", CuteUIKit.ButtonLatte, () => q.Set(q.Value + Step()), stepW, height, fontSize)
            .gameObject.AddComponent<GamepadHoldRepeat>();
        CuteUIKit.Button(row.transform, "최소", CuteUIKit.ButtonLatte, () => q.Set(1), wordW, height, fontSize - 1f);
        CuteUIKit.Button(row.transform, "최대", CuteUIKit.ButtonButter, () => q.Set(q.Max), wordW, height, fontSize - 1f);

        q.input.onEndEdit.AddListener(text => q.Set(int.TryParse(text, out int v) ? v : q.Value));
        q.Show();
        return q;
    }

    public void Set(int value)
    {
        value = Mathf.Clamp(value, 1, Max);
        bool changed = value != Value;
        Value = value;
        Show();
        if (changed) Changed?.Invoke(value);
    }

    public void Clamp() => Set(Value);

    private void Show()
    {
        if (input != null && !input.isFocused) input.SetTextWithoutNotify(Value.ToString());
    }

    private void Update()
    {
        if (input != null && input.isFocused) TextInputFocus.MarkTyping();
    }

    private static int Step() => GameInput.GetKey(KeyCode.LeftShift) || GameInput.GetKey(KeyCode.RightShift) ? 10 : 1;

    private static TMP_InputField BuildInput(Transform parent, float fontSize)
    {
        var box = new GameObject("Input", typeof(RectTransform), typeof(Image));
        box.transform.SetParent(parent, false);
        var le = box.AddComponent<LayoutElement>();
        le.flexibleWidth = 1f;
        le.minWidth      = 30f;

        var area = new GameObject("TextArea", typeof(RectTransform), typeof(RectMask2D));
        area.transform.SetParent(box.transform, false);
        var art = (RectTransform)area.transform;
        art.anchorMin = Vector2.zero;
        art.anchorMax = Vector2.one;
        art.offsetMin = new Vector2(3f, 0f);
        art.offsetMax = new Vector2(-3f, 0f);

        var text = CuteUIKit.Text(area.transform, "1", fontSize, CuteUIKit.Cocoa, FontStyles.Bold);
        var trt  = text.rectTransform;
        trt.anchorMin = Vector2.zero;
        trt.anchorMax = Vector2.one;
        trt.offsetMin = trt.offsetMax = Vector2.zero;

        var input = box.AddComponent<TMP_InputField>();
        input.textViewport   = art;
        input.textComponent  = text;
        input.contentType    = TMP_InputField.ContentType.IntegerNumber;
        input.lineType       = TMP_InputField.LineType.SingleLine;
        input.characterLimit = 4;
        input.richText       = false;
        CuteUIKit.StyleInputField(input);
        var colors = input.colors;
        colors.highlightedColor = colors.selectedColor = Color.white;
        input.colors = colors;
        return input;
    }
}
