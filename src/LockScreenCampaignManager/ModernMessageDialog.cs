using System.Drawing;
using System.Windows.Forms;

namespace LockScreenCampaignManager;

/// <summary>
/// App-wide compatibility facade. Existing MessageBox.Show(...) call sites in
/// this namespace now use the same modern, themed, high-DPI dialog. Keeping
/// the MessageBox signature preserves Yes/No/Cancel, default-button, Escape
/// and close semantics without changing any AD/GPO/update action.
/// Native OS file pickers and security prompts are intentionally untouched.
/// </summary>
internal static class MessageBox
{
    internal static DialogResult Show(string? text, string? caption = null,
        MessageBoxButtons buttons = MessageBoxButtons.OK,
        MessageBoxIcon icon = MessageBoxIcon.None,
        MessageBoxDefaultButton defaultButton = MessageBoxDefaultButton.Button1,
        MessageBoxOptions options = 0)
        => ShowCore(null, text, caption, buttons, icon, defaultButton, options);

    internal static DialogResult Show(IWin32Window? owner, string? text,
        string? caption = null, MessageBoxButtons buttons = MessageBoxButtons.OK,
        MessageBoxIcon icon = MessageBoxIcon.None,
        MessageBoxDefaultButton defaultButton = MessageBoxDefaultButton.Button1,
        MessageBoxOptions options = 0)
        => ShowCore(owner, text, caption, buttons, icon, defaultButton, options);

    private static DialogResult ShowCore(IWin32Window? owner, string? text,
        string? caption, MessageBoxButtons buttons, MessageBoxIcon icon,
        MessageBoxDefaultButton defaultButton, MessageBoxOptions options)
    {
        // Desktop-only/ServiceNotification require an OS desktop modal;
        // WinForms cannot safely replace these specific native options.
        if ((options & (MessageBoxOptions.DefaultDesktopOnly |
                        MessageBoxOptions.ServiceNotification)) != 0)
            return owner is null
                ? System.Windows.Forms.MessageBox.Show(text, caption, buttons, icon,
                    defaultButton, options)
                : System.Windows.Forms.MessageBox.Show(owner, text, caption, buttons,
                    icon, defaultButton, options);

        using var window = new ModernMessageDialog(text ?? "", caption ?? "",
            buttons, icon, defaultButton, options);
        return owner is null ? window.ShowDialog() : window.ShowDialog(owner);
    }
}

/// <summary>
/// Unified confirmation/information/error surface. All text is selectable
/// and scrollable. It is never used for untrusted automatic approval.
/// </summary>
internal sealed class ModernMessageDialog : Form
{
    private readonly Button[] _actions;
    internal DialogResult DefaultAction { get; }
    internal DialogResult SafeDismissal { get; }
    internal MessageBoxButtons ButtonsKind { get; }

    internal ModernMessageDialog(string text, string caption,
        MessageBoxButtons buttons, MessageBoxIcon icon,
        MessageBoxDefaultButton defaultButton, MessageBoxOptions options = 0)
    {
        ButtonsKind = buttons;
        Name = "ModernMessageDialog";
        Text = "LockScreen Campaign Manager - " +
            UiLocale.Display(string.IsNullOrWhiteSpace(caption)
                ? "Notification" : caption);
        StartPosition = FormStartPosition.CenterParent;
        AutoScaleMode = AutoScaleMode.Dpi;
        Font = new Font("Segoe UI", 9.5f);
        BackColor = UiStyle.Canvas;
        FormBorderStyle = FormBorderStyle.Sizable;
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = false;
        SizeGripStyle = SizeGripStyle.Hide;
        KeyPreview = true;

        // Use screen-aware bounds rather than a tiny standard MessageBox.
        // Large warnings (updater approval, AD safety checks) scroll.
        int lines = text.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Split('\n').Sum(x => Math.Max(1, (x.Length + 73) / 74));
        var display = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1280, 800);
        int minWidth = Math.Min(590, Math.Max(460, display.Width - 70));
        int minHeight = Math.Min(325, Math.Max(290, display.Height - 75));
        int width = Math.Clamp(720 + (text.Length > 850 ? 110 : 0),
            minWidth, Math.Max(minWidth, display.Width - 72));
        int height = Math.Clamp(265 + Math.Min(lines, 30) * 21,
            minHeight, Math.Max(minHeight, display.Height - 72));
        MinimumSize = new Size(minWidth, minHeight);
        Size = new Size(width, height);

        bool warning = icon is MessageBoxIcon.Warning or MessageBoxIcon.Error;
        bool error = icon is MessageBoxIcon.Error;
        bool question = icon == MessageBoxIcon.Question;
        Color tone = SystemInformation.HighContrast ? SystemColors.Highlight :
            error ? Color.FromArgb(179, 39, 48) :
            warning ? Color.FromArgb(172, 99, 11) : UiStyle.Accent;
        Color soft = SystemInformation.HighContrast ? SystemColors.Window :
            error ? Color.FromArgb(255, 239, 238) :
            warning ? Color.FromArgb(255, 245, 227) : UiStyle.SoftBlue;

