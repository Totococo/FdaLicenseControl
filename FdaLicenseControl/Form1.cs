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
        private Button btnSettings;
        private Label lblStatus;
        private Button btnDownloadDevices;
        private Button btnDownloadZyxel;
        private DataGridView dgvInventory;
        private DataGridView dgvZyxel;
        private ComboBox cmbHistory;
        private ComboBox cmbZyxelHistory;
        private Label lblZyxelStatus;
        private TableLayoutPanel mainLayout;
        private IReadOnlyList<NinjaOneClientInventory>? currentInventory;
        private readonly NinjaOneInventoryHistoryService _historyService = new NinjaOneInventoryHistoryService();
        private readonly FdaUiStateService _uiStateService = new FdaUiStateService();
        private FdaUiState _uiState = new FdaUiState();
        private bool _isUpdatingHistoryComboBox = false;
        private bool _initialHistoryLoaded = false;
        private readonly ZyxelNebulaInventoryHistoryService _zyxelHistoryService = new ZyxelNebulaInventoryHistoryService();
        private bool _isUpdatingZyxelHistoryComboBox = false;

        // Tag used for each DataGridViewRow to store stable key and references
        private sealed class InventoryGridRowTag
        {
            public bool IsClient { get; init; }
            public NinjaOneClientInventory? Client { get; init; }
            public NinjaOneLocationInventory? Location { get; init; }
            public int OrganizationId { get; init; }
            public int? LocationId { get; init; }
            public string Key { get; init; } = string.Empty;
        }

        private sealed class ZyxelGridRowTag
        {
            public bool IsOrganization { get; init; }

            // Client must be present for both organization and site rows
            public ZyxelNebulaClientInventory Client { get; init; }

            public ZyxelNebulaSiteInventory? Site { get; init; }

            // Key used for highlighting persistence
            public string HighlightKey { get; init; } = string.Empty;
        }

        // in-memory list of last loaded Zyxel clients (preserve IsExpanded)
        private IReadOnlyList<ZyxelNebulaClientInventory>? _zyxelClients;



        public Form1()
        {
            InitializeComponent();
            this.Text = "FDA License Control";
            this.StartPosition = FormStartPosition.CenterScreen;
            this.WindowState = FormWindowState.Maximized;
            this.AutoScaleMode = AutoScaleMode.Dpi;
            this.MinimumSize = new Size(1000, 650);
            InitializeNinjaControls();
            this.Shown += Form1_Shown;
        }

        private CancellationTokenSource? _zyxelCts;
        private bool _isZyxelDownloadInProgress = false;

        private async void BtnDownloadZyxel_Click(object? sender, EventArgs e)
        {
            if (_isZyxelDownloadInProgress)
                return;

            _isZyxelDownloadInProgress = true;
            try
            {
                var confirm = MessageBox.Show(this,
                    "Une nouvelle extraction Zyxel Nebula va être lancée.\n\nLes anciennes extractions Zyxel seront conservées.\n\nVoulez-vous continuer ?",
                    "Nouvelle extraction Zyxel Nebula",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question,
                    MessageBoxDefaultButton.Button2);

                if (confirm != DialogResult.Yes)
                    return;

                if (!ZyxelNebulaSettingsService.IsConfigured())
                {
                    using var f = new ApplicationSettingsForm();
                    f.ShowDialog(this);
                    if (!ZyxelNebulaSettingsService.IsConfigured())
                        return;
                }

                // disable controls
                btnSettings.Enabled = false;
                btnDownloadDevices.Enabled = false;
                cmbHistory.Enabled = false;
                this.UseWaitCursor = true;

                lblStatus.Text = "Connexion à Zyxel Nebula...";

                var progress = new Progress<string>(message => lblStatus.Text = message);

                _zyxelCts = new CancellationTokenSource();
                try
                {
                    var client = new ZyxelNebulaInventoryApiClient();
                    var result = await client.DownloadAllAsync(progress, _zyxelCts.Token);

                    lblStatus.Text = $"{result.OrganizationCount} organisations Zyxel — {result.SiteCount} sites — {result.DeviceCount} équipements.";

                    MessageBox.Show(this,
                        $"{result.OrganizationCount} organisations,\n{result.SiteCount} sites et\n{result.DeviceCount} équipements ont été récupérés.\n\nFichier :\n{result.FilePath}",
                        "Extraction Zyxel Nebula terminée",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, ex.Message, "Erreur Zyxel Nebula", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
                finally
                {
                    _zyxelCts?.Dispose();
                    _zyxelCts = null;
                    btnSettings.Enabled = true;
                    btnDownloadDevices.Enabled = true;
                    cmbHistory.Enabled = true;
                    this.UseWaitCursor = false;
                    this.Cursor = Cursors.Default;
                    dgvZyxel.UseWaitCursor = false;
                    dgvZyxel.Cursor = Cursors.Default;
                }
            }
            finally
            {
                _isZyxelDownloadInProgress = false;
            }
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

            btnSettings = new Button { Text = "Paramètres", AutoSize = true };
            btnSettings.Click += BtnSettings_Click;
            btnDownloadDevices = new Button { Text = "Récupérer les postes NinjaOne", AutoSize = true };
            btnDownloadDevices.Click += BtnDownloadDevices_Click;
            // assign field instance for Zyxel download button (subscribe once below)
            btnDownloadZyxel = new Button { Text = "Récupérer les données Zyxel Nebula", AutoSize = true };
            lblStatus = new Label { Text = string.Empty, AutoSize = true, Anchor = AnchorStyles.Left | AnchorStyles.Right };

            // History controls
            var lblHistNinja = new Label { Text = "Historique :", AutoSize = true, Anchor = AnchorStyles.Left | AnchorStyles.Top };
            cmbHistory = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 320, Anchor = AnchorStyles.Left | AnchorStyles.Top };
            cmbHistory.SelectedIndexChanged += CmbHistory_SelectedIndexChanged;

            historyPanel.Controls.Add(lblHistNinja, 0, 0);
            historyPanel.Controls.Add(cmbHistory, 1, 0);

            var settingsPanel = new FlowLayoutPanel { FlowDirection = FlowDirection.LeftToRight, AutoSize = true, Dock = DockStyle.Fill };
            settingsPanel.Controls.Add(btnSettings);
            tl.Controls.Add(settingsPanel, 0, 0);
            tl.Controls.Add(lblStatus, 1, 0);

            // Add download buttons on a new row, keep layout consistent by adding an empty filler in column 1
            tl.RowCount = 2;
            tl.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            tl.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            var downloadPanel = new FlowLayoutPanel { FlowDirection = FlowDirection.LeftToRight, AutoSize = true, Dock = DockStyle.Fill };
            downloadPanel.Controls.Add(btnDownloadDevices);
            downloadPanel.Controls.Add(btnDownloadZyxel);
            tl.Controls.Add(downloadPanel, 0, 1);
            var filler = new Label { AutoSize = true };
            tl.Controls.Add(filler, 1, 1);

            // main layout: top bar (settings) and tab control
            mainLayout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1, AutoSize = false };
            mainLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            mainLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            // Create top bar with settings button only
            var topBar = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = true, FlowDirection = FlowDirection.LeftToRight, Padding = new Padding(8), Margin = new Padding(4) };
            topBar.Controls.Add(btnSettings);

            mainLayout.Controls.Add(topBar, 0, 0);

            // create tab control for NinjaOne and Zyxel
            var tab = new TabControl { Dock = DockStyle.Fill };
            var tabNinja = new TabPage("NinjaOne");
            var tabZyxel = new TabPage("Zyxel Nebula");

            // NinjaOne tab layout
            var ninjaLayout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1 };
            ninjaLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            ninjaLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

            var ninjaBar = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = true, FlowDirection = FlowDirection.LeftToRight, Padding = new Padding(8), Margin = new Padding(4) };
            var lblHist = new Label { Text = "Historique :", AutoSize = true, Anchor = AnchorStyles.Left | AnchorStyles.Top };
            cmbHistory.Width = 320;
            ninjaBar.Controls.Add(lblHistNinja);
            ninjaBar.Controls.Add(cmbHistory);
            ninjaBar.Controls.Add(btnDownloadDevices);
            ninjaBar.Controls.Add(lblStatus);

            ninjaLayout.Controls.Add(ninjaBar, 0, 0);

            // create DataGridView for NinjaOne (reuse existing field)
            // ensure properties
            dgvInventory = dgvInventory ?? new DataGridView();
            dgvInventory.Dock = DockStyle.Fill;
            dgvInventory.Visible = true;
            dgvInventory.Enabled = true;
            dgvInventory.AutoSize = false;
            dgvInventory.Margin = new Padding(3);

            // create DataGridView instance settings
            dgvInventory.ReadOnly = true;
            dgvInventory.AllowUserToAddRows = false;
            dgvInventory.AllowUserToDeleteRows = false;
            dgvInventory.RowHeadersVisible = false;
            dgvInventory.MultiSelect = false;
            dgvInventory.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            dgvInventory.AutoGenerateColumns = false;
            dgvInventory.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None;
            // explicit property group already applied above

            // Columns
            // Two columns: ClientColumn (Client / emplacement) and Count (Nombre de postes)
            var clientCol = new DataGridViewTextBoxColumn { Name = "ClientColumn", HeaderText = "Client / emplacement" };
            clientCol.AutoSizeMode = DataGridViewAutoSizeColumnMode.None;
            clientCol.Width = 650;
            clientCol.MinimumWidth = 400;
            clientCol.Resizable = DataGridViewTriState.True;
            clientCol.SortMode = DataGridViewColumnSortMode.NotSortable;

            var countCol = new DataGridViewTextBoxColumn { Name = "Count", HeaderText = "Nombre de postes" };
            countCol.AutoSizeMode = DataGridViewAutoSizeColumnMode.None;
            countCol.Width = 130;
            countCol.MinimumWidth = 100;
            countCol.Resizable = DataGridViewTriState.True;
            countCol.SortMode = DataGridViewColumnSortMode.NotSortable;
            countCol.DefaultCellStyle = new DataGridViewCellStyle { Alignment = DataGridViewContentAlignment.MiddleCenter };
            // center header text
            countCol.HeaderCell.Style.Alignment = DataGridViewContentAlignment.MiddleCenter;

            // keep default header for NinjaOne table
            countCol.HeaderText = "Nombre de postes";
            dgvInventory.Columns.AddRange(new DataGridViewColumn[] { clientCol, countCol });
            dgvInventory.CellMouseClick += DgvInventory_CellMouseClick;
            dgvInventory.CellMouseEnter += DgvInventory_CellMouseEnter;
            dgvInventory.CellMouseLeave += DgvInventory_CellMouseLeave;

            ninjaLayout.Controls.Add(dgvInventory, 0, 1);
            tabNinja.Controls.Add(ninjaLayout);

            // Zyxel tab layout
            var zyxelLayout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1 };
            zyxelLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            zyxelLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

            var zyxelBar = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = true, FlowDirection = FlowDirection.LeftToRight, Padding = new Padding(8), Margin = new Padding(4) };
            var lblZHist = new Label { Text = "Historique :", AutoSize = true, Anchor = AnchorStyles.Left | AnchorStyles.Top };
            cmbZyxelHistory = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 320, Anchor = AnchorStyles.Left | AnchorStyles.Top };
            cmbZyxelHistory.SelectedIndexChanged += CmbZyxelHistory_SelectedIndexChanged;
            // reuse existing Zyxel download button (btnDownloadZyxel variable)
            // subscribe click once
            btnDownloadZyxel.Click -= BtnDownloadZyxel_Click;
            btnDownloadZyxel.Click += BtnDownloadZyxel_Click;
            lblZyxelStatus = new Label { Text = string.Empty, AutoSize = true, Anchor = AnchorStyles.Left | AnchorStyles.Right };

            zyxelBar.Controls.Add(lblZHist);
            zyxelBar.Controls.Add(cmbZyxelHistory);
            zyxelBar.Controls.Add(btnDownloadZyxel);
            zyxelBar.Controls.Add(lblZyxelStatus);

            zyxelLayout.Controls.Add(zyxelBar, 0, 0);

            // create Zyxel DataGridView (reuse field)
            dgvZyxel = dgvZyxel ?? new DataGridView();
            dgvZyxel.Dock = DockStyle.Fill;
            dgvZyxel.Visible = true;
            dgvZyxel.Enabled = true;
            dgvZyxel.AutoSize = false;
            dgvZyxel.Margin = new Padding(3);

            dgvZyxel.ReadOnly = true;
            dgvZyxel.AllowUserToAddRows = false;
            dgvZyxel.AllowUserToDeleteRows = false;
            dgvZyxel.AllowUserToResizeRows = false;
            dgvZyxel.RowHeadersVisible = false;
            dgvZyxel.MultiSelect = false;
            dgvZyxel.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            dgvZyxel.AutoGenerateColumns = false;
            dgvZyxel.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None;
            dgvZyxel.BackgroundColor = SystemColors.Window;

            var zClientCol = new DataGridViewTextBoxColumn { Name = "ZClientColumn", HeaderText = "Client / site" };
            zClientCol.AutoSizeMode = DataGridViewAutoSizeColumnMode.None;
            zClientCol.Width = 650;
            zClientCol.MinimumWidth = 400;
            zClientCol.Resizable = DataGridViewTriState.True;
            zClientCol.SortMode = DataGridViewColumnSortMode.NotSortable;

            var zCountCol = new DataGridViewTextBoxColumn { Name = "ZCount", HeaderText = "Nombre de licences" };
            zCountCol.AutoSizeMode = DataGridViewAutoSizeColumnMode.None;
            zCountCol.Width = 160;
            zCountCol.MinimumWidth = 120;
            zCountCol.Resizable = DataGridViewTriState.True;
            zCountCol.SortMode = DataGridViewColumnSortMode.NotSortable;
            zCountCol.DefaultCellStyle = new DataGridViewCellStyle { Alignment = DataGridViewContentAlignment.MiddleCenter };
            zCountCol.HeaderCell.Style.Alignment = DataGridViewContentAlignment.MiddleCenter;

            dgvZyxel.Columns.AddRange(new DataGridViewColumn[] { zClientCol, zCountCol });
            // register a single CellMouseClick handler for Zyxel grid
            dgvZyxel.CellMouseClick -= ZyxelDataGridView_CellMouseClick;
            dgvZyxel.CellMouseClick += ZyxelDataGridView_CellMouseClick;

            zyxelLayout.Controls.Add(dgvZyxel, 0, 1);
            tabZyxel.Controls.Add(zyxelLayout);

            tab.TabPages.Add(tabNinja);
            tab.TabPages.Add(tabZyxel);

            mainLayout.Controls.Add(tab, 0, 1);
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

            // If we have a selection, load it explicitly (do not rely on SelectedIndexChanged)
            try
            {
                if (cmbHistory.Items.Count > 0 && cmbHistory.SelectedItem is NinjaOneInventoryHistoryItem sel)
                {
                    await LoadHistoryItemAsync(sel);
                }

            }
            catch (Exception ex)
            {
                // show status and error but do not crash
                lblStatus.Text = "Impossible de charger la dernière extraction.";
                MessageBox.Show(this, ex.Message, "Erreur lecture historique", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private async Task LoadZyxelHistoryAsync(string? selectFilePath = null)
        {
            try
            {
                var items = await _zyxelHistoryService.GetHistoryAsync();
                _isUpdatingZyxelHistoryComboBox = true;
                cmbZyxelHistory.Items.Clear();
                cmbZyxelHistory.DisplayMember = "DisplayText";
                foreach (var it in items)
                    cmbZyxelHistory.Items.Add(it);

                if (cmbZyxelHistory.Items.Count == 0)
                {
                    lblZyxelStatus.Text = "Aucune extraction Zyxel Nebula disponible.";
                }
                else
                {
                    object? toSelect = null;
                    if (!string.IsNullOrEmpty(selectFilePath))
                        toSelect = items.FirstOrDefault(x => string.Equals(x.FilePath, selectFilePath, StringComparison.OrdinalIgnoreCase));
                    if (toSelect == null)
                        toSelect = cmbZyxelHistory.Items[0];
                    cmbZyxelHistory.SelectedItem = toSelect;
                }
            }
            catch
            {
                // ignore
            }
            finally
            {
                _isUpdatingZyxelHistoryComboBox = false;
            }

            try
            {
                if (cmbZyxelHistory.Items.Count > 0 && cmbZyxelHistory.SelectedItem is ZyxelNebulaInventoryHistoryItem sel)
                {
                    await LoadZyxelHistoryItemAsync(sel);
                }
            }
            catch (Exception ex)
            {
                lblZyxelStatus.Text = "Impossible de charger la dernière extraction Zyxel.";
                MessageBox.Show(this, ex.Message, "Erreur lecture historique Zyxel", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private async void CmbHistory_SelectedIndexChanged(object? sender, EventArgs e)
        {
            if (_isUpdatingHistoryComboBox)
                return;
            if (cmbHistory.SelectedItem is not NinjaOneInventoryHistoryItem item)
                return;

            await LoadHistoryItemAsync(item);
        }

        private async void CmbZyxelHistory_SelectedIndexChanged(object? sender, EventArgs e)
        {
            if (_isUpdatingZyxelHistoryComboBox)
                return;
            if (cmbZyxelHistory.SelectedItem is not ZyxelNebulaInventoryHistoryItem item)
                return;

            await LoadZyxelHistoryItemAsync(item);
        }

        private async Task LoadHistoryItemAsync(NinjaOneInventoryHistoryItem item)
        {
            try
            {
                var reader = new NinjaOneInventoryReader();
                currentInventory = await reader.ReadAsync(item.FilePath);
                PopulateGridFromInventory(currentInventory);
                lblStatus.Text = $"{item.RetrievedAtUtc.ToLocalTime():dd/MM/yyyy HH:mm} — {item.DeviceCount} postes, {item.OrganizationCount} clients, {item.LocationCount} emplacements";
            }
            catch (Exception ex)
            {
                lblStatus.Text = "Impossible de charger la dernière extraction.";
                MessageBox.Show(this, ex.Message, "Erreur lecture historique", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private async Task LoadZyxelHistoryItemAsync(ZyxelNebulaInventoryHistoryItem item)
        {
            try
            {
                var reader = new ZyxelNebulaInventoryReader();
                var clients = reader.Read(item.FilePath);
                // keep clients in memory (preserve IsExpanded) then build grid
                _zyxelClients = clients;
                RebuildZyxelGridFromClients(clients, null, -1);
                // RebuildZyxelGridFromClients already applied highlights

                lblZyxelStatus.Text = string.Format("{0:dd/MM/yyyy HH:mm} — {1} clients — {2} sites — {3} licences — {4} organisations ignorées",
                    item.RetrievedAtUtc.ToLocalTime().DateTime, clients.Length, item.SiteCount, item.DeviceCount, item.SkippedOrganizationCount);
            }
            catch (Exception ex)
            {
                lblZyxelStatus.Text = "Impossible de charger l'extraction Zyxel.";
                MessageBox.Show(this, ex.Message, "Erreur lecture historique Zyxel", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                // Ensure wait cursor is never left active after loading
                this.UseWaitCursor = false;
                this.Cursor = Cursors.Default;
                dgvZyxel.UseWaitCursor = false;
                dgvZyxel.Cursor = Cursors.Default;
            }
        }

        private async void Form1_Shown(object? sender, EventArgs e)
        {
            this.Shown -= Form1_Shown;

            if (_initialHistoryLoaded)
                return;
            _initialHistoryLoaded = true;
            if (!NinjaOneSettingsService.IsConfigured())
            {
                using var f = new NinjaOneSettingsForm();
                f.ShowDialog(this);
                UpdateStatusLabel();
            }

            // Load UI state once
            try
            {
                _uiState = _uiStateService.Load();
            }
            catch
            {
                _uiState = new FdaUiState();
            }

            // Load history on first display
            try
            {
                await LoadHistoryAsync();
                // load zyxel history after ninja
                await LoadZyxelHistoryAsync();
            }
            catch
            {
                // ignore
            }
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
                var clientKey = $"organization:{client.OrganizationId}";
                var clientTag = new InventoryGridRowTag { IsClient = true, Client = client, OrganizationId = client.OrganizationId, LocationId = null, Key = clientKey };
                row.Tag = clientTag;
                row.DefaultCellStyle.Font = new Font(dgvInventory.Font, FontStyle.Bold);
                row.DefaultCellStyle.BackColor = Color.FromArgb(240, 240, 240);
                var prefix = client.CanExpand ? (client.IsExpanded ? "▼ " : "▶ ") : string.Empty;
                row.Cells[0].Value = prefix + client.ClientName;
                row.Cells[1].Value = client.TotalDeviceCount.ToString();

                ApplyRowHighlight(row, clientTag);

                // If can expand and is expanded, add location rows
                if (client.CanExpand && client.IsExpanded)
                {
                    foreach (var loc in client.Locations)
                    {
                        var idx = dgvInventory.Rows.Add();
                        var rloc = dgvInventory.Rows[idx];
                        var locId = loc.LocationId;
                        var locKey = $"location:{client.OrganizationId}:{(locId.HasValue ? locId.Value.ToString() : "none")}";
                        var locTag = new InventoryGridRowTag { IsClient = false, Client = client, Location = loc, OrganizationId = client.OrganizationId, LocationId = locId, Key = locKey };
                        rloc.Tag = locTag;
                        // location row: indented arrow then name
                        rloc.Cells[0].Value = "    ↳ " + loc.LocationName;
                        rloc.Cells[1].Value = loc.DeviceCount.ToString();
                        rloc.DefaultCellStyle.Padding = new Padding(12, 0, 0, 0);
                        // ensure location rows are not bold and have default background
                        rloc.DefaultCellStyle.Font = dgvInventory.Font;
                        rloc.DefaultCellStyle.BackColor = dgvInventory.DefaultCellStyle.BackColor;

                        ApplyRowHighlight(rloc, locTag);
                    }
                }
            }
        }

        private void ApplyRowHighlight(DataGridViewRow row, InventoryGridRowTag tag)
        {
            var isHighlighted = !string.IsNullOrEmpty(tag.Key) && _uiState.HighlightedRowKeys.Contains(tag.Key);
            if (isHighlighted)
            {
                row.DefaultCellStyle.BackColor = Color.LightGreen;
                row.DefaultCellStyle.SelectionBackColor = Color.LightGreen;
                row.DefaultCellStyle.ForeColor = Color.Black;
                row.DefaultCellStyle.SelectionForeColor = Color.Black;
                if (tag.IsClient)
                    row.DefaultCellStyle.Font = new Font(dgvInventory.Font, FontStyle.Bold);
                else
                    row.DefaultCellStyle.Font = dgvInventory.Font;
            }
            else
            {
                // restore defaults depending on type
                if (tag.IsClient)
                {
                    row.DefaultCellStyle.BackColor = Color.FromArgb(240, 240, 240);
                    row.DefaultCellStyle.Font = new Font(dgvInventory.Font, FontStyle.Bold);
                }
                else
                {
                    row.DefaultCellStyle.BackColor = dgvInventory.DefaultCellStyle.BackColor;
                    row.DefaultCellStyle.Font = dgvInventory.Font;
                }

                // selection colors to defaults
                row.DefaultCellStyle.SelectionBackColor = SystemColors.Highlight;
                row.DefaultCellStyle.SelectionForeColor = SystemColors.HighlightText;
                row.DefaultCellStyle.ForeColor = dgvInventory.DefaultCellStyle.ForeColor;
            }
        }

        private void DgvInventory_CellMouseClick(object? sender, DataGridViewCellMouseEventArgs e)
        {
            if (e.RowIndex < 0 || e.RowIndex >= dgvInventory.Rows.Count)
                return;

            var row = dgvInventory.Rows[e.RowIndex];
            if (row.Tag is not InventoryGridRowTag tag)
                return;

            // If client and click within first 28 pixels on client column -> toggle expand only
            if (tag.IsClient && tag.Client != null && tag.Client.CanExpand && e.ColumnIndex == 0 && e.X < 28)
            {
                tag.Client.IsExpanded = !tag.Client.IsExpanded;
                PopulateGridFromInventory(currentInventory);
                return;
            }

            // Toggle highlight for this key
            if (string.IsNullOrEmpty(tag.Key))
                return;

            try
            {
                if (_uiState.HighlightedRowKeys.Contains(tag.Key))
                    _uiState.HighlightedRowKeys.Remove(tag.Key);
                else
                    _uiState.HighlightedRowKeys.Add(tag.Key);

                // persist
                try
                {
                    _uiStateService.Save(_uiState);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, ex.Message, "Erreur sauvegarde état UI", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }

                // apply to visible rows
                for (int i = 0; i < dgvInventory.Rows.Count; i++)
                {
                    if (dgvInventory.Rows[i].Tag is InventoryGridRowTag rtag && rtag.Key == tag.Key)
                        ApplyRowHighlight(dgvInventory.Rows[i], rtag);
                }
            }
            catch
            {
                // ignore
            }
        }

        private void DgvInventory_CellMouseEnter(object? sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0 || e.RowIndex >= dgvInventory.Rows.Count)
                return;
            var row = dgvInventory.Rows[e.RowIndex];
            if (row.Tag is InventoryGridRowTag tag && tag.IsClient && tag.Client != null && tag.Client.CanExpand)
                this.Cursor = Cursors.Hand;
            else
                this.Cursor = Cursors.Default;
        }

        private void DgvInventory_CellMouseLeave(object? sender, DataGridViewCellEventArgs e)
        {
            this.Cursor = Cursors.Default;
        }

        private void ZyxelDataGridView_CellMouseClick(object? sender, DataGridViewCellMouseEventArgs e)
        {
            if (e.RowIndex < 0 || e.RowIndex >= dgvZyxel.Rows.Count)
                return;
            if (e.ColumnIndex < 0 || e.ColumnIndex >= dgvZyxel.Columns.Count)
                return;

            var row = dgvZyxel.Rows[e.RowIndex];
            if (row.Tag is not ZyxelGridRowTag tag || tag.Client == null)
                return;

            // If click is on the first column and within the first 28 pixels and it's an organization that can expand -> toggle expand only
            if (tag.IsOrganization && tag.Client.CanExpand && e.ColumnIndex == 0 && e.X <= 28)
            {
                // preserve scroll position and selected organization key
                int savedIndex = -1;
                try { savedIndex = dgvZyxel.FirstDisplayedScrollingRowIndex; } catch { savedIndex = -1; }
                var orgKey = tag.Client.OrganizationKey;
                tag.Client.IsExpanded = !tag.Client.IsExpanded;
                // rebuild from in-memory clients to preserve IsExpanded
                if (_zyxelClients != null)
                    RebuildZyxelGridFromClients(_zyxelClients, orgKey, savedIndex);
                return;
            }

            // For other valid clicks: toggle highlight
            var key = tag.HighlightKey;
            if (string.IsNullOrEmpty(key))
                return;

            try
            {
                if (_uiState.HighlightedRowKeys.Contains(key))
                    _uiState.HighlightedRowKeys.Remove(key);
                else
                    _uiState.HighlightedRowKeys.Add(key);

                // persist
                try
                {
                    _uiStateService.Save(_uiState);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, ex.Message, "Erreur sauvegarde état UI", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }

                // apply to visible rows matching this key only
                for (int i = 0; i < dgvZyxel.Rows.Count; i++)
                {
                    if (dgvZyxel.Rows[i].Tag is ZyxelGridRowTag rtag && rtag.HighlightKey == key)
                        ApplyZyxelRowHighlight(dgvZyxel.Rows[i], rtag.HighlightKey);
                }
            }
            catch
            {
                // ignore
            }
        }

        private void RebuildZyxelGridFromClients(IReadOnlyList<ZyxelNebulaClientInventory> clients, string? focusOrgKey, int savedFirstIndex)
        {
            dgvZyxel.Rows.Clear();
            if (clients == null)
                return;
            for (int ci = 0; ci < clients.Count; ci++)
            {
                var client = clients[ci];

                // Validate that OrganizationKey is not empty
                if (string.IsNullOrEmpty(client.OrganizationKey))
                {
#if DEBUG
                    System.Diagnostics.Debug.WriteLine($"ERROR: Client {client.OrganizationName} has empty OrganizationKey.");
#endif
                    continue;
                }

                var rowIndex = dgvZyxel.Rows.Add();
                var row = dgvZyxel.Rows[rowIndex];
                var key = $"zyxel:organization:{client.OrganizationKey.Trim()}";
                var tag = new ZyxelGridRowTag { IsOrganization = true, Client = client, Site = null, HighlightKey = key };
                row.Tag = tag;
                row.DefaultCellStyle.Font = new Font(dgvZyxel.Font, FontStyle.Bold);
                row.DefaultCellStyle.BackColor = dgvInventory.DefaultCellStyle.BackColor; // same as ninja org bg
                var prefix = client.CanExpand ? (client.IsExpanded ? "▼ " : "▶ ") : string.Empty;
                row.Cells[0].Value = prefix + client.OrganizationName;
                row.Cells[1].Value = client.TotalLicenseCount.ToString();
#if DEBUG
                System.Diagnostics.Debug.WriteLine($"Zyxel row: {client.OrganizationName} | OrganizationKey={client.OrganizationKey} | HighlightKey={tag.HighlightKey}");
#endif
                ApplyZyxelRowHighlight(row, key);

                if (client.CanExpand && client.IsExpanded)
                {
                    foreach (var site in client.Sites)
                    {
                        var idx = dgvZyxel.Rows.Add();
                        var rsite = dgvZyxel.Rows[idx];
                        var siteIdOrNone = string.IsNullOrEmpty(site.SiteId) ? "none" : site.SiteId.Trim();
                        var skey = $"zyxel:site:{client.OrganizationKey.Trim()}:{siteIdOrNone}";
                        var stag = new ZyxelGridRowTag { IsOrganization = false, Client = client, Site = site, HighlightKey = skey };
                        rsite.Tag = stag;
                        rsite.Cells[0].Value = "    ↳ " + site.SiteName;
                        rsite.Cells[1].Value = site.LicenseCount.ToString();
                        rsite.DefaultCellStyle.Padding = new Padding(12, 0, 0, 0);
                        rsite.DefaultCellStyle.Font = dgvZyxel.Font;
                        ApplyZyxelRowHighlight(rsite, skey);
                    }
                }
            }

            // restore scroll position
            try
            {
                if (savedFirstIndex >= 0 && savedFirstIndex < dgvZyxel.Rows.Count)
                    dgvZyxel.FirstDisplayedScrollingRowIndex = savedFirstIndex;
            }
            catch { }

            // optionally re-select focused org row
            if (!string.IsNullOrEmpty(focusOrgKey))
            {
                for (int i = 0; i < dgvZyxel.Rows.Count; i++)
                {
                    if (dgvZyxel.Rows[i].Tag is ZyxelGridRowTag t && t.IsOrganization && t.Client.OrganizationKey == focusOrgKey)
                    {
                        dgvZyxel.CurrentCell = dgvZyxel.Rows[i].Cells[0];
                        break;
                    }
                }
            }
        }

        private void ApplyZyxelRowHighlight(DataGridViewRow row, string highlightKey)
        {
            var isHighlighted = !string.IsNullOrEmpty(highlightKey) && _uiState.HighlightedRowKeys.Contains(highlightKey);
            if (isHighlighted)
            {
                row.DefaultCellStyle.BackColor = Color.LightGreen;
                row.DefaultCellStyle.SelectionBackColor = Color.LightGreen;
                row.DefaultCellStyle.ForeColor = Color.Black;
                row.DefaultCellStyle.SelectionForeColor = Color.Black;
                // preserve font weight
                if (row.Tag is ZyxelGridRowTag t && t.IsOrganization)
                    row.DefaultCellStyle.Font = new Font(dgvZyxel.Font, FontStyle.Bold);
                else
                    row.DefaultCellStyle.Font = dgvZyxel.Font;
            }
            else
            {
                if (row.Tag is ZyxelGridRowTag t && t.IsOrganization)
                {
                    row.DefaultCellStyle.BackColor = Color.FromArgb(240, 240, 240);
                    row.DefaultCellStyle.Font = new Font(dgvZyxel.Font, FontStyle.Bold);
                }
                else
                {
                    row.DefaultCellStyle.BackColor = dgvZyxel.DefaultCellStyle.BackColor;
                    row.DefaultCellStyle.Font = dgvZyxel.Font;
                }

                row.DefaultCellStyle.SelectionBackColor = SystemColors.Highlight;
                row.DefaultCellStyle.SelectionForeColor = SystemColors.HighlightText;
                row.DefaultCellStyle.ForeColor = dgvZyxel.DefaultCellStyle.ForeColor;
            }
        }

        private void BtnSettings_Click(object? sender, EventArgs e)
        {
            using var f = new ApplicationSettingsForm();
            f.ShowDialog(this);
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
                btnSettings.Enabled = false;
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
                btnSettings.Enabled = true;
                this.UseWaitCursor = false;
            }
        }
    }
}
