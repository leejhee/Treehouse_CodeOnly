using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using System.Linq;

public class InventoryManager : MonoBehaviour
{
    public static InventoryManager instance;
    private List<ItemSlot> itemSlotPool = new List<ItemSlot>();

    public GameObject inventoryCanvasRoot;
    public Transform scrollViewContent;
    public ItemSlot itemSlotPrefab;

    public Image mainPanelImage;
    public TMPro.TMP_Text coinCount;
    public ItemData selectedItem;

    private void Awake()
    {
        instance = this;
        selectedItem = null;
        OnInventoryItemButtonClick(null,0);
    }

    private void OnDestroy()
    {
        instance = null;
    }

    public void PickUpFieldItem(int id)
    {
        GameManager.Instance.AddItemOnInventory(id);
        GameManager.Instance.RemoveItemOnField(id);
        GameManager.Instance.SaveFile();
    }

    public void Remove(int id)
    {
        GameManager.Instance.RemoveItemOnInventory(id);
        GameManager.Instance.SaveFile();
    }

    public void OnInventoryItemButtonClick(ItemData data, int count)
    {
        selectedItem = data;
        if ( null == data )
        {
            mainPanelImage.gameObject.SetActive(false);
            return;
        }
        mainPanelImage.gameObject.SetActive(true);
        mainPanelImage.sprite = data.GetInventoryIcon();
        mainPanelImage.SetNativeSize();
        //true일 때 왼쪽!!!
        Color color = count <= 0 ? Color.black : Color.white;
        mainPanelImage.color = color;
    }

    public void OnArrangeButtonClick()
    {
        if(true == WarpManager.instance.isField)
        {
            SystemMessageFunc.Notify("여기서는 배치할 수 없어요.", true);
            return;
        }

        //openbutton이나 closebutton이 켜져있을 때에 배치 모드를 들어가면 안된다.
        if (true == WarpManager.instance.toAnotherHouseButton.activeSelf || true == WarpManager.instance.closeButton.activeSelf)
        {
            SystemMessageFunc.Notify("워프 화살표 있을 때는 배치 못해요.", true);
            return;
        }

        OnExitButton();
        ArrangeManager.instance.StartArrangeFurniture(selectedItem);
    }

    public void OnInventroyOpenButton()
    {
        if (true == ArrangeManager.instance.nowArranging)
        {
            SystemMessageFunc.Notify("배치를 완료한 후 인벤토리를 열어주세요.", true);
            return;
        }

        SystemMessageFunc.Debug("인벤토리 오픈");
        ModuleManager.isInteractable = false;
        inventoryCanvasRoot.SetActive(true);
        ListItems();
    }

    public void OnExitButton()
    {
        ModuleManager.isInteractable = true;
        inventoryCanvasRoot.SetActive(false);
    }

    public void OnStoreOpenButton()
    {
        SystemMessageFunc.Debug("상점->인벤토리 오픈");
        inventoryCanvasRoot.SetActive(false);
        StoreManager.instance.OnStoreOpenButton();
    }

    public void ListItems()
    {
        foreach(var itemSlot in itemSlotPool)
        {
            itemSlot.gameObject.SetActive(false);
        }

        coinCount.SetText(GameManager.Instance.saveData.coin.ToString("N0"));
        int i = -1;
        //for int를 C#에서 지원하는 방식. Array, List, Dictionary를 순회하는 방식이다.
        foreach(var data in GameManager.Instance.dataManager.itemDataWrapper.itemDataList)
        {
            i++;

            //[TODO] 이제희 : LinQ공부!!
            var item = GameManager.Instance.saveData.inventoryItemList.Find(x => x.id == data.id);
            /*if (item.id == 14)
            {
                continue;
            }*/
                 
            int count = 0;
            if(null != item )
            {
                Debug.Log("아이템의 아이디는" + item.id + "아이템의 카운트는" + item.count);
                count = item.count; 
            }
            else
            {
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
            //[TODO] 이제희 : C# 람다식 공부해오기, Delegate, Action.
            itemButton.onClick.AddListener(() => { OnInventoryItemButtonClick(data, count); });
        }
    }



    
}
