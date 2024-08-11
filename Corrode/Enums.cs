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
using System.ComponentModel;

namespace Corrode
{
    public partial class Corrode
    {
        /// <summary>
        ///     Corrode notification types.
        /// </summary>
        [Flags]
        public enum Notifications : uint
        {
            [Description("alert")] NOTIFICATION_ALERT_MESSAGE = 1,
            [Description("region")] NOTIFICATION_REGION_MESSAGE = 2,
            [Description("group")] NOTIFICATION_GROUP_MESSAGE = 4,
            [Description("balance")] NOTIFICATION_BALANCE = 8,
            [Description("message")] NOTIFICATION_INSTANT_MESSAGE = 16,
            [Description("notice")] NOTIFICATION_GROUP_NOTICE = 32,
            [Description("local")] NOTIFICATION_LOCAL_CHAT = 64,
            [Description("dialog")] NOTIFICATION_SCRIPT_DIALOG = 128,
            [Description("friendship")] NOTIFICATION_FRIENDSHIP = 256,
            [Description("inventory")] NOTIFICATION_INVENTORY = 512,
            [Description("permission")] NOTIFICATION_SCRIPT_PERMISSION = 1024,
            [Description("lure")] NOTIFICATION_TELEPORT_LURE = 2048,
            [Description("effect")] NOTIFICATION_VIEWER_EFFECT = 4096,
            [Description("collision")] NOTIFICATION_MEAN_COLLISION = 8192,
            [Description("crossing")] NOTIFICATION_REGION_CROSSED = 16384,
            [Description("terse")] NOTIFICATION_TERSE_UPDATES = 32768,
            [Description("typing")] NOTIFICATION_TYPING = 65536,
            [Description("invite")] NOTIFICATION_GROUP_INVITE = 131072,
            [Description("economy")] NOTIFICATION_ECONOMY = 262144,
            [Description("membership")] NOTIFICATION_GROUP_MEMBERSHIP = 524288,
            [Description("url")] NOTIFICATION_LOAD_URL = 1048576,
            [Description("ownersay")] NOTIFICATION_OWNER_SAY = 2097152,
            [Description("regionsayto")] NOTIFICATION_REGION_SAY_TO = 4194304,
            [Description("objectim")] NOTIFICATION_OBJECT_INSTANT_MESSAGE = 8388608,
            [Description("rlv")] NOTIFICATION_RLV_MESSAGE = 16777216,
            [Description("debug")] NOTIFICATION_DEBUG_MESSAGE = 33554432,
            [Description("avatars")] NOTIFICATION_RADAR_AVATARS = 67108864,
            [Description("primitives")] NOTIFICATION_RADAR_PRIMITIVES = 134217728
        }
        
        /// <summary>
        ///     Keys recognized by Corrode.
        /// </summary>
        private enum ScriptKeys : uint
        {
            [Description("none")] NONE = 0,

            [IsCommand(true)]
            [CommandInputSyntax(
                "<command=getcommand>&<group=<UUID|STRING>>&<password=<STRING>>&<entity=<syntax|permission>>&entity=syntax:<type=<input>>&[callback=<STRING>]"
            )]
            [CommandPermissionMask((uint)Permissions.PERMISSION_NONE)]
            [Description("getcommand")]
            GETCOMMAND,

            [IsCommand(true)]
            [CommandInputSyntax(
                "<command=listcommands>&<group=<UUID|STRING>>&<password=<STRING>>&[callback=<STRING>]")]
            [CommandPermissionMask((uint)Permissions.PERMISSION_NONE)]
            [Description("listcommands")]
            LISTCOMMANDS,

            [IsCommand(true)]
            [CommandInputSyntax(
                "<command=getconnectedregions>&<group=<UUID|STRING>>&<password=<STRING>>&[callback=<STRING>]")]
            [CommandPermissionMask((uint)Permissions.PERMISSION_LAND)]
            [Description("getconnectedregions")]
            GETCONNECTEDREGIONS,

            [IsCommand(true)]
            [CommandInputSyntax(
                "<command=getnetworkdata>&<group=<UUID|STRING>>&<password=<STRING>>&[data=<NetworkManager[,NetworkManager...]>]&[callback=<STRING>]"
            )]
            [CommandPermissionMask((uint)Permissions.PERMISSION_GROOMING)]
            [Description("getnetworkdata")]
            GETNETWORKDATA,

            [IsCommand(true)]
            [CommandInputSyntax(
                "<command=typing>&<group=<UUID|STRING>>&<password=<STRING>>&<action=<enable|disable|get>>&[callback=<STRING>]"
            )]
            [CommandPermissionMask((uint)Permissions.PERMISSION_GROOMING)]
            [Description("typing")]
            TYPING,

            [IsCommand(true)]
            [CommandInputSyntax(
                "<command=busy>&<group=<UUID|STRING>>&<password=<STRING>>&<action=<enable|disable|get>>&[callback=<STRING>]"
            )]
            [CommandPermissionMask((uint)Permissions.PERMISSION_GROOMING)]
            [Description("busy")]
            BUSY,

            [IsCommand(true)]
            [CommandInputSyntax(
                "<command=away>&<group=<UUID|STRING>>&<password=<STRING>>&<action=<enable|disable|get>>&[callback=<STRING>]"
            )]
            [CommandPermissionMask((uint)Permissions.PERMISSION_GROOMING)]
            [Description("away")]
            AWAY,

            [IsCommand(true)]
            [CommandInputSyntax(
                "<command=getobjectpermissions>&<group=<UUID|STRING>>&<password=<STRING>>&<item=<STRING|UUID>>&[range=<FLOAT>]&[callback=<STRING>]"
            )]
            [CommandPermissionMask((uint)Permissions.PERMISSION_INTERACT)]
            [Description("getobjectpermissions")]
            GETOBJECTPERMISSIONS,
            [Description("scale")] SCALE,
            [Description("uniform")] UNIFORM,

            [IsCommand(true)]
            [CommandInputSyntax(
                "<command=setobjectscale>&<group=<UUID|STRING>>&<password=<STRING>>&<item=<STRING|UUID>>&[range=<FLOAT>]&<scale=<FLOAT>>&[uniform=<BOOL>]&[callback=<STRING>]"
            )]
            [CommandPermissionMask((uint)Permissions.PERMISSION_INTERACT)]
            [Description("setobjectscale")]
            SETOBJECTSCALE,

            [IsCommand(true)]
            [CommandInputSyntax(
                "<command=setprimitivescale>&<group=<UUID|STRING>>&<password=<STRING>>&<item=<STRING|UUID>>&[range=<FLOAT>]&<scale=<FLOAT>>&[uniform=<BOOL>]&[callback=<STRING>]"
            )]
            [CommandPermissionMask((uint)Permissions.PERMISSION_INTERACT)]
            [Description("setprimitivescale")]
            SETPRIMITIVESCALE,

            [IsCommand(true)]
            [CommandInputSyntax(
                "<command=setprimitiverotation>&<group=<UUID|STRING>>&<password=<STRING>>&<item=<STRING|UUID>>&[range=<FLOAT>]&<rotation=<QUATERNION>>&[callback=<STRING>]"
            )]
            [CommandPermissionMask((uint)Permissions.PERMISSION_INTERACT)]
            [Description("setprimitiverotation")]
            SETPRIMITIVEROTATION,

            [IsCommand(true)]
            [CommandInputSyntax(
                "<command=setprimitiveposition>&<group=<UUID|STRING>>&<password=<STRING>>&<item=<STRING|UUID>>&[range=<FLOAT>]&<position=<VECTOR3>>&[callback=<STRING>]"
            )]
            [CommandPermissionMask((uint)Permissions.PERMISSION_INTERACT)]
            [Description("setprimitiveposition")]
            SETPRIMITIVEPOSITION,

            [IsCommand(true)]
            [CommandInputSyntax(
                "<command=exportdae>&<group=<UUID|STRING>>&<password=<STRING>>&<item=<STRING|UUID>>&[range=<FLOAT>]&[format=<ImageFormat>]&[path=<STRING>]&[callback=<STRING>]"
            )]
            [CommandPermissionMask((uint)Permissions.PERMISSION_INTERACT)]
            [Description("exportdae")]
            EXPORTDAE,

            [IsCommand(true)]
            [CommandInputSyntax(
                "<command=exportxml>&<group=<UUID|STRING>>&<password=<STRING>>&<item=<STRING|UUID>>&[range=<FLOAT>]&[format=<ImageFormat>]&[path=<STRING>]&[callback=<STRING>]"
            )]
            [CommandPermissionMask((uint)Permissions.PERMISSION_INTERACT)]
            [Description("exportxml")]
            EXPORTXML,

            [IsCommand(true)]
            [CommandInputSyntax(
                "<command=getprimitivesdata>&<group=<UUID|STRING>>&<password=<STRING>>&<entity=<range|parcel|region|avatar>>&entity=range:[range=<FLOAT>]&entity=parcel:[position=<VECTOR2>]&entity=avatar:<agent=<UUID>|firstname=<STRING>&lastname=<STRING>>&[data=<Primitive[,Primitive...]>]&[callback=<STRING>]"
            )]
            [CommandPermissionMask((uint)Permissions.PERMISSION_INTERACT)]
            [Description("getprimitivesdata")]
            GETPRIMITIVESDATA,

            [IsCommand(true)]
            [CommandInputSyntax(
                "<command=getavatarsdata>&<group=<UUID|STRING>>&<password=<STRING>>&<entity=<range|parcel|region|avatar>>&entity=range:[range=<FLOAT>]&entity=parcel:[position=<VECTOR2>]&entity=avatar:<agent=<UUID>|firstname=<STRING>&lastname=<STRING>>&[data=<Avatar[,Avatar...]>]&[callback=<STRING>]"
            )]
            [CommandPermissionMask((uint)Permissions.PERMISSION_INTERACT)]
            [Description("getavatarsdata")]
            GETAVATARSDATA,
            [Description("format")] FORMAT,
            [Description("volume")] VOLUME,
            [Description("audible")] AUDIBLE,
            [Description("path")] PATH,

            [IsCommand(true)]
            [CommandInputSyntax(
                "<command=inventory>&<group=<UUID|STRING>>&<password=<STRING>>&<action=<ls|cwd|cd|mkdir|chmod|rm|cp|mv|ln>>&action=ls|mkdir|chmod:[path=<STRING>]&action=cd,action=rm:<path=<STRING>>&action=mkdir:<name=<STRING>>&action=chmod:<permissions=<STRING>>&action=cp|mv|ln:<source=<STRING>>&action=cp|mv|ln:<target=<STRING>>&[callback=<STRING>]"
            )]
            [CommandPermissionMask((uint)Permissions.PERMISSION_INVENTORY)]
            [Description("inventory")]
            INVENTORY,
            [Description("offset")] OFFSET,
            [Description("alpha")] ALPHA,
            [Description("color")] COLOR,

            [IsCommand(true)]
            [CommandInputSyntax(
                "<command=deleteviewereffect>&<group=<UUID|STRING>>&<password=<STRING>>&<effect=<Look|Point>>&<id=<UUID>>&[callback=<STRING>]"
            )]
            [CommandPermissionMask((uint)Permissions.PERMISSION_INTERACT)]
            [Description("deleteviewereffect")]
            DELETEVIEWEREFFECT,

            [IsCommand(true)]
            [CommandInputSyntax(
                "<command=getviewereffects>&<group=<UUID|STRING>>&<password=<STRING>>&<effect=<Look|Point|Sphere|Beam>>&[callback=<STRING>]"
            )]
            [CommandPermissionMask((uint)Permissions.PERMISSION_INTERACT)]
            [Description("getviewereffects")]
            GETVIEWEREFFECTS,

            [IsCommand(true)]
            [CommandInputSyntax(
                "<command=setviewereffect>&<group=<UUID|STRING>>&<password=<STRING>>&<effect=<Look|Point|Sphere|Beam>>&effect=Look:<item=<UUID|STRING>&<range=<FLOAT>>>|<agent=<UUID>|firstname=<STRING>&lastname=<STRING>>&effect=Look:<offset=<VECTOR3>>&effect=Look:<type=LookAt>&effect=Point:<item=<UUID|STRING>&<range=<FLOAT>>>|<agent=<UUID>|firstname=<STRING>&lastname=<STRING>>&effect=Point:<offset=<VECTOR3>>&effect=Point:<type=PointAt>&effect=Beam:<item=<UUID|STRING>&<range=<FLOAT>>>|<agent=<UUID>|firstname=<STRING>&lastname=<STRING>>&effect=Beam:<color=<VECTOR3>>&effect=Beam:<alpha=<FLOAT>>&effect=Beam:<duration=<FLOAT>>&effect=Beam:<offset=<VECTOR3>>&effect=Sphere:<color=<VECTOR3>>&effect=Sphere:<alpha=<FLOAT>>&effect=Sphere:<duration=<FLOAT>>&effect=Sphere:<offset=<VECTOR3>>&[id=<UUID>]&[callback=<STRING>]"
            )]
            [CommandPermissionMask((uint)Permissions.PERMISSION_INTERACT)]
            [Description("setviewereffect")]
            SETVIEWEREFFECT,

