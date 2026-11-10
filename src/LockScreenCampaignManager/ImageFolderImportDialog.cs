using System.Drawing;
using System.Windows.Forms;

namespace LockScreenCampaignManager;

/// <summary>
/// Read-only preview of filename/day inference. Nothing is assigned to a campaign
/// until the operator explicitly accepts a complete, unique image mapping.
/// </summary>
internal sealed class ImageFolderImportDialog : Form
{
    private sealed record FileChoice(string Label, string Path);

    private readonly CampaignProject _source;
    private ImageFilenameMatcher.Proposal? _proposal;
    private readonly Label _intro = new();
    private readonly Label _notice = new();
    private readonly TextBox _folder = new();
    private readonly DataGridView _grid = new();
    private readonly CheckBox _approveFallback = new();
    private readonly Button _accept = new();
    private Button _browse = new();
    private bool _loading;
    private bool _hasManualOrUncertain;
    internal CampaignProject? ResultProject { get; private set; }

    internal ImageFolderImportDialog(CampaignProject source)
    {
        _source = source;
        Text = "Import folder - Match images to schedule";
        AutoScaleMode = AutoScaleMode.Dpi;
        Font = new Font("Segoe UI", 9.5f);
        StartPosition = FormStartPosition.CenterParent;
        MinimumSize = new Size(870, 610);
        Size = new Size(1120, 765);
        BackColor = UiStyle.Canvas;
        Build();
        UiStyle.Apply(this);
        UiWindowPlacement.Attach(this, "image-folder-import");
    }

    private static Button MakeButton(string text, int width, Action action)
    {
        var result = new Button {
            Text = UiLocale.Display(text), Width = width, Height = 38,
            MinimumSize = new Size(width, 38), AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowOnly, AutoEllipsis = false,
            Margin = new Padding(2, 4, 8, 4)
        };
        result.Click += (_, _) => action();
        return result;
    }

