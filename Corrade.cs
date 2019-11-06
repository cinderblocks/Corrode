///////////////////////////////////////////////////////////////////////////
//  Copyright (C) Wizardry and Steamworks 2014 - License: GNU GPLv3      //
//  Please see: http://www.gnu.org/licenses/gpl.html for legal details,  //
//  rights of fair usage, the disclaimer and warranty conditions.        //
///////////////////////////////////////////////////////////////////////////

#region

using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Xml;
using OpenMetaverse;
using Timer = System.Timers.Timer;

#endregion

[assembly: CLSCompliant(true)]

namespace Corrade
{
    internal static class Corrade
    {
        /// <summary>
        ///     Corrade channel sent to the simulator.
        /// </summary>
        private const string CLIENT_CHANNEL = @"[Wizardry and Steamworks]:Corrade";

        /// <summary>
        ///     Corrade version sent to the simulator.
        /// </summary>
        private const string CORRADE_VERSION = @"7.9.23";

        private const string CORRADE_COMPILE_DATE = @"1st of September 2014";

        /// <summary>
        ///     Semaphores that sense the state of the connection. When any of these semaphores fail,
        ///     Corrade does not consider itself connected anymore and terminates.
        /// </summary>
        private static readonly Dictionary<char, ManualResetEvent> ConnectionSemaphores = new Dictionary
            <char, ManualResetEvent>
        {
            {'l', new ManualResetEvent(false)},
            {'s', new ManualResetEvent(false)}
        };

        private static readonly GridClient Client = new GridClient();

        private static readonly object FileLock = new object();

        private static readonly object GroupWorkersLock = new object();

        private static readonly Dictionary<string, int> GroupWorkers = new Dictionary<string, int>();

        private static readonly Timer IdleJoinGroupChatTimer = new Timer();

        private static readonly object DatabaseLock = new object();

        private static readonly Dictionary<string, object> DatabaseLocks = new Dictionary<string, object>();

        private static readonly object GroupNotificationsLock = new object();

        private static readonly HashSet<Notification> GroupNotifications =
            new HashSet<Notification>();

        // This extension method is broken out so you can use a similar pattern with
        // other MetaData elements in the future. This is your base method for each.
        private static T GetAttribute<T>(this Enum value) where T : Attribute
        {
            System.Type type = value.GetType();
            MemberInfo[] memberInfo = type.GetMember(value.ToString());
            object[] attributes = memberInfo[0].GetCustomAttributes(typeof (T), false);
            return (T) attributes[0];
        }

        ///////////////////////////////////////////////////////////////////////////
        //    Copyright (C) 2014 Wizardry and Steamworks - License: GNU GPLv3    //
        ///////////////////////////////////////////////////////////////////////////
        /// <summary>
        ///     Gets a field value from an enumeration by searching the enumeration
        ///     through reflection for a provided description.
        /// </summary>
        /// <param name="enumeration">the enumeration to searche</param>
        /// <param name="description"> the description to search for</param>
        /// <returns>the value of the field with the provided description</returns>
        private static uint wasGetEnumValueFromDescription(this Enum enumeration, string description)
        {
            return (from fi in enumeration.GetType().GetFields(BindingFlags.Static | BindingFlags.Public)
                let attribute = ((Enum) fi.GetValue(enumeration)).GetAttribute<DescriptionAttribute>()
                where
                    attribute != null && !string.IsNullOrEmpty(attribute.Description) &&
                    attribute.Description.Equals(description, StringComparison.InvariantCulture)
                select (uint) fi.GetValue(enumeration)).FirstOrDefault();
        }

        ///////////////////////////////////////////////////////////////////////////
        //    Copyright (C) 2014 Wizardry and Steamworks - License: GNU GPLv3    //
        ///////////////////////////////////////////////////////////////////////////
        /// <summary>
        ///     Returns all the field descriptions of an enumeration.
        /// </summary>
        /// <param name="enumeration">the enumeration</param>
        /// <returns>the field descriptions</returns>
        private static IEnumerable<string> wasGetEnumDescriptions(this Enum enumeration)
        {
            return from fi in enumeration.GetType().GetFields(BindingFlags.Static | BindingFlags.Public)
                select ((Enum) fi.GetValue(enumeration)).GetAttribute<DescriptionAttribute>()
                into attribute
                where attribute != null && !string.IsNullOrEmpty(attribute.Description)
                select attribute.Description;
        }

        // This method creates a specific call to the above method, requesting the
        // Description MetaData attribute.
        private static string GetEnumDescription(this Enum value)
        {
            DescriptionAttribute attribute = value.GetAttribute<DescriptionAttribute>();
            return attribute == null ? value.ToString() : attribute.Description;
        }

        ///////////////////////////////////////////////////////////////////////////
        //    Copyright (C) 2014 Wizardry and Steamworks - License: GNU GPLv3    //
        ///////////////////////////////////////////////////////////////////////////
        /// <summary>
        ///     Enumerates the fields of an object along with the child objects,
        ///     provided that all child objects are part of a specified namespace.
        /// </summary>
        /// <param name="object">the object to enumerate</param>
        /// <param name="namespace">the namespace to enumerate in</param>
        /// <returns>child objects of the object</returns>
        private static IEnumerable<KeyValuePair<FieldInfo, object>> wasGetFields(object @object, string @namespace)
        {
            if (@object == null) yield break;

            foreach (FieldInfo fi in @object.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public))
            {
                if (fi.FieldType.FullName.Split(new[] {'.', '+'})
                    .Contains(@namespace, StringComparer.InvariantCultureIgnoreCase))
                {
                    foreach (KeyValuePair<FieldInfo, object> sf in wasGetFields(fi.GetValue(@object), @namespace))
                    {
                        yield return sf;
                    }
                }
                yield return new KeyValuePair<FieldInfo, object>(fi, @object);
            }
        }

        ///////////////////////////////////////////////////////////////////////////
        //    Copyright (C) 2014 Wizardry and Steamworks - License: GNU GPLv3    //
        ///////////////////////////////////////////////////////////////////////////
        /// <summary>
        ///     Enumerates the properties of an object along with the child objects,
        ///     provided that all child objects are part of a specified namespace.
        /// </summary>
        /// <param name="object">the object to enumerate</param>
        /// <param name="namespace">the namespace to enumerate in</param>
        /// <returns>child objects of the object</returns>
        private static IEnumerable<KeyValuePair<PropertyInfo, object>> wasGetProperties(object @object,
            string @namespace)
        {
            if (@object == null) yield break;

            foreach (PropertyInfo pi in @object.GetType().GetProperties(BindingFlags.Instance | BindingFlags.Public))
            {
                if (pi.PropertyType.FullName.Split(new[] {'.', '+'})
                    .Contains(@namespace, StringComparer.InvariantCultureIgnoreCase))
                {
                    foreach (
                        KeyValuePair<PropertyInfo, object> sp in
                            wasGetProperties(pi.GetValue(@object, null), @namespace))
                    {
                        yield return sp;
                    }
                }
                yield return new KeyValuePair<PropertyInfo, object>(pi, @object);
            }
        }

        ///////////////////////////////////////////////////////////////////////////
        //    Copyright (C) 2014 Wizardry and Steamworks - License: GNU GPLv3    //
        ///////////////////////////////////////////////////////////////////////////
        /// <summary>
        ///     This is a wrapper for both FieldInfo and PropertyInfo SetValue.
        /// </summary>
        /// <param name="info">either a FieldInfo or PropertyInfo</param>
        /// <param name="object">the object to set the value on</param>
        /// <param name="value">the value to set</param>
        private static void wasSetInfoValue<I, T>(I info, ref T @object, object value)
        {
            object o = @object;
            FieldInfo fi = (object) info as FieldInfo;
            if (fi != null)
            {
                ((FieldInfo) (object) info).SetValue(o, value);
                @object = (T) o;
                return;
            }
            PropertyInfo pi = (object) info as PropertyInfo;
            if (pi != null)
            {
                ((PropertyInfo) (object) info).SetValue(o, value, null);
                @object = (T) o;
            }
        }

        ///////////////////////////////////////////////////////////////////////////
        //    Copyright (C) 2014 Wizardry and Steamworks - License: GNU GPLv3    //
        ///////////////////////////////////////////////////////////////////////////
        /// <summary>
        ///     This is a wrapper for both FieldInfo and PropertyInfo GetValue.
        /// </summary>
        /// <param name="info">either a FieldInfo or PropertyInfo</param>
        /// <param name="value">the object to get from</param>
        /// <returns>the value of the field or property</returns>
        private static object wasGetInfoValue<T>(T info, object value)
        {
            FieldInfo fi = (object) info as FieldInfo;
            if (fi != null)
            {
                return ((FieldInfo) (object) info).GetValue(value);
            }
            PropertyInfo pi = (object) info as PropertyInfo;
            if (pi != null)
            {
                return ((PropertyInfo) (object) info).GetValue(value, null);
            }
            return null;
        }

        private static IEnumerable<string> wasGetInfo(object info, object value)
        {
            if (info == null) yield break;
            object data = wasGetInfoValue(info, value);
            // Handle arrays
            Array list = data as Array;
            if (list != null)
            {
                IList array = (IList) data;
                if (array.Count.Equals(0)) yield break;
                foreach (object item in array)
                {
                    string itemValue = item.ToString();
                    if (string.IsNullOrEmpty(itemValue)) continue;
                    yield return itemValue;
                }
                yield break;
            }
            string @string = data.ToString();
            if (string.IsNullOrEmpty(@string)) yield break;
            yield return @string;
        }

        private static void wasSetInfo<T>(object info, object value, string setting, ref T @object)
        {
            if (info != null)
            {
                if (wasGetInfoValue(info, value) is string)
                {
                    wasSetInfoValue(info, ref @object, setting);
                }
                if (wasGetInfoValue(info, value) is UUID)
                {
                    UUID UUIDData;
                    if (!UUID.TryParse(setting, out UUIDData))
                    {
                        UUIDData =
                            SearchInventoryItem(Client.Inventory.Store.RootFolder,
                                setting,
                                Configuration.SERVICES_TIMEOUT).FirstOrDefault().AssetUUID;
                    }
                    if (UUIDData.Equals(UUID.Zero))
                    {
                        throw new Exception(
                            GetEnumDescription(ScriptError.INVENTORY_ITEM_NOT_FOUND));
                    }
                    wasSetInfoValue(info, ref @object, UUIDData);
                }
                if (wasGetInfoValue(info, value) is bool)
                {
                    bool boolData;
                    if (bool.TryParse(setting, out boolData))
                    {
                        wasSetInfoValue(info, ref @object, boolData);
                    }
                }
                if (wasGetInfoValue(info, value) is int)
                {
                    int intData;
                    if (int.TryParse(setting, out intData))
                    {
                        wasSetInfoValue(info, ref @object, intData);
                    }
                }
                if (wasGetInfoValue(info, value) is uint)
                {
                    uint uintData;
                    if (uint.TryParse(setting, out uintData))
                    {
                        wasSetInfoValue(info, ref @object, uintData);
                    }
                }
                if (wasGetInfoValue(info, value) is DateTime)
                {
                    DateTime dateTimeData;
                    if (DateTime.TryParse(setting, out dateTimeData))
                    {
                        wasSetInfoValue(info, ref @object, dateTimeData);
                    }
                }
            }
        }

        ///////////////////////////////////////////////////////////////////////////
        //    Copyright (C) 2014 Wizardry and Steamworks - License: GNU GPLv3    //
        ///////////////////////////////////////////////////////////////////////////
        /// <summary>
        ///     Determines whether an agent has a set of powers for a group.
        /// </summary>
        /// <param name="agentUUID">the agent UUID</param>
        /// <param name="groupUUID">the UUID of the group</param>
        /// <param name="powers">a GroupPowers structure</param>
        /// <param name="millisecondsTimeout">timeout for the search in milliseconds</param>
        /// <returns>true if the agent has the powers</returns>
        private static bool HasGroupPowers(UUID agentUUID, UUID groupUUID, GroupPowers powers, int millisecondsTimeout)
        {
            bool hasPowers = false;
            ManualResetEvent avatarGroupsEvent = new ManualResetEvent(false);
            EventHandler<AvatarGroupsReplyEventArgs> AvatarGroupsReplyEventHandler = (o, s) =>
            {
                hasPowers =
                    s.Groups.Any(m => m.GroupID.Equals(groupUUID) && (m.GroupPowers & powers) != GroupPowers.None);
                avatarGroupsEvent.Set();
            };
            Client.Avatars.AvatarGroupsReply += AvatarGroupsReplyEventHandler;
            Client.Avatars.RequestAvatarProperties(agentUUID);
            if (!avatarGroupsEvent.WaitOne(millisecondsTimeout, false))
            {
                Client.Avatars.AvatarGroupsReply -= AvatarGroupsReplyEventHandler;
                return false;
            }
            Client.Avatars.AvatarGroupsReply -= AvatarGroupsReplyEventHandler;
            return hasPowers;
        }

        ///////////////////////////////////////////////////////////////////////////
        //    Copyright (C) 2013 Wizardry and Steamworks - License: GNU GPLv3    //
        ///////////////////////////////////////////////////////////////////////////
        /// <summary>
        ///     Determines whether an agent referenced by an UUID is in a group
        ///     referenced by an UUID.
        /// </summary>
        /// <param name="agentUUID">the UUID of the agent</param>
        /// <param name="groupUUID">the UUID of the groupt</param>
        /// <param name="millisecondsTimeout">timeout for the search in milliseconds</param>
        /// <returns>true if the agent is in the group</returns>
        private static bool AgentInGroup(UUID agentUUID, UUID groupUUID, int millisecondsTimeout)
        {
            bool agentInGroup = false;
            ManualResetEvent agentInGroupEvent = new ManualResetEvent(false);
            EventHandler<GroupMembersReplyEventArgs> HandleGroupMembersReplyDelegate = (o, s) =>
            {
                agentInGroup = s.Members.Any(m => m.Value.ID.Equals(agentUUID));
                agentInGroupEvent.Set();
            };
            Client.Groups.GroupMembersReply += HandleGroupMembersReplyDelegate;
            Client.Groups.RequestGroupMembers(groupUUID);
            if (!agentInGroupEvent.WaitOne(millisecondsTimeout, false))
            {
                Client.Groups.GroupMembersReply -= HandleGroupMembersReplyDelegate;
                return false;
            }
            Client.Groups.GroupMembersReply -= HandleGroupMembersReplyDelegate;
            return agentInGroup;
        }

        /// <summary>
        ///     Used to check whether a group name matches a group password.
        /// </summary>
        /// <param name="groupName">the name of the group</param>
        /// <param name="password">the password for the group</param>
        /// <returns>true if the agent has authenticated</returns>
        private static bool Authenticate(string groupName, string password)
        {
            return Configuration.GROUPS.Any(
                g =>
                    g.Name.Equals(groupName, StringComparison.InvariantCulture) &&
                    password.Equals(g.Password, StringComparison.InvariantCulture));
        }

        /// <summary>
        ///     Used to check whether a group has certain permissions for Corrade.
        /// </summary>
        /// <param name="groupName">the name of the group</param>
        /// <param name="permission">the numeric Corrade permission</param>
        /// <returns>true if the group has permission</returns>
        private static bool HasCorradePermission(string groupName, int permission)
        {
            return permission != 0 &&
                   Configuration.GROUPS.Any(
                       g =>
                           g.Name.Equals(groupName, StringComparison.InvariantCulture) &&
                           (g.PermissionMask & permission) != 0);
        }

        /// <summary>
        ///     Used to check whether a group has a certain notification for Corrade.
        /// </summary>
        /// <param name="groupName">the name of the group</param>
        /// <param name="notification">the numeric Corrade notification</param>
        /// <returns>true if the group has the notification</returns>
        private static bool HasCorradeNotification(string groupName, int notification)
        {
            return notification != 0 &&
                   Configuration.GROUPS.Any(
                       g => g.Name.Equals(groupName, StringComparison.InvariantCulture) &&
                            (g.NotificationMask & notification) != 0);
        }

        ///////////////////////////////////////////////////////////////////////////
        //    Copyright (C) 2013 Wizardry and Steamworks - License: GNU GPLv3    //
        ///////////////////////////////////////////////////////////////////////////
        /// <summary>
        ///     Fetches a group.
        /// </summary>
        /// <param name="groupUUID">the UUID of the group</param>
        /// <param name="millisecondsTimeout">timeout for the search in milliseconds</param>
        /// <param name="group">a group object to store the group profile</param>
        /// <returns>true if the group was found and false otherwise</returns>
        private static bool RequestGroup(UUID groupUUID, int millisecondsTimeout, ref OpenMetaverse.Group group)
        {
            OpenMetaverse.Group localGroup = new OpenMetaverse.Group();
            ManualResetEvent GroupProfileEvent = new ManualResetEvent(false);
            EventHandler<GroupProfileEventArgs> GroupProfileDelegate = (o, s) =>
            {
                localGroup = s.Group;
                GroupProfileEvent.Set();
            };
            Client.Groups.GroupProfile += GroupProfileDelegate;
            Client.Groups.RequestGroupProfile(groupUUID);
            if (!GroupProfileEvent.WaitOne(millisecondsTimeout, false))
            {
                Client.Groups.GroupProfile -= GroupProfileDelegate;
                return false;
            }
            Client.Groups.GroupProfile -= GroupProfileDelegate;
            group = localGroup;
            return true;
        }

        ///////////////////////////////////////////////////////////////////////////
        //    Copyright (C) 2014 Wizardry and Steamworks - License: GNU GPLv3    //
        ///////////////////////////////////////////////////////////////////////////
        /// <summary>
        ///     Get the parcel of a simulator given a position.
        /// </summary>
        /// <param name="simulator">the simulator containing the parcel</param>
        /// <param name="position">a position within the parcel</param>
        /// <param name="millisecondsTimeout">timeout for the search in milliseconds</param>
        /// <param name="parcel">a parcel object where to store the found parcel</param>
        /// <returns>true if the parcel could be found</returns>
        private static bool GetParcelAtPosition(Simulator simulator, Vector3 position, int millisecondsTimeout,
            ref Parcel parcel)
        {
            Parcel localParcel = null;
            ManualResetEvent RequestAllSimParcelsEvent = new ManualResetEvent(false);
            EventHandler<SimParcelsDownloadedEventArgs> SimParcelsDownloadedDelegate =
                (o, s) => RequestAllSimParcelsEvent.Set();
            Client.Parcels.SimParcelsDownloaded += SimParcelsDownloadedDelegate;
            Client.Parcels.RequestAllSimParcels(simulator, true, simulator.Stats.LastLag);
            if (!RequestAllSimParcelsEvent.WaitOne(millisecondsTimeout*simulator.Stats.LastLag, false))
            {
                Client.Parcels.SimParcelsDownloaded -= SimParcelsDownloadedDelegate;
                return false;
            }
            Client.Parcels.SimParcelsDownloaded -= SimParcelsDownloadedDelegate;
            Client.Network.CurrentSim.Parcels.ForEach(delegate(Parcel currentParcel)
            {
                if (!(position.X >= currentParcel.AABBMin.X) || !(position.X <= currentParcel.AABBMax.X) ||
                    !(position.Y >= currentParcel.AABBMin.Y) || !(position.Y <= currentParcel.AABBMax.Y))
                    return;
                localParcel = currentParcel;
            });
            if (localParcel == null)
                return false;
            parcel = localParcel;
            return true;
        }

        ///////////////////////////////////////////////////////////////////////////
        //    Copyright (C) 2014 Wizardry and Steamworks - License: GNU GPLv3    //
        ///////////////////////////////////////////////////////////////////////////
        /// <summary>
        ///     Find a named primitive in range.
        /// </summary>
        /// <param name="item">the name or UUID of the primitive</param>
        /// <param name="range">the range in meters to search for the object</param>
        /// <param name="millisecondsTimeout">timeout for the search in milliseconds</param>
        /// <param name="primitive">a primitive object to store the result</param>
        /// <returns>true if the primitive could be found</returns>
        private static bool FindPrimitive(string item, float range, int millisecondsTimeout, ref Primitive primitive)
        {
            UUID itemUUID;
            if (!UUID.TryParse(item, out itemUUID))
            {
                itemUUID = UUID.Zero;
            }
            //HashSet<Primitive> primitives = null;
            HashSet<Primitive> primitives = !itemUUID.Equals(UUID.Zero)
                ? new HashSet<Primitive>(Client.Network.CurrentSim.ObjectsPrimitives.FindAll(
                    p => p.ID.Equals(itemUUID)).ToArray())
                : new HashSet<Primitive>(Client.Network.CurrentSim.ObjectsPrimitives.FindAll(
                    p => !p.ID.Equals(UUID.Zero)).ToArray());
            if (primitives.Count == 0)
                return false;
            HashSet<Avatar> avatars =
                new HashSet<Avatar>(Client.Network.CurrentSim.ObjectsAvatars.FindAll(a => !a.ID.Equals(UUID.Zero)));
            Hashtable primitiveQueue = new Hashtable(primitives.Count);
            object LockObject = new object();
            Parallel.ForEach(primitives, p =>
            {
                // child primitive of either an avatar or primitive
                if (!p.ParentID.Equals(0))
                {
                    // find the parent of the primitive
                    Primitive parentPrimitive = FindParent(p, primitives);
                    // the parent primitive has no other parent
                    if (parentPrimitive.ParentID.Equals(0))
                    {
                        // if the parent is in range, add the child
                        if (Vector3.Distance(parentPrimitive.Position, Client.Self.SimPosition) < range)
                        {
                            lock (LockObject)
                            {
                                primitiveQueue.Add(p.ID, p.LocalID);
                            }
                            return;
                        }
                    }
                    // check if an avatar is the parent of the parent primitive
                    Avatar parentAvatar = avatars.FirstOrDefault(a => a.LocalID.Equals(parentPrimitive.ParentID));
                    // parent avatar not found, this should not happen
                    if (parentAvatar == null) return;
                    // check if the avatar is in range
                    if (Vector3.Distance(parentAvatar.Position, Client.Self.SimPosition) < range)
                    {
                        lock (LockObject)
                        {
                            primitiveQueue.Add(p.ID, p.LocalID);
                        }
                    }
                    return;
                }
                // primitive is a parent and it is in a 10m range
                if (Vector3.Distance(p.Position, Client.Self.SimPosition) < range)
                {
                    lock (LockObject)
                    {
                        primitiveQueue.Add(p.ID, p.LocalID);
                    }
                }
            });
            if (primitiveQueue.Count.Equals(0))
                return false;
            ManualResetEvent ObjectPropertiesEvent = new ManualResetEvent(false);
            EventHandler<ObjectPropertiesEventArgs> ObjectPropertiesEventHandler = (sender, args) =>
            {
                primitiveQueue.Remove(args.Properties.ObjectID);
                if (args.Properties.Name.Equals(item, StringComparison.InvariantCulture))
                {
                    ObjectPropertiesEvent.Set();
                }
                if (!itemUUID.Equals(UUID.Zero) && args.Properties.ItemID.Equals(itemUUID))
                {
                    ObjectPropertiesEvent.Set();
                }
                if (primitiveQueue.Count.Equals(0))
                {
                    ObjectPropertiesEvent.Set();
                }
            };
            Client.Objects.ObjectProperties += ObjectPropertiesEventHandler;
            Client.Objects.SelectObjects(Client.Network.CurrentSim, primitiveQueue.Values.Cast<uint>().ToArray(), true);
            if (
                !ObjectPropertiesEvent.WaitOne(
                    millisecondsTimeout, false))
            {
                Client.Objects.ObjectProperties -= ObjectPropertiesEventHandler;
                return false;
            }
            Client.Objects.ObjectProperties -= ObjectPropertiesEventHandler;
            primitive =
                primitives.FirstOrDefault(
                    p =>
                        p.ID.Equals(itemUUID) ||
                        (p.Properties != null && p.Properties.Name.Equals(item, StringComparison.InvariantCulture)));
            return primitive != null;
        }

        ///////////////////////////////////////////////////////////////////////////
        //    Copyright (C) 2014 Wizardry and Steamworks - License: GNU GPLv3    //
        ///////////////////////////////////////////////////////////////////////////
        /// <summary>Finds the parent primitive in a list of primitives.</summary>
        /// <param name="primitive">The primitive for which the parent must be found.</param>
        /// <param name="primitives">The list of primitives where to find the parent.</param>
        /// <returns>The parent of the primitive or the primitive in case the list does not contain the parent.</returns>
        private static Primitive FindParent(Primitive primitive, HashSet<Primitive> primitives)
        {
            while (!primitive.ParentID.Equals(0))
            {
                Primitive currentPrimitive = primitive;
                Primitive parent = primitives.FirstOrDefault(p => p.LocalID.Equals(currentPrimitive.ParentID));
                if (parent == null) return primitive;
                primitive = parent;
            }
            return primitive;
        }

        ///////////////////////////////////////////////////////////////////////////
        //    Copyright (C) 2014 Wizardry and Steamworks - License: GNU GPLv3    //
        ///////////////////////////////////////////////////////////////////////////
        /// <summary>
        ///     Get all worn attachments.
        /// </summary>
        /// <param name="millisecondsTimeout">timeout for the search in milliseconds</param>
        /// <param name="attachments">a dictionary containing attachment points to primitives</param>
        /// <returns>a dictionary of attachment points to primitives</returns>
        private static bool GetAttachments(int millisecondsTimeout,
            ref List<KeyValuePair<AttachmentPoint, Primitive>> attachments)
        {
            HashSet<Primitive> primitives = new HashSet<Primitive>(Client.Network.CurrentSim.ObjectsPrimitives.FindAll(
                p => p.ParentID.Equals(Client.Self.LocalID)));
            Hashtable primitiveQueue = new Hashtable(primitives.Count);
            object LockObject = new object();
            Parallel.ForEach(primitives, p =>
            {
                lock (LockObject)
                {
                    primitiveQueue.Add(p.ID, p.LocalID);
                }
            });
            ManualResetEvent ObjectPropertiesEvent = new ManualResetEvent(false);
            EventHandler<ObjectPropertiesEventArgs> ObjectPropertiesEventHandler = (sender, args) =>
            {
                primitiveQueue.Remove(args.Properties.ObjectID);
                if (!primitiveQueue.Count.Equals(0)) return;
                ObjectPropertiesEvent.Set();
            };
            Client.Objects.ObjectProperties += ObjectPropertiesEventHandler;
            Client.Objects.SelectObjects(Client.Network.CurrentSim, primitiveQueue.Values.Cast<uint>().ToArray(), true);
            if (!ObjectPropertiesEvent.WaitOne(millisecondsTimeout, false))
            {
                Client.Objects.ObjectProperties -= ObjectPropertiesEventHandler;
                return false;
            }
            Client.Objects.ObjectProperties -= ObjectPropertiesEventHandler;
            List<KeyValuePair<AttachmentPoint, Primitive>> localAttachments =
                new List<KeyValuePair<AttachmentPoint, Primitive>>(primitives.Count);
            Parallel.ForEach(primitives, primitive =>
            {
                const uint ATTACHMENT_MASK = 0xF0;
                uint fixedState = ((primitive.PrimData.State & ATTACHMENT_MASK) >> 4) |
                                  ((primitive.PrimData.State & ~ATTACHMENT_MASK) << 4);
                lock (LockObject)
                {
                    localAttachments.Add(new KeyValuePair<AttachmentPoint, Primitive>((AttachmentPoint) fixedState,
                        primitive));
                }
            });
            attachments = localAttachments;
            return true;
        }

        ///////////////////////////////////////////////////////////////////////////
        //    Copyright (C) 2014 Wizardry and Steamworks - License: GNU GPLv3    //
        ///////////////////////////////////////////////////////////////////////////
        /// ///
        /// <summary>
        ///     Searches the current inventory for an item by name or UUID and
        ///     returns all the items that match the item name.
        /// </summary>
        /// <param name="rootFolder">a folder from which to search</param>
        /// <param name="item">the name  or UUID of the item to be found</param>
        /// <param name="millisecondsTimeout">timeout for the search</param>
        /// <returns>returns a list of items matching the item name</returns>
        private static IEnumerable<InventoryItem> SearchInventoryItem(InventoryBase rootFolder, string item,
            int millisecondsTimeout)
        {
            HashSet<InventoryBase> contents =
                new HashSet<InventoryBase>(Client.Inventory.FolderContents(rootFolder.UUID, Client.Self.AgentID,
                    true, true, InventorySortOrder.ByName, millisecondsTimeout));
            UUID itemUUID;
            UUID.TryParse(item, out itemUUID);
            foreach (InventoryBase inventory in contents)
            {
                InventoryItem i = inventory as InventoryItem;
                if (i != null)
                {
                    if (inventory.Name.Equals(item, StringComparison.InvariantCulture) ||
                        (!itemUUID.Equals(UUID.Zero) &&
                         (inventory.UUID.Equals(itemUUID) || i.AssetUUID.Equals(itemUUID))))
                    {
                        yield return i;
                    }
                }
                if (contents.Count == 0)
                    continue;
                InventoryFolder inventoryFolder = inventory as InventoryFolder;
                if (inventoryFolder == null)
                    continue;
                foreach (InventoryItem inventoryItem in SearchInventoryItem(inventoryFolder, item, millisecondsTimeout))
                {
                    yield return inventoryItem;
                }
            }
        }

        /// <summary>
        ///     Posts messages to console or log-files.
        /// </summary>
        /// <param name="messages">a list of messages</param>
        private static void Feedback(params object[] messages)
        {
            HashSet<string> output = new HashSet<string>
            {
                string.Format(CultureInfo.InvariantCulture, "[{0}]",
                    DateTime.Now.ToString("dd-MM-yyyy HH:mm", DateTimeFormatInfo.InvariantInfo))
            };
            object LockObject = new object();
            Parallel.ForEach(messages, message =>
            {
                System.Type type = message.GetType();
                if (type == typeof (ConsoleError))
                    lock (LockObject)
                    {
                        output.Add(GetEnumDescription((Enum) message));
                    }
                if (type == typeof (string))
                    lock (LockObject)
                    {
                        output.Add(message.ToString());
                    }
            });
            // Attempt to write to log file,
            try
            {
                lock (FileLock)
                {
                    using (StreamWriter logWriter = File.AppendText(Configuration.LOG_FILE))
                    {
                        logWriter.WriteLine(string.Join(Environment.NewLine, output.ToArray()));
                        logWriter.Flush();
                        logWriter.Close();
                    }
                }
            }
            catch (Exception e)
            {
                // or fail and append the fail message.
                output.Add(string.Format(CultureInfo.InvariantCulture,
                    "The request could not be logged to {0} and returned the error message {1}.",
                    Configuration.LOG_FILE, e.Message));
            }
            // If we do not have a console, do not log.
            if (!Console.WindowWidth.Equals(0))
                Console.WriteLine(string.Join(" : ", output.ToArray()));
        }

        /// <summary>
        ///     Writes the logo and the version.
        /// </summary>
        private static void WriteLogo()
        {
            StringBuilder sb = new StringBuilder();
            List<string> Logo = new List<string>
            {
                Environment.NewLine,
                Environment.NewLine,
                @"       _..--=--..._  " + Environment.NewLine,
                @"    .-'            '-.  .-.  " + Environment.NewLine,
                @"   /.'              '.\/  /  " + Environment.NewLine,
                @"  |=-     Corrade    -=| (  " + Environment.NewLine,
                @"   \'.              .'/\  \  " + Environment.NewLine,
                @"    '-.,_____ _____.-'  '-'  " + Environment.NewLine,
                @"          [_____]=8  " + Environment.NewLine,
                @"               \  " + Environment.NewLine,
                @"                 Good day!  ",
                Environment.NewLine,
                Environment.NewLine,
                string.Format(CultureInfo.InvariantCulture,
                    Environment.NewLine + "Version: {0} Compiled: {1}" + Environment.NewLine, CORRADE_VERSION,
                    CORRADE_COMPILE_DATE),
                string.Format(CultureInfo.InvariantCulture,
                    "(c) Copyright 2013 Wizardry and Steamworks" + Environment.NewLine),
            };
            Logo.ForEach(line => sb.Append(line));
            Feedback(sb.ToString());
        }

        // Main entry point.
        public static void Main()
        {
            // Load the configuration file.
            Configuration.Load("Corrade.ini");
            // Set-up watcher for dynamically reading the configuration file.
            FileSystemWatcher watcher = new FileSystemWatcher
            {
                Path = Directory.GetCurrentDirectory(),
                Filter = "Corrade.ini",
                NotifyFilter = NotifyFilters.LastWrite
            };
            watcher.Changed += HandleConfigurationFileChanged;
            watcher.EnableRaisingEvents = true;
            // Suppress standard OpenMetaverse logs, we have better ones.
            Settings.LOG_LEVEL = Helpers.LogLevel.None;
            Client.Settings.STORE_LAND_PATCHES = true;
            Client.Settings.ALWAYS_DECODE_OBJECTS = true;
            Client.Settings.ALWAYS_REQUEST_OBJECTS = true;
            Client.Settings.SEND_AGENT_APPEARANCE = true;
            Client.Settings.AVATAR_TRACKING = true;
            Client.Settings.OBJECT_TRACKING = true;
            Client.Settings.ENABLE_CAPS = true;
            // Install global event handlers.
            Client.Network.LoginProgress += HandleLoginProgress;
            Client.Appearance.AppearanceSet += HandleAppearanceSet;
            Client.Network.SimConnected += HandleSimulatorConnected;
            Client.Network.Disconnected += HandleDisconnected;
            Client.Network.SimDisconnected += HandleSimulatorDisconnected;
            Client.Network.EventQueueRunning += HandleEventQueueRunning;
            Client.Friends.FriendshipOffered += HandleFriendshipOffered;
            Client.Self.TeleportProgress += HandleTeleportProgress;
            Client.Self.ScriptQuestion += HandleScriptQuestion;
            Client.Self.AlertMessage += HandleAlertMessage;
            Client.Objects.AvatarUpdate += HandleAvatarUpdate;
            Client.Objects.TerseObjectUpdate += HandleTerseObjectUpdate;
            Client.Network.SimChanged += HandleSimChanged;
            Client.Self.MoneyBalance += HandleMoneyBalance;
            Client.Self.ChatFromSimulator += HandleChatFromSimulator;
            Client.Self.ScriptDialog += HandleScriptDialog;
            // Each Instant Message is processed in its own thread.
            Client.Self.IM += (sender, args) => new Thread(o => HandleSelfIM(args)).Start();
            Client.Inventory.InventoryObjectOffered += HandleInventoryObjectOffered;
            // Write the logo.
            WriteLogo();
            // Proceed to log-in.
            LoginParams login = new LoginParams(
                Client,
                Configuration.FIRST_NAME,
                Configuration.LAST_NAME,
                Configuration.PASSWORD,
                CLIENT_CHANNEL,
                CORRADE_VERSION.ToString(CultureInfo.InvariantCulture),
                Configuration.LOGIN_URL)
            {
                Author = "Wizardry and Steamworks",
                AgreeToTos = Configuration.TOS_ACCEPTED,
                Start = Configuration.START_LOCATION,
                UserAgent = "libopenmetaverse"
            };
            // Check TOS
            if (!login.AgreeToTos)
            {
                Feedback(ConsoleError.TOS_NOT_ACCEPTED);
                Environment.Exit(1);
            }
            Feedback(ConsoleError.LOGGING_IN);
            Client.Network.Login(login);
            IdleJoinGroupChatTimer.Interval = Configuration.IDLE_JOIN_GROUP_CHAT_TIME;
            IdleJoinGroupChatTimer.AutoReset = true;
            IdleJoinGroupChatTimer.Elapsed += (sender, args) => Parallel.ForEach(Configuration.GROUPS, g =>
            {
                UUID groupChatJoinUUID = g.UUID;
                if (groupChatJoinUUID.Equals(UUID.Zero) &&
                    !GroupNameToUUID(g.Name, Configuration.SERVICES_TIMEOUT, ref groupChatJoinUUID))
                {
                    Feedback(ConsoleError.UNABLE_TO_JOIN_GROUP_CHAT, g.Name);
                    return;
                }
                // already in the chat session
                if (Client.Self.GroupChatSessions.ContainsKey(groupChatJoinUUID)) return;
                if (
                    !HasGroupPowers(Client.Self.AgentID, groupChatJoinUUID, GroupPowers.JoinChat,
                        Configuration.SERVICES_TIMEOUT))
                {
                    Feedback(ConsoleError.UNABLE_TO_JOIN_GROUP_CHAT, g.Name);
                    return;
                }
                bool chatJoined = false;
                ManualResetEvent GroupChatJoinedEvent = new ManualResetEvent(false);
                EventHandler<GroupChatJoinedEventArgs> GroupChatJoinedEventHandler = (s, a) =>
                {
                    if (a.Success)
                    {
                        chatJoined = true;
                    }
                    GroupChatJoinedEvent.Set();
                };
                Client.Self.GroupChatJoined += GroupChatJoinedEventHandler;
                Client.Self.RequestJoinGroupChat(groupChatJoinUUID);
                if (!GroupChatJoinedEvent.WaitOne(Configuration.SERVICES_TIMEOUT, false))
                {
                    Client.Self.GroupChatJoined -= GroupChatJoinedEventHandler;
                    Feedback(ConsoleError.UNABLE_TO_JOIN_GROUP_CHAT, g.Name);
                    return;
                }
                Client.Self.GroupChatJoined -= GroupChatJoinedEventHandler;
                if (chatJoined)
                    return;
                Feedback(ConsoleError.UNABLE_TO_JOIN_GROUP_CHAT, g.Name);
            });
            IdleJoinGroupChatTimer.Start();
            /*
             * The main thread spins around waiting for the semaphores to become invalidated,
             * at which point Corrade will consider its connection to the grid severed and
             * will terminate.
             *
             */
            WaitHandle.WaitAny(ConnectionSemaphores.Values.Select(s => (WaitHandle) s).ToArray());
            // Now log-out.
            Feedback(ConsoleError.LOGGING_OUT);
            IdleJoinGroupChatTimer.Stop();
            Client.Network.Logout();
            Client.Network.Shutdown(NetworkManager.DisconnectType.ClientInitiated);
        }

