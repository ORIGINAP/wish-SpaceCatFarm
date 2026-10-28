using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

[DisallowMultipleComponent]
public class CutePop : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler, IPointerUpHandler,
                       ISelectHandler, IDeselectHandler
{
    public float hoverScale = 1.05f;
    public float pressScale = 0.94f;

    private Selectable selectable;
    private bool       hovering, navSelected;
    private Coroutine  co;

    private void Awake() => selectable = GetComponent<Selectable>();

    private void OnDisable()
    {
        hovering = navSelected = false;
        co = null;
        transform.localScale = Vector3.one;
    }

    public void OnPointerEnter(PointerEventData e) { hovering = true;  To(hoverScale, 0.16f); }
    public void OnPointerExit(PointerEventData e)  { hovering = false; To(Resting, 0.12f); }
    public void OnPointerDown(PointerEventData e)  => To(pressScale, 0.08f, overshoot: 0f);
    public void OnPointerUp(PointerEventData e)    => To(hovering ? hoverScale : Resting, 0.16f);

    public void OnSelect(BaseEventData e)
    {
        if (GameInput.GetMouseButton(0) || GameInput.GetMouseButtonUp(0) || GamepadPointer.Active) return;
        navSelected = true;
        To(hoverScale, 0.16f);
    }

    public void OnDeselect(BaseEventData e)
    {
        navSelected = false;
        if (!hovering) To(1f, 0.12f);
    }

    private float Resting => navSelected ? hoverScale : 1f;

    private void To(float scale, float duration, float overshoot = 2.2f)
    {
        if (!isActiveAndEnabled) return;
        if (selectable != null && !selectable.IsInteractable()) scale = 1f;
        if (co != null) StopCoroutine(co);
        co = StartCoroutine(Run(scale, duration, overshoot));
    }

    private IEnumerator Run(float scale, float duration, float overshoot)
    {
        yield return CuteUIKit.ScaleTo(transform, scale, duration, overshoot);
        co = null;
    }
}
