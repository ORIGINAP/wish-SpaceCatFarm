using UnityEngine;

[DefaultExecutionOrder(-50)]
[RequireComponent(typeof(SpriteRenderer))]
public class CatSkinRenderer : MonoBehaviour
{
    private SpriteRenderer sr;
    private int    color, accessory;
    private Sprite shown;

    public void SetLook(CatLook look) => SetLook(look != null ? look.color : 0, look != null ? look.accessory : 0);

    public void SetLook(int color, int accessory)
    {
        this.color     = color;
        this.accessory = accessory;
        shown = null;
        Apply();
    }

    private void LateUpdate() => Apply();

    private void Apply()
    {
        if (sr == null) sr = GetComponent<SpriteRenderer>();
        var current = sr.sprite;
        if (current == null || current == shown) return;
        shown = CatSkin.Get(current, color, accessory);
        sr.sprite = shown;
    }
}
