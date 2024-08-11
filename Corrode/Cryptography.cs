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
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Corrode
{
    public partial class Corrode
    {
               /// <summary>
        ///     Gets an array element at a given modulo index.
        /// </summary>
        /// <typeparam name="T">the array type</typeparam>
        /// <param name="index">a positive or negative index of the element</param>
        /// <param name="data">the array</param>
        /// <return>an array element</return>
        public static T wasGetElementAt<T>(T[] data, int index)
        {
            switch (index < 0)
            {
                case true:
                    return data[((index%data.Length) + data.Length)%data.Length];
                default:
                    return data[index%data.Length];
            }
        }

        /// <summary>
        ///     Gets a sub-array from an array.
        /// </summary>
        /// <typeparam name="T">the array type</typeparam>
        /// <param name="data">the array</param>
        /// <param name="start">the start index</param>
        /// <param name="stop">the stop index (-1 denotes the end)</param>
        /// <returns>the array slice between start and stop</returns>
        public static T[] wasGetSubArray<T>(T[] data, int start, int stop)
        {
            if (stop.Equals(-1))
                stop = data.Length - 1;
            T[] result = new T[stop - start + 1];
            Array.Copy(data, start, result, 0, stop - start + 1);
            return result;
        }

        /// <summary>
        ///     Delete a sub-array and return the result.
        /// </summary>
        /// <typeparam name="T">the array type</typeparam>
        /// <param name="data">the array</param>
        /// <param name="start">the start index</param>
        /// <param name="stop">the stop index (-1 denotes the end)</param>
        /// <returns>the array without elements between start and stop</returns>
        public static T[] wasDeleteSubArray<T>(T[] data, int start, int stop)
        {
            if (stop.Equals(-1))
                stop = data.Length - 1;
            T[] result = new T[data.Length - (stop - start) - 1];
            Array.Copy(data, 0, result, 0, start);
            Array.Copy(data, stop + 1, result, start, data.Length - stop - 1);
            return result;
        }

        /// <summary>
        ///     Concatenate multiple arrays.
        /// </summary>
        /// <typeparam name="T">the array type</typeparam>
        /// <param name="arrays">multiple arrays</param>
        /// <returns>a flat array with all arrays concatenated</returns>
        public static T[] wasConcatenateArrays<T>(params T[][] arrays)
        {
            int resultLength = 0;
            foreach (T[] o in arrays)
            {
                resultLength += o.Length;
            }
            T[] result = new T[resultLength];
            int offset = 0;
            for (int x = 0; x < arrays.Length; x++)
            {
                arrays[x].CopyTo(result, offset);
                offset += arrays[x].Length;
            }
            return result;
        }

        /// <summary>
        ///     Permutes an array in reverse a given number of times.
        /// </summary>
        /// <typeparam name="T">the array type</typeparam>
        /// <param name="input">the array</param>
        /// <param name="times">the number of times to permute</param>
        /// <returns>the array with the elements permuted</returns>
        private static T[] wasReversePermuteArrayElements<T>(T[] input, int times)
        {
            if (times.Equals(0)) return input;
            T[] slice = new T[input.Length];
            Array.Copy(input, 1, slice, 0, input.Length - 1);
            Array.Copy(input, 0, slice, input.Length - 1, 1);
            return wasReversePermuteArrayElements(slice, --times);
        }

        /// <summary>
        ///     Permutes an array forward a given number of times.
        /// </summary>
        /// <typeparam name="T">the array type</typeparam>
        /// <param name="input">the array</param>
        /// <param name="times">the number of times to permute</param>
        /// <returns>the array with the elements permuted</returns>
        private static T[] wasForwardPermuteArrayElements<T>(T[] input, int times)
        {
            if (times.Equals(0)) return input;
            T[] slice = new T[input.Length];
            Array.Copy(input, input.Length - 1, slice, 0, 1);
            Array.Copy(input, 0, slice, 1, input.Length - 1);
            return wasForwardPermuteArrayElements(slice, --times);
        }

        /// <summary>
        ///     Encrypt or decrypt a message given a set of rotors, plugs and a reflector.
        /// </summary>
        /// <param name="message">the message to encyrpt or decrypt</param>
        /// <param name="rotors">any combination of: 1, 2, 3, 4, 5, 6, 7, 8, b, g</param>
        /// <param name="plugs">the letter representing the start character for the rotor</param>
        /// <param name="reflector">any one of: B, b, C, c</param>
        /// <returns>either a decrypted or encrypted string</returns>
        private static string wasEnigma(string message, char[] rotors, char[] plugs, char reflector)
        {
            Dictionary<char, char[]> def_rotors = new Dictionary<char, char[]>
            {
                {
                    '1', new[]
                    {
                        'e', 'k', 'm', 'f', 'l',
                        'g', 'd', 'q', 'v', 'z',
                        'n', 't', 'o', 'w', 'y',
                        'h', 'x', 'u', 's', 'p',
                        'a', 'i', 'b', 'r', 'c',
                        'j'
                    }
                },
                {
                    '2', new[]
                    {
                        'a', 'j', 'd', 'k', 's',
                        'i', 'r', 'u', 'x', 'b',
                        'l', 'h', 'w', 't', 'm',
                        'c', 'q', 'g', 'z', 'n',
                        'p', 'y', 'f', 'v', 'o',
                        'e'
                    }
                },
                {
                    '3', new[]
                    {
                        'b', 'd', 'f', 'h', 'j',
                        'l', 'c', 'p', 'r', 't',
                        'x', 'v', 'z', 'n', 'y',
                        'e', 'i', 'w', 'g', 'a',
                        'k', 'm', 'u', 's', 'q',
                        'o'
                    }
                },
                {
                    '4', new[]
                    {
                        'e', 's', 'o', 'v', 'p',
                        'z', 'j', 'a', 'y', 'q',
                        'u', 'i', 'r', 'h', 'x',
                        'l', 'n', 'f', 't', 'g',
                        'k', 'd', 'c', 'm', 'w',
                        'b'
                    }
                },
                {
                    '5', new[]
                    {
                        'v', 'z', 'b', 'r', 'g',
                        'i', 't', 'y', 'u', 'p',
                        's', 'd', 'n', 'h', 'l',
                        'x', 'a', 'w', 'm', 'j',
                        'q', 'o', 'f', 'e', 'c',
                        'k'
                    }
                },
                {
                    '6', new[]
                    {
                        'j', 'p', 'g', 'v', 'o',
                        'u', 'm', 'f', 'y', 'q',
                        'b', 'e', 'n', 'h', 'z',
                        'r', 'd', 'k', 'a', 's',
                        'x', 'l', 'i', 'c', 't',
                        'w'
                    }
                },
                {
                    '7', new[]
                    {
                        'n', 'z', 'j', 'h', 'g',
                        'r', 'c', 'x', 'm', 'y',
                        's', 'w', 'b', 'o', 'u',
                        'f', 'a', 'i', 'v', 'l',
                        'p', 'e', 'k', 'q', 'd',
                        't'
                    }
                },
                {
                    '8', new[]
                    {
                        'f', 'k', 'q', 'h', 't',
                        'l', 'x', 'o', 'c', 'b',
                        'j', 's', 'p', 'd', 'z',
                        'r', 'a', 'm', 'e', 'w',
                        'n', 'i', 'u', 'y', 'g',
                        'v'
                    }
                },
                {
                    'b', new[]
                    {
                        'l', 'e', 'y', 'j', 'v',
                        'c', 'n', 'i', 'x', 'w',
                        'p', 'b', 'q', 'm', 'd',
                        'r', 't', 'a', 'k', 'z',
                        'g', 'f', 'u', 'h', 'o',
                        's'
                    }
                },
                {
                    'g', new[]
                    {
                        'f', 's', 'o', 'k', 'a',
                        'n', 'u', 'e', 'r', 'h',
                        'm', 'b', 't', 'i', 'y',
                        'c', 'w', 'l', 'q', 'p',
                        'z', 'x', 'v', 'g', 'j',
                        'd'
                    }
                }
            };

            Dictionary<char, char[]> def_reflectors = new Dictionary<char, char[]>
            {
                {
                    'B', new[]
                    {
                        'a', 'y', 'b', 'r', 'c', 'u', 'd', 'h',
                        'e', 'q', 'f', 's', 'g', 'l', 'i', 'p',
                        'j', 'x', 'k', 'n', 'm', 'o', 't', 'z',
                        'v', 'w'
                    }
                },
                {
                    'b', new[]
                    {
                        'a', 'e', 'b', 'n', 'c', 'k', 'd', 'q',
                        'f', 'u', 'g', 'y', 'h', 'w', 'i', 'j',
                        'l', 'o', 'm', 'p', 'r', 'x', 's', 'z',
                        't', 'v'
                    }
                },
                {
                    'C', new[]
                    {
                        'a', 'f', 'b', 'v', 'c', 'p', 'd', 'j',
                        'e', 'i', 'g', 'o', 'h', 'y', 'k', 'r',
                        'l', 'z', 'm', 'x', 'n', 'w', 't', 'q',
                        's', 'u'
                    }
                },
                {
                    'c', new[]
                    {
                        'a', 'r', 'b', 'd', 'c', 'o', 'e', 'j',
                        'f', 'n', 'g', 't', 'h', 'k', 'i', 'v',
                        'l', 'm', 'p', 'w', 'q', 'z', 's', 'x',
                        'u', 'y'
                    }
                }
            };

            // Setup rotors from plugs.
            foreach (char rotor in rotors)
            {
                char plug = plugs[Array.IndexOf(rotors, rotor)];
                int i = Array.IndexOf(def_rotors[rotor], plug);
                if (i.Equals(0)) continue;
                def_rotors[rotor] = wasConcatenateArrays(new[] {plug},
                    wasGetSubArray(wasDeleteSubArray(def_rotors[rotor], i, i), i, -1),
                    wasGetSubArray(wasDeleteSubArray(def_rotors[rotor], i + 1, -1), 0, i - 1));
            }

            StringBuilder result = new StringBuilder();
            foreach (char c in message)
            {
                if (!char.IsLetter(c))
                {
                    result.Append(c);
                    continue;
                }

                // Normalize to lower.
                char l = char.ToLower(c);

                Action<char[]> rotate = o =>
                {
                    int i = o.Length - 1;
                    do
                    {
                        def_rotors[o[0]] = wasForwardPermuteArrayElements(def_rotors[o[0]], 1);
                        if (i.Equals(0))
                        {
                            rotors = wasReversePermuteArrayElements(o, 1);
                            continue;
                        }
                        l = wasGetElementAt(def_rotors[o[1]], Array.IndexOf(def_rotors[o[0]], l) - 1);
                        o = wasReversePermuteArrayElements(o, 1);
                    } while (--i > -1);
                };

                // Forward pass through the Enigma's rotors.
                rotate.Invoke(rotors);

                // Reflect
                int x = Array.IndexOf(def_reflectors[reflector], l);
                l = (x + 1)%2 == 0 ? def_reflectors[reflector][x - 1] : def_reflectors[reflector][x + 1];

                // Reverse the order of the rotors.
                Array.Reverse(rotors);

                // Reverse pass through the Enigma's rotors.
                rotate.Invoke(rotors);

                if (char.IsUpper(c))
                {
                    l = char.ToUpper(l);
                }
                result.Append(l);
            }

            return result.ToString();
        }

        /// <summary>
        ///     Expand the VIGENRE key to the length of the input.
        /// </summary>
        /// <param name="input">the input to expand to</param>
        /// <param name="enc_key">the key to expand</param>
        /// <returns>the expanded key</returns>
        private static string wasVigenereExpandKey(string input, string enc_key)
        {
            string exp_key = "";
            int i = 0, j = 0;
            do
            {
                char p = input[i];
                if (!char.IsLetter(p))
                {
                    exp_key += p;
                    ++i;
                    continue;
                }
                int m = j%enc_key.Length;
                exp_key += enc_key[m];
                ++j;
                ++i;
            } while (i < input.Length);
            return exp_key;
        }

        /// <summary>
        ///     Encrypt using VIGENERE.
        /// </summary>
        /// <param name="input">the input to encrypt</param>
        /// <param name="enc_key">the key to encrypt with</param>
        /// <returns>the encrypted input</returns>
        private static string wasEncryptVIGENERE(string input, string enc_key)
        {
            char[] a =
            {
                'a', 'b', 'c', 'd', 'e', 'f', 'g', 'h', 'i', 'j', 'k', 'l', 'm',
                'n', 'o', 'p', 'q', 'r', 's', 't', 'u', 'v', 'w', 'x', 'y', 'z'
            };

            enc_key = wasVigenereExpandKey(input, enc_key);
            string result = "";
            int i = 0;
            do
            {
                char p = input[i];
                if (!char.IsLetter(p))
                {
                    result += p;
                    ++i;
                    continue;
                }
                char q =
                    wasReversePermuteArrayElements(a, Array.IndexOf(a, enc_key[i]))[
                        Array.IndexOf(a, char.ToLowerInvariant(p))];
                if (char.IsUpper(p))
                {
                    q = char.ToUpperInvariant(q);
                }
                result += q;
                ++i;
            } while (i < input.Length);
            return result;
        }

        /// <summary>
        ///     Decrypt using VIGENERE.
        /// </summary>
        /// <param name="input">the input to decrypt</param>
        /// <param name="enc_key">the key to decrypt with</param>
        /// <returns>the decrypted input</returns>
        private static string wasDecryptVIGENERE(string input, string enc_key)
        {
            char[] a =
            {
                'a', 'b', 'c', 'd', 'e', 'f', 'g', 'h', 'i', 'j', 'k', 'l', 'm',
                'n', 'o', 'p', 'q', 'r', 's', 't', 'u', 'v', 'w', 'x', 'y', 'z'
            };

            enc_key = wasVigenereExpandKey(input, enc_key);
            string result = "";
            int i = 0;
            do
            {
                char p = input[i];
                if (!char.IsLetter(p))
                {
                    result += p;
                    ++i;
                    continue;
                }
                char q =
                    a[
                        Array.IndexOf(wasReversePermuteArrayElements(a, Array.IndexOf(a, enc_key[i])),
                            char.ToLowerInvariant(p))];
                if (char.IsUpper(p))
                {
                    q = char.ToUpperInvariant(q);
                }
                result += q;
                ++i;
            } while (i < input.Length);
            return result;
        }

        /// <summary>
        ///     An implementation of the ATBASH cypher for latin alphabets.
        /// </summary>
        /// <param name="data">the data to encrypt or decrypt</param>
        /// <returns>the encrypted or decrypted data</returns>
        private static string wasATBASH(string data)
        {
            char[] a =
            {
                'a', 'b', 'c', 'd', 'e', 'f', 'g', 'h', 'i', 'j', 'k', 'l', 'm',
                'n', 'o', 'p', 'q', 'r', 's', 't', 'u', 'v', 'w', 'x', 'y', 'z'
            };

            char[] input = data.ToArray();

            Parallel.ForEach(Enumerable.Range(0, data.Length), i =>
            {
                char e = input[i];
                if (!char.IsLetter(e)) return;
                int x = 25 - Array.BinarySearch(a, char.ToLowerInvariant(e));
                if (!char.IsUpper(e))
                {
                    input[i] = a[x];
                    return;
                }
                input[i] = char.ToUpperInvariant(a[x]);
            });

            return new string(input);
        }
    }
}