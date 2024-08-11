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
using System.ComponentModel;
using System.Linq;
using System.Reflection;
using System.Threading;
using OpenMetaverse;

namespace Corrode
{
    public partial class Corrode
    {
                /// <summary>
        ///     Configuration keys.
        /// </summary>
        private struct ConfigurationKeys
        {
            public const string FIRST_NAME = @"firstname";
            public const string LAST_NAME = @"lastname";
            public const string LOGIN_URL = @"loginurl";
            public const string HTTP = @"http";
            public const string PREFIX = @"prefix";
            public const string TIMEOUT = @"timeout";
            public const string THROTTLE = @"throttle";
            public const string SERVICES = @"services";
            public const string TOS_ACCEPTED = @"tosaccepted";
            public const string AUTO_ACTIVATE_GROUP = @"autoactivategroup";
            public const string GROUP_CREATE_FEE = @"groupcreatefee";
            public const string START_LOCATION = @"startlocation";
            public const string LOG = @"log";
            public const string NAME = @"name";
            public const string UUID = @"uuid";
            public const string PASSWORD = @"password";
            public const string CHATLOG = @"chatlog";
            public const string DATABASE = @"database";
            public const string PERMISSIONS = @"permissions";
            public const string NOTIFICATIONS = @"notifications";
            public const string CALLBACKS = @"callbacks";
            public const string QUEUE_LENGTH = @"queuelength";
            public const string CLIENT = @"client";
            public const string NAGGLE = @"naggle";
            public const string CONNECTIONS = @"connections";
            public const string EXPECT100CONTINUE = @"expect100continue";
            public const string MAC = @"MAC";
            public const string ID0 = @"ID0";
            public const string SERVER = @"server";
            public const string MEMBERSHIP = @"membership";
            public const string SWEEP = @"sweep";
            public const string ENABLE = @"enable";
            public const string REBAKE = @"rebake";
            public const string ACTIVATE = @"activate";
            public const string DATA = @"data";
            public const string THREADS = @"threads";
            public const string COMMANDS = @"commands";
            public const string RLV = @"rlv";
            public const string WORKERS = @"workers";
            public const string ENCODE = @"encode";
            public const string DECODE = @"decode";
            public const string ENCRYPT = @"encrypt";
            public const string DECRYPT = @"decrypt";
            public const string INPUT = @"input";
            public const string OUTPUT = @"output";
            public const string ENIGMA = @"enigma";
            public const string ROTORS = @"rotors";
            public const string PLUGS = @"plugs";
            public const string REFLECTOR = @"reflector";
            public const string SECRET = @"secret";
            public const string VIGENERE = @"vigenere";
            public const string IM = @"im";
            public const string RANGE = @"range";
            public const string DECAY = @"decay";
            public const string LOGOUT = @"logout";
            public const string FILE = @"file";
            public const string DIRECTORY = @"directory";
            public const string LOCAL = @"local";
            public const string REGION = @"region";
            public const string BIND = @"bind";
            public const string IDLE = @"idle";
            public const string COMPRESSION = @"compression";
            public const string EXIT_CODE = @"exitcode";
            public const string EXPECTED = @"expected";
            public const string ABNORMAL = @"abnormal";
        }

        /// <summary>
        ///     Corrode's internal thread structure.
        /// </summary>
        public struct CorrodeThread
        {
            private static readonly HashSet<Thread> WorkSet = new HashSet<Thread>();
            private static readonly object LockObject = new object();

            public void Spawn(ThreadStart s, int m)
            {
                lock (LockObject)
                {
                    WorkSet.RemoveWhere(o => !o.IsAlive);
                    if (WorkSet.Count > m)
                    {
                        return;
                    }
                }
                Thread t = new Thread(s) {IsBackground = true, Priority = ThreadPriority.BelowNormal};
                lock (LockObject)
                {
                    WorkSet.Add(t);
                }
                t.Start();
            }
        }

        /// <summary>
        ///     An inventory item.
        /// </summary>
        private struct DirItem
        {
            [Description("item")] public UUID Item;
            [Description("name")] public string Name;
            [Description("permissions")] public string Permissions;
            [Description("type")] public DirItemType Type;

            public static DirItem FromInventoryBase(InventoryBase inventoryBase)
            {
                DirItem item = new DirItem
                {
                    Name = inventoryBase.Name,
                    Item = inventoryBase.UUID,
                    Permissions = CORRADE_CONSTANTS.PERMISSIONS.NONE
                };

                if (inventoryBase is InventoryFolder)
                {
                    item.Type = DirItemType.FOLDER;
                    return item;
                }

                if (!(inventoryBase is InventoryItem)) return item;

                InventoryItem inventoryItem = inventoryBase as InventoryItem;
                item.Permissions = wasPermissionsToString(inventoryItem.Permissions);

                if (inventoryItem is InventoryWearable)
                {
                    item.Type = (DirItemType) typeof (DirItemType).GetFields(BindingFlags.Public |
                                                                             BindingFlags.Static)
                        .AsParallel().FirstOrDefault(
                            o =>
                                string.Equals(o.Name,
                                    Enum.GetName(typeof (WearableType),
                                        (inventoryItem as InventoryWearable).WearableType),
                                    StringComparison.InvariantCultureIgnoreCase)).GetValue(null);
                    return item;
                }

                if (inventoryItem is InventoryTexture)
                {
                    item.Type = DirItemType.TEXTURE;
                    return item;
                }

                if (inventoryItem is InventorySound)
                {
                    item.Type = DirItemType.SOUND;
                    return item;
                }

                if (inventoryItem is InventoryCallingCard)
                {
                    item.Type = DirItemType.CALLINGCARD;
                    return item;
                }

                if (inventoryItem is InventoryLandmark)
                {
                    item.Type = DirItemType.LANDMARK;
                    return item;
                }

                if (inventoryItem is InventoryObject)
                {
                    item.Type = DirItemType.OBJECT;
                    return item;
                }

                if (inventoryItem is InventoryNotecard)
                {
                    item.Type = DirItemType.NOTECARD;
                    return item;
                }

                if (inventoryItem is InventoryCategory)
                {
                    item.Type = DirItemType.CATEGORY;
                    return item;
                }

                if (inventoryItem is InventoryLSL)
                {
                    item.Type = DirItemType.LSL;
                    return item;
                }

                if (inventoryItem is InventorySnapshot)
                {
                    item.Type = DirItemType.SNAPSHOT;
                    return item;
                }

                if (inventoryItem is InventoryAttachment)
                {
                    item.Type = DirItemType.ATTACHMENT;
                    return item;
                }

                if (inventoryItem is InventoryAnimation)
                {
                    item.Type = DirItemType.ANIMATION;
                    return item;
                }

                if (inventoryItem is InventoryGesture)
                {
                    item.Type = DirItemType.GESTURE;
                    return item;
                }

                item.Type = DirItemType.NONE;
                return item;
            }
        }
        
