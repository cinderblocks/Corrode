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
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using OpenMetaverse;

namespace Corrode
{
    public partial class Corrode
    {
                private struct Configuration
        {
            private static string _firstName;
            private static string _lastName;
            private static string _password;
            private static string _loginURL;
            private static string _instantMessageLogDirectory;
            private static bool _instantMessageLogEnabled;
            private static string _localMessageLogDirectory;
            private static bool _localMessageLogEnabled;
            private static string _regionMessageLogDirectory;
            private static bool _regionMessageLogEnabled;
            private static bool _enableHTTPServer;
            private static string _HTTPServerPrefix;
            private static int _HTTPServerTimeout;
            private static HTTPCompressionMethod _HTTPServerCompression;
            private static int _callbackTimeout;
            private static int _callbackThrottle;
            private static int _callbackQueueLength;
            private static int _notificationTimeout;
            private static int _notificationThrottle;
            private static int _notificationQueueLength;
            private static int _connectionLimit;
            private static int _connectionIdleTime;
            private static float _range;
            private static int _maximumNotificationThreads;
            private static int _maximumCommandThreads;
            private static int _maximumRLVThreads;
            private static int _maximumInstantMessageThreads;
            private static bool _useNaggle;
            private static bool _useExpect100Continue;
            private static int _servicesTimeout;
            private static int _dataTimeout;
            private static wasAdaptiveAlarm.DECAY_TYPE _dataDecayType;
            private static int _rebakeDelay;
            private static int _membershipSweepInterval;
            private static bool _TOSAccepted;
            private static string _startLocation;
            private static string _bindIPAddress;
            private static string _networkCardMAC;
            private static string _driveIdentifierHash;
            private static string _clientLogFile;
            private static bool _clientLogEnabled;
            private static bool _autoActivateGroup;
            private static int _activateDelay;
            private static int _groupCreateFee;
            private static int _exitCodeExpected;
            private static int _exitCodeAbnormal;
            private static HashSet<Group> _groups;
            private static HashSet<Master> _masters;
            private static List<Filter> _inputFilters;
            private static List<Filter> _outputFilters;
            private static string _vigenereSecret;
            private static ENIGMA _enigma;
            private static int _logoutGrace;

            public static string FIRST_NAME
            {
                get
                {
                    lock (ClientInstanceConfigurationLock)
                    {
                        return _firstName;
                    }
                }
                private set
                {
                    lock (ClientInstanceConfigurationLock)
                    {
                        _firstName = value;
                    }
                }
            }

            public static string LAST_NAME
            {
                get
                {
                    lock (ClientInstanceConfigurationLock)
                    {
                        return _lastName;
                    }
                }
                private set
                {
                    lock (ClientInstanceConfigurationLock)
                    {
                        _lastName = value;
                    }
                }
            }

            public static string PASSWORD
            {
                get
                {
                    lock (ClientInstanceConfigurationLock)
                    {
                        return _password;
                    }
                }
                private set
                {
                    lock (ClientInstanceConfigurationLock)
                    {
                        _password = value;
                    }
                }
            }

            public static string LOGIN_URL
            {
                get
                {
                    lock (ClientInstanceConfigurationLock)
                    {
                        return _loginURL;
                    }
                }
                private set
                {
                    lock (ClientInstanceConfigurationLock)
                    {
                        _loginURL = value;
                    }
                }
            }

            public static string INSTANT_MESSAGE_LOG_DIRECTORY
            {
                get
                {
                    lock (ClientInstanceConfigurationLock)
                    {
                        return _instantMessageLogDirectory;
                    }
                }
                private set
                {
                    lock (ClientInstanceConfigurationLock)
                    {
                        _instantMessageLogDirectory = value;
                    }
                }
            }

            public static bool INSTANT_MESSAGE_LOG_ENABLED
            {
                get
                {
                    lock (ClientInstanceConfigurationLock)
                    {
                        return _instantMessageLogEnabled;
                    }
                }
                private set
                {
                    lock (ClientInstanceConfigurationLock)
                    {
                        _instantMessageLogEnabled = value;
                    }
                }
            }

            public static string LOCAL_MESSAGE_LOG_DIRECTORY
            {
                get
                {
                    lock (ClientInstanceConfigurationLock)
                    {
                        return _localMessageLogDirectory;
                    }
                }
                private set
                {
                    lock (ClientInstanceConfigurationLock)
                    {
                        _localMessageLogDirectory = value;
                    }
                }
            }

            public static bool LOCAL_MESSAGE_LOG_ENABLED
            {
                get
                {
                    lock (ClientInstanceConfigurationLock)
                    {
                        return _localMessageLogEnabled;
                    }
                }
                private set
                {
                    lock (ClientInstanceConfigurationLock)
                    {
                        _localMessageLogEnabled = value;
                    }
                }
            }

            public static string REGION_MESSAGE_LOG_DIRECTORY
            {
                get
                {
                    lock (ClientInstanceConfigurationLock)
                    {
                        return _regionMessageLogDirectory;
                    }
                }
                private set
                {
                    lock (ClientInstanceConfigurationLock)
                    {
                        _regionMessageLogDirectory = value;
                    }
                }
            }

            public static bool REGION_MESSAGE_LOG_ENABLED
            {
                get
                {
                    lock (ClientInstanceConfigurationLock)
                    {
                        return _regionMessageLogEnabled;
                    }
                }
                private set
                {
                    lock (ClientInstanceConfigurationLock)
                    {
                        _regionMessageLogEnabled = value;
                    }
                }
            }

            public static bool ENABLE_HTTP_SERVER
            {
                get
                {
                    lock (ClientInstanceConfigurationLock)
                    {
                        return _enableHTTPServer;
                    }
                }
                private set
                {
                    lock (ClientInstanceConfigurationLock)
                    {
                        _enableHTTPServer = value;
                    }
                }
            }

            public static string HTTP_SERVER_PREFIX
            {
                get
                {
                    lock (ClientInstanceConfigurationLock)
                    {
                        return _HTTPServerPrefix;
                    }
                }
                private set
                {
                    lock (ClientInstanceConfigurationLock)
                    {
                        _HTTPServerPrefix = value;
                    }
                }
            }

            public static int HTTP_SERVER_TIMEOUT
            {
                get
                {
                    lock (ClientInstanceConfigurationLock)
                    {
                        return _HTTPServerTimeout;
                    }
                }
                private set
                {
                    lock (ClientInstanceConfigurationLock)
                    {
                        _HTTPServerTimeout = value;
                    }
                }
            }

            public static HTTPCompressionMethod HTTP_SERVER_COMPRESSION
            {
                get
                {
                    lock (ClientInstanceConfigurationLock)
                    {
                        return _HTTPServerCompression;
                    }
                }
                private set
                {
                    lock (ClientInstanceConfigurationLock)
                    {
                        _HTTPServerCompression = value;
                    }
                }
            }

            public static int CALLBACK_TIMEOUT
            {
                get
                {
                    lock (ClientInstanceConfigurationLock)
                    {
                        return _callbackTimeout;
                    }
                }
                private set
                {
                    lock (ClientInstanceConfigurationLock)
                    {
                        _callbackTimeout = value;
                    }
                }
            }

            public static int CALLBACK_THROTTLE
            {
                get
                {
                    lock (ClientInstanceConfigurationLock)
                    {
                        return _callbackThrottle;
                    }
                }
                private set
                {
                    lock (ClientInstanceConfigurationLock)
                    {
                        _callbackThrottle = value;
                    }
                }
            }

            public static int CALLBACK_QUEUE_LENGTH
            {
                get
                {
                    lock (ClientInstanceConfigurationLock)
                    {
                        return _callbackQueueLength;
                    }
                }
                private set
                {
                    lock (ClientInstanceConfigurationLock)
                    {
                        _callbackQueueLength = value;
                    }
                }
            }

            public static int NOTIFICATION_TIMEOUT
            {
                get
                {
                    lock (ClientInstanceConfigurationLock)
                    {
                        return _notificationTimeout;
                    }
                }
                private set
                {
                    lock (ClientInstanceConfigurationLock)
                    {
                        _notificationTimeout = value;
                    }
                }
            }

            public static int NOTIFICATION_THROTTLE
            {
                get
                {
                    lock (ClientInstanceConfigurationLock)
                    {
                        return _notificationThrottle;
                    }
                }
                private set
                {
                    lock (ClientInstanceConfigurationLock)
                    {
                        _notificationThrottle = value;
                    }
                }
            }

