using System.Collections.Generic;
using UnityEngine;

public class SimpleAssetCollectionData : ScriptableObject
{
    public Color color = Color.white;
    public List<Object> items = new List<Object>();
    public string sourceFolderGuid = string.Empty;
    public List<string> sourceFolderGuids = new List<string>();
    public List<Object> manualItems = new List<Object>();
    public List<Object> folderItems = new List<Object>();
    public int folderSourceVersion;
}
