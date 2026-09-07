using System.Collections;
using System.Collections.Generic;
using System.Data;
using Unity.VisualScripting;
using UnityEngine;
#if DEVELOPMENT_BUILD || UNITY_EDITOR
using TreeHouse.PerformanceBenchmark;
using Unity.Profiling;
#endif
//using static UnityEditor.Progress;

public class ArrangeManager : MonoBehaviour
{
#if DEVELOPMENT_BUILD || UNITY_EDITOR
    private static readonly ProfilerMarker DisableNearColliderMarker = new ProfilerMarker("TH.Arrange.DisableNearCollider");
#endif

    private Camera _nearColliderCamera;
    private Vector3 _lastNearColliderCameraPosition;
    private bool _hasNearColliderCameraPosition;
    private bool _nearColliderStateDirty = true;

    public static ArrangeManager instance;
    public Transform furnitureParent;
    public GameObject grid;
    public GameObject arrangeCanvas;

    public GameObject confirmButton;
    public GameObject cantConfirmButton;

    public ArrangeFurniture arrangePrefab;
    public FixedFurniture fixedPrefab;
    public FixedFurniture nowRearrangingFixedFurniture;
    public ArrangeFurniture nowArrangingFurniture;
    public List<FixedFurniture> fixedFurniturePool = new List<FixedFurniture>();

    public List<FixedFurniture> fixedFurnitureList = new List<FixedFurniture>();
    public bool nowArranging = false;

    public const int negativeBoundX = 5000;
    public const int positiveBoundX = 5000+20;

    public const int negativeBoundY = 0;
    public const int positiveBoundY = 7;

    public const int negativeBoundZ = -10;
    public const int positiveBoundZ = 10;

    public AudioSource arrangeSound;
    public AudioClip floorSound;
    public AudioClip wallSound;

    private void Awake()
    {
        instance = this;
        arrangeSound = GetComponent<AudioSource>();
    }
    private void OnDestroy()
    {
        instance = null;
    }

    public void LoadFixedFurniture()
    {
        foreach(var pastOne in fixedFurnitureList)
        {
            pastOne.gameObject.SetActive(false);
            var childList = pastOne.innerParent.GetComponentsInChildren<Transform>();
            foreach(Transform child in pastOne.innerParent)
            {
                Destroy(child.gameObject);
            }
        }

        fixedFurnitureList.Clear();
        
        var furnitureList = GameManager.Instance.saveData.houseList[WarpManager.nowHouseArea -1].furnitureList;
        if (fixedFurniturePool.Count < furnitureList.Count)
        {
            fixedFurniturePool.Capacity = furnitureList.Count;
            int originCount = fixedFurniturePool.Count;
            int resultCount = furnitureList.Count;
            for (int i =0;i< resultCount - originCount; i++)
            {
                fixedFurniturePool.Add(Instantiate<FixedFurniture>(fixedPrefab, furnitureParent));
            }
        }

        int poolIndex = -1;
        foreach (var furniture in furnitureList)
        {
            poolIndex++;

            FixedFurniture fixedComponent = fixedFurniturePool[poolIndex];
            fixedComponent.gameObject.SetActive(true);

            fixedComponent.feature = furniture;
            fixedComponent.data = furniture.GetData();
            fixedComponent.transform.localPosition = new Vector3(furniture.x, furniture.y, furniture.z);
            if (fixedComponent.data.eArrangeType == ItemData.EArrangeType.Wall)
            {
                fixedComponent.transform.eulerAngles = new Vector3(0, furniture.yRotation, 0);
            }
            var collider = fixedComponent.GetComponent<BoxCollider>();
            collider.size = new Vector3(fixedComponent.data.floorX, fixedComponent.data.height, fixedComponent.data.floorY);

            Instantiate(fixedComponent.data.GetPrefab(), fixedComponent.innerParent);

            fixedFurnitureList.Add(fixedComponent);

            if (fixedComponent.innerParent.GetChild(0).transform.localPosition.y != 0f)
            {
                fixedComponent.innerParent.GetChild(0).transform.localPosition = Vector3.zero;
            }
        }

        InvalidateNearColliderState();

    }

    public void StartRearrangeFixedFurniture(FixedFurniture fixedOne)
    {
        if(true == nowArranging)
        {
            return;
        }
        nowRearrangingFixedFurniture = fixedOne;
        fixedOne.gameObject.SetActive(false);
        InvalidateNearColliderState();
        StartArrangeFurniture(fixedOne.data);
    }

