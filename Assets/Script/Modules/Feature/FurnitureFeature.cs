using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[System.Serializable]
public class FurnitureFeature
{
    public int arrangeNo;   //새로 가구를 배치할 때 배정해주는 고유번호.

    public float x;
    public float y;
    public float z;

    public int id;  //item의 id임.

    public float yRotation = 0f;

    private ItemData data;
    public ItemData GetData()
    {
        if( null == data )
        {
            data = GameManager.Instance.dataManager.itemDataWrapper.itemDataList.Find(x => x.id == id);
        }
        return data;
    }
}