        private static void HandleScriptDialog(object sender, ScriptDialogEventArgs e)
        {
            // First check if the group is able to receive dialog notifications.
            Parallel.ForEach(
                GroupNotifications.Where(
                    n => HasCorradeNotification(n.GROUP, (int) Notifications.NOTIFICATION_SCRIPT_DIALOG)), n =>
                    {
                        // Next, check if the group has registered to receive dialog notifications.
                        if ((n.NOTIFICATION_MASK & (int) Notifications.NOTIFICATION_SCRIPT_DIALOG) == 0)
                        {
                            return;
                        }
                        new Thread(script_dialog =>
                        {
                            Dictionary<string, string> notification = new Dictionary<string, string>
                            {
                                {ScriptKeys.TYPE, GetEnumDescription(Notifications.NOTIFICATION_SCRIPT_DIALOG)},
                                {ScriptKeys.MESSAGE, e.Message},
                                {ScriptKeys.FIRSTNAME, e.FirstName},
                                {ScriptKeys.LASTNAME, e.LastName},
                                {ScriptKeys.CHANNEL, e.Channel.ToString(CultureInfo.InvariantCulture)},
                                {ScriptKeys.NAME, e.ObjectName},
                                {ScriptKeys.ITEM, e.ObjectID.ToString()},
                                {ScriptKeys.OWNER, e.OwnerID.ToString()},
                                {
                                    ScriptKeys.BUTTON,
                                    string.Join(LINDEN_CONSTANTS.LSL.CSV_DELIMITER, e.ButtonLabels.ToArray())
                                }
                            };
                            string error = wasPOST(n.URL, wasKeyValueEscape(notification));
                            if (!string.IsNullOrEmpty(error))
                            {
                                Feedback(ConsoleError.NOTIFICATION_COULD_NOT_BE_SENT);
                            }
                        }).Start();
                    });
        }

        private static void HandleChatFromSimulator(object sender, ChatEventArgs e)
        {
            // Ignore self
            if (e.SourceID.Equals(Client.Self.AgentID)) return;
            // Ignore chat with no message (ie: start / stop typing)
            if (string.IsNullOrEmpty(e.Message)) return;
            // First check if the group is able to receive local chat message notifications.
            Parallel.ForEach(
                GroupNotifications.Where(
                    n => HasCorradeNotification(n.GROUP, (int) Notifications.NOTIFICATION_LOCAL_CHAT)), n =>
                    {
                        // Next, check if the group has registered to receive local chat message notifications.
                        if ((n.NOTIFICATION_MASK & (int) Notifications.NOTIFICATION_LOCAL_CHAT) == 0)
                        {
                            return;
                        }
                        new Thread(local_chat =>
                        {
                            AgentName agent = new AgentName().FromFullName(e.FromName);
                            Dictionary<string, string> notification = new Dictionary<string, string>
                            {
                                {ScriptKeys.TYPE, GetEnumDescription(Notifications.NOTIFICATION_LOCAL_CHAT)},
                                {ScriptKeys.MESSAGE, e.Message},
                                {ScriptKeys.FIRSTNAME, agent.FirstName},
                                {ScriptKeys.LASTNAME, agent.LastName},
                                {ScriptKeys.OWNER, e.OwnerID.ToString()},
                                {ScriptKeys.ITEM, e.SourceID.ToString()}
                            };
                            string error = wasPOST(n.URL, wasKeyValueEscape(notification));
                            if (!string.IsNullOrEmpty(error))
                            {
                                Feedback(ConsoleError.NOTIFICATION_COULD_NOT_BE_SENT);
                            }
                        }).Start();
                    });
        }

        private static void HandleMoneyBalance(object sender, BalanceEventArgs e)
        {
            // First check if the group is able to receive alert message notifications.
            Parallel.ForEach(
                GroupNotifications.Where(
                    n => HasCorradeNotification(n.GROUP, (int) Notifications.NOTIFICATION_ALERT_MESSAGE)), n =>
                    {
                        // Next, check if the group has registered to receive alert message notifications.
                        if ((n.NOTIFICATION_MASK & (int) Notifications.NOTIFICATION_ALERT_MESSAGE) == 0)
                        {
                            return;
                        }
                        new Thread(alert_message =>
                        {
                            Dictionary<string, string> notification = new Dictionary<string, string>
                            {
                                {ScriptKeys.TYPE, GetEnumDescription(Notifications.NOTIFICATION_BALANCE)},
                                {ScriptKeys.BALANCE, e.Balance.ToString(CultureInfo.InvariantCulture)}
                            };
                            string error = wasPOST(n.URL, wasKeyValueEscape(notification));
                            if (!string.IsNullOrEmpty(error))
                            {
                                Feedback(ConsoleError.NOTIFICATION_COULD_NOT_BE_SENT);
                            }
                        }).Start();
                    });
        }

        private static void HandleAlertMessage(object sender, AlertMessageEventArgs e)
        {
            // First check if the group is able to receive alert message notifications.
            Parallel.ForEach(
                GroupNotifications.Where(
                    n => HasCorradeNotification(n.GROUP, (int) Notifications.NOTIFICATION_ALERT_MESSAGE)), n =>
                    {
                        // Next, check if the group has registered to receive alert message notifications.
                        if ((n.NOTIFICATION_MASK & (int) Notifications.NOTIFICATION_ALERT_MESSAGE) == 0)
                        {
                            return;
                        }
                        new Thread(alert_message =>
                        {
                            Dictionary<string, string> notification = new Dictionary<string, string>
                            {
                                {ScriptKeys.TYPE, GetEnumDescription(Notifications.NOTIFICATION_ALERT_MESSAGE)},
                                {ScriptKeys.MESSAGE, e.Message}
                            };
                            string error = wasPOST(n.URL, wasKeyValueEscape(notification));
                            if (!string.IsNullOrEmpty(error))
                            {
                                Feedback(ConsoleError.NOTIFICATION_COULD_NOT_BE_SENT);
                            }
                        }).Start();
                    });
        }

        private static void HandleInventoryObjectOffered(object sender, InventoryObjectOfferedEventArgs e)
        {
            if (
                !Configuration.MASTERS.Select(
                    master => string.Format(CultureInfo.InvariantCulture, "{0} {1}", master.FirstName, master.LastName))
                    .
                    Any(name => name.Equals(e.Offer.FromAgentName, StringComparison.InvariantCultureIgnoreCase)))
                return;
            e.Accept = true;
        }

        private static void HandleScriptQuestion(object sender, ScriptQuestionEventArgs e)
        {
            switch (e.Questions)
            {
                case ScriptPermission.TriggerAnimation:
                    Client.Self.ScriptQuestionReply(Client.Network.CurrentSim, e.ItemID, e.TaskID,
                        ScriptPermission.TriggerAnimation);
                    break;
            }
        }

        private static void HandleConfigurationFileChanged(object sender, FileSystemEventArgs e)
        {
            Feedback(ConsoleError.CONFIGURATION_FILE_MODIFIED);
            Configuration.Load(e.Name);
        }

        private static void HandleDisconnected(object sender, DisconnectedEventArgs e)
        {
            Feedback(ConsoleError.DISCONNECTED);
            Parallel.ForEach(ConnectionSemaphores.Where(sem => sem.Key.Equals('l')), sem => sem.Value.Set());
        }

        private static void HandleEventQueueRunning(object sender, EventQueueRunningEventArgs e)
        {
            Feedback(ConsoleError.EVENT_QUEUE_STARTED);
        }

        private static void HandleSimulatorConnected(object sender, SimConnectedEventArgs e)
        {
            Feedback(ConsoleError.SIMULATOR_CONNECTED);
        }

        private static void HandleSimulatorDisconnected(object sender, SimDisconnectedEventArgs e)
        {
            if (Client.Network.Simulators.Any())
                return;
            Feedback(ConsoleError.ALL_SIMULATORS_DISCONNECTED);
            Parallel.ForEach(ConnectionSemaphores.Where(sem => sem.Key.Equals('s')), sem => sem.Value.Set());
        }

        private static void HandleAppearanceSet(object sender, AppearanceSetEventArgs e)
        {
            if (e.Success)
            {
                Feedback(ConsoleError.APPEARANCE_SET_SUCCEEDED);
                return;
            }
            Feedback(ConsoleError.APPEARANCE_SET_FAILED);
        }

        private static void HandleLoginProgress(object sender, LoginProgressEventArgs e)
        {
            switch (e.Status)
            {
                case LoginStatus.Success:
                    Feedback(ConsoleError.LOGIN_SUCCEEDED);
                    break;
                case LoginStatus.Failed:
                    Feedback(ConsoleError.LOGIN_FAILED, e.FailReason);
                    Parallel.ForEach(ConnectionSemaphores.Where(sem => sem.Key.Equals('l')), sem => sem.Value.Set());
                    break;
            }
        }

        private static void HandleFriendshipOffered(object sender, FriendshipOfferedEventArgs e)
        {
            if (
                !Configuration.MASTERS.Select(
                    master => string.Format(CultureInfo.InvariantCulture, "{0} {1}", master.FirstName, master.LastName))
                    .Any(name => name.Equals(e.AgentName, StringComparison.CurrentCultureIgnoreCase)))
                return;
            Feedback(ConsoleError.ACCEPTED_FRIENDSHIP, e.AgentName);
            Client.Friends.AcceptFriendship(e.AgentID, e.SessionID);
        }

        private static void HandleTeleportProgress(object sender, TeleportEventArgs e)
        {
            switch (e.Status)
            {
                case TeleportStatus.Finished:
                    Feedback(ConsoleError.TELEPORT_SUCCEEDED);
                    break;
                case TeleportStatus.Failed:
                    Feedback(ConsoleError.TELEPORT_FAILED);
                    break;
            }
        }

        private static void HandleSelfIM(InstantMessageEventArgs e)
        {
            // Ignore self.
            if (e.IM.FromAgentName.Equals(string.Join(" ", new[] {Client.Self.FirstName, Client.Self.LastName}),
                StringComparison.InvariantCulture))
                return;
            // Process dialog messages.
            switch (e.IM.Dialog)
            {
                    // Ignore typing messages.
                case InstantMessageDialog.StartTyping:
                case InstantMessageDialog.StopTyping:
                    return;
                case InstantMessageDialog.InventoryOffered:
                    Feedback(ConsoleError.GOT_INVENTORY_OFFER, e.IM.Message.Replace(Environment.NewLine, " "));
                    return;
                case InstantMessageDialog.MessageBox:
                    Feedback(ConsoleError.GOT_SERVER_MESSAGE, e.IM.Message.Replace(Environment.NewLine, " "));
                    return;
                case InstantMessageDialog.RequestTeleport:
                    Feedback(ConsoleError.GOT_TELEPORT_LURE, e.IM.Message.Replace(Environment.NewLine, " "));
                    if (
                        !Configuration.MASTERS.Select(
                            master =>
                                string.Format(CultureInfo.InvariantCulture, "{0} {1}", master.FirstName, master.LastName))
                            .
                            Any(name => name.Equals(e.IM.FromAgentName, StringComparison.InvariantCultureIgnoreCase)))
                        return;
                    Feedback(ConsoleError.ACCEPTING_TELEPORT_LURE, e.IM.FromAgentName);
                    Client.Self.SignaledAnimations.ForEach(animation => Client.Self.AnimationStop(animation.Key, true));
                    if (Client.Self.Movement.SitOnGround || Client.Self.SittingOn != 0)
                    {
                        Client.Self.Stand();
                    }
                    Client.Self.TeleportLureRespond(e.IM.FromAgentID, e.IM.IMSessionID, true);
                    return;
                case InstantMessageDialog.GroupInvitation:
                    Feedback(ConsoleError.GOT_GROUP_INVITE, e.IM.Message.Replace(Environment.NewLine, " "));
                    if (
                        !Configuration.MASTERS.Select(
                            master =>
                                string.Format(CultureInfo.InvariantCulture, "{0}.{1}", master.FirstName, master.LastName))
                            .
                            Any(name => name.Equals(e.IM.FromAgentName, StringComparison.InvariantCultureIgnoreCase)))
                        return;
                    Feedback(ConsoleError.ACCEPTING_GROUP_INVITE, e.IM.FromAgentName);
                    Client.Self.GroupInviteRespond(e.IM.FromAgentID, e.IM.IMSessionID, true);
                    return;
                case InstantMessageDialog.GroupNotice:
                    Feedback(ConsoleError.GOT_GROUP_NOTICE, e.IM.Message.Replace(Environment.NewLine, " "));
                    // Send notices to notifications.
                    Parallel.ForEach(
                        GroupNotifications, n =>
                        {
                            // Check if the group has registered to receive region message notifications.
                            if ((n.NOTIFICATION_MASK & (int) Notifications.NOTIFICATION_GROUP_NOTICE) ==
                                0)
                            {
                                return;
                            }
                            new Thread(group_notice =>
                            {
                                // Grab the agent name sending the message.
                                AgentName agent = new AgentName().FromFullName(e.IM.FromAgentName);
                                Dictionary<string, string> notification = new Dictionary<string, string>
                                {
                                    {
                                        ScriptKeys.TYPE,
                                        GetEnumDescription(Notifications.NOTIFICATION_GROUP_NOTICE)
                                    },
                                    {ScriptKeys.FIRSTNAME, agent.FirstName},
                                    {ScriptKeys.LASTNAME, agent.LastName},
                                };
                                string[] noticeData = e.IM.Message.Split('|');
                                if (noticeData.Length > 0 && !string.IsNullOrEmpty(noticeData[0]))
                                {
                                    notification.Add(ScriptKeys.SUBJECT, noticeData[0]);
                                }
                                if (noticeData.Length > 1 && !string.IsNullOrEmpty(noticeData[1]))
                                {
                                    notification.Add(ScriptKeys.MESSAGE, noticeData[1]);
                                }
                                string error = wasPOST(n.URL, wasKeyValueEscape(notification));
                                if (!string.IsNullOrEmpty(error))
                                {
                                    Feedback(ConsoleError.NOTIFICATION_COULD_NOT_BE_SENT);
                                }
                            }).Start();
                        });
                    return;
                case InstantMessageDialog.SessionSend:
                case InstantMessageDialog.MessageFromAgent:
                    // Check if this is a group message.
                    // Note that this is a lousy way of doing it but libomv does not properly set the GroupIM field
                    // such that the only way to determine if we have a group message is to check that the UUID
                    // of the session is actually the UUID of a current group. Furthermore, what's worse is that 
                    // group mesages can appear both through SessionSend and from MessageFromAgent. Hence the problem.
                    OpenMetaverse.Group messageGroup = new OpenMetaverse.Group();
                    bool messageFromGroup = false;
                    ManualResetEvent CurrentGroupsEvent = new ManualResetEvent(false);
                    EventHandler<CurrentGroupsEventArgs> CurrentGroupsEventHandler = (sender, args) =>
                    {
                        messageFromGroup = args.Groups.Any(o => o.Key.Equals(e.IM.IMSessionID));
                        messageGroup = args.Groups.FirstOrDefault(o => o.Key.Equals(e.IM.IMSessionID)).Value;
                        CurrentGroupsEvent.Set();
                    };
                    Client.Groups.CurrentGroups += CurrentGroupsEventHandler;
                    Client.Groups.RequestCurrentGroups();
                    if (!CurrentGroupsEvent.WaitOne(Configuration.SERVICES_TIMEOUT))
                    {
                        Client.Groups.CurrentGroups -= CurrentGroupsEventHandler;
                        return;
                    }
                    Client.Groups.CurrentGroups -= CurrentGroupsEventHandler;
                    if (messageFromGroup)
                    {
                        Feedback(ConsoleError.GOT_GROUP_MESSAGE, e.IM.Message.Replace(Environment.NewLine, " "));
                        // Send group messages to notifications but limit by group workers.
                        Parallel.ForEach(
                            Configuration.GROUPS.Where(
                                g =>
                                    g.Name.Equals(messageGroup.Name, StringComparison.InvariantCulture)),
                            g =>
                            {
                                // Check if the group has registered to receive group message notifications.
                                if (
                                    GroupNotifications.Any(
                                        n =>
                                            n.GROUP.Equals(g.Name, StringComparison.InvariantCulture) &&
                                            (n.NOTIFICATION_MASK &
                                             (int) Notifications.NOTIFICATION_GROUP_MESSAGE) == 0))
                                {
                                    return;
                                }
                                // create the notification
                                new Thread(group_message =>
                                {
                                    // increment the group workers
                                    lock (GroupWorkers)
                                    {
                                        // Check if the worker count for a given group has been exceeded and bail if that is the case.
                                        if (
                                            GroupWorkers.Where(
                                                w => w.Key.Equals(g.Name, StringComparison.InvariantCulture))
                                                .
                                                Any(
                                                    w =>
                                                        Configuration.GROUPS.Where(
                                                            o =>
                                                                o.Name.Equals(g.Name, StringComparison.InvariantCulture))
                                                            .Any(o => w.Value >= o.Workers)))
                                        {
                                            Feedback(e.IM.FromAgentID, ConsoleError.WORKERS_EXCEEDED);
                                            return;
                                        }
                                        // Check if the group is added to the pool of workers and add it if not.
                                        if (!GroupWorkers.ContainsKey(g.Name))
                                            GroupWorkers.Add(g.Name, 0);
                                        GroupWorkers[g.Name]++;
                                    }
                                    string URL =
                                        GroupNotifications.FirstOrDefault(
                                            n => n.GROUP.Equals(g.Name, StringComparison.InvariantCulture)).URL;
                                    if (!string.IsNullOrEmpty(URL))
                                    {
                                        // Grab the agent name sending the message.
                                        AgentName agent = new AgentName().FromFullName(e.IM.FromAgentName);
                                        Dictionary<string, string> notification = new Dictionary<string, string>
                                        {
                                            {
                                                ScriptKeys.TYPE,
                                                GetEnumDescription(Notifications.NOTIFICATION_GROUP_MESSAGE)
                                            },
                                            {ScriptKeys.GROUP, g.Name},
                                            {ScriptKeys.MESSAGE, e.IM.Message},
                                            {ScriptKeys.FIRSTNAME, agent.FirstName},
                                            {ScriptKeys.LASTNAME, agent.LastName}
                                        };
                                        string error = wasPOST(URL, wasKeyValueEscape(notification));
                                        if (!string.IsNullOrEmpty(error))
                                        {
                                            Feedback(g.Name, ConsoleError.NOTIFICATION_COULD_NOT_BE_SENT);
                                        }
                                    }

                                    lock (GroupWorkers)
                                    {
                                        GroupWorkers[g.Name]--;
                                    }
                                }).Start();
                            });
                        // Log group messages
                        Parallel.ForEach(Configuration.GROUPS, g =>
                        {
                            if (!g.Name.Equals(messageGroup.Name, StringComparison.InvariantCulture))
                                return;
                            // Attempt to write to log file,
                            try
                            {
                                lock (FileLock)
                                {
                                    using (StreamWriter logWriter = File.AppendText(g.ChatLog))
                                    {
                                        logWriter.WriteLine(
                                            string.Format(CultureInfo.InvariantCulture, "[{0}] {1} : {2}",
                                                DateTime.Now.ToString("MM-dd-yyyy HH:mm",
                                                    DateTimeFormatInfo.InvariantInfo),
                                                e.IM.FromAgentName,
                                                e.IM.Message)
                                            );
                                        logWriter.Flush();
                                        logWriter.Close();
                                    }
                                }
                            }
                            catch (Exception ex)
                            {
                                // or fail and append the fail message.
                                Feedback(ConsoleError.COULD_NOT_WRITE_TO_GROUP_CHAT_LOGFILE, ex.Message);
                            }
                        });
                        return;
                    }
                    // Check if this is an instant message.
                    if (e.IM.ToAgentID.Equals(Client.Self.AgentID))
                    {
                        Feedback(ConsoleError.GOT_INSTANT_MESSAGE, e.IM.Message.Replace(Environment.NewLine, " "));
                        // If we have instant message notifications installed, send the messages to the configured groups.
                        Parallel.ForEach(
                            GroupNotifications.Where(
                                n =>
                                    HasCorradeNotification(n.GROUP,
                                        (int) Notifications.NOTIFICATION_INSTANT_MESSAGE)),
                            n =>
                            {
                                // Next, check if the group has registered to receive instant message notifications.
                                if ((n.NOTIFICATION_MASK & (int) Notifications.NOTIFICATION_INSTANT_MESSAGE) == 0)
                                {
                                    return;
                                }
                                new Thread(instant_message =>
                                {
                                    // Grab the agent name sending the message.
                                    AgentName agent = new AgentName().FromFullName(e.IM.FromAgentName);
                                    Dictionary<string, string> notification = new Dictionary<string, string>
                                    {
                                        {
                                            ScriptKeys.TYPE,
                                            GetEnumDescription(Notifications.NOTIFICATION_INSTANT_MESSAGE)
                                        },
                                        {ScriptKeys.MESSAGE, e.IM.Message},
                                        {ScriptKeys.FIRSTNAME, agent.FirstName},
                                        {ScriptKeys.LASTNAME, agent.LastName}
                                    };
                                    string error = wasPOST(n.URL, wasKeyValueEscape(notification));
                                    if (!string.IsNullOrEmpty(error))
                                    {
                                        Feedback(ConsoleError.NOTIFICATION_COULD_NOT_BE_SENT);
                                    }
                                }).Start();
                            });
                        return;
                    }
                    // Check if this is a region message.
                    if (e.IM.IMSessionID.Equals(UUID.Zero))
                    {
                        Feedback(ConsoleError.GOT_REGION_MESSAGE, e.IM.Message.Replace(Environment.NewLine, " "));
                        Parallel.ForEach(
                            GroupNotifications, n =>
                            {
                                // Check if the group has registered to receive region message notifications.
                                if ((n.NOTIFICATION_MASK & (int) Notifications.NOTIFICATION_REGION_MESSAGE) ==
                                    0)
                                {
                                    return;
                                }
                                new Thread(region_message =>
                                {
                                    // Grab the agent name sending the message.
                                    AgentName agent = new AgentName().FromFullName(e.IM.FromAgentName);
                                    Dictionary<string, string> notification = new Dictionary<string, string>
                                    {
                                        {
                                            ScriptKeys.TYPE,
                                            GetEnumDescription(Notifications.NOTIFICATION_REGION_MESSAGE)
                                        },
                                        {ScriptKeys.MESSAGE, e.IM.Message},
                                        {ScriptKeys.FIRSTNAME, agent.FirstName},
                                        {ScriptKeys.LASTNAME, agent.LastName}
                                    };
                                    string error = wasPOST(n.URL, wasKeyValueEscape(notification));
                                    if (!string.IsNullOrEmpty(error))
                                    {
                                        Feedback(ConsoleError.NOTIFICATION_COULD_NOT_BE_SENT);
                                    }
                                }).Start();
                            });
                        return;
                    }
                    break;
            }
            // Now we can start processing commands.
            // Get group and password.
            string group = Uri.UnescapeDataString(wasKeyValueGet(ScriptKeys.GROUP, e.IM.Message));
            string password = Uri.UnescapeDataString(wasKeyValueGet(ScriptKeys.PASSWORD, e.IM.Message));
            // Check whether group and password are valid.
            if (!Authenticate(group, password))
            {
                Feedback(e.IM.FromAgentID, ConsoleError.ACCESS_DENIED);
                return;
            }
            string fromAgentName = string.Empty;
            if (!AgentUUIDToName(e.IM.FromAgentID, Configuration.SERVICES_TIMEOUT, ref fromAgentName))
            {
                Feedback(e.IM.FromAgentID, ConsoleError.AGENT_NOT_FOUND);
                return;
            }
            Feedback(string.Format(CultureInfo.InvariantCulture, "{0} ({1}) : {2}", fromAgentName,
                e.IM.IMSessionID.ToString(),
                e.IM.Message));
            lock (GroupWorkersLock)
            {
                // Bail if no workers are set for the requested group.
                if (!Configuration.GROUPS.Any(g => g.Name.Equals(group, StringComparison.InvariantCulture)))
                {
                    Feedback(e.IM.FromAgentName, ConsoleError.NO_WORKERS_SET_FOR_GROUP);
                    return;
                }
                // Check if the worker count for a given group has been exceeded and bail if that is the case.
                if (
                    GroupWorkers.Where(w => w.Key.Equals(group, StringComparison.InvariantCulture)).
                        Any(
                            w =>
                                Configuration.GROUPS.Where(g => g.Name.Equals(group, StringComparison.InvariantCulture))
                                    .Any(g => w.Value >= g.Workers)))
                {
                    Feedback(e.IM.FromAgentID, ConsoleError.WORKERS_EXCEEDED);
                    return;
                }
                // Check if the group is added to the pool of workers and add it if not.
                if (!GroupWorkers.ContainsKey(group))
                    GroupWorkers.Add(group, 0);
                GroupWorkers[group]++;
                WorkerDelgate workerDelegate = ProcessCommand;
                workerDelegate.BeginInvoke(e.IM.Message, ProcessCommandReturn, workerDelegate);
            }
        }