        /// <summary>
        ///     ENIGMA machine settings.
        /// </summary>
        private struct ENIGMA
        {
            public char[] plugs;
            public char reflector;
            public char[] rotors;
        }

        /// <summary>
        ///     Group structure.
        /// </summary>
        private struct Group
        {
            public string ChatLog;
            public bool ChatLogEnabled;
            public string DatabaseFile;
            public string Name;
            public uint NotificationMask;
            public string Password;
            public uint PermissionMask;
            public UUID UUID;
            public uint Workers;
        }

        /// <summary>
        ///     A structure for group invites.
        /// </summary>
        private struct GroupInvite
        {
            [Description("agent")] public Agent Agent;
            [Description("fee")] public int Fee;
            [Description("group")] public string Group;
            [Description("session")] public UUID Session;
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

                public const string HOME_SET = @"Home position set.";
            }

            public struct ASSETS
            {
                public struct NOTECARD
                {
                    public const string NEWLINE = "\n";
                }
            }

            public struct AVATARS
            {
                public const int SET_DISPLAY_NAME_SUCCESS = 200;
                public const string LASTNAME_PLACEHOLDER = @"Resident";
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

            public struct ESTATE
            {
                public const int REGION_RESTART_DELAY = 120;

                public struct MESSAGES
                {
                    public const string REGION_RESTART_MESSAGE = @"restart";
                }
            }

            public struct GRID
            {
                public const string SECOND_LIFE = @"Second Life";
            }

            public struct GROUPS
            {
                public const int MAXIMUM_NUMBER_OF_ROLES = 10;
            }

            public struct LSL
            {
                public const string CSV_DELIMITER = @", ";
                public const float SENSOR_RANGE = 96;
            }

            public struct REGION
            {
                public const float TELEPORT_MINIMUM_DISTANCE = 1;
            }

            public struct VIEWER
            {
                public const float MAXIMUM_DRAW_DISTANCE = 4096;
            }
        }
        
        /// <summary>
        ///     A structure to track LookAt effects.
        /// </summary>
        private struct LookAtEffect
        {
            [Description("effect")] public UUID Effect;
            [Description("offset")] public Vector3d Offset;
            [Description("source")] public UUID Source;
            [Description("target")] public UUID Target;
            [Description("type")] public LookAtType Type;
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
        ///     A Corrode notification.
        /// </summary>
        public struct Notification
        {
            public string GroupName;
            public SerializableDictionary<Notifications, HashSet<string>> NotificationDestination;
            public uint NotificationMask;
        }

        /// <summary>
        ///     An element from the notification queue waiting to be dispatched.
        /// </summary>
        private struct NotificationQueueElement
        {
            public Dictionary<string, string> message;
            public string URL;
        }

        /// <summary>
        ///     A structure to track PointAt effects.
        /// </summary>
        private struct PointAtEffect
        {
            [Description("effect")] public UUID Effect;
            [Description("offset")] public Vector3d Offset;
            [Description("source")] public UUID Source;
            [Description("target")] public UUID Target;
            [Description("type")] public PointAtType Type;
        }

        /// <summary>
        ///     A structure for script dialogs.
        /// </summary>
        private struct ScriptDialog
        {
            public Agent Agent;
            [Description("button")] public List<string> Button;
            [Description("channel")] public int Channel;
            [Description("item")] public UUID Item;
            [Description("message")] public string Message;
            [Description("name")] public string Name;
        }
        
        /// <summary>
        ///     A structure for script permission requests.
        /// </summary>
        private struct ScriptPermissionRequest
        {
            public Agent Agent;
            [Description("item")] public UUID Item;
            [Description("name")] public string Name;
            [Description("permission")] public ScriptPermission Permission;
            [Description("region")] public string Region;
            [Description("task")] public UUID Task;
        }


        /// <summary>
        ///     A structure to track Sphere effects.
        /// </summary>
        private struct SphereEffect
        {
            [Description("alpha")] public float Alpha;
            [Description("color")] public Vector3 Color;
            [Description("duration")] public float Duration;
            [Description("effect")] public UUID Effect;
            [Description("offset")] public Vector3d Offset;
            [Description("termination")] public DateTime Termination;
        }

        /// <summary>
        ///     A structure for teleport lures.
        /// </summary>
        private struct TeleportLure
        {
            public Agent Agent;
            public UUID Session;
        }
    }
}

