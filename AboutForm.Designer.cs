using System.Windows.Forms;

namespace Defender_Cab_Verification_Tool
{
    partial class AboutForm
    {
        private System.ComponentModel.IContainer components = null;
        private Label lblCreatedBy;
        private Label lblMaintainedBy;
        private Label lblVersion;
        private Button btnClose;

        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null)) components.Dispose();
            base.Dispose(disposing);
        }

        private void InitializeComponent()
        {
            this.lblCreatedBy = new System.Windows.Forms.Label();
            this.lblMaintainedBy = new System.Windows.Forms.Label();
            this.lblVersion = new System.Windows.Forms.Label();
            this.btnClose = new System.Windows.Forms.Button();
            this.SuspendLayout();
            // 
            // lblCreatedBy
            // 
            this.lblCreatedBy.AutoSize = true;
            this.lblCreatedBy.Font = new System.Drawing.Font("Microsoft Sans Serif", 12F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.lblCreatedBy.Location = new System.Drawing.Point(12, 18);
            this.lblCreatedBy.Name = "lblCreatedBy";
            this.lblCreatedBy.Size = new System.Drawing.Size(172, 25);
            this.lblCreatedBy.TabIndex = 1;
            this.lblCreatedBy.Text = "Created by AveYo";
            // 
            // lblMaintainedBy
            // 
            this.lblMaintainedBy.AutoSize = true;
            this.lblMaintainedBy.Font = new System.Drawing.Font("Microsoft Sans Serif", 12F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.lblMaintainedBy.Location = new System.Drawing.Point(12, 60);
            this.lblMaintainedBy.Name = "lblMaintainedBy";
            this.lblMaintainedBy.Size = new System.Drawing.Size(358, 25);
            this.lblMaintainedBy.TabIndex = 2;
            this.lblMaintainedBy.Text = "Updated and Maintained by steven4554";
            // 
            // lblVersion
            // 
            this.lblVersion.AutoSize = true;
            this.lblVersion.Font = new System.Drawing.Font("Microsoft Sans Serif", 12F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.lblVersion.Location = new System.Drawing.Point(12, 103);
            this.lblVersion.Name = "lblVersion";
            this.lblVersion.Size = new System.Drawing.Size(90, 25);
            this.lblVersion.TabIndex = 3;
            this.lblVersion.Text = "Version: ";
            // 
            // btnClose
            // 
            this.btnClose.Location = new System.Drawing.Point(441, 169);
            this.btnClose.Name = "btnClose";
            this.btnClose.Size = new System.Drawing.Size(96, 30);
            this.btnClose.TabIndex = 4;
            this.btnClose.Text = "Close";
            this.btnClose.Click += new System.EventHandler(this.BtnClose_Click);
            // 
            // AboutForm
            // 
            this.ClientSize = new System.Drawing.Size(571, 211);
            this.Controls.Add(this.lblCreatedBy);
            this.Controls.Add(this.lblMaintainedBy);
            this.Controls.Add(this.lblVersion);
            this.Controls.Add(this.btnClose);
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.Name = "AboutForm";
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
            this.Text = "About Defender CAB Verification Tool";
            this.ResumeLayout(false);
            this.PerformLayout();

        }
    }
}