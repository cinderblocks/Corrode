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
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Configuration.Install;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Reflection;
using System.ServiceProcess;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web;
using System.Xml;
using System.Xml.Serialization;
using AIMLbot;
using CSJ2K;
using SkiaSharp;
using OpenMetaverse;
using OpenMetaverse.Rendering;
using Parallel = System.Threading.Tasks.Parallel;
using Path = System.IO.Path;
using ThreadState = System.Threading.ThreadState;
using Timer = System.Timers.Timer;

namespace Corrode
{
    public partial class Corrode : ServiceBase
    {
        public delegate bool EventHandler(NativeMethods.CtrlType ctrlType);

        public Corrode()
        {
            if (Environment.UserInteractive) return;
            // *FIXME: 
            //try
            //{
            //    InstalledServiceName = (string)
            //        new ManagementObjectSearcher("SELECT * FROM Win32_Service where ProcessId = " +
            //                                     Process.GetCurrentProcess().Id).Get()
            //            .Cast<ManagementBaseObject>()
            //            .First()["Name"];
            //}
            //catch (Exception)
            {
                InstalledServiceName = CORRADE_CONSTANTS.DEFAULT_SERVICE_NAME;
            }
            CorrodeEventLog.Source = InstalledServiceName;
            CorrodeEventLog.Log = CORRADE_CONSTANTS.LOG_FACILITY;
            ((ISupportInitialize) (CorrodeEventLog)).BeginInit();
            if (!EventLog.SourceExists(CorrodeEventLog.Source))
            {
                EventLog.CreateEventSource(CorrodeEventLog.Source, CorrodeEventLog.Log);
            }
            ((ISupportInitialize) (CorrodeEventLog)).EndInit();
        }

        /// <summary>
        ///     Sweep for group members.
        /// </summary>
        private static void GroupMembershipSweep()
        {
            Queue<UUID> groupUUIDs = new Queue<UUID>();
            Queue<int> memberCount = new Queue<int>();
            // The total list of members.
            HashSet<UUID> groupMembers = new HashSet<UUID>();
            // New members that have joined the group.
            HashSet<UUID> joinedMembers = new HashSet<UUID>();
            // Members that have parted the group.
            HashSet<UUID> partedMembers = new HashSet<UUID>();

            ManualResetEvent GroupMembersReplyEvent = new ManualResetEvent(false);
            EventHandler<GroupMembersReplyEventArgs> HandleGroupMembersReplyDelegate = (sender, args) =>
            {
                lock (GroupMembersLock)
                {
                    if (!GroupMembers.ContainsKey(args.GroupID))
                    {
                        groupMembers.UnionWith(args.Members.Values.Select(o => o.ID));
                        GroupMembersReplyEvent.Set();
                        return;
                    }
                }
                object LockObject = new object();
                Parallel.ForEach(
                    args.Members.Values,
                    o =>
                    {
                        lock (GroupMembersLock)
                        {
                            if (GroupMembers[args.GroupID].Contains(o.ID)) return;
                        }
                        lock (LockObject)
                        {
                            joinedMembers.Add(o.ID);
                        }
                    });
                lock (GroupMembersLock)
                {
                    Parallel.ForEach(
                        GroupMembers[args.GroupID],
                        o =>
                        {
                            if (args.Members.Values.AsParallel().Any(p => p.ID.Equals(o))) return;
                            lock (LockObject)
                            {
                                partedMembers.Add(o);
                            }
                        });
                }
            };

            while (runGroupMembershipSweepThread)
            {
                Thread.Sleep(Configuration.MEMBERSHIP_SWEEP_INTERVAL);
                if (!Client.Network.Connected) continue;

                HashSet<UUID> currentGroups = new HashSet<UUID>();
                if (!GetCurrentGroups(Configuration.SERVICES_TIMEOUT, Configuration.DATA_TIMEOUT, ref currentGroups))
                    continue;

                // Enqueue configured groups that are currently joined groups.
                groupUUIDs.Clear();
                object LockObject = new object();
                Parallel.ForEach(Configuration.GROUPS.AsParallel().Select(o => new {group = o, groupUUID = o.UUID})
                    .Where(p => currentGroups.Any(o => o.Equals(p.groupUUID)))
                    .Select(o => o.group), o =>
                    {
                        lock (LockObject)
                        {
                            groupUUIDs.Enqueue(o.UUID);
                        }
                    });


                // Bail if no configured groups are also joined.
                if (groupUUIDs.Count.Equals(0)) continue;

                // Get the last member count.
                memberCount.Clear();
                lock (GroupMembersLock)
                {
                    Parallel.ForEach(GroupMembers.AsParallel().SelectMany(
                        members => groupUUIDs,
                        (members, groupUUID) => new {members, groupUUID})
                        .Where(o => o.groupUUID.Equals(o.members.Key))
                        .Select(p => p.members), o =>
                        {
                            lock (LockObject)
                            {
                                memberCount.Enqueue(o.Value.Count);
                            }
                        });
                }

                do
                {
                    // Pause a second between group sweeps.
                    Thread.Sleep(1000);
                    // Dequeue the first group.
                    UUID groupUUID = groupUUIDs.Dequeue();
                    // Clear the total list of members.
                    groupMembers.Clear();
                    // Clear the members that have joined the group.
                    joinedMembers.Clear();
                    // Clear the members that have left the group.
                    partedMembers.Clear();
                    lock (ClientInstanceGroupsLock)
                    {
                        Client.Groups.GroupMembersReply += HandleGroupMembersReplyDelegate;
                        GroupMembersReplyEvent.Reset();
                        Client.Groups.RequestGroupMembers(groupUUID);
                        if (!GroupMembersReplyEvent.WaitOne(Configuration.SERVICES_TIMEOUT, false))
                        {
                            Client.Groups.GroupMembersReply -= HandleGroupMembersReplyDelegate;
                            continue;
                        }
                        Client.Groups.GroupMembersReply -= HandleGroupMembersReplyDelegate;
                    }
                    lock (GroupMembersLock)
                    {
                        if (!GroupMembers.ContainsKey(groupUUID))
                        {
                            GroupMembers.Add(groupUUID, new HashSet<UUID>(groupMembers));
                            continue;
                        }
                    }
                    if (!memberCount.Count.Equals(0))
                    {
                        if (!memberCount.Dequeue().Equals(groupMembers.Count))
                        {
                            if (!joinedMembers.Count.Equals(0))
                            {
                                Parallel.ForEach(
                                    joinedMembers,
                                    o =>
                                    {
                                        string agentName = string.Empty;
                                        if (AgentUUIDToName(
                                            o,
                                            Configuration.SERVICES_TIMEOUT,
                                            ref agentName))
                                        {
                                            CorrodeThreadPool[CorrodeThreadType.NOTIFICATION].Spawn(
                                                () =>
                                                    SendNotification(
                                                        Notifications.NOTIFICATION_GROUP_MEMBERSHIP,
                                                        new GroupMembershipEventArgs
                                                        {
                                                            AgentName = agentName,
                                                            AgentUUID = o,
                                                            Action = Action.JOINED
                                                        }),
                                                Configuration.MAXIMUM_NOTIFICATION_THREADS);
                                        }
                                    });
                            }
                            joinedMembers.Clear();
                            if (!partedMembers.Count.Equals(0))
                            {
                                Parallel.ForEach(
                                    partedMembers,
                                    o =>
                                    {
                                        string agentName = string.Empty;
                                        if (AgentUUIDToName(
                                            o,
                                            Configuration.SERVICES_TIMEOUT,
                                            ref agentName))
                                        {
                                            CorrodeThreadPool[CorrodeThreadType.NOTIFICATION].Spawn(
                                                () =>
                                                    SendNotification(
                                                        Notifications.NOTIFICATION_GROUP_MEMBERSHIP,
                                                        new GroupMembershipEventArgs
                                                        {
                                                            AgentName = agentName,
                                                            AgentUUID = o,
                                                            Action = Action.PARTED
                                                        }),
                                                Configuration.MAXIMUM_NOTIFICATION_THREADS);
                                        }
                                    });
                            }
                        }
                        partedMembers.Clear();
                    }
                    lock (GroupMembersLock)
                    {
                        GroupMembers[groupUUID].Clear();
                        foreach (UUID member in groupMembers)
                        {
                            GroupMembers[groupUUID].Add(member);
                        }
                    }
                    groupMembers.Clear();
                } while (!groupUUIDs.Count.Equals(0) && runGroupMembershipSweepThread);
            }
        }

        private static bool ConsoleCtrlCheck(NativeMethods.CtrlType ctrlType)
        {
            // Set the user disconnect semaphore.
            ConnectionSemaphores['u'].Set();
            // Wait for threads to finish.
            Thread.Sleep(Configuration.SERVICES_TIMEOUT);
            return true;
        }
        
        /// <summary>
        ///     Combine multiple paths.
        /// </summary>
        /// <param name="paths">an array of paths</param>
        /// <returns>a combined path</returns>
        private static string wasPathCombine(params string[] paths)
        {
            if (paths.Length.Equals(0)) return string.Empty;
            return paths.Length < 2
                ? paths[0]
                : Path.Combine(Path.Combine(paths[0], paths[1]), wasPathCombine(paths.Skip(2).ToArray()));
        }

        /// <summary>
        ///     Retrives all the attributes of type T from an enumeration.
        /// </summary>
        /// <returns>a list of type T attributes</returns>
        private static IEnumerable<T> wasGetEnumAttributes<T>()
        {
            return typeof (T).GetFields(BindingFlags.Static | BindingFlags.Public)
                .AsParallel().Select(o => wasGetAttributeFromEnumValue<T>((Enum) o.GetValue(null)));
        }

        /// <summary>
        ///     Retrieves an attribute of type T from an enumeration.
        /// </summary>
        /// <returns>an attribute of type T</returns>
        private static T wasGetAttributeFromEnumValue<T>(Enum value)
        {
            return (T) value.GetType()
                .GetField(value.ToString())
                .GetCustomAttributes(typeof (T), false)
                .SingleOrDefault();
        }

        /// <summary>
        ///     Returns all the field descriptions of an enumeration.
        /// </summary>
        /// <returns>the field descriptions</returns>
        private static IEnumerable<string> wasGetEnumDescriptions<T>()
        {
            return typeof (T).GetFields(BindingFlags.Static | BindingFlags.Public)
                .AsParallel().Select(o => wasGetDescriptionFromEnumValue((Enum) o.GetValue(null)));
        }

        /// <summary>
        ///     Get the description from an enumeration value.
        /// </summary>
        /// <param name="value">an enumeration value</param>
        /// <returns>the description or the empty string</returns>
        private static string wasGetDescriptionFromEnumValue(Enum value)
        {
            DescriptionAttribute attribute = value.GetType()
                .GetField(value.ToString())
                .GetCustomAttributes(typeof (DescriptionAttribute), false)
                .SingleOrDefault() as DescriptionAttribute;
            return attribute != null ? attribute.Description : string.Empty;
        }

        /// <summary>
        ///     Get enumeration value from its description.
        /// </summary>
        /// <typeparam name="T">the enumeration type</typeparam>
        /// <param name="description">the description of a member</param>
        /// <returns>the value or the default of T if case no description found</returns>
        private static T wasGetEnumValueFromDescription<T>(string description)
        {
            var field = typeof (T).GetFields()
                .AsParallel().SelectMany(f => f.GetCustomAttributes(
                    typeof (DescriptionAttribute), false), (
                        f, a) => new {Field = f, Att = a}).SingleOrDefault(a => ((DescriptionAttribute) a.Att)
                            .Description.Equals(description));
            return field != null ? (T) field.Field.GetRawConstantValue() : default(T);
        }

        /// <summary>
        ///     Get the description of structure member.
        /// </summary>
        /// <typeparam name="T">the type of the structure to search</typeparam>
        /// <param name="structure">the structure to search</param>
        /// <param name="item">the value of the item to search</param>
        /// <returns>the description or the empty string</returns>
        private static string wasGetStructureMemberDescription<T>(T structure, object item) where T : struct
        {
            var field = typeof (T).GetFields()
                .AsParallel().SelectMany(f => f.GetCustomAttributes(typeof (DescriptionAttribute), false),
                    (f, a) => new {Field = f, Att = a}).SingleOrDefault(f => f.Field.GetValue(structure).Equals(item));
            return field != null ? ((DescriptionAttribute) field.Att).Description : string.Empty;
        }

        /// <summary>
        ///     Swaps two integers passed by reference using XOR.
        /// </summary>
        /// <param name="q">first integer to swap</param>
        /// <param name="p">second integer to swap</param>
        private static void wasXORSwap(ref int q, ref int p)
        {
            q ^= p;
            p ^= q;
            q ^= p;
        }

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
                if (fi.FieldType.FullName.Split('.', '+')
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
                if (pi.PropertyType.FullName.Split('.', '+')
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

        /// <summary>
        ///     This is a wrapper for both FieldInfo and PropertyInfo SetValue.
        /// </summary>
        /// <param name="info">either a FieldInfo or PropertyInfo</param>
        /// <param name="object">the object to set the value on</param>
        /// <param name="value">the value to set</param>
        private static void wasSetInfoValue<TK, TV>(TK info, ref TV @object, object value)
        {
            object o = @object;
            FieldInfo fi = (object) info as FieldInfo;
            if (fi != null)
            {
                fi.SetValue(o, value);
                @object = (TV) o;
                return;
            }
            PropertyInfo pi = (object) info as PropertyInfo;
            if (pi != null)
            {
                pi.SetValue(o, value, null);
                @object = (TV) o;
            }
        }

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
                return fi.GetValue(value);
            }
            PropertyInfo pi = (object) info as PropertyInfo;
            if (pi != null)
            {
                if (!pi.GetIndexParameters().Length.Equals(0))
                {
                    return value;
                }
                return pi.GetValue(value, null);
            }
            return null;
        }

        /// <summary>
        ///     The function gets the value from FieldInfo or PropertyInfo.
        /// </summary>
        /// <param name="info">a FieldInfo or PropertyInfo structure</param>
        /// <param name="value">the value to get</param>
        /// <returns>the value or values as a string</returns>
        private static IEnumerable<string> wasGetInfo(object info, object value)
        {
            if (info == null) yield break;
            object data = wasGetInfoValue(info, value);
            if (data == null) yield break;
            // Handle arrays and lists
            if (data is Array || data is IList)
            {
                IList iList = (IList) data;
                if (iList.Count.Equals(0)) yield break;
                foreach (object item in iList.Cast<object>().Where(o => o != null))
                {
                    foreach (KeyValuePair<FieldInfo, object> fi in wasGetFields(item, item.GetType().Name))
                    {
                        if (fi.Key != null)
                        {
                            foreach (string fieldString in wasGetInfo(fi.Key, fi.Value))
                            {
                                yield return fi.Key.Name;
                                yield return fieldString;
                            }
                        }
                    }
                    foreach (KeyValuePair<PropertyInfo, object> pi in wasGetProperties(item, item.GetType().Name))
                    {
                        if (pi.Key != null)
                        {
                            foreach (string propertyString in wasGetInfo(pi.Key, pi.Value))
                            {
                                yield return pi.Key.Name;
                                yield return propertyString;
                            }
                        }
                    }
                    // Don't bother with primitive types.
                    if (item.GetType().IsPrimitive)
                    {
                        yield return item.ToString();
                    }
                }
                yield break;
            }
            // Handle Dictionary
            if (data is IDictionary)
            {
                Hashtable dictionary = new Hashtable(data as IDictionary);
                if (dictionary.Count.Equals(0)) yield break;
                foreach (DictionaryEntry entry in dictionary)
                {
                    // First the keys.
                    foreach (KeyValuePair<FieldInfo, object> fi in wasGetFields(entry.Key, entry.Key.GetType().Name))
                    {
                        if (fi.Key != null)
                        {
                            foreach (string fieldString in wasGetInfo(fi.Key, fi.Value))
                            {
                                yield return fi.Key.Name;
                                yield return fieldString;
                            }
                        }
                    }
                    foreach (
                        KeyValuePair<PropertyInfo, object> pi in wasGetProperties(entry.Key, entry.Key.GetType().Name))
                    {
                        if (pi.Key != null)
                        {
                            foreach (string propertyString in wasGetInfo(pi.Key, pi.Value))
                            {
                                yield return pi.Key.Name;
                                yield return propertyString;
                            }
                        }
                    }
                    // Then the values.
                    foreach (KeyValuePair<FieldInfo, object> fi in wasGetFields(entry.Value, entry.Value.GetType().Name)
                        )
                    {
                        if (fi.Key != null)
                        {
                            foreach (string fieldString in wasGetInfo(fi.Key, fi.Value))
                            {
                                yield return fi.Key.Name;
                                yield return fieldString;
                            }
                        }
                    }
                    foreach (
                        KeyValuePair<PropertyInfo, object> pi in
                            wasGetProperties(entry.Value, entry.Value.GetType().Name))
                    {
                        if (pi.Key != null)
                        {
                            foreach (string propertyString in wasGetInfo(pi.Key, pi.Value))
                            {
                                yield return pi.Key.Name;
                                yield return propertyString;
                            }
                        }
                    }
                }
                yield break;
            }
            // Handle InternalDictionary
            FieldInfo internalDictionaryInfo = data.GetType()
                .GetField("Dictionary",
                    BindingFlags.Default | BindingFlags.CreateInstance | BindingFlags.Instance | BindingFlags.NonPublic);
            if (internalDictionaryInfo != null)
            {
                Hashtable internalDictionary = new Hashtable(internalDictionaryInfo.GetValue(data) as IDictionary);
                if (internalDictionary.Count.Equals(0)) yield break;
                foreach (DictionaryEntry entry in internalDictionary)
                {
                    // First the keys.
                    foreach (KeyValuePair<FieldInfo, object> fi in wasGetFields(entry.Key, entry.Key.GetType().Name))
                    {
                        if (fi.Key != null)
                        {
                            foreach (string fieldString in wasGetInfo(fi.Key, fi.Value))
                            {
                                yield return fi.Key.Name;
                                yield return fieldString;
                            }
                        }
                    }
                    foreach (
                        KeyValuePair<PropertyInfo, object> pi in wasGetProperties(entry.Key, entry.Key.GetType().Name))
                    {
                        if (pi.Key != null)
                        {
                            foreach (string propertyString in wasGetInfo(pi.Key, pi.Value))
                            {
                                yield return pi.Key.Name;
                                yield return propertyString;
                            }
                        }
                    }
                    // Then the values.
                    foreach (KeyValuePair<FieldInfo, object> fi in wasGetFields(entry.Value, entry.Value.GetType().Name)
                        )
                    {
                        if (fi.Key != null)
                        {
                            foreach (string fieldString in wasGetInfo(fi.Key, fi.Value))
                            {
                                yield return fi.Key.Name;
                                yield return fieldString;
                            }
                        }
                    }
                    foreach (
                        KeyValuePair<PropertyInfo, object> pi in
                            wasGetProperties(entry.Value, entry.Value.GetType().Name))
                    {
                        if (pi.Key != null)
                        {
                            foreach (string propertyString in wasGetInfo(pi.Key, pi.Value))
                            {
                                yield return pi.Key.Name;
                                yield return propertyString;
                            }
                        }
                    }
                }
                yield break;
            }

            string @string = data.ToString();
            if (string.IsNullOrEmpty(@string)) yield break;
            yield return @string;
        }

        /// <summary>
        ///     Sets the value of FieldInfo or PropertyInfo.
        /// </summary>
        /// <typeparam name="T">the type to set</typeparam>
        /// <param name="info">a FieldInfo or PropertyInfo object</param>
        /// <param name="value">the object's value</param>
        /// <param name="setting">the value to set to</param>
        /// <param name="object">the object to set the values for</param>
        private static void wasSetInfo<T>(object info, object value, string setting, ref T @object)
        {
            if (info == null) return;
            if (wasGetInfoValue(info, value) is string)
            {
                wasSetInfoValue(info, ref @object, setting);
            }
            if (wasGetInfoValue(info, value) is UUID)
            {
                UUID UUIDData;
                if (!UUID.TryParse(setting, out UUIDData))
                {
                    InventoryItem item = FindInventory<InventoryBase>(Client.Inventory.Store.RootNode,
                        setting).FirstOrDefault() as InventoryItem;
                    if (item == null)
                    {
                        throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.INVENTORY_ITEM_NOT_FOUND));
                    }
                    UUIDData = item.UUID;
                }
                if (UUIDData.Equals(UUID.Zero))
                {
                    throw new Exception(
                        wasGetDescriptionFromEnumValue(ScriptError.INVENTORY_ITEM_NOT_FOUND));
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
            if (wasGetInfoValue(info, value) is ParcelFlags)
            {
                uint parcelFlags;
                switch (!uint.TryParse(setting, out parcelFlags))
                {
                    case true:
                        Parallel.ForEach(wasCSVToEnumerable(setting), o =>
                        {
                            Parallel.ForEach(typeof (ParcelFlags).GetFields(BindingFlags.Public | BindingFlags.Static)
                                .AsParallel().Where(p => p.Name.Equals(o, StringComparison.Ordinal)),
                                p => { parcelFlags |= ((uint) p.GetValue(null)); });
                        });
                        break;
                }
                wasSetInfoValue(info, ref @object, parcelFlags);
            }
        }

        /// <summary>
        ///     Converts Linden item permissions to a formatted string:
        ///     CDEMVT - Copy, Damage, Export, Modify, Move, Transfer
        ///     BBBBBBEEEEEEGGGGGGNNNNNNOOOOOO - Base, Everyone, Group, Next, Owner
        /// </summary>
        /// <param name="permissions">the item permissions</param>
        /// <returns>the literal permissions for an item</returns>
        private static string wasPermissionsToString(OpenMetaverse.Permissions permissions)
        {
            Func<PermissionMask, string> segment = o =>
            {
                StringBuilder seg = new StringBuilder();

                switch (!((uint) o & (uint) PermissionMask.Copy).Equals(0))
                {
                    case true:
                        seg.Append("c");
                        break;
                    default:
                        seg.Append("-");
                        break;
                }

                switch (!((uint) o & (uint) PermissionMask.Damage).Equals(0))
                {
                    case true:
                        seg.Append("d");
                        break;
                    default:
                        seg.Append("-");
                        break;
                }

                switch (!((uint) o & (uint) PermissionMask.Export).Equals(0))
                {
                    case true:
                        seg.Append("e");
                        break;
                    default:
                        seg.Append("-");
                        break;
                }

                switch (!((uint) o & (uint) PermissionMask.Modify).Equals(0))
                {
                    case true:
                        seg.Append("m");
                        break;
                    default:
                        seg.Append("-");
                        break;
                }

                switch (!((uint) o & (uint) PermissionMask.Move).Equals(0))
                {
                    case true:
                        seg.Append("v");
                        break;
                    default:
                        seg.Append("-");
                        break;
                }

                switch (!((uint) o & (uint) PermissionMask.Transfer).Equals(0))
                {
                    case true:
                        seg.Append("t");
                        break;
                    default:
                        seg.Append("-");
                        break;
                }

                return seg.ToString();
            };

            StringBuilder x = new StringBuilder();
            x.Append(segment(permissions.BaseMask));
            x.Append(segment(permissions.EveryoneMask));
            x.Append(segment(permissions.GroupMask));
            x.Append(segment(permissions.NextOwnerMask));
            x.Append(segment(permissions.OwnerMask));
            return x.ToString();
        }

        /// <summary>
        ///     Converts a formatted string to item permissions:
        ///     CDEMVT - Copy, Damage, Export, Modify, Move, Transfer
        ///     BBBBBBEEEEEEGGGGGGNNNNNNOOOOOO - Base, Everyone, Group, Next, Owner
        /// </summary>
        /// <param name="permissions">the item permissions</param>
        /// <returns>the permissions for an item</returns>
        private static OpenMetaverse.Permissions wasStringToPermissions(string permissions)
        {
            Func<string, uint> segment = o =>
            {
                uint r = 0;
                switch (!char.ToLower(o[0]).Equals('c'))
                {
                    case false:
                        r |= (uint) PermissionMask.Copy;
                        break;
                }

                switch (!char.ToLower(o[1]).Equals('d'))
                {
                    case false:
                        r |= (uint) PermissionMask.Damage;
                        break;
                }

                switch (!char.ToLower(o[2]).Equals('e'))
                {
                    case false:
                        r |= (uint) PermissionMask.Export;
                        break;
                }

                switch (!char.ToLower(o[3]).Equals('m'))
                {
                    case false:
                        r |= (uint) PermissionMask.Modify;
                        break;
                }

                switch (!char.ToLower(o[4]).Equals('v'))
                {
                    case false:
                        r |= (uint) PermissionMask.Move;
                        break;
                }

                switch (!char.ToLower(o[5]).Equals('t'))
                {
                    case false:
                        r |= (uint) PermissionMask.Transfer;
                        break;
                }

                return r;
            };

            return new OpenMetaverse.Permissions(segment(permissions.Substring(0, 6)),
                segment(permissions.Substring(6, 6)), segment(permissions.Substring(12, 6)),
                segment(permissions.Substring(18, 6)), segment(permissions.Substring(24, 6)));
        }

        /// <summary>
        ///     Get the parcel of a simulator given a position.
        /// </summary>
        /// <param name="simulator">the simulator containing the parcel</param>
        /// <param name="position">a position within the parcel</param>
        /// <param name="parcel">a parcel object where to store the found parcel</param>
        /// <returns>true if the parcel could be found</returns>
        private static bool GetParcelAtPosition(Simulator simulator, Vector3 position,
            ref Parcel parcel)
        {
            HashSet<Parcel> localParcels = new HashSet<Parcel>();
            ManualResetEvent RequestAllSimParcelsEvent = new ManualResetEvent(false);
            EventHandler<SimParcelsDownloadedEventArgs> SimParcelsDownloadedDelegate =
                (sender, args) => RequestAllSimParcelsEvent.Set();
            lock (ClientInstanceParcelsLock)
            {
                Client.Parcels.SimParcelsDownloaded += SimParcelsDownloadedDelegate;
                switch (!simulator.IsParcelMapFull())
                {
                    case true:
                        Client.Parcels.RequestAllSimParcels(simulator);
                        break;
                    default:
                        RequestAllSimParcelsEvent.Set();
                        break;
                }
                if (!RequestAllSimParcelsEvent.WaitOne(Configuration.SERVICES_TIMEOUT, false))
                {
                    Client.Parcels.SimParcelsDownloaded -= SimParcelsDownloadedDelegate;
                    return false;
                }
                Client.Parcels.SimParcelsDownloaded -= SimParcelsDownloadedDelegate;
                simulator.Parcels.ForEach(currentParcel =>
                {
                    if (!(position.X >= currentParcel.AABBMin.X) || !(position.X <= currentParcel.AABBMax.X) ||
                        !(position.Y >= currentParcel.AABBMin.Y) || !(position.Y <= currentParcel.AABBMax.Y))
                        return;
                    localParcels.Add(currentParcel);
                });
            }
            Parcel localParcel = localParcels.OrderBy(o => Vector3.Distance(o.AABBMin, o.AABBMax)).FirstOrDefault();
            if (localParcel == null)
                return false;
            parcel = localParcel;
            return true;
        }

        /// <summary>
        ///     Determines whether a vector falls within a parcel.
        /// </summary>
        /// <param name="position">a 3D vector</param>
        /// <param name="parcel">a parcel</param>
        /// <returns>true if the vector falls within the parcel bounds</returns>
        private static bool IsVectorInParcel(Vector3 position, Parcel parcel)
        {
            return position.X >= parcel.AABBMin.X && position.X <= parcel.AABBMax.X &&
                   position.Y >= parcel.AABBMin.Y && position.Y <= parcel.AABBMax.Y;
        }

        /// <summary>
        ///     Find a named primitive in range (whether attachment or in-world).
        /// </summary>
        /// <param name="item">the name or UUID of the primitive</param>
        /// <param name="range">the range in meters to search for the object</param>
        /// <param name="primitive">a primitive object to store the result</param>
        /// <param name="millisecondsTimeout">the services timeout in milliseconds</param>
        /// <param name="dataTimeout">the data timeout in milliseconds</param>
        /// <returns>true if the primitive could be found</returns>
        private static bool FindPrimitive<T>(T item, float range, ref Primitive primitive, int millisecondsTimeout,
            int dataTimeout)
        {
            HashSet<Primitive> selectedPrimitives = new HashSet<Primitive>();
            HashSet<Primitive> objectsPrimitives =
                new HashSet<Primitive>(GetPrimitives(range, millisecondsTimeout, dataTimeout));
            HashSet<Avatar> objectsAvatars = new HashSet<Avatar>(GetAvatars(range, millisecondsTimeout, dataTimeout));
            Parallel.ForEach(objectsPrimitives, o =>
            {
                switch (o.ParentID)
                {
                    // primitive is a parent and it is in range
                    case 0:
                        if (Vector3.Distance(o.Position, Client.Self.SimPosition) <= range)
                        {
                            if (item is UUID && o.ID.Equals(item))
                            {
                                selectedPrimitives.Add(o);
                                break;
                            }
                            if (item is string)
                            {
                                selectedPrimitives.Add(o);
                            }
                        }
                        break;
                    // primitive is a child
                    default:
                        // find the parent of the primitive
                        Primitive primitiveParent = objectsPrimitives.FirstOrDefault(p => p.LocalID.Equals(o.ParentID));
                        // if the primitive has a parent
                        if (primitiveParent != null)
                        {
                            // if the parent is in range, add the child
                            if (Vector3.Distance(primitiveParent.Position, Client.Self.SimPosition) <= range)
                            {
                                if (item is UUID && o.ID.Equals(item))
                                {
                                    selectedPrimitives.Add(o);
                                    break;
                                }
                                if (item is string)
                                {
                                    selectedPrimitives.Add(o);
                                }
                                break;
                            }
                        }
                        // check if an avatar is the parent of the parent primitive
                        Avatar avatarParent =
                            objectsAvatars.FirstOrDefault(p => p.LocalID.Equals(o.ParentID));
                        // parent avatar not found, this should not happen
                        if (avatarParent != null)
                        {
                            // check if the avatar is in range
                            if (Vector3.Distance(avatarParent.Position, Client.Self.SimPosition) <= range)
                            {
                                if (item is UUID && o.ID.Equals(item))
                                {
                                    selectedPrimitives.Add(o);
                                    break;
                                }
                                if (item is string)
                                {
                                    selectedPrimitives.Add(o);
                                }
                            }
                        }
                        break;
                }
            });
            if (selectedPrimitives.Count.Equals(0)) return false;
            if (!UpdatePrimitives(ref selectedPrimitives, dataTimeout))
                return false;
            primitive =
                selectedPrimitives.FirstOrDefault(
                    o =>
                        (item is UUID && o.ID.Equals(item)) ||
                        (item is string && (item as string).Equals(o.Properties.Name, StringComparison.Ordinal)));
            return primitive != null;
        }

