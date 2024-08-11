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
using System.Threading;
using DirectoryManager = OpenMetaverse.DirectoryManager;
using DirGroupsReplyEventArgs = OpenMetaverse.DirGroupsReplyEventArgs;
using DirPeopleReplyEventArgs = OpenMetaverse.DirPeopleReplyEventArgs;
using GroupProfileEventArgs = OpenMetaverse.GroupProfileEventArgs;
using GroupRole = OpenMetaverse.GroupRole;
using GroupRolesDataReplyEventArgs = OpenMetaverse.GroupRolesDataReplyEventArgs;
using MoneyBalanceReplyEventArgs = OpenMetaverse.MoneyBalanceReplyEventArgs;
using UUIDNameReplyEventArgs = OpenMetaverse.UUIDNameReplyEventArgs;

namespace Corrode
{
    public partial class Corrode
    {
        /// <summary>
        ///     Tries to build an UUID out of the data string.
        /// </summary>
        /// <param name="data">a string</param>
        /// <returns>an UUID or the supplied string in case data could not be resolved</returns>
        private static object StringOrUUID(string data)
        {
            if (string.IsNullOrEmpty(data))
            {
                return null;
            }

            if (!OpenMetaverse.UUID.TryParse(data, out var UUID))
            {
                return data;
            }
            return UUID;
        }

        /// <summary>
        ///     Updates the current balance by requesting it from the grid.
        /// </summary>
        /// <param name="millisecondsTimeout">timeout for the request in milliseconds</param>
        /// <returns>true if the balance could be retrieved</returns>
        private static bool UpdateBalance(int millisecondsTimeout)
        {
            ManualResetEvent MoneyBalanceEvent = new ManualResetEvent(false);
            EventHandler<MoneyBalanceReplyEventArgs> MoneyBalanceEventHandler =
                (sender, args) => MoneyBalanceEvent.Set();
            lock (ClientInstanceSelfLock)
            {
                Client.Self.MoneyBalanceReply += MoneyBalanceEventHandler;
                Client.Self.RequestBalance();
                if (!MoneyBalanceEvent.WaitOne(millisecondsTimeout, false))
                {
                    Client.Self.MoneyBalanceReply -= MoneyBalanceEventHandler;
                    return false;
                }
                Client.Self.MoneyBalanceReply -= MoneyBalanceEventHandler;
            }
            return true;
        }

        /// <summary>
        ///     Resolves a group name to an UUID by using the directory search.
        /// </summary>
        /// <param name="groupName">the name of the group to resolve</param>
        /// <param name="millisecondsTimeout">timeout for the search in milliseconds</param>
        /// <param name="dataTimeout">timeout for receiving answers from services</param>
        /// <param name="groupUUID">an object in which to store the UUID of the group</param>
        /// <returns>true if the group name could be resolved to an UUID</returns>
        private static bool directGroupNameToUUID(string groupName, int millisecondsTimeout, int dataTimeout,
            ref OpenMetaverse.UUID groupUUID)
        {
            OpenMetaverse.UUID localGroupUUID = OpenMetaverse.UUID.Zero;
            wasAdaptiveAlarm DirGroupsReceivedAlarm = new wasAdaptiveAlarm(Configuration.DATA_DECAY_TYPE);
            EventHandler<DirGroupsReplyEventArgs> DirGroupsReplyDelegate = (sender, args) =>
            {
                DirGroupsReceivedAlarm.Alarm(dataTimeout);
                DirectoryManager.GroupSearchData groupSearchData =
                    args.MatchedGroups.AsParallel()
                        .FirstOrDefault(o => o.GroupName.Equals(groupName, StringComparison.Ordinal));
                if (groupSearchData.Equals(default(DirectoryManager.GroupSearchData))) return;
                localGroupUUID = groupSearchData.GroupID;
            };
            Client.Directory.DirGroupsReply += DirGroupsReplyDelegate;
            Client.Directory.StartGroupSearch(groupName, 0);
            if (!DirGroupsReceivedAlarm.Signal.WaitOne(millisecondsTimeout, false))
            {
                Client.Directory.DirGroupsReply -= DirGroupsReplyDelegate;
                return false;
            }
            Client.Directory.DirGroupsReply -= DirGroupsReplyDelegate;
            if (localGroupUUID.Equals(OpenMetaverse.UUID.Zero)) return false;
            groupUUID = localGroupUUID;
            return true;
        }