        /// <summary>
        ///     This function is responsible for processing messages sent to corrade.
        /// </summary>
        /// <param name="message">the message</param>
        /// <returns>a command, a group, a success status (true or false) and additional data depending on the command</returns>
        private static string ProcessCommand(string message)
        {
            Dictionary<string, string> result = new Dictionary<string, string>();
            string command = Uri.UnescapeDataString(wasKeyValueGet(ScriptKeys.COMMAND, message));
            if (!string.IsNullOrEmpty(command))
            {
                result.Add(ScriptKeys.COMMAND, command);
            }
            string group = Uri.UnescapeDataString(wasKeyValueGet(ScriptKeys.GROUP, message));
            if (!string.IsNullOrEmpty(group))
            {
                result.Add(ScriptKeys.GROUP, group);
            }

            System.Action execute;

            switch (command)
            {
                case ScriptKeys.JOIN:
                    execute = () =>
                    {
                        UUID groupUUID =
                            Configuration.GROUPS.FirstOrDefault(
                                g => g.Name.Equals(group, StringComparison.InvariantCulture)).UUID;
                        if (groupUUID.Equals(UUID.Zero) &&
                            !GroupNameToUUID(group, Configuration.SERVICES_TIMEOUT, ref groupUUID))
                        {
                            throw new Exception(GetEnumDescription(ScriptError.GROUP_NOT_FOUND));
                        }
                        if (AgentInGroup(Client.Self.AgentID, groupUUID, Configuration.SERVICES_TIMEOUT))
                        {
                            throw new Exception(GetEnumDescription(ScriptError.ALREADY_IN_GROUP));
                        }
                        ManualResetEvent GroupJoinedReplyEvent = new ManualResetEvent(false);
                        EventHandler<GroupOperationEventArgs> GroupOperationEventHandler =
                            (sender, args) => GroupJoinedReplyEvent.Set();
                        Client.Groups.GroupJoinedReply += GroupOperationEventHandler;
                        Client.Groups.RequestJoinGroup(groupUUID);
                        if (!GroupJoinedReplyEvent.WaitOne(Configuration.SERVICES_TIMEOUT, false))
                        {
                            Client.Groups.GroupJoinedReply -= GroupOperationEventHandler;
                            throw new Exception(GetEnumDescription(ScriptError.COULD_NOT_JOIN_GROUP));
                        }
                        Client.Groups.GroupJoinedReply -= GroupOperationEventHandler;
                        if (!AgentInGroup(Client.Self.AgentID, groupUUID, Configuration.SERVICES_TIMEOUT))
                        {
                            throw new Exception(GetEnumDescription(ScriptError.COULD_NOT_JOIN_GROUP));
                        }
                    };
                    break;
                case ScriptKeys.CREATEGROUP:
                    execute = () =>
                    {
                        if (
                            !HasCorradePermission(group, (int) Permissions.PERMISSION_ECONOMY))
                        {
                            throw new Exception(GetEnumDescription(ScriptError.NO_CORRADE_PERMISSIONS));
                        }
                        ManualResetEvent MoneyBalanceEvent = new ManualResetEvent(false);
                        EventHandler<MoneyBalanceReplyEventArgs> MoneyBalanceEventHandler =
                            (sender, args) => MoneyBalanceEvent.Set();
                        Client.Self.MoneyBalanceReply += MoneyBalanceEventHandler;
                        Client.Self.RequestBalance();
                        if (!MoneyBalanceEvent.WaitOne(Configuration.SERVICES_TIMEOUT, false))
                        {
                            Client.Self.MoneyBalanceReply -= MoneyBalanceEventHandler;
                            throw new Exception(GetEnumDescription(ScriptError.TIMEOUT_WAITING_FOR_BALANCE));
                        }
                        Client.Self.MoneyBalanceReply -= MoneyBalanceEventHandler;
                        if (Client.Self.Balance < Configuration.GROUP_CREATE_FEE)
                        {
                            throw new Exception(GetEnumDescription(ScriptError.INSUFFICIENT_FUNDS));
                        }
                        OpenMetaverse.Group updateGroup = new OpenMetaverse.Group
                        {
                            Name = group
                        };
                        string fields = Uri.UnescapeDataString(wasKeyValueGet(ScriptKeys.DATA, message));
                        wasCSVToStructure(fields, ref updateGroup);
                        bool succeeded = false;
                        ManualResetEvent GroupCreatedReplyEvent = new ManualResetEvent(false);
                        EventHandler<GroupCreatedReplyEventArgs> GroupCreatedEventHandler = (sender, args) =>
                        {
                            if (args.Success)
                            {
                                succeeded = true;
                            }
                            GroupCreatedReplyEvent.Set();
                        };
                        Client.Groups.GroupCreatedReply += GroupCreatedEventHandler;
                        Client.Groups.RequestCreateGroup(updateGroup);
                        if (!GroupCreatedReplyEvent.WaitOne(Configuration.SERVICES_TIMEOUT, false))
                        {
                            Client.Groups.GroupCreatedReply -= GroupCreatedEventHandler;
                            throw new Exception(GetEnumDescription(ScriptError.COULD_NOT_CREATE_GROUP));
                        }
                        Client.Groups.GroupCreatedReply -= GroupCreatedEventHandler;
                        if (!succeeded)
                        {
                            throw new Exception(GetEnumDescription(ScriptError.COULD_NOT_CREATE_GROUP));
                        }
                    };
                    break;
                case ScriptKeys.INVITE:
                    execute = () =>
                    {
                        UUID groupUUID =
                            Configuration.GROUPS.FirstOrDefault(
                                g => g.Name.Equals(group, StringComparison.InvariantCulture)).UUID;
                        if (groupUUID.Equals(UUID.Zero) &&
                            !GroupNameToUUID(group, Configuration.SERVICES_TIMEOUT, ref groupUUID))
                        {
                            throw new Exception(GetEnumDescription(ScriptError.GROUP_NOT_FOUND));
                        }
                        if (
                            !HasGroupPowers(Client.Self.AgentID, groupUUID, GroupPowers.Invite,
                                Configuration.SERVICES_TIMEOUT))
                        {
                            throw new Exception(GetEnumDescription(ScriptError.NO_GROUP_POWER_FOR_COMMAND));
                        }
                        UUID agentUUID = UUID.Zero;
                        if (
                            !AgentNameToUUID(Uri.UnescapeDataString(wasKeyValueGet(ScriptKeys.FIRSTNAME, message)),
                                Uri.UnescapeDataString(wasKeyValueGet(ScriptKeys.LASTNAME, message)),
                                Configuration.SERVICES_TIMEOUT, ref agentUUID))
                        {
                            throw new Exception(GetEnumDescription(ScriptError.AGENT_NOT_FOUND));
                        }
                        if (AgentInGroup(agentUUID, groupUUID, Configuration.SERVICES_TIMEOUT))
                        {
                            throw new Exception(GetEnumDescription(ScriptError.ALREADY_IN_GROUP));
                        }
                        // role UUID is optional
                        HashSet<UUID> roleUUIDs = new HashSet<UUID> {UUID.Zero};
                        string role = Uri.UnescapeDataString(wasKeyValueGet(ScriptKeys.ROLE, message));
                        if (!string.IsNullOrEmpty(role))
                        {
                            object LockObject = new object();
                            Parallel.ForEach(
                                role.Split(new[] {LINDEN_CONSTANTS.LSL.CSV_DELIMITER},
                                    StringSplitOptions.RemoveEmptyEntries), pair =>
                                    {
                                        UUID inviteRoleUUID = UUID.Zero;
                                        if (!RoleNameToRoleUUID(pair.Trim(), groupUUID,
                                            Configuration.SERVICES_TIMEOUT, ref inviteRoleUUID))
                                        {
                                            return;
                                        }
                                        if (!inviteRoleUUID.Equals(UUID.Zero))
                                        {
                                            lock (LockObject)
                                            {
                                                roleUUIDs.Add(inviteRoleUUID);
                                            }
                                        }
                                    });
                        }
                        Client.Groups.Invite(groupUUID, roleUUIDs.ToList(), agentUUID);
                    };
                    break;
                case ScriptKeys.EJECT:
                    execute = () =>
                    {
                        UUID groupUUID =
                            Configuration.GROUPS.FirstOrDefault(
                                g => g.Name.Equals(group, StringComparison.InvariantCulture)).UUID;
                        if (groupUUID.Equals(UUID.Zero) &&
                            !GroupNameToUUID(group, Configuration.SERVICES_TIMEOUT, ref groupUUID))
                        {
                            throw new Exception(GetEnumDescription(ScriptError.GROUP_NOT_FOUND));
                        }
                        if (
                            !HasGroupPowers(Client.Self.AgentID, groupUUID, GroupPowers.Eject,
                                Configuration.SERVICES_TIMEOUT))
                        {
                            throw new Exception(GetEnumDescription(ScriptError.NO_GROUP_POWER_FOR_COMMAND));
                        }
                        UUID agentUUID = UUID.Zero;
                        if (
                            !AgentNameToUUID(Uri.UnescapeDataString(wasKeyValueGet(ScriptKeys.FIRSTNAME, message)),
                                Uri.UnescapeDataString(wasKeyValueGet(ScriptKeys.LASTNAME, message)),
                                Configuration.SERVICES_TIMEOUT, ref agentUUID))
                        {
                            throw new Exception(GetEnumDescription(ScriptError.AGENT_NOT_FOUND));
                        }
                        if (!AgentInGroup(agentUUID, groupUUID, Configuration.SERVICES_TIMEOUT))
                        {
                            throw new Exception(GetEnumDescription(ScriptError.NOT_IN_GROUP));
                        }
                        OpenMetaverse.Group updateGroup = new OpenMetaverse.Group();
                        if (!RequestGroup(groupUUID, Configuration.SERVICES_TIMEOUT, ref updateGroup))
                        {
                            throw new Exception(GetEnumDescription(ScriptError.GROUP_NOT_FOUND));
                        }
                        ManualResetEvent GroupRoleMembersReplyEvent = new ManualResetEvent(false);
                        bool succeeded = false;
                        EventHandler<GroupRolesMembersReplyEventArgs> GroupRoleMembersEventHandler = (sender, args) =>
                        {
                            if (args.RolesMembers.Any(
                                o => o.Key.Equals(updateGroup.OwnerRole) && o.Value.Equals(agentUUID)))
                            {
                                throw new Exception(GetEnumDescription(ScriptError.CANNOT_EJECT_OWNERS));
                            }
                            foreach (
                                KeyValuePair<UUID, UUID> arg in
                                    args.RolesMembers.Where(arg => arg.Value.Equals(agentUUID)))
                                if (!agentUUID.Equals(UUID.Zero))
                                    Client.Groups.RemoveFromRole(groupUUID, arg.Key,
                                        agentUUID);
                            succeeded = true;
                            GroupRoleMembersReplyEvent.Set();
                        };
                        Client.Groups.GroupRoleMembersReply += GroupRoleMembersEventHandler;
                        Client.Groups.RequestGroupRolesMembers(groupUUID);
                        if (!GroupRoleMembersReplyEvent.WaitOne(Configuration.SERVICES_TIMEOUT, false))
                        {
                            Client.Groups.GroupRoleMembersReply -= GroupRoleMembersEventHandler;
                            throw new Exception(GetEnumDescription(ScriptError.COULD_NOT_EJECT_AGENT));
                        }
                        Client.Groups.GroupRoleMembersReply -= GroupRoleMembersEventHandler;
                        if (!succeeded)
                        {
                            throw new Exception(GetEnumDescription(ScriptError.COULD_NOT_DEMOTE_AGENT));
                        }
                        ManualResetEvent GroupEjectEvent = new ManualResetEvent(false);
                        succeeded = false;
                        EventHandler<GroupOperationEventArgs> GroupOperationEventHandler = (sender, args) =>
                        {
                            if (args.Success)
                            {
                                succeeded = true;
                            }
                            GroupEjectEvent.Set();
                        };
                        Client.Groups.GroupMemberEjected += GroupOperationEventHandler;
                        Client.Groups.EjectUser(groupUUID, agentUUID);
                        if (!GroupEjectEvent.WaitOne(Configuration.SERVICES_TIMEOUT, false))
                        {
                            Client.Groups.GroupMemberEjected -= GroupOperationEventHandler;
                            throw new Exception(GetEnumDescription(ScriptError.COULD_NOT_EJECT_AGENT));
                        }
                        Client.Groups.GroupMemberEjected -= GroupOperationEventHandler;
                        if (!succeeded)
                        {
                            throw new Exception(GetEnumDescription(ScriptError.COULD_NOT_EJECT_AGENT));
                        }
                    };
                    break;
                case ScriptKeys.UPDATEGROUPDATA:
                    execute = () =>
                    {
                        UUID groupUUID =
                            Configuration.GROUPS.FirstOrDefault(
                                g => g.Name.Equals(group, StringComparison.InvariantCulture)).UUID;
                        if (groupUUID.Equals(UUID.Zero) &&
                            !GroupNameToUUID(group, Configuration.SERVICES_TIMEOUT, ref groupUUID))
                        {
                            throw new Exception(GetEnumDescription(ScriptError.GROUP_NOT_FOUND));
                        }
                        if (!AgentInGroup(Client.Self.AgentID, groupUUID, Configuration.SERVICES_TIMEOUT))
                        {
                            throw new Exception(GetEnumDescription(ScriptError.NOT_IN_GROUP));
                        }
                        if (
                            !HasGroupPowers(Client.Self.AgentID, groupUUID, GroupPowers.ChangeIdentity,
                                Configuration.SERVICES_TIMEOUT))
                        {
                            throw new Exception(GetEnumDescription(ScriptError.NO_GROUP_POWER_FOR_COMMAND));
                        }
                        OpenMetaverse.Group updateGroup = new OpenMetaverse.Group();
                        if (!RequestGroup(groupUUID, Configuration.SERVICES_TIMEOUT, ref updateGroup))
                        {
                            throw new Exception(GetEnumDescription(ScriptError.GROUP_NOT_FOUND));
                        }
                        string fields = Uri.UnescapeDataString(wasKeyValueGet(ScriptKeys.DATA, message));
                        wasCSVToStructure(fields, ref updateGroup);
                        Client.Groups.UpdateGroup(groupUUID, updateGroup);
                    };
                    break;
                case ScriptKeys.LEAVE:
                    execute = () =>
                    {
                        UUID groupUUID =
                            Configuration.GROUPS.FirstOrDefault(
                                g => g.Name.Equals(group, StringComparison.InvariantCulture)).UUID;
                        if (groupUUID.Equals(UUID.Zero) &&
                            !GroupNameToUUID(group, Configuration.SERVICES_TIMEOUT, ref groupUUID))
                        {
                            throw new Exception(GetEnumDescription(ScriptError.GROUP_NOT_FOUND));
                        }
                        if (!AgentInGroup(Client.Self.AgentID, groupUUID, Configuration.SERVICES_TIMEOUT))
                        {
                            throw new Exception(GetEnumDescription(ScriptError.NOT_IN_GROUP));
                        }
                        ManualResetEvent GroupLeaveReplyEvent = new ManualResetEvent(false);
                        bool succeeded = false;
                        EventHandler<GroupOperationEventArgs> GroupOperationEventHandler = (sender, args) =>
                        {
                            if (args.Success)
                            {
                                succeeded = true;
                            }
                            GroupLeaveReplyEvent.Set();
                        };
                        Client.Groups.GroupLeaveReply += GroupOperationEventHandler;
                        Client.Groups.LeaveGroup(groupUUID);
                        if (!GroupLeaveReplyEvent.WaitOne(Configuration.SERVICES_TIMEOUT, false))
                        {
                            Client.Groups.GroupLeaveReply -= GroupOperationEventHandler;
                            throw new Exception(GetEnumDescription(ScriptError.COULD_NOT_LEAVE_GROUP));
                        }
                        Client.Groups.GroupLeaveReply -= GroupOperationEventHandler;
                        if (!succeeded)
                        {
                            throw new Exception(GetEnumDescription(ScriptError.COULD_NOT_LEAVE_GROUP));
                        }
                    };
                    break;
                case ScriptKeys.CREATEROLE:
                    execute = () =>
                    {
                        UUID groupUUID =
                            Configuration.GROUPS.FirstOrDefault(
                                g => g.Name.Equals(group, StringComparison.InvariantCulture)).UUID;
                        if (groupUUID.Equals(UUID.Zero) &&
                            !GroupNameToUUID(group, Configuration.SERVICES_TIMEOUT, ref groupUUID))
                        {
                            throw new Exception(GetEnumDescription(ScriptError.GROUP_NOT_FOUND));
                        }
                        if (!AgentInGroup(Client.Self.AgentID, groupUUID, Configuration.SERVICES_TIMEOUT))
                        {
                            throw new Exception(GetEnumDescription(ScriptError.NOT_IN_GROUP));
                        }
                        if (
                            !HasGroupPowers(Client.Self.AgentID, groupUUID, GroupPowers.CreateRole,
                                Configuration.SERVICES_TIMEOUT))
                        {
                            throw new Exception(GetEnumDescription(ScriptError.NO_GROUP_POWER_FOR_COMMAND));
                        }
                        ManualResetEvent GroupRoleDataReplyEvent = new ManualResetEvent(false);
                        Dictionary<UUID, string> roleData = new Dictionary<UUID, string>();
                        EventHandler<GroupRolesDataReplyEventArgs> GroupRolesDataEventHandler = (sender, args) =>
                        {
                            object LockObject = new object();
                            Parallel.ForEach(args.Roles, pair =>
                            {
                                lock (LockObject)
                                {
                                    roleData.Add(pair.Value.ID, pair.Value.Name);
                                }
                            });
                            GroupRoleDataReplyEvent.Set();
                        };
                        Client.Groups.GroupRoleDataReply += GroupRolesDataEventHandler;
                        Client.Groups.RequestGroupRoles(groupUUID);
                        if (!GroupRoleDataReplyEvent.WaitOne(Configuration.SERVICES_TIMEOUT, false))
                        {
                            Client.Groups.GroupRoleDataReply -= GroupRolesDataEventHandler;
                            throw new Exception(GetEnumDescription(ScriptError.TIMEOUT_GETTING_GROUP_ROLES));
                        }
                        Client.Groups.GroupRoleDataReply -= GroupRolesDataEventHandler;
                        if (roleData.Count >= LINDEN_CONSTANTS.GROUPS.MAXIMUM_NUMBER_OF_ROLES)
                        {
                            throw new Exception(GetEnumDescription(ScriptError.MAXIMUM_NUMBER_OF_ROLES_EXCEEDED));
                        }
                        string role = Uri.UnescapeDataString(wasKeyValueGet(ScriptKeys.ROLE, message));
                        if (string.IsNullOrEmpty(role))
                        {
                            throw new Exception(GetEnumDescription(ScriptError.NO_ROLE_NAME_SPECIFIED));
                        }
                        ulong powers = 0;
                        Parallel.ForEach(
                            Uri.UnescapeDataString(wasKeyValueGet(ScriptKeys.POWERS, message))
                                .Split(new[] {LINDEN_CONSTANTS.LSL.CSV_DELIMITER}, StringSplitOptions.RemoveEmptyEntries),
                            data =>
                                Parallel.ForEach(
                                    typeof (GroupPowers).GetFields(BindingFlags.Public | BindingFlags.Static),
                                    info =>
                                    {
                                        if (info.Name.Equals(data.Trim(), StringComparison.InvariantCulture))
                                        {
                                            powers |= ((ulong) info.GetValue(null));
                                        }
                                    }));
                        Client.Groups.CreateRole(groupUUID, new GroupRole
                        {
                            Name = role,
                            Description = Uri.UnescapeDataString(wasKeyValueGet(ScriptKeys.DESCRIPTION, message)),
                            GroupID = groupUUID,
                            ID = UUID.Random(),
                            Powers = (GroupPowers) powers,
                            Title = Uri.UnescapeDataString(wasKeyValueGet(ScriptKeys.TITLE, message))
                        });
                        UUID roleUUID = UUID.Zero;
                        if (
                            !RoleNameToRoleUUID(role, groupUUID,
                                Configuration.SERVICES_TIMEOUT, ref roleUUID))
                        {
                            throw new Exception(GetEnumDescription(ScriptError.COULD_NOT_CREATE_ROLE));
                        }
                        if (roleUUID.Equals(UUID.Zero))
                        {
                            throw new Exception(GetEnumDescription(ScriptError.COULD_NOT_CREATE_ROLE));
                        }
                    };
                    break;
                case ScriptKeys.GETROLES:
                    execute = () =>
                    {
                        UUID groupUUID =
                            Configuration.GROUPS.FirstOrDefault(
                                g => g.Name.Equals(group, StringComparison.InvariantCulture)).UUID;
                        if (groupUUID.Equals(UUID.Zero) &&
                            !GroupNameToUUID(group, Configuration.SERVICES_TIMEOUT, ref groupUUID))
                        {
                            throw new Exception(GetEnumDescription(ScriptError.GROUP_NOT_FOUND));
                        }
                        if (!AgentInGroup(Client.Self.AgentID, groupUUID, Configuration.SERVICES_TIMEOUT))
                        {
                            throw new Exception(GetEnumDescription(ScriptError.NOT_IN_GROUP));
                        }
                        ManualResetEvent GroupRoleDataReplyEvent = new ManualResetEvent(false);
                        List<string> roleData = new List<string>();
                        EventHandler<GroupRolesDataReplyEventArgs> GroupRolesDataEventHandler = (sender, args) =>
                        {
                            object LockObject = new object();
                            Parallel.ForEach(args.Roles, pair =>
                            {
                                lock (LockObject)
                                {
                                    roleData.Add(pair.Value.Name);
                                    roleData.Add(pair.Value.Title);
                                    roleData.Add(pair.Value.ID.ToString());
                                }
                            });
                            GroupRoleDataReplyEvent.Set();
                        };
                        Client.Groups.GroupRoleDataReply += GroupRolesDataEventHandler;
                        Client.Groups.RequestGroupRoles(groupUUID);
                        if (!GroupRoleDataReplyEvent.WaitOne(Configuration.SERVICES_TIMEOUT, false))
                        {
                            Client.Groups.GroupRoleDataReply -= GroupRolesDataEventHandler;
                            throw new Exception(GetEnumDescription(ScriptError.TIMEOUT_GETTING_GROUP_ROLES));
                        }
                        Client.Groups.GroupRoleDataReply -= GroupRolesDataEventHandler;
                        if (roleData.Count == 0)
                        {
                            throw new Exception(GetEnumDescription(ScriptError.COULD_NOT_GET_ROLES));
                        }
                        result.Add(ResultKeys.ROLES,
                            string.Join(LINDEN_CONSTANTS.LSL.CSV_DELIMITER, roleData.ToArray()));
                    };
                    break;
                case ScriptKeys.GETROLESMEMBERS:
                    execute = () =>
                    {
                        UUID groupUUID =
                            Configuration.GROUPS.FirstOrDefault(
                                g => g.Name.Equals(group, StringComparison.InvariantCulture)).UUID;
                        if (groupUUID.Equals(UUID.Zero) &&
                            !GroupNameToUUID(group, Configuration.SERVICES_TIMEOUT, ref groupUUID))
                        {
                            throw new Exception(GetEnumDescription(ScriptError.GROUP_NOT_FOUND));
                        }
                        if (
                            !AgentInGroup(Client.Self.AgentID, groupUUID,
                                Configuration.SERVICES_TIMEOUT))
                        {
                            throw new Exception(GetEnumDescription(ScriptError.NOT_IN_GROUP));
                        }
                        ManualResetEvent GroupRoleMembersReplyEvent = new ManualResetEvent(false);
                        List<string> roleData = new List<string>();
                        EventHandler<GroupRolesMembersReplyEventArgs> GroupRolesMembersEventHandler = (sender, args) =>
                        {
                            object LockObject = new object();
                            Parallel.ForEach(args.RolesMembers, pair =>
                            {
                                string roleName = string.Empty;
                                if (
                                    !RoleUUIDToName(pair.Key, groupUUID,
                                        Configuration.SERVICES_TIMEOUT,
                                        ref roleName))
                                    return;
                                string agentName = string.Empty;
                                if (!AgentUUIDToName(pair.Value, Configuration.SERVICES_TIMEOUT, ref agentName))
                                    return;
                                lock (LockObject)
                                {
                                    roleData.Add(agentName);
                                    roleData.Add(roleName);
                                }
                            });
                            GroupRoleMembersReplyEvent.Set();
                        };
                        Client.Groups.GroupRoleMembersReply += GroupRolesMembersEventHandler;
                        Client.Groups.RequestGroupRolesMembers(groupUUID);
                        if (!GroupRoleMembersReplyEvent.WaitOne(Configuration.SERVICES_TIMEOUT, false))
                        {
                            Client.Groups.GroupRoleMembersReply -= GroupRolesMembersEventHandler;
                            throw new Exception(GetEnumDescription(ScriptError.TIMEOUT_GETING_GROUP_ROLES_MEMBERS));
                        }
                        Client.Groups.GroupRoleMembersReply -= GroupRolesMembersEventHandler;
                        if (roleData.Count == 0)
                        {
                            throw new Exception(GetEnumDescription(ScriptError.COULD_NOT_GET_GROUP_ROLES_MEMBERS));
                        }
                        result.Add(ResultKeys.MEMBERS,
                            string.Join(LINDEN_CONSTANTS.LSL.CSV_DELIMITER, roleData.ToArray()));
                    };
                    break;
                case ScriptKeys.GETROLEPOWERS:
                    execute = () =>
                    {
                        UUID groupUUID =
                            Configuration.GROUPS.FirstOrDefault(
                                g => g.Name.Equals(group, StringComparison.InvariantCulture)).UUID;
                        if (groupUUID.Equals(UUID.Zero) &&
                            !GroupNameToUUID(group, Configuration.SERVICES_TIMEOUT, ref groupUUID))
                        {
                            throw new Exception(GetEnumDescription(ScriptError.GROUP_NOT_FOUND));
                        }
                        if (
                            !AgentInGroup(Client.Self.AgentID, groupUUID,
                                Configuration.SERVICES_TIMEOUT))
                        {
                            throw new Exception(GetEnumDescription(ScriptError.NOT_IN_GROUP));
                        }
                        string role = Uri.UnescapeDataString(wasKeyValueGet(ScriptKeys.ROLE, message));
                        UUID roleUUID;
                        if (!UUID.TryParse(role, out roleUUID))
                        {
                            if (
                                !RoleNameToRoleUUID(role, groupUUID,
                                    Configuration.SERVICES_TIMEOUT, ref roleUUID))
                            {
                                throw new Exception(GetEnumDescription(ScriptError.ROLE_NOT_FOUND));
                            }
                        }
                        ManualResetEvent GroupRoleDataReplyEvent = new ManualResetEvent(false);
                        List<string> roleData = new List<string>();
                        EventHandler<GroupRolesDataReplyEventArgs> GroupRoleDataEventHandler = (sender, args) =>
                        {
                            object LockObject = new object();
                            Parallel.ForEach(args.Roles.Values, pair =>
                            {
                                if (pair.ID.Equals(roleUUID))
                                {
                                    Parallel.ForEach(
                                        typeof (GroupPowers).GetFields(BindingFlags.Public | BindingFlags.Static),
                                        info =>
                                        {
                                            if (((ulong) info.GetValue(null) & (ulong) pair.Powers) == 0)
                                                return;
                                            lock (LockObject)
                                            {
                                                roleData.Add(info.Name);
                                            }
                                        });
                                }
                            });
                            GroupRoleDataReplyEvent.Set();
                        };
                        Client.Groups.GroupRoleDataReply += GroupRoleDataEventHandler;
                        Client.Groups.RequestGroupRoles(groupUUID);
                        if (!GroupRoleDataReplyEvent.WaitOne(Configuration.SERVICES_TIMEOUT, false))
                        {
                            Client.Groups.GroupRoleDataReply -= GroupRoleDataEventHandler;
                            throw new Exception(GetEnumDescription(ScriptError.TIMEOUT_GETTING_ROLE_POWERS));
                        }
                        Client.Groups.GroupRoleDataReply -= GroupRoleDataEventHandler;
                        if (roleData.Count == 0)
                        {
                            throw new Exception(GetEnumDescription(ScriptError.NO_GROUP_POWERS));
                        }
                        result.Add(ResultKeys.POWERS,
                            string.Join(LINDEN_CONSTANTS.LSL.CSV_DELIMITER, roleData.ToArray()));
                    };
                    break;
                case ScriptKeys.DELETEROLE:
                    execute = () =>
                    {
                        UUID groupUUID =
                            Configuration.GROUPS.FirstOrDefault(
                                g => g.Name.Equals(group, StringComparison.InvariantCulture)).UUID;
                        if (groupUUID.Equals(UUID.Zero) &&
                            !GroupNameToUUID(group, Configuration.SERVICES_TIMEOUT, ref groupUUID))
                        {
                            throw new Exception(GetEnumDescription(ScriptError.GROUP_NOT_FOUND));
                        }
                        if (!AgentInGroup(Client.Self.AgentID, groupUUID, Configuration.SERVICES_TIMEOUT))
                        {
                            throw new Exception(GetEnumDescription(ScriptError.NOT_IN_GROUP));
                        }
                        if (
                            !HasGroupPowers(Client.Self.AgentID, groupUUID, GroupPowers.DeleteRole,
                                Configuration.SERVICES_TIMEOUT))
                        {
                            throw new Exception(GetEnumDescription(ScriptError.NO_GROUP_POWER_FOR_COMMAND));
                        }
                        string role = Uri.UnescapeDataString(wasKeyValueGet(ScriptKeys.ROLE, message));
                        UUID roleUUID;
                        if (!UUID.TryParse(role, out roleUUID))
                        {
                            if (
                                !RoleNameToRoleUUID(role, groupUUID,
                                    Configuration.SERVICES_TIMEOUT, ref roleUUID))
                            {
                                throw new Exception(GetEnumDescription(ScriptError.ROLE_NOT_FOUND));
                            }
                        }
                        if (roleUUID.Equals(UUID.Zero))
                        {
                            throw new Exception(GetEnumDescription(ScriptError.CANNOT_DELETE_THE_EVERYONE_ROLE));
                        }
                        OpenMetaverse.Group updateGroup = new OpenMetaverse.Group();
                        if (!RequestGroup(groupUUID, Configuration.SERVICES_TIMEOUT, ref updateGroup))
                        {
                            throw new Exception(GetEnumDescription(ScriptError.GROUP_NOT_FOUND));
                        }
                        if (updateGroup.OwnerRole.Equals(roleUUID))
                        {
                            throw new Exception(GetEnumDescription(ScriptError.CANNOT_REMOVE_OWNER_ROLE));
                        }
                        // remove member from role
                        ManualResetEvent GroupRoleMembersReplyEvent = new ManualResetEvent(false);
                        EventHandler<GroupRolesMembersReplyEventArgs> GroupRolesMembersEventHandler = (sender, args) =>
                        {
                            Parallel.ForEach(args.RolesMembers,
                                pair =>
                                {
                                    if (!pair.Key.Equals(roleUUID)) return;
                                    Client.Groups.RemoveFromRole(groupUUID, roleUUID, pair.Value);
                                });
                            GroupRoleMembersReplyEvent.Set();
                        };
                        Client.Groups.GroupRoleMembersReply += GroupRolesMembersEventHandler;
                        Client.Groups.RequestGroupRolesMembers(groupUUID);
                        if (!GroupRoleMembersReplyEvent.WaitOne(Configuration.SERVICES_TIMEOUT, false))
                        {
                            Client.Groups.GroupRoleMembersReply -= GroupRolesMembersEventHandler;
                            throw new Exception(GetEnumDescription(ScriptError.COULD_NOT_EJECT_AGENT));
                        }
                        Client.Groups.GroupRoleMembersReply -= GroupRolesMembersEventHandler;
                        Client.Groups.DeleteRole(groupUUID, roleUUID);
                    };
                    break;
                case ScriptKeys.ADDTOROLE:
                    execute = () =>
                    {
                        UUID groupUUID =
                            Configuration.GROUPS.FirstOrDefault(
                                g => g.Name.Equals(group, StringComparison.InvariantCulture)).UUID;
                        if (groupUUID.Equals(UUID.Zero) &&
                            !GroupNameToUUID(group, Configuration.SERVICES_TIMEOUT, ref groupUUID))
                        {
                            throw new Exception(GetEnumDescription(ScriptError.GROUP_NOT_FOUND));
                        }
                        if (!AgentInGroup(Client.Self.AgentID, groupUUID, Configuration.SERVICES_TIMEOUT))
                        {
                            throw new Exception(GetEnumDescription(ScriptError.NOT_IN_GROUP));
                        }
                        if (
                            !HasGroupPowers(Client.Self.AgentID, groupUUID, GroupPowers.AssignMember,
                                Configuration.SERVICES_TIMEOUT))
                        {
                            throw new Exception(GetEnumDescription(ScriptError.NO_GROUP_POWER_FOR_COMMAND));
                        }
                        UUID agentUUID = UUID.Zero;
                        if (
                            !AgentNameToUUID(Uri.UnescapeDataString(wasKeyValueGet(ScriptKeys.FIRSTNAME, message)),
                                Uri.UnescapeDataString(wasKeyValueGet(ScriptKeys.LASTNAME, message)),
                                Configuration.SERVICES_TIMEOUT, ref agentUUID))
                        {
                            throw new Exception(GetEnumDescription(ScriptError.AGENT_NOT_FOUND));
                        }
                        string role = Uri.UnescapeDataString(wasKeyValueGet(ScriptKeys.ROLE, message));
                        UUID roleUUID;
                        if (!UUID.TryParse(role, out roleUUID))
                        {
                            if (
                                !RoleNameToRoleUUID(role, groupUUID,
                                    Configuration.SERVICES_TIMEOUT, ref roleUUID))
                            {
                                throw new Exception(GetEnumDescription(ScriptError.ROLE_NOT_FOUND));
                            }
                        }
                        if (roleUUID.Equals(UUID.Zero))
                        {
                            throw new Exception(
                                GetEnumDescription(ScriptError.GROUP_MEMBERS_ARE_BY_DEFAULT_IN_THE_EVERYONE_ROLE));
                        }
                        Client.Groups.AddToRole(groupUUID, roleUUID, agentUUID);
                    };
                    break;
                case ScriptKeys.DELETEFROMROLE:
                    execute = () =>
                    {
                        UUID groupUUID =
                            Configuration.GROUPS.FirstOrDefault(
                                g => g.Name.Equals(group, StringComparison.InvariantCulture)).UUID;
                        if (groupUUID.Equals(UUID.Zero) &&
                            !GroupNameToUUID(group, Configuration.SERVICES_TIMEOUT, ref groupUUID))
                        {
                            throw new Exception(GetEnumDescription(ScriptError.GROUP_NOT_FOUND));
                        }
                        if (!AgentInGroup(Client.Self.AgentID, groupUUID, Configuration.SERVICES_TIMEOUT))
                        {
                            throw new Exception(GetEnumDescription(ScriptError.NOT_IN_GROUP));
                        }
                        if (
                            !HasGroupPowers(Client.Self.AgentID, groupUUID, GroupPowers.RemoveMember,
                                Configuration.SERVICES_TIMEOUT))
                        {
                            throw new Exception(GetEnumDescription(ScriptError.NO_GROUP_POWER_FOR_COMMAND));
                        }
                        UUID agentUUID = UUID.Zero;
                        if (
                            !AgentNameToUUID(Uri.UnescapeDataString(wasKeyValueGet(ScriptKeys.FIRSTNAME, message)),
                                Uri.UnescapeDataString(wasKeyValueGet(ScriptKeys.LASTNAME, message)),
                                Configuration.SERVICES_TIMEOUT, ref agentUUID))
                        {
                            throw new Exception(GetEnumDescription(ScriptError.AGENT_NOT_FOUND));
                        }
                        string role = Uri.UnescapeDataString(wasKeyValueGet(ScriptKeys.ROLE, message));
                        UUID roleUUID;
                        if (!UUID.TryParse(role, out roleUUID))
                        {
                            if (
                                !RoleNameToRoleUUID(role, groupUUID,
                                    Configuration.SERVICES_TIMEOUT, ref roleUUID))
                            {
                                throw new Exception(GetEnumDescription(ScriptError.ROLE_NOT_FOUND));
                            }
                        }
                        if (roleUUID.Equals(UUID.Zero))
                        {
                            throw new Exception(
                                GetEnumDescription(ScriptError.CANNOT_DELETE_A_GROUP_MEMBER_FROM_THE_EVERYONE_ROLE));
                        }
                        OpenMetaverse.Group updateGroup = new OpenMetaverse.Group();
                        if (!RequestGroup(groupUUID, Configuration.SERVICES_TIMEOUT, ref updateGroup))
                        {
                            throw new Exception(GetEnumDescription(ScriptError.GROUP_NOT_FOUND));
                        }
                        if (updateGroup.OwnerRole.Equals(roleUUID))
                        {
                            throw new Exception(GetEnumDescription(ScriptError.CANNOT_REMOVE_USER_FROM_OWNER_ROLE));
                        }
                        Client.Groups.RemoveFromRole(groupUUID, roleUUID,
                            agentUUID);
                    };
                    break;
                case ScriptKeys.TELL:
                    execute = () =>
                    {
                        if (!HasCorradePermission(group, (int) Permissions.PERMISSION_TALK))
                        {
                            throw new Exception(GetEnumDescription(ScriptError.NO_CORRADE_PERMISSIONS));
                        }
                        switch (
                            Uri.UnescapeDataString(wasKeyValueGet(ScriptKeys.ENTITY, message))
                                .ToLower(CultureInfo.InvariantCulture))
                        {
                            case Entity.AVATAR:
                                UUID agentUUID = UUID.Zero;
                                if (
                                    !AgentNameToUUID(
                                        Uri.UnescapeDataString(wasKeyValueGet(ScriptKeys.FIRSTNAME, message)),
                                        Uri.UnescapeDataString(wasKeyValueGet(ScriptKeys.LASTNAME, message)),
                                        Configuration.SERVICES_TIMEOUT, ref agentUUID))
                                {
                                    throw new Exception(GetEnumDescription(ScriptError.AGENT_NOT_FOUND));
                                }
                                Client.Self.InstantMessage(agentUUID,
                                    Uri.UnescapeDataString(wasKeyValueGet(ScriptKeys.MESSAGE, message)));
                                break;
                            case Entity.GROUP:
                                UUID groupUUID =
                                    Configuration.GROUPS.FirstOrDefault(
                                        g => g.Name.Equals(group, StringComparison.InvariantCulture)).UUID;
                                if (groupUUID.Equals(UUID.Zero) &&
                                    !GroupNameToUUID(group, Configuration.SERVICES_TIMEOUT, ref groupUUID))
                                {
                                    throw new Exception(GetEnumDescription(ScriptError.GROUP_NOT_FOUND));
                                }
                                if (!AgentInGroup(Client.Self.AgentID, groupUUID, Configuration.SERVICES_TIMEOUT))
                                {
                                    throw new Exception(GetEnumDescription(ScriptError.NOT_IN_GROUP));
                                }
                                if (!Client.Self.GroupChatSessions.ContainsKey(groupUUID))
                                {
                                    if (
                                        !HasGroupPowers(Client.Self.AgentID, groupUUID, GroupPowers.JoinChat,
                                            Configuration.SERVICES_TIMEOUT))
                                    {
                                        throw new Exception(GetEnumDescription(ScriptError.NO_GROUP_POWER_FOR_COMMAND));
                                    }
                                    bool succeeded = false;
                                    ManualResetEvent GroupChatJoinedEvent = new ManualResetEvent(false);
                                    EventHandler<GroupChatJoinedEventArgs> GroupChatJoinedEventHandler =
                                        (sender, args) =>
                                        {
                                            if (args.Success)
                                            {
                                                succeeded = true;
                                            }
                                            GroupChatJoinedEvent.Set();
                                        };
                                    Client.Self.GroupChatJoined += GroupChatJoinedEventHandler;
                                    Client.Self.RequestJoinGroupChat(groupUUID);
                                    if (!GroupChatJoinedEvent.WaitOne(Configuration.SERVICES_TIMEOUT, false))
                                    {
                                        Client.Self.GroupChatJoined -= GroupChatJoinedEventHandler;
                                        throw new Exception(GetEnumDescription(ScriptError.UNABLE_TO_JOIN_GROUP_CHAT));
                                    }
                                    Client.Self.GroupChatJoined -= GroupChatJoinedEventHandler;
                                    if (!succeeded)
                                    {
                                        throw new Exception(GetEnumDescription(ScriptError.UNABLE_TO_JOIN_GROUP_CHAT));
                                    }
                                }
                                Client.Self.InstantMessageGroup(groupUUID,
                                    Uri.UnescapeDataString(wasKeyValueGet(ScriptKeys.MESSAGE, message)));
                                break;
                            case Entity.LOCAL:
                                int chatChannel;
                                if (
                                    !int.TryParse(Uri.UnescapeDataString(wasKeyValueGet(ScriptKeys.CHANNEL, message)),
                                        out chatChannel))
                                {
                                    chatChannel = 0;
                                }
                                string type = Uri.UnescapeDataString(wasKeyValueGet(ScriptKeys.TYPE, message));
                                ChatType chatType = !string.IsNullOrEmpty(type)
                                    ? (ChatType)
                                        typeof (ChatType).GetFields(BindingFlags.Public | BindingFlags.Static)
                                            .FirstOrDefault(
                                                t =>
                                                    t.Name.Equals(type,
                                                        StringComparison.InvariantCultureIgnoreCase))
                                            .GetValue(null)
                                    : ChatType.Normal;
                                Client.Self.Chat(Uri.UnescapeDataString(wasKeyValueGet(ScriptKeys.MESSAGE, message)),
                                    chatChannel,
                                    chatType);
                                break;
                            case Entity.ESTATE:
                                Client.Estate.EstateMessage(
                                    Uri.UnescapeDataString(wasKeyValueGet(ScriptKeys.MESSAGE, message)));
                                break;
                            case Entity.REGION:
                                Client.Estate.SimulatorMessage(
                                    Uri.UnescapeDataString(wasKeyValueGet(ScriptKeys.MESSAGE, message)));
                                break;
                            default:
                                throw new Exception(GetEnumDescription(ScriptError.UNKNOWN_ENTITY));
                        }
                    };
                    break;
                case ScriptKeys.NOTICE:
                    execute = () =>
                    {
                        UUID groupUUID =
                            Configuration.GROUPS.FirstOrDefault(
                                g => g.Name.Equals(group, StringComparison.InvariantCulture)).UUID;
                        if (groupUUID.Equals(UUID.Zero) &&
                            !GroupNameToUUID(group, Configuration.SERVICES_TIMEOUT, ref groupUUID))
                        {
                            throw new Exception(GetEnumDescription(ScriptError.GROUP_NOT_FOUND));
                        }
                        if (!AgentInGroup(Client.Self.AgentID, groupUUID, Configuration.SERVICES_TIMEOUT))
                        {
                            throw new Exception(GetEnumDescription(ScriptError.NOT_IN_GROUP));
                        }
                        if (
                            !HasGroupPowers(Client.Self.AgentID, groupUUID, GroupPowers.SendNotices,
                                Configuration.SERVICES_TIMEOUT))
                        {
                            throw new Exception(GetEnumDescription(ScriptError.NO_GROUP_POWER_FOR_COMMAND));
                        }
                        GroupNotice notice = new GroupNotice
                        {
                            Message = Uri.UnescapeDataString(wasKeyValueGet(ScriptKeys.MESSAGE, message)),
                            Subject = Uri.UnescapeDataString(wasKeyValueGet(ScriptKeys.SUBJECT, message))
                        };
                        string item = Uri.UnescapeDataString(wasKeyValueGet(ScriptKeys.ITEM, message));
                        if (!string.IsNullOrEmpty(item))
                        {
                            UUID itemUUID;
                            if (!UUID.TryParse(item, out itemUUID))
                            {
                                itemUUID =
                                    SearchInventoryItem(Client.Inventory.Store.RootFolder, item,
                                        Configuration.SERVICES_TIMEOUT).FirstOrDefault().UUID;
                                if (itemUUID.Equals(UUID.Zero))
                                {
                                    throw new Exception(GetEnumDescription(ScriptError.INVENTORY_ITEM_NOT_FOUND));
                                }
                            }
                            notice.AttachmentID = itemUUID;
                        }
                        Client.Groups.SendGroupNotice(groupUUID, notice);
                    };
                    break;
                case ScriptKeys.PAY:
                    execute = () =>
                    {
                        if (
                            !HasCorradePermission(group, (int) Permissions.PERMISSION_ECONOMY))
                        {
                            throw new Exception(GetEnumDescription(ScriptError.NO_CORRADE_PERMISSIONS));
                        }
                        int amount;
                        if (
                            !int.TryParse(Uri.UnescapeDataString(wasKeyValueGet(ScriptKeys.AMOUNT, message)), out amount))
                        {
                            throw new Exception(GetEnumDescription(ScriptError.INVALID_PAY_AMOUNT));
                        }
                        if (amount.Equals(0))
                        {
                            throw new Exception(GetEnumDescription(ScriptError.INVALID_PAY_AMOUNT));
                        }
                        ManualResetEvent MoneyBalanceEvent = new ManualResetEvent(false);
                        EventHandler<BalanceEventArgs> MoneyBalanceEventHandler =
                            (sender, args) => MoneyBalanceEvent.Set();
                        Client.Self.MoneyBalance += MoneyBalanceEventHandler;
                        Client.Self.RequestBalance();
                        if (!MoneyBalanceEvent.WaitOne(Configuration.SERVICES_TIMEOUT, false))
                        {
                            Client.Self.MoneyBalance -= MoneyBalanceEventHandler;
                            throw new Exception(GetEnumDescription(ScriptError.TIMEOUT_WAITING_FOR_BALANCE));
                        }
                        Client.Self.MoneyBalance -= MoneyBalanceEventHandler;
                        if (Client.Self.Balance < amount)
                        {
                            throw new Exception(GetEnumDescription(ScriptError.INSUFFICIENT_FUNDS));
                        }
                        UUID targetUUID = UUID.Zero;
                        switch (
                            Uri.UnescapeDataString(wasKeyValueGet(ScriptKeys.ENTITY, message))
                                .ToLower(CultureInfo.InvariantCulture))
                        {
                            case Entity.GROUP:
                                targetUUID =
                                    Configuration.GROUPS.FirstOrDefault(
                                        g => g.Name.Equals(group, StringComparison.InvariantCulture)).UUID;
                                if (targetUUID.Equals(UUID.Zero) &&
                                    !GroupNameToUUID(group, Configuration.SERVICES_TIMEOUT, ref targetUUID))
                                {
                                    throw new Exception(GetEnumDescription(ScriptError.GROUP_NOT_FOUND));
                                }
                                Client.Self.GiveGroupMoney(targetUUID, amount,
                                    Uri.UnescapeDataString(wasKeyValueGet(ScriptKeys.REASON, message)));
                                break;
                            case Entity.AVATAR:
                                if (
                                    !AgentNameToUUID(
                                        Uri.UnescapeDataString(wasKeyValueGet(ScriptKeys.FIRSTNAME, message)),
                                        Uri.UnescapeDataString(wasKeyValueGet(ScriptKeys.LASTNAME, message)),
                                        Configuration.SERVICES_TIMEOUT, ref targetUUID))
                                {
                                    throw new Exception(GetEnumDescription(ScriptError.AGENT_NOT_FOUND));
                                }
                                Client.Self.GiveAvatarMoney(targetUUID, amount,
                                    Uri.UnescapeDataString(wasKeyValueGet(ScriptKeys.REASON, message)));
                                break;
                            case Entity.OBJECT:
                                if (
                                    !UUID.TryParse(Uri.UnescapeDataString(wasKeyValueGet(ScriptKeys.TARGET, message)),
                                        out targetUUID))
                                {
                                    throw new Exception(GetEnumDescription(ScriptError.INVALID_PAY_TARGET));
                                }
                                Client.Self.GiveObjectMoney(targetUUID, amount,
                                    Uri.UnescapeDataString(wasKeyValueGet(ScriptKeys.REASON, message)));
                                break;
                            default:
                                throw new Exception(GetEnumDescription(ScriptError.UNKNOWN_ENTITY));
                        }
                    };
                    break;
                case ScriptKeys.GETBALANCE:
                    execute = () =>
                    {
                        if (
                            !HasCorradePermission(group, (int) Permissions.PERMISSION_ECONOMY))
                        {
                            throw new Exception(GetEnumDescription(ScriptError.NO_CORRADE_PERMISSIONS));
                        }
                        ManualResetEvent MoneyBalanceEvent = new ManualResetEvent(false);
                        EventHandler<BalanceEventArgs> MoneyBalanceEventHandler =
                            (sender, args) => MoneyBalanceEvent.Set();
                        Client.Self.MoneyBalance += MoneyBalanceEventHandler;
                        Client.Self.RequestBalance();
                        if (!MoneyBalanceEvent.WaitOne(Configuration.SERVICES_TIMEOUT, false))
                        {
                            Client.Self.MoneyBalance -= MoneyBalanceEventHandler;
                            throw new Exception(GetEnumDescription(ScriptError.TIMEOUT_WAITING_FOR_BALANCE));
                        }
                        Client.Self.MoneyBalance -= MoneyBalanceEventHandler;
                        result.Add(ResultKeys.BALANCE, Client.Self.Balance.ToString(CultureInfo.InvariantCulture));
                    };
                    break;
                case ScriptKeys.TELEPORT:
                    execute = () =>
                    {
                        if (
                            !HasCorradePermission(group,
                                (int) Permissions.PERMISSION_MOVEMENT))
                        {
                            throw new Exception(GetEnumDescription(ScriptError.NO_CORRADE_PERMISSIONS));
                        }
                        string region = Uri.UnescapeDataString(wasKeyValueGet(ScriptKeys.REGION, message));
                        if (string.IsNullOrEmpty(region))
                        {
                            throw new Exception(GetEnumDescription(ScriptError.NO_REGION_SPECIFIED));
                        }
                        Vector3 position;
                        if (
                            !Vector3.TryParse(Uri.UnescapeDataString(wasKeyValueGet(ScriptKeys.POSITION, message)),
                                out position))
                        {
                            throw new Exception(GetEnumDescription(ScriptError.NO_POSITION_SPECIFIED));
                        }
                        ManualResetEvent TeleportEvent = new ManualResetEvent(false);
                        bool succeeded = false;
                        EventHandler<TeleportEventArgs> TeleportEventHandler = (sender, args) =>
                        {
                            if (!args.Status.Equals(TeleportStatus.Finished))
                                return;
                            if (Client.Network.CurrentSim.Name.Equals(region, StringComparison.InvariantCulture))
                            {
                                succeeded = true;
                            }
                            TeleportEvent.Set();
                        };
                        Client.Self.SignaledAnimations.ForEach(
                            animation => Client.Self.AnimationStop(animation.Key, true));
                        if (Client.Self.Movement.SitOnGround || Client.Self.SittingOn != 0)
                        {
                            Client.Self.Stand();
                        }
                        Client.Self.TeleportProgress += TeleportEventHandler;
                        Client.Self.Teleport(region, position);
                        if (!TeleportEvent.WaitOne(Configuration.SERVICES_TIMEOUT, false))
                        {
                            Client.Self.TeleportProgress -= TeleportEventHandler;
                            throw new Exception(GetEnumDescription(ScriptError.TELEPORT_FAILED));
                        }
                        Client.Self.TeleportProgress -= TeleportEventHandler;
                        if (!succeeded)
                        {
                            throw new Exception(GetEnumDescription(ScriptError.TELEPORT_FAILED));
                        }
                    };
                    break;
                case ScriptKeys.LURE:
                    execute = () =>
                    {
                        if (
                            !HasCorradePermission(group,
                                (int) Permissions.PERMISSION_MOVEMENT))
                        {
                            throw new Exception(GetEnumDescription(ScriptError.NO_CORRADE_PERMISSIONS));
                        }
                        UUID agentUUID = UUID.Zero;
                        if (
                            !AgentNameToUUID(Uri.UnescapeDataString(wasKeyValueGet(ScriptKeys.FIRSTNAME, message)),
                                Uri.UnescapeDataString(wasKeyValueGet(ScriptKeys.LASTNAME, message)),
                                Configuration.SERVICES_TIMEOUT, ref agentUUID))
                        {
                            throw new Exception(GetEnumDescription(ScriptError.AGENT_NOT_FOUND));
                        }
                        Client.Self.SendTeleportLure(agentUUID,
                            Uri.UnescapeDataString(wasKeyValueGet(ScriptKeys.MESSAGE, message)));
                    };
                    break;
                case ScriptKeys.SETHOME:
                    execute = () =>
                    {
                        if (
                            !HasCorradePermission(group,
                                (int) Permissions.PERMISSION_MOVEMENT))
                        {
                            throw new Exception(GetEnumDescription(ScriptError.NO_CORRADE_PERMISSIONS));
                        }
                        bool succeeded = true;
                        EventHandler<AlertMessageEventArgs> AlertMessageEventHandler = (sender, args) =>
                        {
                            if (args.Message.Equals(LINDEN_CONSTANTS.ALERTS.UNABLE_TO_SET_HOME))
                            {
                                succeeded = false;
                            }
                        };
                        Client.Self.AlertMessage += AlertMessageEventHandler;
                        Client.Self.SetHome();
                        if (!succeeded)
                        {
                            Client.Self.AlertMessage -= AlertMessageEventHandler;
                            throw new Exception(GetEnumDescription(ScriptError.UNABLE_TO_SET_HOME));
                        }
                        Client.Self.AlertMessage -= AlertMessageEventHandler;
                    };
                    break;
                case ScriptKeys.GOHOME:
                    execute = () =>
                    {
                        if (
                            !HasCorradePermission(group,
                                (int) Permissions.PERMISSION_MOVEMENT))
                        {
                            throw new Exception(GetEnumDescription(ScriptError.NO_CORRADE_PERMISSIONS));
                        }
                        Client.Self.SignaledAnimations.ForEach(
                            animation => Client.Self.AnimationStop(animation.Key, true));
                        if (Client.Self.Movement.SitOnGround || Client.Self.SittingOn != 0)
                        {
                            Client.Self.Stand();
                        }
                        if (!Client.Self.GoHome())
                        {
                            throw new Exception(GetEnumDescription(ScriptError.UNABLE_TO_GO_HOME));
                        }
                    };
                    break;
                case ScriptKeys.GETREGIONDATA:
                    execute = () =>
                    {
                        if (!HasCorradePermission(group, (int) Permissions.PERMISSION_LAND))
                        {
                            throw new Exception(GetEnumDescription(ScriptError.NO_CORRADE_PERMISSIONS));
                        }
                        string fields = Uri.UnescapeDataString(wasKeyValueGet(ScriptKeys.DATA, message));
                        List<string> csv = new List<string>();
                        object LockObject = new object();
                        Parallel.ForEach(
                            fields.Split(new[] {LINDEN_CONSTANTS.LSL.CSV_DELIMITER},
                                StringSplitOptions.RemoveEmptyEntries),
                            name =>
                            {
                                KeyValuePair<FieldInfo, object> fi = wasGetFields(Client.Network.CurrentSim,
                                    Client.Network.CurrentSim.GetType().Name)
                                    .FirstOrDefault(
                                        o => o.Key.Name.Equals(name, StringComparison.InvariantCultureIgnoreCase));

                                lock (LockObject)
                                {
                                    List<string> data = new List<string> {name};
                                    data.AddRange(wasGetInfo(fi.Key, fi.Value));
                                    if (data.Count >= 2)
                                    {
                                        csv.AddRange(data);
                                    }
                                }

                                KeyValuePair<PropertyInfo, object> pi =
                                    wasGetProperties(Client.Network.CurrentSim, Client.Network.CurrentSim.GetType().Name)
                                        .FirstOrDefault(
                                            o => o.Key.Name.Equals(name, StringComparison.InvariantCultureIgnoreCase));

                                lock (LockObject)
                                {
                                    List<string> data = new List<string> {name};
                                    data.AddRange(wasGetInfo(pi.Key, pi.Value));
                                    if (data.Count >= 2)
                                    {
                                        csv.AddRange(data);
                                    }
                                }
                            });
                        result.Add(ResultKeys.DATA, string.Join(LINDEN_CONSTANTS.LSL.CSV_DELIMITER, csv.ToArray()));
                    };
                    break;
                case ScriptKeys.SIT:
                    execute = () =>
                    {
                        if (
                            !HasCorradePermission(group,
                                (int) Permissions.PERMISSION_MOVEMENT))
                        {
                            throw new Exception(GetEnumDescription(ScriptError.NO_CORRADE_PERMISSIONS));
                        }
                        float range;
                        if (
                            !float.TryParse(Uri.UnescapeDataString(wasKeyValueGet(ScriptKeys.RANGE, message)), out range))
                        {
                            throw new Exception(GetEnumDescription(ScriptError.NO_RANGE_PROVIDED));
                        }
                        Primitive primitive = null;
                        if (
                            !FindPrimitive(Uri.UnescapeDataString(wasKeyValueGet(ScriptKeys.ITEM, message)), range,
                                Configuration.SERVICES_TIMEOUT,
                                ref primitive))
                        {
                            throw new Exception(GetEnumDescription(ScriptError.PRIMITIVE_NOT_FOUND));
                        }
                        ManualResetEvent SitEvent = new ManualResetEvent(false);
                        bool succeeded = false;
                        EventHandler<AvatarSitResponseEventArgs> AvatarSitEventHandler = (sender, args) =>
                        {
                            if (!args.ObjectID.Equals(UUID.Zero))
                            {
                                succeeded = true;
                            }
                            SitEvent.Set();
                        };
                        EventHandler<AlertMessageEventArgs> AlertMessageEventHandler = (sender, args) =>
                        {
                            if (args.Message.Equals(LINDEN_CONSTANTS.ALERTS.NO_ROOM_TO_SIT_HERE))
                            {
                                succeeded = false;
                            }
                            SitEvent.Set();
                        };
                        Client.Self.SignaledAnimations.ForEach(
                            animation => Client.Self.AnimationStop(animation.Key, true));
                        if (Client.Self.Movement.SitOnGround || Client.Self.SittingOn != 0)
                        {
                            Client.Self.Stand();
                        }
                        Client.Self.AvatarSitResponse += AvatarSitEventHandler;
                        Client.Self.AlertMessage += AlertMessageEventHandler;
                        Client.Self.RequestSit(primitive.ID, Vector3.Zero);
                        if (!SitEvent.WaitOne(Configuration.SERVICES_TIMEOUT, false))
                        {
                            Client.Self.AvatarSitResponse -= AvatarSitEventHandler;
                            Client.Self.AlertMessage -= AlertMessageEventHandler;
                            throw new Exception(GetEnumDescription(ScriptError.COULD_NOT_SIT));
                        }
                        Client.Self.AvatarSitResponse -= AvatarSitEventHandler;
                        Client.Self.AlertMessage -= AlertMessageEventHandler;
                        if (!succeeded)
                        {
                            throw new Exception(GetEnumDescription(ScriptError.COULD_NOT_SIT));
                        }
                        Client.Self.Sit();
                    };
                    break;
                case ScriptKeys.STAND:
                    execute = () =>
                    {
                        if (
                            !HasCorradePermission(group,
                                (int) Permissions.PERMISSION_MOVEMENT))
                        {
                            throw new Exception(GetEnumDescription(ScriptError.NO_CORRADE_PERMISSIONS));
                        }
                        Client.Self.SignaledAnimations.ForEach(
                            animation => Client.Self.AnimationStop(animation.Key, true));
                        if (Client.Self.Movement.SitOnGround || Client.Self.SittingOn != 0)
                        {
                            Client.Self.Stand();
                        }
                    };
                    break;
                case ScriptKeys.PARCELEJECT:
                    execute = () =>
                    {
                        if (!HasCorradePermission(group, (int) Permissions.PERMISSION_LAND))
                        {
                            throw new Exception(GetEnumDescription(ScriptError.NO_CORRADE_PERMISSIONS));
                        }
                        UUID groupUUID =
                            Configuration.GROUPS.FirstOrDefault(
                                g => g.Name.Equals(group, StringComparison.InvariantCulture)).UUID;
                        if (groupUUID.Equals(UUID.Zero) &&
                            !GroupNameToUUID(group, Configuration.SERVICES_TIMEOUT, ref groupUUID))
                        {
                            throw new Exception(GetEnumDescription(ScriptError.GROUP_NOT_FOUND));
                        }
                        if (
                            !HasGroupPowers(Client.Self.AgentID, groupUUID, GroupPowers.LandEjectAndFreeze,
                                Configuration.SERVICES_TIMEOUT))
                        {
                            throw new Exception(GetEnumDescription(ScriptError.NO_GROUP_POWER_FOR_COMMAND));
                        }
                        UUID agentUUID = UUID.Zero;
                        if (
                            !AgentNameToUUID(Uri.UnescapeDataString(wasKeyValueGet(ScriptKeys.FIRSTNAME, message)),
                                Uri.UnescapeDataString(wasKeyValueGet(ScriptKeys.LASTNAME, message)),
                                Configuration.SERVICES_TIMEOUT, ref agentUUID))
                        {
                            throw new Exception(GetEnumDescription(ScriptError.AGENT_NOT_FOUND));
                        }
                        bool alsoban;
                        if (!bool.TryParse(Uri.UnescapeDataString(wasKeyValueGet(ScriptKeys.BAN, message)), out alsoban))
                        {
                            alsoban = false;
                        }
                        Client.Parcels.EjectUser(agentUUID, alsoban);
                    };
                    break;
                case ScriptKeys.PARCELFREEZE:
                    execute = () =>
                    {
                        if (!HasCorradePermission(group, (int) Permissions.PERMISSION_LAND))
                        {
                            throw new Exception(GetEnumDescription(ScriptError.NO_CORRADE_PERMISSIONS));
                        }
                        UUID groupUUID =
                            Configuration.GROUPS.FirstOrDefault(
                                g => g.Name.Equals(group, StringComparison.InvariantCulture)).UUID;
                        if (groupUUID.Equals(UUID.Zero) &&
                            !GroupNameToUUID(group, Configuration.SERVICES_TIMEOUT, ref groupUUID))
                        {
                            throw new Exception(GetEnumDescription(ScriptError.GROUP_NOT_FOUND));
                        }
                        if (
                            !HasGroupPowers(Client.Self.AgentID, groupUUID, GroupPowers.LandEjectAndFreeze,
                                Configuration.SERVICES_TIMEOUT))
                        {
                            throw new Exception(GetEnumDescription(ScriptError.NO_GROUP_POWER_FOR_COMMAND));
                        }
                        UUID agentUUID = UUID.Zero;
                        if (
                            !AgentNameToUUID(Uri.UnescapeDataString(wasKeyValueGet(ScriptKeys.FIRSTNAME, message)),
                                Uri.UnescapeDataString(wasKeyValueGet(ScriptKeys.LASTNAME, message)),
                                Configuration.SERVICES_TIMEOUT, ref agentUUID))
                        {
                            throw new Exception(GetEnumDescription(ScriptError.AGENT_NOT_FOUND));
                        }
                        bool freeze;
                        if (
                            !bool.TryParse(Uri.UnescapeDataString(wasKeyValueGet(ScriptKeys.FREEZE, message)),
                                out freeze))
                        {
                            freeze = false;
                        }
                        Client.Parcels.FreezeUser(agentUUID, freeze);
                    };
                    break;
                case ScriptKeys.PARCELMUSIC:
                    execute = () =>
                    {
                        if (!HasCorradePermission(group, (int) Permissions.PERMISSION_LAND))
                        {
                            throw new Exception(GetEnumDescription(ScriptError.NO_CORRADE_PERMISSIONS));
                        }
                        UUID groupUUID =
                            Configuration.GROUPS.FirstOrDefault(
                                g => g.Name.Equals(group, StringComparison.InvariantCulture)).UUID;
                        if (groupUUID.Equals(UUID.Zero) &&
                            !GroupNameToUUID(group, Configuration.SERVICES_TIMEOUT, ref groupUUID))
                        {
                            throw new Exception(GetEnumDescription(ScriptError.GROUP_NOT_FOUND));
                        }
                        if (
                            !HasGroupPowers(Client.Self.AgentID, groupUUID, GroupPowers.ChangeMedia,
                                Configuration.SERVICES_TIMEOUT))
                        {
                            throw new Exception(GetEnumDescription(ScriptError.NO_GROUP_POWER_FOR_COMMAND));
                        }
                        Parcel parcel = null;
                        if (
                            !GetParcelAtPosition(Client.Network.CurrentSim, Client.Self.SimPosition,
                                Configuration.SERVICES_TIMEOUT, ref parcel))
                        {
                            throw new Exception(GetEnumDescription(ScriptError.COULD_NOT_FIND_PARCEL));
                        }
                        parcel.MusicURL = Uri.UnescapeDataString(wasKeyValueGet(ScriptKeys.URL, message));
                        parcel.Update(Client.Network.CurrentSim, true);
                    };
                    break;
                case ScriptKeys.SETPROFILEDATA:
                    execute = () =>
                    {
                        if (
                            !HasCorradePermission(group,
                                (int) Permissions.PERMISSION_GROOMING))
                        {
                            throw new Exception(GetEnumDescription(ScriptError.NO_CORRADE_PERMISSIONS));
                        }
                        ManualResetEvent[] AvatarProfileDataEvent =
                        {
                            new ManualResetEvent(false),
                            new ManualResetEvent(false)
                        };
                        Avatar.AvatarProperties properties = new Avatar.AvatarProperties();
                        Avatar.Interests interests = new Avatar.Interests();
                        EventHandler<AvatarPropertiesReplyEventArgs> AvatarPropertiesEventHandler = (sender, args) =>
                        {
                            properties = args.Properties;
                            AvatarProfileDataEvent[0].Set();
                        };
                        EventHandler<AvatarInterestsReplyEventArgs> AvatarInterestsEventHandler = (sender, args) =>
                        {
                            interests = args.Interests;
                            AvatarProfileDataEvent[1].Set();
                        };
                        Client.Avatars.AvatarPropertiesReply += AvatarPropertiesEventHandler;
                        Client.Avatars.AvatarInterestsReply += AvatarInterestsEventHandler;
                        Client.Avatars.RequestAvatarProperties(Client.Self.AgentID);
                        if (
                            !WaitHandle.WaitAll(AvatarProfileDataEvent.Select(s => (WaitHandle) s).ToArray(),
                                Configuration.SERVICES_TIMEOUT))
                        {
                            Client.Avatars.AvatarPropertiesReply -= AvatarPropertiesEventHandler;
                            Client.Avatars.AvatarInterestsReply -= AvatarInterestsEventHandler;
                            throw new Exception(GetEnumDescription(ScriptError.TIMEOUT_GETTING_PROFILE));
                        }
                        Client.Avatars.AvatarPropertiesReply -= AvatarPropertiesEventHandler;
                        Client.Avatars.AvatarInterestsReply -= AvatarInterestsEventHandler;
                        string fields = Uri.UnescapeDataString(wasKeyValueGet(ScriptKeys.DATA, message));
                        wasCSVToStructure(fields, ref properties);
                        wasCSVToStructure(fields, ref interests);
                        Client.Self.UpdateProfile(properties);
                        Client.Self.UpdateInterests(interests);
                    };
                    break;
                case ScriptKeys.GETPROFILEDATA:
                    execute = () =>
                    {
                        if (
                            !HasCorradePermission(group,
                                (int) Permissions.PERMISSION_INTERACT))
                        {
                            throw new Exception(GetEnumDescription(ScriptError.NO_CORRADE_PERMISSIONS));
                        }
                        UUID agentUUID = UUID.Zero;
                        if (
                            !AgentNameToUUID(Uri.UnescapeDataString(wasKeyValueGet(ScriptKeys.FIRSTNAME, message)),
                                Uri.UnescapeDataString(wasKeyValueGet(ScriptKeys.LASTNAME, message)),
                                Configuration.SERVICES_TIMEOUT, ref agentUUID))
                        {
                            throw new Exception(GetEnumDescription(ScriptError.AGENT_NOT_FOUND));
                        }
                        ManualResetEvent[] AvatarProfileDataEvent =
                        {
                            new ManualResetEvent(false),
                            new ManualResetEvent(false)
                        };
                        Avatar.AvatarProperties properties = new Avatar.AvatarProperties();
                        Avatar.Interests interests = new Avatar.Interests();
                        EventHandler<AvatarPropertiesReplyEventArgs> AvatarPropertiesEventHandler = (sender, args) =>
                        {
                            properties = args.Properties;
                            AvatarProfileDataEvent[0].Set();
                        };
                        EventHandler<AvatarInterestsReplyEventArgs> AvatarInterestsEventHandler = (sender, args) =>
                        {
                            interests = args.Interests;
                            AvatarProfileDataEvent[1].Set();
                        };
                        Client.Avatars.AvatarPropertiesReply += AvatarPropertiesEventHandler;
                        Client.Avatars.AvatarInterestsReply += AvatarInterestsEventHandler;
                        Client.Avatars.RequestAvatarProperties(agentUUID);
                        if (
                            !WaitHandle.WaitAll(AvatarProfileDataEvent.Select(s => (WaitHandle) s).ToArray(),
                                Configuration.SERVICES_TIMEOUT))
                        {
                            Client.Avatars.AvatarPropertiesReply -= AvatarPropertiesEventHandler;
                            Client.Avatars.AvatarInterestsReply -= AvatarInterestsEventHandler;
                            throw new Exception(GetEnumDescription(ScriptError.TIMEOUT_GETTING_PROFILE));
                        }
                        Client.Avatars.AvatarPropertiesReply -= AvatarPropertiesEventHandler;
                        Client.Avatars.AvatarInterestsReply -= AvatarInterestsEventHandler;
                        string fields = Uri.UnescapeDataString(wasKeyValueGet(ScriptKeys.DATA, message));
                        List<string> csv = new List<string>();
                        object LockObject = new object();
                        Parallel.ForEach(
                            fields.Split(new[] {LINDEN_CONSTANTS.LSL.CSV_DELIMITER},
                                StringSplitOptions.RemoveEmptyEntries),
                            name =>
                            {
                                KeyValuePair<FieldInfo, object> fi = wasGetFields(properties,
                                    properties.GetType().Name)
                                    .FirstOrDefault(
                                        o => o.Key.Name.Equals(name, StringComparison.InvariantCultureIgnoreCase));

                                lock (LockObject)
                                {
                                    List<string> data = new List<string> {name};
                                    data.AddRange(wasGetInfo(fi.Key, fi.Value));
                                    if (data.Count >= 2)
                                    {
                                        csv.AddRange(data);
                                    }
                                }

                                KeyValuePair<PropertyInfo, object> pi =
                                    wasGetProperties(properties, properties.GetType().Name)
                                        .FirstOrDefault(
                                            o => o.Key.Name.Equals(name, StringComparison.InvariantCultureIgnoreCase));

                                lock (LockObject)
                                {
                                    List<string> data = new List<string> {name};
                                    data.AddRange(wasGetInfo(pi.Key, pi.Value));
                                    if (data.Count >= 2)
                                    {
                                        csv.AddRange(data);
                                    }
                                }

                                fi = wasGetFields(interests, interests.GetType().Name)
                                    .FirstOrDefault(
                                        o => o.Key.Name.Equals(name, StringComparison.InvariantCultureIgnoreCase));

                                lock (LockObject)
                                {
                                    List<string> data = new List<string> {name};
                                    data.AddRange(wasGetInfo(fi.Key, fi.Value));
                                    if (data.Count >= 2)
                                    {
                                        csv.AddRange(data);
                                    }
                                }

                                pi = wasGetProperties(interests, interests.GetType().Name)
                                    .FirstOrDefault(
                                        o => o.Key.Name.Equals(name, StringComparison.InvariantCultureIgnoreCase));

                                lock (LockObject)
                                {
                                    List<string> data = new List<string> {name};
                                    data.AddRange(wasGetInfo(pi.Key, pi.Value));
                                    if (data.Count >= 2)
                                    {
                                        csv.AddRange(data);
                                    }
                                }
                            });
                        result.Add(ResultKeys.DATA, string.Join(LINDEN_CONSTANTS.LSL.CSV_DELIMITER, csv.ToArray()));
                    };
                    break;
                case ScriptKeys.GIVE:
                    execute = () =>
                    {
                        if (
                            !HasCorradePermission(group,
                                (int) Permissions.PERMISSION_INVENTORY))
                        {
                            throw new Exception(GetEnumDescription(ScriptError.NO_CORRADE_PERMISSIONS));
                        }
                        InventoryItem item =
                            SearchInventoryItem(Client.Inventory.Store.RootFolder,
                                Uri.UnescapeDataString(wasKeyValueGet(ScriptKeys.ITEM, message)),
                                Configuration.SERVICES_TIMEOUT).FirstOrDefault();
                        if (item == null)
                        {
                            throw new Exception(GetEnumDescription(ScriptError.INVENTORY_ITEM_NOT_FOUND));
                        }
                        switch (Uri.UnescapeDataString(wasKeyValueGet(ScriptKeys.ENTITY, message))
                            .ToLower(CultureInfo.InvariantCulture))
                        {
                            case Entity.AVATAR:
                                UUID agentUUID = UUID.Zero;
                                if (
                                    !AgentNameToUUID(
                                        Uri.UnescapeDataString(wasKeyValueGet(ScriptKeys.FIRSTNAME, message)),
                                        Uri.UnescapeDataString(wasKeyValueGet(ScriptKeys.LASTNAME, message)),
                                        Configuration.SERVICES_TIMEOUT, ref agentUUID))
                                {
                                    throw new Exception(GetEnumDescription(ScriptError.AGENT_NOT_FOUND));
                                }
                                Client.Inventory.GiveItem(item.UUID, item.Name, item.AssetType, agentUUID, true);
                                break;
                            case Entity.OBJECT:
                                float range;
                                if (
                                    !float.TryParse(Uri.UnescapeDataString(wasKeyValueGet(ScriptKeys.RANGE, message)),
                                        out range))
                                {
                                    throw new Exception(GetEnumDescription(ScriptError.NO_RANGE_PROVIDED));
                                }
                                Primitive primitive = null;
                                if (
                                    !FindPrimitive(Uri.UnescapeDataString(wasKeyValueGet(ScriptKeys.TARGET, message)),
                                        range,
                                        Configuration.SERVICES_TIMEOUT,
                                        ref primitive))
                                {
                                    throw new Exception(GetEnumDescription(ScriptError.PRIMITIVE_NOT_FOUND));
                                }
                                Client.Inventory.UpdateTaskInventory(primitive.LocalID, item);
                                break;
                            default:
                                throw new Exception(GetEnumDescription(ScriptError.UNKNOWN_ENTITY));
                        }
                    };
                    break;
                case ScriptKeys.DELETEITEM:
                    execute = () =>
                    {
                        if (
                            !HasCorradePermission(group,
                                (int) Permissions.PERMISSION_INVENTORY))
                        {
                            throw new Exception(GetEnumDescription(ScriptError.NO_CORRADE_PERMISSIONS));
                        }
                        HashSet<InventoryItem> items =
                            new HashSet<InventoryItem>(SearchInventoryItem(Client.Inventory.Store.RootFolder,
                                Uri.UnescapeDataString(wasKeyValueGet(ScriptKeys.ITEM, message)),
                                Configuration.SERVICES_TIMEOUT));
                        if (items.Count == 0)
                        {
                            throw new Exception(GetEnumDescription(ScriptError.INVENTORY_ITEM_NOT_FOUND));
                        }
                        Parallel.ForEach(items, item =>
                        {
                            switch (item.AssetType)
                            {
                                case AssetType.Folder:
                                    Client.Inventory.MoveFolder(item.UUID,
                                        Client.Inventory.FindFolderForType(AssetType.TrashFolder));
                                    break;
                                default:
                                    Client.Inventory.MoveItem(item.UUID,
                                        Client.Inventory.FindFolderForType(AssetType.TrashFolder));
                                    break;
                            }
                        });
                    };
                    break;
                case ScriptKeys.EMPTYTRASH:
                    execute = () =>
                    {
                        if (
                            !HasCorradePermission(group,
                                (int) Permissions.PERMISSION_INVENTORY))
                        {
                            throw new Exception(GetEnumDescription(ScriptError.NO_CORRADE_PERMISSIONS));
                        }
                        Client.Inventory.EmptyTrash();
                    };
                    break;
                case ScriptKeys.FLY:
                    execute = () =>
                    {
                        if (
                            !HasCorradePermission(group,
                                (int) Permissions.PERMISSION_MOVEMENT))
                        {
                            throw new Exception(GetEnumDescription(ScriptError.NO_CORRADE_PERMISSIONS));
                        }
                        string action = Uri.UnescapeDataString(wasKeyValueGet(ScriptKeys.ACTION, message))
                            .ToLower(CultureInfo.InvariantCulture);
                        switch (action)
                        {
                            case Action.START:
                            case Action.STOP:
                                Client.Self.SignaledAnimations.ForEach(
                                    animation => Client.Self.AnimationStop(animation.Key, true));
                                if (Client.Self.Movement.SitOnGround || Client.Self.SittingOn != 0)
                                {
                                    Client.Self.Stand();
                                }
                                Client.Self.Fly(action.Equals(Action.START,
                                    StringComparison.InvariantCultureIgnoreCase));
                                break;
                            default:
                                throw new Exception(GetEnumDescription(ScriptError.FLY_ACTION_START_OR_STOP));
                        }
                    };
                    break;
                case ScriptKeys.ADDPICK:
                    execute = () =>
                    {
                        if (
                            !HasCorradePermission(group,
                                (int) Permissions.PERMISSION_GROOMING))
                        {
                            throw new Exception(GetEnumDescription(ScriptError.NO_CORRADE_PERMISSIONS));
                        }
                        string item = Uri.UnescapeDataString(wasKeyValueGet(ScriptKeys.ITEM, message));
                        UUID textureUUID = UUID.Zero;
                        if (!string.IsNullOrEmpty(item))
                        {
                            if (!UUID.TryParse(item, out textureUUID))
                            {
                                textureUUID =
                                    SearchInventoryItem(Client.Inventory.Store.RootFolder, item,
                                        Configuration.SERVICES_TIMEOUT).FirstOrDefault().AssetUUID;
                            }
                            if (textureUUID.Equals(UUID.Zero))
                            {
                                throw new Exception(GetEnumDescription(ScriptError.TEXTURE_NOT_FOUND));
                            }
                        }
                        ManualResetEvent AvatarPicksReplyEvent = new ManualResetEvent(false);
                        UUID pickUUID = UUID.Zero;
                        string pickName = Uri.UnescapeDataString(wasKeyValueGet(ScriptKeys.NAME, message));
                        if (string.IsNullOrEmpty(pickName))
                        {
                            throw new Exception(GetEnumDescription(ScriptError.EMPTY_PICK_NAME));
                        }
                        EventHandler<AvatarPicksReplyEventArgs> AvatarPicksEventHandler = (sender, args) =>
                        {
                            pickUUID =
                                args.Picks.FirstOrDefault(
                                    pick => pick.Value.Equals(pickName, StringComparison.InvariantCulture)).Key;
                            AvatarPicksReplyEvent.Set();
                        };
                        Client.Avatars.AvatarPicksReply += AvatarPicksEventHandler;
                        Client.Avatars.RequestAvatarPicks(Client.Self.AgentID);
                        if (!AvatarPicksReplyEvent.WaitOne(Configuration.SERVICES_TIMEOUT, false))
                        {
                            Client.Avatars.AvatarPicksReply -= AvatarPicksEventHandler;
                            throw new Exception(GetEnumDescription(ScriptError.TIMEOUT_GETTING_PICKS));
                        }
                        Client.Avatars.AvatarPicksReply -= AvatarPicksEventHandler;
                        if (pickUUID.Equals(UUID.Zero))
                        {
                            pickUUID = UUID.Random();
                        }
                        Client.Self.PickInfoUpdate(pickUUID, false, UUID.Zero, pickName,
                            Client.Self.GlobalPosition, textureUUID,
                            Uri.UnescapeDataString(wasKeyValueGet(ScriptKeys.DESCRIPTION, message)));
                    };
                    break;
                case ScriptKeys.DELETEPICK:
                    execute = () =>
                    {
                        if (
                            !HasCorradePermission(group,
                                (int) Permissions.PERMISSION_GROOMING))
                        {
                            throw new Exception(GetEnumDescription(ScriptError.NO_CORRADE_PERMISSIONS));
                        }
                        ManualResetEvent AvatarPicksReplyEvent = new ManualResetEvent(false);
                        string pickName = Uri.UnescapeDataString(wasKeyValueGet(ScriptKeys.NAME, message));
                        if (string.IsNullOrEmpty(pickName))
                        {
                            throw new Exception(GetEnumDescription(ScriptError.EMPTY_PICK_NAME));
                        }
                        UUID pickUUID = UUID.Zero;
                        EventHandler<AvatarPicksReplyEventArgs> AvatarPicksEventHandler = (sender, args) =>
                        {
                            pickUUID =
                                args.Picks.FirstOrDefault(
                                    pick => pick.Value.Equals(pickName, StringComparison.InvariantCulture)).Key;
                            AvatarPicksReplyEvent.Set();
                        };
                        Client.Avatars.AvatarPicksReply += AvatarPicksEventHandler;
                        Client.Avatars.RequestAvatarPicks(Client.Self.AgentID);
                        if (!AvatarPicksReplyEvent.WaitOne(Configuration.SERVICES_TIMEOUT, false))
                        {
                            Client.Avatars.AvatarPicksReply -= AvatarPicksEventHandler;
                            throw new Exception(GetEnumDescription(ScriptError.TIMEOUT_GETTING_PICKS));
                        }
                        Client.Avatars.AvatarPicksReply -= AvatarPicksEventHandler;
                        if (pickUUID.Equals(UUID.Zero))
                        {
                            pickUUID = UUID.Random();
                        }
                        Client.Self.PickDelete(pickUUID);
                    };
                    break;
                case ScriptKeys.TOUCH:
                    execute = () =>
                    {
                        if (
                            !HasCorradePermission(group, (int) Permissions.PERMISSION_INTERACT))
                        {
                            throw new Exception(GetEnumDescription(ScriptError.NO_CORRADE_PERMISSIONS));
                        }
                        float range;
                        if (
                            !float.TryParse(Uri.UnescapeDataString(wasKeyValueGet(ScriptKeys.RANGE, message)), out range))
                        {
                            throw new Exception(GetEnumDescription(ScriptError.NO_RANGE_PROVIDED));
                        }
                        Primitive primitive = null;
                        if (
                            !FindPrimitive(Uri.UnescapeDataString(wasKeyValueGet(ScriptKeys.ITEM, message)), range,
                                Configuration.SERVICES_TIMEOUT,
                                ref primitive))
                        {
                            throw new Exception(GetEnumDescription(ScriptError.PRIMITIVE_NOT_FOUND));
                        }
                        Client.Self.Touch(primitive.LocalID);
                    };
                    break;
                case ScriptKeys.MODERATE:
                    execute = () =>
                    {
                        UUID groupUUID =
                            Configuration.GROUPS.FirstOrDefault(
                                g => g.Name.Equals(group, StringComparison.InvariantCulture)).UUID;
                        if (groupUUID.Equals(UUID.Zero) &&
                            !GroupNameToUUID(group, Configuration.SERVICES_TIMEOUT, ref groupUUID))
                        {
                            throw new Exception(GetEnumDescription(ScriptError.GROUP_NOT_FOUND));
                        }
                        if (
                            !HasGroupPowers(Client.Self.AgentID, groupUUID, GroupPowers.ModerateChat,
                                Configuration.SERVICES_TIMEOUT))
                        {
                            throw new Exception(GetEnumDescription(ScriptError.NO_GROUP_POWER_FOR_COMMAND));
                        }
                        UUID agentUUID = UUID.Zero;
                        if (
                            !AgentNameToUUID(Uri.UnescapeDataString(wasKeyValueGet(ScriptKeys.FIRSTNAME, message)),
                                Uri.UnescapeDataString(wasKeyValueGet(ScriptKeys.LASTNAME, message)),
                                Configuration.SERVICES_TIMEOUT, ref agentUUID))
                        {
                            throw new Exception(GetEnumDescription(ScriptError.AGENT_NOT_FOUND));
                        }
                        if (!AgentInGroup(agentUUID, groupUUID, Configuration.SERVICES_TIMEOUT))
                        {
                            throw new Exception(GetEnumDescription(ScriptError.AGENT_NOT_IN_GROUP));
                        }
                        bool silence;
                        if (
                            !bool.TryParse(Uri.UnescapeDataString(wasKeyValueGet(ScriptKeys.SILENCE, message)),
                                out silence))
                        {
                            silence = false;
                        }
                        string type =
                            Uri.UnescapeDataString(wasKeyValueGet(ScriptKeys.TYPE, message))
                                .ToLower(CultureInfo.InvariantCulture);
                        switch (type)
                        {
                            case Type.TEXT:
                            case Type.VOICE:
                                Client.Self.ModerateChatSessions(groupUUID, agentUUID, type, silence);
                                break;
                            default:
                                throw new Exception(GetEnumDescription(ScriptError.TYPE_CAN_BE_VOICE_OR_TEXT));
                        }
                    };
                    break;
                case ScriptKeys.REBAKE:
                    execute = () =>
                    {
                        if (
                            !HasCorradePermission(group,
                                (int) Permissions.PERMISSION_GROOMING))
                        {
                            throw new Exception(GetEnumDescription(ScriptError.NO_CORRADE_PERMISSIONS));
                        }
                        Client.Appearance.RequestSetAppearance(true);
                    };
                    break;
                case ScriptKeys.GETATTACHMENTS:
                    execute = () =>
                    {
                        if (
                            !HasCorradePermission(group,
                                (int) Permissions.PERMISSION_GROOMING))
                        {
                            throw new Exception(GetEnumDescription(ScriptError.NO_CORRADE_PERMISSIONS));
                        }
                        List<KeyValuePair<AttachmentPoint, Primitive>> attachments =
                            new List<KeyValuePair<AttachmentPoint, Primitive>>();
                        if (!GetAttachments(Configuration.SERVICES_TIMEOUT, ref attachments))
                        {
                            throw new Exception(GetEnumDescription(ScriptError.FAILED_TO_GET_ATTACHMENTS));
                        }
                        List<string> csv = new List<string>();
                        object LockObject = new object();
                        Parallel.ForEach(attachments, attachment =>
                        {
                            lock (LockObject)
                            {
                                csv.Add(attachment.Key.ToString());
                                csv.Add(attachment.Value.Properties.Name);
                            }
                        });
                        result.Add(ResultKeys.ATTACHMENTS,
                            string.Join(LINDEN_CONSTANTS.LSL.CSV_DELIMITER, csv.ToArray()));
                    };
                    break;
                case ScriptKeys.ATTACH:
                    execute = () =>
                    {
                        if (
                            !HasCorradePermission(group,
                                (int) Permissions.PERMISSION_GROOMING))
                        {
                            throw new Exception(GetEnumDescription(ScriptError.NO_CORRADE_PERMISSIONS));
                        }
                        string attachments = Uri.UnescapeDataString(wasKeyValueGet(ScriptKeys.ATTACHMENTS, message));
                        if (string.IsNullOrEmpty(attachments))
                        {
                            throw new Exception(GetEnumDescription(ScriptError.EMPTY_ATTACHMENTS));
                        }
                        Dictionary<string, string> pointAttachments =
                            Regex.Matches(attachments, @"\s*(?<key>.+?)\s*,\s*(?<value>.+?)\s*(,|$)",
                                RegexOptions.Compiled)
                                .Cast<Match>()
                                .ToDictionary(m => m.Groups["key"].Value, m => m.Groups["value"].Value);
                        Parallel.ForEach(pointAttachments,
                            pair =>
                                Parallel.ForEach(
                                    typeof (AttachmentPoint).GetFields(BindingFlags.Public | BindingFlags.Static)
                                        .Where(
                                            a =>
                                                a.Name.Equals(pair.Key, StringComparison.InvariantCultureIgnoreCase)),
                                    point =>
                                    {
                                        InventoryItem item =
                                            SearchInventoryItem(Client.Inventory.Store.RootFolder, pair.Value,
                                                Configuration.SERVICES_TIMEOUT).FirstOrDefault();
                                        if (item == null)
                                            return;
                                        Client.Appearance.Attach(item, (AttachmentPoint) point.GetValue(null),
                                            true);
                                    }));
                    };
                    break;
                case ScriptKeys.DETACH:
                    execute = () =>
                    {
                        if (
                            !HasCorradePermission(group,
                                (int) Permissions.PERMISSION_GROOMING))
                        {
                            throw new Exception(GetEnumDescription(ScriptError.NO_CORRADE_PERMISSIONS));
                        }
                        string attachments = Uri.UnescapeDataString(wasKeyValueGet(ScriptKeys.ATTACHMENTS, message));
                        if (string.IsNullOrEmpty(attachments))
                        {
                            throw new Exception(GetEnumDescription(ScriptError.EMPTY_ATTACHMENTS));
                        }
                        Parallel.ForEach(
                            attachments.Split(new[] {LINDEN_CONSTANTS.LSL.CSV_DELIMITER},
                                StringSplitOptions.RemoveEmptyEntries), attachment =>
                                {
                                    InventoryItem item =
                                        SearchInventoryItem(Client.Inventory.Store.RootFolder, attachment.Trim(),
                                            Configuration.SERVICES_TIMEOUT).FirstOrDefault();
                                    if (item != null)
                                    {
                                        Client.Appearance.Detach(item);
                                    }
                                });
                    };
                    break;
                case ScriptKeys.RETURNPRIMITIVES:
                    execute = () =>
                    {
                        if (!HasCorradePermission(group, (int) Permissions.PERMISSION_LAND))
                        {
                            throw new Exception(GetEnumDescription(ScriptError.NO_CORRADE_PERMISSIONS));
                        }
                        UUID agentUUID = UUID.Zero;
                        if (
                            !AgentNameToUUID(Uri.UnescapeDataString(wasKeyValueGet(ScriptKeys.FIRSTNAME, message)),
                                Uri.UnescapeDataString(wasKeyValueGet(ScriptKeys.LASTNAME, message)),
                                Configuration.SERVICES_TIMEOUT, ref agentUUID))
                        {
                            throw new Exception(GetEnumDescription(ScriptError.AGENT_NOT_FOUND));
                        }
                        string type = Uri.UnescapeDataString(wasKeyValueGet(ScriptKeys.TYPE, message))
                            .ToLower(CultureInfo.InvariantCulture);
                        switch (
                            Uri.UnescapeDataString(wasKeyValueGet(ScriptKeys.ENTITY, message))
                                .ToLower(CultureInfo.InvariantCulture))
                        {
                            case Entity.PARCEL:
                                Vector3 position;
                                HashSet<Parcel> parcels = new HashSet<Parcel>();
                                switch (Vector3.TryParse(
                                    Uri.UnescapeDataString(wasKeyValueGet(ScriptKeys.POSITION, message)),
                                    out position))
                                {
                                    case false:
                                        Client.Network.CurrentSim.Parcels.ForEach(p => parcels.Add(p));
                                        break;
                                    case true:
                                        Parcel parcel = null;
                                        if (!GetParcelAtPosition(Client.Network.CurrentSim, position,
                                            Configuration.SERVICES_TIMEOUT, ref parcel))
                                        {
                                            throw new Exception(GetEnumDescription(ScriptError.COULD_NOT_FIND_PARCEL));
                                        }
                                        parcels.Add(parcel);
                                        break;
                                }
                                Parallel.ForEach(parcels,
                                    parcel =>
                                        Client.Parcels.ReturnObjects(Client.Network.CurrentSim, parcel.LocalID,
                                            !string.IsNullOrEmpty(type)
                                                ? (ObjectReturnType)
                                                    typeof (ObjectReturnType).GetFields(BindingFlags.Public |
                                                                                        BindingFlags.Static)
                                                        .FirstOrDefault(
                                                            t =>
                                                                t.Name.Equals(type,
                                                                    StringComparison.InvariantCultureIgnoreCase))
                                                        .GetValue(null)
                                                : ObjectReturnType.Other, new List<UUID> {agentUUID}));
                                break;
                            case Entity.ESTATE:
                                bool allEstates;
                                if (
                                    !bool.TryParse(Uri.UnescapeDataString(wasKeyValueGet(ScriptKeys.ALL, message)),
                                        out allEstates))
                                {
                                    allEstates = false;
                                }
                                Client.Estate.SimWideReturn(agentUUID, !string.IsNullOrEmpty(type)
                                    ? (EstateTools.EstateReturnFlags)
                                        typeof (ObjectReturnType).GetFields(BindingFlags.Public | BindingFlags.Static)
                                            .FirstOrDefault(
                                                t =>
                                                    t.Name.Equals(type,
                                                        StringComparison.InvariantCultureIgnoreCase))
                                            .GetValue(null)
                                    : EstateTools.EstateReturnFlags.ReturnScriptedAndOnOthers, allEstates);
                                break;
                            default:
                                throw new Exception(GetEnumDescription(ScriptError.UNKNOWN_ENTITY));
                        }
                    };
                    break;
                case ScriptKeys.GETPRIMITIVEOWNERS:
                    execute = () =>
                    {
                        if (!HasCorradePermission(group, (int) Permissions.PERMISSION_LAND))
                        {
                            throw new Exception(GetEnumDescription(ScriptError.NO_CORRADE_PERMISSIONS));
                        }
                        Vector3 position;
                        HashSet<Parcel> parcels = new HashSet<Parcel>();
                        switch (Vector3.TryParse(
                            Uri.UnescapeDataString(wasKeyValueGet(ScriptKeys.POSITION, message)),
                            out position))
                        {
                            case false:
                                Client.Network.CurrentSim.Parcels.ForEach(p => parcels.Add(p));
                                break;
                            case true:
                                Parcel parcel = null;
                                if (!GetParcelAtPosition(Client.Network.CurrentSim, position,
                                    Configuration.SERVICES_TIMEOUT, ref parcel))
                                {
                                    throw new Exception(GetEnumDescription(ScriptError.COULD_NOT_FIND_PARCEL));
                                }
                                parcels.Add(parcel);
                                break;
                        }
                        ManualResetEvent ParcelObjectOwnersReplyEvent = new ManualResetEvent(false);
                        Dictionary<string, int> primitives = new Dictionary<string, int>();
                        EventHandler<ParcelObjectOwnersReplyEventArgs> ParcelObjectOwnersEventHandler =
                            (sender, args) =>
                            {
                                //object LockObject = new object();
                                foreach (ParcelManager.ParcelPrimOwners primowners in args.PrimOwners)
                                {
                                    string owner = string.Empty;
                                    if (!AgentUUIDToName(primowners.OwnerID, Configuration.SERVICES_TIMEOUT, ref owner))
                                        return;
                                    if (!primitives.ContainsKey(owner))
                                    {
                                        primitives.Add(owner, primowners.Count);
                                        continue;
                                    }
                                    primitives[owner] += primowners.Count;
                                }
                                ParcelObjectOwnersReplyEvent.Set();
                            };
                        foreach (Parcel parcel in parcels)
                        {
                            Client.Parcels.ParcelObjectOwnersReply += ParcelObjectOwnersEventHandler;
                            Client.Parcels.RequestObjectOwners(Client.Network.CurrentSim, parcel.LocalID);
                            if (!ParcelObjectOwnersReplyEvent.WaitOne(Configuration.SERVICES_TIMEOUT, false))
                            {
                                Client.Parcels.ParcelObjectOwnersReply -= ParcelObjectOwnersEventHandler;
                                throw new Exception(GetEnumDescription(ScriptError.COULD_NOT_GET_LAND_USERS));
                            }
                            Client.Parcels.ParcelObjectOwnersReply -= ParcelObjectOwnersEventHandler;
                        }
                        if (primitives.Count == 0)
                        {
                            throw new Exception(GetEnumDescription(ScriptError.COULD_NOT_GET_LAND_USERS));
                        }
                        result.Add(ResultKeys.OWNERS,
                            string.Join(LINDEN_CONSTANTS.LSL.CSV_DELIMITER,
                                primitives.Select(
                                    p =>
                                        string.Join(LINDEN_CONSTANTS.LSL.CSV_DELIMITER,
                                            new[] {p.Key, p.Value.ToString(CultureInfo.InvariantCulture)}))
                                    .ToArray()
                                ));
                    };
                    break;
                case ScriptKeys.GETGROUPDATA:
                    execute = () =>
                    {
                        UUID groupUUID =
                            Configuration.GROUPS.FirstOrDefault(
                                g => g.Name.Equals(group, StringComparison.InvariantCulture)).UUID;
                        if (groupUUID.Equals(UUID.Zero) &&
                            !GroupNameToUUID(group, Configuration.SERVICES_TIMEOUT, ref groupUUID))
                        {
                            throw new Exception(GetEnumDescription(ScriptError.GROUP_NOT_FOUND));
                        }
                        OpenMetaverse.Group dataGroup = new OpenMetaverse.Group();
                        if (!RequestGroup(groupUUID, Configuration.SERVICES_TIMEOUT, ref dataGroup))
                        {
                            throw new Exception(GetEnumDescription(ScriptError.GROUP_NOT_FOUND));
                        }
                        string fields = Uri.UnescapeDataString(wasKeyValueGet(ScriptKeys.DATA, message));
                        List<string> csv = new List<string>();
                        object LockObject = new object();
                        Parallel.ForEach(
                            fields.Split(new[] {LINDEN_CONSTANTS.LSL.CSV_DELIMITER},
                                StringSplitOptions.RemoveEmptyEntries),
                            name =>
                            {
                                KeyValuePair<FieldInfo, object> fi = wasGetFields(dataGroup, dataGroup.GetType().Name)
                                    .FirstOrDefault(
                                        o => o.Key.Name.Equals(name, StringComparison.InvariantCultureIgnoreCase));

                                lock (LockObject)
                                {
                                    List<string> data = new List<string> {name};
                                    data.AddRange(wasGetInfo(fi.Key, fi.Value));
                                    if (data.Count >= 2)
                                    {
                                        csv.AddRange(data);
                                    }
                                }

                                KeyValuePair<PropertyInfo, object> pi =
                                    wasGetProperties(dataGroup, dataGroup.GetType().Name)
                                        .FirstOrDefault(
                                            o => o.Key.Name.Equals(name, StringComparison.InvariantCultureIgnoreCase));

                                lock (LockObject)
                                {
                                    List<string> data = new List<string> {name};
                                    data.AddRange(wasGetInfo(pi.Key, pi.Value));
                                    if (data.Count >= 2)
                                    {
                                        csv.AddRange(data);
                                    }
                                }
                            });
                        result.Add(ResultKeys.DATA, string.Join(LINDEN_CONSTANTS.LSL.CSV_DELIMITER, csv.ToArray()));
                    };
                    break;
                case ScriptKeys.GETPRIMITIVEDATA:
                    execute = () =>
                    {
                        if (
                            !HasCorradePermission(group,
                                (int) Permissions.PERMISSION_INTERACT))
                        {
                            throw new Exception(GetEnumDescription(ScriptError.NO_CORRADE_PERMISSIONS));
                        }
                        float range;
                        if (
                            !float.TryParse(Uri.UnescapeDataString(wasKeyValueGet(ScriptKeys.RANGE, message)), out range))
                        {
                            throw new Exception(GetEnumDescription(ScriptError.NO_RANGE_PROVIDED));
                        }
                        Primitive primitive = null;
                        if (
                            !FindPrimitive(Uri.UnescapeDataString(wasKeyValueGet(ScriptKeys.ITEM, message)), range,
                                Configuration.SERVICES_TIMEOUT,
                                ref primitive))
                        {
                            throw new Exception(GetEnumDescription(ScriptError.PRIMITIVE_NOT_FOUND));
                        }
                        string fields = Uri.UnescapeDataString(wasKeyValueGet(ScriptKeys.DATA, message));
                        List<string> csv = new List<string>();
                        object LockObject = new object();
                        Parallel.ForEach(
                            fields.Split(new[] {LINDEN_CONSTANTS.LSL.CSV_DELIMITER},
                                StringSplitOptions.RemoveEmptyEntries),
                            name =>
                            {
                                KeyValuePair<FieldInfo, object> fi = wasGetFields(primitive, primitive.GetType().Name)
                                    .FirstOrDefault(
                                        o => o.Key.Name.Equals(name, StringComparison.InvariantCultureIgnoreCase));

                                lock (LockObject)
                                {
                                    List<string> data = new List<string> {name};
                                    data.AddRange(wasGetInfo(fi.Key, fi.Value));
                                    if (data.Count >= 2)
                                    {
                                        csv.AddRange(data);
                                    }
                                }

                                KeyValuePair<PropertyInfo, object> pi =
                                    wasGetProperties(primitive, primitive.GetType().Name)
                                        .FirstOrDefault(
                                            o => o.Key.Name.Equals(name, StringComparison.InvariantCultureIgnoreCase));

                                lock (LockObject)
                                {
                                    List<string> data = new List<string> {name};
                                    data.AddRange(wasGetInfo(pi.Key, pi.Value));
                                    if (data.Count >= 2)
                                    {
                                        csv.AddRange(data);
                                    }
                                }
                            });
                        result.Add(ResultKeys.DATA, string.Join(LINDEN_CONSTANTS.LSL.CSV_DELIMITER, csv.ToArray()));
                    };
                    break;
                case ScriptKeys.GETPARCELDATA:
                    execute = () =>
                    {
                        if (!HasCorradePermission(group, (int) Permissions.PERMISSION_LAND))
                        {
                            throw new Exception(GetEnumDescription(ScriptError.NO_CORRADE_PERMISSIONS));
                        }
                        Vector3 position;
                        if (
                            !Vector3.TryParse(Uri.UnescapeDataString(wasKeyValueGet(ScriptKeys.POSITION, message)),
                                out position))
                        {
                            position = Client.Self.SimPosition;
                        }
                        Parcel parcel = null;
                        if (
                            !GetParcelAtPosition(Client.Network.CurrentSim, position,
                                Configuration.SERVICES_TIMEOUT, ref parcel))
                        {
                            throw new Exception(GetEnumDescription(ScriptError.COULD_NOT_FIND_PARCEL));
                        }
                        string fields = Uri.UnescapeDataString(wasKeyValueGet(ScriptKeys.DATA, message));
                        List<string> csv = new List<string>();
                        object LockObject = new object();
                        Parallel.ForEach(
                            fields.Split(new[] {LINDEN_CONSTANTS.LSL.CSV_DELIMITER},
                                StringSplitOptions.RemoveEmptyEntries),
                            name =>
                            {
                                KeyValuePair<FieldInfo, object> fi = wasGetFields(parcel, parcel.GetType().Name)
                                    .FirstOrDefault(
                                        o => o.Key.Name.Equals(name, StringComparison.InvariantCultureIgnoreCase));

                                lock (LockObject)
                                {
                                    List<string> data = new List<string> {name};
                                    data.AddRange(wasGetInfo(fi.Key, fi.Value));
                                    if (data.Count >= 2)
                                    {
                                        csv.AddRange(data);
                                    }
                                }

                                KeyValuePair<PropertyInfo, object> pi =
                                    wasGetProperties(parcel, parcel.GetType().Name)
                                        .FirstOrDefault(
                                            o => o.Key.Name.Equals(name, StringComparison.InvariantCultureIgnoreCase));

                                lock (LockObject)
                                {
                                    List<string> data = new List<string> {name};
                                    data.AddRange(wasGetInfo(pi.Key, pi.Value));
                                    if (data.Count >= 2)
                                    {
                                        csv.AddRange(data);
                                    }
                                }
                            });
                        result.Add(ResultKeys.DATA, string.Join(LINDEN_CONSTANTS.LSL.CSV_DELIMITER, csv.ToArray()));
                    };
                    break;
                case ScriptKeys.REZ:
                    execute = () =>
                    {
                        if (!HasCorradePermission(group, (int) Permissions.PERMISSION_INVENTORY))
                        {
                            throw new Exception(
                                GetEnumDescription(ScriptError.NO_CORRADE_PERMISSIONS));
                        }
                        UUID groupUUID =
                            Configuration.GROUPS.FirstOrDefault(
                                g => g.Name.Equals(group, StringComparison.InvariantCulture)).UUID;
                        if (groupUUID.Equals(UUID.Zero) &&
                            !GroupNameToUUID(group, Configuration.SERVICES_TIMEOUT, ref groupUUID))
                        {
                            throw new Exception(GetEnumDescription(ScriptError.GROUP_NOT_FOUND));
                        }
                        if (
                            !HasGroupPowers(Client.Self.AgentID, groupUUID, GroupPowers.AllowRez,
                                Configuration.SERVICES_TIMEOUT))
                        {
                            throw new Exception(GetEnumDescription(ScriptError.NO_GROUP_POWER_FOR_COMMAND));
                        }
                        InventoryItem item =
                            SearchInventoryItem(Client.Inventory.Store.RootFolder,
                                Uri.UnescapeDataString(wasKeyValueGet(ScriptKeys.ITEM, message)),
                                Configuration.SERVICES_TIMEOUT).FirstOrDefault();
                        if (item == null)
                        {
                            throw new Exception(GetEnumDescription(ScriptError.INVENTORY_ITEM_NOT_FOUND));
                        }
                        Vector3 position;
                        if (
                            !Vector3.TryParse(Uri.UnescapeDataString(wasKeyValueGet(ScriptKeys.POSITION, message)),
                                out position))
                        {
                            throw new Exception(GetEnumDescription(ScriptError.INVALID_POSITION));
                        }
                        Quaternion rotation;
                        if (
                            !Quaternion.TryParse(Uri.UnescapeDataString(wasKeyValueGet(ScriptKeys.ROTATION, message)),
                                out rotation))
                        {
                            throw new Exception(GetEnumDescription(ScriptError.INVALID_ROTATION));
                        }
                        Client.Inventory.RequestRezFromInventory(Client.Network.CurrentSim, rotation, position, item,
                            groupUUID);
                    };
                    break;
                case ScriptKeys.DEREZ:
                    execute = () =>
                    {
                        if (!HasCorradePermission(group, (int) Permissions.PERMISSION_INVENTORY))
                        {
                            throw new Exception(
                                GetEnumDescription(ScriptError.NO_CORRADE_PERMISSIONS));
                        }
                        float range;
                        if (
                            !float.TryParse(Uri.UnescapeDataString(wasKeyValueGet(ScriptKeys.RANGE, message)), out range))
                        {
                            throw new Exception(GetEnumDescription(ScriptError.NO_RANGE_PROVIDED));
                        }
                        Primitive primitive = null;
                        if (
                            !FindPrimitive(Uri.UnescapeDataString(wasKeyValueGet(ScriptKeys.ITEM, message)), range,
                                Configuration.SERVICES_TIMEOUT,
                                ref primitive))
                        {
                            throw new Exception(GetEnumDescription(ScriptError.PRIMITIVE_NOT_FOUND));
                        }
                        Client.Inventory.RequestDeRezToInventory(primitive.LocalID);
                    };
                    break;
                case ScriptKeys.SETSCRIPTRUNNING:
                    execute = () =>
                    {
                        if (!HasCorradePermission(group, (int) Permissions.PERMISSION_INTERACT))
                        {
                            throw new Exception(
                                GetEnumDescription(ScriptError.NO_CORRADE_PERMISSIONS));
                        }
                        float range;
                        if (
                            !float.TryParse(Uri.UnescapeDataString(wasKeyValueGet(ScriptKeys.RANGE, message)), out range))
                        {
                            throw new Exception(GetEnumDescription(ScriptError.NO_RANGE_PROVIDED));
                        }
                        string entity = Uri.UnescapeDataString(wasKeyValueGet(ScriptKeys.ENTITY, message));
                        UUID entityUUID;
                        if (!UUID.TryParse(entity, out entityUUID))
                        {
                            if (string.IsNullOrEmpty(entity))
                            {
                                throw new Exception(GetEnumDescription(ScriptError.UNKNOWN_ENTITY));
                            }
                            entityUUID = UUID.Zero;
                        }
                        Primitive primitive = null;
                        if (
                            !FindPrimitive(Uri.UnescapeDataString(wasKeyValueGet(ScriptKeys.ITEM, message)), range,
                                Configuration.SERVICES_TIMEOUT,
                                ref primitive))
                        {
                            throw new Exception(GetEnumDescription(ScriptError.PRIMITIVE_NOT_FOUND));
                        }
                        List<InventoryBase> inventory =
                            Client.Inventory.GetTaskInventory(primitive.ID, primitive.LocalID,
                                Configuration.SERVICES_TIMEOUT).ToList();
                        InventoryItem item = !entityUUID.Equals(UUID.Zero)
                            ? inventory.FirstOrDefault(o => o.UUID.Equals(entityUUID)) as InventoryItem
                            : inventory.FirstOrDefault(o => o.Name.Equals(entity)) as InventoryItem;
                        if (item == null)
                        {
                            throw new Exception(GetEnumDescription(ScriptError.INVENTORY_ITEM_NOT_FOUND));
                        }
                        switch (item.AssetType)
                        {
                            case AssetType.LSLBytecode:
                            case AssetType.LSLText:
                                break;
                            default:
                                throw new Exception(GetEnumDescription(ScriptError.ITEM_IS_NOT_A_SCRIPT));
                        }
                        string action = Uri.UnescapeDataString(wasKeyValueGet(ScriptKeys.ACTION, message))
                            .ToLower(CultureInfo.InvariantCulture);
                        switch (action)
                        {
                            case Action.START:
                                Client.Inventory.RequestSetScriptRunning(primitive.ID, item.UUID, true);
                                break;
                            case Action.STOP:
                                Client.Inventory.RequestSetScriptRunning(primitive.ID, item.UUID, false);
                                break;
                            default:
                                throw new Exception(GetEnumDescription(ScriptError.INVALID_ACTION));
                        }
                        ManualResetEvent ScriptRunningReplyEvent = new ManualResetEvent(false);
                        bool succeeded = false;
                        EventHandler<ScriptRunningReplyEventArgs> ScriptRunningEventHandler = (sender, args) =>
                        {
                            switch (action)
                            {
                                case Action.START:
                                    succeeded = args.IsRunning;
                                    break;
                                case Action.STOP:
                                    succeeded = !args.IsRunning;
                                    break;
                            }
                            ScriptRunningReplyEvent.Set();
                        };
                        Client.Inventory.ScriptRunningReply += ScriptRunningEventHandler;
                        Client.Inventory.RequestGetScriptRunning(primitive.ID, item.UUID);
                        if (!ScriptRunningReplyEvent.WaitOne(Configuration.SERVICES_TIMEOUT, false))
                        {
                            Client.Inventory.ScriptRunningReply -= ScriptRunningEventHandler;
                            throw new Exception(GetEnumDescription(ScriptError.COULD_NOT_GET_SCRIPT_STATE));
                        }
                        Client.Inventory.ScriptRunningReply -= ScriptRunningEventHandler;
                        if (!succeeded)
                        {
                            throw new Exception(GetEnumDescription(ScriptError.COULD_NOT_SET_SCRIPT_STATE));
                        }
                    };
                    break;
                case ScriptKeys.GETSCRIPTRUNNING:
                    execute = () =>
                    {
                        if (!HasCorradePermission(group, (int) Permissions.PERMISSION_INTERACT))
                        {
                            throw new Exception(
                                GetEnumDescription(ScriptError.NO_CORRADE_PERMISSIONS));
                        }
                        float range;
                        if (
                            !float.TryParse(Uri.UnescapeDataString(wasKeyValueGet(ScriptKeys.RANGE, message)), out range))
                        {
                            throw new Exception(GetEnumDescription(ScriptError.NO_RANGE_PROVIDED));
                        }
                        string entity = Uri.UnescapeDataString(wasKeyValueGet(ScriptKeys.ENTITY, message));
                        UUID entityUUID;
                        if (!UUID.TryParse(entity, out entityUUID))
                        {
                            if (string.IsNullOrEmpty(entity))
                            {
                                throw new Exception(GetEnumDescription(ScriptError.UNKNOWN_ENTITY));
                            }
                            entityUUID = UUID.Zero;
                        }
                        Primitive primitive = null;
                        if (
                            !FindPrimitive(Uri.UnescapeDataString(wasKeyValueGet(ScriptKeys.ITEM, message)), range,
                                Configuration.SERVICES_TIMEOUT,
                                ref primitive))
                        {
                            throw new Exception(GetEnumDescription(ScriptError.PRIMITIVE_NOT_FOUND));
                        }
                        List<InventoryBase> inventory =
                            Client.Inventory.GetTaskInventory(primitive.ID, primitive.LocalID,
                                Configuration.SERVICES_TIMEOUT).ToList();
                        InventoryItem item = !entityUUID.Equals(UUID.Zero)
                            ? inventory.FirstOrDefault(o => o.UUID.Equals(entityUUID)) as InventoryItem
                            : inventory.FirstOrDefault(o => o.Name.Equals(entity)) as InventoryItem;
                        if (item == null)
                        {
                            throw new Exception(GetEnumDescription(ScriptError.INVENTORY_ITEM_NOT_FOUND));
                        }
                        switch (item.AssetType)
                        {
                            case AssetType.LSLBytecode:
                            case AssetType.LSLText:
                                break;
                            default:
                                throw new Exception(GetEnumDescription(ScriptError.ITEM_IS_NOT_A_SCRIPT));
                        }
                        ManualResetEvent ScriptRunningReplyEvent = new ManualResetEvent(false);
                        bool running = false;
                        EventHandler<ScriptRunningReplyEventArgs> ScriptRunningEventHandler = (sender, args) =>
                        {
                            running = args.IsRunning;
                            ScriptRunningReplyEvent.Set();
                        };
                        Client.Inventory.ScriptRunningReply += ScriptRunningEventHandler;
                        Client.Inventory.RequestGetScriptRunning(primitive.ID, item.UUID);
                        if (!ScriptRunningReplyEvent.WaitOne(Configuration.SERVICES_TIMEOUT, false))
                        {
                            Client.Inventory.ScriptRunningReply -= ScriptRunningEventHandler;
                            throw new Exception(GetEnumDescription(ScriptError.COULD_NOT_GET_SCRIPT_STATE));
                        }
                        Client.Inventory.ScriptRunningReply -= ScriptRunningEventHandler;
                        result.Add(ResultKeys.RUNNING, running.ToString());
                    };
                    break;
                case ScriptKeys.GETPRIMITIVEINVENTORY:
                    execute = () =>
                    {
                        if (!HasCorradePermission(group, (int) Permissions.PERMISSION_INTERACT))
                        {
                            throw new Exception(
                                GetEnumDescription(ScriptError.NO_CORRADE_PERMISSIONS));
                        }
                        float range;
                        if (
                            !float.TryParse(Uri.UnescapeDataString(wasKeyValueGet(ScriptKeys.RANGE, message)), out range))
                        {
                            throw new Exception(GetEnumDescription(ScriptError.NO_RANGE_PROVIDED));
                        }
                        Primitive primitive = null;
                        if (
                            !FindPrimitive(Uri.UnescapeDataString(wasKeyValueGet(ScriptKeys.ITEM, message)), range,
                                Configuration.SERVICES_TIMEOUT,
                                ref primitive))
                        {
                            throw new Exception(GetEnumDescription(ScriptError.PRIMITIVE_NOT_FOUND));
                        }
                        List<InventoryBase> inventory =
                            Client.Inventory.GetTaskInventory(primitive.ID, primitive.LocalID,
                                Configuration.SERVICES_TIMEOUT).ToList();
                        List<string> csv = new List<string>();
                        object LockObject = new object();
                        Parallel.ForEach(inventory, i =>
                        {
                            lock (LockObject)
                            {
                                csv.Add(i.Name);
                                csv.Add(i.UUID.ToString());
                            }
                        });
                        result.Add(ResultKeys.INVENTORY, string.Join(LINDEN_CONSTANTS.LSL.CSV_DELIMITER, csv.ToArray()));
                    };
                    break;
                case ScriptKeys.GETPRIMITIVEINVENTORYDATA:
                    execute = () =>
                    {
                        if (!HasCorradePermission(group, (int) Permissions.PERMISSION_INTERACT))
                        {
                            throw new Exception(
                                GetEnumDescription(ScriptError.NO_CORRADE_PERMISSIONS));
                        }
                        float range;
                        if (
                            !float.TryParse(Uri.UnescapeDataString(wasKeyValueGet(ScriptKeys.RANGE, message)), out range))
                        {
                            throw new Exception(GetEnumDescription(ScriptError.NO_RANGE_PROVIDED));
                        }
                        Primitive primitive = null;
                        if (
                            !FindPrimitive(Uri.UnescapeDataString(wasKeyValueGet(ScriptKeys.ITEM, message)), range,
                                Configuration.SERVICES_TIMEOUT,
                                ref primitive))
                        {
                            throw new Exception(GetEnumDescription(ScriptError.PRIMITIVE_NOT_FOUND));
                        }
                        string entity = Uri.UnescapeDataString(wasKeyValueGet(ScriptKeys.ENTITY, message));
                        UUID entityUUID;
                        if (!UUID.TryParse(entity, out entityUUID))
                        {
                            if (string.IsNullOrEmpty(entity))
                            {
                                throw new Exception(GetEnumDescription(ScriptError.UNKNOWN_ENTITY));
                            }
                            entityUUID = UUID.Zero;
                        }
                        List<InventoryBase> inventory =
                            Client.Inventory.GetTaskInventory(primitive.ID, primitive.LocalID,
                                Configuration.SERVICES_TIMEOUT).ToList();
                        InventoryItem item = !entityUUID.Equals(UUID.Zero)
                            ? inventory.FirstOrDefault(o => o.UUID.Equals(entityUUID)) as InventoryItem
                            : inventory.FirstOrDefault(o => o.Name.Equals(entity)) as InventoryItem;
                        if (item == null)
                        {
                            throw new Exception(GetEnumDescription(ScriptError.INVENTORY_ITEM_NOT_FOUND));
                        }
                        string fields = Uri.UnescapeDataString(wasKeyValueGet(ScriptKeys.DATA, message));
                        List<string> csv = new List<string>();
                        object LockObject = new object();
                        Parallel.ForEach(
                            fields.Split(new[] {LINDEN_CONSTANTS.LSL.CSV_DELIMITER},
                                StringSplitOptions.RemoveEmptyEntries),
                            name =>
                            {
                                KeyValuePair<FieldInfo, object> fi = wasGetFields(item, item.GetType().Name)
                                    .FirstOrDefault(
                                        o => o.Key.Name.Equals(name, StringComparison.InvariantCultureIgnoreCase));

                                lock (LockObject)
                                {
                                    List<string> data = new List<string> {name};
                                    data.AddRange(wasGetInfo(fi.Key, fi.Value));
                                    if (data.Count >= 2)
                                    {
                                        csv.AddRange(data);
                                    }
                                }

                                KeyValuePair<PropertyInfo, object> pi =
                                    wasGetProperties(item, item.GetType().Name)
                                        .FirstOrDefault(
                                            o =>
                                                o.Key.Name.Equals(name, StringComparison.InvariantCultureIgnoreCase));

                                lock (LockObject)
                                {
                                    List<string> data = new List<string> {name};
                                    data.AddRange(wasGetInfo(pi.Key, pi.Value));
                                    if (data.Count >= 2)
                                    {
                                        csv.AddRange(data);
                                    }
                                }
                            });
                        result.Add(ResultKeys.DATA, string.Join(LINDEN_CONSTANTS.LSL.CSV_DELIMITER, csv.ToArray()));
                    };
                    break;
                case ScriptKeys.GETINVENTORYDATA:
                    execute = () =>
                    {
                        if (!HasCorradePermission(group, (int) Permissions.PERMISSION_INVENTORY))
                        {
                            throw new Exception(
                                GetEnumDescription(ScriptError.NO_CORRADE_PERMISSIONS));
                        }
                        InventoryItem item =
                            SearchInventoryItem(Client.Inventory.Store.RootFolder,
                                Uri.UnescapeDataString(wasKeyValueGet(ScriptKeys.ITEM, message)),
                                Configuration.SERVICES_TIMEOUT).FirstOrDefault();
                        if (item == null)
                        {
                            throw new Exception(GetEnumDescription(ScriptError.INVENTORY_ITEM_NOT_FOUND));
                        }
                        string fields = Uri.UnescapeDataString(wasKeyValueGet(ScriptKeys.DATA, message));
                        List<string> csv = new List<string>();
                        object LockObject = new object();
                        Parallel.ForEach(
                            fields.Split(new[] {LINDEN_CONSTANTS.LSL.CSV_DELIMITER},
                                StringSplitOptions.RemoveEmptyEntries),
                            name =>
                            {
                                KeyValuePair<FieldInfo, object> fi = wasGetFields(item, item.GetType().Name)
                                    .FirstOrDefault(
                                        o => o.Key.Name.Equals(name, StringComparison.InvariantCultureIgnoreCase));

                                lock (LockObject)
                                {
                                    List<string> data = new List<string> {name};
                                    data.AddRange(wasGetInfo(fi.Key, fi.Value));
                                    if (data.Count >= 2)
                                    {
                                        csv.AddRange(data);
                                    }
                                }

                                KeyValuePair<PropertyInfo, object> pi =
                                    wasGetProperties(item, item.GetType().Name)
                                        .FirstOrDefault(
                                            o =>
                                                o.Key.Name.Equals(name, StringComparison.InvariantCultureIgnoreCase));

                                lock (LockObject)
                                {
                                    List<string> data = new List<string> {name};
                                    data.AddRange(wasGetInfo(pi.Key, pi.Value));
                                    if (data.Count >= 2)
                                    {
                                        csv.AddRange(data);
                                    }
                                }
                            });
                        result.Add(ResultKeys.DATA, string.Join(LINDEN_CONSTANTS.LSL.CSV_DELIMITER, csv.ToArray()));
                    };
                    break;
                case ScriptKeys.GETPARTICLESYSTEM:
                    execute = () =>
                    {
                        if (!HasCorradePermission(group, (int) Permissions.PERMISSION_INTERACT))
                        {
                            throw new Exception(GetEnumDescription(ScriptError.NO_CORRADE_PERMISSIONS));
                        }
                        float range;
                        if (
                            !float.TryParse(Uri.UnescapeDataString(wasKeyValueGet(ScriptKeys.RANGE, message)), out range))
                        {
                            throw new Exception(GetEnumDescription(ScriptError.NO_RANGE_PROVIDED));
                        }
                        Primitive primitive = null;
                        if (
                            !FindPrimitive(Uri.UnescapeDataString(wasKeyValueGet(ScriptKeys.ITEM, message)), range,
                                Configuration.SERVICES_TIMEOUT,
                                ref primitive))
                        {
                            throw new Exception(GetEnumDescription(ScriptError.PRIMITIVE_NOT_FOUND));
                        }
                        StringBuilder particleSystem = new StringBuilder();
                        particleSystem.Append("PSYS_PART_FLAGS, 0");
                        if ((primitive.ParticleSys.PartDataFlags &
                             Primitive.ParticleSystem.ParticleDataFlags.InterpColor) != 0)
                            particleSystem.Append(" | PSYS_PART_INTERP_COLOR_MASK");
                        if ((primitive.ParticleSys.PartDataFlags &
                             Primitive.ParticleSystem.ParticleDataFlags.InterpScale) != 0)
                            particleSystem.Append(" | PSYS_PART_INTERP_SCALE_MASK");
                        if ((primitive.ParticleSys.PartDataFlags & Primitive.ParticleSystem.ParticleDataFlags.Bounce) !=
                            0)
                            particleSystem.Append(" | PSYS_PART_BOUNCE_MASK");
                        if ((primitive.ParticleSys.PartDataFlags & Primitive.ParticleSystem.ParticleDataFlags.Wind) != 0)
                            particleSystem.Append(" | PSYS_PART_WIND_MASK");
                        if ((primitive.ParticleSys.PartDataFlags & Primitive.ParticleSystem.ParticleDataFlags.FollowSrc) !=
                            0)
                            particleSystem.Append(" | PSYS_PART_FOLLOW_SRC_MASK");
                        if ((primitive.ParticleSys.PartDataFlags &
                             Primitive.ParticleSystem.ParticleDataFlags.FollowVelocity) != 0)
                            particleSystem.Append(" | PSYS_PART_FOLLOW_VELOCITY_MASK");
                        if ((primitive.ParticleSys.PartDataFlags & Primitive.ParticleSystem.ParticleDataFlags.TargetPos) !=
                            0)
                            particleSystem.Append(" | PSYS_PART_TARGET_POS_MASK");
                        if ((primitive.ParticleSys.PartDataFlags &
                             Primitive.ParticleSystem.ParticleDataFlags.TargetLinear) != 0)
                            particleSystem.Append(" | PSYS_PART_TARGET_LINEAR_MASK");
                        if ((primitive.ParticleSys.PartDataFlags & Primitive.ParticleSystem.ParticleDataFlags.Emissive) !=
                            0)
                            particleSystem.Append(" | PSYS_PART_EMISSIVE_MASK");
                        particleSystem.Append(LINDEN_CONSTANTS.LSL.CSV_DELIMITER);
                        particleSystem.Append("PSYS_SRC_PATTERN, 0");
                        if (((long) primitive.ParticleSys.Pattern & (long) Primitive.ParticleSystem.SourcePattern.Drop) !=
                            0)
                            particleSystem.Append(" | PSYS_SRC_PATTERN_DROP");
                        if (((long) primitive.ParticleSys.Pattern &
                             (long) Primitive.ParticleSystem.SourcePattern.Explode) != 0)
                            particleSystem.Append(" | PSYS_SRC_PATTERN_EXPLODE");
                        if (((long) primitive.ParticleSys.Pattern & (long) Primitive.ParticleSystem.SourcePattern.Angle) !=
                            0)
                            particleSystem.Append(" | PSYS_SRC_PATTERN_ANGLE");
                        if (((long) primitive.ParticleSys.Pattern &
                             (long) Primitive.ParticleSystem.SourcePattern.AngleCone) != 0)
                            particleSystem.Append(" | PSYS_SRC_PATTERN_ANGLE_CONE");
                        if (((long) primitive.ParticleSys.Pattern &
                             (long) Primitive.ParticleSystem.SourcePattern.AngleConeEmpty) != 0)
                            particleSystem.Append(" | PSYS_SRC_PATTERN_ANGLE_CONE_EMPTY");
                        particleSystem.Append(LINDEN_CONSTANTS.LSL.CSV_DELIMITER);
                        particleSystem.Append("PSYS_PART_START_ALPHA, " +
                                              String.Format("{0:0.00000}", primitive.ParticleSys.PartStartColor.A) +
                                              LINDEN_CONSTANTS.LSL.CSV_DELIMITER);
                        particleSystem.Append("PSYS_PART_END_ALPHA, " +
                                              String.Format("{0:0.00000}", primitive.ParticleSys.PartEndColor.A) +
                                              LINDEN_CONSTANTS.LSL.CSV_DELIMITER);
                        particleSystem.Append("PSYS_PART_START_COLOR, " +
                                              primitive.ParticleSys.PartStartColor.ToRGBString() +
                                              LINDEN_CONSTANTS.LSL.CSV_DELIMITER);
                        particleSystem.Append("PSYS_PART_END_COLOR, " + primitive.ParticleSys.PartEndColor.ToRGBString() +
                                              LINDEN_CONSTANTS.LSL.CSV_DELIMITER);
                        particleSystem.Append("PSYS_PART_START_SCALE, <" +
                                              String.Format("{0:0.00000}", primitive.ParticleSys.PartStartScaleX) + ", " +
                                              String.Format("{0:0.00000}", primitive.ParticleSys.PartStartScaleY) +
                                              ", 0>, ");
                        particleSystem.Append("PSYS_PART_END_SCALE, <" +
                                              String.Format("{0:0.00000}", primitive.ParticleSys.PartEndScaleX) + ", " +
                                              String.Format("{0:0.00000}", primitive.ParticleSys.PartEndScaleY) +
                                              ", 0>, ");
                        particleSystem.Append("PSYS_PART_MAX_AGE, " +
                                              String.Format("{0:0.00000}", primitive.ParticleSys.PartMaxAge) +
                                              LINDEN_CONSTANTS.LSL.CSV_DELIMITER);
                        particleSystem.Append("PSYS_SRC_MAX_AGE, " +
                                              String.Format("{0:0.00000}", primitive.ParticleSys.MaxAge) +
                                              LINDEN_CONSTANTS.LSL.CSV_DELIMITER);
                        particleSystem.Append("PSYS_SRC_ACCEL, " + primitive.ParticleSys.PartAcceleration +
                                              LINDEN_CONSTANTS.LSL.CSV_DELIMITER);
                        particleSystem.Append("PSYS_SRC_BURST_PART_COUNT, " +
                                              String.Format("{0:0}", primitive.ParticleSys.BurstPartCount) +
                                              LINDEN_CONSTANTS.LSL.CSV_DELIMITER);
                        particleSystem.Append("PSYS_SRC_BURST_RADIUS, " +
                                              String.Format("{0:0.00000}", primitive.ParticleSys.BurstRadius) +
                                              LINDEN_CONSTANTS.LSL.CSV_DELIMITER);
                        particleSystem.Append("PSYS_SRC_BURST_RATE, " +
                                              String.Format("{0:0.00000}", primitive.ParticleSys.BurstRate) +
                                              LINDEN_CONSTANTS.LSL.CSV_DELIMITER);
                        particleSystem.Append("PSYS_SRC_BURST_SPEED_MIN, " +
                                              String.Format("{0:0.00000}", primitive.ParticleSys.BurstSpeedMin) +
                                              LINDEN_CONSTANTS.LSL.CSV_DELIMITER);
                        particleSystem.Append("PSYS_SRC_BURST_SPEED_MAX, " +
                                              String.Format("{0:0.00000}", primitive.ParticleSys.BurstSpeedMax) +
                                              LINDEN_CONSTANTS.LSL.CSV_DELIMITER);
                        particleSystem.Append("PSYS_SRC_INNERANGLE, " +
                                              String.Format("{0:0.00000}", primitive.ParticleSys.InnerAngle) +
                                              LINDEN_CONSTANTS.LSL.CSV_DELIMITER);
                        particleSystem.Append("PSYS_SRC_OUTERANGLE, " +
                                              String.Format("{0:0.00000}", primitive.ParticleSys.OuterAngle) +
                                              LINDEN_CONSTANTS.LSL.CSV_DELIMITER);
                        particleSystem.Append("PSYS_SRC_OMEGA, " + primitive.ParticleSys.AngularVelocity +
                                              LINDEN_CONSTANTS.LSL.CSV_DELIMITER);
                        particleSystem.Append("PSYS_SRC_TEXTURE, (key)\"" + primitive.ParticleSys.Texture + "\"" +
                                              LINDEN_CONSTANTS.LSL.CSV_DELIMITER);
                        particleSystem.Append("PSYS_SRC_TARGET_KEY, (key)\"" + primitive.ParticleSys.Target + "\"");
                        result.Add(ResultKeys.PARTICLESYSTEM, particleSystem.ToString());
                    };
                    break;
                case ScriptKeys.ACTIVATE:
                    execute = () =>
                    {
                        if (
                            !HasCorradePermission(group,
                                (int) Permissions.PERMISSION_GROOMING))
                        {
                            throw new Exception(GetEnumDescription(ScriptError.NO_CORRADE_PERMISSIONS));
                        }
                        UUID groupUUID =
                            Configuration.GROUPS.FirstOrDefault(
                                g => g.Name.Equals(group, StringComparison.InvariantCulture)).UUID;
                        if (groupUUID.Equals(UUID.Zero) &&
                            !GroupNameToUUID(group, Configuration.SERVICES_TIMEOUT, ref groupUUID))
                        {
                            throw new Exception(GetEnumDescription(ScriptError.GROUP_NOT_FOUND));
                        }
                        if (!AgentInGroup(Client.Self.AgentID, groupUUID, Configuration.SERVICES_TIMEOUT))
                        {
                            throw new Exception(GetEnumDescription(ScriptError.AGENT_NOT_IN_GROUP));
                        }
                        Client.Groups.ActivateGroup(groupUUID);
                    };
                    break;
                case ScriptKeys.SETTITLE:
                    execute = () =>
                    {
                        if (
                            !HasCorradePermission(group,
                                (int) Permissions.PERMISSION_GROOMING))
                        {
                            throw new Exception(GetEnumDescription(ScriptError.NO_CORRADE_PERMISSIONS));
                        }
                        UUID groupUUID =
                            Configuration.GROUPS.FirstOrDefault(
                                g => g.Name.Equals(group, StringComparison.InvariantCulture)).UUID;
                        if (groupUUID.Equals(UUID.Zero) &&
                            !GroupNameToUUID(group, Configuration.SERVICES_TIMEOUT, ref groupUUID))
                        {
                            throw new Exception(GetEnumDescription(ScriptError.GROUP_NOT_FOUND));
                        }
                        if (!AgentInGroup(Client.Self.AgentID, groupUUID, Configuration.SERVICES_TIMEOUT))
                        {
                            throw new Exception(GetEnumDescription(ScriptError.AGENT_NOT_IN_GROUP));
                        }
                        ManualResetEvent GroupRoleDataReplyEvent = new ManualResetEvent(false);
                        Dictionary<string, UUID> roleData = new Dictionary<string, UUID>();
                        EventHandler<GroupRolesDataReplyEventArgs> Groups_GroupRoleDataReply = (sender, args) =>
                        {
                            object LockObject = new object();
                            Parallel.ForEach(args.Roles, pair =>
                            {
                                lock (LockObject)
                                {
                                    roleData.Add(pair.Value.Title, pair.Value.ID);
                                }
                            });
                            GroupRoleDataReplyEvent.Set();
                        };
                        Client.Groups.GroupRoleDataReply += Groups_GroupRoleDataReply;
                        Client.Groups.RequestGroupRoles(groupUUID);
                        if (!GroupRoleDataReplyEvent.WaitOne(Configuration.SERVICES_TIMEOUT, false))
                        {
                            Client.Groups.GroupRoleDataReply -= Groups_GroupRoleDataReply;
                            throw new Exception(GetEnumDescription(ScriptError.TIMEOUT_GETTING_GROUP_ROLES));
                        }
                        Client.Groups.GroupRoleDataReply -= Groups_GroupRoleDataReply;
                        if (roleData.Count == 0)
                        {
                            throw new Exception(GetEnumDescription(ScriptError.COULD_NOT_GET_ROLES));
                        }
                        UUID roleUUID =
                            roleData.FirstOrDefault(
                                o =>
                                    o.Key.Equals(Uri.UnescapeDataString(wasKeyValueGet(ScriptKeys.TITLE, message)),
                                        StringComparison.InvariantCultureIgnoreCase))
                                .Value;
                        if (roleUUID.Equals(UUID.Zero))
                        {
                            throw new Exception(GetEnumDescription(ScriptError.COULD_NOT_FIND_TITLE));
                        }
                        Client.Groups.ActivateTitle(groupUUID, roleUUID);
                    };
                    break;
                case ScriptKeys.MOVE:
                    execute = () =>
                    {
                        if (
                            !HasCorradePermission(group,
                                (int) Permissions.PERMISSION_MOVEMENT))
                        {
                            throw new Exception(GetEnumDescription(ScriptError.NO_CORRADE_PERMISSIONS));
                        }
                        Vector3 position;
                        if (
                            !Vector3.TryParse(Uri.UnescapeDataString(wasKeyValueGet(ScriptKeys.POSITION, message)),
                                out position))
                        {
                            throw new Exception(GetEnumDescription(ScriptError.INVALID_POSITION));
                        }
                        switch (
                            Uri.UnescapeDataString(wasKeyValueGet(ScriptKeys.ACTION, message))
                                .ToLower(CultureInfo.InvariantCulture))
                        {
                            case Action.START:
                                uint moveRegionX, moveRegionY;
                                Utils.LongToUInts(Client.Network.CurrentSim.Handle, out moveRegionX, out moveRegionY);
                                Client.Self.SignaledAnimations.ForEach(
                                    animation => Client.Self.AnimationStop(animation.Key, true));
                                if (Client.Self.Movement.SitOnGround || Client.Self.SittingOn != 0)
                                {
                                    Client.Self.Stand();
                                }
                                Client.Self.AutoPilotCancel();
                                Client.Self.Movement.TurnToward(position, true);
                                Client.Self.AutoPilot(position.X + moveRegionX, position.Y + moveRegionY, position.Z);
                                break;
                            case Action.STOP:
                                Client.Self.AutoPilotCancel();
                                break;
                            default:
                                throw new Exception(GetEnumDescription(ScriptError.UNKNOWN_MOVE_ACTION));
                        }
                    };
                    break;
                case ScriptKeys.STARTPROPOSAL:
                    execute = () =>
                    {
                        UUID groupUUID =
                            Configuration.GROUPS.FirstOrDefault(
                                g => g.Name.Equals(group, StringComparison.InvariantCulture)).UUID;
                        if (groupUUID.Equals(UUID.Zero) &&
                            !GroupNameToUUID(group, Configuration.SERVICES_TIMEOUT, ref groupUUID))
                        {
                            throw new Exception(GetEnumDescription(ScriptError.GROUP_NOT_FOUND));
                        }
                        if (!AgentInGroup(Client.Self.AgentID, groupUUID, Configuration.SERVICES_TIMEOUT))
                        {
                            throw new Exception(GetEnumDescription(ScriptError.NOT_IN_GROUP));
                        }
                        if (
                            !HasGroupPowers(Client.Self.AgentID, groupUUID, GroupPowers.StartProposal,
                                Configuration.SERVICES_TIMEOUT))
                        {
                            throw new Exception(GetEnumDescription(ScriptError.NO_GROUP_POWER_FOR_COMMAND));
                        }
                        int duration;
                        if (
                            !int.TryParse(Uri.UnescapeDataString(wasKeyValueGet(ScriptKeys.DURATION, message)),
                                out duration))
                        {
                            throw new Exception(GetEnumDescription(ScriptError.INVALID_PROPOSAL_DURATION));
                        }
                        float majority;
                        if (
                            !float.TryParse(Uri.UnescapeDataString(wasKeyValueGet(ScriptKeys.MAJORITY, message)),
                                out majority))
                        {
                            throw new Exception(GetEnumDescription(ScriptError.INVALID_PROPOSAL_MAJORITY));
                        }
                        int quorum;
                        if (
                            !int.TryParse(Uri.UnescapeDataString(wasKeyValueGet(ScriptKeys.QUORUM, message)), out quorum))
                        {
                            throw new Exception(GetEnumDescription(ScriptError.INVALID_PROPOSAL_QUORUM));
                        }
                        string text = Uri.UnescapeDataString(wasKeyValueGet(ScriptKeys.TEXT, message));
                        if (string.IsNullOrEmpty(text))
                        {
                            throw new Exception(GetEnumDescription(ScriptError.INVALID_PROPOSAL_TEXT));
                        }
                        Client.Groups.StartProposal(groupUUID, new GroupProposal
                        {
                            Duration = duration,
                            Majority = majority,
                            Quorum = quorum,
                            VoteText = text
                        });
                    };
                    break;
                case ScriptKeys.MUTE:
                    execute = () =>
                    {
                        if (
                            !HasCorradePermission(group, (int) Permissions.PERMISSION_MUTE))
                        {
                            throw new Exception(GetEnumDescription(ScriptError.NO_CORRADE_PERMISSIONS));
                        }
                        UUID targetUUID;
                        if (
                            !UUID.TryParse(Uri.UnescapeDataString(wasKeyValueGet(ScriptKeys.TARGET, message)),
                                out targetUUID))
                        {
                            throw new Exception(GetEnumDescription(ScriptError.INVALID_MUTE_TARGET));
                        }
                        switch (
                            Uri.UnescapeDataString(wasKeyValueGet(ScriptKeys.ACTION, message))
                                .ToLower(CultureInfo.InvariantCulture))
                        {
                            case Action.MUTE:
                                string type = Uri.UnescapeDataString(wasKeyValueGet(ScriptKeys.TYPE, message));
                                MuteType muteType = !string.IsNullOrEmpty(type)
                                    ? (MuteType)
                                        typeof (MuteType).GetFields(BindingFlags.Public | BindingFlags.Static)
                                            .FirstOrDefault(
                                                t =>
                                                    t.Name.Equals(type,
                                                        StringComparison.InvariantCultureIgnoreCase))
                                            .GetValue(null)
                                    : MuteType.ByName;
                                ManualResetEvent MuteListUpdatedEvent = new ManualResetEvent(false);
                                EventHandler<EventArgs> MuteListUpdatedEventHandler =
                                    (sender, args) => MuteListUpdatedEvent.Set();
                                Client.Self.MuteListUpdated += MuteListUpdatedEventHandler;
                                Client.Self.UpdateMuteListEntry(muteType, targetUUID,
                                    Uri.UnescapeDataString(wasKeyValueGet(ScriptKeys.NAME, message)));
                                if (!MuteListUpdatedEvent.WaitOne(Configuration.SERVICES_TIMEOUT, false))
                                {
                                    Client.Self.MuteListUpdated -= MuteListUpdatedEventHandler;
                                    throw new Exception(GetEnumDescription(ScriptError.COULD_NOT_UPDATE_MUTE_LIST));
                                }
                                Client.Self.MuteListUpdated -= MuteListUpdatedEventHandler;
                                break;
                            case Action.UNMUTE:
                                Client.Self.RemoveMuteListEntry(targetUUID,
                                    Uri.UnescapeDataString(wasKeyValueGet(ScriptKeys.NAME, message)));
                                break;
                            default:
                                throw new Exception(GetEnumDescription(ScriptError.INVALID_ACTION));
                        }
                    };
                    break;
                case ScriptKeys.GETMUTES:
                    execute = () =>
                    {
                        if (!HasCorradePermission(group, (int) Permissions.PERMISSION_MUTE))
                        {
                            throw new Exception(GetEnumDescription(ScriptError.NO_CORRADE_PERMISSIONS));
                        }
                        List<string> mutes = new List<string>();
                        object LockObject = new object();
                        Parallel.ForEach(Client.Self.MuteList.Copy(), o =>
                        {
                            lock (LockObject)
                            {
                                mutes.Add(o.Value.Name);
                                mutes.Add(o.Value.ID.ToString());
                            }
                        });
                        result.Add(ResultKeys.MUTES,
                            string.Join(LINDEN_CONSTANTS.LSL.CSV_DELIMITER, mutes.ToArray()));
                    };
                    break;
                case ScriptKeys.DATABASE:
                    execute = () =>
                    {
                        if (!HasCorradePermission(group, (int) Permissions.PERMISSION_DATABASE))
                        {
                            throw new Exception(GetEnumDescription(ScriptError.NO_CORRADE_PERMISSIONS));
                        }
                        string databaseFile =
                            Configuration.GROUPS.FirstOrDefault(
                                g => g.Name.Equals(group, StringComparison.InvariantCulture)).DatabaseFile;
                        if (databaseFile.Equals(string.Empty))
                        {
                            throw new Exception(GetEnumDescription(ScriptError.NO_DATABASE_FILE_CONFIGURED));
                        }
                        if (!File.Exists(databaseFile))
                        {
                            // create the file and close it
                            File.Create(databaseFile).Close();
                        }
                        switch (
                            Uri.UnescapeDataString(wasKeyValueGet(ScriptKeys.ACTION, message))
                                .ToLower(CultureInfo.InvariantCulture))
                        {
                            case Action.GET:
                                string databaseGetkey = Uri.UnescapeDataString(wasKeyValueGet(ScriptKeys.KEY, message));
                                if (string.IsNullOrEmpty(databaseGetkey))
                                {
                                    throw new Exception(GetEnumDescription(ScriptError.NO_DATABASE_KEY_SPECIFIED));
                                }
                                lock (DatabaseLock)
                                {
                                    if (!DatabaseLocks.ContainsKey(group))
                                    {
                                        DatabaseLocks.Add(group, new object());
                                    }
                                }
                                lock (DatabaseLocks[group])
                                {
                                    result.Add(databaseGetkey,
                                        Uri.UnescapeDataString(wasKeyValueGet(databaseGetkey,
                                            File.ReadAllText(databaseFile))));
                                }
                                lock (DatabaseLock)
                                {
                                    if (DatabaseLocks.ContainsKey(group))
                                    {
                                        DatabaseLocks.Remove(group);
                                    }
                                }
                                break;
                            case Action.SET:
                                string databaseSetKey = Uri.UnescapeDataString(wasKeyValueGet(ScriptKeys.KEY, message));
                                if (string.IsNullOrEmpty(databaseSetKey))
                                {
                                    throw new Exception(GetEnumDescription(ScriptError.NO_DATABASE_KEY_SPECIFIED));
                                }
                                string databaseSetValue =
                                    Uri.UnescapeDataString(wasKeyValueGet(ScriptKeys.VALUE, message));
                                if (string.IsNullOrEmpty(databaseSetValue))
                                {
                                    throw new Exception(GetEnumDescription(ScriptError.NO_DATABASE_VALUE_SPECIFIED));
                                }
                                lock (DatabaseLock)
                                {
                                    if (!DatabaseLocks.ContainsKey(group))
                                    {
                                        DatabaseLocks.Add(group, new object());
                                    }
                                }
                                lock (DatabaseLocks[group])
                                {
                                    string contents = File.ReadAllText(databaseFile);
                                    using (StreamWriter recreateDatabase = new StreamWriter(databaseFile, false))
                                    {
                                        recreateDatabase.Write(wasKeyValueSet(databaseSetKey,
                                            databaseSetValue, contents));
                                        recreateDatabase.Flush();
                                        recreateDatabase.Close();
                                    }
                                }
                                lock (DatabaseLock)
                                {
                                    if (DatabaseLocks.ContainsKey(group))
                                    {
                                        DatabaseLocks.Remove(group);
                                    }
                                }
                                break;
                            default:
                                throw new Exception(GetEnumDescription(ScriptError.UNKNOWN_DATABASE_ACTION));
                        }
                    };
                    break;
                case ScriptKeys.NOTIFY:
                    execute = () =>
                    {
                        if (!HasCorradePermission(group, (int) Permissions.PERMISSION_NOTIFICATIONS))
                        {
                            throw new Exception(GetEnumDescription(ScriptError.NO_CORRADE_PERMISSIONS));
                        }
                        switch (
                            Uri.UnescapeDataString(wasKeyValueGet(ScriptKeys.ACTION, message))
                                .ToLower(CultureInfo.InvariantCulture))
                        {
                            case Action.SET:
                                string url = Uri.UnescapeDataString(wasKeyValueGet(ScriptKeys.URL, message));
                                if (string.IsNullOrEmpty(url))
                                {
                                    throw new Exception(GetEnumDescription(ScriptError.INVALID_URL_PROVIDED));
                                }
                                Uri notifyURL;
                                if (!Uri.TryCreate(url, UriKind.Absolute, out notifyURL))
                                {
                                    throw new Exception(GetEnumDescription(ScriptError.INVALID_URL_PROVIDED));
                                }
                                string notificationTypes =
                                    Uri.UnescapeDataString(wasKeyValueGet(ScriptKeys.TYPE, message))
                                        .ToLower(CultureInfo.InvariantCulture);
                                if (string.IsNullOrEmpty(notificationTypes))
                                {
                                    throw new Exception(GetEnumDescription(ScriptError.INVALID_NOTIFICATION_TYPES));
                                }
                                int notifications = 0;
                                Parallel.ForEach(
                                    notificationTypes.Split(new[] {LINDEN_CONSTANTS.LSL.CSV_DELIMITER},
                                        StringSplitOptions.RemoveEmptyEntries),
                                    n =>
                                    {
                                        int notification =
                                            (int)
                                                wasGetEnumValueFromDescription(new Notifications(), n.Trim());
                                        if (!HasCorradeNotification(group, notification))
                                        {
                                            throw new Exception(GetEnumDescription(ScriptError.NOTIFICATION_NOT_ALLOWED));
                                        }
                                        notifications = notifications | notification;
                                    });
                                // Build the notification.
                                Notification note = new Notification
                                {
                                    GROUP = group,
                                    URL = url,
                                    NOTIFICATION_MASK = notifications
                                };
                                lock (GroupNotificationsLock)
                                {
                                    // If we already have the same notification, bail
                                    if (GroupNotifications.Contains(note)) break;
                                    // Otherwise, replace it.
                                    GroupNotifications.RemoveWhere(
                                        o => o.GROUP.Equals(group, StringComparison.InvariantCulture));
                                    GroupNotifications.Add(note);
                                }
                                break;
                            case Action.GET:
                                // If the group has no insalled notifications, bail
                                if (!GroupNotifications.Any(g => g.GROUP.Equals(group)))
                                {
                                    break;
                                }
                                HashSet<string> csv = new HashSet<string>();
                                object LockObject = new object();
                                Parallel.ForEach(wasGetEnumDescriptions(new Notifications()), n =>
                                {
                                    int notification =
                                        (int) wasGetEnumValueFromDescription(new Notifications(), n);
                                    lock (GroupNotificationsLock)
                                    {
                                        // If the group does not have the notification installed, bail
                                        if (
                                            GroupNotifications.Any(
                                                g =>
                                                    g.GROUP.Equals(group, StringComparison.InvariantCulture) &&
                                                    (g.NOTIFICATION_MASK & notification) == 0))
                                        {
                                            return;
                                        }
                                    }
                                    lock (LockObject)
                                    {
                                        csv.Add(n);
                                    }
                                });
                                result.Add(ResultKeys.NOTIFICATIONS,
                                    string.Join(LINDEN_CONSTANTS.LSL.CSV_DELIMITER, csv.ToArray()));
                                break;
                            default:
                                throw new Exception(GetEnumDescription(ScriptError.UNKNOWN_NOTIFICATIONS_ACTION));
                        }
                    };
                    break;
                case ScriptKeys.REPLYTODIALOG:
                    execute = () =>
                    {
                        if (!HasCorradePermission(group, (int) Permissions.PERMISSION_INTERACT))
                        {
                            throw new Exception(GetEnumDescription(ScriptError.NO_CORRADE_PERMISSIONS));
                        }
                        int channel;
                        if (
                            !int.TryParse(Uri.UnescapeDataString(wasKeyValueGet(ScriptKeys.CHANNEL, message)),
                                out channel))
                        {
                            throw new Exception(GetEnumDescription(ScriptError.NO_CHANNEL_SPECIFIED));
                        }
                        int index;
                        if (!int.TryParse(Uri.UnescapeDataString(wasKeyValueGet(ScriptKeys.INDEX, message)), out index))
                        {
                            throw new Exception(GetEnumDescription(ScriptError.NO_BUTTON_INDEX_SPECIFIED));
                        }
                        string label = Uri.UnescapeDataString(wasKeyValueGet(ScriptKeys.BUTTON, message));
                        if (string.IsNullOrEmpty(label))
                        {
                            throw new Exception(GetEnumDescription(ScriptError.NO_BUTTON_SPECIFIED));
                        }
                        UUID itemUUID;
                        if (
                            !UUID.TryParse(Uri.UnescapeDataString(wasKeyValueGet(ScriptKeys.ITEM, message)),
                                out itemUUID))
                        {
                            throw new Exception(GetEnumDescription(ScriptError.NO_ITEM_SPECIFIED));
                        }
                        Client.Self.ReplyToScriptDialog(channel, index, label, itemUUID);
                    };
                    break;
                case ScriptKeys.ANIMATION:
                    execute = () =>
                    {
                        if (!HasCorradePermission(group, (int) Permissions.PERMISSION_GROOMING))
                        {
                            throw new Exception(GetEnumDescription(ScriptError.NO_CORRADE_PERMISSIONS));
                        }
                        string item = Uri.UnescapeDataString(wasKeyValueGet(ScriptKeys.ITEM, message));
                        if (string.IsNullOrEmpty(item))
                        {
                            throw new Exception(GetEnumDescription(ScriptError.NO_ITEM_SPECIFIED));
                        }
                        UUID itemUUID;
                        if (!UUID.TryParse(item, out itemUUID))
                        {
                            itemUUID =
                                SearchInventoryItem(Client.Inventory.Store.RootFolder, item,
                                    Configuration.SERVICES_TIMEOUT).FirstOrDefault().UUID;
                            if (itemUUID.Equals(UUID.Zero))
                            {
                                throw new Exception(GetEnumDescription(ScriptError.INVENTORY_ITEM_NOT_FOUND));
                            }
                        }
                        switch (
                            Uri.UnescapeDataString(wasKeyValueGet(ScriptKeys.ACTION, message))
                                .ToLower(CultureInfo.InvariantCulture))
                        {
                            case Action.START:
                                Client.Self.AnimationStart(itemUUID, true);
                                break;
                            case Action.STOP:
                                Client.Self.AnimationStop(itemUUID, true);
                                break;
                            default:
                                throw new Exception(GetEnumDescription(ScriptError.UNKNOWN_ANIMATION_ACTION));
                        }
                    };
                    break;
                case ScriptKeys.GETANIMATIONS:
                    execute = () =>
                    {
                        if (!HasCorradePermission(group, (int) Permissions.PERMISSION_GROOMING))
                        {
                            throw new Exception(GetEnumDescription(ScriptError.NO_CORRADE_PERMISSIONS));
                        }
                        List<string> csv = new List<string>();
                        Client.Self.SignaledAnimations.ForEach(
                            kvp =>
                                csv.AddRange(new List<string>
                                {
                                    kvp.Key.ToString(),
                                    kvp.Value.ToString(CultureInfo.InvariantCulture)
                                }));
                        result.Add(ResultKeys.ANIMATIONS,
                            string.Join(LINDEN_CONSTANTS.LSL.CSV_DELIMITER, csv.ToArray()));
                    };
                    break;
                case ScriptKeys.RESTARTREGION:
                    execute = () =>
                    {
                        if (!HasCorradePermission(group, (int) Permissions.PERMISSION_LAND))
                        {
                            throw new Exception(GetEnumDescription(ScriptError.NO_CORRADE_PERMISSIONS));
                        }
                        if (!Client.Network.CurrentSim.IsEstateManager)
                        {
                            throw new Exception(GetEnumDescription(ScriptError.NO_LAND_RIGHTS));
                        }
                        switch (
                            Uri.UnescapeDataString(wasKeyValueGet(ScriptKeys.ACTION, message))
                                .ToLower(CultureInfo.InvariantCulture))
                        {
                            case Action.RESTART:
                                Client.Estate.RestartRegion();
                                break;
                            case Action.CANCEL:
                                Client.Estate.CancelRestart();
                                break;
                            default:
                                throw new Exception(GetEnumDescription(ScriptError.UNKNOWN_RESTART_ACTION));
                        }
                    };
                    break;
                case ScriptKeys.GETREGIONTOP:
                    execute = () =>
                    {
                        if (!HasCorradePermission(group, (int) Permissions.PERMISSION_LAND))
                        {
                            throw new Exception(GetEnumDescription(ScriptError.NO_CORRADE_PERMISSIONS));
                        }
                        if (!Client.Network.CurrentSim.IsEstateManager)
                        {
                            throw new Exception(GetEnumDescription(ScriptError.NO_LAND_RIGHTS));
                        }
                        int amount;
                        if (
                            !int.TryParse(Uri.UnescapeDataString(wasKeyValueGet(ScriptKeys.AMOUNT, message)), out amount))
                        {
                            amount = 5;
                        }
                        List<string> csv = new List<string>();
                        object LockObject = new object();
                        switch (
                            Uri.UnescapeDataString(wasKeyValueGet(ScriptKeys.TYPE, message))
                                .ToLower(CultureInfo.InvariantCulture))
                        {
                            case Type.SCRIPTS:
                                Dictionary<UUID, EstateTask> topScripts = new Dictionary<UUID, EstateTask>();
                                ManualResetEvent TopScriptsReplyEvent = new ManualResetEvent(false);
                                EventHandler<TopScriptsReplyEventArgs> TopScriptsReplyEventHandler = (sender, args) =>
                                {
                                    topScripts =
                                        args.Tasks.OrderByDescending(o => o.Value.Score)
                                            .ToDictionary(o => o.Key, o => o.Value);
                                    TopScriptsReplyEvent.Set();
                                };
                                Client.Estate.TopScriptsReply += TopScriptsReplyEventHandler;
                                Client.Estate.RequestTopScripts();
                                if (!TopScriptsReplyEvent.WaitOne(Configuration.SERVICES_TIMEOUT))
                                {
                                    Client.Estate.TopScriptsReply -= TopScriptsReplyEventHandler;
                                    throw new Exception(GetEnumDescription(ScriptError.TIMEOUT_GETTING_TOP_SCRIPTS));
                                }
                                Client.Estate.TopScriptsReply -= TopScriptsReplyEventHandler;
                                Parallel.ForEach(topScripts.Take(amount), script =>
                                {
                                    lock (LockObject)
                                    {
                                        csv.Add(script.Key.ToString());
                                        csv.Add(script.Value.Score.ToString(CultureInfo.InvariantCulture));
                                        csv.Add(script.Value.TaskName);
                                        csv.Add(script.Value.OwnerName);
                                        csv.Add(script.Value.Position.ToString());
                                    }
                                });
                                break;
                            case Type.COLLIDERS:
                                Dictionary<UUID, EstateTask> topColliders = new Dictionary<UUID, EstateTask>();
                                ManualResetEvent TopCollidersReplyEvent = new ManualResetEvent(false);
                                EventHandler<TopCollidersReplyEventArgs> TopCollidersReplyEventHandler =
                                    (sender, args) =>
                                    {
                                        topScripts =
                                            args.Tasks.OrderByDescending(o => o.Value.Score)
                                                .ToDictionary(o => o.Key, o => o.Value);
                                        TopCollidersReplyEvent.Set();
                                    };
                                Client.Estate.TopCollidersReply += TopCollidersReplyEventHandler;
                                Client.Estate.RequestTopScripts();
                                if (!TopCollidersReplyEvent.WaitOne(Configuration.SERVICES_TIMEOUT))
                                {
                                    Client.Estate.TopCollidersReply -= TopCollidersReplyEventHandler;
                                    throw new Exception(GetEnumDescription(ScriptError.TIMEOUT_GETTING_TOP_SCRIPTS));
                                }
                                Client.Estate.TopCollidersReply -= TopCollidersReplyEventHandler;
                                Parallel.ForEach(topColliders.Take(amount), script =>
                                {
                                    lock (LockObject)
                                    {
                                        csv.Add(script.Key.ToString());
                                        csv.Add(script.Value.Score.ToString(CultureInfo.InvariantCulture));
                                        csv.Add(script.Value.TaskName);
                                        csv.Add(script.Value.OwnerName);
                                        csv.Add(script.Value.Position.ToString());
                                    }
                                });
                                break;
                            default:
                                throw new Exception(GetEnumDescription(ScriptError.UNKNOWN_TOP_TYPE));
                        }
                        result.Add(ResultKeys.TOP,
                            string.Join(LINDEN_CONSTANTS.LSL.CSV_DELIMITER, csv.ToArray()));
                    };
                    break;
                case ScriptKeys.SETESTATELIST:
                    execute = () =>
                    {
                        if (!HasCorradePermission(group, (int) Permissions.PERMISSION_LAND))
                        {
                            throw new Exception(GetEnumDescription(ScriptError.NO_CORRADE_PERMISSIONS));
                        }
                        if (!Client.Network.CurrentSim.IsEstateManager)
                        {
                            throw new Exception(GetEnumDescription(ScriptError.NO_LAND_RIGHTS));
                        }
                        bool allEstates;
                        if (
                            !bool.TryParse(Uri.UnescapeDataString(wasKeyValueGet(ScriptKeys.ALL, message)),
                                out allEstates))
                        {
                            allEstates = false;
                        }
                        UUID targetUUID = UUID.Zero;
                        switch (
                            Uri.UnescapeDataString(wasKeyValueGet(ScriptKeys.TYPE, message))
                                .ToLower(CultureInfo.InvariantCulture))
                        {
                            case Type.BAN:
                                if (
                                    !AgentNameToUUID(
                                        Uri.UnescapeDataString(wasKeyValueGet(ScriptKeys.FIRSTNAME, message)),
                                        Uri.UnescapeDataString(wasKeyValueGet(ScriptKeys.LASTNAME, message)),
                                        Configuration.SERVICES_TIMEOUT, ref targetUUID))
                                {
                                    throw new Exception(GetEnumDescription(ScriptError.AGENT_NOT_FOUND));
                                }
                                switch (
                                    Uri.UnescapeDataString(wasKeyValueGet(ScriptKeys.ACTION, message))
                                        .ToLower(CultureInfo.InvariantCulture))
                                {
                                    case Action.ADD:
                                        Client.Estate.BanUser(targetUUID, allEstates);
                                        break;
                                    case Action.REMOVE:
                                        Client.Estate.UnbanUser(targetUUID, allEstates);
                                        break;
                                    default:
                                        throw new Exception(GetEnumDescription(ScriptError.UNKNWON_ESTATE_LIST_ACTION));
                                }
                                break;
                            case Type.GROUP:
                                if (
                                    !UUID.TryParse(Uri.UnescapeDataString(wasKeyValueGet(ScriptKeys.TARGET, message)),
                                        out targetUUID))
                                {
                                    if (
                                        !GroupNameToUUID(
                                            Uri.UnescapeDataString(wasKeyValueGet(ScriptKeys.TARGET, message)),
                                            Configuration.SERVICES_TIMEOUT, ref targetUUID))
                                    {
                                        throw new Exception(GetEnumDescription(ScriptError.GROUP_NOT_FOUND));
                                    }
                                }
                                switch (
                                    Uri.UnescapeDataString(wasKeyValueGet(ScriptKeys.ACTION, message))
                                        .ToLower(CultureInfo.InvariantCulture))
                                {
                                    case Action.ADD:
                                        Client.Estate.AddAllowedGroup(targetUUID, allEstates);
                                        break;
                                    case Action.REMOVE:
                                        Client.Estate.RemoveAllowedGroup(targetUUID, allEstates);
                                        break;
                                    default:
                                        throw new Exception(GetEnumDescription(ScriptError.UNKNWON_ESTATE_LIST_ACTION));
                                }
                                break;
                            case Type.USER:
                                if (
                                    !AgentNameToUUID(
                                        Uri.UnescapeDataString(wasKeyValueGet(ScriptKeys.FIRSTNAME, message)),
                                        Uri.UnescapeDataString(wasKeyValueGet(ScriptKeys.LASTNAME, message)),
                                        Configuration.SERVICES_TIMEOUT, ref targetUUID))
                                {
                                    throw new Exception(GetEnumDescription(ScriptError.AGENT_NOT_FOUND));
                                }
                                switch (
                                    Uri.UnescapeDataString(wasKeyValueGet(ScriptKeys.ACTION, message))
                                        .ToLower(CultureInfo.InvariantCulture))
                                {
                                    case Action.ADD:
                                        Client.Estate.AddAllowedUser(targetUUID, allEstates);
                                        break;
                                    case Action.REMOVE:
                                        Client.Estate.RemoveAllowedUser(targetUUID, allEstates);
                                        break;
                                    default:
                                        throw new Exception(GetEnumDescription(ScriptError.UNKNWON_ESTATE_LIST_ACTION));
                                }
                                break;
                            case Type.MANAGER:
                                if (
                                    !AgentNameToUUID(
                                        Uri.UnescapeDataString(wasKeyValueGet(ScriptKeys.FIRSTNAME, message)),
                                        Uri.UnescapeDataString(wasKeyValueGet(ScriptKeys.LASTNAME, message)),
                                        Configuration.SERVICES_TIMEOUT, ref targetUUID))
                                {
                                    throw new Exception(GetEnumDescription(ScriptError.AGENT_NOT_FOUND));
                                }
                                switch (
                                    Uri.UnescapeDataString(wasKeyValueGet(ScriptKeys.ACTION, message))
                                        .ToLower(CultureInfo.InvariantCulture))
                                {
                                    case Action.ADD:
                                        Client.Estate.AddEstateManager(targetUUID, allEstates);
                                        break;
                                    case Action.REMOVE:
                                        Client.Estate.RemoveEstateManager(targetUUID, allEstates);
                                        break;
                                    default:
                                        throw new Exception(GetEnumDescription(ScriptError.UNKNWON_ESTATE_LIST_ACTION));
                                }
                                break;
                            default:
                                throw new Exception(GetEnumDescription(ScriptError.UNKNOWN_ESTATE_LIST));
                        }
                    };
                    break;
                case ScriptKeys.GETESTATELIST:
                    execute = () =>
                    {
                        if (!HasCorradePermission(group, (int) Permissions.PERMISSION_LAND))
                        {
                            throw new Exception(GetEnumDescription(ScriptError.NO_CORRADE_PERMISSIONS));
                        }
                        if (!Client.Network.CurrentSim.IsEstateManager)
                        {
                            throw new Exception(GetEnumDescription(ScriptError.NO_LAND_RIGHTS));
                        }
                        List<UUID> estateList = new List<UUID>();
                        ManualResetEvent EstateListReplyEvent = new ManualResetEvent(false);
                        switch (
                            Uri.UnescapeDataString(wasKeyValueGet(ScriptKeys.TYPE, message))
                                .ToLower(CultureInfo.InvariantCulture))
                        {
                            case Type.BAN:
                                EventHandler<EstateBansReplyEventArgs> EstateBansReplyEventHandler = (sender, args) =>
                                {
                                    estateList = args.Banned;
                                    EstateListReplyEvent.Set();
                                };
                                Client.Estate.EstateBansReply += EstateBansReplyEventHandler;
                                Client.Estate.RequestInfo();
                                if (!EstateListReplyEvent.WaitOne(Configuration.SERVICES_TIMEOUT))
                                {
                                    Client.Estate.EstateBansReply -= EstateBansReplyEventHandler;
                                    throw new Exception(GetEnumDescription(ScriptError.TIMEOUT_WAITING_FOR_ESTATE_LIST));
                                }
                                Client.Estate.EstateBansReply -= EstateBansReplyEventHandler;
                                break;
                            case Type.GROUP:
                                EventHandler<EstateGroupsReplyEventArgs> EstateGroupsReplyEvenHandler =
                                    (sender, args) =>
                                    {
                                        estateList = args.AllowedGroups;
                                        EstateListReplyEvent.Set();
                                    };
                                Client.Estate.EstateGroupsReply += EstateGroupsReplyEvenHandler;
                                Client.Estate.RequestInfo();
                                if (!EstateListReplyEvent.WaitOne(Configuration.SERVICES_TIMEOUT))
                                {
                                    Client.Estate.EstateGroupsReply -= EstateGroupsReplyEvenHandler;
                                    throw new Exception(GetEnumDescription(ScriptError.TIMEOUT_WAITING_FOR_ESTATE_LIST));
                                }
                                Client.Estate.EstateGroupsReply -= EstateGroupsReplyEvenHandler;
                                break;
                            case Type.MANAGER:
                                EventHandler<EstateManagersReplyEventArgs> EstateManagersReplyEventHandler =
                                    (sender, args) =>
                                    {
                                        estateList = args.Managers;
                                        EstateListReplyEvent.Set();
                                    };
                                Client.Estate.EstateManagersReply += EstateManagersReplyEventHandler;
                                Client.Estate.RequestInfo();
                                if (!EstateListReplyEvent.WaitOne(Configuration.SERVICES_TIMEOUT))
                                {
                                    Client.Estate.EstateManagersReply -= EstateManagersReplyEventHandler;
                                    throw new Exception(GetEnumDescription(ScriptError.TIMEOUT_WAITING_FOR_ESTATE_LIST));
                                }
                                Client.Estate.EstateManagersReply -= EstateManagersReplyEventHandler;
                                break;
                            case Type.USER:
                                EventHandler<EstateUsersReplyEventArgs> EstateUsersReplyEventHandler =
                                    (sender, args) =>
                                    {
                                        estateList = args.AllowedUsers;
                                        EstateListReplyEvent.Set();
                                    };
                                Client.Estate.EstateUsersReply += EstateUsersReplyEventHandler;
                                Client.Estate.RequestInfo();
                                if (!EstateListReplyEvent.WaitOne(Configuration.SERVICES_TIMEOUT))
                                {
                                    Client.Estate.EstateUsersReply -= EstateUsersReplyEventHandler;
                                    throw new Exception(GetEnumDescription(ScriptError.TIMEOUT_WAITING_FOR_ESTATE_LIST));
                                }
                                Client.Estate.EstateUsersReply -= EstateUsersReplyEventHandler;
                                break;
                            default:
                                throw new Exception(GetEnumDescription(ScriptError.UNKNOWN_ESTATE_LIST));
                        }
                        if (estateList.Count == 0) return;
                        HashSet<string> csv = new HashSet<string>();
                        object LockObject = new object();
                        Parallel.ForEach(estateList, data =>
                        {
                            lock (LockObject)
                            {
                                csv.Add(data.ToString());
                            }
                        });
                        result.Add(ResultKeys.LIST,
                            string.Join(LINDEN_CONSTANTS.LSL.CSV_DELIMITER, csv.ToArray()));
                    };
                    break;
                case ScriptKeys.GETAVATARDATA:
                    execute = () =>
                    {
                        if (!HasCorradePermission(group, (int) Permissions.PERMISSION_INTERACT))
                        {
                            throw new Exception(GetEnumDescription(ScriptError.NO_CORRADE_PERMISSIONS));
                        }
                        UUID agentUUID = UUID.Zero;
                        if (
                            !AgentNameToUUID(Uri.UnescapeDataString(wasKeyValueGet(ScriptKeys.FIRSTNAME, message)),
                                Uri.UnescapeDataString(wasKeyValueGet(ScriptKeys.LASTNAME, message)),
                                Configuration.SERVICES_TIMEOUT, ref agentUUID))
                        {
                            throw new Exception(GetEnumDescription(ScriptError.AGENT_NOT_FOUND));
                        }
                        Avatar avatar = Client.Network.CurrentSim.ObjectsAvatars.Find(o => o.ID.Equals(agentUUID));
                        if (avatar == null)
                        {
                            throw new Exception(GetEnumDescription(ScriptError.AVATAR_NOT_ON_SIMULATOR));
                        }
                        string fields = Uri.UnescapeDataString(wasKeyValueGet(ScriptKeys.DATA, message));
                        List<string> csv = new List<string>();
                        object LockObject = new object();
                        Parallel.ForEach(
                            fields.Split(new[] {LINDEN_CONSTANTS.LSL.CSV_DELIMITER},
                                StringSplitOptions.RemoveEmptyEntries),
                            name =>
                            {
                                KeyValuePair<FieldInfo, object> fi = wasGetFields(avatar, avatar.GetType().Name)
                                    .FirstOrDefault(
                                        o => o.Key.Name.Equals(name, StringComparison.InvariantCultureIgnoreCase));

                                lock (LockObject)
                                {
                                    List<string> data = new List<string> {name};
                                    data.AddRange(wasGetInfo(fi.Key, fi.Value));
                                    if (data.Count >= 2)
                                    {
                                        csv.AddRange(data);
                                    }
                                }

                                KeyValuePair<PropertyInfo, object> pi =
                                    wasGetProperties(avatar, avatar.GetType().Name)
                                        .FirstOrDefault(
                                            o => o.Key.Name.Equals(name, StringComparison.InvariantCultureIgnoreCase));

                                lock (LockObject)
                                {
                                    List<string> data = new List<string> {name};
                                    data.AddRange(wasGetInfo(pi.Key, pi.Value));
                                    if (data.Count >= 2)
                                    {
                                        csv.AddRange(data);
                                    }
                                }
                            });
                        result.Add(ResultKeys.DATA, string.Join(LINDEN_CONSTANTS.LSL.CSV_DELIMITER, csv.ToArray()));
                    };
                    break;
                case ScriptKeys.DIRECTORYSEARCH:
                    execute = () =>
                    {
                        if (!HasCorradePermission(group, (int) Permissions.PERMISSION_DIRECTORY))
                        {
                            throw new Exception(GetEnumDescription(ScriptError.NO_CORRADE_PERMISSIONS));
                        }
                        int timeout;
                        if (
                            !int.TryParse(Uri.UnescapeDataString(wasKeyValueGet(ScriptKeys.TIMEOUT, message)),
                                out timeout))
                        {
                            timeout = Configuration.SERVICES_TIMEOUT;
                        }
                        object LockObject = new object();
                        List<string> csv = new List<string>();
                        int handledEvents = 0;
                        int counter = 1;
                        string name = Uri.UnescapeDataString(wasKeyValueGet(ScriptKeys.NAME, message));
                        string fields = Uri.UnescapeDataString(wasKeyValueGet(ScriptKeys.DATA, message));
                        switch (
                            Uri.UnescapeDataString(wasKeyValueGet(ScriptKeys.TYPE, message))
                                .ToLower(CultureInfo.InvariantCulture))
                        {
                            case Type.CLASSIFIED:
                                DirectoryManager.Classified searchClassified = new DirectoryManager.Classified();
                                wasCSVToStructure(Uri.UnescapeDataString(wasKeyValueGet(ScriptKeys.DATA, message)),
                                    ref searchClassified);
                                Dictionary<DirectoryManager.Classified, int> classifieds =
                                    new Dictionary<DirectoryManager.Classified, int>();
                                ManualResetEvent DirClassifiedsEvent = new ManualResetEvent(false);
                                EventHandler<DirClassifiedsReplyEventArgs> DirClassifiedsEventHandler =
                                    (sender, args) => Parallel.ForEach(args.Classifieds, classifiedMatch =>
                                    {
                                        int score = !string.IsNullOrEmpty(fields)
                                            ? wasGetFields(searchClassified, searchClassified.GetType().Name)
                                                .Sum(
                                                    fs =>
                                                        (from fc in
                                                            wasGetFields(classifiedMatch, classifiedMatch.GetType().Name)
                                                            let fso = wasGetInfoValue(fs.Key, fs.Value)
                                                            where fso != null
                                                            let fco = wasGetInfoValue(fc.Key, fc.Value)
                                                            where fco != null
                                                            where fso.Equals(fco)
                                                            select fso).Count())
                                            : 0;
                                        lock (LockObject)
                                        {
                                            classifieds.Add(classifiedMatch, score);
                                        }
                                    });
                                Client.Directory.DirClassifiedsReply += DirClassifiedsEventHandler;
                                Client.Directory.StartClassifiedSearch(name);
                                DirClassifiedsEvent.WaitOne(timeout);
                                Client.Directory.DirClassifiedsReply -= DirClassifiedsEventHandler;
                                DirectoryManager.Classified topClassified =
                                    classifieds.OrderByDescending(p => p.Value).FirstOrDefault().Key;
                                Parallel.ForEach(
                                    wasGetFields(topClassified, topClassified.GetType().Name),
                                    fc =>
                                    {
                                        lock (LockObject)
                                        {
                                            csv.Add(fc.Key.Name);
                                            csv.AddRange(wasGetInfo(fc.Key, fc.Value));
                                        }
                                    });
                                break;
                            case Type.EVENT:
                                DirectoryManager.EventsSearchData searchEvent = new DirectoryManager.EventsSearchData();
                                wasCSVToStructure(Uri.UnescapeDataString(wasKeyValueGet(ScriptKeys.DATA, message)),
                                    ref searchEvent);
                                Dictionary<DirectoryManager.EventsSearchData, int> events =
                                    new Dictionary<DirectoryManager.EventsSearchData, int>();
                                ManualResetEvent DirEventsReplyEvent = new ManualResetEvent(false);
                                EventHandler<DirEventsReplyEventArgs> DirEventsEventHandler =
                                    (sender, args) =>
                                    {
                                        handledEvents += args.MatchedEvents.Count;
                                        Parallel.ForEach(args.MatchedEvents, eventMatch =>
                                        {
                                            int score = !string.IsNullOrEmpty(fields)
                                                ? wasGetFields(searchEvent, searchEvent.GetType().Name)
                                                    .Sum(
                                                        fs =>
                                                            (from fc in
                                                                wasGetFields(eventMatch, eventMatch.GetType().Name)
                                                                let fso = wasGetInfoValue(fs.Key, fs.Value)
                                                                where fso != null
                                                                let fco = wasGetInfoValue(fc.Key, fc.Value)
                                                                where fco != null
                                                                where fso.Equals(fco)
                                                                select fso).Count())
                                                : 0;
                                            lock (LockObject)
                                            {
                                                events.Add(eventMatch, score);
                                            }
                                        });
                                        if ((handledEvents - counter)%
                                            LINDEN_CONSTANTS.DIRECTORY.EVENT.SEARCH_RESULTS_COUNT == 0)
                                        {
                                            ++counter;
                                            Client.Directory.StartEventsSearch(name, (uint) handledEvents);
                                        }
                                    };
                                Client.Directory.DirEventsReply += DirEventsEventHandler;
                                Client.Directory.StartEventsSearch(name,
                                    (uint) handledEvents);
                                DirEventsReplyEvent.WaitOne(timeout);
                                Client.Directory.DirEventsReply -= DirEventsEventHandler;
                                DirectoryManager.EventsSearchData topEvent =
                                    events.OrderByDescending(p => p.Value).FirstOrDefault().Key;
                                Parallel.ForEach(wasGetFields(topEvent, topEvent.GetType().Name),
                                    fc =>
                                    {
                                        lock (LockObject)
                                        {
                                            csv.Add(fc.Key.Name);
                                            csv.AddRange(wasGetInfo(fc.Key, fc.Value));
                                        }
                                    });
                                break;
                            case Type.GROUP:
                                if (string.IsNullOrEmpty(name))
                                {
                                    throw new Exception(GetEnumDescription(ScriptError.NO_SEARCH_TEXT_PROVIDED));
                                }
                                DirectoryManager.GroupSearchData searchGroup = new DirectoryManager.GroupSearchData();
                                wasCSVToStructure(Uri.UnescapeDataString(wasKeyValueGet(ScriptKeys.DATA, message)),
                                    ref searchGroup);
                                Dictionary<DirectoryManager.GroupSearchData, int> groups =
                                    new Dictionary<DirectoryManager.GroupSearchData, int>();
                                ManualResetEvent DirGroupsReplyEvent = new ManualResetEvent(false);
                                EventHandler<DirGroupsReplyEventArgs> DirGroupsEventHandler =
                                    (sender, args) =>
                                    {
                                        handledEvents += args.MatchedGroups.Count;
                                        Parallel.ForEach(args.MatchedGroups, groupMatch =>
                                        {
                                            int score = !string.IsNullOrEmpty(fields)
                                                ? wasGetFields(searchGroup, searchGroup.GetType().Name)
                                                    .Sum(
                                                        fs =>
                                                            (from fc in
                                                                wasGetFields(groupMatch, groupMatch.GetType().Name)
                                                                let fso = wasGetInfoValue(fs.Key, fs.Value)
                                                                where fso != null
                                                                let fco = wasGetInfoValue(fc.Key, fc.Value)
                                                                where fco != null
                                                                where fso.Equals(fco)
                                                                select fso).Count())
                                                : 0;
                                            lock (LockObject)
                                            {
                                                groups.Add(groupMatch, score);
                                            }
                                        });
                                        if ((handledEvents - counter)%
                                            LINDEN_CONSTANTS.DIRECTORY.GROUP.SEARCH_RESULTS_COUNT == 0)
                                        {
                                            ++counter;
                                            Client.Directory.StartGroupSearch(name, handledEvents);
                                        }
                                    };
                                Client.Directory.DirGroupsReply += DirGroupsEventHandler;
                                Client.Directory.StartGroupSearch(name, handledEvents);
                                DirGroupsReplyEvent.WaitOne(timeout);
                                Client.Directory.DirGroupsReply -= DirGroupsEventHandler;
                                DirectoryManager.GroupSearchData topGroup =
                                    groups.OrderByDescending(p => p.Value).FirstOrDefault().Key;
                                Parallel.ForEach(wasGetFields(topGroup, topGroup.GetType().Name),
                                    fc =>
                                    {
                                        lock (LockObject)
                                        {
                                            csv.Add(fc.Key.Name);
                                            csv.AddRange(wasGetInfo(fc.Key, fc.Value));
                                        }
                                    });
                                break;
                            case Type.LAND:
                                DirectoryManager.DirectoryParcel searchLand = new DirectoryManager.DirectoryParcel();
                                wasCSVToStructure(Uri.UnescapeDataString(wasKeyValueGet(ScriptKeys.DATA, message)),
                                    ref searchLand);
                                Dictionary<DirectoryManager.DirectoryParcel, int> lands =
                                    new Dictionary<DirectoryManager.DirectoryParcel, int>();
                                ManualResetEvent DirLandReplyEvent = new ManualResetEvent(false);
                                EventHandler<DirLandReplyEventArgs> DirLandReplyEventArgs =
                                    (sender, args) =>
                                    {
                                        handledEvents += args.DirParcels.Count;
                                        Parallel.ForEach(args.DirParcels, parcelMatch =>
                                        {
                                            int score = !string.IsNullOrEmpty(fields)
                                                ? wasGetFields(searchLand, searchLand.GetType().Name)
                                                    .Sum(
                                                        fs =>
                                                            (from fc in
                                                                wasGetFields(parcelMatch, parcelMatch.GetType().Name)
                                                                let fso = wasGetInfoValue(fs.Key, fs.Value)
                                                                where fso != null
                                                                let fco = wasGetInfoValue(fc.Key, fc.Value)
                                                                where fco != null
                                                                where fso.Equals(fco)
                                                                select fso).Count())
                                                : 0;
                                            lock (LockObject)
                                            {
                                                lands.Add(parcelMatch, score);
                                            }
                                        });
                                        if ((handledEvents - counter)%
                                            LINDEN_CONSTANTS.DIRECTORY.LAND.SEARCH_RESULTS_COUNT == 0)
                                        {
                                            ++counter;
                                            Client.Directory.StartLandSearch(DirectoryManager.DirFindFlags.SortAsc,
                                                DirectoryManager.SearchTypeFlags.Any, int.MaxValue, int.MaxValue,
                                                handledEvents);
                                        }
                                    };
                                Client.Directory.DirLandReply += DirLandReplyEventArgs;
                                Client.Directory.StartLandSearch(DirectoryManager.DirFindFlags.SortAsc,
                                    DirectoryManager.SearchTypeFlags.Any, int.MaxValue, int.MaxValue, handledEvents);
                                DirLandReplyEvent.WaitOne(timeout);
                                Client.Directory.DirLandReply -= DirLandReplyEventArgs;
                                DirectoryManager.DirectoryParcel topLand =
                                    lands.OrderByDescending(p => p.Value).FirstOrDefault().Key;
                                Parallel.ForEach(wasGetFields(topLand, topLand.GetType().Name),
                                    fc =>
                                    {
                                        lock (LockObject)
                                        {
                                            csv.Add(fc.Key.Name);
                                            csv.AddRange(wasGetInfo(fc.Key, fc.Value));
                                        }
                                    });
                                break;
                            case Type.PEOPLE:
                                if (string.IsNullOrEmpty(name))
                                {
                                    throw new Exception(GetEnumDescription(ScriptError.NO_SEARCH_TEXT_PROVIDED));
                                }
                                DirectoryManager.AgentSearchData searchAgent = new DirectoryManager.AgentSearchData();
                                Dictionary<DirectoryManager.AgentSearchData, int> agents =
                                    new Dictionary<DirectoryManager.AgentSearchData, int>();
                                ManualResetEvent AgentSearchDataEvent = new ManualResetEvent(false);
                                EventHandler<DirPeopleReplyEventArgs> DirPeopleReplyEventHandler =
                                    (sender, args) => Parallel.ForEach(args.MatchedPeople, peopleMatch =>
                                    {
                                        handledEvents += args.MatchedPeople.Count;
                                        int score = !string.IsNullOrEmpty(fields)
                                            ? wasGetFields(searchAgent, searchAgent.GetType().Name)
                                                .Sum(
                                                    fs =>
                                                        (from fc in
                                                            wasGetFields(peopleMatch, peopleMatch.GetType().Name)
                                                            let fso = wasGetInfoValue(fs.Key, fs.Value)
                                                            where fso != null
                                                            let fco = wasGetInfoValue(fc.Key, fc.Value)
                                                            where fco != null
                                                            where fso.Equals(fco)
                                                            select fso).Count())
                                            : 0;
                                        lock (LockObject)
                                        {
                                            agents.Add(peopleMatch, score);
                                        }
                                        if ((handledEvents - counter)%
                                            LINDEN_CONSTANTS.DIRECTORY.PEOPLE.SEARCH_RESULTS_COUNT == 0)
                                        {
                                            ++counter;
                                            Client.Directory.StartPeopleSearch(name, handledEvents);
                                        }
                                    });
                                Client.Directory.DirPeopleReply += DirPeopleReplyEventHandler;
                                Client.Directory.StartPeopleSearch(name, handledEvents);
                                AgentSearchDataEvent.WaitOne(timeout);
                                Client.Directory.DirPeopleReply -= DirPeopleReplyEventHandler;
                                DirectoryManager.AgentSearchData topAgent =
                                    agents.OrderByDescending(p => p.Value).FirstOrDefault().Key;
                                Parallel.ForEach(wasGetFields(topAgent, topAgent.GetType().Name),
                                    fc =>
                                    {
                                        lock (LockObject)
                                        {
                                            csv.Add(fc.Key.Name);
                                            csv.AddRange(wasGetInfo(fc.Key, fc.Value));
                                        }
                                    });
                                break;
                            case Type.PLACE:
                                if (string.IsNullOrEmpty(name))
                                {
                                    throw new Exception(GetEnumDescription(ScriptError.NO_SEARCH_TEXT_PROVIDED));
                                }
                                DirectoryManager.PlacesSearchData searchPlaces = new DirectoryManager.PlacesSearchData();
                                wasCSVToStructure(Uri.UnescapeDataString(wasKeyValueGet(ScriptKeys.DATA, message)),
                                    ref searchPlaces);
                                Dictionary<DirectoryManager.PlacesSearchData, int> places =
                                    new Dictionary<DirectoryManager.PlacesSearchData, int>();
                                ManualResetEvent DirPlacesReplyEvent = new ManualResetEvent(false);
                                EventHandler<PlacesReplyEventArgs> DirPlacesReplyEventHandler =
                                    (sender, args) => Parallel.ForEach(args.MatchedPlaces, parcelMatch =>
                                    {
                                        int score = !string.IsNullOrEmpty(fields)
                                            ? wasGetFields(searchPlaces, searchPlaces.GetType().Name)
                                                .Sum(
                                                    fs =>
                                                        (from fc in
                                                            wasGetFields(parcelMatch, parcelMatch.GetType().Name)
                                                            let fso = wasGetInfoValue(fs.Key, fs.Value)
                                                            where fso != null
                                                            let fco = wasGetInfoValue(fc.Key, fc.Value)
                                                            where fco != null
                                                            where fso.Equals(fco)
                                                            select fso).Count())
                                            : 0;
                                        lock (LockObject)
                                        {
                                            places.Add(parcelMatch, score);
                                        }
                                    });
                                Client.Directory.PlacesReply += DirPlacesReplyEventHandler;
                                Client.Directory.StartPlacesSearch(name);
                                DirPlacesReplyEvent.WaitOne(timeout);
                                Client.Directory.PlacesReply -= DirPlacesReplyEventHandler;
                                DirectoryManager.PlacesSearchData topPlace =
                                    places.OrderByDescending(p => p.Value).FirstOrDefault().Key;
                                Parallel.ForEach(wasGetFields(topPlace, topPlace.GetType().Name),
                                    fc =>
                                    {
                                        lock (LockObject)
                                        {
                                            csv.Add(fc.Key.Name);
                                            csv.AddRange(wasGetInfo(fc.Key, fc.Value));
                                        }
                                    });
                                break;
                            default:
                                throw new Exception(GetEnumDescription(ScriptError.UNKNOWN_DIRECTORY_SEARCH_TYPE));
                        }
                        if (csv.Count != 0)
                        {
                            result.Add(ResultKeys.SEARCH,
                                string.Join(LINDEN_CONSTANTS.LSL.CSV_DELIMITER, csv.ToArray()));
                        }
                    };
                    break;
                default:
                    execute = () => { throw new Exception(GetEnumDescription(ScriptError.COMMAND_NOT_FOUND)); };
                    break;
            }

            // execute command and check for errors
            bool success = false;
            try
            {
                execute.Invoke();
                success = true;
            }
            catch (Exception e)
            {
                result.Add(ResultKeys.ERROR, e.Message);
            }
            result.Add(ResultKeys.SUCCESS, success.ToString(CultureInfo.InvariantCulture));

            // build afterburn
            System.Action afterburn = () =>
            {
                object AfterburnLock = new object();
                Parallel.ForEach(wasKeyValueDecode(message), kvp =>
                {
                    if (ScriptKeys.GetKeys().Contains(kvp.Key))
                        return;
                    if (string.IsNullOrEmpty(kvp.Key) || string.IsNullOrEmpty(kvp.Value)) return;
                    lock (AfterburnLock)
                    {
                        result.Add(Uri.EscapeDataString(kvp.Key), Uri.EscapeDataString(kvp.Value));
                    }
                });
            };
            afterburn.Invoke();

            // send callback
            System.Action callback = () =>
            {
                string url = Uri.UnescapeDataString(wasKeyValueGet(ScriptKeys.CALLBACK, message));
                if (!string.IsNullOrEmpty(url))
                {
                    string error = wasPOST(url, wasKeyValueEscape(result));
                    if (!string.IsNullOrEmpty(error))
                    {
                        result.Add(ScriptKeys.CALLBACK, url);
                        result.Add(ResultKeys.CALLBACKERROR, error);
                    }
                }
            };
            callback.Invoke();

            return wasKeyValueEncode(result);
        }

