using TMPro;
using UnityEngine;

public class ChatBubble : MonoBehaviour
{
    private const float Width    = 4.5f;
    private const float FontSize = 2.6f;
    private const float Border   = 0.04f;
    private const float TailSize = 0.19f;

    private Transform      root;
    private TextMeshPro    text;
    private SpriteRenderer border, fill;
    private Transform      tail;
    private float          headroom;
    private float          until;

    public static void Show(Transform target, string message, float headroom)
    {
        if (target == null || string.IsNullOrEmpty(message)) return;
        var bubble = target.GetComponent<ChatBubble>();
        if (bubble == null) bubble = target.gameObject.AddComponent<ChatBubble>();
        bubble.Say(message, headroom);
    }

    public static float SpriteTop(Transform t)
    {
        var sr = t != null ? t.GetComponent<SpriteRenderer>() : null;
        return sr != null && sr.sprite != null ? sr.bounds.max.y - t.position.y : 0.5f;
    }

    private void Say(string message, float headroomAbove)
    {
        if (root == null) Build();
        headroom = headroomAbove;
        root.gameObject.SetActive(true);
        // 입력 글자 서식 해석 막기
        text.text = $"<noparse>{message.Replace("</noparse>", "")}</noparse>";

        text.ForceMeshUpdate();
        var b    = text.textBounds;
        var size = new Vector2(b.size.x + 0.35f, b.size.y + 0.2f);
        border.transform.localPosition = new Vector3(b.center.x, b.center.y, 0.01f);
        border.size = size;
        fill.size   = size - new Vector2(Border * 2f, Border * 2f);
        tail.localPosition = new Vector3(0f, -size.y * 0.5f, 0f);

        until = Time.time + Mathf.Clamp(2f + message.Length * 0.08f, 3f, 8f);
        LateUpdate();
    }

    private void LateUpdate()
    {
        if (root == null || !root.gameObject.activeSelf) return;
        if (Time.time > until)
        {
            root.gameObject.SetActive(false);
            return;
        }
        root.position = transform.position + new Vector3(0f, headroom + 0.23f, 0f);
    }

    private void OnDestroy()
    {
        if (root != null) Destroy(root.gameObject);
    }

    private void Build()
    {
        var layers   = SortingLayer.layers;
        int topLayer = layers.Length > 0 ? layers[layers.Length - 1].id : 0;

        var go = new GameObject($"ChatBubble_{name}");
        text = go.AddComponent<TextMeshPro>();
        root = go.transform;
        text.fontSize         = FontSize;
        text.color            = CuteUIKit.Cocoa;
        text.alignment        = TextAlignmentOptions.Bottom;
        text.textWrappingMode = TextWrappingModes.Normal;
        text.rectTransform.pivot     = new Vector2(0.5f, 0f);
        text.rectTransform.sizeDelta = new Vector2(Width, 3f);
        text.sortingLayerID = topLayer;
        text.sortingOrder   = 200;

        border = Sprite(root, "Background", CuteUIKit.Rounded(12), CuteUIKit.Latte, 198, topLayer);
        fill   = Sprite(border.transform, "Fill", CuteUIKit.Rounded(8), CuteUIKit.Cream, 199, topLayer);
        var t  = Sprite(border.transform, "Tail", CuteUIKit.Rounded(3), CuteUIKit.Latte, 198, topLayer);
        t.size = new Vector2(TailSize, TailSize);
        tail = t.transform;
        tail.localRotation = Quaternion.Euler(0f, 0f, 45f);
        float tailFill = TailSize - Border * 2f;
        Sprite(tail, "Fill", CuteUIKit.Rounded(2), CuteUIKit.Cream, 199, topLayer).size = new Vector2(tailFill, tailFill);
    }

    private static SpriteRenderer Sprite(Transform parent, string objName, Sprite sprite, Color color, int order, int layer)
    {
        var go = new GameObject(objName);
        go.transform.SetParent(parent, false);
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite         = sprite;
        sr.drawMode       = SpriteDrawMode.Sliced;
        sr.sharedMaterial = WorldConnectionLine.SharedMaterial;
        sr.color          = color;
        sr.sortingLayerID = layer;
        sr.sortingOrder   = order;
        return sr;
    }
}
