using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;


public class CameraRotateHandler : MonoBehaviour
{
    public float speed;

    public GameObject playerObj;
    public GameObject cameraObj;

    private int usingPointerId;

    public void Awake()
    {
#if UNITY_EDITOR
        usingPointerId = 2;
#else
        usingPointerId = 0;
#endif
    }


    public void TouchCallback(Vector2 delta,Vector2 startPos,Vector2 touchPos)
    {
        Debug.Log($"카메라 로테이트 굿");
        Vector2 wholeDelta = touchPos - startPos;
        Vector3 playerEulerAngles = new Vector3(0, wholeDelta.x, 0);        
        
        Vector3 rotatePos;
        rotatePos = new Vector3(delta.y, -1 * delta.x, 0);
        rotatePos *= 0.03f;
        cameraObj.transform.Rotate(rotatePos);

        Debug.Log($"카메라 로테이트 델타 {wholeDelta}");
        playerObj.transform.localEulerAngles += playerEulerAngles;
    }
    public void OnDragStart(BaseEventData baseData)
    {
        PointerEventData eventData = baseData as PointerEventData;
        if (usingPointerId != eventData.pointerId)
        {
            return;
        }

        TouchHandler.SetCallback(TouchCallback);
    }

    //void Update()
    //{

    //    if (Input.touchCount == 1) // 터치가 되었다면,
    //    {

    //        if (isBuildingTouched || isBuying)
    //        {
    //            //빌딩옮기는 중이면 예외처리
    //            return;
    //        }
    //        Vector3 rotatePos;
    //        Touch touch = Input.GetTouch(0);
    //        if (touch.phase == TouchPhase.Began) // 터치 페이즈 
    //        {
    //            prePos = touch.position;  //터치의 이전 좌표 기록
    //        }
    //        else if (touch.phase == TouchPhase.Moved)    //터치하고 움직인다면,
    //        {
    //            //여기는 무빙
    //            nowPos = touch.position;  //움직인만큼 좌표 입력
    //            rotatePos = new Vector3(touch.deltaPosition.y, -1 * touch.deltaPosition.x, 0);
    //            rotatePos *= 0.03f;
    //            cam.transform.Rotate(rotatePos);
    //            cam.transform.eulerAngles = new Vector3(cam.transform.eulerAngles.x, cam.transform.eulerAngles.y, 0);
    //            //cam.transform.rotation = Quaternion.Euler(cam.transform.rotation.x, cam.transform.rotation.y, 0);
    //            prePos = touch.position;  // 다시 계산하기 위해 이전좌표 재설정
    //        }


    //    }

    //    if (Input.touchCount == 2)
    //    {
    //        if (isBuildingTouched || isBuying)
    //        {
    //            //빌딩옮기는 중이면 예외처리
    //            return;
    //        }
    //        Touch touch = Input.GetTouch(0);
    //        Touch touch2 = Input.GetTouch(1);

    //        //카메라 자체 이동
    //        if (touch.phase == TouchPhase.Began) // 터치 페이즈 
    //        {
    //            //무빙
    //            prePos = touch.position;  //터치의 이전 좌표 기록

    //            //확대축소
    //            preZoomPos = touch.position - touch2.position;   //줌땡기려고
    //            preZoomMag = preZoomPos.magnitude;
    //        }
    //        else if (touch.phase == TouchPhase.Moved)    //터치하고 움직인다면,
    //        {

    //            bool isZoomed = false;
    //            //여기는 확대축소
    //            nowZoomPos = touch.position - touch2.position;
    //            nowZoomMag = nowZoomPos.magnitude;
    //            deltaZoomMag = preZoomMag - nowZoomMag;
    //            deltaZoomMag *= 0.1f;
    //            if (Mathf.Abs(deltaZoomMag) > 4.0f)
    //            {

    //                isZoomed = true;
    //                cam.fieldOfView += deltaZoomMag;
    //                if (cam.fieldOfView > 120)
    //                {
    //                    cam.fieldOfView = 120;
    //                }
    //                else if (cam.fieldOfView < 40)
    //                {
    //                    cam.fieldOfView = 40;
    //                }
    //            }
    //            preZoomPos = touch.position - touch2.position;   //줌땡기려고
    //            preZoomMag = preZoomPos.magnitude;

    //            if (isZoomed)
    //            {
    //                return;
    //            }
    //            //여기는 무빙
    //            nowPos = touch.position;  //움직인만큼 좌표 입력
    //            movePos = (Vector3)(prePos - nowPos) * cameraSpeed; //움직인만큼 벡터로 변환
    //            cam.transform.Translate(movePos);   //움직여줌
    //            Vector3 limit = cam.transform.position;
    //            if (Mathf.Abs(cam.transform.position.x) > 40)
    //            {
    //                if (limit.x > 0)
    //                {
    //                    limit.x = 40;
    //                }
    //                else
    //                {
    //                    limit.x = -40;
    //                }

    //            }
    //            if (Mathf.Abs(cam.transform.position.y) > 40)
    //            {
    //                if (limit.y > 0)
    //                {
    //                    limit.y = 40;
    //                }
    //                else
    //                {
    //                    limit.y = -40;
    //                }
    //            }
    //            if (Mathf.Abs(cam.transform.position.z) > 40)
    //            {
    //                if (limit.z > 0)
    //                {
    //                    limit.z = 40;
    //                }
    //                else
    //                {
    //                    limit.z = -40;
    //                }
    //            }

    //            cam.transform.position = limit;


    //            preZoomPos = nowZoomPos;
    //            prePos = touch.position;  // 다시 계산하기 위해 이전좌표 재설정

    //        }
    //        else if (touch.phase == TouchPhase.Ended)
    //        {
    //            cam.fieldOfView = 60;
    //        }
    //    }
    //}
}