    public void StartArrangeFurniture(ItemData item)
    {
        grid.SetActive(true);
        arrangeCanvas.SetActive(true);
        nowArranging = true;
        if(null == nowArrangingFurniture)
        {
            nowArrangingFurniture = Instantiate<ArrangeFurniture>(arrangePrefab, furnitureParent);
        }
        else
        {
            foreach(Transform child in nowArrangingFurniture.parent)
            {
                Destroy(child.gameObject);
            }
        }
        nowArrangingFurniture.gameObject.SetActive(true);
        nowArrangingFurniture.data = item;
        var furnitureModel = item.GetPrefab();

        var obj = Instantiate(furnitureModel, nowArrangingFurniture.parent);
        obj.transform.localPosition = Vector3.zero;

        nowArrangingFurniture.redCube.transform.localScale = new Vector3(item.floorX, item.height, item.floorY);
        nowArrangingFurniture.greenCube.transform.localScale = new Vector3(item.floorX, item.height, item.floorY);
    }

    public void CancelArrange()
    {
        grid.SetActive(false);
        arrangeCanvas.SetActive(false);
        nowArranging = false;
        if (null == nowArrangingFurniture)
        {
            SystemMessageFunc.Debug("CancelArrange : nowArrangingFurniture가 NULL이었음");
            //당연히 이래야죵
            return;
        }

        if(null != nowRearrangingFixedFurniture)
        {
            nowRearrangingFixedFurniture.gameObject.SetActive(true);
            nowRearrangingFixedFurniture = null;
        }

        nowArrangingFurniture.gameObject.SetActive(false);
        InvalidateNearColliderState();

    }

    public void ConfirmArrange()
    {
        grid.SetActive(false);
        arrangeCanvas.SetActive(false);
        nowArranging = false;
        if (null == nowArrangingFurniture)
        {
            SystemMessageFunc.Debug("ConfirmArrange : nowArrangingFurniture가 NULL이었음");
            //당연히 여기들어오면 안되죵
            return;
        }

        var furnitureList = GameManager.Instance.saveData.houseList[WarpManager.nowHouseArea -1].furnitureList;


        FurnitureFeature feature;
        FixedFurniture fixedFurniture;
        if (null != nowRearrangingFixedFurniture)
        {
            fixedFurniture = nowRearrangingFixedFurniture;
            
            feature = nowRearrangingFixedFurniture.feature;
            if (fixedFurniture.data.eArrangeType == ItemData.EArrangeType.Wall)
            {
                feature.yRotation = nowArrangingFurniture.yRot;
                //fixedFurniture.transform.rotation = Quaternion.Euler(new Vector3(0, feature.yRotation, 0));
            }
            //feature.x = Mathf.RoundToInt(nowArrangingFurniture.transform.localPosition.x);
            //feature.y = Mathf.RoundToInt(nowArrangingFurniture.transform.localPosition.y);
            //feature.z = Mathf.RoundToInt(nowArrangingFurniture.transform.localPosition.z);
            feature.x = nowArrangingFurniture.transform.localPosition.x;
            feature.y = nowArrangingFurniture.transform.localPosition.y;
            feature.z = nowArrangingFurniture.transform.localPosition.z;

            nowRearrangingFixedFurniture = null;
            GameManager.Instance.SaveFile();
        }
        else
        {
            int arrangeNo = GameManager.Instance.saveData.houseList[WarpManager.nowHouseArea -1].GetArrangeNo();
            feature = new FurnitureFeature();
            var data = nowArrangingFurniture.data;
            feature.arrangeNo = arrangeNo;
            if (nowArrangingFurniture.data.eArrangeType == ItemData.EArrangeType.Wall)
            {
                feature.yRotation = nowArrangingFurniture.yRot;
            }
            //feature.x = Mathf.RoundToInt(nowArrangingFurniture.transform.localPosition.x);
            //feature.y = Mathf.RoundToInt(nowArrangingFurniture.transform.localPosition.y);
            //feature.z = Mathf.RoundToInt(nowArrangingFurniture.transform.localPosition.z);
            feature.x = nowArrangingFurniture.transform.localPosition.x;
            feature.y = nowArrangingFurniture.transform.localPosition.y;
            feature.z = nowArrangingFurniture.transform.localPosition.z;

            feature.id = data.id;
            furnitureList.Add(feature);
            //Debug.Log($"{nowArrangingFurniture.transform.localPosition.y},{feature.y}");
            GameManager.Instance.SaveFile();

            fixedFurniture = fixedFurniturePool.Find(x => x.gameObject.activeSelf == false);

            if (null == fixedFurniture)
            {
                fixedFurniture = Instantiate<FixedFurniture>(fixedPrefab, furnitureParent);
                fixedFurniturePool.Add(fixedFurniture);
            }
            fixedFurnitureList.Add(fixedFurniture);

            fixedFurniture.feature = feature;
            fixedFurniture.data = feature.GetData();
            var collider = fixedFurniture.GetComponent<BoxCollider>();
            collider.size = new Vector3(fixedFurniture.data.floorX, fixedFurniture.data.height, fixedFurniture.data.floorY);
            var obj = Instantiate(fixedFurniture.data.GetPrefab(), fixedFurniture.innerParent);
            obj.transform.localPosition = Vector3.zero;
            /*if (fixedFurniture.data.eArrangeType == ItemData.EArrangeType.Wall)
            {
                //obj.transform.rotation = Quaternion.Euler(new Vector3(0, feature.yRotation, 0)); 
            }*/
            InventoryManager.instance.Remove(fixedFurniture.data.id);
        }
        fixedFurniture.gameObject.SetActive(true);

        fixedFurniture.transform.localPosition = new Vector3(feature.x, feature.y, feature.z);
        fixedFurniture.transform.rotation = Quaternion.Euler(new Vector3(0, feature.yRotation, 0));

        if (fixedFurniture.data.eArrangeType == ItemData.EArrangeType.Floor)
        {
            arrangeSound.PlayOneShot(floorSound);
        }
        else if (fixedFurniture.data.eArrangeType == ItemData.EArrangeType.Wall)
        {
            arrangeSound.PlayOneShot(wallSound);
        }

        nowArrangingFurniture.gameObject.SetActive(false);

        InvalidateNearColliderState();

    }

