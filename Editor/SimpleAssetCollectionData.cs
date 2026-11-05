using System.Collections.Generic;
using UnityEngine;

public class SimpleAssetCollectionData : ScriptableObject
{
    public Color color = Color.white;
    public List<Object> items = new List<Object>();
    public string sourceFolderGuid = string.Empty;
    public List<Object> folderItems = new List<Object>();
}