            [IsCommand(true)]
            [CommandInputSyntax(
                "<command=ai>&<group=<UUID|STRING>>&<password=<STRING>>&<action=<process|enable|disable|rebuild>>&[callback=<STRING>]"
            )]
            [CommandPermissionMask((uint)Permissions.PERMISSION_TALK)]
            [Description("ai")]
            AI,

            [IsCommand(true)]
            [CommandInputSyntax(
                "<command=gettitles>&<group=<UUID|STRING>>&<password=<STRING>>&[callback=<STRING>]")]
            [CommandPermissionMask((uint)Permissions.PERMISSION_GROUP)]
            [Description("gettitles")]
            GETTITLES,

            [IsCommand(true)]
            [CommandInputSyntax(
                "<command=tag>&<group=<UUID|STRING>>&<password=<STRING>>&action=<set|get>&action=set:<title=<STRING>>&[callback=<STRING>]"
            )]
            [CommandPermissionMask((uint)Permissions.PERMISSION_GROOMING)]
            [Description("tag")]
            TAG,

            [IsCommand(true)]
            [CommandInputSyntax(
                "<command=filter>&<group=<UUID|STRING>>&<password=<STRING>>&action=<set|get>&action=get:<type=<input|output>>&action=set:<input=<STRING>>&action=set:<output=<STRING>>&[callback=<STRING>]"
            )]
            [CommandPermissionMask((uint)Permissions.PERMISSION_FILTER)]
            [Description("filter")]
            FILTER,

            [IsCommand(true)]
            [CommandInputSyntax(
                    "<command=run>&<group=<UUID|STRING>>&<password=<STRING>>&<action=<enable|disable|get>>&[callback=<STRING>]"
                )
            ]
            [CommandPermissionMask((uint)Permissions.PERMISSION_MOVEMENT)]
            [Description("run")]
            RUN,

            [IsCommand(true)]
            [CommandInputSyntax("<command=relax>&<group=<UUID|STRING>>&<password=<STRING>>&[callback=<STRING>]")]
            [CommandPermissionMask((uint)Permissions.PERMISSION_MOVEMENT)]
            [Description("relax")]
            RELAX,
            [Description("sift")] SIFT,

            [IsCommand(true)]
            [CommandInputSyntax(
                "<command=rlv>&<group=<UUID|STRING>>&<password=<STRING>>&<action=<enable|disable>>&[callback=<STRING>]")
            ]
            [CommandPermissionMask((uint)Permissions.PERMISSION_SYSTEM)]
            [Description("rlv")]
            RLV,

            [IsCommand(true)]
            [CommandInputSyntax(
                "<command=getinventorypath>&<group=<UUID|STRING>>&<password=<STRING>>&<pattern=<STRING>>&[callback=<STRING>]"
            )]
            [CommandPermissionMask((uint)Permissions.PERMISSION_INVENTORY)]
            [Description("getinventorypath")]
            GETINVENTORYPATH,
            [Description("committed")] COMMITTED,
            [Description("credit")] CREDIT,
            [Description("success")] SUCCESS,
            [Description("transaction")] TRANSACTION,

            [IsCommand(true)]
            [CommandInputSyntax(
                "<command=getscriptdialogs>&<group=<UUID|STRING>>&<password=<STRING>>&[callback=<STRING>]")]
            [CommandPermissionMask((uint)Permissions.PERMISSION_INTERACT)]
            [Description("getscriptdialogs")]
            GETSCRIPTDIALOGS,

            [IsCommand(true)]
            [CommandInputSyntax(
                "<command=getscriptpermissionrequests>&<group=<UUID|STRING>>&<password=<STRING>>&[callback=<STRING>]")]
            [CommandPermissionMask((uint)Permissions.PERMISSION_INTERACT)]
            [Description("getscriptpermissionrequests")]
            GETSCRIPTPERMISSIONREQUESTS,

            [IsCommand(true)]
            [CommandInputSyntax(
                "<command=getteleportlures>&<group=<UUID|STRING>>&<password=<STRING>>&[callback=<STRING>]")]
            [CommandPermissionMask((uint)Permissions.PERMISSION_MOVEMENT)]
            [Description("getteleportlures")]
            GETTELEPORTLURES,

            [IsCommand(true)]
            [CommandInputSyntax(
                "<command=replytogroupinvite>&<group=<UUID|STRING>>&<password=<STRING>>&[action=<accept|decline>]&<session=<UUID>>&[callback=<STRING>]"
            )]
            [CommandPermissionMask((uint)Permissions.PERMISSION_GROUP | (uint)Permissions.PERMISSION_ECONOMY)]
            [Description("replytogroupinvite")]
            REPLYTOGROUPINVITE,

            [IsCommand(true)]
            [CommandInputSyntax(
                "<command=getgroupinvites>&<group=<UUID|STRING>>&<password=<STRING>>&[callback=<STRING>]")]
            [CommandPermissionMask((uint)Permissions.PERMISSION_GROUP)]
            [Description("getgroupinvites")]
            GETGROUPINVITES,

            [IsCommand(true)]
            [CommandInputSyntax(
                "<command=getmemberroles>&<group=<UUID|STRING>>&<password=<STRING>>&<agent=<UUID>|firstname=<STRING>&lastname=<STRING>>&[callback=<STRING>]"
            )]
            [CommandPermissionMask((uint)Permissions.PERMISSION_GROUP)]
            [Description("getmemberroles")]
            GETMEMBERROLES,

            [IsCommand(true)]
            [CommandInputSyntax(
                "<command=execute>&<group=<UUID|STRING>>&<password=<STRING>>&<file=<STRING>>&[parameter=<STRING>]&[callback=<STRING>]"
            )]
            [CommandPermissionMask((uint)Permissions.PERMISSION_EXECUTE)]
            [Description("execute")]
            EXECUTE,
            [Description("parameter")] PARAMETER,
            [Description("file")] FILE,

            [IsCommand(true)]
            [CommandInputSyntax(
                "<command=cache>&<group=<UUID|STRING>>&<password=<STRING>>&<action=<purge|load|save>>&[callback=<STRING>]"
            )]
            [CommandPermissionMask((uint)Permissions.PERMISSION_SYSTEM)]
            [Description("cache")]
            CACHE,

            [IsCommand(true)]
            [CommandInputSyntax(
                "<command=getgridregiondata>&<group=<UUID|STRING>>&<password=<STRING>>&<data=<GridRegion[,GridRegion...]>>&[region=<STRING>]&[callback=<STRING>]"
            )]
            [CommandPermissionMask((uint)Permissions.PERMISSION_LAND)]
            [Description("getgridregiondata")]
            GETGRIDREGIONDATA,

            [IsCommand(true)]
            [CommandInputSyntax(
                "<command=getregionparcelsboundingbox>&<group=<UUID|STRING>>&<password=<STRING>>&[region=<STRING>]&[region=<STRING>]&[callback=<STRING>]"
            )]
            [CommandPermissionMask((uint)Permissions.PERMISSION_LAND)]
            [Description("getregionparcelsboundingbox")]
            GETREGIONPARCELSBOUNDINGBOX,
            [Description("pattern")] PATTERN,

            [IsCommand(true)]
            [CommandInputSyntax(
                "<command=searchinventory>&<group=<UUID|STRING>>&<password=<STRING>>&<pattern=<STRING>>&[type=<AssetType>]&[callback=<STRING>]"
            )]
            [CommandPermissionMask((uint)Permissions.PERMISSION_INVENTORY)]
            [Description("searchinventory")]
            SEARCHINVENTORY,

            [IsCommand(true)]
            [CommandInputSyntax(
                "<command=getterrainheight>&<group=<UUID|STRING>>&<password=<STRING>>&[southwest=<VECTOR>]&[northwest=<VECTOR>]&[region=<STRING>]&[callback=<STRING>]"
            )]
            [CommandPermissionMask((uint)Permissions.PERMISSION_LAND)]
            [Description("getterrainheight")]
            GETTERRAINHEIGHT,
            [Description("northeast")] NORTHEAST,
            [Description("southwest")] SOUTHWEST,

            [IsCommand(true)]
            [CommandInputSyntax(
                "<command=configuration>&<group=<UUID|STRING>>&<password=<STRING>>&<action=<read|write|get|set>>&action=write:<data=<STRING>>&action=get:<path=<STRING>>&action=set:<path=<STRING>>&action=set:<data=<STRING>>&[callback=<STRING>]"
            )]
            [CommandPermissionMask((uint)Permissions.PERMISSION_SYSTEM)]
            [Description("configuration")]
            CONFIGURATION,

            [IsCommand(true)]
            [CommandInputSyntax(
                "<command=upload>&<group=<UUID|STRING>>&<password=<STRING>>&<name=<STRING>>&<type=<Texture|Sound|Animation|Clothing|Bodypart|Landmark|Gesture|Notecard|LSLText>>&type=Clothing:[wear=<WearableType>]&type=Bodypart:[wear=<WearableType>]&<data=<STRING>>&[callback=<STRING>]"
            )]
            [CommandPermissionMask((uint)Permissions.PERMISSION_INVENTORY | (uint)Permissions.PERMISSION_ECONOMY)]
            [Description("upload")]
            UPLOAD,

            [IsCommand(true)]
            [CommandInputSyntax(
                "<command=download>&<group=<UUID|STRING>>&<password=<STRING>>&<name=<STRING>>&<type=<Texture|Sound|Animation|Clothing|Bodypart|Landmark|Gesture|Notecard|LSLText>>&type=Texture:[format=<ImageFormat>]&[path=<STRING>]&[callback=<STRING>]"
            )]
            [CommandPermissionMask((uint)Permissions.PERMISSION_INTERACT | (uint)Permissions.PERMISSION_SYSTEM)]
            [Description("download")]
            DOWNLOAD,

            [IsCommand(true)]
            [CommandInputSyntax(
                    "<command=setparceldata>&<group=<UUID|STRING>>&<password=<STRING>>&[position=<VECTOR>]&[data=<Parcel[,Parcel...]>]&[region=<STRING>]&[callback=<STRING>]"
                )
            ]
            [CommandPermissionMask((uint)Permissions.PERMISSION_LAND)]
            [Description("setparceldata")]
            SETPARCELDATA,
            [Description("new")] NEW,
            [Description("old")] OLD,
            [Description("aggressor")] AGGRESSOR,
            [Description("magnitude")] MAGNITUDE,
            [Description("time")] TIME,
            [Description("victim")] VICTIM,

            [IsCommand(true)]
            [CommandInputSyntax(
                "<command=playgesture>&<group=<UUID|STRING>>&<password=<STRING>>&<item=<STRING|UUID>>&[callback=<STRING>]"
            )]
            [CommandPermissionMask((uint)Permissions.PERMISSION_GROOMING)]
            [Description("playgesture")]
            PLAYGESTURE,

            [IsCommand(true)]
            [CommandInputSyntax(
                "<command=jump>&<group=<UUID|STRING>>&<password=<STRING>>&<action=<start|stop>>&[callback=<STRING>]")]
            [CommandPermissionMask((uint)Permissions.PERMISSION_MOVEMENT)]
            [Description("jump")]
            JUMP,

            [IsCommand(true)]
            [CommandInputSyntax(
                "<command=crouch>&<group=<UUID|STRING>>&<password=<STRING>>&<action=<start|stop>>&[callback=<STRING>]")]
            [CommandPermissionMask((uint)Permissions.PERMISSION_MOVEMENT)]
            [Description("crouch")]
            CROUCH,

            [IsCommand(true)]
            [CommandInputSyntax(
                "<command=turnto>&<group=<UUID|STRING>>&<password=<STRING>>&<position=<VECTOR3>>&[callback=<STRING>]")]
            [CommandPermissionMask((uint)Permissions.PERMISSION_MOVEMENT)]
            [Description("turnto")]
            TURNTO,

            [IsCommand(true)]
            [CommandInputSyntax(
                "<command=nudge>&<group=<UUID|STRING>>&<password=<STRING>>&<direction=<left|right|up|down|back|forward>>&[callback=<STRING>]"
            )]
            [CommandPermissionMask((uint)Permissions.PERMISSION_MOVEMENT)]
            [Description("nudge")]
            NUDGE,

            [IsCommand(true)]
            [CommandInputSyntax(
                "<command=createnotecard>&<group=<UUID|STRING>>&<password=<STRING>>&<name=<STRING>>&[text=<STRING>]&[description=<STRING>]&[callback=<STRING>]"
            )]
            [CommandPermissionMask((uint)Permissions.PERMISSION_INVENTORY)]
            [Description("createnotecard")]
            CREATENOTECARD,
            [Description("direction")] DIRECTION,
            [Description("agent")] AGENT,

            [IsCommand(true)]
            [CommandInputSyntax(
                "<command=replytoinventoryoffer>&<group=<UUID|STRING>>&<password=<STRING>>&<action=<accept|decline>>&<session=<UUID>>&[folder=<STRING>]&[callback=<STRING>]"
            )]
            [CommandPermissionMask((uint)Permissions.PERMISSION_INVENTORY)]
            [Description("replytoinventoryoffer")]
            REPLYTOINVENTORYOFFER,

