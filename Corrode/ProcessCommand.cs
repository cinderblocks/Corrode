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
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Xml;
using AIMLbot;
using CoreJ2K;
using SkiaSharp;
using OpenMetaverse;
using OpenMetaverse.Assets;
using OpenMetaverse.Imaging;
using OpenMetaverse.Rendering;
using OpenMetaverse.StructuredData;
using Parallel = System.Threading.Tasks.Parallel;
using Path = System.IO.Path;

namespace Corrode
{
    public partial class Corrode
    {
        /// <summary>
        ///     This function is responsible for processing commands.
        /// </summary>
        /// <param name="message">the message</param>
        /// <returns>a dictionary of key-value pairs representing the results of the command</returns>
        private static Dictionary<string, string> ProcessCommand(string message)
        {
            Dictionary<string, string> result = new Dictionary<string, string>();
            string command =
                wasInput(wasKeyValueGet(wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.COMMAND)), message));
            if (!string.IsNullOrEmpty(command))
            {
                result.Add(wasGetDescriptionFromEnumValue(ScriptKeys.COMMAND), command);
            }
            string group =
                wasInput(wasKeyValueGet(wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.GROUP)), message));
            if (!string.IsNullOrEmpty(group))
            {
                result.Add(wasGetDescriptionFromEnumValue(ScriptKeys.GROUP), group);
            }

            System.Action execute;

            switch (wasGetEnumValueFromDescription<ScriptKeys>(command))
            {
                case ScriptKeys.JOIN:
                    execute = () =>
                    {
                        if (!HasCorrodePermission(group, (int) Permissions.PERMISSION_GROUP))
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.NO_CORRADE_PERMISSIONS));
                        }
                        Group configuredGroup =
                            Configuration.GROUPS.AsParallel().FirstOrDefault(
                                o => o.Name.Equals(group, StringComparison.Ordinal));
                        UUID groupUUID = UUID.Zero;
                        switch (!configuredGroup.Equals(default(Group)))
                        {
                            case true:
                                groupUUID = configuredGroup.UUID;
                                break;
                            default:
                                if (!GroupNameToUUID(group, Configuration.SERVICES_TIMEOUT, Configuration.DATA_TIMEOUT,
                                    ref groupUUID))
                                {
                                    throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.GROUP_NOT_FOUND));
                                }
                                break;
                        }
                        if (AgentInGroup(Client.Self.AgentID, groupUUID, Configuration.SERVICES_TIMEOUT,
                            Configuration.DATA_TIMEOUT))
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.ALREADY_IN_GROUP));
                        }
                        OpenMetaverse.Group commandGroup = new OpenMetaverse.Group();
                        if (!RequestGroup(groupUUID, Configuration.SERVICES_TIMEOUT, ref commandGroup))
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.GROUP_NOT_FOUND));
                        }
                        if (!commandGroup.OpenEnrollment)
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.GROUP_NOT_OPEN));
                        }
                        if (!Client.Network.MaxAgentGroups.Equals(-1))
                        {
                            HashSet<UUID> currentGroups = new HashSet<UUID>();
                            if (
                                !GetCurrentGroups(Configuration.SERVICES_TIMEOUT, Configuration.DATA_TIMEOUT,
                                    ref currentGroups))
                            {
                                throw new Exception(
                                    wasGetDescriptionFromEnumValue(ScriptError.COULD_NOT_GET_CURRENT_GROUPS));
                            }
                            if (currentGroups.Count >= Client.Network.MaxAgentGroups)
                            {
                                throw new Exception(
                                    wasGetDescriptionFromEnumValue(ScriptError.MAXIMUM_NUMBER_OF_GROUPS_REACHED));
                            }
                        }
                        ManualResetEvent GroupJoinedReplyEvent = new ManualResetEvent(false);
                        EventHandler<GroupOperationEventArgs> GroupOperationEventHandler =
                            (sender, args) => GroupJoinedReplyEvent.Set();
                        lock (ClientInstanceGroupsLock)
                        {
                            Client.Groups.GroupJoinedReply += GroupOperationEventHandler;
                            Client.Groups.RequestJoinGroup(groupUUID);
                            if (!GroupJoinedReplyEvent.WaitOne(Configuration.SERVICES_TIMEOUT, false))
                            {
                                Client.Groups.GroupJoinedReply -= GroupOperationEventHandler;
                                throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.TIMEOUT_JOINING_GROUP));
                            }
                            Client.Groups.GroupJoinedReply -= GroupOperationEventHandler;
                        }
                        if (
                            !AgentInGroup(Client.Self.AgentID, groupUUID, Configuration.SERVICES_TIMEOUT,
                                Configuration.DATA_TIMEOUT))
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.COULD_NOT_JOIN_GROUP));
                        }
                    };
                    break;
                case ScriptKeys.CREATEGROUP:
                    execute = () =>
                    {
                        if (!HasCorrodePermission(group, (int) Permissions.PERMISSION_GROUP))
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.NO_CORRADE_PERMISSIONS));
                        }
                        if (!UpdateBalance(Configuration.SERVICES_TIMEOUT))
                        {
                            throw new Exception(
                                wasGetDescriptionFromEnumValue(ScriptError.UNABLE_TO_OBTAIN_MONEY_BALANCE));
                        }
                        if (Client.Self.Balance < Configuration.GROUP_CREATE_FEE)
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.INSUFFICIENT_FUNDS));
                        }
                        if (!Configuration.GROUP_CREATE_FEE.Equals(0) &&
                            !HasCorrodePermission(group, (int) Permissions.PERMISSION_ECONOMY))
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.NO_CORRADE_PERMISSIONS));
                        }
                        OpenMetaverse.Group commandGroup = new OpenMetaverse.Group
                        {
                            Name = group
                        };
                        wasCSVToStructure(
                            wasInput(wasKeyValueGet(wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.DATA)),
                                message)),
                            ref commandGroup);
                        bool succeeded = false;
                        ManualResetEvent GroupCreatedReplyEvent = new ManualResetEvent(false);
                        EventHandler<GroupCreatedReplyEventArgs> GroupCreatedEventHandler = (sender, args) =>
                        {
                            succeeded = args.Success;
                            GroupCreatedReplyEvent.Set();
                        };
                        lock (ClientInstanceGroupsLock)
                        {
                            Client.Groups.GroupCreatedReply += GroupCreatedEventHandler;
                            Client.Groups.RequestCreateGroup(commandGroup);
                            if (!GroupCreatedReplyEvent.WaitOne(Configuration.SERVICES_TIMEOUT, false))
                            {
                                Client.Groups.GroupCreatedReply -= GroupCreatedEventHandler;
                                throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.TIMEOUT_CREATING_GROUP));
                            }
                            Client.Groups.GroupCreatedReply -= GroupCreatedEventHandler;
                        }
                        if (!succeeded)
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.COULD_NOT_CREATE_GROUP));
                        }
                    };
                    break;
                case ScriptKeys.INVITE:
                    execute = () =>
                    {
                        if (!HasCorrodePermission(group, (int) Permissions.PERMISSION_GROUP))
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.NO_CORRADE_PERMISSIONS));
                        }
                        Group configuredGroup =
                            Configuration.GROUPS.AsParallel().FirstOrDefault(
                                o => o.Name.Equals(group, StringComparison.Ordinal));
                        UUID groupUUID = UUID.Zero;
                        switch (!configuredGroup.Equals(default(Group)))
                        {
                            case true:
                                groupUUID = configuredGroup.UUID;
                                break;
                            default:
                                if (!GroupNameToUUID(group, Configuration.SERVICES_TIMEOUT, Configuration.DATA_TIMEOUT,
                                    ref groupUUID))
                                {
                                    throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.GROUP_NOT_FOUND));
                                }
                                break;
                        }
                        if (
                            !HasGroupPowers(Client.Self.AgentID, groupUUID, GroupPowers.Invite,
                                Configuration.SERVICES_TIMEOUT, Configuration.DATA_TIMEOUT))
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.NO_GROUP_POWER_FOR_COMMAND));
                        }
                        UUID agentUUID;
                        if (
                            !UUID.TryParse(
                                wasInput(wasKeyValueGet(
                                    wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.AGENT)), message)),
                                out agentUUID) && !AgentNameToUUID(
                                    wasInput(
                                        wasKeyValueGet(wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.FIRSTNAME)),
                                            message)),
                                    wasInput(
                                        wasKeyValueGet(wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.LASTNAME)),
                                            message)),
                                    Configuration.SERVICES_TIMEOUT, Configuration.DATA_TIMEOUT, ref agentUUID))
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.AGENT_NOT_FOUND));
                        }
                        if (AgentInGroup(agentUUID, groupUUID, Configuration.SERVICES_TIMEOUT,
                            Configuration.DATA_TIMEOUT))
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.ALREADY_IN_GROUP));
                        }
                        HashSet<UUID> roleUUIDs = new HashSet<UUID>();
                        foreach (
                            string role in
                                wasCSVToEnumerable(
                                    wasInput(wasKeyValueGet(wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.ROLE)),
                                        message)))
                                    .AsParallel().Where(o => !string.IsNullOrEmpty(o)))
                        {
                            UUID roleUUID;
                            if (!UUID.TryParse(role, out roleUUID) &&
                                !RoleNameToRoleUUID(role, groupUUID,
                                    Configuration.SERVICES_TIMEOUT, Configuration.DATA_TIMEOUT, ref roleUUID))
                            {
                                throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.ROLE_NOT_FOUND));
                            }
                            if (!roleUUIDs.Contains(roleUUID))
                            {
                                roleUUIDs.Add(roleUUID);
                            }
                        }
                        // No roles specified, so assume everyone role.
                        if (roleUUIDs.Count.Equals(0))
                        {
                            roleUUIDs.Add(UUID.Zero);
                        }
                        if (!roleUUIDs.All(o => o.Equals(UUID.Zero)) &&
                            !HasGroupPowers(Client.Self.AgentID, groupUUID, GroupPowers.AssignMember,
                                Configuration.SERVICES_TIMEOUT, Configuration.DATA_TIMEOUT))
                        {
                            throw new Exception(
                                wasGetDescriptionFromEnumValue(ScriptError.NO_GROUP_POWER_FOR_COMMAND));
                        }
                        Client.Groups.Invite(groupUUID, roleUUIDs.ToList(), agentUUID);
                    };
                    break;
                case ScriptKeys.REPLYTOGROUPINVITE:
                    execute = () =>
                    {
                        if (!HasCorrodePermission(group, (int) Permissions.PERMISSION_GROUP))
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.NO_CORRADE_PERMISSIONS));
                        }
                        uint action =
                            (uint) wasGetEnumValueFromDescription<Action>(
                                wasInput(
                                    wasKeyValueGet(wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.ACTION)), message))
                                    .ToLowerInvariant());
                        Group configuredGroup =
                            Configuration.GROUPS.AsParallel().FirstOrDefault(
                                o => o.Name.Equals(group, StringComparison.Ordinal));
                        UUID groupUUID = UUID.Zero;
                        switch (!configuredGroup.Equals(default(Group)))
                        {
                            case true:
                                groupUUID = configuredGroup.UUID;
                                break;
                            default:
                                if (!GroupNameToUUID(group, Configuration.SERVICES_TIMEOUT, Configuration.DATA_TIMEOUT,
                                    ref groupUUID))
                                {
                                    throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.GROUP_NOT_FOUND));
                                }
                                break;
                        }
                        if (AgentInGroup(Client.Self.AgentID, groupUUID, Configuration.SERVICES_TIMEOUT,
                            Configuration.DATA_TIMEOUT))
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.ALREADY_IN_GROUP));
                        }
                        UUID sessionUUID;
                        if (
                            !UUID.TryParse(
                                wasInput(
                                    wasKeyValueGet(wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.SESSION)),
                                        message)),
                                out sessionUUID))
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.NO_SESSION_SPECIFIED));
                        }
                        lock (GroupInviteLock)
                        {
                            if (!GroupInvites.AsParallel().Any(o => o.Session.Equals(sessionUUID)))
                            {
                                throw new Exception(
                                    wasGetDescriptionFromEnumValue(ScriptError.UNKNOWN_GROUP_INVITE_SESSION));
                            }
                        }
                        int amount;
                        lock (GroupInviteLock)
                        {
                            GroupInvite groupInvite =
                                GroupInvites.AsParallel().FirstOrDefault(o => o.Session.Equals(sessionUUID));
                            if (groupInvite.Equals(default(GroupInvite)))
                                throw new Exception(
                                    wasGetDescriptionFromEnumValue(ScriptError.UNKNOWN_GROUP_INVITE_SESSION));
                            amount = groupInvite.Fee;
                        }
                        if (!amount.Equals(0) && action.Equals((uint) Action.ACCEPT))
                        {
                            if (!HasCorrodePermission(group, (int) Permissions.PERMISSION_ECONOMY))
                            {
                                throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.NO_CORRADE_PERMISSIONS));
                            }
                            if (!UpdateBalance(Configuration.SERVICES_TIMEOUT))
                            {
                                throw new Exception(
                                    wasGetDescriptionFromEnumValue(ScriptError.UNABLE_TO_OBTAIN_MONEY_BALANCE));
                            }
                            if (Client.Self.Balance < amount)
                            {
                                throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.INSUFFICIENT_FUNDS));
                            }
                        }
                        Client.Self.GroupInviteRespond(groupUUID, sessionUUID,
                            action.Equals((uint) Action.ACCEPT));
                    };
                    break;
                case ScriptKeys.GETGROUPINVITES:
                    execute = () =>
                    {
                        if (!HasCorrodePermission(group, (int) Permissions.PERMISSION_GROUP))
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.NO_CORRADE_PERMISSIONS));
                        }
                        List<string> csv = new List<string>();
                        object LockObject = new object();
                        lock (GroupInviteLock)
                        {
                            Parallel.ForEach(GroupInvites, o =>
                            {
                                lock (LockObject)
                                {
                                    csv.AddRange(new[]
                                    {wasGetStructureMemberDescription(o.Agent, o.Agent.FirstName), o.Agent.FirstName});
                                    csv.AddRange(new[]
                                    {wasGetStructureMemberDescription(o.Agent, o.Agent.LastName), o.Agent.LastName});
                                    csv.AddRange(new[]
                                    {wasGetStructureMemberDescription(o.Agent, o.Agent.UUID), o.Agent.UUID.ToString()});
                                    csv.AddRange(new[] {wasGetStructureMemberDescription(o, o.Group), o.Group});
                                    csv.AddRange(new[]
                                    {wasGetStructureMemberDescription(o, o.Session), o.Session.ToString()});
                                    csv.AddRange(new[]
                                    {
                                        wasGetStructureMemberDescription(o, o.Fee),
                                        o.Fee.ToString(CultureInfo.InvariantCulture)
                                    });
                                }
                            });
                        }
                        if (!csv.Count.Equals(0))
                        {
                            result.Add(wasGetDescriptionFromEnumValue(ResultKeys.DATA),
                                wasEnumerableToCSV(csv));
                        }
                    };
                    break;
                case ScriptKeys.EJECT:
                    execute = () =>
                    {
                        if (!HasCorrodePermission(group, (int) Permissions.PERMISSION_GROUP))
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.NO_CORRADE_PERMISSIONS));
                        }
                        Group configuredGroup =
                            Configuration.GROUPS.AsParallel().FirstOrDefault(
                                o => o.Name.Equals(group, StringComparison.Ordinal));
                        UUID groupUUID = UUID.Zero;
                        switch (!configuredGroup.Equals(default(Group)))
                        {
                            case true:
                                groupUUID = configuredGroup.UUID;
                                break;
                            default:
                                if (!GroupNameToUUID(group, Configuration.SERVICES_TIMEOUT, Configuration.DATA_TIMEOUT,
                                    ref groupUUID))
                                {
                                    throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.GROUP_NOT_FOUND));
                                }
                                break;
                        }
                        if (
                            !HasGroupPowers(Client.Self.AgentID, groupUUID, GroupPowers.Eject,
                                Configuration.SERVICES_TIMEOUT, Configuration.DATA_TIMEOUT) ||
                            !HasGroupPowers(Client.Self.AgentID, groupUUID, GroupPowers.RemoveMember,
                                Configuration.SERVICES_TIMEOUT, Configuration.DATA_TIMEOUT))
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.NO_GROUP_POWER_FOR_COMMAND));
                        }
                        UUID agentUUID;
                        if (
                            !UUID.TryParse(
                                wasInput(wasKeyValueGet(
                                    wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.AGENT)), message)),
                                out agentUUID) && !AgentNameToUUID(
                                    wasInput(
                                        wasKeyValueGet(wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.FIRSTNAME)),
                                            message)),
                                    wasInput(
                                        wasKeyValueGet(wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.LASTNAME)),
                                            message)),
                                    Configuration.SERVICES_TIMEOUT, Configuration.DATA_TIMEOUT, ref agentUUID))
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.AGENT_NOT_FOUND));
                        }
                        if (
                            !AgentInGroup(agentUUID, groupUUID, Configuration.SERVICES_TIMEOUT,
                                Configuration.DATA_TIMEOUT))
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.NOT_IN_GROUP));
                        }
                        OpenMetaverse.Group commandGroup = new OpenMetaverse.Group();
                        if (!RequestGroup(groupUUID, Configuration.SERVICES_TIMEOUT, ref commandGroup))
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.GROUP_NOT_FOUND));
                        }
                        ManualResetEvent GroupRoleMembersReplyEvent = new ManualResetEvent(false);
                        EventHandler<GroupRolesMembersReplyEventArgs> GroupRoleMembersEventHandler = (sender, args) =>
                        {
                            if (args.RolesMembers.AsParallel().Any(
                                o => o.Key.Equals(commandGroup.OwnerRole) && o.Value.Equals(agentUUID)))
                            {
                                throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.CANNOT_EJECT_OWNERS));
                            }
                            Parallel.ForEach(
                                args.RolesMembers.AsParallel().Where(
                                    o => o.Value.Equals(agentUUID)),
                                o => Client.Groups.RemoveFromRole(groupUUID, o.Key, agentUUID));
                            GroupRoleMembersReplyEvent.Set();
                        };
                        lock (ClientInstanceGroupsLock)
                        {
                            Client.Groups.GroupRoleMembersReply += GroupRoleMembersEventHandler;
                            Client.Groups.RequestGroupRolesMembers(groupUUID);
                            if (!GroupRoleMembersReplyEvent.WaitOne(Configuration.SERVICES_TIMEOUT, false))
                            {
                                Client.Groups.GroupRoleMembersReply -= GroupRoleMembersEventHandler;
                                throw new Exception(
                                    wasGetDescriptionFromEnumValue(ScriptError.TIMEOUT_GETTING_GROUP_ROLE_MEMBERS));
                            }
                            Client.Groups.GroupRoleMembersReply -= GroupRoleMembersEventHandler;
                        }
                        ManualResetEvent GroupEjectEvent = new ManualResetEvent(false);
                        bool succeeded = false;
                        EventHandler<GroupOperationEventArgs> GroupOperationEventHandler = (sender, args) =>
                        {
                            succeeded = args.Success;
                            GroupEjectEvent.Set();
                        };
                        lock (ClientInstanceGroupsLock)
                        {
                            Client.Groups.GroupMemberEjected += GroupOperationEventHandler;
                            Client.Groups.EjectUser(groupUUID, agentUUID);
                            if (!GroupEjectEvent.WaitOne(Configuration.SERVICES_TIMEOUT, false))
                            {
                                Client.Groups.GroupMemberEjected -= GroupOperationEventHandler;
                                throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.TIMEOUT_EJECTING_AGENT));
                            }
                            Client.Groups.GroupMemberEjected -= GroupOperationEventHandler;
                        }
                        if (!succeeded)
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.COULD_NOT_EJECT_AGENT));
                        }
                    };
                    break;
                case ScriptKeys.GETGROUPACCOUNTSUMMARYDATA:
                    execute = () =>
                    {
                        if (!HasCorrodePermission(group, (int) Permissions.PERMISSION_GROUP))
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.NO_CORRADE_PERMISSIONS));
                        }
                        Group configuredGroup =
                            Configuration.GROUPS.AsParallel().FirstOrDefault(
                                o => o.Name.Equals(group, StringComparison.Ordinal));
                        UUID groupUUID = UUID.Zero;
                        switch (!configuredGroup.Equals(default(Group)))
                        {
                            case true:
                                groupUUID = configuredGroup.UUID;
                                break;
                            default:
                                if (!GroupNameToUUID(group, Configuration.SERVICES_TIMEOUT, Configuration.DATA_TIMEOUT,
                                    ref groupUUID))
                                {
                                    throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.GROUP_NOT_FOUND));
                                }
                                break;
                        }
                        int days;
                        if (
                            !int.TryParse(
                                wasInput(wasKeyValueGet(
                                    wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.DAYS)), message)),
                                out days))
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.INVALID_DAYS));
                        }
                        int interval;
                        if (
                            !int.TryParse(
                                wasInput(
                                    wasKeyValueGet(wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.INTERVAL)),
                                        message)),
                                out interval))
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.INVALID_INTERVAL));
                        }
                        ManualResetEvent RequestGroupAccountSummaryEvent = new ManualResetEvent(false);
                        GroupAccountSummary summary = new GroupAccountSummary();
                        EventHandler<GroupAccountSummaryReplyEventArgs> RequestGroupAccountSummaryEventHandler =
                            (sender, args) =>
                            {
                                summary = args.Summary;
                                RequestGroupAccountSummaryEvent.Set();
                            };
                        lock (ClientInstanceGroupsLock)
                        {
                            Client.Groups.GroupAccountSummaryReply += RequestGroupAccountSummaryEventHandler;
                            Client.Groups.RequestGroupAccountSummary(groupUUID, days, interval);
                            if (!RequestGroupAccountSummaryEvent.WaitOne(Configuration.SERVICES_TIMEOUT, false))
                            {
                                Client.Groups.GroupAccountSummaryReply -= RequestGroupAccountSummaryEventHandler;
                                throw new Exception(
                                    wasGetDescriptionFromEnumValue(ScriptError.TIMEOUT_GETTING_GROUP_ACCOUNT_SUMMARY));
                            }
                            Client.Groups.GroupAccountSummaryReply -= RequestGroupAccountSummaryEventHandler;
                        }
                        List<string> data = new List<string>(GetStructuredData(summary,
                            wasInput(wasKeyValueGet(wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.DATA)),
                                message)))
                            );
                        if (!data.Count.Equals(0))
                        {
                            result.Add(wasGetDescriptionFromEnumValue(ResultKeys.DATA),
                                wasEnumerableToCSV(data));
                        }
                    };
                    break;
                case ScriptKeys.UPDATEGROUPDATA:
                    execute = () =>
                    {
                        if (!HasCorrodePermission(group, (int) Permissions.PERMISSION_GROUP))
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.NO_CORRADE_PERMISSIONS));
                        }
                        Group configuredGroup =
                            Configuration.GROUPS.AsParallel().FirstOrDefault(
                                o => o.Name.Equals(group, StringComparison.Ordinal));
                        UUID groupUUID = UUID.Zero;
                        switch (!configuredGroup.Equals(default(Group)))
                        {
                            case true:
                                groupUUID = configuredGroup.UUID;
                                break;
                            default:
                                if (!GroupNameToUUID(group, Configuration.SERVICES_TIMEOUT, Configuration.DATA_TIMEOUT,
                                    ref groupUUID))
                                {
                                    throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.GROUP_NOT_FOUND));
                                }
                                break;
                        }
                        if (
                            !AgentInGroup(Client.Self.AgentID, groupUUID, Configuration.SERVICES_TIMEOUT,
                                Configuration.DATA_TIMEOUT))
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.NOT_IN_GROUP));
                        }
                        if (
                            !HasGroupPowers(Client.Self.AgentID, groupUUID, GroupPowers.ChangeIdentity,
                                Configuration.SERVICES_TIMEOUT, Configuration.DATA_TIMEOUT))
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.NO_GROUP_POWER_FOR_COMMAND));
                        }
                        OpenMetaverse.Group commandGroup = new OpenMetaverse.Group();
                        if (!RequestGroup(groupUUID, Configuration.SERVICES_TIMEOUT, ref commandGroup))
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.GROUP_NOT_FOUND));
                        }
                        wasCSVToStructure(
                            wasInput(wasKeyValueGet(wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.DATA)),
                                message)),
                            ref commandGroup);
                        Client.Groups.UpdateGroup(groupUUID, commandGroup);
                    };
                    break;
                case ScriptKeys.LEAVE:
                    execute = () =>
                    {
                        if (!HasCorrodePermission(group, (int) Permissions.PERMISSION_GROUP))
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.NO_CORRADE_PERMISSIONS));
                        }
                        Group configuredGroup =
                            Configuration.GROUPS.AsParallel().FirstOrDefault(
                                o => o.Name.Equals(group, StringComparison.Ordinal));
                        UUID groupUUID = UUID.Zero;
                        switch (!configuredGroup.Equals(default(Group)))
                        {
                            case true:
                                groupUUID = configuredGroup.UUID;
                                break;
                            default:
                                if (!GroupNameToUUID(group, Configuration.SERVICES_TIMEOUT, Configuration.DATA_TIMEOUT,
                                    ref groupUUID))
                                {
                                    throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.GROUP_NOT_FOUND));
                                }
                                break;
                        }
                        if (
                            !AgentInGroup(Client.Self.AgentID, groupUUID, Configuration.SERVICES_TIMEOUT,
                                Configuration.DATA_TIMEOUT))
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.NOT_IN_GROUP));
                        }
                        ManualResetEvent GroupLeaveReplyEvent = new ManualResetEvent(false);
                        bool succeeded = false;
                        EventHandler<GroupOperationEventArgs> GroupOperationEventHandler = (sender, args) =>
                        {
                            succeeded = args.Success;
                            GroupLeaveReplyEvent.Set();
                        };
                        lock (ClientInstanceGroupsLock)
                        {
                            Client.Groups.GroupLeaveReply += GroupOperationEventHandler;
                            Client.Groups.LeaveGroup(groupUUID);
                            if (!GroupLeaveReplyEvent.WaitOne(Configuration.SERVICES_TIMEOUT, false))
                            {
                                Client.Groups.GroupLeaveReply -= GroupOperationEventHandler;
                                throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.TIMEOUT_LEAVING_GROUP));
                            }
                            Client.Groups.GroupLeaveReply -= GroupOperationEventHandler;
                        }
                        if (!succeeded)
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.COULD_NOT_LEAVE_GROUP));
                        }
                    };
                    break;
                case ScriptKeys.CREATEROLE:
                    execute = () =>
                    {
                        if (!HasCorrodePermission(group, (int) Permissions.PERMISSION_GROUP))
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.NO_CORRADE_PERMISSIONS));
                        }
                        Group configuredGroup =
                            Configuration.GROUPS.AsParallel().FirstOrDefault(
                                o => o.Name.Equals(group, StringComparison.Ordinal));
                        UUID groupUUID = UUID.Zero;
                        switch (!configuredGroup.Equals(default(Group)))
                        {
                            case true:
                                groupUUID = configuredGroup.UUID;
                                break;
                            default:
                                if (!GroupNameToUUID(group, Configuration.SERVICES_TIMEOUT, Configuration.DATA_TIMEOUT,
                                    ref groupUUID))
                                {
                                    throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.GROUP_NOT_FOUND));
                                }
                                break;
                        }
                        if (
                            !AgentInGroup(Client.Self.AgentID, groupUUID, Configuration.SERVICES_TIMEOUT,
                                Configuration.DATA_TIMEOUT))
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.NOT_IN_GROUP));
                        }
                        if (
                            !HasGroupPowers(Client.Self.AgentID, groupUUID, GroupPowers.CreateRole,
                                Configuration.SERVICES_TIMEOUT, Configuration.DATA_TIMEOUT))
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.NO_GROUP_POWER_FOR_COMMAND));
                        }
                        ManualResetEvent GroupRoleDataReplyEvent = new ManualResetEvent(false);
                        int roleCount = 0;
                        EventHandler<GroupRolesDataReplyEventArgs> GroupRolesDataEventHandler = (sender, args) =>
                        {
                            roleCount = args.Roles.Count;
                            GroupRoleDataReplyEvent.Set();
                        };
                        lock (ClientInstanceGroupsLock)
                        {
                            Client.Groups.GroupRoleDataReply += GroupRolesDataEventHandler;
                            Client.Groups.RequestGroupRoles(groupUUID);
                            if (!GroupRoleDataReplyEvent.WaitOne(Configuration.SERVICES_TIMEOUT, false))
                            {
                                Client.Groups.GroupRoleDataReply -= GroupRolesDataEventHandler;
                                throw new Exception(
                                    wasGetDescriptionFromEnumValue(ScriptError.TIMEOUT_GETTING_GROUP_ROLES));
                            }
                            Client.Groups.GroupRoleDataReply -= GroupRolesDataEventHandler;
                        }
                        if (roleCount >= LINDEN_CONSTANTS.GROUPS.MAXIMUM_NUMBER_OF_ROLES)
                        {
                            throw new Exception(
                                wasGetDescriptionFromEnumValue(ScriptError.MAXIMUM_NUMBER_OF_ROLES_EXCEEDED));
                        }
                        string role =
                            wasInput(wasKeyValueGet(wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.ROLE)),
                                message));
                        if (string.IsNullOrEmpty(role))
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.NO_ROLE_NAME_SPECIFIED));
                        }
                        ulong powers = 0;
                        Parallel.ForEach(wasCSVToEnumerable(
                            wasInput(wasKeyValueGet(wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.POWERS)),
                                message))),
                            o =>
                                Parallel.ForEach(
                                    typeof (GroupPowers).GetFields(BindingFlags.Public | BindingFlags.Static)
                                        .AsParallel().Where(p => p.Name.Equals(o, StringComparison.Ordinal)),
                                    q => { powers |= ((ulong) q.GetValue(null)); }));
                        if (!HasGroupPowers(Client.Self.AgentID, groupUUID, GroupPowers.ChangeActions,
                            Configuration.SERVICES_TIMEOUT, Configuration.DATA_TIMEOUT))
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.NO_GROUP_POWER_FOR_COMMAND));
                        }
                        Client.Groups.CreateRole(groupUUID, new GroupRole
                        {
                            Name = role,
                            Description =
                                wasInput(
                                    wasKeyValueGet(wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.DESCRIPTION)),
                                        message)),
                            GroupID = groupUUID,
                            ID = UUID.Random(),
                            Powers = (GroupPowers) powers,
                            Title =
                                wasInput(wasKeyValueGet(
                                    wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.TITLE)), message))
                        });
                        UUID roleUUID = UUID.Zero;
                        if (
                            !RoleNameToRoleUUID(role, groupUUID,
                                Configuration.SERVICES_TIMEOUT, Configuration.DATA_TIMEOUT, ref roleUUID))
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.COULD_NOT_CREATE_ROLE));
                        }
                    };
                    break;
                case ScriptKeys.GETROLES:
                    execute = () =>
                    {
                        if (!HasCorrodePermission(group, (int) Permissions.PERMISSION_GROUP))
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.NO_CORRADE_PERMISSIONS));
                        }
                        Group configuredGroup =
                            Configuration.GROUPS.AsParallel().FirstOrDefault(
                                o => o.Name.Equals(group, StringComparison.Ordinal));
                        UUID groupUUID = UUID.Zero;
                        switch (!configuredGroup.Equals(default(Group)))
                        {
                            case true:
                                groupUUID = configuredGroup.UUID;
                                break;
                            default:
                                if (!GroupNameToUUID(group, Configuration.SERVICES_TIMEOUT, Configuration.DATA_TIMEOUT,
                                    ref groupUUID))
                                {
                                    throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.GROUP_NOT_FOUND));
                                }
                                break;
                        }
                        if (
                            !AgentInGroup(Client.Self.AgentID, groupUUID, Configuration.SERVICES_TIMEOUT,
                                Configuration.DATA_TIMEOUT))
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.NOT_IN_GROUP));
                        }
                        ManualResetEvent GroupRoleDataReplyEvent = new ManualResetEvent(false);
                        List<string> csv = new List<string>();
                        EventHandler<GroupRolesDataReplyEventArgs> GroupRolesDataEventHandler = (sender, args) =>
                        {
                            csv.AddRange(args.Roles.AsParallel().Select(o => new[]
                            {
                                o.Value.Name,
                                o.Value.ID.ToString(),
                                o.Value.Title,
                                o.Value.Description
                            }).SelectMany(o => o));
                            GroupRoleDataReplyEvent.Set();
                        };
                        lock (ClientInstanceGroupsLock)
                        {
                            Client.Groups.GroupRoleDataReply += GroupRolesDataEventHandler;
                            Client.Groups.RequestGroupRoles(groupUUID);
                            if (!GroupRoleDataReplyEvent.WaitOne(Configuration.SERVICES_TIMEOUT, false))
                            {
                                Client.Groups.GroupRoleDataReply -= GroupRolesDataEventHandler;
                                throw new Exception(
                                    wasGetDescriptionFromEnumValue(ScriptError.TIMEOUT_GETTING_GROUP_ROLES));
                            }
                            Client.Groups.GroupRoleDataReply -= GroupRolesDataEventHandler;
                        }
                        if (!csv.Count.Equals(0))
                        {
                            result.Add(wasGetDescriptionFromEnumValue(ResultKeys.DATA),
                                wasEnumerableToCSV(csv));
                        }
                    };
                    break;
                case ScriptKeys.GETMEMBERS:
                    execute = () =>
                    {
                        if (!HasCorrodePermission(group, (int) Permissions.PERMISSION_GROUP))
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.NO_CORRADE_PERMISSIONS));
                        }
                        Group configuredGroup =
                            Configuration.GROUPS.AsParallel().FirstOrDefault(
                                o => o.Name.Equals(group, StringComparison.Ordinal));
                        UUID groupUUID = UUID.Zero;
                        switch (!configuredGroup.Equals(default(Group)))
                        {
                            case true:
                                groupUUID = configuredGroup.UUID;
                                break;
                            default:
                                if (!GroupNameToUUID(group, Configuration.SERVICES_TIMEOUT, Configuration.DATA_TIMEOUT,
                                    ref groupUUID))
                                {
                                    throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.GROUP_NOT_FOUND));
                                }
                                break;
                        }
                        if (
                            !AgentInGroup(Client.Self.AgentID, groupUUID,
                                Configuration.SERVICES_TIMEOUT, Configuration.DATA_TIMEOUT))
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.NOT_IN_GROUP));
                        }
                        ManualResetEvent agentInGroupEvent = new ManualResetEvent(false);
                        List<string> csv = new List<string>();
                        EventHandler<GroupMembersReplyEventArgs> HandleGroupMembersReplyDelegate = (sender, args) =>
                        {
                            foreach (KeyValuePair<UUID, GroupMember> pair in args.Members)
                            {
                                string agentName = string.Empty;
                                if (
                                    !AgentUUIDToName(pair.Value.ID, Configuration.SERVICES_TIMEOUT, ref agentName))
                                    continue;
                                csv.Add(agentName);
                                csv.Add(pair.Key.ToString());
                            }
                            agentInGroupEvent.Set();
                        };
                        lock (ClientInstanceGroupsLock)
                        {
                            Client.Groups.GroupMembersReply += HandleGroupMembersReplyDelegate;
                            Client.Groups.RequestGroupMembers(groupUUID);
                            if (!agentInGroupEvent.WaitOne(Configuration.SERVICES_TIMEOUT, false))
                            {
                                Client.Groups.GroupMembersReply -= HandleGroupMembersReplyDelegate;
                                throw new Exception(
                                    wasGetDescriptionFromEnumValue(ScriptError.TIMEOUT_GETTING_GROUP_MEMBERS));
                            }
                            Client.Groups.GroupMembersReply -= HandleGroupMembersReplyDelegate;
                        }
                        if (!csv.Count.Equals(0))
                        {
                            result.Add(wasGetDescriptionFromEnumValue(ResultKeys.DATA),
                                wasEnumerableToCSV(csv));
                        }
                    };
                    break;
                case ScriptKeys.GETMEMBERROLES:
                    execute = () =>
                    {
                        if (!HasCorrodePermission(group, (int) Permissions.PERMISSION_GROUP))
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.NO_CORRADE_PERMISSIONS));
                        }
                        Group configuredGroup =
                            Configuration.GROUPS.AsParallel().FirstOrDefault(
                                o => o.Name.Equals(group, StringComparison.Ordinal));
                        UUID groupUUID = UUID.Zero;
                        switch (!configuredGroup.Equals(default(Group)))
                        {
                            case true:
                                groupUUID = configuredGroup.UUID;
                                break;
                            default:
                                if (!GroupNameToUUID(group, Configuration.SERVICES_TIMEOUT, Configuration.DATA_TIMEOUT,
                                    ref groupUUID))
                                {
                                    throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.GROUP_NOT_FOUND));
                                }
                                break;
                        }
                        if (
                            !AgentInGroup(Client.Self.AgentID, groupUUID, Configuration.SERVICES_TIMEOUT,
                                Configuration.DATA_TIMEOUT))
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.NOT_IN_GROUP));
                        }
                        UUID agentUUID;
                        if (
                            !UUID.TryParse(
                                wasInput(wasKeyValueGet(
                                    wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.AGENT)), message)),
                                out agentUUID) && !AgentNameToUUID(
                                    wasInput(
                                        wasKeyValueGet(wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.FIRSTNAME)),
                                            message)),
                                    wasInput(
                                        wasKeyValueGet(wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.LASTNAME)),
                                            message)),
                                    Configuration.SERVICES_TIMEOUT, Configuration.DATA_TIMEOUT, ref agentUUID))
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.AGENT_NOT_FOUND));
                        }
                        if (
                            !AgentInGroup(agentUUID, groupUUID, Configuration.SERVICES_TIMEOUT,
                                Configuration.DATA_TIMEOUT))
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.AGENT_NOT_IN_GROUP));
                        }
                        HashSet<string> csv = new HashSet<string>();
                        // get roles for a member
                        ManualResetEvent GroupRoleMembersReplyEvent = new ManualResetEvent(false);
                        EventHandler<GroupRolesMembersReplyEventArgs> GroupRolesMembersEventHandler = (sender, args) =>
                        {
                            foreach (
                                KeyValuePair<UUID, UUID> pair in
                                    args.RolesMembers.AsParallel().Where(o => o.Value.Equals(agentUUID))
                                )
                            {
                                string roleName = string.Empty;
                                if (
                                    !RoleUUIDToName(pair.Key, groupUUID, Configuration.SERVICES_TIMEOUT,
                                        Configuration.DATA_TIMEOUT,
                                        ref roleName))
                                    continue;
                                csv.Add(roleName);
                            }
                            GroupRoleMembersReplyEvent.Set();
                        };
                        lock (ClientInstanceGroupsLock)
                        {
                            Client.Groups.GroupRoleMembersReply += GroupRolesMembersEventHandler;
                            Client.Groups.RequestGroupRolesMembers(groupUUID);
                            if (!GroupRoleMembersReplyEvent.WaitOne(Configuration.SERVICES_TIMEOUT, false))
                            {
                                Client.Groups.GroupRoleMembersReply -= GroupRolesMembersEventHandler;
                                throw new Exception(
                                    wasGetDescriptionFromEnumValue(ScriptError.TIMEOUT_GETING_GROUP_ROLES_MEMBERS));
                            }
                            Client.Groups.GroupRoleMembersReply -= GroupRolesMembersEventHandler;
                        }
                        if (!csv.Count.Equals(0))
                        {
                            result.Add(wasGetDescriptionFromEnumValue(ResultKeys.DATA),
                                wasEnumerableToCSV(csv));
                        }
                    };
                    break;
                case ScriptKeys.GETROLEMEMBERS:
                    execute = () =>
                    {
                        if (!HasCorrodePermission(group, (int) Permissions.PERMISSION_GROUP))
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.NO_CORRADE_PERMISSIONS));
                        }
                        Group configuredGroup =
                            Configuration.GROUPS.AsParallel().FirstOrDefault(
                                o => o.Name.Equals(group, StringComparison.Ordinal));
                        UUID groupUUID = UUID.Zero;
                        switch (!configuredGroup.Equals(default(Group)))
                        {
                            case true:
                                groupUUID = configuredGroup.UUID;
                                break;
                            default:
                                if (!GroupNameToUUID(group, Configuration.SERVICES_TIMEOUT, Configuration.DATA_TIMEOUT,
                                    ref groupUUID))
                                {
                                    throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.GROUP_NOT_FOUND));
                                }
                                break;
                        }
                        if (
                            !AgentInGroup(Client.Self.AgentID, groupUUID,
                                Configuration.SERVICES_TIMEOUT, Configuration.DATA_TIMEOUT))
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.NOT_IN_GROUP));
                        }
                        string role =
                            wasInput(wasKeyValueGet(wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.ROLE)),
                                message));
                        if (string.IsNullOrEmpty(role))
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.NO_ROLE_NAME_SPECIFIED));
                        }
                        UUID roleUUID;
                        if (!UUID.TryParse(role, out roleUUID) && !RoleNameToRoleUUID(role, groupUUID,
                            Configuration.SERVICES_TIMEOUT, Configuration.DATA_TIMEOUT,
                            ref roleUUID))
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.ROLE_NOT_FOUND));
                        }
                        List<string> csv = new List<string>();
                        ManualResetEvent GroupRoleMembersReplyEvent = new ManualResetEvent(false);
                        EventHandler<GroupRolesMembersReplyEventArgs> GroupRolesMembersEventHandler =
                            (sender, args) =>
                            {
                                foreach (
                                    KeyValuePair<UUID, UUID> pair in
                                        args.RolesMembers.AsParallel().Where(o => o.Key.Equals(roleUUID)))
                                {
                                    string agentName = string.Empty;
                                    if (
                                        !AgentUUIDToName(pair.Value, Configuration.SERVICES_TIMEOUT, ref agentName))
                                        continue;
                                    csv.Add(agentName);
                                    csv.Add(pair.Value.ToString());
                                }
                                GroupRoleMembersReplyEvent.Set();
                            };
                        lock (ClientInstanceGroupsLock)
                        {
                            Client.Groups.GroupRoleMembersReply += GroupRolesMembersEventHandler;
                            Client.Groups.RequestGroupRolesMembers(groupUUID);
                            if (!GroupRoleMembersReplyEvent.WaitOne(Configuration.SERVICES_TIMEOUT, false))
                            {
                                Client.Groups.GroupRoleMembersReply -= GroupRolesMembersEventHandler;
                                throw new Exception(
                                    wasGetDescriptionFromEnumValue(ScriptError.TIMEOUT_GETING_GROUP_ROLES_MEMBERS));
                            }
                            Client.Groups.GroupRoleMembersReply -= GroupRolesMembersEventHandler;
                        }
                        if (!csv.Count.Equals(0))
                        {
                            result.Add(wasGetDescriptionFromEnumValue(ResultKeys.DATA),
                                wasEnumerableToCSV(csv));
                        }
                    };
                    break;
                case ScriptKeys.GETROLESMEMBERS:
                    execute = () =>
                    {
                        if (!HasCorrodePermission(group, (int) Permissions.PERMISSION_GROUP))
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.NO_CORRADE_PERMISSIONS));
                        }
                        Group configuredGroup =
                            Configuration.GROUPS.AsParallel().FirstOrDefault(
                                o => o.Name.Equals(group, StringComparison.Ordinal));
                        UUID groupUUID = UUID.Zero;
                        switch (!configuredGroup.Equals(default(Group)))
                        {
                            case true:
                                groupUUID = configuredGroup.UUID;
                                break;
                            default:
                                if (!GroupNameToUUID(group, Configuration.SERVICES_TIMEOUT, Configuration.DATA_TIMEOUT,
                                    ref groupUUID))
                                {
                                    throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.GROUP_NOT_FOUND));
                                }
                                break;
                        }
                        if (
                            !AgentInGroup(Client.Self.AgentID, groupUUID,
                                Configuration.SERVICES_TIMEOUT, Configuration.DATA_TIMEOUT))
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.NOT_IN_GROUP));
                        }
                        List<string> csv = new List<string>();
                        ManualResetEvent GroupRoleMembersReplyEvent = new ManualResetEvent(false);
                        EventHandler<GroupRolesMembersReplyEventArgs> GroupRolesMembersEventHandler =
                            (sender, args) =>
                            {
                                // First resolve the all the role names to role UUIDs
                                Hashtable roleUUIDNames = new Hashtable(args.RolesMembers.Count);
                                foreach (
                                    UUID roleUUID in
                                        args.RolesMembers.AsParallel().GroupBy(o => o.Key).Select(o => o.First().Key))
                                {
                                    string roleName = string.Empty;
                                    if (
                                        !RoleUUIDToName(roleUUID, groupUUID, Configuration.SERVICES_TIMEOUT,
                                            Configuration.DATA_TIMEOUT,
                                            ref roleName))
                                        continue;
                                    roleUUIDNames.Add(roleUUID, roleName);
                                }
                                // Next, associate role names with agent names and UUIDs.
                                foreach (KeyValuePair<UUID, UUID> pair in args.RolesMembers)
                                {
                                    if (!roleUUIDNames.ContainsKey(pair.Key)) continue;
                                    string agentName = string.Empty;
                                    if (
                                        !AgentUUIDToName(pair.Value, Configuration.SERVICES_TIMEOUT, ref agentName))
                                        continue;
                                    csv.Add(roleUUIDNames[pair.Key] as string);
                                    csv.Add(agentName);
                                    csv.Add(pair.Value.ToString());
                                }
                                GroupRoleMembersReplyEvent.Set();
                            };
                        lock (ClientInstanceGroupsLock)
                        {
                            Client.Groups.GroupRoleMembersReply += GroupRolesMembersEventHandler;
                            Client.Groups.RequestGroupRolesMembers(groupUUID);
                            if (!GroupRoleMembersReplyEvent.WaitOne(Configuration.SERVICES_TIMEOUT, false))
                            {
                                Client.Groups.GroupRoleMembersReply -= GroupRolesMembersEventHandler;
                                throw new Exception(
                                    wasGetDescriptionFromEnumValue(ScriptError.TIMEOUT_GETING_GROUP_ROLES_MEMBERS));
                            }
                            Client.Groups.GroupRoleMembersReply -= GroupRolesMembersEventHandler;
                        }
                        if (!csv.Count.Equals(0))
                        {
                            result.Add(wasGetDescriptionFromEnumValue(ResultKeys.DATA),
                                wasEnumerableToCSV(csv));
                        }
                    };
                    break;
                case ScriptKeys.GETROLEPOWERS:
                    execute = () =>
                    {
                        if (!HasCorrodePermission(group, (int) Permissions.PERMISSION_GROUP))
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.NO_CORRADE_PERMISSIONS));
                        }
                        Group configuredGroup =
                            Configuration.GROUPS.AsParallel().FirstOrDefault(
                                o => o.Name.Equals(group, StringComparison.Ordinal));
                        UUID groupUUID = UUID.Zero;
                        switch (!configuredGroup.Equals(default(Group)))
                        {
                            case true:
                                groupUUID = configuredGroup.UUID;
                                break;
                            default:
                                if (!GroupNameToUUID(group, Configuration.SERVICES_TIMEOUT, Configuration.DATA_TIMEOUT,
                                    ref groupUUID))
                                {
                                    throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.GROUP_NOT_FOUND));
                                }
                                break;
                        }
                        if (
                            !HasGroupPowers(Client.Self.AgentID, groupUUID, GroupPowers.RoleProperties,
                                Configuration.SERVICES_TIMEOUT, Configuration.DATA_TIMEOUT))
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.NO_GROUP_POWER_FOR_COMMAND));
                        }
                        if (
                            !AgentInGroup(Client.Self.AgentID, groupUUID,
                                Configuration.SERVICES_TIMEOUT, Configuration.DATA_TIMEOUT))
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.NOT_IN_GROUP));
                        }
                        string role =
                            wasInput(wasKeyValueGet(wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.ROLE)),
                                message));
                        UUID roleUUID;
                        if (!UUID.TryParse(role, out roleUUID) && !RoleNameToRoleUUID(role, groupUUID,
                            Configuration.SERVICES_TIMEOUT, Configuration.DATA_TIMEOUT,
                            ref roleUUID))
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.ROLE_NOT_FOUND));
                        }
                        HashSet<string> csv = new HashSet<string>();
                        ManualResetEvent GroupRoleDataReplyEvent = new ManualResetEvent(false);
                        EventHandler<GroupRolesDataReplyEventArgs> GroupRoleDataEventHandler = (sender, args) =>
                        {
                            GroupRole queryRole =
                                args.Roles.Values.AsParallel().FirstOrDefault(o => o.ID.Equals(roleUUID));
                            csv.UnionWith(typeof (GroupPowers).GetFields(BindingFlags.Public | BindingFlags.Static)
                                .AsParallel().Where(
                                    o =>
                                        !(((ulong) o.GetValue(null) &
                                           (ulong) queryRole.Powers)).Equals(0))
                                .Select(o => o.Name));
                            GroupRoleDataReplyEvent.Set();
                        };
                        lock (ClientInstanceGroupsLock)
                        {
                            Client.Groups.GroupRoleDataReply += GroupRoleDataEventHandler;
                            Client.Groups.RequestGroupRoles(groupUUID);
                            if (!GroupRoleDataReplyEvent.WaitOne(Configuration.SERVICES_TIMEOUT, false))
                            {
                                Client.Groups.GroupRoleDataReply -= GroupRoleDataEventHandler;
                                throw new Exception(
                                    wasGetDescriptionFromEnumValue(ScriptError.TIMEOUT_GETTING_ROLE_POWERS));
                            }
                            Client.Groups.GroupRoleDataReply -= GroupRoleDataEventHandler;
                        }
                        if (!csv.Count.Equals(0))
                        {
                            result.Add(wasGetDescriptionFromEnumValue(ResultKeys.DATA),
                                wasEnumerableToCSV(csv));
                        }
                    };
                    break;
                case ScriptKeys.DELETEROLE:
                    execute = () =>
                    {
                        if (!HasCorrodePermission(group, (int) Permissions.PERMISSION_GROUP))
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.NO_CORRADE_PERMISSIONS));
                        }
                        Group configuredGroup =
                            Configuration.GROUPS.AsParallel().FirstOrDefault(
                                o => o.Name.Equals(group, StringComparison.Ordinal));
                        UUID groupUUID = UUID.Zero;
                        switch (!configuredGroup.Equals(default(Group)))
                        {
                            case true:
                                groupUUID = configuredGroup.UUID;
                                break;
                            default:
                                if (!GroupNameToUUID(group, Configuration.SERVICES_TIMEOUT, Configuration.DATA_TIMEOUT,
                                    ref groupUUID))
                                {
                                    throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.GROUP_NOT_FOUND));
                                }
                                break;
                        }
                        if (
                            !AgentInGroup(Client.Self.AgentID, groupUUID, Configuration.SERVICES_TIMEOUT,
                                Configuration.DATA_TIMEOUT))
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.NOT_IN_GROUP));
                        }
                        if (
                            !HasGroupPowers(Client.Self.AgentID, groupUUID, GroupPowers.DeleteRole,
                                Configuration.SERVICES_TIMEOUT, Configuration.DATA_TIMEOUT) ||
                            !HasGroupPowers(Client.Self.AgentID, groupUUID, GroupPowers.RemoveMember,
                                Configuration.SERVICES_TIMEOUT, Configuration.DATA_TIMEOUT))
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.NO_GROUP_POWER_FOR_COMMAND));
                        }
                        string role =
                            wasInput(wasKeyValueGet(wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.ROLE)),
                                message));
                        UUID roleUUID;
                        if (!UUID.TryParse(role, out roleUUID) && !RoleNameToRoleUUID(role, groupUUID,
                            Configuration.SERVICES_TIMEOUT, Configuration.DATA_TIMEOUT,
                            ref roleUUID))
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.ROLE_NOT_FOUND));
                        }
                        if (roleUUID.Equals(UUID.Zero))
                        {
                            throw new Exception(
                                wasGetDescriptionFromEnumValue(ScriptError.CANNOT_DELETE_THE_EVERYONE_ROLE));
                        }
                        OpenMetaverse.Group commandGroup = new OpenMetaverse.Group();
                        if (!RequestGroup(groupUUID, Configuration.SERVICES_TIMEOUT, ref commandGroup))
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.GROUP_NOT_FOUND));
                        }
                        if (commandGroup.OwnerRole.Equals(roleUUID))
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.CANNOT_REMOVE_OWNER_ROLE));
                        }
                        // remove members from role
                        ManualResetEvent GroupRoleMembersReplyEvent = new ManualResetEvent(false);
                        EventHandler<GroupRolesMembersReplyEventArgs> GroupRolesMembersEventHandler = (sender, args) =>
                        {
                            Parallel.ForEach(args.RolesMembers.AsParallel().Where(o => o.Key.Equals(roleUUID)),
                                o => Client.Groups.RemoveFromRole(groupUUID, roleUUID, o.Value));
                            GroupRoleMembersReplyEvent.Set();
                        };
                        lock (ClientInstanceGroupsLock)
                        {
                            Client.Groups.GroupRoleMembersReply += GroupRolesMembersEventHandler;
                            Client.Groups.RequestGroupRolesMembers(groupUUID);
                            if (!GroupRoleMembersReplyEvent.WaitOne(Configuration.SERVICES_TIMEOUT, false))
                            {
                                Client.Groups.GroupRoleMembersReply -= GroupRolesMembersEventHandler;
                                throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.TIMEOUT_EJECTING_AGENT));
                            }
                            Client.Groups.GroupRoleMembersReply -= GroupRolesMembersEventHandler;
                        }
                        Client.Groups.DeleteRole(groupUUID, roleUUID);
                    };
                    break;
                case ScriptKeys.ADDTOROLE:
                    execute = () =>
                    {
                        if (!HasCorrodePermission(group, (int) Permissions.PERMISSION_GROUP))
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.NO_CORRADE_PERMISSIONS));
                        }
                        Group configuredGroup =
                            Configuration.GROUPS.AsParallel().FirstOrDefault(
                                o => o.Name.Equals(group, StringComparison.Ordinal));
                        UUID groupUUID = UUID.Zero;
                        switch (!configuredGroup.Equals(default(Group)))
                        {
                            case true:
                                groupUUID = configuredGroup.UUID;
                                break;
                            default:
                                if (!GroupNameToUUID(group, Configuration.SERVICES_TIMEOUT, Configuration.DATA_TIMEOUT,
                                    ref groupUUID))
                                {
                                    throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.GROUP_NOT_FOUND));
                                }
                                break;
                        }
                        if (
                            !AgentInGroup(Client.Self.AgentID, groupUUID, Configuration.SERVICES_TIMEOUT,
                                Configuration.DATA_TIMEOUT))
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.NOT_IN_GROUP));
                        }
                        if (
                            !HasGroupPowers(Client.Self.AgentID, groupUUID, GroupPowers.AssignMember,
                                Configuration.SERVICES_TIMEOUT, Configuration.DATA_TIMEOUT))
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.NO_GROUP_POWER_FOR_COMMAND));
                        }
                        UUID agentUUID;
                        if (
                            !UUID.TryParse(
                                wasInput(wasKeyValueGet(
                                    wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.AGENT)), message)),
                                out agentUUID) && !AgentNameToUUID(
                                    wasInput(
                                        wasKeyValueGet(wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.FIRSTNAME)),
                                            message)),
                                    wasInput(
                                        wasKeyValueGet(wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.LASTNAME)),
                                            message)),
                                    Configuration.SERVICES_TIMEOUT, Configuration.DATA_TIMEOUT, ref agentUUID))
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.AGENT_NOT_FOUND));
                        }
                        string role =
                            wasInput(wasKeyValueGet(wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.ROLE)),
                                message));
                        UUID roleUUID;
                        if (!UUID.TryParse(role, out roleUUID) && !RoleNameToRoleUUID(role, groupUUID,
                            Configuration.SERVICES_TIMEOUT, Configuration.DATA_TIMEOUT,
                            ref roleUUID))
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.ROLE_NOT_FOUND));
                        }
                        if (roleUUID.Equals(UUID.Zero))
                        {
                            throw new Exception(
                                wasGetDescriptionFromEnumValue(
                                    ScriptError.GROUP_MEMBERS_ARE_BY_DEFAULT_IN_THE_EVERYONE_ROLE));
                        }
                        Client.Groups.AddToRole(groupUUID, roleUUID, agentUUID);
                    };
                    break;
                case ScriptKeys.DELETEFROMROLE:
                    execute = () =>
                    {
                        if (!HasCorrodePermission(group, (int) Permissions.PERMISSION_GROUP))
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.NO_CORRADE_PERMISSIONS));
                        }
                        Group configuredGroup =
                            Configuration.GROUPS.AsParallel().FirstOrDefault(
                                o => o.Name.Equals(group, StringComparison.Ordinal));
                        UUID groupUUID = UUID.Zero;
                        switch (!configuredGroup.Equals(default(Group)))
                        {
                            case true:
                                groupUUID = configuredGroup.UUID;
                                break;
                            default:
                                if (!GroupNameToUUID(group, Configuration.SERVICES_TIMEOUT, Configuration.DATA_TIMEOUT,
                                    ref groupUUID))
                                {
                                    throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.GROUP_NOT_FOUND));
                                }
                                break;
                        }
                        if (
                            !AgentInGroup(Client.Self.AgentID, groupUUID, Configuration.SERVICES_TIMEOUT,
                                Configuration.DATA_TIMEOUT))
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.NOT_IN_GROUP));
                        }
                        if (
                            !HasGroupPowers(Client.Self.AgentID, groupUUID, GroupPowers.RemoveMember,
                                Configuration.SERVICES_TIMEOUT, Configuration.DATA_TIMEOUT))
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.NO_GROUP_POWER_FOR_COMMAND));
                        }
                        UUID agentUUID;
                        if (
                            !UUID.TryParse(
                                wasInput(wasKeyValueGet(
                                    wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.AGENT)), message)),
                                out agentUUID) && !AgentNameToUUID(
                                    wasInput(
                                        wasKeyValueGet(wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.FIRSTNAME)),
                                            message)),
                                    wasInput(
                                        wasKeyValueGet(wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.LASTNAME)),
                                            message)),
                                    Configuration.SERVICES_TIMEOUT, Configuration.DATA_TIMEOUT, ref agentUUID))
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.AGENT_NOT_FOUND));
                        }
                        string role =
                            wasInput(wasKeyValueGet(wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.ROLE)),
                                message));
                        UUID roleUUID;
                        if (!UUID.TryParse(role, out roleUUID) && !RoleNameToRoleUUID(role, groupUUID,
                            Configuration.SERVICES_TIMEOUT, Configuration.DATA_TIMEOUT,
                            ref roleUUID))
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.ROLE_NOT_FOUND));
                        }
                        if (roleUUID.Equals(UUID.Zero))
                        {
                            throw new Exception(
                                wasGetDescriptionFromEnumValue(
                                    ScriptError.CANNOT_DELETE_A_GROUP_MEMBER_FROM_THE_EVERYONE_ROLE));
                        }
                        OpenMetaverse.Group commandGroup = new OpenMetaverse.Group();
                        if (!RequestGroup(groupUUID, Configuration.SERVICES_TIMEOUT, ref commandGroup))
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.GROUP_NOT_FOUND));
                        }
                        if (commandGroup.OwnerRole.Equals(roleUUID))
                        {
                            throw new Exception(
                                wasGetDescriptionFromEnumValue(ScriptError.CANNOT_REMOVE_USER_FROM_OWNER_ROLE));
                        }
                        Client.Groups.RemoveFromRole(groupUUID, roleUUID,
                            agentUUID);
                    };
                    break;
                case ScriptKeys.TELL:
                    execute = () =>
                    {
                        if (!HasCorrodePermission(group, (int) Permissions.PERMISSION_TALK))
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.NO_CORRADE_PERMISSIONS));
                        }
                        string data = wasInput(
                            wasKeyValueGet(wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.MESSAGE)),
                                message));
                        List<string> myName =
                            new List<string>(
                                GetAvatarNames(string.Join(" ", Client.Self.FirstName, Client.Self.LastName)));
                        switch (
                            wasGetEnumValueFromDescription<Entity>(
                                wasInput(
                                    wasKeyValueGet(wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.ENTITY)),
                                        message)).ToLowerInvariant()))
                        {
                            case Entity.AVATAR:
                                UUID agentUUID;
                                if (
                                    !UUID.TryParse(
                                        wasInput(
                                            wasKeyValueGet(wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.AGENT)),
                                                message)), out agentUUID) && !AgentNameToUUID(
                                                    wasInput(
                                                        wasKeyValueGet(
                                                            wasOutput(
                                                                wasGetDescriptionFromEnumValue(ScriptKeys.FIRSTNAME)),
                                                            message)),
                                                    wasInput(
                                                        wasKeyValueGet(
                                                            wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.LASTNAME)),
                                                            message)),
                                                    Configuration.SERVICES_TIMEOUT, Configuration.DATA_TIMEOUT,
                                                    ref agentUUID))
                                {
                                    throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.AGENT_NOT_FOUND));
                                }
                                Client.Self.InstantMessage(agentUUID, data);
                                // Log instant messages,
                                if (Configuration.INSTANT_MESSAGE_LOG_ENABLED)
                                {
                                    string agentName = "";
                                    if (!AgentUUIDToName(
                                        agentUUID,
                                        Configuration.SERVICES_TIMEOUT,
                                        ref agentName))
                                    {
                                        throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.AGENT_NOT_FOUND));
                                    }
                                    List<string> fullName =
                                        new List<string>(
                                            GetAvatarNames(agentName));
                                    try
                                    {
                                        lock (InstantMessageLogFileLock)
                                        {
                                            using (
                                                StreamWriter logWriter =
                                                    File.AppendText(
                                                        wasPathCombine(Configuration.INSTANT_MESSAGE_LOG_DIRECTORY,
                                                            string.Join(" ", fullName.First(), fullName.Last())) +
                                                        "." +
                                                        CORRADE_CONSTANTS.LOG_FILE_EXTENSION))
                                            {
                                                logWriter.WriteLine("[{0}] {1} {2} : {3}",
                                                    DateTime.Now.ToString(CORRADE_CONSTANTS.DATE_TIME_STAMP,
                                                        DateTimeFormatInfo.InvariantInfo), myName.First(), myName.Last(),
                                                    data);
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
                                break;
                            case Entity.GROUP:
                                Group configuredGroup =
                                    Configuration.GROUPS.AsParallel().FirstOrDefault(
                                        o => o.Name.Equals(group, StringComparison.Ordinal));
                                UUID groupUUID = UUID.Zero;
                                switch (!configuredGroup.Equals(default(Group)))
                                {
                                    case true:
                                        groupUUID = configuredGroup.UUID;
                                        break;
                                    default:
                                        if (
                                            !GroupNameToUUID(group, Configuration.SERVICES_TIMEOUT,
                                                Configuration.DATA_TIMEOUT,
                                                ref groupUUID))
                                        {
                                            throw new Exception(
                                                wasGetDescriptionFromEnumValue(ScriptError.GROUP_NOT_FOUND));
                                        }
                                        break;
                                }
                                if (
                                    !AgentInGroup(Client.Self.AgentID, groupUUID, Configuration.SERVICES_TIMEOUT,
                                        Configuration.DATA_TIMEOUT))
                                {
                                    throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.NOT_IN_GROUP));
                                }
                                if (!Client.Self.GroupChatSessions.ContainsKey(groupUUID))
                                {
                                    if (
                                        !HasGroupPowers(Client.Self.AgentID, groupUUID, GroupPowers.JoinChat,
                                            Configuration.SERVICES_TIMEOUT, Configuration.DATA_TIMEOUT))
                                    {
                                        throw new Exception(
                                            wasGetDescriptionFromEnumValue(ScriptError.NO_GROUP_POWER_FOR_COMMAND));
                                    }

                                    if (!JoinGroupChat(groupUUID, Configuration.SERVICES_TIMEOUT))
                                    {
                                        throw new Exception(
                                            wasGetDescriptionFromEnumValue(ScriptError.UNABLE_TO_JOIN_GROUP_CHAT));
                                    }
                                }
                                Client.Self.InstantMessageGroup(groupUUID, data);
                                Parallel.ForEach(
                                    Configuration.GROUPS.AsParallel().Where(
                                        o => o.UUID.Equals(groupUUID) && o.ChatLogEnabled),
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
                                                            DateTimeFormatInfo.InvariantInfo), myName.First(),
                                                        myName.Last(),
                                                        data);
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
                                break;
                            case Entity.LOCAL:
                                int chatChannel;
                                if (
                                    !int.TryParse(
                                        wasInput(
                                            wasKeyValueGet(
                                                wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.CHANNEL)),
                                                message)),
                                        out chatChannel))
                                {
                                    chatChannel = 0;
                                }
                                FieldInfo chatTypeInfo = typeof (ChatType).GetFields(BindingFlags.Public |
                                                                                     BindingFlags.Static)
                                    .AsParallel().FirstOrDefault(
                                        o =>
                                            o.Name.Equals(
                                                wasInput(
                                                    wasKeyValueGet(
                                                        wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.TYPE)),
                                                        message)),
                                                StringComparison.Ordinal));
                                ChatType chatType = chatTypeInfo != null
                                    ? (ChatType)
                                        chatTypeInfo
                                            .GetValue(null)
                                    : ChatType.Normal;
                                Client.Self.Chat(
                                    data,
                                    chatChannel,
                                    chatType);
                                // Log local chat,
                                if (Configuration.LOCAL_MESSAGE_LOG_ENABLED)
                                {
                                    List<string> fullName =
                                        new List<string>(
                                            GetAvatarNames(string.Join(" ", Client.Self.FirstName, Client.Self.LastName)));
                                    try
                                    {
                                        lock (LocalLogFileLock)
                                        {
                                            using (
                                                StreamWriter logWriter =
                                                    File.AppendText(
                                                        wasPathCombine(Configuration.LOCAL_MESSAGE_LOG_DIRECTORY,
                                                            Client.Network.CurrentSim.Name) + "." +
                                                        CORRADE_CONSTANTS.LOG_FILE_EXTENSION))
                                            {
                                                logWriter.WriteLine("[{0}] {1} {2} ({3}) : {4}",
                                                    DateTime.Now.ToString(CORRADE_CONSTANTS.DATE_TIME_STAMP,
                                                        DateTimeFormatInfo.InvariantInfo), fullName.First(),
                                                    fullName.Last(), Enum.GetName(typeof (ChatType), chatType),
                                                    data);
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
                            case Entity.ESTATE:
                                Client.Estate.EstateMessage(data);
                                break;
                            case Entity.REGION:
                                Client.Estate.SimulatorMessage(data);
                                break;
                            default:
                                throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.UNKNOWN_ENTITY));
                        }
                    };
                    break;
                case ScriptKeys.AI:
                    execute = () =>
                    {
                        if (!HasCorrodePermission(group, (int) Permissions.PERMISSION_TALK))
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.NO_CORRADE_PERMISSIONS));
                        }
                        switch (wasGetEnumValueFromDescription<Action>(
                            wasInput(
                                wasKeyValueGet(wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.ACTION)), message))
                                .ToLowerInvariant()))
                        {
                            case Action.PROCESS:
                                string request =
                                    wasInput(
                                        wasKeyValueGet(wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.MESSAGE)),
                                            message));
                                if (string.IsNullOrEmpty(request))
                                {
                                    throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.NO_MESSAGE_PROVIDED));
                                }
                                if (AIMLBot.isAcceptingUserInput)
                                {
                                    lock (AIMLBotLock)
                                    {
                                        result.Add(wasGetDescriptionFromEnumValue(ResultKeys.DATA),
                                            AIMLBot.Chat(new Request(request, AIMLBotUser, AIMLBot)).Output);
                                    }
                                }
                                break;
                            case Action.ENABLE:
                                lock (AIMLBotLock)
                                {
                                    switch (!AIMLBotBrainCompiled)
                                    {
                                        case true:
                                            new Thread(
                                                () =>
                                                {
                                                    lock (AIMLBotLock)
                                                    {
                                                        LoadChatBotFiles.Invoke();
                                                        AIMLBotConfigurationWatcher.EnableRaisingEvents = true;
                                                    }
                                                }) {IsBackground = true, Priority = ThreadPriority.BelowNormal}.Start();
                                            break;
                                        default:
                                            AIMLBotConfigurationWatcher.EnableRaisingEvents = true;
                                            AIMLBot.isAcceptingUserInput = true;
                                            break;
                                    }
                                }
                                break;
                            case Action.DISABLE:
                                lock (AIMLBotLock)
                                {
                                    AIMLBotConfigurationWatcher.EnableRaisingEvents = false;
                                    AIMLBot.isAcceptingUserInput = false;
                                }
                                break;
                            case Action.REBUILD:
                                lock (AIMLBotLock)
                                {
                                    AIMLBotConfigurationWatcher.EnableRaisingEvents = false;
                                    string AIMLBotBrain =
                                        wasPathCombine(
                                            Directory.GetCurrentDirectory(), AIML_BOT_CONSTANTS.DIRECTORY,
                                            AIML_BOT_CONSTANTS.BRAIN.DIRECTORY, AIML_BOT_CONSTANTS.BRAIN_FILE);
                                    if (File.Exists(AIMLBotBrain))
                                    {
                                        try
                                        {
                                            File.Delete(AIMLBotBrain);
                                        }
                                        catch (Exception)
                                        {
                                            throw new Exception(
                                                wasGetDescriptionFromEnumValue(ScriptError.COULD_NOT_REMOVE_BRAIN_FILE));
                                        }
                                    }
                                    LoadChatBotFiles.Invoke();
                                    AIMLBotConfigurationWatcher.EnableRaisingEvents = true;
                                }
                                break;
                            default:
                                throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.UNKNOWN_ACTION));
                        }
                    };
                    break;
                case ScriptKeys.NOTICE:
                    execute = () =>
                    {
                        if (!HasCorrodePermission(group, (int) Permissions.PERMISSION_GROUP))
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.NO_CORRADE_PERMISSIONS));
                        }
                        Group configuredGroup =
                            Configuration.GROUPS.AsParallel().FirstOrDefault(
                                o => o.Name.Equals(group, StringComparison.Ordinal));
                        UUID groupUUID = UUID.Zero;
                        switch (!configuredGroup.Equals(default(Group)))
                        {
                            case true:
                                groupUUID = configuredGroup.UUID;
                                break;
                            default:
                                if (!GroupNameToUUID(group, Configuration.SERVICES_TIMEOUT, Configuration.DATA_TIMEOUT,
                                    ref groupUUID))
                                {
                                    throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.GROUP_NOT_FOUND));
                                }
                                break;
                        }
                        if (
                            !AgentInGroup(Client.Self.AgentID, groupUUID, Configuration.SERVICES_TIMEOUT,
                                Configuration.DATA_TIMEOUT))
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.NOT_IN_GROUP));
                        }
                        if (
                            !HasGroupPowers(Client.Self.AgentID, groupUUID, GroupPowers.SendNotices,
                                Configuration.SERVICES_TIMEOUT, Configuration.DATA_TIMEOUT))
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.NO_GROUP_POWER_FOR_COMMAND));
                        }
                        GroupNotice notice = new GroupNotice
                        {
                            Message =
                                wasInput(
                                    wasKeyValueGet(wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.MESSAGE)),
                                        message)),
                            Subject =
                                wasInput(
                                    wasKeyValueGet(wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.SUBJECT)),
                                        message)),
                            OwnerID = Client.Self.AgentID
                        };
                        string item =
                            wasInput(wasKeyValueGet(wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.ITEM)),
                                message));
                        if (!string.IsNullOrEmpty(item) && !UUID.TryParse(item, out notice.AttachmentID))
                        {
                            InventoryBase inventoryBaseItem =
                                FindInventory<InventoryBase>(Client.Inventory.Store.RootNode, item
                                    ).FirstOrDefault();
                            if (inventoryBaseItem == null)
                            {
                                throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.INVENTORY_ITEM_NOT_FOUND));
                            }
                            notice.AttachmentID = inventoryBaseItem.UUID;
                        }
                        Client.Groups.SendGroupNotice(groupUUID, notice);
                    };
                    break;
                case ScriptKeys.PAY:
                    execute = () =>
                    {
                        if (
                            !HasCorrodePermission(group, (int) Permissions.PERMISSION_ECONOMY))
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.NO_CORRADE_PERMISSIONS));
                        }
                        int amount;
                        if (
                            !int.TryParse(
                                wasInput(
                                    wasKeyValueGet(wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.AMOUNT)), message)),
                                out amount))
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.INVALID_PAY_AMOUNT));
                        }
                        if (amount.Equals(0))
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.INVALID_PAY_AMOUNT));
                        }
                        if (!UpdateBalance(Configuration.SERVICES_TIMEOUT))
                        {
                            throw new Exception(
                                wasGetDescriptionFromEnumValue(ScriptError.UNABLE_TO_OBTAIN_MONEY_BALANCE));
                        }
                        if (Client.Self.Balance < amount)
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.INSUFFICIENT_FUNDS));
                        }
                        UUID targetUUID = UUID.Zero;
                        switch (
                            wasGetEnumValueFromDescription<Entity>(
                                wasInput(
                                    wasKeyValueGet(wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.ENTITY)),
                                        message)).ToLowerInvariant()))
                        {
                            case Entity.GROUP:
                                Group configuredGroup =
                                    Configuration.GROUPS.AsParallel().FirstOrDefault(
                                        o => o.Name.Equals(group, StringComparison.Ordinal));
                                switch (!configuredGroup.Equals(default(Group)))
                                {
                                    case true:
                                        targetUUID = configuredGroup.UUID;
                                        break;
                                    default:
                                        if (
                                            !GroupNameToUUID(group, Configuration.SERVICES_TIMEOUT,
                                                Configuration.DATA_TIMEOUT,
                                                ref targetUUID))
                                        {
                                            throw new Exception(
                                                wasGetDescriptionFromEnumValue(ScriptError.GROUP_NOT_FOUND));
                                        }
                                        break;
                                }
                                Client.Self.GiveGroupMoney(targetUUID, amount,
                                    wasInput(
                                        wasKeyValueGet(wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.REASON)),
                                            message)));
                                break;
                            case Entity.AVATAR:
                                if (
                                    !UUID.TryParse(
                                        wasInput(
                                            wasKeyValueGet(wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.AGENT)),
                                                message)), out targetUUID) && !AgentNameToUUID(
                                                    wasInput(
                                                        wasKeyValueGet(
                                                            wasOutput(
                                                                wasGetDescriptionFromEnumValue(ScriptKeys.FIRSTNAME)),
                                                            message)),
                                                    wasInput(
                                                        wasKeyValueGet(
                                                            wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.LASTNAME)),
                                                            message)),
                                                    Configuration.SERVICES_TIMEOUT, Configuration.DATA_TIMEOUT,
                                                    ref targetUUID))
                                {
                                    throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.AGENT_NOT_FOUND));
                                }
                                Client.Self.GiveAvatarMoney(targetUUID, amount,
                                    wasInput(
                                        wasKeyValueGet(wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.REASON)),
                                            message)));
                                break;
                            case Entity.OBJECT:
                                if (
                                    !UUID.TryParse(
                                        wasInput(
                                            wasKeyValueGet(wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.TARGET)),
                                                message)),
                                        out targetUUID))
                                {
                                    throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.INVALID_PAY_TARGET));
                                }
                                Client.Self.GiveObjectMoney(targetUUID, amount,
                                    wasInput(
                                        wasKeyValueGet(wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.REASON)),
                                            message)));
                                break;
                            default:
                                throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.UNKNOWN_ENTITY));
                        }
                    };
                    break;
                case ScriptKeys.GETBALANCE:
                    execute = () =>
                    {
                        if (
                            !HasCorrodePermission(group, (int) Permissions.PERMISSION_ECONOMY))
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.NO_CORRADE_PERMISSIONS));
                        }
                        if (!UpdateBalance(Configuration.SERVICES_TIMEOUT))
                        {
                            throw new Exception(
                                wasGetDescriptionFromEnumValue(ScriptError.UNABLE_TO_OBTAIN_MONEY_BALANCE));
                        }
                        result.Add(wasGetDescriptionFromEnumValue(ResultKeys.DATA),
                            Client.Self.Balance.ToString(CultureInfo.InvariantCulture));
                    };
                    break;
                case ScriptKeys.TELEPORT:
                    execute = () =>
                    {
                        if (
                            !HasCorrodePermission(group,
                                (int) Permissions.PERMISSION_MOVEMENT))
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.NO_CORRADE_PERMISSIONS));
                        }
                        // We override the default teleport since region names are unique and case insensitive.
                        ulong regionHandle = 0;
                        string region =
                            wasInput(wasKeyValueGet(wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.REGION)),
                                message));
                        if (string.IsNullOrEmpty(region))
                        {
                            region = Client.Network.CurrentSim.Name;
                        }
                        ManualResetEvent GridRegionEvent = new ManualResetEvent(false);
                        EventHandler<GridRegionEventArgs> GridRegionEventHandler =
                            (sender, args) =>
                            {
                                if (!args.Region.Name.Equals(region, StringComparison.InvariantCultureIgnoreCase))
                                    return;
                                regionHandle = args.Region.RegionHandle;
                                GridRegionEvent.Set();
                            };
                        lock (ClientInstanceGridLock)
                        {
                            Client.Grid.GridRegion += GridRegionEventHandler;
                            Client.Grid.RequestMapRegion(region, GridLayerType.Objects);
                            if (!GridRegionEvent.WaitOne(Client.Settings.MAP_REQUEST_TIMEOUT, false))
                            {
                                Client.Grid.GridRegion -= GridRegionEventHandler;
                                throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.TIMEOUT_GETTING_REGION));
                            }
                            Client.Grid.GridRegion -= GridRegionEventHandler;
                        }
                        if (regionHandle.Equals(0))
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.REGION_NOT_FOUND));
                        }
                        Vector3 position;
                        if (
                            !Vector3.TryParse(
                                wasInput(
                                    wasKeyValueGet(wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.POSITION)),
                                        message)),
                                out position))
                        {
                            position = Client.Self.SimPosition;
                        }
                        if (regionHandle.Equals(Client.Network.CurrentSim.Handle) &&
                            Vector3.Distance(Client.Self.SimPosition, position) <
                            LINDEN_CONSTANTS.REGION.TELEPORT_MINIMUM_DISTANCE)
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.DESTINATION_TOO_CLOSE));
                        }
                        ManualResetEvent TeleportEvent = new ManualResetEvent(false);
                        bool succeeded = false;
                        EventHandler<TeleportEventArgs> TeleportEventHandler = (sender, args) =>
                        {
                            switch (args.Status)
                            {
                                case TeleportStatus.Cancelled:
                                case TeleportStatus.Failed:
                                case TeleportStatus.Finished:
                                    succeeded = args.Status.Equals(TeleportStatus.Finished);
                                    TeleportEvent.Set();
                                    break;
                            }
                        };
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
                            Client.Self.TeleportProgress += TeleportEventHandler;
                            Client.Self.Teleport(regionHandle, position);
                            if (!TeleportEvent.WaitOne(Configuration.SERVICES_TIMEOUT, false))
                            {
                                Client.Self.TeleportProgress -= TeleportEventHandler;
                                throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.TIMEOUT_DURING_TELEPORT));
                            }
                            Client.Self.TeleportProgress -= TeleportEventHandler;
                        }
                        if (!succeeded)
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.TELEPORT_FAILED));
                        }
                        // Set the camera on the avatar.
                        Client.Self.Movement.Camera.LookAt(
                            Client.Self.SimPosition,
                            Client.Self.SimPosition
                            );
                    };
                    break;
                case ScriptKeys.LURE:
                    execute = () =>
                    {
                        if (
                            !HasCorrodePermission(group,
                                (int) Permissions.PERMISSION_MOVEMENT))
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.NO_CORRADE_PERMISSIONS));
                        }
                        UUID agentUUID;
                        if (
                            !UUID.TryParse(
                                wasInput(wasKeyValueGet(
                                    wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.AGENT)), message)),
                                out agentUUID) && !AgentNameToUUID(
                                    wasInput(
                                        wasKeyValueGet(wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.FIRSTNAME)),
                                            message)),
                                    wasInput(
                                        wasKeyValueGet(wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.LASTNAME)),
                                            message)),
                                    Configuration.SERVICES_TIMEOUT, Configuration.DATA_TIMEOUT, ref agentUUID))
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.AGENT_NOT_FOUND));
                        }
                        Client.Self.SendTeleportLure(agentUUID,
                            wasInput(wasKeyValueGet(wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.MESSAGE)),
                                message)));
                    };
                    break;
                case ScriptKeys.SETHOME:
                    execute = () =>
                    {
                        if (
                            !HasCorrodePermission(group,
                                (int) Permissions.PERMISSION_GROOMING))
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.NO_CORRADE_PERMISSIONS));
                        }
                        bool succeeded = true;
                        ManualResetEvent AlertMessageEvent = new ManualResetEvent(false);
                        EventHandler<AlertMessageEventArgs> AlertMessageEventHandler = (sender, args) =>
                        {
                            switch (args.Message)
                            {
                                case LINDEN_CONSTANTS.ALERTS.UNABLE_TO_SET_HOME:
                                    succeeded = false;
                                    AlertMessageEvent.Set();
                                    break;
                                case LINDEN_CONSTANTS.ALERTS.HOME_SET:
                                    succeeded = true;
                                    AlertMessageEvent.Set();
                                    break;
                            }
                        };
                        lock (ClientInstanceSelfLock)
                        {
                            Client.Self.AlertMessage += AlertMessageEventHandler;
                            Client.Self.SetHome();
                            if (!AlertMessageEvent.WaitOne(Configuration.SERVICES_TIMEOUT, false))
                            {
                                Client.Self.AlertMessage -= AlertMessageEventHandler;
                                throw new Exception(
                                    wasGetDescriptionFromEnumValue(ScriptError.TIMEOUT_REQUESTING_TO_SET_HOME));
                            }
                            Client.Self.AlertMessage -= AlertMessageEventHandler;
                        }
                        if (!succeeded)
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.UNABLE_TO_SET_HOME));
                        }
                    };
                    break;
                case ScriptKeys.GOHOME:
                    execute = () =>
                    {
                        if (
                            !HasCorrodePermission(group,
                                (int) Permissions.PERMISSION_MOVEMENT))
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.NO_CORRADE_PERMISSIONS));
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
                        bool succeeded = Client.Self.GoHome();
                        if (!succeeded)
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.UNABLE_TO_GO_HOME));
                        }
                        // Set the camera on the avatar.
                        Client.Self.Movement.Camera.LookAt(
                            Client.Self.SimPosition,
                            Client.Self.SimPosition
                            );
                    };
                    break;
                case ScriptKeys.GETREGIONDATA:
                    execute = () =>
                    {
                        if (!HasCorrodePermission(group, (int) Permissions.PERMISSION_LAND))
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.NO_CORRADE_PERMISSIONS));
                        }
                        string region =
                            wasInput(wasKeyValueGet(wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.REGION)),
                                message));
                        Simulator simulator =
                            Client.Network.Simulators.FirstOrDefault(
                                o =>
                                    o.Name.Equals(
                                        string.IsNullOrEmpty(region) ? Client.Network.CurrentSim.Name : region,
                                        StringComparison.InvariantCultureIgnoreCase));
                        if (simulator == null)
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.REGION_NOT_FOUND));
                        }
                        List<string> data = new List<string>(GetStructuredData(simulator,
                            wasInput(wasKeyValueGet(wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.DATA)),
                                message)))
                            );
                        if (!data.Count.Equals(0))
                        {
                            result.Add(wasGetDescriptionFromEnumValue(ResultKeys.DATA),
                                wasEnumerableToCSV(data));
                        }
                    };
                    break;
                case ScriptKeys.GETGRIDREGIONDATA:
                    execute = () =>
                    {
                        if (!HasCorrodePermission(group, (int) Permissions.PERMISSION_LAND))
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.NO_CORRADE_PERMISSIONS));
                        }
                        string region =
                            wasInput(wasKeyValueGet(wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.REGION)),
                                message));
                        if (string.IsNullOrEmpty(region))
                        {
                            region = Client.Network.CurrentSim.Name;
                        }
                        ManualResetEvent GridRegionEvent = new ManualResetEvent(false);
                        GridRegion gridRegion = new GridRegion();
                        EventHandler<GridRegionEventArgs> GridRegionEventHandler = (sender, args) =>
                        {
                            if (!args.Region.Name.Equals(region, StringComparison.InvariantCultureIgnoreCase))
                                return;
                            gridRegion = args.Region;
                            GridRegionEvent.Set();
                        };
                        lock (ClientInstanceGridLock)
                        {
                            Client.Grid.GridRegion += GridRegionEventHandler;
                            Client.Grid.RequestMapRegion(region, GridLayerType.Objects);
                            if (!GridRegionEvent.WaitOne(Configuration.SERVICES_TIMEOUT, false))
                            {
                                Client.Grid.GridRegion -= GridRegionEventHandler;
                                throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.TIMEOUT_GETTING_REGION));
                            }
                            Client.Grid.GridRegion -= GridRegionEventHandler;
                        }
                        if (gridRegion.Equals(default(GridRegion)))
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.REGION_NOT_FOUND));
                        }
                        List<string> data = new List<string>(GetStructuredData(gridRegion,
                            wasInput(wasKeyValueGet(wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.DATA)),
                                message))));
                        if (!data.Count.Equals(0))
                        {
                            result.Add(wasGetDescriptionFromEnumValue(ResultKeys.DATA),
                                wasEnumerableToCSV(data));
                        }
                    };
                    break;
                case ScriptKeys.GETNETWORKDATA:
                    execute = () =>
                    {
                        if (!HasCorrodePermission(group, (int) Permissions.PERMISSION_GROOMING))
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.NO_CORRADE_PERMISSIONS));
                        }
                        List<string> data = new List<string>(GetStructuredData(Client.Network,
                            wasInput(wasKeyValueGet(wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.DATA)),
                                message))));
                        if (!data.Count.Equals(0))
                        {
                            result.Add(wasGetDescriptionFromEnumValue(ResultKeys.DATA),
                                wasEnumerableToCSV(data));
                        }
                    };
                    break;
                case ScriptKeys.GETCONNECTEDREGIONS:
                    execute = () =>
                    {
                        if (!HasCorrodePermission(group, (int) Permissions.PERMISSION_LAND))
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.NO_CORRADE_PERMISSIONS));
                        }
                        result.Add(wasGetDescriptionFromEnumValue(ResultKeys.DATA),
                            wasEnumerableToCSV(Client.Network.Simulators.Select(o => o.Name)));
                    };
                    break;
                case ScriptKeys.LISTCOMMANDS:
                    execute = () =>
                    {
                        HashSet<string> data = new HashSet<string>();
                        object LockObject = new object();
                        Parallel.ForEach(wasGetEnumDescriptions<ScriptKeys>(), o =>
                        {
                            ScriptKeys scriptKey = wasGetEnumValueFromDescription<ScriptKeys>(o);
                            IsCommandAttribute isCommandAttribute =
                                wasGetAttributeFromEnumValue<IsCommandAttribute>(scriptKey);
                            if (isCommandAttribute == null || !isCommandAttribute.IsCommand)
                                return;
                            CommandPermissionMaskAttribute commandPermissionMaskAttribute =
                                wasGetAttributeFromEnumValue<CommandPermissionMaskAttribute>(scriptKey);
                            if (commandPermissionMaskAttribute == null) return;
                            Group commandGroup =
                                Configuration.GROUPS.AsParallel()
                                    .FirstOrDefault(
                                        p => p.Name.Equals(group, StringComparison.InvariantCultureIgnoreCase));
                            if (commandGroup.Equals(default(Group)) ||
                                (commandGroup.PermissionMask & commandPermissionMaskAttribute.PermissionMask).Equals(0))
                                return;
                            lock (LockObject)
                            {
                                data.Add(o);
                            }
                        });
                        if (!data.Count.Equals(0))
                        {
                            result.Add(wasGetDescriptionFromEnumValue(ResultKeys.DATA), wasEnumerableToCSV(data));
                        }
                    };
                    break;
                case ScriptKeys.GETCOMMAND:
                    execute = () =>
                    {
                        string name =
                            wasInput(wasKeyValueGet(wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.NAME)),
                                message));
                        if (string.IsNullOrEmpty(name))
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.NO_NAME_PROVIDED));
                        }
                        IsCommandAttribute isCommandAttribute =
                            wasGetAttributeFromEnumValue<IsCommandAttribute>(
                                wasGetEnumValueFromDescription<ScriptKeys>(name));
                        if (isCommandAttribute == null || isCommandAttribute.IsCommand.Equals(false))
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.COMMAND_NOT_FOUND));
                        }
                        CommandPermissionMaskAttribute commandPermissionMaskAttribute =
                            wasGetAttributeFromEnumValue<CommandPermissionMaskAttribute>(
                                wasGetEnumValueFromDescription<ScriptKeys>(name));
                        if (commandPermissionMaskAttribute == null)
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.NO_CORRADE_PERMISSIONS));
                        }
                        Group commandGroup =
                            Configuration.GROUPS.AsParallel()
                                .FirstOrDefault(
                                    p => p.Name.Equals(group, StringComparison.InvariantCultureIgnoreCase));
                        if (commandGroup.Equals(default(Group)))
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.GROUP_NOT_FOUND));
                        }
                        switch (
                            wasGetEnumValueFromDescription<Entity>(
                                wasInput(
                                    wasKeyValueGet(wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.ENTITY)),
                                        message)).ToLowerInvariant()))
                        {
                            case Entity.SYNTAX:
                                switch (
                                    wasGetEnumValueFromDescription<Type>(
                                        wasInput(
                                            wasKeyValueGet(wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.TYPE)),
                                                message)).ToLowerInvariant()))
                                {
                                    case Type.INPUT:
                                        CommandInputSyntaxAttribute commandInputSyntaxAttribute = wasGetAttributeFromEnumValue
                                            <CommandInputSyntaxAttribute>(
                                                wasGetEnumValueFromDescription<ScriptKeys>(name));
                                        if (commandInputSyntaxAttribute != null &&
                                            !string.IsNullOrEmpty(commandInputSyntaxAttribute.Syntax))
                                        {
                                            result.Add(wasGetDescriptionFromEnumValue(ResultKeys.DATA),
                                                commandInputSyntaxAttribute.Syntax);
                                        }
                                        break;
                                    default:
                                        throw new Exception(
                                            wasGetDescriptionFromEnumValue(ScriptError.UNKNOWN_SYNTAX_TYPE));
                                }
                                break;
                            case Entity.PERMISSION:
                                HashSet<string> data = new HashSet<string>();
                                object LockObject = new object();
                                Parallel.ForEach(wasGetEnumDescriptions<Permissions>(), o =>
                                {
                                    Permissions permission = wasGetEnumValueFromDescription<Permissions>(o);
                                    if ((commandPermissionMaskAttribute.PermissionMask & (uint) permission).Equals(0))
                                        return;
                                    lock (LockObject)
                                    {
                                        data.Add(o);
                                    }
                                });
                                if (!data.Count.Equals(0))
                                {
                                    result.Add(wasGetDescriptionFromEnumValue(ResultKeys.DATA), wasEnumerableToCSV(data));
                                }
                                break;
                            default:
                                throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.UNKNOWN_ENTITY));
                        }
                    };
                    break;
                case ScriptKeys.SIT:
                    execute = () =>
                    {
                        if (
                            !HasCorrodePermission(group,
                                (int) Permissions.PERMISSION_MOVEMENT))
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.NO_CORRADE_PERMISSIONS));
                        }
                        float range;
                        if (
                            !float.TryParse(
                                wasInput(wasKeyValueGet(
                                    wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.RANGE)), message)),
                                out range))
                        {
                            range = Configuration.RANGE;
                        }
                        Primitive primitive = null;
                        if (
                            !FindPrimitive(
                                StringOrUUID(wasInput(wasKeyValueGet(
                                    wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.ITEM)), message))),
                                range,
                                ref primitive, Configuration.SERVICES_TIMEOUT, Configuration.DATA_TIMEOUT))
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.PRIMITIVE_NOT_FOUND));
                        }
                        ManualResetEvent SitEvent = new ManualResetEvent(false);
                        bool succeeded = false;
                        EventHandler<AvatarSitResponseEventArgs> AvatarSitEventHandler = (sender, args) =>
                        {
                            succeeded = !args.ObjectID.Equals(UUID.Zero);
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
                            if (!SitEvent.WaitOne(Configuration.SERVICES_TIMEOUT, false))
                            {
                                Client.Self.AvatarSitResponse -= AvatarSitEventHandler;
                                Client.Self.AlertMessage -= AlertMessageEventHandler;
                                throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.TIMEOUT_REQUESTING_SIT));
                            }
                            Client.Self.AvatarSitResponse -= AvatarSitEventHandler;
                            Client.Self.AlertMessage -= AlertMessageEventHandler;
                        }
                        if (!succeeded)
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.COULD_NOT_SIT));
                        }
                        Client.Self.Sit();
                        // Set the camera on the avatar.
                        Client.Self.Movement.Camera.LookAt(
                            Client.Self.SimPosition,
                            Client.Self.SimPosition
                            );
                    };
                    break;
                case ScriptKeys.RELAX:
                    execute = () =>
                    {
                        if (!HasCorrodePermission(group, (int) Permissions.PERMISSION_MOVEMENT))
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.NO_CORRADE_PERMISSIONS));
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
                        Client.Self.SitOnGround();
                        // Set the camera on the avatar.
                        Client.Self.Movement.Camera.LookAt(
                            Client.Self.SimPosition,
                            Client.Self.SimPosition
                            );
                    };
                    break;
                case ScriptKeys.AWAY:
                    execute = () =>
                    {
                        if (!HasCorrodePermission(group, (int) Permissions.PERMISSION_GROOMING))
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.NO_CORRADE_PERMISSIONS));
                        }
                        switch (wasGetEnumValueFromDescription<Action>(
                            wasInput(
                                wasKeyValueGet(wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.ACTION)), message))
                                .ToLowerInvariant()))
                        {
                            case Action.ENABLE:
                                Client.Self.AnimationStart(Animations.AWAY, true);
                                Client.Self.Movement.Away = true;
                                Client.Self.Movement.SendUpdate(true);
                                break;
                            case Action.DISABLE:
                                Client.Self.Movement.Away = false;
                                Client.Self.AnimationStop(Animations.AWAY, true);
                                Client.Self.Movement.SendUpdate(true);
                                break;
                            case Action.GET:
                                result.Add(wasGetDescriptionFromEnumValue(ScriptKeys.DATA),
                                    Client.Self.Movement.Away.ToString());
                                break;
                            default:
                                throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.UNKNOWN_ACTION));
                        }
                    };
                    break;
                case ScriptKeys.BUSY:
                    execute = () =>
                    {
                        if (!HasCorrodePermission(group, (int) Permissions.PERMISSION_GROOMING))
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.NO_CORRADE_PERMISSIONS));
                        }
                        switch (wasGetEnumValueFromDescription<Action>(
                            wasInput(
                                wasKeyValueGet(wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.ACTION)), message))
                                .ToLowerInvariant()))
                        {
                            case Action.ENABLE:
                                Client.Self.AnimationStart(Animations.BUSY, true);
                                break;
                            case Action.DISABLE:
                                Client.Self.AnimationStop(Animations.BUSY, true);
                                break;
                            case Action.GET:
                                result.Add(wasGetDescriptionFromEnumValue(ScriptKeys.DATA),
                                    Client.Self.SignaledAnimations.ContainsKey(Animations.BUSY).ToString());
                                break;
                            default:
                                throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.UNKNOWN_ACTION));
                        }
                    };
                    break;
                case ScriptKeys.TYPING:
                    execute = () =>
                    {
                        if (!HasCorrodePermission(group, (int) Permissions.PERMISSION_GROOMING))
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.NO_CORRADE_PERMISSIONS));
                        }
                        switch (wasGetEnumValueFromDescription<Action>(
                            wasInput(
                                wasKeyValueGet(wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.ACTION)), message))
                                .ToLowerInvariant()))
                        {
                            case Action.ENABLE:
                                Client.Self.AnimationStart(Animations.TYPE, true);
                                break;
                            case Action.DISABLE:
                                Client.Self.AnimationStop(Animations.TYPE, true);
                                break;
                            case Action.GET:
                                result.Add(wasGetDescriptionFromEnumValue(ScriptKeys.DATA),
                                    Client.Self.SignaledAnimations.ContainsKey(Animations.TYPE).ToString());
                                break;
                            default:
                                throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.UNKNOWN_ACTION));
                        }
                    };
                    break;
                case ScriptKeys.RUN:
                    execute = () =>
                    {
                        if (!HasCorrodePermission(group, (int) Permissions.PERMISSION_MOVEMENT))
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.NO_CORRADE_PERMISSIONS));
                        }
                        Action action = wasGetEnumValueFromDescription<Action>(
                            wasInput(
                                wasKeyValueGet(wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.ACTION)), message))
                                .ToLowerInvariant());
                        switch (action)
                        {
                            case Action.ENABLE:
                            case Action.DISABLE:
                                Client.Self.Movement.AlwaysRun = !action.Equals(Action.DISABLE);
                                Client.Self.Movement.SendUpdate(true);
                                break;
                            case Action.GET:
                                result.Add(wasGetDescriptionFromEnumValue(ScriptKeys.DATA),
                                    Client.Self.Movement.AlwaysRun.ToString());
                                break;
                            default:
                                throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.UNKNOWN_ACTION));
                        }
                    };
                    break;
                case ScriptKeys.STAND:
                    execute = () =>
                    {
                        if (
                            !HasCorrodePermission(group,
                                (int) Permissions.PERMISSION_MOVEMENT))
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.NO_CORRADE_PERMISSIONS));
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
                        // Set the camera on the avatar.
                        Client.Self.Movement.Camera.LookAt(
                            Client.Self.SimPosition,
                            Client.Self.SimPosition
                            );
                    };
                    break;
                case ScriptKeys.GETPARCELLIST:
                    execute = () =>
                    {
                        if (!HasCorrodePermission(group, (int) Permissions.PERMISSION_LAND))
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.NO_CORRADE_PERMISSIONS));
                        }
                        Group configuredGroup =
                            Configuration.GROUPS.AsParallel().FirstOrDefault(
                                o => o.Name.Equals(group, StringComparison.Ordinal));
                        UUID groupUUID = UUID.Zero;
                        switch (!configuredGroup.Equals(default(Group)))
                        {
                            case true:
                                groupUUID = configuredGroup.UUID;
                                break;
                            default:
                                if (!GroupNameToUUID(group, Configuration.SERVICES_TIMEOUT, Configuration.DATA_TIMEOUT,
                                    ref groupUUID))
                                {
                                    throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.GROUP_NOT_FOUND));
                                }
                                break;
                        }
                        Vector3 position;
                        if (
                            !Vector3.TryParse(
                                wasInput(
                                    wasKeyValueGet(wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.POSITION)),
                                        message)),
                                out position))
                        {
                            position = Client.Self.SimPosition;
                        }
                        string region =
                            wasInput(wasKeyValueGet(wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.REGION)),
                                message));
                        Simulator simulator =
                            Client.Network.Simulators.FirstOrDefault(
                                o =>
                                    o.Name.Equals(
                                        string.IsNullOrEmpty(region) ? Client.Network.CurrentSim.Name : region,
                                        StringComparison.InvariantCultureIgnoreCase));
                        if (simulator == null)
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.REGION_NOT_FOUND));
                        }
                        Parcel parcel = null;
                        if (!GetParcelAtPosition(simulator, position, ref parcel))
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.COULD_NOT_FIND_PARCEL));
                        }
                        FieldInfo accessField = typeof (AccessList).GetFields(
                            BindingFlags.Public | BindingFlags.Static)
                            .AsParallel().FirstOrDefault(
                                o =>
                                    o.Name.Equals(
                                        wasInput(
                                            wasKeyValueGet(wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.TYPE)),
                                                message)),
                                        StringComparison.Ordinal));
                        if (accessField == null)
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.UNKNOWN_ACCESS_LIST_TYPE));
                        }
                        AccessList accessType = (AccessList) accessField.GetValue(null);
                        if (!simulator.IsEstateManager)
                        {
                            if (!parcel.OwnerID.Equals(Client.Self.AgentID))
                            {
                                if (!parcel.IsGroupOwned && !parcel.GroupID.Equals(groupUUID))
                                {
                                    throw new Exception(
                                        wasGetDescriptionFromEnumValue(ScriptError.NO_GROUP_POWER_FOR_COMMAND));
                                }
                                switch (accessType)
                                {
                                    case AccessList.Access:
                                        if (
                                            !HasGroupPowers(Client.Self.AgentID, groupUUID,
                                                GroupPowers.LandManageAllowed, Configuration.SERVICES_TIMEOUT,
                                                Configuration.DATA_TIMEOUT))
                                        {
                                            throw new Exception(
                                                wasGetDescriptionFromEnumValue(ScriptError.NO_GROUP_POWER_FOR_COMMAND));
                                        }
                                        break;
                                    case AccessList.Ban:
                                        if (
                                            !HasGroupPowers(Client.Self.AgentID, groupUUID, GroupPowers.LandManageBanned,
                                                Configuration.SERVICES_TIMEOUT, Configuration.DATA_TIMEOUT))
                                        {
                                            throw new Exception(
                                                wasGetDescriptionFromEnumValue(ScriptError.NO_GROUP_POWER_FOR_COMMAND));
                                        }
                                        break;
                                    case AccessList.Both:
                                        if (
                                            !HasGroupPowers(Client.Self.AgentID, groupUUID,
                                                GroupPowers.LandManageAllowed, Configuration.SERVICES_TIMEOUT,
                                                Configuration.DATA_TIMEOUT))
                                        {
                                            throw new Exception(
                                                wasGetDescriptionFromEnumValue(ScriptError.NO_GROUP_POWER_FOR_COMMAND));
                                        }
                                        if (
                                            !HasGroupPowers(Client.Self.AgentID, groupUUID, GroupPowers.LandManageBanned,
                                                Configuration.SERVICES_TIMEOUT, Configuration.DATA_TIMEOUT))
                                        {
                                            throw new Exception(
                                                wasGetDescriptionFromEnumValue(ScriptError.NO_GROUP_POWER_FOR_COMMAND));
                                        }
                                        break;
                                }
                            }
                        }
                        List<string> csv = new List<string>();
                        ManualResetEvent ParcelAccessListEvent = new ManualResetEvent(false);
                        EventHandler<ParcelAccessListReplyEventArgs> ParcelAccessListHandler = (sender, args) =>
                        {
                            foreach (ParcelManager.ParcelAccessEntry parcelAccess in args.AccessList)
                            {
                                string agent = string.Empty;
                                if (
                                    !AgentUUIDToName(parcelAccess.AgentID, Configuration.SERVICES_TIMEOUT, ref agent))
                                    continue;
                                csv.Add(agent);
                                csv.Add(parcelAccess.AgentID.ToString());
                                csv.Add(parcelAccess.Flags.ToString());
                                csv.Add(parcelAccess.Time.ToString(CultureInfo.InvariantCulture));
                            }
                            ParcelAccessListEvent.Set();
                        };
                        lock (ClientInstanceParcelsLock)
                        {
                            Client.Parcels.ParcelAccessListReply += ParcelAccessListHandler;
                            Client.Parcels.RequestParcelAccessList(simulator, parcel.LocalID, accessType, 0);
                            if (!ParcelAccessListEvent.WaitOne(Configuration.SERVICES_TIMEOUT, false))
                            {
                                Client.Parcels.ParcelAccessListReply -= ParcelAccessListHandler;
                                throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.TIMEOUT_GETTING_PARCELS));
                            }
                            Client.Parcels.ParcelAccessListReply -= ParcelAccessListHandler;
                        }
                        if (!csv.Count.Equals(0))
                        {
                            result.Add(wasGetDescriptionFromEnumValue(ResultKeys.DATA),
                                wasEnumerableToCSV(csv));
                        }
                    };
                    break;
                case ScriptKeys.PARCELRECLAIM:
                    execute = () =>
                    {
                        if (!HasCorrodePermission(group, (int) Permissions.PERMISSION_LAND))
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.NO_CORRADE_PERMISSIONS));
                        }
                        Vector3 position;
                        if (
                            !Vector3.TryParse(
                                wasInput(
                                    wasKeyValueGet(wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.POSITION)),
                                        message)),
                                out position))
                        {
                            position = Client.Self.SimPosition;
                        }
                        string region =
                            wasInput(wasKeyValueGet(wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.REGION)),
                                message));
                        Simulator simulator =
                            Client.Network.Simulators.FirstOrDefault(
                                o =>
                                    o.Name.Equals(
                                        string.IsNullOrEmpty(region) ? Client.Network.CurrentSim.Name : region,
                                        StringComparison.InvariantCultureIgnoreCase));
                        if (simulator == null)
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.REGION_NOT_FOUND));
                        }
                        Parcel parcel = null;
                        if (!GetParcelAtPosition(simulator, position, ref parcel))
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.COULD_NOT_FIND_PARCEL));
                        }
                        if (!simulator.IsEstateManager)
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.NO_LAND_RIGHTS));
                        }
                        Client.Parcels.Reclaim(simulator, parcel.LocalID);
                    };
                    break;
                case ScriptKeys.PARCELRELEASE:
                    execute = () =>
                    {
                        if (!HasCorrodePermission(group, (int) Permissions.PERMISSION_LAND))
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.NO_CORRADE_PERMISSIONS));
                        }
                        Group configuredGroup =
                            Configuration.GROUPS.AsParallel().FirstOrDefault(
                                o => o.Name.Equals(group, StringComparison.Ordinal));
                        UUID groupUUID = UUID.Zero;
                        switch (!configuredGroup.Equals(default(Group)))
                        {
                            case true:
                                groupUUID = configuredGroup.UUID;
                                break;
                            default:
                                if (!GroupNameToUUID(group, Configuration.SERVICES_TIMEOUT, Configuration.DATA_TIMEOUT,
                                    ref groupUUID))
                                {
                                    throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.GROUP_NOT_FOUND));
                                }
                                break;
                        }
                        Vector3 position;
                        if (
                            !Vector3.TryParse(
                                wasInput(
                                    wasKeyValueGet(wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.POSITION)),
                                        message)),
                                out position))
                        {
                            position = Client.Self.SimPosition;
                        }
                        string region =
                            wasInput(wasKeyValueGet(wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.REGION)),
                                message));
                        Simulator simulator =
                            Client.Network.Simulators.FirstOrDefault(
                                o =>
                                    o.Name.Equals(
                                        string.IsNullOrEmpty(region) ? Client.Network.CurrentSim.Name : region,
                                        StringComparison.InvariantCultureIgnoreCase));
                        if (simulator == null)
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.REGION_NOT_FOUND));
                        }
                        Parcel parcel = null;
                        if (!GetParcelAtPosition(simulator, position, ref parcel))
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.COULD_NOT_FIND_PARCEL));
                        }
                        if (!simulator.IsEstateManager)
                        {
                            if (!parcel.OwnerID.Equals(Client.Self.AgentID))
                            {
                                if (!parcel.IsGroupOwned && !parcel.GroupID.Equals(groupUUID))
                                {
                                    throw new Exception(
                                        wasGetDescriptionFromEnumValue(ScriptError.NO_GROUP_POWER_FOR_COMMAND));
                                }
                                if (
                                    !HasGroupPowers(Client.Self.AgentID, groupUUID, GroupPowers.LandRelease,
                                        Configuration.SERVICES_TIMEOUT, Configuration.DATA_TIMEOUT))
                                {
                                    throw new Exception(
                                        wasGetDescriptionFromEnumValue(ScriptError.NO_GROUP_POWER_FOR_COMMAND));
                                }
                            }
                        }
                        Client.Parcels.ReleaseParcel(simulator, parcel.LocalID);
                    };
                    break;
                case ScriptKeys.PARCELDEED:
                    execute = () =>
                    {
                        if (!HasCorrodePermission(group, (int) Permissions.PERMISSION_LAND))
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.NO_CORRADE_PERMISSIONS));
                        }
                        Group configuredGroup =
                            Configuration.GROUPS.AsParallel().FirstOrDefault(
                                o => o.Name.Equals(group, StringComparison.Ordinal));
                        UUID groupUUID = UUID.Zero;
                        switch (!configuredGroup.Equals(default(Group)))
                        {
                            case true:
                                groupUUID = configuredGroup.UUID;
                                break;
                            default:
                                if (!GroupNameToUUID(group, Configuration.SERVICES_TIMEOUT, Configuration.DATA_TIMEOUT,
                                    ref groupUUID))
                                {
                                    throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.GROUP_NOT_FOUND));
                                }
                                break;
                        }
                        Vector3 position;
                        if (
                            !Vector3.TryParse(
                                wasInput(
                                    wasKeyValueGet(wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.POSITION)),
                                        message)),
                                out position))
                        {
                            position = Client.Self.SimPosition;
                        }
                        string region =
                            wasInput(wasKeyValueGet(wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.REGION)),
                                message));
                        Simulator simulator =
                            Client.Network.Simulators.FirstOrDefault(
                                o =>
                                    o.Name.Equals(
                                        string.IsNullOrEmpty(region) ? Client.Network.CurrentSim.Name : region,
                                        StringComparison.InvariantCultureIgnoreCase));
                        if (simulator == null)
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.REGION_NOT_FOUND));
                        }
                        Parcel parcel = null;
                        if (!GetParcelAtPosition(simulator, position, ref parcel))
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.COULD_NOT_FIND_PARCEL));
                        }
                        if (!simulator.IsEstateManager)
                        {
                            if (!parcel.OwnerID.Equals(Client.Self.AgentID))
                            {
                                if (!parcel.IsGroupOwned && !parcel.GroupID.Equals(groupUUID))
                                {
                                    throw new Exception(
                                        wasGetDescriptionFromEnumValue(ScriptError.NO_GROUP_POWER_FOR_COMMAND));
                                }
                            }
                        }
                        if (
                            !HasGroupPowers(Client.Self.AgentID, groupUUID, GroupPowers.LandDeed,
                                Configuration.SERVICES_TIMEOUT, Configuration.DATA_TIMEOUT))
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.NO_GROUP_POWER_FOR_COMMAND));
                        }
                        Client.Parcels.DeedToGroup(simulator, parcel.LocalID, groupUUID);
                    };
                    break;
                case ScriptKeys.PARCELBUY:
                    execute = () =>
                    {
                        if (!HasCorrodePermission(group, (int) Permissions.PERMISSION_LAND))
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.NO_CORRADE_PERMISSIONS));
                        }
                        Group configuredGroup =
                            Configuration.GROUPS.AsParallel().FirstOrDefault(
                                o => o.Name.Equals(group, StringComparison.Ordinal));
                        UUID groupUUID = UUID.Zero;
                        switch (!configuredGroup.Equals(default(Group)))
                        {
                            case true:
                                groupUUID = configuredGroup.UUID;
                                break;
                            default:
                                if (!GroupNameToUUID(group, Configuration.SERVICES_TIMEOUT, Configuration.DATA_TIMEOUT,
                                    ref groupUUID))
                                {
                                    throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.GROUP_NOT_FOUND));
                                }
                                break;
                        }
                        Vector3 position;
                        if (
                            !Vector3.TryParse(
                                wasInput(
                                    wasKeyValueGet(wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.POSITION)),
                                        message)),
                                out position))
                        {
                            position = Client.Self.SimPosition;
                        }
                        string region =
                            wasInput(wasKeyValueGet(wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.REGION)),
                                message));
                        Simulator simulator =
                            Client.Network.Simulators.FirstOrDefault(
                                o =>
                                    o.Name.Equals(
                                        string.IsNullOrEmpty(region) ? Client.Network.CurrentSim.Name : region,
                                        StringComparison.InvariantCultureIgnoreCase));
                        if (simulator == null)
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.REGION_NOT_FOUND));
                        }
                        Parcel parcel = null;
                        if (!GetParcelAtPosition(simulator, position, ref parcel))
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.COULD_NOT_FIND_PARCEL));
                        }
                        bool forGroup;
                        if (
                            !bool.TryParse(
                                wasInput(
                                    wasKeyValueGet(wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.FORGROUP)),
                                        message)),
                                out forGroup))
                        {
                            if (
                                !HasGroupPowers(Client.Self.AgentID, groupUUID, GroupPowers.LandDeed,
                                    Configuration.SERVICES_TIMEOUT, Configuration.DATA_TIMEOUT))
                            {
                                throw new Exception(
                                    wasGetDescriptionFromEnumValue(ScriptError.NO_GROUP_POWER_FOR_COMMAND));
                            }
                            forGroup = true;
                        }
                        bool removeContribution;
                        if (!bool.TryParse(
                            wasInput(
                                wasKeyValueGet(wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.REMOVECONTRIBUTION)),
                                    message)),
                            out removeContribution))
                        {
                            removeContribution = true;
                        }
                        ManualResetEvent ParcelInfoEvent = new ManualResetEvent(false);
                        UUID parcelUUID = UUID.Zero;
                        EventHandler<ParcelInfoReplyEventArgs> ParcelInfoEventHandler = (sender, args) =>
                        {
                            parcelUUID = args.Parcel.ID;
                            ParcelInfoEvent.Set();
                        };
                        lock (ClientInstanceParcelsLock)
                        {
                            Client.Parcels.ParcelInfoReply += ParcelInfoEventHandler;
                            Client.Parcels.RequestParcelInfo(parcelUUID);
                            if (!ParcelInfoEvent.WaitOne(Configuration.SERVICES_TIMEOUT, false))
                            {
                                Client.Parcels.ParcelInfoReply -= ParcelInfoEventHandler;
                                throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.TIMEOUT_GETTING_PARCELS));
                            }
                            Client.Parcels.ParcelInfoReply -= ParcelInfoEventHandler;
                        }
                        bool forSale = false;
                        int handledEvents = 0;
                        int counter = 1;
                        ManualResetEvent DirLandReplyEvent = new ManualResetEvent(false);
                        EventHandler<DirLandReplyEventArgs> DirLandReplyEventArgs =
                            (sender, args) =>
                            {
                                handledEvents += args.DirParcels.Count;
                                Parallel.ForEach(args.DirParcels, o =>
                                {
                                    if (o.ID.Equals(parcelUUID))
                                    {
                                        forSale = o.ForSale;
                                        DirLandReplyEvent.Set();
                                    }
                                });
                                if (((handledEvents - counter)%
                                     LINDEN_CONSTANTS.DIRECTORY.LAND.SEARCH_RESULTS_COUNT).Equals(0))
                                {
                                    ++counter;
                                    Client.Directory.StartLandSearch(DirectoryManager.DirFindFlags.SortAsc,
                                        DirectoryManager.SearchTypeFlags.Any, int.MaxValue, int.MaxValue,
                                        handledEvents);
                                }
                                DirLandReplyEvent.Set();
                            };
                        lock (ClientInstanceDirectoryLock)
                        {
                            Client.Directory.DirLandReply += DirLandReplyEventArgs;
                            Client.Directory.StartLandSearch(DirectoryManager.DirFindFlags.SortAsc,
                                DirectoryManager.SearchTypeFlags.Any, int.MaxValue, int.MaxValue, handledEvents);
                            if (!DirLandReplyEvent.WaitOne(Configuration.SERVICES_TIMEOUT, false))
                            {
                                Client.Directory.DirLandReply -= DirLandReplyEventArgs;
                                throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.TIMEOUT_GETTING_PARCELS));
                            }
                            Client.Directory.DirLandReply -= DirLandReplyEventArgs;
                        }
                        if (!forSale)
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.PARCEL_NOT_FOR_SALE));
                        }
                        if (!UpdateBalance(Configuration.SERVICES_TIMEOUT))
                        {
                            throw new Exception(
                                wasGetDescriptionFromEnumValue(ScriptError.UNABLE_TO_OBTAIN_MONEY_BALANCE));
                        }
                        if (Client.Self.Balance < parcel.SalePrice)
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.INSUFFICIENT_FUNDS));
                        }
                        if (!parcel.SalePrice.Equals(0) &&
                            !HasCorrodePermission(group, (int) Permissions.PERMISSION_ECONOMY))
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.NO_CORRADE_PERMISSIONS));
                        }
                        Client.Parcels.Buy(simulator, parcel.LocalID, forGroup, groupUUID,
                            removeContribution, parcel.Area, parcel.SalePrice);
                    };
                    break;
                case ScriptKeys.PARCELEJECT:
                    execute = () =>
                    {
                        if (!HasCorrodePermission(group, (int) Permissions.PERMISSION_LAND))
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.NO_CORRADE_PERMISSIONS));
                        }
                        Group configuredGroup =
                            Configuration.GROUPS.AsParallel().FirstOrDefault(
                                o => o.Name.Equals(group, StringComparison.Ordinal));
                        UUID groupUUID = UUID.Zero;
                        switch (!configuredGroup.Equals(default(Group)))
                        {
                            case true:
                                groupUUID = configuredGroup.UUID;
                                break;
                            default:
                                if (!GroupNameToUUID(group, Configuration.SERVICES_TIMEOUT, Configuration.DATA_TIMEOUT,
                                    ref groupUUID))
                                {
                                    throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.GROUP_NOT_FOUND));
                                }
                                break;
                        }
                        Vector3 position;
                        if (
                            !Vector3.TryParse(
                                wasInput(
                                    wasKeyValueGet(wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.POSITION)),
                                        message)),
                                out position))
                        {
                            position = Client.Self.SimPosition;
                        }
                        string region =
                            wasInput(wasKeyValueGet(wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.REGION)),
                                message));
                        Simulator simulator =
                            Client.Network.Simulators.FirstOrDefault(
                                o =>
                                    o.Name.Equals(
                                        string.IsNullOrEmpty(region) ? Client.Network.CurrentSim.Name : region,
                                        StringComparison.InvariantCultureIgnoreCase));
                        if (simulator == null)
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.REGION_NOT_FOUND));
                        }
                        Parcel parcel = null;
                        if (!GetParcelAtPosition(simulator, position, ref parcel))
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.COULD_NOT_FIND_PARCEL));
                        }
                        if (!simulator.IsEstateManager)
                        {
                            if (!parcel.OwnerID.Equals(Client.Self.AgentID))
                            {
                                if (!parcel.IsGroupOwned && !parcel.GroupID.Equals(groupUUID))
                                {
                                    throw new Exception(
                                        wasGetDescriptionFromEnumValue(ScriptError.NO_GROUP_POWER_FOR_COMMAND));
                                }
                                if (!HasGroupPowers(Client.Self.AgentID, groupUUID, GroupPowers.LandEjectAndFreeze,
                                    Configuration.SERVICES_TIMEOUT, Configuration.DATA_TIMEOUT))
                                {
                                    throw new Exception(
                                        wasGetDescriptionFromEnumValue(ScriptError.NO_GROUP_POWER_FOR_COMMAND));
                                }
                            }
                        }
                        UUID agentUUID;
                        if (
                            !UUID.TryParse(
                                wasInput(wasKeyValueGet(
                                    wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.AGENT)), message)),
                                out agentUUID) && !AgentNameToUUID(
                                    wasInput(
                                        wasKeyValueGet(wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.FIRSTNAME)),
                                            message)),
                                    wasInput(
                                        wasKeyValueGet(wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.LASTNAME)),
                                            message)),
                                    Configuration.SERVICES_TIMEOUT, Configuration.DATA_TIMEOUT, ref agentUUID))
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.AGENT_NOT_FOUND));
                        }
                        bool alsoban;
                        if (
                            !bool.TryParse(
                                wasInput(wasKeyValueGet(wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.BAN)),
                                    message)),
                                out alsoban))
                        {
                            alsoban = false;
                        }
                        Client.Parcels.EjectUser(agentUUID, alsoban);
                    };
                    break;
                case ScriptKeys.PARCELFREEZE:
                    execute = () =>
                    {
                        if (!HasCorrodePermission(group, (int) Permissions.PERMISSION_LAND))
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.NO_CORRADE_PERMISSIONS));
                        }
                        Group configuredGroup =
                            Configuration.GROUPS.AsParallel().FirstOrDefault(
                                o => o.Name.Equals(group, StringComparison.Ordinal));
                        UUID groupUUID = UUID.Zero;
                        switch (!configuredGroup.Equals(default(Group)))
                        {
                            case true:
                                groupUUID = configuredGroup.UUID;
                                break;
                            default:
                                if (!GroupNameToUUID(group, Configuration.SERVICES_TIMEOUT, Configuration.DATA_TIMEOUT,
                                    ref groupUUID))
                                {
                                    throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.GROUP_NOT_FOUND));
                                }
                                break;
                        }
                        Vector3 position;
                        if (
                            !Vector3.TryParse(
                                wasInput(
                                    wasKeyValueGet(wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.POSITION)),
                                        message)),
                                out position))
                        {
                            position = Client.Self.SimPosition;
                        }
                        string region =
                            wasInput(wasKeyValueGet(wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.REGION)),
                                message));
                        Simulator simulator =
                            Client.Network.Simulators.FirstOrDefault(
                                o =>
                                    o.Name.Equals(
                                        string.IsNullOrEmpty(region) ? Client.Network.CurrentSim.Name : region,
                                        StringComparison.InvariantCultureIgnoreCase));
                        if (simulator == null)
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.REGION_NOT_FOUND));
                        }
                        Parcel parcel = null;
                        if (!GetParcelAtPosition(simulator, position, ref parcel))
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.COULD_NOT_FIND_PARCEL));
                        }
                        if (!simulator.IsEstateManager)
                        {
                            if (!parcel.OwnerID.Equals(Client.Self.AgentID))
                            {
                                if (!parcel.IsGroupOwned && !parcel.GroupID.Equals(groupUUID))
                                {
                                    throw new Exception(
                                        wasGetDescriptionFromEnumValue(ScriptError.NO_GROUP_POWER_FOR_COMMAND));
                                }
                                if (!HasGroupPowers(Client.Self.AgentID, groupUUID, GroupPowers.LandEjectAndFreeze,
                                    Configuration.SERVICES_TIMEOUT, Configuration.DATA_TIMEOUT))
                                {
                                    throw new Exception(
                                        wasGetDescriptionFromEnumValue(ScriptError.NO_GROUP_POWER_FOR_COMMAND));
                                }
                            }
                        }
                        UUID agentUUID;
                        if (
                            !UUID.TryParse(
                                wasInput(wasKeyValueGet(
                                    wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.AGENT)), message)),
                                out agentUUID) && !AgentNameToUUID(
                                    wasInput(
                                        wasKeyValueGet(wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.FIRSTNAME)),
                                            message)),
                                    wasInput(
                                        wasKeyValueGet(wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.LASTNAME)),
                                            message)),
                                    Configuration.SERVICES_TIMEOUT, Configuration.DATA_TIMEOUT, ref agentUUID))
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.AGENT_NOT_FOUND));
                        }
                        bool freeze;
                        if (
                            !bool.TryParse(
                                wasInput(
                                    wasKeyValueGet(wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.FREEZE)), message)),
                                out freeze))
                        {
                            freeze = false;
                        }
                        Client.Parcels.FreezeUser(agentUUID, freeze);
                    };
                    break;
                case ScriptKeys.SETPROFILEDATA:
                    execute = () =>
                    {
                        if (
                            !HasCorrodePermission(group,
                                (int) Permissions.PERMISSION_GROOMING))
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.NO_CORRADE_PERMISSIONS));
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
                        lock (ClientInstanceAvatarsLock)
                        {
                            Client.Avatars.AvatarPropertiesReply += AvatarPropertiesEventHandler;
                            Client.Avatars.AvatarInterestsReply += AvatarInterestsEventHandler;
                            Client.Avatars.RequestAvatarProperties(Client.Self.AgentID);
                            if (
                                !WaitHandle.WaitAll(AvatarProfileDataEvent.Select(o => (WaitHandle) o).ToArray(),
                                    Configuration.SERVICES_TIMEOUT, false))
                            {
                                Client.Avatars.AvatarPropertiesReply -= AvatarPropertiesEventHandler;
                                Client.Avatars.AvatarInterestsReply -= AvatarInterestsEventHandler;
                                throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.TIMEOUT_GETTING_PROFILE));
                            }
                            Client.Avatars.AvatarPropertiesReply -= AvatarPropertiesEventHandler;
                            Client.Avatars.AvatarInterestsReply -= AvatarInterestsEventHandler;
                        }
                        string fields =
                            wasInput(wasKeyValueGet(wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.DATA)),
                                message));
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
                            !HasCorrodePermission(group,
                                (int) Permissions.PERMISSION_INTERACT))
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.NO_CORRADE_PERMISSIONS));
                        }
                        UUID agentUUID;
                        if (
                            !UUID.TryParse(
                                wasInput(wasKeyValueGet(
                                    wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.AGENT)), message)),
                                out agentUUID) && !AgentNameToUUID(
                                    wasInput(
                                        wasKeyValueGet(wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.FIRSTNAME)),
                                            message)),
                                    wasInput(
                                        wasKeyValueGet(wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.LASTNAME)),
                                            message)),
                                    Configuration.SERVICES_TIMEOUT, Configuration.DATA_TIMEOUT, ref agentUUID))
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.AGENT_NOT_FOUND));
                        }
                        wasAdaptiveAlarm ProfileDataReceivedAlarm = new wasAdaptiveAlarm(Configuration.DATA_DECAY_TYPE);
                        Avatar.AvatarProperties properties = new Avatar.AvatarProperties();
                        Avatar.Interests interests = new Avatar.Interests();
                        List<AvatarGroup> groups = new List<AvatarGroup>();
                        AvatarPicksReplyEventArgs picks = null;
                        AvatarClassifiedReplyEventArgs classifieds = null;
                        object LockObject = new object();
                        EventHandler<AvatarInterestsReplyEventArgs> AvatarInterestsReplyEventHandler = (sender, args) =>
                        {
                            ProfileDataReceivedAlarm.Alarm(Configuration.DATA_TIMEOUT);
                            interests = args.Interests;
                        };
                        EventHandler<AvatarPropertiesReplyEventArgs> AvatarPropertiesReplyEventHandler =
                            (sender, args) =>
                            {
                                ProfileDataReceivedAlarm.Alarm(Configuration.DATA_TIMEOUT);
                                properties = args.Properties;
                            };
                        EventHandler<AvatarGroupsReplyEventArgs> AvatarGroupsReplyEventHandler = (sender, args) =>
                        {
                            ProfileDataReceivedAlarm.Alarm(Configuration.DATA_TIMEOUT);
                            lock (LockObject)
                            {
                                groups.AddRange(args.Groups);
                            }
                        };
                        EventHandler<AvatarPicksReplyEventArgs> AvatarPicksReplyEventHandler =
                            (sender, args) =>
                            {
                                ProfileDataReceivedAlarm.Alarm(Configuration.DATA_TIMEOUT);
                                picks = args;
                            };
                        EventHandler<AvatarClassifiedReplyEventArgs> AvatarClassifiedReplyEventHandler =
                            (sender, args) =>
                            {
                                ProfileDataReceivedAlarm.Alarm(Configuration.DATA_TIMEOUT);
                                classifieds = args;
                            };
                        lock (ClientInstanceAvatarsLock)
                        {
                            Client.Avatars.AvatarInterestsReply += AvatarInterestsReplyEventHandler;
                            Client.Avatars.AvatarPropertiesReply += AvatarPropertiesReplyEventHandler;
                            Client.Avatars.AvatarGroupsReply += AvatarGroupsReplyEventHandler;
                            Client.Avatars.AvatarPicksReply += AvatarPicksReplyEventHandler;
                            Client.Avatars.AvatarClassifiedReply += AvatarClassifiedReplyEventHandler;
                            Client.Avatars.RequestAvatarProperties(agentUUID);
                            Client.Avatars.RequestAvatarPicks(agentUUID);
                            Client.Avatars.RequestAvatarClassified(agentUUID);
                            if (!ProfileDataReceivedAlarm.Signal.WaitOne(Configuration.SERVICES_TIMEOUT, false))
                            {
                                Client.Avatars.AvatarInterestsReply -= AvatarInterestsReplyEventHandler;
                                Client.Avatars.AvatarPropertiesReply -= AvatarPropertiesReplyEventHandler;
                                Client.Avatars.AvatarGroupsReply -= AvatarGroupsReplyEventHandler;
                                Client.Avatars.AvatarPicksReply -= AvatarPicksReplyEventHandler;
                                Client.Avatars.AvatarClassifiedReply -= AvatarClassifiedReplyEventHandler;
                                throw new Exception(
                                    wasGetDescriptionFromEnumValue(ScriptError.TIMEOUT_GETTING_AVATAR_DATA));
                            }
                            Client.Avatars.AvatarInterestsReply -= AvatarInterestsReplyEventHandler;
                            Client.Avatars.AvatarPropertiesReply -= AvatarPropertiesReplyEventHandler;
                            Client.Avatars.AvatarGroupsReply -= AvatarGroupsReplyEventHandler;
                            Client.Avatars.AvatarPicksReply -= AvatarPicksReplyEventHandler;
                            Client.Avatars.AvatarClassifiedReply -= AvatarClassifiedReplyEventHandler;
                        }
                        string fields =
                            wasInput(wasKeyValueGet(wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.DATA)),
                                message));
                        List<string> csv = new List<string>();
                        csv.AddRange(GetStructuredData(properties, fields));
                        csv.AddRange(GetStructuredData(interests, fields));
                        csv.AddRange(GetStructuredData(groups, fields));
                        if (picks != null)
                        {
                            csv.AddRange(GetStructuredData(picks, fields));
                        }
                        if (classifieds != null)
                        {
                            csv.AddRange(GetStructuredData(classifieds, fields));
                        }
                        if (!csv.Count.Equals(0))
                        {
                            result.Add(wasGetDescriptionFromEnumValue(ResultKeys.DATA),
                                wasEnumerableToCSV(csv));
                        }
                    };
                    break;
                case ScriptKeys.GIVE:
                    execute = () =>
                    {
                        if (
                            !HasCorrodePermission(group,
                                (int) Permissions.PERMISSION_INVENTORY))
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.NO_CORRADE_PERMISSIONS));
                        }
                        InventoryBase item =
                            FindInventory<InventoryBase>(Client.Inventory.Store.RootNode,
                                wasInput(wasKeyValueGet(
                                    wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.ITEM)), message))
                                ).FirstOrDefault();
                        if (item == null)
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.INVENTORY_ITEM_NOT_FOUND));
                        }
                        switch (
                            wasGetEnumValueFromDescription<Entity>(
                                wasInput(
                                    wasKeyValueGet(wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.ENTITY)),
                                        message)).ToLowerInvariant()))
                        {
                            case Entity.AVATAR:
                                UUID agentUUID;
                                if (
                                    !UUID.TryParse(
                                        wasInput(
                                            wasKeyValueGet(wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.AGENT)),
                                                message)), out agentUUID) && !AgentNameToUUID(
                                                    wasInput(
                                                        wasKeyValueGet(
                                                            wasOutput(
                                                                wasGetDescriptionFromEnumValue(ScriptKeys.FIRSTNAME)),
                                                            message)),
                                                    wasInput(
                                                        wasKeyValueGet(
                                                            wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.LASTNAME)),
                                                            message)),
                                                    Configuration.SERVICES_TIMEOUT, Configuration.DATA_TIMEOUT,
                                                    ref agentUUID))
                                {
                                    throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.AGENT_NOT_FOUND));
                                }
                                InventoryItem inventoryItem = item as InventoryItem;
                                if (inventoryItem != null)
                                {
                                    Client.Inventory.GiveItem(item.UUID, item.Name,
                                        inventoryItem.AssetType, agentUUID, true);
                                }
                                break;
                            case Entity.OBJECT:
                                float range;
                                if (
                                    !float.TryParse(
                                        wasInput(
                                            wasKeyValueGet(wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.RANGE)),
                                                message)),
                                        out range))
                                {
                                    range = Configuration.RANGE;
                                }
                                Primitive primitive = null;
                                if (
                                    !FindPrimitive(
                                        StringOrUUID(wasInput(
                                            wasKeyValueGet(wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.TARGET)),
                                                message))),
                                        range,
                                        ref primitive, Configuration.SERVICES_TIMEOUT, Configuration.DATA_TIMEOUT))
                                {
                                    throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.PRIMITIVE_NOT_FOUND));
                                }
                                Client.Inventory.UpdateTaskInventory(primitive.LocalID,
                                    item as InventoryItem);
                                break;
                            default:
                                throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.UNKNOWN_ENTITY));
                        }
                    };
                    break;
                case ScriptKeys.DELETEITEM:
                    execute = () =>
                    {
                        if (
                            !HasCorrodePermission(group,
                                (int) Permissions.PERMISSION_INVENTORY))
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.NO_CORRADE_PERMISSIONS));
                        }
                        HashSet<InventoryItem> items =
                            new HashSet<InventoryItem>(FindInventory<InventoryBase>(Client.Inventory.Store.RootNode,
                                wasInput(wasKeyValueGet(
                                    wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.ITEM)), message))
                                ).Cast<InventoryItem>());
                        if (items.Count.Equals(0))
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.INVENTORY_ITEM_NOT_FOUND));
                        }
                        Parallel.ForEach(items, o =>
                        {
                            switch (o.AssetType)
                            {
                                case AssetType.Folder:
                                    Client.Inventory.MoveFolder(o.UUID,
                                        Client.Inventory.FindFolderForType(FolderType.Trash));
                                    break;
                                default:
                                    Client.Inventory.MoveItem(o.UUID,
                                        Client.Inventory.FindFolderForType(FolderType.Trash));
                                    break;
                            }
                        });
                    };
                    break;
                case ScriptKeys.EMPTYTRASH:
                    execute = () =>
                    {
                        if (
                            !HasCorrodePermission(group,
                                (int) Permissions.PERMISSION_INVENTORY))
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.NO_CORRADE_PERMISSIONS));
                        }
                        Client.Inventory.EmptyTrash();
                    };
                    break;
                case ScriptKeys.FLY:
                    execute = () =>
                    {
                        if (
                            !HasCorrodePermission(group,
                                (int) Permissions.PERMISSION_MOVEMENT))
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.NO_CORRADE_PERMISSIONS));
                        }
                        Action action =
                            wasGetEnumValueFromDescription<Action>(
                                wasInput(
                                    wasKeyValueGet(wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.ACTION)), message))
                                    .ToLowerInvariant());
                        switch (action)
                        {
                            case Action.START:
                            case Action.STOP:
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
                                Client.Self.Fly(action.Equals(Action.START));
                                break;
                            default:
                                throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.FLY_ACTION_START_OR_STOP));
                        }
                        // Set the camera on the avatar.
                        Client.Self.Movement.Camera.LookAt(
                            Client.Self.SimPosition,
                            Client.Self.SimPosition
                            );
                    };
                    break;
                case ScriptKeys.ADDPICK:
                    execute = () =>
                    {
                        if (
                            !HasCorrodePermission(group,
                                (int) Permissions.PERMISSION_GROOMING))
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.NO_CORRADE_PERMISSIONS));
                        }
                        string item =
                            wasInput(wasKeyValueGet(wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.ITEM)),
                                message));
                        UUID textureUUID = UUID.Zero;
                        if (!string.IsNullOrEmpty(item) && !UUID.TryParse(item, out textureUUID))
                        {
                            InventoryBase inventoryBaseItem =
                                FindInventory<InventoryBase>(Client.Inventory.Store.RootNode, item
                                    ).FirstOrDefault();
                            if (inventoryBaseItem == null)
                            {
                                throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.TEXTURE_NOT_FOUND));
                            }
                            textureUUID = inventoryBaseItem.UUID;
                        }
                        ManualResetEvent AvatarPicksReplyEvent = new ManualResetEvent(false);
                        UUID pickUUID = UUID.Zero;
                        string name =
                            wasInput(wasKeyValueGet(wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.NAME)),
                                message));
                        if (string.IsNullOrEmpty(name))
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.EMPTY_PICK_NAME));
                        }
                        EventHandler<AvatarPicksReplyEventArgs> AvatarPicksEventHandler = (sender, args) =>
                        {
                            KeyValuePair<UUID, string> pick =
                                args.Picks.AsParallel()
                                    .FirstOrDefault(o => o.Value.Equals(name, StringComparison.Ordinal));
                            if (!pick.Equals(default(KeyValuePair<UUID, string>)))
                                pickUUID = pick.Key;
                            AvatarPicksReplyEvent.Set();
                        };
                        lock (ClientInstanceAvatarsLock)
                        {
                            Client.Avatars.AvatarPicksReply += AvatarPicksEventHandler;
                            Client.Avatars.RequestAvatarPicks(Client.Self.AgentID);
                            if (!AvatarPicksReplyEvent.WaitOne(Configuration.SERVICES_TIMEOUT, false))
                            {
                                Client.Avatars.AvatarPicksReply -= AvatarPicksEventHandler;
                                throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.TIMEOUT_GETTING_PICKS));
                            }
                            Client.Avatars.AvatarPicksReply -= AvatarPicksEventHandler;
                        }
                        if (pickUUID.Equals(UUID.Zero))
                        {
                            pickUUID = UUID.Random();
                        }
                        Client.Self.PickInfoUpdate(pickUUID, false, UUID.Zero, name,
                            Client.Self.GlobalPosition, textureUUID,
                            wasInput(
                                wasKeyValueGet(wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.DESCRIPTION)),
                                    message)));
                    };
                    break;
                case ScriptKeys.DELETEPICK:
                    execute = () =>
                    {
                        if (
                            !HasCorrodePermission(group,
                                (int) Permissions.PERMISSION_GROOMING))
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.NO_CORRADE_PERMISSIONS));
                        }
                        ManualResetEvent AvatarPicksReplyEvent = new ManualResetEvent(false);
                        string input =
                            wasInput(wasKeyValueGet(wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.NAME)),
                                message));
                        if (string.IsNullOrEmpty(input))
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.EMPTY_PICK_NAME));
                        }
                        UUID pickUUID = UUID.Zero;
                        EventHandler<AvatarPicksReplyEventArgs> AvatarPicksEventHandler = (sender, args) =>
                        {
                            KeyValuePair<UUID, string> pick = args.Picks.AsParallel().FirstOrDefault(
                                o => o.Value.Equals(input, StringComparison.Ordinal));
                            if (!pick.Equals(default(KeyValuePair<UUID, string>)))
                                pickUUID = pick.Key;
                            AvatarPicksReplyEvent.Set();
                        };
                        lock (ClientInstanceAvatarsLock)
                        {
                            Client.Avatars.AvatarPicksReply += AvatarPicksEventHandler;
                            Client.Avatars.RequestAvatarPicks(Client.Self.AgentID);
                            if (!AvatarPicksReplyEvent.WaitOne(Configuration.SERVICES_TIMEOUT, false))
                            {
                                Client.Avatars.AvatarPicksReply -= AvatarPicksEventHandler;
                                throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.TIMEOUT_GETTING_PICKS));
                            }
                            Client.Avatars.AvatarPicksReply -= AvatarPicksEventHandler;
                        }
                        if (pickUUID.Equals(UUID.Zero))
                        {
                            pickUUID = UUID.Random();
                        }
                        Client.Self.PickDelete(pickUUID);
                    };
                    break;
                case ScriptKeys.ADDCLASSIFIED:
                    execute = () =>
                    {
                        if (!HasCorrodePermission(group, (int) Permissions.PERMISSION_GROOMING) ||
                            !HasCorrodePermission(group, (int) Permissions.PERMISSION_ECONOMY))
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.NO_CORRADE_PERMISSIONS));
                        }
                        string item =
                            wasInput(wasKeyValueGet(wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.ITEM)),
                                message));
                        UUID textureUUID = UUID.Zero;
                        if (!string.IsNullOrEmpty(item) && !UUID.TryParse(item, out textureUUID))
                        {
                            InventoryBase inventoryBaseItem =
                                FindInventory<InventoryBase>(Client.Inventory.Store.RootNode, item
                                    ).FirstOrDefault();
                            if (inventoryBaseItem == null)
                            {
                                throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.TEXTURE_NOT_FOUND));
                            }
                            textureUUID = inventoryBaseItem.UUID;
                        }
                        string name =
                            wasInput(wasKeyValueGet(wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.NAME)),
                                message));
                        if (string.IsNullOrEmpty(name))
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.EMPTY_CLASSIFIED_NAME));
                        }
                        string classifiedDescription =
                            wasInput(
                                wasKeyValueGet(wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.DESCRIPTION)),
                                    message));
                        ManualResetEvent AvatarClassifiedReplyEvent = new ManualResetEvent(false);
                        UUID classifiedUUID = UUID.Zero;
                        EventHandler<AvatarClassifiedReplyEventArgs> AvatarClassifiedEventHandler = (sender, args) =>
                        {
                            KeyValuePair<UUID, string> classified = args.Classifieds.AsParallel().FirstOrDefault(
                                o =>
                                    o.Value.Equals(name, StringComparison.Ordinal));
                            if (!classified.Equals(default(KeyValuePair<UUID, string>)))
                                classifiedUUID = classified.Key;
                            AvatarClassifiedReplyEvent.Set();
                        };
                        lock (ClientInstanceAvatarsLock)
                        {
                            Client.Avatars.AvatarClassifiedReply += AvatarClassifiedEventHandler;
                            Client.Avatars.RequestAvatarClassified(Client.Self.AgentID);
                            if (!AvatarClassifiedReplyEvent.WaitOne(Configuration.SERVICES_TIMEOUT, false))
                            {
                                Client.Avatars.AvatarClassifiedReply -= AvatarClassifiedEventHandler;
                                throw new Exception(
                                    wasGetDescriptionFromEnumValue(ScriptError.TIMEOUT_GETTING_CLASSIFIEDS));
                            }
                            Client.Avatars.AvatarClassifiedReply -= AvatarClassifiedEventHandler;
                        }
                        if (classifiedUUID.Equals(UUID.Zero))
                        {
                            classifiedUUID = UUID.Random();
                        }
                        int price;
                        if (
                            !int.TryParse(
                                wasInput(wasKeyValueGet(
                                    wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.PRICE)), message)),
                                out price))
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.INVALID_PRICE));
                        }
                        if (price < 0)
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.INVALID_PRICE));
                        }
                        bool renew;
                        if (
                            !bool.TryParse(
                                wasInput(wasKeyValueGet(
                                    wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.RENEW)), message)),
                                out renew))
                        {
                            renew = false;
                        }
                        FieldInfo classifiedCategoriesField = typeof (DirectoryManager.ClassifiedCategories).GetFields(
                            BindingFlags.Public |
                            BindingFlags.Static)
                            .AsParallel().FirstOrDefault(o =>
                                o.Name.Equals(
                                    wasInput(
                                        wasKeyValueGet(wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.TYPE)),
                                            message)),
                                    StringComparison.Ordinal));
                        Client.Self.UpdateClassifiedInfo(classifiedUUID, classifiedCategoriesField != null
                            ? (DirectoryManager.ClassifiedCategories)
                                classifiedCategoriesField.GetValue(null)
                            : DirectoryManager.ClassifiedCategories.Any, textureUUID, price,
                            name, classifiedDescription, renew);
                    };
                    break;
                case ScriptKeys.DELETECLASSIFIED:
                    execute = () =>
                    {
                        if (!HasCorrodePermission(group, (int) Permissions.PERMISSION_GROOMING))
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.NO_CORRADE_PERMISSIONS));
                        }
                        string name =
                            wasInput(wasKeyValueGet(wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.NAME)),
                                message));
                        if (string.IsNullOrEmpty(name))
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.EMPTY_CLASSIFIED_NAME));
                        }
                        ManualResetEvent AvatarClassifiedReplyEvent = new ManualResetEvent(false);
                        UUID classifiedUUID = UUID.Zero;
                        EventHandler<AvatarClassifiedReplyEventArgs> AvatarClassifiedEventHandler = (sender, args) =>
                        {
                            KeyValuePair<UUID, string> classified = args.Classifieds.AsParallel().FirstOrDefault(
                                o =>
                                    o.Value.Equals(name, StringComparison.Ordinal));
                            if (!classified.Equals(default(KeyValuePair<UUID, string>)))
                                classifiedUUID = classified.Key;
                            AvatarClassifiedReplyEvent.Set();
                        };
                        lock (ClientInstanceAvatarsLock)
                        {
                            Client.Avatars.AvatarClassifiedReply += AvatarClassifiedEventHandler;
                            Client.Avatars.RequestAvatarClassified(Client.Self.AgentID);
                            if (!AvatarClassifiedReplyEvent.WaitOne(Configuration.SERVICES_TIMEOUT, false))
                            {
                                Client.Avatars.AvatarClassifiedReply -= AvatarClassifiedEventHandler;
                                throw new Exception(
                                    wasGetDescriptionFromEnumValue(ScriptError.TIMEOUT_GETTING_CLASSIFIEDS));
                            }
                            Client.Avatars.AvatarClassifiedReply -= AvatarClassifiedEventHandler;
                        }
                        if (classifiedUUID.Equals(UUID.Zero))
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.COULD_NOT_FIND_CLASSIFIED));
                        }
                        Client.Self.DeleteClassified(classifiedUUID);
                    };
                    break;
                case ScriptKeys.TOUCH:
                    execute = () =>
                    {
                        if (
                            !HasCorrodePermission(group, (int) Permissions.PERMISSION_INTERACT))
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.NO_CORRADE_PERMISSIONS));
                        }
                        float range;
                        if (
                            !float.TryParse(
                                wasInput(wasKeyValueGet(
                                    wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.RANGE)), message)),
                                out range))
                        {
                            range = Configuration.RANGE;
                        }
                        Primitive primitive = null;
                        if (
                            !FindPrimitive(
                                StringOrUUID(wasInput(wasKeyValueGet(
                                    wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.ITEM)), message))),
                                range,
                                ref primitive, Configuration.SERVICES_TIMEOUT, Configuration.DATA_TIMEOUT))
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.PRIMITIVE_NOT_FOUND));
                        }
                        Client.Self.Touch(primitive.LocalID);
                    };
                    break;
                case ScriptKeys.MODERATE:
                    execute = () =>
                    {
                        if (!HasCorrodePermission(group, (int) Permissions.PERMISSION_GROUP))
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.NO_CORRADE_PERMISSIONS));
                        }
                        Group configuredGroup =
                            Configuration.GROUPS.AsParallel().FirstOrDefault(
                                o => o.Name.Equals(group, StringComparison.Ordinal));
                        UUID groupUUID = UUID.Zero;
                        switch (!configuredGroup.Equals(default(Group)))
                        {
                            case true:
                                groupUUID = configuredGroup.UUID;
                                break;
                            default:
                                if (!GroupNameToUUID(group, Configuration.SERVICES_TIMEOUT, Configuration.DATA_TIMEOUT,
                                    ref groupUUID))
                                {
                                    throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.GROUP_NOT_FOUND));
                                }
                                break;
                        }
                        if (
                            !HasGroupPowers(Client.Self.AgentID, groupUUID, GroupPowers.ModerateChat,
                                Configuration.SERVICES_TIMEOUT, Configuration.DATA_TIMEOUT))
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.NO_GROUP_POWER_FOR_COMMAND));
                        }
                        UUID agentUUID;
                        if (
                            !UUID.TryParse(
                                wasInput(wasKeyValueGet(
                                    wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.AGENT)), message)),
                                out agentUUID) && !AgentNameToUUID(
                                    wasInput(
                                        wasKeyValueGet(wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.FIRSTNAME)),
                                            message)),
                                    wasInput(
                                        wasKeyValueGet(wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.LASTNAME)),
                                            message)),
                                    Configuration.SERVICES_TIMEOUT, Configuration.DATA_TIMEOUT, ref agentUUID))
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.AGENT_NOT_FOUND));
                        }
                        if (
                            !AgentInGroup(agentUUID, groupUUID, Configuration.SERVICES_TIMEOUT,
                                Configuration.DATA_TIMEOUT))
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.AGENT_NOT_IN_GROUP));
                        }
                        bool silence;
                        if (
                            !bool.TryParse(
                                wasInput(
                                    wasKeyValueGet(wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.SILENCE)),
                                        message)),
                                out silence))
                        {
                            silence = false;
                        }
                        Type type =
                            wasGetEnumValueFromDescription<Type>(
                                wasInput(wasKeyValueGet(
                                    wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.TYPE)), message))
                                    .ToLowerInvariant());
                        switch (type)
                        {
                            case Type.TEXT:
                            case Type.VOICE:
                                Client.Self.ModerateChatSessions(groupUUID, agentUUID,
                                    wasGetDescriptionFromEnumValue(type),
                                    silence);
                                break;
                            default:
                                throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.TYPE_CAN_BE_VOICE_OR_TEXT));
                        }
                    };
                    break;
                case ScriptKeys.REBAKE:
                    execute = () =>
                    {
                        if (
                            !HasCorrodePermission(group,
                                (int) Permissions.PERMISSION_GROOMING))
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.NO_CORRADE_PERMISSIONS));
                        }
                        RebakeTimer.Change(Configuration.REBAKE_DELAY, 0);
                    };
                    break;
                case ScriptKeys.GETWEARABLES:
                    execute = () =>
                    {
                        if (!HasCorrodePermission(group, (int) Permissions.PERMISSION_GROOMING))
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.NO_CORRADE_PERMISSIONS));
                        }
                        HashSet<string> data =
                            new HashSet<string>(GetWearables(Client.Inventory.Store.RootNode)
                                .AsParallel()
                                .Select(o => new[]
                                {
                                    o.Value.ToString(),
                                    Client.Inventory.Store[o.Key.ItemID].Name
                                }).SelectMany(o => o));
                        if (!data.Count.Equals(0))
                        {
                            result.Add(wasGetDescriptionFromEnumValue(ResultKeys.DATA),
                                wasEnumerableToCSV(data));
                        }
                    };
                    break;
                case ScriptKeys.WEAR:
                    execute = () =>
                    {
                        if (!HasCorrodePermission(group, (int) Permissions.PERMISSION_GROOMING))
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.NO_CORRADE_PERMISSIONS));
                        }
                        string wearables =
                            wasInput(wasKeyValueGet(
                                wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.WEARABLES)), message));
                        if (string.IsNullOrEmpty(wearables))
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.EMPTY_WEARABLES));
                        }
                        bool replace;
                        if (
                            !bool.TryParse(
                                wasInput(
                                    wasKeyValueGet(wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.REPLACE)),
                                        message)),
                                out replace))
                        {
                            replace = true;
                        }
                        Parallel.ForEach(wasCSVToEnumerable(
                            wearables), o =>
                            {
                                InventoryBase inventoryBaseItem =
                                    FindInventory<InventoryBase>(Client.Inventory.Store.RootNode, o
                                        ).AsParallel().FirstOrDefault(p => p is InventoryWearable);
                                if (inventoryBaseItem == null)
                                    return;
                                Wear(inventoryBaseItem as InventoryItem, replace);
                            });
                        RebakeTimer.Change(Configuration.REBAKE_DELAY, 0);
                    };
                    break;
                case ScriptKeys.UNWEAR:
                    execute = () =>
                    {
                        if (!HasCorrodePermission(group, (int) Permissions.PERMISSION_GROOMING))
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.NO_CORRADE_PERMISSIONS));
                        }
                        string wearables =
                            wasInput(wasKeyValueGet(
                                wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.WEARABLES)), message));
                        if (string.IsNullOrEmpty(wearables))
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.EMPTY_WEARABLES));
                        }
                        Parallel.ForEach(wasCSVToEnumerable(
                            wearables), o =>
                            {
                                InventoryBase inventoryBaseItem =
                                    FindInventory<InventoryBase>(Client.Inventory.Store.RootNode, o
                                        ).AsParallel().FirstOrDefault(p => p is InventoryWearable);
                                if (inventoryBaseItem == null)
                                    return;
                                UnWear(inventoryBaseItem as InventoryItem);
                            });
                        RebakeTimer.Change(Configuration.REBAKE_DELAY, 0);
                    };
                    break;
                case ScriptKeys.GETATTACHMENTS:
                    execute = () =>
                    {
                        if (
                            !HasCorrodePermission(group,
                                (int) Permissions.PERMISSION_GROOMING))
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.NO_CORRADE_PERMISSIONS));
                        }
                        List<string> attachments = GetAttachments(
                            Configuration.SERVICES_TIMEOUT).AsParallel().Select(o => new[]
                            {
                                o.Value.ToString(),
                                o.Key.Properties.Name
                            }).SelectMany(o => o).ToList();
                        if (!attachments.Count.Equals(0))
                        {
                            result.Add(wasGetDescriptionFromEnumValue(ResultKeys.DATA),
                                wasEnumerableToCSV(attachments));
                        }
                    };
                    break;
                case ScriptKeys.ATTACH:
                    execute = () =>
                    {
                        if (
                            !HasCorrodePermission(group,
                                (int) Permissions.PERMISSION_GROOMING))
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.NO_CORRADE_PERMISSIONS));
                        }
                        string attachments =
                            wasInput(
                                wasKeyValueGet(wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.ATTACHMENTS)),
                                    message));
                        if (string.IsNullOrEmpty(attachments))
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.EMPTY_ATTACHMENTS));
                        }
                        bool replace;
                        if (
                            !bool.TryParse(
                                wasInput(
                                    wasKeyValueGet(wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.REPLACE)),
                                        message)),
                                out replace))
                        {
                            replace = true;
                        }
                        Parallel.ForEach(
                            wasCSVToEnumerable(attachments).AsParallel().Select((o, p) => new {o, p})
                                .GroupBy(q => q.p/2, q => q.o)
                                .Select(o => o.ToList())
                                .TakeWhile(o => o.Count%2 == 0)
                                .ToDictionary(o => o.First(), p => p.Last()),
                            o =>
                                Parallel.ForEach(
                                    typeof (AttachmentPoint).GetFields(BindingFlags.Public | BindingFlags.Static)
                                        .AsParallel().Where(
                                            p =>
                                                p.Name.Equals(o.Key, StringComparison.Ordinal)),
                                    q =>
                                    {
                                        InventoryBase inventoryBaseItem =
                                            FindInventory<InventoryBase>(Client.Inventory.Store.RootNode, o.Value
                                                )
                                                .AsParallel().FirstOrDefault(
                                                    r => r is InventoryObject || r is InventoryAttachment);
                                        if (inventoryBaseItem == null)
                                            return;
                                        Attach(inventoryBaseItem as InventoryItem, (AttachmentPoint) q.GetValue(null),
                                            replace);
                                    }));
                        RebakeTimer.Change(Configuration.REBAKE_DELAY, 0);
                    };
                    break;
                case ScriptKeys.DETACH:
                    execute = () =>
                    {
                        if (
                            !HasCorrodePermission(group,
                                (int) Permissions.PERMISSION_GROOMING))
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.NO_CORRADE_PERMISSIONS));
                        }
                        string attachments =
                            wasInput(
                                wasKeyValueGet(wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.ATTACHMENTS)),
                                    message));
                        if (string.IsNullOrEmpty(attachments))
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.EMPTY_ATTACHMENTS));
                        }
                        Parallel.ForEach(wasCSVToEnumerable(
                            attachments), o =>
                            {
                                InventoryBase inventoryBaseItem =
                                    FindInventory<InventoryBase>(Client.Inventory.Store.RootNode, o
                                        )
                                        .AsParallel().FirstOrDefault(
                                            p =>
                                                p is InventoryObject || p is InventoryAttachment);
                                if (inventoryBaseItem == null)
                                    return;
                                Detach(inventoryBaseItem as InventoryItem);
                            });
                        RebakeTimer.Change(Configuration.REBAKE_DELAY, 0);
                    };
                    break;
                case ScriptKeys.RETURNPRIMITIVES:
                    execute = () =>
                    {
                        if (!HasCorrodePermission(group, (int) Permissions.PERMISSION_LAND))
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.NO_CORRADE_PERMISSIONS));
                        }
                        Group configuredGroup =
                            Configuration.GROUPS.AsParallel().FirstOrDefault(
                                o => o.Name.Equals(group, StringComparison.Ordinal));
                        UUID groupUUID = UUID.Zero;
                        switch (!configuredGroup.Equals(default(Group)))
                        {
                            case true:
                                groupUUID = configuredGroup.UUID;
                                break;
                            default:
                                if (!GroupNameToUUID(group, Configuration.SERVICES_TIMEOUT, Configuration.DATA_TIMEOUT,
                                    ref groupUUID))
                                {
                                    throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.GROUP_NOT_FOUND));
                                }
                                break;
                        }
                        UUID agentUUID;
                        if (
                            !UUID.TryParse(
                                wasInput(wasKeyValueGet(
                                    wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.AGENT)), message)),
                                out agentUUID) && !AgentNameToUUID(
                                    wasInput(
                                        wasKeyValueGet(wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.FIRSTNAME)),
                                            message)),
                                    wasInput(
                                        wasKeyValueGet(wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.LASTNAME)),
                                            message)),
                                    Configuration.SERVICES_TIMEOUT, Configuration.DATA_TIMEOUT, ref agentUUID))
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.AGENT_NOT_FOUND));
                        }
                        string region =
                            wasInput(wasKeyValueGet(wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.REGION)),
                                message));
                        Simulator simulator =
                            Client.Network.Simulators.FirstOrDefault(
                                o =>
                                    o.Name.Equals(
                                        string.IsNullOrEmpty(region) ? Client.Network.CurrentSim.Name : region,
                                        StringComparison.InvariantCultureIgnoreCase));
                        if (simulator == null)
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.REGION_NOT_FOUND));
                        }
                        string type =
                            wasInput(wasKeyValueGet(wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.TYPE)),
                                message));
                        switch (
                            wasGetEnumValueFromDescription<Entity>(
                                wasInput(
                                    wasKeyValueGet(wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.ENTITY)),
                                        message)).ToLowerInvariant()))
                        {
                            case Entity.PARCEL:
                                Vector3 position;
                                HashSet<Parcel> parcels = new HashSet<Parcel>();
                                switch (Vector3.TryParse(
                                    wasInput(
                                        wasKeyValueGet(wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.POSITION)),
                                            message)),
                                    out position))
                                {
                                    case false:
                                        // Get all sim parcels
                                        ManualResetEvent SimParcelsDownloadedEvent = new ManualResetEvent(false);
                                        EventHandler<SimParcelsDownloadedEventArgs> SimParcelsDownloadedEventHandler =
                                            (sender, args) => SimParcelsDownloadedEvent.Set();
                                        lock (ClientInstanceParcelsLock)
                                        {
                                            Client.Parcels.SimParcelsDownloaded += SimParcelsDownloadedEventHandler;
                                            Client.Parcels.RequestAllSimParcels(simulator);
                                            if (simulator.IsParcelMapFull())
                                            {
                                                SimParcelsDownloadedEvent.Set();
                                            }
                                            if (
                                                !SimParcelsDownloadedEvent.WaitOne(Configuration.SERVICES_TIMEOUT, false))
                                            {
                                                Client.Parcels.SimParcelsDownloaded -= SimParcelsDownloadedEventHandler;
                                                throw new Exception(
                                                    wasGetDescriptionFromEnumValue(ScriptError.TIMEOUT_GETTING_PARCELS));
                                            }
                                            Client.Parcels.SimParcelsDownloaded -= SimParcelsDownloadedEventHandler;
                                        }
                                        simulator.Parcels.ForEach(o => parcels.Add(o));
                                        break;
                                    case true:
                                        Parcel parcel = null;
                                        if (!GetParcelAtPosition(simulator, position, ref parcel))
                                        {
                                            throw new Exception(
                                                wasGetDescriptionFromEnumValue(ScriptError.COULD_NOT_FIND_PARCEL));
                                        }
                                        parcels.Add(parcel);
                                        break;
                                }
                                FieldInfo objectReturnTypeField = typeof (ObjectReturnType).GetFields(
                                    BindingFlags.Public |
                                    BindingFlags.Static)
                                    .AsParallel().FirstOrDefault(
                                        o =>
                                            o.Name.Equals(type
                                                .ToLowerInvariant(),
                                                StringComparison.Ordinal));
                                ObjectReturnType returnType = objectReturnTypeField != null
                                    ? (ObjectReturnType)
                                        objectReturnTypeField
                                            .GetValue(null)
                                    : ObjectReturnType.Other;
                                if (!simulator.IsEstateManager)
                                {
                                    Parallel.ForEach(
                                        parcels.AsParallel().Where(o => !o.OwnerID.Equals(Client.Self.AgentID)), o =>
                                        {
                                            if (!o.IsGroupOwned || !o.GroupID.Equals(groupUUID))
                                            {
                                                throw new Exception(
                                                    wasGetDescriptionFromEnumValue(
                                                        ScriptError.NO_GROUP_POWER_FOR_COMMAND));
                                            }
                                            GroupPowers power = new GroupPowers();
                                            switch (returnType)
                                            {
                                                case ObjectReturnType.Other:
                                                    power = GroupPowers.ReturnNonGroup;
                                                    break;
                                                case ObjectReturnType.Group:
                                                    power = GroupPowers.ReturnGroupSet;
                                                    break;
                                                case ObjectReturnType.Owner:
                                                    power = GroupPowers.ReturnGroupOwned;
                                                    break;
                                            }
                                            if (!HasGroupPowers(Client.Self.AgentID, groupUUID, power,
                                                Configuration.SERVICES_TIMEOUT, Configuration.DATA_TIMEOUT))
                                            {
                                                throw new Exception(
                                                    wasGetDescriptionFromEnumValue(
                                                        ScriptError.NO_GROUP_POWER_FOR_COMMAND));
                                            }
                                        });
                                }
                                Parallel.ForEach(parcels,
                                    o =>
                                        Client.Parcels.ReturnObjects(simulator, o.LocalID,
                                            returnType
                                            , new List<UUID> {agentUUID}));

                                break;
                            case Entity.ESTATE:
                                if (!simulator.IsEstateManager)
                                {
                                    throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.NO_LAND_RIGHTS));
                                }
                                bool allEstates;
                                if (
                                    !bool.TryParse(
                                        wasInput(
                                            wasKeyValueGet(wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.ALL)),
                                                message)),
                                        out allEstates))
                                {
                                    allEstates = false;
                                }
                                FieldInfo estateReturnFlagsField = typeof (EstateTools.EstateReturnFlags).GetFields(
                                    BindingFlags.Public | BindingFlags.Static)
                                    .AsParallel().FirstOrDefault(
                                        o =>
                                            o.Name.Equals(type,
                                                StringComparison.Ordinal));
                                Client.Estate.SimWideReturn(agentUUID, estateReturnFlagsField != null
                                    ? (EstateTools.EstateReturnFlags)
                                        estateReturnFlagsField
                                            .GetValue(null)
                                    : EstateTools.EstateReturnFlags.ReturnScriptedAndOnOthers, allEstates);
                                break;
                            default:
                                throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.UNKNOWN_ENTITY));
                        }
                    };
                    break;
                case ScriptKeys.GETPRIMITIVEOWNERS:
                    execute = () =>
                    {
                        if (!HasCorrodePermission(group, (int) Permissions.PERMISSION_LAND))
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.NO_CORRADE_PERMISSIONS));
                        }
                        Group configuredGroup =
                            Configuration.GROUPS.AsParallel().FirstOrDefault(
                                o => o.Name.Equals(group, StringComparison.Ordinal));
                        UUID groupUUID = UUID.Zero;
                        switch (!configuredGroup.Equals(default(Group)))
                        {
                            case true:
                                groupUUID = configuredGroup.UUID;
                                break;
                            default:
                                if (!GroupNameToUUID(group, Configuration.SERVICES_TIMEOUT, Configuration.DATA_TIMEOUT,
                                    ref groupUUID))
                                {
                                    throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.GROUP_NOT_FOUND));
                                }
                                break;
                        }
                        string region =
                            wasInput(wasKeyValueGet(wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.REGION)),
                                message));
                        Simulator simulator =
                            Client.Network.Simulators.FirstOrDefault(
                                o =>
                                    o.Name.Equals(
                                        string.IsNullOrEmpty(region) ? Client.Network.CurrentSim.Name : region,
                                        StringComparison.InvariantCultureIgnoreCase));
                        if (simulator == null)
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.REGION_NOT_FOUND));
                        }
                        Vector3 position;
                        HashSet<Parcel> parcels = new HashSet<Parcel>();
                        switch (Vector3.TryParse(
                            wasInput(wasKeyValueGet(
                                wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.POSITION)), message)),
                            out position))
                        {
                            case true:
                                Parcel parcel = null;
                                if (!GetParcelAtPosition(simulator, position, ref parcel))
                                {
                                    throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.COULD_NOT_FIND_PARCEL));
                                }
                                parcels.Add(parcel);
                                break;
                            default:
                                // Get all sim parcels
                                ManualResetEvent SimParcelsDownloadedEvent = new ManualResetEvent(false);
                                EventHandler<SimParcelsDownloadedEventArgs> SimParcelsDownloadedEventHandler =
                                    (sender, args) => SimParcelsDownloadedEvent.Set();
                                lock (ClientInstanceParcelsLock)
                                {
                                    Client.Parcels.SimParcelsDownloaded += SimParcelsDownloadedEventHandler;
                                    Client.Parcels.RequestAllSimParcels(simulator);
                                    if (simulator.IsParcelMapFull())
                                    {
                                        SimParcelsDownloadedEvent.Set();
                                    }
                                    if (!SimParcelsDownloadedEvent.WaitOne(Configuration.SERVICES_TIMEOUT, false))
                                    {
                                        Client.Parcels.SimParcelsDownloaded -= SimParcelsDownloadedEventHandler;
                                        throw new Exception(
                                            wasGetDescriptionFromEnumValue(ScriptError.TIMEOUT_GETTING_PARCELS));
                                    }
                                    Client.Parcels.SimParcelsDownloaded -= SimParcelsDownloadedEventHandler;
                                }
                                simulator.Parcels.ForEach(o => parcels.Add(o));
                                break;
                        }
                        Parallel.ForEach(parcels.AsParallel().Where(o => !o.OwnerID.Equals(Client.Self.AgentID)),
                            o =>
                            {
                                if (!o.IsGroupOwned || !o.GroupID.Equals(groupUUID))
                                {
                                    throw new Exception(
                                        wasGetDescriptionFromEnumValue(ScriptError.NO_GROUP_POWER_FOR_COMMAND));
                                }
                                bool permissions = false;
                                Parallel.ForEach(
                                    new HashSet<GroupPowers>
                                    {
                                        GroupPowers.ReturnGroupSet,
                                        GroupPowers.ReturnGroupOwned,
                                        GroupPowers.ReturnNonGroup
                                    }, p =>
                                    {
                                        if (HasGroupPowers(Client.Self.AgentID, groupUUID, p,
                                            Configuration.SERVICES_TIMEOUT, Configuration.DATA_TIMEOUT))
                                        {
                                            permissions = true;
                                        }
                                    });
                                if (!permissions)
                                {
                                    throw new Exception(
                                        wasGetDescriptionFromEnumValue(ScriptError.NO_GROUP_POWER_FOR_COMMAND));
                                }
                            });
                        ManualResetEvent ParcelObjectOwnersReplyEvent = new ManualResetEvent(false);
                        Dictionary<string, int> primitives = new Dictionary<string, int>();
                        EventHandler<ParcelObjectOwnersReplyEventArgs> ParcelObjectOwnersEventHandler =
                            (sender, args) =>
                            {
                                //object LockObject = new object();
                                foreach (ParcelManager.ParcelPrimOwners primowner in args.PrimOwners)
                                {
                                    string owner = string.Empty;
                                    if (
                                        !AgentUUIDToName(primowner.OwnerID, Configuration.SERVICES_TIMEOUT, ref owner))
                                        continue;
                                    if (!primitives.ContainsKey(owner))
                                    {
                                        primitives.Add(owner, primowner.Count);
                                        continue;
                                    }
                                    primitives[owner] += primowner.Count;
                                }
                                ParcelObjectOwnersReplyEvent.Set();
                            };
                        foreach (Parcel parcel in parcels)
                        {
                            lock (ClientInstanceParcelsLock)
                            {
                                Client.Parcels.ParcelObjectOwnersReply += ParcelObjectOwnersEventHandler;
                                Client.Parcels.RequestObjectOwners(simulator, parcel.LocalID);
                                if (!ParcelObjectOwnersReplyEvent.WaitOne(Configuration.SERVICES_TIMEOUT, false))
                                {
                                    Client.Parcels.ParcelObjectOwnersReply -= ParcelObjectOwnersEventHandler;
                                    throw new Exception(
                                        wasGetDescriptionFromEnumValue(ScriptError.TIMEOUT_GETTING_LAND_USERS));
                                }
                                Client.Parcels.ParcelObjectOwnersReply -= ParcelObjectOwnersEventHandler;
                            }
                        }
                        if (primitives.Count.Equals(0))
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.COULD_NOT_GET_LAND_USERS));
                        }
                        List<string> data = new List<string>(primitives.AsParallel().Select(
                            p => wasEnumerableToCSV(new[] {p.Key, p.Value.ToString(CultureInfo.InvariantCulture)})));
                        if (!data.Count.Equals(0))
                        {
                            result.Add(wasGetDescriptionFromEnumValue(ResultKeys.DATA),
                                wasEnumerableToCSV(data));
                        }
                    };
                    break;
                case ScriptKeys.GETGROUPDATA:
                    execute = () =>
                    {
                        if (!HasCorrodePermission(group, (int) Permissions.PERMISSION_GROUP))
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.NO_CORRADE_PERMISSIONS));
                        }
                        Group configuredGroup =
                            Configuration.GROUPS.AsParallel().FirstOrDefault(
                                o => o.Name.Equals(group, StringComparison.Ordinal));
                        UUID groupUUID = UUID.Zero;
                        switch (!configuredGroup.Equals(default(Group)))
                        {
                            case true:
                                groupUUID = configuredGroup.UUID;
                                break;
                            default:
                                if (!GroupNameToUUID(group, Configuration.SERVICES_TIMEOUT, Configuration.DATA_TIMEOUT,
                                    ref groupUUID))
                                {
                                    throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.GROUP_NOT_FOUND));
                                }
                                break;
                        }
                        OpenMetaverse.Group dataGroup = new OpenMetaverse.Group();
                        if (!RequestGroup(groupUUID, Configuration.SERVICES_TIMEOUT, ref dataGroup))
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.GROUP_NOT_FOUND));
                        }
                        List<string> data = new List<string>(GetStructuredData(dataGroup,
                            wasInput(wasKeyValueGet(wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.DATA)),
                                message))));
                        if (!data.Count.Equals(0))
                        {
                            result.Add(wasGetDescriptionFromEnumValue(ResultKeys.DATA),
                                wasEnumerableToCSV(data));
                        }
                    };
                    break;
                case ScriptKeys.GETPRIMITIVEDATA:
                    execute = () =>
                    {
                        if (
                            !HasCorrodePermission(group,
                                (int) Permissions.PERMISSION_INTERACT))
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.NO_CORRADE_PERMISSIONS));
                        }
                        float range;
                        if (
                            !float.TryParse(
                                wasInput(wasKeyValueGet(
                                    wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.RANGE)), message)),
                                out range))
                        {
                            range = Configuration.RANGE;
                        }
                        Primitive primitive = null;
                        if (
                            !FindPrimitive(
                                StringOrUUID(wasInput(wasKeyValueGet(
                                    wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.ITEM)), message))),
                                range,
                                ref primitive, Configuration.SERVICES_TIMEOUT, Configuration.DATA_TIMEOUT))
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.PRIMITIVE_NOT_FOUND));
                        }
                        List<string> data = new List<string>(GetStructuredData(primitive,
                            wasInput(wasKeyValueGet(wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.DATA)),
                                message))));
                        if (!data.Count.Equals(0))
                        {
                            result.Add(wasGetDescriptionFromEnumValue(ResultKeys.DATA),
                                wasEnumerableToCSV(data));
                        }
                    };
                    break;
                case ScriptKeys.GETPARCELDATA:
                    execute = () =>
                    {
                        if (!HasCorrodePermission(group, (int) Permissions.PERMISSION_LAND))
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.NO_CORRADE_PERMISSIONS));
                        }
                        Vector3 position;
                        if (
                            !Vector3.TryParse(
                                wasInput(
                                    wasKeyValueGet(wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.POSITION)),
                                        message)),
                                out position))
                        {
                            position = Client.Self.SimPosition;
                        }
                        string region =
                            wasInput(wasKeyValueGet(wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.REGION)),
                                message));
                        Simulator simulator =
                            Client.Network.Simulators.FirstOrDefault(
                                o =>
                                    o.Name.Equals(
                                        string.IsNullOrEmpty(region) ? Client.Network.CurrentSim.Name : region,
                                        StringComparison.InvariantCultureIgnoreCase));
                        if (simulator == null)
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.REGION_NOT_FOUND));
                        }
                        Parcel parcel = null;
                        if (!GetParcelAtPosition(simulator, position, ref parcel))
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.COULD_NOT_FIND_PARCEL));
                        }
                        List<string> data = new List<string>(GetStructuredData(parcel,
                            wasInput(wasKeyValueGet(wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.DATA)),
                                message))));
                        if (!data.Count.Equals(0))
                        {
                            result.Add(wasGetDescriptionFromEnumValue(ResultKeys.DATA),
                                wasEnumerableToCSV(data));
                        }
                    };
                    break;
                case ScriptKeys.SETPARCELDATA:
                    execute = () =>
                    {
                        if (!HasCorrodePermission(group, (int) Permissions.PERMISSION_LAND))
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.NO_CORRADE_PERMISSIONS));
                        }
                        Group configuredGroup =
                            Configuration.GROUPS.AsParallel().FirstOrDefault(
                                o => o.Name.Equals(group, StringComparison.Ordinal));
                        UUID groupUUID = UUID.Zero;
                        switch (!configuredGroup.Equals(default(Group)))
                        {
                            case true:
                                groupUUID = configuredGroup.UUID;
                                break;
                            default:
                                if (!GroupNameToUUID(group, Configuration.SERVICES_TIMEOUT, Configuration.DATA_TIMEOUT,
                                    ref groupUUID))
                                {
                                    throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.GROUP_NOT_FOUND));
                                }
                                break;
                        }
                        Vector3 position;
                        if (
                            !Vector3.TryParse(
                                wasInput(
                                    wasKeyValueGet(wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.POSITION)),
                                        message)),
                                out position))
                        {
                            position = Client.Self.SimPosition;
                        }
                        string region =
                            wasInput(wasKeyValueGet(wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.REGION)),
                                message));
                        Simulator simulator =
                            Client.Network.Simulators.FirstOrDefault(
                                o =>
                                    o.Name.Equals(
                                        string.IsNullOrEmpty(region) ? Client.Network.CurrentSim.Name : region,
                                        StringComparison.InvariantCultureIgnoreCase));
                        if (simulator == null)
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.REGION_NOT_FOUND));
                        }
                        Parcel parcel = null;
                        if (!GetParcelAtPosition(simulator, position, ref parcel))
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.COULD_NOT_FIND_PARCEL));
                        }
                        if (!parcel.OwnerID.Equals(Client.Self.AgentID))
                        {
                            if (!parcel.IsGroupOwned && !parcel.GroupID.Equals(groupUUID))
                            {
                                throw new Exception(
                                    wasGetDescriptionFromEnumValue(ScriptError.NO_GROUP_POWER_FOR_COMMAND));
                            }
                        }
                        wasCSVToStructure(
                            wasInput(wasKeyValueGet(wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.DATA)),
                                message)), ref parcel);
                        parcel.Update(Client, simulator, true);
                    };
                    break;
                case ScriptKeys.GETREGIONPARCELSBOUNDINGBOX:
                    execute = () =>
                    {
                        if (!HasCorrodePermission(group, (int) Permissions.PERMISSION_LAND))
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.NO_CORRADE_PERMISSIONS));
                        }
                        string region =
                            wasInput(wasKeyValueGet(wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.REGION)),
                                message));
                        Simulator simulator =
                            Client.Network.Simulators.FirstOrDefault(
                                o =>
                                    o.Name.Equals(
                                        string.IsNullOrEmpty(region) ? Client.Network.CurrentSim.Name : region,
                                        StringComparison.InvariantCultureIgnoreCase));
                        if (simulator == null)
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.REGION_NOT_FOUND));
                        }
                        // Get all sim parcels
                        ManualResetEvent SimParcelsDownloadedEvent = new ManualResetEvent(false);
                        EventHandler<SimParcelsDownloadedEventArgs> SimParcelsDownloadedEventHandler =
                            (sender, args) => SimParcelsDownloadedEvent.Set();
                        lock (ClientInstanceParcelsLock)
                        {
                            Client.Parcels.SimParcelsDownloaded += SimParcelsDownloadedEventHandler;
                            Client.Parcels.RequestAllSimParcels(simulator);
                            if (simulator.IsParcelMapFull())
                            {
                                SimParcelsDownloadedEvent.Set();
                            }
                            if (!SimParcelsDownloadedEvent.WaitOne(Configuration.SERVICES_TIMEOUT, false))
                            {
                                Client.Parcels.SimParcelsDownloaded -= SimParcelsDownloadedEventHandler;
                                throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.TIMEOUT_GETTING_PARCELS));
                            }
                            Client.Parcels.SimParcelsDownloaded -= SimParcelsDownloadedEventHandler;
                        }
                        List<Vector3> csv = new List<Vector3>();
                        simulator.Parcels.ForEach(o => csv.AddRange(new[] {o.AABBMin, o.AABBMax}));
                        if (!csv.Count.Equals(0))
                        {
                            result.Add(wasGetDescriptionFromEnumValue(ResultKeys.DATA),
                                wasEnumerableToCSV(csv.Select(o => o.ToString())));
                        }
                    };
                    break;
                case ScriptKeys.DOWNLOAD:
                    execute = () =>
                    {
                        if (!HasCorrodePermission(group, (int) Permissions.PERMISSION_INTERACT))
                        {
                            throw new Exception(
                                wasGetDescriptionFromEnumValue(ScriptError.NO_CORRADE_PERMISSIONS));
                        }
                        string item =
                            wasInput(wasKeyValueGet(wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.ITEM)),
                                message));
                        if (string.IsNullOrEmpty(item))
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.NO_ITEM_SPECIFIED));
                        }
                        UUID itemUUID;
                        InventoryItem inventoryItem = null;
                        if (!UUID.TryParse(item, out itemUUID))
                        {
                            InventoryBase inventoryBase =
                                FindInventory<InventoryBase>(Client.Inventory.Store.RootNode, item
                                    ).FirstOrDefault();
                            if (inventoryBase == null)
                            {
                                throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.INVENTORY_ITEM_NOT_FOUND));
                            }
                            inventoryItem = inventoryBase as InventoryItem;
                            if (inventoryItem == null)
                            {
                                throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.INVENTORY_ITEM_NOT_FOUND));
                            }
                            itemUUID = inventoryItem.AssetUUID;
                        }
                        byte[] assetData = null;
                        switch (!Client.Assets.Cache.HasAsset(itemUUID))
                        {
                            case true:
                                FieldInfo assetTypeInfo = typeof (AssetType).GetFields(BindingFlags.Public |
                                                                                       BindingFlags.Static)
                                    .AsParallel().FirstOrDefault(
                                        o =>
                                            o.Name.Equals(
                                                wasInput(
                                                    wasKeyValueGet(
                                                        wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.TYPE)),
                                                        message)),
                                                StringComparison.Ordinal));
                                if (assetTypeInfo == null)
                                {
                                    throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.UNKNOWN_ASSET_TYPE));
                                }
                                AssetType assetType = (AssetType) assetTypeInfo.GetValue(null);
                                ManualResetEvent RequestAssetEvent = new ManualResetEvent(false);
                                bool succeeded = false;
                                switch (assetType)
                                {
                                    case AssetType.Mesh:
                                        Client.Assets.RequestMesh(itemUUID, delegate(bool completed, AssetMesh asset)
                                        {
                                            if (!asset.AssetID.Equals(itemUUID)) return;
                                            succeeded = completed;
                                            if (succeeded)
                                            {
                                                assetData = asset.MeshData.AsBinary();
                                            }
                                            RequestAssetEvent.Set();
                                        });
                                        if (!RequestAssetEvent.WaitOne(Configuration.SERVICES_TIMEOUT, false))
                                        {
                                            throw new Exception(
                                                wasGetDescriptionFromEnumValue(ScriptError.TIMEOUT_TRANSFERRING_ASSET));
                                        }
                                        break;
                                    // All of these can only be fetched if they exist locally.
                                    case AssetType.LSLText:
                                    case AssetType.Notecard:
                                        if (!HasCorrodePermission(group, (int) Permissions.PERMISSION_INVENTORY))
                                        {
                                            throw new Exception(
                                                wasGetDescriptionFromEnumValue(ScriptError.NO_CORRADE_PERMISSIONS));
                                        }
                                        var transferID = UUID.Random();
                                        Client.Assets.RequestInventoryAsset(inventoryItem, true, transferID,
                                            delegate(AssetDownload transfer, Asset asset)
                                            {
                                                succeeded = transfer.Success;
                                                if (transfer.Success)
                                                {
                                                    assetData = asset.AssetData;
                                                }
                                                RequestAssetEvent.Set();
                                            });
                                        if (!RequestAssetEvent.WaitOne(Configuration.SERVICES_TIMEOUT, false))
                                        {
                                            throw new Exception(
                                                wasGetDescriptionFromEnumValue(ScriptError.TIMEOUT_TRANSFERRING_ASSET));
                                        }
                                        break;
                                    // All images go through RequestImage and can be fetched directly from the asset server.
                                    case AssetType.Texture:
                                        Client.Assets.RequestImage(itemUUID, ImageType.Normal,
                                            delegate(TextureRequestState state, AssetTexture asset)
                                            {
                                                if (!asset.AssetID.Equals(itemUUID)) return;
                                                if (!state.Equals(TextureRequestState.Finished)) return;
                                                assetData = asset.AssetData;
                                                succeeded = true;
                                                RequestAssetEvent.Set();
                                            });
                                        if (!RequestAssetEvent.WaitOne(Configuration.SERVICES_TIMEOUT, false))
                                        {
                                            throw new Exception(
                                                wasGetDescriptionFromEnumValue(ScriptError.TIMEOUT_TRANSFERRING_ASSET));
                                        }
                                        // *FIXME: I'm just slamming a PNG for now.
                                        // Convert to desired format if specified.
                                        string format =
                                            wasInput(wasKeyValueGet(
                                                wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.FORMAT)),
                                                message));
                                        if (!string.IsNullOrEmpty(format))
                                        {
                                            using (var bmp = J2kImage.FromBytes(assetData).As<SKBitmap>())
                                            {
                                                if (bmp == null)
                                                {
                                                    throw new Exception(
                                                        wasGetDescriptionFromEnumValue(
                                                            ScriptError.UNABLE_TO_DECODE_ASSET_DATA));
                                                }
                                                assetData = bmp.Encode(SKEncodedImageFormat.Png, 100).ToArray();
                                            }
                                        }
                                        break;
                                    // All of these can be fetched directly from the asset server.
                                    case AssetType.Landmark:
                                    case AssetType.Gesture:
                                    case AssetType.Animation: // Animatn
                                    case AssetType.Sound: // Ogg Vorbis
                                    case AssetType.Clothing:
                                    case AssetType.Bodypart:
                                        Client.Assets.RequestAsset(itemUUID, assetType, true,
                                            delegate(AssetDownload transfer, Asset asset)
                                            {
                                                if (!transfer.AssetID.Equals(itemUUID)) return;
                                                succeeded = transfer.Success;
                                                if (transfer.Success)
                                                {
                                                    assetData = asset.AssetData;
                                                }
                                                RequestAssetEvent.Set();
                                            });
                                        if (!RequestAssetEvent.WaitOne(Configuration.SERVICES_TIMEOUT, false))
                                        {
                                            throw new Exception(
                                                wasGetDescriptionFromEnumValue(ScriptError.TIMEOUT_TRANSFERRING_ASSET));
                                        }
                                        break;
                                    default:
                                        throw new Exception(
                                            wasGetDescriptionFromEnumValue(ScriptError.UNKNOWN_ASSET_TYPE));
                                }
                                if (!succeeded)
                                {
                                    throw new Exception(
                                        wasGetDescriptionFromEnumValue(ScriptError.FAILED_TO_DOWNLOAD_ASSET));
                                }
                                Client.Assets.Cache.SaveAssetToCache(itemUUID, assetData);
                                break;
                            default:
                                assetData = Client.Assets.Cache.GetCachedAssetBytes(itemUUID);
                                break;
                        }
                        // If no path was specificed, then send the data.
                        string path =
                            wasInput(wasKeyValueGet(
                                wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.PATH)),
                                message));
                        if (string.IsNullOrEmpty(path))
                        {
                            result.Add(wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.DATA)),
                                Convert.ToBase64String(assetData));
                            return;
                        }
                        if (
                            !HasCorrodePermission(group, (int) Permissions.PERMISSION_SYSTEM))
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.NO_CORRADE_PERMISSIONS));
                        }
                        // Otherwise, save it to the specified file.
                        using (FileStream fs = File.Open(path, FileMode.Create))
                        {
                            using (BinaryWriter bw = new BinaryWriter(fs))
                            {
                                bw.Write(assetData);
                                bw.Flush();
                            }
                            fs.Flush();
                        }
                    };
                    break;
                case ScriptKeys.UPLOAD:
                    execute = () =>
                    {
                        if (!HasCorrodePermission(group, (int) Permissions.PERMISSION_INVENTORY))
                        {
                            throw new Exception(
                                wasGetDescriptionFromEnumValue(ScriptError.NO_CORRADE_PERMISSIONS));
                        }
                        string name =
                            wasInput(wasKeyValueGet(wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.NAME)),
                                message));
                        if (string.IsNullOrEmpty(name))
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.NO_NAME_PROVIDED));
                        }
                        uint permissions = 0;
                        Parallel.ForEach(wasCSVToEnumerable(
                            wasInput(
                                wasKeyValueGet(wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.PERMISSIONS)),
                                    message))),
                            o =>
                                Parallel.ForEach(
                                    typeof (PermissionMask).GetFields(BindingFlags.Public | BindingFlags.Static)
                                        .AsParallel().Where(p => p.Name.Equals(o, StringComparison.Ordinal)),
                                    q => { permissions |= ((uint) q.GetValue(null)); }));
                        FieldInfo assetTypeInfo = typeof (AssetType).GetFields(BindingFlags.Public |
                                                                               BindingFlags.Static)
                            .AsParallel().FirstOrDefault(o =>
                                o.Name.Equals(
                                    wasInput(
                                        wasKeyValueGet(
                                            wasGetDescriptionFromEnumValue(
                                                ScriptKeys.TYPE),
                                            message)),
                                    StringComparison.Ordinal));
                        if (assetTypeInfo == null)
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.UNKNOWN_ASSET_TYPE));
                        }
                        AssetType assetType = (AssetType) assetTypeInfo.GetValue(null);
                        byte[] data;
                        try
                        {
                            data = Convert.FromBase64String(
                                wasInput(wasKeyValueGet(wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.DATA)),
                                    message)));
                        }
                        catch (Exception)
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.INVALID_ASSET_DATA));
                        }
                        bool succeeded = false;
                        switch (assetType)
                        {
                            case AssetType.Texture:
                            case AssetType.Sound:
                            case AssetType.Animation:
                                // the holy asset trinity is charged money
                                if (!HasCorrodePermission(group, (int) Permissions.PERMISSION_ECONOMY))
                                {
                                    throw new Exception(
                                        wasGetDescriptionFromEnumValue(ScriptError.NO_CORRADE_PERMISSIONS));
                                }
                                if (!UpdateBalance(Configuration.SERVICES_TIMEOUT))
                                {
                                    throw new Exception(
                                        wasGetDescriptionFromEnumValue(ScriptError.UNABLE_TO_OBTAIN_MONEY_BALANCE));
                                }
                                if (Client.Self.Balance < Client.Settings.UPLOAD_COST)
                                {
                                    throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.INSUFFICIENT_FUNDS));
                                }
                                switch (assetType)
                                {
                                    case AssetType.Texture:
                                        // *FIXME:
                                        // If the user did not send a JPEG-2000 Codestream, attempt to convert the data
                                        // and then encode to JPEG-2000 Codestream since that is what Second Life expects.
                                        if (J2kImage.FromBytes(data) == null)
                                        {
                                            using (var bmp = SKBitmap.Decode(data))
                                            {
                                                var p = J2kImage.GetDefaultDecoderParameterList();
                                                p["file_format"] = "off";
                                                data = J2kImage.ToBytes(bmp, p);
                                            }
                                        }
                                        break;
                                }
                                // now create and upload the asset
                                ManualResetEvent CreateItemFromAssetEvent = new ManualResetEvent(false);
                                Client.Inventory.RequestCreateItemFromAsset(data, name,
                                    wasInput(
                                        wasKeyValueGet(
                                            wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.DESCRIPTION)),
                                            message)),
                                    assetType,
                                    (InventoryType)
                                        (typeof (InventoryType).GetFields(BindingFlags.Public | BindingFlags.Static)
                                            .AsParallel().FirstOrDefault(
                                                o => o.Name.Equals(Enum.GetName(typeof (AssetType), assetType),
                                                    StringComparison.Ordinal))).GetValue(null),
                                    Client.Inventory.FindFolderForType(assetType),
                                    delegate(bool completed, string status, UUID itemID, UUID assetID)
                                    {
                                        succeeded = completed;
                                        CreateItemFromAssetEvent.Set();
                                    });
                                if (!CreateItemFromAssetEvent.WaitOne(Configuration.SERVICES_TIMEOUT, false))
                                {
                                    throw new Exception(
                                        wasGetDescriptionFromEnumValue(ScriptError.TIMEOUT_UPLOADING_ASSET));
                                }
                                break;
                            case AssetType.Bodypart:
                            case AssetType.Clothing:
                                FieldInfo wearTypeInfo = typeof (MuteType).GetFields(BindingFlags.Public |
                                                                                     BindingFlags.Static)
                                    .AsParallel().FirstOrDefault(
                                        o =>
                                            o.Name.Equals(
                                                wasInput(
                                                    wasKeyValueGet(
                                                        wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.WEAR)),
                                                        message)),
                                                StringComparison.Ordinal));
                                if (wearTypeInfo == null)
                                {
                                    throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.UNKNOWN_WEARABLE_TYPE));
                                }
                                UUID wearableUUID = Client.Assets.RequestUpload(assetType, data, false);
                                if (wearableUUID.Equals(UUID.Zero))
                                {
                                    throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.ASSET_UPLOAD_FAILED));
                                }
                                ManualResetEvent CreateWearableEvent = new ManualResetEvent(false);
                                Client.Inventory.RequestCreateItem(Client.Inventory.FindFolderForType(assetType),
                                    name,
                                    wasInput(
                                        wasKeyValueGet(
                                            wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.DESCRIPTION)),
                                            message)),
                                    assetType,
                                    wearableUUID, InventoryType.Wearable, (WearableType) wearTypeInfo.GetValue(null),
                                    permissions == 0 ? PermissionMask.Transfer : (PermissionMask) permissions,
                                    delegate(bool completed, InventoryItem createdItem)
                                    {
                                        succeeded = completed;
                                        CreateWearableEvent.Set();
                                    });
                                if (!CreateWearableEvent.WaitOne(Configuration.SERVICES_TIMEOUT, false))
                                {
                                    throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.TIMEOUT_CREATING_ITEM));
                                }
                                break;
                            case AssetType.Landmark:
                                UUID landmarkUUID = Client.Assets.RequestUpload(assetType, data, false);
                                if (landmarkUUID.Equals(UUID.Zero))
                                {
                                    throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.ASSET_UPLOAD_FAILED));
                                }
                                ManualResetEvent CreateLandmarkEvent = new ManualResetEvent(false);
                                Client.Inventory.RequestCreateItem(Client.Inventory.FindFolderForType(assetType),
                                    name,
                                    wasInput(
                                        wasKeyValueGet(
                                            wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.DESCRIPTION)),
                                            message)),
                                    assetType,
                                    landmarkUUID, InventoryType.Landmark, PermissionMask.All,
                                    delegate(bool completed, InventoryItem createdItem)
                                    {
                                        succeeded = completed;
                                        CreateLandmarkEvent.Set();
                                    });
                                if (!CreateLandmarkEvent.WaitOne(Configuration.SERVICES_TIMEOUT, false))
                                {
                                    throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.TIMEOUT_CREATING_ITEM));
                                }
                                break;
                            case AssetType.Gesture:
                                ManualResetEvent CreateGestureEvent = new ManualResetEvent(false);
                                InventoryItem newGesture = null;
                                Client.Inventory.RequestCreateItem(Client.Inventory.FindFolderForType(assetType),
                                    name,
                                    wasInput(
                                        wasKeyValueGet(
                                            wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.DESCRIPTION)),
                                            message)),
                                    assetType,
                                    UUID.Random(), InventoryType.Gesture,
                                    permissions == 0 ? PermissionMask.Transfer : (PermissionMask) permissions,
                                    delegate(bool completed, InventoryItem createdItem)
                                    {
                                        succeeded = completed;
                                        newGesture = createdItem;
                                        CreateGestureEvent.Set();
                                    });
                                if (!CreateGestureEvent.WaitOne(Configuration.SERVICES_TIMEOUT, false))
                                {
                                    throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.TIMEOUT_CREATING_ITEM));
                                }
                                if (!succeeded)
                                {
                                    throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.UNABLE_TO_CREATE_ITEM));
                                }
                                ManualResetEvent UploadGestureAssetEvent = new ManualResetEvent(false);
                                Client.Inventory.RequestUploadGestureAsset(data, newGesture.UUID,
                                    delegate(bool completed, string status, UUID itemUUID, UUID assetUUID)
                                    {
                                        succeeded = completed;
                                        UploadGestureAssetEvent.Set();
                                    });
                                if (!UploadGestureAssetEvent.WaitOne(Configuration.SERVICES_TIMEOUT, false))
                                {
                                    throw new Exception(
                                        wasGetDescriptionFromEnumValue(ScriptError.TIMEOUT_UPLOADING_ASSET));
                                }
                                break;
                            case AssetType.Notecard:
                                ManualResetEvent CreateNotecardEvent = new ManualResetEvent(false);
                                InventoryItem newNotecard = null;
                                Client.Inventory.RequestCreateItem(Client.Inventory.FindFolderForType(assetType),
                                    name,
                                    wasInput(
                                        wasKeyValueGet(
                                            wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.DESCRIPTION)),
                                            message)),
                                    assetType,
                                    UUID.Random(), InventoryType.Notecard,
                                    permissions == 0 ? PermissionMask.Transfer : (PermissionMask) permissions,
                                    delegate(bool completed, InventoryItem createdItem)
                                    {
                                        succeeded = completed;
                                        newNotecard = createdItem;
                                        CreateNotecardEvent.Set();
                                    });
                                if (!CreateNotecardEvent.WaitOne(Configuration.SERVICES_TIMEOUT, false))
                                {
                                    throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.TIMEOUT_CREATING_ITEM));
                                }
                                if (!succeeded)
                                {
                                    throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.UNABLE_TO_CREATE_ITEM));
                                }
                                ManualResetEvent UploadNotecardAssetEvent = new ManualResetEvent(false);
                                Client.Inventory.RequestUploadNotecardAsset(data, newNotecard.UUID,
                                    delegate(bool completed, string status, UUID itemUUID, UUID assetUUID)
                                    {
                                        succeeded = completed;
                                        UploadNotecardAssetEvent.Set();
                                    });
                                if (!UploadNotecardAssetEvent.WaitOne(Configuration.SERVICES_TIMEOUT, false))
                                {
                                    throw new Exception(
                                        wasGetDescriptionFromEnumValue(ScriptError.TIMEOUT_UPLOADING_ASSET));
                                }
                                break;
                            case AssetType.LSLText:
                                ManualResetEvent CreateScriptEvent = new ManualResetEvent(false);
                                InventoryItem newScript = null;
                                Client.Inventory.RequestCreateItem(Client.Inventory.FindFolderForType(assetType),
                                    name,
                                    wasInput(
                                        wasKeyValueGet(
                                            wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.DESCRIPTION)),
                                            message)),
                                    assetType,
                                    UUID.Random(), InventoryType.LSL,
                                    permissions == 0 ? PermissionMask.Transfer : (PermissionMask) permissions,
                                    delegate(bool completed, InventoryItem createdItem)
                                    {
                                        succeeded = completed;
                                        newScript = createdItem;
                                        CreateScriptEvent.Set();
                                    });
                                if (!CreateScriptEvent.WaitOne(Configuration.SERVICES_TIMEOUT, false))
                                {
                                    throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.TIMEOUT_CREATING_ITEM));
                                }
                                ManualResetEvent UpdateScriptEvent = new ManualResetEvent(false);
                                Client.Inventory.RequestUpdateScriptAgentInventory(data, newScript.UUID, true,
                                    delegate(bool completed, string status, bool compiled, List<string> messages,
                                        UUID itemID, UUID assetID)
                                    {
                                        succeeded = completed;
                                        UpdateScriptEvent.Set();
                                    });
                                if (!UpdateScriptEvent.WaitOne(Configuration.SERVICES_TIMEOUT, false))
                                {
                                    throw new Exception(
                                        wasGetDescriptionFromEnumValue(ScriptError.TIMEOUT_UPLOADING_ASSET));
                                }
                                break;
                            default:
                                throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.UNKNOWN_INVENTORY_TYPE));
                        }
                        if (!succeeded)
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.ASSET_UPLOAD_FAILED));
                        }
                    };
                    break;
                case ScriptKeys.REZ:
                    execute = () =>
                    {
                        if (!HasCorrodePermission(group, (int) Permissions.PERMISSION_INVENTORY))
                        {
                            throw new Exception(
                                wasGetDescriptionFromEnumValue(ScriptError.NO_CORRADE_PERMISSIONS));
                        }
                        Group configuredGroup =
                            Configuration.GROUPS.AsParallel().FirstOrDefault(
                                o => o.Name.Equals(group, StringComparison.Ordinal));
                        UUID groupUUID = UUID.Zero;
                        switch (!configuredGroup.Equals(default(Group)))
                        {
                            case true:
                                groupUUID = configuredGroup.UUID;
                                break;
                            default:
                                if (!GroupNameToUUID(group, Configuration.SERVICES_TIMEOUT, Configuration.DATA_TIMEOUT,
                                    ref groupUUID))
                                {
                                    throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.GROUP_NOT_FOUND));
                                }
                                break;
                        }
                        InventoryBase inventoryBaseItem =
                            FindInventory<InventoryBase>(Client.Inventory.Store.RootNode,
                                wasInput(wasKeyValueGet(
                                    wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.ITEM)), message))
                                ).FirstOrDefault();
                        if (inventoryBaseItem == null)
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.INVENTORY_ITEM_NOT_FOUND));
                        }
                        Vector3 position;
                        if (
                            !Vector3.TryParse(
                                wasInput(
                                    wasKeyValueGet(wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.POSITION)),
                                        message)),
                                out position))
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.INVALID_POSITION));
                        }
                        Quaternion rotation;
                        if (
                            !Quaternion.TryParse(
                                wasInput(
                                    wasKeyValueGet(wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.ROTATION)),
                                        message)),
                                out rotation))
                        {
                            rotation = Quaternion.CreateFromEulers(0, 0, 0);
                        }
                        string region =
                            wasInput(wasKeyValueGet(wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.REGION)),
                                message));
                        Simulator simulator =
                            Client.Network.Simulators.FirstOrDefault(
                                o =>
                                    o.Name.Equals(
                                        string.IsNullOrEmpty(region) ? Client.Network.CurrentSim.Name : region,
                                        StringComparison.InvariantCultureIgnoreCase));
                        if (simulator == null)
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.REGION_NOT_FOUND));
                        }
                        Parcel parcel = null;
                        if (!GetParcelAtPosition(simulator, position, ref parcel))
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.COULD_NOT_FIND_PARCEL));
                        }
                        if (((uint) parcel.Flags & (uint) ParcelFlags.CreateObjects).Equals(0))
                        {
                            if (!simulator.IsEstateManager)
                            {
                                if (!parcel.OwnerID.Equals(Client.Self.AgentID))
                                {
                                    if (!parcel.IsGroupOwned && !parcel.GroupID.Equals(groupUUID))
                                    {
                                        throw new Exception(
                                            wasGetDescriptionFromEnumValue(ScriptError.NO_GROUP_POWER_FOR_COMMAND));
                                    }
                                    if (!HasGroupPowers(Client.Self.AgentID, groupUUID, GroupPowers.AllowRez,
                                        Configuration.SERVICES_TIMEOUT, Configuration.DATA_TIMEOUT))
                                    {
                                        throw new Exception(
                                            wasGetDescriptionFromEnumValue(ScriptError.NO_GROUP_POWER_FOR_COMMAND));
                                    }
                                }
                            }
                        }
                        Client.Inventory.RequestRezFromInventory(simulator, rotation, position,
                            inventoryBaseItem as InventoryItem,
                            groupUUID);
                    };
                    break;
                case ScriptKeys.DEREZ:
                    execute = () =>
                    {
                        if (!HasCorrodePermission(group, (int) Permissions.PERMISSION_INVENTORY))
                        {
                            throw new Exception(
                                wasGetDescriptionFromEnumValue(ScriptError.NO_CORRADE_PERMISSIONS));
                        }
                        float range;
                        if (
                            !float.TryParse(
                                wasInput(wasKeyValueGet(
                                    wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.RANGE)), message)),
                                out range))
                        {
                            range = Configuration.RANGE;
                        }
                        UUID folderUUID;
                        string folder =
                            wasInput(wasKeyValueGet(wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.FOLDER)),
                                message));
                        if (string.IsNullOrEmpty(folder) || !UUID.TryParse(folder, out folderUUID))
                        {
                            folderUUID =
                                Client.Inventory.Store.Items[Client.Inventory.FindFolderForType(AssetType.Object)].Data
                                    .UUID;
                        }
                        if (folderUUID.Equals(UUID.Zero))
                        {
                            InventoryBase inventoryBaseItem =
                                FindInventory<InventoryBase>(Client.Inventory.Store.RootNode, folder
                                    ).FirstOrDefault();
                            if (inventoryBaseItem != null)
                            {
                                InventoryItem item = inventoryBaseItem as InventoryItem;
                                if (item == null || !item.AssetType.Equals(AssetType.Folder))
                                {
                                    throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.FOLDER_NOT_FOUND));
                                }
                                folderUUID = inventoryBaseItem.UUID;
                            }
                        }
                        FieldInfo deRezDestionationTypeInfo = typeof (DeRezDestination).GetFields(BindingFlags.Public |
                                                                                                  BindingFlags.Static)
                            .AsParallel().FirstOrDefault(
                                o =>
                                    o.Name.Equals(
                                        wasInput(
                                            wasKeyValueGet(wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.TYPE)),
                                                message)),
                                        StringComparison.Ordinal));
                        Primitive primitive = null;
                        if (
                            !FindPrimitive(
                                StringOrUUID(wasInput(wasKeyValueGet(
                                    wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.ITEM)), message))),
                                range,
                                ref primitive, Configuration.SERVICES_TIMEOUT, Configuration.DATA_TIMEOUT))
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.PRIMITIVE_NOT_FOUND));
                        }
                        Client.Inventory.RequestDeRezToInventory(primitive.LocalID, deRezDestionationTypeInfo != null
                            ? (DeRezDestination)
                                deRezDestionationTypeInfo
                                    .GetValue(null)
                            : DeRezDestination.AgentInventoryTake, folderUUID, UUID.Random());
                    };
                    break;
                case ScriptKeys.SETSCRIPTRUNNING:
                    execute = () =>
                    {
                        if (!HasCorrodePermission(group, (int) Permissions.PERMISSION_INTERACT))
                        {
                            throw new Exception(
                                wasGetDescriptionFromEnumValue(ScriptError.NO_CORRADE_PERMISSIONS));
                        }
                        float range;
                        if (
                            !float.TryParse(
                                wasInput(wasKeyValueGet(
                                    wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.RANGE)), message)),
                                out range))
                        {
                            range = Configuration.RANGE;
                        }
                        string entity =
                            wasInput(wasKeyValueGet(wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.ENTITY)),
                                message));
                        UUID entityUUID;
                        if (!UUID.TryParse(entity, out entityUUID))
                        {
                            if (string.IsNullOrEmpty(entity))
                            {
                                throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.UNKNOWN_ENTITY));
                            }
                            entityUUID = UUID.Zero;
                        }
                        Primitive primitive = null;
                        if (
                            !FindPrimitive(
                                StringOrUUID(wasInput(wasKeyValueGet(
                                    wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.ITEM)), message))),
                                range,
                                ref primitive, Configuration.SERVICES_TIMEOUT, Configuration.DATA_TIMEOUT))
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.PRIMITIVE_NOT_FOUND));
                        }
                        List<InventoryBase> inventory =
                            Client.Inventory.GetTaskInventory(primitive.ID, primitive.LocalID,
                                Configuration.SERVICES_TIMEOUT).ToList();
                        InventoryItem item = !entityUUID.Equals(UUID.Zero)
                            ? inventory.AsParallel().FirstOrDefault(o => o.UUID.Equals(entityUUID)) as InventoryItem
                            : inventory.AsParallel().FirstOrDefault(o => o.Name.Equals(entity)) as InventoryItem;
                        if (item == null)
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.INVENTORY_ITEM_NOT_FOUND));
                        }
                        switch (item.AssetType)
                        {
                            case AssetType.LSLBytecode:
                            case AssetType.LSLText:
                                break;
                            default:
                                throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.ITEM_IS_NOT_A_SCRIPT));
                        }
                        Action action =
                            wasGetEnumValueFromDescription<Action>(
                                wasInput(
                                    wasKeyValueGet(wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.ACTION)), message))
                                    .ToLowerInvariant());
                        switch (action)
                        {
                            case Action.START:
                            case Action.STOP:
                                Client.Inventory.RequestSetScriptRunning(primitive.ID, item.UUID,
                                    action.Equals(Action.START));
                                break;
                            default:
                                throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.UNKNOWN_ACTION));
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
                        lock (ClientInstanceInventoryLock)
                        {
                            Client.Inventory.ScriptRunningReply += ScriptRunningEventHandler;
                            Client.Inventory.RequestGetScriptRunning(primitive.ID, item.UUID);
                            if (!ScriptRunningReplyEvent.WaitOne(Configuration.SERVICES_TIMEOUT, false))
                            {
                                Client.Inventory.ScriptRunningReply -= ScriptRunningEventHandler;
                                throw new Exception(
                                    wasGetDescriptionFromEnumValue(ScriptError.TIMEOUT_GETTING_SCRIPT_STATE));
                            }
                            Client.Inventory.ScriptRunningReply -= ScriptRunningEventHandler;
                        }
                        if (!succeeded)
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.COULD_NOT_SET_SCRIPT_STATE));
                        }
                    };
                    break;
                case ScriptKeys.GETSCRIPTRUNNING:
                    execute = () =>
                    {
                        if (!HasCorrodePermission(group, (int) Permissions.PERMISSION_INTERACT))
                        {
                            throw new Exception(
                                wasGetDescriptionFromEnumValue(ScriptError.NO_CORRADE_PERMISSIONS));
                        }
                        float range;
                        if (
                            !float.TryParse(
                                wasInput(wasKeyValueGet(
                                    wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.RANGE)), message)),
                                out range))
                        {
                            range = Configuration.RANGE;
                        }
                        string entity =
                            wasInput(wasKeyValueGet(wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.ENTITY)),
                                message));
                        UUID entityUUID;
                        if (!UUID.TryParse(entity, out entityUUID))
                        {
                            if (string.IsNullOrEmpty(entity))
                            {
                                throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.UNKNOWN_ENTITY));
                            }
                            entityUUID = UUID.Zero;
                        }
                        Primitive primitive = null;
                        if (
                            !FindPrimitive(
                                StringOrUUID(wasInput(wasKeyValueGet(
                                    wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.ITEM)), message))),
                                range,
                                ref primitive, Configuration.SERVICES_TIMEOUT, Configuration.DATA_TIMEOUT))
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.PRIMITIVE_NOT_FOUND));
                        }
                        List<InventoryBase> inventory =
                            Client.Inventory.GetTaskInventory(primitive.ID, primitive.LocalID,
                                Configuration.SERVICES_TIMEOUT).ToList();
                        InventoryItem item = !entityUUID.Equals(UUID.Zero)
                            ? inventory.AsParallel().FirstOrDefault(o => o.UUID.Equals(entityUUID)) as InventoryItem
                            : inventory.AsParallel().FirstOrDefault(o => o.Name.Equals(entity)) as InventoryItem;
                        if (item == null)
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.INVENTORY_ITEM_NOT_FOUND));
                        }
                        switch (item.AssetType)
                        {
                            case AssetType.LSLBytecode:
                            case AssetType.LSLText:
                                break;
                            default:
                                throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.ITEM_IS_NOT_A_SCRIPT));
                        }
                        ManualResetEvent ScriptRunningReplyEvent = new ManualResetEvent(false);
                        bool running = false;
                        EventHandler<ScriptRunningReplyEventArgs> ScriptRunningEventHandler = (sender, args) =>
                        {
                            running = args.IsRunning;
                            ScriptRunningReplyEvent.Set();
                        };
                        lock (ClientInstanceInventoryLock)
                        {
                            Client.Inventory.ScriptRunningReply += ScriptRunningEventHandler;
                            Client.Inventory.RequestGetScriptRunning(primitive.ID, item.UUID);
                            if (!ScriptRunningReplyEvent.WaitOne(Configuration.SERVICES_TIMEOUT, false))
                            {
                                Client.Inventory.ScriptRunningReply -= ScriptRunningEventHandler;
                                throw new Exception(
                                    wasGetDescriptionFromEnumValue(ScriptError.TIMEOUT_GETTING_SCRIPT_STATE));
                            }
                            Client.Inventory.ScriptRunningReply -= ScriptRunningEventHandler;
                        }
                        result.Add(wasGetDescriptionFromEnumValue(ResultKeys.DATA), running.ToString());
                    };
                    break;
                case ScriptKeys.GETPRIMITIVEINVENTORY:
                    execute = () =>
                    {
                        if (!HasCorrodePermission(group, (int) Permissions.PERMISSION_INTERACT))
                        {
                            throw new Exception(
                                wasGetDescriptionFromEnumValue(ScriptError.NO_CORRADE_PERMISSIONS));
                        }
                        float range;
                        if (
                            !float.TryParse(
                                wasInput(wasKeyValueGet(
                                    wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.RANGE)), message)),
                                out range))
                        {
                            range = Configuration.RANGE;
                        }
                        Primitive primitive = null;
                        if (
                            !FindPrimitive(
                                StringOrUUID(wasInput(wasKeyValueGet(
                                    wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.ITEM)), message))),
                                range,
                                ref primitive, Configuration.SERVICES_TIMEOUT, Configuration.DATA_TIMEOUT))
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.PRIMITIVE_NOT_FOUND));
                        }
                        List<string> data =
                            new List<string>(Client.Inventory.GetTaskInventory(primitive.ID, primitive.LocalID,
                                Configuration.SERVICES_TIMEOUT).AsParallel().Select(o => new[]
                                {
                                    o.Name,
                                    o.UUID.ToString()
                                }).SelectMany(o => o));
                        if (!data.Count.Equals(0))
                        {
                            result.Add(wasGetDescriptionFromEnumValue(ResultKeys.DATA),
                                wasEnumerableToCSV(data));
                        }
                    };
                    break;
                case ScriptKeys.GETPRIMITIVEINVENTORYDATA:
                    execute = () =>
                    {
                        if (!HasCorrodePermission(group, (int) Permissions.PERMISSION_INTERACT))
                        {
                            throw new Exception(
                                wasGetDescriptionFromEnumValue(ScriptError.NO_CORRADE_PERMISSIONS));
                        }
                        float range;
                        if (
                            !float.TryParse(
                                wasInput(wasKeyValueGet(
                                    wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.RANGE)), message)),
                                out range))
                        {
                            range = Configuration.RANGE;
                        }
                        Primitive primitive = null;
                        if (
                            !FindPrimitive(
                                StringOrUUID(wasInput(wasKeyValueGet(
                                    wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.ITEM)), message))),
                                range,
                                ref primitive, Configuration.SERVICES_TIMEOUT, Configuration.DATA_TIMEOUT))
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.PRIMITIVE_NOT_FOUND));
                        }
                        string entity =
                            wasInput(wasKeyValueGet(wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.ENTITY)),
                                message));
                        UUID entityUUID;
                        if (!UUID.TryParse(entity, out entityUUID))
                        {
                            if (string.IsNullOrEmpty(entity))
                            {
                                throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.UNKNOWN_ENTITY));
                            }
                            entityUUID = UUID.Zero;
                        }
                        List<InventoryBase> inventory =
                            Client.Inventory.GetTaskInventory(primitive.ID, primitive.LocalID,
                                Configuration.SERVICES_TIMEOUT).ToList();
                        InventoryItem item = !entityUUID.Equals(UUID.Zero)
                            ? inventory.AsParallel().FirstOrDefault(o => o.UUID.Equals(entityUUID)) as InventoryItem
                            : inventory.AsParallel().FirstOrDefault(o => o.Name.Equals(entity)) as InventoryItem;
                        if (item == null)
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.INVENTORY_ITEM_NOT_FOUND));
                        }
                        List<string> data = new List<string>(GetStructuredData(item,
                            wasInput(wasKeyValueGet(wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.DATA)),
                                message))));
                        if (!data.Count.Equals(0))
                        {
                            result.Add(wasGetDescriptionFromEnumValue(ResultKeys.DATA),
                                wasEnumerableToCSV(data));
                        }
                    };
                    break;
                case ScriptKeys.UPDATEPRIMITIVEINVENTORY:
                    execute = () =>
                    {
                        if (!HasCorrodePermission(group, (int) Permissions.PERMISSION_INTERACT))
                        {
                            throw new Exception(
                                wasGetDescriptionFromEnumValue(ScriptError.NO_CORRADE_PERMISSIONS));
                        }
                        float range;
                        if (
                            !float.TryParse(
                                wasInput(wasKeyValueGet(
                                    wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.RANGE)), message)),
                                out range))
                        {
                            range = Configuration.RANGE;
                        }
                        Primitive primitive = null;
                        if (
                            !FindPrimitive(
                                StringOrUUID(wasInput(wasKeyValueGet(
                                    wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.ITEM)), message))),
                                range,
                                ref primitive, Configuration.SERVICES_TIMEOUT, Configuration.DATA_TIMEOUT))
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.PRIMITIVE_NOT_FOUND));
                        }
                        string entity =
                            wasInput(wasKeyValueGet(wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.ENTITY)),
                                message));
                        UUID entityUUID;
                        if (!UUID.TryParse(entity, out entityUUID))
                        {
                            if (string.IsNullOrEmpty(entity))
                            {
                                throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.UNKNOWN_ENTITY));
                            }
                            entityUUID = UUID.Zero;
                        }
                        InventoryBase inventoryBaseItem;
                        switch (
                            wasGetEnumValueFromDescription<Action>(
                                wasInput(
                                    wasKeyValueGet(wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.ACTION)),
                                        message)).ToLowerInvariant()))
                        {
                            case Action.ADD:
                                inventoryBaseItem =
                                    FindInventory<InventoryBase>(Client.Inventory.Store.RootNode,
                                        !entityUUID.Equals(UUID.Zero) ? entityUUID.ToString() : entity
                                        ).FirstOrDefault();
                                if (inventoryBaseItem == null)
                                {
                                    throw new Exception(
                                        wasGetDescriptionFromEnumValue(ScriptError.INVENTORY_ITEM_NOT_FOUND));
                                }
                                Client.Inventory.UpdateTaskInventory(primitive.LocalID,
                                    inventoryBaseItem as InventoryItem);
                                break;
                            case Action.REMOVE:
                                if (entityUUID.Equals(UUID.Zero))
                                {
                                    inventoryBaseItem = Client.Inventory.GetTaskInventory(primitive.ID,
                                        primitive.LocalID,
                                        Configuration.SERVICES_TIMEOUT)
                                        .AsParallel()
                                        .FirstOrDefault(o => o.Name.Equals(entity));
                                    if (inventoryBaseItem == null)
                                    {
                                        throw new Exception(
                                            wasGetDescriptionFromEnumValue(ScriptError.INVENTORY_ITEM_NOT_FOUND));
                                    }
                                    entityUUID = inventoryBaseItem.UUID;
                                }
                                Client.Inventory.RemoveTaskInventory(primitive.LocalID, entityUUID,
                                    Client.Network.Simulators.FirstOrDefault(
                                        o => o.Handle.Equals(primitive.RegionHandle)));
                                break;
                            case Action.TAKE:
                                inventoryBaseItem = !entityUUID.Equals(UUID.Zero)
                                    ? Client.Inventory.GetTaskInventory(primitive.ID, primitive.LocalID,
                                        Configuration.SERVICES_TIMEOUT)
                                        .AsParallel()
                                        .FirstOrDefault(o => o.UUID.Equals(entityUUID))
                                    : Client.Inventory.GetTaskInventory(primitive.ID, primitive.LocalID,
                                        Configuration.SERVICES_TIMEOUT)
                                        .AsParallel()
                                        .FirstOrDefault(o => o.Name.Equals(entity));
                                InventoryItem inventoryItem = inventoryBaseItem as InventoryItem;
                                if (inventoryItem == null)
                                {
                                    throw new Exception(
                                        wasGetDescriptionFromEnumValue(ScriptError.INVENTORY_ITEM_NOT_FOUND));
                                }
                                UUID folderUUID;
                                string folder =
                                    wasInput(
                                        wasKeyValueGet(wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.FOLDER)),
                                            message));
                                if (string.IsNullOrEmpty(folder) || !UUID.TryParse(folder, out folderUUID))
                                {
                                    folderUUID =
                                        Client.Inventory.Store.Items[
                                            Client.Inventory.FindFolderForType(inventoryItem.AssetType)].Data
                                            .UUID;
                                }
                                Client.Inventory.MoveTaskInventory(primitive.LocalID, inventoryItem.UUID, folderUUID,
                                    Client.Network.Simulators.FirstOrDefault(
                                        o => o.Handle.Equals(primitive.RegionHandle)));
                                break;
                            default:
                                throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.UNKNOWN_ACTION));
                        }
                    };
                    break;
                case ScriptKeys.GETINVENTORYDATA:
                    execute = () =>
                    {
                        if (!HasCorrodePermission(group, (int) Permissions.PERMISSION_INVENTORY))
                        {
                            throw new Exception(
                                wasGetDescriptionFromEnumValue(ScriptError.NO_CORRADE_PERMISSIONS));
                        }
                        InventoryBase inventoryBaseItem =
                            FindInventory<InventoryBase>(Client.Inventory.Store.RootNode,
                                wasInput(wasKeyValueGet(
                                    wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.ITEM)), message))
                                ).FirstOrDefault();
                        if (inventoryBaseItem == null)
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.INVENTORY_ITEM_NOT_FOUND));
                        }
                        List<string> data = new List<string>(GetStructuredData(inventoryBaseItem as InventoryItem,
                            wasInput(wasKeyValueGet(wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.DATA)),
                                message))));
                        if (!data.Count.Equals(0))
                        {
                            result.Add(wasGetDescriptionFromEnumValue(ResultKeys.DATA),
                                wasEnumerableToCSV(data));
                        }
                    };
                    break;
                case ScriptKeys.SEARCHINVENTORY:
                    execute = () =>
                    {
                        if (!HasCorrodePermission(group, (int) Permissions.PERMISSION_INVENTORY))
                        {
                            throw new Exception(
                                wasGetDescriptionFromEnumValue(ScriptError.NO_CORRADE_PERMISSIONS));
                        }
                        HashSet<AssetType> assetTypes = new HashSet<AssetType>();
                        Parallel.ForEach(wasCSVToEnumerable(
                            wasInput(wasKeyValueGet(wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.TYPE)),
                                message))),
                            o => Parallel.ForEach(
                                typeof (AssetType).GetFields(BindingFlags.Public | BindingFlags.Static)
                                    .AsParallel().Where(p => p.Name.Equals(o, StringComparison.Ordinal)),
                                q => assetTypes.Add((AssetType) q.GetValue(null))));
                        string pattern =
                            wasInput(wasKeyValueGet(wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.PATTERN)),
                                message));
                        if (string.IsNullOrEmpty(pattern))
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.NO_PATTERN_PROVIDED));
                        }
                        Regex search;
                        try
                        {
                            search = new Regex(pattern, RegexOptions.Compiled);
                        }
                        catch
                        {
                            throw new Exception(
                                wasGetDescriptionFromEnumValue(ScriptError.COULD_NOT_COMPILE_REGULAR_EXPRESSION));
                        }
                        List<string> csv = new List<string>();
                        object LockObject = new object();
                        Parallel.ForEach(FindInventory<InventoryBase>(Client.Inventory.Store.RootNode, search
                            ),
                            o =>
                            {
                                InventoryItem inventoryItem = o as InventoryItem;
                                if (inventoryItem == null) return;
                                if (!assetTypes.Count.Equals(0) && !assetTypes.Contains(inventoryItem.AssetType))
                                    return;
                                lock (LockObject)
                                {
                                    csv.Add(Enum.GetName(typeof (AssetType), inventoryItem.AssetType));
                                    csv.Add(inventoryItem.Name);
                                    csv.Add(inventoryItem.AssetUUID.ToString());
                                }
                            });
                        if (!csv.Count.Equals(0))
                        {
                            result.Add(wasGetDescriptionFromEnumValue(ResultKeys.DATA),
                                wasEnumerableToCSV(csv));
                        }
                    };
                    break;
                case ScriptKeys.GETINVENTORYPATH:
                    execute = () =>
                    {
                        if (!HasCorrodePermission(group, (int) Permissions.PERMISSION_INVENTORY))
                        {
                            throw new Exception(
                                wasGetDescriptionFromEnumValue(ScriptError.NO_CORRADE_PERMISSIONS));
                        }
                        HashSet<AssetType> assetTypes = new HashSet<AssetType>();
                        Parallel.ForEach(wasCSVToEnumerable(
                            wasInput(wasKeyValueGet(wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.TYPE)),
                                message))),
                            o => Parallel.ForEach(
                                typeof (AssetType).GetFields(BindingFlags.Public | BindingFlags.Static)
                                    .AsParallel().Where(p => p.Name.Equals(o, StringComparison.Ordinal)),
                                q => assetTypes.Add((AssetType) q.GetValue(null))));
                        string pattern =
                            wasInput(wasKeyValueGet(wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.PATTERN)),
                                message));
                        if (string.IsNullOrEmpty(pattern))
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.NO_PATTERN_PROVIDED));
                        }
                        Regex search;
                        try
                        {
                            search = new Regex(pattern, RegexOptions.Compiled);
                        }
                        catch
                        {
                            throw new Exception(
                                wasGetDescriptionFromEnumValue(ScriptError.COULD_NOT_COMPILE_REGULAR_EXPRESSION));
                        }
                        List<string> csv = new List<string>();
                        Parallel.ForEach(FindInventoryPath<InventoryBase>(Client.Inventory.Store.RootNode,
                            search, new LinkedList<string>()).AsParallel().Select(o => o.Value),
                            o => csv.Add(string.Join(CORRADE_CONSTANTS.PATH_SEPARATOR, o.ToArray())));
                        if (!csv.Count.Equals(0))
                        {
                            result.Add(wasGetDescriptionFromEnumValue(ResultKeys.DATA),
                                wasEnumerableToCSV(csv));
                        }
                    };
                    break;
                case ScriptKeys.GETPARTICLESYSTEM:
                    execute = () =>
                    {
                        if (!HasCorrodePermission(group, (int) Permissions.PERMISSION_INTERACT))
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.NO_CORRADE_PERMISSIONS));
                        }
                        float range;
                        if (
                            !float.TryParse(
                                wasInput(wasKeyValueGet(
                                    wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.RANGE)), message)),
                                out range))
                        {
                            range = Configuration.RANGE;
                        }
                        Primitive primitive = null;
                        if (
                            !FindPrimitive(
                                StringOrUUID(wasInput(wasKeyValueGet(
                                    wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.ITEM)), message))),
                                range,
                                ref primitive, Configuration.SERVICES_TIMEOUT, Configuration.DATA_TIMEOUT))
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.PRIMITIVE_NOT_FOUND));
                        }
                        StringBuilder particleSystem = new StringBuilder();
                        particleSystem.Append("PSYS_PART_FLAGS, 0");
                        if (!((long) primitive.ParticleSys.PartDataFlags &
                              (long) Primitive.ParticleSystem.ParticleDataFlags.InterpColor).Equals(0))
                            particleSystem.Append(" | PSYS_PART_INTERP_COLOR_MASK");
                        if (!((long) primitive.ParticleSys.PartDataFlags &
                              (long) Primitive.ParticleSystem.ParticleDataFlags.InterpScale).Equals(0))
                            particleSystem.Append(" | PSYS_PART_INTERP_SCALE_MASK");
                        if (
                            !((long) primitive.ParticleSys.PartDataFlags &
                              (long) Primitive.ParticleSystem.ParticleDataFlags.Bounce).Equals(0))
                            particleSystem.Append(" | PSYS_PART_BOUNCE_MASK");
                        if (
                            !((long) primitive.ParticleSys.PartDataFlags &
                              (long) Primitive.ParticleSystem.ParticleDataFlags.Wind).Equals(0))
                            particleSystem.Append(" | PSYS_PART_WIND_MASK");
                        if (
                            !((long) primitive.ParticleSys.PartDataFlags &
                              (long) Primitive.ParticleSystem.ParticleDataFlags.FollowSrc).Equals(0))
                            particleSystem.Append(" | PSYS_PART_FOLLOW_SRC_MASK");
                        if (!((long) primitive.ParticleSys.PartDataFlags &
                              (long) Primitive.ParticleSystem.ParticleDataFlags.FollowVelocity).Equals(0))
                            particleSystem.Append(" | PSYS_PART_FOLLOW_VELOCITY_MASK");
                        if (
                            !((long) primitive.ParticleSys.PartDataFlags &
                              (long) Primitive.ParticleSystem.ParticleDataFlags.TargetPos).Equals(0))
                            particleSystem.Append(" | PSYS_PART_TARGET_POS_MASK");
                        if (!((long) primitive.ParticleSys.PartDataFlags &
                              (long) Primitive.ParticleSystem.ParticleDataFlags.TargetLinear).Equals(0))
                            particleSystem.Append(" | PSYS_PART_TARGET_LINEAR_MASK");
                        if (
                            !((long) primitive.ParticleSys.PartDataFlags &
                              (long) Primitive.ParticleSystem.ParticleDataFlags.Emissive).Equals(0))
                            particleSystem.Append(" | PSYS_PART_EMISSIVE_MASK");
                        particleSystem.Append(",");
                        particleSystem.Append("PSYS_SRC_PATTERN, 0");
                        if (
                            !((long) primitive.ParticleSys.Pattern & (long) Primitive.ParticleSystem.SourcePattern.Drop)
                                .Equals(0))
                            particleSystem.Append(" | PSYS_SRC_PATTERN_DROP");
                        if (!((long) primitive.ParticleSys.Pattern &
                              (long) Primitive.ParticleSystem.SourcePattern.Explode).Equals(0))
                            particleSystem.Append(" | PSYS_SRC_PATTERN_EXPLODE");
                        if (
                            !((long) primitive.ParticleSys.Pattern & (long) Primitive.ParticleSystem.SourcePattern.Angle)
                                .Equals(0))
                            particleSystem.Append(" | PSYS_SRC_PATTERN_ANGLE");
                        if (!((long) primitive.ParticleSys.Pattern &
                              (long) Primitive.ParticleSystem.SourcePattern.AngleCone).Equals(0))
                            particleSystem.Append(" | PSYS_SRC_PATTERN_ANGLE_CONE");
                        if (!((long) primitive.ParticleSys.Pattern &
                              (long) Primitive.ParticleSystem.SourcePattern.AngleConeEmpty).Equals(0))
                            particleSystem.Append(" | PSYS_SRC_PATTERN_ANGLE_CONE_EMPTY");
                        particleSystem.Append(",");
                        particleSystem.Append("PSYS_PART_START_ALPHA, " +
                                              string.Format(CultureInfo.InvariantCulture, "{0:0.00000}",
                                                  primitive.ParticleSys.PartStartColor.A) +
                                              ",");
                        particleSystem.Append("PSYS_PART_END_ALPHA, " +
                                              string.Format(CultureInfo.InvariantCulture, "{0:0.00000}",
                                                  primitive.ParticleSys.PartEndColor.A) +
                                              ",");
                        particleSystem.Append("PSYS_PART_START_COLOR, " +
                                              primitive.ParticleSys.PartStartColor.ToRGBString() +
                                              ",");
                        particleSystem.Append("PSYS_PART_END_COLOR, " + primitive.ParticleSys.PartEndColor.ToRGBString() +
                                              ",");
                        particleSystem.Append("PSYS_PART_START_SCALE, <" +
                                              string.Format(CultureInfo.InvariantCulture, "{0:0.00000}",
                                                  primitive.ParticleSys.PartStartScaleX) + ", " +
                                              string.Format(CultureInfo.InvariantCulture, "{0:0.00000}",
                                                  primitive.ParticleSys.PartStartScaleY) +
                                              ", 0>, ");
                        particleSystem.Append("PSYS_PART_END_SCALE, <" +
                                              string.Format(CultureInfo.InvariantCulture, "{0:0.00000}",
                                                  primitive.ParticleSys.PartEndScaleX) + ", " +
                                              string.Format(CultureInfo.InvariantCulture, "{0:0.00000}",
                                                  primitive.ParticleSys.PartEndScaleY) +
                                              ", 0>, ");
                        particleSystem.Append("PSYS_PART_MAX_AGE, " +
                                              string.Format(CultureInfo.InvariantCulture, "{0:0.00000}",
                                                  primitive.ParticleSys.PartMaxAge) +
                                              ",");
                        particleSystem.Append("PSYS_SRC_MAX_AGE, " +
                                              string.Format(CultureInfo.InvariantCulture, "{0:0.00000}",
                                                  primitive.ParticleSys.MaxAge) +
                                              ",");
                        particleSystem.Append("PSYS_SRC_ACCEL, " + primitive.ParticleSys.PartAcceleration +
                                              ",");
                        particleSystem.Append("PSYS_SRC_BURST_PART_COUNT, " +
                                              string.Format(CultureInfo.InvariantCulture, "{0:0}",
                                                  primitive.ParticleSys.BurstPartCount) +
                                              ",");
                        particleSystem.Append("PSYS_SRC_BURST_RADIUS, " +
                                              string.Format(CultureInfo.InvariantCulture, "{0:0.00000}",
                                                  primitive.ParticleSys.BurstRadius) +
                                              ",");
                        particleSystem.Append("PSYS_SRC_BURST_RATE, " +
                                              string.Format(CultureInfo.InvariantCulture, "{0:0.00000}",
                                                  primitive.ParticleSys.BurstRate) +
                                              ",");
                        particleSystem.Append("PSYS_SRC_BURST_SPEED_MIN, " +
                                              string.Format(CultureInfo.InvariantCulture, "{0:0.00000}",
                                                  primitive.ParticleSys.BurstSpeedMin) +
                                              ",");
                        particleSystem.Append("PSYS_SRC_BURST_SPEED_MAX, " +
                                              string.Format(CultureInfo.InvariantCulture, "{0:0.00000}",
                                                  primitive.ParticleSys.BurstSpeedMax) +
                                              ",");
                        particleSystem.Append("PSYS_SRC_INNERANGLE, " +
                                              string.Format(CultureInfo.InvariantCulture, "{0:0.00000}",
                                                  primitive.ParticleSys.InnerAngle) +
                                              ",");
                        particleSystem.Append("PSYS_SRC_OUTERANGLE, " +
                                              string.Format(CultureInfo.InvariantCulture, "{0:0.00000}",
                                                  primitive.ParticleSys.OuterAngle) +
                                              ",");
                        particleSystem.Append("PSYS_SRC_OMEGA, " + primitive.ParticleSys.AngularVelocity +
                                              ",");
                        particleSystem.Append("PSYS_SRC_TEXTURE, (key)\"" + primitive.ParticleSys.Texture + "\"" +
                                              ",");
                        particleSystem.Append("PSYS_SRC_TARGET_KEY, (key)\"" + primitive.ParticleSys.Target + "\"");
                        result.Add(wasGetDescriptionFromEnumValue(ResultKeys.DATA), particleSystem.ToString());
                    };
                    break;
                case ScriptKeys.CREATENOTECARD:
                    execute = () =>
                    {
                        if (!HasCorrodePermission(group, (int) Permissions.PERMISSION_INVENTORY))
                        {
                            throw new Exception(
                                wasGetDescriptionFromEnumValue(ScriptError.NO_CORRADE_PERMISSIONS));
                        }
                        string name =
                            wasInput(wasKeyValueGet(wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.NAME)),
                                message));
                        if (string.IsNullOrEmpty(name))
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.NO_NAME_PROVIDED));
                        }
                        ManualResetEvent CreateNotecardEvent = new ManualResetEvent(false);
                        bool succeeded = false;
                        InventoryItem newItem = null;
                        Client.Inventory.RequestCreateItem(Client.Inventory.FindFolderForType(AssetType.Notecard),
                            name,
                            wasInput(
                                wasKeyValueGet(wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.DESCRIPTION)),
                                    message)),
                            AssetType.Notecard,
                            UUID.Random(), InventoryType.Notecard, PermissionMask.All,
                            delegate(bool completed, InventoryItem createdItem)
                            {
                                succeeded = completed;
                                newItem = createdItem;
                                CreateNotecardEvent.Set();
                            });
                        if (!CreateNotecardEvent.WaitOne(Configuration.SERVICES_TIMEOUT, false))
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.TIMEOUT_CREATING_ITEM));
                        }
                        if (!succeeded)
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.UNABLE_TO_CREATE_ITEM));
                        }
                        AssetNotecard blank = new AssetNotecard
                        {
                            BodyText = LINDEN_CONSTANTS.ASSETS.NOTECARD.NEWLINE
                        };
                        blank.Encode();
                        ManualResetEvent UploadBlankNotecardEvent = new ManualResetEvent(false);
                        succeeded = false;
                        Client.Inventory.RequestUploadNotecardAsset(blank.AssetData, newItem.UUID,
                            delegate(bool completed, string status, UUID itemUUID, UUID assetUUID)
                            {
                                succeeded = completed;
                                UploadBlankNotecardEvent.Set();
                            });
                        if (!UploadBlankNotecardEvent.WaitOne(Configuration.SERVICES_TIMEOUT, false))
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.TIMEOUT_UPLOADING_ITEM));
                        }
                        if (!succeeded)
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.UNABLE_TO_UPLOAD_ITEM));
                        }
                        string text =
                            wasInput(wasKeyValueGet(wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.TEXT)),
                                message));
                        if (!string.IsNullOrEmpty(text))
                        {
                            AssetNotecard notecard = new AssetNotecard
                            {
                                BodyText = text
                            };
                            notecard.Encode();
                            ManualResetEvent UploadNotecardDataEvent = new ManualResetEvent(false);
                            succeeded = false;
                            Client.Inventory.RequestUploadNotecardAsset(notecard.AssetData, newItem.UUID,
                                delegate(bool completed, string status, UUID itemUUID, UUID assetUUID)
                                {
                                    succeeded = completed;
                                    UploadNotecardDataEvent.Set();
                                });
                            if (!UploadNotecardDataEvent.WaitOne(Configuration.SERVICES_TIMEOUT, false))
                            {
                                throw new Exception(
                                    wasGetDescriptionFromEnumValue(ScriptError.TIMEOUT_UPLOADING_ITEM_DATA));
                            }
                            if (!succeeded)
                            {
                                throw new Exception(
                                    wasGetDescriptionFromEnumValue(ScriptError.UNABLE_TO_UPLOAD_ITEM_DATA));
                            }
                        }
                    };
                    break;
                case ScriptKeys.ACTIVATE:
                    execute = () =>
                    {
                        if (
                            !HasCorrodePermission(group,
                                (int) Permissions.PERMISSION_GROOMING))
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.NO_CORRADE_PERMISSIONS));
                        }
                        Group configuredGroup =
                            Configuration.GROUPS.AsParallel().FirstOrDefault(
                                o => o.Name.Equals(group, StringComparison.Ordinal));
                        UUID groupUUID = UUID.Zero;
                        switch (!configuredGroup.Equals(default(Group)))
                        {
                            case true:
                                groupUUID = configuredGroup.UUID;
                                break;
                            default:
                                if (!GroupNameToUUID(group, Configuration.SERVICES_TIMEOUT, Configuration.DATA_TIMEOUT,
                                    ref groupUUID))
                                {
                                    throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.GROUP_NOT_FOUND));
                                }
                                break;
                        }
                        if (
                            !AgentInGroup(Client.Self.AgentID, groupUUID, Configuration.SERVICES_TIMEOUT,
                                Configuration.DATA_TIMEOUT))
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.AGENT_NOT_IN_GROUP));
                        }
                        Client.Groups.ActivateGroup(groupUUID);
                    };
                    break;
                case ScriptKeys.TAG:
                    execute = () =>
                    {
                        if (
                            !HasCorrodePermission(group,
                                (int) Permissions.PERMISSION_GROOMING))
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.NO_CORRADE_PERMISSIONS));
                        }
                        Group configuredGroup =
                            Configuration.GROUPS.AsParallel().FirstOrDefault(
                                o => o.Name.Equals(group, StringComparison.Ordinal));
                        UUID groupUUID = UUID.Zero;
                        switch (!configuredGroup.Equals(default(Group)))
                        {
                            case true:
                                groupUUID = configuredGroup.UUID;
                                break;
                            default:
                                if (!GroupNameToUUID(group, Configuration.SERVICES_TIMEOUT, Configuration.DATA_TIMEOUT,
                                    ref groupUUID))
                                {
                                    throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.GROUP_NOT_FOUND));
                                }
                                break;
                        }
                        if (
                            !AgentInGroup(Client.Self.AgentID, groupUUID, Configuration.SERVICES_TIMEOUT,
                                Configuration.DATA_TIMEOUT))
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.AGENT_NOT_IN_GROUP));
                        }
                        switch (wasGetEnumValueFromDescription<Action>(
                            wasInput(
                                wasKeyValueGet(wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.ACTION)), message))
                                .ToLowerInvariant()))
                        {
                            case Action.SET:
                                ManualResetEvent GroupRoleDataReplyEvent = new ManualResetEvent(false);
                                Dictionary<string, UUID> roleData = new Dictionary<string, UUID>();
                                EventHandler<GroupRolesDataReplyEventArgs> Groups_GroupRoleDataReply = (sender, args) =>
                                {
                                    roleData = args.Roles.ToDictionary(o => o.Value.Title, o => o.Value.ID);
                                    GroupRoleDataReplyEvent.Set();
                                };
                                lock (ClientInstanceGroupsLock)
                                {
                                    Client.Groups.GroupRoleDataReply += Groups_GroupRoleDataReply;
                                    Client.Groups.RequestGroupRoles(groupUUID);
                                    if (!GroupRoleDataReplyEvent.WaitOne(Configuration.SERVICES_TIMEOUT, false))
                                    {
                                        Client.Groups.GroupRoleDataReply -= Groups_GroupRoleDataReply;
                                        throw new Exception(
                                            wasGetDescriptionFromEnumValue(ScriptError.TIMEOUT_GETTING_GROUP_ROLES));
                                    }
                                    Client.Groups.GroupRoleDataReply -= Groups_GroupRoleDataReply;
                                }
                                KeyValuePair<string, UUID> role = roleData.AsParallel().FirstOrDefault(
                                    o =>
                                        o.Key.Equals(
                                            wasInput(
                                                wasKeyValueGet(
                                                    wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.TITLE)),
                                                    message)),
                                            StringComparison.Ordinal));
                                if (role.Equals(default(KeyValuePair<string, UUID>)))
                                {
                                    throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.COULD_NOT_FIND_TITLE));
                                }
                                Client.Groups.ActivateTitle(groupUUID, role.Value);
                                break;
                            case Action.GET:
                                string title = string.Empty;
                                ManualResetEvent GroupTitlesReplyEvent = new ManualResetEvent(false);
                                EventHandler<GroupTitlesReplyEventArgs> GroupTitlesReplyEventHandler = (sender, args) =>
                                {
                                    KeyValuePair<UUID, GroupTitle> pair =
                                        args.Titles.AsParallel().FirstOrDefault(o => o.Value.Selected);
                                    if (!pair.Equals(default(KeyValuePair<UUID, GroupTitle>)))
                                    {
                                        title = pair.Value.Title;
                                    }
                                    GroupTitlesReplyEvent.Set();
                                };
                                lock (ClientInstanceGroupsLock)
                                {
                                    Client.Groups.GroupTitlesReply += GroupTitlesReplyEventHandler;
                                    Client.Groups.RequestGroupTitles(groupUUID);
                                    if (!GroupTitlesReplyEvent.WaitOne(Configuration.SERVICES_TIMEOUT, false))
                                    {
                                        Client.Groups.GroupTitlesReply -= GroupTitlesReplyEventHandler;
                                        throw new Exception(
                                            wasGetDescriptionFromEnumValue(ScriptError.TIMEOUT_GETTING_GROUP_TITLES));
                                    }
                                    Client.Groups.GroupTitlesReply -= GroupTitlesReplyEventHandler;
                                }
                                if (!title.Equals(string.Empty))
                                {
                                    result.Add(wasGetDescriptionFromEnumValue(ResultKeys.DATA), title);
                                }
                                break;
                            default:
                                throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.UNKNOWN_ACTION));
                        }
                    };
                    break;
                case ScriptKeys.GETTITLES:
                    execute = () =>
                    {
                        if (!HasCorrodePermission(group, (int) Permissions.PERMISSION_GROUP))
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.NO_CORRADE_PERMISSIONS));
                        }
                        Group configuredGroup =
                            Configuration.GROUPS.AsParallel().FirstOrDefault(
                                o => o.Name.Equals(group, StringComparison.Ordinal));
                        UUID groupUUID = UUID.Zero;
                        switch (!configuredGroup.Equals(default(Group)))
                        {
                            case true:
                                groupUUID = configuredGroup.UUID;
                                break;
                            default:
                                if (!GroupNameToUUID(group, Configuration.SERVICES_TIMEOUT, Configuration.DATA_TIMEOUT,
                                    ref groupUUID))
                                {
                                    throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.GROUP_NOT_FOUND));
                                }
                                break;
                        }
                        if (
                            !AgentInGroup(Client.Self.AgentID, groupUUID, Configuration.SERVICES_TIMEOUT,
                                Configuration.DATA_TIMEOUT))
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.AGENT_NOT_IN_GROUP));
                        }
                        List<string> csv = new List<string>();
                        ManualResetEvent GroupTitlesReplyEvent = new ManualResetEvent(false);
                        EventHandler<GroupTitlesReplyEventArgs> GroupTitlesReplyEventHandler = (sender, args) =>
                        {
                            foreach (KeyValuePair<UUID, GroupTitle> title in args.Titles)
                            {
                                string roleName = string.Empty;
                                if (
                                    !RoleUUIDToName(title.Value.RoleID, groupUUID, Configuration.SERVICES_TIMEOUT,
                                        Configuration.DATA_TIMEOUT,
                                        ref roleName))
                                    continue;
                                csv.Add(title.Value.Title);
                                csv.Add(title.Key.ToString());
                                csv.Add(roleName);
                                csv.Add(title.Value.RoleID.ToString());
                            }
                            GroupTitlesReplyEvent.Set();
                        };
                        lock (ClientInstanceGroupsLock)
                        {
                            Client.Groups.GroupTitlesReply += GroupTitlesReplyEventHandler;
                            Client.Groups.RequestGroupTitles(groupUUID);
                            if (!GroupTitlesReplyEvent.WaitOne(Configuration.SERVICES_TIMEOUT, false))
                            {
                                Client.Groups.GroupTitlesReply -= GroupTitlesReplyEventHandler;
                                throw new Exception(
                                    wasGetDescriptionFromEnumValue(ScriptError.TIMEOUT_GETTING_GROUP_TITLES));
                            }
                            Client.Groups.GroupTitlesReply -= GroupTitlesReplyEventHandler;
                        }
                        if (!csv.Count.Equals(0))
                        {
                            result.Add(wasGetDescriptionFromEnumValue(ResultKeys.DATA),
                                wasEnumerableToCSV(csv));
                        }
                    };
                    break;
                case ScriptKeys.AUTOPILOT:
                    execute = () =>
                    {
                        if (
                            !HasCorrodePermission(group,
                                (int) Permissions.PERMISSION_MOVEMENT))
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.NO_CORRADE_PERMISSIONS));
                        }
                        switch (
                            wasGetEnumValueFromDescription<Action>(
                                wasInput(
                                    wasKeyValueGet(wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.ACTION)),
                                        message)).ToLowerInvariant()))
                        {
                            case Action.START:
                                Vector3 position;
                                if (
                                    !Vector3.TryParse(
                                        wasInput(wasKeyValueGet(
                                            wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.POSITION)), message)),
                                        out position))
                                {
                                    throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.INVALID_POSITION));
                                }
                                uint moveRegionX, moveRegionY;
                                Utils.LongToUInts(Client.Network.CurrentSim.Handle, out moveRegionX, out moveRegionY);
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
                                Client.Self.AutoPilotCancel();
                                Client.Self.Movement.TurnToward(position, true);
                                Client.Self.AutoPilot(position.X + moveRegionX, position.Y + moveRegionY, position.Z);
                                break;
                            case Action.STOP:
                                Client.Self.AutoPilotCancel();
                                break;
                            default:
                                throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.UNKNOWN_MOVE_ACTION));
                        }
                        // Set the camera on the avatar.
                        Client.Self.Movement.Camera.LookAt(
                            Client.Self.SimPosition,
                            Client.Self.SimPosition
                            );
                    };
                    break;
                case ScriptKeys.TURNTO:
                    execute = () =>
                    {
                        if (
                            !HasCorrodePermission(group,
                                (int) Permissions.PERMISSION_MOVEMENT))
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.NO_CORRADE_PERMISSIONS));
                        }
                        Vector3 position;
                        if (
                            !Vector3.TryParse(
                                wasInput(
                                    wasKeyValueGet(wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.POSITION)),
                                        message)),
                                out position))
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.INVALID_POSITION));
                        }
                        Client.Self.Movement.TurnToward(position, true);
                        // Set the camera on the avatar.
                        Client.Self.Movement.Camera.LookAt(
                            Client.Self.SimPosition,
                            Client.Self.SimPosition
                            );
                    };
                    break;
                case ScriptKeys.NUDGE:
                    execute = () =>
                    {
                        if (
                            !HasCorrodePermission(group,
                                (int) Permissions.PERMISSION_MOVEMENT))
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.NO_CORRADE_PERMISSIONS));
                        }
                        switch (wasGetEnumValueFromDescription<Direction>(
                            wasInput(wasKeyValueGet(
                                wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.DIRECTION)),
                                message))
                                .ToLowerInvariant()))
                        {
                            case Direction.BACK:
                                Client.Self.Movement.SendManualUpdate(AgentManager.ControlFlags.AGENT_CONTROL_AT_NEG,
                                    Client.Self.Movement.Camera.Position,
                                    Client.Self.Movement.Camera.AtAxis, Client.Self.Movement.Camera.LeftAxis,
                                    Client.Self.Movement.Camera.UpAxis,
                                    Client.Self.Movement.BodyRotation, Client.Self.Movement.HeadRotation,
                                    Client.Self.Movement.Camera.Far, AgentFlags.None, AgentState.None, true);
                                break;
                            case Direction.FORWARD:
                                Client.Self.Movement.SendManualUpdate(AgentManager.ControlFlags.AGENT_CONTROL_AT_POS,
                                    Client.Self.Movement.Camera.Position,
                                    Client.Self.Movement.Camera.AtAxis, Client.Self.Movement.Camera.LeftAxis,
                                    Client.Self.Movement.Camera.UpAxis,
                                    Client.Self.Movement.BodyRotation, Client.Self.Movement.HeadRotation,
                                    Client.Self.Movement.Camera.Far, AgentFlags.None,
                                    AgentState.None, true);
                                break;
                            case Direction.LEFT:
                                Client.Self.Movement.SendManualUpdate(AgentManager.ControlFlags.
                                    AGENT_CONTROL_LEFT_POS, Client.Self.Movement.Camera.Position,
                                    Client.Self.Movement.Camera.AtAxis, Client.Self.Movement.Camera.LeftAxis,
                                    Client.Self.Movement.Camera.UpAxis,
                                    Client.Self.Movement.BodyRotation, Client.Self.Movement.HeadRotation,
                                    Client.Self.Movement.Camera.Far, AgentFlags.None,
                                    AgentState.None, true);
                                break;
                            case Direction.RIGHT:
                                Client.Self.Movement.SendManualUpdate(AgentManager.ControlFlags.
                                    AGENT_CONTROL_LEFT_NEG, Client.Self.Movement.Camera.Position,
                                    Client.Self.Movement.Camera.AtAxis, Client.Self.Movement.Camera.LeftAxis,
                                    Client.Self.Movement.Camera.UpAxis,
                                    Client.Self.Movement.BodyRotation, Client.Self.Movement.HeadRotation,
                                    Client.Self.Movement.Camera.Far, AgentFlags.None,
                                    AgentState.None, true);
                                break;
                            case Direction.UP:
                                Client.Self.Movement.SendManualUpdate(AgentManager.ControlFlags.AGENT_CONTROL_UP_POS,
                                    Client.Self.Movement.Camera.Position,
                                    Client.Self.Movement.Camera.AtAxis, Client.Self.Movement.Camera.LeftAxis,
                                    Client.Self.Movement.Camera.UpAxis,
                                    Client.Self.Movement.BodyRotation, Client.Self.Movement.HeadRotation,
                                    Client.Self.Movement.Camera.Far, AgentFlags.None,
                                    AgentState.None, true);
                                break;
                            case Direction.DOWN:
                                Client.Self.Movement.SendManualUpdate(AgentManager.ControlFlags.AGENT_CONTROL_UP_NEG,
                                    Client.Self.Movement.Camera.Position,
                                    Client.Self.Movement.Camera.AtAxis, Client.Self.Movement.Camera.LeftAxis,
                                    Client.Self.Movement.Camera.UpAxis,
                                    Client.Self.Movement.BodyRotation, Client.Self.Movement.HeadRotation,
                                    Client.Self.Movement.Camera.Far, AgentFlags.None,
                                    AgentState.None, true);
                                break;
                            default:
                                throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.UNKNOWN_DIRECTION));
                        }
                        // Set the camera on the avatar.
                        Client.Self.Movement.Camera.LookAt(
                            Client.Self.SimPosition,
                            Client.Self.SimPosition
                            );
                    };
                    break;
                case ScriptKeys.SETVIEWEREFFECT:
                    execute = () =>
                    {
                        if (
                            !HasCorrodePermission(group,
                                (int) Permissions.PERMISSION_INTERACT))
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.NO_CORRADE_PERMISSIONS));
                        }
                        UUID effectUUID;
                        if (!UUID.TryParse(wasInput(wasKeyValueGet(
                            wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.ID)), message)), out effectUUID))
                        {
                            effectUUID = UUID.Random();
                        }
                        Vector3 offset;
                        if (
                            !Vector3.TryParse(
                                wasInput(
                                    wasKeyValueGet(
                                        wasOutput(
                                            wasGetDescriptionFromEnumValue(ScriptKeys.OFFSET)),
                                        message)),
                                out offset))
                        {
                            offset = Client.Self.SimPosition;
                        }
                        ViewerEffectType viewerEffectType = wasGetEnumValueFromDescription<ViewerEffectType>(
                            wasInput(
                                wasKeyValueGet(wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.EFFECT)), message))
                                .ToLowerInvariant());
                        switch (viewerEffectType)
                        {
                            case ViewerEffectType.BEAM:
                            case ViewerEffectType.POINT:
                            case ViewerEffectType.LOOK:
                                string item = wasInput(wasKeyValueGet(
                                    wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.ITEM)), message));
                                UUID targetUUID;
                                switch (!string.IsNullOrEmpty(item))
                                {
                                    case true:
                                        float range;
                                        if (
                                            !float.TryParse(
                                                wasInput(wasKeyValueGet(
                                                    wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.RANGE)), message)),
                                                out range))
                                        {
                                            range = Configuration.RANGE;
                                        }
                                        Primitive primitive = null;
                                        if (
                                            !FindPrimitive(
                                                StringOrUUID(item),
                                                range,
                                                ref primitive, Configuration.SERVICES_TIMEOUT,
                                                Configuration.DATA_TIMEOUT))
                                        {
                                            throw new Exception(
                                                wasGetDescriptionFromEnumValue(ScriptError.PRIMITIVE_NOT_FOUND));
                                        }
                                        targetUUID = primitive.ID;
                                        break;
                                    default:
                                        if (
                                            !UUID.TryParse(
                                                wasInput(wasKeyValueGet(
                                                    wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.AGENT)), message)),
                                                out targetUUID) && !AgentNameToUUID(
                                                    wasInput(
                                                        wasKeyValueGet(
                                                            wasOutput(
                                                                wasGetDescriptionFromEnumValue(ScriptKeys.FIRSTNAME)),
                                                            message)),
                                                    wasInput(
                                                        wasKeyValueGet(
                                                            wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.LASTNAME)),
                                                            message)),
                                                    Configuration.SERVICES_TIMEOUT, Configuration.DATA_TIMEOUT,
                                                    ref targetUUID))
                                        {
                                            throw new Exception(
                                                wasGetDescriptionFromEnumValue(ScriptError.AGENT_NOT_FOUND));
                                        }
                                        break;
                                }
                                switch (viewerEffectType)
                                {
                                    case ViewerEffectType.LOOK:
                                        FieldInfo lookAtTypeInfo = typeof (LookAtType).GetFields(BindingFlags.Public |
                                                                                                 BindingFlags.Static)
                                            .AsParallel().FirstOrDefault(
                                                o =>
                                                    o.Name.Equals(
                                                        wasInput(
                                                            wasKeyValueGet(
                                                                wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.TYPE)),
                                                                message)),
                                                        StringComparison.Ordinal));
                                        LookAtType lookAtType = lookAtTypeInfo != null
                                            ? (LookAtType)
                                                lookAtTypeInfo
                                                    .GetValue(null)
                                            : LookAtType.None;
                                        Client.Self.LookAtEffect(Client.Self.AgentID, targetUUID, offset,
                                            lookAtType, effectUUID);
                                        if (LookAtEffects.AsParallel().Any(o => o.Effect.Equals(effectUUID)))
                                        {
                                            LookAtEffects.RemoveWhere(o => o.Effect.Equals(effectUUID));
                                        }
                                        if (!lookAtType.Equals(LookAtType.None))
                                        {
                                            LookAtEffects.Add(new LookAtEffect
                                            {
                                                Effect = effectUUID,
                                                Offset = offset,
                                                Source = Client.Self.AgentID,
                                                Target = targetUUID,
                                                Type = lookAtType
                                            });
                                        }
                                        break;
                                    case ViewerEffectType.POINT:
                                        FieldInfo pointAtTypeInfo = typeof (PointAtType).GetFields(BindingFlags.Public |
                                                                                                   BindingFlags.Static)
                                            .AsParallel().FirstOrDefault(
                                                o =>
                                                    o.Name.Equals(
                                                        wasInput(
                                                            wasKeyValueGet(
                                                                wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.TYPE)),
                                                                message)),
                                                        StringComparison.Ordinal));
                                        PointAtType pointAtType = pointAtTypeInfo != null
                                            ? (PointAtType)
                                                pointAtTypeInfo
                                                    .GetValue(null)
                                            : PointAtType.None;
                                        Client.Self.PointAtEffect(Client.Self.AgentID, targetUUID, offset,
                                            pointAtType, effectUUID);
                                        if (PointAtEffects.AsParallel().Any(o => o.Effect.Equals(effectUUID)))
                                        {
                                            PointAtEffects.RemoveWhere(o => o.Effect.Equals(effectUUID));
                                        }
                                        if (!pointAtType.Equals(PointAtType.None))
                                        {
                                            PointAtEffects.Add(new PointAtEffect
                                            {
                                                Effect = effectUUID,
                                                Offset = offset,
                                                Source = Client.Self.AgentID,
                                                Target = targetUUID,
                                                Type = pointAtType
                                            });
                                        }
                                        break;
                                    case ViewerEffectType.BEAM:
                                    case ViewerEffectType.SPHERE:
                                        Vector3 RGB;
                                        if (
                                            !Vector3.TryParse(
                                                wasInput(
                                                    wasKeyValueGet(
                                                        wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.COLOR)),
                                                        message)),
                                                out RGB))
                                        {
                                            RGB = new Vector3(Client.Settings.DEFAULT_EFFECT_COLOR.R,
                                                Client.Settings.DEFAULT_EFFECT_COLOR.G,
                                                Client.Settings.DEFAULT_EFFECT_COLOR.B);
                                        }
                                        float alpha;
                                        if (!float.TryParse(
                                            wasInput(
                                                wasKeyValueGet(
                                                    wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.ALPHA)),
                                                    message)), out alpha))
                                        {
                                            alpha = Client.Settings.DEFAULT_EFFECT_COLOR.A;
                                        }
                                        float duration;
                                        if (
                                            !float.TryParse(
                                                wasInput(
                                                    wasKeyValueGet(
                                                        wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.DURATION)),
                                                        message)),
                                                out duration))
                                        {
                                            duration = 1;
                                        }
                                        Color4 color = new Color4(RGB.X, RGB.Y, RGB.Z, alpha);
                                        switch (viewerEffectType)
                                        {
                                            case ViewerEffectType.BEAM:
                                                Client.Self.BeamEffect(Client.Self.AgentID, targetUUID, offset,
                                                    color, duration, effectUUID);
                                                lock (BeamEffectsLock)
                                                {
                                                    if (BeamEffects.AsParallel().Any(o => o.Effect.Equals(effectUUID)))
                                                    {
                                                        BeamEffects.RemoveWhere(o => o.Effect.Equals(effectUUID));
                                                    }
                                                    BeamEffects.Add(new BeamEffect
                                                    {
                                                        Effect = effectUUID,
                                                        Source = Client.Self.AgentID,
                                                        Target = targetUUID,
                                                        Color = new Vector3(color.R, color.G, color.B),
                                                        Alpha = color.A,
                                                        Duration = duration,
                                                        Offset = offset,
                                                        Termination = DateTime.Now.AddSeconds(duration)
                                                    });
                                                }
                                                break;
                                            case ViewerEffectType.SPHERE:
                                                Client.Self.SphereEffect(offset, color, duration,
                                                    effectUUID);
                                                lock (SphereEffectsLock)
                                                {
                                                    if (SphereEffects.AsParallel().Any(o => o.Effect.Equals(effectUUID)))
                                                    {
                                                        SphereEffects.RemoveWhere(o => o.Effect.Equals(effectUUID));
                                                    }
                                                    SphereEffects.Add(new SphereEffect
                                                    {
                                                        Color = new Vector3(color.R, color.G, color.B),
                                                        Alpha = color.A,
                                                        Duration = duration,
                                                        Effect = effectUUID,
                                                        Offset = offset,
                                                        Termination = DateTime.Now.AddSeconds(duration)
                                                    });
                                                }
                                                break;
                                        }
                                        break;
                                }
                                break;
                            default:
                                throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.UNKNOWN_EFFECT));
                        }
                        result.Add(wasGetDescriptionFromEnumValue(ResultKeys.DATA), effectUUID.ToString());
                    };
                    break;
                case ScriptKeys.GETVIEWEREFFECTS:
                    execute = () =>
                    {
                        if (
                            !HasCorrodePermission(group,
                                (int) Permissions.PERMISSION_INTERACT))
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.NO_CORRADE_PERMISSIONS));
                        }
                        List<string> csv = new List<string>();
                        object LockObject = new object();
                        ViewerEffectType viewerEffectType = wasGetEnumValueFromDescription<ViewerEffectType>(
                            wasInput(
                                wasKeyValueGet(wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.EFFECT)), message))
                                .ToLowerInvariant());
                        switch (viewerEffectType)
                        {
                            case ViewerEffectType.LOOK:
                                Parallel.ForEach(LookAtEffects, o =>
                                {
                                    lock (LockObject)
                                    {
                                        csv.AddRange(new[]
                                        {wasGetStructureMemberDescription(o, o.Effect), o.Effect.ToString()});
                                        csv.AddRange(new[]
                                        {wasGetStructureMemberDescription(o, o.Source), o.Source.ToString()});
                                        csv.AddRange(new[]
                                        {wasGetStructureMemberDescription(o, o.Target), o.Target.ToString()});
                                        csv.AddRange(new[]
                                        {wasGetStructureMemberDescription(o, o.Offset), o.Offset.ToString()});
                                        csv.AddRange(new[]
                                        {
                                            wasGetStructureMemberDescription(o, o.Type),
                                            Enum.GetName(typeof (LookAtType), o.Type)
                                        });
                                    }
                                });
                                break;
                            case ViewerEffectType.POINT:
                                Parallel.ForEach(PointAtEffects, o =>
                                {
                                    lock (LockObject)
                                    {
                                        csv.AddRange(new[]
                                        {wasGetStructureMemberDescription(o, o.Effect), o.Effect.ToString()});
                                        csv.AddRange(new[]
                                        {wasGetStructureMemberDescription(o, o.Source), o.Source.ToString()});
                                        csv.AddRange(new[]
                                        {wasGetStructureMemberDescription(o, o.Target), o.Target.ToString()});
                                        csv.AddRange(new[]
                                        {wasGetStructureMemberDescription(o, o.Offset), o.Offset.ToString()});
                                        csv.AddRange(new[]
                                        {
                                            wasGetStructureMemberDescription(o, o.Type),
                                            Enum.GetName(typeof (PointAtType), o.Type)
                                        });
                                    }
                                });
                                break;
                            case ViewerEffectType.SPHERE:
                                lock (SphereEffectsLock)
                                {
                                    Parallel.ForEach(SphereEffects, o =>
                                    {
                                        lock (LockObject)
                                        {
                                            csv.AddRange(new[]
                                            {wasGetStructureMemberDescription(o, o.Effect), o.Effect.ToString()});
                                            csv.AddRange(new[]
                                            {wasGetStructureMemberDescription(o, o.Offset), o.Offset.ToString()});
                                            csv.AddRange(new[]
                                            {wasGetStructureMemberDescription(o, o.Color), o.Color.ToString()});
                                            csv.AddRange(new[]
                                            {
                                                wasGetStructureMemberDescription(o, o.Alpha),
                                                o.Alpha.ToString(CultureInfo.InvariantCulture)
                                            });
                                            csv.AddRange(new[]
                                            {
                                                wasGetStructureMemberDescription(o, o.Duration),
                                                o.Duration.ToString(CultureInfo.InvariantCulture)
                                            });
                                            csv.AddRange(new[]
                                            {
                                                wasGetStructureMemberDescription(o, o.Termination),
                                                o.Termination.ToString(CultureInfo.InvariantCulture)
                                            });
                                        }
                                    });
                                }
                                break;
                            case ViewerEffectType.BEAM:
                                lock (BeamEffectsLock)
                                {
                                    Parallel.ForEach(BeamEffects, o =>
                                    {
                                        lock (LockObject)
                                        {
                                            csv.AddRange(new[]
                                            {wasGetStructureMemberDescription(o, o.Effect), o.Effect.ToString()});
                                            csv.AddRange(new[]
                                            {wasGetStructureMemberDescription(o, o.Offset), o.Offset.ToString()});
                                            csv.AddRange(new[]
                                            {wasGetStructureMemberDescription(o, o.Source), o.Source.ToString()});
                                            csv.AddRange(new[]
                                            {wasGetStructureMemberDescription(o, o.Target), o.Target.ToString()});
                                            csv.AddRange(new[]
                                            {wasGetStructureMemberDescription(o, o.Color), o.Color.ToString()});
                                            csv.AddRange(new[]
                                            {
                                                wasGetStructureMemberDescription(o, o.Alpha),
                                                o.Alpha.ToString(CultureInfo.InvariantCulture)
                                            });
                                            csv.AddRange(new[]
                                            {
                                                wasGetStructureMemberDescription(o, o.Duration),
                                                o.Duration.ToString(CultureInfo.InvariantCulture)
                                            });
                                            csv.AddRange(new[]
                                            {
                                                wasGetStructureMemberDescription(o, o.Termination),
                                                o.Termination.ToString(CultureInfo.InvariantCulture)
                                            });
                                        }
                                    });
                                }
                                break;
                            default:
                                throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.UNKNOWN_EFFECT));
                        }
                        if (!csv.Count.Equals(0))
                        {
                            result.Add(wasGetDescriptionFromEnumValue(ResultKeys.DATA),
                                wasEnumerableToCSV(csv));
                        }
                    };
                    break;
                case ScriptKeys.DELETEVIEWEREFFECT:
                    execute = () =>
                    {
                        if (
                            !HasCorrodePermission(group,
                                (int) Permissions.PERMISSION_INTERACT))
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.NO_CORRADE_PERMISSIONS));
                        }
                        UUID effectUUID;
                        if (!UUID.TryParse(wasInput(wasKeyValueGet(
                            wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.ID)), message)), out effectUUID))
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.NO_EFFECT_UUID_PROVIDED));
                        }
                        ViewerEffectType viewerEffectType = wasGetEnumValueFromDescription<ViewerEffectType>(
                            wasInput(
                                wasKeyValueGet(wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.EFFECT)), message))
                                .ToLowerInvariant());
                        switch (viewerEffectType)
                        {
                            case ViewerEffectType.LOOK:
                                LookAtEffect lookAtEffect =
                                    LookAtEffects.AsParallel().FirstOrDefault(o => o.Effect.Equals(effectUUID));
                                if (lookAtEffect.Equals(default(LookAtEffect)))
                                {
                                    throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.EFFECT_NOT_FOUND));
                                }
                                Client.Self.LookAtEffect(Client.Self.AgentID, lookAtEffect.Target, Vector3.Zero,
                                    LookAtType.None, effectUUID);
                                LookAtEffects.Remove(lookAtEffect);
                                break;
                            case ViewerEffectType.POINT:
                                PointAtEffect pointAtEffect =
                                    PointAtEffects.AsParallel().FirstOrDefault(o => o.Effect.Equals(effectUUID));
                                if (pointAtEffect.Equals(default(PointAtEffect)))
                                {
                                    throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.EFFECT_NOT_FOUND));
                                }
                                Client.Self.PointAtEffect(Client.Self.AgentID, pointAtEffect.Target, Vector3.Zero,
                                    PointAtType.None, effectUUID);
                                PointAtEffects.Remove(pointAtEffect);
                                break;
                            default:
                                throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.INVALID_VIEWER_EFFECT));
                        }
                    };
                    break;
                case ScriptKeys.STARTPROPOSAL:
                    execute = () =>
                    {
                        if (!HasCorrodePermission(group, (int) Permissions.PERMISSION_GROUP))
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.NO_CORRADE_PERMISSIONS));
                        }
                        Group configuredGroup =
                            Configuration.GROUPS.AsParallel().FirstOrDefault(
                                o => o.Name.Equals(group, StringComparison.Ordinal));
                        UUID groupUUID = UUID.Zero;
                        switch (!configuredGroup.Equals(default(Group)))
                        {
                            case true:
                                groupUUID = configuredGroup.UUID;
                                break;
                            default:
                                if (!GroupNameToUUID(group, Configuration.SERVICES_TIMEOUT, Configuration.DATA_TIMEOUT,
                                    ref groupUUID))
                                {
                                    throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.GROUP_NOT_FOUND));
                                }
                                break;
                        }
                        if (
                            !AgentInGroup(Client.Self.AgentID, groupUUID, Configuration.SERVICES_TIMEOUT,
                                Configuration.DATA_TIMEOUT))
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.NOT_IN_GROUP));
                        }
                        if (
                            !HasGroupPowers(Client.Self.AgentID, groupUUID, GroupPowers.StartProposal,
                                Configuration.SERVICES_TIMEOUT, Configuration.DATA_TIMEOUT))
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.NO_GROUP_POWER_FOR_COMMAND));
                        }
                        int duration;
                        if (
                            !int.TryParse(
                                wasInput(
                                    wasKeyValueGet(wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.DURATION)),
                                        message)),
                                out duration))
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.INVALID_PROPOSAL_DURATION));
                        }
                        float majority;
                        if (
                            !float.TryParse(
                                wasInput(
                                    wasKeyValueGet(wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.MAJORITY)),
                                        message)),
                                out majority))
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.INVALID_PROPOSAL_MAJORITY));
                        }
                        int quorum;
                        if (
                            !int.TryParse(
                                wasInput(
                                    wasKeyValueGet(wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.QUORUM)), message)),
                                out quorum))
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.INVALID_PROPOSAL_QUORUM));
                        }
                        string text =
                            wasInput(wasKeyValueGet(wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.TEXT)),
                                message));
                        if (string.IsNullOrEmpty(text))
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.INVALID_PROPOSAL_TEXT));
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
                            !HasCorrodePermission(group, (int) Permissions.PERMISSION_MUTE))
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.NO_CORRADE_PERMISSIONS));
                        }
                        UUID targetUUID;
                        if (
                            !UUID.TryParse(
                                wasInput(
                                    wasKeyValueGet(wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.TARGET)), message)),
                                out targetUUID))
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.INVALID_MUTE_TARGET));
                        }
                        switch (
                            wasGetEnumValueFromDescription<Action>(
                                wasInput(
                                    wasKeyValueGet(wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.ACTION)),
                                        message)).ToLowerInvariant()))
                        {
                            case Action.MUTE:
                                FieldInfo muteTypeInfo = typeof (MuteType).GetFields(BindingFlags.Public |
                                                                                     BindingFlags.Static)
                                    .AsParallel().FirstOrDefault(
                                        o =>
                                            o.Name.Equals(
                                                wasInput(
                                                    wasKeyValueGet(
                                                        wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.TYPE)),
                                                        message)),
                                                StringComparison.Ordinal));
                                ManualResetEvent MuteListUpdatedEvent = new ManualResetEvent(false);
                                EventHandler<EventArgs> MuteListUpdatedEventHandler =
                                    (sender, args) => MuteListUpdatedEvent.Set();
                                lock (ClientInstanceSelfLock)
                                {
                                    Client.Self.MuteListUpdated += MuteListUpdatedEventHandler;
                                    Client.Self.UpdateMuteListEntry(muteTypeInfo != null
                                        ? (MuteType)
                                            muteTypeInfo
                                                .GetValue(null)
                                        : MuteType.ByName, targetUUID,
                                        wasInput(
                                            wasKeyValueGet(wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.NAME)),
                                                message)));
                                    if (!MuteListUpdatedEvent.WaitOne(Configuration.SERVICES_TIMEOUT, false))
                                    {
                                        Client.Self.MuteListUpdated -= MuteListUpdatedEventHandler;
                                        throw new Exception(
                                            wasGetDescriptionFromEnumValue(ScriptError.TIMEOUT_UPDATING_MUTE_LIST));
                                    }
                                    Client.Self.MuteListUpdated -= MuteListUpdatedEventHandler;
                                }
                                break;
                            case Action.UNMUTE:
                                Client.Self.RemoveMuteListEntry(targetUUID,
                                    wasInput(
                                        wasKeyValueGet(wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.NAME)),
                                            message)));
                                break;
                            default:
                                throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.UNKNOWN_ACTION));
                        }
                    };
                    break;
                case ScriptKeys.GETMUTES:
                    execute = () =>
                    {
                        if (!HasCorrodePermission(group, (int) Permissions.PERMISSION_MUTE))
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.NO_CORRADE_PERMISSIONS));
                        }
                        List<string> data = new List<string>(Client.Self.MuteList.Copy().AsParallel().Select(o => new[]
                        {
                            o.Value.Name,
                            o.Value.ID.ToString()
                        }).SelectMany(o => o));
                        if (!data.Count.Equals(0))
                        {
                            result.Add(wasGetDescriptionFromEnumValue(ResultKeys.DATA),
                                wasEnumerableToCSV(data));
                        }
                    };
                    break;
                case ScriptKeys.DATABASE:
                    execute = () =>
                    {
                        if (!HasCorrodePermission(group, (int) Permissions.PERMISSION_DATABASE))
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.NO_CORRADE_PERMISSIONS));
                        }
                        Group configuredGroup =
                            Configuration.GROUPS.AsParallel().FirstOrDefault(
                                o => o.Name.Equals(group, StringComparison.Ordinal));
                        switch (!configuredGroup.Equals(default(Group)))
                        {
                            case false:
                                UUID groupUUID = UUID.Zero;
                                if (!GroupNameToUUID(group, Configuration.SERVICES_TIMEOUT, Configuration.DATA_TIMEOUT,
                                    ref groupUUID))
                                {
                                    throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.GROUP_NOT_FOUND));
                                }
                                configuredGroup =
                                    Configuration.GROUPS.AsParallel().FirstOrDefault(o => o.UUID.Equals(groupUUID));
                                if (configuredGroup.Equals(default(Group)))
                                    throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.GROUP_NOT_FOUND));
                                break;
                        }
                        if (string.IsNullOrEmpty(configuredGroup.DatabaseFile))
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.NO_DATABASE_FILE_CONFIGURED));
                        }
                        if (!File.Exists(configuredGroup.DatabaseFile))
                        {
                            // create the file and close it
                            File.Create(configuredGroup.DatabaseFile).Close();
                        }
                        switch (
                            wasGetEnumValueFromDescription<Action>(
                                wasInput(
                                    wasKeyValueGet(wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.ACTION)),
                                        message)).ToLowerInvariant()))
                        {
                            case Action.GET:
                                string databaseGetkey =
                                    wasInput(
                                        wasKeyValueGet(wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.KEY)),
                                            message));
                                if (string.IsNullOrEmpty(databaseGetkey))
                                {
                                    throw new Exception(
                                        wasGetDescriptionFromEnumValue(ScriptError.NO_DATABASE_KEY_SPECIFIED));
                                }
                                lock (DatabaseFileLock)
                                {
                                    if (!DatabaseLocks.ContainsKey(configuredGroup.Name))
                                    {
                                        DatabaseLocks.Add(configuredGroup.Name, new object());
                                    }
                                }
                                lock (DatabaseLocks[configuredGroup.Name])
                                {
                                    string databaseGetValue = wasKeyValueGet(databaseGetkey,
                                        File.ReadAllText(configuredGroup.DatabaseFile));
                                    if (!string.IsNullOrEmpty(databaseGetValue))
                                    {
                                        result.Add(databaseGetkey,
                                            wasInput(databaseGetValue));
                                    }
                                }
                                lock (DatabaseFileLock)
                                {
                                    if (DatabaseLocks.ContainsKey(configuredGroup.Name))
                                    {
                                        DatabaseLocks.Remove(configuredGroup.Name);
                                    }
                                }
                                break;
                            case Action.SET:
                                string databaseSetKey =
                                    wasInput(
                                        wasKeyValueGet(wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.KEY)),
                                            message));
                                if (string.IsNullOrEmpty(databaseSetKey))
                                {
                                    throw new Exception(
                                        wasGetDescriptionFromEnumValue(ScriptError.NO_DATABASE_KEY_SPECIFIED));
                                }
                                string databaseSetValue =
                                    wasInput(
                                        wasKeyValueGet(wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.VALUE)),
                                            message));
                                if (string.IsNullOrEmpty(databaseSetValue))
                                {
                                    throw new Exception(
                                        wasGetDescriptionFromEnumValue(ScriptError.NO_DATABASE_VALUE_SPECIFIED));
                                }
                                lock (DatabaseFileLock)
                                {
                                    if (!DatabaseLocks.ContainsKey(configuredGroup.Name))
                                    {
                                        DatabaseLocks.Add(configuredGroup.Name, new object());
                                    }
                                }
                                lock (DatabaseLocks[configuredGroup.Name])
                                {
                                    string contents = File.ReadAllText(configuredGroup.DatabaseFile);
                                    using (
                                        StreamWriter recreateDatabase = new StreamWriter(configuredGroup.DatabaseFile,
                                            false))
                                    {
                                        recreateDatabase.Write(wasKeyValueSet(databaseSetKey,
                                            databaseSetValue, contents));
                                        recreateDatabase.Flush();
                                        //recreateDatabase.Close();
                                    }
                                }
                                lock (DatabaseFileLock)
                                {
                                    if (DatabaseLocks.ContainsKey(configuredGroup.Name))
                                    {
                                        DatabaseLocks.Remove(configuredGroup.Name);
                                    }
                                }
                                break;
                            case Action.DELETE:
                                string databaseDeleteKey =
                                    wasInput(
                                        wasKeyValueGet(wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.KEY)),
                                            message));
                                if (string.IsNullOrEmpty(databaseDeleteKey))
                                {
                                    throw new Exception(
                                        wasGetDescriptionFromEnumValue(ScriptError.NO_DATABASE_KEY_SPECIFIED));
                                }
                                lock (DatabaseFileLock)
                                {
                                    if (!DatabaseLocks.ContainsKey(configuredGroup.Name))
                                    {
                                        DatabaseLocks.Add(configuredGroup.Name, new object());
                                    }
                                }
                                lock (DatabaseLocks[configuredGroup.Name])
                                {
                                    string contents = File.ReadAllText(configuredGroup.DatabaseFile);
                                    using (
                                        StreamWriter recreateDatabase = new StreamWriter(configuredGroup.DatabaseFile,
                                            false))
                                    {
                                        recreateDatabase.Write(wasKeyValueDelete(databaseDeleteKey, contents));
                                        recreateDatabase.Flush();
                                        //recreateDatabase.Close();
                                    }
                                }
                                lock (DatabaseFileLock)
                                {
                                    if (DatabaseLocks.ContainsKey(configuredGroup.Name))
                                    {
                                        DatabaseLocks.Remove(configuredGroup.Name);
                                    }
                                }
                                break;
                            default:
                                throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.UNKNOWN_DATABASE_ACTION));
                        }
                    };
                    break;
                case ScriptKeys.NOTIFY:
                    execute = () =>
                    {
                        if (!HasCorrodePermission(group, (int) Permissions.PERMISSION_NOTIFICATIONS))
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.NO_CORRADE_PERMISSIONS));
                        }
                        Group configuredGroup =
                            Configuration.GROUPS.AsParallel().FirstOrDefault(
                                o => o.Name.Equals(group, StringComparison.Ordinal));
                        switch (!configuredGroup.Equals(default(Group)))
                        {
                            case false:
                                UUID groupUUID = UUID.Zero;
                                if (!GroupNameToUUID(group, Configuration.SERVICES_TIMEOUT, Configuration.DATA_TIMEOUT,
                                    ref groupUUID))
                                {
                                    throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.GROUP_NOT_FOUND));
                                }
                                configuredGroup =
                                    Configuration.GROUPS.AsParallel().FirstOrDefault(o => o.UUID.Equals(groupUUID));
                                if (configuredGroup.Equals(default(Group)))
                                    throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.GROUP_NOT_FOUND));
                                break;
                        }
                        string url = wasInput(
                            wasKeyValueGet(wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.URL)),
                                message));
                        string notificationTypes =
                            wasInput(
                                wasKeyValueGet(wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.TYPE)),
                                    message))
                                .ToLowerInvariant();
                        switch (
                            wasGetEnumValueFromDescription<Action>(
                                wasInput(
                                    wasKeyValueGet(wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.ACTION)),
                                        message)).ToLowerInvariant()))
                        {
                            case Action.ADD:
                                if (string.IsNullOrEmpty(url))
                                {
                                    throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.INVALID_URL_PROVIDED));
                                }
                                Uri notifyURL;
                                if (!Uri.TryCreate(url, UriKind.Absolute, out notifyURL))
                                {
                                    throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.INVALID_URL_PROVIDED));
                                }
                                if (string.IsNullOrEmpty(notificationTypes))
                                {
                                    throw new Exception(
                                        wasGetDescriptionFromEnumValue(ScriptError.INVALID_NOTIFICATION_TYPES));
                                }
                                Notification notification;
                                lock (GroupNotificationsLock)
                                {
                                    notification =
                                        GroupNotifications.AsParallel().FirstOrDefault(
                                            o => o.GroupName.Equals(configuredGroup.Name, StringComparison.Ordinal));
                                }
                                if (notification.Equals(default(Notification)))
                                {
                                    notification = new Notification
                                    {
                                        GroupName = configuredGroup.Name,
                                        NotificationMask = 0,
                                        NotificationDestination =
                                            new SerializableDictionary<Notifications, HashSet<string>>()
                                    };
                                }
                                Parallel.ForEach(wasCSVToEnumerable(
                                    notificationTypes),
                                    o =>
                                    {
                                        uint notificationValue = (uint) wasGetEnumValueFromDescription<Notifications>(o);
                                        if (!GroupHasNotification(configuredGroup.Name, notificationValue))
                                        {
                                            throw new Exception(
                                                wasGetDescriptionFromEnumValue(ScriptError.NOTIFICATION_NOT_ALLOWED));
                                        }
                                        notification.NotificationMask |= notificationValue;
                                        switch (
                                            !notification.NotificationDestination.ContainsKey(
                                                (Notifications) notificationValue))
                                        {
                                            case true:
                                                notification.NotificationDestination.Add(
                                                    (Notifications) notificationValue, new HashSet<string> {url});
                                                break;
                                            default:
                                                // notification destination is already there
                                                if (notification.NotificationDestination[
                                                    (Notifications) notificationValue].Contains(url)) break;
                                                notification.NotificationDestination[(Notifications) notificationValue]
                                                    .Add(url);
                                                break;
                                        }
                                    });
                                lock (GroupNotificationsLock)
                                {
                                    // Replace notification.
                                    GroupNotifications.RemoveWhere(
                                        o => o.GroupName.Equals(configuredGroup.Name, StringComparison.Ordinal));
                                    GroupNotifications.Add(notification);
                                }
                                break;
                            case Action.REMOVE:
                                HashSet<Notification> groupNotifications = new HashSet<Notification>();
                                lock (GroupNotificationsLock)
                                {
                                    Parallel.ForEach(GroupNotifications, o =>
                                    {
                                        if ((!wasCSVToEnumerable(notificationTypes)
                                            .AsParallel()
                                            .Any(p => !(o.NotificationMask &
                                                        (uint) wasGetEnumValueFromDescription<Notifications>(p))
                                                .Equals(0)) &&
                                             !o.NotificationDestination.Values.Any(p => p.Contains(url))) ||
                                            !o.GroupName.Equals(configuredGroup.Name, StringComparison.Ordinal))
                                        {
                                            groupNotifications.Add(o);
                                            return;
                                        }
                                        SerializableDictionary<Notifications, HashSet<string>>
                                            notificationDestination =
                                                new SerializableDictionary<Notifications, HashSet<string>>();
                                        Parallel.ForEach(o.NotificationDestination, p =>
                                        {
                                            switch (!wasCSVToEnumerable(notificationTypes)
                                                .AsParallel()
                                                .Any(
                                                    q =>
                                                        wasGetEnumValueFromDescription<Notifications>(q)
                                                            .Equals(p.Key)))
                                            {
                                                case true:
                                                    notificationDestination.Add(p.Key, p.Value);
                                                    break;
                                                default:
                                                    HashSet<string> URLs =
                                                        new HashSet<string>(p.Value.Where(q => !q.Equals(url)));
                                                    if (URLs.Count.Equals(0)) return;
                                                    notificationDestination.Add(p.Key, URLs);
                                                    break;
                                            }
                                        });
                                        groupNotifications.Add(new Notification
                                        {
                                            GroupName = o.GroupName,
                                            NotificationMask =
                                                notificationDestination.Keys.Cast<uint>().Aggregate((p, q) => p |= q),
                                            NotificationDestination = notificationDestination
                                        });
                                    });
                                    // Now assign the new notifications.
                                    GroupNotifications = groupNotifications;
                                }
                                break;
                            case Action.LIST:
                                // If the group has no installed notifications, bail
                                List<string> csv = new List<string>();
                                object LockObject = new object();
                                lock (GroupNotificationsLock)
                                {
                                    Notification groupNotification =
                                        GroupNotifications.AsParallel().FirstOrDefault(
                                            o => o.GroupName.Equals(configuredGroup.Name, StringComparison.Ordinal));
                                    if (!groupNotification.Equals(default(Notification)))
                                    {
                                        Parallel.ForEach(wasGetEnumDescriptions<Notifications>(), o =>
                                        {
                                            if ((groupNotification.NotificationMask &
                                                 (uint) wasGetEnumValueFromDescription<Notifications>(o)).Equals(0))
                                                return;
                                            lock (LockObject)
                                            {
                                                csv.Add(o);
                                                csv.AddRange(groupNotification.NotificationDestination[
                                                    wasGetEnumValueFromDescription<Notifications>(o)]);
                                            }
                                        });
                                    }
                                }
                                if (!csv.Count.Equals(0))
                                {
                                    result.Add(wasGetDescriptionFromEnumValue(ResultKeys.DATA),
                                        wasEnumerableToCSV(csv));
                                }
                                break;
                            case Action.CLEAR:
                                lock (GroupNotificationsLock)
                                {
                                    GroupNotifications.RemoveWhere(
                                        o => o.GroupName.Equals(configuredGroup.Name, StringComparison.Ordinal));
                                }
                                break;
                            default:
                                throw new Exception(
                                    wasGetDescriptionFromEnumValue(ScriptError.UNKNOWN_ACTION));
                        }
                        // Now save the state.
                        lock (GroupNotificationsLock)
                        {
                            SaveNotificationState.Invoke();
                        }
                    };
                    break;
                case ScriptKeys.REPLYTOTELEPORTLURE:
                    execute = () =>
                    {
                        if (!HasCorrodePermission(group, (int) Permissions.PERMISSION_MOVEMENT))
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.NO_CORRADE_PERMISSIONS));
                        }
                        UUID agentUUID;
                        if (
                            !UUID.TryParse(
                                wasInput(wasKeyValueGet(
                                    wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.AGENT)), message)),
                                out agentUUID) && !AgentNameToUUID(
                                    wasInput(
                                        wasKeyValueGet(wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.FIRSTNAME)),
                                            message)),
                                    wasInput(
                                        wasKeyValueGet(wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.LASTNAME)),
                                            message)),
                                    Configuration.SERVICES_TIMEOUT, Configuration.DATA_TIMEOUT, ref agentUUID))
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.AGENT_NOT_FOUND));
                        }
                        UUID sessionUUID;
                        if (
                            !UUID.TryParse(
                                wasInput(
                                    wasKeyValueGet(wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.SESSION)),
                                        message)),
                                out sessionUUID))
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.NO_SESSION_SPECIFIED));
                        }
                        Client.Self.TeleportLureRespond(agentUUID, sessionUUID, wasGetEnumValueFromDescription<Action>(
                            wasInput(wasKeyValueGet(wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.ACTION)),
                                message))
                                .ToLowerInvariant()).Equals(Action.ACCEPT));
                    };
                    break;
                case ScriptKeys.GETTELEPORTLURES:
                    execute = () =>
                    {
                        if (!HasCorrodePermission(group, (int) Permissions.PERMISSION_MOVEMENT))
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.NO_CORRADE_PERMISSIONS));
                        }
                        List<string> csv = new List<string>();
                        object LockObject = new object();
                        lock (TeleportLureLock)
                        {
                            Parallel.ForEach(TeleportLures, o =>
                            {
                                lock (LockObject)
                                {
                                    csv.AddRange(new[]
                                    {wasGetStructureMemberDescription(o.Agent, o.Agent.FirstName), o.Agent.FirstName});
                                    csv.AddRange(new[]
                                    {wasGetStructureMemberDescription(o.Agent, o.Agent.LastName), o.Agent.LastName});
                                    csv.AddRange(new[]
                                    {wasGetStructureMemberDescription(o.Agent, o.Agent.UUID), o.Agent.UUID.ToString()});
                                    csv.AddRange(new[]
                                    {wasGetStructureMemberDescription(o, o.Session), o.Session.ToString()});
                                }
                            });
                        }
                        if (!csv.Count.Equals(0))
                        {
                            result.Add(wasGetDescriptionFromEnumValue(ResultKeys.DATA),
                                wasEnumerableToCSV(csv));
                        }
                    };
                    break;
                case ScriptKeys.REPLYTOSCRIPTPERMISSIONREQUEST:
                    execute = () =>
                    {
                        if (!HasCorrodePermission(group, (int) Permissions.PERMISSION_INTERACT))
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.NO_CORRADE_PERMISSIONS));
                        }
                        UUID itemUUID;
                        if (
                            !UUID.TryParse(
                                wasInput(wasKeyValueGet(
                                    wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.ITEM)), message)),
                                out itemUUID))
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.NO_ITEM_SPECIFIED));
                        }
                        UUID taskUUID;
                        if (
                            !UUID.TryParse(
                                wasInput(wasKeyValueGet(
                                    wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.TASK)), message)),
                                out taskUUID))
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.NO_TASK_SPECIFIED));
                        }
                        int permissionMask = 0;
                        Parallel.ForEach(wasCSVToEnumerable(
                            wasInput(
                                wasKeyValueGet(wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.PERMISSIONS)),
                                    message))),
                            o =>
                                Parallel.ForEach(
                                    typeof (ScriptPermission).GetFields(BindingFlags.Public | BindingFlags.Static)
                                        .AsParallel().Where(p => p.Name.Equals(o, StringComparison.Ordinal)),
                                    q => { permissionMask |= ((int) q.GetValue(null)); }));
                        Simulator simulator = Client.Network.Simulators.FirstOrDefault(
                            o => o.Name.Equals(wasInput(
                                wasKeyValueGet(wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.REGION)),
                                    message)), StringComparison.InvariantCultureIgnoreCase));
                        if (simulator == null)
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.REGION_NOT_FOUND));
                        }
                        Client.Self.ScriptQuestionReply(simulator, itemUUID, taskUUID,
                            (ScriptPermission) permissionMask);
                    };
                    break;
                case ScriptKeys.GETSCRIPTPERMISSIONREQUESTS:
                    execute = () =>
                    {
                        if (!HasCorrodePermission(group, (int) Permissions.PERMISSION_INTERACT))
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.NO_CORRADE_PERMISSIONS));
                        }
                        List<string> csv = new List<string>();
                        object LockObject = new object();
                        lock (ScriptPermissionRequestLock)
                        {
                            Parallel.ForEach(ScriptPermissionRequests, o =>
                            {
                                lock (LockObject)
                                {
                                    csv.AddRange(new[] {wasGetStructureMemberDescription(o, o.Name), o.Name});
                                    csv.AddRange(new[]
                                    {wasGetStructureMemberDescription(o.Agent, o.Agent.FirstName), o.Agent.FirstName});
                                    csv.AddRange(new[]
                                    {wasGetStructureMemberDescription(o.Agent, o.Agent.LastName), o.Agent.LastName});
                                    csv.AddRange(new[]
                                    {wasGetStructureMemberDescription(o.Agent, o.Agent.UUID), o.Agent.UUID.ToString()});
                                    csv.AddRange(new[] {wasGetStructureMemberDescription(o, o.Item), o.Item.ToString()});
                                    csv.AddRange(new[] {wasGetStructureMemberDescription(o, o.Task), o.Task.ToString()});
                                    csv.Add(wasGetStructureMemberDescription(o, o.Permission));
                                    csv.AddRange(typeof (ScriptPermission).GetFields(BindingFlags.Public |
                                                                                     BindingFlags.Static)
                                        .AsParallel().Where(
                                            p =>
                                                !(((int) p.GetValue(null) &
                                                   (int) o.Permission)).Equals(0))
                                        .Select(p => p.Name).ToArray());
                                    csv.AddRange(new[] {wasGetStructureMemberDescription(o, o.Region), o.Region});
                                }
                            });
                        }
                        if (!csv.Count.Equals(0))
                        {
                            result.Add(wasGetDescriptionFromEnumValue(ResultKeys.DATA),
                                wasEnumerableToCSV(csv));
                        }
                    };
                    break;
                case ScriptKeys.REPLYTOSCRIPTDIALOG:
                    execute = () =>
                    {
                        if (!HasCorrodePermission(group, (int) Permissions.PERMISSION_INTERACT))
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.NO_CORRADE_PERMISSIONS));
                        }
                        int channel;
                        if (
                            !int.TryParse(
                                wasInput(
                                    wasKeyValueGet(wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.CHANNEL)),
                                        message)),
                                out channel))
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.NO_CHANNEL_SPECIFIED));
                        }
                        int index;
                        if (
                            !int.TryParse(
                                wasInput(wasKeyValueGet(
                                    wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.INDEX)), message)),
                                out index))
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.NO_BUTTON_INDEX_SPECIFIED));
                        }
                        string label =
                            wasInput(wasKeyValueGet(wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.BUTTON)),
                                message));
                        if (string.IsNullOrEmpty(label))
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.NO_BUTTON_SPECIFIED));
                        }
                        UUID itemUUID;
                        if (
                            !UUID.TryParse(
                                wasInput(wasKeyValueGet(
                                    wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.ITEM)), message)),
                                out itemUUID))
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.NO_ITEM_SPECIFIED));
                        }
                        Client.Self.ReplyToScriptDialog(channel, index, label, itemUUID);
                    };
                    break;
                case ScriptKeys.GETSCRIPTDIALOGS:
                    execute = () =>
                    {
                        if (!HasCorrodePermission(group, (int) Permissions.PERMISSION_INTERACT))
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.NO_CORRADE_PERMISSIONS));
                        }
                        List<string> csv = new List<string>();
                        object LockObject = new object();
                        lock (ScriptDialogLock)
                        {
                            Parallel.ForEach(ScriptDialogs, o =>
                            {
                                lock (LockObject)
                                {
                                    csv.AddRange(new[] {wasGetStructureMemberDescription(o, o.Message), o.Message});
                                    csv.AddRange(new[]
                                    {wasGetStructureMemberDescription(o.Agent, o.Agent.FirstName), o.Agent.FirstName});
                                    csv.AddRange(new[]
                                    {wasGetStructureMemberDescription(o.Agent, o.Agent.LastName), o.Agent.LastName});
                                    csv.AddRange(new[]
                                    {wasGetStructureMemberDescription(o.Agent, o.Agent.UUID), o.Agent.UUID.ToString()});
                                    csv.AddRange(new[]
                                    {
                                        wasGetStructureMemberDescription(o, o.Channel),
                                        o.Channel.ToString(CultureInfo.InvariantCulture)
                                    });
                                    csv.AddRange(new[] {wasGetStructureMemberDescription(o, o.Name), o.Name});
                                    csv.AddRange(new[] {wasGetStructureMemberDescription(o, o.Item), o.Item.ToString()});
                                    csv.Add(wasGetStructureMemberDescription(o, o.Button));
                                    csv.AddRange(o.Button.ToArray());
                                }
                            });
                        }
                        if (!csv.Count.Equals(0))
                        {
                            result.Add(wasGetDescriptionFromEnumValue(ResultKeys.DATA),
                                wasEnumerableToCSV(csv));
                        }
                    };
                    break;
                case ScriptKeys.ANIMATION:
                    execute = () =>
                    {
                        if (!HasCorrodePermission(group, (int) Permissions.PERMISSION_GROOMING))
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.NO_CORRADE_PERMISSIONS));
                        }
                        string item =
                            wasInput(wasKeyValueGet(wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.ITEM)),
                                message));
                        if (string.IsNullOrEmpty(item))
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.NO_ITEM_SPECIFIED));
                        }
                        UUID itemUUID;
                        if (!UUID.TryParse(item, out itemUUID))
                        {
                            InventoryBase inventoryBaseItem =
                                FindInventory<InventoryBase>(Client.Inventory.Store.RootNode, item
                                    ).FirstOrDefault();
                            if (inventoryBaseItem == null)
                            {
                                throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.INVENTORY_ITEM_NOT_FOUND));
                            }
                            InventoryItem inventoryItem = inventoryBaseItem as InventoryItem;
                            if (inventoryItem == null)
                            {
                                throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.INVENTORY_ITEM_NOT_FOUND));
                            }
                            itemUUID = inventoryItem.AssetUUID;
                        }
                        switch (
                            wasGetEnumValueFromDescription<Action>(
                                wasInput(
                                    wasKeyValueGet(wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.ACTION)),
                                        message)).ToLowerInvariant()))
                        {
                            case Action.START:
                                Client.Self.AnimationStart(itemUUID, true);
                                break;
                            case Action.STOP:
                                Client.Self.AnimationStop(itemUUID, true);
                                break;
                            default:
                                throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.UNKNOWN_ANIMATION_ACTION));
                        }
                    };
                    break;
                case ScriptKeys.PLAYGESTURE:
                    execute = () =>
                    {
                        if (!HasCorrodePermission(group, (int) Permissions.PERMISSION_GROOMING))
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.NO_CORRADE_PERMISSIONS));
                        }
                        string item =
                            wasInput(wasKeyValueGet(wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.ITEM)),
                                message));
                        if (string.IsNullOrEmpty(item))
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.NO_ITEM_SPECIFIED));
                        }
                        UUID itemUUID;
                        if (!UUID.TryParse(item, out itemUUID))
                        {
                            InventoryBase inventoryBaseItem =
                                FindInventory<InventoryBase>(Client.Inventory.Store.RootNode, item
                                    ).FirstOrDefault();
                            if (inventoryBaseItem == null)
                            {
                                throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.INVENTORY_ITEM_NOT_FOUND));
                            }
                            itemUUID = inventoryBaseItem.UUID;
                        }
                        Client.Self.PlayGesture(itemUUID);
                    };
                    break;
                case ScriptKeys.GETANIMATIONS:
                    execute = () =>
                    {
                        if (!HasCorrodePermission(group, (int) Permissions.PERMISSION_GROOMING))
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.NO_CORRADE_PERMISSIONS));
                        }
                        List<string> csv = new List<string>();
                        Client.Self.SignaledAnimations.ForEach(
                            o =>
                                csv.AddRange(new List<string>
                                {
                                    o.Key.ToString(),
                                    o.Value.ToString(CultureInfo.InvariantCulture)
                                }));
                        if (!csv.Count.Equals(0))
                        {
                            result.Add(wasGetDescriptionFromEnumValue(ResultKeys.DATA),
                                wasEnumerableToCSV(csv));
                        }
                    };
                    break;
                case ScriptKeys.RESTARTREGION:
                    execute = () =>
                    {
                        if (!HasCorrodePermission(group, (int) Permissions.PERMISSION_LAND))
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.NO_CORRADE_PERMISSIONS));
                        }
                        if (!Client.Network.CurrentSim.IsEstateManager)
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.NO_LAND_RIGHTS));
                        }
                        int delay;
                        if (
                            !int.TryParse(
                                wasInput(wasKeyValueGet(
                                    wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.DELAY)), message))
                                    .ToLowerInvariant(), out delay))
                        {
                            delay = LINDEN_CONSTANTS.ESTATE.REGION_RESTART_DELAY;
                        }
                        switch (
                            wasGetEnumValueFromDescription<Action>(
                                wasInput(
                                    wasKeyValueGet(wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.ACTION)),
                                        message)).ToLowerInvariant()))
                        {
                            case Action.RESTART:
                                // Manually override Client.Estate.RestartRegion();
                                Client.Estate.EstateOwnerMessage(
                                    LINDEN_CONSTANTS.ESTATE.MESSAGES.REGION_RESTART_MESSAGE,
                                    delay.ToString(CultureInfo.InvariantCulture));
                                break;
                            case Action.CANCEL:
                                Client.Estate.CancelRestart();
                                break;
                            default:
                                throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.UNKNOWN_RESTART_ACTION));
                        }
                    };
                    break;
                case ScriptKeys.SETREGIONDEBUG:
                    execute = () =>
                    {
                        if (!HasCorrodePermission(group, (int) Permissions.PERMISSION_LAND))
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.NO_CORRADE_PERMISSIONS));
                        }
                        if (!Client.Network.CurrentSim.IsEstateManager)
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.NO_LAND_RIGHTS));
                        }
                        bool scripts;
                        if (
                            !bool.TryParse(
                                wasInput(
                                    wasKeyValueGet(wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.SCRIPTS)),
                                        message))
                                    .ToLowerInvariant(), out scripts))
                        {
                            scripts = false;
                        }
                        bool collisions;
                        if (
                            !bool.TryParse(
                                wasInput(
                                    wasKeyValueGet(wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.COLLISIONS)),
                                        message))
                                    .ToLowerInvariant(), out collisions))
                        {
                            collisions = false;
                        }
                        bool physics;
                        if (
                            !bool.TryParse(
                                wasInput(
                                    wasKeyValueGet(wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.PHYSICS)),
                                        message))
                                    .ToLowerInvariant(), out physics))
                        {
                            physics = false;
                        }
                        Client.Estate.SetRegionDebug(!scripts, !collisions, !physics);
                    };
                    break;
                case ScriptKeys.GETREGIONTOP:
                    execute = () =>
                    {
                        if (!HasCorrodePermission(group, (int) Permissions.PERMISSION_LAND))
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.NO_CORRADE_PERMISSIONS));
                        }
                        if (!Client.Network.CurrentSim.IsEstateManager)
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.NO_LAND_RIGHTS));
                        }
                        Dictionary<UUID, EstateTask> topTasks = new Dictionary<UUID, EstateTask>();
                        switch (
                            wasGetEnumValueFromDescription<Type>(
                                wasInput(wasKeyValueGet(
                                    wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.TYPE)), message))
                                    .ToLowerInvariant()))
                        {
                            case Type.SCRIPTS:
                                ManualResetEvent TopScriptsReplyEvent = new ManualResetEvent(false);
                                EventHandler<TopScriptsReplyEventArgs> TopScriptsReplyEventHandler = (sender, args) =>
                                {
                                    topTasks =
                                        args.Tasks.OrderByDescending(o => o.Value.Score)
                                            .ToDictionary(o => o.Key, o => o.Value);
                                    TopScriptsReplyEvent.Set();
                                };
                                lock (ClientInstanceEstateLock)
                                {
                                    Client.Estate.TopScriptsReply += TopScriptsReplyEventHandler;
                                    Client.Estate.RequestTopScripts();
                                    if (!TopScriptsReplyEvent.WaitOne(Configuration.SERVICES_TIMEOUT, false))
                                    {
                                        Client.Estate.TopScriptsReply -= TopScriptsReplyEventHandler;
                                        throw new Exception(
                                            wasGetDescriptionFromEnumValue(ScriptError.TIMEOUT_GETTING_TOP_SCRIPTS));
                                    }
                                    Client.Estate.TopScriptsReply -= TopScriptsReplyEventHandler;
                                }
                                break;
                            case Type.COLLIDERS:
                                ManualResetEvent TopCollidersReplyEvent = new ManualResetEvent(false);
                                EventHandler<TopCollidersReplyEventArgs> TopCollidersReplyEventHandler =
                                    (sender, args) =>
                                    {
                                        topTasks =
                                            args.Tasks.OrderByDescending(o => o.Value.Score)
                                                .ToDictionary(o => o.Key, o => o.Value);
                                        TopCollidersReplyEvent.Set();
                                    };
                                lock (ClientInstanceEstateLock)
                                {
                                    Client.Estate.TopCollidersReply += TopCollidersReplyEventHandler;
                                    Client.Estate.RequestTopScripts();
                                    if (!TopCollidersReplyEvent.WaitOne(Configuration.SERVICES_TIMEOUT, false))
                                    {
                                        Client.Estate.TopCollidersReply -= TopCollidersReplyEventHandler;
                                        throw new Exception(
                                            wasGetDescriptionFromEnumValue(ScriptError.TIMEOUT_GETTING_TOP_SCRIPTS));
                                    }
                                    Client.Estate.TopCollidersReply -= TopCollidersReplyEventHandler;
                                }
                                break;
                            default:
                                throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.UNKNOWN_TOP_TYPE));
                        }
                        int amount;
                        if (
                            !int.TryParse(
                                wasInput(
                                    wasKeyValueGet(wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.AMOUNT)), message)),
                                out amount))
                        {
                            amount = topTasks.Count;
                        }
                        List<string> data = new List<string>(topTasks.Take(amount).Select(o => new[]
                        {
                            o.Value.Score.ToString(CultureInfo.InvariantCulture),
                            o.Value.TaskName,
                            o.Key.ToString(),
                            o.Value.OwnerName,
                            o.Value.Position.ToString()
                        }).SelectMany(o => o));
                        if (!data.Count.Equals(0))
                        {
                            result.Add(wasGetDescriptionFromEnumValue(ResultKeys.DATA),
                                wasEnumerableToCSV(data));
                        }
                    };
                    break;
                case ScriptKeys.SETESTATELIST:
                    execute = () =>
                    {
                        if (!HasCorrodePermission(group, (int) Permissions.PERMISSION_LAND))
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.NO_CORRADE_PERMISSIONS));
                        }
                        if (!Client.Network.CurrentSim.IsEstateManager)
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.NO_LAND_RIGHTS));
                        }
                        bool allEstates;
                        if (
                            !bool.TryParse(
                                wasInput(wasKeyValueGet(wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.ALL)),
                                    message)),
                                out allEstates))
                        {
                            allEstates = false;
                        }
                        UUID targetUUID;
                        switch (
                            wasGetEnumValueFromDescription<Type>(
                                wasInput(wasKeyValueGet(
                                    wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.TYPE)), message))
                                    .ToLowerInvariant()))
                        {
                            case Type.BAN:
                                if (
                                    !UUID.TryParse(
                                        wasInput(
                                            wasKeyValueGet(wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.AGENT)),
                                                message)), out targetUUID) && !AgentNameToUUID(
                                                    wasInput(
                                                        wasKeyValueGet(
                                                            wasOutput(
                                                                wasGetDescriptionFromEnumValue(ScriptKeys.FIRSTNAME)),
                                                            message)),
                                                    wasInput(
                                                        wasKeyValueGet(
                                                            wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.LASTNAME)),
                                                            message)),
                                                    Configuration.SERVICES_TIMEOUT, Configuration.DATA_TIMEOUT,
                                                    ref targetUUID))
                                {
                                    throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.AGENT_NOT_FOUND));
                                }
                                switch (
                                    wasGetEnumValueFromDescription<Action>(
                                        wasInput(
                                            wasKeyValueGet(
                                                wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.ACTION)), message))
                                            .ToLowerInvariant()))
                                {
                                    case Action.ADD:
                                        Client.Estate.BanUser(targetUUID, allEstates);
                                        break;
                                    case Action.REMOVE:
                                        Client.Estate.UnbanUser(targetUUID, allEstates);
                                        break;
                                    default:
                                        throw new Exception(
                                            wasGetDescriptionFromEnumValue(ScriptError.UNKNOWN_ESTATE_LIST_ACTION));
                                }
                                break;
                            case Type.GROUP:
                                if (
                                    !UUID.TryParse(
                                        wasInput(
                                            wasKeyValueGet(wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.TARGET)),
                                                message)),
                                        out targetUUID) && !GroupNameToUUID(
                                            wasInput(
                                                wasKeyValueGet(
                                                    wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.TARGET)),
                                                    message)),
                                            Configuration.SERVICES_TIMEOUT, Configuration.DATA_TIMEOUT, ref targetUUID))
                                {
                                    throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.GROUP_NOT_FOUND));
                                }
                                switch (
                                    wasGetEnumValueFromDescription<Action>(
                                        wasInput(
                                            wasKeyValueGet(
                                                wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.ACTION)), message))
                                            .ToLowerInvariant()))
                                {
                                    case Action.ADD:
                                        Client.Estate.AddAllowedGroup(targetUUID, allEstates);
                                        break;
                                    case Action.REMOVE:
                                        Client.Estate.RemoveAllowedGroup(targetUUID, allEstates);
                                        break;
                                    default:
                                        throw new Exception(
                                            wasGetDescriptionFromEnumValue(ScriptError.UNKNOWN_ESTATE_LIST_ACTION));
                                }
                                break;
                            case Type.USER:
                                if (
                                    !UUID.TryParse(
                                        wasInput(
                                            wasKeyValueGet(wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.AGENT)),
                                                message)), out targetUUID) && !AgentNameToUUID(
                                                    wasInput(
                                                        wasKeyValueGet(
                                                            wasOutput(
                                                                wasGetDescriptionFromEnumValue(ScriptKeys.FIRSTNAME)),
                                                            message)),
                                                    wasInput(
                                                        wasKeyValueGet(
                                                            wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.LASTNAME)),
                                                            message)),
                                                    Configuration.SERVICES_TIMEOUT, Configuration.DATA_TIMEOUT,
                                                    ref targetUUID))
                                {
                                    throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.AGENT_NOT_FOUND));
                                }
                                switch (
                                    wasGetEnumValueFromDescription<Action>(
                                        wasInput(
                                            wasKeyValueGet(
                                                wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.ACTION)), message))
                                            .ToLowerInvariant()))
                                {
                                    case Action.ADD:
                                        Client.Estate.AddAllowedUser(targetUUID, allEstates);
                                        break;
                                    case Action.REMOVE:
                                        Client.Estate.RemoveAllowedUser(targetUUID, allEstates);
                                        break;
                                    default:
                                        throw new Exception(
                                            wasGetDescriptionFromEnumValue(ScriptError.UNKNOWN_ESTATE_LIST_ACTION));
                                }
                                break;
                            case Type.MANAGER:
                                if (
                                    !UUID.TryParse(
                                        wasInput(
                                            wasKeyValueGet(wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.AGENT)),
                                                message)), out targetUUID) && !AgentNameToUUID(
                                                    wasInput(
                                                        wasKeyValueGet(
                                                            wasOutput(
                                                                wasGetDescriptionFromEnumValue(ScriptKeys.FIRSTNAME)),
                                                            message)),
                                                    wasInput(
                                                        wasKeyValueGet(
                                                            wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.LASTNAME)),
                                                            message)),
                                                    Configuration.SERVICES_TIMEOUT, Configuration.DATA_TIMEOUT,
                                                    ref targetUUID))
                                {
                                    throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.AGENT_NOT_FOUND));
                                }
                                switch (
                                    wasGetEnumValueFromDescription<Action>(
                                        wasInput(
                                            wasKeyValueGet(
                                                wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.ACTION)), message))
                                            .ToLowerInvariant()))
                                {
                                    case Action.ADD:
                                        Client.Estate.AddEstateManager(targetUUID, allEstates);
                                        break;
                                    case Action.REMOVE:
                                        Client.Estate.RemoveEstateManager(targetUUID, allEstates);
                                        break;
                                    default:
                                        throw new Exception(
                                            wasGetDescriptionFromEnumValue(ScriptError.UNKNOWN_ESTATE_LIST_ACTION));
                                }
                                break;
                            default:
                                throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.UNKNOWN_ESTATE_LIST));
                        }
                    };
                    break;
                case ScriptKeys.GETESTATELIST:
                    execute = () =>
                    {
                        if (!HasCorrodePermission(group, (int) Permissions.PERMISSION_LAND))
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.NO_CORRADE_PERMISSIONS));
                        }
                        if (!Client.Network.CurrentSim.IsEstateManager)
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.NO_LAND_RIGHTS));
                        }
                        List<UUID> estateList = new List<UUID>();
                        wasAdaptiveAlarm EstateListReceivedAlarm = new wasAdaptiveAlarm(Configuration.DATA_DECAY_TYPE);
                        switch (
                            wasGetEnumValueFromDescription<Type>(
                                wasInput(wasKeyValueGet(
                                    wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.TYPE)), message))
                                    .ToLowerInvariant()))
                        {
                            case Type.BAN:
                                EventHandler<EstateBansReplyEventArgs> EstateBansReplyEventHandler = (sender, args) =>
                                {
                                    EstateListReceivedAlarm.Alarm(Configuration.DATA_TIMEOUT);
                                    if (args.Count.Equals(0))
                                    {
                                        EstateListReceivedAlarm.Signal.Set();
                                        return;
                                    }
                                    estateList.AddRange(args.Banned);
                                };
                                lock (ClientInstanceEstateLock)
                                {
                                    Client.Estate.EstateBansReply += EstateBansReplyEventHandler;
                                    Client.Estate.RequestInfo();
                                    if (!EstateListReceivedAlarm.Signal.WaitOne(Configuration.SERVICES_TIMEOUT, false))
                                    {
                                        Client.Estate.EstateBansReply -= EstateBansReplyEventHandler;
                                        throw new Exception(
                                            wasGetDescriptionFromEnumValue(ScriptError.TIMEOUT_RETRIEVING_ESTATE_LIST));
                                    }
                                    Client.Estate.EstateBansReply -= EstateBansReplyEventHandler;
                                }
                                break;
                            case Type.GROUP:
                                EventHandler<EstateGroupsReplyEventArgs> EstateGroupsReplyEvenHandler =
                                    (sender, args) =>
                                    {
                                        EstateListReceivedAlarm.Alarm(Configuration.DATA_TIMEOUT);
                                        if (args.Count.Equals(0))
                                        {
                                            EstateListReceivedAlarm.Signal.Set();
                                            return;
                                        }
                                        estateList.AddRange(args.AllowedGroups);
                                    };
                                lock (ClientInstanceEstateLock)
                                {
                                    Client.Estate.EstateGroupsReply += EstateGroupsReplyEvenHandler;
                                    Client.Estate.RequestInfo();
                                    if (!EstateListReceivedAlarm.Signal.WaitOne(Configuration.SERVICES_TIMEOUT, false))
                                    {
                                        Client.Estate.EstateGroupsReply -= EstateGroupsReplyEvenHandler;
                                        throw new Exception(
                                            wasGetDescriptionFromEnumValue(ScriptError.TIMEOUT_RETRIEVING_ESTATE_LIST));
                                    }
                                    Client.Estate.EstateGroupsReply -= EstateGroupsReplyEvenHandler;
                                }
                                break;
                            case Type.MANAGER:
                                EventHandler<EstateManagersReplyEventArgs> EstateManagersReplyEventHandler =
                                    (sender, args) =>
                                    {
                                        EstateListReceivedAlarm.Alarm(Configuration.DATA_TIMEOUT);
                                        if (args.Count.Equals(0))
                                        {
                                            EstateListReceivedAlarm.Signal.Set();
                                            return;
                                        }
                                        estateList.AddRange(args.Managers);
                                    };
                                lock (ClientInstanceEstateLock)
                                {
                                    Client.Estate.EstateManagersReply += EstateManagersReplyEventHandler;
                                    Client.Estate.RequestInfo();
                                    if (!EstateListReceivedAlarm.Signal.WaitOne(Configuration.SERVICES_TIMEOUT, false))
                                    {
                                        Client.Estate.EstateManagersReply -= EstateManagersReplyEventHandler;
                                        throw new Exception(
                                            wasGetDescriptionFromEnumValue(ScriptError.TIMEOUT_RETRIEVING_ESTATE_LIST));
                                    }
                                    Client.Estate.EstateManagersReply -= EstateManagersReplyEventHandler;
                                }
                                break;
                            case Type.USER:
                                EventHandler<EstateUsersReplyEventArgs> EstateUsersReplyEventHandler =
                                    (sender, args) =>
                                    {
                                        EstateListReceivedAlarm.Alarm(Configuration.DATA_TIMEOUT);
                                        if (args.Count.Equals(0))
                                        {
                                            EstateListReceivedAlarm.Signal.Set();
                                            return;
                                        }
                                        estateList.AddRange(args.AllowedUsers);
                                    };
                                lock (ClientInstanceEstateLock)
                                {
                                    Client.Estate.EstateUsersReply += EstateUsersReplyEventHandler;
                                    Client.Estate.RequestInfo();
                                    if (!EstateListReceivedAlarm.Signal.WaitOne(Configuration.SERVICES_TIMEOUT, false))
                                    {
                                        Client.Estate.EstateUsersReply -= EstateUsersReplyEventHandler;
                                        throw new Exception(
                                            wasGetDescriptionFromEnumValue(ScriptError.TIMEOUT_RETRIEVING_ESTATE_LIST));
                                    }
                                    Client.Estate.EstateUsersReply -= EstateUsersReplyEventHandler;
                                }
                                break;
                            default:
                                throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.UNKNOWN_ESTATE_LIST));
                        }
                        List<string> data = new List<string>(estateList.ConvertAll(o => o.ToString()));
                        if (!data.Count.Equals(0))
                        {
                            result.Add(wasGetDescriptionFromEnumValue(ResultKeys.DATA),
                                wasEnumerableToCSV(data));
                        }
                    };
                    break;
                case ScriptKeys.GETAVATARDATA:
                    execute = () =>
                    {
                        if (!HasCorrodePermission(group, (int) Permissions.PERMISSION_INTERACT))
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.NO_CORRADE_PERMISSIONS));
                        }
                        UUID agentUUID;
                        if (
                            !UUID.TryParse(
                                wasInput(wasKeyValueGet(
                                    wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.AGENT)), message)),
                                out agentUUID) && !AgentNameToUUID(
                                    wasInput(
                                        wasKeyValueGet(wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.FIRSTNAME)),
                                            message)),
                                    wasInput(
                                        wasKeyValueGet(wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.LASTNAME)),
                                            message)),
                                    Configuration.SERVICES_TIMEOUT, Configuration.DATA_TIMEOUT, ref agentUUID))
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.AGENT_NOT_FOUND));
                        }
                        float range;
                        if (
                            !float.TryParse(
                                wasInput(wasKeyValueGet(
                                    wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.RANGE)), message)),
                                out range))
                        {
                            range = Configuration.RANGE;
                        }
                        Avatar avatar =
                            GetAvatars(range, Configuration.SERVICES_TIMEOUT, Configuration.DATA_TIMEOUT)
                                .FirstOrDefault(o => o.ID.Equals(agentUUID));
                        if (avatar == null)
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.AVATAR_NOT_IN_RANGE));
                        wasAdaptiveAlarm ProfileDataReceivedAlarm = new wasAdaptiveAlarm(Configuration.DATA_DECAY_TYPE);
                        object LockObject = new object();
                        EventHandler<AvatarInterestsReplyEventArgs> AvatarInterestsReplyEventHandler = (sender, args) =>
                        {
                            ProfileDataReceivedAlarm.Alarm(Configuration.DATA_TIMEOUT);
                            avatar.ProfileInterests = args.Interests;
                        };
                        EventHandler<AvatarPropertiesReplyEventArgs> AvatarPropertiesReplyEventHandler =
                            (sender, args) =>
                            {
                                ProfileDataReceivedAlarm.Alarm(Configuration.DATA_TIMEOUT);
                                avatar.ProfileProperties = args.Properties;
                            };
                        EventHandler<AvatarGroupsReplyEventArgs> AvatarGroupsReplyEventHandler = (sender, args) =>
                        {
                            ProfileDataReceivedAlarm.Alarm(Configuration.DATA_TIMEOUT);
                            lock (LockObject)
                            {
                                avatar.Groups.AddRange(args.Groups.Select(o => o.GroupID));
                            }
                        };
                        EventHandler<AvatarPicksReplyEventArgs> AvatarPicksReplyEventHandler =
                            (sender, args) => ProfileDataReceivedAlarm.Alarm(Configuration.DATA_TIMEOUT);
                        EventHandler<AvatarClassifiedReplyEventArgs> AvatarClassifiedReplyEventHandler =
                            (sender, args) => ProfileDataReceivedAlarm.Alarm(Configuration.DATA_TIMEOUT);
                        lock (ClientInstanceAvatarsLock)
                        {
                            Client.Avatars.AvatarInterestsReply += AvatarInterestsReplyEventHandler;
                            Client.Avatars.AvatarPropertiesReply += AvatarPropertiesReplyEventHandler;
                            Client.Avatars.AvatarGroupsReply += AvatarGroupsReplyEventHandler;
                            Client.Avatars.AvatarPicksReply += AvatarPicksReplyEventHandler;
                            Client.Avatars.AvatarClassifiedReply += AvatarClassifiedReplyEventHandler;
                            Client.Avatars.RequestAvatarProperties(agentUUID);
                            Client.Avatars.RequestAvatarPicks(agentUUID);
                            Client.Avatars.RequestAvatarClassified(agentUUID);
                            if (!ProfileDataReceivedAlarm.Signal.WaitOne(Configuration.SERVICES_TIMEOUT, false))
                            {
                                Client.Avatars.AvatarInterestsReply -= AvatarInterestsReplyEventHandler;
                                Client.Avatars.AvatarPropertiesReply -= AvatarPropertiesReplyEventHandler;
                                Client.Avatars.AvatarGroupsReply -= AvatarGroupsReplyEventHandler;
                                Client.Avatars.AvatarPicksReply -= AvatarPicksReplyEventHandler;
                                Client.Avatars.AvatarClassifiedReply -= AvatarClassifiedReplyEventHandler;
                                throw new Exception(
                                    wasGetDescriptionFromEnumValue(ScriptError.TIMEOUT_GETTING_AVATAR_DATA));
                            }
                            Client.Avatars.AvatarInterestsReply -= AvatarInterestsReplyEventHandler;
                            Client.Avatars.AvatarPropertiesReply -= AvatarPropertiesReplyEventHandler;
                            Client.Avatars.AvatarGroupsReply -= AvatarGroupsReplyEventHandler;
                            Client.Avatars.AvatarPicksReply -= AvatarPicksReplyEventHandler;
                            Client.Avatars.AvatarClassifiedReply -= AvatarClassifiedReplyEventHandler;
                        }
                        List<string> data = new List<string>(GetStructuredData(avatar,
                            wasInput(wasKeyValueGet(wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.DATA)),
                                message))));
                        if (!data.Count.Equals(0))
                        {
                            result.Add(wasGetDescriptionFromEnumValue(ResultKeys.DATA),
                                wasEnumerableToCSV(data));
                        }
                    };
                    break;
                case ScriptKeys.GETAVATARPOSITIONS:
                    execute = () =>
                    {
                        if (!HasCorrodePermission(group, (int) Permissions.PERMISSION_INTERACT))
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.NO_CORRADE_PERMISSIONS));
                        }
                        Vector3 position;
                        if (
                            !Vector3.TryParse(
                                wasInput(
                                    wasKeyValueGet(wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.POSITION)),
                                        message)),
                                out position))
                        {
                            position = Client.Self.SimPosition;
                        }
                        Entity entity = wasGetEnumValueFromDescription<Entity>(
                            wasInput(
                                wasKeyValueGet(wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.ENTITY)), message))
                                .ToLowerInvariant());
                        string region =
                            wasInput(wasKeyValueGet(wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.REGION)),
                                message));
                        Simulator simulator =
                            Client.Network.Simulators.FirstOrDefault(
                                o =>
                                    o.Name.Equals(
                                        string.IsNullOrEmpty(region) ? Client.Network.CurrentSim.Name : region,
                                        StringComparison.InvariantCultureIgnoreCase));
                        if (simulator == null)
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.REGION_NOT_FOUND));
                        }
                        Parcel parcel = null;
                        switch (entity)
                        {
                            case Entity.REGION:
                                break;
                            case Entity.PARCEL:
                                if (
                                    !GetParcelAtPosition(simulator, position, ref parcel))
                                {
                                    throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.COULD_NOT_FIND_PARCEL));
                                }
                                break;
                            default:
                                throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.UNKNOWN_ENTITY));
                        }
                        List<string> csv = new List<string>();
                        Dictionary<UUID, Vector3> avatarPositions = new Dictionary<UUID, Vector3>();
                        simulator.AvatarPositions.ForEach(o => avatarPositions.Add(o.Key, o.Value));
                        foreach (KeyValuePair<UUID, Vector3> p in avatarPositions)
                        {
                            string name = string.Empty;
                            if (
                                !AgentUUIDToName(p.Key, Configuration.SERVICES_TIMEOUT,
                                    ref name)) continue;
                            switch (entity)
                            {
                                case Entity.REGION:
                                    break;
                                case Entity.PARCEL:
                                    if (parcel == null) return;
                                    Parcel avatarParcel = null;
                                    if (!GetParcelAtPosition(simulator, p.Value, ref avatarParcel))
                                        continue;
                                    if (!avatarParcel.LocalID.Equals(parcel.LocalID)) continue;
                                    break;
                            }
                            csv.Add(name);
                            csv.Add(p.Key.ToString());
                            csv.Add(p.Value.ToString());
                        }
                        if (!csv.Count.Equals(0))
                        {
                            result.Add(wasGetDescriptionFromEnumValue(ResultKeys.DATA),
                                wasEnumerableToCSV(csv));
                        }
                    };
                    break;
                case ScriptKeys.GETMAPAVATARPOSITIONS:
                    execute = () =>
                    {
                        if (!HasCorrodePermission(group, (int) Permissions.PERMISSION_INTERACT))
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.NO_CORRADE_PERMISSIONS));
                        }
                        string region =
                            wasInput(wasKeyValueGet(wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.REGION)),
                                message));
                        if (string.IsNullOrEmpty(region))
                        {
                            region = Client.Network.CurrentSim.Name;
                        }
                        ulong regionHandle = 0;
                        ManualResetEvent GridRegionEvent = new ManualResetEvent(false);
                        EventHandler<GridRegionEventArgs> GridRegionEventHandler = (sender, args) =>
                        {
                            if (!args.Region.Name.Equals(region, StringComparison.InvariantCultureIgnoreCase))
                                return;
                            regionHandle = args.Region.RegionHandle;
                            GridRegionEvent.Set();
                        };
                        lock (ClientInstanceGridLock)
                        {
                            Client.Grid.GridRegion += GridRegionEventHandler;
                            Client.Grid.RequestMapRegion(region, GridLayerType.Objects);
                            if (!GridRegionEvent.WaitOne(Configuration.SERVICES_TIMEOUT, false))
                            {
                                Client.Grid.GridRegion -= GridRegionEventHandler;
                                throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.TIMEOUT_GETTING_REGION));
                            }
                            Client.Grid.GridRegion -= GridRegionEventHandler;
                        }
                        if (regionHandle.Equals(0))
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.REGION_NOT_FOUND));
                        }
                        HashSet<MapItem> mapItems =
                            new HashSet<MapItem>(Client.Grid.MapItems(regionHandle, GridItemType.AgentLocations,
                                GridLayerType.Objects, Configuration.SERVICES_TIMEOUT));
                        if (mapItems.Count.Equals(0))
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.NO_MAP_ITEMS_FOUND));
                        }
                        List<string> data =
                            new List<string>(mapItems.AsParallel()
                                .Where(o => (o as MapAgentLocation) != null)
                                .Select(o => new[]
                                {
                                    ((MapAgentLocation) o).AvatarCount.ToString(CultureInfo.InvariantCulture),
                                    new Vector3(o.LocalX, o.LocalY, 0).ToString()
                                }).SelectMany(o => o));
                        if (!data.Count.Equals(0))
                        {
                            result.Add(wasGetDescriptionFromEnumValue(ResultKeys.DATA),
                                wasEnumerableToCSV(data));
                        }
                    };
                    break;
                case ScriptKeys.GETSELFDATA:
                    execute = () =>
                    {
                        if (!HasCorrodePermission(group, (int) Permissions.PERMISSION_GROOMING))
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.NO_CORRADE_PERMISSIONS));
                        }
                        List<string> data = new List<string>(GetStructuredData(Client.Self,
                            wasInput(wasKeyValueGet(wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.DATA)),
                                message))));
                        if (!data.Count.Equals(0))
                        {
                            result.Add(wasGetDescriptionFromEnumValue(ResultKeys.DATA),
                                wasEnumerableToCSV(data));
                        }
                    };
                    break;
                case ScriptKeys.DISPLAYNAME:
                    execute = () =>
                    {
                        if (!HasCorrodePermission(group, (int) Permissions.PERMISSION_GROOMING))
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.NO_CORRADE_PERMISSIONS));
                        }
                        string previous = string.Empty;
                        Client.Avatars.GetDisplayNames(new List<UUID> {Client.Self.AgentID},
                            (succeded, names, IDs) =>
                            {
                                if (!succeded || names.Length < 1)
                                {
                                    throw new Exception(
                                        wasGetDescriptionFromEnumValue(ScriptError.FAILED_TO_GET_DISPLAY_NAME));
                                }
                                previous = names[0].DisplayName;
                            });
                        switch (
                            wasGetEnumValueFromDescription<Action>(
                                wasInput(
                                    wasKeyValueGet(wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.ACTION)),
                                        message)).ToLowerInvariant()))
                        {
                            case Action.GET:
                                result.Add(wasGetDescriptionFromEnumValue(ResultKeys.DATA), previous);
                                break;
                            case Action.SET:
                                string name =
                                    wasInput(
                                        wasKeyValueGet(wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.NAME)),
                                            message));
                                if (string.IsNullOrEmpty(name))
                                {
                                    throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.NO_NAME_PROVIDED));
                                }
                                bool succeeded = true;
                                ManualResetEvent SetDisplayNameEvent = new ManualResetEvent(false);
                                EventHandler<SetDisplayNameReplyEventArgs> SetDisplayNameEventHandler =
                                    (sender, args) =>
                                    {
                                        succeeded = args.Status.Equals(LINDEN_CONSTANTS.AVATARS.SET_DISPLAY_NAME_SUCCESS);
                                        SetDisplayNameEvent.Set();
                                    };
                                lock (ClientInstanceSelfLock)
                                {
                                    Client.Self.SetDisplayNameReply += SetDisplayNameEventHandler;
                                    Client.Self.SetDisplayName(previous, name);
                                    if (!SetDisplayNameEvent.WaitOne(Configuration.SERVICES_TIMEOUT, false))
                                    {
                                        Client.Self.SetDisplayNameReply -= SetDisplayNameEventHandler;
                                        throw new Exception(
                                            wasGetDescriptionFromEnumValue(ScriptError.TIMEOUT_WAITING_FOR_ESTATE_LIST));
                                    }
                                    Client.Self.SetDisplayNameReply -= SetDisplayNameEventHandler;
                                }
                                if (!succeeded)
                                {
                                    throw new Exception(
                                        wasGetDescriptionFromEnumValue(ScriptError.COULD_NOT_SET_DISPLAY_NAME));
                                }
                                break;
                            default:
                                throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.UNKNOWN_ACTION));
                        }
                    };
                    break;
                case ScriptKeys.GETINVENTORYOFFERS:
                    execute = () =>
                    {
                        if (!HasCorrodePermission(group, (int) Permissions.PERMISSION_INVENTORY))
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.NO_CORRADE_PERMISSIONS));
                        }
                        object LockObject = new object();
                        List<string> csv = new List<string>();
                        lock (InventoryOffersLock)
                        {
                            Parallel.ForEach(InventoryOffers, o =>
                            {
                                List<string> name =
                                    new List<string>(
                                        GetAvatarNames(o.Key.Offer.FromAgentName));
                                lock (LockObject)
                                {
                                    csv.AddRange(new[]
                                    {wasGetDescriptionFromEnumValue(ScriptKeys.FIRSTNAME), name.First()});
                                    csv.AddRange(new[]
                                    {wasGetDescriptionFromEnumValue(ScriptKeys.FIRSTNAME), name.Last()});
                                    csv.AddRange(new[]
                                    {
                                        wasGetDescriptionFromEnumValue(ScriptKeys.TYPE),
                                        o.Key.AssetType.ToString()
                                    });
                                    csv.AddRange(new[]
                                    {wasGetDescriptionFromEnumValue(ScriptKeys.MESSAGE), o.Key.Offer.Message});
                                    csv.AddRange(new[]
                                    {
                                        wasGetDescriptionFromEnumValue(ScriptKeys.SESSION),
                                        o.Key.Offer.IMSessionID.ToString()
                                    });
                                }
                            });
                        }
                        if (!csv.Count.Equals(0))
                        {
                            result.Add(wasGetDescriptionFromEnumValue(ResultKeys.DATA),
                                wasEnumerableToCSV(csv));
                        }
                    };
                    break;
                case ScriptKeys.REPLYTOINVENTORYOFFER:
                    execute = () =>
                    {
                        if (!HasCorrodePermission(group, (int) Permissions.PERMISSION_INVENTORY))
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.NO_CORRADE_PERMISSIONS));
                        }
                        UUID session;
                        if (
                            !UUID.TryParse(
                                wasInput(
                                    wasKeyValueGet(wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.SESSION)),
                                        message)),
                                out session))
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.NO_SESSION_SPECIFIED));
                        }
                        lock (InventoryOffersLock)
                        {
                            if (!InventoryOffers.AsParallel().Any(o => o.Key.Offer.IMSessionID.Equals(session)))
                            {
                                throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.INVENTORY_OFFER_NOT_FOUND));
                            }
                        }
                        KeyValuePair<InventoryObjectOfferedEventArgs, ManualResetEvent> offer;
                        lock (InventoryOffersLock)
                        {
                            offer =
                                InventoryOffers.AsParallel()
                                    .FirstOrDefault(o => o.Key.Offer.IMSessionID.Equals(session));
                        }
                        UUID folderUUID;
                        string folder =
                            wasInput(wasKeyValueGet(wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.FOLDER)),
                                message));
                        if (string.IsNullOrEmpty(folder) || !UUID.TryParse(folder, out folderUUID))
                        {
                            folderUUID =
                                Client.Inventory.Store.Items[Client.Inventory.FindFolderForType(offer.Key.AssetType)]
                                    .Data.UUID;
                        }
                        if (folderUUID.Equals(UUID.Zero))
                        {
                            InventoryBase inventoryBaseItem =
                                FindInventory<InventoryBase>(Client.Inventory.Store.RootNode, folder
                                    ).FirstOrDefault();
                            InventoryItem item = inventoryBaseItem as InventoryItem;
                            if (item != null && item.AssetType.Equals(AssetType.Folder))
                            {
                                folderUUID = inventoryBaseItem.UUID;
                            }
                        }
                        switch (
                            wasGetEnumValueFromDescription<Action>(
                                wasInput(
                                    wasKeyValueGet(wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.ACTION)),
                                        message)).ToLowerInvariant()))
                        {
                            case Action.ACCEPT:
                                lock (InventoryOffersLock)
                                {
                                    if (!folderUUID.Equals(UUID.Zero))
                                    {
                                        offer.Key.FolderID = folderUUID;
                                    }
                                    offer.Key.Accept = true;
                                    offer.Value.Set();
                                    SaveInventoryOffersState.Invoke();
                                }
                                break;
                            case Action.DECLINE:
                                lock (InventoryOffersLock)
                                {
                                    offer.Key.Accept = false;
                                    offer.Value.Set();
                                    SaveInventoryOffersState.Invoke();
                                }
                                break;
                            default:
                                throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.UNKNOWN_ACTION));
                        }
                    };
                    break;
                case ScriptKeys.GETFRIENDSLIST:
                    execute = () =>
                    {
                        if (!HasCorrodePermission(group, (int) Permissions.PERMISSION_FRIENDSHIP))
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.NO_CORRADE_PERMISSIONS));
                        }
                        List<string> csv = new List<string>();
                        Client.Friends.FriendList.ForEach(o =>
                        {
                            csv.Add(o.Name);
                            csv.Add(o.UUID.ToString());
                        });
                        if (!csv.Count.Equals(0))
                        {
                            result.Add(wasGetDescriptionFromEnumValue(ResultKeys.DATA),
                                wasEnumerableToCSV(csv));
                        }
                    };
                    break;
                case ScriptKeys.GETFRIENDSHIPREQUESTS:
                    execute = () =>
                    {
                        if (!HasCorrodePermission(group, (int) Permissions.PERMISSION_FRIENDSHIP))
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.NO_CORRADE_PERMISSIONS));
                        }
                        List<string> csv = new List<string>();
                        Client.Friends.FriendRequests.ForEach(o =>
                        {
                            string name = string.Empty;
                            if (
                                !AgentUUIDToName(o.Key, Configuration.SERVICES_TIMEOUT,
                                    ref name))
                            {
                                return;
                            }
                            csv.Add(name);
                            csv.Add(o.Key.ToString());
                        });
                        if (!csv.Count.Equals(0))
                        {
                            result.Add(wasGetDescriptionFromEnumValue(ResultKeys.DATA),
                                wasEnumerableToCSV(csv));
                        }
                    };
                    break;
                case ScriptKeys.REPLYTOFRIENDSHIPREQUEST:
                    execute = () =>
                    {
                        if (!HasCorrodePermission(group, (int) Permissions.PERMISSION_FRIENDSHIP))
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.NO_CORRADE_PERMISSIONS));
                        }
                        UUID agentUUID;
                        if (
                            !UUID.TryParse(
                                wasInput(wasKeyValueGet(
                                    wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.AGENT)), message)),
                                out agentUUID) && !AgentNameToUUID(
                                    wasInput(
                                        wasKeyValueGet(wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.FIRSTNAME)),
                                            message)),
                                    wasInput(
                                        wasKeyValueGet(wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.LASTNAME)),
                                            message)),
                                    Configuration.SERVICES_TIMEOUT, Configuration.DATA_TIMEOUT, ref agentUUID))
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.AGENT_NOT_FOUND));
                        }
                        UUID session = UUID.Zero;
                        Client.Friends.FriendRequests.ForEach(o =>
                        {
                            if (o.Key.Equals(agentUUID))
                            {
                                session = o.Value;
                            }
                        });
                        if (session.Equals(UUID.Zero))
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.NO_FRIENDSHIP_OFFER_FOUND));
                        }
                        switch (
                            wasGetEnumValueFromDescription<Action>(
                                wasInput(
                                    wasKeyValueGet(wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.ACTION)),
                                        message)).ToLowerInvariant()))
                        {
                            case Action.ACCEPT:
                                Client.Friends.AcceptFriendship(agentUUID, session);
                                break;
                            case Action.DECLINE:
                                Client.Friends.DeclineFriendship(agentUUID, session);
                                break;
                            default:
                                throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.UNKNOWN_ACTION));
                        }
                    };
                    break;
                case ScriptKeys.GETFRIENDDATA:
                    execute = () =>
                    {
                        if (!HasCorrodePermission(group, (int) Permissions.PERMISSION_FRIENDSHIP))
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.NO_CORRADE_PERMISSIONS));
                        }
                        UUID agentUUID;
                        if (
                            !UUID.TryParse(
                                wasInput(wasKeyValueGet(
                                    wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.AGENT)), message)),
                                out agentUUID) && !AgentNameToUUID(
                                    wasInput(
                                        wasKeyValueGet(wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.FIRSTNAME)),
                                            message)),
                                    wasInput(
                                        wasKeyValueGet(wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.LASTNAME)),
                                            message)),
                                    Configuration.SERVICES_TIMEOUT, Configuration.DATA_TIMEOUT, ref agentUUID))
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.AGENT_NOT_FOUND));
                        }
                        FriendInfo friend = Client.Friends.FriendList.Find(o => o.UUID.Equals(agentUUID));
                        if (friend == null)
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.FRIEND_NOT_FOUND));
                        }
                        List<string> data = new List<string>(GetStructuredData(friend,
                            wasInput(wasKeyValueGet(wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.DATA)),
                                message))));
                        if (!data.Count.Equals(0))
                        {
                            result.Add(wasGetDescriptionFromEnumValue(ResultKeys.DATA),
                                wasEnumerableToCSV(data));
                        }
                    };
                    break;
                case ScriptKeys.OFFERFRIENDSHIP:
                    execute = () =>
                    {
                        if (!HasCorrodePermission(group, (int) Permissions.PERMISSION_FRIENDSHIP))
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.NO_CORRADE_PERMISSIONS));
                        }
                        UUID agentUUID;
                        if (
                            !UUID.TryParse(
                                wasInput(wasKeyValueGet(
                                    wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.AGENT)), message)),
                                out agentUUID) && !AgentNameToUUID(
                                    wasInput(
                                        wasKeyValueGet(wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.FIRSTNAME)),
                                            message)),
                                    wasInput(
                                        wasKeyValueGet(wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.LASTNAME)),
                                            message)),
                                    Configuration.SERVICES_TIMEOUT, Configuration.DATA_TIMEOUT, ref agentUUID))
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.AGENT_NOT_FOUND));
                        }
                        FriendInfo friend = Client.Friends.FriendList.Find(o => o.UUID.Equals(agentUUID));
                        if (friend != null)
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.AGENT_ALREADY_FRIEND));
                        }
                        Client.Friends.OfferFriendship(agentUUID,
                            wasInput(wasKeyValueGet(wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.MESSAGE)),
                                message)));
                    };
                    break;
                case ScriptKeys.TERMINATEFRIENDSHIP:
                    execute = () =>
                    {
                        if (!HasCorrodePermission(group, (int) Permissions.PERMISSION_FRIENDSHIP))
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.NO_CORRADE_PERMISSIONS));
                        }
                        UUID agentUUID;
                        if (
                            !UUID.TryParse(
                                wasInput(wasKeyValueGet(
                                    wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.AGENT)), message)),
                                out agentUUID) && !AgentNameToUUID(
                                    wasInput(
                                        wasKeyValueGet(wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.FIRSTNAME)),
                                            message)),
                                    wasInput(
                                        wasKeyValueGet(wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.LASTNAME)),
                                            message)),
                                    Configuration.SERVICES_TIMEOUT, Configuration.DATA_TIMEOUT, ref agentUUID))
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.AGENT_NOT_FOUND));
                        }
                        FriendInfo friend = Client.Friends.FriendList.Find(o => o.UUID.Equals(agentUUID));
                        if (friend == null)
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.FRIEND_NOT_FOUND));
                        }
                        Client.Friends.TerminateFriendship(agentUUID);
                    };
                    break;
                case ScriptKeys.GRANTFRIENDRIGHTS:
                    execute = () =>
                    {
                        if (!HasCorrodePermission(group, (int) Permissions.PERMISSION_FRIENDSHIP))
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.NO_CORRADE_PERMISSIONS));
                        }
                        UUID agentUUID;
                        if (
                            !UUID.TryParse(
                                wasInput(wasKeyValueGet(
                                    wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.AGENT)), message)),
                                out agentUUID) && !AgentNameToUUID(
                                    wasInput(
                                        wasKeyValueGet(wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.FIRSTNAME)),
                                            message)),
                                    wasInput(
                                        wasKeyValueGet(wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.LASTNAME)),
                                            message)),
                                    Configuration.SERVICES_TIMEOUT, Configuration.DATA_TIMEOUT, ref agentUUID))
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.AGENT_NOT_FOUND));
                        }
                        FriendInfo friend = Client.Friends.FriendList.Find(o => o.UUID.Equals(agentUUID));
                        if (friend == null)
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.FRIEND_NOT_FOUND));
                        }
                        int rights = 0;
                        Parallel.ForEach(wasCSVToEnumerable(
                            wasInput(wasKeyValueGet(wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.RIGHTS)),
                                message))),
                            o =>
                                Parallel.ForEach(
                                    typeof (FriendRights).GetFields(BindingFlags.Public | BindingFlags.Static)
                                        .AsParallel().Where(p => p.Name.Equals(o, StringComparison.Ordinal)),
                                    q => { rights |= ((int) q.GetValue(null)); }));
                        Client.Friends.GrantRights(agentUUID, (FriendRights) rights);
                    };
                    break;
                case ScriptKeys.MAPFRIEND:
                    execute = () =>
                    {
                        if (!HasCorrodePermission(group, (int) Permissions.PERMISSION_FRIENDSHIP))
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.NO_CORRADE_PERMISSIONS));
                        }
                        UUID agentUUID;
                        if (
                            !UUID.TryParse(
                                wasInput(wasKeyValueGet(
                                    wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.AGENT)), message)),
                                out agentUUID) && !AgentNameToUUID(
                                    wasInput(
                                        wasKeyValueGet(wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.FIRSTNAME)),
                                            message)),
                                    wasInput(
                                        wasKeyValueGet(wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.LASTNAME)),
                                            message)),
                                    Configuration.SERVICES_TIMEOUT, Configuration.DATA_TIMEOUT, ref agentUUID))
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.AGENT_NOT_FOUND));
                        }
                        FriendInfo friend = Client.Friends.FriendList.Find(o => o.UUID.Equals(agentUUID));
                        if (friend == null)
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.FRIEND_NOT_FOUND));
                        }
                        if (!friend.CanSeeThemOnMap)
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.FRIEND_DOES_NOT_ALLOW_MAPPING));
                        }
                        ulong regionHandle = 0;
                        Vector3 position = Vector3.Zero;
                        ManualResetEvent FriendFoundEvent = new ManualResetEvent(false);
                        bool offline = false;
                        EventHandler<FriendFoundReplyEventArgs> FriendFoundEventHandler = (sender, args) =>
                        {
                            if (args.RegionHandle.Equals(0))
                            {
                                offline = true;
                                FriendFoundEvent.Set();
                                return;
                            }
                            regionHandle = args.RegionHandle;
                            position = args.Location;
                            FriendFoundEvent.Set();
                        };
                        lock (ClientInstanceFriendsLock)
                        {
                            Client.Friends.FriendFoundReply += FriendFoundEventHandler;
                            Client.Friends.MapFriend(agentUUID);
                            if (!FriendFoundEvent.WaitOne(Configuration.SERVICES_TIMEOUT, false))
                            {
                                Client.Friends.FriendFoundReply -= FriendFoundEventHandler;
                                throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.TIMEOUT_MAPPING_FRIEND));
                            }
                            Client.Friends.FriendFoundReply -= FriendFoundEventHandler;
                        }
                        if (offline)
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.FRIEND_OFFLINE));
                        }
                        UUID parcelUUID = Client.Parcels.RequestRemoteParcelID(position, regionHandle, UUID.Zero);
                        ManualResetEvent ParcelInfoEvent = new ManualResetEvent(false);
                        string regionName = string.Empty;
                        EventHandler<ParcelInfoReplyEventArgs> ParcelInfoEventHandler = (sender, args) =>
                        {
                            regionName = args.Parcel.SimName;
                            ParcelInfoEvent.Set();
                        };
                        lock (ClientInstanceParcelsLock)
                        {
                            Client.Parcels.ParcelInfoReply += ParcelInfoEventHandler;
                            Client.Parcels.RequestParcelInfo(parcelUUID);
                            if (!ParcelInfoEvent.WaitOne(Configuration.SERVICES_TIMEOUT, false))
                            {
                                Client.Parcels.ParcelInfoReply -= ParcelInfoEventHandler;
                                throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.TIMEOUT_GETTING_PARCELS));
                            }
                            Client.Parcels.ParcelInfoReply -= ParcelInfoEventHandler;
                        }
                        result.Add(wasGetDescriptionFromEnumValue(ResultKeys.DATA),
                            wasEnumerableToCSV(new[] {regionName, position.ToString()}));
                    };
                    break;
                case ScriptKeys.GETOBJECTPERMISSIONS:
                    execute = () =>
                    {
                        if (
                            !HasCorrodePermission(group,
                                (int) Permissions.PERMISSION_INTERACT))
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.NO_CORRADE_PERMISSIONS));
                        }
                        float range;
                        if (
                            !float.TryParse(
                                wasInput(wasKeyValueGet(
                                    wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.RANGE)), message)),
                                out range))
                        {
                            range = Configuration.RANGE;
                        }
                        Primitive primitive = null;
                        if (
                            !FindPrimitive(
                                StringOrUUID(wasInput(wasKeyValueGet(
                                    wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.ITEM)), message))),
                                range,
                                ref primitive, Configuration.SERVICES_TIMEOUT, Configuration.DATA_TIMEOUT))
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.PRIMITIVE_NOT_FOUND));
                        }
                        // if the primitive is not an object (the root) or the primitive 
                        // is not an object as an avatar attachment then bail out
                        if (!primitive.ParentID.Equals(0) && !primitive.ParentID.Equals(Client.Self.LocalID))
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.ITEM_IS_NOT_AN_OBJECT));
                        }
                        result.Add(wasGetDescriptionFromEnumValue(ScriptKeys.PERMISSIONS),
                            wasPermissionsToString(primitive.Properties.Permissions));
                    };
                    break;
                case ScriptKeys.SETOBJECTPERMISSIONS:
                    execute = () =>
                    {
                        if (
                            !HasCorrodePermission(group,
                                (int) Permissions.PERMISSION_INTERACT))
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.NO_CORRADE_PERMISSIONS));
                        }
                        float range;
                        if (
                            !float.TryParse(
                                wasInput(wasKeyValueGet(
                                    wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.RANGE)), message)),
                                out range))
                        {
                            range = Configuration.RANGE;
                        }
                        Primitive primitive = null;
                        if (
                            !FindPrimitive(
                                StringOrUUID(wasInput(wasKeyValueGet(
                                    wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.ITEM)), message))),
                                range,
                                ref primitive, Configuration.SERVICES_TIMEOUT, Configuration.DATA_TIMEOUT))
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.PRIMITIVE_NOT_FOUND));
                        }
                        // if the primitive is not an object (the root) or the primitive 
                        // is not an object as an avatar attachment then bail out
                        if (!primitive.ParentID.Equals(0) && !primitive.ParentID.Equals(Client.Self.LocalID))
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.ITEM_IS_NOT_AN_OBJECT));
                        }
                        string itemPermissions =
                            wasInput(
                                wasKeyValueGet(
                                    wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.PERMISSIONS)), message));
                        if (string.IsNullOrEmpty(itemPermissions))
                        {
                            throw new Exception(
                                wasGetDescriptionFromEnumValue(ScriptError.NO_PERMISSIONS_PROVIDED));
                        }
                        OpenMetaverse.Permissions permissions = wasStringToPermissions(itemPermissions);
                        Client.Objects.SetPermissions(
                            Client.Network.Simulators.FirstOrDefault(o => o.Handle.Equals(primitive.RegionHandle)),
                            new List<uint> {primitive.LocalID},
                            PermissionWho.Base, permissions.BaseMask, true);
                        Client.Objects.SetPermissions(
                            Client.Network.Simulators.FirstOrDefault(o => o.Handle.Equals(primitive.RegionHandle)),
                            new List<uint> {primitive.LocalID},
                            PermissionWho.Owner, permissions.OwnerMask, true);
                        Client.Objects.SetPermissions(
                            Client.Network.Simulators.FirstOrDefault(o => o.Handle.Equals(primitive.RegionHandle)),
                            new List<uint> {primitive.LocalID},
                            PermissionWho.Group, permissions.GroupMask, true);
                        Client.Objects.SetPermissions(
                            Client.Network.Simulators.FirstOrDefault(o => o.Handle.Equals(primitive.RegionHandle)),
                            new List<uint> {primitive.LocalID},
                            PermissionWho.Everyone, permissions.EveryoneMask, true);
                        Client.Objects.SetPermissions(
                            Client.Network.Simulators.FirstOrDefault(o => o.Handle.Equals(primitive.RegionHandle)),
                            new List<uint> {primitive.LocalID},
                            PermissionWho.NextOwner, permissions.NextOwnerMask, true);
                    };
                    break;
                case ScriptKeys.OBJECTDEED:
                    execute = () =>
                    {
                        if (!HasCorrodePermission(group, (int) Permissions.PERMISSION_INTERACT))
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.NO_CORRADE_PERMISSIONS));
                        }
                        Group configuredGroup =
                            Configuration.GROUPS.AsParallel().FirstOrDefault(
                                o => o.Name.Equals(group, StringComparison.Ordinal));
                        UUID groupUUID = UUID.Zero;
                        switch (!configuredGroup.Equals(default(Group)))
                        {
                            case true:
                                groupUUID = configuredGroup.UUID;
                                break;
                            default:
                                if (!GroupNameToUUID(group, Configuration.SERVICES_TIMEOUT, Configuration.DATA_TIMEOUT,
                                    ref groupUUID))
                                {
                                    throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.GROUP_NOT_FOUND));
                                }
                                break;
                        }
                        float range;
                        if (
                            !float.TryParse(
                                wasInput(wasKeyValueGet(
                                    wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.RANGE)), message)),
                                out range))
                        {
                            range = Configuration.RANGE;
                        }
                        Primitive primitive = null;
                        if (
                            !FindPrimitive(
                                StringOrUUID(wasInput(wasKeyValueGet(
                                    wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.ITEM)), message))),
                                range,
                                ref primitive, Configuration.SERVICES_TIMEOUT, Configuration.DATA_TIMEOUT))
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.PRIMITIVE_NOT_FOUND));
                        }
                        // if the primitive is not an object (the root) or the primitive 
                        // is not an object as an avatar attachment then bail out
                        if (!primitive.ParentID.Equals(0) && !primitive.ParentID.Equals(Client.Self.LocalID))
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.ITEM_IS_NOT_AN_OBJECT));
                        }
                        Client.Objects.DeedObject(
                            Client.Network.Simulators.FirstOrDefault(o => o.Handle.Equals(primitive.RegionHandle)),
                            primitive.LocalID, groupUUID);
                    };
                    break;
                case ScriptKeys.SETOBJECTGROUP:
                    execute = () =>
                    {
                        if (!HasCorrodePermission(group, (int) Permissions.PERMISSION_INTERACT))
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.NO_CORRADE_PERMISSIONS));
                        }
                        Group configuredGroup =
                            Configuration.GROUPS.AsParallel().FirstOrDefault(
                                o => o.Name.Equals(group, StringComparison.Ordinal));
                        UUID groupUUID = UUID.Zero;
                        switch (!configuredGroup.Equals(default(Group)))
                        {
                            case true:
                                groupUUID = configuredGroup.UUID;
                                break;
                            default:
                                if (!GroupNameToUUID(group, Configuration.SERVICES_TIMEOUT, Configuration.DATA_TIMEOUT,
                                    ref groupUUID))
                                {
                                    throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.GROUP_NOT_FOUND));
                                }
                                break;
                        }
                        float range;
                        if (
                            !float.TryParse(
                                wasInput(wasKeyValueGet(
                                    wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.RANGE)), message)),
                                out range))
                        {
                            range = Configuration.RANGE;
                        }
                        Primitive primitive = null;
                        if (
                            !FindPrimitive(
                                StringOrUUID(wasInput(wasKeyValueGet(
                                    wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.ITEM)), message))),
                                range,
                                ref primitive, Configuration.SERVICES_TIMEOUT, Configuration.DATA_TIMEOUT))
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.PRIMITIVE_NOT_FOUND));
                        }
                        // if the primitive is not an object (the root) or the primitive 
                        // is not an object as an avatar attachment then bail out
                        if (!primitive.ParentID.Equals(0) && !primitive.ParentID.Equals(Client.Self.LocalID))
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.ITEM_IS_NOT_AN_OBJECT));
                        }
                        Client.Objects.SetObjectsGroup(
                            Client.Network.Simulators.FirstOrDefault(o => o.Handle.Equals(primitive.RegionHandle)),
                            new List<uint> {primitive.LocalID},
                            groupUUID);
                    };
                    break;
                case ScriptKeys.SETOBJECTSALEINFO:
                    execute = () =>
                    {
                        if (!HasCorrodePermission(group, (int) Permissions.PERMISSION_INTERACT))
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.NO_CORRADE_PERMISSIONS));
                        }
                        int price;
                        if (
                            !int.TryParse(
                                wasInput(wasKeyValueGet(
                                    wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.PRICE)), message)),
                                out price))
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.INVALID_PRICE));
                        }
                        if (price < 0)
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.INVALID_PRICE));
                        }
                        float range;
                        if (
                            !float.TryParse(
                                wasInput(wasKeyValueGet(
                                    wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.RANGE)), message)),
                                out range))
                        {
                            range = Configuration.RANGE;
                        }
                        Primitive primitive = null;
                        if (
                            !FindPrimitive(
                                StringOrUUID(wasInput(wasKeyValueGet(
                                    wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.ITEM)), message))),
                                range,
                                ref primitive, Configuration.SERVICES_TIMEOUT, Configuration.DATA_TIMEOUT))
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.PRIMITIVE_NOT_FOUND));
                        }
                        // if the primitive is not an object (the root) or the primitive 
                        // is not an object as an avatar attachment then bail out
                        if (!primitive.ParentID.Equals(0) && !primitive.ParentID.Equals(Client.Self.LocalID))
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.ITEM_IS_NOT_AN_OBJECT));
                        }
                        FieldInfo saleTypeInfo = typeof (SaleType).GetFields(BindingFlags.Public |
                                                                             BindingFlags.Static)
                            .AsParallel().FirstOrDefault(o =>
                                o.Name.Equals(
                                    wasInput(
                                        wasKeyValueGet(wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.TYPE)),
                                            message)),
                                    StringComparison.Ordinal));
                        Client.Objects.SetSaleInfo(
                            Client.Network.Simulators.FirstOrDefault(o => o.Handle.Equals(primitive.RegionHandle)),
                            primitive.LocalID, saleTypeInfo != null
                                ? (SaleType)
                                    saleTypeInfo.GetValue(null)
                                : SaleType.Copy, price);
                    };
                    break;
                case ScriptKeys.SETOBJECTPOSITION:
                    execute = () =>
                    {
                        if (!HasCorrodePermission(group, (int) Permissions.PERMISSION_INTERACT))
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.NO_CORRADE_PERMISSIONS));
                        }
                        float range;
                        if (
                            !float.TryParse(
                                wasInput(wasKeyValueGet(
                                    wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.RANGE)), message)),
                                out range))
                        {
                            range = Configuration.RANGE;
                        }
                        Primitive primitive = null;
                        if (
                            !FindPrimitive(
                                StringOrUUID(wasInput(wasKeyValueGet(
                                    wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.ITEM)), message))),
                                range,
                                ref primitive, Configuration.SERVICES_TIMEOUT, Configuration.DATA_TIMEOUT))
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.PRIMITIVE_NOT_FOUND));
                        }
                        // if the primitive is not an object (the root) or the primitive 
                        // is not an object as an avatar attachment then bail out
                        if (!primitive.ParentID.Equals(0) && !primitive.ParentID.Equals(Client.Self.LocalID))
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.ITEM_IS_NOT_AN_OBJECT));
                        }
                        Vector3 position;
                        if (
                            !Vector3.TryParse(
                                wasInput(
                                    wasKeyValueGet(wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.POSITION)),
                                        message)),
                                out position))
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.INVALID_POSITION));
                        }
                        Client.Objects.SetPosition(
                            Client.Network.Simulators.FirstOrDefault(o => o.Handle.Equals(primitive.RegionHandle)),
                            primitive.LocalID, position);
                    };
                    break;
                case ScriptKeys.SETPRIMITIVEPOSITION:
                    execute = () =>
                    {
                        if (!HasCorrodePermission(group, (int) Permissions.PERMISSION_INTERACT))
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.NO_CORRADE_PERMISSIONS));
                        }
                        float range;
                        if (
                            !float.TryParse(
                                wasInput(wasKeyValueGet(
                                    wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.RANGE)), message)),
                                out range))
                        {
                            range = Configuration.RANGE;
                        }
                        Primitive primitive = null;
                        if (
                            !FindPrimitive(
                                StringOrUUID(wasInput(wasKeyValueGet(
                                    wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.ITEM)), message))),
                                range,
                                ref primitive, Configuration.SERVICES_TIMEOUT, Configuration.DATA_TIMEOUT))
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.PRIMITIVE_NOT_FOUND));
                        }
                        Vector3 position;
                        if (
                            !Vector3.TryParse(
                                wasInput(
                                    wasKeyValueGet(wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.POSITION)),
                                        message)),
                                out position))
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.INVALID_POSITION));
                        }
                        Client.Objects.SetPosition(
                            Client.Network.Simulators.FirstOrDefault(o => o.Handle.Equals(primitive.RegionHandle)),
                            primitive.LocalID, position, true);
                    };
                    break;
                case ScriptKeys.SETOBJECTROTATION:
                    execute = () =>
                    {
                        if (!HasCorrodePermission(group, (int) Permissions.PERMISSION_INTERACT))
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.NO_CORRADE_PERMISSIONS));
                        }
                        float range;
                        if (
                            !float.TryParse(
                                wasInput(wasKeyValueGet(
                                    wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.RANGE)), message)),
                                out range))
                        {
                            range = Configuration.RANGE;
                        }
                        Primitive primitive = null;
                        if (
                            !FindPrimitive(
                                StringOrUUID(wasInput(wasKeyValueGet(
                                    wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.ITEM)), message))),
                                range,
                                ref primitive, Configuration.SERVICES_TIMEOUT, Configuration.DATA_TIMEOUT))
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.PRIMITIVE_NOT_FOUND));
                        }
                        // if the primitive is not an object (the root) or the primitive 
                        // is not an object as an avatar attachment then bail out
                        if (!primitive.ParentID.Equals(0) && !primitive.ParentID.Equals(Client.Self.LocalID))
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.ITEM_IS_NOT_AN_OBJECT));
                        }
                        Quaternion rotation;
                        if (
                            !Quaternion.TryParse(
                                wasInput(
                                    wasKeyValueGet(wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.ROTATION)),
                                        message)),
                                out rotation))
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.INVALID_ROTATION));
                        }
                        Client.Objects.SetRotation(
                            Client.Network.Simulators.FirstOrDefault(o => o.Handle.Equals(primitive.RegionHandle)),
                            primitive.LocalID, rotation);
                    };
                    break;
                case ScriptKeys.SETPRIMITIVEROTATION:
                    execute = () =>
                    {
                        if (!HasCorrodePermission(group, (int) Permissions.PERMISSION_INTERACT))
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.NO_CORRADE_PERMISSIONS));
                        }
                        float range;
                        if (
                            !float.TryParse(
                                wasInput(wasKeyValueGet(
                                    wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.RANGE)), message)),
                                out range))
                        {
                            range = Configuration.RANGE;
                        }
                        Primitive primitive = null;
                        if (
                            !FindPrimitive(
                                StringOrUUID(wasInput(wasKeyValueGet(
                                    wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.ITEM)), message))),
                                range,
                                ref primitive, Configuration.SERVICES_TIMEOUT, Configuration.DATA_TIMEOUT))
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.PRIMITIVE_NOT_FOUND));
                        }
                        Quaternion rotation;
                        if (
                            !Quaternion.TryParse(
                                wasInput(
                                    wasKeyValueGet(wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.ROTATION)),
                                        message)),
                                out rotation))
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.INVALID_ROTATION));
                        }
                        Client.Objects.SetRotation(
                            Client.Network.Simulators.FirstOrDefault(o => o.Handle.Equals(primitive.RegionHandle)),
                            primitive.LocalID, rotation, true);
                    };
                    break;
                case ScriptKeys.SETOBJECTSCALE:
                    execute = () =>
                    {
                        if (!HasCorrodePermission(group, (int) Permissions.PERMISSION_INTERACT))
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.NO_CORRADE_PERMISSIONS));
                        }
                        float range;
                        if (
                            !float.TryParse(
                                wasInput(wasKeyValueGet(
                                    wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.RANGE)), message)),
                                out range))
                        {
                            range = Configuration.RANGE;
                        }
                        bool uniform;
                        if (
                            !bool.TryParse(
                                wasInput(wasKeyValueGet(
                                    wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.UNIFORM)), message)),
                                out uniform))
                        {
                            uniform = true;
                        }
                        Primitive primitive = null;
                        if (
                            !FindPrimitive(
                                StringOrUUID(wasInput(wasKeyValueGet(
                                    wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.ITEM)), message))),
                                range,
                                ref primitive, Configuration.SERVICES_TIMEOUT, Configuration.DATA_TIMEOUT))
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.PRIMITIVE_NOT_FOUND));
                        }
                        // if the primitive is not an object (the root) or the primitive 
                        // is not an object as an avatar attachment then bail out
                        if (!primitive.ParentID.Equals(0) && !primitive.ParentID.Equals(Client.Self.LocalID))
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.ITEM_IS_NOT_AN_OBJECT));
                        }
                        Vector3 scale;
                        if (
                            !Vector3.TryParse(
                                wasInput(
                                    wasKeyValueGet(wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.SCALE)),
                                        message)),
                                out scale))
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.INVALID_SCALE));
                        }
                        Client.Objects.SetScale(
                            Client.Network.Simulators.FirstOrDefault(o => o.Handle.Equals(primitive.RegionHandle)),
                            primitive.LocalID, scale, false, uniform);
                    };
                    break;
                case ScriptKeys.SETPRIMITIVESCALE:
                    execute = () =>
                    {
                        if (!HasCorrodePermission(group, (int) Permissions.PERMISSION_INTERACT))
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.NO_CORRADE_PERMISSIONS));
                        }
                        float range;
                        if (
                            !float.TryParse(
                                wasInput(wasKeyValueGet(
                                    wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.RANGE)), message)),
                                out range))
                        {
                            range = Configuration.RANGE;
                        }
                        bool uniform;
                        if (
                            !bool.TryParse(
                                wasInput(wasKeyValueGet(
                                    wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.UNIFORM)), message)),
                                out uniform))
                        {
                            uniform = true;
                        }
                        Primitive primitive = null;
                        if (
                            !FindPrimitive(
                                StringOrUUID(wasInput(wasKeyValueGet(
                                    wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.ITEM)), message))),
                                range,
                                ref primitive, Configuration.SERVICES_TIMEOUT, Configuration.DATA_TIMEOUT))
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.PRIMITIVE_NOT_FOUND));
                        }
                        Vector3 scale;
                        if (
                            !Vector3.TryParse(
                                wasInput(
                                    wasKeyValueGet(wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.SCALE)),
                                        message)),
                                out scale))
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.INVALID_SCALE));
                        }
                        Client.Objects.SetScale(
                            Client.Network.Simulators.FirstOrDefault(o => o.Handle.Equals(primitive.RegionHandle)),
                            primitive.LocalID, scale, true, uniform);
                    };
                    break;
                case ScriptKeys.SETPRIMITIVENAME:
                    execute = () =>
                    {
                        if (!HasCorrodePermission(group, (int) Permissions.PERMISSION_INTERACT))
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.NO_CORRADE_PERMISSIONS));
                        }
                        float range;
                        if (
                            !float.TryParse(
                                wasInput(wasKeyValueGet(
                                    wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.RANGE)), message)),
                                out range))
                        {
                            range = Configuration.RANGE;
                        }
                        Primitive primitive = null;
                        if (
                            !FindPrimitive(
                                StringOrUUID(wasInput(wasKeyValueGet(
                                    wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.ITEM)), message))),
                                range,
                                ref primitive, Configuration.SERVICES_TIMEOUT, Configuration.DATA_TIMEOUT))
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.PRIMITIVE_NOT_FOUND));
                        }
                        string name =
                            wasInput(wasKeyValueGet(wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.NAME)),
                                message));
                        if (string.IsNullOrEmpty(name))
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.NO_NAME_PROVIDED));
                        }
                        Client.Objects.SetName(
                            Client.Network.Simulators.FirstOrDefault(o => o.Handle.Equals(primitive.RegionHandle)),
                            primitive.LocalID, name);
                    };
                    break;
                case ScriptKeys.SETPRIMITIVEDESCRIPTION:
                    execute = () =>
                    {
                        if (!HasCorrodePermission(group, (int) Permissions.PERMISSION_INTERACT))
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.NO_CORRADE_PERMISSIONS));
                        }
                        float range;
                        if (
                            !float.TryParse(
                                wasInput(wasKeyValueGet(
                                    wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.RANGE)), message)),
                                out range))
                        {
                            range = Configuration.RANGE;
                        }
                        Primitive primitive = null;
                        if (
                            !FindPrimitive(
                                StringOrUUID(wasInput(wasKeyValueGet(
                                    wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.ITEM)), message))),
                                range,
                                ref primitive, Configuration.SERVICES_TIMEOUT, Configuration.DATA_TIMEOUT))
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.PRIMITIVE_NOT_FOUND));
                        }
                        string description =
                            wasInput(
                                wasKeyValueGet(wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.DESCRIPTION)),
                                    message));
                        if (string.IsNullOrEmpty(description))
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.NO_DESCRIPTION_PROVIDED));
                        }
                        Client.Objects.SetDescription(
                            Client.Network.Simulators.FirstOrDefault(o => o.Handle.Equals(primitive.RegionHandle)),
                            primitive.LocalID, description);
                    };
                    break;
                case ScriptKeys.CHANGEAPPEARANCE:
                    execute = () =>
                    {
                        if (!HasCorrodePermission(group, (int) Permissions.PERMISSION_GROOMING))
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.NO_CORRADE_PERMISSIONS));
                        }
                        string folder =
                            wasInput(wasKeyValueGet(wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.FOLDER)),
                                message));
                        if (string.IsNullOrEmpty(folder))
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.NO_FOLDER_SPECIFIED));
                        }
                        // Check for items that can be worn.
                        List<InventoryBase> items =
                            GetInventoryFolderContents<InventoryBase>(Client.Inventory.Store.RootNode, folder)
                                .AsParallel().Where(CanBeWorn)
                                .ToList();
                        if (items.Count.Equals(0))
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.NO_EQUIPABLE_ITEMS));
                        }
                        // Now remove the current outfit items.
                        Client.Inventory.Store.GetContents(
                            Client.Inventory.FindFolderForType(FolderType.CurrentOutfit)).FindAll(
                                o => CanBeWorn(o) && ((InventoryItem) o).AssetType.Equals(AssetType.Link))
                            .ForEach(p =>
                            {
                                InventoryItem item = ResolveItemLink(p as InventoryItem);
                                if (item is InventoryWearable)
                                {
                                    if (!IsBodyPart(item))
                                    {
                                        UnWear(item);
                                        return;
                                    }
                                    if (items.AsParallel().Any(q =>
                                    {
                                        InventoryWearable i = q as InventoryWearable;
                                        return i != null &&
                                               ((InventoryWearable) item).WearableType.Equals(i.WearableType);
                                    })) UnWear(item);
                                    return;
                                }
                                if (item is InventoryAttachment || item is InventoryObject)
                                {
                                    Detach(item);
                                }
                            });
                        // And equip the specified folder.
                        Parallel.ForEach(items, o =>
                        {
                            InventoryItem item = o as InventoryItem;
                            if (item is InventoryWearable)
                            {
                                Wear(item, false);
                                return;
                            }
                            if (item is InventoryAttachment || item is InventoryObject)
                            {
                                Attach(item, AttachmentPoint.Default, false);
                            }
                        });
                        // And rebake.
                        RebakeTimer.Change(Configuration.REBAKE_DELAY, 0);
                    };
                    break;
                case ScriptKeys.PLAYSOUND:
                    execute = () =>
                    {
                        if (!HasCorrodePermission(group, (int) Permissions.PERMISSION_INTERACT))
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.NO_CORRADE_PERMISSIONS));
                        }
                        Vector3 position;
                        if (
                            !Vector3.TryParse(
                                wasInput(
                                    wasKeyValueGet(wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.POSITION)),
                                        message)),
                                out position))
                        {
                            position = Client.Self.SimPosition;
                        }
                        float gain;
                        if (!float.TryParse(
                            wasInput(wasKeyValueGet(wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.GAIN)),
                                message)),
                            out gain))
                        {
                            gain = 1;
                        }
                        UUID itemUUID;
                        string item =
                            wasInput(wasKeyValueGet(wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.ITEM)),
                                message));
                        if (!UUID.TryParse(item, out itemUUID))
                        {
                            InventoryBase inventoryBaseItem =
                                FindInventory<InventoryBase>(Client.Inventory.Store.RootNode, item
                                    ).FirstOrDefault();
                            if (inventoryBaseItem == null)
                            {
                                throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.INVENTORY_ITEM_NOT_FOUND));
                            }
                            itemUUID = inventoryBaseItem.UUID;
                        }
                        Client.Sound.SendSoundTrigger(itemUUID, position, gain);
                    };
                    break;
                case ScriptKeys.TERRAIN:
                    execute = () =>
                    {
                        if (!HasCorrodePermission(group, (int) Permissions.PERMISSION_LAND))
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.NO_CORRADE_PERMISSIONS));
                        }
                        string region =
                            wasInput(wasKeyValueGet(wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.REGION)),
                                message));
                        Simulator simulator =
                            Client.Network.Simulators.FirstOrDefault(
                                o =>
                                    o.Name.Equals(
                                        string.IsNullOrEmpty(region) ? Client.Network.CurrentSim.Name : region,
                                        StringComparison.InvariantCultureIgnoreCase));
                        if (simulator == null)
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.REGION_NOT_FOUND));
                        }
                        byte[] data = null;
                        switch (wasGetEnumValueFromDescription<Action>(
                            wasInput(wasKeyValueGet(wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.ACTION)),
                                message))
                                .ToLowerInvariant()))
                        {
                            case Action.GET:
                                ManualResetEvent[] DownloadTerrainEvents =
                                {
                                    new ManualResetEvent(false),
                                    new ManualResetEvent(false)
                                };
                                EventHandler<InitiateDownloadEventArgs> InitiateDownloadEventHandler =
                                    (sender, args) =>
                                    {
                                        Client.Assets.RequestAssetXfer(args.SimFileName, false, false, UUID.Zero,
                                            AssetType.Unknown, false);
                                        DownloadTerrainEvents[0].Set();
                                    };
                                EventHandler<XferReceivedEventArgs> XferReceivedEventHandler = (sender, args) =>
                                {
                                    data = args.Xfer.AssetData;
                                    DownloadTerrainEvents[1].Set();
                                };
                                lock (ClientInstanceAssetsLock)
                                {
                                    Client.Assets.InitiateDownload += InitiateDownloadEventHandler;
                                    Client.Assets.XferReceived += XferReceivedEventHandler;
                                    Client.Estate.EstateOwnerMessage("terrain", new List<string>
                                    {
                                        "download filename",
                                        simulator.Name
                                    });
                                    if (!WaitHandle.WaitAll(DownloadTerrainEvents.Select(o => (WaitHandle) o).ToArray(),
                                        Configuration.SERVICES_TIMEOUT, false))
                                    {
                                        Client.Assets.InitiateDownload -= InitiateDownloadEventHandler;
                                        Client.Assets.XferReceived -= XferReceivedEventHandler;
                                        throw new Exception(
                                            wasGetDescriptionFromEnumValue(ScriptError.TIMEOUT_DOWNLOADING_ASSET));
                                    }
                                    Client.Assets.InitiateDownload -= InitiateDownloadEventHandler;
                                    Client.Assets.XferReceived -= XferReceivedEventHandler;
                                }
                                if (data == null || !data.Length.Equals(0))
                                {
                                    throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.EMPTY_ASSET_DATA));
                                }
                                result.Add(wasGetDescriptionFromEnumValue(ResultKeys.DATA), Convert.ToBase64String(data));
                                break;
                            case Action.SET:
                                try
                                {
                                    data = Convert.FromBase64String(
                                        wasInput(
                                            wasKeyValueGet(wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.DATA)),
                                                message)));
                                }
                                catch (Exception)
                                {
                                    throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.INVALID_ASSET_DATA));
                                }
                                if (!data.Length.Equals(0))
                                {
                                    throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.EMPTY_ASSET_DATA));
                                }
                                ManualResetEvent AssetUploadEvent = new ManualResetEvent(false);
                                EventHandler<AssetUploadEventArgs> AssetUploadEventHandler = (sender, args) =>
                                {
                                    if (args.Upload.Transferred.Equals(args.Upload.Size))
                                    {
                                        AssetUploadEvent.Set();
                                    }
                                };
                                lock (ClientInstanceAssetsLock)
                                {
                                    Client.Assets.UploadProgress += AssetUploadEventHandler;
                                    Client.Estate.UploadTerrain(data, simulator.Name);
                                    if (!AssetUploadEvent.WaitOne(Configuration.SERVICES_TIMEOUT, false))
                                    {
                                        Client.Assets.UploadProgress -= AssetUploadEventHandler;
                                        throw new Exception(
                                            wasGetDescriptionFromEnumValue(ScriptError.TIMEOUT_UPLOADING_ASSET));
                                    }
                                    Client.Assets.UploadProgress -= AssetUploadEventHandler;
                                }
                                break;
                            default:
                                throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.UNKNOWN_ACTION));
                        }
                    };
                    break;
                case ScriptKeys.GETTERRAINHEIGHT:
                    execute = () =>
                    {
                        if (!HasCorrodePermission(group, (int) Permissions.PERMISSION_LAND))
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.NO_CORRADE_PERMISSIONS));
                        }
                        string region =
                            wasInput(wasKeyValueGet(wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.REGION)),
                                message));
                        Simulator simulator =
                            Client.Network.Simulators.FirstOrDefault(
                                o =>
                                    o.Name.Equals(
                                        string.IsNullOrEmpty(region) ? Client.Network.CurrentSim.Name : region,
                                        StringComparison.InvariantCultureIgnoreCase));
                        if (simulator == null)
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.REGION_NOT_FOUND));
                        }
                        Vector3 southwest;
                        if (
                            !Vector3.TryParse(
                                wasInput(
                                    wasKeyValueGet(wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.SOUTHWEST)),
                                        message)),
                                out southwest))
                        {
                            southwest = new Vector3(0, 0, 0);
                        }
                        Vector3 northeast;
                        if (
                            !Vector3.TryParse(
                                wasInput(
                                    wasKeyValueGet(wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.NORTHEAST)),
                                        message)),
                                out northeast))
                        {
                            northeast = new Vector3(255, 255, 0);
                        }

                        int x1 = Convert.ToInt32(southwest.X);
                        int y1 = Convert.ToInt32(southwest.Y);
                        int x2 = Convert.ToInt32(northeast.X);
                        int y2 = Convert.ToInt32(northeast.Y);

                        if (x1 > x2)
                        {
                            wasXORSwap(ref x1, ref x2);
                        }
                        if (y1 > y2)
                        {
                            wasXORSwap(ref y1, ref y2);
                        }

                        int sx = x2 - x1 + 1;
                        int sy = y2 - y1 + 1;

                        float[] csv = new float[sx*sy];
                        Parallel.ForEach(Enumerable.Range(x1, sx), x => Parallel.ForEach(Enumerable.Range(y1, sy), y =>
                        {
                            float height;
                            csv[sx*x + y] = simulator.TerrainHeightAtPoint(x, y, out height)
                                ? height
                                : -1;
                        }));

                        if (!csv.Length.Equals(0))
                        {
                            result.Add(wasGetDescriptionFromEnumValue(ResultKeys.DATA),
                                wasEnumerableToCSV(csv.Select(o => o.ToString(CultureInfo.InvariantCulture))));
                        }
                    };
                    break;
                case ScriptKeys.CROUCH:
                    execute = () =>
                    {
                        if (!HasCorrodePermission(group, (int) Permissions.PERMISSION_MOVEMENT))
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.NO_CORRADE_PERMISSIONS));
                        }
                        Action action =
                            wasGetEnumValueFromDescription<Action>(
                                wasInput(
                                    wasKeyValueGet(wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.ACTION)), message))
                                    .ToLowerInvariant());
                        switch (action)
                        {
                            case Action.START:
                            case Action.STOP:
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
                                Client.Self.Crouch(action.Equals(Action.START));
                                break;
                            default:
                                throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.FLY_ACTION_START_OR_STOP));
                        }
                        // Set the camera on the avatar.
                        Client.Self.Movement.Camera.LookAt(
                            Client.Self.SimPosition,
                            Client.Self.SimPosition
                            );
                    };
                    break;
                case ScriptKeys.JUMP:
                    execute = () =>
                    {
                        if (!HasCorrodePermission(group, (int) Permissions.PERMISSION_MOVEMENT))
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.NO_CORRADE_PERMISSIONS));
                        }
                        Action action =
                            wasGetEnumValueFromDescription<Action>(wasInput(
                                wasKeyValueGet(wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.ACTION)), message))
                                .ToLowerInvariant());
                        switch (action)
                        {
                            case Action.START:
                            case Action.STOP:
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
                                Client.Self.Jump(action.Equals(Action.START));
                                break;
                            default:
                                throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.FLY_ACTION_START_OR_STOP));
                        }
                        // Set the camera on the avatar.
                        Client.Self.Movement.Camera.LookAt(
                            Client.Self.SimPosition,
                            Client.Self.SimPosition
                            );
                    };
                    break;
                case ScriptKeys.EXECUTE:
                    execute = () =>
                    {
                        if (!HasCorrodePermission(group, (int) Permissions.PERMISSION_EXECUTE))
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.NO_CORRADE_PERMISSIONS));
                        }
                        string file =
                            wasInput(wasKeyValueGet(wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.FILE)),
                                message));
                        if (string.IsNullOrEmpty(file))
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.NO_EXECUTABLE_FILE_PROVIDED));
                        }
                        ProcessStartInfo p = new ProcessStartInfo(file,
                            wasInput(wasKeyValueGet(
                                wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.PARAMETER)), message)))
                        {
                            RedirectStandardOutput = true,
                            RedirectStandardError = true,
                            WindowStyle = ProcessWindowStyle.Normal,
                            UseShellExecute = false
                        };
                        StringBuilder stdout = new StringBuilder();
                        StringBuilder stderr = new StringBuilder();
                        ManualResetEvent[] StdEvent =
                        {
                            new ManualResetEvent(false),
                            new ManualResetEvent(false)
                        };
                        Process q;
                        try
                        {
                            q = Process.Start(p);
                        }
                        catch (Exception)
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.COULD_NOT_START_PROCESS));
                        }
                        q.OutputDataReceived += (sender, output) =>
                        {
                            if (output.Data == null)
                            {
                                StdEvent[0].Set();
                                return;
                            }
                            stdout.AppendLine(output.Data);
                        };
                        q.ErrorDataReceived += (sender, output) =>
                        {
                            if (output.Data == null)
                            {
                                StdEvent[1].Set();
                                return;
                            }
                            stderr.AppendLine(output.Data);
                        };
                        q.BeginErrorReadLine();
                        q.BeginOutputReadLine();
                        if (!q.WaitForExit(Configuration.SERVICES_TIMEOUT))
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.TIMEOUT_WAITING_FOR_EXECUTION));
                        }
                        if (StdEvent[0].WaitOne(Configuration.SERVICES_TIMEOUT) && !stdout.Length.Equals(0))
                        {
                            result.Add(wasGetDescriptionFromEnumValue(ResultKeys.DATA), stdout.ToString());
                        }
                        if (StdEvent[1].WaitOne(Configuration.SERVICES_TIMEOUT) && !stderr.Length.Equals(0))
                        {
                            result.Add(wasGetDescriptionFromEnumValue(ResultKeys.DATA), stderr.ToString());
                        }
                    };
                    break;
                case ScriptKeys.CONFIGURATION:
                    execute = () =>
                    {
                        if (!HasCorrodePermission(group, (int) Permissions.PERMISSION_SYSTEM))
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.NO_CORRADE_PERMISSIONS));
                        }
                        Action action = wasGetEnumValueFromDescription<Action>(wasInput(
                            wasKeyValueGet(wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.ACTION)), message))
                            .ToLowerInvariant());
                        switch (action)
                        {
                            case Action.READ:
                                try
                                {
                                    result.Add(wasGetDescriptionFromEnumValue(ResultKeys.DATA),
                                        Configuration.Read(CORRADE_CONSTANTS.CONFIGURATION_FILE));
                                }
                                catch (Exception)
                                {
                                    throw new Exception(
                                        wasGetDescriptionFromEnumValue(ScriptError.UNABLE_TO_LOAD_CONFIGURATION));
                                }
                                break;
                            case Action.WRITE:
                                try
                                {
                                    Configuration.Write(CORRADE_CONSTANTS.CONFIGURATION_FILE, wasKeyValueGet(
                                        wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.DATA)),
                                        message));
                                }
                                catch (Exception)
                                {
                                    throw new Exception(
                                        wasGetDescriptionFromEnumValue(ScriptError.UNABLE_TO_SAVE_CONFIGURATION));
                                }
                                break;
                            case Action.SET:
                            case Action.GET:
                                string path =
                                    wasInput(wasKeyValueGet(wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.PATH)),
                                        message));
                                if (string.IsNullOrEmpty(path))
                                {
                                    throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.NO_PATH_PROVIDED));
                                }
                                XmlDocument conf = new XmlDocument();
                                try
                                {
                                    conf.LoadXml(Configuration.Read(CORRADE_CONSTANTS.CONFIGURATION_FILE));
                                }
                                catch (Exception)
                                {
                                    throw new Exception(
                                        wasGetDescriptionFromEnumValue(ScriptError.UNABLE_TO_LOAD_CONFIGURATION));
                                }
                                string data;
                                switch (action)
                                {
                                    case Action.GET:
                                        try
                                        {
                                            data = conf.SelectSingleNode(path).InnerXml;
                                        }
                                        catch (Exception)
                                        {
                                            throw new Exception(
                                                wasGetDescriptionFromEnumValue(ScriptError.INVALID_XML_PATH));
                                        }
                                        if (!string.IsNullOrEmpty(data))
                                        {
                                            result.Add(wasGetDescriptionFromEnumValue(ResultKeys.DATA), data);
                                        }
                                        break;
                                    case Action.SET:
                                        data =
                                            wasInput(
                                                wasKeyValueGet(
                                                    wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.DATA)),
                                                    message));
                                        if (string.IsNullOrEmpty(data))
                                        {
                                            throw new Exception(
                                                wasGetDescriptionFromEnumValue(ScriptError.NO_DATA_PROVIDED));
                                        }
                                        try
                                        {
                                            conf.SelectSingleNode(path).InnerXml = data;
                                        }
                                        catch (Exception)
                                        {
                                            throw new Exception(
                                                wasGetDescriptionFromEnumValue(ScriptError.INVALID_XML_PATH));
                                        }
                                        try
                                        {
                                            Configuration.Write(CORRADE_CONSTANTS.CONFIGURATION_FILE, conf);
                                        }
                                        catch (Exception)
                                        {
                                            throw new Exception(
                                                wasGetDescriptionFromEnumValue(ScriptError.UNABLE_TO_SAVE_CONFIGURATION));
                                        }
                                        break;
                                }
                                break;
                            default:
                                throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.UNKNOWN_ACTION));
                        }
                    };
                    break;
                case ScriptKeys.CACHE:
                    execute = () =>
                    {
                        if (!HasCorrodePermission(group, (int) Permissions.PERMISSION_SYSTEM))
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.NO_CORRADE_PERMISSIONS));
                        }
                        switch (wasGetEnumValueFromDescription<Action>(wasInput(
                            wasKeyValueGet(wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.ACTION)), message))
                            .ToLowerInvariant()))
                        {
                            case Action.PURGE:
                                Cache.Purge();
                                break;
                            case Action.SAVE:
                                SaveCorrodeCache.Invoke();
                                break;
                            case Action.LOAD:
                                LoadCorrodeCache.Invoke();
                                break;
                            default:
                                throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.UNKNOWN_ACTION));
                        }
                    };
                    break;
                case ScriptKeys.LOGOUT:
                    execute = () =>
                    {
                        if (!HasCorrodePermission(group, (int) Permissions.PERMISSION_SYSTEM))
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.NO_CORRADE_PERMISSIONS));
                        }
                        ConnectionSemaphores['u'].Set();
                    };
                    break;
                case ScriptKeys.RLV:
                    execute = () =>
                    {
                        if (!HasCorrodePermission(group, (int) Permissions.PERMISSION_SYSTEM))
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.NO_CORRADE_PERMISSIONS));
                        }
                        switch (wasGetEnumValueFromDescription<Action>(
                            wasInput(
                                wasKeyValueGet(wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.ACTION)),
                                    message)).ToLowerInvariant()))
                        {
                            case Action.ENABLE:
                                EnableRLV = true;
                                break;
                            case Action.DISABLE:
                                EnableRLV = false;
                                lock (RLVRulesLock)
                                {
                                    RLVRules.Clear();
                                }
                                break;
                        }
                    };
                    break;
                case ScriptKeys.FILTER:
                    execute = () =>
                    {
                        if (!HasCorrodePermission(group, (int) Permissions.PERMISSION_FILTER))
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.NO_CORRADE_PERMISSIONS));
                        }
                        switch (wasGetEnumValueFromDescription<Action>(
                            wasInput(
                                wasKeyValueGet(wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.ACTION)),
                                    message)).ToLowerInvariant()))
                        {
                            case Action.SET:
                                List<Filter> inputFilters = new List<Filter>();
                                string input =
                                    wasInput(wasKeyValueGet(wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.INPUT)),
                                        message));
                                if (!string.IsNullOrEmpty(input))
                                {
                                    foreach (
                                        KeyValuePair<string, string> i in
                                            wasCSVToEnumerable(input).AsParallel().Select((o, p) => new {o, p})
                                                .GroupBy(q => q.p/2, q => q.o)
                                                .Select(o => o.ToList())
                                                .TakeWhile(o => o.Count%2 == 0)
                                                .ToDictionary(o => o.First(), p => p.Last()))
                                    {
                                        inputFilters.Add(wasGetEnumValueFromDescription<Filter>(i.Key));
                                        inputFilters.Add(wasGetEnumValueFromDescription<Filter>(i.Value));
                                    }
                                    lock (InputFiltersLock)
                                    {
                                        Configuration.INPUT_FILTERS = inputFilters;
                                    }
                                }
                                List<Filter> outputFilters = new List<Filter>();
                                string output =
                                    wasInput(wasKeyValueGet(
                                        wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.OUTPUT)),
                                        message));
                                if (!string.IsNullOrEmpty(output))
                                {
                                    foreach (
                                        KeyValuePair<string, string> i in
                                            wasCSVToEnumerable(output).AsParallel().Select((o, p) => new {o, p})
                                                .GroupBy(q => q.p/2, q => q.o)
                                                .Select(o => o.ToList())
                                                .TakeWhile(o => o.Count%2 == 0)
                                                .ToDictionary(o => o.First(), p => p.Last()))
                                    {
                                        outputFilters.Add(wasGetEnumValueFromDescription<Filter>(i.Key));
                                        outputFilters.Add(wasGetEnumValueFromDescription<Filter>(i.Value));
                                    }
                                    lock (OutputFiltersLock)
                                    {
                                        Configuration.OUTPUT_FILTERS = outputFilters;
                                    }
                                }
                                break;
                            case Action.GET:
                                switch (wasGetEnumValueFromDescription<Type>(
                                    wasInput(
                                        wasKeyValueGet(wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.TYPE)),
                                            message)).ToLowerInvariant()))
                                {
                                    case Type.INPUT:
                                        lock (InputFiltersLock)
                                        {
                                            if (!Configuration.INPUT_FILTERS.Count.Equals(0))
                                            {
                                                result.Add(wasGetDescriptionFromEnumValue(ResultKeys.DATA),
                                                    wasEnumerableToCSV(Configuration.INPUT_FILTERS.Select(
                                                        o => wasGetDescriptionFromEnumValue(o))));
                                            }
                                        }
                                        break;
                                    case Type.OUTPUT:
                                        lock (OutputFiltersLock)
                                        {
                                            if (!Configuration.OUTPUT_FILTERS.Count.Equals(0))
                                            {
                                                result.Add(wasGetDescriptionFromEnumValue(ResultKeys.DATA),
                                                    wasEnumerableToCSV(Configuration.OUTPUT_FILTERS.Select(
                                                        o => wasGetDescriptionFromEnumValue(o))));
                                            }
                                        }
                                        break;
                                }
                                break;
                        }
                    };
                    break;
                case ScriptKeys.INVENTORY:
                    execute = () =>
                    {
                        if (!HasCorrodePermission(group, (int) Permissions.PERMISSION_INVENTORY))
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.NO_CORRADE_PERMISSIONS));
                        }
                        Group configuredGroup =
                            Configuration.GROUPS.AsParallel().FirstOrDefault(
                                o => o.Name.Equals(group, StringComparison.Ordinal));
                        UUID groupUUID = UUID.Zero;
                        switch (!configuredGroup.Equals(default(Group)))
                        {
                            case true:
                                groupUUID = configuredGroup.UUID;
                                break;
                            default:
                                if (!GroupNameToUUID(group, Configuration.SERVICES_TIMEOUT, Configuration.DATA_TIMEOUT,
                                    ref groupUUID))
                                {
                                    throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.GROUP_NOT_FOUND));
                                }
                                break;
                        }
                        lock (GroupDirectoryTrackersLock)
                        {
                            if (!GroupDirectoryTrackers.Contains(groupUUID))
                            {
                                GroupDirectoryTrackers.Add(groupUUID, Client.Inventory.Store.RootFolder);
                            }
                        }
                        string path =
                            wasInput(wasKeyValueGet(wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.PATH)),
                                message));
                        Func<string, InventoryBase, InventoryBase> findPath = null;
                        findPath = (o, p) =>
                        {
                            if (string.IsNullOrEmpty(o)) return p;

                            // Split all paths.
                            string[] unpack = o.Split(CORRADE_CONSTANTS.PATH_SEPARATOR[0]);
                            // Pop first item to process.
                            string first = unpack.First();
                            // Remove item.
                            unpack = unpack.AsParallel().Where(q => !q.Equals(first)).ToArray();

                            InventoryBase next = p;

                            // Avoid preceeding slashes.
                            if (string.IsNullOrEmpty(first)) goto CONTINUE;

                            HashSet<InventoryBase> contents =
                                new HashSet<InventoryBase>(Client.Inventory.Store.GetContents(p.UUID));
                            try
                            {
                                UUID itemUUID;
                                switch (!UUID.TryParse(first, out itemUUID))
                                {
                                    case true:
                                        next = contents.SingleOrDefault(q => q.Name.Equals(first));
                                        break;
                                    default:
                                        next = contents.SingleOrDefault(q => q.UUID.Equals(itemUUID));
                                        break;
                                }
                            }
                            catch (Exception)
                            {
                                throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.AMBIGUOUS_PATH));
                            }

                            if (next == null || next.Equals(default(InventoryBase)))
                            {
                                throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.PATH_NOT_FOUND));
                            }

                            if (!(next is InventoryFolder))
                            {
                                return next;
                            }

                            CONTINUE:
                            return findPath(string.Join(CORRADE_CONSTANTS.PATH_SEPARATOR, unpack),
                                Client.Inventory.Store[next.UUID]);
                        };
                        InventoryBase item;
                        List<string> csv = new List<string>();
                        Action action = wasGetEnumValueFromDescription<Action>(
                            wasInput(
                                wasKeyValueGet(wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.ACTION)), message))
                                .ToLowerInvariant());
                        switch (action)
                        {
                            case Action.LS:
                                switch (!string.IsNullOrEmpty(path))
                                {
                                    case true:
                                        if (path[0].Equals(CORRADE_CONSTANTS.PATH_SEPARATOR[0]))
                                        {
                                            item = Client.Inventory.Store.RootFolder;
                                            break;
                                        }
                                        goto default;
                                    default:
                                        lock (GroupDirectoryTrackersLock)
                                        {
                                            item = GroupDirectoryTrackers[groupUUID] as InventoryBase;
                                        }
                                        break;
                                }
                                item = findPath(path, item);
                                switch (item is InventoryFolder)
                                {
                                    case true:
                                        foreach (DirItem dirItem in Client.Inventory.Store.GetContents(
                                            item.UUID).AsParallel().Select(
                                                o => DirItem.FromInventoryBase(o)))
                                        {
                                            csv.AddRange(new[]
                                            {wasGetStructureMemberDescription(dirItem, dirItem.Name), dirItem.Name});
                                            csv.AddRange(new[]
                                            {
                                                wasGetStructureMemberDescription(dirItem, dirItem.Item),
                                                dirItem.Item.ToString()
                                            });
                                            csv.AddRange(new[]
                                            {
                                                wasGetStructureMemberDescription(dirItem, dirItem.Type),
                                                wasGetDescriptionFromEnumValue(dirItem.Type)
                                            });
                                            csv.AddRange(new[]
                                            {
                                                wasGetStructureMemberDescription(dirItem, dirItem.Permissions),
                                                dirItem.Permissions
                                            });
                                        }
                                        break;
                                    case false:
                                        DirItem dir = DirItem.FromInventoryBase(item);
                                        csv.AddRange(new[] {wasGetStructureMemberDescription(dir, dir.Name), dir.Name});
                                        csv.AddRange(new[]
                                        {
                                            wasGetStructureMemberDescription(dir, dir.Item),
                                            dir.Item.ToString()
                                        });
                                        csv.AddRange(new[]
                                        {
                                            wasGetStructureMemberDescription(dir, dir.Type),
                                            wasGetDescriptionFromEnumValue(dir.Type)
                                        });
                                        csv.AddRange(new[]
                                        {
                                            wasGetStructureMemberDescription(dir, dir.Permissions),
                                            dir.Permissions
                                        });
                                        break;
                                }
                                break;
                            case Action.CWD:
                                lock (GroupDirectoryTrackersLock)
                                {
                                    DirItem dirItem =
                                        DirItem.FromInventoryBase(
                                            GroupDirectoryTrackers[groupUUID] as InventoryBase);
                                    csv.AddRange(new[]
                                    {wasGetStructureMemberDescription(dirItem, dirItem.Name), dirItem.Name});
                                    csv.AddRange(new[]
                                    {wasGetStructureMemberDescription(dirItem, dirItem.Item), dirItem.Item.ToString()});
                                    csv.AddRange(new[]
                                    {
                                        wasGetStructureMemberDescription(dirItem, dirItem.Type),
                                        wasGetDescriptionFromEnumValue(dirItem.Type)
                                    });
                                    csv.AddRange(new[]
                                    {
                                        wasGetStructureMemberDescription(dirItem, dirItem.Permissions),
                                        dirItem.Permissions
                                    });
                                }
                                break;
                            case Action.CD:
                                if (string.IsNullOrEmpty(path))
                                {
                                    throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.NO_PATH_PROVIDED));
                                }
                                switch (!path[0].Equals(CORRADE_CONSTANTS.PATH_SEPARATOR[0]))
                                {
                                    case true:
                                        lock (GroupDirectoryTrackersLock)
                                        {
                                            item = GroupDirectoryTrackers[groupUUID] as InventoryBase;
                                        }
                                        break;
                                    default:
                                        item = Client.Inventory.Store.RootFolder;
                                        break;
                                }
                                item = findPath(path, item);
                                if (!(item is InventoryFolder))
                                {
                                    throw new Exception(
                                        wasGetDescriptionFromEnumValue(ScriptError.UNEXPECTED_ITEM_IN_PATH));
                                }
                                lock (GroupDirectoryTrackersLock)
                                {
                                    GroupDirectoryTrackers[groupUUID] = item;
                                }
                                break;
                            case Action.MKDIR:
                                string mkdirName =
                                    wasInput(wasKeyValueGet(wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.NAME)),
                                        message));
                                if (string.IsNullOrEmpty(mkdirName))
                                {
                                    throw new Exception(
                                        wasGetDescriptionFromEnumValue(ScriptError.NO_NAME_PROVIDED));
                                }
                                switch (!string.IsNullOrEmpty(path))
                                {
                                    case true:
                                        if (path[0].Equals(CORRADE_CONSTANTS.PATH_SEPARATOR[0]))
                                        {
                                            item = Client.Inventory.Store.RootFolder;
                                            break;
                                        }
                                        goto default;
                                    default:
                                        lock (GroupDirectoryTrackersLock)
                                        {
                                            item = GroupDirectoryTrackers[groupUUID] as InventoryBase;
                                        }
                                        break;
                                }
                                item = findPath(path, item);
                                if (!(item is InventoryFolder))
                                {
                                    throw new Exception(
                                        wasGetDescriptionFromEnumValue(ScriptError.UNEXPECTED_ITEM_IN_PATH));
                                }
                                if (Client.Inventory.CreateFolder(item.UUID, mkdirName) == UUID.Zero)
                                {
                                    throw new Exception(
                                        wasGetDescriptionFromEnumValue(ScriptError.UNABLE_TO_CREATE_FOLDER));
                                }
                                UpdateInventoryRecursive.Invoke(Client.Inventory.Store.RootFolder);
                                break;
                            case Action.CHMOD:
                                string itemPermissions =
                                    wasInput(
                                        wasKeyValueGet(
                                            wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.PERMISSIONS)), message));
                                if (string.IsNullOrEmpty(itemPermissions))
                                {
                                    throw new Exception(
                                        wasGetDescriptionFromEnumValue(ScriptError.NO_PERMISSIONS_PROVIDED));
                                }
                                switch (!string.IsNullOrEmpty(path))
                                {
                                    case true:
                                        if (path[0].Equals(CORRADE_CONSTANTS.PATH_SEPARATOR[0]))
                                        {
                                            item = Client.Inventory.Store.RootFolder;
                                            break;
                                        }
                                        goto default;
                                    default:
                                        lock (GroupDirectoryTrackersLock)
                                        {
                                            item = GroupDirectoryTrackers[groupUUID] as InventoryBase;
                                        }
                                        break;
                                }
                                item = findPath(path, item);
                                Action<InventoryItem, string> setPermissions = (o, p) =>
                                {
                                    OpenMetaverse.Permissions permissions = wasStringToPermissions(p);
                                    o.Permissions = permissions;
                                    Client.Inventory.RequestUpdateItem(o);
                                    bool succeeded = false;
                                    ManualResetEvent ItemReceivedEvent = new ManualResetEvent(false);
                                    EventHandler<ItemReceivedEventArgs> ItemReceivedEventHandler =
                                        (sender, args) =>
                                        {
                                            if (!args.Item.UUID.Equals(o.UUID)) return;
                                            succeeded = args.Item.Permissions.Equals(permissions);
                                            ItemReceivedEvent.Set();
                                        };
                                    lock (ClientInstanceInventoryLock)
                                    {
                                        Client.Inventory.ItemReceived += ItemReceivedEventHandler;
                                        Client.Inventory.RequestFetchInventory(o.UUID, o.OwnerID);
                                        if (!ItemReceivedEvent.WaitOne(Configuration.SERVICES_TIMEOUT, false))
                                        {
                                            Client.Inventory.ItemReceived -= ItemReceivedEventHandler;
                                            throw new Exception(
                                                wasGetDescriptionFromEnumValue(ScriptError.TIMEOUT_RETRIEVING_ITEM));
                                        }
                                        Client.Inventory.ItemReceived -= ItemReceivedEventHandler;
                                    }
                                    if (!succeeded)
                                    {
                                        throw new Exception(
                                            wasGetDescriptionFromEnumValue(ScriptError.SETTING_PERMISSIONS_FAILED));
                                    }
                                };
                                switch (item is InventoryFolder)
                                {
                                    case true:
                                        foreach (InventoryItem inventoryItem in Client.Inventory.Store.GetContents(
                                            item.UUID).OfType<InventoryItem>())
                                        {
                                            setPermissions.Invoke(inventoryItem, itemPermissions);
                                        }
                                        break;
                                    default:
                                        setPermissions.Invoke(item as InventoryItem, itemPermissions);
                                        break;
                                }
                                UpdateInventoryRecursive.Invoke(Client.Inventory.Store.RootFolder);
                                break;
                            case Action.RM:
                                switch (!string.IsNullOrEmpty(path))
                                {
                                    case true:
                                        if (path[0].Equals(CORRADE_CONSTANTS.PATH_SEPARATOR[0]))
                                        {
                                            item = Client.Inventory.Store.RootFolder;
                                            break;
                                        }
                                        goto default;
                                    default:
                                        lock (GroupDirectoryTrackersLock)
                                        {
                                            item = GroupDirectoryTrackers[groupUUID] as InventoryBase;
                                        }
                                        break;
                                }
                                item = findPath(path, item);
                                switch (item is InventoryFolder)
                                {
                                    case true:
                                        Client.Inventory.MoveFolder(item.UUID,
                                            Client.Inventory.FindFolderForType(FolderType.Trash));
                                        break;
                                    default:
                                        Client.Inventory.MoveItem(item.UUID,
                                            Client.Inventory.FindFolderForType(FolderType.Trash));
                                        break;
                                }
                                UpdateInventoryRecursive.Invoke(Client.Inventory.Store.RootFolder);
                                break;
                            case Action.CP:
                            case Action.MV:
                            case Action.LN:
                                string lnSourcePath =
                                    wasInput(wasKeyValueGet(
                                        wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.SOURCE)),
                                        message));
                                InventoryBase sourceItem;
                                switch (!string.IsNullOrEmpty(lnSourcePath))
                                {
                                    case true:
                                        if (lnSourcePath[0].Equals(CORRADE_CONSTANTS.PATH_SEPARATOR[0]))
                                        {
                                            sourceItem = Client.Inventory.Store.RootFolder;
                                            break;
                                        }
                                        goto default;
                                    default:
                                        lock (GroupDirectoryTrackersLock)
                                        {
                                            sourceItem = GroupDirectoryTrackers[groupUUID] as InventoryBase;
                                        }
                                        break;
                                }
                                sourceItem = findPath(lnSourcePath, sourceItem);
                                switch (action)
                                {
                                    case Action.CP:
                                    case Action.LN:
                                        if (sourceItem is InventoryFolder)
                                        {
                                            throw new Exception(
                                                wasGetDescriptionFromEnumValue(ScriptError.EXPECTED_ITEM_AS_SOURCE));
                                        }
                                        break;
                                }
                                string lnTargetPath =
                                    wasInput(wasKeyValueGet(
                                        wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.TARGET)),
                                        message));
                                InventoryBase targetItem;
                                switch (!string.IsNullOrEmpty(lnTargetPath))
                                {
                                    case true:
                                        if (lnTargetPath[0].Equals(CORRADE_CONSTANTS.PATH_SEPARATOR[0]))
                                        {
                                            targetItem = Client.Inventory.Store.RootFolder;
                                            break;
                                        }
                                        goto default;
                                    default:
                                        lock (GroupDirectoryTrackersLock)
                                        {
                                            targetItem = GroupDirectoryTrackers[groupUUID] as InventoryBase;
                                        }
                                        break;
                                }
                                targetItem = findPath(lnTargetPath, targetItem);
                                if (!(targetItem is InventoryFolder))
                                {
                                    throw new Exception(
                                        wasGetDescriptionFromEnumValue(ScriptError.EXPECTED_FOLDER_AS_TARGET));
                                }
                                switch (action)
                                {
                                    case Action.LN:
                                        Client.Inventory.CreateLink(targetItem.UUID, sourceItem, (succeeded, newItem) =>
                                        {
                                            if (!succeeded)
                                            {
                                                throw new Exception(
                                                    wasGetDescriptionFromEnumValue(ScriptError.UNABLE_TO_CREATE_ITEM));
                                            }
                                            Client.Inventory.RequestFetchInventory(newItem.UUID, newItem.OwnerID);
                                        });
                                        break;
                                    case Action.MV:
                                        switch (sourceItem is InventoryFolder)
                                        {
                                            case true:
                                                Client.Inventory.MoveFolder(sourceItem.UUID, targetItem.UUID);
                                                break;
                                            default:
                                                Client.Inventory.MoveItem(sourceItem.UUID, targetItem.UUID);
                                                break;
                                        }
                                        break;
                                    case Action.CP:
                                        Client.Inventory.RequestCopyItem(sourceItem.UUID, targetItem.UUID,
                                            sourceItem.Name,
                                            newItem =>
                                            {
                                                if (newItem == null)
                                                {
                                                    throw new Exception(
                                                        wasGetDescriptionFromEnumValue(ScriptError.UNABLE_TO_CREATE_ITEM));
                                                }
                                            });
                                        break;
                                }
                                UpdateInventoryRecursive.Invoke(Client.Inventory.Store.RootFolder);
                                break;
                            default:
                                throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.UNKNOWN_ACTION));
                        }
                        if (!csv.Count.Equals(0))
                        {
                            result.Add(wasGetDescriptionFromEnumValue(ResultKeys.DATA),
                                wasEnumerableToCSV(csv));
                        }
                    };
                    break;
                case ScriptKeys.GETAVATARSDATA:
                    execute = () =>
                    {
                        if (
                            !HasCorrodePermission(group,
                                (int) Permissions.PERMISSION_INTERACT))
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.NO_CORRADE_PERMISSIONS));
                        }
                        float range;
                        if (
                            !float.TryParse(
                                wasInput(wasKeyValueGet(
                                    wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.RANGE)), message)),
                                out range))
                        {
                            range = Configuration.RANGE;
                        }
                        HashSet<Avatar> avatars = new HashSet<Avatar>();
                        object LockObject = new object();
                        switch (wasGetEnumValueFromDescription<Entity>(
                            wasInput(
                                wasKeyValueGet(wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.ENTITY)), message))
                                .ToLowerInvariant()))
                        {
                            case Entity.RANGE:
                                Parallel.ForEach(
                                    GetAvatars(range, Configuration.SERVICES_TIMEOUT, Configuration.DATA_TIMEOUT)
                                        .AsParallel()
                                        .Where(o => Vector3.Distance(o.Position, Client.Self.SimPosition) <= range),
                                    o =>
                                    {
                                        lock (LockObject)
                                        {
                                            avatars.Add(o);
                                        }
                                    });
                                break;
                            case Entity.PARCEL:
                                Vector3 position;
                                if (
                                    !Vector3.TryParse(
                                        wasInput(
                                            wasKeyValueGet(
                                                wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.POSITION)),
                                                message)),
                                        out position))
                                {
                                    position = Client.Self.SimPosition;
                                }
                                Parcel parcel = null;
                                if (
                                    !GetParcelAtPosition(Client.Network.CurrentSim, position, ref parcel))
                                {
                                    throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.COULD_NOT_FIND_PARCEL));
                                }
                                Parallel.ForEach(GetAvatars(new[]
                                {
                                    Vector3.Distance(Client.Self.SimPosition, parcel.AABBMin),
                                    Vector3.Distance(Client.Self.SimPosition, parcel.AABBMax),
                                    Vector3.Distance(Client.Self.SimPosition,
                                        new Vector3(parcel.AABBMin.X, parcel.AABBMax.Y, 0)),
                                    Vector3.Distance(Client.Self.SimPosition,
                                        new Vector3(parcel.AABBMax.X, parcel.AABBMin.Y, 0))
                                }.Max(), Configuration.SERVICES_TIMEOUT, Configuration.DATA_TIMEOUT)
                                    .AsParallel()
                                    .Where(o => IsVectorInParcel(o.Position, parcel)), o =>
                                    {
                                        lock (LockObject)
                                        {
                                            avatars.Add(o);
                                        }
                                    });
                                break;
                            case Entity.REGION:
                                // Get all sim parcels
                                ManualResetEvent SimParcelsDownloadedEvent = new ManualResetEvent(false);
                                EventHandler<SimParcelsDownloadedEventArgs> SimParcelsDownloadedEventHandler =
                                    (sender, args) => SimParcelsDownloadedEvent.Set();
                                lock (ClientInstanceParcelsLock)
                                {
                                    Client.Parcels.SimParcelsDownloaded += SimParcelsDownloadedEventHandler;
                                    Client.Parcels.RequestAllSimParcels(Client.Network.CurrentSim);
                                    if (Client.Network.CurrentSim.IsParcelMapFull())
                                    {
                                        SimParcelsDownloadedEvent.Set();
                                    }
                                    if (!SimParcelsDownloadedEvent.WaitOne(Configuration.SERVICES_TIMEOUT, false))
                                    {
                                        Client.Parcels.SimParcelsDownloaded -= SimParcelsDownloadedEventHandler;
                                        throw new Exception(
                                            wasGetDescriptionFromEnumValue(ScriptError.TIMEOUT_GETTING_PARCELS));
                                    }
                                    Client.Parcels.SimParcelsDownloaded -= SimParcelsDownloadedEventHandler;
                                }
                                HashSet<Parcel> regionParcels =
                                    new HashSet<Parcel>(Client.Network.CurrentSim.Parcels.Copy().Values);
                                Parallel.ForEach(
                                    GetAvatars(
                                        regionParcels.AsParallel().Select(o => new[]
                                        {
                                            Vector3.Distance(Client.Self.SimPosition, o.AABBMin),
                                            Vector3.Distance(Client.Self.SimPosition, o.AABBMax),
                                            Vector3.Distance(Client.Self.SimPosition,
                                                new Vector3(o.AABBMin.X, o.AABBMax.Y, 0)),
                                            Vector3.Distance(Client.Self.SimPosition,
                                                new Vector3(o.AABBMax.X, o.AABBMin.Y, 0))
                                        }.Max()).Max(), Configuration.SERVICES_TIMEOUT, Configuration.DATA_TIMEOUT)
                                        .AsParallel()
                                        .Where(o => regionParcels.AsParallel().Any(p => IsVectorInParcel(o.Position, p))),
                                    o =>
                                    {
                                        lock (LockObject)
                                        {
                                            avatars.Add(o);
                                        }
                                    });
                                break;
                            case Entity.AVATAR:
                                UUID agentUUID = UUID.Zero;
                                if (
                                    !UUID.TryParse(
                                        wasInput(
                                            wasKeyValueGet(wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.AGENT)),
                                                message)), out agentUUID) && !AgentNameToUUID(
                                                    wasInput(
                                                        wasKeyValueGet(
                                                            wasOutput(
                                                                wasGetDescriptionFromEnumValue(ScriptKeys.FIRSTNAME)),
                                                            message)),
                                                    wasInput(
                                                        wasKeyValueGet(
                                                            wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.LASTNAME)),
                                                            message)),
                                                    Configuration.SERVICES_TIMEOUT, Configuration.DATA_TIMEOUT,
                                                    ref agentUUID))
                                {
                                    throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.AGENT_NOT_FOUND));
                                }
                                Avatar avatar = GetAvatars(range, Configuration.SERVICES_TIMEOUT,
                                    Configuration.DATA_TIMEOUT).AsParallel().FirstOrDefault(o => o.ID.Equals(agentUUID));
                                if (avatar == null)
                                    throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.AVATAR_NOT_IN_RANGE));
                                avatars.Add(avatar);
                                break;
                            default:
                                throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.UNKNOWN_ENTITY));
                        }

                        // allow partial results
                        UpdateAvatars(ref avatars, Configuration.SERVICES_TIMEOUT, Configuration.DATA_TIMEOUT);

                        List<string> data = new List<string>();

                        Parallel.ForEach(avatars, o =>
                        {
                            lock (LockObject)
                            {
                                data.AddRange(GetStructuredData(o,
                                    wasInput(
                                        wasKeyValueGet(
                                            wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.DATA)),
                                            message)))
                                    );
                            }
                        });
                        if (!data.Count.Equals(0))
                        {
                            result.Add(wasGetDescriptionFromEnumValue(ResultKeys.DATA),
                                wasEnumerableToCSV(data));
                        }
                    };
                    break;
                case ScriptKeys.GETPRIMITIVESDATA:
                    execute = () =>
                    {
                        if (
                            !HasCorrodePermission(group,
                                (int) Permissions.PERMISSION_INTERACT))
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.NO_CORRADE_PERMISSIONS));
                        }
                        float range;
                        if (
                            !float.TryParse(
                                wasInput(wasKeyValueGet(
                                    wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.RANGE)), message)),
                                out range))
                        {
                            range = Configuration.RANGE;
                        }
                        HashSet<Primitive> updatePrimitives = new HashSet<Primitive>();
                        object LockObject = new object();
                        switch (wasGetEnumValueFromDescription<Entity>(
                            wasInput(
                                wasKeyValueGet(wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.ENTITY)), message))
                                .ToLowerInvariant()))
                        {
                            case Entity.RANGE:
                                Parallel.ForEach(
                                    GetPrimitives(range, Configuration.SERVICES_TIMEOUT, Configuration.DATA_TIMEOUT)
                                        .AsParallel()
                                        .Where(o => Vector3.Distance(o.Position, Client.Self.SimPosition) <= range),
                                    o =>
                                    {
                                        lock (LockObject)
                                        {
                                            updatePrimitives.Add(o);
                                        }
                                    });
                                break;
                            case Entity.PARCEL:
                                Vector3 position;
                                if (
                                    !Vector3.TryParse(
                                        wasInput(
                                            wasKeyValueGet(
                                                wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.POSITION)),
                                                message)),
                                        out position))
                                {
                                    position = Client.Self.SimPosition;
                                }
                                Parcel parcel = null;
                                if (
                                    !GetParcelAtPosition(Client.Network.CurrentSim, position, ref parcel))
                                {
                                    throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.COULD_NOT_FIND_PARCEL));
                                }
                                Parallel.ForEach(GetPrimitives(new[]
                                {
                                    Vector3.Distance(Client.Self.SimPosition, parcel.AABBMin),
                                    Vector3.Distance(Client.Self.SimPosition, parcel.AABBMax),
                                    Vector3.Distance(Client.Self.SimPosition,
                                        new Vector3(parcel.AABBMin.X, parcel.AABBMax.Y, 0)),
                                    Vector3.Distance(Client.Self.SimPosition,
                                        new Vector3(parcel.AABBMax.X, parcel.AABBMin.Y, 0))
                                }.Max(), Configuration.SERVICES_TIMEOUT, Configuration.DATA_TIMEOUT), o =>
                                {
                                    lock (LockObject)
                                    {
                                        updatePrimitives.Add(o);
                                    }
                                });
                                break;
                            case Entity.REGION:
                                // Get all sim parcels
                                ManualResetEvent SimParcelsDownloadedEvent = new ManualResetEvent(false);
                                EventHandler<SimParcelsDownloadedEventArgs> SimParcelsDownloadedEventHandler =
                                    (sender, args) => SimParcelsDownloadedEvent.Set();
                                lock (ClientInstanceParcelsLock)
                                {
                                    Client.Parcels.SimParcelsDownloaded += SimParcelsDownloadedEventHandler;
                                    Client.Parcels.RequestAllSimParcels(Client.Network.CurrentSim);
                                    if (Client.Network.CurrentSim.IsParcelMapFull())
                                    {
                                        SimParcelsDownloadedEvent.Set();
                                    }
                                    if (!SimParcelsDownloadedEvent.WaitOne(Configuration.SERVICES_TIMEOUT, false))
                                    {
                                        Client.Parcels.SimParcelsDownloaded -= SimParcelsDownloadedEventHandler;
                                        throw new Exception(
                                            wasGetDescriptionFromEnumValue(ScriptError.TIMEOUT_GETTING_PARCELS));
                                    }
                                    Client.Parcels.SimParcelsDownloaded -= SimParcelsDownloadedEventHandler;
                                }
                                Parallel.ForEach(
                                    GetPrimitives(
                                        Client.Network.CurrentSim.Parcels.Copy().Values.AsParallel().Select(o => new[]
                                        {
                                            Vector3.Distance(Client.Self.SimPosition, o.AABBMin),
                                            Vector3.Distance(Client.Self.SimPosition, o.AABBMax),
                                            Vector3.Distance(Client.Self.SimPosition,
                                                new Vector3(o.AABBMin.X, o.AABBMax.Y, 0)),
                                            Vector3.Distance(Client.Self.SimPosition,
                                                new Vector3(o.AABBMax.X, o.AABBMin.Y, 0))
                                        }.Max()).Max(), Configuration.SERVICES_TIMEOUT, Configuration.DATA_TIMEOUT),
                                    o =>
                                    {
                                        lock (LockObject)
                                        {
                                            updatePrimitives.Add(o);
                                        }
                                    });
                                break;
                            case Entity.AVATAR:
                                UUID agentUUID = UUID.Zero;
                                if (
                                    !UUID.TryParse(
                                        wasInput(
                                            wasKeyValueGet(wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.AGENT)),
                                                message)), out agentUUID) && !AgentNameToUUID(
                                                    wasInput(
                                                        wasKeyValueGet(
                                                            wasOutput(
                                                                wasGetDescriptionFromEnumValue(ScriptKeys.FIRSTNAME)),
                                                            message)),
                                                    wasInput(
                                                        wasKeyValueGet(
                                                            wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.LASTNAME)),
                                                            message)),
                                                    Configuration.SERVICES_TIMEOUT, Configuration.DATA_TIMEOUT,
                                                    ref agentUUID))
                                {
                                    throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.AGENT_NOT_FOUND));
                                }
                                Avatar avatar = GetAvatars(range, Configuration.SERVICES_TIMEOUT,
                                    Configuration.DATA_TIMEOUT).AsParallel().FirstOrDefault(o => o.ID.Equals(agentUUID));
                                if (avatar == null)
                                    throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.AVATAR_NOT_IN_RANGE));
                                HashSet<Primitive> objectsPrimitives =
                                    new HashSet<Primitive>(GetPrimitives(range, Configuration.SERVICES_TIMEOUT,
                                        Configuration.DATA_TIMEOUT));
                                Parallel.ForEach(objectsPrimitives,
                                    o =>
                                    {
                                        switch (!o.ParentID.Equals(avatar.LocalID))
                                        {
                                            case true:
                                                Primitive primitiveParent =
                                                    objectsPrimitives.AsParallel()
                                                        .FirstOrDefault(p => p.LocalID.Equals(o.ParentID));
                                                if (primitiveParent != null &&
                                                    primitiveParent.ParentID.Equals(avatar.LocalID))
                                                {
                                                    lock (LockObject)
                                                    {
                                                        updatePrimitives.Add(o);
                                                    }
                                                }
                                                break;
                                            default:
                                                lock (LockObject)
                                                {
                                                    updatePrimitives.Add(o);
                                                }
                                                break;
                                        }
                                    });
                                break;
                            default:
                                throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.UNKNOWN_ENTITY));
                        }

                        // allow partial results
                        UpdatePrimitives(ref updatePrimitives, Configuration.DATA_TIMEOUT);

                        List<string> data = new List<string>();
                        Parallel.ForEach(updatePrimitives, o =>
                        {
                            lock (LockObject)
                            {
                                data.AddRange(GetStructuredData(o,
                                    wasInput(
                                        wasKeyValueGet(wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.DATA)),
                                            message)))
                                    );
                            }
                        });
                        if (!data.Count.Equals(0))
                        {
                            result.Add(wasGetDescriptionFromEnumValue(ResultKeys.DATA),
                                wasEnumerableToCSV(data));
                        }
                    };
                    break;
                case ScriptKeys.EXPORTXML:
                    execute = () =>
                    {
                        if (
                            !HasCorrodePermission(group,
                                (int) Permissions.PERMISSION_INTERACT))
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.NO_CORRADE_PERMISSIONS));
                        }
                        float range;
                        if (
                            !float.TryParse(
                                wasInput(wasKeyValueGet(
                                    wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.RANGE)), message)),
                                out range))
                        {
                            range = Configuration.RANGE;
                        }
                        Primitive primitive = null;
                        if (
                            !FindPrimitive(
                                StringOrUUID(wasInput(wasKeyValueGet(
                                    wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.ITEM)), message))),
                                range,
                                ref primitive, Configuration.SERVICES_TIMEOUT, Configuration.DATA_TIMEOUT))
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.PRIMITIVE_NOT_FOUND));
                        }

                        // if the primitive is not an object (the root) or the primitive 
                        // is not an object as an avatar attachment then do not export it.
                        if (!primitive.ParentID.Equals(0) && !GetAvatars(range, Configuration.SERVICES_TIMEOUT,
                            Configuration.DATA_TIMEOUT)
                            .AsParallel()
                            .Any(o => o.LocalID.Equals(primitive.ParentID)))
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.ITEM_IS_NOT_AN_OBJECT));
                        }

                        HashSet<Primitive> exportPrimitivesSet = new HashSet<Primitive>();
                        Primitive root = new Primitive(primitive) {Position = Vector3.Zero};
                        exportPrimitivesSet.Add(root);

                        object LockObject = new object();

                        // find all the children that have the object as parent.
                        Parallel.ForEach(GetPrimitives(range, Configuration.SERVICES_TIMEOUT,
                            Configuration.DATA_TIMEOUT), o =>
                            {
                                if (!o.ParentID.Equals(root.LocalID))
                                    return;
                                Primitive child = new Primitive(o);
                                child.Position = root.Position + child.Position*root.Rotation;
                                child.Rotation = root.Rotation*child.Rotation;
                                lock (LockObject)
                                {
                                    exportPrimitivesSet.Add(child);
                                }
                            });

                        // add all the textures to export
                        HashSet<UUID> exportTexturesSet = new HashSet<UUID>();
                        Parallel.ForEach(exportPrimitivesSet, o =>
                        {
                            lock (LockObject)
                            {
                                if (!o.Textures.DefaultTexture.TextureID.Equals(Primitive.TextureEntry.WHITE_TEXTURE) &&
                                    !exportTexturesSet.Contains(o.Textures.DefaultTexture.TextureID))
                                    exportTexturesSet.Add(new UUID(o.Textures.DefaultTexture.TextureID));
                            }
                            Parallel.ForEach(o.Textures.FaceTextures, p =>
                            {
                                lock (LockObject)
                                {
                                    if (p != null &&
                                        !p.TextureID.Equals(Primitive.TextureEntry.WHITE_TEXTURE) &&
                                        !exportTexturesSet.Contains(p.TextureID))
                                        exportTexturesSet.Add(new UUID(p.TextureID));
                                }
                            });

                            lock (LockObject)
                            {
                                if (o.Sculpt != null && !o.Sculpt.SculptTexture.Equals(UUID.Zero) &&
                                    !exportTexturesSet.Contains(o.Sculpt.SculptTexture))
                                    exportTexturesSet.Add(new UUID(o.Sculpt.SculptTexture));
                            }
                        });

                        // Get the destination format to convert the downloaded textures to.
                        string format =
                            wasInput(wasKeyValueGet(
                                wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.FORMAT)),
                                message));
                        PropertyInfo formatProperty = null;
                        if (!string.IsNullOrEmpty(format))
                        {
                            // *FIXME:
                            /*
                            formatProperty = typeof (ImageFormat).GetProperties(
                                BindingFlags.Public |
                                BindingFlags.Static)
                                .AsParallel().FirstOrDefault(
                                    o =>
                                        string.Equals(o.Name, format, StringComparison.Ordinal));
                            if (formatProperty == null)
                            {
                                throw new Exception(
                                    wasGetDescriptionFromEnumValue(ScriptError.UNKNOWN_IMAGE_FORMAT_REQUESTED));
                            }
                            */
                        }

                        // download all the textures.
                        Dictionary<string, byte[]> exportTextureSetFiles = new Dictionary<string, byte[]>();
                        Parallel.ForEach(exportTexturesSet, o =>
                        {
                            byte[] assetData = null;
                            switch (!Client.Assets.Cache.HasAsset(o))
                            {
                                case true:
                                    lock (ClientInstanceAssetsLock)
                                    {
                                        ManualResetEvent RequestAssetEvent = new ManualResetEvent(false);
                                        Client.Assets.RequestImage(o, ImageType.Normal,
                                            delegate(TextureRequestState state, AssetTexture asset)
                                            {
                                                if (!asset.AssetID.Equals(o)) return;
                                                if (!state.Equals(TextureRequestState.Finished)) return;
                                                assetData = asset.AssetData;
                                                RequestAssetEvent.Set();
                                            });
                                        if (!RequestAssetEvent.WaitOne(Configuration.SERVICES_TIMEOUT, false))
                                        {
                                            throw new Exception(
                                                wasGetDescriptionFromEnumValue(ScriptError.TIMEOUT_TRANSFERRING_ASSET));
                                        }
                                    }
                                    Client.Assets.Cache.SaveAssetToCache(o, assetData);
                                    break;
                                default:
                                    assetData = Client.Assets.Cache.GetCachedAssetBytes(o);
                                    break;
                            }
                            switch (formatProperty != null)
                            {
                                // *FIXME:
                                /*
                                case true:
                                    ManagedImage managedImage;
                                    if (!OpenJPEG.DecodeToImage(assetData, out managedImage))
                                    {
                                        throw new Exception(
                                            wasGetDescriptionFromEnumValue(
                                                ScriptError.UNABLE_TO_DECODE_ASSET_DATA));
                                    }
                                    using (MemoryStream imageStream = new MemoryStream())
                                    {
                                        try
                                        {
                                            using (Bitmap bitmapImage = managedImage.ExportBitmap())
                                            {
                                                EncoderParameters encoderParameters =
                                                    new EncoderParameters(1);
                                                encoderParameters.Param[0] =
                                                    new EncoderParameter(Encoder.Quality, 100L);
                                                bitmapImage.Save(imageStream,
                                                    ImageCodecInfo.GetImageDecoders()
                                                        .AsParallel()
                                                        .FirstOrDefault(
                                                            p =>
                                                                p.FormatID.Equals(
                                                                    ((ImageFormat)
                                                                        formatProperty.GetValue(
                                                                            new ImageFormat(Guid.Empty)))
                                                                        .Guid)),
                                                    encoderParameters);
                                            }
                                        }
                                        catch (Exception)
                                        {
                                            throw new Exception(
                                                wasGetDescriptionFromEnumValue(
                                                    ScriptError.UNABLE_TO_CONVERT_TO_REQUESTED_FORMAT));
                                        }
                                        lock (LockObject)
                                        {
                                            exportTextureSetFiles.Add(
                                                o + "." + format.ToLower(),
                                                imageStream.ToArray());
                                        }
                                    }
                                    break;
                                */
                                default:
                                    format = "j2c";
                                    lock (LockObject)
                                    {
                                        exportTextureSetFiles.Add(o + "." + "j2c",
                                            assetData);
                                    }
                                    break;
                            }
                        });

                        HashSet<char> invalidPathCharacters = new HashSet<char>(Path.GetInvalidPathChars());

                        using (MemoryStream zipMemoryStream = new MemoryStream())
                        {
                            using (
                                ZipArchive zipOutputStream = new ZipArchive(zipMemoryStream, ZipArchiveMode.Create, true)
                                )
                            {
                                ZipArchive zipOutputStreamClosure = zipOutputStream;
                                // add all the textures to the zip file
                                Parallel.ForEach(exportTextureSetFiles, o =>
                                {
                                    lock (LockObject)
                                    {
                                        ZipArchiveEntry textureEntry =
                                            zipOutputStreamClosure.CreateEntry(
                                                new string(
                                                    o.Key.Where(p => !invalidPathCharacters.Contains(p)).ToArray()));
                                        using (Stream textureEntryDataStream = textureEntry.Open())
                                        {
                                            using (
                                                BinaryWriter textureEntryDataStreamWriter =
                                                    new BinaryWriter(textureEntryDataStream))
                                            {
                                                textureEntryDataStreamWriter.Write(o.Value);
                                                textureEntryDataStream.Flush();
                                            }
                                        }
                                    }
                                });

                                // add the primitives XML data to the zip file
                                ZipArchiveEntry primitiveEntry =
                                    zipOutputStreamClosure.CreateEntry(
                                        new string(
                                            (primitive.Properties.Name + ".xml").Where(
                                                p => !invalidPathCharacters.Contains(p))
                                                .ToArray()));
                                using (Stream primitiveEntryDataStream = primitiveEntry.Open())
                                {
                                    using (
                                        StreamWriter primitiveEntryDataStreamWriter =
                                            new StreamWriter(primitiveEntryDataStream))
                                    {
                                        primitiveEntryDataStreamWriter.Write(
                                            OSDParser.SerializeLLSDXmlString(
                                                Helpers.PrimListToOSD(exportPrimitivesSet.ToList())));
                                        primitiveEntryDataStreamWriter.Flush();
                                    }
                                }
                            }

                            // Base64-encode the zip stream and send it.
                            zipMemoryStream.Seek(0, SeekOrigin.Begin);

                            // If no path was specificed, then send the data.
                            string path =
                                wasInput(wasKeyValueGet(
                                    wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.PATH)),
                                    message));
                            if (string.IsNullOrEmpty(path))
                            {
                                result.Add(wasGetDescriptionFromEnumValue(ResultKeys.DATA),
                                    Convert.ToBase64String(zipMemoryStream.ToArray()));
                                return;
                            }
                            if (
                                !HasCorrodePermission(group, (int) Permissions.PERMISSION_SYSTEM))
                            {
                                throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.NO_CORRADE_PERMISSIONS));
                            }
                            // Otherwise, save it to the specified file.
                            using (FileStream fs = File.Open(path, FileMode.Create))
                            {
                                zipMemoryStream.WriteTo(fs);
                                zipMemoryStream.Flush();
                            }
                        }
                    };
                    break;
                case ScriptKeys.EXPORTDAE:
                    execute = () =>
                    {
                        if (
                            !HasCorrodePermission(group,
                                (int) Permissions.PERMISSION_INTERACT))
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.NO_CORRADE_PERMISSIONS));
                        }
                        float range;
                        if (
                            !float.TryParse(
                                wasInput(wasKeyValueGet(
                                    wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.RANGE)), message)),
                                out range))
                        {
                            range = Configuration.RANGE;
                        }
                        Primitive primitive = null;
                        if (
                            !FindPrimitive(
                                StringOrUUID(wasInput(wasKeyValueGet(
                                    wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.ITEM)), message))),
                                range,
                                ref primitive, Configuration.SERVICES_TIMEOUT, Configuration.DATA_TIMEOUT))
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.PRIMITIVE_NOT_FOUND));
                        }

                        // if the primitive is not an object (the root) or the primitive 
                        // is not an object as an avatar attachment then do not export it.
                        if (!primitive.ParentID.Equals(0) && !GetAvatars(range, Configuration.SERVICES_TIMEOUT,
                            Configuration.DATA_TIMEOUT)
                            .AsParallel()
                            .Any(o => o.LocalID.Equals(primitive.ParentID)))
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.ITEM_IS_NOT_AN_OBJECT));
                        }

                        HashSet<Primitive> exportPrimitivesSet = new HashSet<Primitive>();
                        Primitive root = new Primitive(primitive) {Position = Vector3.Zero};
                        exportPrimitivesSet.Add(root);

                        object LockObject = new object();

                        // find all the children that have the object as parent.
                        Parallel.ForEach(GetPrimitives(range, Configuration.SERVICES_TIMEOUT,
                            Configuration.DATA_TIMEOUT), o =>
                            {
                                if (!o.ParentID.Equals(root.LocalID))
                                    return;
                                Primitive child = new Primitive(o);
                                child.Position = root.Position + child.Position*root.Rotation;
                                child.Rotation = root.Rotation*child.Rotation;
                                lock (LockObject)
                                {
                                    exportPrimitivesSet.Add(child);
                                }
                            });

                        // update the primitives in the link set
                        if (!UpdatePrimitives(ref exportPrimitivesSet, Configuration.DATA_TIMEOUT))
                            throw new Exception(
                                wasGetDescriptionFromEnumValue(ScriptError.COULD_NOT_GET_PRIMITIVE_PROPERTIES));

                        // add all the textures to export
                        HashSet<UUID> exportTexturesSet = new HashSet<UUID>();
                        Parallel.ForEach(exportPrimitivesSet, o =>
                        {
                            Primitive.TextureEntryFace defaultTexture = o.Textures.DefaultTexture;
                            lock (LockObject)
                            {
                                if (defaultTexture != null && !exportTexturesSet.Contains(defaultTexture.TextureID))
                                    exportTexturesSet.Add(defaultTexture.TextureID);
                            }
                            Parallel.ForEach(o.Textures.FaceTextures, p =>
                            {
                                if (p != null)
                                {
                                    lock (LockObject)
                                    {
                                        if (!exportTexturesSet.Contains(p.TextureID))
                                        {
                                            exportTexturesSet.Add(p.TextureID);
                                        }
                                    }
                                }
                            });
                        });

                        // Get the destination format to convert the downloaded textures to.
                        string format =
                            wasInput(wasKeyValueGet(
                                wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.FORMAT)),
                                message));
                        PropertyInfo formatProperty = null;
                        if (!string.IsNullOrEmpty(format))
                        {
                            // FIXME:
                            /*
                            formatProperty = typeof (ImageFormat).GetProperties(
                                BindingFlags.Public |
                                BindingFlags.Static)
                                .AsParallel().FirstOrDefault(
                                    o =>
                                        string.Equals(o.Name, format, StringComparison.Ordinal));
                            if (formatProperty == null)
                            {
                                throw new Exception(
                                    wasGetDescriptionFromEnumValue(ScriptError.UNKNOWN_IMAGE_FORMAT_REQUESTED));
                            }
                            */
                        }

                        // download all the textures.
                        Dictionary<string, byte[]> exportTextureSetFiles = new Dictionary<string, byte[]>();
                        Dictionary<UUID, string> exportMeshTextures = new Dictionary<UUID, string>();
                        Parallel.ForEach(exportTexturesSet, o =>
                        {
                            byte[] assetData = null;
                            switch (!Client.Assets.Cache.HasAsset(o))
                            {
                                case true:
                                    lock (ClientInstanceAssetsLock)
                                    {
                                        ManualResetEvent RequestAssetEvent = new ManualResetEvent(false);
                                        Client.Assets.RequestImage(o, ImageType.Normal,
                                            delegate(TextureRequestState state, AssetTexture asset)
                                            {
                                                if (!asset.AssetID.Equals(o)) return;
                                                if (!state.Equals(TextureRequestState.Finished)) return;
                                                assetData = asset.AssetData;
                                                RequestAssetEvent.Set();
                                            });
                                        if (!RequestAssetEvent.WaitOne(Configuration.SERVICES_TIMEOUT, false))
                                        {
                                            throw new Exception(
                                                wasGetDescriptionFromEnumValue(ScriptError.TIMEOUT_TRANSFERRING_ASSET));
                                        }
                                    }
                                    Client.Assets.Cache.SaveAssetToCache(o, assetData);
                                    break;
                                default:
                                    assetData = Client.Assets.Cache.GetCachedAssetBytes(o);
                                    break;
                            }
                            switch (formatProperty != null)
                            {
                                // *FIXME:
                                /*case true:
                                    var bmp = J2kImage.FromBytes(assetData).As<SKBitmap>();
                                    if (bmp == null)
                                    {
                                        throw new Exception(
                                            wasGetDescriptionFromEnumValue(
                                                ScriptError.UNABLE_TO_DECODE_ASSET_DATA));
                                    }
                                    using (MemoryStream imageStream = new MemoryStream())
                                    {
                                        try
                                        {
                                            using (Bitmap bitmapImage = managedImage.ExportBitmap())
                                            {
                                                EncoderParameters encoderParameters =
                                                    new EncoderParameters(1);
                                                encoderParameters.Param[0] =
                                                    new EncoderParameter(Encoder.Quality, 100L);
                                                bitmapImage.Save(imageStream,
                                                    ImageCodecInfo.GetImageDecoders()
                                                        .AsParallel()
                                                        .FirstOrDefault(
                                                            p =>
                                                                p.FormatID.Equals(
                                                                    ((ImageFormat)
                                                                        formatProperty.GetValue(
                                                                            new ImageFormat(Guid.Empty)))
                                                                        .Guid)),
                                                    encoderParameters);
                                            }
                                        }
                                        catch (Exception)
                                        {
                                            throw new Exception(
                                                wasGetDescriptionFromEnumValue(
                                                    ScriptError.UNABLE_TO_CONVERT_TO_REQUESTED_FORMAT));
                                        }
                                        lock (LockObject)
                                        {
                                            exportTextureSetFiles.Add(
                                                o + "." + format.ToLower(),
                                                imageStream.ToArray());
                                            exportMeshTextures.Add(o,
                                                o.ToString());
                                        }
                                    }

                                    bmp.Dispose();
                                    break;
                                    */
                                default:
                                    format = "j2c";
                                    lock (LockObject)
                                    {
                                        exportTextureSetFiles.Add(o + "." + "j2c",
                                            assetData);
                                        exportMeshTextures.Add(o,
                                            o.ToString());
                                    }
                                    break;
                            }
                        });

                        // meshmerize all the primitives
                        HashSet<FacetedMesh> exportMeshSet = new HashSet<FacetedMesh>();
                        MeshmerizerR mesher = new MeshmerizerR();
                        Parallel.ForEach(exportPrimitivesSet, o =>
                        {
                            FacetedMesh mesh = null;
                            if (!MakeFacetedMesh(o, mesher, ref mesh, Configuration.SERVICES_TIMEOUT))
                            {
                                throw new Exception(
                                    wasGetDescriptionFromEnumValue(ScriptError.COULD_NOT_MESHMERIZE_OBJECT));
                            }
                            if (mesh == null) return;
                            Parallel.ForEach(mesh.Faces, p =>
                            {
                                Primitive.TextureEntryFace textureEntryFace = p.TextureFace;
                                if (textureEntryFace == null) return;

                                // Sculpt UV vertically flipped compared to prims. Flip back
                                if (o.Sculpt != null && !o.Sculpt.SculptTexture.Equals(UUID.Zero) &&
                                    !o.Sculpt.Type.Equals(SculptType.Mesh))
                                {
                                    textureEntryFace = (Primitive.TextureEntryFace) textureEntryFace.Clone();
                                    textureEntryFace.RepeatV *= -1;
                                }
                                // Texture transform for this face
                                mesher.TransformTexCoords(p.Vertices, p.Center, textureEntryFace, o.Scale);
                            });
                            lock (LockObject)
                            {
                                exportMeshSet.Add(mesh);
                            }
                        });

                        HashSet<char> invalidPathCharacters = new HashSet<char>(Path.GetInvalidPathChars());

                        using (MemoryStream zipMemoryStream = new MemoryStream())
                        {
                            using (
                                ZipArchive zipOutputStream = new ZipArchive(zipMemoryStream, ZipArchiveMode.Create, true)
                                )
                            {
                                ZipArchive zipOutputStreamClosure = zipOutputStream;
                                // add all the textures to the zip file
                                Parallel.ForEach(exportTextureSetFiles, o =>
                                {
                                    lock (LockObject)
                                    {
                                        ZipArchiveEntry textureEntry =
                                            zipOutputStreamClosure.CreateEntry(
                                                new string(
                                                    o.Key.Where(
                                                        p => !invalidPathCharacters.Contains(p)).ToArray()));
                                        using (Stream textureEntryDataStream = textureEntry.Open())
                                        {
                                            using (
                                                BinaryWriter textureEntryDataStreamWriter =
                                                    new BinaryWriter(textureEntryDataStream))
                                            {
                                                textureEntryDataStreamWriter.Write(o.Value);
                                                textureEntryDataStream.Flush();
                                            }
                                        }
                                    }
                                });

                                // add the primitives XML data to the zip file
                                ZipArchiveEntry primitiveEntry =
                                    zipOutputStreamClosure.CreateEntry(
                                        new string(
                                            (primitive.Properties.Name + ".dae").Where(
                                                p => !invalidPathCharacters.Contains(p))
                                                .ToArray()));
                                using (Stream primitiveEntryDataStream = primitiveEntry.Open())
                                {
                                    using (
                                        XmlTextWriter XMLTextWriter = new XmlTextWriter(primitiveEntryDataStream,
                                            Encoding.UTF8))
                                    {
                                        XMLTextWriter.Formatting = Formatting.Indented;
                                        XMLTextWriter.WriteProcessingInstruction("xml",
                                            "version=\"1.0\" encoding=\"utf-8\"");
                                        GenerateCollada(exportMeshSet, exportMeshTextures, format)
                                            .WriteContentTo(XMLTextWriter);
                                        XMLTextWriter.Flush();
                                    }
                                }
                            }

                            // Base64-encode the zip stream and send it.
                            zipMemoryStream.Seek(0, SeekOrigin.Begin);

                            // If no path was specificed, then send the data.
                            string path =
                                wasInput(wasKeyValueGet(
                                    wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.PATH)),
                                    message));
                            if (string.IsNullOrEmpty(path))
                            {
                                result.Add(wasGetDescriptionFromEnumValue(ResultKeys.DATA),
                                    Convert.ToBase64String(zipMemoryStream.ToArray()));
                                return;
                            }
                            if (
                                !HasCorrodePermission(group, (int) Permissions.PERMISSION_SYSTEM))
                            {
                                throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.NO_CORRADE_PERMISSIONS));
                            }
                            // Otherwise, save it to the specified file.
                            using (FileStream fs = File.Open(path, FileMode.Create))
                            {
                                zipMemoryStream.WriteTo(fs);
                                zipMemoryStream.Flush();
                            }
                        }
                    };
                    break;
                case ScriptKeys.VERSION:
                    execute =
                        () =>
                            result.Add(wasGetDescriptionFromEnumValue(ResultKeys.DATA),
                                CORRADE_CONSTANTS.CORRADE_VERSION);
                    break;
                case ScriptKeys.DIRECTORYSEARCH:
                    execute = () =>
                    {
                        if (!HasCorrodePermission(group, (int) Permissions.PERMISSION_DIRECTORY))
                        {
                            throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.NO_CORRADE_PERMISSIONS));
                        }
                        wasAdaptiveAlarm DirectorySearchResultsAlarm =
                            new wasAdaptiveAlarm(Configuration.DATA_DECAY_TYPE);
                        string name =
                            wasInput(wasKeyValueGet(wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.NAME)),
                                message));
                        string fields =
                            wasInput(wasKeyValueGet(wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.DATA)),
                                message));
                        object LockObject = new object();
                        List<string> csv = new List<string>();
                        int handledEvents = 0;
                        int counter = 1;
                        switch (
                            wasGetEnumValueFromDescription<Type>(
                                wasInput(wasKeyValueGet(
                                    wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.TYPE)), message))
                                    .ToLowerInvariant()))
                        {
                            case Type.CLASSIFIED:
                                DirectoryManager.Classified searchClassified = new DirectoryManager.Classified();
                                wasCSVToStructure(
                                    wasInput(
                                        wasKeyValueGet(wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.DATA)),
                                            message)),
                                    ref searchClassified);
                                Dictionary<DirectoryManager.Classified, int> classifieds =
                                    new Dictionary<DirectoryManager.Classified, int>();
                                EventHandler<DirClassifiedsReplyEventArgs> DirClassifiedsEventHandler =
                                    (sender, args) => Parallel.ForEach(args.Classifieds, o =>
                                    {
                                        DirectorySearchResultsAlarm.Alarm(Configuration.DATA_TIMEOUT);
                                        int score = !string.IsNullOrEmpty(fields)
                                            ? wasGetFields(searchClassified, searchClassified.GetType().Name)
                                                .Sum(
                                                    p =>
                                                        (from q in
                                                            wasGetFields(o,
                                                                o.GetType().Name)
                                                            let r = wasGetInfoValue(p.Key, p.Value)
                                                            where r != null
                                                            let s = wasGetInfoValue(q.Key, q.Value)
                                                            where s != null
                                                            where r.Equals(s)
                                                            select r).Count())
                                            : 0;
                                        lock (LockObject)
                                        {
                                            if (!classifieds.ContainsKey(o))
                                            {
                                                classifieds.Add(o, score);
                                            }
                                        }
                                    });
                                lock (ClientInstanceDirectoryLock)
                                {
                                    Client.Directory.DirClassifiedsReply += DirClassifiedsEventHandler;
                                    Client.Directory.StartClassifiedSearch(name);
                                    DirectorySearchResultsAlarm.Signal.WaitOne(Configuration.SERVICES_TIMEOUT, false);
                                    Client.Directory.DirClassifiedsReply -= DirClassifiedsEventHandler;
                                }
                                Dictionary<DirectoryManager.Classified, int> safeClassifieds;
                                lock (LockObject)
                                {
                                    safeClassifieds =
                                        classifieds.OrderByDescending(o => o.Value)
                                            .ToDictionary(o => o.Key, p => p.Value);
                                }
                                Parallel.ForEach(safeClassifieds,
                                    o => Parallel.ForEach(wasGetFields(o.Key, o.Key.GetType().Name), p =>
                                    {
                                        lock (LockObject)
                                        {
                                            csv.Add(p.Key.Name);
                                            csv.AddRange(wasGetInfo(p.Key, p.Value));
                                        }
                                    }));
                                break;
                            case Type.EVENT:
                                DirectoryManager.EventsSearchData searchEvent = new DirectoryManager.EventsSearchData();
                                wasCSVToStructure(
                                    wasInput(
                                        wasKeyValueGet(wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.DATA)),
                                            message)),
                                    ref searchEvent);
                                Dictionary<DirectoryManager.EventsSearchData, int> events =
                                    new Dictionary<DirectoryManager.EventsSearchData, int>();
                                EventHandler<DirEventsReplyEventArgs> DirEventsEventHandler =
                                    (sender, args) =>
                                    {
                                        DirectorySearchResultsAlarm.Alarm(Configuration.DATA_TIMEOUT);
                                        handledEvents += args.MatchedEvents.Count;
                                        Parallel.ForEach(args.MatchedEvents, o =>
                                        {
                                            int score = !string.IsNullOrEmpty(fields)
                                                ? wasGetFields(searchEvent, searchEvent.GetType().Name)
                                                    .Sum(
                                                        p =>
                                                            (from q in
                                                                wasGetFields(o, o.GetType().Name)
                                                                let r = wasGetInfoValue(p.Key, p.Value)
                                                                where r != null
                                                                let s = wasGetInfoValue(q.Key, q.Value)
                                                                where s != null
                                                                where r.Equals(s)
                                                                select r).Count())
                                                : 0;
                                            lock (LockObject)
                                            {
                                                if (!events.ContainsKey(o))
                                                {
                                                    events.Add(o, score);
                                                }
                                            }
                                        });
                                        if (handledEvents > LINDEN_CONSTANTS.DIRECTORY.EVENT.SEARCH_RESULTS_COUNT &&
                                            ((handledEvents - counter)%
                                             LINDEN_CONSTANTS.DIRECTORY.EVENT.SEARCH_RESULTS_COUNT).Equals(0))
                                        {
                                            ++counter;
                                            Client.Directory.StartEventsSearch(name, (uint) handledEvents);
                                        }
                                    };
                                lock (ClientInstanceDirectoryLock)
                                {
                                    Client.Directory.DirEventsReply += DirEventsEventHandler;
                                    Client.Directory.StartEventsSearch(name,
                                        (uint) handledEvents);
                                    DirectorySearchResultsAlarm.Signal.WaitOne(Configuration.SERVICES_TIMEOUT, false);
                                    Client.Directory.DirEventsReply -= DirEventsEventHandler;
                                }
                                Dictionary<DirectoryManager.EventsSearchData, int> safeEvents;
                                lock (LockObject)
                                {
                                    safeEvents = events.OrderByDescending(o => o.Value)
                                        .ToDictionary(o => o.Key, p => p.Value);
                                }
                                Parallel.ForEach(safeEvents,
                                    o => Parallel.ForEach(wasGetFields(o.Key, o.Key.GetType().Name), p =>
                                    {
                                        lock (LockObject)
                                        {
                                            csv.Add(p.Key.Name);
                                            csv.AddRange(wasGetInfo(p.Key, p.Value));
                                        }
                                    }));
                                break;
                            case Type.GROUP:
                                if (string.IsNullOrEmpty(name))
                                {
                                    throw new Exception(
                                        wasGetDescriptionFromEnumValue(ScriptError.NO_SEARCH_TEXT_PROVIDED));
                                }
                                DirectoryManager.GroupSearchData searchGroup = new DirectoryManager.GroupSearchData();
                                wasCSVToStructure(
                                    wasInput(
                                        wasKeyValueGet(wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.DATA)),
                                            message)),
                                    ref searchGroup);
                                Dictionary<DirectoryManager.GroupSearchData, int> groups =
                                    new Dictionary<DirectoryManager.GroupSearchData, int>();
                                EventHandler<DirGroupsReplyEventArgs> DirGroupsEventHandler =
                                    (sender, args) =>
                                    {
                                        DirectorySearchResultsAlarm.Alarm(Configuration.DATA_TIMEOUT);
                                        handledEvents += args.MatchedGroups.Count;
                                        Parallel.ForEach(args.MatchedGroups, o =>
                                        {
                                            int score = !string.IsNullOrEmpty(fields)
                                                ? wasGetFields(searchGroup, searchGroup.GetType().Name)
                                                    .Sum(
                                                        p =>
                                                            (from q in
                                                                wasGetFields(o, o.GetType().Name)
                                                                let r = wasGetInfoValue(p.Key, p.Value)
                                                                where r != null
                                                                let s = wasGetInfoValue(q.Key, q.Value)
                                                                where s != null
                                                                where r.Equals(s)
                                                                select r).Count())
                                                : 0;
                                            lock (LockObject)
                                            {
                                                if (!groups.ContainsKey(o))
                                                {
                                                    groups.Add(o, score);
                                                }
                                            }
                                        });
                                        if (handledEvents > LINDEN_CONSTANTS.DIRECTORY.GROUP.SEARCH_RESULTS_COUNT &&
                                            ((handledEvents - counter)%
                                             LINDEN_CONSTANTS.DIRECTORY.GROUP.SEARCH_RESULTS_COUNT).Equals(0))
                                        {
                                            ++counter;
                                            Client.Directory.StartGroupSearch(name, handledEvents);
                                        }
                                    };
                                lock (ClientInstanceDirectoryLock)
                                {
                                    Client.Directory.DirGroupsReply += DirGroupsEventHandler;
                                    Client.Directory.StartGroupSearch(name, handledEvents);
                                    DirectorySearchResultsAlarm.Signal.WaitOne(Configuration.SERVICES_TIMEOUT, false);
                                    Client.Directory.DirGroupsReply -= DirGroupsEventHandler;
                                }
                                Dictionary<DirectoryManager.GroupSearchData, int> safeGroups;
                                lock (LockObject)
                                {
                                    safeGroups = groups.OrderByDescending(o => o.Value)
                                        .ToDictionary(o => o.Key, p => p.Value);
                                }
                                Parallel.ForEach(safeGroups,
                                    o => Parallel.ForEach(wasGetFields(o.Key, o.Key.GetType().Name), p =>
                                    {
                                        lock (LockObject)
                                        {
                                            csv.Add(p.Key.Name);
                                            csv.AddRange(wasGetInfo(p.Key, p.Value));
                                        }
                                    }));
                                break;
                            case Type.LAND:
                                DirectoryManager.DirectoryParcel searchLand = new DirectoryManager.DirectoryParcel();
                                wasCSVToStructure(
                                    wasInput(
                                        wasKeyValueGet(wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.DATA)),
                                            message)),
                                    ref searchLand);
                                Dictionary<DirectoryManager.DirectoryParcel, int> lands =
                                    new Dictionary<DirectoryManager.DirectoryParcel, int>();
                                EventHandler<DirLandReplyEventArgs> DirLandReplyEventArgs =
                                    (sender, args) =>
                                    {
                                        DirectorySearchResultsAlarm.Alarm(Configuration.DATA_TIMEOUT);
                                        handledEvents += args.DirParcels.Count;
                                        Parallel.ForEach(args.DirParcels, o =>
                                        {
                                            int score = !string.IsNullOrEmpty(fields)
                                                ? wasGetFields(searchLand, searchLand.GetType().Name)
                                                    .Sum(
                                                        p =>
                                                            (from q in
                                                                wasGetFields(o, o.GetType().Name)
                                                                let r = wasGetInfoValue(p.Key, p.Value)
                                                                where r != null
                                                                let s = wasGetInfoValue(q.Key, q.Value)
                                                                where s != null
                                                                where r.Equals(s)
                                                                select r).Count())
                                                : 0;
                                            lock (LockObject)
                                            {
                                                if (!lands.ContainsKey(o))
                                                {
                                                    lands.Add(o, score);
                                                }
                                            }
                                        });
                                        if (handledEvents > LINDEN_CONSTANTS.DIRECTORY.LAND.SEARCH_RESULTS_COUNT &&
                                            ((handledEvents - counter)%
                                             LINDEN_CONSTANTS.DIRECTORY.LAND.SEARCH_RESULTS_COUNT).Equals(0))
                                        {
                                            ++counter;
                                            Client.Directory.StartLandSearch(DirectoryManager.DirFindFlags.SortAsc,
                                                DirectoryManager.SearchTypeFlags.Any, int.MaxValue, int.MaxValue,
                                                handledEvents);
                                        }
                                    };
                                lock (ClientInstanceDirectoryLock)
                                {
                                    Client.Directory.DirLandReply += DirLandReplyEventArgs;
                                    Client.Directory.StartLandSearch(DirectoryManager.DirFindFlags.SortAsc,
                                        DirectoryManager.SearchTypeFlags.Any, int.MaxValue, int.MaxValue, handledEvents);
                                    DirectorySearchResultsAlarm.Signal.WaitOne(Configuration.SERVICES_TIMEOUT, false);
                                    Client.Directory.DirLandReply -= DirLandReplyEventArgs;
                                }
                                Dictionary<DirectoryManager.DirectoryParcel, int> safeLands;
                                lock (LockObject)
                                {
                                    safeLands = lands.OrderByDescending(o => o.Value)
                                        .ToDictionary(o => o.Key, p => p.Value);
                                }
                                Parallel.ForEach(safeLands,
                                    o => Parallel.ForEach(wasGetFields(o.Key, o.Key.GetType().Name), p =>
                                    {
                                        lock (LockObject)
                                        {
                                            csv.Add(p.Key.Name);
                                            csv.AddRange(wasGetInfo(p.Key, p.Value));
                                        }
                                    }));
                                break;
                            case Type.PEOPLE:
                                if (string.IsNullOrEmpty(name))
                                {
                                    throw new Exception(
                                        wasGetDescriptionFromEnumValue(ScriptError.NO_SEARCH_TEXT_PROVIDED));
                                }
                                DirectoryManager.AgentSearchData searchAgent = new DirectoryManager.AgentSearchData();
                                Dictionary<DirectoryManager.AgentSearchData, int> agents =
                                    new Dictionary<DirectoryManager.AgentSearchData, int>();
                                EventHandler<DirPeopleReplyEventArgs> DirPeopleReplyEventHandler =
                                    (sender, args) =>
                                    {
                                        DirectorySearchResultsAlarm.Alarm(Configuration.DATA_TIMEOUT);
                                        handledEvents += args.MatchedPeople.Count;
                                        Parallel.ForEach(args.MatchedPeople, o =>
                                        {
                                            int score = !string.IsNullOrEmpty(fields)
                                                ? wasGetFields(searchAgent, searchAgent.GetType().Name)
                                                    .Sum(
                                                        p =>
                                                            (from q in
                                                                wasGetFields(o, o.GetType().Name)
                                                                let r = wasGetInfoValue(p.Key, p.Value)
                                                                where r != null
                                                                let s = wasGetInfoValue(q.Key, q.Value)
                                                                where s != null
                                                                where r.Equals(s)
                                                                select r).Count())
                                                : 0;
                                            lock (LockObject)
                                            {
                                                if (!agents.ContainsKey(o))
                                                {
                                                    agents.Add(o, score);
                                                }
                                            }
                                        });
                                        if (handledEvents > LINDEN_CONSTANTS.DIRECTORY.PEOPLE.SEARCH_RESULTS_COUNT &&
                                            ((handledEvents - counter)%
                                             LINDEN_CONSTANTS.DIRECTORY.PEOPLE.SEARCH_RESULTS_COUNT).Equals(0))
                                        {
                                            ++counter;
                                            Client.Directory.StartPeopleSearch(name, handledEvents);
                                        }
                                    };
                                lock (ClientInstanceDirectoryLock)
                                {
                                    Client.Directory.DirPeopleReply += DirPeopleReplyEventHandler;
                                    Client.Directory.StartPeopleSearch(name, handledEvents);
                                    DirectorySearchResultsAlarm.Signal.WaitOne(Configuration.SERVICES_TIMEOUT, false);
                                    Client.Directory.DirPeopleReply -= DirPeopleReplyEventHandler;
                                }
                                Dictionary<DirectoryManager.AgentSearchData, int> safeAgents;
                                lock (LockObject)
                                {
                                    safeAgents = agents.OrderByDescending(o => o.Value)
                                        .ToDictionary(o => o.Key, p => p.Value);
                                }
                                Parallel.ForEach(safeAgents,
                                    o => Parallel.ForEach(wasGetFields(o.Key, o.Key.GetType().Name), p =>
                                    {
                                        lock (LockObject)
                                        {
                                            csv.Add(p.Key.Name);
                                            csv.AddRange(wasGetInfo(p.Key, p.Value));
                                        }
                                    }));
                                break;
                            case Type.PLACE:
                                if (string.IsNullOrEmpty(name))
                                {
                                    throw new Exception(
                                        wasGetDescriptionFromEnumValue(ScriptError.NO_SEARCH_TEXT_PROVIDED));
                                }
                                DirectoryManager.PlacesSearchData searchPlaces = new DirectoryManager.PlacesSearchData();
                                wasCSVToStructure(
                                    wasInput(
                                        wasKeyValueGet(wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.DATA)),
                                            message)),
                                    ref searchPlaces);
                                Dictionary<DirectoryManager.PlacesSearchData, int> places =
                                    new Dictionary<DirectoryManager.PlacesSearchData, int>();
                                EventHandler<PlacesReplyEventArgs> DirPlacesReplyEventHandler =
                                    (sender, args) => Parallel.ForEach(args.MatchedPlaces, o =>
                                    {
                                        DirectorySearchResultsAlarm.Alarm(Configuration.DATA_TIMEOUT);
                                        int score = !string.IsNullOrEmpty(fields)
                                            ? wasGetFields(searchPlaces, searchPlaces.GetType().Name)
                                                .Sum(
                                                    p =>
                                                        (from q in
                                                            wasGetFields(o, o.GetType().Name)
                                                            let r = wasGetInfoValue(p.Key, p.Value)
                                                            where r != null
                                                            let s = wasGetInfoValue(q.Key, q.Value)
                                                            where s != null
                                                            where r.Equals(s)
                                                            select r).Count())
                                            : 0;
                                        lock (LockObject)
                                        {
                                            if (!places.ContainsKey(o))
                                            {
                                                places.Add(o, score);
                                            }
                                        }
                                    });
                                lock (ClientInstanceDirectoryLock)
                                {
                                    Client.Directory.PlacesReply += DirPlacesReplyEventHandler;
                                    Client.Directory.StartPlacesSearch(name);
                                    DirectorySearchResultsAlarm.Signal.WaitOne(Configuration.SERVICES_TIMEOUT, false);
                                    Client.Directory.PlacesReply -= DirPlacesReplyEventHandler;
                                }
                                Dictionary<DirectoryManager.PlacesSearchData, int> safePlaces;
                                lock (LockObject)
                                {
                                    safePlaces = places.OrderByDescending(o => o.Value)
                                        .ToDictionary(o => o.Key, p => p.Value);
                                }
                                Parallel.ForEach(safePlaces,
                                    o => Parallel.ForEach(wasGetFields(o.Key, o.Key.GetType().Name), p =>
                                    {
                                        lock (LockObject)
                                        {
                                            csv.Add(p.Key.Name);
                                            csv.AddRange(wasGetInfo(p.Key, p.Value));
                                        }
                                    }));
                                break;
                            default:
                                throw new Exception(
                                    wasGetDescriptionFromEnumValue(ScriptError.UNKNOWN_DIRECTORY_SEARCH_TYPE));
                        }
                        if (!csv.Count.Equals(0))
                        {
                            result.Add(wasGetDescriptionFromEnumValue(ResultKeys.DATA),
                                wasEnumerableToCSV(csv));
                        }
                    };
                    break;
                default:
                    execute =
                        () => { throw new Exception(wasGetDescriptionFromEnumValue(ScriptError.COMMAND_NOT_FOUND)); };
                    break;
            }

            // sift action
            System.Action sift = () =>
            {
                string pattern =
                    wasInput(wasKeyValueGet(wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.SIFT)), message));
                if (string.IsNullOrEmpty(pattern)) return;
                string data;
                if (!result.TryGetValue(wasGetDescriptionFromEnumValue(ResultKeys.DATA), out data)) return;
                data = wasEnumerableToCSV((((new Regex(pattern, RegexOptions.Compiled)).Matches(data)
                    .Cast<Match>()
                    .Select(m => m.Groups)).SelectMany(
                        matchGroups => Enumerable.Range(0, matchGroups.Count).Skip(1),
                        (matchGroups, i) => new {matchGroups, i})
                    .SelectMany(@t => Enumerable.Range(0, @t.matchGroups[@t.i].Captures.Count),
                        (@t, j) => @t.matchGroups[@t.i].Captures[j].Value)));
                if (string.IsNullOrEmpty(data))
                {
                    result.Remove(wasGetDescriptionFromEnumValue(ResultKeys.DATA));
                    return;
                }
                result[wasGetDescriptionFromEnumValue(ResultKeys.DATA)] = data;
            };

            // execute command, sift data and check for errors
            bool success = false;
            try
            {
                execute.Invoke();
                sift.Invoke();
                success = true;
            }
            catch (Exception ex)
            {
                result.Add(wasGetDescriptionFromEnumValue(ResultKeys.ERROR), ex.Message);
            }

            // add the final success status
            result.Add(wasGetDescriptionFromEnumValue(ResultKeys.SUCCESS),
                success.ToString(CultureInfo.InvariantCulture));

            // build afterburn
            object AfterBurnLock = new object();
            HashSet<string> resultKeys = new HashSet<string>(wasGetEnumDescriptions<ResultKeys>());
            HashSet<string> scriptKeys = new HashSet<string>(wasGetEnumDescriptions<ScriptKeys>());
            Parallel.ForEach(wasKeyValueDecode(message), o =>
            {
                // remove keys that are script keys, result keys or invalid key-value pairs
                if (string.IsNullOrEmpty(o.Key) || resultKeys.Contains(wasInput(o.Key)) ||
                    scriptKeys.Contains(wasInput(o.Key)) ||
                    string.IsNullOrEmpty(o.Value))
                    return;
                lock (AfterBurnLock)
                {
                    result.Add(o.Key, o.Value);
                }
            });

            return result;
        }
    }
}
