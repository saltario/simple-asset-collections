using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using Toggle = UnityEngine.UIElements.Toggle;

public class SimpleAssetCollectionWindow : EditorWindow
{
    private const string FOLDER_PATH = "Assets/SimpleAssetCollections/Collections";

    private List<SimpleAssetCollectionData> collections = new List<SimpleAssetCollectionData>();
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
        LoadCollections();
        AssetPreview.SetPreviewTextureCacheSize(2000);
        EditorApplication.update += OnEditorUpdate;
    }

    private void OnDisable()
    {
        EditorApplication.update -= OnEditorUpdate;
        CleanUpEditor();
    }

    private void OnEditorUpdate()
    {
        if (pendingPreviewInstanceIDs.Count > 0)
        {
            List<int> completed = new List<int>();
            foreach (var id in pendingPreviewInstanceIDs)
            {
                if (!AssetPreview.IsLoadingAssetPreview(id)) completed.Add(id);
            }
            if (completed.Count > 0)
            {
                foreach (var id in completed) pendingPreviewInstanceIDs.Remove(id);
                RefreshItemsGrid();
            }
        }
    }

    private void CleanUpEditor()
    {
        if (cachedEditor != null)
        {
            Editor ed = cachedEditor;
            cachedEditor = null;
            EditorApplication.delayCall += () => { if (ed != null) DestroyImmediate(ed); };
        }
    }

    private void LoadCollections()
    {
        if (!Directory.Exists(FOLDER_PATH))
        {
            Directory.CreateDirectory(FOLDER_PATH);
            AssetDatabase.Refresh();
        }

        collections.Clear();
        string[] guids = AssetDatabase.FindAssets("t:SimpleAssetCollectionData", new[] { FOLDER_PATH });
        foreach (var guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            var col = AssetDatabase.LoadAssetAtPath<SimpleAssetCollectionData>(path);
            if (col != null) collections.Add(col);
        }

        if (collections.Count > 0)
        {
            if (selectedCollection == null || !collections.Contains(selectedCollection))
            {
                selectedCollection = collections[0];
            }
        }
        else
        {
            selectedCollection = null;
        }
    }

    private void SaveCollection(SimpleAssetCollectionData col)
    {
        if (col != null)
        {
            EditorUtility.SetDirty(col);
            AssetDatabase.SaveAssets();
        }
    }

    public void CreateGUI()
    {
        VisualElement root = rootVisualElement;

        mainSplitView = new TwoPaneSplitView(0, 200, TwoPaneSplitViewOrientation.Horizontal);
        mainSplitView.style.flexGrow = 1;
        root.Add(mainSplitView);

        // Левая панель (Коллекции)
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

        rightSplitView = new TwoPaneSplitView(1, 260, TwoPaneSplitViewOrientation.Horizontal);
        rightSplitView.style.flexGrow = 1;
        mainSplitView.Add(rightSplitView);

        // Центральная панель (Сетка предметов)
        VisualElement centerPanel = new VisualElement();
        centerPanel.style.flexGrow = 1;
        SetPadding(centerPanel, 6);

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

        VisualElement controlsGroup = new VisualElement();
        controlsGroup.style.flexDirection = FlexDirection.Row;
        controlsGroup.style.alignItems = Align.Center;

        iconSizeSlider = new Slider("Icon Size", 50, 150) { value = iconSize };
        iconSizeSlider.style.width = 170;
        iconSizeSlider.RegisterValueChangedCallback(evt => { iconSize = evt.newValue; CalculateItemsPerRow(); RefreshItemsGrid(); });
        controlsGroup.Add(iconSizeSlider);

        showPreviewToggle = new Toggle("Preview") { value = showPreview, style = { marginLeft = 15 } };
        showPreviewToggle.RegisterValueChangedCallback(evt => { showPreview = evt.newValue; TogglePreviewPanel(showPreview); });
        controlsGroup.Add(showPreviewToggle);

        topBar.Add(controlsGroup);
        centerPanel.Add(topBar);

        itemsScrollView = new ScrollView { style = { flexGrow = 1 } };
        itemsScrollView.RegisterCallback<GeometryChangedEvent>(evt => { if (CalculateItemsPerRow()) RefreshItemsGrid(); });

        // Прием внешних ассетов и перетаскивание между коллекциями
        itemsScrollView.RegisterCallback<DragUpdatedEvent>(evt => DragAndDrop.visualMode = DragAndDropVisualMode.Copy);
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
                    SaveCollection(selectedCollection);
                    RefreshItemsGrid();
                    RefreshCollectionsList();
                }
            }
        });

        centerPanel.Add(itemsScrollView);
        rightSplitView.Add(centerPanel);

        // Правая панель (Превью)
        previewPanel = new VisualElement { style = { flexGrow = 1 } };
        SetPadding(previewPanel, 8);
        previewPanel.style.backgroundColor = new Color(0.2f, 0.2f, 0.2f, 0.3f);

        Label previewTitle = new Label("Inspector Preview") { style = { unityFontStyleAndWeight = FontStyle.Bold, marginBottom = 8 } };
        previewPanel.Add(previewTitle);

        previewContentContainer = new VisualElement { style = { flexGrow = 1 } };
        previewPanel.Add(previewContentContainer);

        rightSplitView.Add(previewPanel);

        root.schedule.Execute(() => TogglePreviewPanel(showPreview));
        RefreshItemsGrid();
    }

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

        foreach (var col in collections)
        {
            if (col == null) continue;

            VisualElement container = new VisualElement();
            container.style.flexDirection = FlexDirection.Row;
            container.style.alignItems = Align.Center;
            container.style.height = 26;
            container.style.paddingLeft = 6;
            container.style.paddingRight = 8;
            container.style.marginBottom = 2;
            SetBorderRadius(container, 4);

            container.style.backgroundColor = selectedCollection == col ? new Color(0.25f, 0.45f, 0.7f, 0.8f) : Color.clear;

            VisualElement colorBar = new VisualElement();
            colorBar.style.width = 4;
            colorBar.style.height = 16;
            colorBar.style.marginRight = 6;
            colorBar.style.borderTopRightRadius = 2;
            colorBar.style.borderBottomRightRadius = 2;
            colorBar.style.backgroundColor = new StyleColor(col.color);
            container.Add(colorBar);

            Label label = new Label($"{col.name}  ({col.items.Count})") { style = { flexGrow = 1, unityTextAlign = TextAnchor.MiddleLeft } };
            container.Add(label);

            // Драг-н-дроп предметов на другую коллекцию в левом списке
            container.RegisterCallback<DragUpdatedEvent>(evt =>
            {
                if (DragAndDrop.objectReferences.Length > 0)
                    DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
            });
            container.RegisterCallback<DragPerformEvent>(evt =>
            {
                DragAndDrop.AcceptDrag();
                bool modified = false;
                foreach (var obj in DragAndDrop.objectReferences)
                {
                    if (obj != null && !col.items.Contains(obj))
                    {
                        col.items.Add(obj);
                        modified = true;
                    }
                }
                if (modified)
                {
                    SaveCollection(col);
                    RefreshCollectionsList();
                    if (selectedCollection == col) RefreshItemsGrid();
                }
            });

            // Клик выбор / двойной клик переименование
            container.RegisterCallback<MouseDownEvent>(evt =>
            {
                if (evt.button == 0)
                {
                    if (evt.clickCount == 2)
                    {
                        // Переименование коллекции при двойном клике
                        container.Clear();
                        TextField renameField = new TextField { value = col.name, style = { flexGrow = 1, height = 20 } };
                        renameField.RegisterCallback<KeyDownEvent>(ke =>
                        {
                            if (ke.keyCode == KeyCode.Return || ke.keyCode == KeyCode.KeypadEnter)
                            {
                                col.name = string.IsNullOrEmpty(renameField.value) ? "Unnamed" : renameField.value;
                                SaveCollection(col);
                                RefreshCollectionsList();
                            }
                        });
                        renameField.RegisterCallback<FocusOutEvent>(fe =>
                        {
                            col.name = string.IsNullOrEmpty(renameField.value) ? "Unnamed" : renameField.value;
                            SaveCollection(col);
                            RefreshCollectionsList();
                        });
                        container.Add(renameField);
                        renameField.Focus();
                        renameSelectAll(renameField);
                    }
                    else
                    {
                        selectedCollection = col;
                        selectedObjects.Clear();
                        UpdateUnitySelection();
                        RefreshItemsGrid();
                        RefreshCollectionsList();
                    }
                    evt.StopPropagation();
                }
            });

            container.RegisterCallback<ContextClickEvent>(evt =>
            {
                GenericMenu menu = new GenericMenu();
                menu.AddItem(new GUIContent("Rename"), false, () => {
                    // Переименование через контекстное меню
                    RefreshCollectionsList();
                });
                menu.AddItem(new GUIContent("Delete"), false, () => DeleteCollection(col));
                menu.ShowAsContext();
                evt.StopPropagation();
            });

            collectionScrollView.Add(container);
        }
    }

    private void renameSelectAll(TextField tf)
    {
        tf.schedule.Execute(() => tf.SelectAll());
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
            SetBorderRadius(itemCard, 4);
        }

        Image previewImage = new Image { style = { width = iconSize, height = iconSize } };
        Texture2D texture = AssetPreview.GetAssetPreview(obj) ?? AssetPreview.GetMiniThumbnail(obj);
        if (AssetPreview.IsLoadingAssetPreview(obj.GetInstanceID()))
        {
            pendingPreviewInstanceIDs.Add(obj.GetInstanceID());
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

        // Поддержка перетаскивания предмета из окна в сцену или другие окна Unity
        itemCard.RegisterCallback<MouseDownEvent>(evt =>
        {
            if (evt.button == 0)
            {
                if (evt.clickCount == 2) { EditorUtility.OpenPropertyEditor(obj); return; }

                if (evt.actionKey)
                {
                    if (selectedObjects.Contains(obj)) selectedObjects.Remove(obj);
                    else selectedObjects.Add(obj);
                }
                else
                {
                    if (!selectedObjects.Contains(obj))
                    {
                        selectedObjects.Clear();
                        selectedObjects.Add(obj);
                    }
                }

                lastSelectedObject = obj;
                UpdateUnitySelection();
                RefreshItemsGrid();
            }
        });

        // Начало DragAndDrop с карточки (чтобы тащить на сцену или в иерархию)
        itemCard.RegisterCallback<MouseMoveEvent>(evt =>
        {
            if ((evt.pressedButtons & 1) != 0 && selectedObjects.Count > 0)
            {
                DragAndDrop.PrepareStartDrag();
                DragAndDrop.objectReferences = selectedObjects.ToArray();
                DragAndDrop.StartDrag("Dragging Collection Items");
                evt.StopPropagation();
            }
        });

        itemCard.RegisterCallback<ContextClickEvent>(evt =>
        {
            if (!selectedObjects.Contains(obj)) { selectedObjects.Clear(); selectedObjects.Add(obj); UpdateUnitySelection(); RefreshItemsGrid(); }
            GenericMenu menu = new GenericMenu();
            menu.AddItem(new GUIContent($"Remove"), false, DeleteSelectedItems);
            menu.ShowAsContext();
            evt.StopPropagation();
        });

        return itemCard;
    }

    private void TogglePreviewPanel(bool enable)
    {
        if (rightSplitView != null)
        {
            if (enable) rightSplitView.UnCollapse();
            else rightSplitView.CollapseChild(1);
        }
        if (enable) UpdatePreviewWindow();
    }

    private void UpdatePreviewWindow()
    {
        if (!showPreview) return;
        previewContentContainer.Clear();
        CleanUpEditor();

        if (selectedObjects.Count == 1)
        {
            Object targetObj = selectedObjects.First();
            previewContentContainer.Add(new Label(targetObj.name) { style = { unityFontStyleAndWeight = FontStyle.Bold, marginBottom = 2 } });
            previewContentContainer.Add(new Label($"Type: {targetObj.GetType().Name}") { style = { fontSize = 11, color = new Color(0.6f, 0.6f, 0.6f), marginBottom = 8 } });

            cachedEditor = Editor.CreateEditor(targetObj);
            if (cachedEditor != null && cachedEditor.HasPreviewGUI())
            {
                IMGUIContainer imguiPreview = new IMGUIContainer(() =>
                {
                    if (cachedEditor != null)
                    {
                        Rect rect = GUILayoutUtility.GetRect(100, 100, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
                        cachedEditor.OnInteractivePreviewGUI(rect, new GUIStyle { normal = { background = Texture2D.blackTexture } });
                    }
                });
                imguiPreview.style.flexGrow = 1;
                previewContentContainer.Add(imguiPreview);
            }
        }
    }

    private void CreateNewCollection()
    {
        var newCol = CreateInstance<SimpleAssetCollectionData>();
        newCol.name = $"Collection {collections.Count + 1}";
        newCol.color = Random.ColorHSV(0f, 1f, 0.7f, 1f, 0.7f, 1f);

        string path = AssetDatabase.GenerateUniqueAssetPath($"{FOLDER_PATH}/NewCol.asset");
        AssetDatabase.CreateAsset(newCol, path);
        AssetDatabase.SaveAssets();

        collections.Add(newCol);
        selectedCollection = newCol;
        RefreshCollectionsList();
        RefreshItemsGrid();
    }

    private void DeleteCollection(SimpleAssetCollectionData col)
    {
        if (EditorUtility.DisplayDialog("Delete Collection", $"Delete \"{col.name}\"?", "Yes", "No"))
        {
            string path = AssetDatabase.GetAssetPath(col);
            AssetDatabase.DeleteAsset(path);
            collections.Remove(col);
            selectedCollection = collections.Count > 0 ? collections[0] : null;
            selectedObjects.Clear();
            RefreshCollectionsList();
            RefreshItemsGrid();
        }
    }

    private void RefreshItemsGrid()
    {
        if (itemsScrollView == null) return;
        itemsScrollView.Clear();
        if (selectedCollection == null || selectedCollection.items == null || selectedCollection.items.Count == 0) return;

        int totalItems = selectedCollection.items.Count;
        int rowCount = Mathf.CeilToInt((float)totalItems / itemsPerRow);

        for (int r = 0; r < rowCount; r++)
        {
            VisualElement row = new VisualElement { style = { flexDirection = FlexDirection.Row, height = iconSize + 30 } };
            for (int i = 0; i < itemsPerRow; i++)
            {
                int index = r * itemsPerRow + i;
                if (index >= totalItems) break;

                Object obj = selectedCollection.items[index];
                if (obj != null) row.Add(MakeItemCard(obj));
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

    private void DeleteSelectedItems()
    {
        if (selectedObjects.Count == 0 || selectedCollection == null) return;
        foreach (var item in selectedObjects) selectedCollection.items.Remove(item);
        selectedObjects.Clear();
        UpdateUnitySelection();
        SaveCollection(selectedCollection);
        RefreshItemsGrid();
        RefreshCollectionsList();
    }

    private void SetBorderRadius(VisualElement el, float value)
    {
        el.style.borderTopLeftRadius = value;
        el.style.borderTopRightRadius = value;
        el.style.borderBottomLeftRadius = value;
        el.style.borderBottomRightRadius = value;
    }

    private void SetPadding(VisualElement el, float v) { el.style.paddingTop = el.style.paddingBottom = el.style.paddingLeft = el.style.paddingRight = v; }
    private void SetMargin(VisualElement el, float v) { el.style.marginTop = el.style.marginBottom = el.style.marginLeft = el.style.marginRight = v; }
}
