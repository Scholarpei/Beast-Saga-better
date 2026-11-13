namespace BeastSagaBetter.Core;

public readonly record struct PanelRect(float X, float Y, float Width, float Height)
{
    public float Right => X + Width;
    public float Bottom => Y + Height;
    public bool IsFinitePositive => float.IsFinite(X) && float.IsFinite(Y) &&
        float.IsFinite(Width) && float.IsFinite(Height) && Width > 0 && Height > 0 &&
        float.IsFinite(Right) && float.IsFinite(Bottom);
}

public static class PanelTextureGeometry
{
    public static int ClampCornerRadius(int radius, int size)
    {
        if (size < 2) throw new ArgumentOutOfRangeException(nameof(size));
        return Math.Clamp(radius, 0, (size - 2) / 2);
    }
}

public readonly record struct PanelBodyLayout(PanelRect View, PanelRect Footer)
{
    public static PanelBodyLayout Create(float width, float height, float scale)
    {
        var contentScale = Math.Min(scale, 1.5f);
        var footerHeight = 18 * contentScale;
        var footerBottomMargin = 6 * contentScale;
        var footerGap = 4 * contentScale;
        var footerY = height - footerBottomMargin - footerHeight;
        var bodyTop = 156 * scale;
        var bodyBottom = footerY - footerGap;
        return new(
            new(20 * scale, bodyTop, Math.Max(1, width - 40 * scale), Math.Max(1, bodyBottom - bodyTop)),
            new(20 * scale, footerY, Math.Max(1, width - 40 * scale), footerHeight));
    }
}

public sealed record StatusPanelGeometry(PanelRect Card, IReadOnlyList<PanelRect> Labels,
    IReadOnlyList<PanelRect> Values, IReadOnlyList<PanelRect> Actions, PanelRect Toggle, float Height)
{
    public static StatusPanelGeometry Create(float width, float scale, IReadOnlyList<float> measuredValues,
        IReadOnlyList<float>? measuredActions = null)
    {
        var pad = 16 * scale;
        var labelWidth = Math.Min(112 * scale, width * .3f);
        var values = new List<PanelRect>();
        var labels = new List<PanelRect>();
        var y = 44 * scale;
        foreach (var measured in measuredValues)
        {
            var height = Math.Max(30 * scale, float.IsFinite(measured) ? measured : 0);
            labels.Add(new(pad, y, labelWidth, 30 * scale));
            values.Add(new(pad + labelWidth + 14 * scale, y,
                Math.Max(1, width - pad * 2 - labelWidth - 14 * scale), height));
            y += height + 10 * scale;
        }
        var card = new PanelRect(0, 0, width, y + 6 * scale);
        var actionGap = 10 * scale;
        var actionHeight = 42 * scale;
        var actionWidths = measuredActions is { Count: > 0 } ? measuredActions : new[] { 1f, 1f, 1f };
        var actions = new List<PanelRect>();
        var actionX = 0f;
        var actionY = card.Bottom + 44 * scale;
        for (var index = 0; index < actionWidths.Count; index++)
        {
            var measured = actionWidths[index];
            var actionWidth = float.IsFinite(measured) && measured > 0
                ? measured
                : (width - actionGap * (actionWidths.Count - 1)) / actionWidths.Count;
            actionWidth = Math.Min(actionWidth, width);
            if (actionX > 0 && actionX + actionWidth > width + .01f)
            {
                actionX = 0;
                actionY += actionHeight + actionGap;
            }
            actions.Add(new(actionX, actionY, Math.Max(1, actionWidth), actionHeight));
            actionX += actionWidth + actionGap;
        }
        var toggle = new PanelRect(0, actionY + actionHeight + 60 * scale, width, 48 * scale);
        return new(card, labels, values, actions, toggle, toggle.Bottom + 16 * scale);
    }
}