    public void CantConfirmArrange()
    {
        SystemMessageFunc.Notify("배치 불가능한 위치입니다.");
    }


    private void Update()
    {
        if (false == ModuleManager.isInteractable)
        {
            return;
        }
        FurnitureUpdate();
        DisableNearCollider();
    }



    public bool IsBuildingColliding(ItemData data, Vector3 rawPos)
    {        
        float xBound = (float)data.floorX / 2f;
        float zBound = (float)data.floorY / 2f;
        float yBound = (float)data.height / 2f;
        if (data.eArrangeType == ItemData.EArrangeType.Floor)
        {
            if (rawPos.x > 0)
            {
                if (Mathf.RoundToInt(positiveBoundX - 5000 - rawPos.x) < xBound + (data.floorX % 2) * 0.5f)
                {

                    return true;
                    //colliding
                }
                //just think about positive bound.
            }
            else
            {
                if (Mathf.RoundToInt(rawPos.x - negativeBoundX + 5000) < xBound - (data.floorX % 2) * 0.5f)
                {
                    return true;
                    //colliding
                }
            }

            if (rawPos.z > 0)
            {
                if (Mathf.RoundToInt(positiveBoundZ - rawPos.z) < zBound + (data.floorY % 2) * 0.5f)
                {

                    return true;
                    //colliding
                }
                //just think about positive bound.
            }
            else
            {
                if (Mathf.RoundToInt(rawPos.z - negativeBoundZ) < zBound - (data.floorY % 2) * 0.5f)
                {

                    return true;
                    //colliding
                }
            }

            if (rawPos.y > 0)
            {
                if (Mathf.RoundToInt(positiveBoundY - rawPos.y) < yBound)
                {

                    return true;
                    //colliding
                }
                //just think about positive bound.
            }
            else
            {
                if (Mathf.RoundToInt(rawPos.y - negativeBoundY) < yBound && data.height != 1)
                {
                    return true;
                    //colliding
                }
            }
        }
        else if (data.eArrangeType == ItemData.EArrangeType.Wall)
        {
            if (rawPos.x >= 18.4f && (rawPos.z > -5.4f || rawPos.z <= -8.5f))
            {
                return true; //선반, 창문 고려. for WallThree
            }
            if (rawPos.z <= -9.4f && ((rawPos.x > 11f && rawPos.x < 14f) || (rawPos.x >= 4.5f && rawPos.x <= 7.5f) || 
                rawPos.x < 1f || rawPos.x > 18f))
            {
                return true; //for WallOne
            }
            if(rawPos.z>=9.0f &&(rawPos.x < 1f || rawPos.x > 18f))
            {
                return true; //for WallTwo
            }
            if (rawPos.x <= 0f && ((rawPos.z >= -7.5f && rawPos.z <= -3.5f) || (rawPos.z >= -1.5f && rawPos.z <= 0.5f) || rawPos.z > 8.4f))
            {
                return true; //for WallFour
            }

            if (rawPos.y > 0)
            {
                if (Mathf.RoundToInt(positiveBoundY - rawPos.y) < yBound)
                {

                    return true;
                    //colliding
                }
                //just think about positive bound.
            }
            else
            {
                if (Mathf.RoundToInt(rawPos.y - negativeBoundY) < yBound && data.height != 1)
                {
                    return true;
                    //colliding
                }
            }
        }    
            

        var furnitureList = GameManager.Instance.saveData.houseList[WarpManager.nowHouseArea -1].furnitureList;
        if(0 == furnitureList.Count)
        {
            return false;
        }

        //we checked every bounds, now we should check other buildings
        foreach (var other in furnitureList)
        {
            bool xBool = false;
            bool zBool = false;
            Vector3 otherPos = new Vector3(other.x, other.y, other.z);
            var otherData = other.GetData();
            float otherXBound = otherData.floorX / 2f;
            float otherYBound = otherData.height / 2f;
            float otherZBound = otherData.floorY / 2f;

            float nowXBound = xBound;
            float nowYBound = yBound;
            float nowZBound = zBound;
            //check other ones vector
            if (otherPos.x > rawPos.x)
            {
                nowXBound += (data.floorX % 2) * 0.5f;
            }
            else
            {
                //홀수 긴쪽.
                otherXBound += (otherData.floorX % 2) * 0.5f;
            }

            if (otherPos.z > rawPos.z)
            {
                //홀수 긴쪽. 5같은 길이면 좌표 작은쪽부터 2/3 이렇게 갈린다.
                nowZBound += (data.floorY % 2) * 0.5f;
            }
            else
            {
                otherZBound += (otherData.floorY % 2) * 0.5f;
            }

            float xDistnace = Mathf.Abs((otherPos.x - rawPos.x));
            if (xDistnace < nowXBound + otherXBound)
            {
                xBool = true;
            }
            else
            {
                continue;
            }

            float zDistnace = Mathf.Abs((otherPos.z - rawPos.z));
            if (zDistnace < nowZBound + otherZBound)
            {
                zBool = true;
            }
            else
            {
                continue;
            }

            float yDistance = Mathf.Abs((otherPos.y - rawPos.y));
            if (yDistance < nowYBound + otherYBound)
            {
                if (zBool && xBool)
                {
                    return true;
                }
            }

        }


        return false;
    }

