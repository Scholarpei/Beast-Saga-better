namespace SafeDialogueTools.Core;

public readonly record struct PanelRect(float X, float Y, float Width, float Height)
{
    public float Right => X + Width;
    public float Bottom => Y + Height;
    public bool IsFinitePositive => float.IsFinite(X) && float.IsFinite(Y) &&
        float.IsFinite(Width) && float.IsFinite(Height) && Width > 0 && Height > 0 &&
        float.IsFinite(Right) && float.IsFinite(Bottom);
}

public readonly record struct PanelBodyLayout(PanelRect View, PanelRect Footer)
{
    public static PanelBodyLayout Create(float width, float height, float scale) => new(
        new(20 * scale, 156 * scale, Math.Max(1, width - 40 * scale), Math.Max(1, height - 206 * scale)),
        new(20 * scale, height - 34 * scale, Math.Max(1, width - 40 * scale), 22 * scale));
}

public sealed record StatusPanelGeometry(PanelRect Card, IReadOnlyList<PanelRect> Labels,
    IReadOnlyList<PanelRect> Values, PanelRect Save, PanelRect Load, PanelRect Toggle, float Height)
{
    public static StatusPanelGeometry Create(float width, float scale, IReadOnlyList<float> measuredValues)
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
        var save = new PanelRect(0, card.Bottom + 48 * scale, width, 46 * scale);
        var load = save with { Y = save.Bottom + 12 * scale };
        var toggle = new PanelRect(0, load.Bottom + 60 * scale, width, 48 * scale);
        return new(card, labels, values, save, load, toggle, toggle.Bottom + 16 * scale);
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
        var y = 16 * scale;
        foreach (var height in heights)
        {
            blocks.Add(new(16 * scale, y, Math.Max(1, width - 32 * scale), Math.Max(1, height)));
            y += Math.Max(1, height) + 8 * scale;
        }
        return new(blocks, y + 8 * scale);
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
