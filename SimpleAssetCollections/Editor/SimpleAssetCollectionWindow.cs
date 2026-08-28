using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

using Toggle = UnityEngine.UIElements.Toggle;

public class SimpleAssetCollectionWindow : EditorWindow
{
    private const string DATA_FOLDER = "Assets/SimpleAssetCollections";
    private const string DATA_PATH = DATA_FOLDER + "/AssetCollectionsData.asset";

    private SmartLibraryData libraryData;
    private SimpleAssetCollectionData selectedCollection;

    private ScrollView collectionScrollView;
    private ScrollView itemsScrollView;
    private Slider iconSizeSlider;
    private Toggle showPreviewToggle;

    private TwoPaneSplitView mainSplitView;
    private TwoPaneSplitView rightSplitView;

    private VisualElement previewPanel;
    private VisualElement previewContentContainer;
    private Editor cachedEditor;

    private float iconSize = 80f;
    private bool showPreview = true;
    private int itemsPerRow = 4;

    private HashSet<Object> selectedObjects = new HashSet<Object>();
    private Object lastSelectedObject;

    private HashSet<int> pendingPreviewInstanceIDs = new HashSet<int>();

    [MenuItem("Window/Asset Collections")]
    public static void ShowWindow()
    {
        SimpleAssetCollectionWindow wnd = GetWindow<SimpleAssetCollectionWindow>();
        wnd.titleContent = new GUIContent("Asset Collections");
        wnd.minSize = new Vector2(750, 450);
    }

    private void OnEnable()
    {
        LoadOrCreateData();
        AssetPreview.SetPreviewTextureCacheSize(2000);
        EditorApplication.update += OnEditorUpdate;
    }

    private void OnDisable()
    {
        EditorApplication.update -= OnEditorUpdate;
        SaveData();
        CleanUpEditor();
    }

    private void OnEditorUpdate()
    {
        if (pendingPreviewInstanceIDs.Count > 0)
        {
            List<int> completed = new List<int>();
            foreach (var instanceID in pendingPreviewInstanceIDs)
            {
                if (!AssetPreview.IsLoadingAssetPreview(instanceID))
                {
                    completed.Add(instanceID);
                }
            }

            if (completed.Count > 0)
            {
                foreach (var id in completed)
                {
                    pendingPreviewInstanceIDs.Remove(id);
                }
                RefreshItemsGrid();
            }
        }
    }

    private void CleanUpEditor()
    {
        if (cachedEditor != null)
        {
            Editor editorToDestroy = cachedEditor;
            cachedEditor = null;

            EditorApplication.delayCall += () =>
            {
                if (editorToDestroy != null)
                {
                    DestroyImmediate(editorToDestroy);
                }
            };
        }
    }

    private void LoadOrCreateData()
    {
        libraryData = AssetDatabase.LoadAssetAtPath<SmartLibraryData>(DATA_PATH);

        if (libraryData == null)
        {
            if (!Directory.Exists(DATA_FOLDER))
            {
                Directory.CreateDirectory(DATA_FOLDER);
                AssetDatabase.Refresh();
            }

            libraryData = CreateInstance<SmartLibraryData>();
            AssetDatabase.CreateAsset(libraryData, DATA_PATH);
            AssetDatabase.SaveAssets();
        }

        if (libraryData.collections.Count > 0)
        {
            selectedCollection = libraryData.collections[0];
        }
    }

    private void SaveData()
    {
        if (libraryData != null)
        {
            EditorUtility.SetDirty(libraryData);
            AssetDatabase.SaveAssets();
        }
    }

