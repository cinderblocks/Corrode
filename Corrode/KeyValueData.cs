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
using System.Linq;

namespace Corrode
{
    public partial class Corrode
    {
                /// <summary>
        ///     Returns the value of a key from a key-value data string.
        /// </summary>
        /// <param name="key">the key of the value</param>
        /// <param name="data">the key-value data segment</param>
        /// <returns>true if the key was found in data</returns>
        private static string wasKeyValueGet(string key, string data)
        {
            return data.Split('&')
                .AsParallel()
                .Select(o => o.Split('=').ToList())
                .Where(o => o.Count.Equals(2))
                .Select(o => new
                {
                    k = o.First(),
                    v = o.Last()
                })
                .Where(o => o.k.Equals(key))
                .Select(o => o.v)
                .FirstOrDefault();
        }

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
            HashSet<string> output = new HashSet<string>(data.Split('&')
                .AsParallel()
                .Select(o => o.Split('=').ToList())
                .Where(o => o.Count.Equals(2))
                .Select(o => new
                {
                    k = o.First(),
                    v = !o.First().Equals(key) ? o.Last() : value
                }).Select(o => string.Join("=", o.k, o.v)));
            string append = string.Join("=", key, value);
            if (!output.Contains(append))
            {
                output.Add(append);
            }
            return string.Join("&", output.ToArray());
        }

        /// <summary>
        ///     Deletes a key-value pair from a string referenced by a key.
        /// </summary>
        /// <param name="key">the key to search for</param>
        /// <param name="data">the key-value data segment</param>
        /// <returns>a key-value pair string</returns>
        private static string wasKeyValueDelete(string key, string data)
        {
            return string.Join("&", data.Split('&')
                .AsParallel()
                .Select(o => o.Split('=').ToList())
                .Where(o => o.Count.Equals(2))
                .Select(o => new
                {
                    k = o.First(),
                    v = o.Last()
                })
                .Where(o => !o.k.Equals(key))
                .Select(o => string.Join("=", o.k, o.v))
                .ToArray());
        }

        /// <summary>
        ///     Decodes key-value pair data to a dictionary.
        /// </summary>
        /// <param name="data">the key-value pair data</param>
        /// <returns>a dictionary containing the keys and values</returns>
        private static Dictionary<string, string> wasKeyValueDecode(string data)
        {
            return data.Split('&')
                .AsParallel()
                .Select(o => o.Split('=').ToList())
                .Where(o => o.Count.Equals(2))
                .Select(o => new
                {
                    k = o.First(),
                    v = o.Last()
                })
                .GroupBy(o => o.k)
                .ToDictionary(o => o.Key, p => p.First().v);
        }

        /// <summary>
        ///     Serialises a dictionary to key-value data.
        /// </summary>
        /// <param name="data">a dictionary</param>
        /// <returns>a key-value data encoded string</returns>
        private static string wasKeyValueEncode(Dictionary<string, string> data)
        {
            return string.Join("&", data.AsParallel().Select(o => string.Join("=", o.Key, o.Value)));
        }

        /// <summary>Escapes a dictionary's keys and values for sending as POST data.</summary>
        /// <param name="data">A dictionary containing keys and values to be escaped</param>
        private static Dictionary<string, string> wasKeyValueEscape(Dictionary<string, string> data)
        {
            return data.AsParallel().ToDictionary(o => wasOutput(o.Key), p => wasOutput(p.Value));
        }
    }
}