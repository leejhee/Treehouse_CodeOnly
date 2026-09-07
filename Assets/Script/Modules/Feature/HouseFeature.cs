using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[System.Serializable]
public class HouseFeature 
{
    public int id;  //house의 id임. 테이블에는 존재하지않고, 인게임 로직에서 array Index등으로 사용.
    public List<FurnitureFeature> furnitureList = new List<FurnitureFeature>();

    public int GetArrangeNo()
    {
        int num = 0;
        for(int i =0;i<furnitureList.Count *2;i++)
        {
            //이거는 LinQ라는 C#고유의 쿼리인데, 나중에 찾아보도록.
            if( null ==furnitureList.Find(x => x.arrangeNo == i))
            {
                return i;
            }
        }
        return num;
    }
}
