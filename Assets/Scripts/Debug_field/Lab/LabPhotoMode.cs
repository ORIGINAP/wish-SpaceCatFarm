using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class LabPhotoMode : LabExperiment
{
    public override string Id      => "photo";
    public override string Title   => "사진 모드";
    public override string Tags    => "싱글 · 멀티";
    public override Color  Accent  => CuteUIKit.Hex(0x9C82E0);
    public override string Summary =>
        "[O]로 화면의 UI를 숨기고 [Space]로 찰칵! 휠로 확대 · 축소, [O]/Esc로 나가요. " +
        "사진은 게임 저장 폴더의 Screenshots에 쌓여요. 친구들과 단체 사진도 찍어 보세요.";
    public override string Status => taken > 0 ? $"찍은 사진 {taken}장 · {Folder.Replace('\\', '/')}" : "아직 찍은 사진 없음";

    private static string Folder => System.IO.Path.Combine(Application.persistentDataPath, "Screenshots");

    private bool active;
    private int  taken;
    private readonly List<Canvas> hidden = new();
    private GameObject hintRoot;
    private TextMeshProUGUI hint;
    private Image flash;

    protected override void LocalTick()
    {
        if (active && EscapeRouter.TryConsume(EscapeRouter.EscPressed)) { SetActive(false); return; }
        if (!TextInputFocus.Active && GameInput.GetKeyDown(KeyCode.O)) { SetActive(!active); return; }
        if (active && !TextInputFocus.Active && GameInput.GetKeyDown(KeyCode.Space)) StartCoroutine(Capture());
    }

    private void SetActive(bool on)
    {
        if (on == active) return;
        if (on && PlayerTileController.IsPauseMenuOpen()) return;
        active = on;
        if (on)
        {
            LabPanelUI.CloseIfOpen();
            hidden.Clear();
            foreach (var c in FindObjectsByType<Canvas>(FindObjectsSortMode.None))
                if (c.isRootCanvas && c.enabled && c.renderMode == RenderMode.ScreenSpaceOverlay) { c.enabled = false; hidden.Add(c); }
            BuildHint();
            hintRoot.SetActive(true);
            Sfx.Play(SfxId.UiOpen);
        }
        else
        {
            foreach (var c in hidden) if (c != null) c.enabled = true;
            hidden.Clear();
            if (hintRoot != null) hintRoot.SetActive(false);
            Sfx.Play(SfxId.UiClose);
        }
    }

    private IEnumerator Capture()
    {
        System.IO.Directory.CreateDirectory(Folder);
        string path = System.IO.Path.Combine(Folder, $"WISH_{System.DateTime.Now:yyyyMMdd_HHmmss_fff}.png");
        hintRoot.SetActive(false);
        ScreenCapture.CaptureScreenshot(path);
        yield return null;
        yield return null;
        taken++;
        if (!active) yield break;
        hintRoot.SetActive(true);
        hint.text = $"찰칵! 저장했어요 ({taken}장)  ·  [Space] 또 찍기  ·  [O]/Esc 나가기";
        Sfx.Play(SfxId.UiConfirm);
        for (float t = 0f; t < 0.35f; t += Time.unscaledDeltaTime)
        {
            flash.color = new Color(1f, 1f, 1f, 0.8f * (1f - t / 0.35f));
            yield return null;
        }
        flash.color = Color.clear;
    }

    private void BuildHint()
    {
        if (hintRoot != null) return;
        hintRoot = new GameObject("PhotoMode", typeof(RectTransform));
        UIStyleKit.CreateOverlayCanvas(hintRoot, 90).GetComponent<GraphicRaycaster>().enabled = false;

        flash = new GameObject("Flash", typeof(RectTransform)).AddComponent<Image>();
        flash.transform.SetParent(hintRoot.transform, false);
        flash.rectTransform.anchorMin = Vector2.zero;
        flash.rectTransform.anchorMax = Vector2.one;
        flash.rectTransform.offsetMin = flash.rectTransform.offsetMax = Vector2.zero;
        flash.color = Color.clear;
        flash.raycastTarget = false;

        var pill = CuteUIKit.Pill(hintRoot.transform, "사진 모드  ·  [Space] 찰칵  ·  휠 확대 · 축소  ·  [O]/Esc 나가기",
                                  new Color(1f, 0.97f, 0.92f, 0.9f), CuteUIKit.Cocoa, 13f, 14, new RectOffset(16, 16, 6, 6), out hint, fitSelf: true);
        pill.anchorMin = pill.anchorMax = pill.pivot = new Vector2(0.5f, 0f);
        pill.anchoredPosition = new Vector2(0f, 24f);
    }

    protected override void Cleanup()
    {
        SetActive(false);
        if (hintRoot != null) Destroy(hintRoot);
        hintRoot = null;
    }

    public override void BuildControls(Transform row)
    {
        LabPanelUI.Btn(row, "사진 모드 켜기 (O)", CuteUIKit.LavenderTag, () => SetActive(true));
    }
}
