using StarterAssets;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
#if DEVELOPMENT_BUILD || UNITY_EDITOR
using Unity.Profiling;
#endif

public class WarpManager : MonoBehaviour
{
#if DEVELOPMENT_BUILD || UNITY_EDITOR
    private static readonly ProfilerMarker WarpRaycastMarker = new ProfilerMarker("TH.Warp.Raycast");
#endif

    private const int InitialRaycastBufferSize = 16;
    private RaycastHit[] _raycastHits = new RaycastHit[InitialRaycastBufferSize];

    public static WarpManager instance;
    public bool isField = true;
    public static int nowWatchingHouse;    //1부터 시작이다.
    public static int nowHouseArea;
    public bool nowWatchingFieldDoor;

    public GameObject openButton;
    public GameObject toAnotherHouseButton;
    public GameObject closeButton;

    public GameObject fieldRoot;
    public GameObject houseRoot;

    public HousingDummy inhouseFirstDummy;  //집 문이랑 겹쳐있는거. 작은 번호로 돌아가는거.
    public HousingDummy inhouseSecondDummy; //집 문 반대쪽에 있는거.
    public FieldDoorDummy fieldDoorDummy;
    public GameObject justDoorDummy; //끝번 집의 문이 없어지는 것을 대신하는 용도.(스크립트 비활성화만 하면 랜덤 방 이동 문이 되는 문제가 생김)
    public GameObject character;

    // Start is called before the first frame update
    void Awake()
    {
        instance = this;
    }

    private void OnDestroy()
    {
        instance = null;
    }

    // Update is called once per frame
    void Update()
    {
        bool checkHouse = false;
        bool checkFieldDoor = false;

#if DEVELOPMENT_BUILD || UNITY_EDITOR
        using (WarpRaycastMarker.Auto())
#endif
        {
        Vector2 center = new Vector2(Screen.width / 2f, Screen.height / 2f);
        //RaycastHit hit;
        Ray ray = Camera.main.ScreenPointToRay(center);
        int hitCount = RaycastWithoutAllocations(ray, 100f);
        for (int i = 0; i < hitCount; i++)
        {
            if (_raycastHits[i].transform.gameObject.TryGetComponent<HousingDummy>(out var housingDummy) == true)
            {
                bool first = nowWatchingHouse < 0;
                checkHouse = true;
                nowWatchingHouse = housingDummy.houseId;
                if (first)
                {
                    SetOpenButton(true);
                }
            }
            else if (_raycastHits[i].transform.gameObject.TryGetComponent<FieldDoorDummy>(out var Field) == true)
            {
                checkFieldDoor = true;
                bool first = nowWatchingFieldDoor == false;
                nowWatchingFieldDoor = true;
                if (first)
                {
                    SetCloseButton(true);
                }
            }
        }
        }

        /*
        if (Physics.Raycast(ray, out hit, 100))
        {
            if (true == hit.transform.gameObject.TryGetComponent<HousingDummy>(out var housingDummy))
            {
                bool first = nowWatchingHouse < 0;
                checkHouse = true;
                nowWatchingHouse = housingDummy.houseId;
                if (first)
                {
                    //진입구간;
                    SetOpenButton(true);
                }


            }
            else if (true == hit.transform.gameObject.TryGetComponent<FieldDoorDummy>(out var FieldDoorDummy))
            {
                checkFieldDoor = true;
                bool first = nowWatchingFieldDoor == false;
                nowWatchingFieldDoor = true;
                if (first)
                {
                    //진입구간;
                    SetCloseButton(true);
                }
            }
            else
            {
                //싹 다 raycastAll로 바꿔도 된다면 코드를 좀 다듬어야겠다. 너무 못생겼다.
                //raycastAll이 무한한 거리의 hit들을 감지한다면 큰일날거 같아서 거리는 100으로만 한다
                //집 안에서, 가구에 포탈이 가려져 있을 때 래이캐스트를 통해 포탈을 탈 수 있게 해주는 코드이다.
                RaycastHit[] hitsInRoom = Physics.RaycastAll(ray, 100);
                if (hitsInRoom != null && isField == false)
                {
                    for (int i = 0; i < hitsInRoom.Length; i++)
                    {
                        if (hitsInRoom[i].transform.CompareTag("Portal") == true)
                        {
                            Debug.Log("포탈 감지됨.");
                            if (hitsInRoom[i].transform.gameObject.TryGetComponent<HousingDummy>(out var Dummy) == true)
                            {
                                bool first = nowWatchingHouse < 0;
                                checkHouse = true;
                                nowWatchingHouse = Dummy.houseId;
                                if (first)
                                {
                                    //진입구간;
                                    SetOpenButton(true);
                                }
                            }
                            else if (hitsInRoom[i].transform.gameObject.TryGetComponent<FieldDoorDummy>(out var Field) == true)
                            {
                                checkFieldDoor = true;
                                bool first = nowWatchingFieldDoor == false;
                                nowWatchingFieldDoor = true;
                                if (first)
                                {
                                    //진입구간;
                                    SetCloseButton(true);
                                }
                            }
                        }
                    }
                }
            }
        }
        */
        if (false == checkHouse)
        {
            if (nowWatchingHouse > 0)
            {
                //진입구간;
                SetOpenButton(false);
            }
            nowWatchingHouse = -1;
        }

        if(false == checkFieldDoor)
        {
            if (nowWatchingFieldDoor == true)
            {
                //진입구간;
                SetCloseButton(false);
            }
            nowWatchingFieldDoor = false;
        }

    }

