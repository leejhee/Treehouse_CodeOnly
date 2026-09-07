using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System;

[System.Serializable]
public class ItemDataWrapper
{
    public List<ItemData> itemDataList;

    public void SetEnum()
    {
        foreach(var item in itemDataList)
        {
            item.eItemType = Enum.Parse<ItemData.EItemType>(item.itemType);
            if(false == Enum.TryParse<ItemData.EArrangeType>(item.arrangeType,out item.eArrangeType))
            {
                item.eArrangeType = ItemData.EArrangeType.NONE;
            }
        }
    }
}