        private static void wasCSVToStructure<T>(string data, ref T structure)
        {
            foreach (
                KeyValuePair<string, string> match in
                    Regex.Matches(data, @"\s*(?<key>.+?)\s*,\s*(?<value>.+?)\s*(,|$)").
                        Cast<Match>().
                        ToDictionary(m => m.Groups["key"].Value, m => m.Groups["value"].Value))
            {
                KeyValuePair<string, string> localMatch = match;
                KeyValuePair<FieldInfo, object> fi =
                    wasGetFields(structure, structure.GetType().Name)
                        .FirstOrDefault(
                            o =>
                                o.Key.Name.Equals(localMatch.Key,
                                    StringComparison.InvariantCultureIgnoreCase));

                wasSetInfo(fi.Key, fi.Value, match.Value, ref structure);

                KeyValuePair<PropertyInfo, object> pi =
                    wasGetProperties(structure, structure.GetType().Name)
                        .FirstOrDefault(
                            o =>
                                o.Key.Name.Equals(localMatch.Key,
                                    StringComparison.InvariantCultureIgnoreCase));

                wasSetInfo(pi.Key, pi.Value, match.Value, ref structure);
            }
        }

        /// <summary>
        ///     Sends a post request to an URL with set key-value pairs.
        /// </summary>
        /// <param name="URL">the url to send the message to</param>
        /// <param name="message">key-value pairs to send</param>
        /// <returns>the error message in case the request fails.</returns>
        private static string wasPOST(string URL, Dictionary<string, string> message)
        {
            try
            {
                byte[] byteArray =
                    Encoding.UTF8.GetBytes(string.Format(CultureInfo.InvariantCulture, "{0}",
                        wasKeyValueEncode(message)));
                WebRequest request = WebRequest.Create(URL);
                request.Timeout = Configuration.CALLBACK_TIMEOUT;
                request.Method = "POST";
                request.ContentType = "application/x-www-form-urlencoded";
                request.ContentLength = byteArray.Length;
                Stream dataStream = request.GetRequestStream();
                dataStream.Write(byteArray, 0, byteArray.Length);
                dataStream.Flush();
                dataStream.Close();
                WebResponse response = request.GetResponse();
                dataStream = response.GetResponseStream();
                if (dataStream != null)
                {
                    StreamReader reader = new StreamReader(dataStream);
                    reader.ReadToEnd();
                    reader.Close();
                    dataStream.Close();
                }
                response.Close();
                return string.Empty;
            }
            catch (Exception e)
            {
                return e.Message;
            }
        }

