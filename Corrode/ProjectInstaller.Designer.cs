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
