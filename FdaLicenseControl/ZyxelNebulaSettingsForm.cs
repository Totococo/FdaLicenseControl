using System;
using System.Drawing;
using System.Windows.Forms;
using FdaLicenseControl.Services;
using FdaLicenseControl.Models;

namespace FdaLicenseControl
{
    public class ZyxelNebulaSettingsForm : Form
    {
        private TableLayoutPanel tl;
        private TextBox txtBaseUrl;
        private TextBox txtApiKey;
        private Button btnTest;
        private Button btnImport;
        private Button btnExport;
        private Button btnCancel;
        private Button btnSave;

        public ZyxelNebulaSettingsForm()
        {
            InitializeComponent();
        }

        private void InitializeComponent()
        {
            this.Text = "Paramètres Zyxel Nebula";
            this.StartPosition = FormStartPosition.CenterParent;
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.ShowInTaskbar = false;
            this.AutoScaleMode = AutoScaleMode.Dpi;
            this.ClientSize = new Size(700, 230);

            tl = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 3, Padding = new Padding(12) };
            tl.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 160));
            tl.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            tl.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
            tl.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
            tl.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            var lblUrl = new Label { Text = "URL de l’API Nebula :", Anchor = AnchorStyles.Left | AnchorStyles.Right, AutoSize = true };
            txtBaseUrl = new TextBox { Dock = DockStyle.Fill };
            var lblKey = new Label { Text = "Clé API Nebula :", Anchor = AnchorStyles.Left | AnchorStyles.Right, AutoSize = true };
            txtApiKey = new TextBox { Dock = DockStyle.Fill, UseSystemPasswordChar = true };

            tl.Controls.Add(lblUrl, 0, 0);
            tl.Controls.Add(txtBaseUrl, 1, 0);
            tl.Controls.Add(lblKey, 0, 1);
            tl.Controls.Add(txtApiKey, 1, 1);

            var btnPanel = new FlowLayoutPanel { FlowDirection = FlowDirection.RightToLeft, Dock = DockStyle.Fill, AutoSize = true };
            btnTest = new Button { Text = "Tester la connexion", AutoSize = true };
            btnImport = new Button { Text = "Importer...", AutoSize = true };
            btnExport = new Button { Text = "Exporter...", AutoSize = true };
            btnCancel = new Button { Text = "Annuler", DialogResult = DialogResult.Cancel, AutoSize = true };
            btnSave = new Button { Text = "Enregistrer", AutoSize = true };

            // add in order: save, cancel, export, import, test (RightToLeft)
            btnPanel.Controls.Add(btnSave);
            btnPanel.Controls.Add(btnCancel);
            btnPanel.Controls.Add(btnExport);
            btnPanel.Controls.Add(btnImport);
            btnPanel.Controls.Add(btnTest);

            tl.Controls.Add(btnPanel, 0, 2);
            tl.SetColumnSpan(btnPanel, 2);

            this.Controls.Add(tl);

            this.AcceptButton = btnSave;
            this.CancelButton = btnCancel;

            this.Load += ZyxelNebulaSettingsForm_Load;
            btnTest.Click += BtnTest_Click;
            btnImport.Click += BtnImport_Click;
            btnExport.Click += BtnExport_Click;
            btnSave.Click += BtnSave_Click;
            btnCancel.Click += BtnCancel_Click;
        }

        private void ZyxelNebulaSettingsForm_Load(object? sender, EventArgs e)
        {
            try
            {
                txtBaseUrl.Text = ZyxelNebulaSettingsService.GetBaseUrl();
                txtApiKey.Text = ZyxelNebulaSettingsService.GetApiKey();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "Erreur lecture configuration Zyxel", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void BtnSave_Click(object? sender, EventArgs e)
        {
            var baseUrl = txtBaseUrl.Text?.Trim() ?? string.Empty;
            var apiKey = txtApiKey.Text?.Trim() ?? string.Empty;

            if (baseUrl.EndsWith("/"))
                baseUrl = baseUrl.Substring(0, baseUrl.Length - 1);

            if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
            {
                MessageBox.Show(this, "L’URL de l’API Zyxel Nebula doit être une adresse HTTPS valide.", "URL invalide", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (string.IsNullOrEmpty(apiKey))
            {
                MessageBox.Show(this, "La clé API Zyxel Nebula est obligatoire.", "Clé manquante", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            try
            {
                ZyxelNebulaSettingsService.Save(baseUrl, apiKey);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "Erreur sauvegarde Zyxel", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

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
            var apiKey = txtApiKey.Text?.Trim() ?? string.Empty;

            if (baseUrl.EndsWith("/"))
                baseUrl = baseUrl.Substring(0, baseUrl.Length - 1);

            if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
            {
                MessageBox.Show(this, "L’URL de l’API Zyxel Nebula doit être une adresse HTTPS valide.", "URL invalide", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (string.IsNullOrEmpty(apiKey))
            {
                MessageBox.Show(this, "La clé API Zyxel Nebula est obligatoire.", "Clé manquante", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            // disable buttons and show wait cursor
            btnTest.Enabled = false;
            btnImport.Enabled = false;
            btnExport.Enabled = false;
            btnSave.Enabled = false;
            btnCancel.Enabled = false;
            var prevCursor = this.UseWaitCursor;
            this.UseWaitCursor = true;
            var prevText = btnTest.Text;
            btnTest.Text = "Test en cours...";

            try
            {
                var service = new ZyxelNebulaApiService();
                var count = await service.TestConnectionAsync(baseUrl, apiKey);
                MessageBox.Show(this, $"Connexion à Zyxel Nebula réussie.\n\n{count} organisation(s) accessible(s).", "Test réussi", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "Échec du test", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                btnTest.Text = prevText;
                this.UseWaitCursor = prevCursor;
                btnTest.Enabled = true;
                btnImport.Enabled = true;
                btnExport.Enabled = true;
                btnSave.Enabled = true;
                btnCancel.Enabled = true;
            }
        }

        private void BtnImport_Click(object? sender, EventArgs e)
        {
            using var dlg = new OpenFileDialog();
            dlg.Filter = "Configuration Zyxel FDA (*.fdazyxelconfig.json)|*.fdazyxelconfig.json|Fichiers JSON (*.json)|*.json|Tous les fichiers (*.*)|*.*";
            if (dlg.ShowDialog(this) != DialogResult.OK)
                return;

            try
            {
                var s = ZyxelNebulaSettingsService.LoadFromFile(dlg.FileName);
                txtBaseUrl.Text = s.BaseUrl;
                txtApiKey.Text = s.ApiKey;
                MessageBox.Show(this, "Configuration chargée. Cliquez sur Enregistrer pour l’appliquer.", "Import terminé", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "Erreur import", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void BtnExport_Click(object? sender, EventArgs e)
        {
            var baseUrl = txtBaseUrl.Text?.Trim() ?? string.Empty;
            var apiKey = txtApiKey.Text?.Trim() ?? string.Empty;

            if (baseUrl.EndsWith("/"))
                baseUrl = baseUrl.Substring(0, baseUrl.Length - 1);

            if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
            {
                MessageBox.Show(this, "L’URL de l’API Zyxel Nebula doit être une adresse HTTPS valide.", "URL invalide", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (string.IsNullOrEmpty(apiKey))
            {
                MessageBox.Show(this, "La clé API Zyxel Nebula est obligatoire.", "Clé manquante", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            var confirm = MessageBox.Show(this,
                "Le fichier exporté contiendra la clé API Zyxel Nebula.\n\nCe fichier doit être conservé dans un emplacement sécurisé.\n\nVoulez-vous continuer ?",
                "Exporter la configuration Zyxel Nebula",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button2);

            if (confirm != DialogResult.Yes)
                return;

            using var dlg = new SaveFileDialog();
            dlg.FileName = "FdaLicenseControl-Zyxel.fdazyxelconfig.json";
            dlg.Filter = "Configuration Zyxel FDA (*.fdazyxelconfig.json)|*.fdazyxelconfig.json|Fichiers JSON (*.json)|*.json|Tous les fichiers (*.*)|*.*";
            if (dlg.ShowDialog(this) != DialogResult.OK)
                return;

            try
            {
                ZyxelNebulaSettingsService.ExportToFile(dlg.FileName, baseUrl, apiKey);
                MessageBox.Show(this, $"Fichier exporté :\n{dlg.FileName}", "Export terminé", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "Erreur export", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }
}