    void FurnitureUpdate()
    {
        Camera mainCam = Camera.main;
        Vector2 center = new Vector2(Screen.width / 2f, Screen.height / 2f);
        RaycastHit hit;

        
        if (Input.GetMouseButtonDown(0) && nowArranging == false)
        {
            Ray ray = mainCam.ScreenPointToRay(Input.mousePosition); //마우스 좌클릭으로 마우스의 위치에서 Ray를 쏘아 오브젝트를 감지
            if (Physics.Raycast(ray, out hit, Mathf.Infinity))
            {
                GameObject target = hit.collider.gameObject; //Ray에 맞은 콜라이더를 타겟으로 설정
                if (true == target.TryGetComponent<FixedFurniture>(out var component))
                {
                    StartRearrangeFixedFurniture(component);
                }
                else
                {
                    return;
                }
            }
        }
        if (true == nowArranging && null != nowArrangingFurniture && true == nowArrangingFurniture.gameObject.activeInHierarchy)
        {
            //배치중일때는 시선을 따라감.
            Vector3 pos = PointCalib(nowArrangingFurniture);
            //we have to process this with gameManager
            //because gameManager has BuildingList, so you can know collidings
            //target.transform.position = gameManager.OnBuildingCollision(targetBuilding, pos);
            nowArrangingFurniture.transform.position = pos;
            bool isCollide = IsBuildingColliding(nowArrangingFurniture.data, nowArrangingFurniture.transform.localPosition);
            nowArrangingFurniture.greenCube.SetActive(false == isCollide);
            nowArrangingFurniture.redCube.SetActive(true == isCollide);
            confirmButton.SetActive(false == isCollide);
            cantConfirmButton.SetActive(true == isCollide);





            //좌클릭을 누르는 동안 마우스 좌표를 받아와 월드좌표로 변환 후, 타겟을 마우스의 위치로 옮깁니다.
            //gameManager.ChangeBuildingPosition(target);

        }
    }