        private static void ProcessCommandReturn(IAsyncResult ar)
        {
            WorkerDelgate workerDelegate = (WorkerDelgate) ar.AsyncState;

            // Pull the request.
            string result = workerDelegate.EndInvoke(ar);

            // When we receive the asynchronous completion of the command processor,
            // we decrement the worker count thereby freeing up a slot for new commands.
            lock (GroupWorkers)
            {
                GroupWorkers[Uri.UnescapeDataString(wasKeyValueGet(ScriptKeys.GROUP, result))]--;
            }
        }

        private static void HandleTerseObjectUpdate(object sender, TerseObjectUpdateEventArgs e)
        {
            if (e.Prim.LocalID == Client.Self.LocalID)
            {
                SetDefaultCamera();
            }
        }

        private static void HandleAvatarUpdate(object sender, AvatarUpdateEventArgs e)
        {
            if (e.Avatar.LocalID == Client.Self.LocalID)
            {
                SetDefaultCamera();
            }
        }

        private static void HandleSimChanged(object sender, SimChangedEventArgs e)
        {
            Client.Self.Movement.SetFOVVerticalAngle(Utils.TWO_PI - 0.05f);
        }

        private static void SetDefaultCamera()
        {
            // SetCamera 5m behind the avatar
            Client.Self.Movement.Camera.LookAt(
                Client.Self.SimPosition + new Vector3(-5, 0, 0)*Client.Self.Movement.BodyRotation,
                Client.Self.SimPosition
                );
        }

