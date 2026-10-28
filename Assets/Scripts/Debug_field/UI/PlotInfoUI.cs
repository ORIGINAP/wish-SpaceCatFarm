using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class PlotInfoUI : MonoBehaviour
{
    private const float RefreshInterval = 0.15f;

    private RectTransform   card;
    private CanvasGroup     group;
    private TextMeshProUGUI text;
    private RectTransform   canvasRT;
    private Camera          cam;
    private PlantTile       shown;
    private float           nextRefresh;
    private readonly List<GrowthFactor> factors = new();
    private readonly StringBuilder sb = new();

    private void Awake() => Build();

    private void Update()
    {
        var plot = Hovered();
        if (plot == null)
        {
            // 해체로 사라진 밭이면 숨김
            if (group.alpha > 0f) { shown = null; group.alpha = 0f; }
            return;
        }

        if (plot != shown || Time.unscaledTime >= nextRefresh)
        {
            shown       = plot;
            nextRefresh = Time.unscaledTime + RefreshInterval;
            text.text   = Describe(plot);
        }
        group.alpha = 1f;
        Follow();
    }

    private PlantTile Hovered()
    {
        var gm = GridManager.Instance;
        if (gm == null) return null;
        if (GameInput.PointerOverUI) return null;
        if (PlayerTileController.IsPauseMenuOpen()) return null;
        var held = PlayerTileController.Instance != null ? PlayerTileController.Instance.SelectedItem : null;
        if (held != null && held.itemType == FarmingItemType.Tile) return null;
        if (cam == null) cam = Camera.main;
        if (cam == null) return null;

        Vector3 mp = InputManager.Instance != null ? InputManager.Instance.CursorScreenPos : GameInput.MousePosition;
        mp.z = -cam.transform.position.z;
        var tile = gm.GetTile(gm.WorldToGrid(cam.ScreenToWorldPoint(mp)));
        return tile is PlantTile plot && !plot.IsGhost ? plot : null;
    }

    private void Follow()
    {
        Vector3 mp = InputManager.Instance != null ? InputManager.Instance.CursorScreenPos : GameInput.MousePosition;
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRT, mp, null, out var local)) return;
        Vector2 size = card.rect.size;
        Vector2 half = canvasRT.rect.size * 0.5f;
        Vector2 pos  = local + new Vector2(22f, -22f);
        if (pos.x + size.x > half.x - 6f)  pos.x = local.x - size.x - 22f;
        if (pos.y - size.y < -half.y + 6f) pos.y = local.y + size.y + 22f;
        card.anchoredPosition = pos;
    }

    private string Describe(PlantTile plot)
    {
        sb.Clear();
        var seed    = plot.CurrentSeed;
        var product = ProductOf(seed);

        if (seed == null)
        {
            sb.Append("<b>빈 밭</b>\n");
            sb.Append(plot.IsPowered ? "<color=#3F8A66>전기 들어옴</color>" : "<color=#C9485B>전기 없음</color>");
            sb.Append(Pad.Active ? " · 씨앗을 들고 RT로 심어요" : " · 씨앗을 들고 클릭해 심어요");
        }
        else
        {
            sb.Append("<b>").Append(product != null ? product.itemName : seed.seedName).Append("</b>");
            if (seed.trait != CropTrait.None) sb.Append("  <color=#6E57B5>[").Append(CropTraits.Name(seed.trait)).Append("]</color>");
            sb.Append('\n');

            if (plot.CurrentState == PlantTile.PlantState.ReadyToHarvest)
                sb.Append(Pad.Active ? "<color=#3F8A66>다 자랐어요! A로 수확</color>" : "<color=#3F8A66>다 자랐어요! 클릭해서 수확</color>");
            else if (!plot.CanGrow)
                sb.Append($"<color=#C9485B>전기가 없어 멈췄어요</color> ({plot.Progress01 * 100f:0}%)");
            else
            {
                float left = plot.RemainingSeconds;
                sb.Append($"자라는 중 {plot.Progress01 * 100f:0}%");
                if (left >= 0f) sb.Append($" · 약 {FormatSeconds(left)} 남음");
            }

            if (seed.trait != CropTrait.None) sb.Append("\n<color=#A88E82>").Append(CropTraits.Describe(seed.trait)).Append("</color>");

            float mult = FarmConditions.GrowthMultiplier(plot, factors);
            if (factors.Count > 0)
            {
                sb.Append($"\n성장 <b>×{mult:0.##}</b> <color=#A88E82>(");
                for (int i = 0; i < factors.Count; i++)
                {
                    if (i > 0) sb.Append(" · ");
                    sb.Append(factors[i].label).Append(" ×").Append(factors[i].mult.ToString("0.##"));
                }
                sb.Append(")</color>");
            }

            float bonus    = FarmConditions.TraitBonusChance(plot);
            float research = TechTreeManager.ExtraYieldChance;
            if (bonus + research > 0f)
            {
                string why = bonus > 0f && research > 0f ? $"{CropTraits.Name(seed.trait)} · 풍성한 수확 연구"
                           : bonus > 0f ? CropTraits.Name(seed.trait) : "풍성한 수확 연구";
                sb.Append($"\n수확 보너스 <b>+{bonus + research:0.#}개</b> <color=#A88E82>({why})</color>");
            }
        }

        sb.Append('\n');
        if (plot.IsFertile)
        {
            var heartyNote = seed != null && seed.trait == CropTrait.Hearty ? "+2" : "+1";
            sb.Append($"<color=#8A5A3C>퇴비 {plot.Fertility}회 남음</color> <color=#A88E82>(수확 {heartyNote} · 황금 확률 2배)</color>");
        }
        else
            sb.Append(Pad.Active ? "<color=#A88E82>퇴비 없음 — 퇴비를 들고 RT를 누르면 수확이 늘어요</color>"
                                 : "<color=#A88E82>퇴비 없음 — 퇴비를 들고 클릭하면 수확이 늘어요</color>");

        if (FarmConditions.IsWatered(plot.GridPosition)) sb.Append("\n<color=#3F78AE>물을 받고 있어요</color>");
        if (FarmConditions.InGreenhouse(plot.GridPosition)) sb.Append("  <color=#3F8A66>온실이 지켜 줘요</color>");

        if (product != null && product.goldenVariant != null)
            sb.Append($"\n<color=#A8741A>황금 작물 확률 {FarmConditions.GoldenChance(plot, plot.IsFertile) * 100f:0.#}%</color>");
        return sb.ToString();
    }

    private static FarmingItemData ProductOf(SeedData seed)
    {
        if (seed?.drops == null) return null;
        foreach (var d in seed.drops)
            if (d.item != null && d.item.isCropProduct) return d.item;
        return null;
    }

    private static string FormatSeconds(float s)
    {
        int t = Mathf.CeilToInt(s);
        return t >= 60 ? $"{t / 60}분 {t % 60:00}초" : $"{t}초";
    }

    private void Build()
    {
        var canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode   = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 430;
        var scaler = gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode         = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = UiScale.Reference;
        scaler.screenMatchMode     = CanvasScaler.ScreenMatchMode.Expand;
        UiScale.Track(scaler);
        canvasRT = (RectTransform)transform;

        var img = CuteUIKit.Card(transform, CuteUIKit.Cream, 12, "PlotInfo");
        img.raycastTarget = false;
        card = img.rectTransform;
        card.anchorMin = card.anchorMax = new Vector2(0.5f, 0.5f);
        card.pivot     = new Vector2(0f, 1f);
        var outline = img.gameObject.AddComponent<Outline>();
        outline.effectColor    = CuteUIKit.Latte;
        outline.effectDistance = new Vector2(2f, -2f);

        var vlg = img.gameObject.AddComponent<VerticalLayoutGroup>();
        vlg.padding = new RectOffset(12, 12, 8, 9);
        vlg.childControlWidth  = true;
        vlg.childControlHeight = true;
        var fit = img.gameObject.AddComponent<ContentSizeFitter>();
        fit.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
        fit.verticalFit   = ContentSizeFitter.FitMode.PreferredSize;

        text = CuteUIKit.Text(card, "", 12f, CuteUIKit.Cocoa, FontStyles.Normal, TextAlignmentOptions.TopLeft);
        text.raycastTarget = false;
        text.richText = true;
        var le = text.gameObject.AddComponent<LayoutElement>();
        le.preferredWidth = 330f;
        text.textWrappingMode = TextWrappingModes.Normal;

        group = img.gameObject.AddComponent<CanvasGroup>();
        group.alpha          = 0f;
        group.blocksRaycasts = false;
        group.interactable   = false;
    }
}
