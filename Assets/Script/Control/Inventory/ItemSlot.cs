using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class ItemSlot : MonoBehaviour
{
    public Button button;
    public Image iconImage;
    public TMP_Text countText;

    public bool SetItem(ItemData item, int count, bool setBlack = false)
    {
        Sprite iconLoadSprite = item.GetInventoryIcon();
        if (null == iconLoadSprite)
        {
            SystemMessageFunc.Debug("아이콘을 못찾았다, 이름은 " + item.resourceName);
            gameObject.SetActive(false);
            return false;
        }
        iconImage.sprite = iconLoadSprite;
        iconImage.SetNativeSize();
        if(count <= 0)
        {
            if(true == setBlack)
            {
                iconImage.color = Color.black;
            }
            else
            {
                iconImage.color = Color.white;
            }
            countText.gameObject.SetActive(false);
        }
        else
        {
            iconImage.color = Color.white;
            countText.gameObject.SetActive(true);
            countText.SetText(count.ToString("N0"));
        }
        return true;

    }
}
