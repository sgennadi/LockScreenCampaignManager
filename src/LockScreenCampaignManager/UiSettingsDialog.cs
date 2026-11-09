using System.Drawing;
using System.Windows.Forms;
namespace LockScreenCampaignManager;

internal sealed class UiSettingsDialog : Form
{
    public UiSettingsDialog()
    {
        Text = "Interface settings";
        StartPosition = FormStartPosition.CenterParent;
        AutoScaleMode = AutoScaleMode.Dpi;
        Font = new Font("Segoe UI", 9.5f);
        MinimumSize = new Size(520, 360);
        Size = new Size(650, 410);
        var panel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 5,
            Padding = new Padding(16) };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 48));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 52));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 60));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 61));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 61));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 62));
        Controls.Add(panel);
        static ComboBox Combo() => new() { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList };
        void AddLabel(int row, string name) => panel.Controls.Add(new Label {
            Text = name, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft
        }, 0, row);
        var density = Combo();
        density.Items.AddRange(new object[] { "Auto", "Compact", "Comfortable" });
        density.SelectedItem = UiStyle.Preferences.Density;
        AddLabel(0, "UI density"); panel.Controls.Add(density, 1, 0);
        var scale = Combo();
        scale.Items.AddRange(new object[] { "100%", "110%", "125%", "150%", "175%", "200%" });
        scale.SelectedItem = UiStyle.Preferences.UiScalePercent + "%";
        AddLabel(1, "UI scale"); panel.Controls.Add(scale, 1, 1);
        var fonts = Combo();
        fonts.Items.AddRange(new object[] { "90%", "100%", "110%", "125%" });
        fonts.SelectedItem = UiStyle.Preferences.FontScalePercent + "%";
        AddLabel(2, "Font scale"); panel.Controls.Add(fonts, 1, 2);
        var info = new Label { Dock = DockStyle.Fill, Text = "Windows DPI is detected automatically (PerMonitorV2). " +
            "Settings are saved for the current user.", ForeColor = UiStyle.Muted };
        panel.Controls.Add(info, 0, 3); panel.SetColumnSpan(info, 2);
        var save = new Button { Text = "Apply", Width = 150, Height = 38, BackColor = UiStyle.Accent,
            ForeColor = Color.White, Dock = DockStyle.Right };
        save.Click += (_, _) =>
        {
            try
            {
                UiStyle.ChangePreferences(new UiPreferences {
                    Density = (string)(density.SelectedItem ?? "Auto"),
                    UiScalePercent = int.Parse(((string)(scale.SelectedItem ?? "100%")).TrimEnd('%')),
                    FontScalePercent = int.Parse(((string)(fonts.SelectedItem ?? "100%")).TrimEnd('%'))
                });
                DialogResult = DialogResult.OK;
            }
            catch (Exception ex) { UiErrorDialog.Show(this, "UI settings", ex); }
        };
        panel.Controls.Add(save, 1, 4);
        UiStyle.Apply(this);
        UiWindowPlacement.Attach(this, "ui-settings");
    }
}
