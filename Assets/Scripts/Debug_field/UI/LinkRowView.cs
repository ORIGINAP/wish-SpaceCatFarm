using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class LinkRowView : MonoBehaviour
{
    private Func<TileItem>  owner;
    private Action          closeWindow;
    private Func<int, string> describe;
    private TextMeshProUGUI text;
    private Button          unlinkButton;
    private TileItem        shownOwner;
    private int             shownVersion = -1;

    public static LinkRowView Build(Transform parent, Func<TileItem> owner, Action closeWindow, Func<int, string> describe)
    {
        var card = CuteUIKit.Card(parent, CuteUIKit.LatteLight, 12, "LinkRow");
        card.raycastTarget = false;
        var hlg = card.gameObject.AddComponent<HorizontalLayoutGroup>();
        hlg.padding                = new RectOffset(6, 6, 4, 4);
        hlg.spacing                = 6f;
        hlg.childAlignment         = TextAnchor.MiddleLeft;
        hlg.childControlWidth      = hlg.childControlHeight = true;
        hlg.childForceExpandWidth  = hlg.childForceExpandHeight = false;

        var view = card.gameObject.AddComponent<LinkRowView>();
        view.owner        = owner;
        view.closeWindow  = closeWindow;
        view.describe     = describe;

        view.text = CuteUIKit.Text(card.transform, "", 10f, CuteUIKit.Cocoa, align: TextAlignmentOptions.MidlineLeft);
        view.text.textWrappingMode = TextWrappingModes.Normal;
        var le = view.text.gameObject.AddComponent<LayoutElement>();
        le.flexibleWidth  = 1f;
        le.preferredWidth = 0f;
        CuteUIKit.Button(card.transform, "연결하기", CuteUIKit.ButtonSky, view.OnLink, 80f, 26f, 11f);
        view.unlinkButton = CuteUIKit.Button(card.transform, "끊기", CuteUIKit.ButtonCoral, view.OnUnlink, 46f, 26f, 11f);
        view.Refresh();
        return view;
    }

    private void Update()
    {
        if (owner == null) return;
        if (owner() != shownOwner || TileItem.LinkVersion != shownVersion) Refresh();
    }

    public void Refresh()
    {
        var t = owner != null ? owner() : null;
        shownOwner   = t;
        shownVersion = TileItem.LinkVersion;
        int links = t != null ? t.Links.Count : 0;
        text.text = describe(links);
        unlinkButton.gameObject.SetActive(links > 0);
    }

    private void OnLink()
    {
        var t = owner != null ? owner() : null;
        if (t == null) return;
        closeWindow?.Invoke();
        LinkModeController.Begin(t);
    }

    private void OnUnlink()
    {
        var t = owner != null ? owner() : null;
        if (t == null || t.Links.Count == 0) return;
        int n = t.Links.Count;
        t.UnlinkAll();
        Sfx.Play(SfxId.UiClose);
        NotificationUI.Instance?.Show($"{LinkRules.NameOf(t)}: 연결 {n}개를 끊었어요");
        Refresh();
    }
}