            public static int NOTIFICATION_QUEUE_LENGTH
            {
                get
                {
                    lock (ClientInstanceConfigurationLock)
                    {
                        return _notificationQueueLength;
                    }
                }
                private set
                {
                    lock (ClientInstanceConfigurationLock)
                    {
                        _notificationQueueLength = value;
                    }
                }
            }

            public static int CONNECTION_LIMIT
            {
                get
                {
                    lock (ClientInstanceConfigurationLock)
                    {
                        return _connectionLimit;
                    }
                }
                private set
                {
                    lock (ClientInstanceConfigurationLock)
                    {
                        _connectionLimit = value;
                    }
                }
            }

            public static int CONNECTION_IDLE_TIME
            {
                get
                {
                    lock (ClientInstanceConfigurationLock)
                    {
                        return _connectionIdleTime;
                    }
                }
                private set
                {
                    lock (ClientInstanceConfigurationLock)
                    {
                        _connectionIdleTime = value;
                    }
                }
            }

            public static float RANGE
            {
                get
                {
                    lock (ClientInstanceConfigurationLock)
                    {
                        return _range;
                    }
                }
                private set
                {
                    lock (ClientInstanceConfigurationLock)
                    {
                        _range = value;
                    }
                }
            }

            public static int MAXIMUM_NOTIFICATION_THREADS
            {
                get
                {
                    lock (ClientInstanceConfigurationLock)
                    {
                        return _maximumNotificationThreads;
                    }
                }
                private set
                {
                    lock (ClientInstanceConfigurationLock)
                    {
                        _maximumNotificationThreads = value;
                    }
                }
            }

            public static int MAXIMUM_COMMAND_THREADS
            {
                get
                {
                    lock (ClientInstanceConfigurationLock)
                    {
                        return _maximumCommandThreads;
                    }
                }
                private set
                {
                    lock (ClientInstanceConfigurationLock)
                    {
                        _maximumCommandThreads = value;
                    }
                }
            }

            public static int MAXIMUM_RLV_THREADS
            {
                get
                {
                    lock (ClientInstanceConfigurationLock)
                    {
                        return _maximumRLVThreads;
                    }
                }
                private set
                {
                    lock (ClientInstanceConfigurationLock)
                    {
                        _maximumRLVThreads = value;
                    }
                }
            }

            public static int MAXIMUM_INSTANT_MESSAGE_THREADS
            {
                get
                {
                    lock (ClientInstanceConfigurationLock)
                    {
                        return _maximumInstantMessageThreads;
                    }
                }
                private set
                {
                    lock (ClientInstanceConfigurationLock)
                    {
                        _maximumInstantMessageThreads = value;
                    }
                }
            }

            public static bool USE_NAGGLE
            {
                get
                {
                    lock (ClientInstanceConfigurationLock)
                    {
                        return _useNaggle;
                    }
                }
                private set
                {
                    lock (ClientInstanceConfigurationLock)
                    {
                        _useNaggle = value;
                    }
                }
            }

            public static bool USE_EXPECT100CONTINUE
            {
                get
                {
                    lock (ClientInstanceConfigurationLock)
                    {
                        return _useExpect100Continue;
                    }
                }
                private set
                {
                    lock (ClientInstanceConfigurationLock)
                    {
                        _useExpect100Continue = value;
                    }
                }
            }

            public static int SERVICES_TIMEOUT
            {
                get
                {
                    lock (ClientInstanceConfigurationLock)
                    {
                        return _servicesTimeout;
                    }
                }
                private set
                {
                    lock (ClientInstanceConfigurationLock)
                    {
                        _servicesTimeout = value;
                    }
                }
            }

            public static int DATA_TIMEOUT
            {
                get
                {
                    lock (ClientInstanceConfigurationLock)
                    {
                        return _dataTimeout;
                    }
                }
                private set
                {
                    lock (ClientInstanceConfigurationLock)
                    {
                        _dataTimeout = value;
                    }
                }
            }

            public static wasAdaptiveAlarm.DECAY_TYPE DATA_DECAY_TYPE
            {
                get
                {
                    lock (ClientInstanceConfigurationLock)
                    {
                        return _dataDecayType;
                    }
                }
                private set
                {
                    lock (ClientInstanceConfigurationLock)
                    {
                        _dataDecayType = value;
                    }
                }
            }

            public static int REBAKE_DELAY
            {
                get
                {
                    lock (ClientInstanceConfigurationLock)
                    {
                        return _rebakeDelay;
                    }
                }
                private set
                {
                    lock (ClientInstanceConfigurationLock)
                    {
                        _rebakeDelay = value;
                    }
                }
            }

            public static int MEMBERSHIP_SWEEP_INTERVAL
            {
                get
                {
                    lock (ClientInstanceConfigurationLock)
                    {
                        return _membershipSweepInterval;
                    }
                }
                private set
                {
                    lock (ClientInstanceConfigurationLock)
                    {
                        _membershipSweepInterval = value;
                    }
                }
            }

            public static bool TOS_ACCEPTED
            {
                get
                {
                    lock (ClientInstanceConfigurationLock)
                    {
                        return _TOSAccepted;
                    }
                }
                private set
                {
                    lock (ClientInstanceConfigurationLock)
                    {
                        _TOSAccepted = value;
                    }
                }
            }

            public static string START_LOCATION
            {
                get
                {
                    lock (ClientInstanceConfigurationLock)
                    {
                        return _startLocation;
                    }
                }
                private set
                {
                    lock (ClientInstanceConfigurationLock)
                    {
                        _startLocation = value;
                    }
                }
            }

            public static string BIND_IP_ADDRESS
            {
                get
                {
                    lock (ClientInstanceConfigurationLock)
                    {
                        return _bindIPAddress;
                    }
                }
                private set
                {
                    lock (ClientInstanceConfigurationLock)
                    {
                        _bindIPAddress = value;
                    }
                }
            }

            public static string NETWORK_CARD_MAC
            {
                get
                {
                    lock (ClientInstanceConfigurationLock)
                    {
                        return _networkCardMAC;
                    }
                }
                private set
                {
                    lock (ClientInstanceConfigurationLock)
                    {
                        _networkCardMAC = value;
                    }
                }
            }

            public static string DRIVE_IDENTIFIER_HASH
            {
                get
                {
                    lock (ClientInstanceConfigurationLock)
                    {
                        return _driveIdentifierHash;
                    }
                }
                private set
                {
                    lock (ClientInstanceConfigurationLock)
                    {
                        _driveIdentifierHash = value;
                    }
                }
            }

            public static string CLIENT_LOG_FILE
            {
                get
                {
                    lock (ClientInstanceConfigurationLock)
                    {
                        return _clientLogFile;
                    }
                }
                private set
                {
                    lock (ClientInstanceConfigurationLock)
                    {
                        _clientLogFile = value;
                    }
                }
            }

            public static bool CLIENT_LOG_ENABLED
            {
                get
                {
                    lock (ClientInstanceConfigurationLock)
                    {
                        return _clientLogEnabled;
                    }
                }
                private set
                {
                    lock (ClientInstanceConfigurationLock)
                    {
                        _clientLogEnabled = value;
                    }
                }
            }

            public static bool AUTO_ACTIVATE_GROUP
            {
                get
                {
                    lock (ClientInstanceConfigurationLock)
                    {
                        return _autoActivateGroup;
                    }
                }
                private set
                {
                    lock (ClientInstanceConfigurationLock)
                    {
                        _autoActivateGroup = value;
                    }
                }
            }

            public static int ACTIVATE_DELAY
            {
                get
                {
                    lock (ClientInstanceConfigurationLock)
                    {
                        return _activateDelay;
                    }
                }
                private set
                {
                    lock (ClientInstanceConfigurationLock)
                    {
                        _activateDelay = value;
                    }
                }
            }

            public static int GROUP_CREATE_FEE
            {
                get
                {
                    lock (ClientInstanceConfigurationLock)
                    {
                        return _groupCreateFee;
                    }
                }
                private set
                {
                    lock (ClientInstanceConfigurationLock)
                    {
                        _groupCreateFee = value;
                    }
                }
            }

            public static int EXIT_CODE_EXPECTED
            {
                get
                {
                    lock (ClientInstanceConfigurationLock)
                    {
                        return _exitCodeExpected;
                    }
                }
                private set
                {
                    lock (ClientInstanceConfigurationLock)
                    {
                        _exitCodeExpected = value;
                    }
                }
            }

