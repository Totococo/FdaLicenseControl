using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using FdaLicenseControl.Services;
using FdaLicenseControl.Models;

namespace FdaLicenseControl
{
    public partial class Form1 : Form
    {
        private Button btnNinjaSettings;
        private Label lblStatus;
        private Button btnDownloadDevices;
        private DataGridView dgvInventory;
        private ComboBox cmbHistory;
        private TableLayoutPanel mainLayout;
        private IReadOnlyList<NinjaOneClientInventory>? currentInventory;
        private readonly NinjaOneInventoryHistoryService _historyService = new NinjaOneInventoryHistoryService();
        private bool _isUpdatingHistoryComboBox = false;

        public Form1()
        {
            InitializeComponent();
            InitializeNinjaControls();
            this.Shown += Form1_Shown;
        }

        private void InitializeNinjaControls()
        {
            var historyPanel = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                AutoSize = true,
                ColumnCount = 2,
                Padding = new Padding(8)
            };
            var tl = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                AutoSize = true,
                ColumnCount = 2,
                Padding = new Padding(8)
            };
            tl.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            tl.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

            historyPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            historyPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

            btnNinjaSettings = new Button { Text = "Paramètres NinjaOne", AutoSize = true };
            btnNinjaSettings.Click += BtnNinjaSettings_Click;
            btnDownloadDevices = new Button { Text = "Récupérer les postes NinjaOne", AutoSize = true };
            btnDownloadDevices.Click += BtnDownloadDevices_Click;
            lblStatus = new Label { Text = string.Empty, AutoSize = true, Anchor = AnchorStyles.Left | AnchorStyles.Right };

