using System;
using System.Linq;
using System.Threading;
using OpenMetaverse;

namespace Corrode
{
    public partial class Corrode
    {
                /// <summary>
        ///     Determines whether an agent has a set of powers for a group.
        /// </summary>
        /// <param name="agentUUID">the agent UUID</param>
        /// <param name="groupUUID">the UUID of the group</param>
        /// <param name="powers">a GroupPowers structure</param>
        /// <param name="millisecondsTimeout">timeout for the search in milliseconds</param>
        /// <param name="dataTimeout">timeout for receiving answers from services</param>
        /// <returns>true if the agent has the powers</returns>
        private static bool HasGroupPowers(UUID agentUUID, UUID groupUUID, GroupPowers powers,
            int millisecondsTimeout, int dataTimeout)
        {
            bool hasPowers = false;
            wasAdaptiveAlarm AvatarGroupsReceivedAlarm = new wasAdaptiveAlarm(Configuration.DATA_DECAY_TYPE);
            EventHandler<AvatarGroupsReplyEventArgs> AvatarGroupsReplyEventHandler = (sender, args) =>
            {
                AvatarGroupsReceivedAlarm.Alarm(dataTimeout);
                hasPowers =
                    args.Groups.AsParallel().Any(
                        o => o.GroupID.Equals(groupUUID) && !(o.GroupPowers & powers).Equals(GroupPowers.None));
            };
            lock (ClientInstanceAvatarsLock)
            {
                Client.Avatars.AvatarGroupsReply += AvatarGroupsReplyEventHandler;
                Client.Avatars.RequestAvatarProperties(agentUUID);
                if (!AvatarGroupsReceivedAlarm.Signal.WaitOne(millisecondsTimeout, false))
                {
                    Client.Avatars.AvatarGroupsReply -= AvatarGroupsReplyEventHandler;
                    return false;
                }
                Client.Avatars.AvatarGroupsReply -= AvatarGroupsReplyEventHandler;
            }
            return hasPowers;
        }

        /// <summary>
        ///     Attempts to join the group chat for a given group.
        /// </summary>
        /// <param name="groupUUID">the UUID of the group to join the group chat for</param>
        /// <param name="millisecondsTimeout">timeout for joining the group chat</param>
        /// <returns>true if the group chat was joined</returns>
        private static bool JoinGroupChat(UUID groupUUID, int millisecondsTimeout)
        {
            bool succeeded = false;
            ManualResetEvent GroupChatJoinedEvent = new ManualResetEvent(false);
            EventHandler<GroupChatJoinedEventArgs> GroupChatJoinedEventHandler =
                (sender, args) =>
                {
                    succeeded = args.Success;
                    GroupChatJoinedEvent.Set();
                };
            lock (ClientInstanceSelfLock)
            {
                Client.Self.GroupChatJoined += GroupChatJoinedEventHandler;
                Client.Self.RequestJoinGroupChat(groupUUID);
                if (!GroupChatJoinedEvent.WaitOne(millisecondsTimeout, false))
                {
                    Client.Self.GroupChatJoined -= GroupChatJoinedEventHandler;
                    return false;
                }
                Client.Self.GroupChatJoined -= GroupChatJoinedEventHandler;
            }
            return succeeded;
        }

