using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class FieldManager : MonoBehaviour
{

    public GameObject parentObj;
    private GameObject characterControl;
    public LayerMask layer;
    // Start is called before the first frame update
    void Start()
    {
        characterControl = CharacterControl.instance.gameObject;
        //일단 시작할 때 테이블에서 전체 필드 데이터를 떼온다.
        List<SaveDataClass.FieldItem> dropItemsData = new List<SaveDataClass.FieldItem>();
        List<SaveDataClass.FieldItem> reservedSpawnItemsData = new List<SaveDataClass.FieldItem>();

        List<GameObject> fieldItemsforCheck = new List<GameObject>(); //이거 그대로 둬도 된다.
        //Dictionary<GameObject, int> didItemOverlapped = new Dictionary<GameObject, int>();
        //List<List<GameObject>> groupedOverlapped = new List<List<GameObject>>();

        foreach (var item in GameManager.Instance.dataManager.itemDataWrapper.itemDataList)
        {
            if(item.maxFieldDropCount < 0 )
            {
                //하우스 예외처리
                Debug.Log(item.resourceName+ " 하우스나와야해");
                continue;
            }
            var dropItem = new SaveDataClass.FieldItem();
            dropItem.id = item.id;
            dropItem.count = item.maxFieldDropCount;
            dropItemsData.Add(dropItem);

            int fieldIndex = GameManager.Instance.saveData.fieldItemList.FindIndex(x => x.id == item.id);
            SaveDataClass.FieldItem pick;
            if (fieldIndex < 0 || fieldIndex >= GameManager.Instance.saveData.fieldItemList.Count)
            {
                pick = new SaveDataClass.FieldItem();
                pick.id = item.id;
                pick.count = 0;
            }
            else
            {
                pick = GameManager.Instance.saveData.fieldItemList[fieldIndex];
            }

            int ableSpawnCount = item.maxFieldDropCount - pick.count;
            if (0 >= ableSpawnCount)
            {
                //스폰리스트에 안넣어도댐. 이미 최대값임.
                continue;
            }

            var field = new SaveDataClass.FieldItem
            {
                id = pick.id,
                count = ableSpawnCount,
            };
            reservedSpawnItemsData.Add(field);
        }

        parentObj.transform.position = characterControl.transform.position;        
        DateTime lastTime = GameManager.Instance.saveData.lastEndTime;
        DateTime nowTime = DateTime.Now;

        TimeSpan timeSpan = nowTime - lastTime;
        int totalMinutes = (int)timeSpan.TotalMinutes;
        if (timeSpan.TotalMinutes < 20f)
        {
            totalMinutes = (int)timeSpan.TotalMinutes + 19; //초기에 너무 아이템이 안나올 경우 1분 안에 템들이 모두 젠되게 해준다.
        }

        foreach (var spawn in reservedSpawnItemsData)
        {
            Debug.Log("드랍 루프중");
            var data = GameManager.Instance.dataManager.itemDataWrapper.itemDataList.Find(x => x.id == spawn.id);
            if(null == data)
            {
                Debug.Log($"드랍 못함. 데이터 널. {spawn.id}");
                continue;
            }
            if(totalMinutes < data.regenTimeMinutes)
            {
                Debug.Log($"리젠 시간 안됨 id : {spawn.id}");
                continue;
            }
            int spawnCount = 0;
            int checkCount = totalMinutes / data.regenTimeMinutes;
            for (int i = 0; i < checkCount; i++)
            {
                int random = UnityEngine.Random.Range(0, 100);
                if (random < data.regenProbabilityPercent)
                {
                    //성공!
                    spawnCount += data.regenCount;
                    Debug.Log($"성공!! 인덱스{data.id}, 카운트{spawnCount} ");
                    if (spawnCount >= spawn.count)
                    {
                        spawnCount = spawn.count;
                        break;
                    }
                }
            }

            if (spawnCount <= 0)
            {
                continue;
            }

            int index = GameManager.Instance.saveData.fieldItemList.FindIndex(x => x.id == spawn.id);
            SaveDataClass.FieldItem pick;
            if (index < 0 || index >= GameManager.Instance.saveData.fieldItemList.Count)
            {
                pick = new SaveDataClass.FieldItem();
                pick.id = spawn.id;
                pick.count = 0;

                GameManager.Instance.saveData.fieldItemList.Add(pick);
                index = GameManager.Instance.saveData.fieldItemList.Count - 1;
            }

            var fieldItem = GameManager.Instance.saveData.fieldItemList[index];
            fieldItem.count += spawnCount;
            if (fieldItem.count > data.maxFieldDropCount)
            {
                fieldItem.count = data.maxFieldDropCount;
            }
            Debug.Log($"성공!!");
            GameManager.Instance.saveData.fieldItemList[index] = fieldItem;
        }

        foreach (var fieldItem in GameManager.Instance.saveData.fieldItemList)
        {
            var data = GameManager.Instance.dataManager.itemDataWrapper.itemDataList.Find(x => x.id == fieldItem.id);
            GameObject prefab = data.GetPrefab();
            if (null == prefab)
            {
                Debug.LogError("프리팹이 없었음. " + data.resourceName);
                continue;
            }

            for (int i =0;i<fieldItem.count;i++)
            {
                GameObject inst = Instantiate(prefab, parentObj.transform);
                randomSpawn(inst, data);
                
                BoxCollider instCollider = null;
                
                if(false == inst.TryGetComponent<BoxCollider>(out instCollider))
                {
                    instCollider = inst.AddComponent<BoxCollider>();
                }
                instCollider.size = new Vector3(data.floorX, data.height, data.floorY);
                //instCollider.isTrigger = true; 굳이 넣을 필요 없어 보인다. 
                //Collider[] overlappedItems = Physics.OverlapBox(instCollider.center + inst.transform.position, instCollider.size / 2);
                /*
                if (overlappedItems != null)
                {
                    Debug.Log($"overlapped items exists.");
                    foreach (var item in overlappedItems)
                    {
                        Debug.Log($"{item.gameObject.name}");
                    }                   
                    if (data.floorX <= 2 && data.floorY <= 2)
                    {
                        randomSpawn(inst, data);
                    }
                }
                */
                ItemPickup pickup = inst.GetComponent<ItemPickup>();
                if (null == pickup)
                {
                    pickup = inst.AddComponent<ItemPickup>();
                }
                pickup.Initialize(data);
                fieldItemsforCheck.Add(inst);
            }
        }


        foreach(var checkItem in fieldItemsforCheck)
        {
            //겹치는 상황이 발생하는 친구들을 식별하기 위한 코드.
            //너무 많은 삽질을 했다. 그래프로 구조 만들면, 오브젝트들이 너무 많이 늘어졌을때 이동을 어떻게 시킬것인가????????????? 이동시키지 마!

            //setActive로 해결한다. 겹치면 배열 내 애들 전부 비활성화.
            //먹어서 없어지면 그 리스트 없애고, 새 리스트를 생성한다.

            BoxCollider collider = checkItem.GetComponent<BoxCollider>();
            Collider[] overlappedItems = checkOverlap(checkItem, collider);
            
            if (overlappedItems.Length > 0)
            {
                int i = 0;
                while (i < 10)
                {
                    randomSpawnWithoutY(checkItem);
                    Debug.Log("아이템 이격 시도 완료.");
                    if (checkOverlap(checkItem, collider).Length < 1)
                    {
                        Debug.Log("아이템 이격 완료.");
                        break;
                    }
                    i++;
                }
                if (i == 10)
                {
                    Debug.Log("아이템 이격 실패.");
                    Destroy(checkItem);
                }
            }
                
            /*if (overlappedItems.Length > 0)
            {
                //Debug.Log($"{checkItem.name}에 겹치는 아이템은 : {overlappedItems.Length}개");
                didItemOverlapped.Add(checkItem, true); //체크했는지에 대해 true를 눌러준다.
               
                for (int i = 0; i < overlappedItems.Length; i++)
                {
                    //Debug.Log(overlappedItems[i]);//검출 성공! 자기자신 제외하게까지 설정 완료.

                    //other = overlappedItems[i].gameObject;
                    //BoxCollider otherCollider = other.GetComponent<BoxCollider>();
                    
                    for(int j = 0; j < otherItemOverlapped.Length; j++)
                    {

                    }
                }
                

            }           //디버깅용 코드. 무시하세요.
            */
            
        }
        
        GameManager.Instance.SaveFile();

    }

    void randomSpawn(GameObject inst, ItemData data)
    {
        
        //[TODO] 이제희 : 꽤나 적절한 랜덤 포지션을 찾아서 넣어주던가, 따로 로직을 파던가..!!!!
        //data는 오직 yaxiscal을 위한 매개변수이다.
        float randSector = UnityEngine.Random.Range(0.0f, 1.0f);
        if (randSector < 0.15625f)
        {
            inst.transform.localPosition = new Vector3(UnityEngine.Random.Range(-110f, -20f), data.yAxisCalibrationField, UnityEngine.Random.Range(40f, 110f));
        }
        else if (randSector >= 0.15625f && randSector < 0.3125f)
        {
            inst.transform.localPosition = new Vector3(UnityEngine.Random.Range(30f, 110f), data.yAxisCalibrationField, UnityEngine.Random.Range(40f, 110f));
        }
        else if (randSector >= 0.3125f && randSector < 0.35f)
        {
            inst.transform.localPosition = new Vector3(UnityEngine.Random.Range(-20f, 30f), data.yAxisCalibrationField, UnityEngine.Random.Range(40f, 110f));
        }
        else if (randSector >= 0.35f && randSector < 0.4375f)
        {
            inst.transform.localPosition = new Vector3(UnityEngine.Random.Range(-110f, -20f), data.yAxisCalibrationField, UnityEngine.Random.Range(-10f, 40f));
        }
        else if (randSector >= 0.4375f && randSector < 0.5f)
        {
            inst.transform.localPosition = new Vector3(UnityEngine.Random.Range(30f, 110f), data.yAxisCalibrationField, UnityEngine.Random.Range(-10f, 40f));
        }
        else if (randSector >= 0.5f && randSector < 0.6875f)
        {
            inst.transform.localPosition = new Vector3(UnityEngine.Random.Range(-110f, -20f), data.yAxisCalibrationField, UnityEngine.Random.Range(-110f, -10f));
        }
        else if (randSector >= 0.6875f && randSector < 0.875f)
        {
            inst.transform.localPosition = new Vector3(UnityEngine.Random.Range(30f, 110f), data.yAxisCalibrationField, UnityEngine.Random.Range(-110f, -10f));
        }
        else
        {
            inst.transform.localPosition = new Vector3(UnityEngine.Random.Range(-20f, 30f), data.yAxisCalibrationField, UnityEngine.Random.Range(-100f, -10f));
        }
    }

    void randomSpawnWithoutY(GameObject inst)
    {
        float randSector = UnityEngine.Random.Range(0.0f, 1.0f);
        if (randSector < 0.15625f)
        {
            inst.transform.localPosition = new Vector3(UnityEngine.Random.Range(-110f, -20f), inst.transform.position.y, UnityEngine.Random.Range(40f, 110f));
        }
        else if (randSector >= 0.15625f && randSector < 0.3125f)
        {
            inst.transform.localPosition = new Vector3(UnityEngine.Random.Range(30f, 110f), inst.transform.position.y, UnityEngine.Random.Range(40f, 110f));
        }
        else if (randSector >= 0.3125f && randSector < 0.35f)
        {
            inst.transform.localPosition = new Vector3(UnityEngine.Random.Range(-20f, 30f), inst.transform.position.y, UnityEngine.Random.Range(40f, 110f));
        }
        else if (randSector >= 0.35f && randSector < 0.4375f)
        {
            inst.transform.localPosition = new Vector3(UnityEngine.Random.Range(-110f, -20f), inst.transform.position.y, UnityEngine.Random.Range(-10f, 40f));
        }
        else if (randSector >= 0.4375f && randSector < 0.5f)
        {
            inst.transform.localPosition = new Vector3(UnityEngine.Random.Range(30f, 110f), inst.transform.position.y, UnityEngine.Random.Range(-10f, 40f));
        }
        else if (randSector >= 0.5f && randSector < 0.6875f)
        {
            inst.transform.localPosition = new Vector3(UnityEngine.Random.Range(-110f, -20f), inst.transform.position.y, UnityEngine.Random.Range(-110f, -10f));
        }
        else if (randSector >= 0.6875f && randSector < 0.875f)
        {
            inst.transform.localPosition = new Vector3(UnityEngine.Random.Range(30f, 110f), inst.transform.position.y, UnityEngine.Random.Range(-110f, -10f));
        }
        else
        {
            inst.transform.localPosition = new Vector3(UnityEngine.Random.Range(-20f, 30f), inst.transform.position.y, UnityEngine.Random.Range(-100f, -10f));
        }
    }
    Collider[] checkOverlap(GameObject gameObject, BoxCollider collider)
    {
        collider.enabled = false;
        Collider[] overlappedItems = Physics.OverlapBox(gameObject.transform.position, collider.size / 2, Quaternion.identity, layer);
        collider.enabled = true;

        return overlappedItems;
    }

    // Update is called once per frame
    void Update()
    {
        
    }
}
