using System.Drawing;
using System.Windows.Forms;

namespace LockScreenCampaignManager;

/// <summary>
/// Bounded, vertically scrollable status region. Long observations wrap
/// to its available width, without consuming the inventory grid's space.
/// </summary>
internal sealed class UiWrappedStatusPanel : FlowLayoutPanel
{
    private bool _fittingWidths;

    internal UiWrappedStatusPanel(params Label[] labels)
    {
        Dock = DockStyle.Fill;
        AutoScroll = true;
        AutoSize = false;
        FlowDirection = FlowDirection.TopDown;
        WrapContents = false;
        RightToLeft = RightToLeft.No;
        Padding = Padding.Empty;
        foreach (Label label in labels)
        {
            label.Dock = DockStyle.None;
            label.Anchor = AnchorStyles.Top | AnchorStyles.Left;
            label.AutoSize = true;
            label.AutoEllipsis = false;
            Controls.Add(label);
        }
        ClientSizeChanged += (_, _) => FitLabelWidths();
        FitLabelWidths();
    }

    private void FitLabelWidths()
    {
        if (_fittingWidths || IsDisposed) return;
        _fittingWidths = true;
        try
        {
            // Reserve space even before the scrollbar appears to avoid
            // alternating between horizontal and vertical scrollbar layouts.
            int available = Math.Max(1, ClientSize.Width - Padding.Horizontal -
                SystemInformation.VerticalScrollBarWidth - 4);
            foreach (Label label in Controls.OfType<Label>())
            {
                var limit = new Size(Math.Max(1, available - label.Margin.Horizontal), 0);
                if (label.MaximumSize != limit) label.MaximumSize = limit;
            }
        }
        finally { _fittingWidths = false; }
    }
}