            [IsCommand(true)]
            [CommandInputSyntax(
                "<command=getinventoryoffers>&<group=<UUID|STRING>>&<password=<STRING>>&[callback=<STRING>]")]
            [CommandPermissionMask((uint)Permissions.PERMISSION_INVENTORY)]
            [Description("getinventoryoffers")]
            GETINVENTORYOFFERS,

            [IsCommand(true)]
            [CommandInputSyntax(
                "<command=updateprimitiveinventory>&<group=<UUID|STRING>>&<password=<STRING>>&<action=<add|remove|take>>&action=add:<entity=<UUID|STRING>>&action=remove:<entity=<UUID|STRING>>&action=take:<entity=<UUID|STRING>>&action=take:<folder=<UUID|STRING>>&[callback=<STRING>]"
            )]
            [CommandPermissionMask((uint)Permissions.PERMISSION_INTERACT)]
            [Description("updateprimitiveinventory")]
            UPDATEPRIMITIVEINVENTORY,

            [IsCommand(true)]
            [CommandInputSyntax("<command=version>&<group=<UUID|STRING>>&<password=<STRING>>&[callback=<STRING>]")]
            [CommandPermissionMask((uint)Permissions.PERMISSION_NONE)]
            [Description("version")]
            VERSION,

            [IsCommand(true)]
            [CommandInputSyntax(
                "<command=playsound>&<group=<UUID|STRING>>&<password=<STRING>>&<item=<UUID|STRING>>&[gain=<FLOAT>]&[position=<VECTOR3>]&[callback=<STRING>]"
            )]
            [CommandPermissionMask((uint)Permissions.PERMISSION_INTERACT)]
            [Description("playsound")]
            PLAYSOUND,
            [Description("gain")] GAIN,

            [IsCommand(true)]
            [CommandInputSyntax(
                "<command=getrolemembers>&<group=<UUID|STRING>>&<password=<STRING>>&<role=<UUID|STRING>>&[callback=<STRING>]"
            )]
            [CommandPermissionMask((uint)Permissions.PERMISSION_GROUP)]
            [Description("getrolemembers")]
            GETROLEMEMBERS,
            [Description("status")] STATUS,

            [IsCommand(true)]
            [CommandInputSyntax(
                "<command=getmembers>&<group=<UUID|STRING>>&<password=<STRING>>&[callback=<STRING>]")]
            [CommandPermissionMask((uint)Permissions.PERMISSION_GROUP)]
            [Description("getmembers")]
            GETMEMBERS,

            [IsCommand(true)]
            [CommandInputSyntax(
                "<command=replytoteleportlure>&<group=<UUID|STRING>>&<password=<STRING>>&<agent=<UUID>|firstname=<STRING>&lastname=<STRING>>&<session=<UUID>>&<action=<accept|decline>>&[callback=<STRING>]"
            )]
            [CommandPermissionMask((uint)Permissions.PERMISSION_MOVEMENT)]
            [Description("replytoteleportlure")]
            REPLYTOTELEPORTLURE,
            [Description("session")] SESSION,

            [IsCommand(true)]
            [CommandInputSyntax(
                "<command=replytoscriptpermissionrequest>&<group=<UUID|STRING>>&<password=<STRING>>&<task=<UUID>>&<item=<UUID>>&<permissions=<ScriptPermission>>&[callback=<STRING>]"
            )]
            [CommandPermissionMask((uint)Permissions.PERMISSION_INTERACT)]
            [Description("replytoscriptpermissionrequest")]
            REPLYTOSCRIPTPERMISSIONREQUEST,
            [Description("task")] TASK,

            [IsCommand(true)]
            [CommandInputSyntax(
                "<command=getparcellist>&<group=<UUID|STRING>>&<password=<STRING>>&[position=<VECTOR2>]&[region=<STRING>]&[callback=<STRING>]"
            )]
            [CommandPermissionMask((uint)Permissions.PERMISSION_LAND)]
            [Description("getparcellist")]
            GETPARCELLIST,

            [IsCommand(true)]
            [CommandInputSyntax(
                "<command=parcelrelease>&<group=<UUID|STRING>>&<password=<STRING>>&[position=<VECTOR2>]&[region=<STRING>]&[callback=<STRING>]"
            )]
            [CommandPermissionMask((uint)Permissions.PERMISSION_LAND)]
            [Description("parcelrelease")]
            PARCELRELEASE,

            [IsCommand(true)]
            [CommandInputSyntax(
                "<command=parcelbuy>&<group=<UUID|STRING>>&<password=<STRING>>&[position=<VECTOR2>]&[forgroup=<BOOL>]&[removecontribution=<BOOL>]&[region=<STRING>]&[callback=<STRING>]"
            )]
            [CommandPermissionMask((uint)Permissions.PERMISSION_LAND | (uint)Permissions.PERMISSION_ECONOMY)]
            [Description("parcelbuy")]
            PARCELBUY,
            [Description("removecontribution")] REMOVECONTRIBUTION,
            [Description("forgroup")] FORGROUP,

            [IsCommand(true)]
            [CommandInputSyntax(
                "<command=parceldeed>&<group=<UUID|STRING>>&<password=<STRING>>&[position=<VECTOR2>]&[region=<STRING>]&[callback=<STRING>]"
            )]
            [CommandPermissionMask((uint)Permissions.PERMISSION_LAND)]
            [Description("parceldeed")]
            PARCELDEED,

            [IsCommand(true)]
            [CommandInputSyntax(
                "<command=parcelreclaim>&<group=<UUID|STRING>>&<password=<STRING>>&[position=<VECTOR2>]&[region=<STRING>]&[callback=<STRING>]"
            )]
            [CommandPermissionMask((uint)Permissions.PERMISSION_LAND)]
            [Description("parcelreclaim")]
            PARCELRECLAIM,

            [IsCommand(true)]
            [CommandInputSyntax(
                "<command=unwear>&<group=<UUID|STRING>>&<password=<STRING>>&<wearables=<STRING[,UUID...]>>&[callback=<STRING>]"
            )]
            [CommandPermissionMask((uint)Permissions.PERMISSION_GROOMING)]
            [Description("unwear")]
            UNWEAR,

            [IsCommand(true)]
            [CommandInputSyntax(
                "<command=wear>&<group=<UUID|STRING>>&<password=<STRING>>&<wearables=<STRING[,UUID...]>>&[callback=<STRING>]"
            )]
            [CommandPermissionMask((uint)Permissions.PERMISSION_GROOMING)]
            [Description("wear")]
            WEAR,
            [Description("wearables")] WEARABLES,

            [IsCommand(true)]
            [CommandInputSyntax(
                "<command=getwearables>&<group=<UUID|STRING>>&<password=<STRING>>&[callback=<STRING>]")]
            [CommandPermissionMask((uint)Permissions.PERMISSION_GROOMING)]
            [Description("getwearables")]
            GETWEARABLES,

            [IsCommand(true)]
            [CommandInputSyntax(
                "<command=changeappearance>&<group=<UUID|STRING>>&<password=<STRING>>&<folder=<UUID|STRING>>&[callback=<STRING>]"
            )]
            [CommandPermissionMask((uint)Permissions.PERMISSION_GROOMING)]
            [Description("changeappearance")]
            CHANGEAPPEARANCE,
            [Description("folder")] FOLDER,
            [Description("replace")] REPLACE,

            [IsCommand(true)]
            [CommandInputSyntax(
                "<command=setobjectrotation>&<group=<UUID|STRING>>&<password=<STRING>>&<item=<UUID|STRING>>&[range=<FLOAT>]&<rotation=<QUARTERNION>>&[callback=<STRING>]"
            )]
            [CommandPermissionMask((uint)Permissions.PERMISSION_INTERACT)]
            [Description("setobjectrotation")]
            SETOBJECTROTATION,

            [IsCommand(true)]
            [CommandInputSyntax(
                "<command=setprimitivedescription>&<group=<UUID|STRING>>&<password=<STRING>>&<item=<UUID|STRING>>&[range=<FLOAT>]&<description=<STRING>>&[callback=<STRING>]"
            )]
            [CommandPermissionMask((uint)Permissions.PERMISSION_INTERACT)]
            [Description("setprimitivedescription")]
            SETPRIMITIVEDESCRIPTION,

            [IsCommand(true)]
            [CommandInputSyntax(
                "<command=setprimitivename>&<group=<UUID|STRING>>&<password=<STRING>>&<item=<UUID|STRING>>&[range=<FLOAT>]&<name=<STRING>>&[callback=<STRING>]"
            )]
            [CommandPermissionMask((uint)Permissions.PERMISSION_INTERACT)]
            [Description("setprimitivename")]
            SETPRIMITIVENAME,

            [IsCommand(true)]
            [CommandInputSyntax(
                "<command=setobjectposition>&<group=<UUID|STRING>>&<password=<STRING>>&<item=<UUID|STRING>>&[range=<FLOAT>]&<position=<VECTOR3>>&[callback=<STRING>]"
            )]
            [CommandPermissionMask((uint)Permissions.PERMISSION_INTERACT)]
            [Description("setobjectposition")]
            SETOBJECTPOSITION,

            [IsCommand(true)]
            [CommandInputSyntax(
                "<command=setobjectsaleinfo>&<group=<UUID|STRING>>&<password=<STRING>>&<item=<UUID|STRING>>&[range=<FLOAT>]&<price=<INTEGER>>&<type=<SaleType>>&[callback=<STRING>]"
            )]
            [CommandPermissionMask((uint)Permissions.PERMISSION_INTERACT)]
            [Description("setobjectsaleinfo")]
            SETOBJECTSALEINFO,

            [IsCommand(true)]
            [CommandInputSyntax(
                "<command=setobjectgroup>&<group=<UUID|STRING>>&<password=<STRING>>&<item=<UUID|STRING>>&[range=<FLOAT>]&[callback=<STRING>]"
            )]
            [CommandPermissionMask((uint)Permissions.PERMISSION_INTERACT)]
            [Description("setobjectgroup")]
            SETOBJECTGROUP,

            [IsCommand(true)]
            [CommandInputSyntax(
                "<command=objectdeed>&<group=<UUID|STRING>>&<password=<STRING>>&<item=<UUID|STRING>>&[range=<FLOAT>]&[callback=<STRING>]"
            )]
            [CommandPermissionMask((uint)Permissions.PERMISSION_INTERACT)]
            [Description("objectdeed")]
            OBJECTDEED,

            [IsCommand(true)]
            [CommandInputSyntax(
                "<command=setobjectpermissions>&<group=<UUID|STRING>>&<password=<STRING>>&<item=<UUID|STRING>>&[range=<FLOAT>]&<permissions=<STRING>>&[callback=<STRING>]"
            )]
            [CommandPermissionMask((uint)Permissions.PERMISSION_INTERACT)]
            [Description("setobjectpermissions")]
            SETOBJECTPERMISSIONS,
            [Description("permissions")] PERMISSIONS,

            [IsCommand(true)]
            [CommandInputSyntax(
                "<command=getavatarpositions>&<group=<UUID|STRING>>&<password=<STRING>>&<entity=<region|parcel>>&entity=parcel:<position=<VECTOR2>>&[region=<STRING>]&[callback=<STRING>]"
            )]
            [CommandPermissionMask((uint)Permissions.PERMISSION_INTERACT)]
            [Description("getavatarpositions")]
            GETAVATARPOSITIONS,
            [Description("delay")] DELAY,
            [Description("asset")] ASSET,

            [IsCommand(true)]
            [CommandInputSyntax(
                "<command=setregiondebug>&<group=<UUID|STRING>>&<password=<STRING>>&<scripts=<BOOL>>&<collisions=<BOOL>>&<physics=<BOOL>>&[callback=<STRING>]"
            )]
            [CommandPermissionMask((uint)Permissions.PERMISSION_LAND)]
            [Description("setregiondebug")]
            SETREGIONDEBUG,
            [Description("scripts")] SCRIPTS,
            [Description("collisions")] COLLISIONS,
            [Description("physics")] PHYSICS,

            [IsCommand(true)]
            [CommandInputSyntax(
                "<command=getmapavatarpositions>&<group=<UUID|STRING>>&<password=<STRING>>&<region=<STRING>>&[callback=<STRING>]"
            )]
            [CommandPermissionMask((uint)Permissions.PERMISSION_INTERACT)]
            [Description("getmapavatarpositions")]
            GETMAPAVATARPOSITIONS,

            [IsCommand(true)]
            [CommandInputSyntax(
                "<command=mapfriend>&<group=<UUID|STRING>>&<password=<STRING>>&<agent=<UUID>|firstname=<STRING>&lastname=<STRING>>&[callback=<STRING>]"
            )]
            [CommandPermissionMask((uint)Permissions.PERMISSION_FRIENDSHIP)]
            [Description("mapfriend")]
            MAPFRIEND,

