using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace InventoryView
{
    [Serializable]
    public class CharacterData
    {
        // Character name.
        public string name;

        // Where the items are found.
        // Example: Inventory, Vault, Deed, Home, ...
        public string source;

        // The items the characer has.
        public List<ItemData> items = new List<ItemData>();

        public ItemData AddItem(ItemData newItem)
        {
            items.Add(newItem);
            return newItem;
        }
    }

    [Serializable]
    public class ItemData
    {
        // The item's tap description.
        public string tap;

        // If the item is inside something then this is the container it's in.
        public ItemData parent;

        // If the item is a container then this is the list of items it's holding.
        public List<ItemData> items = new List<ItemData>();

        public ItemData AddItem(ItemData newItem)
        {
            newItem.parent = this;
            items.Add(newItem);
            return newItem;
        }
    }
}
