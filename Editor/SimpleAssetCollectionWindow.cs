using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
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
    private Button clearMissingButton;

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

    private sealed class CollectionColorPopup : PopupWindowContent
    {
        private readonly SimpleAssetCollectionData collection;
        private readonly VisualElement colorBar;

        public CollectionColorPopup(SimpleAssetCollectionData collection, VisualElement colorBar)
        {
            this.collection = collection;
            this.colorBar = colorBar;
        }

        public override Vector2 GetWindowSize() => new Vector2(230, 40);

        public override void OnGUI(Rect rect)
        {
            EditorGUI.BeginChangeCheck();
            Color color = EditorGUI.ColorField(
                new Rect(8, 8, rect.width - 16, EditorGUIUtility.singleLineHeight),
                new GUIContent("Color"),
                collection.color,
                false,
                false,
                false);
            if (!EditorGUI.EndChangeCheck()) return;

            collection.color = color;
            colorBar.style.backgroundColor = new StyleColor(color);
            EditorUtility.SetDirty(collection);
            AssetDatabase.SaveAssets();
        }
    }

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
        root.focusable = true;
        root.RegisterCallback<KeyDownEvent>(evt =>
        {
            if (evt.target is TextField) return;
            if ((evt.keyCode == KeyCode.Delete || evt.keyCode == KeyCode.Backspace) && selectedObjects.Count > 0)
            {
                DeleteSelectedItems();
                evt.StopPropagation();
            }
        });

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
        RegisterFolderDropTarget(addCollectionBtn, folderPaths =>
        {
            foreach (string folderPath in folderPaths)
                CreateNewCollection(folderPath);
        });
        leftPanel.Add(addCollectionBtn);

        collectionScrollView = new ScrollView();
        collectionScrollView.style.flexGrow = 1;
        RegisterFolderDropTarget(collectionScrollView, folderPaths =>
        {
            foreach (string folderPath in folderPaths)
                CreateNewCollection(folderPath);
        });
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

        clearMissingButton = new Button(ClearMissingReferences);
        clearMissingButton.style.marginLeft = 12;
        controlsGroup.Add(clearMissingButton);
        UpdateClearMissingButton();

        topBar.Add(controlsGroup);
        centerPanel.Add(topBar);

        itemsScrollView = new ScrollView { style = { flexGrow = 1 } };
        itemsScrollView.RegisterCallback<GeometryChangedEvent>(evt => { if (CalculateItemsPerRow()) RefreshItemsGrid(); });

        // Прием внешних ассетов и перетаскивание между коллекциями
        itemsScrollView.RegisterCallback<DragUpdatedEvent>(evt =>
        {
            if (selectedCollection != null && DragAndDrop.objectReferences.Length > 0)
                DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
        });
        itemsScrollView.RegisterCallback<DragPerformEvent>(evt =>
        {
            if (selectedCollection == null) return;
            DragAndDrop.AcceptDrag();
            HandleDropIntoCollection(selectedCollection, DragAndDrop.objectReferences);
            evt.StopPropagation();
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
        if (e.type == EventType.KeyDown && !EditorGUIUtility.editingTextField &&
            (e.keyCode == KeyCode.Delete || e.keyCode == KeyCode.Backspace))
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
            colorBar.RegisterCallback<MouseDownEvent>(evt =>
            {
                if (evt.button != 0 || evt.clickCount != 2) return;
                UnityEditor.PopupWindow.Show(colorBar.worldBound, new CollectionColorPopup(col, colorBar));
                evt.StopPropagation();
            });
            container.Add(colorBar);

            Label label = new Label($"{col.name}  ({(col.items == null ? 0 : col.items.Count)})") { style = { flexGrow = 1, unityTextAlign = TextAnchor.MiddleLeft } };
            container.Add(label);

            if (!string.IsNullOrEmpty(col.sourceFolderGuid))
            {
                string sourceFolderPath = AssetDatabase.GUIDToAssetPath(col.sourceFolderGuid);
                Label folderBadge = new Label("DIR");
                folderBadge.tooltip = string.IsNullOrEmpty(sourceFolderPath)
                    ? "The source folder is missing."
                    : sourceFolderPath;
                folderBadge.style.fontSize = 9;
                folderBadge.style.marginLeft = 4;
                folderBadge.style.marginRight = 4;
                folderBadge.style.paddingLeft = 3;
                folderBadge.style.paddingRight = 3;
                folderBadge.style.color = string.IsNullOrEmpty(sourceFolderPath)
                    ? new Color(1f, 0.55f, 0.45f)
                    : new Color(0.55f, 0.9f, 0.65f);
                folderBadge.style.backgroundColor = new Color(0.15f, 0.3f, 0.2f, 0.7f);
                SetBorderRadius(folderBadge, 3);
                container.Add(folderBadge);

                Button refreshFolderButton = new Button(() => RefreshFolderCollection(col)) { text = "Sync" };
                refreshFolderButton.tooltip = $"Refresh contents from {folderBadge.tooltip}";
                refreshFolderButton.style.width = 40;
                refreshFolderButton.style.height = 20;
                refreshFolderButton.style.marginLeft = 2;
                container.Add(refreshFolderButton);
            }

            // Драг-н-дроп предметов на другую коллекцию в левом списке
            container.RegisterCallback<DragUpdatedEvent>(evt =>
            {
                if (DragAndDrop.objectReferences.Length > 0)
                    DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
            });
            container.RegisterCallback<DragPerformEvent>(evt =>
            {
                DragAndDrop.AcceptDrag();
                HandleDropIntoCollection(col, DragAndDrop.objectReferences);
                evt.StopPropagation();
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
                        lastSelectedObject = null;
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
                if (!string.IsNullOrEmpty(col.sourceFolderGuid))
                    menu.AddItem(new GUIContent("Stop folder sync"), false, () => StopFolderSync(col));
                menu.ShowAsContext();
                evt.StopPropagation();
            });

            collectionScrollView.Add(container);
        }

        UpdateClearMissingButton();
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
        itemCard.userData = obj;
        itemCard.style.width = iconSize;
        itemCard.style.height = iconSize + 25;
        SetMargin(itemCard, 3);
        itemCard.style.alignItems = Align.Center;

        bool isSelected = selectedObjects.Contains(obj);
        itemCard.style.backgroundColor = isSelected
            ? new Color(0.25f, 0.5f, 0.85f, 0.7f)
            : Color.clear;
        if (isSelected) SetBorderRadius(itemCard, 4);

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

        Vector2 dragStartPosition = Vector2.zero;
        bool dragArmed = false;

        itemCard.RegisterCallback<MouseDownEvent>(evt =>
        {
            if (evt.button != 0) return;
            if (evt.clickCount == 2)
            {
                dragArmed = false;
                OpenItem(obj);
                evt.StopPropagation();
                return;
            }

            SelectItem(obj, evt.shiftKey, evt.actionKey);
            dragStartPosition = evt.mousePosition;
            dragArmed = true;
            itemCard.CaptureMouse();
            evt.StopPropagation();
        });

        itemCard.RegisterCallback<MouseUpEvent>(evt =>
        {
            dragArmed = false;
            if (itemCard.HasMouseCapture()) itemCard.ReleaseMouse();
        });
        itemCard.RegisterCallback<MouseMoveEvent>(evt =>
        {
            if (!dragArmed || (evt.pressedButtons & 1) == 0 ||
                (evt.mousePosition - dragStartPosition).sqrMagnitude < 16f)
                return;

            Object[] draggedObjects = selectedObjects.Where(selected => selected != null).ToArray();
            if (draggedObjects.Length == 0) return;

            if (itemCard.HasMouseCapture()) itemCard.ReleaseMouse();
            DragAndDrop.PrepareStartDrag();
            DragAndDrop.objectReferences = draggedObjects;
            DragAndDrop.StartDrag("Dragging Collection Items");
            dragArmed = false;
            evt.StopPropagation();
        });

        itemCard.RegisterCallback<ContextClickEvent>(evt =>
        {
            if (!selectedObjects.Contains(obj))
            {
                selectedObjects.Clear();
                selectedObjects.Add(obj);
                lastSelectedObject = obj;
                UpdateItemSelectionVisuals();
                UpdateUnitySelection();
            }
            GenericMenu menu = new GenericMenu();
            menu.AddItem(new GUIContent("Remove from collection"), false, DeleteSelectedItems);
            menu.ShowAsContext();
            evt.StopPropagation();
        });

        return itemCard;
    }

    private VisualElement MakeMissingItemCard(int index)
    {
        VisualElement itemCard = new VisualElement();
        itemCard.style.width = iconSize;
        itemCard.style.height = iconSize + 25;
        SetMargin(itemCard, 3);
        itemCard.style.alignItems = Align.Center;
        itemCard.style.justifyContent = Justify.Center;
        itemCard.style.backgroundColor = new Color(0.35f, 0.18f, 0.18f, 0.8f);
        SetBorderRadius(itemCard, 4);
        itemCard.tooltip = "This asset reference is missing. Use Clear Missing to remove it.";

        Label missingLabel = new Label("Missing\nasset");
        missingLabel.style.width = iconSize;
        missingLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
        missingLabel.style.color = new Color(1f, 0.7f, 0.7f);
        itemCard.Add(missingLabel);
        itemCard.Add(new Label($"#{index + 1}") { style = { fontSize = 10 } });
        return itemCard;
    }

    private void OpenItem(Object obj)
    {
        GameObject gameObject = obj as GameObject;
        if (gameObject == null && obj is Component component)
            gameObject = component.gameObject;

        if (gameObject != null && PrefabUtility.IsPartOfPrefabAsset(gameObject))
        {
            string prefabPath = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(gameObject);
            if (!string.IsNullOrEmpty(prefabPath))
            {
                PrefabStageUtility.OpenPrefab(prefabPath);
                return;
            }
        }

        EditorUtility.OpenPropertyEditor(obj);
    }

    private void SelectItem(Object obj, bool shift, bool actionKey)
    {
        List<Object> validItems = selectedCollection.items.Where(item => item != null).ToList();
        int clickedIndex = validItems.IndexOf(obj);
        int anchorIndex = lastSelectedObject == null ? -1 : validItems.IndexOf(lastSelectedObject);

        if (shift && clickedIndex >= 0 && anchorIndex >= 0)
        {
            if (!actionKey) selectedObjects.Clear();
            int first = Mathf.Min(clickedIndex, anchorIndex);
            int last = Mathf.Max(clickedIndex, anchorIndex);
            for (int i = first; i <= last; i++)
                selectedObjects.Add(validItems[i]);
        }
        else if (actionKey)
        {
            if (!selectedObjects.Add(obj))
                selectedObjects.Remove(obj);
        }
        else if (!selectedObjects.Contains(obj))
        {
            selectedObjects.Clear();
            selectedObjects.Add(obj);
        }

        lastSelectedObject = obj;
        UpdateItemSelectionVisuals();
        UpdateUnitySelection();
    }

    private void UpdateItemSelectionVisuals()
    {
        if (itemsScrollView == null) return;
        foreach (VisualElement row in itemsScrollView.Children())
        {
            foreach (VisualElement card in row.Children())
            {
                if (!(card.userData is Object item)) continue;
                bool isSelected = selectedObjects.Contains(item);
                card.style.backgroundColor = isSelected
                    ? new Color(0.25f, 0.5f, 0.85f, 0.7f)
                    : Color.clear;
                if (isSelected) SetBorderRadius(card, 4);
            }
        }
    }

    private void UpdateClearMissingButton()
    {
        if (clearMissingButton == null) return;
        int missingCount = selectedCollection == null || selectedCollection.items == null
            ? 0
            : selectedCollection.items.Count(item => item == null);
        clearMissingButton.text = $"Clear missing ({missingCount})";
        clearMissingButton.SetEnabled(missingCount > 0);
    }

    private void ClearMissingReferences()
    {
        if (selectedCollection == null || selectedCollection.items == null) return;
        int missingCount = selectedCollection.items.Count(item => item == null);
        if (missingCount == 0) return;

        Undo.RecordObject(selectedCollection, "Clear Missing Collection Items");
        selectedCollection.items.RemoveAll(item => item == null);
        SaveCollection(selectedCollection);
        RefreshItemsGrid();
        RefreshCollectionsList();
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

    private void RegisterFolderDropTarget(VisualElement target, System.Action<string[]> onFoldersDropped)
    {
        target.RegisterCallback<DragUpdatedEvent>(evt =>
        {
            if (GetDroppedFolderPaths(DragAndDrop.objectReferences).Length == 0) return;
            DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
            evt.StopPropagation();
        });
        target.RegisterCallback<DragPerformEvent>(evt =>
        {
            string[] folderPaths = GetDroppedFolderPaths(DragAndDrop.objectReferences);
            if (folderPaths.Length == 0) return;

            DragAndDrop.AcceptDrag();
            evt.StopPropagation();
            onFoldersDropped(folderPaths);
        });
    }

    private string[] GetDroppedFolderPaths(Object[] objects)
    {
        return objects
            .Where(obj => obj != null)
            .Select(obj => AssetDatabase.GetAssetPath(obj))
            .Where(AssetDatabase.IsValidFolder)
            .Distinct()
            .ToArray();
    }

    private void HandleDropIntoCollection(SimpleAssetCollectionData col, Object[] objects)
    {
        string[] folderPaths = GetDroppedFolderPaths(objects);
        if (folderPaths.Length > 1)
        {
            EditorUtility.DisplayDialog(
                "One source folder per collection",
                "Drop one folder onto a collection. To create several folder collections, drop the folders onto + Collection or the empty area in the collection list.",
                "OK");
            return;
        }

        Undo.RecordObject(col, "Update Asset Collection");
        bool modified = false;
        if (folderPaths.Length == 1)
        {
            col.sourceFolderGuid = AssetDatabase.AssetPathToGUID(folderPaths[0]);
            if (!SyncFolderCollectionContents(col)) return;
            modified = true;
        }

        foreach (Object obj in objects)
        {
            if (obj == null || AssetDatabase.IsValidFolder(AssetDatabase.GetAssetPath(obj))) continue;
            if (col.items == null) col.items = new List<Object>();
            if (col.items.Contains(obj)) continue;
            col.items.Add(obj);
            modified = true;
        }

        if (!modified) return;
        SaveCollection(col);
        RefreshCollectionsList();
        if (selectedCollection == col) RefreshItemsGrid();
    }

    private bool SyncFolderCollectionContents(SimpleAssetCollectionData col)
    {
        string folderPath = AssetDatabase.GUIDToAssetPath(col.sourceFolderGuid);
        if (!AssetDatabase.IsValidFolder(folderPath))
        {
            EditorUtility.DisplayDialog(
                "Source folder not found",
                "The linked folder is missing. Relink a folder by dragging it onto this collection.",
                "OK");
            return false;
        }

        List<Object> importedItems = new List<Object>();
        foreach (string guid in AssetDatabase.FindAssets(string.Empty, new[] { folderPath }))
        {
            string assetPath = AssetDatabase.GUIDToAssetPath(guid);
            if (AssetDatabase.IsValidFolder(assetPath)) continue;

            Object asset = AssetDatabase.LoadMainAssetAtPath(assetPath);
            if (asset != null && !(asset is SimpleAssetCollectionData) && !importedItems.Contains(asset))
                importedItems.Add(asset);
        }

        List<Object> previousFolderItems = col.folderItems ?? new List<Object>();
        List<Object> updatedItems = (col.items ?? new List<Object>())
            .Where(item => item == null || !previousFolderItems.Contains(item))
            .ToList();

        col.folderItems = importedItems;
        foreach (Object item in importedItems)
        {
            if (!updatedItems.Contains(item))
                updatedItems.Add(item);
        }
        col.items = updatedItems;
        return true;
    }

    private void RefreshFolderCollection(SimpleAssetCollectionData col)
    {
        if (col == null || string.IsNullOrEmpty(col.sourceFolderGuid)) return;
        Undo.RecordObject(col, "Refresh Collection from Folder");
        if (!SyncFolderCollectionContents(col)) return;

        SaveCollection(col);
        RefreshCollectionsList();
        if (selectedCollection == col) RefreshItemsGrid();
    }

    private void StopFolderSync(SimpleAssetCollectionData col)
    {
        Undo.RecordObject(col, "Stop Collection Folder Sync");
        col.sourceFolderGuid = string.Empty;
        col.folderItems = new List<Object>();
        SaveCollection(col);
        RefreshCollectionsList();
    }

    private void CreateNewCollection()
    {
        CreateNewCollection(null);
    }

    private void CreateNewCollection(string sourceFolderPath)
    {
        bool hasSourceFolder = !string.IsNullOrEmpty(sourceFolderPath);
        string collectionName = hasSourceFolder ? Path.GetFileName(sourceFolderPath) : $"Collection {collections.Count + 1}";
        var newCol = CreateInstance<SimpleAssetCollectionData>();
        newCol.name = collectionName;
        newCol.color = Random.ColorHSV(0f, 1f, 0.7f, 1f, 0.7f, 1f);
        if (hasSourceFolder)
            newCol.sourceFolderGuid = AssetDatabase.AssetPathToGUID(sourceFolderPath);

        string assetName = hasSourceFolder ? collectionName : "NewCol";
        string path = AssetDatabase.GenerateUniqueAssetPath($"{FOLDER_PATH}/{assetName}.asset");
        AssetDatabase.CreateAsset(newCol, path);
        if (hasSourceFolder)
            SyncFolderCollectionContents(newCol);
        AssetDatabase.SaveAssets();

        collections.Add(newCol);
        selectedCollection = newCol;
        selectedObjects.Clear();
        lastSelectedObject = null;
        UpdateUnitySelection();
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
            lastSelectedObject = null;
            UpdateUnitySelection();
            RefreshCollectionsList();
            RefreshItemsGrid();
        }
    }

    private void RefreshItemsGrid()
    {
        if (itemsScrollView == null) return;
        itemsScrollView.Clear();
        UpdateClearMissingButton();
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
                row.Add(obj != null ? MakeItemCard(obj) : MakeMissingItemCard(index));
            }
            itemsScrollView.Add(row);
        }
    }

    private void UpdateUnitySelection()
    {
        selectedObjects.RemoveWhere(item => item == null);
        Selection.objects = selectedObjects.ToArray();
        Focus();
        UpdatePreviewWindow();
    }

    private void DeleteSelectedItems()
    {
        if (selectedObjects.Count == 0 || selectedCollection == null) return;
        Undo.RecordObject(selectedCollection, "Remove Items from Collection");
        foreach (var item in selectedObjects) selectedCollection.items.Remove(item);
        selectedObjects.Clear();
        lastSelectedObject = null;
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