            [IsCommand(true)]
            [CommandInputSyntax(
                "<command=replytofriendshiprequest>&<group=<UUID|STRING>>&<password=<STRING>>&<agent=<UUID>|firstname=<STRING>&lastname=<STRING>>&<action=<accept|decline>>&[callback=<STRING>]"
            )]
            [CommandPermissionMask((uint)Permissions.PERMISSION_FRIENDSHIP)]
            [Description("replytofriendshiprequest")]
            REPLYTOFRIENDSHIPREQUEST,

            [IsCommand(true)]
            [CommandInputSyntax(
                "<command=getfriendshiprequests>&<group=<UUID|STRING>>&<password=<STRING>>&[callback=<STRING>]")]
            [CommandPermissionMask((uint)Permissions.PERMISSION_FRIENDSHIP)]
            [Description("getfriendshiprequests")]
            GETFRIENDSHIPREQUESTS,

            [IsCommand(true)]
            [CommandInputSyntax(
                "<command=grantfriendrights>&<group=<UUID|STRING>>&<password=<STRING>>&<agent=<UUID>|firstname=<STRING>&lastname=<STRING>>&<rights=<FriendRights>>&[callback=<STRING>]"
            )]
            [CommandPermissionMask((uint)Permissions.PERMISSION_FRIENDSHIP)]
            [Description("grantfriendrights")]
            GRANTFRIENDRIGHTS,
            [Description("rights")] RIGHTS,

            [IsCommand(true)]
            [CommandInputSyntax(
                "<command=getfriendslist>&<group=<UUID|STRING>>&<password=<STRING>>&[callback=<STRING>]"
            )]
            [CommandPermissionMask((uint)Permissions.PERMISSION_FRIENDSHIP)]
            [Description("getfriendslist")]
            GETFRIENDSLIST,

            [IsCommand(true)]
            [CommandInputSyntax(
                "<command=terminatefriendship>&<group=<UUID|STRING>>&<password=<STRING>>&<agent=<UUID>|firstname=<STRING>&lastname=<STRING>>&[callback=<STRING>]"
            )]
            [CommandPermissionMask((uint)Permissions.PERMISSION_FRIENDSHIP)]
            [Description("terminatefriendship")]
            TERMINATEFRIENDSHIP,

            [IsCommand(true)]
            [CommandInputSyntax(
                "<command=offerfriendship>&<group=<UUID|STRING>>&<password=<STRING>>&<agent=<UUID>|firstname=<STRING>&lastname=<STRING>>&[callback=<STRING>]"
            )]
            [CommandPermissionMask((uint)Permissions.PERMISSION_FRIENDSHIP)]
            [Description("offerfriendship")]
            OFFERFRIENDSHIP,

            [IsCommand(true)]
            [CommandInputSyntax(
                "<command=getfrienddata>&<group=<UUID|STRING>>&<password=<STRING>>&<agent=<UUID>|firstname=<STRING>&lastname=<STRING>>&<data=<FriendInfo[,FriendInfo...]>>&[callback=<STRING>]"
            )]
            [CommandPermissionMask((uint)Permissions.PERMISSION_FRIENDSHIP)]
            [Description("getfrienddata")]
            GETFRIENDDATA,
            [Description("days")] DAYS,
            [Description("interval")] INTERVAL,

            [IsCommand(true)]
            [CommandInputSyntax(
                "<command=getgroupaccountsummarydata>&<group=<UUID|STRING>>&<password=<STRING>>&<data=<GroupAccountSummary[,GroupAccountSummary...]>>&<days=<INTEGER>>&<interval=<INTEGER>>&[callback=<STRING>]"
            )]
            [CommandPermissionMask((uint)Permissions.PERMISSION_GROUP)]
            [Description("getgroupaccountsummarydata")]
            GETGROUPACCOUNTSUMMARYDATA,

            [IsCommand(true)]
            [CommandInputSyntax(
                    "<command=getselfdata>&<group=<UUID|STRING>>&<password=<STRING>>&<data=<AgentManager[,AgentManager...]>>&[callback=<STRING>]"
                )
            ]
            [CommandPermissionMask((uint)Permissions.PERMISSION_GROOMING)]
            [Description("getselfdata")]
            GETSELFDATA,

            [IsCommand(true)]
            [CommandInputSyntax(
                "<command=deleteclassified>&<group=<UUID|STRING>>&<password=<STRING>>&<name=<STRING>>&[callback=<STRING>]"
            )]
            [CommandPermissionMask((uint)Permissions.PERMISSION_GROOMING)]
            [Description("deleteclassified")]
            DELETECLASSIFIED,

            [IsCommand(true)]
            [CommandInputSyntax(
                "<command=addclassified>&<group=<UUID|STRING>>&<password=<STRING>>&<name=<STRING>>&<price=<INTEGER>>&<type=<Any|Shopping|LandRental|PropertyRental|SpecialAttraction|NewProducts|Employment|Wanted|Service|Personal>>&[item=<UUID|STRING>]&[description=<STRING>]&[renew=<BOOL>]&[callback=<STRING>]"
            )]
            [CommandPermissionMask((uint)Permissions.PERMISSION_GROOMING | (uint)Permissions.PERMISSION_ECONOMY)]
            [Description("addclassified")]
            ADDCLASSIFIED,
            [Description("price")] PRICE,
            [Description("renew")] RENEW,

            [IsCommand(true)]
            [CommandInputSyntax("<command=logout>&<group=<UUID|STRING>>&<password=<STRING>>&[callback=<STRING>]")]
            [CommandPermissionMask((uint)Permissions.PERMISSION_SYSTEM)]
            [Description("logout")]
            LOGOUT,

            [IsCommand(true)]
            [CommandInputSyntax(
                "<command=displayname>&<group=<UUID|STRING>>&<password=<STRING>>&<action=<get|set>>&action=set:<name=<STRING>>&[callback=<STRING>]"
            )]
            [CommandPermissionMask((uint)Permissions.PERMISSION_GROOMING)]
            [Description("displayname")]
            DISPLAYNAME,

            [IsCommand(true)]
            [CommandInputSyntax(
                "<command=returnprimitives>&<group=<UUID|STRING>>&<password=<STRING>>&<agent=<UUID>|firstname=<STRING>&lastname=<STRING>>&<entity=<parcel|estate>>&<type=<Owner|Group|Other|Sell|ReturnScripted|ReturnOnOthersLand|ReturnScriptedAndOnOthers>>&type=Owner|Group|Other|Sell:[position=<VECTOR2>]&type=ReturnScripted|ReturnOnOthersLand|ReturnScriptedAndOnOthers:[all=<BOOL>]&[region=<STRING>]&[callback=<STRING>]"
            )]
            [CommandPermissionMask((uint)Permissions.PERMISSION_LAND)]
            [Description("returnprimitives")]
            RETURNPRIMITIVES,

            [IsCommand(true)]
            [CommandInputSyntax(
                "<command=getgroupdata>&<group=<UUID|STRING>>&<password=<STRING>>&<data=<Group[,Group...]>>&[callback=<STRING>]"
            )]
            [CommandPermissionMask((uint)Permissions.PERMISSION_GROUP)]
            [Description("getgroupdata")]
            GETGROUPDATA,

            [IsCommand(true)]
            [CommandInputSyntax(
                "<command=getavatardata>&<group=<UUID|STRING>>&<password=<STRING>>&<agent=<UUID>|firstname=<STRING>&lastname=<STRING>>&<data=<Avatar[,Avatar...]>>&[callback=<STRING>]"
            )]
            [CommandPermissionMask((uint)Permissions.PERMISSION_INTERACT)]
            [Description("getavatardata")]
            GETAVATARDATA,

            [IsCommand(true)]
            [CommandInputSyntax(
                "<command=getprimitiveinventory>&<group=<UUID|STRING>>&<password=<STRING>>&<item=<UUID|STRING>>&[range=<FLOAT>]&[callback=<STRING>]"
            )]
            [CommandPermissionMask((uint)Permissions.PERMISSION_INTERACT)]
            [Description("getprimitiveinventory")]
            GETPRIMITIVEINVENTORY,

            [IsCommand(true)]
            [CommandInputSyntax(
                "<command=getinventorydata>&<group=<UUID|STRING>>&<password=<STRING>>&<item=<UUID|STRING>>&[range=<FLOAT>]&<data=<InventoryItem[,InventoryItem...]>>&[callback=<STRING>]"
            )]
            [CommandPermissionMask((uint)Permissions.PERMISSION_INVENTORY)]
            [Description("getinventorydata")]
            GETINVENTORYDATA,

            [IsCommand(true)]
            [CommandInputSyntax(
                "<command=getprimitiveinventorydata>&<group=<UUID|STRING>>&<password=<STRING>>&<item=<UUID|STRING>>&[range=<FLOAT>]&<data=<InventoryItem[,InventoryItem...]>>&<entity=<STRING|UUID>>&[callback=<STRING>]"
            )]
            [CommandPermissionMask((uint)Permissions.PERMISSION_INTERACT)]
            [Description("getprimitiveinventorydata")]
            GETPRIMITIVEINVENTORYDATA,

            [IsCommand(true)]
            [CommandInputSyntax(
                "<command=getscriptrunning>&<group=<UUID|STRING>>&<password=<STRING>>&<item=<UUID|STRING>>&[range=<FLOAT>]&<entity=<STRING|UUID>>&[callback=<STRING>]"
            )]
            [CommandPermissionMask((uint)Permissions.PERMISSION_INTERACT)]
            [Description("getscriptrunning")]
            GETSCRIPTRUNNING,

            [IsCommand(true)]
            [CommandInputSyntax(
                "<command=setscriptrunning>&<group=<UUID|STRING>>&<password=<STRING>>&<item=<UUID|STRING>>&[range=<FLOAT>]&<entity=<STRING|UUID>>&<action=<start|stop>>&[callback=<STRING>]"
            )]
            [CommandPermissionMask((uint)Permissions.PERMISSION_INTERACT)]
            [Description("setscriptrunning")]
            SETSCRIPTRUNNING,

            [IsCommand(true)]
            [CommandInputSyntax(
                "<command=derez>&<group=<UUID|STRING>>&<password=<STRING>>&<item=<UUID|STRING>>&[range=<FLOAT>]&[folder=<STRING|UUID>]&[type=<DeRezDestination>]&[callback=<STRING>]"
            )]
            [CommandPermissionMask((uint)Permissions.PERMISSION_INTERACT)]
            [Description("derez")]
            DEREZ,

            [IsCommand(true)]
            [CommandInputSyntax(
                "<command=getparceldata>&<group=<UUID|STRING>>&<password=<STRING>>&<data=<Parcel[,Parcel...]>>&[position=<VECTOR2>]&[region=<STRING>]&[callback=<STRING>]"
            )]
            [CommandPermissionMask((uint)Permissions.PERMISSION_LAND)]
            [Description("getparceldata")]
            GETPARCELDATA,

            [IsCommand(true)]
            [CommandInputSyntax(
                "<command=rez>&<group=<UUID|STRING>>&<password=<STRING>>&<position=<VECTOR2>>&<item=<UUID|STRING>&[rotation=<QUARTERNION>]&[region=<STRING>]&[callback=<STRING>]"
            )]
            [CommandPermissionMask((uint)Permissions.PERMISSION_INTERACT)]
            [Description("rez")]
            REZ,
            [Description("rotation")] ROTATION,
            [Description("index")] INDEX,

            [IsCommand(true)]
            [CommandInputSyntax(
                "<command=replytoscriptdialog>&<group=<UUID|STRING>>&<password=<STRING>>&<channel=<INTEGER>>&<index=<INTEGER>&<button=<STRING>>&<item=<UUID>>&[callback=<STRING>]"
            )]
            [CommandPermissionMask((uint)Permissions.PERMISSION_INTERACT)]
            [Description("replytoscriptdialog")]
            REPLYTOSCRIPTDIALOG,
            [Description("owner")] OWNER,
            [Description("button")] BUTTON,

            [IsCommand(true)]
            [CommandInputSyntax(
                "<command=getanimations>&<group=<UUID|STRING>>&<password=<STRING>>&[callback=<STRING>]")
            ]
            [CommandPermissionMask((uint)Permissions.PERMISSION_GROOMING)]
            [Description("getanimations")]
            GETANIMATIONS,

            [IsCommand(true)]
            [CommandInputSyntax(
                "<command=animation>&<group=<UUID|STRING>>&<password=<STRING>>&<item=<UUID|STRING>>&<action=<start|stop>>&[callback=<STRING>]"
            )]
            [CommandPermissionMask((uint)Permissions.PERMISSION_GROOMING)]
            [Description("animation")]
            ANIMATION,

            [IsCommand(true)]
            [CommandInputSyntax(
                "<command=setestatelist>&<group=<UUID|STRING>>&<password=<STRING>>&<type=<ban|group|manager|user>>&<action=<add|remove>>&type=ban|manager|user,action=add|remove:<agent=<UUID>|firstname=<STRING>&lastname=<STRING>>&type=group,action=add|remove:<target=<STRING|UUID>>&[all=<BOOL>]&[callback=<STRING>]"
            )]
            [CommandPermissionMask((uint)Permissions.PERMISSION_LAND)]
            [Description("setestatelist")]
            SETESTATELIST,

