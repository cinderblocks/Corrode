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
using System.ComponentModel;
using System.Text.RegularExpressions;
using OpenMetaverse;

namespace Corrode
{
    public partial class Corrode
    {
        /// <summary>Holds all the active RLV rules.</summary>
        private static readonly HashSet<RLVRule> RLVRules = new HashSet<RLVRule>();

        /// <summary>Locks down RLV for linear concurrent access.</summary>
        private static readonly object RLVRulesLock = new object();

        /// <summary>RLV Wearables</summary>
        private static readonly List<RLVWearable> RLVWearables = new List<RLVWearable>
        {
            new RLVWearable { Name = @"gloves", WearableType = WearableType.Gloves },
            new RLVWearable { Name = @"jacket", WearableType = WearableType.Jacket },
            new RLVWearable { Name = @"pants", WearableType = WearableType.Pants },
            new RLVWearable { Name = @"shirt", WearableType = WearableType.Shirt },
            new RLVWearable { Name = @"shoes", WearableType = WearableType.Shoes },
            new RLVWearable { Name = @"skirt", WearableType = WearableType.Skirt },
            new RLVWearable { Name = @"socks", WearableType = WearableType.Socks },
            new RLVWearable { Name = @"underpants", WearableType = WearableType.Underpants },
            new RLVWearable { Name = @"undershirt", WearableType = WearableType.Undershirt },
            new RLVWearable { Name = @"skin", WearableType = WearableType.Skin },
            new RLVWearable { Name = @"eyes", WearableType = WearableType.Eyes },
            new RLVWearable { Name = @"hair", WearableType = WearableType.Hair },
            new RLVWearable { Name = @"shape", WearableType = WearableType.Shape },
            new RLVWearable { Name = @"alpha", WearableType = WearableType.Alpha },
            new RLVWearable { Name = @"tattoo", WearableType = WearableType.Tattoo },
            new RLVWearable { Name = @"physics", WearableType = WearableType.Physics }
        };

        /// <summary>RLV Attachments.</summary>
        private static readonly List<RLVAttachment> RLVAttachments = new List<RLVAttachment>
        {
            new RLVAttachment { Name = @"none", AttachmentPoint = AttachmentPoint.Default },
            new RLVAttachment { Name = @"chest", AttachmentPoint = AttachmentPoint.Chest },
            new RLVAttachment { Name = @"skull", AttachmentPoint = AttachmentPoint.Skull },
            new RLVAttachment { Name = @"left shoulder", AttachmentPoint = AttachmentPoint.LeftShoulder },
            new RLVAttachment { Name = @"right shoulder", AttachmentPoint = AttachmentPoint.RightShoulder },
            new RLVAttachment { Name = @"left hand", AttachmentPoint = AttachmentPoint.LeftHand },
            new RLVAttachment { Name = @"right hand", AttachmentPoint = AttachmentPoint.RightHand },
            new RLVAttachment { Name = @"left foot", AttachmentPoint = AttachmentPoint.LeftFoot },
            new RLVAttachment { Name = @"right foot", AttachmentPoint = AttachmentPoint.RightFoot },
            new RLVAttachment { Name = @"spine", AttachmentPoint = AttachmentPoint.Spine },
            new RLVAttachment { Name = @"pelvis", AttachmentPoint = AttachmentPoint.Pelvis },
            new RLVAttachment { Name = @"mouth", AttachmentPoint = AttachmentPoint.Mouth },
            new RLVAttachment { Name = @"chin", AttachmentPoint = AttachmentPoint.Chin },
            new RLVAttachment { Name = @"left ear", AttachmentPoint = AttachmentPoint.LeftEar },
            new RLVAttachment { Name = @"right ear", AttachmentPoint = AttachmentPoint.RightEar },
            new RLVAttachment { Name = @"left eyeball", AttachmentPoint = AttachmentPoint.LeftEyeball },
            new RLVAttachment { Name = @"right eyeball", AttachmentPoint = AttachmentPoint.RightEyeball },
            new RLVAttachment { Name = @"nose", AttachmentPoint = AttachmentPoint.Nose },
            new RLVAttachment { Name = @"r upper arm", AttachmentPoint = AttachmentPoint.RightUpperArm },
            new RLVAttachment { Name = @"r forearm", AttachmentPoint = AttachmentPoint.RightForearm },
            new RLVAttachment { Name = @"l upper arm", AttachmentPoint = AttachmentPoint.LeftUpperArm },
            new RLVAttachment { Name = @"l forearm", AttachmentPoint = AttachmentPoint.LeftForearm },
            new RLVAttachment { Name = @"right hip", AttachmentPoint = AttachmentPoint.RightHip },
            new RLVAttachment { Name = @"r upper leg", AttachmentPoint = AttachmentPoint.RightUpperLeg },
            new RLVAttachment { Name = @"r lower leg", AttachmentPoint = AttachmentPoint.RightLowerLeg },
            new RLVAttachment { Name = @"left hip", AttachmentPoint = AttachmentPoint.LeftHip },
            new RLVAttachment { Name = @"l upper leg", AttachmentPoint = AttachmentPoint.LeftUpperLeg },
            new RLVAttachment { Name = @"l lower leg", AttachmentPoint = AttachmentPoint.LeftLowerLeg },
            new RLVAttachment { Name = @"stomach", AttachmentPoint = AttachmentPoint.Stomach },
            new RLVAttachment { Name = @"left pec", AttachmentPoint = AttachmentPoint.LeftPec },
            new RLVAttachment { Name = @"right pec", AttachmentPoint = AttachmentPoint.RightPec },
            new RLVAttachment { Name = @"center 2", AttachmentPoint = AttachmentPoint.HUDCenter2 },
            new RLVAttachment { Name = @"top right", AttachmentPoint = AttachmentPoint.HUDTopRight },
            new RLVAttachment { Name = @"top", AttachmentPoint = AttachmentPoint.HUDTop },
            new RLVAttachment { Name = @"top left", AttachmentPoint = AttachmentPoint.HUDTopLeft },
            new RLVAttachment { Name = @"center", AttachmentPoint = AttachmentPoint.HUDCenter },
            new RLVAttachment { Name = @"bottom left", AttachmentPoint = AttachmentPoint.HUDBottomLeft },
            new RLVAttachment { Name = @"bottom", AttachmentPoint = AttachmentPoint.HUDBottom },
            new RLVAttachment { Name = @"bottom right", AttachmentPoint = AttachmentPoint.HUDBottomRight },
            new RLVAttachment { Name = @"neck", AttachmentPoint = AttachmentPoint.Neck },
            new RLVAttachment { Name = @"root", AttachmentPoint = AttachmentPoint.Root }
        };