    public void CreateGUI()
    {
        VisualElement root = rootVisualElement;

        // 0. MAIN SPLIT VIEW
        mainSplitView = new TwoPaneSplitView(0, 200, TwoPaneSplitViewOrientation.Horizontal);
        mainSplitView.style.flexGrow = 1;
        root.Add(mainSplitView);

        // 1. LEFT PANEL: Collections
        VisualElement leftPanel = new VisualElement();
        SetPadding(leftPanel, 6);
        leftPanel.style.backgroundColor = new Color(0.22f, 0.22f, 0.22f, 0.4f);

        Button addCollectionBtn = new Button(CreateNewCollection) { text = "+ Collection" };
        addCollectionBtn.style.height = 28;
        addCollectionBtn.style.marginBottom = 6;
        addCollectionBtn.style.unityFontStyleAndWeight = FontStyle.Bold;
        leftPanel.Add(addCollectionBtn);

        collectionScrollView = new ScrollView();
        collectionScrollView.style.flexGrow = 1;
        leftPanel.Add(collectionScrollView);
        mainSplitView.Add(leftPanel);

        RefreshCollectionsList();

        // RIGHT SPLIT VIEW (Items Grid & Preview)
        rightSplitView = new TwoPaneSplitView(1, 260, TwoPaneSplitViewOrientation.Horizontal);
        rightSplitView.style.flexGrow = 1;
        mainSplitView.Add(rightSplitView);

        // 2. CENTER PANEL: Items Grid
        VisualElement centerPanel = new VisualElement();
        centerPanel.style.flexGrow = 1;
        SetPadding(centerPanel, 6);

        // Toolbar block
        VisualElement topBar = new VisualElement();
        topBar.style.flexDirection = FlexDirection.Row;
        topBar.style.justifyContent = Justify.SpaceBetween;
        topBar.style.alignItems = Align.Center;
        topBar.style.marginBottom = 8;
        topBar.style.paddingTop = 6;
        topBar.style.paddingBottom = 6;
        topBar.style.paddingLeft = 8;
        topBar.style.paddingRight = 8;
        topBar.style.backgroundColor = new Color(0.18f, 0.18f, 0.18f, 0.8f);
        topBar.style.borderBottomWidth = 1;
        topBar.style.borderBottomColor = new Color(0.1f, 0.1f, 0.1f);
        topBar.style.borderTopWidth = 1;
        topBar.style.borderTopColor = new Color(0.28f, 0.28f, 0.28f);

        VisualElement controlsGroup = new VisualElement();
        controlsGroup.style.flexDirection = FlexDirection.Row;
        controlsGroup.style.alignItems = Align.Center;

        iconSizeSlider = new Slider("Icon Size", 50, 150);
        iconSizeSlider.value = iconSize;
        iconSizeSlider.style.width = 170;
        iconSizeSlider.RegisterValueChangedCallback(evt =>
        {
            iconSize = evt.newValue;
            CalculateItemsPerRow();
            RefreshItemsGrid();
        });
        controlsGroup.Add(iconSizeSlider);

        showPreviewToggle = new Toggle("Preview");
        showPreviewToggle.value = showPreview;
        showPreviewToggle.style.marginLeft = 15;
        showPreviewToggle.RegisterValueChangedCallback(evt =>
        {
            showPreview = evt.newValue;
            TogglePreviewPanel(showPreview);
        });
        controlsGroup.Add(showPreviewToggle);

        topBar.Add(controlsGroup);

        Label dropHint = new Label("Press [Del] to remove");
        dropHint.style.color = new Color(0.6f, 0.6f, 0.6f);
        dropHint.style.fontSize = 11;
        topBar.Add(dropHint);

        centerPanel.Add(topBar);

        itemsScrollView = new ScrollView();
        itemsScrollView.style.flexGrow = 1;

        itemsScrollView.RegisterCallback<GeometryChangedEvent>(evt =>
        {
            if (CalculateItemsPerRow())
            {
                RefreshItemsGrid();
            }
        });

        itemsScrollView.RegisterCallback<DragUpdatedEvent>(evt =>
        {
            DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
        });
        itemsScrollView.RegisterCallback<DragPerformEvent>(evt =>
        {
            DragAndDrop.AcceptDrag();
            if (selectedCollection != null)
            {
                bool added = false;
                foreach (var obj in DragAndDrop.objectReferences)
                {
                    if (obj != null && !selectedCollection.items.Contains(obj))
                    {
                        selectedCollection.items.Add(obj);
                        added = true;
                    }
                }

                if (added)
                {
                    SaveData();
                    RefreshItemsGrid();
                    RefreshCollectionsList();
                }
            }
        });

        centerPanel.Add(itemsScrollView);
        rightSplitView.Add(centerPanel);

        // 3. RIGHT PANEL: Interactive Preview
        previewPanel = new VisualElement();
        previewPanel.style.flexGrow = 1;
        SetPadding(previewPanel, 8);
        previewPanel.style.backgroundColor = new Color(0.2f, 0.2f, 0.2f, 0.3f);

        Label previewTitle = new Label("Inspector Preview");
        previewTitle.style.unityFontStyleAndWeight = FontStyle.Bold;
        previewTitle.style.marginBottom = 8;
        previewPanel.Add(previewTitle);

        previewContentContainer = new VisualElement();
        previewContentContainer.style.flexGrow = 1;
        previewPanel.Add(previewContentContainer);

        rightSplitView.Add(previewPanel);

        root.schedule.Execute(() => TogglePreviewPanel(showPreview));
        RefreshItemsGrid();
    }