        /// <summary>
        ///     Determines whether an agent referenced by an UUID is in a group
        ///     referenced by an UUID.
        /// </summary>
        /// <param name="agentUUID">the UUID of the agent</param>
        /// <param name="groupUUID">the UUID of the groupt</param>
        /// <param name="millisecondsTimeout">timeout for the search in milliseconds</param>
        /// <param name="dataTimeout">timeout for receiving answers from services</param>
        /// <returns>true if the agent is in the group</returns>
        private static bool AgentInGroup(UUID agentUUID, UUID groupUUID, int millisecondsTimeout, int dataTimeout)
        {
            bool agentInGroup = false;
            wasAdaptiveAlarm GroupMembersReceivedAlarm = new wasAdaptiveAlarm(Configuration.DATA_DECAY_TYPE);
            EventHandler<GroupMembersReplyEventArgs> HandleGroupMembersReplyDelegate = (sender, args) =>
            {
                GroupMembersReceivedAlarm.Alarm(dataTimeout);
                agentInGroup = args.Members.AsParallel().Any(o => o.Value.ID.Equals(agentUUID));
            };
            lock (ClientInstanceGroupsLock)
            {
                Client.Groups.GroupMembersReply += HandleGroupMembersReplyDelegate;
                Client.Groups.RequestGroupMembers(groupUUID);
                if (!GroupMembersReceivedAlarm.Signal.WaitOne(millisecondsTimeout, false))
                {
                    Client.Groups.GroupMembersReply -= HandleGroupMembersReplyDelegate;
                    return false;
                }
                Client.Groups.GroupMembersReply -= HandleGroupMembersReplyDelegate;
            }
            return agentInGroup;
        }

        /// <summary>
        ///     Used to check whether a group name matches a group password.
        /// </summary>
        /// <param name="group">the name of the group</param>
        /// <param name="password">the password for the group</param>
        /// <returns>true if the agent has authenticated</returns>
        private static bool Authenticate(string group, string password)
        {
            UUID groupUUID;
            return UUID.TryParse(group, out groupUUID)
                ? Configuration.GROUPS.AsParallel().Any(
                    o =>
                        groupUUID.Equals(o.UUID) &&
                        password.Equals(o.Password, StringComparison.Ordinal))
                : Configuration.GROUPS.AsParallel().Any(
                    o =>
                        o.Name.Equals(group, StringComparison.Ordinal) &&
                        password.Equals(o.Password, StringComparison.Ordinal));
        }

        /// <summary>
        ///     Used to check whether a group has certain permissions for Corrode.
        /// </summary>
        /// <param name="group">the name of the group</param>
        /// <param name="permission">the numeric Corrode permission</param>
        /// <returns>true if the group has permission</returns>
        private static bool HasCorrodePermission(string group, int permission)
        {
            UUID groupUUID;
            return !permission.Equals(0) && UUID.TryParse(group, out groupUUID)
                ? Configuration.GROUPS.AsParallel()
                    .Any(o => groupUUID.Equals(o.UUID) && !(o.PermissionMask & permission).Equals(0))
                : Configuration.GROUPS.AsParallel().Any(
                    o =>
                        o.Name.Equals(group, StringComparison.Ordinal) &&
                        !(o.PermissionMask & permission).Equals(0));
        }

        /// <summary>
        ///     Used to check whether a group has a certain notification for Corrode.
        /// </summary>
        /// <param name="group">the name of the group</param>
        /// <param name="notification">the numeric Corrode notification</param>
        /// <returns>true if the group has the notification</returns>
        private static bool GroupHasNotification(string group, uint notification)
        {
            UUID groupUUID;
            return !notification.Equals(0) && UUID.TryParse(group, out groupUUID)
                ? Configuration.GROUPS.AsParallel().Any(
                    o => groupUUID.Equals(o.UUID) &&
                         !(o.NotificationMask & notification).Equals(0))
                : Configuration.GROUPS.AsParallel().Any(
                    o => o.Name.Equals(group, StringComparison.Ordinal) &&
                         !(o.NotificationMask & notification).Equals(0));
        }

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
            EventHandler<GroupProfileEventArgs> GroupProfileDelegate = (sender, args) =>
            {
                localGroup = args.Group;
                GroupProfileEvent.Set();
            };
            lock (ClientInstanceGroupsLock)
            {
                Client.Groups.GroupProfile += GroupProfileDelegate;
                Client.Groups.RequestGroupProfile(groupUUID);
                if (!GroupProfileEvent.WaitOne(millisecondsTimeout, false))
                {
                    Client.Groups.GroupProfile -= GroupProfileDelegate;
                    return false;
                }
                Client.Groups.GroupProfile -= GroupProfileDelegate;
            }
            group = localGroup;
            return true;
        }
    }
}

