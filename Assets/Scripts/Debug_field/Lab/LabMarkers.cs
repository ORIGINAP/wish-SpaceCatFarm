using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class LabMarkers : MonoBehaviour
{
    // HUD 여백
    private const float InsetSide = 50f, InsetTop = 96f, InsetBottom = 140f;

    private sealed class Mark
    {
        public Vector2 pos;
        public Color   color;
        public string  label;
        public bool    edge;
        public RectTransform arrow;
        public Image   arrowImg;
        public TextMeshProUGUI text;
    }

    private static LabMarkers instance;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => instance = null;
    private readonly Dictionary<object, Mark> marks = new();
    private RectTransform root;

    private static LabMarkers Instance
    {
        get
        {
            if (instance != null) return instance;
            var go = new GameObject("LabMarkers", typeof(RectTransform));
            UIStyleKit.CreateOverlayCanvas(go, 24).GetComponent<GraphicRaycaster>().enabled = false;
            instance = go.AddComponent<LabMarkers>();
            instance.root = (RectTransform)go.transform;
            return instance;
        }
    }

    public static void Set(object key, Vector2 worldPos, Color color, string label, bool edge = true)
    {
        var self = Instance;
        if (!self.marks.TryGetValue(key, out var m)) self.marks[key] = m = new Mark();
        m.pos   = worldPos;
        m.color = color;
        m.label = label;
        m.edge  = edge;
        MinimapUI.SetMarker(key, worldPos, color);
    }

    public static void Remove(object key)
    {
        MinimapUI.RemoveMarker(key);
        if (instance == null || !instance.marks.TryGetValue(key, out var m)) return;
        if (m.arrow != null) Destroy(m.arrow.gameObject);
        instance.marks.Remove(key);
    }

    private void OnDestroy()
    {
        foreach (var key in marks.Keys) MinimapUI.RemoveMarker(key);
        if (instance == this) instance = null;
    }

    private void LateUpdate()
    {
        var cam = Camera.main;
        if (cam == null) return;
        var canvasScale = root.lossyScale.x > 0f ? root.lossyScale.x : 1f;
        Vector2 half = new Vector2(Screen.width, Screen.height) * 0.5f / canvasScale;
        Vector2 me   = Lab.LocalPos;

        foreach (var m in marks.Values)
        {
            Vector3 sp = cam.WorldToScreenPoint(new Vector3(m.pos.x, m.pos.y, 0f));
            bool onScreen = sp.x > 0f && sp.x < Screen.width && sp.y > 0f && sp.y < Screen.height;
            bool show = m.edge && !onScreen;
            if (!show)
            {
                if (m.arrow != null && m.arrow.gameObject.activeSelf) m.arrow.gameObject.SetActive(false);
                continue;
            }
            if (m.arrow == null) BuildArrow(m);
            m.arrow.gameObject.SetActive(true);

            Vector2 dir = new Vector2(sp.x - Screen.width * 0.5f, sp.y - Screen.height * 0.5f) / canvasScale;
            if (dir.sqrMagnitude < 0.01f) dir = Vector2.up;
            float sx = (half.x - InsetSide) / Mathf.Max(0.001f, Mathf.Abs(dir.x));
            float sy = (half.y - (dir.y > 0f ? InsetTop : InsetBottom)) / Mathf.Max(0.001f, Mathf.Abs(dir.y));
            Vector2 at = dir * Mathf.Min(sx, sy);
            m.arrow.anchoredPosition = at;
            m.text.rectTransform.anchoredPosition = new Vector2(0f, at.y < 0f ? 58f : -2f);
            m.arrowImg.rectTransform.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg - 90f);
            m.arrowImg.color = m.color;
            float dist = Vector2.Distance(me, m.pos);
            string t = string.IsNullOrEmpty(m.label) ? $"{dist:0}m" : $"{m.label}\n<size=80%>{dist:0}m</size>";
            if (m.text.text != t) m.text.text = t;
        }
    }

    private static readonly string[] ArrowRows =
    {
        "....##....",
        "...####...",
        "..######..",
        ".########.",
        "##########",
        "...####...",
        "...####...",
    };

    private void BuildArrow(Mark m)
    {
        var go = new GameObject("Arrow", typeof(RectTransform));
        go.transform.SetParent(root, false);
        m.arrow = (RectTransform)go.transform;
        m.arrow.anchorMin = m.arrow.anchorMax = new Vector2(0.5f, 0.5f);
        m.arrow.sizeDelta = new Vector2(30f, 30f);

        var bg = CuteUIKit.Layer(m.arrow, "Bg", CuteUIKit.Rounded(15), new Color(1f, 0.97f, 0.92f, 0.92f));
        bg.rectTransform.offsetMin = bg.rectTransform.offsetMax = Vector2.zero;
        var arrowGO = new GameObject("Tip", typeof(RectTransform));
        arrowGO.transform.SetParent(m.arrow, false);
        m.arrowImg = arrowGO.AddComponent<Image>();
        m.arrowImg.sprite = LabArt.Pixel("edgeArrow", ArrowRows, "##FFFFFF", new Vector2(0.5f, 0.5f));
        m.arrowImg.raycastTarget = false;
        var art = m.arrowImg.rectTransform;
        art.sizeDelta = new Vector2(20f, 14f);

        m.text = CuteUIKit.Text(m.arrow, "", 11f, CuteUIKit.Cocoa, FontStyles.Bold);
        m.text.outlineWidth = 0.25f;
        m.text.outlineColor = new Color32(255, 247, 234, 255);
        var trt = m.text.rectTransform;
        trt.anchorMin = trt.anchorMax = new Vector2(0.5f, 0f);
        trt.pivot     = new Vector2(0.5f, 1f);
        trt.anchoredPosition = new Vector2(0f, -2f);
        trt.sizeDelta = new Vector2(120f, 30f);
    }
}