        #region NAME AND UUID RESOLVERS

        ///////////////////////////////////////////////////////////////////////////
        //    Copyright (C) 2013 Wizardry and Steamworks - License: GNU GPLv3    //
        ///////////////////////////////////////////////////////////////////////////
        /// <summary>
        ///     Resolves a group name to an UUID by using the directory search.
        /// </summary>
        /// <param name="groupName">the name of the group to resolve</param>
        /// <param name="millisecondsTimeout">timeout for the search in milliseconds</param>
        /// <param name="groupUUID">an object in which to store the UUID of the group</param>
        /// <returns>true if the group name could be resolved to an UUID</returns>
        private static bool GroupNameToUUID(string groupName, int millisecondsTimeout, ref UUID groupUUID)
        {
            UUID localGroupUUID = UUID.Zero;
            ManualResetEvent DirGroupsEvent = new ManualResetEvent(false);
            EventHandler<DirGroupsReplyEventArgs> DirGroupsReplyDelegate = (o, s) =>
            {
                // do not LINQ
                foreach (DirectoryManager.GroupSearchData match in s.MatchedGroups)
                {
                    if (!match.GroupName.Equals(groupName, StringComparison.InvariantCulture)) continue;
                    localGroupUUID = match.GroupID;
                }
                DirGroupsEvent.Set();
            };
            Client.Directory.DirGroupsReply += DirGroupsReplyDelegate;
            Client.Directory.StartGroupSearch(groupName, 0);
            if (!DirGroupsEvent.WaitOne(millisecondsTimeout, false))
            {
                Client.Directory.DirGroupsReply -= DirGroupsReplyDelegate;
                return false;
            }
            Client.Directory.DirGroupsReply -= DirGroupsReplyDelegate;
            if (localGroupUUID.Equals(UUID.Zero))
                return false;
            groupUUID = localGroupUUID;
            return true;
        }