    // Надежный перехват клавиши Delete / Backspace через систему событий окна
    private void OnGUI()
    {
        Event e = Event.current;
        if (e.type == EventType.KeyDown && (e.keyCode == KeyCode.Delete || e.keyCode == KeyCode.Backspace))
        {
            if (selectedObjects.Count > 0)
            {
                DeleteSelectedItems();
                e.Use();
            }
        }
    }

    private void RefreshCollectionsList()
    {
        if (collectionScrollView == null) return;
        collectionScrollView.Clear();

        foreach (var col in libraryData.collections)
        {
            VisualElement container = new VisualElement();
            container.style.flexDirection = FlexDirection.Row;
            container.style.alignItems = Align.Center;
            container.style.height = 26;
            container.style.paddingLeft = 6;
            container.style.paddingRight = 8;
            container.style.marginBottom = 2;
            container.style.borderTopLeftRadius = 4;
            container.style.borderBottomLeftRadius = 4;
            container.style.borderTopRightRadius = 4;
            container.style.borderBottomRightRadius = 4;

            if (selectedCollection == col)
            {
                container.style.backgroundColor = new Color(0.25f, 0.45f, 0.7f, 0.8f);
            }
            else
            {
                container.style.backgroundColor = Color.clear;
            }

            VisualElement colorBar = new VisualElement();
            colorBar.style.width = 4;
            colorBar.style.height = 16;
            colorBar.style.marginRight = 6;
            colorBar.style.borderTopRightRadius = 2;
            colorBar.style.borderBottomRightRadius = 2;
            colorBar.style.backgroundColor = new StyleColor(col.color);
            container.Add(colorBar);

            Label label = new Label();
            label.style.unityTextAlign = TextAnchor.MiddleLeft;
            label.style.flexGrow = 1;
            int count = col.items != null ? col.items.Count : 0;
            label.text = $"{col.name}  ({count})";
            container.Add(label);

            container.RegisterCallback<MouseDownEvent>(evt =>
            {
                if (evt.button == 0)
                {
                    selectedCollection = col;
                    selectedObjects.Clear();
                    UpdateUnitySelection();
                    RefreshItemsGrid();
                    RefreshCollectionsList();
                    evt.StopPropagation();
                }
            });

            container.RegisterCallback<ContextClickEvent>(evt =>
            {
                GenericMenu menu = new GenericMenu();
                menu.AddItem(new GUIContent("Rename"), false, () => StartInlineRename(label, col));
                menu.AddItem(new GUIContent("Delete"), false, () => DeleteCollection(col));
                menu.ShowAsContext();
                evt.StopPropagation();
            });

            container.RegisterCallback<DragUpdatedEvent>(evt =>
            {
                if (DragAndDrop.objectReferences.Length > 0)
                {
                    DragAndDrop.visualMode = DragAndDropVisualMode.Move;
                    evt.StopPropagation();
                }
            });

            container.RegisterCallback<DragPerformEvent>(evt =>
            {
                DragAndDrop.AcceptDrag();
                evt.StopPropagation();

                var sourceCollection = DragAndDrop.GetGenericData("SourceCollection") as SimpleAssetCollectionData;

                bool changed = false;
                foreach (var obj in DragAndDrop.objectReferences)
                {
                    if (obj == null) continue;

                    if (sourceCollection != null && sourceCollection != col)
                    {
                        sourceCollection.items.Remove(obj);
                    }

                    if (!col.items.Contains(obj))
                    {
                        col.items.Add(obj);
                        changed = true;
                    }
                }

                if (changed || sourceCollection != null)
                {
                    SaveData();
                    selectedObjects.Clear();
                    UpdateUnitySelection();
                    RefreshItemsGrid();
                    RefreshCollectionsList();
                }
            });

            collectionScrollView.Add(container);
        }
    }