        /// <summary>
        ///     Creates a faceted mesh from a primitive.
        /// </summary>
        /// <param name="primitive">the primitive to convert</param>
        /// <param name="mesher">the mesher to use</param>
        /// <param name="facetedMesh">a reference to an output facted mesh object</param>
        /// <param name="millisecondsTimeout">the services timeout</param>
        /// <returns>true if the mesh could be created successfully</returns>
        private static bool MakeFacetedMesh(Primitive primitive, MeshmerizerR mesher, ref FacetedMesh facetedMesh,
            int millisecondsTimeout)
        {
            if (primitive.Sculpt == null || primitive.Sculpt.SculptTexture.Equals(UUID.Zero))
            {
                facetedMesh = mesher.GenerateFacetedMesh(primitive, DetailLevel.Highest);
                return true;
            }
            if (!primitive.Sculpt.Type.Equals(SculptType.Mesh))
            {
                byte[] assetData = null;
                switch (!Client.Assets.Cache.HasAsset(primitive.Sculpt.SculptTexture))
                {
                    case true:
                        lock (ClientInstanceAssetsLock)
                        {
                            ManualResetEvent ImageDownloadedEvent = new ManualResetEvent(false);
                            Client.Assets.RequestImage(primitive.Sculpt.SculptTexture, (state, args) =>
                            {
                                if (!state.Equals(TextureRequestState.Finished)) return;
                                assetData = args.AssetData;
                                ImageDownloadedEvent.Set();
                            });
                            if (!ImageDownloadedEvent.WaitOne(millisecondsTimeout, false))
                                return false;
                        }
                        Client.Assets.Cache.SaveAssetToCache(primitive.Sculpt.SculptTexture, assetData);
                        break;
                    default:
                        assetData = Client.Assets.Cache.GetCachedAssetBytes(primitive.Sculpt.SculptTexture);
                        break;
                }

                using (SKBitmap image = J2kImage.FromBytes(assetData).As<SKBitmap>())
                {
                    facetedMesh = mesher.GenerateFacetedSculptMesh(primitive, image, DetailLevel.Highest);
                }
                return true;
            }
            FacetedMesh localFacetedMesh = null;
            ManualResetEvent MeshDownloadedEvent = new ManualResetEvent(false);
            lock (ClientInstanceAssetsLock)
            {
                Client.Assets.RequestMesh(primitive.Sculpt.SculptTexture, (success, meshAsset) =>
                {
                    FacetedMesh.TryDecodeFromAsset(primitive, meshAsset, DetailLevel.Highest, out localFacetedMesh);
                    MeshDownloadedEvent.Set();
                });

                if (!MeshDownloadedEvent.WaitOne(millisecondsTimeout, false))
                    return false;
            }

            if (localFacetedMesh == null)
                return false;

            facetedMesh = localFacetedMesh;
            return true;
        }

        /// <summary>
        ///     Generates a Collada DAE XML Document.
        /// </summary>
        /// <param name="facetedMeshSet">the faceted meshes</param>
        /// <param name="textures">a dictionary of UUID to texture names</param>
        /// <param name="imageFormat">the image export format</param>
        /// <returns>the DAE document</returns>
        /// <remarks>
        ///     This function is a branch-in of several functions of the Radegast Viewer with some changes by Wizardry and
        ///     Steamworks.
        /// </remarks>
        private static XmlDocument GenerateCollada(IEnumerable<FacetedMesh> facetedMeshSet,
            Dictionary<UUID, string> textures, string imageFormat)
        {
            List<MaterialInfo> AllMeterials = new List<MaterialInfo>();

            XmlDocument Doc = new XmlDocument();
            var root = Doc.AppendChild(Doc.CreateElement("COLLADA"));
            root.Attributes.Append(Doc.CreateAttribute("xmlns")).Value = "http://www.collada.org/2005/11/COLLADASchema";
            root.Attributes.Append(Doc.CreateAttribute("version")).Value = "1.4.1";

            var asset = root.AppendChild(Doc.CreateElement("asset"));
            var contributor = asset.AppendChild(Doc.CreateElement("contributor"));
            contributor.AppendChild(Doc.CreateElement("author")).InnerText = "Radegast User";
            contributor.AppendChild(Doc.CreateElement("authoring_tool")).InnerText = "Radegast Collada Export";

            asset.AppendChild(Doc.CreateElement("created")).InnerText = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss");
            asset.AppendChild(Doc.CreateElement("modified")).InnerText = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss");

            var unit = asset.AppendChild(Doc.CreateElement("unit"));
            unit.Attributes.Append(Doc.CreateAttribute("name")).Value = "meter";
            unit.Attributes.Append(Doc.CreateAttribute("meter")).Value = "1";

            asset.AppendChild(Doc.CreateElement("up_axis")).InnerText = "Z_UP";

            var images = root.AppendChild(Doc.CreateElement("library_images"));
            var geomLib = root.AppendChild(Doc.CreateElement("library_geometries"));
            var effects = root.AppendChild(Doc.CreateElement("library_effects"));
            var materials = root.AppendChild(Doc.CreateElement("library_materials"));
            var scene = root.AppendChild(Doc.CreateElement("library_visual_scenes"))
                .AppendChild(Doc.CreateElement("visual_scene"));
            scene.Attributes.Append(Doc.CreateAttribute("id")).InnerText = "Scene";
            scene.Attributes.Append(Doc.CreateAttribute("name")).InnerText = "Scene";

            foreach (string name in textures.Values)
            {
                string colladaName = name + "_" + imageFormat.ToLower();
                var image = images.AppendChild(Doc.CreateElement("image"));
                image.Attributes.Append(Doc.CreateAttribute("id")).InnerText = colladaName;
                image.Attributes.Append(Doc.CreateAttribute("name")).InnerText = colladaName;
                image.AppendChild(Doc.CreateElement("init_from")).InnerText =
                    wasURIUnescapeDataString(name + "." + imageFormat.ToLower());
            }

            Func<XmlNode, string, string, List<float>, bool> addSource = (mesh, src_id, param, vals) =>
            {
                var source = mesh.AppendChild(Doc.CreateElement("source"));
                source.Attributes.Append(Doc.CreateAttribute("id")).InnerText = src_id;
                var src_array = source.AppendChild(Doc.CreateElement("float_array"));

                src_array.Attributes.Append(Doc.CreateAttribute("id")).InnerText = string.Format("{0}-{1}", src_id,
                    "array");
                src_array.Attributes.Append(Doc.CreateAttribute("count")).InnerText = vals.Count.ToString();

                StringBuilder sb = new StringBuilder();
                for (int i = 0; i < vals.Count; i++)
                {
                    sb.Append(vals[i].ToString(CultureInfo.InvariantCulture));
                    if (i != vals.Count - 1)
                    {
                        sb.Append(" ");
                    }
                }
                src_array.InnerText = sb.ToString();

                var acc = source.AppendChild(Doc.CreateElement("technique_common"))
                    .AppendChild(Doc.CreateElement("accessor"));
                acc.Attributes.Append(Doc.CreateAttribute("source")).InnerText = string.Format("#{0}-{1}", src_id,
                    "array");
                acc.Attributes.Append(Doc.CreateAttribute("count")).InnerText = (vals.Count/param.Length).ToString();
                acc.Attributes.Append(Doc.CreateAttribute("stride")).InnerText = param.Length.ToString();

                foreach (char c in param)
                {
                    var pX = acc.AppendChild(Doc.CreateElement("param"));
                    pX.Attributes.Append(Doc.CreateAttribute("name")).InnerText = c.ToString();
                    pX.Attributes.Append(Doc.CreateAttribute("type")).InnerText = "float";
                }

                return true;
            };

            Func<Primitive.TextureEntryFace, MaterialInfo> getMaterial = o =>
            {
                MaterialInfo ret = AllMeterials.FirstOrDefault(mat => mat.Matches(o));

                if (ret != null) return ret;
                ret = new MaterialInfo
                {
                    TextureID = o.TextureID,
                    Color = o.RGBA,
                    Name = string.Format("Material{0}", AllMeterials.Count)
                };
                AllMeterials.Add(ret);

                return ret;
            };

            Func<FacetedMesh, List<MaterialInfo>> getMaterials = o =>
            {
                var ret = new List<MaterialInfo>();

                for (int face_num = 0; face_num < o.Faces.Count; face_num++)
                {
                    var te = o.Faces[face_num].TextureFace;
                    if (te.RGBA.A < 0.01f)
                    {
                        continue;
                    }
                    var mat = getMaterial.Invoke(te);
                    if (!ret.Contains(mat))
                    {
                        ret.Add(mat);
                    }
                }
                return ret;
            };

            Func<XmlNode, string, string, FacetedMesh, List<int>, bool> addPolygons =
                (mesh, geomID, materialID, obj, faces_to_include) =>
                {
                    var polylist = mesh.AppendChild(Doc.CreateElement("polylist"));
                    polylist.Attributes.Append(Doc.CreateAttribute("material")).InnerText = materialID;

                    // Vertices semantic
                    {
                        var input = polylist.AppendChild(Doc.CreateElement("input"));
                        input.Attributes.Append(Doc.CreateAttribute("semantic")).InnerText = "VERTEX";
                        input.Attributes.Append(Doc.CreateAttribute("offset")).InnerText = "0";
                        input.Attributes.Append(Doc.CreateAttribute("source")).InnerText = string.Format("#{0}-{1}",
                            geomID, "vertices");
                    }

                    // Normals semantic
                    {
                        var input = polylist.AppendChild(Doc.CreateElement("input"));
                        input.Attributes.Append(Doc.CreateAttribute("semantic")).InnerText = "NORMAL";
                        input.Attributes.Append(Doc.CreateAttribute("offset")).InnerText = "0";
                        input.Attributes.Append(Doc.CreateAttribute("source")).InnerText = string.Format("#{0}-{1}",
                            geomID, "normals");
                    }

                    // UV semantic
                    {
                        var input = polylist.AppendChild(Doc.CreateElement("input"));
                        input.Attributes.Append(Doc.CreateAttribute("semantic")).InnerText = "TEXCOORD";
                        input.Attributes.Append(Doc.CreateAttribute("offset")).InnerText = "0";
                        input.Attributes.Append(Doc.CreateAttribute("source")).InnerText = string.Format("#{0}-{1}",
                            geomID, "map0");
                    }

                    // Save indices
                    var vcount = polylist.AppendChild(Doc.CreateElement("vcount"));
                    var p = polylist.AppendChild(Doc.CreateElement("p"));
                    int index_offset = 0;
                    int num_tris = 0;
                    StringBuilder pBuilder = new StringBuilder();
                    StringBuilder vcountBuilder = new StringBuilder();

                    for (int face_num = 0; face_num < obj.Faces.Count; face_num++)
                    {
                        var face = obj.Faces[face_num];
                        if (faces_to_include == null || faces_to_include.Contains(face_num))
                        {
                            for (int i = 0; i < face.Indices.Count; i++)
                            {
                                int index = index_offset + face.Indices[i];
                                pBuilder.Append(index);
                                pBuilder.Append(" ");
                                if (i%3 == 0)
                                {
                                    vcountBuilder.Append("3 ");
                                    num_tris++;
                                }
                            }
                        }
                        index_offset += face.Vertices.Count;
                    }

                    p.InnerText = pBuilder.ToString().TrimEnd();
                    vcount.InnerText = vcountBuilder.ToString().TrimEnd();
                    polylist.Attributes.Append(Doc.CreateAttribute("count")).InnerText = num_tris.ToString();

                    return true;
                };

            Func<FacetedMesh, MaterialInfo, List<int>> getFacesWithMaterial = (obj, mat) =>
            {
                var ret = new List<int>();
                for (int face_num = 0; face_num < obj.Faces.Count; face_num++)
                {
                    if (mat == getMaterial.Invoke(obj.Faces[face_num].TextureFace))
                    {
                        ret.Add(face_num);
                    }
                }
                return ret;
            };

            Func<Vector3, Quaternion, Vector3, float[]> createSRTMatrix = (scale, q, pos) =>
            {
                float[] mat = new float[16];

                // Transpose the quaternion (don't ask me why)
                q.X = q.X*-1f;
                q.Y = q.Y*-1f;
                q.Z = q.Z*-1f;

                float x2 = q.X + q.X;
                float y2 = q.Y + q.Y;
                float z2 = q.Z + q.Z;
                float xx = q.X*x2;
                float xy = q.X*y2;
                float xz = q.X*z2;
                float yy = q.Y*y2;
                float yz = q.Y*z2;
                float zz = q.Z*z2;
                float wx = q.W*x2;
                float wy = q.W*y2;
                float wz = q.W*z2;

                mat[0] = (1.0f - (yy + zz))*scale.X;
                mat[1] = (xy - wz)*scale.X;
                mat[2] = (xz + wy)*scale.X;
                mat[3] = 0.0f;

                mat[4] = (xy + wz)*scale.Y;
                mat[5] = (1.0f - (xx + zz))*scale.Y;
                mat[6] = (yz - wx)*scale.Y;
                mat[7] = 0.0f;

                mat[8] = (xz - wy)*scale.Z;
                mat[9] = (yz + wx)*scale.Z;
                mat[10] = (1.0f - (xx + yy))*scale.Z;
                mat[11] = 0.0f;

                //Positional parts
                mat[12] = pos.X;
                mat[13] = pos.Y;
                mat[14] = pos.Z;
                mat[15] = 1.0f;

                return mat;
            };

            Func<XmlNode, bool> generateEffects = o =>
            {
                // Effects (face color, alpha)
                foreach (var mat in AllMeterials)
                {
                    var color = mat.Color;
                    var effect = effects.AppendChild(Doc.CreateElement("effect"));
                    effect.Attributes.Append(Doc.CreateAttribute("id")).InnerText = mat.Name + "-fx";
                    var profile = effect.AppendChild(Doc.CreateElement("profile_COMMON"));
                    string colladaName = null;

                    KeyValuePair<UUID, string> kvp = textures.FirstOrDefault(p => p.Key.Equals(mat.TextureID));

                    if (!kvp.Equals(default(KeyValuePair<UUID, string>)))
                    {
                        UUID textID = kvp.Key;
                        colladaName = textures[textID] + "_" + imageFormat.ToLower();
                        var newparam = profile.AppendChild(Doc.CreateElement("newparam"));
                        newparam.Attributes.Append(Doc.CreateAttribute("sid")).InnerText = colladaName + "-surface";
                        var surface = newparam.AppendChild(Doc.CreateElement("surface"));
                        surface.Attributes.Append(Doc.CreateAttribute("type")).InnerText = "2D";
                        surface.AppendChild(Doc.CreateElement("init_from")).InnerText = colladaName;
                        newparam = profile.AppendChild(Doc.CreateElement("newparam"));
                        newparam.Attributes.Append(Doc.CreateAttribute("sid")).InnerText = colladaName + "-sampler";
                        newparam.AppendChild(Doc.CreateElement("sampler2D"))
                            .AppendChild(Doc.CreateElement("source"))
                            .InnerText = colladaName + "-surface";
                    }

                    var t = profile.AppendChild(Doc.CreateElement("technique"));
                    t.Attributes.Append(Doc.CreateAttribute("sid")).InnerText = "common";
                    var phong = t.AppendChild(Doc.CreateElement("phong"));

                    var diffuse = phong.AppendChild(Doc.CreateElement("diffuse"));
                    // Only one <color> or <texture> can appear inside diffuse element
                    if (colladaName != null)
                    {
                        var txtr = diffuse.AppendChild(Doc.CreateElement("texture"));
                        txtr.Attributes.Append(Doc.CreateAttribute("texture")).InnerText = colladaName + "-sampler";
                        txtr.Attributes.Append(Doc.CreateAttribute("texcoord")).InnerText = colladaName;
                    }
                    else
                    {
                        var diffuseColor = diffuse.AppendChild(Doc.CreateElement("color"));
                        diffuseColor.Attributes.Append(Doc.CreateAttribute("sid")).InnerText = "diffuse";
                        diffuseColor.InnerText = string.Format("{0} {1} {2} {3}",
                            color.R.ToString(CultureInfo.InvariantCulture),
                            color.G.ToString(CultureInfo.InvariantCulture),
                            color.B.ToString(CultureInfo.InvariantCulture),
                            color.A.ToString(CultureInfo.InvariantCulture));
                    }

                    phong.AppendChild(Doc.CreateElement("transparency"))
                        .AppendChild(Doc.CreateElement("float"))
                        .InnerText = color.A.ToString(CultureInfo.InvariantCulture);
                }

                return true;
            };

            int prim_nr = 0;
            foreach (var obj in facetedMeshSet)
            {
                int total_num_vertices = 0;
                string name = string.Format("prim{0}", prim_nr++);
                string geomID = name;

                var geom = geomLib.AppendChild(Doc.CreateElement("geometry"));
                geom.Attributes.Append(Doc.CreateAttribute("id")).InnerText = string.Format("{0}-{1}", geomID, "mesh");
                var mesh = geom.AppendChild(Doc.CreateElement("mesh"));

                List<float> position_data = new List<float>();
                List<float> normal_data = new List<float>();
                List<float> uv_data = new List<float>();

                int num_faces = obj.Faces.Count;

                for (int face_num = 0; face_num < num_faces; face_num++)
                {
                    var face = obj.Faces[face_num];
                    total_num_vertices += face.Vertices.Count;

                    foreach (var v in face.Vertices)
                    {
                        position_data.Add(v.Position.X);
                        position_data.Add(v.Position.Y);
                        position_data.Add(v.Position.Z);

                        normal_data.Add(v.Normal.X);
                        normal_data.Add(v.Normal.Y);
                        normal_data.Add(v.Normal.Z);

                        uv_data.Add(v.TexCoord.X);
                        uv_data.Add(v.TexCoord.Y);
                    }
                }

                addSource.Invoke(mesh, string.Format("{0}-{1}", geomID, "positions"), "XYZ", position_data);
                addSource.Invoke(mesh, string.Format("{0}-{1}", geomID, "normals"), "XYZ", normal_data);
                addSource.Invoke(mesh, string.Format("{0}-{1}", geomID, "map0"), "ST", uv_data);

                // Add the <vertices> element
                {
                    var verticesNode = mesh.AppendChild(Doc.CreateElement("vertices"));
                    verticesNode.Attributes.Append(Doc.CreateAttribute("id")).InnerText = string.Format("{0}-{1}",
                        geomID, "vertices");
                    var verticesInput = verticesNode.AppendChild(Doc.CreateElement("input"));
                    verticesInput.Attributes.Append(Doc.CreateAttribute("semantic")).InnerText = "POSITION";
                    verticesInput.Attributes.Append(Doc.CreateAttribute("source")).InnerText = string.Format(
                        "#{0}-{1}", geomID, "positions");
                }

                var objMaterials = getMaterials.Invoke(obj);

                // Add triangles
                foreach (var objMaterial in objMaterials)
                {
                    addPolygons.Invoke(mesh, geomID, objMaterial.Name + "-material", obj,
                        getFacesWithMaterial.Invoke(obj, objMaterial));
                }

                var node = scene.AppendChild(Doc.CreateElement("node"));
                node.Attributes.Append(Doc.CreateAttribute("type")).InnerText = "NODE";
                node.Attributes.Append(Doc.CreateAttribute("id")).InnerText = geomID;
                node.Attributes.Append(Doc.CreateAttribute("name")).InnerText = geomID;

                // Set tranform matrix (node position, rotation and scale)
                var matrix = node.AppendChild(Doc.CreateElement("matrix"));

                var srt = createSRTMatrix.Invoke(obj.Prim.Scale, obj.Prim.Rotation, obj.Prim.Position);
                string matrixVal = "";
                for (int i = 0; i < 4; i++)
                {
                    for (int j = 0; j < 4; j++)
                    {
                        matrixVal += srt[j*4 + i].ToString(CultureInfo.InvariantCulture) + " ";
                    }
                }
                matrix.InnerText = matrixVal.TrimEnd();

                // Geometry of the node
                var nodeGeometry = node.AppendChild(Doc.CreateElement("instance_geometry"));

                // Bind materials
                var tq = nodeGeometry.AppendChild(Doc.CreateElement("bind_material"))
                    .AppendChild(Doc.CreateElement("technique_common"));
                foreach (var objMaterial in objMaterials)
                {
                    var instanceMaterial = tq.AppendChild(Doc.CreateElement("instance_material"));
                    instanceMaterial.Attributes.Append(Doc.CreateAttribute("symbol")).InnerText =
                        string.Format("{0}-{1}", objMaterial.Name, "material");
                    instanceMaterial.Attributes.Append(Doc.CreateAttribute("target")).InnerText =
                        string.Format("#{0}-{1}", objMaterial.Name, "material");
                }

                nodeGeometry.Attributes.Append(Doc.CreateAttribute("url")).InnerText = string.Format("#{0}-{1}", geomID,
                    "mesh");
            }

            generateEffects.Invoke(effects);

            // Materials
            foreach (var objMaterial in AllMeterials)
            {
                var mat = materials.AppendChild(Doc.CreateElement("material"));
                mat.Attributes.Append(Doc.CreateAttribute("id")).InnerText = objMaterial.Name + "-material";
                var matEffect = mat.AppendChild(Doc.CreateElement("instance_effect"));
                matEffect.Attributes.Append(Doc.CreateAttribute("url")).InnerText = string.Format("#{0}-{1}",
                    objMaterial.Name, "fx");
            }

            root.AppendChild(Doc.CreateElement("scene"))
                .AppendChild(Doc.CreateElement("instance_visual_scene"))
                .Attributes.Append(Doc.CreateAttribute("url")).InnerText = "#Scene";

            return Doc;
        }

        /// <summary>
        ///     Fetches all the avatars in-range.
        /// </summary>
        /// <param name="range">the range to extend or contract to</param>
        /// <param name="millisecondsTimeout">the timeout in milliseconds</param>
        /// <param name="dataTimeout">the data timeout in milliseconds</param>
        /// <returns>the avatars in range</returns>
        private static IEnumerable<Avatar> GetAvatars(float range, int millisecondsTimeout, int dataTimeout)
        {
            switch (Client.Self.Movement.Camera.Far < range)
            {
                case true:
                    IEnumerable<Avatar> avatars;
                    wasAdaptiveAlarm RangeUpdateAlarm = new wasAdaptiveAlarm(Configuration.DATA_DECAY_TYPE);
                    EventHandler<AvatarUpdateEventArgs> AvatarUpdateEventHandler =
                        (sender, args) => { RangeUpdateAlarm.Alarm(dataTimeout); };
                    lock (ClientInstanceObjectsLock)
                    {
                        Client.Objects.AvatarUpdate += AvatarUpdateEventHandler;
                        lock (ClientInstanceConfigurationLock)
                        {
                            Client.Self.Movement.Camera.Far = range;
                            RangeUpdateAlarm.Alarm(dataTimeout);
                            RangeUpdateAlarm.Signal.WaitOne(millisecondsTimeout, false);
                            avatars =
                                Client.Network.Simulators.Select(o => o.ObjectsAvatars)
                                    .Select(o => o.Copy().Values)
                                    .SelectMany(o => o);
                            Client.Self.Movement.Camera.Far = Configuration.RANGE;
                        }
                        Client.Objects.AvatarUpdate -= AvatarUpdateEventHandler;
                    }
                    return avatars;
                default:
                    return Client.Network.CurrentSim.ObjectsAvatars.Copy().Values;
            }
        }

        /// <summary>
        ///     Fetches all the primitives in-range.
        /// </summary>
        /// <param name="range">the range to extend or contract to</param>
        /// <param name="millisecondsTimeout">the timeout in milliseconds</param>
        /// <param name="dataTimeout">the data timeout in milliseconds</param>
        /// <returns>the primitives in range</returns>
        private static IEnumerable<Primitive> GetPrimitives(float range, int millisecondsTimeout, int dataTimeout)
        {
            switch (Client.Self.Movement.Camera.Far < range)
            {
                case true:
                    IEnumerable<Primitive> primitives;
                    wasAdaptiveAlarm RangeUpdateAlarm = new wasAdaptiveAlarm(Configuration.DATA_DECAY_TYPE);
                    EventHandler<PrimEventArgs> ObjectUpdateEventHandler =
                        (sender, args) => { RangeUpdateAlarm.Alarm(dataTimeout); };
                    lock (ClientInstanceObjectsLock)
                    {
                        Client.Objects.ObjectUpdate += ObjectUpdateEventHandler;
                        lock (ClientInstanceConfigurationLock)
                        {
                            Client.Self.Movement.Camera.Far = range;
                            RangeUpdateAlarm.Alarm(dataTimeout);
                            RangeUpdateAlarm.Signal.WaitOne(millisecondsTimeout, false);
                            primitives =
                                Client.Network.Simulators.Select(o => o.ObjectsPrimitives)
                                    .Select(o => o.Copy().Values)
                                    .SelectMany(o => o);
                            Client.Self.Movement.Camera.Far = Configuration.RANGE;
                        }
                        Client.Objects.ObjectUpdate -= ObjectUpdateEventHandler;
                    }
                    return primitives;
                default:
                    return Client.Network.CurrentSim.ObjectsPrimitives.Copy().Values;
            }
        }

        /// <summary>
        ///     Updates a set of primitives by scanning their properties.
        /// </summary>
        /// <param name="primitives">a list of primitives to update</param>
        /// <param name="dataTimeout">the timeout for receiving data from the grid</param>
        /// <returns>a list of updated primitives</returns>
        private static bool UpdatePrimitives(ref HashSet<Primitive> primitives, int dataTimeout)
        {
            HashSet<Primitive> scansPrimitives = new HashSet<Primitive>(primitives);
            HashSet<Primitive> localPrimitives = new HashSet<Primitive>();
            Dictionary<UUID, ManualResetEvent> primitiveEvents =
                new Dictionary<UUID, ManualResetEvent>(
                    scansPrimitives
                        .AsParallel().ToDictionary(o => o.ID, p => new ManualResetEvent(false)));
            Dictionary<UUID, Stopwatch> stopWatch = new Dictionary<UUID, Stopwatch>(
                scansPrimitives
                    .AsParallel().ToDictionary(o => o.ID, p => new Stopwatch()));
            HashSet<long> times = new HashSet<long>(new[] {(long) dataTimeout});
            EventHandler<ObjectPropertiesEventArgs> ObjectPropertiesEventHandler = (sender, args) =>
            {
                KeyValuePair<UUID, ManualResetEvent> queueElement =
                    primitiveEvents.AsParallel().FirstOrDefault(o => o.Key.Equals(args.Properties.ObjectID));
                if (queueElement.Equals(default(KeyValuePair<UUID, ManualResetEvent>))) return;
                Primitive updatedPrimitive =
                    scansPrimitives.AsParallel().FirstOrDefault(o => o.ID.Equals(args.Properties.ObjectID));
                if (updatedPrimitive == null) return;
                updatedPrimitive.Properties = args.Properties;
                localPrimitives.Add(updatedPrimitive);
                stopWatch[queueElement.Key].Stop();
                times.Add(stopWatch[queueElement.Key].ElapsedMilliseconds);
                queueElement.Value.Set();
            };
            lock (ClientInstanceObjectsLock)
            {
                Parallel.ForEach(primitiveEvents,
                    new ParallelOptions
                    {
                        // Don't choke the chicken.
                        MaxDegreeOfParallelism = Environment.ProcessorCount > 1 ? Environment.ProcessorCount - 1 : 1
                    }, o =>
                    {
                        Primitive queryPrimitive =
                            scansPrimitives.AsParallel().SingleOrDefault(p => p.ID.Equals(o.Key));
                        if (queryPrimitive == null) return;
                        stopWatch[queryPrimitive.ID].Start();
                        Client.Objects.ObjectProperties += ObjectPropertiesEventHandler;
                        Client.Objects.SelectObject(
                            Client.Network.Simulators.FirstOrDefault(p => p.Handle.Equals(queryPrimitive.RegionHandle)),
                            queryPrimitive.LocalID,
                            true);
                        int average = (int) times.Average();
                        primitiveEvents[queryPrimitive.ID].WaitOne(
                            average != 0 ? average : dataTimeout, false);
                        Client.Objects.ObjectProperties -= ObjectPropertiesEventHandler;
                    });
            }
            if (!scansPrimitives.Count.Equals(localPrimitives.Count))
                return false;
            primitives = localPrimitives;
            return true;
        }