            [IsCommand(true)]
            [CommandInputSyntax(
                "<command=getestatelist>&<group=<UUID|STRING>>&<password=<STRING>>&<type=<ban|group|manager|user>>&[callback=<STRING>]"
            )]
            [CommandPermissionMask((uint)Permissions.PERMISSION_LAND)]
            [Description("getestatelist")]
            GETESTATELIST,
            [Description("all")] ALL,

            [IsCommand(true)]
            [CommandInputSyntax(
                "<command=getregiontop>&<group=<UUID|STRING>>&<password=<STRING>>&<type=<scripts|colliders>>&[callback=<STRING>]"
            )]
            [CommandPermissionMask((uint)Permissions.PERMISSION_LAND)]
            [Description("getregiontop")]
            GETREGIONTOP,

            [IsCommand(true)]
            [CommandInputSyntax(
                "<command=restartregion>&<group=<UUID|STRING>>&<password=<STRING>>&<action=<scripts|colliders>>&[delay=<INTEGER>]&[callback=<STRING>]"
            )]
            [CommandPermissionMask((uint)Permissions.PERMISSION_LAND)]
            [Description("restartregion")]
            RESTARTREGION,

            [IsCommand(true)]
            [CommandInputSyntax(
                "<command=directorysearch>&<group=<UUID|STRING>>&<password=<STRING>>&<type=<classified|event|group|land|people|places>>&type=classified:<data=<Classified[,Classified...]>>&type=classified:<name=<STRING>>&type=event:<data=<EventsSearchData[,EventSearchData...]>>&type=event:<name=<STRING>>&type=group:<data=<GroupSearchData[,GroupSearchData...]>>&type=land:<data=<DirectoryParcel[,DirectoryParcel...]>>&type=people:<data=<AgentSearchData[,AgentSearchData...]>>&type=places:<data=<DirectoryParcel[,DirectoryParcel...]>>&[callback=<STRING>]"
            )]
            [CommandPermissionMask((uint)Permissions.PERMISSION_DIRECTORY)]
            [Description("directorysearch")]
            DIRECTORYSEARCH,

            [IsCommand(true)]
            [CommandInputSyntax(
                "<command=getprofiledata>&<group=<UUID|STRING>>&<password=<STRING>>&<agent=<UUID>|firstname=<STRING>&lastname=<STRING>>&<data=<AvatarProperties[,AvatarProperties...]>>&[callback=<STRING>]"
            )]
            [CommandPermissionMask((uint)Permissions.PERMISSION_INTERACT)]
            [Description("getprofiledata")]
            GETPROFILEDATA,

            [IsCommand(true)]
            [CommandInputSyntax(
                "<command=getparticlesystem>&<group=<UUID|STRING>>&<password=<STRING>>&<item=<UUID|STRING>>&[range=<FLOAT>]&[callback=<STRING>]"
            )]
            [CommandPermissionMask((uint)Permissions.PERMISSION_INTERACT)]
            [Description("getparticlesystem")]
            GETPARTICLESYSTEM,
            [Description("data")] DATA,
            [Description("range")] RANGE,
            [Description("balance")] BALANCE,
            [Description("key")] KEY,
            [Description("value")] VALUE,

            [IsCommand(true)]
            [CommandInputSyntax(
                "<command=database>&<group=<UUID|STRING>>&<password=<STRING>>&<action=<get|set|delete>>&action=get|delete:<key=<STRING>>&action=set:<key=<STRING>>&action=set:<value=<STRING>>&[callback=<STRING>]"
            )]
            [CommandPermissionMask((uint)Permissions.PERMISSION_DATABASE)]
            [Description("database")]
            DATABASE,
            [Description("text")] TEXT,
            [Description("quorum")] QUORUM,
            [Description("majority")] MAJORITY,

            [IsCommand(true)]
            [CommandInputSyntax(
                "<command=startproposal>&<group=<UUID|STRING>>&<password=<STRING>>&<duration=<INTEGER>>&<majority=<FLOAT>>&<quorum=<INTEGER>>&<text=<STRING>>&[callback=<STRING>]"
            )]
            [CommandPermissionMask((uint)Permissions.PERMISSION_GROUP)]
            [Description("startproposal")]
            STARTPROPOSAL,
            [Description("duration")] DURATION,
            [Description("action")] ACTION,

            [IsCommand(true)]
            [CommandInputSyntax(
                "<command=deletefromrole>&<group=<UUID|STRING>>&<password=<STRING>>&<agent=<UUID>|firstname=<STRING>&lastname=<STRING>>&<role=<UUID|STRING>>&[callback=<STRING>]"
            )]
            [CommandPermissionMask((uint)Permissions.PERMISSION_GROUP)]
            [Description("deletefromrole")]
            DELETEFROMROLE,

            [IsCommand(true)]
            [CommandInputSyntax(
                "<command=addtorole>&<group=<UUID|STRING>>&<password=<STRING>>&<agent=<UUID>|firstname=<STRING>&lastname=<STRING>>&<role=<UUID|STRING>>&[callback=<STRING>]"
            )]
            [CommandPermissionMask((uint)Permissions.PERMISSION_GROUP)]
            [Description("addtorole")]
            ADDTOROLE,

            [IsCommand(true)]
            [CommandInputSyntax("<command=leave>&<group=<UUID|STRING>>&<password=<STRING>>&[callback=<STRING>]")]
            [CommandPermissionMask((uint)Permissions.PERMISSION_GROUP)]
            [Description("leave")]
            LEAVE,

            [IsCommand(true)]
            [CommandInputSyntax(
                "<command=updategroupdata>&<group=<UUID|STRING>>&<password=<STRING>>&<data=<[Charter<,STRING>][,ListInProfile<,BOOL>][,MembershipFee<,INTEGER>][,OpenEnrollment<,BOOL>][,ShowInList<,BOOL>]>>&[callback=<STRING>]"
            )]
            [CommandPermissionMask((uint)Permissions.PERMISSION_GROUP)]
            [Description("updategroupdata")]
            UPDATEGROUPDATA,

            [IsCommand(true)]
            [CommandInputSyntax(
                "<command=eject>&<group=<UUID|STRING>>&<password=<STRING>>&<agent=<UUID>|firstname=<STRING>&lastname=<STRING>>&[callback=<STRING>]"
            )]
            [CommandPermissionMask((uint)Permissions.PERMISSION_GROUP)]
            [Description("eject")]
            EJECT,

            [IsCommand(true)]
            [CommandInputSyntax(
                "<command=invite>&<group=<UUID|STRING>>&<password=<STRING>>&<agent=<UUID>|firstname=<STRING>&lastname=<STRING>>&[role=<UUID[,STRING...]>]&[callback=<STRING>]"
            )]
            [CommandPermissionMask((uint)Permissions.PERMISSION_GROUP)]
            [Description("invite")]
            INVITE,

            [IsCommand(true)]
            [CommandInputSyntax("<command=join>&<group=<UUID|STRING>>&<password=<STRING>>&[callback=<STRING>]")]
            [CommandPermissionMask((uint)Permissions.PERMISSION_GROUP | (uint)Permissions.PERMISSION_ECONOMY)]
            [Description("join")]
            JOIN,
            [Description("callback")] CALLBACK,
            [Description("group")] GROUP,
            [Description("password")] PASSWORD,
            [Description("firstname")] FIRSTNAME,
            [Description("lastname")] LASTNAME,
            [Description("command")] COMMAND,
            [Description("role")] ROLE,
            [Description("title")] TITLE,

            [IsCommand(true)]
            [CommandInputSyntax(
                "<command=tell>&<group=<UUID|STRING>>&<password=<STRING>>&<entity=<local|group|avatar|estate|region>>&entity=local:<type=<Normal|Whisper|Shout>>&entity=local,type=Normal|Whisper|Shout:[channel=<INTEGER>]&entity=avatar:<agent=<UUID>|firstname=<STRING>&lastname=<STRING>>&[callback=<STRING>]"
            )]
            [CommandPermissionMask((uint)Permissions.PERMISSION_TALK)]
            [Description("tell")]
            TELL,

            [IsCommand(true)]
            [CommandInputSyntax(
                "<command=notice>&<group=<UUID|STRING>>&<password=<STRING>>&<message=<STRING>>&[subject=<STRING>]&[item=<UUID|STRING>]&[callback=<STRING>]"
            )]
            [CommandPermissionMask((uint)Permissions.PERMISSION_GROUP)]
            [Description("notice")]
            NOTICE,
            [Description("message")] MESSAGE,
            [Description("subject")] SUBJECT,
            [Description("item")] ITEM,

            [IsCommand(true)]
            [CommandInputSyntax(
                "<command=pay>&<group=<UUID|STRING>>&<password=<STRING>>&<entity=<avatar|object|group>>&entity=avatar:<agent=<UUID>|firstname=<STRING>&lastname=<STRING>>&entity=object:<target=<UUID>>&[reason=<STRING>]&[callback=<STRING>]"
            )]
            [CommandPermissionMask((uint)Permissions.PERMISSION_ECONOMY)]
            [Description("pay")]
            PAY,
            [Description("amount")] AMOUNT,
            [Description("target")] TARGET,
            [Description("reason")] REASON,

            [IsCommand(true)]
            [CommandInputSyntax(
                "<command=getbalance>&<group=<UUID|STRING>>&<password=<STRING>>&[callback=<STRING>]")]
            [CommandPermissionMask((uint)Permissions.PERMISSION_ECONOMY)]
            [Description("getbalance")]
            GETBALANCE,

            [IsCommand(true)]
            [CommandInputSyntax(
                "<command=teleport>&<group=<UUID|STRING>>&<password=<STRING>>&<region=<STRING>>&[position=<VECTOR3>]&[callback=<STRING>]"
            )]
            [CommandPermissionMask((uint)Permissions.PERMISSION_MOVEMENT)]
            [Description("teleport")]
            TELEPORT,
            [Description("region")] REGION,
            [Description("position")] POSITION,

            [IsCommand(true)]
            [CommandInputSyntax(
                "<command=getregiondata>&<group=<UUID|STRING>>&<password=<STRING>>&<data=<Simulator[,Simulator...]>>&[region=<STRING>]&[callback=<STRING>]"
            )]
            [CommandPermissionMask((uint)Permissions.PERMISSION_LAND)]
            [Description("getregiondata")]
            GETREGIONDATA,

            [IsCommand(true)]
            [CommandInputSyntax(
                "<command=sit>&<group=<UUID|STRING>>&<password=<STRING>>&<item=<UUID|STRING>>&[range=<FLOAT>]&[callback=<STRING>]"
            )]
            [CommandPermissionMask((uint)Permissions.PERMISSION_MOVEMENT)]
            [Description("sit")]
            SIT,

            [IsCommand(true)]
            [CommandInputSyntax("<command=stand>&<group=<UUID|STRING>>&<password=<STRING>>&[callback=<STRING>]")]
            [CommandPermissionMask((uint)Permissions.PERMISSION_MOVEMENT)]
            [Description("stand")]
            STAND,
            [Description("ban")] BAN,

            [IsCommand(true)]
            [CommandInputSyntax(
                "<command=parceleject>&<group=<UUID|STRING>>&<password=<STRING>>&<agent=<UUID>|firstname=<STRING>&lastname=<STRING>>&[ban=<BOOL>]&[position=<VECTOR2>]&[region=<STRING>]&[callback=<STRING>]"
            )]
            [CommandPermissionMask((uint)Permissions.PERMISSION_LAND)]
            [Description("parceleject")]
            PARCELEJECT,

            [IsCommand(true)]
            [CommandInputSyntax(
                "<command=creategroup>&<group=<UUID|STRING>>&<password=<STRING>>&<data=<Group[,Group...]>>&[callback=<STRING>]"
            )]
            [CommandPermissionMask((uint)Permissions.PERMISSION_GROUP | (uint)Permissions.PERMISSION_ECONOMY)]
            [Description("creategroup")]
            CREATEGROUP,

            [IsCommand(true)]
            [CommandInputSyntax(
                "<command=parcelfreeze>&<group=<UUID|STRING>>&<password=<STRING>>&<agent=<UUID>|firstname=<STRING>&lastname=<STRING>>&[freeze=<BOOL>]&[region=<STRING>]&[callback=<STRING>]"
            )]
            [CommandPermissionMask((uint)Permissions.PERMISSION_LAND)]
            [Description("parcelfreeze")]
            PARCELFREEZE,

            [IsCommand(true)]
            [CommandInputSyntax(
                "<command=createrole>&<group=<UUID|STRING>>&<password=<STRING>>&<role=<STRING>>&[powers=<GroupPowers[,GroupPowers...]>]&[title=<STRING>]&[description=<STRING>]&[callback=<STRING>]"
            )]
            [CommandPermissionMask((uint)Permissions.PERMISSION_GROUP)]
            [Description("createrole")]
            CREATEROLE,

