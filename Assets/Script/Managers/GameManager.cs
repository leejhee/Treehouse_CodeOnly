using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
public class GameManager : MonoBehaviour
{
    public DataManager dataManager;

    public static GameManager Instance;
    public SaveDataClass saveData;
    public Image fade;
    public bool clickedFlag = false;
    GameObject[] UIArray = null;

    // Start is called before the first frame update
    void Awake()
    {
        if(null == Instance)
        {
            dataManager = new DataManager();
            dataManager.Initialize();

            Instance = this;
            DontDestroyOnLoad(gameObject);
            saveData = JsonManager.LoadSaveData();
            if (saveData == null)
            {
                JsonManager.SaveJson(new SaveDataClass());
            }
            


        }
        else
        {
            Destroy(gameObject);
        }

    }

    private void Start()
    {
        //Debug.Log(Application.persistentDataPath);
        UIArray = GameObject.FindGameObjectsWithTag("UI");
    }

    private void Update()
    {
        if (clickedFlag == true && Input.GetMouseButtonDown(0) == true)
        {
            if(UIArray != null)
            {
                foreach (GameObject UIs in UIArray)
                {
                    UIs.SetActive(true);
                }
                clickedFlag = false;
            }           
        }
    }

    public void AddCoin(int count)
    {
        if (null == saveData)
        {
            saveData = new SaveDataClass();
        }

        saveData.coin += count;
        Instance.SaveFile();
        StoreManager.instance.UpdateCoin();
    }

    public void AddHouse()
    {
        if (null == saveData)
        {
            saveData = new SaveDataClass();
        }
        int nowCount = saveData.houseList.Count;
        var feature = new HouseFeature();
        feature.id = nowCount + 1;
        saveData.houseList.Add(feature);
    }

    public void AddItemOnInventory(int itemId)
    {
        if(null == saveData)
        {
            saveData = new SaveDataClass();
        }

        var feature = saveData.inventoryItemList.Find(x => x.id == itemId);
        if(null == feature)
        {
            feature = new ItemFeature(itemId);
            feature.count = 1;
            saveData.inventoryItemList.Add(feature);
        }
        else
        {
            feature.count++;
        }
    }

    public void RemoveItemOnInventory(int itemId)
    {
        if (null == saveData)
        {
            saveData = new SaveDataClass();
        }

        var feature = saveData.inventoryItemList.Find(x => x.id == itemId);
        feature.count--;
        if (feature.count <= 0)
        {
            saveData.inventoryItemList.Remove(feature);
        }
    }

    public void RemoveItemOnField(int itemId)
    {
        if (null == saveData)
        {
            saveData = new SaveDataClass();
        }

        int index = saveData.fieldItemList.FindIndex(x => x.id == itemId);
        var fieldFeature = saveData.fieldItemList[index];
        fieldFeature.count--;
        if(fieldFeature.count <=0)
        {
            saveData.fieldItemList.RemoveAt(index);
        }
        else
        {
            saveData.fieldItemList[index] = fieldFeature;
        }
    }

    public void OnHideAllClick()
    {
        if (UIArray != null)
        {
            if (clickedFlag == false)
            {
                foreach (GameObject UIs in UIArray)
                {
                    UIs.SetActive(false);
                }
                clickedFlag = true;
            }
        }
        else { return; }
    }

    public void SaveFile()
    {
        if(saveData != null)
        {
            Debug.LogWarning(GameManager.Instance.saveData.lastEndTime + " 그리고 현재시간 " + System.DateTime.Now);

            GameManager.Instance.saveData.lastEndTime = System.DateTime.Now;
            GameManager.Instance.saveData.dateTimeText = GameManager.Instance.saveData.lastEndTime.ToString();

            JsonManager.SaveJson(saveData);
        }
        else
        {
            Debug.Log("세이브데이터가 NULLL이었어요!!!!!");
        }
    }

    public void SetFade(System.Action callbackOnFadeOut)
    {
        StartCoroutine(FadeCor(callbackOnFadeOut));
    }
    
    IEnumerator FadeCor(System.Action callbackOnFadeOut)
    {
        fade.gameObject.SetActive(true);
        StartCoroutine(ModuleManager.FadeModule_Image(fade, 0, 1, 0.5f));
        yield return new WaitForSeconds(0.5f);
        callbackOnFadeOut?.Invoke();
        yield return new WaitForSeconds(0.2f);
        StartCoroutine(ModuleManager.FadeModule_Image(fade, 1, 0, 0.5f));
        yield return new WaitForSeconds(0.6f);
        fade.gameObject.SetActive(false);
    }

}
