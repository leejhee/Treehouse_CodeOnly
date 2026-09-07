using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

public class JoyPadHandler : MonoBehaviour
{
    public float settingSpeed;
    public RectTransform padBg;
    public RectTransform stoneRect;

    private Vector2 centerRectPos;
    private float bgRadius;

    public static Vector2 velocity;

    private int usingPointerId;

    public void Awake()
    {
        velocity = Vector2.zero;
        this.centerRectPos = this.padBg.transform.position;
        //정사각형이라 치자
        this.bgRadius = this.padBg.sizeDelta.x > this.padBg.sizeDelta.y ? this.padBg.sizeDelta.x/2f  : this.padBg.sizeDelta.y/2f;
#if UNITY_EDITOR
        usingPointerId = 2;
#else
        usingPointerId = 0;
#endif
    }

    public void OnScreenPointerEnter(BaseEventData baseData)
    {
        Debug.Log("조이패드 터치");
        PointerEventData eventData = baseData as PointerEventData;
        if (usingPointerId != eventData.pointerId)
        {
            Debug.Log("조이패드 터치 ID리턴 ");
            return;
        }
        if(false == TouchHandler.SetCallback(TouchCallback))
        {
            Debug.Log("조이패드 터치 콜백리턴");
            return;
        }
        Debug.Log("조이패드 터치");
        this.stoneRect.anchoredPosition = this.centerRectPos;
        
    }

    public void TouchCallback(Vector2 delta, Vector2 startPos, Vector2 touchPos)
    {
        Vector2 length = touchPos - startPos;
        Debug.Log($"조이패드 렝스 {length}");
        if (length.magnitude > bgRadius)
        {
            length.Normalize();
            length *= bgRadius;
        }
        this.stoneRect.position = centerRectPos + length;
        Vector2 nowVelocity = new Vector2(length.y, length.x);

        velocity = nowVelocity;
    }
}
