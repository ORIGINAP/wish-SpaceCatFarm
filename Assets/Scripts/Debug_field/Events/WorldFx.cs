using UnityEngine;

public static class WorldFx
{
    public static int TopLayer
    {
        get
        {
            var layers = SortingLayer.layers;
            return layers.Length > 0 ? layers[layers.Length - 1].id : 0;
        }
    }

    public static Material Material()
    {
        var p  = PlayerTileController.Instance;
        var sr = p != null ? p.GetComponent<SpriteRenderer>() : null;
        return sr != null ? sr.sharedMaterial : null;
    }

    public static SpriteRenderer Sprite(Transform parent, string name, Sprite sprite, Color color, int order, float worldSize)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = sprite;
        sr.color  = color;
        var mat = Material();
        if (mat != null) sr.sharedMaterial = mat;
        sr.sortingLayerID = TopLayer;
        sr.sortingOrder   = order;
        float w = sprite != null ? sprite.bounds.size.x : 1f;
        go.transform.localScale = Vector3.one * (worldSize / Mathf.Max(0.001f, w));
        return sr;
    }
}
