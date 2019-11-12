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
