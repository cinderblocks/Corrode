/**
 * Copyright(C) 2013 Wizardry and Steamworks
 * Copyright(C) 2019 Sjofn LLC
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

#region

using System.ComponentModel;
using System.Configuration.Install;

#endregion

namespace Corrode
{
    [RunInstaller(true)]
    public partial class ProjectInstaller : Installer
    {
        public ProjectInstaller()
        {
            InitializeComponent();
            // Set the service name.
            string serviceName = string.IsNullOrEmpty(Corrode.InstalledServiceName)
                ? CorrodeInstaller.ServiceName
                : Corrode.InstalledServiceName;
            CorrodeInstaller.ServiceName = serviceName;
            CorrodeInstaller.DisplayName = serviceName;
        }
    }
}