        /// <summary>RLV attachment structure.</summary>
        private struct RLVAttachment
        {
            public AttachmentPoint AttachmentPoint;
            public string Name;
        }

        /// <summary>
        ///     Enumeration for supported RLV commands.
        /// </summary>
        private enum RLVBehaviour : uint
        {
            [Description("none")] NONE = 0,
            [Description("version")] VERSION,
            [Description("versionnew")] VERSIONNEW,
            [Description("versionnum")] VERSIONNUM,
            [Description("getgroup")] GETGROUP,
            [Description("setgroup")] SETGROUP,
            [Description("getsitid")] GETSITID,
            [Description("getstatusall")] GETSTATUSALL,
            [Description("getstatus")] GETSTATUS,
            [Description("sit")] SIT,
            [Description("unsit")] UNSIT,
            [Description("setrot")] SETROT,
            [Description("tpto")] TPTO,
            [Description("getoutfit")] GETOUTFIT,
            [Description("getattach")] GETATTACH,
            [Description("remattach")] REMATTACH,
            [Description("detach")] DETACH,
            [Description("detachme")] DETACHME,
            [Description("remoutfit")] REMOUTFIT,
            [Description("attach")] ATTACH,
            [Description("attachoverreplace")] ATTACHOVERORREPLACE,
            [Description("attachover")] ATTACHOVER,
            [Description("getinv")] GETINV,
            [Description("getinvworn")] GETINVWORN,
            [Description("getpath")] GETPATH,
            [Description("getpathnew")] GETPATHNEW,
            [Description("findfolder")] FINDFOLDER,
            [Description("clear")] CLEAR,
            [Description("accepttp")] ACCEPTTP,
            [Description("acceptpermission")] ACCEPTPERMISSION
        }

        private struct RLVRule
        {
            public string Behaviour;
            public UUID ObjectUUID;
            public string Option;
            public string Param;
        }

        /// <summary>RLV wearable structure.</summary>
        private struct RLVWearable
        {
            public string Name;
            public WearableType WearableType;
        }

        /// <summary>Structure for RLV constants.</summary>
        private struct RLV_CONSTANTS
        {
            public const string COMMAND_OPERATOR = @"@";
            public const string VIEWER = @"RestrainedLife viewer";
            public const string SHORT_VERSION = @"1.23";
            public const string LONG_VERSION = @"1230100";
            public const string FORCE = @"force";
            public const string FALSE_MARKER = @"0";
            public const string TRUE_MARKER = @"1";
            public const string CSV_DELIMITER = @",";
            public const string DOT_MARKER = @".";
            public const string TILDE_MARKER = @"~";
            public const string PROPORTION_SEPARATOR = @"|";
            public const string SHARED_FOLDER_NAME = @"#RLV";
            public const string AND_OPERATOR = @"&&";
            public const string PATH_SEPARATOR = @"/";
            public const string Y = @"y";
            public const string ADD = @"add";
            public const string N = @"n";
            public const string REM = @"rem";
            public const string STATUS_SEPARATOR = @";";

            /// <summary>Regex used to match RLV commands.</summary>
            public static readonly Regex RLVRegEx = new Regex(@"(?<behaviour>[^:=]+)(:(?<option>[^=]*))?=(?<param>\w+)",
                RegexOptions.Compiled);
        }
    }
}