        /// <summary>
        ///     Updates a set of avatars by scanning their profile data.
        /// </summary>
        /// <param name="avatars">a list of avatars to update</param>
        /// <param name="millisecondsTimeout">the amount of time in milliseconds to timeout</param>
        /// <param name="dataTimeout">the data timeout</param>
        /// <returns>a list of updated avatars</returns>
        private static bool UpdateAvatars(ref HashSet<Avatar> avatars, int millisecondsTimeout,
            int dataTimeout)
        {
            HashSet<Avatar> scansAvatars = new HashSet<Avatar>(avatars);
            Dictionary<UUID, wasAdaptiveAlarm> avatarAlarms =
                new Dictionary<UUID, wasAdaptiveAlarm>(scansAvatars.AsParallel()
                    .ToDictionary(o => o.ID, p => new wasAdaptiveAlarm(Configuration.DATA_DECAY_TYPE)));
            Dictionary<UUID, Avatar> avatarUpdates = new Dictionary<UUID, Avatar>(scansAvatars.AsParallel()
                .ToDictionary(o => o.ID, p => p));
            object LockObject = new object();
            EventHandler<AvatarInterestsReplyEventArgs> AvatarInterestsReplyEventHandler = (sender, args) =>
            {
                avatarAlarms[args.AvatarID].Alarm(dataTimeout);
                avatarUpdates[args.AvatarID].ProfileInterests = args.Interests;
            };
            EventHandler<AvatarPropertiesReplyEventArgs> AvatarPropertiesReplyEventHandler =
                (sender, args) =>
                {
                    avatarAlarms[args.AvatarID].Alarm(dataTimeout);
                    avatarUpdates[args.AvatarID].ProfileProperties = args.Properties;
                };
            EventHandler<AvatarGroupsReplyEventArgs> AvatarGroupsReplyEventHandler = (sender, args) =>
            {
                avatarAlarms[args.AvatarID].Alarm(dataTimeout);
                lock (LockObject)
                {
                    avatarUpdates[args.AvatarID].Groups.AddRange(args.Groups.Select(o => o.GroupID));
                }
            };
            EventHandler<AvatarPicksReplyEventArgs> AvatarPicksReplyEventHandler =
                (sender, args) => avatarAlarms[args.AvatarID].Alarm(dataTimeout);
            EventHandler<AvatarClassifiedReplyEventArgs> AvatarClassifiedReplyEventHandler =
                (sender, args) => avatarAlarms[args.AvatarID].Alarm(dataTimeout);
            lock (ClientInstanceAvatarsLock)
            {
                Parallel.ForEach(scansAvatars, new ParallelOptions
                {
                    MaxDegreeOfParallelism = Environment.ProcessorCount > 1 ? Environment.ProcessorCount - 1 : 1
                }, o =>
                {
                    Client.Avatars.AvatarInterestsReply += AvatarInterestsReplyEventHandler;
                    Client.Avatars.AvatarPropertiesReply += AvatarPropertiesReplyEventHandler;
                    Client.Avatars.AvatarGroupsReply += AvatarGroupsReplyEventHandler;
                    Client.Avatars.AvatarPicksReply += AvatarPicksReplyEventHandler;
                    Client.Avatars.AvatarClassifiedReply += AvatarClassifiedReplyEventHandler;
                    Client.Avatars.RequestAvatarProperties(o.ID);
                    Client.Avatars.RequestAvatarPicks(o.ID);
                    Client.Avatars.RequestAvatarClassified(o.ID);
                    avatarAlarms[o.ID].Signal.WaitOne(millisecondsTimeout, false);
                    Client.Avatars.AvatarInterestsReply -= AvatarInterestsReplyEventHandler;
                    Client.Avatars.AvatarPropertiesReply -= AvatarPropertiesReplyEventHandler;
                    Client.Avatars.AvatarGroupsReply -= AvatarGroupsReplyEventHandler;
                    Client.Avatars.AvatarPicksReply -= AvatarPicksReplyEventHandler;
                    Client.Avatars.AvatarClassifiedReply -= AvatarClassifiedReplyEventHandler;
                });
            }

            avatars = new HashSet<Avatar>(avatarUpdates.Values);

            return
                !avatarUpdates.Values.AsParallel()
                    .Any(
                        o =>
                            o == null || (
                                o.ProfileInterests.Equals(default(Avatar.Interests)) &&
                                o.ProfileProperties.Equals(default(Avatar.AvatarProperties)) &&
                                o.Groups.Count.Equals(0)));
        }

        /// <summary>
        ///     Requests the UUIDs of all the current groups.
        /// </summary>
        /// <param name="millisecondsTimeout">timeout for the search in milliseconds</param>
        /// <param name="dataTimeout">timeout for receiving answers from services</param>
        /// <param name="groups">a hashset where to store the UUIDs</param>
        /// <returns>true if the current groups could be fetched</returns>
        private static bool directGetCurrentGroups(int millisecondsTimeout, int dataTimeout, ref HashSet<UUID> groups)
        {
            wasAdaptiveAlarm CurrentGroupsReceivedAlarm = new wasAdaptiveAlarm(Configuration.DATA_DECAY_TYPE);
            List<UUID> currentGroups = new List<UUID>();
            object LockObject = new object();
            EventHandler<CurrentGroupsEventArgs> CurrentGroupsEventHandler = (sender, args) =>
            {
                CurrentGroupsReceivedAlarm.Alarm(dataTimeout);
                lock (LockObject)
                {
                    currentGroups.AddRange(args.Groups.Select(o => o.Value.ID));
                }
            };
            Client.Groups.CurrentGroups += CurrentGroupsEventHandler;
            Client.Groups.RequestCurrentGroups();
            if (!CurrentGroupsReceivedAlarm.Signal.WaitOne(millisecondsTimeout, false))
            {
                Client.Groups.CurrentGroups -= CurrentGroupsEventHandler;
                return false;
            }
            Client.Groups.CurrentGroups -= CurrentGroupsEventHandler;
            lock (LockObject)
            {
                if (currentGroups.Count.Equals(0)) return false;
                groups = new HashSet<UUID>(currentGroups);
            }
            return true;
        }

        /// <summary>
        ///     A wrapper for retrieveing all the current groups that implements caching.
        /// </summary>
        /// <param name="millisecondsTimeout">timeout for the search in milliseconds</param>
        /// <param name="dataTimeout">timeout for receiving answers from services</param>
        /// <param name="groups">a hashset where to store the UUIDs</param>
        /// <returns>true if the current groups could be fetched</returns>
        private static bool GetCurrentGroups(int millisecondsTimeout, int dataTimeout, ref HashSet<UUID> groups)
        {
            lock (Cache.Locks.CurrentGroupsCacheLock)
            {
                if (!Cache.CurrentGroupsCache.Count.Equals(0))
                {
                    groups = Cache.CurrentGroupsCache;
                    return true;
                }
            }
            bool succeeded;
            lock (ClientInstanceGroupsLock)
            {
                succeeded = directGetCurrentGroups(millisecondsTimeout, dataTimeout, ref groups);
            }
            if (succeeded)
            {
                lock (Cache.Locks.GroupCacheLock)
                {
                    Cache.CurrentGroupsCache = groups;
                }
            }
            return succeeded;
        }

        /// <summary>
        ///     Get all worn attachments.
        /// </summary>
        /// <param name="millisecondsTimeout">timeout for the search in milliseconds</param>
        /// <returns>attachment points by primitives</returns>
        private static IEnumerable<KeyValuePair<Primitive, AttachmentPoint>> GetAttachments(
            int millisecondsTimeout)
        {
            HashSet<Primitive> primitives;
            lock (ClientInstanceNetworkLock)
            {
                primitives =
                    new HashSet<Primitive>(Client.Network.CurrentSim.ObjectsPrimitives.FindAll(
                        o => o.ParentID.Equals(Client.Self.LocalID)));
            }
            Hashtable primitiveQueue = new Hashtable(primitives.ToDictionary(o => o.ID, o => o.LocalID));
            ManualResetEvent ObjectPropertiesEvent = new ManualResetEvent(false);
            EventHandler<ObjectPropertiesEventArgs> ObjectPropertiesEventHandler = (sender, args) =>
            {
                primitiveQueue.Remove(args.Properties.ObjectID);
                if (!primitiveQueue.Count.Equals(0)) return;
                ObjectPropertiesEvent.Set();
            };
            lock (ClientInstanceObjectsLock)
            {
                Client.Objects.ObjectProperties += ObjectPropertiesEventHandler;
                Client.Objects.SelectObjects(Client.Network.CurrentSim, primitiveQueue.Values.Cast<uint>().ToArray(),
                    true);
                if (ObjectPropertiesEvent.WaitOne(millisecondsTimeout, false))
                {
                    Client.Objects.ObjectProperties -= ObjectPropertiesEventHandler;
                    foreach (KeyValuePair<Primitive, AttachmentPoint> pair in primitives
                        .Select(
                            o =>
                                new KeyValuePair<Primitive, AttachmentPoint>(o,
                                    (AttachmentPoint) (((o.PrimData.State & 0xF0) >> 4) |
                                                       ((o.PrimData.State & ~0xF0) << 4)))))
                    {
                        yield return pair;
                    }
                }
                Client.Objects.ObjectProperties -= ObjectPropertiesEventHandler;
            }
        }

        /// <summary>
        ///     Gets the inventory wearables that are currently being worn.
        /// </summary>
        /// <param name="root">the folder to start the search from</param>
        /// <returns>key value pairs of wearables by name</returns>
        private static IEnumerable<KeyValuePair<AppearanceManager.WearableData, WearableType>> GetWearables(
            InventoryNode root)
        {
            InventoryFolder inventoryFolder = Client.Inventory.Store[root.Data.UUID] as InventoryFolder;
            if (inventoryFolder == null)
            {
                InventoryItem inventoryItem = Client.Inventory.Store[root.Data.UUID] as InventoryItem;
                if (inventoryItem != null)
                {
                    WearableType wearableType = Client.Appearance.IsItemWorn(inventoryItem);
                    if (!wearableType.Equals(WearableType.Invalid))
                    {
                        foreach (
                            var wearable in
                                Client.Appearance.GetWearables()
                                    .AsParallel().Where(o => o.ItemID.Equals(inventoryItem.UUID)))
                        {
                            yield return
                                new KeyValuePair<AppearanceManager.WearableData, WearableType>(wearable, wearable.WearableType);
                        }
                    }
                    yield break;
                }
            }
            foreach (
                KeyValuePair<AppearanceManager.WearableData, WearableType> item in
                    root.Nodes.Values.AsParallel().SelectMany(GetWearables))
            {
                yield return item;
            }
        }

        /// ///
        /// <summary>
        ///     Fetches items by searching the inventory starting with an inventory
        ///     node where the search criteria finds:
        ///     - name as string
        ///     - name as Regex
        ///     - UUID as UUID
        /// </summary>
        /// <param name="root">the node to start the search from</param>
        /// <param name="criteria">the name, UUID or Regex of the item to be found</param>
        /// <returns>a list of items matching the item name</returns>
        private static IEnumerable<T> FindInventory<T>(InventoryNode root, object criteria)
        {
            if ((criteria is Regex && (criteria as Regex).IsMatch(root.Data.Name)) ||
                (criteria is string &&
                 (criteria as string).Equals(root.Data.Name, StringComparison.Ordinal)) ||
                (criteria is UUID && criteria.Equals(root.Data.UUID)))
            {
                if (typeof (T) == typeof (InventoryNode))
                {
                    yield return (T) (object) root;
                }
                if (typeof (T) == typeof (InventoryBase))
                {
                    yield return (T) (object) Client.Inventory.Store[root.Data.UUID];
                }
            }
            foreach (T item in root.Nodes.Values.AsParallel().SelectMany(node => FindInventory<T>(node, criteria)))
            {
                yield return item;
            }
        }

        /// ///
        /// <summary>
        ///     Fetches items and their full path from the inventory starting with
        ///     an inventory node where the search criteria finds:
        ///     - name as string
        ///     - name as Regex
        ///     - UUID as UUID
        /// </summary>
        /// <param name="root">the node to start the search from</param>
        /// <param name="criteria">the name, UUID or Regex of the item to be found</param>
        /// <param name="prefix">any prefix to append to the found paths</param>
        /// <returns>items matching criteria and their full inventoy path</returns>
        private static IEnumerable<KeyValuePair<T, LinkedList<string>>> FindInventoryPath<T>(
            InventoryNode root, object criteria, LinkedList<string> prefix)
        {
            if ((criteria is Regex && (criteria as Regex).IsMatch(root.Data.Name)) ||
                (criteria is string &&
                 (criteria as string).Equals(root.Data.Name, StringComparison.Ordinal)) ||
                (criteria is UUID && criteria.Equals(root.Data.UUID)))
            {
                if (typeof (T) == typeof (InventoryBase))
                {
                    yield return
                        new KeyValuePair<T, LinkedList<string>>((T) (object) Client.Inventory.Store[root.Data.UUID],
                            new LinkedList<string>(
                                prefix.Concat(new[] {root.Data.Name})));
                }
                if (typeof (T) == typeof (InventoryNode))
                {
                    yield return
                        new KeyValuePair<T, LinkedList<string>>((T) (object) root,
                            new LinkedList<string>(
                                prefix.Concat(new[] {root.Data.Name})));
                }
            }
            foreach (
                KeyValuePair<T, LinkedList<string>> o in
                    root.Nodes.Values.AsParallel()
                        .SelectMany(o => FindInventoryPath<T>(o, criteria, new LinkedList<string>(
                            prefix.Concat(new[] {root.Data.Name})))))
            {
                yield return o;
            }
        }

        /// <summary>
        ///     Gets all the items from an inventory folder and returns the items.
        /// </summary>
        /// <param name="rootFolder">a folder from which to search</param>
        /// <param name="folder">the folder to search for</param>
        /// <returns>a list of items from the folder</returns>
        private static IEnumerable<T> GetInventoryFolderContents<T>(InventoryNode rootFolder,
            string folder)
        {
            foreach (
                InventoryNode node in
                    rootFolder.Nodes.Values.AsParallel()
                        .Where(node => node.Data is InventoryFolder && node.Data.Name.Equals(folder))
                )
            {
                foreach (InventoryNode item in node.Nodes.Values)
                {
                    if (typeof (T) == typeof (InventoryNode))
                    {
                        yield return (T) (object) item;
                    }
                    if (typeof (T) == typeof (InventoryBase))
                    {
                        yield return (T) (object) Client.Inventory.Store[item.Data.UUID];
                    }
                }
                break;
            }
        }

        /// <summary>
        ///     Posts messages to console or log-files.
        /// </summary>
        /// <param name="messages">a list of messages</param>
        private static void Feedback(params string[] messages)
        {
            List<string> output = new List<string>
            {
                CORRADE_CONSTANTS.CORRADE,
                string.Format(CultureInfo.InvariantCulture, "[{0}]",
                    DateTime.Now.ToString(CORRADE_CONSTANTS.DATE_TIME_STAMP, DateTimeFormatInfo.InvariantInfo))
            };

            output.AddRange(messages.Select(message => message));

            // Attempt to write to log file,
            if (Configuration.CLIENT_LOG_ENABLED)
            {
                try
                {
                    lock (ClientLogFileLock)
                    {
                        using (
                            StreamWriter logWriter =
                                File.AppendText(Configuration.CLIENT_LOG_FILE))
                        {
                            logWriter.WriteLine(string.Join(CORRADE_CONSTANTS.ERROR_SEPARATOR, output.ToArray()));
                            //logWriter.Flush();
                        }
                    }
                }
                catch (Exception ex)
                {
                    // or fail and append the fail message.
                    output.Add(string.Format(CultureInfo.InvariantCulture, "{0} {1}",
                        wasGetDescriptionFromEnumValue(
                            ConsoleError.COULD_NOT_WRITE_TO_CLIENT_LOG_FILE),
                        ex.Message));
                }
            }

            if (!Environment.UserInteractive)
            {
                switch (Environment.OSVersion.Platform)
                {
                    case PlatformID.Win32NT:
                        CorrodeEventLog.WriteEntry(string.Join(CORRADE_CONSTANTS.ERROR_SEPARATOR, output.ToArray()),
                            EventLogEntryType.Information);
                        break;
                }
                return;
            }

            Console.WriteLine(string.Join(CORRADE_CONSTANTS.ERROR_SEPARATOR, output.ToArray()));
        }

        public static int Main(string[] args)
        {
            if (Environment.UserInteractive)
            {
                if (!args.Length.Equals(0))
                {
                    string action = string.Empty;
                    for (int i = 0; i < args.Length; ++i)
                    {
                        switch (args[i].ToUpper())
                        {
                            case "/INSTALL":
                                action = "INSTALL";
                                break;
                            case "/UNINSTALL":
                                action = "UNINSTALL";
                                break;
                            case "/NAME":
                                if (args.Length > i + 1)
                                {
                                    InstalledServiceName = args[++i];
                                }
                                break;
                        }
                    }
                    switch (action)
                    {
                        case "INSTALL":
                            return InstallService();
                        case "UNINSTALL":
                            return UninstallService();
                    }
                }
                // run interactively and log to console
                Corrode corrade = new Corrode();
                corrade.OnStart(null);
                return 0;
            }

            // run as a standard service
            Run(new Corrode());
            return 0;
        }

        private static int InstallService()
        {
            try
            {
                // install the service with the Windows Service Control Manager (SCM)
                ManagedInstallerClass.InstallHelper(new[] {Assembly.GetExecutingAssembly().Location});
            }
            catch (Exception ex)
            {
                if (ex.InnerException != null && ex.InnerException.GetType() == typeof (Win32Exception))
                {
                    Win32Exception we = (Win32Exception) ex.InnerException;
                    Console.WriteLine("Error(0x{0:X}): Service already installed!", we.ErrorCode);
                    return we.ErrorCode;
                }
                Console.WriteLine(ex.ToString());
                return -1;
            }

            return 0;
        }

        private static int UninstallService()
        {
            try
            {
                // uninstall the service from the Windows Service Control Manager (SCM)
                ManagedInstallerClass.InstallHelper(new[] {"/u", Assembly.GetExecutingAssembly().Location});
            }
            catch (Exception ex)
            {
                if (ex.InnerException.GetType() == typeof (Win32Exception))
                {
                    Win32Exception we = (Win32Exception) ex.InnerException;
                    Console.WriteLine("Error(0x{0:X}): Service not installed!", we.ErrorCode);
                    return we.ErrorCode;
                }
                Console.WriteLine(ex.ToString());
                return -1;
            }

            return 0;
        }

        protected override void OnStop()
        {
            base.OnStop();
            ConnectionSemaphores['u'].Set();
        }

        protected override void OnStart(string[] args)
        {
            base.OnStart(args);
            //Debugger.Break();
            programThread = new Thread(new Corrode().Program);
            programThread.Start();
        }