    private void Build()
    {
        var layout = new TableLayoutPanel {
            Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5,
            Padding = new Padding(15), BackColor = UiStyle.Canvas
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 89));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 59));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 90));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));
        Controls.Add(layout);
        _intro.Dock = DockStyle.Fill;
        _intro.Font = new Font("Segoe UI Semibold", 13f);
        _intro.ForeColor = UiStyle.Foreground;
        _intro.Text = "Choose a folder after setting schedule days / hours" +
            Environment.NewLine +
            "Names such as Monday.jpg, fri_03.png, 01.jpg or 2026-11-09.jpg " +
            "can suggest a match. Ambiguities are never applied automatically.";
        layout.Controls.Add(_intro, 0, 0);

        var pathBar = new TableLayoutPanel {
            Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1
        };
        pathBar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        pathBar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 170));
        _folder.ReadOnly = true;
        _folder.Dock = DockStyle.Fill;
        _folder.RightToLeft = RightToLeft.No;
        _folder.PlaceholderText = "Choose a folder with up to 30 images...";
        pathBar.Controls.Add(_folder, 0, 0);
        _browse = MakeButton("Choose folder...", 151, Browse);
        pathBar.Controls.Add(_browse, 1, 0);
        layout.Controls.Add(pathBar, 0, 1);

        _grid.Name = "ImageFolderMappingGrid";
        _grid.Dock = DockStyle.Fill;
        _grid.ReadOnly = false;
        _grid.AllowUserToAddRows = false;
        _grid.AllowUserToDeleteRows = false;
        _grid.RowHeadersVisible = false;
        _grid.MultiSelect = false;
        _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        _grid.Columns.Add(new DataGridViewTextBoxColumn {
            HeaderText = "Slot", ReadOnly = true, FillWeight = 35
        });
        _grid.Columns.Add(new DataGridViewTextBoxColumn {
            HeaderText = "Scheduled day / time", ReadOnly = true, FillWeight = 119
        });
        _grid.Columns.Add(new DataGridViewComboBoxColumn {
            HeaderText = "Image (manual override allowed)", FillWeight = 205,
            DisplayStyle = DataGridViewComboBoxDisplayStyle.DropDownButton,
            FlatStyle = FlatStyle.Flat
        });
        _grid.Columns.Add(new DataGridViewTextBoxColumn {
            HeaderText = "Matching explanation", ReadOnly = true, FillWeight = 186
        });
        _grid.CurrentCellDirtyStateChanged += (_, _) => {
            if (_grid.IsCurrentCellDirty) _grid.CommitEdit(DataGridViewDataErrorContexts.Commit);
        };
        _grid.CellValueChanged += (_, e) => {
            if (_loading || e.RowIndex < 0 || e.ColumnIndex != 2) return;
            _grid.Rows[e.RowIndex].Cells[3].Value = "Manually selected / requires review";
            _hasManualOrUncertain = true;
            _approveFallback.Checked = false;
            RefreshNotice();
        };
        _grid.DataError += (_, _) => {
            _accept.Enabled = false;
            _notice.Text = "The selected image is not in this folder. Re-scan before applying.";
        };
        layout.Controls.Add(_grid, 0, 2);

        var feedback = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2 };
        feedback.RowStyles.Add(new RowStyle(SizeType.Absolute, 47));
        feedback.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        _notice.Dock = DockStyle.Fill;
        _notice.Text = "No folder selected. Filename matching is a suggestion, not a deployment.";
        _notice.ForeColor = UiStyle.Muted;
        _notice.AutoEllipsis = true;
        feedback.Controls.Add(_notice, 0, 0);
        _approveFallback.Dock = DockStyle.Fill;
        _approveFallback.Text = "I reviewed ambiguous / manual assignments";
        _approveFallback.CheckedChanged += (_, _) => RefreshNotice();
        feedback.Controls.Add(_approveFallback, 0, 1);
        layout.Controls.Add(feedback, 0, 3);

        var commands = new FlowLayoutPanel {
            Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft,
            WrapContents = true, AutoScroll = true
        };
        commands.Controls.Add(MakeButton("Cancel", 120, () => {
            DialogResult = DialogResult.Cancel;
            Close();
        }));
        _accept.Text = "Apply image mapping";
        _accept.Width = 190;
        _accept.MinimumSize = new Size(190, 38);
        _accept.AutoSize = true;
        _accept.AutoSizeMode = AutoSizeMode.GrowOnly;
        _accept.Height = 38;
        _accept.BackColor = UiStyle.Accent;
        _accept.ForeColor = Color.White;
        _accept.Click += (_, _) => Accept();
        _accept.Enabled = false;
        commands.Controls.Add(_accept);
        layout.Controls.Add(commands, 0, 4);
        AcceptButton = _accept;
    }

    private void Browse()
    {
        using var dialog = new FolderBrowserDialog {
            Description = "Choose the folder containing the campaign images.",
            UseDescriptionForTitle = true,
            ShowNewFolderButton = false
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            var files = ImageFilenameMatcher.ScanFolder(dialog.SelectedPath);
            var proposal = ImageFilenameMatcher.Suggest(_source, files);
            ShowProposal(proposal, dialog.SelectedPath);
        }
        catch (Exception ex)
        {
            _proposal = null;
            _grid.Rows.Clear();
            _accept.Enabled = false;
            _notice.Text = UiLocale.Display("Import failed: ") +
                UniversalDiagnosticsEmbedded.Clean(ex.Message, 175);
            UiErrorDialog.Show(this, "Import image folder", ex);
        }
    }

    private void ShowProposal(ImageFilenameMatcher.Proposal proposal, string folder)
    {
        _proposal = proposal;
        _folder.Text = folder;
        _hasManualOrUncertain = proposal.ReviewCount != 0;
        _approveFallback.Checked = false;
        var choices = new List<FileChoice> { new("(Choose image manually)", "") };
        choices.AddRange(proposal.Files.Select(path =>
            new FileChoice(Path.GetFileName(path), path)));
        var imageColumn = (DataGridViewComboBoxColumn)_grid.Columns[2];
        _loading = true;
        try
        {
            _grid.Rows.Clear();
            imageColumn.DataSource = null;
            imageColumn.DataSource = choices;
            imageColumn.DisplayMember = nameof(FileChoice.Label);
            imageColumn.ValueMember = nameof(FileChoice.Path);
            foreach (var match in proposal.Slots)
            {
                // Only unique, strong filename/day agreements are preselected.
                // Weak or ambiguous matches stay unassigned.
                string picked = match.Strong ? match.SuggestedPath ?? "" : "";
                _grid.Rows.Add(match.Number.ToString("D2"),
                    match.LocalTime.ToString("dd.MM.yyyy HH:mm"),
                    picked, match.Reason);
            }
        }
        finally { _loading = false; }
        RefreshNotice();
    }

    private void RefreshNotice()
    {
        if (_proposal is null)
        {
            _accept.Enabled = false;
            return;
        }
        var selected = _grid.Rows.Cast<DataGridViewRow>()
            .Select(row => Convert.ToString(row.Cells[2].Value) ?? "")
            .ToArray();
        int missing = selected.Count(string.IsNullOrWhiteSpace);
        int duplicated = selected.Where(x => x.Length > 0)
            .GroupBy(x => x, StringComparer.OrdinalIgnoreCase)
            .Sum(g => Math.Max(0, g.Count() - 1));
        _accept.Enabled = missing == 0 && duplicated == 0 &&
            (!_hasManualOrUncertain || _approveFallback.Checked);
        _notice.Text = $"{_proposal.ConfidentCount}/{_proposal.Slots.Count} strong filename matches. " +
            $"Unassigned: {missing}. Duplicate mappings: {duplicated}. " +
            (_hasManualOrUncertain ? "Review every remaining row before applying." :
                "All proposed assignments match their scheduled slots.");
    }

    private void Accept()
    {
        if (_proposal is null) return;
        RefreshNotice();
        if (!_accept.Enabled) return;
        try
        {
            var selected = _grid.Rows.Cast<DataGridViewRow>()
                .Select(row => Convert.ToString(row.Cells[2].Value) ?? "")
                .ToList();
            if (selected.Count != _proposal.Files.Count ||
                selected.Any(string.IsNullOrWhiteSpace) ||
                selected.Distinct(StringComparer.OrdinalIgnoreCase).Count() != selected.Count ||
                selected.Any(s => !_proposal.Files.Contains(s, StringComparer.OrdinalIgnoreCase)))
                throw new InvalidDataException("Incomplete or duplicate image mapping.");
            var project = _proposal.Schedule;
            project.ImagePaths = selected;
            CampaignSchedule.Validate(project, requireImages: true);
            ResultProject = project;
            DialogResult = DialogResult.OK;
            Close();
        }
        catch (Exception ex)
        {
            UiErrorDialog.Show(this, "Validate imported image assignments", ex);
        }
    }
}