        /// <summary>
        ///     A wrapper for resolving group names to UUIDs by using Corrode's internal cache.
        /// </summary>
        /// <param name="groupName">the name of the group to resolve</param>
        /// <param name="millisecondsTimeout">timeout for the search in milliseconds</param>
        /// <param name="dataTimeout">timeout for receiving answers from services</param>
        /// <param name="groupUUID">an object in which to store the UUID of the group</param>
        /// <returns>true if the group name could be resolved to an UUID</returns>
        private static bool GroupNameToUUID(string groupName, int millisecondsTimeout, int dataTimeout,
            ref OpenMetaverse.UUID groupUUID)
        {
            lock (Cache.Locks.GroupCacheLock)
            {
                Cache.Groups @group = Cache.GroupCache.AsParallel().FirstOrDefault(o => o.Name.Equals(groupName));

                if (!@group.Equals(default(Cache.Groups)))
                {
                    groupUUID = @group.UUID;
                    return true;
                }
            }
            bool succeeded;
            lock (ClientInstanceDirectoryLock)
            {
                succeeded = directGroupNameToUUID(groupName, millisecondsTimeout, dataTimeout, ref groupUUID);
            }
            if (succeeded)
            {
                lock (Cache.Locks.GroupCacheLock)
                {
                    Cache.GroupCache.Add(new Cache.Groups
                    {
                        Name = groupName,
                        UUID = groupUUID
                    });
                }
            }
            return succeeded;
        }

        /// <summary>
        ///     Resolves a group name to an UUID by using the directory search.
        /// </summary>
        /// <param name="groupName">a string to store the name to</param>
        /// <param name="millisecondsTimeout">timeout for the search in milliseconds</param>
        /// <param name="dataTimeout">timeout for receiving answers from services</param>
        /// <param name="groupUUID">the UUID of the group to resolve</param>
        /// <returns>true if the group UUID could be resolved to an name</returns>
        private static bool directGroupUUIDToName(OpenMetaverse.UUID groupUUID, int millisecondsTimeout, int dataTimeout,
            ref string groupName)
        {
            string localGroupName = groupName;
            wasAdaptiveAlarm GroupProfileReceivedAlarm = new wasAdaptiveAlarm(Configuration.DATA_DECAY_TYPE);
            EventHandler<GroupProfileEventArgs> GroupProfileDelegate = (o, s) =>
            {
                GroupProfileReceivedAlarm.Alarm(dataTimeout);
                localGroupName = s.Group.Name;
            };
            Client.Groups.GroupProfile += GroupProfileDelegate;
            Client.Groups.RequestGroupProfile(groupUUID);
            if (!GroupProfileReceivedAlarm.Signal.WaitOne(millisecondsTimeout, false))
            {
                Client.Groups.GroupProfile -= GroupProfileDelegate;
                return false;
            }
            Client.Groups.GroupProfile -= GroupProfileDelegate;
            groupName = localGroupName;
            return true;
        }

        /// <summary>
        ///     A wrapper for resolving group names to UUIDs by using Corrode's internal cache.
        /// </summary>
        /// <param name="groupName">a string to store the name to</param>
        /// <param name="millisecondsTimeout">timeout for the search in milliseconds</param>
        /// <param name="dataTimeout">timeout for receiving answers from services</param>
        /// <param name="groupUUID">the UUID of the group to resolve</param>
        /// <returns>true if the group UUID could be resolved to an name</returns>
        private static bool GroupUUIDToName(OpenMetaverse.UUID groupUUID, int millisecondsTimeout, int dataTimeout,
            ref string groupName)
        {
            lock (Cache.Locks.GroupCacheLock)
            {
                Cache.Groups @group = Cache.GroupCache.AsParallel().FirstOrDefault(o => o.UUID.Equals(groupUUID));

                if (!@group.Equals(default(Cache.Groups)))
                {
                    groupName = @group.Name;
                    return true;
                }
            }
            bool succeeded;
            lock (ClientInstanceGroupsLock)
            {
                succeeded = directGroupUUIDToName(groupUUID, millisecondsTimeout, dataTimeout, ref groupName);
            }
            if (succeeded)
            {
                lock (Cache.Locks.GroupCacheLock)
                {
                    Cache.GroupCache.Add(new Cache.Groups
                    {
                        Name = groupName,
                        UUID = groupUUID
                    });
                }
            }
            return succeeded;
        }