            public static int EXIT_CODE_ABNORMAL
            {
                get
                {
                    lock (ClientInstanceConfigurationLock)
                    {
                        return _exitCodeAbnormal;
                    }
                }
                private set
                {
                    lock (ClientInstanceConfigurationLock)
                    {
                        _exitCodeAbnormal = value;
                    }
                }
            }

            public static HashSet<Group> GROUPS
            {
                get
                {
                    lock (ClientInstanceConfigurationLock)
                    {
                        return _groups;
                    }
                }
                private set
                {
                    lock (ClientInstanceConfigurationLock)
                    {
                        _groups = value;
                    }
                }
            }

            public static HashSet<Master> MASTERS
            {
                get
                {
                    lock (ClientInstanceConfigurationLock)
                    {
                        return _masters;
                    }
                }
                private set
                {
                    lock (ClientInstanceConfigurationLock)
                    {
                        _masters = value;
                    }
                }
            }

            public static List<Filter> INPUT_FILTERS
            {
                get
                {
                    lock (ClientInstanceConfigurationLock)
                    {
                        return _inputFilters;
                    }
                }
                set
                {
                    lock (ClientInstanceConfigurationLock)
                    {
                        _inputFilters = value;
                    }
                }
            }

            public static List<Filter> OUTPUT_FILTERS
            {
                get
                {
                    lock (ClientInstanceConfigurationLock)
                    {
                        return _outputFilters;
                    }
                }
                set
                {
                    lock (ClientInstanceConfigurationLock)
                    {
                        _outputFilters = value;
                    }
                }
            }

            public static string VIGENERE_SECRET
            {
                get
                {
                    lock (ClientInstanceConfigurationLock)
                    {
                        return _vigenereSecret;
                    }
                }
                private set
                {
                    lock (ClientInstanceConfigurationLock)
                    {
                        _vigenereSecret = value;
                    }
                }
            }

            public static ENIGMA ENIGMA
            {
                get
                {
                    lock (ClientInstanceConfigurationLock)
                    {
                        return _enigma;
                    }
                }
                set
                {
                    lock (ClientInstanceConfigurationLock)
                    {
                        _enigma = value;
                    }
                }
            }

            public static int LOGOUT_GRACE
            {
                get
                {
                    lock (ClientInstanceConfigurationLock)
                    {
                        return _logoutGrace;
                    }
                }
                private set
                {
                    lock (ClientInstanceConfigurationLock)
                    {
                        _logoutGrace = value;
                    }
                }
            }

            public static string Read(string file)
            {
                lock (ConfigurationFileLock)
                {
                    return File.ReadAllText(file);
                }
            }

            public static void Write(string file, string data)
            {
                lock (ConfigurationFileLock)
                {
                    File.WriteAllText(file, data);
                }
            }

            public static void Write(string file, XmlDocument document)
            {
                lock (ConfigurationFileLock)
                {
                    document.Save(file);
                }
            }