            // History controls
            var lblHist = new Label { Text = "Historique :", AutoSize = true, Anchor = AnchorStyles.Left | AnchorStyles.Top };
            cmbHistory = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 320, Anchor = AnchorStyles.Left | AnchorStyles.Top };
            cmbHistory.SelectedIndexChanged += CmbHistory_SelectedIndexChanged;

            historyPanel.Controls.Add(lblHist, 0, 0);
            historyPanel.Controls.Add(cmbHistory, 1, 0);

            tl.Controls.Add(btnNinjaSettings, 0, 0);
            tl.Controls.Add(lblStatus, 1, 0);

            // Add download button on a new row, keep layout consistent by adding an empty filler in column 1
            tl.RowCount = 2;
            tl.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            tl.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            tl.Controls.Add(btnDownloadDevices, 0, 1);
            var filler = new Label { AutoSize = true };
            tl.Controls.Add(filler, 1, 1);

            // main layout: row 0 = historyPanel, row1 = controls (auto), row2 = dgv (percent)
            mainLayout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 3, ColumnCount = 1 };
            mainLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            mainLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            mainLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            mainLayout.Controls.Add(historyPanel, 0, 0);
            mainLayout.Controls.Add(tl, 0, 1);

            // create DataGridView
            dgvInventory = new DataGridView
            {
                Dock = DockStyle.Fill,
                ReadOnly = true,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                RowHeadersVisible = false,
                MultiSelect = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                AutoGenerateColumns = false
            };

            // Columns
            // Two columns: ClientColumn (Client / emplacement) and Count (Nombre de postes)
            var clientCol = new DataGridViewTextBoxColumn { Name = "ClientColumn", HeaderText = "Client / emplacement" };
            clientCol.AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
            clientCol.MinimumWidth = 500;
            clientCol.SortMode = DataGridViewColumnSortMode.NotSortable;

            var countCol = new DataGridViewTextBoxColumn { Name = "Count", HeaderText = "Nombre de postes" };
            countCol.AutoSizeMode = DataGridViewAutoSizeColumnMode.None;
            countCol.Width = 140;
            countCol.MinimumWidth = 110;
            countCol.SortMode = DataGridViewColumnSortMode.NotSortable;
            countCol.DefaultCellStyle = new DataGridViewCellStyle { Alignment = DataGridViewContentAlignment.MiddleCenter };

            dgvInventory.Columns.AddRange(new DataGridViewColumn[] { clientCol, countCol });
            dgvInventory.CellClick += DgvInventory_CellClick;
            dgvInventory.CellMouseEnter += DgvInventory_CellMouseEnter;
            dgvInventory.CellMouseLeave += DgvInventory_CellMouseLeave;

            mainLayout.Controls.Add(dgvInventory, 0, 2);
            this.Controls.Add(mainLayout);

            UpdateStatusLabel();
        }

        private async Task LoadHistoryAsync(string? selectFilePath = null)
        {
            try
            {
                var items = await _historyService.GetHistoryAsync();
                _isUpdatingHistoryComboBox = true;
                cmbHistory.Items.Clear();
                cmbHistory.DisplayMember = "DisplayText";
                foreach (var it in items)
                    cmbHistory.Items.Add(it);

                if (cmbHistory.Items.Count == 0)
                {
                    lblStatus.Text = "Aucune extraction disponible.";
                }
                else
                {
                    // select requested or first
                    object? toSelect = null;
                    if (!string.IsNullOrEmpty(selectFilePath))
                        toSelect = items.FirstOrDefault(x => string.Equals(x.FilePath, selectFilePath, StringComparison.OrdinalIgnoreCase));
                    if (toSelect == null)
                        toSelect = cmbHistory.Items[0];
                    cmbHistory.SelectedItem = toSelect;
                }
            }
            catch
            {
                // ignore errors while listing history
            }
            finally
            {
                _isUpdatingHistoryComboBox = false;
            }
        }

        private async void CmbHistory_SelectedIndexChanged(object? sender, EventArgs e)
        {
            if (_isUpdatingHistoryComboBox)
                return;
            if (cmbHistory.SelectedItem is not NinjaOneInventoryHistoryItem item)
                return;

            try
            {
                var reader = new NinjaOneInventoryReader();
                currentInventory = await reader.ReadAsync(item.FilePath);
                PopulateGridFromInventory(currentInventory);
                lblStatus.Text = $"{item.RetrievedAtUtc.ToLocalTime():dd/MM/yyyy HH:mm} — {item.DeviceCount} postes, {item.OrganizationCount} clients, {item.LocationCount} emplacements";
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "Erreur lecture historique", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
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

            // Load history on first display
            _ = LoadHistoryAsync();
        }

        private void PopulateGridFromInventory(IReadOnlyList<NinjaOneClientInventory>? inventory)
        {
            dgvInventory.Rows.Clear();
            if (inventory == null)
                return;
            foreach (var client in inventory)
            {
                // Client main row
                var rowIndex = dgvInventory.Rows.Add();
                var row = dgvInventory.Rows[rowIndex];
                row.Tag = client;
                row.DefaultCellStyle.Font = new Font(dgvInventory.Font, FontStyle.Bold);
                row.DefaultCellStyle.BackColor = Color.FromArgb(240, 240, 240);
                var prefix = client.CanExpand ? (client.IsExpanded ? "▼ " : "▶ ") : string.Empty;
                row.Cells[0].Value = prefix + client.ClientName;
                row.Cells[1].Value = client.TotalDeviceCount.ToString();

                // If can expand and is expanded, add location rows
                if (client.CanExpand && client.IsExpanded)
                {
                    foreach (var loc in client.Locations)
                    {
                        var idx = dgvInventory.Rows.Add();
                        var rloc = dgvInventory.Rows[idx];
                        rloc.Tag = loc;
                        // location row: indented arrow then name
                        rloc.Cells[0].Value = "    ↳ " + loc.LocationName;
                        rloc.Cells[1].Value = loc.DeviceCount.ToString();
                        rloc.DefaultCellStyle.Padding = new Padding(12, 0, 0, 0);
                        // ensure location rows are not bold and have default background
                        rloc.DefaultCellStyle.Font = dgvInventory.Font;
                        rloc.DefaultCellStyle.BackColor = dgvInventory.DefaultCellStyle.BackColor;
                    }
                }
            }
        }

        private void DgvInventory_CellClick(object? sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0 || e.RowIndex >= dgvInventory.Rows.Count)
                return;
            var row = dgvInventory.Rows[e.RowIndex];
            if (row.Tag is NinjaOneClientInventory client)
            {
                if (!client.CanExpand)
                    return;
                client.IsExpanded = !client.IsExpanded;
                PopulateGridFromInventory(currentInventory);
            }
            // if it's a location row, do nothing
        }

        private void DgvInventory_CellMouseEnter(object? sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0 || e.RowIndex >= dgvInventory.Rows.Count)
                return;
            var row = dgvInventory.Rows[e.RowIndex];
            if (row.Tag is NinjaOneClientInventory client && client.CanExpand)
                this.Cursor = Cursors.Hand;
            else
                this.Cursor = Cursors.Default;
        }

        private void DgvInventory_CellMouseLeave(object? sender, DataGridViewCellEventArgs e)
        {
            this.Cursor = Cursors.Default;
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

        private async void BtnDownloadDevices_Click(object? sender, EventArgs e)
        {
            var confirm = MessageBox.Show(this,
                "Une nouvelle extraction NinjaOne va être lancée.\n\nLe fichier actuellement affiché sera conservé dans l’historique.\n\nVoulez-vous continuer ?",
                "Nouvelle extraction NinjaOne",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question,
                MessageBoxDefaultButton.Button2);

            if (confirm != DialogResult.Yes)
                return;

            if (!NinjaOneSettingsService.IsConfigured())
            {
                using var f = new NinjaOneSettingsForm();
                f.ShowDialog(this);
                if (!NinjaOneSettingsService.IsConfigured())
                    return;
            }

            using var cts = new CancellationTokenSource();
            try
            {
                btnDownloadDevices.Enabled = false;
                btnNinjaSettings.Enabled = false;
                var previousCursor = this.UseWaitCursor;
                this.UseWaitCursor = true;
                lblStatus.Text = "Connexion à NinjaOne...";

                var progress = new Progress<int>(n => lblStatus.Text = $"{n} postes récupérés...");

                var client = new NinjaOneDeviceApiClient();
                var result = await client.DownloadAllDevicesAsync(progress, cts.Token);

                // Read inventory and populate grid
                var reader = new NinjaOneInventoryReader();
                currentInventory = await reader.ReadAsync(result.FilePath, cts.Token);
                PopulateGridFromInventory(currentInventory);

                lblStatus.Text = $"{result.DeviceCount} postes récupérés le {result.RetrievedAtUtc:dd/MM/yyyy} à {result.RetrievedAtUtc:HH:mm}.";

                MessageBox.Show(this, $"{result.DeviceCount} postes NinjaOne ont été récupérés.\n\nFichier :\n{result.FilePath}", "Récupération terminée", MessageBoxButtons.OK, MessageBoxIcon.Information);
                // Reload history and select the newly created extraction
                try
                {
                    await LoadHistoryAsync(result.FilePath);
                }
                catch { }
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "Erreur NinjaOne", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                btnDownloadDevices.Enabled = true;
                btnNinjaSettings.Enabled = true;
                this.UseWaitCursor = false;
            }
        }
    }
}