        /// <summary>
        ///     Resolves an agent name to an agent UUID by searching the directory
        ///     services.
        /// </summary>
        /// <param name="agentFirstName">the first name of the agent</param>
        /// <param name="agentLastName">the last name of the agent</param>
        /// <param name="millisecondsTimeout">timeout for the search in milliseconds</param>
        /// <param name="dataTimeout">timeout for receiving answers from services</param>
        /// <param name="agentUUID">an object to store the agent UUID</param>
        /// <returns>true if the agent name could be resolved to an UUID</returns>
        private static bool directAgentNameToUUID(string agentFirstName, string agentLastName, int millisecondsTimeout,
            int dataTimeout,
            ref OpenMetaverse.UUID agentUUID)
        {
            OpenMetaverse.UUID localAgentUUID = OpenMetaverse.UUID.Zero;
            wasAdaptiveAlarm DirPeopleReceivedAlarm = new wasAdaptiveAlarm(Configuration.DATA_DECAY_TYPE);
            EventHandler<DirPeopleReplyEventArgs> DirPeopleReplyDelegate = (sender, args) =>
            {
                DirPeopleReceivedAlarm.Alarm(dataTimeout);
                DirectoryManager.AgentSearchData agentSearchData =
                    args.MatchedPeople.AsParallel().FirstOrDefault(
                        o =>
                            o.FirstName.Equals(agentFirstName, StringComparison.OrdinalIgnoreCase) &&
                            o.LastName.Equals(agentLastName, StringComparison.OrdinalIgnoreCase));
                if (agentSearchData.Equals(default(DirectoryManager.AgentSearchData))) return;
                localAgentUUID = agentSearchData.AgentID;
            };
            Client.Directory.DirPeopleReply += DirPeopleReplyDelegate;
            Client.Directory.StartPeopleSearch(
                string.Format(CultureInfo.InvariantCulture, "{0} {1}", agentFirstName, agentLastName), 0);
            if (!DirPeopleReceivedAlarm.Signal.WaitOne(millisecondsTimeout, false))
            {
                Client.Directory.DirPeopleReply -= DirPeopleReplyDelegate;
                return false;
            }
            Client.Directory.DirPeopleReply -= DirPeopleReplyDelegate;
            if (localAgentUUID.Equals(OpenMetaverse.UUID.Zero)) return false;
            agentUUID = localAgentUUID;
            return true;
        }

        /// <summary>
        ///     A wrapper for looking up an agent name using Corrode's internal cache.
        /// </summary>
        /// <param name="agentFirstName">the first name of the agent</param>
        /// <param name="agentLastName">the last name of the agent</param>
        /// <param name="millisecondsTimeout">timeout for the search in milliseconds</param>
        /// <param name="dataTimeout">timeout for receiving answers from services</param>
        /// <param name="agentUUID">an object to store the agent UUID</param>
        /// <returns>true if the agent name could be resolved to an UUID</returns>
        private static bool AgentNameToUUID(string agentFirstName, string agentLastName, int millisecondsTimeout,
            int dataTimeout,
            ref OpenMetaverse.UUID agentUUID)
        {
            lock (Cache.Locks.AgentCacheLock)
            {
                Cache.Agents agent =
                    Cache.AgentCache.AsParallel().FirstOrDefault(
                        o => o.FirstName.Equals(agentFirstName) && o.LastName.Equals(agentLastName));

                if (!agent.Equals(default(Cache.Agents)))
                {
                    agentUUID = agent.UUID;
                    return true;
                }
            }
            bool succeeded;
            lock (ClientInstanceDirectoryLock)
            {
                succeeded = directAgentNameToUUID(agentFirstName, agentLastName, millisecondsTimeout, dataTimeout,
                    ref agentUUID);
            }
            if (succeeded)
            {
                lock (Cache.Locks.AgentCacheLock)
                {
                    Cache.AgentCache.Add(new Cache.Agents
                    {
                        FirstName = agentFirstName,
                        LastName = agentLastName,
                        UUID = agentUUID
                    });
                }
            }
            return succeeded;
        }