            [IsCommand(true)]
            [CommandInputSyntax(
                "<command=deleterole>&<group=<UUID|STRING>>&<password=<STRING>>&<role=<STRING|UUID>>&[callback=<STRING>]"
            )]
            [CommandPermissionMask((uint)Permissions.PERMISSION_GROUP)]
            [Description("deleterole")]
            DELETEROLE,

            [IsCommand(true)]
            [CommandInputSyntax(
                "<command=getrolesmembers>&<group=<UUID|STRING>>&<password=<STRING>>&[callback=<STRING>]")]
            [CommandPermissionMask((uint)Permissions.PERMISSION_GROUP)]
            [Description("getrolesmembers")]
            GETROLESMEMBERS,

            [IsCommand(true)]
            [CommandInputSyntax("<command=getroles>&<group=<UUID|STRING>>&<password=<STRING>>&[callback=<STRING>]")]
            [CommandPermissionMask((uint)Permissions.PERMISSION_GROUP)]
            [Description("getroles")]
            GETROLES,

            [IsCommand(true)]
            [CommandInputSyntax(
                "<command=getrolepowers>&<group=<UUID|STRING>>&<password=<STRING>>&<role=<UUID|STRING>>&[callback=<STRING>]"
            )]
            [CommandPermissionMask((uint)Permissions.PERMISSION_GROUP)]
            [Description("getrolepowers")]
            GETROLEPOWERS,
            [Description("powers")] POWERS,

            [IsCommand(true)]
            [CommandInputSyntax(
                "<command=lure>&<group=<UUID|STRING>>&<password=<STRING>>&<agent=<UUID>|firstname=<STRING>&lastname=<STRING>>&[callback=<STRING>]"
            )]
            [CommandPermissionMask((uint)Permissions.PERMISSION_MOVEMENT)]
            [Description("lure")]
            LURE,
            [Description("URL")] URL,

            [IsCommand(true)]
            [CommandInputSyntax("<command=sethome>&<group=<UUID|STRING>>&<password=<STRING>>&[callback=<STRING>]")]
            [CommandPermissionMask((uint)Permissions.PERMISSION_GROOMING)]
            [Description("sethome")]
            SETHOME,

            [IsCommand(true)]
            [CommandInputSyntax("<command=gohome>&<group=<UUID|STRING>>&<password=<STRING>>&[callback=<STRING>]")]
            [CommandPermissionMask((uint)Permissions.PERMISSION_MOVEMENT)]
            [Description("gohome")]
            GOHOME,

            [IsCommand(true)]
            [CommandInputSyntax(
                "<command=setprofiledata>&<group=<UUID|STRING>>&<password=<STRING>>&<data=<AvatarProperties[,AvatarProperties...]>>&[callback=<STRING>]"
            )]
            [CommandPermissionMask((uint)Permissions.PERMISSION_GROOMING)]
            [Description("setprofiledata")]
            SETPROFILEDATA,

            [IsCommand(true)]
            [CommandInputSyntax(
                "<command=give>&<group=<UUID|STRING>>&<password=<STRING>>&<entity=<avatar|object>>&entity=avatar:<agent=<UUID>|firstname=<STRING>&lastname=<STRING>>&entity=avatar:<item=<UUID|STRING>&entity=object:<item=<UUID|STRING>&entity=object:[range=<FLOAT>]&entity=object:<target=<UUID|STRING>&[callback=<STRING>]"
            )]
            [CommandPermissionMask((uint)Permissions.PERMISSION_INVENTORY)]
            [Description("give")]
            GIVE,

            [IsCommand(true)]
            [CommandInputSyntax(
                "<command=deleteitem>&<group=<UUID|STRING>>&<password=<STRING>>&<item=<STRING|UUID>>&[callback=<STRING>]"
            )]
            [CommandPermissionMask((uint)Permissions.PERMISSION_INVENTORY)]
            [Description("deleteitem")]
            DELETEITEM,

            [IsCommand(true)]
            [CommandInputSyntax(
                "<command=emptytrash>&<group=<UUID|STRING>>&<password=<STRING>>&[callback=<STRING>]")]
            [CommandPermissionMask((uint)Permissions.PERMISSION_INVENTORY)]
            [Description("emptytrash")]
            EMPTYTRASH,

            [IsCommand(true)]
            [CommandInputSyntax(
                "<command=fly>&<group=<UUID|STRING>>&<password=<STRING>>&<action=<start|stop>>&[callback=<STRING>]")]
            [CommandPermissionMask((uint)Permissions.PERMISSION_MOVEMENT)]
            [Description("fly")]
            FLY,

            [IsCommand(true)]
            [CommandInputSyntax(
                "<command=addpick>&<group=<UUID|STRING>>&<password=<STRING>>&<name=<STRING>>&[description=<STRING>]&[item=<STRING|UUID>]&[callback=<STRING>]"
            )]
            [CommandPermissionMask((uint)Permissions.PERMISSION_GROOMING)]
            [Description("addpick")]
            ADDPICK,

            [IsCommand(true)]
            [CommandInputSyntax(
                "<command=deletepick>&<group=<UUID|STRING>>&<password=<STRING>>&<name=<STRING>>&[callback=<STRING>]")]
            [CommandPermissionMask((uint)Permissions.PERMISSION_GROOMING)]
            [Description("deltepick")]
            DELETEPICK,

            [IsCommand(true)]
            [CommandInputSyntax(
                "<command=touch>&<group=<UUID|STRING>>&<password=<STRING>>&<item=<UUID|STRING>>&[range=<FLOAT>]&[callback=<STRING>]"
            )]
            [CommandPermissionMask((uint)Permissions.PERMISSION_INTERACT)]
            [Description("touch")]
            TOUCH,

            [IsCommand(true)]
            [CommandInputSyntax(
                "<command=moderate>&<group=<UUID|STRING>>&<password=<STRING>>&<agent=<UUID>|firstname=<STRING>&lastname=<STRING>>&<type=<voice|text>>&<silence=<BOOL>>&[callback=<STRING>]"
            )]
            [CommandPermissionMask((uint)Permissions.PERMISSION_GROUP)]
            [Description("moderate")]
            MODERATE,
            [Description("type")] TYPE,
            [Description("silence")] SILENCE,
            [Description("freeze")] FREEZE,

            [IsCommand(true)]
            [CommandInputSyntax("<command=rebake>&<group=<UUID|STRING>>&<password=<STRING>>&[callback=<STRING>]")]
            [CommandPermissionMask((uint)Permissions.PERMISSION_GROOMING)]
            [Description("rebake")]
            REBAKE,

            [IsCommand(true)]
            [CommandInputSyntax(
                "<command=getattachments>&<group=<UUID|STRING>>&<password=<STRING>>&[callback=<STRING>]"
            )]
            [CommandPermissionMask((uint)Permissions.PERMISSION_GROOMING)]
            [Description("getattachments")]
            GETATTACHMENTS,

            [IsCommand(true)]
            [CommandInputSyntax(
                "<command=attach>&<group=<UUID|STRING>>&<password=<STRING>>&<attachments=<AttachmentPoint<,<UUID|STRING>>[,AttachmentPoint<,<UUID|STRING>>...]>&[callback=<STRING>]"
            )]
            [CommandPermissionMask((uint)Permissions.PERMISSION_GROOMING)]
            [Description("attach")]
            ATTACH,
            [Description("attachments")] ATTACHMENTS,

            [IsCommand(true)]
            [CommandInputSyntax(
                "<command=detach>&<group=<UUID|STRING>>&<password=<STRING>>&<attachments=<STRING[,UUID...]>&[callback=<STRING>]"
            )]
            [CommandPermissionMask((uint)Permissions.PERMISSION_GROOMING)]
            [Description("detach")]
            DETACH,

            [IsCommand(true)]
            [CommandInputSyntax(
                "<command=getprimitiveowners>&<group=<UUID|STRING>>&<password=<STRING>>&[position=<VECTOR2>]&[region=<STRING>]&[callback=<STRING>]"
            )]
            [CommandPermissionMask((uint)Permissions.PERMISSION_LAND)]
            [Description("getprimitiveowners")]
            GETPRIMITIVEOWNERS,
            [Description("entity")] ENTITY,
            [Description("channel")] CHANNEL,
            [Description("name")] NAME,
            [Description("description")] DESCRIPTION,

            [IsCommand(true)]
            [CommandInputSyntax(
                "<command=getprimitivedata>&<group=<UUID|STRING>>&<password=<STRING>>&<item=<UUID|STRING>>&[range=<FLOAT>]&<data=<Primitive[,Primitive...]>>&[callback=<STRING>]"
            )]
            [CommandPermissionMask((uint)Permissions.PERMISSION_INTERACT)]
            [Description("getprimitivedata")]
            GETPRIMITIVEDATA,

            [IsCommand(true)]
            [CommandInputSyntax("<command=activate>&<group=<UUID|STRING>>&<password=<STRING>>&[callback=<STRING>]")]
            [CommandPermissionMask((uint)Permissions.PERMISSION_GROOMING)]
            [Description("activate")]
            ACTIVATE,

            [IsCommand(true)]
            [CommandInputSyntax(
                "<command=autopilot>&<group=<UUID|STRING>>&<password=<STRING>>&<position=<VECTOR2>>&<action=<start|stop>>&[callback=<STRING>]"
            )]
            [CommandPermissionMask((uint)Permissions.PERMISSION_MOVEMENT)]
            [Description("autopilot")]
            AUTOPILOT,

            [IsCommand(true)]
            [CommandInputSyntax(
                "<command=mute>&<group=<UUID|STRING>>&<password=<STRING>>&<name=<STRING>>&<target=<UUID>>&<action=<mute|unmute>>&[callback=<STRING>]"
            )]
            [CommandPermissionMask((uint)Permissions.PERMISSION_MUTE)]
            [Description("mute")]
            MUTE,

            [IsCommand(true)]
            [CommandInputSyntax("<command=getmutes>&<group=<UUID|STRING>>&<password=<STRING>>&[callback=<STRING>]")]
            [CommandPermissionMask((uint)Permissions.PERMISSION_MUTE)]
            [Description("getmutes")]
            GETMUTES,

            [IsCommand(true)]
            [CommandInputSyntax("<command=getmutes>&<group=<UUID|STRING>>&<password=<STRING>>&[callback=<STRING>]")]
            [CommandPermissionMask((uint)Permissions.PERMISSION_NOTIFICATIONS)]
            [Description("notify")]
            NOTIFY,
            [Description("source")] SOURCE,
            [Description("effect")] EFFECT,
            [Description("id")] ID,

            [IsCommand(true)]
            [CommandInputSyntax(
                "<command=terrain>&<group=<UUID|STRING>>&<password=<STRING>>&<action=<set|get>>&action=set:<data=<STRING>>&[region=<STRING>]&[callback=<STRING>]"
            )]
            [CommandPermissionMask((uint)Permissions.PERMISSION_LAND)]
            [Description("terrain")]
            TERRAIN,
            [Description("output")] OUTPUT,
            [Description("input")] INPUT
        }

        /// <summary>
        ///     Structure containing errors returned to scripts.
        /// </summary>
        private enum ScriptError
        {
            [Description("none")] NONE = 0,
            [Description("could not join group")] COULD_NOT_JOIN_GROUP,
            [Description("could not leave group")] COULD_NOT_LEAVE_GROUP,
            [Description("agent not found")] AGENT_NOT_FOUND,
            [Description("group not found")] GROUP_NOT_FOUND,
            [Description("already in group")] ALREADY_IN_GROUP,
            [Description("not in group")] NOT_IN_GROUP,
            [Description("role not found")] ROLE_NOT_FOUND,
            [Description("command not found")] COMMAND_NOT_FOUND,
            [Description("could not eject agent")] COULD_NOT_EJECT_AGENT,

            [Description("no group power for command")]
            NO_GROUP_POWER_FOR_COMMAND,
            [Description("cannot eject owners")] CANNOT_EJECT_OWNERS,

            [Description("inventory item not found")]
            INVENTORY_ITEM_NOT_FOUND,
            [Description("invalid pay amount")] INVALID_PAY_AMOUNT,
            [Description("insufficient funds")] INSUFFICIENT_FUNDS,
            [Description("invalid pay target")] INVALID_PAY_TARGET,
            [Description("teleport failed")] TELEPORT_FAILED,
            [Description("primitive not found")] PRIMITIVE_NOT_FOUND,
            [Description("could not sit")] COULD_NOT_SIT,

            [Description("no Corrode permissions")]
            NO_CORRADE_PERMISSIONS,

            [Description("could not create group")]
            COULD_NOT_CREATE_GROUP,
            [Description("could not create role")] COULD_NOT_CREATE_ROLE,

            [Description("no role name specified")]
            NO_ROLE_NAME_SPECIFIED,

            [Description("timeout getting group roles members")]
            TIMEOUT_GETING_GROUP_ROLES_MEMBERS,

            [Description("timeout getting group roles")]
            TIMEOUT_GETTING_GROUP_ROLES,