        ///////////////////////////////////////////////////////////////////////////
        //    Copyright (C) 2013 Wizardry and Steamworks - License: GNU GPLv3    //
        ///////////////////////////////////////////////////////////////////////////
        /// <summary>
        ///     Resolves an agent name to an agent UUID by searching the directory
        ///     services.
        /// </summary>
        /// <param name="agentFirstName">the first name of the agent</param>
        /// <param name="agentLastName">the last name of the agent</param>
        /// <param name="millisecondsTimeout">timeout for the search in milliseconds</param>
        /// <param name="agentUUID">an object to store the agent UUID</param>
        /// <returns>true if the agent name could be resolved to an UUID</returns>
        private static bool AgentNameToUUID(string agentFirstName, string agentLastName, int millisecondsTimeout,
            ref UUID agentUUID)
        {
            UUID localAgentUUID = UUID.Zero;
            ManualResetEvent agentUUIDEvent = new ManualResetEvent(false);
            EventHandler<DirPeopleReplyEventArgs> DirPeopleReplyDelegate = (o, s) =>
            {
                // do not LINQ
                foreach (DirectoryManager.AgentSearchData match in s.MatchedPeople)
                {
                    if (!match.FirstName.Equals(agentFirstName, StringComparison.InvariantCultureIgnoreCase) ||
                        !match.LastName.Equals(agentLastName, StringComparison.InvariantCultureIgnoreCase))
                        continue;
                    localAgentUUID = match.AgentID;
                }
                agentUUIDEvent.Set();
            };
            Client.Directory.DirPeopleReply += DirPeopleReplyDelegate;
            Client.Directory.StartPeopleSearch(
                String.Format(CultureInfo.InvariantCulture, "{0} {1}", agentFirstName, agentLastName), 0);
            if (!agentUUIDEvent.WaitOne(millisecondsTimeout, false))
            {
                Client.Directory.DirPeopleReply -= DirPeopleReplyDelegate;
                return false;
            }
            Client.Directory.DirPeopleReply -= DirPeopleReplyDelegate;
            if (localAgentUUID.Equals(UUID.Zero))
                return false;
            agentUUID = localAgentUUID;
            return true;
        }

        ///////////////////////////////////////////////////////////////////////////
        //    Copyright (C) 2013 Wizardry and Steamworks - License: GNU GPLv3    //
        ///////////////////////////////////////////////////////////////////////////
        /// <summary>
        ///     Resolves an agent UUID to an agent name.
        /// </summary>
        /// <param name="agentUUID">the UUID of the agent</param>
        /// <param name="millisecondsTimeout">timeout for the search in milliseconds</param>
        /// <param name="agentName">an object to store the name of the agent in</param>
        /// <returns>true if the UUID could be resolved to a name</returns>
        private static bool AgentUUIDToName(UUID agentUUID, int millisecondsTimeout, ref string agentName)
        {
            if (agentUUID.Equals(UUID.Zero))
                return false;
            string localAgentName = string.Empty;
            ManualResetEvent agentNameEvent = new ManualResetEvent(false);
            EventHandler<UUIDNameReplyEventArgs> UUIDNameReplyDelegate = (o, s) =>
            {
                // do not LINQ
                foreach (KeyValuePair<UUID, string> match in s.Names)
                {
                    if (!match.Value.Equals(string.Empty))
                    {
                        localAgentName = match.Value;
                    }
                }
                agentNameEvent.Set();
            };
            Client.Avatars.UUIDNameReply += UUIDNameReplyDelegate;
            Client.Avatars.RequestAvatarName(agentUUID);
            if (!agentNameEvent.WaitOne(millisecondsTimeout, false))
            {
                Client.Avatars.UUIDNameReply -= UUIDNameReplyDelegate;
                return false;
            }
            Client.Avatars.UUIDNameReply -= UUIDNameReplyDelegate;
            if (localAgentName == null)
                return false;
            agentName = localAgentName;
            return true;
        }

        ///////////////////////////////////////////////////////////////////////////
        //    Copyright (C) 2013 Wizardry and Steamworks - License: GNU GPLv3    //
        ///////////////////////////////////////////////////////////////////////////
        /// ///
        /// <summary>
        ///     Resolves a role name to a role UUID.
        /// </summary>
        /// <param name="roleName">the name of the role to be resolved to an UUID</param>
        /// <param name="groupUUID">the UUID of the group to query for the role UUID</param>
        /// <param name="millisecondsTimeout">timeout for the search in milliseconds</param>
        /// <param name="roleUUID">an UUID object to store the role UUID in</param>
        /// <returns>true if the role could be found</returns>
        private static bool RoleNameToRoleUUID(string roleName, UUID groupUUID, int millisecondsTimeout,
            ref UUID roleUUID)
        {
            UUID localRoleUUID = UUID.Zero;
            ManualResetEvent GroupRoleDataEvent = new ManualResetEvent(false);
            EventHandler<GroupRolesDataReplyEventArgs> GroupRoleDataReplyDelegate = (o, s) =>
            {
                foreach (KeyValuePair<UUID, GroupRole> match in s.Roles)
                {
                    if (!match.Value.Name.Equals(roleName, StringComparison.InvariantCultureIgnoreCase)) continue;
                    localRoleUUID = match.Key;
                }
                GroupRoleDataEvent.Set();
            };
            Client.Groups.GroupRoleDataReply += GroupRoleDataReplyDelegate;
            Client.Groups.RequestGroupRoles(groupUUID);
            if (!GroupRoleDataEvent.WaitOne(millisecondsTimeout, false))
            {
                Client.Groups.GroupRoleDataReply -= GroupRoleDataReplyDelegate;
                return false;
            }
            Client.Groups.GroupRoleDataReply -= GroupRoleDataReplyDelegate;
            if (localRoleUUID.Equals(UUID.Zero))
                return false;
            roleUUID = localRoleUUID;
            return true;
        }

        ///////////////////////////////////////////////////////////////////////////
        //    Copyright (C) 2013 Wizardry and Steamworks - License: GNU GPLv3    //
        ///////////////////////////////////////////////////////////////////////////
        /// <summary>
        ///     Resolves a role name to a role UUID.
        /// </summary>
        /// <param name="RoleUUID">the UUID of the role to be resolved to a name</param>
        /// <param name="GroupUUID">the UUID of the group to query for the role name</param>
        /// <param name="millisecondsTimeout">timeout for the search in milliseconds</param>
        /// <param name="roleName">a string object to store the role name in</param>
        /// <returns>true if the role could be resolved</returns>
        private static bool RoleUUIDToName(UUID RoleUUID, UUID GroupUUID, int millisecondsTimeout, ref string roleName)
        {
            if (RoleUUID.Equals(UUID.Zero) || GroupUUID.Equals(UUID.Zero))
                return false;
            string localRoleName = string.Empty;
            ManualResetEvent GroupRoleDataEvent = new ManualResetEvent(false);
            EventHandler<GroupRolesDataReplyEventArgs> GroupRoleDataReplyDelegate = (o, s) =>
            {
                foreach (KeyValuePair<UUID, GroupRole> match in s.Roles)
                {
                    if (!match.Key.Equals(RoleUUID)) continue;
                    localRoleName = match.Value.Name;
                }
                GroupRoleDataEvent.Set();
            };
            Client.Groups.GroupRoleDataReply += GroupRoleDataReplyDelegate;
            Client.Groups.RequestGroupRoles(GroupUUID);
            if (!GroupRoleDataEvent.WaitOne(millisecondsTimeout, false))
            {
                Client.Groups.GroupRoleDataReply -= GroupRoleDataReplyDelegate;
                return false;
            }
            Client.Groups.GroupRoleDataReply -= GroupRoleDataReplyDelegate;
            if (string.IsNullOrEmpty(localRoleName)) return false;
            roleName = localRoleName;
            return true;
        }

        #endregion

        #region KEY-VALUE DATA

