using UnityEngine;
using UnityEngine.EventSystems;

public class FixedTouchField : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler
{
    private const int NoPointer = int.MinValue;

    [HideInInspector]
    public Vector2 TouchDist;

    [HideInInspector]
    public bool Pressed;

    private Canvas _rootCanvas;
    private int _activePointerId = NoPointer;
    private Vector2 _pendingDelta;

    private void Awake()
    {
        _rootCanvas = GetComponentInParent<Canvas>();
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        if (_activePointerId != NoPointer)
        {
            return;
        }

        _activePointerId = eventData.pointerId;
        Pressed = true;
        ClearDelta();
    }

    public void OnDrag(PointerEventData eventData)
    {
        if (eventData.pointerId != _activePointerId)
        {
            return;
        }

        float canvasScale = _rootCanvas != null && _rootCanvas.scaleFactor > 0f
            ? _rootCanvas.scaleFactor
            : 1f;

        _pendingDelta += eventData.delta / canvasScale;
        TouchDist = _pendingDelta;
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        if (eventData.pointerId != _activePointerId)
        {
            return;
        }

        ResetPointer();
    }

    public Vector2 ConsumeDelta()
    {
        Vector2 delta = _pendingDelta;
        ClearDelta();
        return delta;
    }

    private void OnDisable()
    {
        ResetPointer();
    }

    private void ClearDelta()
    {
        _pendingDelta = Vector2.zero;
        TouchDist = Vector2.zero;
    }

    private void ResetPointer()
    {
        _activePointerId = NoPointer;
        Pressed = false;
        ClearDelta();
    }
}
