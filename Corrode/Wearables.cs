/*
 * Copyright(C) 2013-2015 Wizardry and Steamworks
 * Copyright(C) 2019-2024 Sjofn LLC
 * All rights reserved.
 * 
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU General Public License as published by
 * the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 * 
 * This program is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.See the
 * GNU General Public License for more details.
 * 
 * You should have received a copy of the GNU General Public License
 * along with this program.If not, see<https://www.gnu.org/licenses/>.
 */

using System.Collections.Generic;
using System.Linq;
using OpenMetaverse;

namespace Corrode
{
    public partial class Corrode
    {
                /// <summary>
        ///     Can an inventory item be worn?
        /// </summary>
        /// <param name="item">item to check</param>
        /// <returns>true if the inventory item can be worn</returns>
        private static bool CanBeWorn(InventoryBase item)
        {
            return item is InventoryWearable || item is InventoryAttachment || item is InventoryObject;
        }

        /// <summary>
        ///     Resolves inventory links and returns a real inventory item that
        ///     the link is pointing to
        /// </summary>
        /// <param name="item">a link or inventory item</param>
        /// <returns>the real inventory item</returns>
        private static InventoryItem ResolveItemLink(InventoryItem item)
        {
            return item.IsLink() && Client.Inventory.Store.Contains(item.AssetUUID) &&
                   Client.Inventory.Store[item.AssetUUID] is InventoryItem
                ? Client.Inventory.Store[item.AssetUUID] as InventoryItem
                : item;
        }

        /// <summary>
        ///     Get current outfit folder links.
        /// </summary>
        /// <returns>a list of inventory items that can be part of appearance (attachments, wearables)</returns>
        private static List<InventoryItem> GetCurrentOutfitFolderLinks(InventoryFolder outfitFolder)
        {
            List<InventoryItem> ret = new List<InventoryItem>();
            if (outfitFolder == null) return ret;

            Client.Inventory.Store.GetContents(outfitFolder)
                .FindAll(b => CanBeWorn(b) && ((InventoryItem) b).AssetType.Equals(AssetType.Link))
                .ForEach(item => ret.Add((InventoryItem) item));

            return ret;
        }

        private static void Attach(InventoryItem item, AttachmentPoint point, bool replace)
        {
            lock (ClientInstanceInventoryLock)
            {
                InventoryItem realItem = ResolveItemLink(item);
                if (realItem == null) return;
                Client.Appearance.Attach(realItem, point, replace);
                AddLink(realItem,
                    Client.Inventory.Store[Client.Inventory.FindFolderForType(FolderType.CurrentOutfit)] as
                        InventoryFolder);
            }
        }

        private static void Detach(InventoryItem item)
        {
            lock (ClientInstanceInventoryLock)
            {
                InventoryItem realItem = ResolveItemLink(item);
                if (realItem == null) return;
                RemoveLink(realItem,
                    Client.Inventory.Store[Client.Inventory.FindFolderForType(FolderType.CurrentOutfit)] as
                        InventoryFolder);
                Client.Appearance.Detach(realItem);
            }
        }

        private static void Wear(InventoryItem item, bool replace)
        {
            lock (ClientInstanceInventoryLock)
            {
                InventoryItem realItem = ResolveItemLink(item);
                if (realItem == null) return;
                Client.Appearance.AddToOutfit(realItem, replace);
                AddLink(realItem,
                    Client.Inventory.Store[Client.Inventory.FindFolderForType(FolderType.CurrentOutfit)] as
                        InventoryFolder);
            }
        }

        private static void UnWear(InventoryItem item)
        {
            lock (ClientInstanceInventoryLock)
            {
                InventoryItem realItem = ResolveItemLink(item);
                if (realItem == null) return;
                Client.Appearance.RemoveFromOutfit(realItem);
                InventoryItem link = GetCurrentOutfitFolderLinks(
                    Client.Inventory.Store[Client.Inventory.FindFolderForType(FolderType.CurrentOutfit)] as
                        InventoryFolder)
                    .AsParallel()
                    .FirstOrDefault(o => o.AssetType.Equals(AssetType.Link) && o.Name.Equals(item.Name));
                if (link == null) return;
                RemoveLink(link,
                    Client.Inventory.Store[Client.Inventory.FindFolderForType(FolderType.CurrentOutfit)] as
                        InventoryFolder);
            }
        }

        /// <summary>
        ///     Is the item a body part?
        /// </summary>
        /// <param name="item">the item to check</param>
        /// <returns>true if the item is a body part</returns>
        private static bool IsBodyPart(InventoryItem item)
        {
            InventoryItem realItem = ResolveItemLink(item);
            if (!(realItem is InventoryWearable)) return false;
            WearableType t = ((InventoryWearable) realItem).WearableType;
            return t.Equals(WearableType.Shape) ||
                   t.Equals(WearableType.Skin) ||
                   t.Equals(WearableType.Eyes) ||
                   t.Equals(WearableType.Hair);
        }

        /// <summary>
        ///     Creates a new current outfit folder link.
        /// </summary>
        /// <param name="item">item to be linked</param>
        /// <param name="outfitFolder">the outfit folder</param>
        private static void AddLink(InventoryItem item, InventoryFolder outfitFolder)
        {
            if (outfitFolder == null) return;

            bool linkExists = null !=
                              GetCurrentOutfitFolderLinks(outfitFolder)
                                  .Find(itemLink => itemLink.AssetUUID.Equals(item.UUID));

            if (linkExists) return;

            string description = (item.InventoryType.Equals(InventoryType.Wearable) && !IsBodyPart(item))
                ? string.Format("@{0}{1:00}", (int) ((InventoryWearable) item).WearableType, 0)
                : string.Empty;
            Client.Inventory.CreateLink(Client.Inventory.FindFolderForType(FolderType.CurrentOutfit), item.UUID,
                item.Name, description, AssetType.Link,
                item.InventoryType, UUID.Random(), (success, newItem) =>
                {
                    if (success)
                    {
                        Client.Inventory.RequestFetchInventory(newItem.UUID, newItem.OwnerID);
                    }
                });
        }

        /// <summary>
        ///     Remove current outfit folder links for multiple specified inventory item.
        /// </summary>
        /// <param name="item">the item whose link should be removed</param>
        /// <param name="outfitFolder">the outfit folder</param>
        private static void RemoveLink(InventoryItem item, InventoryFolder outfitFolder)
        {
            if (outfitFolder == null) return;

            HashSet<UUID> removeItems = new HashSet<UUID>();
            GetCurrentOutfitFolderLinks(outfitFolder)
                .FindAll(
                    itemLink =>
                        itemLink.AssetUUID.Equals(item is InventoryWearable ? item.AssetUUID : item.UUID))
                .ForEach(link => removeItems.Add(link.UUID));

            foreach (var i in removeItems)
            {
                Client.Inventory.RemoveItem(i);
            }
        }
    }
}

