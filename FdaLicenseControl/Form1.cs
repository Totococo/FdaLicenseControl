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
        private Button btnDownloadMicrosoft365;
        private Button btnTestMicrosoftGraphAssociationDuMay;
        private DataGridView dgvInventory;
        private DataGridView dgvZyxel;
        private DataGridView dgvMicrosoft365;
        private ComboBox cmbHistory;
        private ComboBox cmbZyxelHistory;
        private ComboBox cmbMicrosoftHistory;
        private Label lblZyxelStatus;
        private Label lblMicrosoftStatus;
        private TabControl mainTabControl;
        private TabPage tabNinjaPage;
        private TabPage tabZyxelPage;
        private TabPage tabMicrosoftPage;
        private TableLayoutPanel mainLayout;
        private IReadOnlyList<NinjaOneClientInventory>? currentInventory;
        private readonly NinjaOneInventoryHistoryService _historyService = new NinjaOneInventoryHistoryService();
        private readonly FdaUiStateService _uiStateService = new FdaUiStateService();
        private FdaUiState _uiState = new FdaUiState();
        private bool _isUpdatingHistoryComboBox = false;
        private bool _initialHistoryLoaded = false;
        private readonly ZyxelNebulaInventoryHistoryService _zyxelHistoryService = new ZyxelNebulaInventoryHistoryService();
        private bool _isUpdatingZyxelHistoryComboBox = false;
        private bool _isMicrosoft365DownloadInProgress = false;
        private bool _isMicrosoftGraphTestInProgress = false;
        private readonly Microsoft365LicenseHistoryService _microsoft365HistoryService = new Microsoft365LicenseHistoryService();
        private bool _isUpdatingMicrosoft365HistoryComboBox = false;
        private readonly Microsoft365LicenseInventoryReader _microsoft365InventoryReader = new Microsoft365LicenseInventoryReader();
        private IReadOnlyList<Microsoft365CustomerLicenseInventory>? _microsoft365Inventories;
        private readonly HashSet<string> _expandedMicrosoft365ClientKeys = new(StringComparer.OrdinalIgnoreCase);
        private const string AssociationDuMayTenantId = "73deff26-8b73-4073-ae23-1fb9800caa12";

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

        private sealed class Microsoft365GridRowTag
        {
            public bool IsClient { get; init; }

            public bool HasChildren { get; init; }

            public string CustomerKey { get; init; } = string.Empty;

            public string OfficeLocation { get; init; } = string.Empty;

            public string HighlightKey { get; init; } = string.Empty;

            public Microsoft365CustomerLicenseInventory Inventory { get; init; } = new Microsoft365CustomerLicenseInventory();

            public Microsoft365OfficeLicenseInventory? Office { get; init; }
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

        private void DgvMicrosoft365_CellMouseClick(object? sender, DataGridViewCellMouseEventArgs e)
        {
            if (e.RowIndex < 0 || e.RowIndex >= dgvMicrosoft365.Rows.Count)
                return;

            if (dgvMicrosoft365.Rows[e.RowIndex].Tag is not Microsoft365GridRowTag tag)
                return;

            if (string.IsNullOrWhiteSpace(tag.HighlightKey))
                return;

            if (tag.IsClient && tag.HasChildren && !string.IsNullOrWhiteSpace(tag.CustomerKey) && e.ColumnIndex == 0 && e.X < 28)
            {
                ToggleMicrosoft365Expansion(tag.CustomerKey);
                return;
            }

            if (_uiState.HighlightedRowKeys.Contains(tag.HighlightKey))
                _uiState.HighlightedRowKeys.Remove(tag.HighlightKey);
            else
                _uiState.HighlightedRowKeys.Add(tag.HighlightKey);

            try
            {
                _uiStateService.Save(_uiState);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "Erreur sauvegarde état UI", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }

            ApplyMicrosoft365RowHighlight(dgvMicrosoft365.Rows[e.RowIndex], tag.HighlightKey);
        }

        private void PopulateMicrosoft365Grid(IReadOnlyList<Microsoft365CustomerLicenseInventory> inventories)
        {
            _microsoft365Inventories = inventories;
            dgvMicrosoft365.Rows.Clear();
            if (inventories == null)
                return;

            for (int i = 0; i < inventories.Count; i++)
            {
                var inventory = inventories[i];
                var key = BuildMicrosoft365HighlightKey(inventory);
                var hasChildren = inventory.Offices != null && inventory.Offices.Count > 0;
                var isExpanded = hasChildren && _expandedMicrosoft365ClientKeys.Contains(GetMicrosoft365ExpansionKey(inventory));
                AddMicrosoft365ClientRow(inventory, hasChildren, key);

                if (hasChildren && isExpanded)
                {
                    foreach (var office in inventory.Offices)
                        AddMicrosoft365OfficeRow(inventory, office);
                }
            }

            ApplyMicrosoft365GridState();
        }

        private void ApplyMicrosoft365RowHighlight(DataGridViewRow row, string highlightKey)
        {
            var isHighlighted = !string.IsNullOrEmpty(highlightKey) && _uiState.HighlightedRowKeys.Contains(highlightKey);
            if (isHighlighted)
            {
                row.DefaultCellStyle.BackColor = Color.LightGreen;
                row.DefaultCellStyle.SelectionBackColor = Color.LightGreen;
                row.DefaultCellStyle.ForeColor = Color.Black;
                row.DefaultCellStyle.SelectionForeColor = Color.Black;
                row.DefaultCellStyle.Font = new Font(dgvMicrosoft365.Font, FontStyle.Bold);
            }
            else
            {
                row.DefaultCellStyle.BackColor = dgvMicrosoft365.DefaultCellStyle.BackColor;
                row.DefaultCellStyle.SelectionBackColor = SystemColors.Highlight;
                row.DefaultCellStyle.ForeColor = dgvMicrosoft365.DefaultCellStyle.ForeColor;
                row.DefaultCellStyle.SelectionForeColor = SystemColors.HighlightText;
                row.DefaultCellStyle.Font = new Font(dgvMicrosoft365.Font, FontStyle.Bold);
            }
        }

        private void ApplyMicrosoft365GridState()
        {
            for (int i = 0; i < dgvMicrosoft365.Rows.Count; i++)
            {
                if (dgvMicrosoft365.Rows[i].Tag is Microsoft365GridRowTag tag)
                    ApplyMicrosoft365RowHighlight(dgvMicrosoft365.Rows[i], tag.HighlightKey);
            }

            dgvMicrosoft365.ClearSelection();
            this.UseWaitCursor = false;
            this.Cursor = Cursors.Default;
            dgvMicrosoft365.UseWaitCursor = false;
            dgvMicrosoft365.Cursor = Cursors.Default;
        }

        private void AddMicrosoft365ClientRow(Microsoft365CustomerLicenseInventory inventory, bool hasChildren, string highlightKey)
        {
            var rowIndex = dgvMicrosoft365.Rows.Add();
            var row = dgvMicrosoft365.Rows[rowIndex];
            var tag = new Microsoft365GridRowTag
            {
                IsClient = true,
                HasChildren = hasChildren,
                CustomerKey = GetMicrosoft365ExpansionKey(inventory),
                HighlightKey = highlightKey,
                Inventory = inventory
            };

            row.Tag = tag;
            row.DefaultCellStyle.Font = new Font(dgvMicrosoft365.Font, FontStyle.Bold);
            row.Cells[0].Value = hasChildren
                ? (_expandedMicrosoft365ClientKeys.Contains(tag.CustomerKey) ? "▼ " + inventory.CompanyName : "▶ " + inventory.CompanyName)
                : inventory.CompanyName;
            row.Cells[1].Value = inventory.BusinessBasicUsed;
            row.Cells[2].Value = inventory.BusinessStandardUsed;
            row.Cells[3].Value = inventory.ExchangeOnlinePlan1Used;
            row.Cells[4].Value = inventory.TeamsEssentialsUsed;
            row.Cells[5].Value = inventory.TotalUsed;
            row.Cells[0].ToolTipText = hasChildren
                ? (string.Equals(inventory.GraphStatus, "Success", StringComparison.OrdinalIgnoreCase)
                    ? "Détails par bureau disponibles."
                    : "Détails par bureau indisponibles : autorisation Microsoft Graph requise.")
                : string.Empty;
        }

        private void AddMicrosoft365OfficeRow(Microsoft365CustomerLicenseInventory inventory, Microsoft365OfficeLicenseInventory office)
        {
            var rowIndex = dgvMicrosoft365.Rows.Add();
            var row = dgvMicrosoft365.Rows[rowIndex];
            var officeKey = GetMicrosoft365OfficeKey(inventory, office.OfficeLocation);
            row.Tag = new Microsoft365GridRowTag
            {
                IsClient = false,
                HasChildren = false,
                CustomerKey = GetMicrosoft365ExpansionKey(inventory),
                OfficeLocation = office.OfficeLocation,
                HighlightKey = officeKey,
                Inventory = inventory,
                Office = office
            };

            row.DefaultCellStyle.Font = new Font(dgvMicrosoft365.Font, FontStyle.Regular);
            row.Cells[0].Value = "    ↳ " + office.OfficeLocation;
            row.Cells[1].Value = office.BusinessBasicUsed;
            row.Cells[2].Value = office.BusinessStandardUsed;
            row.Cells[3].Value = office.ExchangeOnlinePlan1Used;
            row.Cells[4].Value = office.TeamsEssentialsUsed;
            row.Cells[5].Value = office.TotalUsed;
        }

        private void ToggleMicrosoft365Expansion(string tenantId)
        {
            if (string.IsNullOrWhiteSpace(tenantId))
                return;

            if (_expandedMicrosoft365ClientKeys.Contains(tenantId))
                _expandedMicrosoft365ClientKeys.Remove(tenantId);
            else
                _expandedMicrosoft365ClientKeys.Add(tenantId);

            if (_microsoft365Inventories != null)
                PopulateMicrosoft365Grid(_microsoft365Inventories);
        }

        private static string GetMicrosoft365ExpansionKey(Microsoft365CustomerLicenseInventory inventory)
        {
            var tenantId = inventory.TenantId;
            if (!string.IsNullOrWhiteSpace(tenantId))
                return tenantId.Trim();

            if (!string.IsNullOrWhiteSpace(inventory.CustomerId))
                return inventory.CustomerId.Trim();

            return string.Empty;
        }

        private static string GetMicrosoft365OfficeKey(Microsoft365CustomerLicenseInventory inventory, string officeLocation)
        {
            var tenantId = GetMicrosoft365ExpansionKey(inventory);
            var normalizedOfficeLocation = string.IsNullOrWhiteSpace(officeLocation) ? "Sans bureau" : officeLocation.Trim();
            return $"microsoft365:office:{tenantId}:{normalizedOfficeLocation}";
        }

        private static string BuildMicrosoft365HighlightKey(Microsoft365CustomerLicenseInventory inventory)
        {
            if (!string.IsNullOrWhiteSpace(inventory.CustomerId))
                return $"microsoft365:customer:{inventory.CustomerId.Trim()}";

            var tenantId = inventory.TenantId;
            if (!string.IsNullOrWhiteSpace(tenantId))
                return $"microsoft365:customer:{tenantId.Trim()}";

            var domain = inventory.Domain?.Trim().ToLowerInvariant() ?? string.Empty;
            return $"microsoft365:customer:domain:{domain}";
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

            // create tab control for NinjaOne, Zyxel and Microsoft 365
            mainTabControl = new TabControl { Dock = DockStyle.Fill };
            tabNinjaPage = new TabPage("NinjaOne");
            tabZyxelPage = new TabPage("Zyxel Nebula");
            tabMicrosoftPage = new TabPage("Microsoft 365");

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
            tabNinjaPage.Controls.Add(ninjaLayout);

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
            tabZyxelPage.Controls.Add(zyxelLayout);

            // Microsoft 365 tab layout
            var microsoftLayout = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = false, ColumnCount = 1, RowCount = 2 };
            microsoftLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            microsoftLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

            var microsoftCommandBar = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                WrapContents = true,
                FlowDirection = FlowDirection.LeftToRight,
                Padding = new Padding(8),
                Margin = new Padding(4)
            };

            var lblMicrosoftHistory = new Label { Text = "Historique :", AutoSize = true, Anchor = AnchorStyles.Left | AnchorStyles.Top };
            cmbMicrosoftHistory = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 320, Anchor = AnchorStyles.Left | AnchorStyles.Top };
            cmbMicrosoftHistory.SelectedIndexChanged += CmbMicrosoftHistory_SelectedIndexChanged;
            btnDownloadMicrosoft365 = new Button { Text = "Récupérer les licences Microsoft 365", AutoSize = true };
            btnDownloadMicrosoft365.Click += BtnDownloadMicrosoft365_Click;
            btnTestMicrosoftGraphAssociationDuMay = new Button { Text = "Tester Graph — Association du May", AutoSize = true };
            btnTestMicrosoftGraphAssociationDuMay.Click += BtnTestMicrosoftGraphAssociationDuMay_Click;
            lblMicrosoftStatus = new Label { Text = string.Empty, AutoSize = true, Anchor = AnchorStyles.Left | AnchorStyles.Right };
            microsoftCommandBar.Controls.Add(lblMicrosoftHistory);
            microsoftCommandBar.Controls.Add(cmbMicrosoftHistory);
            microsoftCommandBar.Controls.Add(btnDownloadMicrosoft365);
            microsoftCommandBar.Controls.Add(btnTestMicrosoftGraphAssociationDuMay);
            microsoftCommandBar.Controls.Add(lblMicrosoftStatus);

            dgvMicrosoft365 = new DataGridView
            {
                Dock = DockStyle.Fill,
                ReadOnly = true,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                AllowUserToResizeRows = false,
                RowHeadersVisible = false,
                MultiSelect = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                AutoGenerateColumns = false,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None,
                BackgroundColor = dgvInventory.BackgroundColor
            };

            var microsoftClientCol = new DataGridViewTextBoxColumn { Name = "MicrosoftClient", HeaderText = "Client", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill, MinimumWidth = 250, SortMode = DataGridViewColumnSortMode.NotSortable };
            var microsoftBasicCol = new DataGridViewTextBoxColumn { Name = "MicrosoftBasic", HeaderText = "Business Basic", Width = 120, MinimumWidth = 105, SortMode = DataGridViewColumnSortMode.NotSortable };
            microsoftBasicCol.DefaultCellStyle = new DataGridViewCellStyle { Alignment = DataGridViewContentAlignment.MiddleCenter };
            microsoftBasicCol.HeaderCell.Style.Alignment = DataGridViewContentAlignment.MiddleCenter;
            var microsoftStandardCol = new DataGridViewTextBoxColumn { Name = "MicrosoftStandard", HeaderText = "Business Standard", Width = 125, MinimumWidth = 105, SortMode = DataGridViewColumnSortMode.NotSortable };
            microsoftStandardCol.DefaultCellStyle = new DataGridViewCellStyle { Alignment = DataGridViewContentAlignment.MiddleCenter };
            microsoftStandardCol.HeaderCell.Style.Alignment = DataGridViewContentAlignment.MiddleCenter;
            var microsoftExchangeCol = new DataGridViewTextBoxColumn { Name = "MicrosoftExchange", HeaderText = "Exchange Online P1", Width = 125, MinimumWidth = 105, SortMode = DataGridViewColumnSortMode.NotSortable };
            microsoftExchangeCol.DefaultCellStyle = new DataGridViewCellStyle { Alignment = DataGridViewContentAlignment.MiddleCenter };
            microsoftExchangeCol.HeaderCell.Style.Alignment = DataGridViewContentAlignment.MiddleCenter;
            var microsoftTeamsCol = new DataGridViewTextBoxColumn { Name = "MicrosoftTeams", HeaderText = "Teams Essentials", Width = 120, MinimumWidth = 105, SortMode = DataGridViewColumnSortMode.NotSortable };
            microsoftTeamsCol.DefaultCellStyle = new DataGridViewCellStyle { Alignment = DataGridViewContentAlignment.MiddleCenter };
            microsoftTeamsCol.HeaderCell.Style.Alignment = DataGridViewContentAlignment.MiddleCenter;
            var microsoftTotalCol = new DataGridViewTextBoxColumn { Name = "MicrosoftTotal", HeaderText = "Total utilisé", Width = 110, MinimumWidth = 105, SortMode = DataGridViewColumnSortMode.NotSortable };
            microsoftTotalCol.DefaultCellStyle = new DataGridViewCellStyle { Alignment = DataGridViewContentAlignment.MiddleCenter };
            microsoftTotalCol.HeaderCell.Style.Alignment = DataGridViewContentAlignment.MiddleCenter;

            dgvMicrosoft365.Columns.AddRange(new DataGridViewColumn[] { microsoftClientCol, microsoftBasicCol, microsoftStandardCol, microsoftExchangeCol, microsoftTeamsCol, microsoftTotalCol });
            dgvMicrosoft365.CellMouseClick += DgvMicrosoft365_CellMouseClick;

            microsoftLayout.Controls.Add(microsoftCommandBar, 0, 0);
            microsoftLayout.Controls.Add(dgvMicrosoft365, 0, 1);
            tabMicrosoftPage.Controls.Add(microsoftLayout);

            mainTabControl.TabPages.Add(tabNinjaPage);
            mainTabControl.TabPages.Add(tabZyxelPage);
            mainTabControl.TabPages.Add(tabMicrosoftPage);

            mainLayout.Controls.Add(mainTabControl, 0, 1);
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

        private async void BtnTestMicrosoftGraphAssociationDuMay_Click(object? sender, EventArgs e)
        {
            if (_isMicrosoftGraphTestInProgress)
            {
                return;
            }

            var confirm = MessageBox.Show(this,
                "Le test va récupérer les utilisateurs, les bureaux et les licences attribuées du tenant Association du May.\n\nVoulez-vous continuer ?",
                "Test Microsoft Graph",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question,
                MessageBoxDefaultButton.Button2);

            if (confirm != DialogResult.Yes)
                return;

            _isMicrosoftGraphTestInProgress = true;
            try
            {
                btnSettings.Enabled = false;
                btnDownloadDevices.Enabled = false;
                btnDownloadZyxel.Enabled = false;
                btnDownloadMicrosoft365.Enabled = false;
                btnTestMicrosoftGraphAssociationDuMay.Enabled = false;
                cmbHistory.Enabled = false;
                cmbZyxelHistory.Enabled = false;
                cmbMicrosoftHistory.Enabled = false;
                this.UseWaitCursor = true;
                this.Cursor = Cursors.WaitCursor;
                dgvInventory.UseWaitCursor = true;
                dgvInventory.Cursor = Cursors.WaitCursor;
                dgvZyxel.UseWaitCursor = true;
                dgvZyxel.Cursor = Cursors.WaitCursor;
                dgvMicrosoft365.UseWaitCursor = true;
                dgvMicrosoft365.Cursor = Cursors.WaitCursor;
                lblMicrosoftStatus.Text = "Connexion à Microsoft Graph pour Association du May...";

                var progress = new Progress<string>(message => lblMicrosoftStatus.Text = message);
                var service = new MicrosoftGraphCustomerUserTestService();
                var result = await service.TestAsync(AssociationDuMayTenantId, this.Handle, progress);

                var summary = $"Connexion à Microsoft Graph réussie. Compte : {result.AccountName} — Utilisateurs : {result.UserCount} — Utilisateurs avec licence : {result.LicensedUserCount} — Utilisateurs avec bureau renseigné : {result.UserWithOfficeCount} — Bureaux distincts : {result.OfficeCount} — Affectations de licences : {result.AssignedLicenseCount}";
                lblMicrosoftStatus.Text = summary;

                MessageBox.Show(this,
                    $"Connexion à Microsoft Graph réussie.\n\nCompte : {result.AccountName}\n\nUtilisateurs : {result.UserCount}\n\nUtilisateurs avec licence : {result.LicensedUserCount}\n\nUtilisateurs avec bureau renseigné : {result.UserWithOfficeCount}\n\nBureaux distincts : {result.OfficeCount}\n\nAffectations de licences : {result.AssignedLicenseCount}\n\nFichier :\n{result.FilePath}",
                    "Test Microsoft Graph réussi",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "Erreur Microsoft Graph", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                _isMicrosoftGraphTestInProgress = false;
                btnSettings.Enabled = true;
                btnDownloadDevices.Enabled = true;
                btnDownloadZyxel.Enabled = true;
                btnDownloadMicrosoft365.Enabled = true;
                btnTestMicrosoftGraphAssociationDuMay.Enabled = true;
                cmbHistory.Enabled = true;
                cmbZyxelHistory.Enabled = true;
                cmbMicrosoftHistory.Enabled = true;
                this.UseWaitCursor = false;
                this.Cursor = Cursors.Default;
                dgvInventory.UseWaitCursor = false;
                dgvInventory.Cursor = Cursors.Default;
                dgvZyxel.UseWaitCursor = false;
                dgvZyxel.Cursor = Cursors.Default;
                dgvMicrosoft365.UseWaitCursor = false;
                dgvMicrosoft365.Cursor = Cursors.Default;
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

        private async void CmbMicrosoftHistory_SelectedIndexChanged(object? sender, EventArgs e)
        {
            if (_isUpdatingMicrosoft365HistoryComboBox)
                return;
            if (cmbMicrosoftHistory.SelectedItem is not Microsoft365LicenseHistoryItem item)
                return;

            await LoadMicrosoft365HistoryItemAsync(item);
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
                await LoadMicrosoft365HistoryAsync();
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

        private async Task LoadMicrosoft365HistoryAsync(string? selectFilePath = null)
        {
            try
            {
                var items = await _microsoft365HistoryService.GetHistoryAsync();
                _isUpdatingMicrosoft365HistoryComboBox = true;
                cmbMicrosoftHistory.Items.Clear();
                cmbMicrosoftHistory.DisplayMember = "DisplayText";
                foreach (var item in items)
                    cmbMicrosoftHistory.Items.Add(item);

                if (cmbMicrosoftHistory.Items.Count == 0)
                {
                    lblMicrosoftStatus.Text = "Aucune extraction Microsoft 365 disponible.";
                    dgvMicrosoft365.Rows.Clear();
                }
                else
                {
                    object? toSelect = null;
                    if (!string.IsNullOrEmpty(selectFilePath))
                        toSelect = items.FirstOrDefault(x => string.Equals(x.FilePath, selectFilePath, StringComparison.OrdinalIgnoreCase));
                    if (toSelect == null)
                        toSelect = cmbMicrosoftHistory.Items[0];
                    cmbMicrosoftHistory.SelectedItem = toSelect;
                }
            }
            catch
            {
                // ignore
            }
            finally
            {
                _isUpdatingMicrosoft365HistoryComboBox = false;
            }

            try
            {
                if (cmbMicrosoftHistory.Items.Count > 0 && cmbMicrosoftHistory.SelectedItem is Microsoft365LicenseHistoryItem selected)
                    await LoadMicrosoft365HistoryItemAsync(selected);
            }
            catch (Exception ex)
            {
                lblMicrosoftStatus.Text = "Impossible de charger la dernière extraction Microsoft 365.";
                MessageBox.Show(this, ex.Message, "Erreur lecture historique Microsoft 365", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private async Task LoadMicrosoft365HistoryItemAsync(Microsoft365LicenseHistoryItem item)
        {
            try
            {
                var inventories = await _microsoft365InventoryReader.ReadAsync(item.FilePath);
                PopulateMicrosoft365Grid(inventories);

                var totalBasic = inventories.Sum(x => x.BusinessBasicUsed);
                var totalStandard = inventories.Sum(x => x.BusinessStandardUsed);
                var totalExchange = inventories.Sum(x => x.ExchangeOnlinePlan1Used);
                var totalTeams = inventories.Sum(x => x.TeamsEssentialsUsed);
                var totalGlobal = inventories.Sum(x => x.TotalUsed);
                var clientsWithDetails = inventories.Count(x => x.Offices != null && x.Offices.Count > 0);
                var clientsWithoutGraph = inventories.Count(x => !string.Equals(x.GraphStatus, "Success", StringComparison.OrdinalIgnoreCase));
                var officesTotal = inventories.Sum(x => x.Offices?.Count ?? 0);

                lblMicrosoftStatus.Text = $"{item.RetrievedAtUtc.ToLocalTime():dd/MM/yyyy HH:mm} — {inventories.Count} clients — {totalBasic} Business Basic — {totalStandard} Business Standard — {totalExchange} Exchange Online P1 — {totalTeams} Teams Essentials — {totalGlobal} licences utilisées — {clientsWithDetails} client(s) avec détails par bureau — {clientsWithoutGraph} client(s) sans accès Graph — {officesTotal} bureau(x)";
            }
            catch (Exception ex)
            {
                lblMicrosoftStatus.Text = "Impossible de charger l'extraction Microsoft 365.";
                MessageBox.Show(this, ex.Message, "Erreur lecture historique Microsoft 365", MessageBoxButtons.OK, MessageBoxIcon.Error);
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

        private async void BtnDownloadMicrosoft365_Click(object? sender, EventArgs e)
        {
            if (_isMicrosoft365DownloadInProgress)
            {
                return;
            }

            _isMicrosoft365DownloadInProgress = true;
            try
            {
                var confirm = MessageBox.Show(this,
                    "Une nouvelle extraction des licences Microsoft 365 va être lancée.\n\nLes anciennes extractions seront conservées.\n\nUne fenêtre de connexion Microsoft pourra s’ouvrir.\n\nVoulez-vous continuer ?",
                    "Nouvelle extraction Microsoft 365",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question,
                    MessageBoxDefaultButton.Button2);

                if (confirm != DialogResult.Yes)
                    return;

                if (!MicrosoftPartnerSettingsService.IsConfigured())
                {
                    using var settingsForm = new ApplicationSettingsForm();
                    settingsForm.ShowDialog(this);
                    if (!MicrosoftPartnerSettingsService.IsConfigured())
                        return;
                }

                btnSettings.Enabled = false;
                btnDownloadDevices.Enabled = false;
                btnDownloadZyxel.Enabled = false;
                btnDownloadMicrosoft365.Enabled = false;
                cmbHistory.Enabled = false;
                cmbZyxelHistory.Enabled = false;
                this.UseWaitCursor = true;
                this.Cursor = Cursors.WaitCursor;
                dgvInventory.UseWaitCursor = true;
                dgvInventory.Cursor = Cursors.WaitCursor;
                dgvZyxel.UseWaitCursor = true;
                dgvZyxel.Cursor = Cursors.WaitCursor;
                lblMicrosoftStatus.Text = "Connexion à Microsoft Partner Center...";

                var progress = new Progress<string>(message => lblMicrosoftStatus.Text = message);

                var client = new MicrosoftPartnerLicenseInventoryApiClient();
                var result = await client.DownloadAllAsync(this.Handle, progress, CancellationToken.None);

                lblMicrosoftStatus.Text = $"{result.ProcessedCustomerCount} clients traités — {result.SkippedCustomerCount} ignorés — {result.SubscribedSkuCount} références de licences — Microsoft Graph : {result.GraphProcessedCustomerCount} client(s) traité(s), {result.GraphSkippedCustomerCount} client(s) non accessible(s), {result.GraphUserCount} utilisateur(s) récupéré(s), {result.GraphOfficeCount} bureau(x) trouvé(s).";

                await LoadMicrosoft365HistoryAsync(result.FilePath);

                var messageBoxIcon = result.SkippedCustomerCount > 0 ? MessageBoxIcon.Warning : MessageBoxIcon.Information;
                MessageBox.Show(this,
                    $"{result.CustomerCount} client(s) Partner Center ont été trouvés.\n\n{result.ProcessedCustomerCount} client(s) ont été traités.\n\n{result.SkippedCustomerCount} client(s) ont été ignorés.\n\n{result.SubscribedSkuCount} référence(s) de licences ont été récupérées.\n\nMicrosoft Graph :\n\n{result.GraphProcessedCustomerCount} client(s) traité(s).\n\n{result.GraphSkippedCustomerCount} client(s) non accessible(s).\n\n{result.GraphUserCount} utilisateur(s) récupéré(s).\n\n{result.GraphOfficeCount} bureau(x) trouvé(s).\n\nFichier :\n{result.FilePath}",
                    "Extraction Microsoft 365 terminée",
                    MessageBoxButtons.OK,
                    messageBoxIcon);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "Erreur Microsoft 365", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                _isMicrosoft365DownloadInProgress = false;
                btnSettings.Enabled = true;
                btnDownloadDevices.Enabled = true;
                btnDownloadZyxel.Enabled = true;
                btnDownloadMicrosoft365.Enabled = true;
                cmbHistory.Enabled = true;
                cmbZyxelHistory.Enabled = true;
                this.UseWaitCursor = false;
                this.Cursor = Cursors.Default;
                dgvInventory.UseWaitCursor = false;
                dgvInventory.Cursor = Cursors.Default;
                dgvZyxel.UseWaitCursor = false;
                dgvZyxel.Cursor = Cursors.Default;
            }
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