public readonly record struct ArchiveRowGeometry(PanelRect Time, PanelRect Badge,
    PanelRect Detail, PanelRect Note, float Height)
{
    // Heights come from the actual Unity styles, including wrapping when enabled.
    public static ArchiveRowGeometry Create(float width, float scale, float timeHeight,
        float badgeHeight, float detailHeight, float noteHeight)
    {
        var pad = 14 * scale;
        var badgeWidth = Math.Min(106 * scale, Math.Max(1, width * .3f));
        var top = Math.Max(timeHeight, badgeHeight);
        var detailY = 10 * scale + top + 6 * scale;
        var noteY = detailY + detailHeight + 6 * scale;
        return new(new(pad, 10 * scale, Math.Max(1, width - pad * 2 - badgeWidth - 8 * scale), timeHeight),
            new(width - pad - badgeWidth, 10 * scale, badgeWidth, badgeHeight),
            new(pad, detailY, Math.Max(1, width - pad * 2), detailHeight),
            new(pad, noteY, Math.Max(1, width - pad * 2), noteHeight), noteY + noteHeight + 10 * scale);
    }
}

public readonly record struct ArchiveColumns(PanelRect List, PanelRect Detail, float ControlsHeight,
    bool SeparateSortRow, float Height)
{
    public static ArchiveColumns Create(float width, float height, float scale)
    {
        var stacked = width < 760 * scale;
        var listWidth = stacked ? width : Math.Min(420 * scale, width * .38f);
        var separateSort = listWidth < 320 * scale;
        var controls = (separateSort ? 114 : 74) * scale;
        var list = new PanelRect(0, controls, listWidth, stacked ? 220 * scale : Math.Max(1, height - controls));
        var detail = stacked
            ? new PanelRect(0, list.Bottom + 16 * scale, width, Math.Max(240 * scale, height - list.Bottom - 16 * scale))
            : new PanelRect(list.Right + 16 * scale, 0, Math.Max(1, width - list.Right - 16 * scale), height);
        return new(list, detail, controls, separateSort, Math.Max(height, detail.Bottom));
    }
}

public readonly record struct PanelScrollbarGeometry(PanelRect Track, PanelRect Thumb)
{
    public static bool TryCreate(PanelRect view, float contentHeight, float scroll, out PanelScrollbarGeometry geometry)
    {
        geometry = default;
        if (!view.IsFinitePositive || !float.IsFinite(contentHeight) || contentHeight <= view.Height + 1 ||
            !float.IsFinite(scroll)) return false;
        var trackWidth = Math.Min(4, view.Width);
        var track = new PanelRect(view.Right - trackWidth, view.Y, trackWidth, view.Height);
        var thumbHeight = Math.Clamp((double)view.Height * view.Height / contentHeight,
            Math.Min(24, view.Height), view.Height);
        var progress = Math.Clamp((double)scroll / (contentHeight - view.Height), 0, 1);
        var thumb = new PanelRect(track.X, (float)(view.Y + progress * (view.Height - thumbHeight)),
            trackWidth, (float)thumbHeight);
        geometry = new(track, thumb);
        return track.IsFinitePositive && thumb.IsFinitePositive;
    }
}

public sealed class PanelStyleGeneration
{
    private int width;
    private int height;
    private float scale;
    public bool Ready { get; private set; }
    public int Generation { get; private set; }
    public bool Matches(int nextWidth, int nextHeight, float nextScale, bool resourcesAlive) =>
        Ready && resourcesAlive && width == nextWidth && height == nextHeight && scale == nextScale;
    public void Invalidate() => Ready = false;
    public void Complete(int nextWidth, int nextHeight, float nextScale)
    {
        width = nextWidth;
        height = nextHeight;
        scale = nextScale;
        Generation++;
        Ready = true;
    }
}

public sealed record PanelVerticalStack(IReadOnlyList<PanelRect> Blocks, float Height)
{
    public static PanelVerticalStack Create(float width, float scale, IReadOnlyList<float> heights)
    {
        var blocks = new List<PanelRect>();
        var y = 12 * scale;
        foreach (var height in heights)
        {
            blocks.Add(new(16 * scale, y, Math.Max(1, width - 32 * scale), Math.Max(1, height)));
            y += Math.Max(1, height) + 6 * scale;
        }
        return new(blocks, y + 6 * scale);
    }
}

public sealed class PanelGuiFailurePolicy
{
    // A session logs its first actual GUI exception; subsequent render retries cannot flood the native log.
    public bool Failed { get; private set; }
    public bool RecordFailure()
    {
        var first = !Failed;
        Failed = true;
        return first;
    }
}
