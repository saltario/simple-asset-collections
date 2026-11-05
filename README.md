<div align="center">

# Simple Asset Collections

Organize project assets into custom, color-coded collections and keep frequently used assets close at hand.

[![Unity 6+](https://img.shields.io/badge/Unity-6%2B-black?style=for-the-badge&logo=unity&logoColor=white)](https://unity.com/)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue?style=for-the-badge)](./LICENSE)

</div>

## Features

- Organize assets into named collections with custom colors.
- Drag assets and folders from the Project window into collections.
- Link multiple folders to a collection and sync their contents without duplicates.
- Select and remove multiple items at once.
- Preview assets in the collection window.
- See broken asset references and clear them in one click.
- Open prefabs directly in Prefab Mode.

## Requirements

- Unity 6.0 or newer.

## Installation

1. Open **Window > Package Manager**.
2. Click **+** and choose **Add package from git URL...**.
3. Enter `https://github.com/saltario/simple-asset-collections.git`.
4. Open the tool from **Window > Asset Collections**.

## Using collections

### Create a collection

Click **+ Collection** to create an empty collection. Drag assets from the Project window into the collection's item grid, or drag assets onto a collection in the list.

Double-click the color strip beside a collection to change its color. Double-click the collection name to rename it.

### Link folders and sync

A collection can have multiple source folders:

- Drag one or more folders onto an existing collection to add them as sources.
- Drag one or more folders onto **+ Collection** or an empty area of the collection list to create one folder-backed collection.
- Click **Sync** on a linked collection after changing its folders.

Sync scans all linked folders, adds new assets, removes assets no longer found in those folders, and avoids duplicates. Assets added manually remain in the collection. If folders overlap, a shared asset appears only once.

Right-click a linked collection and open **Folder sources** to remove individual folders or stop syncing all folders.

### Select and manage items

- Click an item to select it.
- **Shift-click** to select a range.
- **Ctrl-click** (Windows/Linux) or **Cmd-click** (macOS) to toggle individual items.
- Press **Delete** or **Backspace**, or right-click the selection and choose **Remove from collection**.
- Click an item to view its preview. Double-click a prefab to open the full prefab in Prefab Mode.

Removing an item that still exists in a linked folder does not remove it from that folder; a later sync will add it back.

### Clear broken references

Missing assets appear as marked placeholders instead of empty spaces. Click **Clear missing** to remove all broken references from the selected collection.

## Preview

<p align="center">
  <img width="1092" height="457" alt="Simple Asset Collections window" src="https://github.com/user-attachments/assets/57d76f58-1115-42b5-bffd-560549d83d99" />
</p>

## Contributing

Bug reports and feature requests are welcome on the [GitHub Issues page](https://github.com/saltario/simple-asset-collections/issues).

## License

Distributed under the MIT License. See [LICENSE](./LICENSE).
