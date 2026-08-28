using System.Collections.Generic;
using UnityEngine;

[System.Serializable]
public class SimpleAssetCollectionData
{
    public string name = "New Collection";
    public Color color = Color.white;
    public List<Object> items = new List<Object>();
}

public class SmartLibraryData : ScriptableObject
{
    public List<SimpleAssetCollectionData> collections = new List<SimpleAssetCollectionData>();
}
