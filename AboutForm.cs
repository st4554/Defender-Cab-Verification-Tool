using System;
using System.Reflection;
using System.Windows.Forms;

namespace Defender_Cab_Verification_Tool
{
    public partial class AboutForm : Form
    {
        public AboutForm()
        {
            InitializeComponent();
            PopulateVersion();
        }

        private void PopulateVersion()
        {
            try
            {
                var assembly = Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly();
                var fileVersionAttr = assembly.GetCustomAttribute<AssemblyFileVersionAttribute>();
                var productVersion = fileVersionAttr?.Version ?? assembly.GetName().Version?.ToString() ?? Application.ProductVersion;
                lblVersion.Text = "Version: " + productVersion;
            }
            catch
            {
                lblVersion.Text = "Version: unknown";
            }
        }

        private void BtnClose_Click(object sender, EventArgs e)
        {
            this.Close();
        }
    }
}