        ///////////////////////////////////////////////////////////////////////////
        //    Copyright (C) 2013 Wizardry and Steamworks - License: GNU GPLv3    //
        ///////////////////////////////////////////////////////////////////////////
        /// <summary>
        ///     Returns the value of a key for the Wizardry and Steamworks key-value
        ///     data structure built for HTTP interop.
        /// </summary>
        /// <param name="key">the key of the value</param>
        /// <param name="data">the key-value data segment</param>
        /// <returns>true if the key was found in data</returns>
        private static string wasKeyValueGet(string key, string data)
        {
            string output = string.Empty;
            if (string.IsNullOrEmpty(key) || string.IsNullOrEmpty(data))
                return output;

            object LockObject = new object();

            Parallel.ForEach(data.Split('&')
                .Select(pair => pair.Split('='))
                .Where(pair => pair.Length.Equals(2) && pair[0].Equals(key, StringComparison.InvariantCulture)), pair =>
                {
                    lock (LockObject)
                    {
                        output = pair[1];
                    }
                });
            return output;
        }

        ///////////////////////////////////////////////////////////////////////////
        //    Copyright (C) 2014 Wizardry and Steamworks - License: GNU GPLv3    //
        ///////////////////////////////////////////////////////////////////////////
        /// <summary>
        ///     Returns a key-value data string with a key set to a given value.
        /// </summary>
        /// <param name="key">the key of the value</param>
        /// <param name="value">the value to set the key to</param>
        /// <param name="data">the key-value data segment</param>
        /// <returns>
        ///     a key-value data string or the empty string if either key or
        ///     value are empty
        /// </returns>
        private static string wasKeyValueSet(string key, string value, string data)
        {
            Dictionary<string, string> output = new Dictionary<string, string>();
            if (string.IsNullOrEmpty(key) || string.IsNullOrEmpty(value))
                return string.Empty;

            if (string.IsNullOrEmpty(data))
                return string.Join("=", new[] {key, value});

            object LockObject = new object();
            Parallel.ForEach(data.Split('&')
                .Select(pair => pair.Split('=')), pair =>
                {
                    if (pair[0].Equals(key, StringComparison.InvariantCulture))
                    {
                        if (!output.ContainsKey(key))
                        {
                            lock (LockObject)
                            {
                                output.Add(key, value);
                            }
                        }
                        return;
                    }
                    if (!output.ContainsKey(pair[0]))
                    {
                        lock (LockObject)
                        {
                            output.Add(pair[0], pair[1]);
                        }
                    }
                });

            if (!output.ContainsKey(key))
            {
                output.Add(key, value);
            }

            return string.Join("&", output.Select(p => string.Join("=", new[] {p.Key, p.Value})).ToArray());
        }

        ///////////////////////////////////////////////////////////////////////////
        //    Copyright (C) 2014 Wizardry and Steamworks - License: GNU GPLv3    //
        ///////////////////////////////////////////////////////////////////////////
        /// <summary>
        ///     Decodes key-value pair data to a dictionary.
        /// </summary>
        /// <param name="data">the key-value pair data</param>
        /// <returns>a dictionary containing the keys and values</returns>
        private static Dictionary<string, string> wasKeyValueDecode(string data)
        {
            Dictionary<string, string> output = new Dictionary<string, string>();
            if (string.IsNullOrEmpty(data))
                return output;

            object LockObject = new object();
            Parallel.ForEach(data.Split('&')
                .Select(pair => pair.Split('='))
                .Where(pair => pair.Length.Equals(2)), pair =>
                {
                    if (!output.ContainsKey(pair[0]))
                    {
                        lock (LockObject)
                        {
                            output.Add(pair[0], pair[1]);
                        }
                    }
                });
            return output;
        }

        ///////////////////////////////////////////////////////////////////////////
        //    Copyright (C) 2014 Wizardry and Steamworks - License: GNU GPLv3    //
        ///////////////////////////////////////////////////////////////////////////
        /// <summary>
        ///     Serialises a dictionary to key-value data.
        /// </summary>
        /// <param name="data">a dictionary</param>
        /// <returns>a key-value data encoded string</returns>
        private static string wasKeyValueEncode(Dictionary<string, string> data)
        {
            HashSet<string> output = new HashSet<string>();
            if (data.Count.Equals(0))
                return string.Empty;

            object LockObject = new object();
            Parallel.ForEach(data.Where(pair => !string.IsNullOrEmpty(pair.Key) && !string.IsNullOrEmpty(pair.Value)),
                pair =>
                {
                    lock (LockObject)
                    {
                        output.Add(string.Join("=", new[] {pair.Key, pair.Value}));
                    }
                });

            return string.Join("&", output.ToArray());
        }

        ///////////////////////////////////////////////////////////////////////////
        //    Copyright (C) 2014 Wizardry and Steamworks - License: GNU GPLv3    //
        ///////////////////////////////////////////////////////////////////////////
        /// <summary>Escapes a dictionary's keys and values for sending as POST data.</summary>
        /// <param name="data">A dictionary containing keys and values to be escaped</param>
        private static Dictionary<string, string> wasKeyValueEscape(Dictionary<string, string> data)
        {
            return data.ToDictionary(
                o => Uri.EscapeDataString(o.Key),
                o => Uri.EscapeDataString(o.Value)
                );
        }

        #endregion

        /// <summary>
        ///     Possible actions.
        /// </summary>
        private struct Action
        {
            public const string GET = "get";
            public const string SET = "set";
            public const string ADD = "add";
            public const string REMOVE = "remove";
            public const string START = "start";
            public const string STOP = "stop";
            public const string MUTE = "mute";
            public const string UNMUTE = "unmute";
            public const string RESTART = "restart";
            public const string CANCEL = "cancel";
        }

        /// <summary>
        ///     An agent anme structure.
        /// </summary>
        private struct AgentName
        {
            public string FirstName;
            public string LastName;

            public AgentName FromFullName(string fullName)
            {
                string[] name = fullName.Split(new[] {' ', '.'}, StringSplitOptions.RemoveEmptyEntries);
                if (name.Length != 2)
                {
                    FirstName = string.Empty;
                    LastName = string.Empty;
                    return this;
                }
                FirstName = name[0];
                LastName = name[1];
                return this;
            }
        }

        private struct Configuration
        {
            public static string FIRST_NAME;

            public static string LAST_NAME;

            public static string PASSWORD;

            public static string LOGIN_URL;

            public static int CALLBACK_TIMEOUT;

            public static int SERVICES_TIMEOUT;

            public static int IDLE_JOIN_GROUP_CHAT_TIME;

            public static bool TOS_ACCEPTED;

            public static string START_LOCATION;

            public static string LOG_FILE;

            public static int GROUP_CREATE_FEE;

            public static HashSet<Group> GROUPS;

            public static HashSet<Master> MASTERS;

            public static void Load(string file)
            {
                FIRST_NAME = string.Empty;
                LAST_NAME = string.Empty;
                PASSWORD = string.Empty;
                LOGIN_URL = string.Empty;
                CALLBACK_TIMEOUT = 5000;
                SERVICES_TIMEOUT = 60000;
                IDLE_JOIN_GROUP_CHAT_TIME = 60000;
                TOS_ACCEPTED = false;
                START_LOCATION = "last";
                LOG_FILE = "Corrade.log";
                GROUP_CREATE_FEE = 100;
                GROUPS = new HashSet<Group>();
                MASTERS = new HashSet<Master>();

                try
                {
                    file = File.ReadAllText(file);
                }
                catch (Exception e)
                {
                    Feedback(ConsoleError.INVALID_CONFIGURATION_FILE, e.Message);
                    Environment.Exit(1);
                }

                XmlDocument conf = new XmlDocument();
                try
                {
                    conf.LoadXml(file);
                }
                catch (XmlException e)
                {
                    Feedback(ConsoleError.INVALID_CONFIGURATION_FILE, e.Message);
                    Environment.Exit(1);
                }

                XmlNode root = conf.DocumentElement;
                if (root == null)
                {
                    Feedback(ConsoleError.INVALID_CONFIGURATION_FILE);
                    Environment.Exit(1);
                }
                if (root != null)
                {
                    XmlNodeList nodeList = root.SelectNodes("/config/client/*");
                    if (nodeList == null)
                        return;
                    try
                    {
                        foreach (XmlNode client in nodeList)
                            switch (client.Name.ToLower(CultureInfo.InvariantCulture))
                            {
                                case ConfigurationKeys.FIRST_NAME:
                                    if (string.IsNullOrEmpty(client.InnerText))
                                    {
                                        throw new Exception("error in client section");
                                    }
                                    FIRST_NAME = client.InnerText;
                                    break;
                                case ConfigurationKeys.LAST_NAME:
                                    if (string.IsNullOrEmpty(client.InnerText))
                                    {
                                        throw new Exception("error in client section");
                                    }
                                    LAST_NAME = client.InnerText;
                                    break;
                                case ConfigurationKeys.PASSWORD:
                                    if (string.IsNullOrEmpty(client.InnerText))
                                    {
                                        throw new Exception("error in client section");
                                    }
                                    PASSWORD = client.InnerText;
                                    break;
                                case ConfigurationKeys.LOGIN_URL:
                                    if (string.IsNullOrEmpty(client.InnerText))
                                    {
                                        throw new Exception("error in client section");
                                    }
                                    LOGIN_URL = client.InnerText;
                                    break;
                                case ConfigurationKeys.CALLBACK_TIMEOUT:
                                    if (!int.TryParse(client.InnerText, out CALLBACK_TIMEOUT))
                                    {
                                        throw new Exception("error in client section");
                                    }
                                    break;
                                case ConfigurationKeys.SERVICES_TIMEOUT:
                                    if (!int.TryParse(client.InnerText, out SERVICES_TIMEOUT))
                                    {
                                        throw new Exception("error in client section");
                                    }
                                    break;
                                case ConfigurationKeys.TOS_ACCEPTED:
                                    if (!bool.TryParse(client.InnerText, out TOS_ACCEPTED))
                                    {
                                        throw new Exception("error in client section");
                                    }
                                    break;
                                case ConfigurationKeys.GROUP_CREATE_FEE:
                                    if (!int.TryParse(client.InnerText, out GROUP_CREATE_FEE))
                                    {
                                        throw new Exception("error in client section");
                                    }
                                    break;
                                case ConfigurationKeys.START_LOCATION:
                                    if (string.IsNullOrEmpty(client.InnerText))
                                    {
                                        throw new Exception("error in client section");
                                    }
                                    START_LOCATION = client.InnerText;
                                    break;
                                case ConfigurationKeys.LOG:
                                    if (string.IsNullOrEmpty(client.InnerText))
                                    {
                                        throw new Exception("error in client section");
                                    }
                                    LOG_FILE = client.InnerText;
                                    break;
                                case ConfigurationKeys.IDLE_JOIN_GROUP_CHAT_TIME:
                                    if (!int.TryParse(client.InnerText, out IDLE_JOIN_GROUP_CHAT_TIME))
                                    {
                                        throw new Exception("error in client section");
                                    }
                                    break;
                            }
                    }
                    catch (Exception e)
                    {
                        Feedback(ConsoleError.INVALID_CONFIGURATION_FILE, e.Message);
                        Environment.Exit(1);
                    }

                    // Process masters.
                    nodeList = root.SelectNodes("/config/masters/*");
                    if (nodeList == null)
                        return;
                    try
                    {
                        foreach (XmlNode mastersNode in nodeList)
                        {
                            Master configMaster = new Master();
                            foreach (XmlNode masterNode in mastersNode.ChildNodes)
                            {
                                switch (masterNode.Name.ToLower(CultureInfo.InvariantCulture))
                                {
                                    case ConfigurationKeys.FIRST_NAME:
                                        if (string.IsNullOrEmpty(masterNode.InnerText))
                                        {
                                            throw new Exception("error in masters section");
                                        }
                                        configMaster.FirstName = masterNode.InnerText;
                                        break;
                                    case ConfigurationKeys.LAST_NAME:
                                        if (string.IsNullOrEmpty(masterNode.InnerText))
                                        {
                                            throw new Exception("error in masters section");
                                        }
                                        configMaster.LastName = masterNode.InnerText;
                                        break;
                                }
                            }
                            MASTERS.Add(configMaster);
                        }
                    }
                    catch (Exception e)
                    {
                        Feedback(ConsoleError.INVALID_CONFIGURATION_FILE, e.Message);
                        Environment.Exit(1);
                    }

                    // Process groups.
                    nodeList = root.SelectNodes("/config/groups/*");
                    if (nodeList == null)
                        return;
                    try
                    {
                        foreach (XmlNode groupsNode in nodeList)
                        {
                            Group configGroup = new Group();
                            foreach (XmlNode groupNode in groupsNode.ChildNodes)
                            {
                                switch (groupNode.Name.ToLower(CultureInfo.InvariantCulture))
                                {
                                    case ConfigurationKeys.NAME:
                                        if (string.IsNullOrEmpty(groupNode.InnerText))
                                        {
                                            throw new Exception("error in group section");
                                        }
                                        configGroup.Name = groupNode.InnerText;
                                        break;
                                    case ConfigurationKeys.UUID:
                                        if (!UUID.TryParse(groupNode.InnerText, out configGroup.UUID))
                                        {
                                            throw new Exception("error in group section");
                                        }
                                        break;
                                    case ConfigurationKeys.PASSWORD:
                                        if (string.IsNullOrEmpty(groupNode.InnerText))
                                        {
                                            throw new Exception("error in group section");
                                        }
                                        configGroup.Password = groupNode.InnerText;
                                        break;
                                    case ConfigurationKeys.WORKERS:
                                        if (!int.TryParse(groupNode.InnerText, out configGroup.Workers))
                                        {
                                            throw new Exception("error in group section");
                                        }
                                        break;
                                    case ConfigurationKeys.CHATLOG:
                                        if (string.IsNullOrEmpty(groupNode.InnerText))
                                        {
                                            throw new Exception("error in group section");
                                        }
                                        configGroup.ChatLog = groupNode.InnerText;
                                        break;
                                    case ConfigurationKeys.DATABASE:
                                        if (string.IsNullOrEmpty(groupNode.InnerText))
                                        {
                                            throw new Exception("error in group section");
                                        }
                                        configGroup.DatabaseFile = groupNode.InnerText;
                                        break;
                                    case ConfigurationKeys.PERMISSIONS:
                                        XmlNodeList permissionNodeList = groupNode.SelectNodes("*");
                                        if (permissionNodeList == null)
                                        {
                                            throw new Exception("error in group permission section");
                                        }
                                        int permissionMask = 0;
                                        const Permissions permissions = new Permissions();
                                        foreach (XmlNode permissioNode in permissionNodeList)
                                        {
                                            XmlNode node = permissioNode;
                                            Parallel.ForEach(
                                                wasGetEnumDescriptions(permissions).Where(name => name.Equals(node.Name,
                                                    StringComparison.InvariantCultureIgnoreCase)), name =>
                                                    {
                                                        bool granted;
                                                        if (!bool.TryParse(node.InnerText, out granted))
                                                        {
                                                            throw new Exception("error in group permission section");
                                                        }
                                                        if (granted)
                                                        {
                                                            permissionMask = permissionMask |
                                                                             (int)
                                                                                 wasGetEnumValueFromDescription(
                                                                                     permissions, name);
                                                        }
                                                    });
                                        }
                                        configGroup.PermissionMask = permissionMask;
                                        break;
                                    case ConfigurationKeys.NOTIFICATIONS:
                                        XmlNodeList notificationNodeList = groupNode.SelectNodes("*");
                                        if (notificationNodeList == null)
                                        {
                                            throw new Exception("error in group notification section");
                                        }
                                        int notificationMask = 0;
                                        const Notifications notifications = new Notifications();
                                        foreach (XmlNode notificationNode in notificationNodeList)
                                        {
                                            XmlNode node = notificationNode;
                                            Parallel.ForEach(
                                                wasGetEnumDescriptions(permissions).Where(name => name.Equals(node.Name,
                                                    StringComparison.InvariantCultureIgnoreCase)), name =>
                                                    {
                                                        bool granted;
                                                        if (!bool.TryParse(node.InnerText, out granted))
                                                        {
                                                            throw new Exception("error in group notification section");
                                                        }
                                                        if (granted)
                                                        {
                                                            notificationMask = notificationMask |
                                                                               (int)
                                                                                   wasGetEnumValueFromDescription(
                                                                                       notifications, name);
                                                        }
                                                    });
                                        }
                                        configGroup.NotificationMask = notificationMask;
                                        break;
                                }
                            }
                            GROUPS.Add(configGroup);
                        }
                    }
                    catch (Exception e)
                    {
                        Feedback(ConsoleError.INVALID_CONFIGURATION_FILE, e.Message);
                        Environment.Exit(1);
                    }
                }
                Feedback(ConsoleError.READ_CONFIGURATION_FILE);
            }
        }

        /// <summary>
        ///     Configuration keys.
        /// </summary>
        private struct ConfigurationKeys
        {
            public const string FIRST_NAME = "firstname";
            public const string LAST_NAME = "lastname";
            public const string LOGIN_URL = "loginurl";
            public const string CALLBACK_TIMEOUT = "callbacktimeout";
            public const string SERVICES_TIMEOUT = "servicestimeout";
            public const string TOS_ACCEPTED = "tosaccepted";
            public const string GROUP_CREATE_FEE = "groupcreatefee";
            public const string START_LOCATION = "startlocation";
            public const string LOG = "log";
            public const string IDLE_JOIN_GROUP_CHAT_TIME = "idlejoingroupchattime";
            public const string NAME = "name";
            public const string UUID = "uuid";
            public const string PASSWORD = "password";
            public const string WORKERS = "workers";
            public const string CHATLOG = "chatlog";
            public const string DATABASE = "database";
            public const string PERMISSIONS = "permissions";
            public const string NOTIFICATIONS = "notifications";
        }

        /// <summary>
        ///     Structure containing error messages printed on console for the owner.
        /// </summary>
        private enum ConsoleError
        {
            [Description("access denied")] ACCESS_DENIED = 1,

            [Description("invalid configuration file")] INVALID_CONFIGURATION_FILE,

            [Description("the Terms of Service (TOS) have not been accepted, please check your configuration file")] TOS_NOT_ACCEPTED,

            [Description("teleport failed")] TELEPORT_FAILED,

            [Description("teleport succeeded")] TELEPORT_SUCCEEDED,

            [Description("accepting teleport lure")] ACCEPTING_TELEPORT_LURE,

            [Description("got server message")] GOT_SERVER_MESSAGE,

            [Description("accepted friendship")] ACCEPTED_FRIENDSHIP,

            [Description("login failed")] LOGIN_FAILED,

            [Description("login succeeded")] LOGIN_SUCCEEDED,

            [Description("failed to set appearance")] APPEARANCE_SET_FAILED,

            [Description("appearance set")] APPEARANCE_SET_SUCCEEDED,

            [Description("all simulators disconnected")] ALL_SIMULATORS_DISCONNECTED,

            [Description("simulator connected")] SIMULATOR_CONNECTED,

            [Description("event queue started")] EVENT_QUEUE_STARTED,

            [Description("disconnected")] DISCONNECTED,

            [Description("logging out")] LOGGING_OUT,

            [Description("logging in")] LOGGING_IN,

            [Description("no workers set for group")] NO_WORKERS_SET_FOR_GROUP,

            [Description("workers exceeded")] WORKERS_EXCEEDED,

            [Description("unable to join group chat")] UNABLE_TO_JOIN_GROUP_CHAT,

            [Description("could not write to group chat logfile")] COULD_NOT_WRITE_TO_GROUP_CHAT_LOGFILE,

            [Description("got inventory offer")] GOT_INVENTORY_OFFER,

            [Description("acceping group invite")] ACCEPTING_GROUP_INVITE,

            [Description("agent not found")] AGENT_NOT_FOUND,

            [Description("got group message")] GOT_GROUP_MESSAGE,

            [Description("got teleport lure")] GOT_TELEPORT_LURE,

            [Description("got group invite")] GOT_GROUP_INVITE,

            [Description("read configuration file")] READ_CONFIGURATION_FILE,

            [Description("configuration file modified")] CONFIGURATION_FILE_MODIFIED,

            [Description("notification could not be sent")] NOTIFICATION_COULD_NOT_BE_SENT,

            [Description("got region message")] GOT_REGION_MESSAGE,

            [Description("got group message")] GOT_GROUP_NOTICE,

            [Description("got insant message")] GOT_INSTANT_MESSAGE
        }

        /// <summary>
        ///     Possible entities.
        /// </summary>
        private struct Entity
        {
            public const string AVATAR = "avatar";
            public const string LOCAL = "local";
            public const string GROUP = "group";
            public const string ESTATE = "estate";
            public const string REGION = "region";
            public const string OBJECT = "object";
            public const string PARCEL = "parcel";
        }

        /// <summary>
        ///     Group structure.
        /// </summary>
        private struct Group
        {
            public string ChatLog;
            public string DatabaseFile;
            public string Name;
            public int NotificationMask;
            public string Password;
            public int PermissionMask;
            public UUID UUID;
            public int Workers;
        }

        /// <summary>
        ///     Linden constants.
        /// </summary>
        private struct LINDEN_CONSTANTS
        {
            public struct ALERTS
            {
                public const string NO_ROOM_TO_SIT_HERE = @"No room to sit here, try another spot.";

                public const string UNABLE_TO_SET_HOME =
                    @"You can only set your 'Home Location' on your land or at a mainland Infohub.";
            }

            public struct DIRECTORY
            {
                public struct EVENT
                {
                    public const int SEARCH_RESULTS_COUNT = 200;
                }

                public struct GROUP
                {
                    public const int SEARCH_RESULTS_COUNT = 100;
                }

                public struct LAND
                {
                    public const int SEARCH_RESULTS_COUNT = 100;
                }

                public struct PEOPLE
                {
                    public const int SEARCH_RESULTS_COUNT = 100;
                }
            }

            public struct GROUPS
            {
                public const int MAXIMUM_NUMBER_OF_ROLES = 10;
            }

            public struct LSL
            {
                public const string CSV_DELIMITER = @", ";
            }
        }

        /// <summary>
        ///     Masters structure.
        /// </summary>
        private struct Master
        {
            public string FirstName;
            public string LastName;
        }

        /// <summary>
        ///     A Corrade notification.
        /// </summary>
        private struct Notification
        {
            public string GROUP;
            public int NOTIFICATION_MASK;
            public string URL;
        }

        /// <summary>
        ///     Corrade notification types.
        /// </summary>
        [Flags]
        private enum Notifications : uint
        {
            [Description("alert")] NOTIFICATION_ALERT_MESSAGE = 1,
            [Description("region")] NOTIFICATION_REGION_MESSAGE = 2,
            [Description("group")] NOTIFICATION_GROUP_MESSAGE = 4,
            [Description("balance")] NOTIFICATION_BALANCE = 8,
            [Description("message")] NOTIFICATION_INSTANT_MESSAGE = 16,
            [Description("notice")] NOTIFICATION_GROUP_NOTICE = 32,
            [Description("local")] NOTIFICATION_LOCAL_CHAT = 64,
            [Description("dialog")] NOTIFICATION_SCRIPT_DIALOG = 128
        }

        /// <summary>
        ///     Corrade permissions.
        /// </summary>
        [Flags]
        private enum Permissions : uint
        {
            [Description("movement")] PERMISSION_MOVEMENT = 1,
            [Description("economy")] PERMISSION_ECONOMY = 2,
            [Description("land")] PERMISSION_LAND = 4,
            [Description("grooming")] PERMISSION_GROOMING = 8,
            [Description("inventory")] PERMISSION_INVENTORY = 16,
            [Description("interact")] PERMISSION_INTERACT = 32,
            [Description("mute")] PERMISSION_MUTE = 64,
            [Description("database")] PERMISSION_DATABASE = 128,
            [Description("notifications")] PERMISSION_NOTIFICATIONS = 256,
            [Description("talk")] PERMISSION_TALK = 512,
            [Description("directory")] PERMISSION_DIRECTORY = 1024
        }

        /// <summary>
        ///     Keys returned by Corrade.
        /// </summary>
        private struct ResultKeys
        {
            public const string INVENTORY = "inventory";

            public const string RUNNING = "running";

            public const string PARTICLESYSTEM = "particlesystem";

            public const string SEARCH = "search";

            public const string LIST = "list";

            public const string TOP = "top";

            public const string ANIMATIONS = "animations";

            public const string DATA = "data";

            public const string ATTACHMENTS = "attachments";

            public const string ROLES = "roles";

            public const string MEMBERS = "members";

            public const string POWERS = "powers";

            public const string BALANCE = "balance";

            public const string OWNERS = "owners";

            public const string ERROR = "error";

            public const string CALLBACKERROR = "callbackerror";

            public const string SUCCESS = "success";

            public const string MUTES = "mutes";

            public const string NOTIFICATIONS = "notifications";
        }

        /// <summary>
        ///     Structure containing errors returned to scripts.
        /// </summary>
        private enum ScriptError
        {
            [Description("could not join group")] COULD_NOT_JOIN_GROUP = 1,

            [Description("could not leave group")] COULD_NOT_LEAVE_GROUP,

            [Description("agent not found")] AGENT_NOT_FOUND,

            [Description("group not found")] GROUP_NOT_FOUND,

            [Description("already in group")] ALREADY_IN_GROUP,

            [Description("not in group")] NOT_IN_GROUP,

            [Description("role not found")] ROLE_NOT_FOUND,

            [Description("command not found")] COMMAND_NOT_FOUND,

            [Description("could not eject agent")] COULD_NOT_EJECT_AGENT,

            [Description("no group power for command")] NO_GROUP_POWER_FOR_COMMAND,

            [Description("cannot eject owners")] CANNOT_EJECT_OWNERS,

            [Description("inventory item not found")] INVENTORY_ITEM_NOT_FOUND,

            [Description("invalid pay amount")] INVALID_PAY_AMOUNT,

            [Description("insufficient funds")] INSUFFICIENT_FUNDS,

            [Description("invalid pay target")] INVALID_PAY_TARGET,

            [Description("timeout waiting for balance")] TIMEOUT_WAITING_FOR_BALANCE,

            [Description("teleport failed")] TELEPORT_FAILED,

            [Description("primitive not found")] PRIMITIVE_NOT_FOUND,

            [Description("could not sit")] COULD_NOT_SIT,

            [Description("no Corrade permissions")] NO_CORRADE_PERMISSIONS,

            [Description("could not create group")] COULD_NOT_CREATE_GROUP,

            [Description("could not create role")] COULD_NOT_CREATE_ROLE,

            [Description("no role name specified")] NO_ROLE_NAME_SPECIFIED,

            [Description("timeout getting group roles members")] TIMEOUT_GETING_GROUP_ROLES_MEMBERS,

            [Description("timeout getting group roles")] TIMEOUT_GETTING_GROUP_ROLES,

            [Description("timeout getting role powers")] TIMEOUT_GETTING_ROLE_POWERS,

            [Description("could not get roles")] COULD_NOT_GET_ROLES,

            [Description("could not get group roles members")] COULD_NOT_GET_GROUP_ROLES_MEMBERS,

            [Description("could not find parcel")] COULD_NOT_FIND_PARCEL,

            [Description("unable to set home")] UNABLE_TO_SET_HOME,

            [Description("unable to go home")] UNABLE_TO_GO_HOME,

            [Description("timeout getting profile")] TIMEOUT_GETTING_PROFILE,

            [Description("texture not found")] TEXTURE_NOT_FOUND,

            [Description("type can only be voice or text")] TYPE_CAN_BE_VOICE_OR_TEXT,

            [Description("agent not in group")] AGENT_NOT_IN_GROUP,

            [Description("failed to get attachments")] FAILED_TO_GET_ATTACHMENTS,

            [Description("empty attachments")] EMPTY_ATTACHMENTS,

            [Description("could not get land users")] COULD_NOT_GET_LAND_USERS,

            [Description("could not demote agent")] COULD_NOT_DEMOTE_AGENT,

            [Description("no region specified")] NO_REGION_SPECIFIED,

            [Description("no position specified")] NO_POSITION_SPECIFIED,

            [Description("empty pick name")] EMPTY_PICK_NAME,

            [Description("unable to join group chat")] UNABLE_TO_JOIN_GROUP_CHAT,

            [Description("invalid position")] INVALID_POSITION,

            [Description("could not find title")] COULD_NOT_FIND_TITLE,

            [Description("fly action can only be start or stop")] FLY_ACTION_START_OR_STOP,

            [Description("invalid proposal text")] INVALID_PROPOSAL_TEXT,

            [Description("invalid proposal quorum")] INVALID_PROPOSAL_QUORUM,

            [Description("invalid proposal majority")] INVALID_PROPOSAL_MAJORITY,

            [Description("invalid proposal duration")] INVALID_PROPOSAL_DURATION,

            [Description("invalid mute target")] INVALID_MUTE_TARGET,

            [Description("invalid action")] INVALID_ACTION,

            [Description("could not update mute list")] COULD_NOT_UPDATE_MUTE_LIST,

            [Description("no database file configured")] NO_DATABASE_FILE_CONFIGURED,

            [Description("no database key specified")] NO_DATABASE_KEY_SPECIFIED,

            [Description("no database value specified")] NO_DATABASE_VALUE_SPECIFIED,

            [Description("unknown database action")] UNKNOWN_DATABASE_ACTION,

            [Description("cannot remove owner role")] CANNOT_REMOVE_OWNER_ROLE,

            [Description("cannot remove user from owner role")] CANNOT_REMOVE_USER_FROM_OWNER_ROLE,

            [Description("timeout getting picks")] TIMEOUT_GETTING_PICKS,

            [Description("maximum number of roles exceeded")] MAXIMUM_NUMBER_OF_ROLES_EXCEEDED,

            [Description("no group powers")] NO_GROUP_POWERS,

            [Description("cannot delete a group member from the everyone role")] CANNOT_DELETE_A_GROUP_MEMBER_FROM_THE_EVERYONE_ROLE,

            [Description("group members are by default in the everyone role")] GROUP_MEMBERS_ARE_BY_DEFAULT_IN_THE_EVERYONE_ROLE,

            [Description("cannot delete the everyone role")] CANNOT_DELETE_THE_EVERYONE_ROLE,

            [Description("invalid url provided")] INVALID_URL_PROVIDED,

            [Description("invalid notification types")] INVALID_NOTIFICATION_TYPES,

            [Description("unknown notifications action")] UNKNOWN_NOTIFICATIONS_ACTION,

            [Description("notification not allowed")] NOTIFICATION_NOT_ALLOWED,

            [Description("no range provided")] NO_RANGE_PROVIDED,

            [Description("unknwon directory search type")] UNKNOWN_DIRECTORY_SEARCH_TYPE,

            [Description("no search text provided")] NO_SEARCH_TEXT_PROVIDED,

            [Description("unknwon restart action")] UNKNOWN_RESTART_ACTION,

            [Description("unknown move action")] UNKNOWN_MOVE_ACTION,

            [Description("timeout getting top scripts")] TIMEOUT_GETTING_TOP_SCRIPTS,

            [Description("timeout waiting for estate list")] TIMEOUT_WAITING_FOR_ESTATE_LIST,

            [Description("unknwon top type")] UNKNOWN_TOP_TYPE,

            [Description("unknown estate list action")] UNKNWON_ESTATE_LIST_ACTION,

            [Description("unknown estate list")] UNKNOWN_ESTATE_LIST,

            [Description("no item specified")] NO_ITEM_SPECIFIED,

            [Description("unknown animation action")] UNKNOWN_ANIMATION_ACTION,

            [Description("no channel specified")] NO_CHANNEL_SPECIFIED,

            [Description("no button index specified")] NO_BUTTON_INDEX_SPECIFIED,

            [Description("no button specified")] NO_BUTTON_SPECIFIED,

            [Description("no land rights")] NO_LAND_RIGHTS,

            [Description("unknown entity")] UNKNOWN_ENTITY,

            [Description("invalid rotation")] INVALID_ROTATION,

            [Description("could not get script state")] COULD_NOT_GET_SCRIPT_STATE,

            [Description("could not set script state")] COULD_NOT_SET_SCRIPT_STATE,

            [Description("item is not a script")] ITEM_IS_NOT_A_SCRIPT,

            [Description("avatar not on simulator")] AVATAR_NOT_ON_SIMULATOR
        }

        /// <summary>
        ///     Keys reconigzed by Corrade.
        /// </summary>
        private struct ScriptKeys
        {
            public const string RETURNPRIMITIVES = "returnprimitives";

            public const string GETGROUPDATA = "getgroupdata";

            public const string GETAVATARDATA = "getavatardata";

            public const string GETPRIMITIVEINVENTORY = "getprimitiveinventory";

            public const string GETINVENTORYDATA = "getinventorydata";

            public const string GETPRIMITIVEINVENTORYDATA = "getprimitiveinventorydata";

            public const string GETSCRIPTRUNNING = "getscriptrunning";

            public const string SETSCRIPTRUNNING = "setscriptrunning";

            public const string DEREZ = "derez";

            public const string GETPARCELDATA = "getparceldata";

            public const string REZ = "rez";

            public const string ROTATION = "rotation";

            public const string INDEX = "index";

            public const string REPLYTODIALOG = "replytodialog";

            public const string OWNER = "owner";

            public const string BUTTON = "button";

            public const string GETANIMATIONS = "getanimations";

            public const string ANIMATION = "animation";

            public const string SETESTATELIST = "setestatelist";

            public const string GETESTATELIST = "getestatelist";

            public const string ALL = "all";

            public const string GETREGIONTOP = "getregiontop";

            public const string RESTARTREGION = "restartregion";

            public const string TIMEOUT = "timeout";

            public const string DIRECTORYSEARCH = "directorysearch";

            public const string GETPROFILEDATA = "getprofiledata";

            public const string GETPARTICLESYSTEM = "getparticlesystem";

            public const string DATA = "data";

            public const string RANGE = "range";

            public const string BALANCE = "balance";

            public const string KEY = "key";

            public const string VALUE = "value";

            public const string DATABASE = "database";

            public const string TEXT = "text";

            public const string QUORUM = "quorum";

            public const string MAJORITY = "majority";

            public const string STARTPROPOSAL = "startproposal";

            public const string DURATION = "duration";

            public const string ACTION = "action";

            public const string DELETEFROMROLE = "deletefromrole";

            public const string ADDTOROLE = "addtorole";

            public const string LEAVE = "leave";

            public const string UPDATEGROUPDATA = "updategroupdata";

            public const string EJECT = "eject";

            public const string INVITE = "invite";

            public const string JOIN = "join";

            public const string CALLBACK = "callback";

            public const string GROUP = "group";

            public const string PASSWORD = "password";

            public const string FIRSTNAME = "firstname";

            public const string LASTNAME = "lastname";

            public const string COMMAND = "command";

            public const string ROLE = "role";

            public const string TITLE = "title";

            public const string TELL = "tell";

            public const string NOTICE = "notice";

            public const string MESSAGE = "message";

            public const string SUBJECT = "subject";

            public const string ITEM = "item";

            public const string PAY = "pay";

            public const string AMOUNT = "amount";

            public const string TARGET = "target";

            public const string REASON = "reason";

            public const string GETBALANCE = "getbalance";

            public const string TELEPORT = "teleport";

            public const string REGION = "region";

            public const string POSITION = "position";

            public const string GETREGIONDATA = "getregiondata";

            public const string SIT = "sit";

            public const string STAND = "stand";

            public const string BAN = "ban";

            public const string PARCELEJECT = "parceleject";

            public const string CREATEGROUP = "creategroup";

            public const string PARCELFREEZE = "parcelfreeze";

            public const string CREATEROLE = "createrole";

            public const string DELETEROLE = "deleterole";

            public const string GETROLESMEMBERS = "getrolesmembers";

            public const string GETROLES = "getroles";

            public const string GETROLEPOWERS = "getrolepowers";

            public const string POWERS = "powers";

            public const string LURE = "lure";

            public const string PARCELMUSIC = "parcelmusic";

            public const string URL = "URL";

            public const string SETHOME = "sethome";

            public const string GOHOME = "gohome";

            public const string SETPROFILEDATA = "setprofiledata";

            public const string GIVE = "give";

            public const string DELETEITEM = "deleteitem";

            public const string EMPTYTRASH = "emptytrash";

            public const string FLY = "fly";

            public const string ADDPICK = "addpick";

            public const string DELETEPICK = "deltepick";

            public const string TOUCH = "touch";

            public const string MODERATE = "moderate";

            public const string TYPE = "type";

            public const string SILENCE = "silence";

            public const string FREEZE = "freeze";

            public const string REBAKE = "rebake";

            public const string GETATTACHMENTS = "getattachments";

            public const string ATTACH = "attach";

            public const string ATTACHMENTS = "attachments";

            public const string DETACH = "detach";

            public const string GETPRIMITIVEOWNERS = "getprimitiveowners";

            public const string ENTITY = "entity";

            public const string CHANNEL = "channel";

            public const string NAME = "name";

            public const string DESCRIPTION = "description";

            public const string GETPRIMITIVEDATA = "getprimitivedata";

            public const string ACTIVATE = "activate";

            public const string MOVE = "move";

            public const string SETTITLE = "settitle";

            public const string MUTE = "mute";

            public const string GETMUTES = "getmutes";

            public const string NOTIFY = "notify";

            public static IEnumerable<string> GetKeys()
            {
                FieldInfo[] p = typeof (ScriptKeys).GetFields();
                return p.Select(v => v.Name.ToLower(CultureInfo.InvariantCulture));
            }
        }

        /// <summary>
        ///     Possible types.
        /// </summary>
        private struct Type
        {
            public const string TEXT = "text";
            public const string VOICE = "voice";
            public const string SCRIPTS = "scripts";
            public const string COLLIDERS = "colliders";
            public const string BAN = "ban";
            public const string GROUP = "group";
            public const string USER = "user";
            public const string MANAGER = "manager";
            public const string CLASSIFIED = "classified";
            public const string EVENT = "event";
            public const string LAND = "land";
            public const string PEOPLE = "people";
            public const string PLACE = "place";
        }

        private delegate string WorkerDelgate(string message);
    }
}