        bool rtl = UiStyle.Preferences.Language == "Hebrew" ||
            (options & MessageBoxOptions.RtlReading) != 0;
        var outer = new TableLayoutPanel {
            Dock = DockStyle.Fill, BackColor = UiStyle.Canvas,
            Padding = new Padding(15), ColumnCount = 1, RowCount = 4
        };
        outer.RowStyles.Add(new RowStyle(SizeType.Absolute, 5));
        outer.RowStyles.Add(new RowStyle(SizeType.Absolute, 102));
        outer.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        outer.RowStyles.Add(new RowStyle(SizeType.Absolute, 76));
        Controls.Add(outer);
        outer.Controls.Add(new Panel {
            Name = "ModernDialogAccent", Dock = DockStyle.Fill,
            BackColor = tone, Margin = new Padding(0)
        }, 0, 0);

        var header = new TableLayoutPanel {
            Name = "ModernDialogHeader",
            Dock = DockStyle.Fill, BackColor = soft,
            Padding = new Padding(13, 10, 13, 8),
            ColumnCount = 2, RowCount = 1, Margin = Padding.Empty
        };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 59));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        var picture = new Panel {
            Name = "ModernDialogIcon",
            Dock = DockStyle.Fill, Margin = new Padding(2, 8, 8, 8),
            BackColor = soft
        };
        picture.Paint += (_, args) =>
        {
            Icon systemIcon = error ? SystemIcons.Error :
                warning ? SystemIcons.Warning :
                question ? SystemIcons.Question : SystemIcons.Information;
            int side = Math.Max(21, Math.Min(38,
                Math.Min(picture.ClientSize.Width, picture.ClientSize.Height) - 4));
            args.Graphics.DrawIcon(systemIcon,
                new Rectangle(2, 2, side, side));
        };
        header.Controls.Add(picture, 0, 0);

        var headerText = new TableLayoutPanel {
            Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2,
            BackColor = soft, Margin = Padding.Empty
        };
        headerText.RowStyles.Add(new RowStyle(SizeType.Percent, 60));
        headerText.RowStyles.Add(new RowStyle(SizeType.Percent, 40));
        var heading = new Label {
            Name = "ModernDialogHeaderTitle",
            Dock = DockStyle.Fill, BackColor = soft,
            ForeColor = UiStyle.Navy,
            Font = new Font("Segoe UI Semibold", 14),
            Text = UiLocale.Display(string.IsNullOrWhiteSpace(caption)
                ? "Notification" : caption),
            TextAlign = ContentAlignment.MiddleLeft,
            AutoEllipsis = true,
            AccessibleRole = AccessibleRole.StaticText
        };
        var captionLine = new Label {
            Name = "ModernDialogHeaderHint", Dock = DockStyle.Fill,
            ForeColor = UiStyle.Foreground, BackColor = soft,
            Text = UiLocale.Display(
                error ? "Action failed — review the details below." :
                warning ? "Please review this warning before continuing." :
                question ? "Choose an action to continue." :
                "Information from LockScreen Campaign Manager."),
            TextAlign = ContentAlignment.MiddleLeft,
            AutoEllipsis = true
        };
        if (rtl) {
            heading.TextAlign = ContentAlignment.MiddleRight;
            captionLine.TextAlign = ContentAlignment.MiddleRight;
        }
        headerText.Controls.Add(heading, 0, 0);
        headerText.Controls.Add(captionLine, 0, 1);
        header.Controls.Add(headerText, 1, 0);
        outer.Controls.Add(header, 0, 1);

        var card = new Panel {
            Name = "ModernDialogContentCard", Dock = DockStyle.Fill,
            BackColor = UiStyle.Surface,
            Padding = new Padding(23, 17, 20, 15),
            Margin = new Padding(0, 9, 0, 9)
        };
        var body = new TextBox {
            Name = "ModernDialogBody", Dock = DockStyle.Fill,
            Multiline = true, ReadOnly = true,
            BorderStyle = BorderStyle.None,
            WordWrap = true, ScrollBars = ScrollBars.Vertical,
            BackColor = UiStyle.Surface, ForeColor = UiStyle.Foreground,
            Font = new Font("Segoe UI", 10.3f),
            RightToLeft = rtl ? RightToLeft.Yes : RightToLeft.No,
            Text = UiLocale.Display(text),
            AccessibleName = UiLocale.Display("Dialog details")
        };
        card.Controls.Add(body);
        outer.Controls.Add(card, 0, 2);

        var footer = new TableLayoutPanel {
            Name = "ModernDialogFooter",
            Dock = DockStyle.Fill, ColumnCount = 2,
            BackColor = UiStyle.Canvas, Margin = Padding.Empty
        };
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 28));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 72));
        footer.Controls.Add(new Label {
            Name = "ModernDialogFooterHint", Dock = DockStyle.Fill,
            Text = UiLocale.Display("Esc closes without approval"),
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = UiStyle.Muted,
            AutoEllipsis = false
        }, 0, 0);
        var actions = new FlowLayoutPanel {
            Name = "ModernDialogActionButtons", Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false, AutoScroll = true,
            Margin = Padding.Empty, Padding = new Padding(0, 12, 0, 3)
        };
        var values = ActionValues(buttons);
        SafeDismissal = values.Any(x => x.Result == DialogResult.Cancel)
            ? DialogResult.Cancel :
            values.Any(x => x.Result == DialogResult.No) ? DialogResult.No :
            values.Any(x => x.Result == DialogResult.Abort) ? DialogResult.Abort :
            DialogResult.OK;
        int index = defaultButton switch {
            MessageBoxDefaultButton.Button2 => 1,
            MessageBoxDefaultButton.Button3 => 2,
            _ => 0
        };
        index = Math.Min(index, values.Length - 1);
        DefaultAction = values[index].Result;
        var created = new List<Button>();
        // Right-to-left flow: add rightmost (negative/Cancel) button first.
        for (int i = values.Length - 1; i >= 0; i--)
        {
            var action = values[i];
            string captionText = UiLocale.Display(action.Caption);
            var item = new Button {
                Name = "ModernDialogAction" + action.Result,
                Text = captionText, DialogResult = action.Result,
                Tag = "NoIcon",
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowOnly,
                MinimumSize = new Size(Math.Max(105,
                    TextRenderer.MeasureText(captionText, Font).Width + 28), 41),
                FlatStyle = FlatStyle.Flat,
                BackColor = action.Result == DefaultAction ?
                    UiStyle.Accent : UiStyle.Surface,
                ForeColor = action.Result == DefaultAction ?
                    Color.White : UiStyle.Foreground,
                Margin = new Padding(5, 5, 5, 5)
            };
            actions.Controls.Add(item);
            created.Add(item);
        }
        _actions = created.ToArray();
        AcceptButton = _actions.Single(x => x.DialogResult == DefaultAction);
        if (_actions.FirstOrDefault(x => x.DialogResult == SafeDismissal)
            is Button dismiss)
            CancelButton = dismiss;
        footer.Controls.Add(actions, 1, 0);
        outer.Controls.Add(footer, 0, 3);
        UiStyle.Apply(this);

        // Preserve the original default selection across localization/scaling.
        var primary = _actions.Single(x => x.DialogResult == DefaultAction);
        Shown += (_, _) => primary.Focus();
        DpiChanged += (_, _) => {
            if (IsHandleCreated && !IsDisposed)
                BeginInvoke(new Action(() =>
                    outer.RowStyles[3].Height = Math.Max(76,
                        actions.Controls.Cast<Control>().Max(x =>
                            x.PreferredSize.Height + x.Margin.Vertical) +
                        actions.Padding.Vertical + 16)));
        };
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        // Clicking X or Esc must never approve an installation/deletion.
        if (e.CloseReason == CloseReason.UserClosing &&
            (DialogResult is DialogResult.None or DialogResult.Cancel))
            DialogResult = SafeDismissal;
        base.OnFormClosing(e);
    }

    internal static (string Caption, DialogResult Result)[] ActionValues(
        MessageBoxButtons buttons) => buttons switch
    {
        MessageBoxButtons.OK =>
            [("OK", DialogResult.OK)],
        MessageBoxButtons.OKCancel =>
            [("OK", DialogResult.OK), ("Cancel", DialogResult.Cancel)],
        MessageBoxButtons.YesNo =>
            [("Yes", DialogResult.Yes), ("No", DialogResult.No)],
        MessageBoxButtons.YesNoCancel =>
            [("Yes", DialogResult.Yes), ("No", DialogResult.No),
             ("Cancel", DialogResult.Cancel)],
        MessageBoxButtons.RetryCancel =>
            [("Retry", DialogResult.Retry), ("Cancel", DialogResult.Cancel)],
        MessageBoxButtons.AbortRetryIgnore =>
            [("Abort", DialogResult.Abort), ("Retry", DialogResult.Retry),
             ("Ignore", DialogResult.Ignore)],
        MessageBoxButtons.CancelTryContinue =>
            [("Cancel", DialogResult.Cancel), ("Try again", DialogResult.TryAgain),
             ("Continue", DialogResult.Continue)],
        _ => throw new ArgumentOutOfRangeException(nameof(buttons))
    };
}