            public static void Load(string file)
            {
                FIRST_NAME = string.Empty;
                LAST_NAME = string.Empty;
                PASSWORD = string.Empty;
                LOGIN_URL = @"https://login.agni.lindenlab.com/cgi-bin/login.cgi";
                CLIENT_LOG_FILE = "logs/Corrode.log";
                CLIENT_LOG_ENABLED = true;
                INSTANT_MESSAGE_LOG_DIRECTORY = @"logs/im";
                INSTANT_MESSAGE_LOG_ENABLED = false;
                LOCAL_MESSAGE_LOG_DIRECTORY = @"logs/local";
                LOCAL_MESSAGE_LOG_ENABLED = false;
                REGION_MESSAGE_LOG_DIRECTORY = @"logs/region";
                REGION_MESSAGE_LOG_ENABLED = false;
                ENABLE_HTTP_SERVER = false;
                HTTP_SERVER_PREFIX = @"http://+:8080/";
                HTTP_SERVER_TIMEOUT = 5000;
                HTTP_SERVER_COMPRESSION = HTTPCompressionMethod.NONE;
                CALLBACK_TIMEOUT = 5000;
                CALLBACK_THROTTLE = 1000;
                CALLBACK_QUEUE_LENGTH = 100;
                NOTIFICATION_TIMEOUT = 5000;
                NOTIFICATION_THROTTLE = 1000;
                NOTIFICATION_QUEUE_LENGTH = 100;
                CONNECTION_LIMIT = 100;
                CONNECTION_IDLE_TIME = 900000;
                RANGE = 64;
                MAXIMUM_NOTIFICATION_THREADS = 10;
                MAXIMUM_COMMAND_THREADS = 10;
                MAXIMUM_RLV_THREADS = 10;
                MAXIMUM_INSTANT_MESSAGE_THREADS = 10;
                USE_NAGGLE = false;
                SERVICES_TIMEOUT = 60000;
                DATA_TIMEOUT = 2500;
                DATA_DECAY_TYPE = wasAdaptiveAlarm.DECAY_TYPE.ARITHMETIC;
                REBAKE_DELAY = 1000;
                ACTIVATE_DELAY = 5000;
                MEMBERSHIP_SWEEP_INTERVAL = 1000;
                TOS_ACCEPTED = false;
                START_LOCATION = "last";
                BIND_IP_ADDRESS = string.Empty;
                NETWORK_CARD_MAC = string.Empty;
                DRIVE_IDENTIFIER_HASH = string.Empty;
                AUTO_ACTIVATE_GROUP = false;
                GROUP_CREATE_FEE = 100;
                EXIT_CODE_EXPECTED = -1;
                EXIT_CODE_ABNORMAL = -2;
                GROUPS = new HashSet<Group>();
                MASTERS = new HashSet<Master>();
                INPUT_FILTERS = new List<Filter> {Filter.RFC1738};
                OUTPUT_FILTERS = new List<Filter> {Filter.RFC1738};
                ENIGMA = new ENIGMA
                {
                    rotors = new[] {'3', 'g', '1'},
                    plugs = new[] {'z', 'p', 'q'},
                    reflector = 'b'
                };
                VIGENERE_SECRET = string.Empty;

                Feedback(wasGetDescriptionFromEnumValue(ConsoleError.READING_CORRADE_CONFIGURATION));

                try
                {
                    lock (ConfigurationFileLock)
                    {
                        file = File.ReadAllText(file);
                    }
                }
                catch (Exception ex)
                {
                    Feedback(wasGetDescriptionFromEnumValue(ConsoleError.INVALID_CONFIGURATION_FILE), ex.Message);
                    Environment.Exit(EXIT_CODE_ABNORMAL);
                }

                XmlDocument conf = new XmlDocument();
                try
                {
                    conf.LoadXml(file);
                }
                catch (XmlException ex)
                {
                    Feedback(wasGetDescriptionFromEnumValue(ConsoleError.INVALID_CONFIGURATION_FILE), ex.Message);
                    Environment.Exit(EXIT_CODE_ABNORMAL);
                }

                XmlNode root = conf.DocumentElement;
                if (root == null)
                {
                    Feedback(wasGetDescriptionFromEnumValue(ConsoleError.INVALID_CONFIGURATION_FILE));
                    Environment.Exit(EXIT_CODE_ABNORMAL);
                }

                // Process client.
                try
                {
                    foreach (XmlNode client in root.SelectNodes("/config/client/*"))
                        switch (client.Name.ToLowerInvariant())
                        {
                            case ConfigurationKeys.FIRST_NAME:
                                if (string.IsNullOrEmpty(client.InnerText))
                                {
                                    throw new Exception("error in client section");
                                }
                                FIRST_NAME = client.InnerText;
                                break;
                            case ConfigurationKeys.LAST_NAME:
                                if (string.IsNullOrEmpty(client.InnerText))
                                {
                                    throw new Exception("error in client section");
                                }
                                LAST_NAME = client.InnerText;
                                break;
                            case ConfigurationKeys.PASSWORD:
                                if (string.IsNullOrEmpty(client.InnerText))
                                {
                                    throw new Exception("error in client section");
                                }
                                PASSWORD = client.InnerText;
                                break;
                            case ConfigurationKeys.LOGIN_URL:
                                if (string.IsNullOrEmpty(client.InnerText))
                                {
                                    throw new Exception("error in client section");
                                }
                                LOGIN_URL = client.InnerText;
                                break;
                            case ConfigurationKeys.TOS_ACCEPTED:
                                bool accepted;
                                if (!bool.TryParse(client.InnerText, out accepted))
                                {
                                    throw new Exception("error in client section");
                                }
                                TOS_ACCEPTED = accepted;
                                break;
                            case ConfigurationKeys.GROUP_CREATE_FEE:
                                int groupCreateFee;
                                if (!int.TryParse(client.InnerText, out groupCreateFee))
                                {
                                    throw new Exception("error in client section");
                                }
                                GROUP_CREATE_FEE = groupCreateFee;
                                break;
                            case ConfigurationKeys.EXIT_CODE:
                                XmlNodeList exitCodeNodeList = client.SelectNodes("*");
                                if (exitCodeNodeList == null)
                                {
                                    throw new Exception("error in client section");
                                }
                                foreach (XmlNode exitCodeNode in exitCodeNodeList)
                                {
                                    switch (exitCodeNode.Name.ToLowerInvariant())
                                    {
                                        case ConfigurationKeys.EXPECTED:
                                            int exitCodeExpected;
                                            if (!int.TryParse(exitCodeNode.InnerText, out exitCodeExpected))
                                            {
                                                throw new Exception("error in client section");
                                            }
                                            EXIT_CODE_EXPECTED = exitCodeExpected;
                                            break;
                                        case ConfigurationKeys.ABNORMAL:
                                            int exitCodeAbnormal;
                                            if (!int.TryParse(exitCodeNode.InnerText, out exitCodeAbnormal))
                                            {
                                                throw new Exception("error in client section");
                                            }
                                            EXIT_CODE_ABNORMAL = exitCodeAbnormal;
                                            break;
                                    }
                                }
                                break;
                            case ConfigurationKeys.AUTO_ACTIVATE_GROUP:
                                bool autoActivateGroup;
                                if (!bool.TryParse(client.InnerText, out autoActivateGroup))
                                {
                                    throw new Exception("error in client section");
                                }
                                AUTO_ACTIVATE_GROUP = autoActivateGroup;
                                break;
                            case ConfigurationKeys.START_LOCATION:
                                if (string.IsNullOrEmpty(client.InnerText))
                                {
                                    throw new Exception("error in client section");
                                }
                                START_LOCATION = client.InnerText;
                                break;
                        }
                }
                catch (Exception ex)
                {
                    Feedback(wasGetDescriptionFromEnumValue(ConsoleError.INVALID_CONFIGURATION_FILE), ex.Message);
                }

                // Process logs.
                try
                {
                    foreach (XmlNode LogNode in root.SelectNodes("/config/logs/*"))
                    {
                        switch (LogNode.Name.ToLowerInvariant())
                        {
                            case ConfigurationKeys.IM:
                                XmlNodeList imLogNodeList = LogNode.SelectNodes("*");
                                if (imLogNodeList == null)
                                {
                                    throw new Exception("error in logs section");
                                }
                                foreach (XmlNode imLogNode in imLogNodeList)
                                {
                                    switch (imLogNode.Name.ToLowerInvariant())
                                    {
                                        case ConfigurationKeys.ENABLE:
                                            bool enable;
                                            if (!bool.TryParse(imLogNode.InnerText, out enable))
                                            {
                                                throw new Exception("error in im logs section");
                                            }
                                            INSTANT_MESSAGE_LOG_ENABLED = enable;
                                            break;
                                        case ConfigurationKeys.DIRECTORY:
                                            if (string.IsNullOrEmpty(imLogNode.InnerText))
                                            {
                                                throw new Exception("error in im logs section");
                                            }
                                            INSTANT_MESSAGE_LOG_DIRECTORY = imLogNode.InnerText;
                                            break;
                                    }
                                }
                                break;
                            case ConfigurationKeys.CLIENT:
                                XmlNodeList clientLogNodeList = LogNode.SelectNodes("*");
                                if (clientLogNodeList == null)
                                {
                                    throw new Exception("error in logs section");
                                }
                                foreach (XmlNode clientLogNode in clientLogNodeList)
                                {
                                    switch (clientLogNode.Name.ToLowerInvariant())
                                    {
                                        case ConfigurationKeys.ENABLE:
                                            bool enable;
                                            if (!bool.TryParse(clientLogNode.InnerText, out enable))
                                            {
                                                throw new Exception("error in client logs section");
                                            }
                                            CLIENT_LOG_ENABLED = enable;
                                            break;
                                        case ConfigurationKeys.FILE:
                                            if (string.IsNullOrEmpty(clientLogNode.InnerText))
                                            {
                                                throw new Exception("error in client logs section");
                                            }
                                            CLIENT_LOG_FILE = clientLogNode.InnerText;
                                            break;
                                    }
                                }
                                break;
                            case ConfigurationKeys.LOCAL:
                                XmlNodeList localLogNodeList = LogNode.SelectNodes("*");
                                if (localLogNodeList == null)
                                {
                                    throw new Exception("error in logs section");
                                }
                                foreach (XmlNode localLogNode in localLogNodeList)
                                {
                                    switch (localLogNode.Name.ToLowerInvariant())
                                    {
                                        case ConfigurationKeys.ENABLE:
                                            bool enable;
                                            if (!bool.TryParse(localLogNode.InnerText, out enable))
                                            {
                                                throw new Exception("error in local logs section");
                                            }
                                            LOCAL_MESSAGE_LOG_ENABLED = enable;
                                            break;
                                        case ConfigurationKeys.DIRECTORY:
                                            if (string.IsNullOrEmpty(localLogNode.InnerText))
                                            {
                                                throw new Exception("error in local logs section");
                                            }
                                            LOCAL_MESSAGE_LOG_DIRECTORY = localLogNode.InnerText;
                                            break;
                                    }
                                }
                                break;
                            case ConfigurationKeys.REGION:
                                XmlNodeList regionLogNodeList = LogNode.SelectNodes("*");
                                if (regionLogNodeList == null)
                                {
                                    throw new Exception("error in logs section");
                                }
                                foreach (XmlNode regionLogNode in regionLogNodeList)
                                {
                                    switch (regionLogNode.Name.ToLowerInvariant())
                                    {
                                        case ConfigurationKeys.ENABLE:
                                            bool enable;
                                            if (!bool.TryParse(regionLogNode.InnerText, out enable))
                                            {
                                                throw new Exception("error in local logs section");
                                            }
                                            REGION_MESSAGE_LOG_ENABLED = enable;
                                            break;
                                        case ConfigurationKeys.DIRECTORY:
                                            if (string.IsNullOrEmpty(regionLogNode.InnerText))
                                            {
                                                throw new Exception("error in local logs section");
                                            }
                                            REGION_MESSAGE_LOG_DIRECTORY = regionLogNode.InnerText;
                                            break;
                                    }
                                }
                                break;
                        }
                    }
                }
                catch (Exception ex)
                {
                    Feedback(wasGetDescriptionFromEnumValue(ConsoleError.INVALID_CONFIGURATION_FILE), ex.Message);
                }


                // Process filters.
                try
                {
                    foreach (XmlNode FilterNode in root.SelectNodes("/config/filters/*"))
                    {
                        switch (FilterNode.Name.ToLowerInvariant())
                        {
                            case ConfigurationKeys.INPUT:
                                XmlNodeList inputFilterNodeList = FilterNode.SelectNodes("*");
                                if (inputFilterNodeList == null)
                                {
                                    throw new Exception("error in filters section");
                                }
                                INPUT_FILTERS = new List<Filter>();
                                foreach (XmlNode inputFilterNode in inputFilterNodeList)
                                {
                                    switch (inputFilterNode.Name.ToLowerInvariant())
                                    {
                                        case ConfigurationKeys.ENCODE:
                                        case ConfigurationKeys.DECODE:
                                        case ConfigurationKeys.ENCRYPT:
                                        case ConfigurationKeys.DECRYPT:
                                            INPUT_FILTERS.Add(wasGetEnumValueFromDescription<Filter>(
                                                inputFilterNode.InnerText));
                                            break;
                                        default:
                                            throw new Exception("error in input filters section");
                                    }
                                }
                                break;
                            case ConfigurationKeys.OUTPUT:
                                XmlNodeList outputFilterNodeList = FilterNode.SelectNodes("*");
                                if (outputFilterNodeList == null)
                                {
                                    throw new Exception("error in filters section");
                                }
                                OUTPUT_FILTERS = new List<Filter>();
                                foreach (XmlNode outputFilterNode in outputFilterNodeList)
                                {
                                    switch (outputFilterNode.Name.ToLowerInvariant())
                                    {
                                        case ConfigurationKeys.ENCODE:
                                        case ConfigurationKeys.DECODE:
                                        case ConfigurationKeys.ENCRYPT:
                                        case ConfigurationKeys.DECRYPT:
                                            OUTPUT_FILTERS.Add(wasGetEnumValueFromDescription<Filter>(
                                                outputFilterNode.InnerText));
                                            break;
                                        default:
                                            throw new Exception("error in output filters section");
                                    }
                                }
                                break;
                        }
                    }
                }
                catch (Exception ex)
                {
                    Feedback(wasGetDescriptionFromEnumValue(ConsoleError.INVALID_CONFIGURATION_FILE), ex.Message);
                }

                // Process cryptography.
                try
                {
                    foreach (XmlNode FilterNode in root.SelectNodes("/config/cryptography/*"))
                    {
                        switch (FilterNode.Name.ToLowerInvariant())
                        {
                            case ConfigurationKeys.ENIGMA:
                                XmlNodeList ENIGMANodeList = FilterNode.SelectNodes("*");
                                if (ENIGMANodeList == null)
                                {
                                    throw new Exception("error in cryptography section");
                                }
                                ENIGMA enigma = new ENIGMA();
                                foreach (XmlNode ENIGMANode in ENIGMANodeList)
                                {
                                    switch (ENIGMANode.Name.ToLowerInvariant())
                                    {
                                        case ConfigurationKeys.ROTORS:
                                            enigma.rotors = ENIGMANode.InnerText.ToArray();
                                            break;
                                        case ConfigurationKeys.PLUGS:
                                            enigma.plugs = ENIGMANode.InnerText.ToArray();
                                            break;
                                        case ConfigurationKeys.REFLECTOR:
                                            enigma.reflector = ENIGMANode.InnerText.SingleOrDefault();
                                            break;
                                    }
                                }
                                ENIGMA = enigma;
                                break;
                            case ConfigurationKeys.VIGENERE:
                                XmlNodeList VIGENERENodeList = FilterNode.SelectNodes("*");
                                if (VIGENERENodeList == null)
                                {
                                    throw new Exception("error in cryptography section");
                                }
                                foreach (XmlNode VIGENERENode in VIGENERENodeList)
                                {
                                    switch (VIGENERENode.Name.ToLowerInvariant())
                                    {
                                        case ConfigurationKeys.SECRET:
                                            VIGENERE_SECRET = VIGENERENode.InnerText;
                                            break;
                                    }
                                }
                                break;
                        }
                    }
                }
                catch (Exception ex)
                {
                    Feedback(wasGetDescriptionFromEnumValue(ConsoleError.INVALID_CONFIGURATION_FILE), ex.Message);
                }


                // Process AIML.
                try
                {
                    foreach (XmlNode AIMLNode in root.SelectNodes("/config/aiml/*"))
                    {
                        switch (AIMLNode.Name.ToLowerInvariant())
                        {
                            case ConfigurationKeys.ENABLE:
                                bool enable;
                                if (!bool.TryParse(AIMLNode.InnerText, out enable))
                                {
                                    throw new Exception("error in AIML section");
                                }
                                EnableAIML = enable;
                                break;
                        }
                    }
                }
                catch (Exception ex)
                {
                    Feedback(wasGetDescriptionFromEnumValue(ConsoleError.INVALID_CONFIGURATION_FILE), ex.Message);
                }

                // Process RLV.
                try
                {
                    foreach (XmlNode RLVNode in root.SelectNodes("/config/rlv/*"))
                    {
                        switch (RLVNode.Name.ToLowerInvariant())
                        {
                            case ConfigurationKeys.ENABLE:
                                bool enable;
                                if (!bool.TryParse(RLVNode.InnerText, out enable))
                                {
                                    throw new Exception("error in RLV section");
                                }
                                EnableRLV = enable;
                                break;
                        }
                    }
                }
                catch (Exception ex)
                {
                    Feedback(wasGetDescriptionFromEnumValue(ConsoleError.INVALID_CONFIGURATION_FILE), ex.Message);
                }

                // Process server.
                try
                {
                    foreach (XmlNode serverNode in root.SelectNodes("/config/server/*"))
                    {
                        switch (serverNode.Name.ToLowerInvariant())
                        {
                            case ConfigurationKeys.HTTP:
                                bool enableHTTPServer;
                                if (!bool.TryParse(serverNode.InnerText, out enableHTTPServer))
                                {
                                    throw new Exception("error in server section");
                                }
                                ENABLE_HTTP_SERVER = enableHTTPServer;
                                break;
                            case ConfigurationKeys.PREFIX:
                                if (string.IsNullOrEmpty(serverNode.InnerText))
                                {
                                    throw new Exception("error in server section");
                                }
                                HTTP_SERVER_PREFIX = serverNode.InnerText;
                                break;
                            case ConfigurationKeys.COMPRESSION:
                                HTTP_SERVER_COMPRESSION = wasGetEnumValueFromDescription<HTTPCompressionMethod>(
                                    serverNode.InnerText);
                                break;
                        }
                    }
                }
                catch (Exception ex)
                {
                    Feedback(wasGetDescriptionFromEnumValue(ConsoleError.INVALID_CONFIGURATION_FILE), ex.Message);
                }

                // Process network.
                try
                {
                    foreach (XmlNode networkNode in root.SelectNodes("/config/network/*"))
                    {
                        switch (networkNode.Name.ToLowerInvariant())
                        {
                            case ConfigurationKeys.BIND:
                                if (!string.IsNullOrEmpty(networkNode.InnerText))
                                {
                                    BIND_IP_ADDRESS = networkNode.InnerText;
                                }
                                break;
                            case ConfigurationKeys.MAC:
                                if (!string.IsNullOrEmpty(networkNode.InnerText))
                                {
                                    NETWORK_CARD_MAC = networkNode.InnerText;
                                }
                                break;
                            case ConfigurationKeys.ID0:
                                if (!string.IsNullOrEmpty(networkNode.InnerText))
                                {
                                    DRIVE_IDENTIFIER_HASH = networkNode.InnerText;
                                }
                                break;
                            case ConfigurationKeys.NAGGLE:
                                bool useNaggle;
                                if (!bool.TryParse(networkNode.InnerText, out useNaggle))
                                {
                                    throw new Exception("error in network section");
                                }
                                USE_NAGGLE = useNaggle;
                                break;
                            case ConfigurationKeys.EXPECT100CONTINUE:
                                bool useExpect100Continue;
                                if (!bool.TryParse(networkNode.InnerText, out useExpect100Continue))
                                {
                                    throw new Exception("error in network section");
                                }
                                USE_EXPECT100CONTINUE = useExpect100Continue;
                                break;
                        }
                    }
                }
                catch (Exception ex)
                {
                    Feedback(wasGetDescriptionFromEnumValue(ConsoleError.INVALID_CONFIGURATION_FILE), ex.Message);
                }

                // Process limits.
                try
                {
                    foreach (XmlNode limitsNode in root.SelectNodes("/config/limits/*"))
                    {
                        switch (limitsNode.Name.ToLowerInvariant())
                        {
                            case ConfigurationKeys.RANGE:
                                float range;
                                if (!float.TryParse(limitsNode.InnerText,
                                    out range))
                                {
                                    throw new Exception("error in range limits section");
                                }
                                RANGE = range;
                                break;
                            case ConfigurationKeys.RLV:
                                XmlNodeList rlvLimitNodeList = limitsNode.SelectNodes("*");
                                if (rlvLimitNodeList == null)
                                {
                                    throw new Exception("error in RLV limits section");
                                }
                                foreach (XmlNode rlvLimitNode in rlvLimitNodeList)
                                {
                                    switch (rlvLimitNode.Name.ToLowerInvariant())
                                    {
                                        case ConfigurationKeys.THREADS:
                                            int maximumRLVThreads;
                                            if (
                                                !int.TryParse(rlvLimitNode.InnerText,
                                                    out maximumRLVThreads))
                                            {
                                                throw new Exception("error in RLV limits section");
                                            }
                                            MAXIMUM_RLV_THREADS = maximumRLVThreads;
                                            break;
                                    }
                                }
                                break;
                            case ConfigurationKeys.COMMANDS:
                                XmlNodeList commandsLimitNodeList = limitsNode.SelectNodes("*");
                                if (commandsLimitNodeList == null)
                                {
                                    throw new Exception("error in commands limits section");
                                }
                                foreach (XmlNode commandsLimitNode in commandsLimitNodeList)
                                {
                                    switch (commandsLimitNode.Name.ToLowerInvariant())
                                    {
                                        case ConfigurationKeys.THREADS:
                                            int maximumCommandThreads;
                                            if (
                                                !int.TryParse(commandsLimitNode.InnerText,
                                                    out maximumCommandThreads))
                                            {
                                                throw new Exception("error in commands limits section");
                                            }
                                            MAXIMUM_COMMAND_THREADS = maximumCommandThreads;
                                            break;
                                    }
                                }
                                break;
                            case ConfigurationKeys.IM:
                                XmlNodeList instantMessageLimitNodeList = limitsNode.SelectNodes("*");
                                if (instantMessageLimitNodeList == null)
                                {
                                    throw new Exception("error in instant message limits section");
                                }
                                foreach (XmlNode instantMessageLimitNode in instantMessageLimitNodeList)
                                {
                                    switch (instantMessageLimitNode.Name.ToLowerInvariant())
                                    {
                                        case ConfigurationKeys.THREADS:
                                            int maximumInstantMessageThreads;
                                            if (
                                                !int.TryParse(instantMessageLimitNode.InnerText,
                                                    out maximumInstantMessageThreads))
                                            {
                                                throw new Exception("error in instant message limits section");
                                            }
                                            MAXIMUM_INSTANT_MESSAGE_THREADS = maximumInstantMessageThreads;
                                            break;
                                    }
                                }
                                break;
                            case ConfigurationKeys.CLIENT:
                                XmlNodeList clientLimitNodeList = limitsNode.SelectNodes("*");
                                if (clientLimitNodeList == null)
                                {
                                    throw new Exception("error in client limits section");
                                }
                                foreach (XmlNode clientLimitNode in clientLimitNodeList)
                                {
                                    switch (clientLimitNode.Name.ToLowerInvariant())
                                    {
                                        case ConfigurationKeys.CONNECTIONS:
                                            int connectionLimit;
                                            if (
                                                !int.TryParse(clientLimitNode.InnerText,
                                                    out connectionLimit))
                                            {
                                                throw new Exception("error in client limits section");
                                            }
                                            CONNECTION_LIMIT = connectionLimit;
                                            break;
                                        case ConfigurationKeys.IDLE:
                                            int connectionIdleTime;
                                            if (
                                                !int.TryParse(clientLimitNode.InnerText,
                                                    out connectionIdleTime))
                                            {
                                                throw new Exception("error in client limits section");
                                            }
                                            CONNECTION_IDLE_TIME = connectionIdleTime;
                                            break;
                                    }
                                }
                                break;
                            case ConfigurationKeys.CALLBACKS:
                                XmlNodeList callbackLimitNodeList = limitsNode.SelectNodes("*");
                                if (callbackLimitNodeList == null)
                                {
                                    throw new Exception("error in callback limits section");
                                }
                                foreach (XmlNode callbackLimitNode in callbackLimitNodeList)
                                {
                                    switch (callbackLimitNode.Name.ToLowerInvariant())
                                    {
                                        case ConfigurationKeys.TIMEOUT:
                                            int callbackTimeout;
                                            if (!int.TryParse(callbackLimitNode.InnerText, out callbackTimeout))
                                            {
                                                throw new Exception("error in callback limits section");
                                            }
                                            CALLBACK_TIMEOUT = callbackTimeout;
                                            break;
                                        case ConfigurationKeys.THROTTLE:
                                            int callbackThrottle;
                                            if (
                                                !int.TryParse(callbackLimitNode.InnerText, out callbackThrottle))
                                            {
                                                throw new Exception("error in callback limits section");
                                            }
                                            CALLBACK_THROTTLE = callbackThrottle;
                                            break;
                                        case ConfigurationKeys.QUEUE_LENGTH:
                                            int callbackQueueLength;
                                            if (
                                                !int.TryParse(callbackLimitNode.InnerText,
                                                    out callbackQueueLength))
                                            {
                                                throw new Exception("error in callback limits section");
                                            }
                                            CALLBACK_QUEUE_LENGTH = callbackQueueLength;
                                            break;
                                    }
                                }
                                break;
                            case ConfigurationKeys.NOTIFICATIONS:
                                XmlNodeList notificationLimitNodeList = limitsNode.SelectNodes("*");
                                if (notificationLimitNodeList == null)
                                {
                                    throw new Exception("error in notification limits section");
                                }
                                foreach (XmlNode notificationLimitNode in notificationLimitNodeList)
                                {
                                    switch (notificationLimitNode.Name.ToLowerInvariant())
                                    {
                                        case ConfigurationKeys.TIMEOUT:
                                            int notificationTimeout;
                                            if (
                                                !int.TryParse(notificationLimitNode.InnerText,
                                                    out notificationTimeout))
                                            {
                                                throw new Exception("error in notification limits section");
                                            }
                                            NOTIFICATION_TIMEOUT = notificationTimeout;
                                            break;
                                        case ConfigurationKeys.THROTTLE:
                                            int notificationThrottle;
                                            if (
                                                !int.TryParse(notificationLimitNode.InnerText,
                                                    out notificationThrottle))
                                            {
                                                throw new Exception("error in notification limits section");
                                            }
                                            NOTIFICATION_THROTTLE = notificationThrottle;
                                            break;
                                        case ConfigurationKeys.QUEUE_LENGTH:
                                            int notificationQueueLength;
                                            if (
                                                !int.TryParse(notificationLimitNode.InnerText,
                                                    out notificationQueueLength))
                                            {
                                                throw new Exception("error in notification limits section");
                                            }
                                            NOTIFICATION_QUEUE_LENGTH = notificationQueueLength;
                                            break;
                                        case ConfigurationKeys.THREADS:
                                            int maximumNotificationThreads;
                                            if (
                                                !int.TryParse(notificationLimitNode.InnerText,
                                                    out maximumNotificationThreads))
                                            {
                                                throw new Exception("error in notification limits section");
                                            }
                                            MAXIMUM_NOTIFICATION_THREADS = maximumNotificationThreads;
                                            break;
                                    }
                                }
                                break;
                            case ConfigurationKeys.SERVER:
                                XmlNodeList HTTPServerLimitNodeList = limitsNode.SelectNodes("*");
                                if (HTTPServerLimitNodeList == null)
                                {
                                    throw new Exception("error in server limits section");
                                }
                                foreach (XmlNode HTTPServerLimitNode in HTTPServerLimitNodeList)
                                {
                                    switch (HTTPServerLimitNode.Name.ToLowerInvariant())
                                    {
                                        case ConfigurationKeys.TIMEOUT:
                                            int HTTPServerTimeout;
                                            if (
                                                !int.TryParse(HTTPServerLimitNode.InnerText,
                                                    out HTTPServerTimeout))
                                            {
                                                throw new Exception("error in server limits section");
                                            }
                                            HTTP_SERVER_TIMEOUT = HTTPServerTimeout;
                                            break;
                                    }
                                }
                                break;
                            case ConfigurationKeys.SERVICES:
                                XmlNodeList servicesLimitNodeList = limitsNode.SelectNodes("*");
                                if (servicesLimitNodeList == null)
                                {
                                    throw new Exception("error in services limits section");
                                }
                                foreach (XmlNode servicesLimitNode in servicesLimitNodeList)
                                {
                                    switch (servicesLimitNode.Name.ToLowerInvariant())
                                    {
                                        case ConfigurationKeys.TIMEOUT:
                                            int servicesTimeout;
                                            if (
                                                !int.TryParse(servicesLimitNode.InnerText,
                                                    out servicesTimeout))
                                            {
                                                throw new Exception("error in services limits section");
                                            }
                                            SERVICES_TIMEOUT = servicesTimeout;
                                            break;
                                        case ConfigurationKeys.REBAKE:
                                            int rebakeDelay;
                                            if (!int.TryParse(servicesLimitNode.InnerText, out rebakeDelay))
                                            {
                                                throw new Exception("error in services limits section");
                                            }
                                            REBAKE_DELAY = rebakeDelay;
                                            break;
                                        case ConfigurationKeys.ACTIVATE:
                                            int activateDelay;
                                            if (
                                                !int.TryParse(servicesLimitNode.InnerText,
                                                    out activateDelay))
                                            {
                                                throw new Exception("error in services limits section");
                                            }
                                            ACTIVATE_DELAY = activateDelay;
                                            break;
                                    }
                                }
                                break;
                            case ConfigurationKeys.DATA:
                                XmlNodeList dataLimitNodeList = limitsNode.SelectNodes("*");
                                if (dataLimitNodeList == null)
                                {
                                    throw new Exception("error in data limits section");
                                }
                                foreach (XmlNode dataLimitNode in dataLimitNodeList)
                                {
                                    switch (dataLimitNode.Name.ToLowerInvariant())
                                    {
                                        case ConfigurationKeys.TIMEOUT:
                                            int dataTimeout;
                                            if (
                                                !int.TryParse(dataLimitNode.InnerText,
                                                    out dataTimeout))
                                            {
                                                throw new Exception("error in data limits section");
                                            }
                                            DATA_TIMEOUT = dataTimeout;
                                            break;
                                        case ConfigurationKeys.DECAY:
                                            DATA_DECAY_TYPE =
                                                wasGetEnumValueFromDescription<wasAdaptiveAlarm.DECAY_TYPE>(
                                                    dataLimitNode.InnerText);
                                            break;
                                    }
                                }
                                break;
                            case ConfigurationKeys.MEMBERSHIP:
                                XmlNodeList membershipLimitNodeList = limitsNode.SelectNodes("*");
                                if (membershipLimitNodeList == null)
                                {
                                    throw new Exception("error in membership limits section");
                                }
                                foreach (XmlNode servicesLimitNode in membershipLimitNodeList)
                                {
                                    switch (servicesLimitNode.Name.ToLowerInvariant())
                                    {
                                        case ConfigurationKeys.SWEEP:
                                            int membershipSweepInterval;
                                            if (
                                                !int.TryParse(servicesLimitNode.InnerText,
                                                    out membershipSweepInterval))
                                            {
                                                throw new Exception("error in membership limits section");
                                            }
                                            MEMBERSHIP_SWEEP_INTERVAL = membershipSweepInterval;
                                            break;
                                    }
                                }
                                break;
                            case ConfigurationKeys.LOGOUT:
                                XmlNodeList logoutLimitNodeList = limitsNode.SelectNodes("*");
                                if (logoutLimitNodeList == null)
                                {
                                    throw new Exception("error in logout limits section");
                                }
                                foreach (XmlNode logoutLimitNode in logoutLimitNodeList)
                                {
                                    switch (logoutLimitNode.Name.ToLowerInvariant())
                                    {
                                        case ConfigurationKeys.TIMEOUT:
                                            int logoutGrace;
                                            if (
                                                !int.TryParse(logoutLimitNode.InnerText,
                                                    out logoutGrace))
                                            {
                                                throw new Exception("error in logout limits section");
                                            }
                                            LOGOUT_GRACE = logoutGrace;
                                            break;
                                    }
                                }
                                break;
                        }
                    }
                }
                catch (Exception ex)
                {
                    Feedback(wasGetDescriptionFromEnumValue(ConsoleError.INVALID_CONFIGURATION_FILE), ex.Message);
                }


                // Process masters.
                try
                {
                    foreach (XmlNode mastersNode in root.SelectNodes("/config/masters/*"))
                    {
                        Master configMaster = new Master();
                        foreach (XmlNode masterNode in mastersNode.ChildNodes)
                        {
                            switch (masterNode.Name.ToLowerInvariant())
                            {
                                case ConfigurationKeys.FIRST_NAME:
                                    if (string.IsNullOrEmpty(masterNode.InnerText))
                                    {
                                        throw new Exception("error in masters section");
                                    }
                                    configMaster.FirstName = masterNode.InnerText;
                                    break;
                                case ConfigurationKeys.LAST_NAME:
                                    if (string.IsNullOrEmpty(masterNode.InnerText))
                                    {
                                        throw new Exception("error in masters section");
                                    }
                                    configMaster.LastName = masterNode.InnerText;
                                    break;
                            }
                        }
                        MASTERS.Add(configMaster);
                    }
                }
                catch (Exception ex)
                {
                    Feedback(wasGetDescriptionFromEnumValue(ConsoleError.INVALID_CONFIGURATION_FILE), ex.Message);
                }

                // Process groups.
                try
                {
                    foreach (XmlNode groupsNode in root.SelectNodes("/config/groups/*"))
                    {
                        Group configGroup = new Group
                        {
                            ChatLog = string.Empty,
                            ChatLogEnabled = false,
                            DatabaseFile = string.Empty,
                            Name = string.Empty,
                            NotificationMask = 0,
                            Password = string.Empty,
                            PermissionMask = 0,
                            UUID = UUID.Zero,
                            Workers = 5
                        };
                        foreach (XmlNode groupNode in groupsNode.ChildNodes)
                        {
                            switch (groupNode.Name.ToLowerInvariant())
                            {
                                case ConfigurationKeys.NAME:
                                    if (string.IsNullOrEmpty(groupNode.InnerText))
                                    {
                                        throw new Exception("error in group section");
                                    }
                                    configGroup.Name = groupNode.InnerText;
                                    break;
                                case ConfigurationKeys.UUID:
                                    if (!UUID.TryParse(groupNode.InnerText, out configGroup.UUID))
                                    {
                                        throw new Exception("error in group section");
                                    }
                                    break;
                                case ConfigurationKeys.PASSWORD:
                                    if (string.IsNullOrEmpty(groupNode.InnerText))
                                    {
                                        throw new Exception("error in group section");
                                    }
                                    configGroup.Password = groupNode.InnerText;
                                    break;
                                case ConfigurationKeys.WORKERS:
                                    if (!uint.TryParse(groupNode.InnerText, out configGroup.Workers))
                                    {
                                        throw new Exception("error in group section");
                                    }
                                    break;
                                case ConfigurationKeys.CHATLOG:
                                    XmlNodeList groupChatLogNodeList = groupNode.SelectNodes("*");
                                    if (groupChatLogNodeList == null)
                                    {
                                        throw new Exception("error in group section");
                                    }
                                    foreach (XmlNode groupChatLogNode in groupChatLogNodeList)
                                    {
                                        switch (groupChatLogNode.Name.ToLowerInvariant())
                                        {
                                            case ConfigurationKeys.ENABLE:
                                                bool enable;
                                                if (!bool.TryParse(groupChatLogNode.InnerText, out enable))
                                                {
                                                    throw new Exception("error in group chat logs section");
                                                }
                                                configGroup.ChatLogEnabled = enable;
                                                break;
                                            case ConfigurationKeys.FILE:
                                                if (string.IsNullOrEmpty(groupChatLogNode.InnerText))
                                                {
                                                    throw new Exception("error in group chat logs section");
                                                }
                                                configGroup.ChatLog = groupChatLogNode.InnerText;
                                                break;
                                        }
                                    }
                                    break;
                                case ConfigurationKeys.DATABASE:
                                    if (string.IsNullOrEmpty(groupNode.InnerText))
                                    {
                                        throw new Exception("error in group section");
                                    }
                                    configGroup.DatabaseFile = groupNode.InnerText;
                                    break;
                                case ConfigurationKeys.PERMISSIONS:
                                    XmlNodeList permissionNodeList = groupNode.SelectNodes("*");
                                    if (permissionNodeList == null)
                                    {
                                        throw new Exception("error in group permission section");
                                    }
                                    uint permissionMask = 0;
                                    foreach (XmlNode permissioNode in permissionNodeList)
                                    {
                                        XmlNode node = permissioNode;
                                        Parallel.ForEach(
                                            wasGetEnumDescriptions<Permissions>()
                                                .AsParallel().Where(name => name.Equals(node.Name,
                                                    StringComparison.Ordinal)), name =>
                                                    {
                                                        bool granted;
                                                        if (!bool.TryParse(node.InnerText, out granted))
                                                        {
                                                            throw new Exception(
                                                                "error in group permission section");
                                                        }
                                                        if (granted)
                                                        {
                                                            permissionMask = permissionMask |
                                                                             (uint)
                                                                                 wasGetEnumValueFromDescription
                                                                                     <Permissions>(name);
                                                        }
                                                    });
                                    }
                                    configGroup.PermissionMask = permissionMask;
                                    break;
                                case ConfigurationKeys.NOTIFICATIONS:
                                    XmlNodeList notificationNodeList = groupNode.SelectNodes("*");
                                    if (notificationNodeList == null)
                                    {
                                        throw new Exception("error in group notification section");
                                    }
                                    uint notificationMask = 0;
                                    foreach (XmlNode notificationNode in notificationNodeList)
                                    {
                                        XmlNode node = notificationNode;
                                        Parallel.ForEach(
                                            wasGetEnumDescriptions<Notifications>()
                                                .AsParallel().Where(name => name.Equals(node.Name,
                                                    StringComparison.Ordinal)), name =>
                                                    {
                                                        bool granted;
                                                        if (!bool.TryParse(node.InnerText, out granted))
                                                        {
                                                            throw new Exception(
                                                                "error in group notification section");
                                                        }
                                                        if (granted)
                                                        {
                                                            notificationMask = notificationMask |
                                                                               (uint)
                                                                                   wasGetEnumValueFromDescription
                                                                                       <Notifications>(name);
                                                        }
                                                    });
                                    }
                                    configGroup.NotificationMask = notificationMask;
                                    break;
                            }
                        }
                        GROUPS.Add(configGroup);
                    }
                }
                catch (Exception ex)
                {
                    Feedback(wasGetDescriptionFromEnumValue(ConsoleError.INVALID_CONFIGURATION_FILE), ex.Message);
                }

                // Enable AIML in case it was enabled in the configuration file.
                switch (EnableAIML)
                {
                    case true:
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
                        break;
                    default:
                        AIMLBotConfigurationWatcher.EnableRaisingEvents = false;
                        AIMLBot.isAcceptingUserInput = false;
                        break;
                }

                // Dynamically disable or enable notifications.
                Parallel.ForEach(wasGetEnumDescriptions<Notifications>().AsParallel().Select(
                    wasGetEnumValueFromDescription<Notifications>), o =>
                    {
                        bool enabled = GROUPS.AsParallel().Any(
                            p =>
                                !(p.NotificationMask & (uint) o).Equals(0));
                        switch (o)
                        {
                            case Notifications.NOTIFICATION_GROUP_MEMBERSHIP:
                                switch (enabled)
                                {
                                    case true:
                                        // Start the group membership thread.
                                        StartGroupMembershipSweepThread.Invoke();
                                        break;
                                    default:
                                        // Stop the group sweep thread.
                                        StopGroupMembershipSweepThread.Invoke();
                                        break;
                                }
                                break;
                            case Notifications.NOTIFICATION_FRIENDSHIP:
                                switch (enabled)
                                {
                                    case true:
                                        Client.Friends.FriendshipOffered += HandleFriendshipOffered;
                                        Client.Friends.FriendshipResponse += HandleFriendShipResponse;
                                        Client.Friends.FriendOnline += HandleFriendOnlineStatus;
                                        Client.Friends.FriendOffline += HandleFriendOnlineStatus;
                                        Client.Friends.FriendRightsUpdate += HandleFriendRightsUpdate;
                                        break;
                                    default:
                                        Client.Friends.FriendshipOffered -= HandleFriendshipOffered;
                                        Client.Friends.FriendshipResponse -= HandleFriendShipResponse;
                                        Client.Friends.FriendOnline -= HandleFriendOnlineStatus;
                                        Client.Friends.FriendOffline -= HandleFriendOnlineStatus;
                                        Client.Friends.FriendRightsUpdate -= HandleFriendRightsUpdate;
                                        break;
                                }
                                break;
                            case Notifications.NOTIFICATION_SCRIPT_PERMISSION:
                                switch (enabled)
                                {
                                    case true:
                                        Client.Self.ScriptQuestion += HandleScriptQuestion;
                                        break;
                                    default:
                                        Client.Self.ScriptQuestion -= HandleScriptQuestion;
                                        break;
                                }
                                break;
                            case Notifications.NOTIFICATION_ALERT_MESSAGE:
                                switch (enabled)
                                {
                                    case true:
                                        Client.Self.AlertMessage += HandleAlertMessage;
                                        break;
                                    default:
                                        Client.Self.AlertMessage -= HandleAlertMessage;
                                        break;
                                }
                                break;
                            case Notifications.NOTIFICATION_BALANCE:
                                switch (enabled)
                                {
                                    case true:
                                        Client.Self.MoneyBalance += HandleMoneyBalance;
                                        break;
                                    default:
                                        Client.Self.MoneyBalance -= HandleMoneyBalance;
                                        break;
                                }
                                break;
                            case Notifications.NOTIFICATION_ECONOMY:
                                switch (enabled)
                                {
                                    case true:
                                        Client.Self.MoneyBalanceReply += HandleMoneyBalance;
                                        break;
                                    default:
                                        Client.Self.MoneyBalanceReply -= HandleMoneyBalance;
                                        break;
                                }
                                break;
                            case Notifications.NOTIFICATION_SCRIPT_DIALOG:
                                switch (enabled)
                                {
                                    case true:
                                        Client.Self.ScriptDialog += HandleScriptDialog;
                                        break;
                                    default:
                                        Client.Self.ScriptDialog -= HandleScriptDialog;
                                        break;
                                }
                                break;
                            case Notifications.NOTIFICATION_TERSE_UPDATES:
                                switch (enabled)
                                {
                                    case true:
                                        Client.Objects.TerseObjectUpdate += HandleTerseObjectUpdate;
                                        break;
                                    default:
                                        Client.Objects.TerseObjectUpdate -= HandleTerseObjectUpdate;
                                        break;
                                }
                                break;
                            case Notifications.NOTIFICATION_VIEWER_EFFECT:
                                switch (enabled)
                                {
                                    case true:
                                        Client.Avatars.ViewerEffect += HandleViewerEffect;
                                        Client.Avatars.ViewerEffectPointAt += HandleViewerEffect;
                                        Client.Avatars.ViewerEffectLookAt += HandleViewerEffect;
                                        break;
                                    default:
                                        Client.Avatars.ViewerEffect -= HandleViewerEffect;
                                        Client.Avatars.ViewerEffectPointAt -= HandleViewerEffect;
                                        Client.Avatars.ViewerEffectLookAt -= HandleViewerEffect;
                                        break;
                                }
                                break;
                            case Notifications.NOTIFICATION_MEAN_COLLISION:
                                switch (enabled)
                                {
                                    case true:
                                        Client.Self.MeanCollision += HandleMeanCollision;
                                        break;
                                    default:
                                        Client.Self.MeanCollision -= HandleMeanCollision;
                                        break;
                                }
                                break;
                            case Notifications.NOTIFICATION_REGION_CROSSED:
                                switch (enabled)
                                {
                                    case true:
                                        Client.Self.RegionCrossed += HandleRegionCrossed;
                                        Client.Network.SimChanged += HandleSimChanged;
                                        break;
                                    default:
                                        Client.Self.RegionCrossed -= HandleRegionCrossed;
                                        Client.Network.SimChanged -= HandleSimChanged;
                                        break;
                                }
                                break;
                            case Notifications.NOTIFICATION_LOAD_URL:
                                switch (enabled)
                                {
                                    case true:
                                        Client.Self.LoadURL += HandleLoadURL;
                                        break;
                                    default:
                                        Client.Self.LoadURL -= HandleLoadURL;
                                        break;
                                }
                                break;
                        }
                    });
                // If any group has either the avatar radar notification or the primitive radar notification then install the listeners.
                switch (
                    GROUPS.AsParallel().Any(
                        o => !(o.NotificationMask & (uint) Notifications.NOTIFICATION_RADAR_AVATARS).Equals(0)) ||
                    GROUPS.AsParallel().Any(
                        o => !(o.NotificationMask & (uint) Notifications.NOTIFICATION_RADAR_PRIMITIVES).Equals(0)))
                {
                    case true:
                        Client.Network.SimChanged += HandleRadarObjects;
                        Client.Objects.AvatarUpdate += HandleAvatarUpdate;
                        Client.Objects.ObjectUpdate += HandleObjectUpdate;
                        Client.Objects.KillObject += HandleKillObject;
                        break;
                    default:
                        Client.Network.SimChanged -= HandleRadarObjects;
                        Client.Objects.AvatarUpdate -= HandleAvatarUpdate;
                        Client.Objects.ObjectUpdate -= HandleObjectUpdate;
                        Client.Objects.KillObject -= HandleKillObject;
                        break;
                }
                // Apply settings to the instance.
                Client.Self.Movement.Camera.Far = RANGE;
                Client.Settings.LOGIN_TIMEOUT = SERVICES_TIMEOUT;
                Client.Settings.LOGOUT_TIMEOUT = SERVICES_TIMEOUT;
                Feedback(wasGetDescriptionFromEnumValue(ConsoleError.READ_CORRADE_CONFIGURATION));
            }
        }
    }
}