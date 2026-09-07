using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[System.Serializable]
public class ItemData
{
    //읽기전용. readonly안써놨다고 막 수정하면 안됨!!
    public enum EArrangeType
    {
        NONE = -1,
        Floor,  //배치 시 바닥에붙음.
        Wall,   //배치 시 벽에붙음
    }

    public enum EItemType
    {
        NONE = -1,
        Furniture,
        House,
        Coin
    }

    public int id = -1;
    public string itemName = "";

    public string itemType = "";
    public EItemType eItemType = EItemType.NONE;

    public string arrangeType = "";
    public EArrangeType eArrangeType = EArrangeType.NONE;

    public int price = -1;
    public string resourceName ="";
    public int floorX = -1;
    public int floorY = -1;
    public int height = -1;
    public int maxFieldDropCount = -1;
    public int maxBuyCount = -1;
    public int regenTimeMinutes = -1;
    public int regenCount = -1;
    public int regenProbabilityPercent = -1;
    public float yAxisCalibrationField = 0f; //필드에서의 prefab 높이 조정값.

    private Sprite inventoryIcon;
    private GameObject prefab;

    public Sprite GetInventoryIcon()
    {
        if(null == inventoryIcon)
        {
            inventoryIcon = Resources.Load<Sprite>("Item/Icon/" + resourceName);
        }
        return inventoryIcon;
    }


    public GameObject GetPrefab()
    {
        if (null == prefab)
        {
            prefab = Resources.Load<GameObject>("Item/FieldObject/" + resourceName);
        }
        return prefab;
    }

}
