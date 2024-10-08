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

using OpenMetaverse;

namespace Corrode;

/// <summary>
///     Material information for Collada DAE Export.
/// </summary>
/// <remarks>This class is taken from the Radegast Viewer with changes by Wizardry and Steamworks.</remarks>
public class MaterialInfo
{
    public Color4 Color;
    public string Name;
    public UUID TextureID;

    public bool Matches(Primitive.TextureEntryFace TextureEntry)
    {
        return TextureID.Equals(TextureEntry.TextureID) && Color.Equals(TextureEntry.RGBA);
    }
}