    private void InvalidateNearColliderState()
    {
        _nearColliderStateDirty = true;
    }

    void DisableNearCollider()
    {
#if DEVELOPMENT_BUILD || UNITY_EDITOR
        using (DisableNearColliderMarker.Auto())
#endif
        {
            if (WarpManager.instance.isField)
            {
                _hasNearColliderCameraPosition = false;
                return;
            }

            if (_nearColliderCamera == null)
            {
                _nearColliderCamera = Camera.main;
            }

            if (_nearColliderCamera == null)
            {
                return;
            }

            Vector3 cameraPosition = _nearColliderCamera.transform.position;
            if (!_nearColliderStateDirty &&
                _hasNearColliderCameraPosition &&
                cameraPosition == _lastNearColliderCameraPosition)
            {
                return;
            }

            _nearColliderStateDirty = false;
            _hasNearColliderCameraPosition = true;
            _lastNearColliderCameraPosition = cameraPosition;

            foreach (FixedFurniture fixedOne in fixedFurnitureList)
            {
#if DEVELOPMENT_BUILD || UNITY_EDITOR
                BenchmarkRuntimeCounters.ArrangeCandidatesChecked++;
#endif
                if (fixedOne.data.id != 6 && fixedOne.data.id != 17)
                {
                    continue;
                }

                float distance = Vector3.Distance(fixedOne.transform.position, cameraPosition);
                if (fixedOne.gameObject.TryGetComponent<BoxCollider>(out BoxCollider carpetBox))
                {
                    carpetBox.isTrigger = distance < 5f;
                }
            }
        }
    }
    Vector3 ScreenToWorld(ItemData data)
    {
       
        Vector2 center = new Vector2(Screen.width / 2f, Screen.height / 2f);
        Camera mainCam = Camera.main;

        //Vector3 normalCameraVector = new Vector3(Mathf.Sqrt(0.6f), Mathf.Sqrt(0.2f), -Mathf.Sqrt(0.2f));
        Vector3 cameraPos = mainCam.transform.position;
        Vector3 cameraToXZ = new Vector3(cameraPos.x - 1.5f * cameraPos.y, 0, cameraPos.z - Mathf.Sqrt(0.75f) * cameraPos.y);
        //저기 이 부분에서 지금 고정값이 들어가있는데, 이게 들어가있는데도 왜 잘되는지 모르겠어요. 이거 원래 되면 안되거등요

        float cameraSqrMag = (cameraPos - cameraToXZ).magnitude;


        Vector3 clickPoint = mainCam.ScreenToWorldPoint(new Vector3(Screen.width / 2f, Screen.height / 2f, cameraSqrMag));

        float x = -1 * cameraPos.y / (cameraPos.y - clickPoint.y) * (cameraPos.x - clickPoint.x) + cameraPos.x;
        float z = -1 * cameraPos.y / (cameraPos.y - clickPoint.y) * (cameraPos.z - clickPoint.z) + cameraPos.z;


            /*그리드는 xz평면 위에있다. 그래서 y=0이어야 한다
            * 그렇지만 카메라는 비스듬히 놓여져 있으므로, 카메라에서 터치하는 값을
            * 변환해준 값은 y=0이 나오지 않는다 만들어줄 수 없다
            * 그렇다고 해서 강제로 y=0으로 하면 클릭좌표와 오브젝트 좌표가 같아보이지 않아 이질감이 생긴다
            * 그걸 해소하기 위한 x,z을 새로 설정해준다
            * 클릭포인트의 좌표벡터 l벡터, x1,y1,z1이라고 놓고, 카메라의 좌표벡터를 n벡터라고 놓자.
            * n벡터와 l벡터를 지나가는 직선을 구하고, 그 직선의 y좌표가 0일때의 x,z를 구한다
            * 그게 위의 것이다.
            */

        //반올림까지 여기서 해부리자
        x = (float)Mathf.RoundToInt(x) - (data.floorX % 2) * 0.5f;
        z = (float)Mathf.RoundToInt(z) - (data.floorY % 2) * 0.5f;

        int xHalfBound = (data.floorX / 2);
        int zHalfBound = (data.floorY / 2);

        //this is for boundary check
        if (x > positiveBoundX - (xHalfBound + (data.floorX % 2) * 0.5f))
        {
            x = positiveBoundX - (xHalfBound + (data.floorX % 2) * 0.5f);
        }
        else if (x < negativeBoundX + xHalfBound)
        {
            x = negativeBoundX + xHalfBound;
        }
        if (z > positiveBoundZ - (zHalfBound + (data.floorY % 2) * 0.5f))
        {
            z = positiveBoundZ - (zHalfBound + (data.floorY % 2) * 0.5f);
        }
        else if (z < negativeBoundZ + zHalfBound)
        {
            z = negativeBoundZ + zHalfBound;
        }
        Vector3 pos = new Vector3(x, data.height / 2f, z);

        return pos;
        
        //각 벽마다 평면이 다르다. 벽별로 해줘야할듯.


    }

