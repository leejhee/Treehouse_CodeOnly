using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[System.Serializable]
public class ItemFeature
{
    public int id;  //1
    public int count;   //10

    public ItemFeature()
    {

    }

    public ItemFeature(int idParam)
    {
        this.id = idParam;
    }
}
