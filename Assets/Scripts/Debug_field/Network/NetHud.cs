using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class NetHud : MonoBehaviour
{
    private static NetHud instance;

    private TextMeshProUGUI label;
    private GameObject      copyButton;

    public static void Ensure()
    {
        if (GridManager.Instance == null) return;
        if (instance == null)
        {
            var go = new GameObject("NetHud");
            UIStyleKit.CreateOverlayCanvas(go, 40);
            instance = go.AddComponent<NetHud>();
            instance.Build();
        }
        instance.Refresh();
    }

    public static void Hide()
    {
        if (instance != null) Destroy(instance.gameObject);
        instance = null;
    }

    public static void CopyCode()
    {
        if (string.IsNullOrEmpty(NetBootstrap.JoinCode)) return;
        GUIUtility.systemCopyBuffer = NetBootstrap.JoinCode;
        NotificationUI.Instance?.Show($"조인 코드 {NetBootstrap.JoinCode}를 복사했어요. 친구에게 보내 주세요!");
        Sfx.Play(SfxId.UiConfirm);
    }

    private void OnEnable()  => NetBootstrap.Changed += Refresh;
    private void OnDisable() => NetBootstrap.Changed -= Refresh;

    private void OnDestroy()
    {
        if (instance == this) instance = null;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => instance = null;

    private void Build()
    {
        var card = new GameObject("Card", typeof(RectTransform));
        card.transform.SetParent(transform, false);
        var rt = (RectTransform)card.transform;
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(1f, 1f);
        rt.anchoredPosition = new Vector2(-95f, -68f);
        rt.sizeDelta        = new Vector2(200f, 34f);
        CuteUIKit.DecorateWindow(rt, catEars: false, radius: 17, border: 3);

        var hlg = card.AddComponent<HorizontalLayoutGroup>();
        hlg.padding                = new RectOffset(8, 8, 0, 0);
        hlg.spacing                = 8f;
        hlg.childAlignment         = TextAnchor.MiddleCenter;
        hlg.childControlWidth      = true;
        hlg.childControlHeight     = true;
        hlg.childForceExpandWidth  = false;
        hlg.childForceExpandHeight = false;
        card.AddComponent<ContentSizeFitter>().horizontalFit = ContentSizeFitter.FitMode.PreferredSize;

        CuteUIKit.Pill(card.transform, "함께", CuteUIKit.MintTag, CuteUIKit.MintText, 11f, 10, new RectOffset(8, 8, 2, 2), out _);
        label = CuteUIKit.Text(card.transform, "", 13f, CuteUIKit.Cocoa, FontStyles.Bold);

        var btn = UIStyleKit.CreateButton(card.transform, "복사", CuteUIKit.ButtonSky, CopyCode, preferredWidth: 46f, preferredHeight: 24f);
        btn.GetComponentInChildren<TextMeshProUGUI>().fontSize = 11f;
        copyButton = btn.gameObject;
    }

    private float nextRefresh;

    private void Update()
    {
        if (Time.unscaledTime < nextRefresh) return;
        nextRefresh = Time.unscaledTime + 1f;
        Refresh();
    }

    private void Refresh()
    {
        if (label == null) return;
        int n = NetBootstrap.PlayerCount;
        if (NetSession.IsHost)
        {
            bool hasCode = !string.IsNullOrEmpty(NetBootstrap.JoinCode);
            label.text = hasCode ? $"조인 코드 <color=#F7879F>{NetBootstrap.JoinCode}</color>  ·  {n}명" : NetBootstrap.Status;
            copyButton.SetActive(hasCode);
        }
        else
        {
            label.text = $"{NetBootstrap.HostName}님의 월드  ·  {n}명";
            copyButton.SetActive(false);
        }
    }
}