    private int RaycastWithoutAllocations(Ray ray, float maxDistance)
    {
        while (true)
        {
            int hitCount = Physics.RaycastNonAlloc(ray, _raycastHits, maxDistance);
            if (hitCount < _raycastHits.Length)
            {
                return hitCount;
            }

            _raycastHits = new RaycastHit[_raycastHits.Length * 2];
        }
    }

    public void SetOpenButton(bool active)
    {
        var houseList = GameManager.Instance.saveData.houseList;
        int count = houseList.Count;
        if (count <= 0 || nowWatchingHouse > count)
        {
            openButton.SetActive(false);
            toAnotherHouseButton.SetActive(false);
            return;
        }
        if (true == ArrangeManager.instance.nowArranging)
        {
            active = false;
        }

        if (isField)
        {            
            //float f = hit.distance;
            //string s = f.ToString("#.##");
            SystemMessageFunc.Debug($"보는집 : {nowWatchingHouse}\n 소유집 : {count}");
            toAnotherHouseButton.SetActive(false);
            openButton.SetActive(active);
            
        }
        else
        {
            SystemMessageFunc.Debug($"보는집 : {nowWatchingHouse}\n 소유집 : {count}\n ");
            openButton.SetActive(false);
            toAnotherHouseButton.SetActive(active);
        }
                
    }

    public void OnOpenButton()
    {
        if(isField)
        {
            Warp(true);
        }
        else { return; }
    }

    public void OnToAnotherHouseButton()
    {
        if (true == ArrangeManager.instance.nowArranging || true == isField)
        {
            return;
        }
        Warp(true);
    }

    public void SetCloseButton(bool active)
    {
        if (true == ArrangeManager.instance.nowArranging)
        {
            active = false;
        }
        closeButton.SetActive(active);
    }

    public void OnCloseButton()
    {
        if (true == ArrangeManager.instance.nowArranging)
        {
            return;
        }
        Warp(false);
    }

    public void Warp(bool isOpen)
    {
        var houseList = GameManager.Instance.saveData.houseList;
        int count = houseList.Count;
        if (true == isOpen)
        {
            //1부터 시작이라 count로 먹여도 됨.
            if (count <= 0 || nowWatchingHouse > count)
            {
                openButton.SetActive(false);
                toAnotherHouseButton.SetActive(false);
                return;
            }
            
        }

        var controller = character.GetComponent<CharacterController>();
        controller.enabled = false;
        
        if(character.TryGetComponent<FirstPersonController>(out var firstPersonController))
        {
            if(isOpen)
            {
                firstPersonController.MoveSpeed = 60f;
            }
            else
            {
                firstPersonController.MoveSpeed = 30f;
            }
        }

        nowHouseArea = nowWatchingHouse;
        Debug.Log($"{nowHouseArea},{nowWatchingHouse}");
        if(isOpen)
        {
            ArrangeManager.instance.LoadFixedFurniture();
        }
        
        GameManager.Instance.SetFade(() =>
       {
           Vector3 pos = isOpen ? new Vector3(5004, 5, 0) : new Vector3(0, 2, 0);
           character.transform.position = pos;
           SystemMessageFunc.Debug(pos.ToString());
           isField = false == isOpen;

           fieldRoot.SetActive(isField);
           houseRoot.SetActive(false == isField);

           //마지막 집.
           if (nowWatchingHouse == count && count >= 2)
           {
               inhouseFirstDummy.gameObject.SetActive(true);
               inhouseFirstDummy.houseId = nowWatchingHouse - 1;

               inhouseSecondDummy.gameObject.SetActive(false); //더 갈 수 없어.
               justDoorDummy.gameObject.SetActive(true);
               //fieldDoorDummy.gameObject.SetActive(false);     //집도 못가. 뒤로 돌아가.
           }
           else if (nowWatchingHouse == 1)
           {
               inhouseFirstDummy.gameObject.SetActive(false);

               if (count >= 2)
               {
                   inhouseSecondDummy.gameObject.SetActive(true);
                   inhouseSecondDummy.houseId = nowWatchingHouse + 1;
               }
               fieldDoorDummy.gameObject.SetActive(true);
               justDoorDummy.gameObject.SetActive(false);
           }
           else if(nowWatchingHouse > 1 && nowWatchingHouse < count)
           {
               //중간에 낀 경우.
               inhouseFirstDummy.gameObject.SetActive(true);
               inhouseFirstDummy.houseId = nowWatchingHouse - 1;

               inhouseSecondDummy.gameObject.SetActive(true); //더 갈 수 이썽
               inhouseSecondDummy.houseId = nowWatchingHouse + 1;
               justDoorDummy.gameObject.SetActive(false);
               //fieldDoorDummy.gameObject.SetActive(false);     //집도 못가. 뒤로 돌아가.
           }


           controller.enabled = true;
       });

        


    }
}
