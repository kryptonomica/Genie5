using System.Collections.Generic;
using System.Xml.Serialization;

namespace Genie.Plugins.InventoryView;

// ── Ported verbatim (in shape) from the Genie 4 plugin's iData.cs ───────────────
// The field names and types are unchanged so an existing InventoryView.xml written
// by the Genie 4 plugin deserializes here without conversion.
//
// One deliberate change: the `parent` back-link is marked [XmlIgnore]. The Genie 4
// version instead nulled every parent before each save (RemoveParents) and rebuilt
// them afterwards (AddParents) purely to dodge XmlSerializer's circular-reference
// error. [XmlIgnore] achieves the same byte-for-byte XML (parent was never written)
// without mutating the live tree on every save — so saves are side-effect-free and
// safe to call from the scan thread. Parents are still rebuilt on *load*.

/// <summary>One character's catalog for a single source (Inventory / Vault / Deed /
/// Home / TraderStorage). Mirrors the Genie 4 <c>CharacterData</c>.</summary>
public class CharacterData
{
    public string name = "";
    public string source = "";
    public List<ItemData> items = new();

    public ItemData AddItem(ItemData newItem)
    {
        items.Add(newItem);
        return newItem;
    }

    public ItemData AddItem(string tap, bool storage = false)
    {
        var newItem = new ItemData { tap = tap, storage = storage };
        items.Add(newItem);
        return newItem;
    }
}

/// <summary>A single inventory node (a container or an item). <c>items</c> are its
/// children; <c>tap</c> is the displayed text. Mirrors the Genie 4 <c>ItemData</c>.</summary>
public class ItemData
{
    public bool storage;
    public string tap = "";

    /// <summary>Back-link to the containing node, rebuilt on load. Not serialized
    /// (would be a circular reference; see the file header).</summary>
    [XmlIgnore] public ItemData? parent;

    public List<ItemData> items = new();

    public ItemData AddItem(ItemData newItem)
    {
        newItem.parent = this;
        items.Add(newItem);
        return newItem;
    }

    public ItemData AddItem(string tap, bool storage = false)
    {
        var newItem = new ItemData { tap = tap, storage = storage, parent = this };
        items.Add(newItem);
        return newItem;
    }
}
