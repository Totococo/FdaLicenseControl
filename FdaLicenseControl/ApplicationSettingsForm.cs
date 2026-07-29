using System;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;
using FdaLicenseControl.Services;
using FdaLicenseControl.Models;

namespace FdaLicenseControl
{
    public class ApplicationSettingsForm : Form
    {
        private TableLayoutPanel mainLayout;
        private TabControl tabControl;

        // NinjaOne controls
        private TextBox txtNinjaBaseUrl;
        private TextBox txtNinjaClientId;
        private TextBox txtNinjaClientSecret;
        private Button btnNinjaTest;

        // Zyxel controls
        private TextBox txtZyxelBaseUrl;
        private TextBox txtZyxelApiKey;
        private Button btnZyxelTest;

        // Microsoft controls
        private TextBox txtMicrosoftPartnerTenantId;
        private TextBox txtMicrosoftClientId;
        private TextBox txtMicrosoftBaseUrl;
        private Button btnMicrosoftTest;

        // Global buttons
        private Button btnImportAll;
        private Button btnExportAll;
        private Button btnCancel;
        private Button btnSave;

        public ApplicationSettingsForm()
        {
            InitializeComponent();
        }

        private void InitializeComponent()
        {
            this.Text = "Paramètres des API";
            this.StartPosition = FormStartPosition.CenterParent;
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.ShowInTaskbar = false;
            this.AutoScaleMode = AutoScaleMode.Dpi;
            this.ClientSize = new Size(800, 420);

            mainLayout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1, Padding = new Padding(8) };
            mainLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            mainLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            tabControl = new TabControl { Dock = DockStyle.Fill };
            var tabNinja = new TabPage("NinjaOne");
            var tabZyxel = new TabPage("Zyxel Nebula");
            var tabMicrosoft = new TabPage("Microsoft 365");

            BuildNinjaTab(tabNinja);
            BuildZyxelTab(tabZyxel);
            BuildMicrosoftTab(tabMicrosoft);

            tabControl.TabPages.Add(tabNinja);
            tabControl.TabPages.Add(tabZyxel);
            tabControl.TabPages.Add(tabMicrosoft);

            // Bottom buttons: Import, Export, Cancel, Save
            var btnPanel = new FlowLayoutPanel { FlowDirection = FlowDirection.LeftToRight, Dock = DockStyle.Fill, AutoSize = true };
            btnImportAll = new Button { Text = "Importer la configuration...", AutoSize = true };
            btnExportAll = new Button { Text = "Exporter la configuration...", AutoSize = true };
            btnSave = new Button { Text = "Enregistrer", AutoSize = true };
            btnCancel = new Button { Text = "Annuler", DialogResult = DialogResult.Cancel, AutoSize = true };
            btnPanel.Controls.Add(btnImportAll);
            btnPanel.Controls.Add(btnExportAll);
            btnPanel.Controls.Add(btnCancel);
            btnPanel.Controls.Add(btnSave);

            mainLayout.Controls.Add(tabControl, 0, 0);
            mainLayout.Controls.Add(btnPanel, 0, 1);
            this.Controls.Add(mainLayout);

            this.AcceptButton = btnSave;
            this.CancelButton = btnCancel;

            this.Load += ApplicationSettingsForm_Load;
            btnSave.Click += BtnSave_Click;
            btnCancel.Click += BtnCancel_Click;
            btnImportAll.Click += BtnImportAll_Click;
            btnExportAll.Click += BtnExportAll_Click;
            // Ensure global import/export events are handled
        }

