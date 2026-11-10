using System.Drawing;
using System.Windows.Forms;

namespace LockScreenCampaignManager;

internal sealed class UiSettingsDialog : Form
{
    public UiSettingsDialog()
    {
        Name = "UiSettingsDialog";
        Text = "Interface settings";
        StartPosition = FormStartPosition.CenterParent;
        AutoScaleMode = AutoScaleMode.Dpi;
        Font = new Font("Segoe UI", 9.5f);
        MinimumSize = new Size(535, 420);
        Size = new Size(670, 500);

        var viewport = new Panel {
            Name = "UiSettingsScrollableViewport",
            Dock = DockStyle.Fill, AutoScroll = true
        };
        Controls.Add(viewport);
        var panel = new TableLayoutPanel {
            Name = "UiSettingsFields", Dock = DockStyle.Top,
            AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 2, RowCount = 6, Padding = new Padding(18, 14, 18, 18)
        };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 46));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 54));
        for (int i = 0; i < 4; i++)
            panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 62));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 100));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 65));
        viewport.Controls.Add(panel);

        static ComboBox Combo(string name) => new() {
            Name = name, Dock = DockStyle.Fill,
            DropDownStyle = ComboBoxStyle.DropDownList,
            Margin = new Padding(5, 7, 5, 9)
        };
        void AddLabel(int row, string name) => panel.Controls.Add(new Label {
            Text = name, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft,
            AutoEllipsis = false, Margin = new Padding(5, 8, 6, 8)
        }, 0, row);
        var language = Combo("UiSettingsLanguage");
        language.Items.AddRange(["English", "עברית (Hebrew)"]);
        language.SelectedIndex = UiStyle.Preferences.Language == "Hebrew" ? 1 : 0;
        AddLabel(0, "Language");
        panel.Controls.Add(language, 1, 0);

        var density = Combo("UiSettingsDensity");
        var densities = new[] { "Auto", "Compact", "Comfortable" };
        density.Items.AddRange(densities.Select(x =>
            (object)UiLocale.Translate(x, UiStyle.Preferences.Language)).ToArray());
        density.SelectedIndex = Math.Max(0,
            Array.IndexOf(densities, UiStyle.Preferences.Density));
        AddLabel(1, "UI density");
        panel.Controls.Add(density, 1, 1);

        var scale = Combo("UiSettingsScale");
        scale.Items.AddRange(["100%", "110%", "125%", "150%", "175%", "200%"]);
        scale.SelectedItem = UiStyle.Preferences.UiScalePercent + "%";
        AddLabel(2, "UI scale");
        panel.Controls.Add(scale, 1, 2);

        var fonts = Combo("UiSettingsFontScale");
        fonts.Items.AddRange(["90%", "100%", "110%", "125%"]);
        fonts.SelectedItem = UiStyle.Preferences.FontScalePercent + "%";
        AddLabel(3, "Font scale");
        panel.Controls.Add(fonts, 1, 3);

        var info = new Label {
            Dock = DockStyle.Fill, AutoSize = false, AutoEllipsis = false,
            Text = UiStyle.Preferences.Language == "Hebrew"
                ? "Windows DPI מזוהה אוטומטית. השפה והגדרות התצוגה נשמרות למשתמש הנוכחי. " +
                  "פתח מחדש חלונות קיימים כדי לרענן טקסט דינמי שטרם תורגם."
                : "Windows DPI is detected automatically (PerMonitorV2). " +
                  "Language and display settings are saved for the current user. " +
                  "Reopen existing windows to refresh dynamic text not yet localized.",
            ForeColor = UiStyle.Muted, TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(5, 10, 5, 5)
        };
        panel.Controls.Add(info, 0, 4);
        panel.SetColumnSpan(info, 2);

        var save = new Button {
            Name = "UiSettingsApply", Text = "Apply",
            Width = 165, Height = 42, BackColor = UiStyle.Accent,
            ForeColor = Color.White, Dock = DockStyle.Right,
            Margin = new Padding(5, 6, 5, 4)
        };
        save.Click += (_, _) =>
        {
            try
            {
                UiStyle.ChangePreferences(new UiPreferences {
                    Language = language.SelectedIndex == 1 ? "Hebrew" : "English",
                    Density = densities[Math.Clamp(density.SelectedIndex, 0, 2)],
                    UiScalePercent = int.Parse(((string)(scale.SelectedItem ?? "100%")).TrimEnd('%')),
                    FontScalePercent = int.Parse(((string)(fonts.SelectedItem ?? "100%")).TrimEnd('%'))
                });
                DialogResult = DialogResult.OK;
            }
            catch (Exception ex) { UiErrorDialog.Show(this, "UI settings", ex); }
        };
        panel.Controls.Add(save, 1, 5);
        UiStyle.Apply(this);
        UiWindowPlacement.Attach(this, "ui-settings");
    }
}