    private bool CalculateItemsPerRow()
    {
        if (itemsScrollView == null) return false;
        float width = itemsScrollView.resolvedStyle.width;
        if (width <= 0) return false;

        int newCount = Mathf.Max(1, Mathf.FloorToInt(width / (iconSize + 12)));
        if (newCount != itemsPerRow)
        {
            itemsPerRow = newCount;
            return true;
        }
        return false;
    }

    private VisualElement MakeItemCard(Object obj)
    {
        VisualElement itemCard = new VisualElement();
        itemCard.style.width = iconSize;
        itemCard.style.height = iconSize + 25;
        SetMargin(itemCard, 3);
        itemCard.style.alignItems = Align.Center;

        if (selectedObjects.Contains(obj))
        {
            itemCard.style.backgroundColor = new Color(0.25f, 0.5f, 0.85f, 0.7f);
            itemCard.style.borderTopLeftRadius = 4;
            itemCard.style.borderTopRightRadius = 4;
            itemCard.style.borderBottomLeftRadius = 4;
            itemCard.style.borderBottomRightRadius = 4;
        }

        Image previewImage = new Image();
        previewImage.style.width = iconSize;
        previewImage.style.height = iconSize;

        Texture2D texture = AssetPreview.GetAssetPreview(obj);
        if (texture == null)
        {
            texture = AssetPreview.GetMiniThumbnail(obj);
            if (AssetPreview.IsLoadingAssetPreview(obj.GetInstanceID()))
            {
                pendingPreviewInstanceIDs.Add(obj.GetInstanceID());
            }
        }
        previewImage.image = texture;
        itemCard.Add(previewImage);

        Label nameLabel = new Label(obj.name);
        nameLabel.style.fontSize = 10;
        nameLabel.style.width = iconSize;
        nameLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
        nameLabel.style.overflow = Overflow.Hidden;
        nameLabel.style.textOverflow = TextOverflow.Ellipsis;
        nameLabel.tooltip = obj.name;
        itemCard.Add(nameLabel);

        Vector2 clickStartPos = Vector2.zero;

        itemCard.RegisterCallback<MouseDownEvent>(evt =>
        {
            if (evt.button == 0)
            {
                clickStartPos = evt.mousePosition;

                if (evt.clickCount == 2)
                {
                    EditorUtility.OpenPropertyEditor(obj);
                    return;
                }

                if (evt.actionKey)
                {
                    if (selectedObjects.Contains(obj))
                        selectedObjects.Remove(obj);
                    else
                        selectedObjects.Add(obj);
                }
                else if (evt.shiftKey && lastSelectedObject != null)
                {
                    SelectRange(lastSelectedObject, obj);
                }
                else
                {
                    selectedObjects.Clear();
                    selectedObjects.Add(obj);
                }

                lastSelectedObject = obj;
                UpdateUnitySelection();
                RefreshItemsGrid();
            }
        });

        itemCard.RegisterCallback<MouseMoveEvent>(evt =>
        {
            if (evt.pressedButtons == 1 && Vector2.Distance(clickStartPos, evt.mousePosition) > 5f)
            {
                if (!selectedObjects.Contains(obj))
                {
                    selectedObjects.Clear();
                    selectedObjects.Add(obj);
                    UpdateUnitySelection();
                    RefreshItemsGrid();
                }

                Object[] dragArray = selectedObjects.ToArray();
                DragAndDrop.PrepareStartDrag();
                DragAndDrop.objectReferences = dragArray;
                DragAndDrop.SetGenericData("SourceCollection", selectedCollection);
                DragAndDrop.StartDrag($"Moving ({dragArray.Length})");
            }
        });

        itemCard.RegisterCallback<ContextClickEvent>(evt =>
        {
            if (!selectedObjects.Contains(obj))
            {
                selectedObjects.Clear();
                selectedObjects.Add(obj);
                UpdateUnitySelection();
                RefreshItemsGrid();
            }

            GenericMenu menu = new GenericMenu();
            menu.AddItem(new GUIContent($"Remove ({selectedObjects.Count})"), false, DeleteSelectedItems);
            menu.ShowAsContext();
            evt.StopPropagation();
        });

        return itemCard;
    }

