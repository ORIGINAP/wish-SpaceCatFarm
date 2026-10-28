using UnityEngine;
using UnityEngine.UI;

public class CatPortrait : MonoBehaviour
{
    private Image   image;
    private int     scale;
    private bool    animate;
    private CatLook look = new();
    private int     frame;
    private float   timer;

    public static CatPortrait Create(Transform parent, Vector2 size, int scale, bool animate, Vector2 pivotOffset)
    {
        var box = new GameObject("CatPortrait", typeof(RectTransform));
        box.transform.SetParent(parent, false);
        ((RectTransform)box.transform).sizeDelta = size;
        var le = box.AddComponent<LayoutElement>();
        le.preferredWidth  = le.minWidth  = size.x;
        le.preferredHeight = le.minHeight = size.y;

        var cat = new GameObject("Cat", typeof(RectTransform));
        cat.transform.SetParent(box.transform, false);
        var rt = (RectTransform)cat.transform;
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = pivotOffset;

        var portrait = box.AddComponent<CatPortrait>();
        portrait.image = cat.AddComponent<Image>();
        portrait.image.raycastTarget = false;
        portrait.scale   = scale;
        portrait.animate = animate;
        portrait.Show();
        return portrait;
    }

    public void Set(CatLook value)
    {
        look = value != null ? value.Copy() : new CatLook();
        Show();
    }

    private void Update()
    {
        if (!animate) return;
        var frames = CatFrames.Sprites;
        if (frames.Count < 2) return;

        float step = CatFrames.FrameSeconds;
        timer += Time.unscaledDeltaTime;
        if (timer < step) return;
        // 오래 멈춰도 한 칸씩만
        timer = Mathf.Repeat(timer, step);
        frame = (frame + 1) % frames.Count;
        Show();
    }

    private void Show()
    {
        var frames = CatFrames.Sprites;
        var sprite = frames.Count > 0 ? CatSkin.Get(frames[frame % frames.Count], look.color, look.accessory) : null;
        image.enabled = sprite != null;
        if (sprite == null) return;

        image.sprite = sprite;
        var rt = image.rectTransform;
        rt.sizeDelta = sprite.rect.size * scale;
        rt.pivot     = new Vector2(sprite.pivot.x / sprite.rect.width, sprite.pivot.y / sprite.rect.height);
    }
}
