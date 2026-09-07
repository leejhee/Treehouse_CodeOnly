using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[System.Serializable]
public class DataManager
{
    public ItemDataWrapper itemDataWrapper;

    public void Initialize()
    {
        Debug.Log("데이터 이니셜라이즈");
        itemDataWrapper = JsonManager.ResourceDataLoad<ItemDataWrapper>("ItemData");
        itemDataWrapper.SetEnum();
    }
}