    Vector3 PointCalib(ArrangeFurniture arrangingFurniture)
    {
        Vector3 pos = Vector3.zero;
        Camera mainCam = Camera.main;
        Vector2 center = new Vector2(Screen.width / 2f, Screen.height / 2f);
        Vector3 cameraPos = mainCam.transform.position;
        ItemData data = arrangingFurniture.data;

        //이건 배치 시에만 호출되므로, 가구가 아니라고 걱정할거 없다.
        if (data.eArrangeType==ItemData.EArrangeType.Floor)
        {
            if(arrangingFurniture.transform.rotation.y!=0)
            {
                arrangingFurniture.transform.rotation = Quaternion.identity;
                //벽가구 배치 후 바닥가구 배치를 할 경우 벽가구에서 돌아간 각도만큼 바닥가구에 적용이 된다. 
                //초기화를 위해서 rotation을 다시 만진다.
            }

            Vector3 cameraToXZ = new Vector3(cameraPos.x - 1.5f * cameraPos.y, 0, cameraPos.z - Mathf.Sqrt(0.75f) * cameraPos.y);
            //저기 이 부분에서 지금 고정값이 들어가있는데, 이게 들어가있는데도 왜 잘되는지 모르겠어요. 이거 원래 되면 안되거등요

            float cameraSqrMag = (cameraPos - cameraToXZ).magnitude;
            
            Vector3 clickPoint = mainCam.ScreenToWorldPoint(new Vector3(Screen.width / 2f, Screen.height / 2f, cameraSqrMag));

            float x = -1 * cameraPos.y / (cameraPos.y - clickPoint.y) * (cameraPos.x - clickPoint.x) + cameraPos.x;
            float z = -1 * cameraPos.y / (cameraPos.y - clickPoint.y) * (cameraPos.z - clickPoint.z) + cameraPos.z;

            x = (float)Mathf.RoundToInt(x) - (data.floorX % 2) * 0.5f;
            z = (float)Mathf.RoundToInt(z) - (data.floorY % 2) * 0.5f;

            int xHalfBound = (data.floorX / 2);
            int zHalfBound = (data.floorY / 2);

            //this is for boundary check
            if (x > positiveBoundX - (xHalfBound + (data.floorX % 2) * 0.5f))
            {
                x = positiveBoundX - (xHalfBound + (data.floorX % 2) * 0.5f);
            }
            else if (x < negativeBoundX + xHalfBound)
            {
                x = negativeBoundX + xHalfBound + (data.floorX % 2) * 0.5f;
            }
            if (z > positiveBoundZ - (zHalfBound + (data.floorY % 2) * 0.5f))
            {
                z = positiveBoundZ - (zHalfBound + (data.floorY % 2) * 0.5f);
            }
            else if (z < negativeBoundZ + zHalfBound)
            {
                z = negativeBoundZ + zHalfBound + (data.floorY % 2) * 0.5f;
            }
            pos = new Vector3(x, (data.height - 1) / 2f , z);

            return pos;
        }
        else if (data.eArrangeType == ItemData.EArrangeType.Wall)
        {
            Ray ray = mainCam.ScreenPointToRay(center);
            RaycastHit[] hits = Physics.RaycastAll(ray, 40);
            if (hits != null)
            {
                for (int i = 0; i < hits.Length; i++)
                {
                    switch(hits[i].transform.gameObject.name)
                    {
                        case "GridWallOne":
                            arrangingFurniture.transform.eulerAngles = new Vector3(0, 0, 0);
                            arrangingFurniture.yRot = 0f;
                            /*if (data.floorX != data.floorY)
                            {
                                //int temp = 0; //swap floorX and floorY
                                //temp = data.floorX;
                                //data.floorX = data.floorY;
                                //data.floorY = data.floorX;
                                nowArrangingFurniture.redCube.transform.localScale = new Vector3(data.floorX, data.height, data.floorY);
                                nowArrangingFurniture.greenCube.transform.localScale = new Vector3(data.floorX, data.height, data.floorY);
                                Debug.Log($"{data.floorX},{data.floorY}");
                            }*/
                            Vector3 pointOne = hits[i].point;
                            //Debug.Log($"1번벽보고있음, {pointOne}");
                            float x1 = pointOne.x;
                            float y1 = pointOne.y;
                            x1 = (float)Mathf.RoundToInt(x1) - (data.floorX % 2) * 0.5f;
                            y1 = (float)Mathf.RoundToInt(y1) - (1 - data.height % 2) * 0.5f; //- (data.height % 2) * 0.5f;

                            int xHalfBoundOne = (data.floorX / 2);
                            int yHalfBoundOne = (data.height / 2);

                            //this is for boundary check
                            if (x1 > positiveBoundX - (xHalfBoundOne + (data.floorX % 2) * 0.5f))
                            {
                                x1 = positiveBoundX - (xHalfBoundOne + (data.floorX % 2) * 0.5f);
                            }
                            else if (x1 < negativeBoundX + xHalfBoundOne)
                            {
                                x1 = negativeBoundX + xHalfBoundOne + (data.floorX % 2) * 0.5f;
                            }
                            if (y1 > positiveBoundY - (yHalfBoundOne + (1 - data.height % 2) * 0.5f))
                            {
                                y1 = positiveBoundY - yHalfBoundOne - (1 - data.height % 2) * 0.5f;
                            }
                            else if (y1 < negativeBoundY + yHalfBoundOne - (1 - data.height % 2) * 0.5f)
                            {
                                y1 = negativeBoundY + yHalfBoundOne - (1 - data.height % 2) * 0.5f; //+ (data.height % 2) * 0.5f;
                            }
                            pos = new Vector3(x1, y1, (data.floorY - 1) / 2f - 10);
                            break;
                        case "GridWallTwo":
                            arrangingFurniture.transform.eulerAngles = new Vector3(0, 180, 0);
                            arrangingFurniture.yRot = 180f;
                            /*if (data.floorX != data.floorY)
                            {
                                //int temp = 0;
                                //temp = data.floorX;
                                //data.floorX = data.floorY;
                                //data.floorY = data.floorX;
                                nowArrangingFurniture.redCube.transform.localScale = new Vector3(data.floorX, data.height, data.floorY);
                                nowArrangingFurniture.greenCube.transform.localScale = new Vector3(data.floorX, data.height, data.floorY);
                                Debug.Log($"{data.floorX},{data.floorY}");
                            }*/
                            Vector3 pointTwo = hits[i].point;
                            //Debug.Log($"2번벽보고있음, {pointTwo}");
                            float x2 = pointTwo.x;
                            float y2 = pointTwo.y;
                            x2 = (float)Mathf.RoundToInt(x2) - (data.floorX % 2) * 0.5f;
                            y2 = (float)Mathf.RoundToInt(y2) - (1 - data.height % 2) * 0.5f;

                            int xHalfBoundTwo = (data.floorX / 2);
                            int yHalfBoundTwo = (data.height / 2);

                            //this is for boundary check
                            if (x2 > positiveBoundX - (xHalfBoundTwo + (data.floorX % 2) * 0.5f))
                            {
                                x2 = positiveBoundX - (xHalfBoundTwo + (data.floorX % 2) * 0.5f);
                            }
                            else if (x2 < negativeBoundX + xHalfBoundTwo)
                            {
                                x2 = negativeBoundX + xHalfBoundTwo + (data.floorX % 2) * 0.5f;
                            }
                            if (y2 > positiveBoundY - (yHalfBoundTwo + (1 - data.height % 2) * 0.5f))
                            {
                                y2 = positiveBoundY - yHalfBoundTwo - (1 - data.height % 2) * 0.5f;
                            }
                            else if (y2 < negativeBoundY + yHalfBoundTwo - (1 - data.height % 2) * 0.5f)
                            {
                                y2 = negativeBoundY + yHalfBoundTwo - (1 - data.height % 2) * 0.5f;
                            }
                            pos = new Vector3(x2, y2, 10 - (data.floorY - 1) / 2f);
                            break;
                        case "GridWallTwoThree":
                            arrangingFurniture.transform.eulerAngles = new Vector3(0, 270, 0);
                            arrangingFurniture.yRot = 270f;
                            Vector3 pointThree = hits[i].point;

                            float y3 = pointThree.y;
                            float z3 = pointThree.z;
                            y3 = (float)Mathf.RoundToInt(y3) - (1 - data.height % 2) * 0.5f;
                            if (arrangingFurniture.data.id == 10)
                            {
                                z3 = (float)Mathf.RoundToInt(z3) - (data.floorY % 2) * 0.5f;
                            }
                            else
                            {
                                z3 = (float)Mathf.RoundToInt(z3) - (1 - data.floorY % 2) * 0.5f;
                            }

                            int yHalfBoundThree = (data.height / 2);
                            int zHalfBoundThree = (data.floorY / 2);

                            //this is for boundary check
                            if (y3 > positiveBoundY - (yHalfBoundThree + (1 - data.height % 2) * 0.5f))
                            {
                                y3 = positiveBoundY - (yHalfBoundThree + (1 - data.height % 2) * 0.5f);
                            }
                            else if (y3 < negativeBoundY + yHalfBoundThree - (1 - data.height % 2) * 0.5f)
                            {
                                y3 = negativeBoundY + yHalfBoundThree - (1 - data.height % 2) * 0.5f;
                            }
                            if (z3 > positiveBoundZ - (zHalfBoundThree + (1 - data.floorY % 2) * 0.5f))
                            {
                                z3 = positiveBoundZ - (zHalfBoundThree + (1 - data.floorY % 2) * 0.5f);
                            }
                            else if (z3 < negativeBoundZ + zHalfBoundThree)
                            {
                                z3 = negativeBoundZ + zHalfBoundThree + (1 - data.floorY % 2) * 0.5f;
                            }
                            //pos = new Vector3(positiveBoundX - (data.floorX - 1) / 2f, y3, z3);
                            pos = new Vector3(positiveBoundX - 1f, y3, z3);
                            //Debug.Log($"3번벽보고있음, {pointThree}에서 pos {pos}");
                            break;
                        case "GridWallTwoFour":
                            arrangingFurniture.transform.eulerAngles = new Vector3(0, 90, 0);
                            arrangingFurniture.yRot = 90f;
                            Vector3 pointFour = hits[i].point;
                            //Debug.Log($"4번벽보고있음, {pointFour}");
                            float y4 = pointFour.y;
                            float z4 = pointFour.z;
                            y4 = (float)Mathf.RoundToInt(y4) - (1 - data.height % 2) * 0.5f;
                            if(arrangingFurniture.data.id == 10)
                            {
                                z4 = (float)Mathf.RoundToInt(z4) - (data.floorY % 2) * 0.5f;
                            }
                            else
                            {
                                z4 = (float)Mathf.RoundToInt(z4) - (1 - data.floorY % 2) * 0.5f;
                            }
                            

                            int yHalfBoundFour = (data.height / 2);
                            int zHalfBoundFour = (data.floorY / 2);

                            //this is for boundary check
                            if (y4 > positiveBoundY - (yHalfBoundFour + (1 - data.height % 2) * 0.5f))
                            {
                                y4 = positiveBoundY - (yHalfBoundFour + (1 - data.height % 2) * 0.5f);
                            }
                            else if (y4 < negativeBoundY + yHalfBoundFour - (1 - data.height % 2) * 0.5f)
                            {
                                y4 = negativeBoundY + yHalfBoundFour - (1 - data.height % 2) * 0.5f;
                            }
                            if (z4 > positiveBoundZ - (zHalfBoundFour + (1 - data.floorY % 2) * 0.5f))
                            {
                                z4 = positiveBoundZ - (zHalfBoundFour + (1 - data.floorY % 2) * 0.5f);
                            }
                            else if (z4 < negativeBoundZ + zHalfBoundFour)
                            {
                                z4 = negativeBoundZ + zHalfBoundFour + (1 - data.floorY % 2) * 0.5f;
                            }
                            //pos = new Vector3(negativeBoundX + (data.floorX - 1) / 2f, y4, z4);
                            pos = new Vector3(negativeBoundX, y4, z4);
                            break;
                        default: break;
                    }
                }
            }
            
            return pos;
        }
        else { return pos; }
    }

}
