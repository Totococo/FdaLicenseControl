using System;
using System.Drawing;
using System.Windows.Forms;
using FdaLicenseControl.Services;

namespace FdaLicenseControl
{
    public class NinjaOneSettingsForm : Form
    {
        private TableLayoutPanel tl;
        private TextBox txtBaseUrl;
        private TextBox txtClientId;
        private TextBox txtClientSecret;
        private Button btnCancel;
        private Button btnSave;
        private Button btnTest;

        public NinjaOneSettingsForm()
        {
            InitializeComponent();
        }

        private void InitializeComponent()
        {
            this.Text = "Paramètres NinjaOne";
            this.StartPosition = FormStartPosition.CenterParent;
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.ShowInTaskbar = false;
            this.ClientSize = new Size(650, 230);
            this.AutoScaleMode = AutoScaleMode.Dpi;

            tl = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 4,
                Padding = new Padding(12)
            };
            tl.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120));
            tl.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            tl.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
            tl.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
            tl.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
            tl.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            var lblUrl = new Label { Text = "URL NinjaOne :", Anchor = AnchorStyles.Left | AnchorStyles.Right, AutoSize = true };
            txtBaseUrl = new TextBox { Dock = DockStyle.Fill };
            var lblId = new Label { Text = "Client ID :", Anchor = AnchorStyles.Left | AnchorStyles.Right, AutoSize = true };
            txtClientId = new TextBox { Dock = DockStyle.Fill };
            var lblSecret = new Label { Text = "Client Secret :", Anchor = AnchorStyles.Left | AnchorStyles.Right, AutoSize = true };
            txtClientSecret = new TextBox { Dock = DockStyle.Fill, UseSystemPasswordChar = true };

            btnCancel = new Button { Text = "Annuler", DialogResult = DialogResult.Cancel, AutoSize = true };
            btnSave = new Button { Text = "Enregistrer", AutoSize = true };
            btnTest = new Button { Text = "Tester la connexion", AutoSize = true };

            tl.Controls.Add(lblUrl, 0, 0);
            tl.Controls.Add(txtBaseUrl, 1, 0);
            tl.Controls.Add(lblId, 0, 1);
            tl.Controls.Add(txtClientId, 1, 1);
            tl.Controls.Add(lblSecret, 0, 2);
            tl.Controls.Add(txtClientSecret, 1, 2);

            var btnPanel = new FlowLayoutPanel { FlowDirection = FlowDirection.RightToLeft, Dock = DockStyle.Fill, AutoSize = true };
            // Order of addition matters with RightToLeft: add Enregistrer (rightmost), Annuler (middle), Tester (leftmost)
            btnPanel.Controls.Add(btnSave);
            btnPanel.Controls.Add(btnCancel);
            btnPanel.Controls.Add(btnTest);
            tl.Controls.Add(btnPanel, 0, 3);
            tl.SetColumnSpan(btnPanel, 2);

            this.Controls.Add(tl);

            this.AcceptButton = btnSave;
            this.CancelButton = btnCancel;

            btnSave.Click += BtnSave_Click;
            btnCancel.Click += BtnCancel_Click;
            btnTest.Click += BtnTest_Click;
            this.Load += NinjaOneSettingsForm_Load;
        }

        private void NinjaOneSettingsForm_Load(object? sender, EventArgs e)
        {
            txtBaseUrl.Text = NinjaOneSettingsService.GetBaseUrl();
            txtClientId.Text = NinjaOneSettingsService.GetClientId();
            txtClientSecret.Text = NinjaOneSettingsService.GetClientSecret();
        }

        private void BtnSave_Click(object? sender, EventArgs e)
        {
            var baseUrl = txtBaseUrl.Text?.Trim() ?? string.Empty;
            var clientId = txtClientId.Text?.Trim() ?? string.Empty;
            var clientSecret = txtClientSecret.Text?.Trim() ?? string.Empty;

            if (string.IsNullOrEmpty(baseUrl) || string.IsNullOrEmpty(clientId) || string.IsNullOrEmpty(clientSecret))
            {
                MessageBox.Show(this, "Tous les champs doivent être remplis.", "Champs manquants", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
            {
                MessageBox.Show(this, "L’URL NinjaOne doit être une adresse HTTPS valide.", "URL invalide", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (baseUrl.EndsWith("/"))
                baseUrl = baseUrl.Substring(0, baseUrl.Length - 1);

            NinjaOneSettingsService.Save(baseUrl, clientId, clientSecret);

            this.DialogResult = DialogResult.OK;
            this.Close();
        }

        private void BtnCancel_Click(object? sender, EventArgs e)
        {
            this.DialogResult = DialogResult.Cancel;
            this.Close();
        }

        private async void BtnTest_Click(object? sender, EventArgs e)
        {
            var baseUrl = txtBaseUrl.Text?.Trim() ?? string.Empty;
            var clientId = txtClientId.Text?.Trim() ?? string.Empty;
            var clientSecret = txtClientSecret.Text?.Trim() ?? string.Empty;

            if (string.IsNullOrEmpty(baseUrl) || string.IsNullOrEmpty(clientId) || string.IsNullOrEmpty(clientSecret))
            {
                MessageBox.Show(this, "Tous les champs doivent être remplis.", "Champs manquants", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
            {
                MessageBox.Show(this, "L’URL NinjaOne doit être une adresse HTTPS valide.", "URL invalide", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            // Disable buttons and show wait cursor
            btnTest.Enabled = false;
            btnCancel.Enabled = false;
            btnSave.Enabled = false;
            var previousCursor = this.UseWaitCursor;
            this.UseWaitCursor = true;
            var previousText = btnTest.Text;
            btnTest.Text = "Test en cours...";

            try
            {
                var service = new Services.NinjaOneAuthenticationService();
                await service.TestConnectionAsync(baseUrl, clientId, clientSecret);
                MessageBox.Show(this, "Connexion à NinjaOne réussie.", "Test réussi", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "Échec du test", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                btnTest.Enabled = true;
                btnCancel.Enabled = true;
                btnSave.Enabled = true;
                this.UseWaitCursor = previousCursor;
                btnTest.Text = previousText;
            }
        }
    }
}
