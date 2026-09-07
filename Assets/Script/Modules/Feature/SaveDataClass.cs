using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[System.Serializable]
public class SaveDataClass
{
    //읽기쓰기 가능.
    [System.Serializable]
    public struct FieldItem
    {
        public int id;
        public int count;
    }

    public string userName; //당장은 쓰는곳없음.
    public int coin = 0;
    public List<ItemFeature> inventoryItemList;
    public List<HouseFeature> houseList;

    public List<FieldItem> fieldItemList;   //게임 시작할 때 
    public string dateTimeText;


    public DateTime lastEndTime;    //가장 마지막에 끈 정보.
    

    public SaveDataClass()
    {
        inventoryItemList = new List<ItemFeature>();
        houseList = new List<HouseFeature>();    
        fieldItemList = new List<FieldItem>();
        lastEndTime = DateTime.Now;
    }

}