        /// <summary>
        ///     Resolves an agent UUID to an agent name.
        /// </summary>
        /// <param name="agentUUID">the UUID of the agent</param>
        /// <param name="millisecondsTimeout">timeout for the search in milliseconds</param>
        /// <param name="agentName">an object to store the name of the agent in</param>
        /// <returns>true if the UUID could be resolved to a name</returns>
        private static bool directAgentUUIDToName(OpenMetaverse.UUID agentUUID, int millisecondsTimeout,
            ref string agentName)
        {
            if (agentUUID.Equals(OpenMetaverse.UUID.Zero))
                return false;
            string localAgentName = string.Empty;
            ManualResetEvent UUIDNameReplyEvent = new ManualResetEvent(false);
            EventHandler<UUIDNameReplyEventArgs> UUIDNameReplyDelegate = (sender, args) =>
            {
                KeyValuePair<OpenMetaverse.UUID, string> UUIDNameReply =
                    args.Names.AsParallel().FirstOrDefault(o => o.Key.Equals(agentUUID));
                if (!UUIDNameReply.Equals(default(KeyValuePair<OpenMetaverse.UUID, string>)))
                    localAgentName = UUIDNameReply.Value;
                UUIDNameReplyEvent.Set();
            };
            Client.Avatars.UUIDNameReply += UUIDNameReplyDelegate;
            Client.Avatars.RequestAvatarName(agentUUID);
            if (!UUIDNameReplyEvent.WaitOne(millisecondsTimeout, false))
            {
                Client.Avatars.UUIDNameReply -= UUIDNameReplyDelegate;
                return false;
            }
            Client.Avatars.UUIDNameReply -= UUIDNameReplyDelegate;
            if (string.IsNullOrEmpty(localAgentName)) return false;
            agentName = localAgentName;
            return true;
        }

        /// <summary>
        ///     A wrapper for agent to UUID lookups using Corrode's internal cache.
        /// </summary>
        /// <param name="agentUUID">the UUID of the agent</param>
        /// <param name="millisecondsTimeout">timeout for the search in milliseconds</param>
        /// <param name="agentName">an object to store the name of the agent in</param>
        /// <returns>true if the UUID could be resolved to a name</returns>
        private static bool AgentUUIDToName(OpenMetaverse.UUID agentUUID, int millisecondsTimeout,
            ref string agentName)
        {
            lock (Cache.Locks.AgentCacheLock)
            {
                Cache.Agents agent = Cache.AgentCache.AsParallel().FirstOrDefault(o => o.UUID.Equals(agentUUID));

                if (!agent.Equals(default(Cache.Agents)))
                {
                    agentName = string.Join(" ", agent.FirstName, agent.LastName);
                    return true;
                }
            }
            bool succeeded;
            lock (ClientInstanceAvatarsLock)
            {
                succeeded = directAgentUUIDToName(agentUUID, millisecondsTimeout, ref agentName);
            }
            if (succeeded)
            {
                List<string> name = new List<string>(GetAvatarNames(agentName));
                lock (Cache.Locks.AgentCacheLock)
                {
                    Cache.AgentCache.Add(new Cache.Agents
                    {
                        FirstName = name.First(),
                        LastName = name.Last(),
                        UUID = agentUUID
                    });
                }
            }
            return succeeded;
        }

