using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class TouchHandler : MonoBehaviour
{

    public delegate void TouchCallback(Vector2 touchDelta,Vector2 startTouchPos,Vector2 nowTouchPos);

    public static TouchCallback nowCallback;

    private Vector2 lastTouch;
    private Vector2 startTouch;
    public void Awake()
    {
        nowCallback = null;
        lastTouch = Vector2.zero;
    }

    // Update is called once per frame
    void Update()
    {
        if(null == nowCallback)
        {
            Debug.Log("콜백 널");
            return;
        }
#if UNITY_EDITOR
        if(false == Input.GetMouseButton(0))
        {
            Debug.Log("터치 0");
            if (null != nowCallback)
            {
                nowCallback = null;
            }
            lastTouch = Input.mousePosition;
            return;
        }

        if(Input.GetMouseButtonDown(0))
        {
            startTouch = Input.mousePosition;
        }

        Vector2 touch = Input.mousePosition;
        nowCallback(lastTouch - touch, startTouch, touch);
        lastTouch = touch;
        Debug.Log("여기 작동중");
#else
        if(Input.touchCount == 0)
        {
            Debug.Log("터치 0");
            if (null != nowCallback)
            {
                nowCallback = null;
            }
            return;
        }
        for (int i = 0; i < Input.touchCount; i++)
        {
            Touch touch = Input.GetTouch(i);
            nowCallback(touch.deltaPosition, touch.rawPosition, touch.position);
        }
               
#endif
    }

    public static bool SetCallback(TouchCallback callback)
    {
        Debug.Log("콜백 시작");
        if (null != nowCallback)
        {
            Debug.Log("콜백 널체크");
            return false;
        }
        if (Input.touchCount > 1)
        {
            Debug.Log("콜백 터치카운트");
            return false;
        }

        nowCallback = callback;
        return true;
    }
}