        // Main entry point.
        public void Program()
        {
            // Set the current directory to the service directory.
            Directory.SetCurrentDirectory(AppDomain.CurrentDomain.BaseDirectory);
            // Load the configuration file.
            Configuration.Load(CORRADE_CONSTANTS.CONFIGURATION_FILE);
            // Write the logo.
            foreach (string line in CORRADE_CONSTANTS.LOGO)
            {
                Feedback(line);
            }
            // Branch on platform and set-up termination handlers.
            switch (Environment.OSVersion.Platform)
            {
                case PlatformID.Win32NT:
                    if (Environment.UserInteractive)
                    {
                        // Setup console handler.
                        ConsoleEventHandler += ConsoleCtrlCheck;
                        NativeMethods.SetConsoleCtrlHandler(ConsoleEventHandler, true);
                        if (Environment.UserInteractive)
                        {
                            Console.CancelKeyPress +=
                                (sender, args) => ConnectionSemaphores['u'].Set();
                        }
                    }
                    break;
            }
            // Set-up watcher for dynamically reading the configuration file.
            FileSystemWatcher configurationWatcher = new FileSystemWatcher
            {
                Path = Directory.GetCurrentDirectory(),
                Filter = CORRADE_CONSTANTS.CONFIGURATION_FILE,
                NotifyFilter = NotifyFilters.LastWrite
            };
            FileSystemEventHandler HandleConfigurationFileChanged =
                (sender, args) => ConfigurationChangedTimer.Change(1000, 0);
            configurationWatcher.Changed += HandleConfigurationFileChanged;
            configurationWatcher.EnableRaisingEvents = true;
            // Set-up the AIML bot in case it has been enabled.
            AIMLBotConfigurationWatcher.Path = wasPathCombine(Directory.GetCurrentDirectory(),
                AIML_BOT_CONSTANTS.DIRECTORY);
            AIMLBotConfigurationWatcher.NotifyFilter = NotifyFilters.LastWrite;
            FileSystemEventHandler HandleAIMLBotConfigurationChanged =
                (sender, args) => AIMLConfigurationChangedTimer.Change(1000, 0);
            AIMLBotConfigurationWatcher.Changed += HandleAIMLBotConfigurationChanged;
            // Network Tweaks
            ServicePointManager.DefaultConnectionLimit = Configuration.CONNECTION_LIMIT;
            ServicePointManager.UseNagleAlgorithm = Configuration.USE_NAGGLE;
            ServicePointManager.Expect100Continue = Configuration.USE_EXPECT100CONTINUE;
            ServicePointManager.MaxServicePointIdleTime = Configuration.CONNECTION_IDLE_TIME;
            // Suppress standard OpenMetaverse logs, we have better ones.
            Settings.LOG_LEVEL = Helpers.LogLevel.None;
            Client.Settings.ALWAYS_REQUEST_PARCEL_ACL = true;
            Client.Settings.ALWAYS_DECODE_OBJECTS = true;
            Client.Settings.ALWAYS_REQUEST_OBJECTS = true;
            Client.Settings.SEND_AGENT_APPEARANCE = true;
            Client.Settings.AVATAR_TRACKING = true;
            Client.Settings.OBJECT_TRACKING = true;
            Client.Settings.PARCEL_TRACKING = true;
            Client.Settings.ALWAYS_REQUEST_PARCEL_DWELL = true;
            Client.Settings.ALWAYS_REQUEST_PARCEL_ACL = true;
            Client.Settings.SEND_AGENT_UPDATES = true;
            // Smoother movement for autopilot.
            Client.Settings.DISABLE_AGENT_UPDATE_DUPLICATE_CHECK = true;
            Client.Settings.USE_ASSET_CACHE = true;
            // More precision for object and avatar tracking updates.
            Client.Settings.USE_INTERPOLATION_TIMER = true;
            // Transfer textures over HTTP if possible.
            Client.Settings.USE_HTTP_TEXTURES = true;
            // Needed for commands dealing with terrain height.
            Client.Settings.STORE_LAND_PATCHES = true;
            // Decode simulator statistics.
            Client.Settings.ENABLE_SIMSTATS = true;
            // Enable multiple simulators
            Client.Settings.MULTIPLE_SIMS = true;
            // Check TOS
            if (!Configuration.TOS_ACCEPTED)
            {
                Feedback(wasGetDescriptionFromEnumValue(ConsoleError.TOS_NOT_ACCEPTED));
                Environment.Exit(Configuration.EXIT_CODE_ABNORMAL);
            }
            // Proceed to log-in.
            LoginParams login = new LoginParams(
                Client,
                Configuration.FIRST_NAME,
                Configuration.LAST_NAME,
                Configuration.PASSWORD,
                CORRADE_CONSTANTS.CLIENT_CHANNEL,
                CORRADE_CONSTANTS.CORRADE_VERSION.ToString(CultureInfo.InvariantCulture),
                Configuration.LOGIN_URL)
            {
                Author = CORRADE_CONSTANTS.WIZARDRY_AND_STEAMWORKS,
                AgreeToTos = Configuration.TOS_ACCEPTED,
                Start = Configuration.START_LOCATION,
                UserAgent =
                    string.Format("{0}/{1} ({2})", CORRADE_CONSTANTS.CORRADE, CORRADE_CONSTANTS.CORRADE_VERSION,
                        CORRADE_CONSTANTS.WIZARDRY_AND_STEAMWORKS_WEBSITE)
            };
            // Set the outgoing IP address if specified in the configuration file.
            if (!string.IsNullOrEmpty(Configuration.BIND_IP_ADDRESS))
            {
                try
                {
                    Settings.BIND_ADDR = IPAddress.Parse(Configuration.BIND_IP_ADDRESS);
                }
                catch (Exception)
                {
                    Feedback(wasGetDescriptionFromEnumValue(ConsoleError.UNKNOWN_IP_ADDRESS));
                    Environment.Exit(Configuration.EXIT_CODE_ABNORMAL);
                }
            }
            // Set the ID0 if specified in the configuration file.
            if (!string.IsNullOrEmpty(Configuration.DRIVE_IDENTIFIER_HASH))
            {
                login.ID0 = Utils.MD5String(Configuration.DRIVE_IDENTIFIER_HASH);
            }
            // Set the MAC if specified in the configuration file.
            if (!string.IsNullOrEmpty(Configuration.NETWORK_CARD_MAC))
            {
                login.MAC = Utils.MD5String(Configuration.NETWORK_CARD_MAC);
            }
            // Load Corrode caches.
            LoadCorrodeCache.Invoke();
            // Load Corrode states.
            lock (GroupNotificationsLock)
            {
                LoadNotificationState.Invoke();
            }
            lock (InventoryOffersLock)
            {
                LoadInventoryOffersState.Invoke();
            }
            // Start the HTTP Server if it is supported
            Thread HTTPListenerThread = null;
            HttpListener HTTPListener = null;
            if (Configuration.ENABLE_HTTP_SERVER && !HttpListener.IsSupported)
            {
                Feedback(wasGetDescriptionFromEnumValue(ConsoleError.HTTP_SERVER_ERROR),
                    wasGetDescriptionFromEnumValue(ConsoleError.HTTP_SERVER_NOT_SUPPORTED));
            }
            if (Configuration.ENABLE_HTTP_SERVER && HttpListener.IsSupported)
            {
                Feedback(wasGetDescriptionFromEnumValue(ConsoleError.STARTING_HTTP_SERVER));
                HTTPListenerThread = new Thread(() =>
                {
                    try
                    {
                        using (HTTPListener = new HttpListener())
                        {
                            HTTPListener.Prefixes.Add(Configuration.HTTP_SERVER_PREFIX);
                            HTTPListener.Start();
                            while (HTTPListener.IsListening)
                            {
                                IAsyncResult result = HTTPListener.BeginGetContext(ProcessHTTPRequest, HTTPListener);
                                result.AsyncWaitHandle.WaitOne(Configuration.HTTP_SERVER_TIMEOUT, false);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Feedback(wasGetDescriptionFromEnumValue(ConsoleError.HTTP_SERVER_ERROR), ex.Message);
                    }
                }) {IsBackground = true, Priority = ThreadPriority.BelowNormal};
                HTTPListenerThread.Start();
            }

            cancellationTokenSource = new CancellationTokenSource();

            // Start the callback thread to send callbacks.
            Thread CallbackThread = new Thread(() =>
            {
                var token = cancellationTokenSource?.Token ?? throw new NullReferenceException();
                do
                {
                    Thread.Sleep(Configuration.CALLBACK_THROTTLE);
                    CallbacksAvailable.Wait(token);

                    CallbackQueueElement callbackQueueElement;
                    if (!CallbackQueue.TryDequeue(out callbackQueueElement)) { break; }

                    try
                    {
                        if (!callbackQueueElement.Equals(default(CallbackQueueElement)))
                        {
                            wasPOST(callbackQueueElement.URL, callbackQueueElement.message,
                                Configuration.CALLBACK_TIMEOUT);
                        }
                    }
                    catch (Exception ex)
                    {
                        Feedback(wasGetDescriptionFromEnumValue(ConsoleError.CALLBACK_ERROR),
                            callbackQueueElement.URL,
                            ex.Message);
                    }
                } while (runCallbackThread);
            }) {IsBackground = true, Priority = ThreadPriority.BelowNormal};
            CallbackThread.Start();
            Thread NotificationThread = new Thread(() =>
            {
                var token = cancellationTokenSource?.Token ?? throw new NullReferenceException();
                do
                {
                    Thread.Sleep(Configuration.NOTIFICATION_THROTTLE);
                    NotificationsAvailable.Wait(token);

                    NotificationQueueElement notificationQueueElement;
                    if (!NotificationQueue.TryDequeue(out notificationQueueElement)) { break; }

                    try
                    {
                        if (!notificationQueueElement.Equals(default(NotificationQueueElement)))
                        {
                            wasPOST(notificationQueueElement.URL, notificationQueueElement.message,
                                Configuration.NOTIFICATION_TIMEOUT);
                        }
                    }
                    catch (Exception ex)
                    {
                        Feedback(wasGetDescriptionFromEnumValue(ConsoleError.NOTIFICATION_ERROR),
                            notificationQueueElement.URL,
                            ex.Message);
                    }
                } while (runNotificationThread);
            }) {IsBackground = true, Priority = ThreadPriority.BelowNormal};
            NotificationThread.Start();
            // Start sphere effect expiration thread
            Thread EffectsExpirationThread = new Thread(() =>
            {
                do
                {
                    Thread.Sleep(1000);
                    lock (SphereEffectsLock)
                    {
                        SphereEffects.RemoveWhere(o => DateTime.Compare(DateTime.Now, o.Termination) > 0);
                    }
                    lock (BeamEffectsLock)
                    {
                        BeamEffects.RemoveWhere(o => DateTime.Compare(DateTime.Now, o.Termination) > 0);
                    }
                } while (runEffectsExpirationThread);
            }) {IsBackground = true, Priority = ThreadPriority.BelowNormal};
            EffectsExpirationThread.Start();
            // Install non-dynamic global event handlers.
            Client.Inventory.InventoryObjectOffered += HandleInventoryObjectOffered;
            Client.Network.LoginProgress += HandleLoginProgress;
            Client.Appearance.AppearanceSet += HandleAppearanceSet;
            Client.Network.SimConnected += HandleSimulatorConnected;
            Client.Network.Disconnected += HandleDisconnected;
            Client.Network.SimDisconnected += HandleSimulatorDisconnected;
            Client.Network.EventQueueRunning += HandleEventQueueRunning;
            Client.Self.TeleportProgress += HandleTeleportProgress;
            Client.Self.ChatFromSimulator += HandleChatFromSimulator;
            Client.Groups.GroupJoinedReply += HandleGroupJoined;
            Client.Groups.GroupLeaveReply += HandleGroupLeave;
            // Each Instant Message is processed in its own thread.
            Client.Self.IM += (sender, args) => CorrodeThreadPool[CorrodeThreadType.INSTANT_MESSAGE].Spawn(
                () => HandleSelfIM(sender, args),
                Configuration.MAXIMUM_INSTANT_MESSAGE_THREADS);
            // Log-in to the grid.
            Feedback(wasGetDescriptionFromEnumValue(ConsoleError.LOGGING_IN));
            Client.Network.Login(login);
            /*
             * The main thread spins around waiting for the semaphores to become invalidated,
             * at which point Corrode will consider its connection to the grid severed and
             * will terminate.
             *
             */
            WaitHandle.WaitAny(ConnectionSemaphores.Values.Select(o => (WaitHandle) o).ToArray());
            // Now log-out.
            Feedback(wasGetDescriptionFromEnumValue(ConsoleError.LOGGING_OUT));
            // Uninstall all installed handlers
            Client.Self.IM -= HandleSelfIM;
            Client.Network.SimChanged -= HandleRadarObjects;
            Client.Objects.AvatarUpdate -= HandleAvatarUpdate;
            Client.Objects.ObjectUpdate -= HandleObjectUpdate;
            Client.Objects.KillObject -= HandleKillObject;
            Client.Self.LoadURL -= HandleLoadURL;
            Client.Self.MoneyBalanceReply -= HandleMoneyBalance;
            Client.Network.SimChanged -= HandleSimChanged;
            Client.Self.RegionCrossed -= HandleRegionCrossed;
            Client.Self.MeanCollision -= HandleMeanCollision;
            Client.Avatars.ViewerEffectLookAt -= HandleViewerEffect;
            Client.Avatars.ViewerEffectPointAt -= HandleViewerEffect;
            Client.Avatars.ViewerEffect -= HandleViewerEffect;
            Client.Objects.TerseObjectUpdate -= HandleTerseObjectUpdate;
            Client.Self.ScriptDialog -= HandleScriptDialog;
            Client.Self.ChatFromSimulator -= HandleChatFromSimulator;
            Client.Self.MoneyBalance -= HandleMoneyBalance;
            Client.Self.AlertMessage -= HandleAlertMessage;
            Client.Self.ScriptQuestion -= HandleScriptQuestion;
            Client.Self.TeleportProgress -= HandleTeleportProgress;
            Client.Friends.FriendRightsUpdate -= HandleFriendRightsUpdate;
            Client.Friends.FriendOffline -= HandleFriendOnlineStatus;
            Client.Friends.FriendOnline -= HandleFriendOnlineStatus;
            Client.Friends.FriendshipResponse -= HandleFriendShipResponse;
            Client.Friends.FriendshipOffered -= HandleFriendshipOffered;
            Client.Network.EventQueueRunning -= HandleEventQueueRunning;
            Client.Network.SimDisconnected -= HandleSimulatorDisconnected;
            Client.Network.Disconnected -= HandleDisconnected;
            Client.Network.SimConnected -= HandleSimulatorConnected;
            Client.Appearance.AppearanceSet -= HandleAppearanceSet;
            Client.Network.LoginProgress -= HandleLoginProgress;
            Client.Inventory.InventoryObjectOffered -= HandleInventoryObjectOffered;
            // Save Corrode states.
            lock (InventoryOffersLock)
            {
                SaveInventoryOffersState.Invoke();
            }
            lock (GroupNotificationsLock)
            {
                SaveNotificationState.Invoke();
            }
            // Save Corrode caches.
            SaveCorrodeCache.Invoke();
            // Stop the sphere effects expiration thread.
            runEffectsExpirationThread = false;
            if (
                (EffectsExpirationThread.ThreadState.Equals(ThreadState.Running) ||
                 EffectsExpirationThread.ThreadState.Equals(ThreadState.WaitSleepJoin)))
            {
                if (!EffectsExpirationThread.Join(1000))
                {
                    try
                    {
                        EffectsExpirationThread.Abort();
                        EffectsExpirationThread.Join();
                    }
                    catch (ThreadStateException)
                    {
                    }
                }
            }
            // Stop the group member sweep thread.
            StopGroupMembershipSweepThread.Invoke();
            // Stop the notification thread.
            runNotificationThread = false;
            if (
                (NotificationThread.ThreadState.Equals(ThreadState.Running) ||
                 NotificationThread.ThreadState.Equals(ThreadState.WaitSleepJoin)))
            {
                if (!NotificationThread.Join(1000))
                {
                    try
                    {
                        NotificationThread.Abort();
                        NotificationThread.Join();
                    }
                    catch (ThreadStateException)
                    {
                    }
                }
            }
            // Stop the callback thread.
            runCallbackThread = false;
            if (
                (CallbackThread.ThreadState.Equals(ThreadState.Running) ||
                 CallbackThread.ThreadState.Equals(ThreadState.WaitSleepJoin)))
            {
                if (!CallbackThread.Join(1000))
                {
                    try
                    {
                        CallbackThread.Abort();
                        CallbackThread.Join();
                    }
                    catch (ThreadStateException)
                    {
                    }
                }
            }
            // Close HTTP server
            if (Configuration.ENABLE_HTTP_SERVER && HttpListener.IsSupported)
            {
                Feedback(wasGetDescriptionFromEnumValue(ConsoleError.STOPPING_HTTP_SERVER));
                if (HTTPListenerThread != null)
                {
                    HTTPListener.Stop();
                    if (
                        (HTTPListenerThread.ThreadState.Equals(ThreadState.Running) ||
                         HTTPListenerThread.ThreadState.Equals(ThreadState.WaitSleepJoin)))
                    {
                        if (!HTTPListenerThread.Join(1000))
                        {
                            try
                            {
                                HTTPListenerThread.Abort();
                                HTTPListenerThread.Join();
                            }
                            catch (ThreadStateException)
                            {
                            }
                        }
                    }
                }
            }
            // Reject any inventory that has not been accepted.
            lock (InventoryOffersLock)
            {
                Parallel.ForEach(InventoryOffers, o =>
                {
                    o.Key.Accept = false;
                    o.Value.Set();
                });

                SaveInventoryOffersState.Invoke();
            }
            // Disable the configuration watcher.
            configurationWatcher.EnableRaisingEvents = false;
            configurationWatcher.Changed -= HandleConfigurationFileChanged;
            // Disable the AIML bot configuration watcher.
            AIMLBotConfigurationWatcher.EnableRaisingEvents = false;
            AIMLBotConfigurationWatcher.Changed -= HandleAIMLBotConfigurationChanged;
            // Save the AIML user session.
            lock (AIMLBotLock)
            {
                if (AIMLBotBrainCompiled)
                {
                    SaveChatBotFiles.Invoke();
                }
            }
            // Logout
            if (Client.Network.Connected)
            {
                // Full speed ahead; do not even attempt to grab a lock.
                ManualResetEvent LoggedOutEvent = new ManualResetEvent(false);
                EventHandler<LoggedOutEventArgs> LoggedOutEventHandler = (sender, args) => LoggedOutEvent.Set();
                Client.Network.LoggedOut += LoggedOutEventHandler;
                Client.Network.RequestLogout();
                if (!LoggedOutEvent.WaitOne(Configuration.LOGOUT_GRACE, false))
                {
                    Client.Network.LoggedOut -= LoggedOutEventHandler;
                    Feedback(wasGetDescriptionFromEnumValue(ConsoleError.TIMEOUT_LOGGING_OUT));
                }
                Client.Network.LoggedOut -= LoggedOutEventHandler;
            }
            if (Client.Network.Connected)
            {
                Client.Network.Shutdown(NetworkManager.DisconnectType.ClientInitiated);
            }
            // Terminate.
            Environment.Exit(Configuration.EXIT_CODE_EXPECTED);
        }

        private static void HandleAvatarUpdate(object sender, AvatarUpdateEventArgs e)
        {
            CorrodeThreadPool[CorrodeThreadType.NOTIFICATION].Spawn(
                () => SendNotification(Notifications.NOTIFICATION_RADAR_AVATARS, e),
                Configuration.MAXIMUM_NOTIFICATION_THREADS);
        }

        private static void HandleObjectUpdate(object sender, PrimEventArgs e)
        {
            CorrodeThreadPool[CorrodeThreadType.NOTIFICATION].Spawn(
                () => SendNotification(Notifications.NOTIFICATION_RADAR_PRIMITIVES, e),
                Configuration.MAXIMUM_NOTIFICATION_THREADS);
        }

        private static void HandleKillObject(object sender, KillObjectEventArgs e)
        {
            KeyValuePair<UUID, Primitive> tracked;
            lock (RadarObjectsLock)
            {
                tracked =
                    RadarObjects.AsParallel().FirstOrDefault(o => o.Value.LocalID.Equals(e.ObjectLocalID));
            }
            if (!tracked.Equals(default(KeyValuePair<UUID, Primitive>)))
            {
                switch (tracked.Value is Avatar)
                {
                    case true:
                        CorrodeThreadPool[CorrodeThreadType.NOTIFICATION].Spawn(
                            () => SendNotification(Notifications.NOTIFICATION_RADAR_AVATARS, e),
                            Configuration.MAXIMUM_NOTIFICATION_THREADS);
                        break;
                    default:
                        CorrodeThreadPool[CorrodeThreadType.NOTIFICATION].Spawn(
                            () => SendNotification(Notifications.NOTIFICATION_RADAR_PRIMITIVES, e),
                            Configuration.MAXIMUM_NOTIFICATION_THREADS);
                        break;
                }
            }
        }

        private static void HandleGroupJoined(object sender, GroupOperationEventArgs e)
        {
            // Add the group to the cache.
            lock (Cache.Locks.CurrentGroupsCacheLock)
            {
                if (!Cache.CurrentGroupsCache.Contains(e.GroupID))
                {
                    Cache.CurrentGroupsCache.Add(e.GroupID);
                }
            }
            // Join group chat if possible.
            if (!Client.Self.GroupChatSessions.ContainsKey(e.GroupID) &&
                HasGroupPowers(Client.Self.AgentID, e.GroupID, GroupPowers.JoinChat,
                    Configuration.SERVICES_TIMEOUT, Configuration.DATA_TIMEOUT))
            {
                JoinGroupChat(e.GroupID, Configuration.SERVICES_TIMEOUT);
            }
        }

        private static void HandleGroupLeave(object sender, GroupOperationEventArgs e)
        {
            // Remove the group from the cache.
            lock (Cache.Locks.CurrentGroupsCacheLock)
            {
                if (Cache.CurrentGroupsCache.Contains(e.GroupID))
                {
                    Cache.CurrentGroupsCache.Remove(e.GroupID);
                }
            }
        }

        private static void HandleLoadURL(object sender, LoadUrlEventArgs e)
        {
            CorrodeThreadPool[CorrodeThreadType.NOTIFICATION].Spawn(
                () => SendNotification(Notifications.NOTIFICATION_LOAD_URL, e),
                Configuration.MAXIMUM_NOTIFICATION_THREADS);
        }

        private static void HandleAppearanceSet(object sender, AppearanceSetEventArgs e)
        {
            if (e.Success)
            {
                Feedback(wasGetDescriptionFromEnumValue(ConsoleError.APPEARANCE_SET_SUCCEEDED));
                return;
            }
            Feedback(wasGetDescriptionFromEnumValue(ConsoleError.APPEARANCE_SET_FAILED));
        }

        private static void HandleRegionCrossed(object sender, RegionCrossedEventArgs e)
        {
            CorrodeThreadPool[CorrodeThreadType.NOTIFICATION].Spawn(
                () => SendNotification(Notifications.NOTIFICATION_REGION_CROSSED, e),
                Configuration.MAXIMUM_NOTIFICATION_THREADS);
        }

        private static void HandleMeanCollision(object sender, MeanCollisionEventArgs e)
        {
            CorrodeThreadPool[CorrodeThreadType.NOTIFICATION].Spawn(
                () => SendNotification(Notifications.NOTIFICATION_MEAN_COLLISION, e),
                Configuration.MAXIMUM_NOTIFICATION_THREADS);
        }

        private static void HandleViewerEffect(object sender, object e)
        {
            CorrodeThreadPool[CorrodeThreadType.NOTIFICATION].Spawn(
                () => SendNotification(Notifications.NOTIFICATION_VIEWER_EFFECT, e),
                Configuration.MAXIMUM_NOTIFICATION_THREADS);
        }

        private static void ProcessHTTPRequest(IAsyncResult ar)
        {
            try
            {
                HttpListener httpListener = ar.AsyncState as HttpListener;
                // bail if we are not listening
                if (httpListener == null || !httpListener.IsListening) return;
                HttpListenerContext httpContext = httpListener.EndGetContext(ar);
                HttpListenerRequest httpRequest = httpContext.Request;
                // only accept POST requests
                if (!httpRequest.HttpMethod.Equals(WebRequestMethods.Http.Post, StringComparison.OrdinalIgnoreCase))
                    return;
                // only accept connected remote endpoints
                if (httpRequest.RemoteEndPoint == null) return;
                using (Stream body = httpRequest.InputStream)
                {
                    using (StreamReader reader = new StreamReader(body, httpRequest.ContentEncoding))
                    {
                        Dictionary<string, string> result = HandleCorrodeCommand(reader.ReadToEnd(),
                            CORRADE_CONSTANTS.WEB_REQUEST,
                            httpRequest.RemoteEndPoint.ToString());
                        using (HttpListenerResponse response = httpContext.Response)
                        {
                            // set the content type based on chosen output filers
                            switch (Configuration.OUTPUT_FILTERS.Last())
                            {
                                case Filter.RFC1738:
                                    response.ContentType = CORRADE_CONSTANTS.CONTENT_TYPE.WWW_FORM_URLENCODED;
                                    break;
                                default:
                                    response.ContentType = CORRADE_CONSTANTS.CONTENT_TYPE.TEXT_PLAIN;
                                    break;
                            }
                            byte[] data = !result.Count.Equals(0)
                                ? Encoding.UTF8.GetBytes(wasKeyValueEncode(wasKeyValueEscape(result)))
                                : new byte[0];
                            response.StatusCode = (int) HttpStatusCode.OK;
                            using (MemoryStream outputStream = new MemoryStream())
                            {
                                switch (Configuration.HTTP_SERVER_COMPRESSION)
                                {
                                    case HTTPCompressionMethod.GZIP:
                                        using (GZipStream dataGZipStream = new GZipStream(outputStream,
                                            CompressionMode.Compress, false))
                                        {
                                            dataGZipStream.Write(data, 0, data.Length);
                                            dataGZipStream.Flush();
                                        }
                                        response.AddHeader("Content-Encoding", "gzip");
                                        data = outputStream.ToArray();
                                        break;
                                    case HTTPCompressionMethod.DEFLATE:
                                        using (
                                            DeflateStream dataDeflateStream = new DeflateStream(outputStream,
                                                CompressionMode.Compress, false))
                                        {
                                            dataDeflateStream.Write(data, 0, data.Length);
                                            dataDeflateStream.Flush();
                                        }
                                        response.AddHeader("Content-Encoding", "deflate");
                                        data = outputStream.ToArray();
                                        break;
                                }
                            }
                            response.ContentLength64 = data.Length;
                            using (Stream responseStream = response.OutputStream)
                            {
                                if (responseStream != null)
                                {
                                    responseStream.Write(data, 0, data.Length);
                                    responseStream.Flush();
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception)
            {
                Feedback(wasGetDescriptionFromEnumValue(ConsoleError.HTTP_SERVER_PROCESSING_ABORTED));
            }
        }

        /// <summary>
        ///     Sends a notification to each group with a configured and installed notification.
        /// </summary>
        /// <param name="notification">the notification to send</param>
        /// <param name="args">the event arguments</param>
        private static void SendNotification(Notifications notification, object args)
        {
            // Only send notifications for groups that have bound to the notification to send.
            lock (GroupNotificationsLock)
            {
                Parallel.ForEach(GroupNotifications, o =>
                {
                    if ((o.NotificationMask & (uint) notification).Equals(0) || !Configuration.GROUPS.AsParallel().Any(
                        p => p.Name.Equals(o.GroupName, StringComparison.Ordinal) &&
                             !(p.NotificationMask & (uint) notification).Equals(0))) return;
                    // Set the notification type
                    Dictionary<string, string> notificationData = new Dictionary<string, string>
                    {
                        {
                            wasGetDescriptionFromEnumValue(ScriptKeys.TYPE),
                            wasGetDescriptionFromEnumValue(notification)
                        }
                    };
                    // Build the notification data
                    switch (notification)
                    {
                        case Notifications.NOTIFICATION_SCRIPT_DIALOG:
                            ScriptDialogEventArgs scriptDialogEventArgs = (ScriptDialogEventArgs) args;
                            notificationData.Add(wasGetDescriptionFromEnumValue(ScriptKeys.MESSAGE),
                                scriptDialogEventArgs.Message);
                            notificationData.Add(wasGetDescriptionFromEnumValue(ScriptKeys.FIRSTNAME),
                                scriptDialogEventArgs.FirstName);
                            notificationData.Add(wasGetDescriptionFromEnumValue(ScriptKeys.LASTNAME),
                                scriptDialogEventArgs.LastName);
                            notificationData.Add(wasGetDescriptionFromEnumValue(ScriptKeys.CHANNEL),
                                scriptDialogEventArgs.Channel.ToString(CultureInfo.InvariantCulture));
                            notificationData.Add(wasGetDescriptionFromEnumValue(ScriptKeys.NAME),
                                scriptDialogEventArgs.ObjectName);
                            notificationData.Add(wasGetDescriptionFromEnumValue(ScriptKeys.ITEM),
                                scriptDialogEventArgs.ObjectID.ToString());
                            notificationData.Add(wasGetDescriptionFromEnumValue(ScriptKeys.OWNER),
                                scriptDialogEventArgs.OwnerID.ToString());
                            notificationData.Add(wasGetDescriptionFromEnumValue(ScriptKeys.BUTTON),
                                wasEnumerableToCSV(scriptDialogEventArgs.ButtonLabels));
                            break;
                        case Notifications.NOTIFICATION_LOCAL_CHAT:
                            ChatEventArgs localChatEventArgs = (ChatEventArgs) args;
                            List<string> chatName =
                                new List<string>(GetAvatarNames(localChatEventArgs.FromName));
                            notificationData.Add(wasGetDescriptionFromEnumValue(ScriptKeys.MESSAGE),
                                localChatEventArgs.Message);
                            notificationData.Add(wasGetDescriptionFromEnumValue(ScriptKeys.FIRSTNAME), chatName.First());
                            notificationData.Add(wasGetDescriptionFromEnumValue(ScriptKeys.LASTNAME), chatName.Last());
                            notificationData.Add(wasGetDescriptionFromEnumValue(ScriptKeys.OWNER),
                                localChatEventArgs.OwnerID.ToString());
                            notificationData.Add(wasGetDescriptionFromEnumValue(ScriptKeys.ITEM),
                                localChatEventArgs.SourceID.ToString());
                            notificationData.Add(wasGetDescriptionFromEnumValue(ScriptKeys.POSITION),
                                localChatEventArgs.Position.ToString());
                            notificationData.Add(wasGetDescriptionFromEnumValue(ScriptKeys.ENTITY),
                                Enum.GetName(typeof (ChatSourceType), localChatEventArgs.SourceType));
                            notificationData.Add(wasGetDescriptionFromEnumValue(ScriptKeys.AUDIBLE),
                                Enum.GetName(typeof (ChatAudibleLevel), localChatEventArgs.AudibleLevel));
                            notificationData.Add(wasGetDescriptionFromEnumValue(ScriptKeys.VOLUME),
                                Enum.GetName(typeof (ChatType), localChatEventArgs.Type));
                            break;
                        case Notifications.NOTIFICATION_BALANCE:
                            BalanceEventArgs balanceEventArgs = (BalanceEventArgs) args;
                            notificationData.Add(wasGetDescriptionFromEnumValue(ScriptKeys.BALANCE),
                                balanceEventArgs.Balance.ToString(CultureInfo.InvariantCulture));
                            break;
                        case Notifications.NOTIFICATION_ALERT_MESSAGE:
                            AlertMessageEventArgs alertMessageEventArgs = (AlertMessageEventArgs) args;
                            notificationData.Add(wasGetDescriptionFromEnumValue(ScriptKeys.MESSAGE),
                                alertMessageEventArgs.Message);
                            break;
                        case Notifications.NOTIFICATION_INVENTORY:
                            System.Type inventoryOfferedType = args.GetType();
                            if (inventoryOfferedType == typeof (InstantMessageEventArgs))
                            {
                                InstantMessageEventArgs inventoryOfferEventArgs = (InstantMessageEventArgs) args;
                                List<string> inventoryObjectOfferedName =
                                    new List<string>(CORRADE_CONSTANTS.AvatarFullNameRegex.Matches(
                                        inventoryOfferEventArgs.IM.FromAgentName)
                                        .Cast<Match>()
                                        .ToDictionary(p => new[]
                                        {
                                            p.Groups["first"].Value,
                                            p.Groups["last"].Value
                                        })
                                        .SelectMany(
                                            p =>
                                                new[]
                                                {
                                                    p.Key[0].Trim(),
                                                    !string.IsNullOrEmpty(p.Key[1])
                                                        ? p.Key[1].Trim()
                                                        : string.Empty
                                                }));
                                switch (!string.IsNullOrEmpty(inventoryObjectOfferedName.Last()))
                                {
                                    case true:
                                        notificationData.Add(wasGetDescriptionFromEnumValue(ScriptKeys.FIRSTNAME),
                                            inventoryObjectOfferedName.First());
                                        notificationData.Add(wasGetDescriptionFromEnumValue(ScriptKeys.LASTNAME),
                                            inventoryObjectOfferedName.Last());
                                        break;
                                    default:
                                        notificationData.Add(wasGetDescriptionFromEnumValue(ScriptKeys.NAME),
                                            inventoryObjectOfferedName.First());
                                        break;
                                }
                                notificationData.Add(wasGetDescriptionFromEnumValue(ScriptKeys.AGENT),
                                    inventoryOfferEventArgs.IM.FromAgentID.ToString());
                                switch (inventoryOfferEventArgs.IM.Dialog)
                                {
                                    case InstantMessageDialog.InventoryAccepted:
                                        notificationData.Add(wasGetDescriptionFromEnumValue(ScriptKeys.ACTION),
                                            wasGetDescriptionFromEnumValue(Action.ACCEPT));
                                        break;
                                    case InstantMessageDialog.InventoryDeclined:
                                        notificationData.Add(wasGetDescriptionFromEnumValue(ScriptKeys.ACTION),
                                            wasGetDescriptionFromEnumValue(Action.DECLINE));
                                        break;
                                    case InstantMessageDialog.TaskInventoryOffered:
                                    case InstantMessageDialog.InventoryOffered:
                                        lock (InventoryOffersLock)
                                        {
                                            KeyValuePair<InventoryObjectOfferedEventArgs, ManualResetEvent>
                                                inventoryObjectOfferedEventArgs =
                                                    InventoryOffers.AsParallel().FirstOrDefault(p =>
                                                        p.Key.Offer.IMSessionID.Equals(
                                                            inventoryOfferEventArgs.IM.IMSessionID));
                                            if (
                                                !inventoryObjectOfferedEventArgs.Equals(
                                                    default(
                                                        KeyValuePair<InventoryObjectOfferedEventArgs, ManualResetEvent>)))
                                            {
                                                switch (inventoryObjectOfferedEventArgs.Key.Accept)
                                                {
                                                    case true:
                                                        notificationData.Add(
                                                            wasGetDescriptionFromEnumValue(ScriptKeys.ACTION),
                                                            wasGetDescriptionFromEnumValue(Action.ACCEPT));
                                                        break;
                                                    default:
                                                        notificationData.Add(
                                                            wasGetDescriptionFromEnumValue(ScriptKeys.ACTION),
                                                            wasGetDescriptionFromEnumValue(Action.DECLINE));
                                                        break;
                                                }
                                            }
                                            GroupCollection groups =
                                                CORRADE_CONSTANTS.InventoryOfferObjectNameRegEx.Match(
                                                    inventoryObjectOfferedEventArgs.Key.Offer.Message).Groups;
                                            if (groups.Count > 0)
                                            {
                                                notificationData.Add(wasGetDescriptionFromEnumValue(ScriptKeys.ITEM),
                                                    groups[1].Value);
                                            }
                                            InventoryOffers.Remove(inventoryObjectOfferedEventArgs.Key);
                                        }
                                        break;
                                }
                                notificationData.Add(wasGetDescriptionFromEnumValue(ScriptKeys.DIRECTION),
                                    wasGetDescriptionFromEnumValue(Action.REPLY));
                                break;
                            }
                            if (inventoryOfferedType == typeof (InventoryObjectOfferedEventArgs))
                            {
                                InventoryObjectOfferedEventArgs inventoryObjectOfferedEventArgs =
                                    (InventoryObjectOfferedEventArgs) args;
                                List<string> inventoryObjectOfferedName =
                                    new List<string>(CORRADE_CONSTANTS.AvatarFullNameRegex.Matches(
                                        inventoryObjectOfferedEventArgs.Offer.FromAgentName)
                                        .Cast<Match>()
                                        .ToDictionary(p => new[]
                                        {
                                            p.Groups["first"].Value,
                                            p.Groups["last"].Value
                                        })
                                        .SelectMany(
                                            p =>
                                                new[]
                                                {
                                                    p.Key[0],
                                                    !string.IsNullOrEmpty(p.Key[1])
                                                        ? p.Key[1]
                                                        : string.Empty
                                                }));
                                switch (!string.IsNullOrEmpty(inventoryObjectOfferedName.Last()))
                                {
                                    case true:
                                        notificationData.Add(wasGetDescriptionFromEnumValue(ScriptKeys.FIRSTNAME),
                                            inventoryObjectOfferedName.First());
                                        notificationData.Add(wasGetDescriptionFromEnumValue(ScriptKeys.LASTNAME),
                                            inventoryObjectOfferedName.Last());
                                        break;
                                    default:
                                        notificationData.Add(wasGetDescriptionFromEnumValue(ScriptKeys.NAME),
                                            inventoryObjectOfferedName.First());
                                        break;
                                }
                                notificationData.Add(wasGetDescriptionFromEnumValue(ScriptKeys.AGENT),
                                    inventoryObjectOfferedEventArgs.Offer.FromAgentID.ToString());
                                notificationData.Add(wasGetDescriptionFromEnumValue(ScriptKeys.ASSET),
                                    inventoryObjectOfferedEventArgs.AssetType.ToString());
                                GroupCollection groups =
                                    CORRADE_CONSTANTS.InventoryOfferObjectNameRegEx.Match(
                                        inventoryObjectOfferedEventArgs.Offer.Message).Groups;
                                if (groups.Count > 0)
                                {
                                    notificationData.Add(wasGetDescriptionFromEnumValue(ScriptKeys.ITEM),
                                        groups[1].Value);
                                }
                                notificationData.Add(wasGetDescriptionFromEnumValue(ScriptKeys.SESSION),
                                    inventoryObjectOfferedEventArgs.Offer.IMSessionID.ToString());
                                notificationData.Add(wasGetDescriptionFromEnumValue(ScriptKeys.DIRECTION),
                                    wasGetDescriptionFromEnumValue(Action.OFFER));
                            }
                            break;
                        case Notifications.NOTIFICATION_SCRIPT_PERMISSION:
                            ScriptQuestionEventArgs scriptQuestionEventArgs = (ScriptQuestionEventArgs) args;
                            notificationData.Add(wasGetDescriptionFromEnumValue(ScriptKeys.ITEM),
                                scriptQuestionEventArgs.ItemID.ToString());
                            notificationData.Add(wasGetDescriptionFromEnumValue(ScriptKeys.TASK),
                                scriptQuestionEventArgs.TaskID.ToString());
                            notificationData.Add(wasGetDescriptionFromEnumValue(ScriptKeys.PERMISSIONS),
                                wasEnumerableToCSV(typeof (ScriptPermission).GetFields(BindingFlags.Public |
                                                                                       BindingFlags.Static)
                                    .AsParallel().Where(
                                        p =>
                                            !(((int) p.GetValue(null) &
                                               (int) scriptQuestionEventArgs.Questions)).Equals(0))
                                    .Select(p => p.Name)));
                            notificationData.Add(wasGetDescriptionFromEnumValue(ScriptKeys.REGION),
                                scriptQuestionEventArgs.Simulator.Name);
                            break;
                        case Notifications.NOTIFICATION_FRIENDSHIP:
                            System.Type friendshipNotificationType = args.GetType();
                            if (friendshipNotificationType == typeof (FriendInfoEventArgs))
                            {
                                FriendInfoEventArgs friendInfoEventArgs = (FriendInfoEventArgs) args;
                                List<string> name =
                                    new List<string>(GetAvatarNames(friendInfoEventArgs.Friend.Name));
                                notificationData.Add(wasGetDescriptionFromEnumValue(ScriptKeys.FIRSTNAME), name.First());
                                notificationData.Add(wasGetDescriptionFromEnumValue(ScriptKeys.LASTNAME), name.Last());
                                notificationData.Add(wasGetDescriptionFromEnumValue(ScriptKeys.AGENT),
                                    friendInfoEventArgs.Friend.UUID.ToString());
                                notificationData.Add(wasGetDescriptionFromEnumValue(ScriptKeys.STATUS),
                                    friendInfoEventArgs.Friend.IsOnline
                                        ? wasGetDescriptionFromEnumValue(Action.ONLINE)
                                        : wasGetDescriptionFromEnumValue(Action.OFFLINE));
                                notificationData.Add(wasGetDescriptionFromEnumValue(ScriptKeys.RIGHTS),
                                    // Return the friend rights as a nice CSV string.
                                    wasEnumerableToCSV(typeof (FriendRights).GetFields(BindingFlags.Public |
                                                                                       BindingFlags.Static)
                                        .AsParallel().Where(
                                            p =>
                                                !(((int) p.GetValue(null) &
                                                   (int) friendInfoEventArgs.Friend.MyFriendRights))
                                                    .Equals(
                                                        0))
                                        .Select(p => p.Name)));
                                notificationData.Add(wasGetDescriptionFromEnumValue(ScriptKeys.ACTION),
                                    wasGetDescriptionFromEnumValue(Action.UPDATE));
                                break;
                            }
                            if (friendshipNotificationType == typeof (FriendshipResponseEventArgs))
                            {
                                FriendshipResponseEventArgs friendshipResponseEventArgs =
                                    (FriendshipResponseEventArgs) args;
                                List<string> friendshipResponseName =
                                    new List<string>(
                                        GetAvatarNames(friendshipResponseEventArgs.AgentName));
                                notificationData.Add(wasGetDescriptionFromEnumValue(ScriptKeys.FIRSTNAME),
                                    friendshipResponseName.First());
                                notificationData.Add(wasGetDescriptionFromEnumValue(ScriptKeys.LASTNAME),
                                    friendshipResponseName.Last());
                                notificationData.Add(wasGetDescriptionFromEnumValue(ScriptKeys.AGENT),
                                    friendshipResponseEventArgs.AgentID.ToString());
                                notificationData.Add(wasGetDescriptionFromEnumValue(ScriptKeys.ACTION),
                                    wasGetDescriptionFromEnumValue(Action.RESPONSE));
                                break;
                            }
                            if (friendshipNotificationType == typeof (FriendshipOfferedEventArgs))
                            {
                                FriendshipOfferedEventArgs friendshipOfferedEventArgs =
                                    (FriendshipOfferedEventArgs) args;
                                List<string> friendshipOfferedName =
                                    new List<string>(GetAvatarNames(friendshipOfferedEventArgs.AgentName));
                                notificationData.Add(wasGetDescriptionFromEnumValue(ScriptKeys.FIRSTNAME),
                                    friendshipOfferedName.First());
                                notificationData.Add(wasGetDescriptionFromEnumValue(ScriptKeys.LASTNAME),
                                    friendshipOfferedName.Last());
                                notificationData.Add(wasGetDescriptionFromEnumValue(ScriptKeys.AGENT),
                                    friendshipOfferedEventArgs.AgentID.ToString());
                                notificationData.Add(wasGetDescriptionFromEnumValue(ScriptKeys.ACTION),
                                    wasGetDescriptionFromEnumValue(Action.REQUEST));
                            }
                            break;
                        case Notifications.NOTIFICATION_TELEPORT_LURE:
                            InstantMessageEventArgs teleportLureEventArgs = (InstantMessageEventArgs) args;
                            List<string> teleportLureName =
                                new List<string>(
                                    GetAvatarNames(teleportLureEventArgs.IM.FromAgentName));
                            notificationData.Add(wasGetDescriptionFromEnumValue(ScriptKeys.FIRSTNAME),
                                teleportLureName.First());
                            notificationData.Add(wasGetDescriptionFromEnumValue(ScriptKeys.LASTNAME),
                                teleportLureName.Last());
                            notificationData.Add(wasGetDescriptionFromEnumValue(ScriptKeys.AGENT),
                                teleportLureEventArgs.IM.FromAgentID.ToString());
                            notificationData.Add(wasGetDescriptionFromEnumValue(ScriptKeys.SESSION),
                                teleportLureEventArgs.IM.IMSessionID.ToString());
                            break;
                        case Notifications.NOTIFICATION_GROUP_NOTICE:
                            InstantMessageEventArgs notificationGroupNoticeEventArgs =
                                (InstantMessageEventArgs) args;
                            List<string> notificationGroupNoticeName =
                                new List<string>(
                                    GetAvatarNames(notificationGroupNoticeEventArgs.IM.FromAgentName));
                            notificationData.Add(wasGetDescriptionFromEnumValue(ScriptKeys.FIRSTNAME),
                                notificationGroupNoticeName.First());
                            notificationData.Add(wasGetDescriptionFromEnumValue(ScriptKeys.LASTNAME),
                                notificationGroupNoticeName.Last());
                            notificationData.Add(wasGetDescriptionFromEnumValue(ScriptKeys.AGENT),
                                notificationGroupNoticeEventArgs.IM.FromAgentID.ToString());
                            string[] noticeData = notificationGroupNoticeEventArgs.IM.Message.Split('|');
                            if (noticeData.Length > 0 && !string.IsNullOrEmpty(noticeData[0]))
                            {
                                notificationData.Add(wasGetDescriptionFromEnumValue(ScriptKeys.SUBJECT), noticeData[0]);
                            }
                            if (noticeData.Length > 1 && !string.IsNullOrEmpty(noticeData[1]))
                            {
                                notificationData.Add(wasGetDescriptionFromEnumValue(ScriptKeys.MESSAGE), noticeData[1]);
                            }
                            switch (notificationGroupNoticeEventArgs.IM.Dialog)
                            {
                                case InstantMessageDialog.GroupNoticeInventoryAccepted:
                                case InstantMessageDialog.GroupNoticeInventoryDeclined:
                                    notificationData.Add(wasGetDescriptionFromEnumValue(ScriptKeys.ACTION),
                                        !notificationGroupNoticeEventArgs.IM.Dialog.Equals(
                                            InstantMessageDialog.GroupNoticeInventoryAccepted)
                                            ? wasGetDescriptionFromEnumValue(Action.DECLINE)
                                            : wasGetDescriptionFromEnumValue(Action.ACCEPT));
                                    break;
                                case InstantMessageDialog.GroupNotice:
                                    notificationData.Add(wasGetDescriptionFromEnumValue(ScriptKeys.ACTION),
                                        wasGetDescriptionFromEnumValue(Action.RECEIVED));
                                    break;
                            }
                            break;
                        case Notifications.NOTIFICATION_INSTANT_MESSAGE:
                            InstantMessageEventArgs notificationInstantMessage =
                                (InstantMessageEventArgs) args;
                            List<string> notificationInstantMessageName =
                                new List<string>(
                                    GetAvatarNames(notificationInstantMessage.IM.FromAgentName));
                            notificationData.Add(wasGetDescriptionFromEnumValue(ScriptKeys.FIRSTNAME),
                                notificationInstantMessageName.First());
                            notificationData.Add(wasGetDescriptionFromEnumValue(ScriptKeys.LASTNAME),
                                notificationInstantMessageName.Last());
                            notificationData.Add(wasGetDescriptionFromEnumValue(ScriptKeys.AGENT),
                                notificationInstantMessage.IM.FromAgentID.ToString());
                            notificationData.Add(wasGetDescriptionFromEnumValue(ScriptKeys.MESSAGE),
                                notificationInstantMessage.IM.Message);
                            break;
                        case Notifications.NOTIFICATION_REGION_MESSAGE:
                            InstantMessageEventArgs notificationRegionMessage =
                                (InstantMessageEventArgs) args;
                            List<string> notificationRegionMessageName =
                                new List<string>(
                                    GetAvatarNames(notificationRegionMessage.IM.FromAgentName));
                            notificationData.Add(wasGetDescriptionFromEnumValue(ScriptKeys.FIRSTNAME),
                                notificationRegionMessageName.First());
                            notificationData.Add(wasGetDescriptionFromEnumValue(ScriptKeys.LASTNAME),
                                notificationRegionMessageName.Last());
                            notificationData.Add(wasGetDescriptionFromEnumValue(ScriptKeys.AGENT),
                                notificationRegionMessage.IM.FromAgentID.ToString());
                            notificationData.Add(wasGetDescriptionFromEnumValue(ScriptKeys.MESSAGE),
                                notificationRegionMessage.IM.Message);
                            break;
                        case Notifications.NOTIFICATION_GROUP_MESSAGE:
                            InstantMessageEventArgs notificationGroupMessage =
                                (InstantMessageEventArgs) args;
                            List<string> notificationGroupMessageName =
                                new List<string>(
                                    GetAvatarNames(notificationGroupMessage.IM.FromAgentName));
                            notificationData.Add(wasGetDescriptionFromEnumValue(ScriptKeys.FIRSTNAME),
                                notificationGroupMessageName.First());
                            notificationData.Add(wasGetDescriptionFromEnumValue(ScriptKeys.LASTNAME),
                                notificationGroupMessageName.Last());
                            notificationData.Add(wasGetDescriptionFromEnumValue(ScriptKeys.AGENT),
                                notificationGroupMessage.IM.FromAgentID.ToString());
                            notificationData.Add(wasGetDescriptionFromEnumValue(ScriptKeys.GROUP), o.GroupName);
                            notificationData.Add(wasGetDescriptionFromEnumValue(ScriptKeys.MESSAGE),
                                notificationGroupMessage.IM.Message);
                            break;
                        case Notifications.NOTIFICATION_VIEWER_EFFECT:
                            System.Type viewerEffectType = args.GetType();
                            if (viewerEffectType == typeof (ViewerEffectEventArgs))
                            {
                                ViewerEffectEventArgs notificationViewerEffectEventArgs =
                                    (ViewerEffectEventArgs) args;
                                notificationData.Add(wasGetDescriptionFromEnumValue(ScriptKeys.EFFECT),
                                    notificationViewerEffectEventArgs.Type.ToString());
                                notificationData.Add(wasGetDescriptionFromEnumValue(ScriptKeys.SOURCE),
                                    notificationViewerEffectEventArgs.SourceID.ToString());
                                notificationData.Add(wasGetDescriptionFromEnumValue(ScriptKeys.TARGET),
                                    notificationViewerEffectEventArgs.TargetID.ToString());
                                notificationData.Add(wasGetDescriptionFromEnumValue(ScriptKeys.POSITION),
                                    notificationViewerEffectEventArgs.TargetPosition.ToString());
                                notificationData.Add(wasGetDescriptionFromEnumValue(ScriptKeys.DURATION),
                                    notificationViewerEffectEventArgs.Duration.ToString(
                                        CultureInfo.InvariantCulture));
                                notificationData.Add(wasGetDescriptionFromEnumValue(ScriptKeys.ID),
                                    notificationViewerEffectEventArgs.EffectID.ToString());
                                notificationData.Add(wasGetDescriptionFromEnumValue(ScriptKeys.ACTION),
                                    wasGetDescriptionFromEnumValue(Action.GENERIC));
                                break;
                            }
                            if (viewerEffectType == typeof (ViewerEffectPointAtEventArgs))
                            {
                                ViewerEffectPointAtEventArgs notificationViewerPointAtEventArgs =
                                    (ViewerEffectPointAtEventArgs) args;
                                notificationData.Add(wasGetDescriptionFromEnumValue(ScriptKeys.SOURCE),
                                    notificationViewerPointAtEventArgs.SourceID.ToString());
                                notificationData.Add(wasGetDescriptionFromEnumValue(ScriptKeys.TARGET),
                                    notificationViewerPointAtEventArgs.TargetID.ToString());
                                notificationData.Add(wasGetDescriptionFromEnumValue(ScriptKeys.POSITION),
                                    notificationViewerPointAtEventArgs.TargetPosition.ToString());
                                notificationData.Add(wasGetDescriptionFromEnumValue(ScriptKeys.DURATION),
                                    notificationViewerPointAtEventArgs.Duration.ToString(
                                        CultureInfo.InvariantCulture));
                                notificationData.Add(wasGetDescriptionFromEnumValue(ScriptKeys.ID),
                                    notificationViewerPointAtEventArgs.EffectID.ToString());
                                notificationData.Add(wasGetDescriptionFromEnumValue(ScriptKeys.ACTION),
                                    wasGetDescriptionFromEnumValue(Action.POINT));
                                break;
                            }
                            if (viewerEffectType == typeof (ViewerEffectLookAtEventArgs))
                            {
                                ViewerEffectLookAtEventArgs notificationViewerLookAtEventArgs =
                                    (ViewerEffectLookAtEventArgs) args;
                                notificationData.Add(wasGetDescriptionFromEnumValue(ScriptKeys.SOURCE),
                                    notificationViewerLookAtEventArgs.SourceID.ToString());
                                notificationData.Add(wasGetDescriptionFromEnumValue(ScriptKeys.TARGET),
                                    notificationViewerLookAtEventArgs.TargetID.ToString());
                                notificationData.Add(wasGetDescriptionFromEnumValue(ScriptKeys.POSITION),
                                    notificationViewerLookAtEventArgs.TargetPosition.ToString());
                                notificationData.Add(wasGetDescriptionFromEnumValue(ScriptKeys.DURATION),
                                    notificationViewerLookAtEventArgs.Duration.ToString(
                                        CultureInfo.InvariantCulture));
                                notificationData.Add(wasGetDescriptionFromEnumValue(ScriptKeys.ID),
                                    notificationViewerLookAtEventArgs.EffectID.ToString());
                                notificationData.Add(wasGetDescriptionFromEnumValue(ScriptKeys.ACTION),
                                    wasGetDescriptionFromEnumValue(Action.LOOK));
                            }
                            break;
                        case Notifications.NOTIFICATION_MEAN_COLLISION:
                            MeanCollisionEventArgs meanCollisionEventArgs =
                                (MeanCollisionEventArgs) args;
                            notificationData.Add(wasGetDescriptionFromEnumValue(ScriptKeys.AGGRESSOR),
                                meanCollisionEventArgs.Aggressor.ToString());
                            notificationData.Add(wasGetDescriptionFromEnumValue(ScriptKeys.MAGNITUDE),
                                meanCollisionEventArgs.Magnitude.ToString(CultureInfo.InvariantCulture));
                            notificationData.Add(wasGetDescriptionFromEnumValue(ScriptKeys.TIME),
                                meanCollisionEventArgs.Time.ToLongDateString());
                            notificationData.Add(wasGetDescriptionFromEnumValue(ScriptKeys.ENTITY),
                                meanCollisionEventArgs.Type.ToString());
                            notificationData.Add(wasGetDescriptionFromEnumValue(ScriptKeys.VICTIM),
                                meanCollisionEventArgs.Victim.ToString());
                            break;
                        case Notifications.NOTIFICATION_REGION_CROSSED:
                            System.Type regionChangeType = args.GetType();
                            if (regionChangeType == typeof (SimChangedEventArgs))
                            {
                                SimChangedEventArgs simChangedEventArgs = (SimChangedEventArgs) args;
                                notificationData.Add(wasGetDescriptionFromEnumValue(ScriptKeys.OLD),
                                    simChangedEventArgs.PreviousSimulator.Name);
                                notificationData.Add(wasGetDescriptionFromEnumValue(ScriptKeys.NEW),
                                    Client.Network.CurrentSim.Name);
                                notificationData.Add(wasGetDescriptionFromEnumValue(ScriptKeys.ACTION),
                                    wasGetDescriptionFromEnumValue(Action.CHANGED));
                                break;
                            }
                            if (regionChangeType == typeof (RegionCrossedEventArgs))
                            {
                                RegionCrossedEventArgs regionCrossedEventArgs =
                                    (RegionCrossedEventArgs) args;
                                notificationData.Add(wasGetDescriptionFromEnumValue(ScriptKeys.OLD),
                                    regionCrossedEventArgs.OldSimulator.Name);
                                notificationData.Add(wasGetDescriptionFromEnumValue(ScriptKeys.NEW),
                                    regionCrossedEventArgs.NewSimulator.Name);
                                notificationData.Add(wasGetDescriptionFromEnumValue(ScriptKeys.ACTION),
                                    wasGetDescriptionFromEnumValue(Action.CROSSED));
                            }
                            break;
                        case Notifications.NOTIFICATION_TERSE_UPDATES:
                            TerseObjectUpdateEventArgs terseObjectUpdateEventArgs =
                                (TerseObjectUpdateEventArgs) args;
                            notificationData.Add(wasGetDescriptionFromEnumValue(ScriptKeys.ID),
                                terseObjectUpdateEventArgs.Prim.ID.ToString());
                            notificationData.Add(wasGetDescriptionFromEnumValue(ScriptKeys.POSITION),
                                terseObjectUpdateEventArgs.Prim.Position.ToString());
                            notificationData.Add(wasGetDescriptionFromEnumValue(ScriptKeys.ROTATION),
                                terseObjectUpdateEventArgs.Prim.Rotation.ToString());
                            notificationData.Add(wasGetDescriptionFromEnumValue(ScriptKeys.ENTITY),
                                terseObjectUpdateEventArgs.Prim.PrimData.PCode.ToString());
                            break;
                        case Notifications.NOTIFICATION_TYPING:
                            InstantMessageEventArgs notificationTypingMessageEventArgs = (InstantMessageEventArgs) args;
                            List<string> notificationTypingMessageName =
                                new List<string>(
                                    GetAvatarNames(notificationTypingMessageEventArgs.IM.FromAgentName));
                            notificationData.Add(wasGetDescriptionFromEnumValue(ScriptKeys.FIRSTNAME),
                                notificationTypingMessageName.First());
                            notificationData.Add(wasGetDescriptionFromEnumValue(ScriptKeys.LASTNAME),
                                notificationTypingMessageName.Last());
                            notificationData.Add(wasGetDescriptionFromEnumValue(ScriptKeys.AGENT),
                                notificationTypingMessageEventArgs.IM.FromAgentID.ToString());
                            switch (notificationTypingMessageEventArgs.IM.Dialog)
                            {
                                case InstantMessageDialog.StartTyping:
                                case InstantMessageDialog.StopTyping:
                                    notificationData.Add(wasGetDescriptionFromEnumValue(ScriptKeys.ACTION),
                                        !notificationTypingMessageEventArgs.IM.Dialog.Equals(
                                            InstantMessageDialog.StartTyping)
                                            ? wasGetDescriptionFromEnumValue(Action.STOP)
                                            : wasGetDescriptionFromEnumValue(Action.START));
                                    break;
                            }
                            break;
                        case Notifications.NOTIFICATION_GROUP_INVITE:
                            InstantMessageEventArgs notificationGroupInviteEventArgs = (InstantMessageEventArgs) args;
                            List<string> notificationGroupInviteName =
                                new List<string>(
                                    GetAvatarNames(notificationGroupInviteEventArgs.IM.FromAgentName));
                            notificationData.Add(wasGetDescriptionFromEnumValue(ScriptKeys.FIRSTNAME),
                                notificationGroupInviteName.First());
                            notificationData.Add(wasGetDescriptionFromEnumValue(ScriptKeys.LASTNAME),
                                notificationGroupInviteName.Last());
                            notificationData.Add(wasGetDescriptionFromEnumValue(ScriptKeys.AGENT),
                                notificationGroupInviteEventArgs.IM.FromAgentID.ToString());
                            notificationData.Add(wasGetDescriptionFromEnumValue(ScriptKeys.GROUP),
                                GroupInvites.AsParallel().FirstOrDefault(
                                    p => p.Session.Equals(notificationGroupInviteEventArgs.IM.IMSessionID))
                                    .Group);
                            notificationData.Add(wasGetDescriptionFromEnumValue(ScriptKeys.SESSION),
                                notificationGroupInviteEventArgs.IM.IMSessionID.ToString());
                            break;
                        case Notifications.NOTIFICATION_ECONOMY:
                            MoneyBalanceReplyEventArgs notificationMoneyBalanceEventArgs =
                                (MoneyBalanceReplyEventArgs) args;
                            notificationData.Add(wasGetDescriptionFromEnumValue(ScriptKeys.BALANCE),
                                notificationMoneyBalanceEventArgs.Balance.ToString(CultureInfo.InvariantCulture));
                            notificationData.Add(wasGetDescriptionFromEnumValue(ScriptKeys.DESCRIPTION),
                                notificationMoneyBalanceEventArgs.Description);
                            notificationData.Add(wasGetDescriptionFromEnumValue(ScriptKeys.COMMITTED),
                                notificationMoneyBalanceEventArgs.MetersCommitted.ToString(CultureInfo.InvariantCulture));
                            notificationData.Add(wasGetDescriptionFromEnumValue(ScriptKeys.CREDIT),
                                notificationMoneyBalanceEventArgs.MetersCredit.ToString(CultureInfo.InvariantCulture));
                            notificationData.Add(wasGetDescriptionFromEnumValue(ScriptKeys.SUCCESS),
                                notificationMoneyBalanceEventArgs.Success.ToString());
                            notificationData.Add(wasGetDescriptionFromEnumValue(ScriptKeys.ID),
                                notificationMoneyBalanceEventArgs.TransactionID.ToString());
                            notificationData.Add(wasGetDescriptionFromEnumValue(ScriptKeys.AMOUNT),
                                notificationMoneyBalanceEventArgs.TransactionInfo.Amount.ToString(
                                    CultureInfo.InvariantCulture));
                            notificationData.Add(wasGetDescriptionFromEnumValue(ScriptKeys.TARGET),
                                notificationMoneyBalanceEventArgs.TransactionInfo.DestID.ToString());
                            notificationData.Add(wasGetDescriptionFromEnumValue(ScriptKeys.SOURCE),
                                notificationMoneyBalanceEventArgs.TransactionInfo.SourceID.ToString());
                            notificationData.Add(wasGetDescriptionFromEnumValue(ScriptKeys.TRANSACTION),
                                Enum.GetName(typeof (MoneyTransactionType),
                                    notificationMoneyBalanceEventArgs.TransactionInfo.TransactionType));
                            break;
                        case Notifications.NOTIFICATION_GROUP_MEMBERSHIP:
                            GroupMembershipEventArgs groupMembershipEventArgs = (GroupMembershipEventArgs) args;
                            List<string> groupMembershipName =
                                new List<string>(
                                    GetAvatarNames(groupMembershipEventArgs.AgentName));
                            notificationData.Add(wasGetDescriptionFromEnumValue(ScriptKeys.FIRSTNAME),
                                groupMembershipName.First());
                            notificationData.Add(wasGetDescriptionFromEnumValue(ScriptKeys.LASTNAME),
                                groupMembershipName.Last());
                            notificationData.Add(wasGetDescriptionFromEnumValue(ScriptKeys.AGENT),
                                groupMembershipEventArgs.AgentUUID.ToString());
                            notificationData.Add(wasGetDescriptionFromEnumValue(ScriptKeys.GROUP),
                                o.GroupName);
                            switch (groupMembershipEventArgs.Action)
                            {
                                case Action.JOINED:
                                case Action.PARTED:
                                    notificationData.Add(wasGetDescriptionFromEnumValue(ScriptKeys.ACTION),
                                        !groupMembershipEventArgs.Action.Equals(
                                            Action.JOINED)
                                            ? wasGetDescriptionFromEnumValue(Action.PARTED)
                                            : wasGetDescriptionFromEnumValue(Action.JOINED));
                                    break;
                            }
                            break;
                        case Notifications.NOTIFICATION_LOAD_URL:
                            LoadUrlEventArgs loadURLEventArgs = (LoadUrlEventArgs) args;
                            notificationData.Add(wasGetDescriptionFromEnumValue(ScriptKeys.NAME),
                                loadURLEventArgs.ObjectName);
                            notificationData.Add(wasGetDescriptionFromEnumValue(ScriptKeys.ITEM),
                                loadURLEventArgs.ObjectID.ToString());
                            notificationData.Add(wasGetDescriptionFromEnumValue(ScriptKeys.OWNER),
                                loadURLEventArgs.OwnerID.ToString());
                            notificationData.Add(wasGetDescriptionFromEnumValue(ScriptKeys.GROUP),
                                loadURLEventArgs.OwnerIsGroup.ToString());
                            notificationData.Add(wasGetDescriptionFromEnumValue(ScriptKeys.MESSAGE),
                                loadURLEventArgs.Message);
                            notificationData.Add(wasGetDescriptionFromEnumValue(ScriptKeys.URL),
                                loadURLEventArgs.URL);
                            break;
                        case Notifications.NOTIFICATION_OWNER_SAY:
                            ChatEventArgs ownerSayEventArgs = (ChatEventArgs) args;
                            notificationData.Add(wasGetDescriptionFromEnumValue(ScriptKeys.MESSAGE),
                                ownerSayEventArgs.Message);
                            notificationData.Add(wasGetDescriptionFromEnumValue(ScriptKeys.ITEM),
                                ownerSayEventArgs.SourceID.ToString());
                            notificationData.Add(wasGetDescriptionFromEnumValue(ScriptKeys.NAME),
                                ownerSayEventArgs.FromName);
                            notificationData.Add(wasGetDescriptionFromEnumValue(ScriptKeys.POSITION),
                                ownerSayEventArgs.Position.ToString());
                            break;
                        case Notifications.NOTIFICATION_REGION_SAY_TO:
                            ChatEventArgs regionSayToEventArgs = (ChatEventArgs) args;
                            notificationData.Add(wasGetDescriptionFromEnumValue(ScriptKeys.MESSAGE),
                                regionSayToEventArgs.Message);
                            notificationData.Add(wasGetDescriptionFromEnumValue(ScriptKeys.OWNER),
                                regionSayToEventArgs.OwnerID.ToString());
                            notificationData.Add(wasGetDescriptionFromEnumValue(ScriptKeys.ITEM),
                                regionSayToEventArgs.SourceID.ToString());
                            notificationData.Add(wasGetDescriptionFromEnumValue(ScriptKeys.NAME),
                                regionSayToEventArgs.FromName);
                            notificationData.Add(wasGetDescriptionFromEnumValue(ScriptKeys.POSITION),
                                regionSayToEventArgs.Position.ToString());
                            break;
                        case Notifications.NOTIFICATION_OBJECT_INSTANT_MESSAGE:
                            InstantMessageEventArgs notificationObjectInstantMessage =
                                (InstantMessageEventArgs) args;
                            notificationData.Add(wasGetDescriptionFromEnumValue(ScriptKeys.OWNER),
                                notificationObjectInstantMessage.IM.FromAgentID.ToString());
                            notificationData.Add(wasGetDescriptionFromEnumValue(ScriptKeys.ITEM),
                                notificationObjectInstantMessage.IM.IMSessionID.ToString());
                            notificationData.Add(wasGetDescriptionFromEnumValue(ScriptKeys.NAME),
                                notificationObjectInstantMessage.IM.FromAgentName);
                            notificationData.Add(wasGetDescriptionFromEnumValue(ScriptKeys.MESSAGE),
                                notificationObjectInstantMessage.IM.Message);
                            break;
                        case Notifications.NOTIFICATION_RLV_MESSAGE:
                            ChatEventArgs RLVEventArgs = (ChatEventArgs) args;
                            notificationData.Add(wasGetDescriptionFromEnumValue(ScriptKeys.ITEM),
                                RLVEventArgs.SourceID.ToString());
                            notificationData.Add(wasGetDescriptionFromEnumValue(ScriptKeys.NAME),
                                RLVEventArgs.FromName);
                            notificationData.Add(wasGetDescriptionFromEnumValue(ScriptKeys.POSITION),
                                RLVEventArgs.Position.ToString());
                            notificationData.Add(wasGetDescriptionFromEnumValue(ScriptKeys.RLV),
                                wasEnumerableToCSV(wasRLVToString(RLVEventArgs.Message)));
                            break;
                        case Notifications.NOTIFICATION_DEBUG_MESSAGE:
                            ChatEventArgs DebugEventArgs = (ChatEventArgs) args;
                            notificationData.Add(wasGetDescriptionFromEnumValue(ScriptKeys.ITEM),
                                DebugEventArgs.SourceID.ToString());
                            notificationData.Add(wasGetDescriptionFromEnumValue(ScriptKeys.NAME),
                                DebugEventArgs.FromName);
                            notificationData.Add(wasGetDescriptionFromEnumValue(ScriptKeys.POSITION),
                                DebugEventArgs.Position.ToString());
                            notificationData.Add(wasGetDescriptionFromEnumValue(ScriptKeys.MESSAGE),
                                DebugEventArgs.Message);
                            break;
                        case Notifications.NOTIFICATION_RADAR_AVATARS:
                            System.Type radarAvatarsType = args.GetType();
                            if (radarAvatarsType == typeof (AvatarUpdateEventArgs))
                            {
                                AvatarUpdateEventArgs avatarUpdateEventArgs =
                                    (AvatarUpdateEventArgs) args;
                                lock (RadarObjectsLock)
                                {
                                    if (RadarObjects.ContainsKey(avatarUpdateEventArgs.Avatar.ID)) return;
                                    RadarObjects.Add(avatarUpdateEventArgs.Avatar.ID, avatarUpdateEventArgs.Avatar);
                                }
                                notificationData.Add(wasGetDescriptionFromEnumValue(ScriptKeys.FIRSTNAME),
                                    avatarUpdateEventArgs.Avatar.FirstName);
                                notificationData.Add(wasGetDescriptionFromEnumValue(ScriptKeys.LASTNAME),
                                    avatarUpdateEventArgs.Avatar.LastName);
                                notificationData.Add(wasGetDescriptionFromEnumValue(ScriptKeys.ID),
                                    avatarUpdateEventArgs.Avatar.ID.ToString());
                                notificationData.Add(wasGetDescriptionFromEnumValue(ScriptKeys.POSITION),
                                    avatarUpdateEventArgs.Avatar.Position.ToString());
                                notificationData.Add(wasGetDescriptionFromEnumValue(ScriptKeys.ROTATION),
                                    avatarUpdateEventArgs.Avatar.Rotation.ToString());
                                notificationData.Add(wasGetDescriptionFromEnumValue(ScriptKeys.ENTITY),
                                    avatarUpdateEventArgs.Avatar.PrimData.PCode.ToString());
                                notificationData.Add(wasGetDescriptionFromEnumValue(ScriptKeys.ACTION),
                                    wasGetDescriptionFromEnumValue(Action.APPEAR));
                                break;
                            }
                            if (radarAvatarsType == typeof (KillObjectEventArgs))
                            {
                                KillObjectEventArgs killObjectEventArgs =
                                    (KillObjectEventArgs) args;
                                Avatar avatar;
                                lock (RadarObjectsLock)
                                {
                                    KeyValuePair<UUID, Primitive> tracked =
                                        RadarObjects.AsParallel().FirstOrDefault(
                                            p => p.Value.LocalID.Equals(killObjectEventArgs.ObjectLocalID));
                                    if (tracked.Equals(default(KeyValuePair<UUID, Primitive>))) return;
                                    RadarObjects.Remove(tracked.Key);
                                    if (!(tracked.Value is Avatar)) return;
                                    avatar = tracked.Value as Avatar;
                                }
                                notificationData.Add(wasGetDescriptionFromEnumValue(ScriptKeys.FIRSTNAME),
                                    avatar.FirstName);
                                notificationData.Add(wasGetDescriptionFromEnumValue(ScriptKeys.LASTNAME),
                                    avatar.LastName);
                                notificationData.Add(wasGetDescriptionFromEnumValue(ScriptKeys.ID),
                                    avatar.ID.ToString());
                                notificationData.Add(wasGetDescriptionFromEnumValue(ScriptKeys.POSITION),
                                    avatar.Position.ToString());
                                notificationData.Add(wasGetDescriptionFromEnumValue(ScriptKeys.ROTATION),
                                    avatar.Rotation.ToString());
                                notificationData.Add(wasGetDescriptionFromEnumValue(ScriptKeys.ENTITY),
                                    avatar.PrimData.PCode.ToString());
                                notificationData.Add(wasGetDescriptionFromEnumValue(ScriptKeys.ACTION),
                                    wasGetDescriptionFromEnumValue(Action.VANISH));
                            }
                            break;
                        case Notifications.NOTIFICATION_RADAR_PRIMITIVES:
                            System.Type radarPrimitivesType = args.GetType();
                            if (radarPrimitivesType == typeof (PrimEventArgs))
                            {
                                PrimEventArgs primEventArgs =
                                    (PrimEventArgs) args;
                                lock (RadarObjectsLock)
                                {
                                    if (RadarObjects.ContainsKey(primEventArgs.Prim.ID)) return;
                                    RadarObjects.Add(primEventArgs.Prim.ID, primEventArgs.Prim);
                                }
                                notificationData.Add(wasGetDescriptionFromEnumValue(ScriptKeys.OWNER),
                                    primEventArgs.Prim.OwnerID.ToString());
                                notificationData.Add(wasGetDescriptionFromEnumValue(ScriptKeys.ID),
                                    primEventArgs.Prim.ID.ToString());
                                notificationData.Add(wasGetDescriptionFromEnumValue(ScriptKeys.POSITION),
                                    primEventArgs.Prim.Position.ToString());
                                notificationData.Add(wasGetDescriptionFromEnumValue(ScriptKeys.ROTATION),
                                    primEventArgs.Prim.Rotation.ToString());
                                notificationData.Add(wasGetDescriptionFromEnumValue(ScriptKeys.ENTITY),
                                    primEventArgs.Prim.PrimData.PCode.ToString());
                                notificationData.Add(wasGetDescriptionFromEnumValue(ScriptKeys.ACTION),
                                    wasGetDescriptionFromEnumValue(Action.APPEAR));
                                break;
                            }
                            if (radarPrimitivesType == typeof (KillObjectEventArgs))
                            {
                                KillObjectEventArgs killObjectEventArgs =
                                    (KillObjectEventArgs) args;
                                Primitive prim;
                                lock (RadarObjectsLock)
                                {
                                    KeyValuePair<UUID, Primitive> tracked =
                                        RadarObjects.AsParallel().FirstOrDefault(
                                            p => p.Value.LocalID.Equals(killObjectEventArgs.ObjectLocalID));
                                    if (tracked.Equals(default(KeyValuePair<UUID, Primitive>))) return;
                                    RadarObjects.Remove(tracked.Key);
                                    prim = tracked.Value;
                                }
                                notificationData.Add(wasGetDescriptionFromEnumValue(ScriptKeys.OWNER),
                                    prim.OwnerID.ToString());
                                notificationData.Add(wasGetDescriptionFromEnumValue(ScriptKeys.ID),
                                    prim.ID.ToString());
                                notificationData.Add(wasGetDescriptionFromEnumValue(ScriptKeys.POSITION),
                                    prim.Position.ToString());
                                notificationData.Add(wasGetDescriptionFromEnumValue(ScriptKeys.ROTATION),
                                    prim.Rotation.ToString());
                                notificationData.Add(wasGetDescriptionFromEnumValue(ScriptKeys.ENTITY),
                                    prim.PrimData.PCode.ToString());
                                notificationData.Add(wasGetDescriptionFromEnumValue(ScriptKeys.ACTION),
                                    wasGetDescriptionFromEnumValue(Action.VANISH));
                            }
                            break;
                    }
                    if (NotificationQueue.Count < Configuration.NOTIFICATION_QUEUE_LENGTH)
                    {
                        Parallel.ForEach(
                            o.NotificationDestination.AsParallel()
                                .Where(p => p.Key.Equals(notification))
                                .SelectMany(p => p.Value), p =>
                            {
                                NotificationQueue.Enqueue(new NotificationQueueElement
                                { URL = p, message = wasKeyValueEscape(notificationData) });
                                NotificationsAvailable.Release();
                            });
                    }
                });
            }
        }

        private static void HandleScriptDialog(object sender, ScriptDialogEventArgs e)
        {
            lock (ScriptDialogLock)
            {
                ScriptDialogs.Add(new ScriptDialog
                {
                    Message = e.Message,
                    Agent = new Agent
                    {
                        FirstName = e.FirstName,
                        LastName = e.LastName,
                        UUID = e.OwnerID
                    },
                    Channel = e.Channel,
                    Name = e.ObjectName,
                    Item = e.ObjectID,
                    Button = e.ButtonLabels
                });
            }
            CorrodeThreadPool[CorrodeThreadType.NOTIFICATION].Spawn(
                () => SendNotification(Notifications.NOTIFICATION_SCRIPT_DIALOG, e),
                Configuration.MAXIMUM_NOTIFICATION_THREADS);
        }

        private static void HandleChatFromSimulator(object sender, ChatEventArgs e)
        {
            // Ignore chat with no message (ie: start / stop typing)
            if (string.IsNullOrEmpty(e.Message)) return;
            switch (e.Type)
            {
                case ChatType.OwnerSay:
                    // If RLV is enabled, process RLV and terminate.
                    if (EnableRLV && e.Message.StartsWith(RLV_CONSTANTS.COMMAND_OPERATOR))
                    {
                        // Send RLV message notifications.
                        CorrodeThreadPool[CorrodeThreadType.NOTIFICATION].Spawn(
                            () => SendNotification(Notifications.NOTIFICATION_RLV_MESSAGE, e),
                            Configuration.MAXIMUM_NOTIFICATION_THREADS);
                        CorrodeThreadPool[CorrodeThreadType.RLV].Spawn(
                            () => HandleRLVBehaviour(e.Message.Substring(1, e.Message.Length - 1), e.SourceID),
                            Configuration.MAXIMUM_RLV_THREADS);
                        break;
                    }
                    // Otherwise, send llOwnerSay notifications.
                    CorrodeThreadPool[CorrodeThreadType.NOTIFICATION].Spawn(
                        () => SendNotification(Notifications.NOTIFICATION_OWNER_SAY, e),
                        Configuration.MAXIMUM_NOTIFICATION_THREADS);
                    break;
                case ChatType.Debug:
                    // Send debug notifications.
                    CorrodeThreadPool[CorrodeThreadType.NOTIFICATION].Spawn(
                        () => SendNotification(Notifications.NOTIFICATION_DEBUG_MESSAGE, e),
                        Configuration.MAXIMUM_NOTIFICATION_THREADS);
                    break;
                case ChatType.Normal:
                case ChatType.Shout:
                case ChatType.Whisper:
                    // Send chat notifications.
                    CorrodeThreadPool[CorrodeThreadType.NOTIFICATION].Spawn(
                        () => SendNotification(Notifications.NOTIFICATION_LOCAL_CHAT, e),
                        Configuration.MAXIMUM_NOTIFICATION_THREADS);
                    // Log local chat,
                    if (Configuration.LOCAL_MESSAGE_LOG_ENABLED)
                    {
                        List<string> fullName =
                            new List<string>(
                                GetAvatarNames(e.FromName));
                        try
                        {
                            lock (LocalLogFileLock)
                            {
                                using (
                                    StreamWriter logWriter =
                                        File.AppendText(
                                            wasPathCombine(Configuration.LOCAL_MESSAGE_LOG_DIRECTORY,
                                                Client.Network.CurrentSim.Name) +
                                            "." +
                                            CORRADE_CONSTANTS.LOG_FILE_EXTENSION))
                                {
                                    logWriter.WriteLine("[{0}] {1} {2} ({3}) : {4}",
                                        DateTime.Now.ToString(CORRADE_CONSTANTS.DATE_TIME_STAMP,
                                            DateTimeFormatInfo.InvariantInfo), fullName.First(), fullName.Last(),
                                        Enum.GetName(typeof (ChatType), e.Type),
                                        e.Message);
                                    //logWriter.Flush();
                                    //logWriter.Close();
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            // or fail and append the fail message.
                            Feedback(
                                wasGetDescriptionFromEnumValue(
                                    ConsoleError.COULD_NOT_WRITE_TO_LOCAL_MESSAGE_LOG_FILE),
                                ex.Message);
                        }
                    }
                    break;
                case (ChatType) 9:
                    // Send llRegionSayTo notification in case we do not have a command.
                    if (!IsCorrodeCommand(e.Message))
                    {
                        // Send chat notifications.
                        CorrodeThreadPool[CorrodeThreadType.NOTIFICATION].Spawn(
                            () => SendNotification(Notifications.NOTIFICATION_REGION_SAY_TO, e),
                            Configuration.MAXIMUM_NOTIFICATION_THREADS);
                        break;
                    }
                    CorrodeThreadPool[CorrodeThreadType.COMMAND].Spawn(
                        () => HandleCorrodeCommand(e.Message, e.FromName, e.OwnerID.ToString()),
                        Configuration.MAXIMUM_COMMAND_THREADS);
                    break;
            }
        }

        private static void HandleAlertMessage(object sender, AlertMessageEventArgs e)
        {
            CorrodeThreadPool[CorrodeThreadType.NOTIFICATION].Spawn(
                () => SendNotification(Notifications.NOTIFICATION_ALERT_MESSAGE, e),
                Configuration.MAXIMUM_NOTIFICATION_THREADS);
        }

        private static void HandleInventoryObjectOffered(object sender, InventoryObjectOfferedEventArgs e)
        {
            // Send notification
            CorrodeThreadPool[CorrodeThreadType.NOTIFICATION].Spawn(
                () => SendNotification(Notifications.NOTIFICATION_INVENTORY, e),
                Configuration.MAXIMUM_NOTIFICATION_THREADS);

            // Accept anything from master avatars.
            if (
                Configuration.MASTERS.AsParallel().Select(
                    o => string.Format(CultureInfo.InvariantCulture, "{0} {1}", o.FirstName, o.LastName))
                    .Any(p => p.Equals(e.Offer.FromAgentName, StringComparison.OrdinalIgnoreCase)))
            {
                e.Accept = true;
                return;
            }

            // We need to block until we get a reply from a script.
            ManualResetEvent wait = new ManualResetEvent(false);
            // Add the inventory offer to the list of inventory items.
            lock (InventoryOffersLock)
            {
                InventoryOffers.Add(e, wait);
                SaveInventoryOffersState.Invoke();
            }

            UpdateInventoryRecursive.Invoke(
                Client.Inventory.Store.Items[Client.Inventory.FindFolderForType(e.AssetType)].Data as
                    InventoryFolder);

            // Find the item in the inventory.
            InventoryBase inventoryBaseItem;
            lock (ClientInstanceInventoryLock)
            {
                inventoryBaseItem = FindInventory<InventoryBase>(Client.Inventory.Store.RootNode, ((Func<string>) (() =>
                {
                    GroupCollection groups =
                        CORRADE_CONSTANTS.InventoryOfferObjectNameRegEx.Match(e.Offer.Message).Groups;
                    return groups.Count > 0 ? groups[1].Value : e.Offer.Message;
                }))()
                    ).FirstOrDefault();
            }

            if (inventoryBaseItem != null)
            {
                // Assume we do not want the item.
                lock (ClientInstanceInventoryLock)
                {
                    Client.Inventory.Move(
                        inventoryBaseItem,
                        Client.Inventory.Store.Items[Client.Inventory.FindFolderForType(FolderType.Trash)].Data as
                            InventoryFolder);
                }
            }

            // Wait for a reply.
            wait.WaitOne(Timeout.Infinite);

            if (!e.Accept) return;

            // If no folder UUID was specified, move it to the default folder for the asset type.
            if (inventoryBaseItem != null)
            {
                switch (!e.FolderID.Equals(UUID.Zero))
                {
                    case true:
                        InventoryBase inventoryBaseFolder;
                        lock (ClientInstanceInventoryLock)
                        {
                            // Locate the folder and move.
                            inventoryBaseFolder =
                                FindInventory<InventoryBase>(Client.Inventory.Store.RootNode, e.FolderID
                                    ).FirstOrDefault();
                            if (inventoryBaseFolder != null)
                            {
                                Client.Inventory.Move(inventoryBaseItem, inventoryBaseFolder as InventoryFolder);
                            }
                        }
                        if (inventoryBaseFolder != null)
                        {
                            UpdateInventoryRecursive.Invoke(inventoryBaseFolder as InventoryFolder);
                        }
                        break;
                    default:
                        lock (ClientInstanceInventoryLock)
                        {
                            Client.Inventory.Move(
                                inventoryBaseItem,
                                Client.Inventory.Store.Items[Client.Inventory.FindFolderForType(e.AssetType)].Data as
                                    InventoryFolder);
                        }
                        UpdateInventoryRecursive.Invoke(
                            Client.Inventory.Store.Items[Client.Inventory.FindFolderForType(e.AssetType)].Data as
                                InventoryFolder);
                        break;
                }
            }
        }

        private static void HandleScriptQuestion(object sender, ScriptQuestionEventArgs e)
        {
            List<string> owner = new List<string>(GetAvatarNames(e.ObjectOwnerName));
            UUID ownerUUID = UUID.Zero;
            // Don't add permission requests from unknown agents.
            if (
                !AgentNameToUUID(owner.First(), owner.Last(), Configuration.SERVICES_TIMEOUT,
                    Configuration.DATA_TIMEOUT,
                    ref ownerUUID))
            {
                return;
            }

            lock (ScriptPermissionRequestLock)
            {
                ScriptPermissionRequests.Add(new ScriptPermissionRequest
                {
                    Name = e.ObjectName,
                    Agent = new Agent
                    {
                        FirstName = owner.First(),
                        LastName = owner.Last(),
                        UUID = ownerUUID
                    },
                    Item = e.ItemID,
                    Task = e.TaskID,
                    Permission = e.Questions,
                    Region = e.Simulator.Name
                });
            }
            CorrodeThreadPool[CorrodeThreadType.NOTIFICATION].Spawn(
                () => SendNotification(Notifications.NOTIFICATION_SCRIPT_PERMISSION, e),
                Configuration.MAXIMUM_NOTIFICATION_THREADS);

            // Handle RLV: acceptpermission
            lock (RLVRulesLock)
            {
                if (
                    !RLVRules.AsParallel()
                        .Any(o => o.Behaviour.Equals(wasGetDescriptionFromEnumValue(RLVBehaviour.ACCEPTPERMISSION))))
                    return;
                lock (ClientInstanceSelfLock)
                {
                    Client.Self.ScriptQuestionReply(e.Simulator, e.ItemID, e.TaskID, e.Questions);
                }
            }
        }

        private static void HandleDisconnected(object sender, DisconnectedEventArgs e)
        {
            Feedback(wasGetDescriptionFromEnumValue(ConsoleError.DISCONNECTED));
            ConnectionSemaphores['l'].Set();
        }

        private static void HandleEventQueueRunning(object sender, EventQueueRunningEventArgs e)
        {
            Feedback(wasGetDescriptionFromEnumValue(ConsoleError.EVENT_QUEUE_STARTED));
        }

        private static void HandleSimulatorConnected(object sender, SimConnectedEventArgs e)
        {
            Feedback(wasGetDescriptionFromEnumValue(ConsoleError.SIMULATOR_CONNECTED));
        }

        private static void HandleSimulatorDisconnected(object sender, SimDisconnectedEventArgs e)
        {
            // if any simulators are still connected, we are not disconnected
            if (Client.Network.Simulators.Any()) return;
            Feedback(wasGetDescriptionFromEnumValue(ConsoleError.ALL_SIMULATORS_DISCONNECTED));
            ConnectionSemaphores['s'].Set();
        }

        private static void HandleLoginProgress(object sender, LoginProgressEventArgs e)
        {
            switch (e.Status)
            {
                case LoginStatus.Success:
                    Feedback(wasGetDescriptionFromEnumValue(ConsoleError.LOGIN_SUCCEEDED));
                    // Start inventory update thread.
                    ManualResetEvent InventoryLoadedEvent = new ManualResetEvent(false);
                    new Thread(() =>
                    {
                        lock (ClientInstanceInventoryLock)
                        {
                            // First load the caches.
                            LoadInventoryCache.Invoke();
                        }
                        // Update the inventory.
                        UpdateInventoryRecursive.Invoke(Client.Inventory.Store.RootFolder);
                        lock (ClientInstanceInventoryLock)
                        {
                            // Now save the caches.
                            SaveInventoryCache.Invoke();
                        }
                        // Signal completion.
                        InventoryLoadedEvent.Set();
                    }) {IsBackground = true, Priority = ThreadPriority.BelowNormal}.Start();
                    // Set current group to land group.
                    new Thread(() =>
                    {
                        if (!Configuration.AUTO_ACTIVATE_GROUP) return;
                        ActivateCurrentLandGroupTimer.Change(Configuration.ACTIVATE_DELAY, 0);
                    }) {IsBackground = true, Priority = ThreadPriority.BelowNormal}.Start();
                    // Retrieve instant messages.
                    new Thread(() =>
                    {
                        // Wait till the inventory has loaded to retrieve messages since 
                        // instant messages may contain commands that must be replayed.
                        InventoryLoadedEvent.WaitOne(Timeout.Infinite, false);
                        lock (ClientInstanceSelfLock)
                        {
                            Client.Self.RetrieveInstantMessages();
                        }
                    }) {IsBackground = true, Priority = ThreadPriority.BelowNormal}.Start();
                    // Set the camera on the avatar.
                    Client.Self.Movement.Camera.LookAt(Client.Self.SimPosition, Client.Self.SimPosition);
                    break;
                case LoginStatus.Failed:
                    string reason;
                    switch (e.FailReason)
                    {
                        case "god":
                            reason = "Grid is down";
                            break;
                        case "key":
                            reason = "Bad username or password";
                            break;
                        case "presence":
                            reason = "Server is still logging us out";
                            break;
                        case "disabled":
                            reason = "This account has been banned";
                            break;
                        case "timed out":
                            reason = "Login request has timed out";
                            break;
                        case "no connection":
                            reason = "Cannot obtain connection for login";
                            break;
                        case "bad response":
                            reason = "Login request returned bad response";
                            break;
                        default:
                            reason = "Unknown error";
                            break;
                    }
                    Feedback(wasGetDescriptionFromEnumValue(ConsoleError.LOGIN_FAILED), reason);
                    ConnectionSemaphores['l'].Set();
                    break;
            }
        }

        private static void HandleFriendOnlineStatus(object sender, FriendInfoEventArgs e)
        {
            CorrodeThreadPool[CorrodeThreadType.NOTIFICATION].Spawn(
                () => SendNotification(Notifications.NOTIFICATION_FRIENDSHIP, e),
                Configuration.MAXIMUM_NOTIFICATION_THREADS);
        }

        private static void HandleFriendRightsUpdate(object sender, FriendInfoEventArgs e)
        {
            CorrodeThreadPool[CorrodeThreadType.NOTIFICATION].Spawn(
                () => SendNotification(Notifications.NOTIFICATION_FRIENDSHIP, e),
                Configuration.MAXIMUM_NOTIFICATION_THREADS);
        }

        private static void HandleFriendShipResponse(object sender, FriendshipResponseEventArgs e)
        {
            CorrodeThreadPool[CorrodeThreadType.NOTIFICATION].Spawn(
                () => SendNotification(Notifications.NOTIFICATION_FRIENDSHIP, e),
                Configuration.MAXIMUM_NOTIFICATION_THREADS);
        }

        private static void HandleFriendshipOffered(object sender, FriendshipOfferedEventArgs e)
        {
            // Send friendship notifications
            CorrodeThreadPool[CorrodeThreadType.NOTIFICATION].Spawn(
                () => SendNotification(Notifications.NOTIFICATION_FRIENDSHIP, e),
                Configuration.MAXIMUM_NOTIFICATION_THREADS);
        }

        private static void HandleTeleportProgress(object sender, TeleportEventArgs e)
        {
            switch (e.Status)
            {
                case TeleportStatus.Finished:
                    Feedback(wasGetDescriptionFromEnumValue(ConsoleError.TELEPORT_SUCCEEDED));
                    // Set current group to land group.
                    new Thread(() =>
                    {
                        if (!Configuration.AUTO_ACTIVATE_GROUP) return;
                        ActivateCurrentLandGroupTimer.Change(Configuration.ACTIVATE_DELAY, 0);
                    }) {IsBackground = true, Priority = ThreadPriority.BelowNormal}.Start();
                    // Set the camera on the avatar.
                    Client.Self.Movement.Camera.LookAt(
                        Client.Self.SimPosition,
                        Client.Self.SimPosition
                        );
                    break;
                case TeleportStatus.Failed:
                    Feedback(wasGetDescriptionFromEnumValue(ConsoleError.TELEPORT_FAILED));
                    break;
            }
        }

        private static void HandleSelfIM(object sender, InstantMessageEventArgs args)
        {
            List<string> fullName =
                new List<string>(
                    GetAvatarNames(args.IM.FromAgentName));
            // Process dialog messages.
            switch (args.IM.Dialog)
            {
                // Send typing notification.
                case InstantMessageDialog.StartTyping:
                case InstantMessageDialog.StopTyping:
                    CorrodeThreadPool[CorrodeThreadType.NOTIFICATION].Spawn(
                        () => SendNotification(Notifications.NOTIFICATION_TYPING, args),
                        Configuration.MAXIMUM_NOTIFICATION_THREADS);
                    return;
                case InstantMessageDialog.FriendshipOffered:
                    // Accept friendships only from masters (for the time being)
                    if (
                        !Configuration.MASTERS.AsParallel().Any(
                            o =>
                                o.FirstName.Equals(fullName.First(), StringComparison.OrdinalIgnoreCase) &&
                                o.LastName.Equals(fullName.Last(), StringComparison.OrdinalIgnoreCase))) return;
                    Feedback(wasGetDescriptionFromEnumValue(ConsoleError.ACCEPTED_FRIENDSHIP), args.IM.FromAgentName);
                    Client.Friends.AcceptFriendship(args.IM.FromAgentID, args.IM.IMSessionID);
                    break;
                case InstantMessageDialog.InventoryAccepted:
                case InstantMessageDialog.InventoryDeclined:
                case InstantMessageDialog.TaskInventoryOffered:
                case InstantMessageDialog.InventoryOffered:
                    CorrodeThreadPool[CorrodeThreadType.NOTIFICATION].Spawn(
                        () => SendNotification(Notifications.NOTIFICATION_INVENTORY, args),
                        Configuration.MAXIMUM_NOTIFICATION_THREADS);
                    return;
                case InstantMessageDialog.MessageBox:
                    // Not used.
                    return;
                case InstantMessageDialog.RequestTeleport:
                    // Handle RLV: acccepttp
                    lock (RLVRulesLock)
                    {
                        if (
                            RLVRules.AsParallel()
                                .Any(o => o.Behaviour.Equals(wasGetDescriptionFromEnumValue(RLVBehaviour.ACCEPTTP))))
                        {
                            lock (ClientInstanceSelfLock)
                            {
                                Client.Self.TeleportLureRespond(args.IM.FromAgentID, args.IM.IMSessionID, true);
                            }
                            return;
                        }
                    }
                    // Handle Corrode
                    List<string> teleportLureName =
                        new List<string>(
                            GetAvatarNames(args.IM.FromAgentName));
                    // Store teleport lure.
                    lock (TeleportLureLock)
                    {
                        TeleportLures.Add(new TeleportLure
                        {
                            Agent = new Agent
                            {
                                FirstName = teleportLureName.First(),
                                LastName = teleportLureName.Last(),
                                UUID = args.IM.FromAgentID
                            },
                            Session = args.IM.IMSessionID
                        });
                    }
                    // Send teleport lure notification.
                    CorrodeThreadPool[CorrodeThreadType.NOTIFICATION].Spawn(
                        () => SendNotification(Notifications.NOTIFICATION_TELEPORT_LURE, args),
                        Configuration.MAXIMUM_NOTIFICATION_THREADS);
                    // If we got a teleport request from a master, then accept it (for the moment).
                    lock (ClientInstanceInventoryLock)
                    {
                        if (!Configuration.MASTERS.AsParallel().Select(
                            o =>
                                string.Format(CultureInfo.InvariantCulture, "{0} {1}", o.FirstName, o.LastName))
                            .
                            Any(p => p.Equals(args.IM.FromAgentName, StringComparison.OrdinalIgnoreCase))) return;
                    }
                    lock (ClientInstanceSelfLock)
                    {
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
                        Client.Self.TeleportLureRespond(args.IM.FromAgentID, args.IM.IMSessionID, true);
                    }
                    return;
                // Group invitations received
                case InstantMessageDialog.GroupInvitation:
                    OpenMetaverse.Group inviteGroup = new OpenMetaverse.Group();
                    if (!RequestGroup(args.IM.FromAgentID, Configuration.SERVICES_TIMEOUT, ref inviteGroup)) return;

                    List<string> groupInviteName =
                        new List<string>(
                            GetAvatarNames(args.IM.FromAgentName));
                    UUID inviteGroupAgent = UUID.Zero;
                    if (
                        !AgentNameToUUID(groupInviteName.First(), groupInviteName.Last(),
                            Configuration.SERVICES_TIMEOUT,
                            Configuration.DATA_TIMEOUT,
                            ref inviteGroupAgent)) return;

                    // Add the group invite - have to track them manually.
                    lock (GroupInviteLock)
                    {
                        GroupInvites.Add(new GroupInvite
                        {
                            Agent = new Agent
                            {
                                FirstName = groupInviteName.First(),
                                LastName = groupInviteName.Last(),
                                UUID = inviteGroupAgent
                            },
                            Group = inviteGroup.Name,
                            Session = args.IM.IMSessionID,
                            Fee = inviteGroup.MembershipFee
                        });
                    }
                    // Send group invitation notification.
                    CorrodeThreadPool[CorrodeThreadType.NOTIFICATION].Spawn(
                        () => SendNotification(Notifications.NOTIFICATION_GROUP_INVITE, args),
                        Configuration.MAXIMUM_NOTIFICATION_THREADS);
                    // If a master sends it, then accept.
                    if (
                        !Configuration.MASTERS.AsParallel().Select(
                            o =>
                                string.Format(CultureInfo.InvariantCulture, "{0}.{1}", o.FirstName, o.LastName))
                            .
                            Any(p => p.Equals(args.IM.FromAgentName, StringComparison.OrdinalIgnoreCase)))
                        return;

                    Client.Self.GroupInviteRespond(inviteGroup.ID, args.IM.IMSessionID, true);
                    return;
                // Group notice inventory accepted, declined or notice received.
                case InstantMessageDialog.GroupNoticeInventoryAccepted:
                case InstantMessageDialog.GroupNoticeInventoryDeclined:
                case InstantMessageDialog.GroupNotice:
                    CorrodeThreadPool[CorrodeThreadType.NOTIFICATION].Spawn(
                        () => SendNotification(Notifications.NOTIFICATION_GROUP_NOTICE, args),
                        Configuration.MAXIMUM_NOTIFICATION_THREADS);
                    return;
                case InstantMessageDialog.SessionSend:
                case InstantMessageDialog.MessageFromAgent:
                    // Check if this is a group message.
                    // Note that this is a lousy way of doing it but libomv does not properly set the GroupIM field
                    // such that the only way to determine if we have a group message is to check that the UUID
                    // of the session is actually the UUID of a current group. Furthermore, what's worse is that 
                    // group mesages can appear both through SessionSend and from MessageFromAgent. Hence the problem.
                    HashSet<UUID> currentGroups = new HashSet<UUID>();
                    if (
                        !GetCurrentGroups(Configuration.SERVICES_TIMEOUT, Configuration.DATA_TIMEOUT,
                            ref currentGroups))
                        return;

                    if (currentGroups.AsParallel().Any(o => o.Equals(args.IM.IMSessionID)))
                    {
                        Group messageGroup =
                            Configuration.GROUPS.AsParallel().FirstOrDefault(p => p.UUID.Equals(args.IM.IMSessionID));
                        if (!messageGroup.Equals(default(Group)))
                        {
                            // Send group notice notifications.
                            CorrodeThreadPool[CorrodeThreadType.NOTIFICATION].Spawn(
                                () => SendNotification(Notifications.NOTIFICATION_GROUP_MESSAGE, args),
                                Configuration.MAXIMUM_NOTIFICATION_THREADS);
                            // Log group messages
                            Parallel.ForEach(
                                Configuration.GROUPS.AsParallel().Where(
                                    o =>
                                        o.Name.Equals(messageGroup.Name, StringComparison.Ordinal) &&
                                        o.ChatLogEnabled),
                                o =>
                                {
                                    // Attempt to write to log file,
                                    try
                                    {
                                        lock (GroupLogFileLock)
                                        {
                                            using (StreamWriter logWriter = File.AppendText(o.ChatLog))
                                            {
                                                logWriter.WriteLine("[{0}] {1} {2} : {3}",
                                                    DateTime.Now.ToString(CORRADE_CONSTANTS.DATE_TIME_STAMP,
                                                        DateTimeFormatInfo.InvariantInfo), fullName.First(),
                                                    fullName.Last(),
                                                    args.IM.Message);
                                                //logWriter.Flush();
                                                //logWriter.Close();
                                            }
                                        }
                                    }
                                    catch (Exception ex)
                                    {
                                        // or fail and append the fail message.
                                        Feedback(
                                            wasGetDescriptionFromEnumValue(
                                                ConsoleError.COULD_NOT_WRITE_TO_GROUP_CHAT_LOG_FILE),
                                            ex.Message);
                                    }
                                });
                        }
                        return;
                    }
                    // Check if this is an instant message.
                    if (args.IM.ToAgentID.Equals(Client.Self.AgentID))
                    {
                        CorrodeThreadPool[CorrodeThreadType.NOTIFICATION].Spawn(
                            () => SendNotification(Notifications.NOTIFICATION_INSTANT_MESSAGE, args),
                            Configuration.MAXIMUM_NOTIFICATION_THREADS);

                        // Check if we were ejected.
                        UUID groupUUID = UUID.Zero;
                        if (
                            GroupNameToUUID(
                                CORRADE_CONSTANTS.EjectedFromGroupRegEx.Match(args.IM.Message).Groups[1].Value,
                                Configuration.SERVICES_TIMEOUT, Configuration.DATA_TIMEOUT, ref groupUUID))
                        {
                            // Remove the group from the cache.
                            lock (Cache.Locks.CurrentGroupsCacheLock)
                            {
                                if (Cache.CurrentGroupsCache.Contains(groupUUID))
                                {
                                    Cache.CurrentGroupsCache.Remove(groupUUID);
                                }
                            }
                        }

                        // Log instant messages,
                        if (Configuration.INSTANT_MESSAGE_LOG_ENABLED)
                        {
                            try
                            {
                                lock (InstantMessageLogFileLock)
                                {
                                    using (
                                        StreamWriter logWriter =
                                            File.AppendText(
                                                wasPathCombine(Configuration.INSTANT_MESSAGE_LOG_DIRECTORY,
                                                    args.IM.FromAgentName) +
                                                "." + CORRADE_CONSTANTS.LOG_FILE_EXTENSION))
                                    {
                                        logWriter.WriteLine("[{0}] {1} {2} : {3}",
                                            DateTime.Now.ToString(CORRADE_CONSTANTS.DATE_TIME_STAMP,
                                                DateTimeFormatInfo.InvariantInfo), fullName.First(), fullName.Last(),
                                            args.IM.Message);
                                        //logWriter.Flush();
                                        //logWriter.Close();
                                    }
                                }
                            }
                            catch (Exception ex)
                            {
                                // or fail and append the fail message.
                                Feedback(
                                    wasGetDescriptionFromEnumValue(
                                        ConsoleError.COULD_NOT_WRITE_TO_INSTANT_MESSAGE_LOG_FILE),
                                    ex.Message);
                            }
                        }
                        return;
                    }
                    // Check if this is a region message.
                    if (args.IM.IMSessionID.Equals(UUID.Zero))
                    {
                        CorrodeThreadPool[CorrodeThreadType.NOTIFICATION].Spawn(
                            () => SendNotification(Notifications.NOTIFICATION_REGION_MESSAGE, args),
                            Configuration.MAXIMUM_NOTIFICATION_THREADS);
                        // Log region messages,
                        if (Configuration.REGION_MESSAGE_LOG_ENABLED)
                        {
                            try
                            {
                                lock (RegionLogFileLock)
                                {
                                    using (
                                        StreamWriter logWriter =
                                            File.AppendText(
                                                wasPathCombine(Configuration.REGION_MESSAGE_LOG_DIRECTORY,
                                                    Client.Network.CurrentSim.Name) + "." +
                                                CORRADE_CONSTANTS.LOG_FILE_EXTENSION))
                                    {
                                        logWriter.WriteLine("[{0}] {1} {2} : {3}",
                                            DateTime.Now.ToString(CORRADE_CONSTANTS.DATE_TIME_STAMP,
                                                DateTimeFormatInfo.InvariantInfo), fullName.First(), fullName.Last(),
                                            args.IM.Message);
                                        //logWriter.Flush();
                                        //logWriter.Close();
                                    }
                                }
                            }
                            catch (Exception ex)
                            {
                                // or fail and append the fail message.
                                Feedback(
                                    wasGetDescriptionFromEnumValue(
                                        ConsoleError.COULD_NOT_WRITE_TO_REGION_MESSAGE_LOG_FILE),
                                    ex.Message);
                            }
                        }
                        return;
                    }
                    break;
            }

            // Where are now in a region where the message is an IM sent by an object.
            // Check if this is not a Corrode command and send an object IM notification.
            if (!IsCorrodeCommand(args.IM.Message))
            {
                CorrodeThreadPool[CorrodeThreadType.NOTIFICATION].Spawn(
                    () => SendNotification(Notifications.NOTIFICATION_OBJECT_INSTANT_MESSAGE, args),
                    Configuration.MAXIMUM_NOTIFICATION_THREADS);
                return;
            }

            // Otherwise process the command.
            CorrodeThreadPool[CorrodeThreadType.COMMAND].Spawn(
                () => HandleCorrodeCommand(args.IM.Message, args.IM.FromAgentName, args.IM.FromAgentID.ToString()),
                Configuration.MAXIMUM_COMMAND_THREADS);
        }



        private static Dictionary<string, string> HandleCorrodeCommand(string message, string sender, string identifier)
        {
            // Now we can start processing commands.
            // Get group and password.
            string group =
                wasInput(wasKeyValueGet(wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.GROUP)), message));
            // Bail if no group set.
            if (string.IsNullOrEmpty(group)) return null;
            // If an UUID was sent, try to resolve to a name and bail if not.
            UUID groupUUID;
            if (UUID.TryParse(group, out groupUUID))
            {
                // First, trust the user to have properly configured the UUID for the group.
                Group configGroup = Configuration.GROUPS.AsParallel().FirstOrDefault(o => o.UUID.Equals(groupUUID));
                switch (!configGroup.Equals(default(Group)))
                {
                    case true:
                        // If they have, then just grab the group name.
                        group = configGroup.Name;
                        break;
                    default:
                        // Otherwise, attempt to resolve the group name.
                        if (
                            !GroupUUIDToName(groupUUID, Configuration.SERVICES_TIMEOUT, Configuration.DATA_TIMEOUT,
                                ref group)) return null;

                        break;
                }
            }
            // Set literal group.
            message = wasKeyValueSet(wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.GROUP)),
                wasOutput(group), message);
            // Get password.
            string password =
                wasInput(wasKeyValueGet(wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.PASSWORD)), message));
            // Bail if no password set.
            if (string.IsNullOrEmpty(password)) return null;
            // Authenticate the request against the group password.
            if (!Authenticate(group, password))
            {
                Feedback(group, wasGetDescriptionFromEnumValue(ConsoleError.ACCESS_DENIED));
                return null;
            }
            // Censor password.
            message = wasKeyValueSet(wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.PASSWORD)),
                CORRADE_CONSTANTS.PASSWORD_CENSOR, message);
            /*
             * OpenSim sends the primitive UUID through args.IM.FromAgentID while Second Life properly sends 
             * the agent UUID - which just shows how crap OpenSim really is. This tries to resolve 
             * args.IM.FromAgentID to a name, which is what Second Life does, otherwise it just sets the name 
             * to the name of the primitive sending the message.
             */
            bool isSecondLife;
            lock (ClientInstanceNetworkLock)
            {
                isSecondLife = Client.Network.CurrentSim.SimVersion.Contains(LINDEN_CONSTANTS.GRID.SECOND_LIFE);
            }
            if (isSecondLife)
            {
                UUID fromAgentID;
                if (UUID.TryParse(identifier, out fromAgentID))
                {
                    if (
                        !AgentUUIDToName(fromAgentID, Configuration.SERVICES_TIMEOUT,
                            ref sender))
                    {
                        Feedback(wasGetDescriptionFromEnumValue(ConsoleError.AGENT_NOT_FOUND),
                            fromAgentID.ToString());
                        return null;
                    }
                }
            }


            // Log the command.
            Feedback(string.Format(CultureInfo.InvariantCulture, "{0} ({1}) : {2}", sender,
                identifier,
                message));

            // Initialize workers for the group if they are not set.
            lock (GroupWorkersLock)
            {
                if (!GroupWorkers.Contains(group))
                {
                    GroupWorkers.Add(group, 0u);
                }
            }

            // Check if the workers have not been exceeded.
            lock (GroupWorkersLock)
            {
                if ((uint) GroupWorkers[group] >
                    Configuration.GROUPS.AsParallel().FirstOrDefault(
                        o => o.Name.Equals(group, StringComparison.InvariantCultureIgnoreCase)).Workers)
                {
                    // And refuse to proceed if they have.
                    Feedback(wasGetDescriptionFromEnumValue(ConsoleError.WORKERS_EXCEEDED),
                        group);
                    return null;
                }
            }

            // Increment the group workers.
            lock (GroupWorkersLock)
            {
                GroupWorkers[group] = ((uint) GroupWorkers[group]) + 1;
            }
            // Perform the command.
            Dictionary<string, string> result = ProcessCommand(message);
            // Decrement the group workers.
            lock (GroupWorkersLock)
            {
                GroupWorkers[group] = ((uint) GroupWorkers[group]) - 1;
            }
            // do not send a callback if the callback queue is saturated
            if (CallbackQueue.Count >= Configuration.CALLBACK_QUEUE_LENGTH) return result;
            // send callback if registered
            string url = wasInput(wasKeyValueGet(wasOutput(
                wasGetDescriptionFromEnumValue(ScriptKeys.CALLBACK)), message));
            // if no url was provided, do not send the callback
            if (string.IsNullOrEmpty(url)) return result;

            CallbackQueue.Enqueue(new CallbackQueueElement { URL = url, message = wasKeyValueEscape(result) });
            CallbacksAvailable.Release();

            return result;
        }

        /// <summary>
        ///     Gets the values from structures as strings.
        /// </summary>
        /// <typeparam name="T">the type of the structure</typeparam>
        /// <param name="structure">the structure</param>
        /// <param name="query">a CSV list of fields or properties to get</param>
        /// <returns>value strings</returns>
        private static IEnumerable<string> GetStructuredData<T>(T structure, string query)
        {
            HashSet<string[]> result = new HashSet<string[]>();
            if (structure.Equals(default(T)))
                return result.SelectMany(o => o);
            object LockObject = new object();
            Parallel.ForEach(wasCSVToEnumerable(query), name =>
            {
                KeyValuePair<FieldInfo, object> fi = wasGetFields(structure, structure.GetType().Name)
                    .AsParallel().FirstOrDefault(o => o.Key.Name.Equals(name, StringComparison.Ordinal));

                lock (LockObject)
                {
                    List<string> data = new List<string> {name};
                    data.AddRange(wasGetInfo(fi.Key, fi.Value));
                    if (data.Count >= 2)
                    {
                        result.Add(data.ToArray());
                    }
                }

                KeyValuePair<PropertyInfo, object> pi =
                    wasGetProperties(structure, structure.GetType().Name)
                        .AsParallel().FirstOrDefault(
                            o => o.Key.Name.Equals(name, StringComparison.Ordinal));
                lock (LockObject)
                {
                    List<string> data = new List<string> {name};
                    data.AddRange(wasGetInfo(pi.Key, pi.Value));
                    if (data.Count >= 2)
                    {
                        result.Add(data.ToArray());
                    }
                }
            });
            return result.SelectMany(o => o);
        }

        /// <summary>
        ///     Takes as input a CSV data values and sets the corresponding
        ///     structure's fields or properties from the CSV data.
        /// </summary>
        /// <typeparam name="T">the type of the structure</typeparam>
        /// <param name="data">a CSV string</param>
        /// <param name="structure">the structure to set the fields and properties for</param>
        private static void wasCSVToStructure<T>(string data, ref T structure)
        {
            foreach (
                KeyValuePair<string, string> match in
                    wasCSVToEnumerable(data).AsParallel().Select((o, p) => new {o, p})
                        .GroupBy(q => q.p/2, q => q.o)
                        .Select(o => o.ToList())
                        .TakeWhile(o => o.Count%2 == 0)
                        .ToDictionary(o => o.First(), p => p.Last()))
            {
                KeyValuePair<string, string> localMatch = match;
                KeyValuePair<FieldInfo, object> fi =
                    wasGetFields(structure, structure.GetType().Name)
                        .AsParallel().FirstOrDefault(
                            o =>
                                o.Key.Name.Equals(localMatch.Key,
                                    StringComparison.Ordinal));

                wasSetInfo(fi.Key, fi.Value, match.Value, ref structure);

                KeyValuePair<PropertyInfo, object> pi =
                    wasGetProperties(structure, structure.GetType().Name)
                        .AsParallel().FirstOrDefault(
                            o =>
                                o.Key.Name.Equals(localMatch.Key,
                                    StringComparison.Ordinal));

                wasSetInfo(pi.Key, pi.Value, match.Value, ref structure);
            }
        }


        /// <summary>
        ///     Sends a post request to an URL with set key-value pairs.
        /// </summary>
        /// <param name="URL">the url to send the message to</param>
        /// <param name="message">key-value pairs to send</param>
        /// <param name="millisecondsTimeout">the time in milliseconds for the request to timeout</param>
        private static void wasPOST(string URL, Dictionary<string, string> message, int millisecondsTimeout)
        {
            HttpWebRequest request = (HttpWebRequest) WebRequest.Create(URL);
            request.Timeout = millisecondsTimeout;
            request.AllowAutoRedirect = true;
            request.AllowWriteStreamBuffering = true;
            request.Pipelined = true;
            request.KeepAlive = true;
            request.ProtocolVersion = HttpVersion.Version11;
            request.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;
            request.Method = WebRequestMethods.Http.Post;
            // set the content type based on chosen output filers
            switch (Configuration.OUTPUT_FILTERS.Last())
            {
                case Filter.RFC1738:
                    request.ContentType = CORRADE_CONSTANTS.CONTENT_TYPE.WWW_FORM_URLENCODED;
                    break;
                default:
                    request.ContentType = CORRADE_CONSTANTS.CONTENT_TYPE.TEXT_PLAIN;
                    break;
            }
            request.UserAgent = string.Format("{0}/{1} ({2})", CORRADE_CONSTANTS.CORRADE,
                CORRADE_CONSTANTS.CORRADE_VERSION,
                CORRADE_CONSTANTS.WIZARDRY_AND_STEAMWORKS_WEBSITE);
            byte[] byteArray =
                Encoding.UTF8.GetBytes(wasKeyValueEncode(message));
            request.ContentLength = byteArray.Length;
            using (Stream dataStream = request.GetRequestStream())
            {
                dataStream.Write(byteArray, 0, byteArray.Length);
                dataStream.Flush();
            }
        }

        private static void HandleTerseObjectUpdate(object sender, TerseObjectUpdateEventArgs e)
        {
            CorrodeThreadPool[CorrodeThreadType.NOTIFICATION].Spawn(
                () => SendNotification(Notifications.NOTIFICATION_TERSE_UPDATES, e),
                Configuration.MAXIMUM_NOTIFICATION_THREADS);
        }

        private static void HandleRadarObjects(object sender, SimChangedEventArgs e)
        {
            lock (RadarObjectsLock)
            {
                if (!RadarObjects.Count.Equals(0))
                {
                    RadarObjects.Clear();
                }
            }
        }

        private static void HandleSimChanged(object sender, SimChangedEventArgs e)
        {
            CorrodeThreadPool[CorrodeThreadType.NOTIFICATION].Spawn(
                () => SendNotification(Notifications.NOTIFICATION_REGION_CROSSED, e),
                Configuration.MAXIMUM_NOTIFICATION_THREADS);
        }

        private static void HandleMoneyBalance(object sender, BalanceEventArgs e)
        {
            CorrodeThreadPool[CorrodeThreadType.NOTIFICATION].Spawn(
                () => SendNotification(Notifications.NOTIFICATION_BALANCE, e),
                Configuration.MAXIMUM_NOTIFICATION_THREADS);
        }

        private static void HandleMoneyBalance(object sender, MoneyBalanceReplyEventArgs e)
        {
            CorrodeThreadPool[CorrodeThreadType.NOTIFICATION].Spawn(
                () => SendNotification(Notifications.NOTIFICATION_ECONOMY, e),
                Configuration.MAXIMUM_NOTIFICATION_THREADS);
        }

        /// <summary>URI unescapes an RFC3986 URI escaped string</summary>
        /// <param name="data">a string to unescape</param>
        /// <returns>the resulting string</returns>
        private static string wasURIUnescapeDataString(string data)
        {
            // Uri.UnescapeDataString can only handle 32766 characters at a time
            return string.Join("", Enumerable.Range(0, (data.Length + 32765)/32766)
                .Select(o => Uri.UnescapeDataString(data.Substring(o*32766, Math.Min(32766, data.Length - (o*32766)))))
                .ToArray());
        }

        /// <summary>RFC3986 URI Escapes a string</summary>
        /// <param name="data">a string to escape</param>
        /// <returns>an RFC3986 escaped string</returns>
        private static string wasURIEscapeDataString(string data)
        {
            // Uri.EscapeDataString can only handle 32766 characters at a time
            return string.Join("", Enumerable.Range(0, (data.Length + 32765)/32766)
                .Select(o => Uri.EscapeDataString(data.Substring(o*32766, Math.Min(32766, data.Length - (o*32766)))))
                .ToArray());
        }

        /// <summary>RFC1738 URL Escapes a string</summary>
        /// <param name="data">a string to escape</param>
        /// <returns>an RFC1738 escaped string</returns>
        private static string wasURLEscapeDataString(string data)
        {
            return HttpUtility.UrlEncode(data);
        }

        /// <summary>RFC1738 URL Unescape a string</summary>
        /// <param name="data">a string to unescape</param>
        /// <returns>an RFC1738 unescaped string</returns>
        private static string wasURLUnescapeDataString(string data)
        {
            return HttpUtility.UrlDecode(data);
        }

        /// <summary>
        ///     Converts a list of string to a comma-separated values string.
        /// </summary>
        /// <param name="l">a list of strings</param>
        /// <returns>a commma-separated list of values</returns>
        /// <remarks>compliant with RFC 4180</remarks>
        public static string wasEnumerableToCSV(IEnumerable<string> l)
        {
            List<string> csv = new List<string>();
            foreach (string s in l)
            {
                List<char> cell = new List<char>();
                foreach (char i in s)
                {
                    cell.Add(i);
                    switch (!i.Equals('"'))
                    {
                        case false:
                            cell.Add(i);
                            break;
                    }
                }
                switch (!cell.Contains('"') && !cell.Contains(' ') && !cell.Contains(',') && !cell.Contains('\r') &&
                        !cell.Contains('\n'))
                {
                    case false:
                        cell.Insert(0, '"');
                        cell.Add('"');
                        break;
                }
                csv.Add(new string(cell.ToArray()));
            }
            return string.Join(",", csv.ToArray());
        }

        /// <summary>
        ///     Converts a comma-separated list of values to a list of strings.
        /// </summary>
        /// <param name="csv">a comma-separated list of values</param>
        /// <returns>a list of strings</returns>
        /// <remarks>compliant with RFC 4180</remarks>
        public static IEnumerable<string> wasCSVToEnumerable(string csv)
        {
            Stack<char> s = new Stack<char>();
            StringBuilder m = new StringBuilder();
            for (int i = 0; i < csv.Length; ++i)
            {
                switch (csv[i])
                {
                    case ',':
                        if (s.Count.Equals(0) || !s.Peek().Equals('"'))
                        {
                            yield return m.ToString();
                            m = new StringBuilder();
                            continue;
                        }
                        m.Append(csv[i]);
                        continue;
                    case '"':
                        if (i + 1 < csv.Length && csv[i] == csv[i + 1])
                        {
                            m.Append(csv[i]);
                            ++i;
                            continue;
                        }
                        if (s.Count.Equals(0) || !s.Peek().Equals(csv[i]))
                        {
                            s.Push(csv[i]);
                            continue;
                        }
                        s.Pop();
                        continue;
                }
                m.Append(csv[i]);
            }

            yield return m.ToString();
        }



        /// <summary>
        ///     Constants for Corrode's integrated chat bot.
        /// </summary>
        private struct AIML_BOT_CONSTANTS
        {
            public const string DIRECTORY = @"AIMLBot";
            public const string BRAIN_FILE = @"AIMLBot.brain";
            public const string BRAIN_SESSION_FILE = @"AIMLbot.session";

            public struct AIML
            {
                public const string DIRECTORY = @"AIML";
            }

            public struct BRAIN
            {
                public const string DIRECTORY = @"brain";
            }

            public struct CONFIG
            {
                public const string DIRECTORY = @"config";
                public const string SETTINGS_FILE = @"Settings.xml";
                public const string NAME = @"NAME";
                public const string AIMLDIRECTORY = @"AIMLDIRECTORY";
                public const string CONFIGDIRECTORY = @"CONFIGDIRECTORY";
                public const string LOGDIRECTORY = @"LOGDIRECTORY";
            }

            public struct LOG
            {
                public const string DIRECTORY = @"logs";
            }
        }
        
        /// <summary>
        ///     Agent structure.
        /// </summary>
        private struct Agent
        {
            [Description("firstname")] public string FirstName;
            [Description("lastname")] public string LastName;
            [Description("uuid")] public UUID UUID;
        }

        /// <summary>
        ///     A structure to track Beam effects.
        /// </summary>
        private struct BeamEffect
        {
            [Description("alpha")] public float Alpha;
            [Description("color")] public Vector3 Color;
            [Description("duration")] public float Duration;
            [Description("effect")] public UUID Effect;
            [Description("offset")] public Vector3d Offset;
            [Description("source")] public UUID Source;
            [Description("target")] public UUID Target;
            [Description("termination")] public DateTime Termination;
        }

        /// <summary>
        ///     Constants used by Corrode.
        /// </summary>
        private struct CORRADE_CONSTANTS
        {
            /// <summary>
            ///     Copyright.
            /// </summary>
            public const string COPYRIGHT = @"(c) Copyright 2013 Wizardry and Steamworks";

            public const string WIZARDRY_AND_STEAMWORKS = @"Wizardry and Steamworks";
            public const string CORRADE = @"Corrode";
            public const string WIZARDRY_AND_STEAMWORKS_WEBSITE = @"http://was.fm";

            /// <summary>
            ///     Censor characters for passwords.
            /// </summary>
            public const string PASSWORD_CENSOR = "***";

            /// <summary>
            ///     Corrode channel sent to the simulator.
            /// </summary>
            public const string CLIENT_CHANNEL = @"[Wizardry and Steamworks]:Corrode";

            public const string CURRENT_OUTFIT_FOLDER_NAME = @"Current Outfit";
            public const string DEFAULT_SERVICE_NAME = @"Corrode";
            public const string LOG_FACILITY = @"Application";
            public const string WEB_REQUEST = @"Web Request";
            public const string CONFIGURATION_FILE = @"Corrode.ini";
            public const string DATE_TIME_STAMP = @"dd-MM-yyyy HH:mm";
            public const string INVENTORY_CACHE_FILE = @"Inventory.cache";
            public const string AGENT_CACHE_FILE = @"Agent.cache";
            public const string GROUP_CACHE_FILE = @"Group.cache";
            public const string PATH_SEPARATOR = @"/";
            public const string ERROR_SEPARATOR = @" : ";
            public const string CACHE_DIRECTORY = @"cache";
            public const string LOG_FILE_EXTENSION = @"log";
            public const string STATE_DIRECTORY = @"state";
            public const string NOTIFICATIONS_STATE_FILE = @"Notifications.state";
            public const string INVENTORY_OFFERS_STATE_FILE = @"InventoryOffers.state";

            public static readonly Regex AvatarFullNameRegex = new Regex(@"^(?<first>.*?)([\s\.]|$)(?<last>.*?)$",
                RegexOptions.Compiled);

            public static readonly Regex OneOrMoRegex = new Regex(@".+?", RegexOptions.Compiled);

            public static readonly Regex InventoryOfferObjectNameRegEx = new Regex(@"^[']{0,1}(.+?)(('\s)|$)",
                RegexOptions.Compiled);

            public static readonly Regex EjectedFromGroupRegEx =
                new Regex(@"You have been ejected from '(.+?)' by .+?\.$", RegexOptions.Compiled);

            /// <summary>
            ///     Conten-types that Corrode can send and receive.
            /// </summary>
            public struct CONTENT_TYPE
            {
                public const string TEXT_PLAIN = @"text/plain";
                public const string WWW_FORM_URLENCODED = @"application/x-www-form-urlencoded";
            }

            public struct PERMISSIONS
            {
                public const string NONE = @"------------------------------";
            }

            /// <summary>
            ///     Corrode version.
            /// </summary>
            public static readonly string CORRADE_VERSION = Assembly.GetEntryAssembly().GetName().Version.ToString();

            /// <summary>
            ///     Corrode compile date.
            /// </summary>
            public static readonly string CORRADE_COMPILE_DATE = new DateTime(2000, 1, 1).Add(new TimeSpan(
                TimeSpan.TicksPerDay*Assembly.GetEntryAssembly().GetName().Version.Build + // days since 1 January 2000
                TimeSpan.TicksPerSecond*2*Assembly.GetEntryAssembly().GetName().Version.Revision)).ToLongDateString();

            /// <summary>
            ///     Corrode Logo.
            /// </summary>
            public static readonly List<string> LOGO = new List<string>
            {
                @"",
                @"       _..--=--..._  ",
                @"    .-'            '-.  .-.  ",
                @"   /.'              '.\/  /  ",
                @"  |=-     Corrode    -=| (  ",
                @"   \'.              .'/\  \  ",
                @"    '-.,_____ _____.-'  '-'  ",
                @"          [_____]=8  ",
                @"               \  ",
                @"                 Good day!  ",
                @"",
                string.Format(CultureInfo.InvariantCulture, "Version: {0}, Compiled: {1}", CORRADE_VERSION,
                    CORRADE_COMPILE_DATE),
                string.Format(CultureInfo.InvariantCulture, "Copyright: {0}", COPYRIGHT)
            };
        }

        /// <summary>
        ///     Corrode's caches.
        /// </summary>
        public struct Cache
        {
            public static HashSet<Agents> AgentCache = new HashSet<Agents>();
            public static HashSet<Groups> GroupCache = new HashSet<Groups>();
            public static HashSet<UUID> CurrentGroupsCache = new HashSet<UUID>();

            internal static void Purge()
            {
                lock (Locks.AgentCacheLock)
                {
                    AgentCache.Clear();
                }
                lock (Locks.GroupCacheLock)
                {
                    GroupCache.Clear();
                }
                lock (Locks.CurrentGroupsCacheLock)
                {
                    CurrentGroupsCache.Clear();
                }
            }

            /// <summary>
            ///     Serializes to a file.
            /// </summary>
            /// <param name="FileName">File path of the new xml file</param>
            /// <param name="o">the object to save</param>
            public static void Save<T>(string FileName, T o)
            {
                try
                {
                    using (StreamWriter writer = new StreamWriter(FileName))
                    {
                        XmlSerializer serializer = new XmlSerializer(typeof (T));
                        serializer.Serialize(writer, o);
                        writer.Flush();
                    }
                }
                catch (Exception e)
                {
                    Feedback(wasGetDescriptionFromEnumValue(ConsoleError.UNABLE_TO_SAVE_CORRADE_CACHE), e.Message);
                }
            }

            /// <summary>
            ///     Load an object from an xml file
            /// </summary>
            /// <param name="FileName">Xml file name</param>
            /// <param name="o">the object to load to</param>
            /// <returns>The object created from the xml file</returns>
            public static T Load<T>(string FileName, T o)
            {
                if (!File.Exists(FileName)) return o;
                try
                {
                    using (FileStream stream = File.OpenRead(FileName))
                    {
                        XmlSerializer serializer = new XmlSerializer(typeof (T));
                        return (T) serializer.Deserialize(stream);
                    }
                }
                catch (Exception ex)
                {
                    Feedback(wasGetDescriptionFromEnumValue(ConsoleError.UNABLE_TO_LOAD_CORRADE_CACHE), ex.Message);
                }
                return o;
            }

            public struct Agents
            {
                public string FirstName;
                public string LastName;
                public UUID UUID;
            }

            public struct Groups
            {
                public string Name;
                public UUID UUID;
            }

            public struct Locks
            {
                public static readonly object AgentCacheLock = new object();
                public static readonly object GroupCacheLock = new object();
                public static readonly object CurrentGroupsCacheLock = new object();
            }
        }

        /// <summary>
        ///     An element from the callback queue waiting to be dispatched.
        /// </summary>
        private struct CallbackQueueElement
        {
            public Dictionary<string, string> message;
            public string URL;
        }


        /// <summary>
        ///     The permission mask of a command.
        /// </summary>
        private class CommandPermissionMaskAttribute : Attribute
        {
            protected readonly uint permissionMask;

            public CommandPermissionMaskAttribute(uint permissionMask)
            {
                this.permissionMask = permissionMask;
            }

            public uint PermissionMask
            {
                get { return permissionMask; }
            }
        }

        /// <summary>
        ///     Whether this is a command or not.
        /// </summary>
        private class IsCommandAttribute : Attribute
        {
            protected readonly bool isCommand;

            public IsCommandAttribute(bool isCommand)
            {
                this.isCommand = isCommand;
            }

            public bool IsCommand
            {
                get { return isCommand; }
            }
        }

        /// <summary>
        ///     The syntax for a command.
        /// </summary>
        private class CommandInputSyntaxAttribute : Attribute
        {
            protected readonly string syntax;

            public CommandInputSyntaxAttribute(string syntax)
            {
                this.syntax = syntax;
            }

            public string Syntax
            {
                get { return syntax; }
            }
        }



        /// <summary>
        ///     An alarm class similar to the UNIX alarm with the added benefit
        ///     of a decaying timer that tracks the time between rescheduling.
        /// </summary>
        /// <remarks>
        ///     (C) Wizardry and Steamworks 2013 - License: GNU GPLv3
        /// </remarks>
        public class wasAdaptiveAlarm
        {
            [Flags]
            public enum DECAY_TYPE
            {
                [Description("none")] NONE = 0,
                [Description("arithmetic")] ARITHMETIC = 1,
                [Description("geometric")] GEOMETRIC = 2,
                [Description("harmonic")] HARMONIC = 4,
                [Description("weighted")] WEIGHTED = 5
            }

            private readonly DECAY_TYPE decay = DECAY_TYPE.NONE;
            private readonly Stopwatch elapsed = new Stopwatch();
            private readonly object LockObject = new object();
            private readonly HashSet<double> times = new HashSet<double>();
            private Timer alarm;

            /// <summary>
            ///     The default constructor using no decay.
            /// </summary>
            public wasAdaptiveAlarm()
            {
                Signal = new ManualResetEvent(false);
            }

            /// <summary>
            ///     The constructor for the wasAdaptiveAlarm class taking as parameter a decay type.
            /// </summary>
            /// <param name="decay">the type of decay: arithmetic, geometric, harmonic, heronian or quadratic</param>
            public wasAdaptiveAlarm(DECAY_TYPE decay)
            {
                Signal = new ManualResetEvent(false);
                this.decay = decay;
            }

            public ManualResetEvent Signal { get; set; }

            public void Alarm(double deadline)
            {
                lock (LockObject)
                {
                    if (alarm == null)
                    {
                        alarm = new Timer(deadline);
                        alarm.Elapsed += (o, p) =>
                        {
                            lock (LockObject)
                            {
                                Signal.Set();
                                elapsed.Stop();
                                times.Clear();
                                alarm = null;
                            }
                        };
                        elapsed.Start();
                        alarm.Start();
                        return;
                    }
                }
                elapsed.Stop();
                times.Add(elapsed.ElapsedMilliseconds);
                elapsed.Reset();
                elapsed.Start();
                lock (LockObject)
                {
                    if (alarm != null)
                    {
                        switch (decay)
                        {
                            case DECAY_TYPE.ARITHMETIC:
                                alarm.Interval = (deadline + times.Aggregate((a, b) => b + a))/(1f + times.Count);
                                break;
                            case DECAY_TYPE.GEOMETRIC:
                                alarm.Interval = Math.Pow(deadline*times.Aggregate((a, b) => b*a), 1f/(1f + times.Count));
                                break;
                            case DECAY_TYPE.HARMONIC:
                                alarm.Interval = (1f + times.Count)/
                                                 (1f/deadline + times.Aggregate((a, b) => 1f/b + 1f/a));
                                break;
                            case DECAY_TYPE.WEIGHTED:
                                HashSet<double> d = new HashSet<double>(times) {deadline};
                                double total = d.Aggregate((a, b) => b + a);
                                alarm.Interval = d.Aggregate((a, b) => Math.Pow(a, 2)/total + Math.Pow(b, 2)/total);
                                break;
                            default:
                                alarm.Interval = deadline;
                                break;
                        }
                    }
                }
            }
        }

        /// <summary>
        ///     Semaphores that sense the state of the connection. When any of these semaphores fail,
        ///     Corrode does not consider itself connected anymore and terminates.
        /// </summary>
        private static readonly Dictionary<char, ManualResetEvent> ConnectionSemaphores = new Dictionary
            <char, ManualResetEvent>
        {
            {'l', new ManualResetEvent(false)},
            {'s', new ManualResetEvent(false)},
            {'u', new ManualResetEvent(false)}
        };

        public static string InstalledServiceName;
        private static Thread programThread;
        private static readonly EventLog CorrodeEventLog = new EventLog();
        private static readonly GridClient Client = new GridClient();

        private static readonly Bot AIMLBot = new Bot
        {
            TrustAIML = false
        };

        private static readonly User AIMLBotUser = new User(CORRADE_CONSTANTS.CORRADE, AIMLBot);
        private static readonly FileSystemWatcher AIMLBotConfigurationWatcher = new FileSystemWatcher();
        private static readonly object AIMLBotLock = new object();
        private static readonly object ClientInstanceGroupsLock = new object();
        private static readonly object ClientInstanceInventoryLock = new object();
        private static readonly object ClientInstanceAvatarsLock = new object();
        private static readonly object ClientInstanceSelfLock = new object();
        private static readonly object ClientInstanceConfigurationLock = new object();
        private static readonly object ClientInstanceParcelsLock = new object();
        private static readonly object ClientInstanceNetworkLock = new object();
        private static readonly object ClientInstanceGridLock = new object();
        private static readonly object ClientInstanceDirectoryLock = new object();
        private static readonly object ClientInstanceEstateLock = new object();
        private static readonly object ClientInstanceObjectsLock = new object();
        private static readonly object ClientInstanceFriendsLock = new object();
        private static readonly object ClientInstanceAssetsLock = new object();
        private static readonly object ClientInstanceAppearanceLock = new object();
        private static readonly object ConfigurationFileLock = new object();
        private static readonly object ClientLogFileLock = new object();
        private static readonly object GroupLogFileLock = new object();
        private static readonly object LocalLogFileLock = new object();
        private static readonly object RegionLogFileLock = new object();
        private static readonly object InstantMessageLogFileLock = new object();
        private static readonly object DatabaseFileLock = new object();
        private static readonly Dictionary<string, object> DatabaseLocks = new Dictionary<string, object>();
        private static readonly object GroupNotificationsLock = new object();
        public static HashSet<Notification> GroupNotifications = new HashSet<Notification>();

        private static readonly SerializableDictionary<InventoryObjectOfferedEventArgs, ManualResetEvent>
            InventoryOffers =
                new SerializableDictionary<InventoryObjectOfferedEventArgs, ManualResetEvent>();

        private static readonly object InventoryOffersLock = new object();

        private CancellationTokenSource cancellationTokenSource = null;

        private static readonly ConcurrentQueue<CallbackQueueElement> CallbackQueue =
            new ConcurrentQueue<CallbackQueueElement>();
        private static readonly SemaphoreSlim CallbacksAvailable = new SemaphoreSlim(0);

        private static readonly ConcurrentQueue<NotificationQueueElement> NotificationQueue =
            new ConcurrentQueue<NotificationQueueElement>();
        private static readonly SemaphoreSlim NotificationsAvailable = new SemaphoreSlim(0);


        private static readonly HashSet<GroupInvite> GroupInvites = new HashSet<GroupInvite>();
        private static readonly object GroupInviteLock = new object();
        private static readonly HashSet<TeleportLure> TeleportLures = new HashSet<TeleportLure>();
        private static readonly object TeleportLureLock = new object();

        private static readonly HashSet<ScriptPermissionRequest> ScriptPermissionRequests =
            new HashSet<ScriptPermissionRequest>();

        private static readonly object ScriptPermissionRequestLock = new object();
        private static readonly HashSet<ScriptDialog> ScriptDialogs = new HashSet<ScriptDialog>();
        private static readonly object ScriptDialogLock = new object();

        private static readonly Dictionary<UUID, HashSet<UUID>> GroupMembers =
            new Dictionary<UUID, HashSet<UUID>>();

        private static readonly object GroupMembersLock = new object();
        private static readonly Hashtable GroupWorkers = new Hashtable();
        private static readonly object GroupWorkersLock = new object();
        private static readonly Hashtable GroupDirectoryTrackers = new Hashtable();
        private static readonly object GroupDirectoryTrackersLock = new object();
        private static readonly HashSet<LookAtEffect> LookAtEffects = new HashSet<LookAtEffect>();
        private static readonly HashSet<PointAtEffect> PointAtEffects = new HashSet<PointAtEffect>();
        private static readonly HashSet<SphereEffect> SphereEffects = new HashSet<SphereEffect>();
        private static readonly object SphereEffectsLock = new object();
        private static readonly HashSet<BeamEffect> BeamEffects = new HashSet<BeamEffect>();
        private static readonly Dictionary<UUID, Primitive> RadarObjects = new Dictionary<UUID, Primitive>();
        private static readonly object RadarObjectsLock = new object();
        private static readonly object BeamEffectsLock = new object();
        private static readonly object InputFiltersLock = new object();
        private static readonly object OutputFiltersLock = new object();
        private static volatile bool EnableRLV;
        private static volatile bool EnableAIML;
        private static volatile bool AIMLBotBrainCompiled;

        /// <summary>
        ///     The various types of threads used by Corrode.
        /// </summary>
        private static readonly Dictionary<CorrodeThreadType, CorrodeThread> CorrodeThreadPool =
            new Dictionary<CorrodeThreadType, CorrodeThread>
            {
                {CorrodeThreadType.COMMAND, new CorrodeThread()},
                {CorrodeThreadType.RLV, new CorrodeThread()},
                {CorrodeThreadType.NOTIFICATION, new CorrodeThread()},
                {CorrodeThreadType.INSTANT_MESSAGE, new CorrodeThread()}
            };

        /// <summary>
        ///     Group membership sweep thread.
        /// </summary>
        private static Thread GroupMembershipSweepThread;

        /// <summary>
        ///     Group membership sweep thread starter.
        /// </summary>
        private static readonly System.Action StartGroupMembershipSweepThread = () =>
        {
            if (GroupMembershipSweepThread != null &&
                (GroupMembershipSweepThread.ThreadState.Equals(ThreadState.Running) ||
                 GroupMembershipSweepThread.ThreadState.Equals(ThreadState.WaitSleepJoin))) return;
            runGroupMembershipSweepThread = true;
            GroupMembershipSweepThread = new Thread(GroupMembershipSweep)
            {
                IsBackground = true,
                Priority = ThreadPriority.BelowNormal
            };
            GroupMembershipSweepThread.Start();
        };

        /// <summary>
        ///     Group membership sweep thread stopper.
        /// </summary>
        private static readonly System.Action StopGroupMembershipSweepThread = () =>
        {
            // Stop the notification thread.
            runGroupMembershipSweepThread = false;
            if (GroupMembershipSweepThread == null ||
                (!GroupMembershipSweepThread.ThreadState.Equals(ThreadState.Running) &&
                 !GroupMembershipSweepThread.ThreadState.Equals(ThreadState.WaitSleepJoin))) return;
            if (GroupMembershipSweepThread.Join(1000)) return;
            try
            {
                GroupMembershipSweepThread.Abort();
                GroupMembershipSweepThread.Join();
            }
            catch (ThreadStateException)
            {
            }
        };

        /// <summary>
        ///     Schedules a load of the configuration file.
        /// </summary>
        private static readonly System.Threading.Timer ConfigurationChangedTimer =
            new System.Threading.Timer(ConfigurationChanged =>
            {
                Feedback(wasGetDescriptionFromEnumValue(ConsoleError.CONFIGURATION_FILE_MODIFIED));
                Configuration.Load(CORRADE_CONSTANTS.CONFIGURATION_FILE);
            });

        /// <summary>
        ///     Schedules a load of the AIML configuration file.
        /// </summary>
        private static readonly System.Threading.Timer AIMLConfigurationChangedTimer =
            new System.Threading.Timer(AIMLConfigurationChanged =>
            {
                Feedback(wasGetDescriptionFromEnumValue(ConsoleError.AIML_CONFIGURATION_MODIFIED));
                new Thread(
                    () =>
                    {
                        lock (AIMLBotLock)
                        {
                            LoadChatBotFiles.Invoke();
                        }
                    }) {IsBackground = true, Priority = ThreadPriority.BelowNormal}.Start();
            });

        /// <summary>
        ///     Global rebake timer.
        /// </summary>
        private static readonly System.Threading.Timer RebakeTimer = new System.Threading.Timer(Rebake =>
        {
            lock (ClientInstanceAppearanceLock)
            {
                ManualResetEvent AppearanceSetEvent = new ManualResetEvent(false);
                EventHandler<AppearanceSetEventArgs> HandleAppearanceSet = (sender, args) => AppearanceSetEvent.Set();
                Client.Appearance.AppearanceSet += HandleAppearanceSet;
                Client.Appearance.RequestSetAppearance(true);
                AppearanceSetEvent.WaitOne(Configuration.SERVICES_TIMEOUT, false);
                Client.Appearance.AppearanceSet -= HandleAppearanceSet;
            }
        });

        /// <summary>
        ///     Current land group activation timer.
        /// </summary>
        private static readonly System.Threading.Timer ActivateCurrentLandGroupTimer =
            new System.Threading.Timer(ActivateCurrentLandGroup =>
            {
                Parcel parcel = null;
                if (!GetParcelAtPosition(Client.Network.CurrentSim, Client.Self.SimPosition, ref parcel)) return;
                Group landGroup =
                    Configuration.GROUPS.AsParallel().FirstOrDefault(o => o.UUID.Equals(parcel.GroupID));
                if (landGroup.UUID.Equals(UUID.Zero)) return;
                Client.Groups.ActivateGroup(landGroup.UUID);
            });

        public static EventHandler ConsoleEventHandler;

        /// <summary>
        ///     Corrode's input filter function.
        /// </summary>
        private static readonly Func<string, string> wasInput = o =>
        {
            if (string.IsNullOrEmpty(o)) return string.Empty;

            List<Filter> safeFilters;
            lock (InputFiltersLock)
            {
                safeFilters = Configuration.INPUT_FILTERS;
            }
            foreach (Filter filter in safeFilters)
            {
                switch (filter)
                {
                    case Filter.RFC1738:
                        o = wasURLUnescapeDataString(o);
                        break;
                    case Filter.RFC3986:
                        o = wasURIUnescapeDataString(o);
                        break;
                    case Filter.ENIGMA:
                        o = wasEnigma(o, Configuration.ENIGMA.rotors.ToArray(), Configuration.ENIGMA.plugs.ToArray(),
                            Configuration.ENIGMA.reflector);
                        break;
                    case Filter.VIGENERE:
                        o = wasDecryptVIGENERE(o, Configuration.VIGENERE_SECRET);
                        break;
                    case Filter.ATBASH:
                        o = wasATBASH(o);
                        break;
                    case Filter.BASE64:
                        o = Encoding.UTF8.GetString(Convert.FromBase64String(o));
                        break;
                }
            }
            return o;
        };

        /// <summary>
        ///     Corrode's output filter function.
        /// </summary>
        private static readonly Func<string, string> wasOutput = o =>
        {
            if (string.IsNullOrEmpty(o)) return string.Empty;

            List<Filter> safeFilters;
            lock (OutputFiltersLock)
            {
                safeFilters = Configuration.OUTPUT_FILTERS;
            }
            foreach (Filter filter in safeFilters)
            {
                switch (filter)
                {
                    case Filter.RFC1738:
                        o = wasURLEscapeDataString(o);
                        break;
                    case Filter.RFC3986:
                        o = wasURIEscapeDataString(o);
                        break;
                    case Filter.ENIGMA:
                        o = wasEnigma(o, Configuration.ENIGMA.rotors.ToArray(), Configuration.ENIGMA.plugs.ToArray(),
                            Configuration.ENIGMA.reflector);
                        break;
                    case Filter.VIGENERE:
                        o = wasEncryptVIGENERE(o, Configuration.VIGENERE_SECRET);
                        break;
                    case Filter.ATBASH:
                        o = wasATBASH(o);
                        break;
                    case Filter.BASE64:
                        o = Convert.ToBase64String(Encoding.UTF8.GetBytes(o));
                        break;
                }
            }
            return o;
        };

        /// <summary>
        ///     Determines whether a string is a Corrode command.
        /// </summary>
        /// <returns>true if the string is a Corrode command</returns>
        private static readonly Func<string, bool> IsCorrodeCommand = o =>
        {
            Dictionary<string, string> data = wasKeyValueDecode(o);
            return !data.Count.Equals(0) && data.ContainsKey(wasGetDescriptionFromEnumValue(ScriptKeys.COMMAND)) &&
                   data.ContainsKey(wasGetDescriptionFromEnumValue(ScriptKeys.GROUP)) &&
                   data.ContainsKey(wasGetDescriptionFromEnumValue(ScriptKeys.PASSWORD));
        };

        /// <summary>
        ///     Gets the first name and last name from an avatar name.
        /// </summary>
        /// <returns>the firstname and the lastname or Resident</returns>
        private static readonly Func<string, IEnumerable<string>> GetAvatarNames =
            o => CORRADE_CONSTANTS.AvatarFullNameRegex.Matches(o)
                .Cast<Match>()
                .ToDictionary(p => new[]
                {
                    p.Groups["first"].Value,
                    p.Groups["last"].Value
                })
                .SelectMany(
                    p =>
                        new[]
                        {
                            p.Key[0].Trim(),
                            !string.IsNullOrEmpty(p.Key[1])
                                ? p.Key[1].Trim()
                                : LINDEN_CONSTANTS.AVATARS.LASTNAME_PLACEHOLDER
                        });

        /// <summary>
        ///     Updates the inventory starting from a folder recursively.
        /// </summary>
        private static readonly Action<InventoryFolder> UpdateInventoryRecursive = o =>
        {
            Thread updateInventoryRecursiveThread = new Thread(() =>
            {
                // Create the queue of folders.
                Dictionary<UUID, ManualResetEvent> inventoryFolders = new Dictionary<UUID, ManualResetEvent>();
                Dictionary<UUID, Stopwatch> inventoryStopwatch = new Dictionary<UUID, Stopwatch>();
                HashSet<long> times = new HashSet<long>(new[] {(long) Client.Settings.CAPS_TIMEOUT});
                // Enqueue the first folder (as the root).
                inventoryFolders.Add(o.UUID, new ManualResetEvent(false));
                inventoryStopwatch.Add(o.UUID, new Stopwatch());

                object LockObject = new object();

                EventHandler<FolderUpdatedEventArgs> FolderUpdatedEventHandler = (p, q) =>
                {
                    // Enqueue all the new folders.
                    Client.Inventory.Store.GetContents(q.FolderID).ForEach(r =>
                    {
                        if (r is InventoryFolder)
                        {
                            UUID inventoryFolderUUID = (r as InventoryFolder).UUID;
                            lock (LockObject)
                            {
                                inventoryFolders.Add(inventoryFolderUUID, new ManualResetEvent(false));
                                inventoryStopwatch.Add(inventoryFolderUUID, new Stopwatch());
                            }
                        }
                    });
                    inventoryFolders[q.FolderID].Set();
                    inventoryStopwatch[q.FolderID].Stop();
                    times.Add(inventoryStopwatch[q.FolderID].ElapsedMilliseconds);
                };

                do
                {
                    // Don't choke the chicken.
                    Thread.Yield();
                    Dictionary<UUID, ManualResetEvent> closureFolders;
                    lock (LockObject)
                    {
                        closureFolders =
                            new Dictionary<UUID, ManualResetEvent>(
                                inventoryFolders.Where(p => !p.Key.Equals(UUID.Zero))
                                    .ToDictionary(p => p.Key, q => q.Value));
                    }
                    lock (ClientInstanceInventoryLock)
                    {
                        Parallel.ForEach(closureFolders,
                            new ParallelOptions
                            {
                                MaxDegreeOfParallelism =
                                    Environment.ProcessorCount > 1 ? Environment.ProcessorCount - 1 : 1
                            },
                            p =>
                            {
                                Client.Inventory.FolderUpdated += FolderUpdatedEventHandler;
                                inventoryStopwatch[p.Key].Start();
                                Client.Inventory.RequestFolderContents(p.Key, Client.Self.AgentID, true, true,
                                    InventorySortOrder.ByDate);
                                closureFolders[p.Key].WaitOne((int) times.Average(), false);
                                Client.Inventory.FolderUpdated -= FolderUpdatedEventHandler;
                            });
                    }
                    Parallel.ForEach(closureFolders, new ParallelOptions
                    {
                        MaxDegreeOfParallelism = Environment.ProcessorCount > 1 ? Environment.ProcessorCount - 1 : 1
                    }, p =>
                    {
                        lock (LockObject)
                        {
                            if (inventoryFolders.ContainsKey(p.Key))
                            {
                                inventoryFolders.Remove(p.Key);
                            }
                            if (inventoryStopwatch.ContainsKey(p.Key))
                            {
                                inventoryStopwatch.Remove(p.Key);
                            }
                        }
                    });
                } while (!inventoryFolders.Count.Equals(0));
            }) {IsBackground = true, Priority = ThreadPriority.Lowest};

            updateInventoryRecursiveThread.Start();
            updateInventoryRecursiveThread.Join(Timeout.Infinite);
        };

        /// <summary>
        ///     Loads the OpenMetaverse inventory cache.
        /// </summary>
        private static readonly System.Action LoadInventoryCache = () =>
        {
            int itemsLoaded =
                Client.Inventory.Store.RestoreFromDisk(Path.Combine(CORRADE_CONSTANTS.CACHE_DIRECTORY,
                    CORRADE_CONSTANTS.INVENTORY_CACHE_FILE));

            Feedback(wasGetDescriptionFromEnumValue(ConsoleError.INVENTORY_CACHE_ITEMS_LOADED),
                itemsLoaded < 0 ? "0" : itemsLoaded.ToString(CultureInfo.InvariantCulture));
        };

        /// <summary>
        ///     Saves the OpenMetaverse inventory cache.
        /// </summary>
        private static readonly System.Action SaveInventoryCache = () =>
        {
            string path = Path.Combine(CORRADE_CONSTANTS.CACHE_DIRECTORY,
                CORRADE_CONSTANTS.INVENTORY_CACHE_FILE);
            int itemsSaved = Client.Inventory.Store.Items.Count;
            Client.Inventory.Store.SaveToDisk(path);

            Feedback(wasGetDescriptionFromEnumValue(ConsoleError.INVENTORY_CACHE_ITEMS_SAVED),
                itemsSaved.ToString(CultureInfo.InvariantCulture));
        };

        /// <summary>
        ///     Loads Corrode's caches.
        /// </summary>
        private static readonly System.Action LoadCorrodeCache = () =>
        {
            lock (Cache.Locks.AgentCacheLock)
            {
                Cache.AgentCache =
                    Cache.Load(Path.Combine(CORRADE_CONSTANTS.CACHE_DIRECTORY, CORRADE_CONSTANTS.AGENT_CACHE_FILE),
                        Cache.AgentCache);
            }
            lock (Cache.Locks.GroupCacheLock)
            {
                Cache.GroupCache =
                    Cache.Load(Path.Combine(CORRADE_CONSTANTS.CACHE_DIRECTORY, CORRADE_CONSTANTS.GROUP_CACHE_FILE),
                        Cache.GroupCache);
            }
        };

        /// <summary>
        ///     Saves Corrode's caches.
        /// </summary>
        private static readonly System.Action SaveCorrodeCache = () =>
        {
            lock (Cache.Locks.AgentCacheLock)
            {
                Cache.Save(Path.Combine(CORRADE_CONSTANTS.CACHE_DIRECTORY, CORRADE_CONSTANTS.AGENT_CACHE_FILE),
                    Cache.AgentCache);
            }
            lock (Cache.Locks.GroupCacheLock)
            {
                Cache.Save(Path.Combine(CORRADE_CONSTANTS.CACHE_DIRECTORY, CORRADE_CONSTANTS.GROUP_CACHE_FILE),
                    Cache.GroupCache);
            }
        };

        /// <summary>
        ///     Saves Corrode notifications.
        /// </summary>
        private static readonly System.Action SaveNotificationState = () =>
        {
            if (!GroupNotifications.Count.Equals(0))
            {
                try
                {
                    using (
                        StreamWriter writer =
                            new StreamWriter(Path.Combine(CORRADE_CONSTANTS.STATE_DIRECTORY,
                                CORRADE_CONSTANTS.NOTIFICATIONS_STATE_FILE)))
                    {
                        XmlSerializer serializer = new XmlSerializer(typeof (HashSet<Notification>));
                        serializer.Serialize(writer, GroupNotifications);
                        writer.Flush();
                    }
                }
                catch (Exception e)
                {
                    Feedback(wasGetDescriptionFromEnumValue(ConsoleError.UNABLE_TO_SAVE_CORRADE_NOTIFICATIONS_STATE),
                        e.Message);
                }
            }
        };

        /// <summary>
        ///     Saves inventory offers.
        /// </summary>
        private static readonly System.Action SaveInventoryOffersState = () =>
        {
            if (!InventoryOffers.Count.Equals(0))
            {
                try
                {
                    using (
                        StreamWriter writer =
                            new StreamWriter(Path.Combine(CORRADE_CONSTANTS.STATE_DIRECTORY,
                                CORRADE_CONSTANTS.INVENTORY_OFFERS_STATE_FILE)))
                    {
                        XmlSerializer serializer = new XmlSerializer(typeof (HashSet<Notification>));
                        serializer.Serialize(writer, InventoryOffers);
                        writer.Flush();
                    }
                }
                catch (Exception e)
                {
                    Feedback(wasGetDescriptionFromEnumValue(ConsoleError.UNABLE_TO_SAVE_CORRADE_INVENTORY_OFFERS_STATE),
                        e.Message);
                }
            }
        };

        /// <summary>
        ///     Loads Corrode notifications.
        /// </summary>
        private static readonly System.Action LoadNotificationState = () =>
        {
            string groupNotificationsStateFile = Path.Combine(CORRADE_CONSTANTS.STATE_DIRECTORY,
                CORRADE_CONSTANTS.NOTIFICATIONS_STATE_FILE);
            if (File.Exists(groupNotificationsStateFile))
            {
                try
                {
                    using (FileStream stream = File.OpenRead(groupNotificationsStateFile))
                    {
                        XmlSerializer serializer = new XmlSerializer(typeof (HashSet<Notification>));
                        Parallel.ForEach((HashSet<Notification>) serializer.Deserialize(stream),
                            o =>
                            {
                                if (!Configuration.GROUPS.AsParallel().Any(p => p.Name.Equals(o.GroupName)) ||
                                    GroupNotifications.Contains(o)) return;
                                GroupNotifications.Add(o);
                            });
                    }
                }
                catch (Exception ex)
                {
                    Feedback(
                        wasGetDescriptionFromEnumValue(ConsoleError.UNABLE_TO_LOAD_CORRADE_NOTIFICATIONS_STATE),
                        ex.Message);
                }
            }
        };

        /// <summary>
        ///     Loads inventory offers.
        /// </summary>
        private static readonly System.Action LoadInventoryOffersState = () =>
        {
            string inventoryOffersStateFile = Path.Combine(CORRADE_CONSTANTS.STATE_DIRECTORY,
                CORRADE_CONSTANTS.INVENTORY_OFFERS_STATE_FILE);
            if (File.Exists(inventoryOffersStateFile))
            {
                try
                {
                    using (FileStream stream = File.OpenRead(inventoryOffersStateFile))
                    {
                        XmlSerializer serializer = new XmlSerializer(typeof (HashSet<Notification>));
                        Parallel.ForEach(
                            (SerializableDictionary<InventoryObjectOfferedEventArgs, ManualResetEvent>)
                                serializer.Deserialize(stream),
                            o => { InventoryOffers.Add(o.Key, o.Value); });
                    }
                }
                catch (Exception ex)
                {
                    Feedback(
                        wasGetDescriptionFromEnumValue(ConsoleError.UNABLE_TO_LOAD_CORRADE_INVENTORY_OFFERS_STATE),
                        ex.Message);
                }
            }
        };

        /// <summary>
        ///     Loads the chatbot configuration and AIML files.
        /// </summary>
        private static readonly System.Action LoadChatBotFiles = () =>
        {
            Feedback(wasGetDescriptionFromEnumValue(ConsoleError.READING_AIML_BOT_CONFIGURATION));
            try
            {
                AIMLBot.isAcceptingUserInput = false;
                AIMLBot.loadSettings(wasPathCombine(
                    Directory.GetCurrentDirectory(), AIML_BOT_CONSTANTS.DIRECTORY,
                    AIML_BOT_CONSTANTS.CONFIG.DIRECTORY, AIML_BOT_CONSTANTS.CONFIG.SETTINGS_FILE));
                string AIMLBotBrain =
                    wasPathCombine(
                        Directory.GetCurrentDirectory(), AIML_BOT_CONSTANTS.DIRECTORY,
                        AIML_BOT_CONSTANTS.BRAIN.DIRECTORY, AIML_BOT_CONSTANTS.BRAIN_FILE);
                switch (File.Exists(AIMLBotBrain))
                {
                    case true:
                        // *FIXME
                        //AIMLBot.loadFromBinaryFile(AIMLBotBrain);
                        break;
                    default:
                        AIMLBot.loadAIMLFromFiles();
                        //AIMLBot.saveToBinaryFile(AIMLBotBrain);
                        break;
                }
                string AIMLBotUserBrain =
                    wasPathCombine(
                        Directory.GetCurrentDirectory(), AIML_BOT_CONSTANTS.DIRECTORY,
                        AIML_BOT_CONSTANTS.BRAIN.DIRECTORY, AIML_BOT_CONSTANTS.BRAIN_SESSION_FILE);
                if (File.Exists(AIMLBotUserBrain))
                {
                    AIMLBotUser.Predicates.loadSettings(AIMLBotUserBrain);
                }
                AIMLBot.isAcceptingUserInput = true;
            }
            catch (Exception ex)
            {
                Feedback(wasGetDescriptionFromEnumValue(ConsoleError.ERROR_LOADING_AIML_BOT_FILES), ex.Message);
                return;
            }
            finally
            {
                AIMLBotBrainCompiled = true;
            }
            Feedback(wasGetDescriptionFromEnumValue(ConsoleError.READ_AIML_BOT_CONFIGURATION));
        };

        /// <summary>
        ///     Saves the chatbot configuration and AIML files.
        /// </summary>
        private static readonly System.Action SaveChatBotFiles = () =>
        {
            Feedback(wasGetDescriptionFromEnumValue(ConsoleError.WRITING_AIML_BOT_CONFIGURATION));
            try
            {
                AIMLBot.isAcceptingUserInput = false;
                AIMLBotUser.Predicates.DictionaryAsXML.Save(wasPathCombine(
                    Directory.GetCurrentDirectory(), AIML_BOT_CONSTANTS.DIRECTORY,
                    AIML_BOT_CONSTANTS.BRAIN.DIRECTORY, AIML_BOT_CONSTANTS.BRAIN_SESSION_FILE));
                AIMLBot.isAcceptingUserInput = true;
            }
            catch (Exception ex)
            {
                Feedback(wasGetDescriptionFromEnumValue(ConsoleError.ERROR_SAVING_AIML_BOT_FILES), ex.Message);
                return;
            }
            Feedback(wasGetDescriptionFromEnumValue(ConsoleError.WROTE_AIML_BOT_CONFIGURATION));
        };

        private static volatile bool runCallbackThread = true;
        private static volatile bool runGroupMembershipSweepThread = true;
        private static volatile bool runNotificationThread = true;
        private static volatile bool runEffectsExpirationThread = true;
    }
}