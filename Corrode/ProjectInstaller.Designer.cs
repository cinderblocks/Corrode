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

using System.ComponentModel;
using System.ServiceProcess;

namespace Corrode
{
    partial class ProjectInstaller
    {
        /// <summary>
        /// Required designer variable.
        /// </summary>
        private IContainer components = null;

        /// <summary> 
        /// Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Component Designer generated code

        /// <summary>
        /// Required method for Designer support - do not modify
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            this.CorrodeProcessInstaller = new System.ServiceProcess.ServiceProcessInstaller();
            this.CorrodeInstaller = new System.ServiceProcess.ServiceInstaller();
            // 
            // CorrodeProcessInstaller
            // 
            this.CorrodeProcessInstaller.Account = System.ServiceProcess.ServiceAccount.LocalSystem;
            this.CorrodeProcessInstaller.Password = null;
            this.CorrodeProcessInstaller.Username = null;
            // 
            // CorrodeInstaller
            // 
            this.CorrodeInstaller.Description = "Corrode Second Life and OpenSim Scripted Agent";
            this.CorrodeInstaller.DisplayName = "Corrode";
            this.CorrodeInstaller.ServiceName = "Corrode";
            this.CorrodeInstaller.ServicesDependedOn = new string[] {
        "Network Connections"};
            // 
            // ProjectInstaller
            // 
            this.Installers.AddRange(new System.Configuration.Install.Installer[] {
            this.CorrodeProcessInstaller,
            this.CorrodeInstaller});

        }

        #endregion

        private ServiceProcessInstaller CorrodeProcessInstaller;
        private ServiceInstaller CorrodeInstaller;
    }
}
