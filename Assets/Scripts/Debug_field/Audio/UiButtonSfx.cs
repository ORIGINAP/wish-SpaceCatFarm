using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

[DisallowMultipleComponent]
public class UiButtonSfx : MonoBehaviour, IPointerEnterHandler
{
    public bool hover;

    private Button button;

    private void Awake()
    {
        button = GetComponent<Button>();
        if (button != null) button.onClick.AddListener(OnClick);
    }

    private void OnDestroy()
    {
        if (button != null) button.onClick.RemoveListener(OnClick);
    }

    private static void OnClick() => Sfx.Play(SfxId.UiClick);

    public void OnPointerEnter(PointerEventData eventData)
    {
        if (hover && button != null && button.IsInteractable()) Sfx.Play(SfxId.UiHover);
    }
}
