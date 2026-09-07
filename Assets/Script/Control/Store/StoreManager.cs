using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class StoreManager : MonoBehaviour
{
    public static StoreManager instance;
    private List<ItemSlot> itemSlotPool = new List<ItemSlot>();

    public GameObject storeCanvasRoot;
    public Transform scrollViewContent;
    public ItemSlot itemSlotPrefab;

    public Image mainPanelImage;
    public TMPro.TMP_Text coinCount;
    public TMPro.TMP_Text selectedCoinCount;
    public GameObject selectedCoinRoot;
    public ItemData selectedItem;

    public Image mainHouseGauge;
    public TMPro.TMP_Text mainCoinCount;
    private void Awake()
    {
        instance = this;
    }

    private void OnDestroy()
    {
        instance = null;
    }

    private void Start()
    {
        SetMainScreen();
        if (GameManager.Instance.saveData.houseList.Count < 1)
        {
            GameManager.Instance.AddHouse();
        }
    }

    void SetMainScreen()
    {
        int hosueCount = GameManager.Instance.saveData.houseList.Count;
        var size = mainHouseGauge.rectTransform.sizeDelta;
        size.x = 240f * ((float)hosueCount - 1f) / 3.0f;
        mainHouseGauge.rectTransform.sizeDelta = size;

        mainCoinCount.SetText(GameManager.Instance.saveData.coin.ToString("N0"));
    }

    public void BuyItem(int id)
    {
        GameManager.Instance.AddItemOnInventory(id);
        GameManager.Instance.SaveFile();
    }

    public void Remove(int id)
    {
        GameManager.Instance.RemoveItemOnInventory(id);
        GameManager.Instance.SaveFile();
    }

    public void OnStoreItemButtonClick(ItemData data)
    {
        selectedItem = data;
        if (null == data
            || data.price <= 0)
        {
            selectedCoinRoot.SetActive(false);
            mainPanelImage.gameObject.SetActive(false);
            return;
        }

        selectedCoinRoot.SetActive(true);
        selectedCoinCount.SetText(data.price.ToString("N0"));

        mainPanelImage.gameObject.SetActive(true);
        mainPanelImage.sprite = data.GetInventoryIcon();
        mainPanelImage.SetNativeSize();

    }

    public void OnBuyButtonClick()
    {
        if (null == selectedItem || selectedItem.price <= 0)
        {
            SystemMessageFunc.Notify("사고싶은 아이템을 먼저 터치해주세요.",true);
            selectedCoinRoot.SetActive(false);
            mainPanelImage.gameObject.SetActive(false);
            return;
        }

        int coin = GameManager.Instance.saveData.coin;
        if(coin < selectedItem.price)
        {
            SystemMessageFunc.Notify($"코인이 {selectedItem.price - coin}만큼 부족해요",true);
            return;
        }

        
        if(selectedItem.eItemType == ItemData.EItemType.House)
        {
            if (GameManager.Instance.saveData.houseList.Count >= 4)
            {
                SystemMessageFunc.Notify("집은 4군데가 최대입니다.");
                return;
            }
            GameManager.Instance.AddCoin(-1 * selectedItem.price);
            GameManager.Instance.AddHouse();
            UpdateHouseCount();
            SystemMessageFunc.Notify("새로운 집 오픈!");
            SetMainScreen();
        }
        else
        {
            GameManager.Instance.AddCoin(-1 * selectedItem.price);
            GameManager.Instance.AddItemOnInventory(selectedItem.id);
            SystemMessageFunc.Notify("구매 완료했습니다");
        }
        
        GameManager.Instance.SaveFile();

        
        UpdateCoin();
    }

    public void OnStoreOpenButton()
    {
        if (true == ArrangeManager.instance.nowArranging)
        {
            SystemMessageFunc.Notify("배치를 완료한 후 상점을 열어주세요.", true);
            return;
        }
        ModuleManager.isInteractable = false;
        SystemMessageFunc.Debug("상점 오픈");
        storeCanvasRoot.SetActive(true);
        ListItems();
    }

    public void OnInventoryOpenButton()
    {
        SystemMessageFunc.Debug("상점->인벤토리 오픈");
        storeCanvasRoot.SetActive(false);
        InventoryManager.instance.OnInventroyOpenButton();
    }

    public void OnExitButton()
    {
        ModuleManager.isInteractable = true;
        storeCanvasRoot.SetActive(false);
    }

    public void UpdateCoin()
    {
        //룰루
        coinCount.SetText(GameManager.Instance.saveData.coin.ToString("N0"));
        mainCoinCount.SetText(GameManager.Instance.saveData.coin.ToString("N0"));
    }
    
    public void UpdateHouseCount()
    {
        ItemSlot houseSlot = itemSlotPool.Find(x => x.countText.IsActive() == true);
        houseSlot.countText.SetText((4 - GameManager.Instance.saveData.houseList.Count).ToString());
    }

    public void ListItems()
    {
        SetMainScreen();

        foreach (var itemSlot in itemSlotPool)
        {
            itemSlot.gameObject.SetActive(false);
        }

        coinCount.SetText(GameManager.Instance.saveData.coin.ToString("N0"));
        mainCoinCount.SetText(GameManager.Instance.saveData.coin.ToString("N0"));
        int houseCount = GameManager.Instance.saveData.houseList.Count;
        int i = -1;
        foreach(var data in GameManager.Instance.dataManager.itemDataWrapper.itemDataList)
        {
            i++;

            int count = data.maxBuyCount;
            switch (data.eItemType)
            {
                case ItemData.EItemType.House:
                    count -= houseCount - 1;
                    if(count <=0)
                    {
                        //하우스는 다 사서 숨기기.
                        continue;
                    }
                    break;
                case ItemData.EItemType.Furniture:
                    break;
                default:
                    continue;
            }

            ItemSlot slotItem = null;
            if (i >= itemSlotPool.Count)
            {
                slotItem = Instantiate<ItemSlot>(itemSlotPrefab, scrollViewContent);
                itemSlotPool.Add(slotItem);
                slotItem.gameObject.SetActive(true);
            }
            else
            {
                slotItem = itemSlotPool[i];
                slotItem.gameObject.SetActive(true);
            }

            if (false == slotItem.SetItem(data, count))
            {
                return;
            }            

            var itemButton = slotItem.button;
            //아이템 버튼을 클릭했을 때 다른 패널의 이미지를 설정
            itemButton.onClick.AddListener(() => { OnStoreItemButtonClick(data); });
        }
    }

    private void Update()
    {
#if UNITY_EDITOR
        if(Input.GetKeyDown(KeyCode.M))
        {
            GameManager.Instance.AddCoin(100);
            GameManager.Instance.SaveFile();
            UpdateCoin();
            SystemMessageFunc.Debug("치트 : 100코인 획득");
            SetMainScreen();
        }
#endif
    }


}
