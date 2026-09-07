using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class ItemPickup : MonoBehaviour
{
    public ItemData itemData;
    public void Initialize(ItemData item)
    {
        itemData = item;
    }


    void Pickup()
    {
        switch(itemData.eItemType)
        {
            case ItemData.EItemType.Coin:
                GameManager.Instance.AddCoin(1);
                SystemMessageFunc.Debug($"코인픽업\n현재코인 : {GameManager.Instance.saveData.coin}");
                break;
            case ItemData.EItemType.Furniture:
                InventoryManager.instance.PickUpFieldItem(itemData.id);
                SystemMessageFunc.Debug($"{itemData.resourceName} 픽업");
                break;
        }
        
        Destroy(gameObject);
    }

    private void OnMouseDown()
    {
        Debug.Log($"레이케스트 {ModuleManager.isInteractable}");
        if (false == ModuleManager.isInteractable)
        {
            return;
        }

        //[TODO] 이제희 : null이 뜨면 안되는데 뜨고있음. 재현 스텝 모름. 한 번 발생.
        GameObject obj = CharacterControl.instance?.gameObject;
        if(null == obj)
        {
            Debug.Log(itemData.resourceName + " 터치");
            return;
        }
        var gap = transform.position - obj.transform.position;
        if( gap.magnitude > 7)
        {
            Debug.Log(itemData.resourceName + " 터치, 거리안됨, 거리 : " + gap.magnitude);
            return;
        }
        Debug.Log(itemData.resourceName + " 터치, 픽업성공");
        Pickup();
    }
}
