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

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using OpenMetaverse;

namespace Corrode
{
    public partial class Corrode
    { 
        /// <summary>
        ///     Serialize an RLV message to a string.
        /// </summary>
        /// <returns>in order: behaviours, options, parameters</returns>
        private static IEnumerable<string> wasRLVToString(string message)
        {
            if (string.IsNullOrEmpty(message)) yield break;

            // Split all commands.
            string[] unpack = message.Split(RLV_CONSTANTS.CSV_DELIMITER[0]);
            // Pop first command to process.
            string first = unpack.First();
            // Remove command.
            unpack = unpack.AsParallel().Where(o => !o.Equals(first)).ToArray();
            // Keep rest of message.
            message = string.Join(RLV_CONSTANTS.CSV_DELIMITER, unpack);

            Match match = RLV_CONSTANTS.RLVRegEx.Match(first);
            if (!match.Success) goto CONTINUE;

            yield return match.Groups["behaviour"].ToString().ToLowerInvariant();
            yield return match.Groups["option"].ToString().ToLowerInvariant();
            yield return match.Groups["param"].ToString().ToLowerInvariant();

            CONTINUE:
            foreach (string slice in wasRLVToString(message))
            {
                yield return slice;
            }
        }
        
        /// <summary>
        ///     Processes a RLV behaviour.
        /// </summary>
        /// <param name="message">the RLV message to process</param>
        /// <param name="senderUUID">the UUID of the sender</param>
        private static void HandleRLVBehaviour(string message, UUID senderUUID)
        {
            if (string.IsNullOrEmpty(message)) return;

            // Split all commands.
            string[] unpack = message.Split(RLV_CONSTANTS.CSV_DELIMITER[0]);
            // Pop first command to process.
            string first = unpack.First();
            // Remove command.
            unpack = unpack.AsParallel().Where(o => !o.Equals(first)).ToArray();
            // Keep rest of message.
            message = string.Join(RLV_CONSTANTS.CSV_DELIMITER, unpack);

            Match match = RLV_CONSTANTS.RLVRegEx.Match(first);
            if (!match.Success) goto CONTINUE;

            RLVRule RLVrule = new RLVRule
            {
                Behaviour = match.Groups["behaviour"].ToString().ToLowerInvariant(),
                Option = match.Groups["option"].ToString().ToLowerInvariant(),
                Param = match.Groups["param"].ToString().ToLowerInvariant(),
                ObjectUUID = senderUUID
            };

            switch (RLVrule.Param)
            {
                case RLV_CONSTANTS.Y:
                case RLV_CONSTANTS.ADD:
                    if (RLVrule.Option.Equals(string.Empty))
                    {
                        lock (RLVRulesLock)
                        {
                            RLVRules.RemoveWhere(
                                o =>
                                    o.Behaviour.Equals(
                                        RLVrule.Behaviour,
                                        StringComparison.InvariantCultureIgnoreCase) &&
                                    o.ObjectUUID.Equals(RLVrule.ObjectUUID));
                        }
                        goto CONTINUE;
                    }
                    lock (RLVRulesLock)
                    {
                        RLVRules.RemoveWhere(
                            o =>
                                o.Behaviour.Equals(
                                    RLVrule.Behaviour,
                                    StringComparison.InvariantCultureIgnoreCase) &&
                                o.ObjectUUID.Equals(RLVrule.ObjectUUID) &&
                                o.Option.Equals(RLVrule.Option, StringComparison.InvariantCultureIgnoreCase));
                    }
                    goto CONTINUE;
                case RLV_CONSTANTS.N:
                case RLV_CONSTANTS.REM:
                    lock (RLVRulesLock)
                    {
                        RLVRules.RemoveWhere(
                            o =>
                                o.Behaviour.Equals(
                                    RLVrule.Behaviour,
                                    StringComparison.InvariantCultureIgnoreCase) &&
                                o.Option.Equals(RLVrule.Option, StringComparison.InvariantCultureIgnoreCase) &&
                                o.ObjectUUID.Equals(RLVrule.ObjectUUID));
                        RLVRules.Add(RLVrule);
                    }
                    goto CONTINUE;
            }

            System.Action execute;

            switch (wasGetEnumValueFromDescription<RLVBehaviour>(RLVrule.Behaviour))
            {
                case RLVBehaviour.VERSION:
                case RLVBehaviour.VERSIONNEW:
                    execute = () =>
                    {
                        int channel;
                        if (!int.TryParse(RLVrule.Param, out channel) || channel < 1)
                        {
                            return;
                        }
                        Client.Self.Chat(
                            string.Format("{0} v{1} (Corrode Version: {2} Compiled: {3})", RLV_CONSTANTS.VIEWER,
                                RLV_CONSTANTS.SHORT_VERSION, CORRADE_CONSTANTS.CORRADE_VERSION,
                                CORRADE_CONSTANTS.CORRADE_COMPILE_DATE), channel,
                            ChatType.Normal);
                    };
                    break;
                case RLVBehaviour.VERSIONNUM:
                    execute = () =>
                    {
                        int channel;
                        if (!int.TryParse(RLVrule.Param, out channel) || channel < 1)
                        {
                            return;
                        }
                        Client.Self.Chat(RLV_CONSTANTS.LONG_VERSION, channel, ChatType.Normal);
                    };
                    break;
                case RLVBehaviour.GETGROUP:
                    execute = () =>
                    {
                        int channel;
                        if (!int.TryParse(RLVrule.Param, out channel) || channel < 1)
                        {
                            return;
                        }
                        UUID groupUUID = Client.Self.ActiveGroup;
                        HashSet<UUID> currentGroups = new HashSet<UUID>();
                        if (
                            !GetCurrentGroups(Configuration.SERVICES_TIMEOUT, Configuration.DATA_TIMEOUT,
                                ref currentGroups))
                            return;
                        if (!currentGroups.AsParallel().Any(o => o.Equals(groupUUID)))
                        {
                            return;
                        }
                        string groupName = string.Empty;
                        if (
                            !GroupUUIDToName(currentGroups.AsParallel().FirstOrDefault(o => o.Equals(groupUUID)),
                                Configuration.SERVICES_TIMEOUT, Configuration.DATA_TIMEOUT, ref groupName))
                        {
                            return;
                        }
                        Client.Self.Chat(
                            groupName,
                            channel,
                            ChatType.Normal);
                    };
                    break;
                case RLVBehaviour.SETGROUP:
                    execute = () =>
                    {
                        if (!RLVrule.Param.Equals(RLV_CONSTANTS.FORCE))
                        {
                            return;
                        }
                        UUID groupUUID;
                        if (!UUID.TryParse(RLVrule.Option, out groupUUID))
                        {
                            return;
                        }
                        HashSet<UUID> currentGroups = new HashSet<UUID>();
                        if (
                            !GetCurrentGroups(Configuration.SERVICES_TIMEOUT, Configuration.DATA_TIMEOUT,
                                ref currentGroups))
                            return;
                        if (!currentGroups.AsParallel().Any(o => o.Equals(groupUUID)))
                        {
                            return;
                        }
                        Client.Groups.ActivateGroup(groupUUID);
                    };
                    break;
                case RLVBehaviour.GETSITID:
                    execute = () =>
                    {
                        int channel;
                        if (!int.TryParse(RLVrule.Param, out channel) || channel < 1)
                        {
                            return;
                        }
                        Avatar me;
                        if (Client.Network.CurrentSim.ObjectsAvatars.TryGetValue(Client.Self.LocalID, out me))
                        {
                            if (me.ParentID != 0)
                            {
                                Primitive sit;
                                if (Client.Network.CurrentSim.ObjectsPrimitives.TryGetValue(me.ParentID, out sit))
                                {
                                    Client.Self.Chat(sit.ID.ToString(), channel, ChatType.Normal);
                                    return;
                                }
                            }
                        }
                        UUID zero = UUID.Zero;
                        Client.Self.Chat(zero.ToString(), channel, ChatType.Normal);
                    };
                    break;
                case RLVBehaviour.SIT:
                    execute = () =>
                    {
                        UUID sitTarget;
                        if (!RLVrule.Param.Equals(RLV_CONSTANTS.FORCE) || !UUID.TryParse(RLVrule.Option, out sitTarget) ||
                            sitTarget.Equals(UUID.Zero))
                        {
                            return;
                        }
                        Primitive primitive = null;
                        if (
                            !FindPrimitive(sitTarget,
                                LINDEN_CONSTANTS.LSL.SENSOR_RANGE,
                                ref primitive, Configuration.SERVICES_TIMEOUT, Configuration.DATA_TIMEOUT))
                        {
                            return;
                        }
                        ManualResetEvent SitEvent = new ManualResetEvent(false);
                        EventHandler<AvatarSitResponseEventArgs> AvatarSitEventHandler =
                            (sender, args) =>
                                SitEvent.Set();
                        EventHandler<AlertMessageEventArgs> AlertMessageEventHandler = (sender, args) => SitEvent.Set();
                        if (Client.Self.Movement.SitOnGround || !Client.Self.SittingOn.Equals(0))
                        {
                            Client.Self.Stand();
                        }
                        // stop all non-built-in animations
                        List<UUID> lindenAnimations = new List<UUID>(typeof (Animations).GetProperties(
                            BindingFlags.Public |
                            BindingFlags.Static).AsParallel().Select(o => (UUID) o.GetValue(null)).ToList());
                        Parallel.ForEach(Client.Self.SignaledAnimations.Copy().Keys, o =>
                        {
                            if (!lindenAnimations.Contains(o))
                                Client.Self.AnimationStop(o, true);
                        });
                        lock (ClientInstanceSelfLock)
                        {
                            Client.Self.AvatarSitResponse += AvatarSitEventHandler;
                            Client.Self.AlertMessage += AlertMessageEventHandler;
                            Client.Self.RequestSit(primitive.ID, Vector3.Zero);
                            SitEvent.WaitOne(Configuration.SERVICES_TIMEOUT, false);
                            Client.Self.AvatarSitResponse -= AvatarSitEventHandler;
                            Client.Self.AlertMessage -= AlertMessageEventHandler;
                        }
                        // Set the camera on the avatar.
                        Client.Self.Movement.Camera.LookAt(
                            Client.Self.SimPosition,
                            Client.Self.SimPosition
                            );
                    };
                    break;
                case RLVBehaviour.UNSIT:
                    execute = () =>
                    {
                        if (!RLVrule.Param.Equals(RLV_CONSTANTS.FORCE))
                        {
                            return;
                        }
                        if (Client.Self.Movement.SitOnGround || !Client.Self.SittingOn.Equals(0))
                        {
                            Client.Self.Stand();
                        }
                        // stop all non-built-in animations
                        List<UUID> lindenAnimations = new List<UUID>(typeof (Animations).GetProperties(
                            BindingFlags.Public |
                            BindingFlags.Static).AsParallel().Select(o => (UUID) o.GetValue(null)).ToList());
                        Parallel.ForEach(Client.Self.SignaledAnimations.Copy().Keys, o =>
                        {
                            if (!lindenAnimations.Contains(o))
                                Client.Self.AnimationStop(o, true);
                        });
                    };
                    break;
                case RLVBehaviour.SETROT:
                    execute = () =>
                    {
                        double rotation;
                        if (!RLVrule.Param.Equals(RLV_CONSTANTS.FORCE) ||
                            !double.TryParse(RLVrule.Option, NumberStyles.Float, CultureInfo.InvariantCulture,
                                out rotation))
                        {
                            return;
                        }
                        Client.Self.Movement.UpdateFromHeading(Math.PI/2d - rotation, true);
                    };
                    break;
                case RLVBehaviour.TPTO:
                    execute = () =>
                    {
                        string[] coordinates = RLVrule.Option.Split('/');
                        if (!coordinates.Length.Equals(3))
                        {
                            return;
                        }
                        float globalX;
                        if (!float.TryParse(coordinates[0], out globalX))
                        {
                            return;
                        }
                        float globalY;
                        if (!float.TryParse(coordinates[1], out globalY))
                        {
                            return;
                        }
                        float altitude;
                        if (!float.TryParse(coordinates[2], out altitude))
                        {
                            return;
                        }
                        float localX, localY;
                        ulong handle = Helpers.GlobalPosToRegionHandle(globalX, globalY, out localX, out localY);
                        Client.Self.RequestTeleport(handle, new Vector3(localX, localY, altitude));
                    };
                    break;
                case RLVBehaviour.GETOUTFIT:
                    execute = () =>
                    {
                        int channel;
                        if (!int.TryParse(RLVrule.Param, out channel) || channel < 1)
                        {
                            return;
                        }
                        HashSet<KeyValuePair<AppearanceManager.WearableData, WearableType>> wearables =
                            new HashSet<KeyValuePair<AppearanceManager.WearableData, WearableType>>(
                                GetWearables(Client.Inventory.Store.RootNode));
                        StringBuilder response = new StringBuilder();
                        switch (!string.IsNullOrEmpty(RLVrule.Option))
                        {
                            case true:
                                RLVWearable RLVwearable = RLVWearables.AsParallel()
                                    .FirstOrDefault(
                                        o => o.Name.Equals(RLVrule.Option, StringComparison.InvariantCultureIgnoreCase));
                                if (RLVwearable.Equals(default(RLVWearable)))
                                {
                                    response.Append(RLV_CONSTANTS.FALSE_MARKER);
                                    break;
                                }
                                if (!wearables.AsParallel().Any(o => o.Value.Equals(RLVwearable.WearableType)))
                                {
                                    response.Append(RLV_CONSTANTS.FALSE_MARKER);
                                    break;
                                }
                                response.Append(RLV_CONSTANTS.TRUE_MARKER);
                                break;
                            default:
                                string[] data = new string[RLVWearables.Count];
                                Parallel.ForEach(Enumerable.Range(0, RLVWearables.Count), o =>
                                {
                                    if (!wearables.AsParallel().Any(p => p.Value.Equals(RLVWearables[o].WearableType)))
                                    {
                                        data[o] = RLV_CONSTANTS.FALSE_MARKER;
                                        return;
                                    }
                                    data[o] = RLV_CONSTANTS.TRUE_MARKER;
                                });
                                response.Append(string.Join("", data.ToArray()));
                                break;
                        }
                        Client.Self.Chat(response.ToString(), channel, ChatType.Normal);
                    };
                    break;
                case RLVBehaviour.GETATTACH:
                    execute = () =>
                    {
                        int channel;
                        if (!int.TryParse(RLVrule.Param, out channel) || channel < 1)
                        {
                            return;
                        }
                        HashSet<Primitive> attachments = new HashSet<Primitive>(
                            GetAttachments(Configuration.SERVICES_TIMEOUT).AsParallel().Select(o => o.Key));
                        StringBuilder response = new StringBuilder();
                        if (attachments.Count.Equals(0))
                        {
                            Client.Self.Chat(response.ToString(), channel, ChatType.Normal);
                        }
                        HashSet<AttachmentPoint> attachmentPoints =
                            new HashSet<AttachmentPoint>(attachments.AsParallel()
                                .Select(o => o.PrimData.AttachmentPoint));
                        switch (!string.IsNullOrEmpty(RLVrule.Option))
                        {
                            case true:
                                RLVAttachment RLVattachment = RLVAttachments.AsParallel().FirstOrDefault(
                                    o => o.Name.Equals(RLVrule.Option, StringComparison.InvariantCultureIgnoreCase));
                                if (RLVattachment.Equals(default(RLVAttachment)))
                                {
                                    response.Append(RLV_CONSTANTS.FALSE_MARKER);
                                    break;
                                }
                                if (!attachmentPoints.Contains(RLVattachment.AttachmentPoint))
                                {
                                    response.Append(RLV_CONSTANTS.FALSE_MARKER);
                                    break;
                                }
                                response.Append(RLV_CONSTANTS.TRUE_MARKER);
                                break;
                            default:
                                string[] data = new string[RLVAttachments.Count];
                                Parallel.ForEach(Enumerable.Range(0, RLVAttachments.Count), o =>
                                {
                                    if (!attachmentPoints.Contains(RLVAttachments[o].AttachmentPoint))
                                    {
                                        data[o] = RLV_CONSTANTS.FALSE_MARKER;
                                        return;
                                    }
                                    data[o] = RLV_CONSTANTS.TRUE_MARKER;
                                });
                                response.Append(string.Join("", data.ToArray()));
                                break;
                        }
                        Client.Self.Chat(response.ToString(), channel, ChatType.Normal);
                    };
                    break;
                case RLVBehaviour.DETACHME:
                    execute = () =>
                    {
                        if (!RLVrule.Param.Equals(RLV_CONSTANTS.FORCE))
                        {
                            return;
                        }
                        KeyValuePair<Primitive, AttachmentPoint> attachment =
                            GetAttachments(Configuration.SERVICES_TIMEOUT)
                                .AsParallel().FirstOrDefault(o => o.Key.ID.Equals(senderUUID));
                        if (attachment.Equals(default(KeyValuePair<Primitive, AttachmentPoint>)))
                        {
                            return;
                        }
                        InventoryBase inventoryBase =
                            FindInventory<InventoryBase>(Client.Inventory.Store.RootNode,
                                attachment.Key.Properties.ItemID
                                )
                                .AsParallel().FirstOrDefault(
                                    p =>
                                        (p is InventoryItem) &&
                                        ((InventoryItem) p).AssetType.Equals(AssetType.Object));
                        if (inventoryBase is InventoryAttachment || inventoryBase is InventoryObject)
                        {
                            Detach(inventoryBase as InventoryItem);
                        }
                        RebakeTimer.Change(Configuration.REBAKE_DELAY, 0);
                    };
                    break;
                case RLVBehaviour.REMATTACH:
                case RLVBehaviour.DETACH:
                    execute = () =>
                    {
                        if (!RLVrule.Param.Equals(RLV_CONSTANTS.FORCE))
                        {
                            return;
                        }
                        InventoryNode RLVFolder =
                            FindInventory<InventoryNode>(Client.Inventory.Store.RootNode,
                                RLV_CONSTANTS.SHARED_FOLDER_NAME)
                                .AsParallel()
                                .FirstOrDefault(o => o.Data is InventoryFolder);
                        if (RLVFolder == null)
                        {
                            return;
                        }
                        InventoryBase inventoryBase;
                        switch (!string.IsNullOrEmpty(RLVrule.Option))
                        {
                            case true:
                                RLVAttachment RLVattachment =
                                    RLVAttachments.AsParallel().FirstOrDefault(
                                        o => o.Name.Equals(RLVrule.Option, StringComparison.InvariantCultureIgnoreCase));
                                switch (!RLVattachment.Equals(default(RLVAttachment)))
                                {
                                    case true: // detach by attachment point
                                        Parallel.ForEach(
                                            GetAttachments(Configuration.SERVICES_TIMEOUT)
                                                .AsParallel().Where(o => o.Value.Equals(RLVattachment.AttachmentPoint)),
                                            o =>
                                            {
                                                inventoryBase =
                                                    FindInventory<InventoryBase>(Client.Inventory.Store.RootNode,
                                                        o.Key.Properties.Name
                                                        )
                                                        .AsParallel().FirstOrDefault(
                                                            p =>
                                                                (p is InventoryItem) &&
                                                                ((InventoryItem) p).AssetType.Equals(
                                                                    AssetType.Object));
                                                if (inventoryBase is InventoryAttachment ||
                                                    inventoryBase is InventoryObject)
                                                {
                                                    Detach(inventoryBase as InventoryItem);
                                                }
                                            });
                                        break;
                                    default: // detach by folder(s) name
                                        Parallel.ForEach(
                                            RLVrule.Option.Split(RLV_CONSTANTS.PATH_SEPARATOR[0])
                                                .AsParallel().Select(
                                                    folder =>
                                                        FindInventory<InventoryBase>(RLVFolder,
                                                            new Regex(Regex.Escape(folder),
                                                                RegexOptions.Compiled | RegexOptions.IgnoreCase)
                                                            ).AsParallel().FirstOrDefault(o => (o is InventoryFolder))),
                                            o =>
                                            {
                                                if (o != null)
                                                {
                                                    Client.Inventory.Store.GetContents(
                                                        o as InventoryFolder).FindAll(CanBeWorn)
                                                        .ForEach(
                                                            p =>
                                                            {
                                                                if (p is InventoryWearable)
                                                                {
                                                                    UnWear(p as InventoryItem);
                                                                    return;
                                                                }
                                                                if (p is InventoryAttachment ||
                                                                    p is InventoryObject)
                                                                {
                                                                    // Multiple attachment points not working in libOpenMetaverse, so just replace.
                                                                    Detach(p as InventoryItem);
                                                                }
                                                            });
                                                }
                                            });
                                        break;
                                }
                                break;
                            default: //detach everything from RLV attachmentpoints
                                Parallel.ForEach(
                                    GetAttachments(Configuration.SERVICES_TIMEOUT)
                                        .AsParallel()
                                        .Where(o => RLVAttachments.Any(p => p.AttachmentPoint.Equals(o.Value))), o =>
                                        {
                                            inventoryBase = FindInventory<InventoryBase>(
                                                Client.Inventory.Store.RootNode, o.Key.Properties.Name
                                                )
                                                .AsParallel().FirstOrDefault(
                                                    p =>
                                                        p is InventoryItem &&
                                                        ((InventoryItem) p).AssetType.Equals(AssetType.Object));
                                            if (inventoryBase is InventoryAttachment || inventoryBase is InventoryObject)
                                            {
                                                Detach(inventoryBase as InventoryItem);
                                            }
                                        });
                                break;
                        }
                        RebakeTimer.Change(Configuration.REBAKE_DELAY, 0);
                    };
                    break;
                case RLVBehaviour.ATTACH:
                case RLVBehaviour.ATTACHOVERORREPLACE:
                case RLVBehaviour.ATTACHOVER:
                    execute = () =>
                    {
                        if (!RLVrule.Param.Equals(RLV_CONSTANTS.FORCE) || string.IsNullOrEmpty(RLVrule.Option))
                        {
                            return;
                        }
                        InventoryNode RLVFolder =
                            FindInventory<InventoryNode>(Client.Inventory.Store.RootNode,
                                RLV_CONSTANTS.SHARED_FOLDER_NAME)
                                .AsParallel()
                                .FirstOrDefault(o => o.Data is InventoryFolder);
                        if (RLVFolder == null)
                        {
                            return;
                        }
                        Parallel.ForEach(
                            RLVrule.Option.Split(RLV_CONSTANTS.PATH_SEPARATOR[0])
                                .AsParallel().Select(
                                    folder =>
                                        FindInventory<InventoryBase>(RLVFolder,
                                            new Regex(Regex.Escape(folder),
                                                RegexOptions.Compiled | RegexOptions.IgnoreCase)
                                            ).AsParallel().FirstOrDefault(o => (o is InventoryFolder))), o =>
                                            {
                                                if (o != null)
                                                {
                                                    Client.Inventory.Store.GetContents(o as InventoryFolder).
                                                        FindAll(CanBeWorn)
                                                        .ForEach(
                                                            p =>
                                                            {
                                                                if (p is InventoryWearable)
                                                                {
                                                                    Wear(p as InventoryItem, true);
                                                                    return;
                                                                }
                                                                if (p is InventoryObject || p is InventoryAttachment)
                                                                {
                                                                    // Multiple attachment points not working in libOpenMetaverse, so just replace.
                                                                    Attach(p as InventoryItem,
                                                                        AttachmentPoint.Default,
                                                                        true);
                                                                }
                                                            });
                                                }
                                            });
                        RebakeTimer.Change(Configuration.REBAKE_DELAY, 0);
                    };
                    break;
                case RLVBehaviour.REMOUTFIT:
                    execute = () =>
                    {
                        if (!RLVrule.Param.Equals(RLV_CONSTANTS.FORCE))
                        {
                            return;
                        }
                        InventoryBase inventoryBase;
                        switch (!string.IsNullOrEmpty(RLVrule.Option))
                        {
                            case true: // A single wearable
                                FieldInfo wearTypeInfo = typeof (WearableType).GetFields(BindingFlags.Public |
                                                                                         BindingFlags.Static)
                                    .AsParallel().FirstOrDefault(
                                        p => p.Name.Equals(RLVrule.Option, StringComparison.InvariantCultureIgnoreCase));
                                if (wearTypeInfo == null)
                                {
                                    break;
                                }
                                KeyValuePair<AppearanceManager.WearableData, WearableType> wearable = GetWearables(
                                    Client.Inventory.Store.RootNode)
                                    .AsParallel().FirstOrDefault(
                                        o => o.Value.Equals((WearableType) wearTypeInfo.GetValue(null)));
                                if (wearable.Equals(default(KeyValuePair<AppearanceManager.WearableData, WearableType>)))
                                {
                                    break;
                                }
                                inventoryBase = FindInventory<InventoryBase>(Client.Inventory.Store.RootNode,
                                    wearable.Value).FirstOrDefault();
                                if (inventoryBase == null)
                                {
                                    break;
                                }
                                UnWear(inventoryBase as InventoryItem);
                                break;
                            default:
                                Parallel.ForEach(GetWearables(Client.Inventory.Store.RootNode)
                                    .AsParallel().Select(o => new[]
                                    {
                                        o.Key
                                    }).SelectMany(o => o), o =>
                                    {
                                        inventoryBase =
                                            FindInventory<InventoryBase>(Client.Inventory.Store.RootNode, o.ItemID
                                                )
                                                .FirstOrDefault(p => (p is InventoryWearable));
                                        if (inventoryBase == null)
                                        {
                                            return;
                                        }
                                        UnWear(inventoryBase as InventoryItem);
                                    });
                                break;
                        }
                        RebakeTimer.Change(Configuration.REBAKE_DELAY, 0);
                    };
                    break;
                case RLVBehaviour.GETPATHNEW:
                case RLVBehaviour.GETPATH:
                    execute = () =>
                    {
                        int channel;
                        if (!int.TryParse(RLVrule.Param, out channel) || channel < 1)
                        {
                            return;
                        }
                        InventoryNode RLVFolder =
                            FindInventory<InventoryNode>(Client.Inventory.Store.RootNode,
                                RLV_CONSTANTS.SHARED_FOLDER_NAME)
                                .AsParallel()
                                .FirstOrDefault(o => o.Data is InventoryFolder);
                        if (RLVFolder == null)
                        {
                            Client.Self.Chat(string.Empty, channel, ChatType.Normal);
                            return;
                        }
                        // General variables
                        InventoryBase inventoryBase = null;
                        KeyValuePair<Primitive, AttachmentPoint> attachment;
                        switch (!string.IsNullOrEmpty(RLVrule.Option))
                        {
                            case true:
                                // Try attachments
                                RLVAttachment RLVattachment =
                                    RLVAttachments.AsParallel().FirstOrDefault(
                                        o => o.Name.Equals(RLVrule.Option, StringComparison.InvariantCultureIgnoreCase));
                                if (!RLVattachment.Equals(default(RLVAttachment)))
                                {
                                    attachment =
                                        GetAttachments(Configuration.SERVICES_TIMEOUT)
                                            .AsParallel()
                                            .FirstOrDefault(o => o.Value.Equals(RLVattachment.AttachmentPoint));
                                    if (attachment.Equals(default(KeyValuePair<Primitive, AttachmentPoint>)))
                                    {
                                        return;
                                    }
                                    inventoryBase = FindInventory<InventoryBase>(
                                        RLVFolder, attachment.Key.Properties.ItemID
                                        )
                                        .AsParallel().FirstOrDefault(
                                            p =>
                                                (p is InventoryItem) &&
                                                ((InventoryItem) p).AssetType.Equals(AssetType.Object));
                                    break;
                                }
                                RLVWearable RLVwearable =
                                    RLVWearables.AsParallel().FirstOrDefault(
                                        o => o.Name.Equals(RLVrule.Option, StringComparison.InvariantCultureIgnoreCase));
                                if (!RLVwearable.Equals(default(RLVWearable)))
                                {
                                    FieldInfo wearTypeInfo = typeof (WearableType).GetFields(BindingFlags.Public |
                                                                                             BindingFlags.Static)
                                        .AsParallel().FirstOrDefault(
                                            p =>
                                                p.Name.Equals(RLVrule.Option,
                                                    StringComparison.InvariantCultureIgnoreCase));
                                    if (wearTypeInfo == null)
                                    {
                                        return;
                                    }
                                    KeyValuePair<AppearanceManager.WearableData, WearableType> wearable = GetWearables(
                                        RLVFolder)
                                        .AsParallel().FirstOrDefault(
                                            o => o.Value.Equals((WearableType) wearTypeInfo.GetValue(null)));
                                    if (
                                        wearable.Equals(
                                            default(KeyValuePair<AppearanceManager.WearableData, WearableType>)))
                                    {
                                        return;
                                    }
                                    inventoryBase =
                                        FindInventory<InventoryBase>(RLVFolder,
                                            wearable
                                                .Key.ItemID)
                                            .AsParallel().FirstOrDefault(o => (o is InventoryWearable));
                                }
                                break;
                            default:
                                attachment =
                                    GetAttachments(Configuration.SERVICES_TIMEOUT)
                                        .AsParallel().FirstOrDefault(o => o.Key.ID.Equals(senderUUID));
                                if (attachment.Equals(default(KeyValuePair<Primitive, AttachmentPoint>)))
                                {
                                    break;
                                }
                                inventoryBase = FindInventory<InventoryBase>(
                                    Client.Inventory.Store.RootNode, attachment.Key.Properties.ItemID
                                    )
                                    .AsParallel().FirstOrDefault(
                                        p =>
                                            (p is InventoryItem) &&
                                            ((InventoryItem) p).AssetType.Equals(AssetType.Object));
                                break;
                        }
                        if (inventoryBase == null)
                        {
                            Client.Self.Chat(string.Empty, channel, ChatType.Normal);
                            return;
                        }
                        KeyValuePair<InventoryBase, LinkedList<string>> path =
                            FindInventoryPath<InventoryBase>(RLVFolder, inventoryBase.Name,
                                new LinkedList<string>()).FirstOrDefault();
                        if (path.Equals(default(KeyValuePair<InventoryBase, LinkedList<string>>)))
                        {
                            Client.Self.Chat(string.Empty, channel, ChatType.Normal);
                            return;
                        }
                        Client.Self.Chat(string.Join(RLV_CONSTANTS.PATH_SEPARATOR, path.Value.ToArray()), channel,
                            ChatType.Normal);
                    };
                    break;
                case RLVBehaviour.FINDFOLDER:
                    execute = () =>
                    {
                        int channel;
                        if (!int.TryParse(RLVrule.Param, out channel) || channel < 1)
                        {
                            return;
                        }
                        if (string.IsNullOrEmpty(RLVrule.Option))
                        {
                            Client.Self.Chat(string.Empty, channel, ChatType.Normal);
                            return;
                        }
                        InventoryNode RLVFolder =
                            FindInventory<InventoryNode>(Client.Inventory.Store.RootNode,
                                RLV_CONSTANTS.SHARED_FOLDER_NAME)
                                .AsParallel()
                                .FirstOrDefault(o => o.Data is InventoryFolder);
                        if (RLVFolder == null)
                        {
                            Client.Self.Chat(string.Empty, channel, ChatType.Normal);
                            return;
                        }
                        List<string> folders = new List<string>();
                        HashSet<string> parts =
                            new HashSet<string>(RLVrule.Option.Split(RLV_CONSTANTS.AND_OPERATOR.ToCharArray()));
                        object LockObject = new object();
                        Parallel.ForEach(FindInventoryPath<InventoryBase>(RLVFolder,
                            CORRADE_CONSTANTS.OneOrMoRegex,
                            new LinkedList<string>())
                            .AsParallel().Where(
                                o =>
                                    o.Key is InventoryFolder &&
                                    !o.Key.Name.Substring(1).Equals(RLV_CONSTANTS.DOT_MARKER) &&
                                    !o.Key.Name.Substring(1).Equals(RLV_CONSTANTS.TILDE_MARKER)), o =>
                                    {
                                        int count = 0;
                                        Parallel.ForEach(parts, p => Parallel.ForEach(o.Value, q =>
                                        {
                                            if (q.Contains(p))
                                            {
                                                Interlocked.Increment(ref count);
                                            }
                                        }));
                                        if (!count.Equals(parts.Count)) return;
                                        lock (LockObject)
                                        {
                                            folders.Add(o.Key.Name);
                                        }
                                    });
                        if (!folders.Count.Equals(0))
                        {
                            Client.Self.Chat(string.Join(RLV_CONSTANTS.PATH_SEPARATOR, folders.ToArray()),
                                channel,
                                ChatType.Normal);
                        }
                    };
                    break;
                case RLVBehaviour.GETINV:
                    execute = () =>
                    {
                        int channel;
                        if (!int.TryParse(RLVrule.Param, out channel) || channel < 1)
                        {
                            return;
                        }
                        if (string.IsNullOrEmpty(RLVrule.Option))
                        {
                            Client.Self.Chat(string.Empty, channel, ChatType.Normal);
                            return;
                        }
                        InventoryNode RLVFolder =
                            FindInventory<InventoryNode>(Client.Inventory.Store.RootNode,
                                RLV_CONSTANTS.SHARED_FOLDER_NAME)
                                .AsParallel()
                                .FirstOrDefault(o => o.Data is InventoryFolder);
                        if (RLVFolder == null)
                        {
                            Client.Self.Chat(string.Empty, channel, ChatType.Normal);
                            return;
                        }
                        InventoryNode optionFolderNode;
                        switch (!string.IsNullOrEmpty(RLVrule.Option))
                        {
                            case true:
                                KeyValuePair<InventoryNode, LinkedList<string>> folderPath = FindInventoryPath
                                    <InventoryNode>(
                                        RLVFolder,
                                        CORRADE_CONSTANTS.OneOrMoRegex,
                                        new LinkedList<string>())
                                    .AsParallel().Where(o => o.Key.Data is InventoryFolder)
                                    .FirstOrDefault(
                                        o =>
                                            string.Join(RLV_CONSTANTS.PATH_SEPARATOR, o.Value.Skip(1).ToArray())
                                                .Equals(RLVrule.Option, StringComparison.InvariantCultureIgnoreCase));
                                if (folderPath.Equals(default(KeyValuePair<InventoryNode, LinkedList<string>>)))
                                {
                                    Client.Self.Chat(string.Empty, channel, ChatType.Normal);
                                    return;
                                }
                                optionFolderNode = folderPath.Key;
                                break;
                            default:
                                optionFolderNode = RLVFolder;
                                break;
                        }
                        HashSet<string> csv = new HashSet<string>();
                        object LockObject = new object();
                        Parallel.ForEach(
                            FindInventory<InventoryBase>(optionFolderNode, CORRADE_CONSTANTS.OneOrMoRegex),
                            o =>
                            {
                                if (o.Name.StartsWith(RLV_CONSTANTS.DOT_MARKER)) return;
                                lock (LockObject)
                                {
                                    csv.Add(o.Name);
                                }
                            });
                        Client.Self.Chat(string.Join(RLV_CONSTANTS.CSV_DELIMITER, csv.ToArray()), channel,
                            ChatType.Normal);
                    };
                    break;
                case RLVBehaviour.GETINVWORN:
                    execute = () =>
                    {
                        int channel;
                        if (!int.TryParse(RLVrule.Param, out channel) || channel < 1)
                        {
                            return;
                        }
                        InventoryNode RLVFolder =
                            FindInventory<InventoryNode>(Client.Inventory.Store.RootNode,
                                RLV_CONSTANTS.SHARED_FOLDER_NAME)
                                .AsParallel()
                                .FirstOrDefault(o => o.Data is InventoryFolder);
                        if (RLVFolder == null)
                        {
                            Client.Self.Chat(string.Empty, channel, ChatType.Normal);
                            return;
                        }
                        KeyValuePair<InventoryNode, LinkedList<string>> folderPath = FindInventoryPath<InventoryNode>(
                            RLVFolder,
                            CORRADE_CONSTANTS.OneOrMoRegex,
                            new LinkedList<string>())
                            .AsParallel().Where(o => o.Key.Data is InventoryFolder)
                            .FirstOrDefault(
                                o =>
                                    string.Join(RLV_CONSTANTS.PATH_SEPARATOR, o.Value.Skip(1).ToArray())
                                        .Equals(RLVrule.Option, StringComparison.InvariantCultureIgnoreCase));
                        if (folderPath.Equals(default(KeyValuePair<InventoryNode, LinkedList<string>>)))
                        {
                            Client.Self.Chat(string.Empty, channel, ChatType.Normal);
                            return;
                        }
                        Func<InventoryNode, string> GetWornIndicator = node =>
                        {
                            Dictionary<AppearanceManager.WearableData, WearableType> currentWearables =
                                GetWearables(Client.Inventory.Store.RootNode).ToDictionary(o => o.Key, o => o.Value);
                            Dictionary<Primitive, AttachmentPoint> currentAttachments =
                                GetAttachments(Configuration.SERVICES_TIMEOUT).ToDictionary(o => o.Key, p => p.Value);

                            int myItemsCount = 0;
                            int myItemsWornCount = 0;

                            Parallel.ForEach(
                                node.Nodes.Values.AsParallel().Where(
                                    n =>
                                        !n.Data.Name.StartsWith(RLV_CONSTANTS.DOT_MARKER) &&
                                        n.Data is InventoryItem && CanBeWorn(n.Data)
                                    ), n =>
                                    {
                                        Interlocked.Increment(ref myItemsCount);
                                        if ((n.Data is InventoryWearable &&
                                             currentWearables.Keys.AsParallel().Any(
                                                 o => o.ItemID.Equals(ResolveItemLink(n.Data as InventoryItem).UUID))) ||
                                            currentAttachments.AsParallel().Any(
                                                o =>
                                                    o.Key.Properties.ItemID.Equals(
                                                        ResolveItemLink(n.Data as InventoryItem).UUID)))
                                        {
                                            Interlocked.Increment(ref myItemsWornCount);
                                        }
                                    });


                            int allItemsCount = 0;
                            int allItemsWornCount = 0;

                            Parallel.ForEach(
                                node.Nodes.Values.AsParallel().Where(
                                    n =>
                                        !n.Data.Name.StartsWith(RLV_CONSTANTS.DOT_MARKER) &&
                                        n.Data is InventoryFolder
                                    ),
                                n => Parallel.ForEach(n.Nodes.Values
                                    .AsParallel().Where(o => !o.Data.Name.StartsWith(RLV_CONSTANTS.DOT_MARKER))
                                    .Where(
                                        o =>
                                            o.Data is InventoryItem && CanBeWorn(o.Data) &&
                                            !o.Data.Name.StartsWith(RLV_CONSTANTS.DOT_MARKER)), p =>
                                            {
                                                Interlocked.Increment(ref allItemsCount);
                                                if ((p.Data is InventoryWearable &&
                                                     currentWearables.Keys.AsParallel().Any(
                                                         o =>
                                                             o.ItemID.Equals(
                                                                 ResolveItemLink(p.Data as InventoryItem).UUID))) ||
                                                    currentAttachments.AsParallel().Any(
                                                        o =>
                                                            o.Key.Properties.ItemID.Equals(
                                                                ResolveItemLink(p.Data as InventoryItem).UUID)))
                                                {
                                                    Interlocked.Increment(ref allItemsWornCount);
                                                }
                                            }));


                            Func<int, int, string> WornIndicator =
                                (all, one) => all > 0 ? (all.Equals(one) ? "3" : (one > 0 ? "2" : "1")) : "0";

                            return WornIndicator(myItemsCount, myItemsWornCount) +
                                   WornIndicator(allItemsCount, allItemsWornCount);
                        };
                        List<string> response = new List<string>
                        {
                            string.Format("{0}{1}", RLV_CONSTANTS.PROPORTION_SEPARATOR,
                                GetWornIndicator(folderPath.Key))
                        };
                        response.AddRange(
                            folderPath.Key.Nodes.Values.AsParallel().Where(node => node.Data is InventoryFolder)
                                .Select(
                                    node =>
                                        string.Format("{0}{1}{2}", node.Data.Name,
                                            RLV_CONSTANTS.PROPORTION_SEPARATOR, GetWornIndicator(node))));

                        Client.Self.Chat(string.Join(RLV_CONSTANTS.CSV_DELIMITER, response.ToArray()),
                            channel,
                            ChatType.Normal);
                    };
                    break;
                case RLVBehaviour.GETSTATUSALL:
                case RLVBehaviour.GETSTATUS:
                    execute = () =>
                    {
                        int channel;
                        if (!int.TryParse(RLVrule.Param, out channel) || channel < 1)
                        {
                            return;
                        }
                        string separator = RLV_CONSTANTS.PATH_SEPARATOR;
                        string filter = string.Empty;
                        if (!string.IsNullOrEmpty(RLVrule.Option))
                        {
                            string[] parts = RLVrule.Option.Split(RLV_CONSTANTS.STATUS_SEPARATOR[0]);
                            if (parts.Length > 1 && parts[1].Length > 0)
                            {
                                separator = parts[1].Substring(0, 1);
                            }
                            if (parts.Length > 0 && parts[0].Length > 0)
                            {
                                filter = parts[0].ToLowerInvariant();
                            }
                        }
                        StringBuilder response = new StringBuilder();
                        lock (RLVRulesLock)
                        {
                            object LockObject = new object();
                            Parallel.ForEach(RLVRules.AsParallel().Where(o =>
                                o.ObjectUUID.Equals(senderUUID) && o.Behaviour.Contains(filter)
                                ), o =>
                                {
                                    lock (LockObject)
                                    {
                                        response.AppendFormat("{0}{1}", separator, o.Behaviour);
                                    }
                                    if (!string.IsNullOrEmpty(o.Option))
                                    {
                                        lock (LockObject)
                                        {
                                            response.AppendFormat("{0}{1}", RLV_CONSTANTS.PATH_SEPARATOR, o.Option);
                                        }
                                    }
                                });
                        }
                        Client.Self.Chat(response.ToString(),
                            channel,
                            ChatType.Normal);
                    };
                    break;
                case RLVBehaviour.CLEAR:
                    execute = () =>
                    {
                        switch (!string.IsNullOrEmpty(RLVrule.Option))
                        {
                            case true:
                                lock (RLVRulesLock)
                                {
                                    RLVRules.RemoveWhere(o => o.Behaviour.Contains(RLVrule.Behaviour));
                                }
                                break;
                            case false:
                                lock (RLVRulesLock)
                                {
                                    RLVRules.RemoveWhere(o => o.ObjectUUID.Equals(senderUUID));
                                }
                                break;
                        }
                    };
                    break;
                default:
                    execute =
                        () =>
                        {
                            throw new Exception(string.Join(CORRADE_CONSTANTS.ERROR_SEPARATOR,
                                wasGetDescriptionFromEnumValue(ConsoleError.BEHAVIOUR_NOT_IMPLEMENTED),
                                RLVrule.Behaviour));
                        };
                    break;
            }

            try
            {
                execute.Invoke();
            }
            catch (Exception ex)
            {
                Feedback(wasGetDescriptionFromEnumValue(ConsoleError.FAILED_TO_MANIFEST_RLV_BEHAVIOUR), ex.Message);
            }

            CONTINUE:
            HandleRLVBehaviour(message, senderUUID);
        }
    }
}