            [Description("timeout getting role powers")]
            TIMEOUT_GETTING_ROLE_POWERS,
            [Description("could not find parcel")] COULD_NOT_FIND_PARCEL,
            [Description("unable to set home")] UNABLE_TO_SET_HOME,
            [Description("unable to go home")] UNABLE_TO_GO_HOME,

            [Description("timeout getting profile")]
            TIMEOUT_GETTING_PROFILE,
            [Description("texture not found")] TEXTURE_NOT_FOUND,

            [Description("type can only be voice or text")]
            TYPE_CAN_BE_VOICE_OR_TEXT,
            [Description("agent not in group")] AGENT_NOT_IN_GROUP,
            [Description("empty attachments")] EMPTY_ATTACHMENTS,

            [Description("could not get land users")]
            COULD_NOT_GET_LAND_USERS,
            [Description("empty pick name")] EMPTY_PICK_NAME,

            [Description("unable to join group chat")]
            UNABLE_TO_JOIN_GROUP_CHAT,
            [Description("invalid position")] INVALID_POSITION,
            [Description("could not find title")] COULD_NOT_FIND_TITLE,

            [Description("fly action can only be start or stop")]
            FLY_ACTION_START_OR_STOP,
            [Description("invalid proposal text")] INVALID_PROPOSAL_TEXT,

            [Description("invalid proposal quorum")]
            INVALID_PROPOSAL_QUORUM,

            [Description("invalid proposal majority")]
            INVALID_PROPOSAL_MAJORITY,

            [Description("invalid proposal duration")]
            INVALID_PROPOSAL_DURATION,
            [Description("invalid mute target")] INVALID_MUTE_TARGET,
            [Description("unknown action")] UNKNOWN_ACTION,

            [Description("no database file configured")]
            NO_DATABASE_FILE_CONFIGURED,

            [Description("no database key specified")]
            NO_DATABASE_KEY_SPECIFIED,

            [Description("no database value specified")]
            NO_DATABASE_VALUE_SPECIFIED,

            [Description("unknown database action")]
            UNKNOWN_DATABASE_ACTION,

            [Description("cannot remove owner role")]
            CANNOT_REMOVE_OWNER_ROLE,

            [Description("cannot remove user from owner role")]
            CANNOT_REMOVE_USER_FROM_OWNER_ROLE,
            [Description("timeout getting picks")] TIMEOUT_GETTING_PICKS,

            [Description("maximum number of roles exceeded")]
            MAXIMUM_NUMBER_OF_ROLES_EXCEEDED,

            [Description("cannot delete a group member from the everyone role")]
            CANNOT_DELETE_A_GROUP_MEMBER_FROM_THE_EVERYONE_ROLE,

            [Description("group members are by default in the everyone role")]
            GROUP_MEMBERS_ARE_BY_DEFAULT_IN_THE_EVERYONE_ROLE,

            [Description("cannot delete the everyone role")]
            CANNOT_DELETE_THE_EVERYONE_ROLE,
            [Description("invalid url provided")] INVALID_URL_PROVIDED,

            [Description("invalid notification types")]
            INVALID_NOTIFICATION_TYPES,

            [Description("notification not allowed")]
            NOTIFICATION_NOT_ALLOWED,

            [Description("unknown directory search type")]
            UNKNOWN_DIRECTORY_SEARCH_TYPE,

            [Description("no search text provided")]
            NO_SEARCH_TEXT_PROVIDED,

            [Description("unknown restart action")]
            UNKNOWN_RESTART_ACTION,
            [Description("unknown move action")] UNKNOWN_MOVE_ACTION,

            [Description("timeout getting top scripts")]
            TIMEOUT_GETTING_TOP_SCRIPTS,

            [Description("timeout waiting for estate list")]
            TIMEOUT_WAITING_FOR_ESTATE_LIST,
            [Description("unknown top type")] UNKNOWN_TOP_TYPE,

            [Description("unknown estate list action")]
            UNKNOWN_ESTATE_LIST_ACTION,
            [Description("unknown estate list")] UNKNOWN_ESTATE_LIST,
            [Description("no item specified")] NO_ITEM_SPECIFIED,

            [Description("unknown animation action")]
            UNKNOWN_ANIMATION_ACTION,
            [Description("no channel specified")] NO_CHANNEL_SPECIFIED,

            [Description("no button index specified")]
            NO_BUTTON_INDEX_SPECIFIED,
            [Description("no button specified")] NO_BUTTON_SPECIFIED,
            [Description("no land rights")] NO_LAND_RIGHTS,
            [Description("unknown entity")] UNKNOWN_ENTITY,
            [Description("invalid rotation")] INVALID_ROTATION,

            [Description("could not set script state")]
            COULD_NOT_SET_SCRIPT_STATE,
            [Description("item is not a script")] ITEM_IS_NOT_A_SCRIPT,

            [Description("failed to get display name")]
            FAILED_TO_GET_DISPLAY_NAME,
            [Description("no name provided")] NO_NAME_PROVIDED,

            [Description("could not set display name")]
            COULD_NOT_SET_DISPLAY_NAME,
            [Description("timeout joining group")] TIMEOUT_JOINING_GROUP,

            [Description("timeout creating group")]
            TIMEOUT_CREATING_GROUP,

            [Description("timeout ejecting agent")]
            TIMEOUT_EJECTING_AGENT,

            [Description("timeout getting group role members")]
            TIMEOUT_GETTING_GROUP_ROLE_MEMBERS,
            [Description("timeout leaving group")] TIMEOUT_LEAVING_GROUP,

            [Description("timeout joining group chat")]
            TIMEOUT_JOINING_GROUP_CHAT,

            [Description("timeout during teleport")]
            TIMEOUT_DURING_TELEPORT,

            [Description("timeout requesting sit")]
            TIMEOUT_REQUESTING_SIT,

            [Description("timeout getting land users")]
            TIMEOUT_GETTING_LAND_USERS,

            [Description("timeout getting script state")]
            TIMEOUT_GETTING_SCRIPT_STATE,

            [Description("timeout updating mute list")]
            TIMEOUT_UPDATING_MUTE_LIST,

            [Description("timeout getting parcels")]
            TIMEOUT_GETTING_PARCELS,
            [Description("empty classified name")] EMPTY_CLASSIFIED_NAME,
            [Description("invalid price")] INVALID_PRICE,

            [Description("timeout getting classifieds")]
            TIMEOUT_GETTING_CLASSIFIEDS,

            [Description("could not find classified")]
            COULD_NOT_FIND_CLASSIFIED,
            [Description("invalid days")] INVALID_DAYS,
            [Description("invalid interval")] INVALID_INTERVAL,

            [Description("timeout getting group account summary")]
            TIMEOUT_GETTING_GROUP_ACCOUNT_SUMMARY,
            [Description("friend not found")] FRIEND_NOT_FOUND,

            [Description("the agent already is a friend")]
            AGENT_ALREADY_FRIEND,

            [Description("no friendship offer found")]
            NO_FRIENDSHIP_OFFER_FOUND,

            [Description("friend does not allow mapping")]
            FRIEND_DOES_NOT_ALLOW_MAPPING,

            [Description("timeout mapping friend")]
            TIMEOUT_MAPPING_FRIEND,
            [Description("friend offline")] FRIEND_OFFLINE,

            [Description("timeout getting region")]
            TIMEOUT_GETTING_REGION,
            [Description("region not found")] REGION_NOT_FOUND,
            [Description("no map items found")] NO_MAP_ITEMS_FOUND,

            [Description("no description provided")]
            NO_DESCRIPTION_PROVIDED,
            [Description("no folder specified")] NO_FOLDER_SPECIFIED,
            [Description("empty wearables")] EMPTY_WEARABLES,
            [Description("parcel not for sale")] PARCEL_NOT_FOR_SALE,

            [Description("unknown access list type")]
            UNKNOWN_ACCESS_LIST_TYPE,
            [Description("no task specified")] NO_TASK_SPECIFIED,

            [Description("timeout getting group members")]
            TIMEOUT_GETTING_GROUP_MEMBERS,
            [Description("group not open")] GROUP_NOT_OPEN,

            [Description("timeout downloading terrain")]
            TIMEOUT_DOWNLOADING_ASSET,

            [Description("timeout uploading terrain")]
            TIMEOUT_UPLOADING_ASSET,
            [Description("empty terrain data")] EMPTY_ASSET_DATA,

            [Description("the specified folder contains no equipable items")]
            NO_EQUIPABLE_ITEMS,

            [Description("inventory offer not found")]
            INVENTORY_OFFER_NOT_FOUND,
            [Description("no session specified")] NO_SESSION_SPECIFIED,
            [Description("folder not found")] FOLDER_NOT_FOUND,
            [Description("timeout creating item")] TIMEOUT_CREATING_ITEM,

            [Description("timeout uploading item")]
            TIMEOUT_UPLOADING_ITEM,
            [Description("unable to upload item")] UNABLE_TO_UPLOAD_ITEM,
            [Description("unable to create item")] UNABLE_TO_CREATE_ITEM,

            [Description("timeout uploading item data")]
            TIMEOUT_UPLOADING_ITEM_DATA,

            [Description("unable to upload item data")]
            UNABLE_TO_UPLOAD_ITEM_DATA,
            [Description("unknown direction")] UNKNOWN_DIRECTION,

            [Description("timeout requesting to set home")]
            TIMEOUT_REQUESTING_TO_SET_HOME,

            [Description("timeout traferring asset")]
            TIMEOUT_TRANSFERRING_ASSET,
            [Description("asset upload failed")] ASSET_UPLOAD_FAILED,

            [Description("failed to download asset")]
            FAILED_TO_DOWNLOAD_ASSET,
            [Description("unknown asset type")] UNKNOWN_ASSET_TYPE,
            [Description("invalid asset data")] INVALID_ASSET_DATA,
            [Description("unknown wearable type")] UNKNOWN_WEARABLE_TYPE,

            [Description("unknown inventory type")]
            UNKNOWN_INVENTORY_TYPE,

            [Description("could not compile regular expression")]
            COULD_NOT_COMPILE_REGULAR_EXPRESSION,
            [Description("no pattern provided")] NO_PATTERN_PROVIDED,

            [Description("no executable file provided")]
            NO_EXECUTABLE_FILE_PROVIDED,

            [Description("timeout waiting for execution")]
            TIMEOUT_WAITING_FOR_EXECUTION,

            [Description("unknown group invite session")]
            UNKNOWN_GROUP_INVITE_SESSION,

            [Description("unable to obtain money balance")]
            UNABLE_TO_OBTAIN_MONEY_BALANCE,

            [Description("timeout getting avatar data")]
            TIMEOUT_GETTING_AVATAR_DATA,

            [Description("timeout retrieving estate list")]
            TIMEOUT_RETRIEVING_ESTATE_LIST,
            [Description("destination too close")] DESTINATION_TOO_CLOSE,

            [Description("timeout getting group titles")]
            TIMEOUT_GETTING_GROUP_TITLES,
            [Description("no message provided")] NO_MESSAGE_PROVIDED,

            [Description("could not remove brain file")]
            COULD_NOT_REMOVE_BRAIN_FILE,
            [Description("unknown effect")] UNKNOWN_EFFECT,

            [Description("no effect UUID provided")]
            NO_EFFECT_UUID_PROVIDED,
            [Description("effect not found")] EFFECT_NOT_FOUND,
            [Description("invalid viewer effect")] INVALID_VIEWER_EFFECT,
            [Description("ambiguous path")] AMBIGUOUS_PATH,
            [Description("path not found")] PATH_NOT_FOUND,

            [Description("unexpected item in path")]
            UNEXPECTED_ITEM_IN_PATH,
            [Description("no path provided")] NO_PATH_PROVIDED,

            [Description("unable to create folder")]
            UNABLE_TO_CREATE_FOLDER,

            [Description("no permissions provided")]
            NO_PERMISSIONS_PROVIDED,

            [Description("setting permissions failed")]
            SETTING_PERMISSIONS_FAILED,

            [Description("timeout retrieving item")]
            TIMEOUT_RETRIEVING_ITEM,

            [Description("expected item as source")]
            EXPECTED_ITEM_AS_SOURCE,

            [Description("expected folder as target")]
            EXPECTED_FOLDER_AS_TARGET,

            [Description("unable to load configuration")]
            UNABLE_TO_LOAD_CONFIGURATION,

            [Description("unable to save configuration")]
            UNABLE_TO_SAVE_CONFIGURATION,
            [Description("invalid xml path")] INVALID_XML_PATH,
            [Description("no data provided")] NO_DATA_PROVIDED,

            [Description("unknown image format requested")]
            UNKNOWN_IMAGE_FORMAT_REQUESTED,

            [Description("unknown image format provided")]
            UNKNOWN_IMAGE_FORMAT_PROVIDED,

            [Description("unable to decode asset data")]
            UNABLE_TO_DECODE_ASSET_DATA,

            [Description("unable to convert to requested format")]
            UNABLE_TO_CONVERT_TO_REQUESTED_FORMAT,

            [Description("could not start process")]
            COULD_NOT_START_PROCESS,