        /// ///
        /// <summary>
        ///     Resolves a role name to a role UUID.
        /// </summary>
        /// <param name="roleName">the name of the role to be resolved to an UUID</param>
        /// <param name="groupUUID">the UUID of the group to query for the role UUID</param>
        /// <param name="millisecondsTimeout">timeout for the search in milliseconds</param>
        /// <param name="dataTimeout">timeout for receiving answers from services</param>
        /// <param name="roleUUID">an UUID object to store the role UUID in</param>
        /// <returns>true if the role could be found</returns>
        private static bool RoleNameToRoleUUID(string roleName, OpenMetaverse.UUID groupUUID, int millisecondsTimeout,
            int dataTimeout,
            ref OpenMetaverse.UUID roleUUID)
        {
            OpenMetaverse.UUID localRoleUUID = OpenMetaverse.UUID.Zero;
            wasAdaptiveAlarm GroupRoleDataReceivedAlarm = new wasAdaptiveAlarm(Configuration.DATA_DECAY_TYPE);
            EventHandler<GroupRolesDataReplyEventArgs> GroupRoleDataReplyDelegate = (sender, args) =>
            {
                GroupRoleDataReceivedAlarm.Alarm(dataTimeout);
                KeyValuePair<OpenMetaverse.UUID, GroupRole> groupRole =
                    args.Roles.AsParallel()
                        .FirstOrDefault(o => o.Value.Name.Equals(roleName, StringComparison.Ordinal));
                if (groupRole.Equals(default(KeyValuePair<OpenMetaverse.UUID, GroupRole>))) return;
                localRoleUUID = groupRole.Key;
            };
            lock (ClientInstanceGroupsLock)
            {
                Client.Groups.GroupRoleDataReply += GroupRoleDataReplyDelegate;
                Client.Groups.RequestGroupRoles(groupUUID);
                if (!GroupRoleDataReceivedAlarm.Signal.WaitOne(millisecondsTimeout, false))
                {
                    Client.Groups.GroupRoleDataReply -= GroupRoleDataReplyDelegate;
                    return false;
                }
                Client.Groups.GroupRoleDataReply -= GroupRoleDataReplyDelegate;
            }
            roleUUID = localRoleUUID;
            return true;
        }

        /// <summary>
        ///     Resolves a role name to a role UUID.
        /// </summary>
        /// <param name="RoleUUID">the UUID of the role to be resolved to a name</param>
        /// <param name="GroupUUID">the UUID of the group to query for the role name</param>
        /// <param name="millisecondsTimeout">timeout for the search in milliseconds</param>
        /// <param name="dataTimeout">timeout for receiving answers from services</param>
        /// <param name="roleName">a string object to store the role name in</param>
        /// <returns>true if the role could be resolved</returns>
        private static bool RoleUUIDToName(OpenMetaverse.UUID RoleUUID, OpenMetaverse.UUID GroupUUID, int millisecondsTimeout, int dataTimeout,
            ref string roleName)
        {
            if (RoleUUID.Equals(OpenMetaverse.UUID.Zero) || GroupUUID.Equals(OpenMetaverse.UUID.Zero))
                return false;
            string localRoleName = string.Empty;
            wasAdaptiveAlarm GroupRoleDataReceivedAlarm = new wasAdaptiveAlarm(Configuration.DATA_DECAY_TYPE);
            EventHandler<GroupRolesDataReplyEventArgs> GroupRoleDataReplyDelegate = (sender, args) =>
            {
                GroupRoleDataReceivedAlarm.Alarm(dataTimeout);
                KeyValuePair<OpenMetaverse.UUID, GroupRole> groupRole =
                    args.Roles.AsParallel().FirstOrDefault(o => o.Key.Equals(RoleUUID));
                if (groupRole.Equals(default(KeyValuePair<OpenMetaverse.UUID, GroupRole>))) return;
                localRoleName = groupRole.Value.Name;
            };
            lock (ClientInstanceGroupsLock)
            {
                Client.Groups.GroupRoleDataReply += GroupRoleDataReplyDelegate;
                Client.Groups.RequestGroupRoles(GroupUUID);
                if (!GroupRoleDataReceivedAlarm.Signal.WaitOne(millisecondsTimeout, false))
                {
                    Client.Groups.GroupRoleDataReply -= GroupRoleDataReplyDelegate;
                    return false;
                }
                Client.Groups.GroupRoleDataReply -= GroupRoleDataReplyDelegate;
            }
            if (string.IsNullOrEmpty(localRoleName)) return false;
            roleName = localRoleName;
            return true;
        }
    }
}
