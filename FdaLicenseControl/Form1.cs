using System;
using System.Drawing;
using System.Windows.Forms;
using FdaLicenseControl.Services;

namespace FdaLicenseControl
{
    public partial class Form1 : Form
    {
        private Button btnNinjaSettings;
        private Label lblStatus;

        public Form1()
        {
            InitializeComponent();
            InitializeNinjaControls();
            this.Shown += Form1_Shown;
        }

        private void InitializeNinjaControls()
        {
            var tl = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                ColumnCount = 2,
                Padding = new Padding(8)
            };
            tl.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            tl.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

            btnNinjaSettings = new Button { Text = "Paramètres NinjaOne", AutoSize = true };
            btnNinjaSettings.Click += BtnNinjaSettings_Click;
            lblStatus = new Label { Text = string.Empty, AutoSize = true, Anchor = AnchorStyles.Left | AnchorStyles.Right };

            tl.Controls.Add(btnNinjaSettings, 0, 0);
            tl.Controls.Add(lblStatus, 1, 0);

            this.Controls.Add(tl);
            UpdateStatusLabel();
        }

        private void Form1_Shown(object? sender, EventArgs e)
        {
            this.Shown -= Form1_Shown;
            if (!NinjaOneSettingsService.IsConfigured())
            {
                using var f = new NinjaOneSettingsForm();
                f.ShowDialog(this);
                UpdateStatusLabel();
            }
        }

        private void BtnNinjaSettings_Click(object? sender, EventArgs e)
        {
            using var f = new NinjaOneSettingsForm();
            f.ShowDialog(this);
            if (NinjaOneSettingsService.IsConfigured())
                lblStatus.Text = "Configuration NinjaOne enregistrée.";
            else
                lblStatus.Text = "Configuration NinjaOne incomplète.";
        }

        private void UpdateStatusLabel()
        {
            if (NinjaOneSettingsService.IsConfigured())
                lblStatus.Text = "Configuration NinjaOne enregistrée.";
            else
                lblStatus.Text = "Configuration NinjaOne incomplète.";
        }
    }
}
