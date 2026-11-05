<div align="center">

# 🗂️ Simple Asset Collections

**A lightweight, lightning-fast asset organization tool for Unity.**  
Stop getting lost in deep project folders. Keep your working assets right at your fingertips.

[![Unity](https://img.shields.io/badge/Unity-6.0%2B-black?style=for-the-badge&logo=unity&logoColor=white)](https://unity.com/)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg?style=for-the-badge)](https://opensource.org/licenses/MIT)
[![PRs Welcome](https://img.shields.io/badge/PRs-welcome-brightgreen.svg?style=for-the-badge)](https://github.com/saltario/simple-asset-collections/issues)

</div>

## ✨ Why Simple Asset Collections?

Unity projects grow fast, and finding that *one* material, prefab, or texture you're currently working on can slow you down. **Simple Asset Collections** acts as your personal project workspace:
- 🎨 **Color-Coded Groups:** Categorize assets visually with custom collection colors.
- ⚡ **Seamless Drag & Drop:** Drop assets or folders from your Project window, or move assets between collections.
- 🔄 **Folder Sync:** Link a collection to a folder and sync newly added or removed assets without duplicates.
- 🔍 **Built-in Inspector Preview:** Inspect selected assets or view interactive 3D previews directly inside the tool window.
- 📐 **Adaptive Grid:** Scale icon sizes smoothly with a responsive layout built on **Unity UI Toolkit**.
- ⌨️ **Workflow Friendly:** Shift/Ctrl/Cmd multi-selection, batch removal, and context menus.
- 🧹 **Reference Cleanup:** Missing asset references are visible and can be removed in one click.
- 🧩 **Prefab Editing:** Double-click a prefab to open it in Prefab Mode.

## 📦 Installation

You can install this package using the **Unity Package Manager** directly from GitHub.

1. Open your Unity project (compatible with **Unity 6+** and newer).
2. Navigate to **Window > Package Manager**.
3. Click the **`+`** icon in the top-left corner and select **"Add package from git URL..."**.
4. Paste the repository link `https://github.com/saltario/simple-asset-collections.git` and click **Add**.

## 🚀 Quick Start

1. Open the tool via the top menu: **Window > Asset Collections**.
2. Click the **`+ Collection`** button on the left panel to create your first group.
3. **Drag and Drop** any assets (Prefabs, Materials, Scripts, Textures, etc.) from your Project window into the items grid.
4. Click an item to preview it. Use **Shift-click** for a range or **Ctrl/Cmd-click** to toggle individual items.
5. Press **`Delete`** or **`Backspace`**, or right-click and choose **Remove from collection**, to remove the selection.
6. Double-click a prefab to open the whole prefab in Prefab Mode. Use **Clear missing** to remove broken asset references.
7. Double-click a collection's color strip to change its color.
8. Drop a folder onto a collection to link and import it, or onto **+ Collection** or an empty area of the collection list to create a folder-backed collection. Press **Sync** on a linked collection after changing its folder contents.

## 🛠️ Built With

- **Unity UI Toolkit** — Modern, high-performance, and responsive editor UI framework.
- **C# / ScriptableObjects** — Clean architecture ensuring zero runtime overhead.

## 🖼️ Preview

Here is what the plugin window looks like in the Unity Editor:

<p align="center">
  <img width="1092" height="457" alt="Simple Asset Collections Window" src="https://github.com/user-attachments/assets/57d76f58-1115-42b5-bffd-560549d83d99" />
</p>

## 🤝 Contributing

Contributions, issues, and feature requests are warmly welcomed!  
Feel free to check the [Issues page](https://github.com/saltario/simple-asset-collections/issues) if you want to contribute.

## 📝 License

Distributed under the **MIT License**. See `LICENSE` for more information.