    private void TogglePreviewPanel(bool enable)
    {
        if (rightSplitView != null)
        {
            if (enable)
            {
                rightSplitView.UnCollapse();
            }
            else
            {
                rightSplitView.CollapseChild(1);
            }
        }

        if (enable)
        {
            UpdatePreviewWindow();
        }
    }

    private void UpdatePreviewWindow()
    {
        if (!showPreview) return;

        previewContentContainer.Clear();
        CleanUpEditor();

        if (selectedObjects.Count == 0)
        {
            Label emptyLabel = new Label("Nothing selected");
            emptyLabel.style.color = new Color(0.6f, 0.6f, 0.6f);
            previewContentContainer.Add(emptyLabel);
        }
        else if (selectedObjects.Count == 1)
        {
            Object targetObj = selectedObjects.First();

            Label nameLabel = new Label(targetObj.name);
            nameLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            nameLabel.style.marginBottom = 2;
            previewContentContainer.Add(nameLabel);

            Label typeLabel = new Label($"Type: {targetObj.GetType().Name}");
            typeLabel.style.fontSize = 11;
            typeLabel.style.color = new Color(0.6f, 0.6f, 0.6f);
            typeLabel.style.marginBottom = 8;
            previewContentContainer.Add(typeLabel);

            cachedEditor = Editor.CreateEditor(targetObj);

            if (cachedEditor != null && cachedEditor.HasPreviewGUI())
            {
                IMGUIContainer imguiPreview = new IMGUIContainer(() =>
                {
                    if (cachedEditor != null)
                    {
                        Rect rect = GUILayoutUtility.GetRect(100, 100, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
                        GUIStyle bgStyle = new GUIStyle();
                        bgStyle.normal.background = Texture2D.blackTexture;

                        cachedEditor.OnInteractivePreviewGUI(rect, bgStyle);
                    }
                });

                imguiPreview.style.flexGrow = 1;
                previewContentContainer.Add(imguiPreview);
            }
            else
            {
                Image previewImage = new Image();
                previewImage.style.flexGrow = 1;
                Texture2D previewTexture = AssetPreview.GetAssetPreview(targetObj) ?? AssetPreview.GetMiniThumbnail(targetObj);
                previewImage.image = previewTexture;
                previewContentContainer.Add(previewImage);
            }
        }
        else
        {
            Label countLabel = new Label($"Selected objects: {selectedObjects.Count}");
            countLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            countLabel.style.marginBottom = 10;
            previewContentContainer.Add(countLabel);

            ScrollView list = new ScrollView();
            foreach (var obj in selectedObjects)
            {
                if (obj == null) continue;
                Label item = new Label($"• {obj.name}");
                item.style.fontSize = 11;
                list.Add(item);
            }
            previewContentContainer.Add(list);
        }
    }

    private void CreateNewCollection()
    {
        var newCol = new SimpleAssetCollectionData
        {
            name = $"Collection {libraryData.collections.Count + 1}",
            color = Random.ColorHSV(0f, 1f, 0.7f, 1f, 0.7f, 1f)
        };
        libraryData.collections.Add(newCol);
        selectedCollection = newCol;
        SaveData();
        RefreshCollectionsList();
        RefreshItemsGrid();
    }

    private void StartInlineRename(Label label, SimpleAssetCollectionData collection)
    {
        VisualElement parent = label.parent;
        if (parent == null) return;

        label.style.display = DisplayStyle.None;

        TextField textField = new TextField();
        textField.value = collection.name;
        textField.style.flexGrow = 1;
        parent.Add(textField);

        textField.Focus();
        textField.SelectAll();

        void FinishRename()
        {
            if (!string.IsNullOrEmpty(textField.value))
            {
                collection.name = textField.value;
                int count = collection.items != null ? collection.items.Count : 0;
                label.text = $"{collection.name}  ({count})";
                SaveData();
            }

            textField.RemoveFromHierarchy();
            label.style.display = DisplayStyle.Flex;
        }

        textField.RegisterCallback<FocusOutEvent>(evt => FinishRename());
        textField.RegisterCallback<KeyDownEvent>(evt =>
        {
            if (evt.keyCode == KeyCode.Return || evt.keyCode == KeyCode.KeypadEnter)
            {
                FinishRename();
                evt.StopPropagation();
            }
            else if (evt.keyCode == KeyCode.Escape)
            {
                textField.RemoveFromHierarchy();
                label.style.display = DisplayStyle.Flex;
                evt.StopPropagation();
            }
        });
    }

    private void DeleteCollection(SimpleAssetCollectionData collection)
    {
        if (EditorUtility.DisplayDialog("Delete Collection", $"Are you sure you want to delete collection \"{collection.name}\"?", "Yes", "No"))
        {
            libraryData.collections.Remove(collection);
            selectedCollection = libraryData.collections.Count > 0 ? libraryData.collections[0] : null;
            selectedObjects.Clear();
            SaveData();
            RefreshCollectionsList();
            RefreshItemsGrid();
        }
    }

    private void RefreshItemsGrid()
    {
        if (itemsScrollView == null) return;
        itemsScrollView.Clear();

        if (selectedCollection == null || selectedCollection.items.Count == 0) return;

        int totalItems = selectedCollection.items.Count;
        int rowCount = Mathf.CeilToInt((float)totalItems / itemsPerRow);

        for (int r = 0; r < rowCount; r++)
        {
            VisualElement row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.height = iconSize + 30;

            for (int i = 0; i < itemsPerRow; i++)
            {
                int index = r * itemsPerRow + i;
                if (index >= totalItems) break;

                Object obj = selectedCollection.items[index];
                if (obj == null) continue;

                VisualElement card = MakeItemCard(obj);
                row.Add(card);
            }

            itemsScrollView.Add(row);
        }
    }

    private void UpdateUnitySelection()
    {
        Selection.objects = selectedObjects.ToArray();
        Focus();
        UpdatePreviewWindow();
    }

    private void SelectRange(Object startObj, Object endObj)
    {
        if (selectedCollection == null) return;

        int startIndex = selectedCollection.items.IndexOf(startObj);
        int endIndex = selectedCollection.items.IndexOf(endObj);

        if (startIndex == -1 || endIndex == -1) return;

        int min = Mathf.Min(startIndex, endIndex);
        int max = Mathf.Max(startIndex, endIndex);

        for (int i = min; i <= max; i++)
        {
            if (selectedCollection.items[i] != null)
                selectedObjects.Add(selectedCollection.items[i]);
        }
    }

    private void DeleteSelectedItems()
    {
        if (selectedObjects.Count == 0 || selectedCollection == null) return;

        foreach (var item in selectedObjects)
        {
            selectedCollection.items.Remove(item);
        }

        selectedObjects.Clear();
        UpdateUnitySelection();
        SaveData();
        RefreshItemsGrid();
        RefreshCollectionsList();
    }

    private void SetPadding(VisualElement element, float value)
    {
        element.style.paddingTop = value;
        element.style.paddingBottom = value;
        element.style.paddingLeft = value;
        element.style.paddingRight = value;
    }

    private void SetMargin(VisualElement element, float value)
    {
        element.style.marginTop = value;
        element.style.marginBottom = value;
        element.style.marginLeft = value;
        element.style.marginRight = value;
    }
}