            [Description("timeout getting primitive data")]
            TIMEOUT_GETTING_PRIMITIVE_DATA,
            [Description("item is not an object")] ITEM_IS_NOT_AN_OBJECT,

            [Description("timeout meshmerizing object")]
            COULD_NOT_MESHMERIZE_OBJECT,

            [Description("could not get primitive properties")]
            COULD_NOT_GET_PRIMITIVE_PROPERTIES,
            [Description("avatar not in range")] AVATAR_NOT_IN_RANGE,
            [Description("invalid scale")] INVALID_SCALE,

            [Description("could not get current groups")]
            COULD_NOT_GET_CURRENT_GROUPS,

            [Description("maximum number of groups reached")]
            MAXIMUM_NUMBER_OF_GROUPS_REACHED,
            [Description("unknown syntax type")] UNKNOWN_SYNTAX_TYPE
        }

        /// <summary>
        ///     Keys returned by Corrode.
        /// </summary>
        private enum ResultKeys : uint
        {
            [Description("none")] NONE = 0,
            [Description("data")] DATA,
            [Description("success")] SUCCESS,
            [Description("error")] ERROR
        }

        /// <summary>
        ///     Corrode permissions.
        /// </summary>
        [Flags]
        private enum Permissions : uint
        {
            [Description("none")] PERMISSION_NONE = 0,
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
            [Description("directory")] PERMISSION_DIRECTORY = 1024,
            [Description("system")] PERMISSION_SYSTEM = 2048,
            [Description("friendship")] PERMISSION_FRIENDSHIP = 4096,
            [Description("execute")] PERMISSION_EXECUTE = 8192,
            [Description("group")] PERMISSION_GROUP = 16384,
            [Description("filter")] PERMISSION_FILTER = 32768
        }

        /// <summary>
        ///     An enumeration of various compression methods
        ///     supproted by Corrode's internal HTTP server.
        /// </summary>
        private enum HTTPCompressionMethod : uint
        {
            [Description("none")] NONE,
            [Description("deflate")] DEFLATE,
            [Description("gzip")] GZIP
        }

        /// <summary>
        ///     Possible input and output filters.
        /// </summary>
        private enum Filter : uint
        {
            [Description("none")] NONE = 0,
            [Description("rfc1738")] RFC1738,
            [Description("rfc3986")] RFC3986,
            [Description("enigma")] ENIGMA,
            [Description("vigenere")] VIGENERE,
            [Description("atbash")] ATBASH,
            [Description("base64")] BASE64
        }

        /// <summary>
        ///     Possible entities.
        /// </summary>
        private enum Entity : uint
        {
            [Description("none")] NONE = 0,
            [Description("avatar")] AVATAR,
            [Description("local")] LOCAL,
            [Description("group")] GROUP,
            [Description("estate")] ESTATE,
            [Description("region")] REGION,
            [Description("object")] OBJECT,
            [Description("parcel")] PARCEL,
            [Description("range")] RANGE,
            [Description("syntax")] SYNTAX,
            [Description("permission")] PERMISSION
        }

        /// <summary>
        ///     Directions in 3D cartesian.
        /// </summary>
        private enum Direction : uint
        {
            [Description("none")] NONE = 0,
            [Description("back")] BACK,
            [Description("forward")] FORWARD,
            [Description("left")] LEFT,
            [Description("right")] RIGHT,
            [Description("up")] UP,
            [Description("down")] DOWN
        }

        /// <summary>
        ///     Holds item types with the wearable inventory item type expanded to wearable types.
        /// </summary>
        private enum DirItemType : uint
        {
            [Description("none")] NONE = 0,
            [Description("texture")] TEXTURE,
            [Description("sound")] SOUND,
            [Description("callingcard")] CALLINGCARD,
            [Description("landmark")] LANDMARK,
            [Description("object")] OBJECT,
            [Description("notecard")] NOTECARD,
            [Description("category")] CATEGORY,
            [Description("LSL")] LSL,
            [Description("snapshot")] SNAPSHOT,
            [Description("attachment")] ATTACHMENT,
            [Description("animation")] ANIMATION,
            [Description("gesture")] GESTURE,
            [Description("folder")] FOLDER,
            [Description("shape")] SHAPE,
            [Description("skin")] SKIN,
            [Description("hair")] HAIR,
            [Description("eyes")] EYES,
            [Description("shirt")] SHIRT,
            [Description("pants")] PANTS,
            [Description("shoes")] SHOES,
            [Description("socks")] SOCKS,
            [Description("jacket")] JACKET,
            [Description("gloves")] GLOVES,
            [Description("undershirt")] UNDERSHIRT,
            [Description("underpants")] UNDERPANTS,
            [Description("skirt")] SKIRT,
            [Description("tattoo")] TATTOO,
            [Description("alpha")] ALPHA,
            [Description("physics")] PHYSICS
        }

        /// <summary>
        ///     The type of threads managed by Corrode.
        /// </summary>
        private enum CorrodeThreadType
        {
            COMMAND = 1,
            RLV = 2,
            NOTIFICATION = 3,
            INSTANT_MESSAGE = 4
        };

        /// <summary>
        ///     Structure containing error messages printed on console for the owner.
        /// </summary>
        private enum ConsoleError
        {
            [Description("none")] NONE = 0,
            [Description("access denied")] ACCESS_DENIED,

            [Description("invalid configuration file")]
            INVALID_CONFIGURATION_FILE,

            [Description(
                "the Terms of Service (TOS) for the grid you are connecting to have not been accepted, please check your configuration file"
            )]
            TOS_NOT_ACCEPTED,
            [Description("teleport failed")] TELEPORT_FAILED,
            [Description("teleport succeeded")] TELEPORT_SUCCEEDED,
            [Description("accepted friendship")] ACCEPTED_FRIENDSHIP,
            [Description("login failed")] LOGIN_FAILED,
            [Description("login succeeded")] LOGIN_SUCCEEDED,

            [Description("failed to set appearance")]
            APPEARANCE_SET_FAILED,
            [Description("appearance set")] APPEARANCE_SET_SUCCEEDED,

            [Description("all simulators disconnected")]
            ALL_SIMULATORS_DISCONNECTED,
            [Description("simulator connected")] SIMULATOR_CONNECTED,
            [Description("event queue started")] EVENT_QUEUE_STARTED,
            [Description("disconnected")] DISCONNECTED,
            [Description("logging out")] LOGGING_OUT,
            [Description("logging in")] LOGGING_IN,
            [Description("agent not found")] AGENT_NOT_FOUND,

            [Description("reading Corrode configuration")]
            READING_CORRADE_CONFIGURATION,

            [Description("read Corrode configuration")]
            READ_CORRADE_CONFIGURATION,

            [Description("configuration file modified")]
            CONFIGURATION_FILE_MODIFIED,
            [Description("HTTP server error")] HTTP_SERVER_ERROR,

            [Description("HTTP server not supported")]
            HTTP_SERVER_NOT_SUPPORTED,
            [Description("starting HTTP server")] STARTING_HTTP_SERVER,
            [Description("stopping HTTP server")] STOPPING_HTTP_SERVER,

            [Description("HTTP server processing aborted")]
            HTTP_SERVER_PROCESSING_ABORTED,
            [Description("timeout logging out")] TIMEOUT_LOGGING_OUT,
            [Description("callback error")] CALLBACK_ERROR,
            [Description("notification error")] NOTIFICATION_ERROR,

            [Description("inventory cache items loaded")]
            INVENTORY_CACHE_ITEMS_LOADED,

            [Description("inventory cache items saved")]
            INVENTORY_CACHE_ITEMS_SAVED,

            [Description("unable to load Corrode cache")]
            UNABLE_TO_LOAD_CORRADE_CACHE,

            [Description("unable to save Corrode cache")]
            UNABLE_TO_SAVE_CORRADE_CACHE,

            [Description("failed to manifest RLV behaviour")]
            FAILED_TO_MANIFEST_RLV_BEHAVIOUR,

            [Description("behaviour not implemented")]
            BEHAVIOUR_NOT_IMPLEMENTED,
            [Description("workers exceeded")] WORKERS_EXCEEDED,

            [Description("AIML bot configuration modified")]
            AIML_CONFIGURATION_MODIFIED,

            [Description("read AIML bot configuration")]
            READ_AIML_BOT_CONFIGURATION,

            [Description("reading AIML bot configuration")]
            READING_AIML_BOT_CONFIGURATION,

            [Description("wrote AIML bot configuration")]
            WROTE_AIML_BOT_CONFIGURATION,

            [Description("writing AIML bot configuration")]
            WRITING_AIML_BOT_CONFIGURATION,

            [Description("error loading AIML bot files")]
            ERROR_LOADING_AIML_BOT_FILES,

            [Description("error saving AIML bot files")]
            ERROR_SAVING_AIML_BOT_FILES,

            [Description("could not write to client log file")]
            COULD_NOT_WRITE_TO_CLIENT_LOG_FILE,

            [Description("could not write to group chat log file")]
            COULD_NOT_WRITE_TO_GROUP_CHAT_LOG_FILE,

            [Description("could not write to instant message log file")]
            COULD_NOT_WRITE_TO_INSTANT_MESSAGE_LOG_FILE,

            [Description("could not write to local message log file")]
            COULD_NOT_WRITE_TO_LOCAL_MESSAGE_LOG_FILE,

            [Description("could not write to region message log file")]
            COULD_NOT_WRITE_TO_REGION_MESSAGE_LOG_FILE,
            [Description("unknown IP address")] UNKNOWN_IP_ADDRESS,

            [Description("unable to save Corrode notifications state")]
            UNABLE_TO_SAVE_CORRADE_NOTIFICATIONS_STATE,

            [Description("unable to load Corrode notifications state")]
            UNABLE_TO_LOAD_CORRADE_NOTIFICATIONS_STATE,

            [Description("unable to save Corrode inventory offers state")]
            UNABLE_TO_SAVE_CORRADE_INVENTORY_OFFERS_STATE,

            [Description("unable to load Corrode inventory offers state")]
            UNABLE_TO_LOAD_CORRADE_INVENTORY_OFFERS_STATE
        }

        /// <summary>
        ///     Various types.
        /// </summary>
        private enum Type : uint
        {
            [Description("none")] NONE = 0,
            [Description("text")] TEXT,
            [Description("voice")] VOICE,
            [Description("scripts")] SCRIPTS,
            [Description("colliders")] COLLIDERS,
            [Description("ban")] BAN,
            [Description("group")] GROUP,
            [Description("user")] USER,
            [Description("manager")] MANAGER,
            [Description("classified")] CLASSIFIED,
            [Description("event")] EVENT,
            [Description("land")] LAND,
            [Description("people")] PEOPLE,
            [Description("place")] PLACE,
            [Description("input")] INPUT,
            [Description("output")] OUTPUT
        }

        /// <summary>
        ///     Possible viewer effects.
        /// </summary>
        private enum ViewerEffectType : uint
        {
            [Description("none")] NONE = 0,
            [Description("look")] LOOK,
            [Description("point")] POINT,
            [Description("sphere")] SPHERE,
            [Description("beam")] BEAM
        }

        /// <summary>
        ///     Possible actions.
        /// </summary>
        private enum Action : uint
        {
            [Description("none")] NONE = 0,
            [Description("get")] GET,
            [Description("set")] SET,
            [Description("add")] ADD,
            [Description("remove")] REMOVE,
            [Description("start")] START,
            [Description("stop")] STOP,
            [Description("mute")] MUTE,
            [Description("unmute")] UNMUTE,
            [Description("restart")] RESTART,
            [Description("cancel")] CANCEL,
            [Description("accept")] ACCEPT,
            [Description("decline")] DECLINE,
            [Description("online")] ONLINE,
            [Description("offline")] OFFLINE,
            [Description("request")] REQUEST,
            [Description("response")] RESPONSE,
            [Description("delete")] DELETE,
            [Description("take")] TAKE,
            [Description("read")] READ,
            [Description("wrtie")] WRITE,
            [Description("purge")] PURGE,
            [Description("crossed")] CROSSED,
            [Description("changed")] CHANGED,
            [Description("reply")] REPLY,
            [Description("offer")] OFFER,
            [Description("generic")] GENERIC,
            [Description("point")] POINT,
            [Description("look")] LOOK,
            [Description("update")] UPDATE,
            [Description("received")] RECEIVED,
            [Description("joined")] JOINED,
            [Description("parted")] PARTED,
            [Description("save")] SAVE,
            [Description("load")] LOAD,
            [Description("enable")] ENABLE,
            [Description("disable")] DISABLE,
            [Description("process")] PROCESS,
            [Description("rebuild")] REBUILD,
            [Description("clear")] CLEAR,
            [Description("ls")] LS,
            [Description("cwd")] CWD,
            [Description("cd")] CD,
            [Description("mkdir")] MKDIR,
            [Description("chmod")] CHMOD,
            [Description("rm")] RM,
            [Description("ln")] LN,
            [Description("mv")] MV,
            [Description("cp")] CP,
            [Description("appear")] APPEAR,
            [Description("vanish")] VANISH,
            [Description("list")] LIST
        }
    }
}