        private void BuildNinjaTab(TabPage tab)
        {
            var tl = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 4, Padding = new Padding(12) };
            tl.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 140));
            tl.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            tl.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
            tl.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
            tl.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
            tl.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            var lblUrl = new Label { Text = "URL NinjaOne :", Anchor = AnchorStyles.Left | AnchorStyles.Right, AutoSize = true };
            txtNinjaBaseUrl = new TextBox { Dock = DockStyle.Fill };
            var lblId = new Label { Text = "Client ID :", Anchor = AnchorStyles.Left | AnchorStyles.Right, AutoSize = true };
            txtNinjaClientId = new TextBox { Dock = DockStyle.Fill };
            var lblSecret = new Label { Text = "Client Secret :", Anchor = AnchorStyles.Left | AnchorStyles.Right, AutoSize = true };
            txtNinjaClientSecret = new TextBox { Dock = DockStyle.Fill, UseSystemPasswordChar = true };

            tl.Controls.Add(lblUrl, 0, 0);
            tl.Controls.Add(txtNinjaBaseUrl, 1, 0);
            tl.Controls.Add(lblId, 0, 1);
            tl.Controls.Add(txtNinjaClientId, 1, 1);
            tl.Controls.Add(lblSecret, 0, 2);
            tl.Controls.Add(txtNinjaClientSecret, 1, 2);

            var btnPanel = new FlowLayoutPanel { FlowDirection = FlowDirection.RightToLeft, Dock = DockStyle.Fill, AutoSize = true };
            btnNinjaTest = new Button { Text = "Tester la connexion", AutoSize = true };
            btnPanel.Controls.Add(btnNinjaTest);
            tl.Controls.Add(btnPanel, 0, 3);
            tl.SetColumnSpan(btnPanel, 2);

            tab.Controls.Add(tl);

            btnNinjaTest.Click += BtnNinjaTest_Click;
            // import/export handled globally
        }

        private void BuildZyxelTab(TabPage tab)
        {
            var tl = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 3, Padding = new Padding(12) };
            tl.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 160));
            tl.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            tl.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
            tl.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
            tl.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            var lblUrl = new Label { Text = "URL de l’API Nebula :", Anchor = AnchorStyles.Left | AnchorStyles.Right, AutoSize = true };
            txtZyxelBaseUrl = new TextBox { Dock = DockStyle.Fill };
            var lblKey = new Label { Text = "Clé API Nebula :", Anchor = AnchorStyles.Left | AnchorStyles.Right, AutoSize = true };
            txtZyxelApiKey = new TextBox { Dock = DockStyle.Fill, UseSystemPasswordChar = true };

            tl.Controls.Add(lblUrl, 0, 0);
            tl.Controls.Add(txtZyxelBaseUrl, 1, 0);
            tl.Controls.Add(lblKey, 0, 1);
            tl.Controls.Add(txtZyxelApiKey, 1, 1);

            var btnPanel = new FlowLayoutPanel { FlowDirection = FlowDirection.RightToLeft, Dock = DockStyle.Fill, AutoSize = true };
            btnZyxelTest = new Button { Text = "Tester la connexion", AutoSize = true };
            btnPanel.Controls.Add(btnZyxelTest);
            tl.Controls.Add(btnPanel, 0, 2);
            tl.SetColumnSpan(btnPanel, 2);

            tab.Controls.Add(tl);

            btnZyxelTest.Click += BtnZyxelTest_Click;
            // import/export handled globally
        }

        private void BuildMicrosoftTab(TabPage tab)
        {
            var tl = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 4, Padding = new Padding(12) };
            tl.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 180));
            tl.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            tl.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
            tl.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
            tl.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
            tl.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            var lblTenantId = new Label { Text = "Tenant partenaire :", Anchor = AnchorStyles.Left | AnchorStyles.Right, AutoSize = true };
            txtMicrosoftPartnerTenantId = new TextBox { Dock = DockStyle.Fill };
            var lblClientId = new Label { Text = "ID de l'application :", Anchor = AnchorStyles.Left | AnchorStyles.Right, AutoSize = true };
            txtMicrosoftClientId = new TextBox { Dock = DockStyle.Fill };
            var lblUrl = new Label { Text = "URL Partner Center :", Anchor = AnchorStyles.Left | AnchorStyles.Right, AutoSize = true };
            txtMicrosoftBaseUrl = new TextBox { Dock = DockStyle.Fill };

            tl.Controls.Add(lblTenantId, 0, 0);
            tl.Controls.Add(txtMicrosoftPartnerTenantId, 1, 0);
            tl.Controls.Add(lblClientId, 0, 1);
            tl.Controls.Add(txtMicrosoftClientId, 1, 1);
            tl.Controls.Add(lblUrl, 0, 2);
            tl.Controls.Add(txtMicrosoftBaseUrl, 1, 2);

            var btnPanel = new FlowLayoutPanel { FlowDirection = FlowDirection.RightToLeft, Dock = DockStyle.Fill, AutoSize = true };
            btnMicrosoftTest = new Button { Text = "Tester la connexion", AutoSize = true };
            btnPanel.Controls.Add(btnMicrosoftTest);
            tl.Controls.Add(btnPanel, 0, 3);
            tl.SetColumnSpan(btnPanel, 2);

            tab.Controls.Add(tl);

            btnMicrosoftTest.Click += BtnMicrosoftTest_Click;
        }

        private void ApplicationSettingsForm_Load(object? sender, EventArgs e)
        {
            // Load existing values
            try
            {
                txtNinjaBaseUrl.Text = NinjaOneSettingsService.GetBaseUrl();
                txtNinjaClientId.Text = NinjaOneSettingsService.GetClientId();
                txtNinjaClientSecret.Text = NinjaOneSettingsService.GetClientSecret();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "Erreur lecture configuration NinjaOne", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }

            try
            {
                txtZyxelBaseUrl.Text = ZyxelNebulaSettingsService.GetBaseUrl();
                txtZyxelApiKey.Text = ZyxelNebulaSettingsService.GetApiKey();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "Erreur lecture configuration Zyxel", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }

            try
            {
                txtMicrosoftPartnerTenantId.Text = MicrosoftPartnerSettingsService.GetPartnerTenantId();
                txtMicrosoftClientId.Text = MicrosoftPartnerSettingsService.GetClientId();
                txtMicrosoftBaseUrl.Text = MicrosoftPartnerSettingsService.GetBaseUrl();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "Erreur lecture configuration Microsoft", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void BtnCancel_Click(object? sender, EventArgs e)
        {
            this.DialogResult = DialogResult.Cancel;
            this.Close();
        }

        private void BtnSave_Click(object? sender, EventArgs e)
        {
            // Gather and validate NinjaOne
            var ninjaBaseUrl = txtNinjaBaseUrl.Text?.Trim() ?? string.Empty;
            var ninjaClientId = txtNinjaClientId.Text?.Trim() ?? string.Empty;
            var ninjaClientSecret = txtNinjaClientSecret.Text?.Trim() ?? string.Empty;
            if (ninjaBaseUrl.EndsWith("/"))
                ninjaBaseUrl = ninjaBaseUrl.Substring(0, ninjaBaseUrl.Length - 1);

            if (string.IsNullOrEmpty(ninjaBaseUrl) || string.IsNullOrEmpty(ninjaClientId) || string.IsNullOrEmpty(ninjaClientSecret))
            {
                tabControl.SelectedIndex = 0;
                MessageBox.Show(this, "La configuration NinjaOne est incomplète.", "Validation", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            if (!Uri.TryCreate(ninjaBaseUrl, UriKind.Absolute, out var nUri) || nUri.Scheme != Uri.UriSchemeHttps)
            {
                tabControl.SelectedIndex = 0;
                MessageBox.Show(this, "L’URL NinjaOne doit être une adresse HTTPS valide.", "Validation", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            // Gather and validate Zyxel
            var zyxelBaseUrl = txtZyxelBaseUrl.Text?.Trim() ?? string.Empty;
            var zyxelApiKey = txtZyxelApiKey.Text?.Trim() ?? string.Empty;
            if (zyxelBaseUrl.EndsWith("/"))
                zyxelBaseUrl = zyxelBaseUrl.Substring(0, zyxelBaseUrl.Length - 1);

            if (string.IsNullOrEmpty(zyxelBaseUrl) || string.IsNullOrEmpty(zyxelApiKey))
            {
                tabControl.SelectedIndex = 1;
                MessageBox.Show(this, "La configuration Zyxel Nebula est incomplète.", "Validation", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            if (!Uri.TryCreate(zyxelBaseUrl, UriKind.Absolute, out var zUri) || zUri.Scheme != Uri.UriSchemeHttps)
            {
                tabControl.SelectedIndex = 1;
                MessageBox.Show(this, "L’URL de l’API Zyxel Nebula doit être une adresse HTTPS valide.", "Validation", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            // Gather and validate Microsoft (optional)
            var msPartnerTenantId = txtMicrosoftPartnerTenantId.Text?.Trim() ?? string.Empty;
            var msClientId = txtMicrosoftClientId.Text?.Trim() ?? string.Empty;
            var msBaseUrl = txtMicrosoftBaseUrl.Text?.Trim() ?? string.Empty;
            if (msBaseUrl.EndsWith("/"))
                msBaseUrl = msBaseUrl.Substring(0, msBaseUrl.Length - 1);

            if (string.IsNullOrEmpty(msBaseUrl))
                msBaseUrl = "https://api.partnercenter.microsoft.com";

            if (!string.IsNullOrEmpty(msPartnerTenantId) || !string.IsNullOrEmpty(msClientId))
            {
                if (string.IsNullOrEmpty(msPartnerTenantId) || string.IsNullOrEmpty(msClientId))
                {
                    tabControl.SelectedIndex = 2;
                    MessageBox.Show(this, "La configuration Microsoft 365 est incomplète.", "Validation", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                if (!Guid.TryParse(msPartnerTenantId, out var msTenantGuid) || msTenantGuid == Guid.Empty)
                {
                    tabControl.SelectedIndex = 2;
                    MessageBox.Show(this, "L'identifiant du tenant partenaire Microsoft n'est pas valide.", "Validation", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                if (!Guid.TryParse(msClientId, out var msClientGuid) || msClientGuid == Guid.Empty)
                {
                    tabControl.SelectedIndex = 2;
                    MessageBox.Show(this, "L'identifiant de l'application Microsoft n'est pas valide.", "Validation", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                if (!Uri.TryCreate(msBaseUrl, UriKind.Absolute, out var msUri) || msUri.Scheme != Uri.UriSchemeHttps)
                {
                    tabControl.SelectedIndex = 2;
                    MessageBox.Show(this, "L'URL Partner Center doit être une adresse HTTPS valide.", "Validation", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
            }

            // All good; save all
            try
            {
                NinjaOneSettingsService.Save(ninjaBaseUrl, ninjaClientId, ninjaClientSecret);
                ZyxelNebulaSettingsService.Save(zyxelBaseUrl, zyxelApiKey);
                MicrosoftPartnerSettingsService.Save(msPartnerTenantId, msClientId, msBaseUrl);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "Erreur sauvegarde", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            this.DialogResult = DialogResult.OK;
            this.Close();
        }

        private void BtnImportAll_Click(object? sender, EventArgs e)
        {
            using var dlg = new OpenFileDialog();
            dlg.Filter = "Configuration FDA (*.fdaconfig.json)|*.fdaconfig.json|Fichiers JSON (*.json)|*.json|Tous les fichiers (*.*)|*.*";
            if (dlg.ShowDialog(this) != DialogResult.OK)
                return;

            try
            {
                var cfg = FdaApplicationConfigurationService.LoadFromFile(dlg.FileName);
                // If successful, populate fields
                txtNinjaBaseUrl.Text = cfg.NinjaOne.BaseUrl;
                txtNinjaClientId.Text = cfg.NinjaOne.ClientId;
                txtNinjaClientSecret.Text = cfg.NinjaOne.ClientSecret;
                txtZyxelBaseUrl.Text = cfg.ZyxelNebula.BaseUrl;
                txtZyxelApiKey.Text = cfg.ZyxelNebula.ApiKey;
                txtMicrosoftPartnerTenantId.Text = cfg.MicrosoftPartner?.PartnerTenantId ?? string.Empty;
                txtMicrosoftClientId.Text = cfg.MicrosoftPartner?.ClientId ?? string.Empty;
                txtMicrosoftBaseUrl.Text = cfg.MicrosoftPartner?.BaseUrl ?? "https://api.partnercenter.microsoft.com";
                tabControl.SelectedIndex = 0;
                MessageBox.Show(this, "La configuration complète a été chargée.\n\nCliquez sur Enregistrer pour l’appliquer.", "Configuration importée", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "Erreur d'importation", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void BtnExportAll_Click(object? sender, EventArgs e)
        {
            // Gather current values
            var ninjaBaseUrl = txtNinjaBaseUrl.Text?.Trim() ?? string.Empty;
            var ninjaClientId = txtNinjaClientId.Text?.Trim() ?? string.Empty;
            var ninjaClientSecret = txtNinjaClientSecret.Text?.Trim() ?? string.Empty;
            if (ninjaBaseUrl.EndsWith("/")) ninjaBaseUrl = ninjaBaseUrl.Substring(0, ninjaBaseUrl.Length - 1);

            var zyxelBaseUrl = txtZyxelBaseUrl.Text?.Trim() ?? string.Empty;
            var zyxelApiKey = txtZyxelApiKey.Text?.Trim() ?? string.Empty;
            if (zyxelBaseUrl.EndsWith("/")) zyxelBaseUrl = zyxelBaseUrl.Substring(0, zyxelBaseUrl.Length - 1);

            var msPartnerTenantId = txtMicrosoftPartnerTenantId.Text?.Trim() ?? string.Empty;
            var msClientId = txtMicrosoftClientId.Text?.Trim() ?? string.Empty;
            var msBaseUrl = txtMicrosoftBaseUrl.Text?.Trim() ?? string.Empty;
            if (msBaseUrl.EndsWith("/")) msBaseUrl = msBaseUrl.Substring(0, msBaseUrl.Length - 1);
            if (string.IsNullOrEmpty(msBaseUrl)) msBaseUrl = "https://api.partnercenter.microsoft.com";

            // Validate ninja
            if (string.IsNullOrEmpty(ninjaBaseUrl) || string.IsNullOrEmpty(ninjaClientId) || string.IsNullOrEmpty(ninjaClientSecret))
            {
                tabControl.SelectedIndex = 0;
                MessageBox.Show(this, "La configuration NinjaOne est incomplète.", "Validation", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            if (!Uri.TryCreate(ninjaBaseUrl, UriKind.Absolute, out var nuri) || nuri.Scheme != Uri.UriSchemeHttps)
            {
                tabControl.SelectedIndex = 0;
                MessageBox.Show(this, "L’URL NinjaOne doit être une adresse HTTPS valide.", "Validation", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            // Validate zyxel
            if (string.IsNullOrEmpty(zyxelBaseUrl) || string.IsNullOrEmpty(zyxelApiKey))
            {
                tabControl.SelectedIndex = 1;
                MessageBox.Show(this, "La configuration Zyxel Nebula est incomplète.", "Validation", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            if (!Uri.TryCreate(zyxelBaseUrl, UriKind.Absolute, out var zuri) || zuri.Scheme != Uri.UriSchemeHttps)
            {
                tabControl.SelectedIndex = 1;
                MessageBox.Show(this, "L’URL de l’API Zyxel Nebula doit être une adresse HTTPS valide.", "Validation", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (!string.IsNullOrEmpty(msPartnerTenantId) || !string.IsNullOrEmpty(msClientId))
            {
                if (string.IsNullOrEmpty(msPartnerTenantId) || string.IsNullOrEmpty(msClientId))
                {
                    tabControl.SelectedIndex = 2;
                    MessageBox.Show(this, "La configuration Microsoft 365 est incomplète.", "Validation", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                if (!Guid.TryParse(msPartnerTenantId, out var msTenantGuid) || msTenantGuid == Guid.Empty)
                {
                    tabControl.SelectedIndex = 2;
                    MessageBox.Show(this, "L'identifiant du tenant partenaire Microsoft n'est pas valide.", "Validation", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                if (!Guid.TryParse(msClientId, out var msClientGuid) || msClientGuid == Guid.Empty)
                {
                    tabControl.SelectedIndex = 2;
                    MessageBox.Show(this, "L'identifiant de l'application Microsoft n'est pas valide.", "Validation", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                if (!Uri.TryCreate(msBaseUrl, UriKind.Absolute, out var msUri) || msUri.Scheme != Uri.UriSchemeHttps)
                {
                    tabControl.SelectedIndex = 2;
                    MessageBox.Show(this, "L'URL Partner Center doit être une adresse HTTPS valide.", "Validation", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
            }

            var confirm = MessageBox.Show(this,
                "Le fichier exporté contiendra les secrets NinjaOne et Zyxel Nebula.\n\nCe fichier doit être conservé dans un emplacement sécurisé.\n\nVoulez-vous continuer ?",
                "Exporter la configuration complète",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button2);

            if (confirm != DialogResult.Yes)
                return;

            using var dlg = new SaveFileDialog();
            dlg.FileName = "FdaLicenseControl.fdaconfig.json";
            dlg.Filter = "Configuration FDA (*.fdaconfig.json)|*.fdaconfig.json|Fichiers JSON (*.json)|*.json";
            if (dlg.ShowDialog(this) != DialogResult.OK)
                return;

            var cfg = new FdaApplicationConfiguration
            {
                NinjaOne = new NinjaOneSettings { BaseUrl = ninjaBaseUrl, ClientId = ninjaClientId, ClientSecret = ninjaClientSecret },
                ZyxelNebula = new ZyxelNebulaSettings { BaseUrl = zyxelBaseUrl, ApiKey = zyxelApiKey },
                MicrosoftPartner = new MicrosoftPartnerSettings { PartnerTenantId = msPartnerTenantId, ClientId = msClientId, BaseUrl = msBaseUrl }
            };

            try
            {
                FdaApplicationConfigurationService.ExportToFile(dlg.FileName, cfg);
                MessageBox.Show(this, $"La configuration complète a été exportée.\n\nFichier :\n{dlg.FileName}", "Export terminé", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "Erreur export", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private async void BtnNinjaTest_Click(object? sender, EventArgs e)
        {
            var baseUrl = txtNinjaBaseUrl.Text?.Trim() ?? string.Empty;
            var clientId = txtNinjaClientId.Text?.Trim() ?? string.Empty;
            var clientSecret = txtNinjaClientSecret.Text?.Trim() ?? string.Empty;

            if (baseUrl.EndsWith("/"))
                baseUrl = baseUrl.Substring(0, baseUrl.Length - 1);

            if (string.IsNullOrEmpty(baseUrl) || string.IsNullOrEmpty(clientId) || string.IsNullOrEmpty(clientSecret))
            {
                MessageBox.Show(this, "La configuration NinjaOne est incomplète.", "Validation", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
            {
                MessageBox.Show(this, "L’URL NinjaOne doit être une adresse HTTPS valide.", "Validation", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            // disable both tabs buttons and global buttons
            SetAllButtonsEnabled(false);
            var prevCursor = this.UseWaitCursor;
            this.UseWaitCursor = true;
            var prevText = btnNinjaTest.Text;
            btnNinjaTest.Text = "Test en cours...";

            try
            {
                var service = new NinjaOneAuthenticationService();
                await service.TestConnectionAsync(baseUrl, clientId, clientSecret);
                MessageBox.Show(this, "Connexion à NinjaOne réussie.", "Test NinjaOne réussi", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "Échec du test NinjaOne", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                btnNinjaTest.Text = prevText;
                this.UseWaitCursor = prevCursor;
                SetAllButtonsEnabled(true);
            }
        }

        private async void BtnMicrosoftTest_Click(object? sender, EventArgs e)
        {
            var partnerTenantId = txtMicrosoftPartnerTenantId.Text?.Trim() ?? string.Empty;
            var clientId = txtMicrosoftClientId.Text?.Trim() ?? string.Empty;
            var baseUrl = txtMicrosoftBaseUrl.Text?.Trim() ?? string.Empty;

            if (baseUrl.EndsWith("/"))
                baseUrl = baseUrl.Substring(0, baseUrl.Length - 1);

            if (string.IsNullOrEmpty(baseUrl))
                baseUrl = "https://api.partnercenter.microsoft.com";

            if (string.IsNullOrEmpty(partnerTenantId) || string.IsNullOrEmpty(clientId))
            {
                tabControl.SelectedIndex = 2;
                MessageBox.Show(this, "La configuration Microsoft 365 est incomplète.", "Validation", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (!Guid.TryParse(partnerTenantId, out var tenantGuid) || tenantGuid == Guid.Empty)
            {
                tabControl.SelectedIndex = 2;
                MessageBox.Show(this, "L'identifiant du tenant partenaire Microsoft n'est pas valide.", "Validation", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (!Guid.TryParse(clientId, out var appGuid) || appGuid == Guid.Empty)
            {
                tabControl.SelectedIndex = 2;
                MessageBox.Show(this, "L'identifiant de l'application Microsoft n'est pas valide.", "Validation", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
            {
                tabControl.SelectedIndex = 2;
                MessageBox.Show(this, "L'URL Partner Center doit être une adresse HTTPS valide.", "Validation", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            SetAllButtonsEnabled(false);
            var previousCursor = this.UseWaitCursor;
            this.UseWaitCursor = true;
            var previousText = btnMicrosoftTest.Text;
            btnMicrosoftTest.Text = "Connexion en cours...";

            try
            {
                var service = new MicrosoftPartnerAuthenticationService();
                var result = await service.TestConnectionAsync(partnerTenantId, clientId, baseUrl, this.Handle, CancellationToken.None);

                var complianceText = result.IsMfaCompliant == true ? "Oui" : "Non vérifiable";
                MessageBox.Show(this,
                    $"Connexion à Microsoft Partner Center réussie.\n\nCompte : {result.AccountName}\n\n{result.CustomerCount} client(s) accessible(s).\n\nConformité MFA : {complianceText}",
                    "Test Microsoft 365 réussi",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "Échec du test Microsoft 365", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                btnMicrosoftTest.Text = previousText;
                this.UseWaitCursor = false;
                Cursor = Cursors.Default;
                SetAllButtonsEnabled(true);
            }
        }

        private async void BtnZyxelTest_Click(object? sender, EventArgs e)
        {
            var baseUrl = txtZyxelBaseUrl.Text?.Trim() ?? string.Empty;
            var apiKey = txtZyxelApiKey.Text?.Trim() ?? string.Empty;

            if (baseUrl.EndsWith("/"))
                baseUrl = baseUrl.Substring(0, baseUrl.Length - 1);

            if (string.IsNullOrEmpty(baseUrl) || string.IsNullOrEmpty(apiKey))
            {
                MessageBox.Show(this, "La configuration Zyxel Nebula est incomplète.", "Validation", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
            {
                MessageBox.Show(this, "L’URL de l’API Zyxel Nebula doit être une adresse HTTPS valide.", "Validation", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            SetAllButtonsEnabled(false);
            var prevCursor = this.UseWaitCursor;
            this.UseWaitCursor = true;
            var prevText = btnZyxelTest.Text;
            btnZyxelTest.Text = "Test en cours...";

            try
            {
                var service = new ZyxelNebulaApiService();
                var count = await service.TestConnectionAsync(baseUrl, apiKey);
                MessageBox.Show(this, $"Connexion à Zyxel Nebula réussie.\n\n{count} organisation(s) accessible(s).", "Test Zyxel Nebula réussi", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "Échec du test Zyxel Nebula", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                btnZyxelTest.Text = prevText;
                this.UseWaitCursor = prevCursor;
                SetAllButtonsEnabled(true);
            }
        }

        private void SetAllButtonsEnabled(bool enabled)
        {
            btnNinjaTest.Enabled = enabled;
            btnZyxelTest.Enabled = enabled;
            btnMicrosoftTest.Enabled = enabled;
            if (btnImportAll != null) btnImportAll.Enabled = enabled;
            if (btnExportAll != null) btnExportAll.Enabled = enabled;
            btnSave.Enabled = enabled;
            btnCancel.Enabled = enabled;
        }

        // per-tab import/export removed; use global import/export instead

        // per-tab import/export removed; use global import/export instead
    }
}
