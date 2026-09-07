using System;
using System.Drawing;
using System.Windows.Forms;

namespace Defender_Cab_Verification_Tool
{
    public partial class CleanupForm : Form
    {
        private Label _lbl;
        private ProgressBar _bar;

        public CleanupForm()
        {
            InitializeComponent();
        }

        private void InitializeComponent()
        {
            this.StartPosition = FormStartPosition.CenterParent;
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.ShowInTaskbar = false;
            this.ControlBox = false;
            this.ClientSize = new Size(420, 96);
            this.Text = "Cleaning";

            _lbl = new Label
            {
                AutoSize = false,
                Text = "Cleaning extracted files and folders...\r\nThis may take a moment.",
                TextAlign = ContentAlignment.MiddleLeft,
                Dock = DockStyle.Top,
                Height = 56,
                Padding = new Padding(12, 12, 12, 0)
            };

            _bar = new ProgressBar
            {
                Style = ProgressBarStyle.Marquee,
                MarqueeAnimationSpeed = 30,
                Dock = DockStyle.Bottom,
                Height = 20
            };

            this.Controls.Add(_lbl);
            this.Controls.Add(_bar);
        }
    }
}