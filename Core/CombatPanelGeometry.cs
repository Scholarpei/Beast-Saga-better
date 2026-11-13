namespace BeastSagaBetter.Core;

public sealed record CombatPanelGeometry(float Width, float ViewHeight, float ContentHeight,
    PanelRect Hint, PanelRect Checkpoint, PanelRect TeamHeading, IReadOnlyList<PanelRect> Units,
    PanelRect RestoreSelected, PanelRect RestoreAll, PanelRect Defeat, PanelRect? Confirmation, PanelRect Result)
{
    public static CombatPanelGeometry Create(float pageWidth, float viewHeight, int units,
        float hintHeight, float checkpointHeight, float resultHeight, float confirmationTextHeight)
    {
        var width = Math.Max(32, pageWidth - 20);
        var hint = new PanelRect(0, 0, width, Math.Max(24, hintHeight));
        var checkpoint = new PanelRect(0, hint.Bottom + 10, width, Math.Max(42, checkpointHeight));
        var heading = new PanelRect(0, checkpoint.Bottom + 14, width, 22);
        var y = heading.Y + 28;
        var rows = new List<PanelRect>();
        for (var index = 0; index < Math.Max(0, units); index++)
        { rows.Add(new(0, y, width, 52)); y += 58; }
        if (units <= 0) y += 34;
        var half = (width - 12) / 2;
        var single = new PanelRect(0, y, half, 34);
        var all = new PanelRect(half + 12, y, half, 34);
        var defeat = new PanelRect(0, y + 48, width, 34);
        y = defeat.Y + 45;
        PanelRect? confirmation = null;
        if (confirmationTextHeight > 0)
        { confirmation = new(0, y, width, Math.Max(70, confirmationTextHeight) + 52); y = confirmation.Value.Bottom + 12; }
        var result = new PanelRect(0, y, width, Math.Max(65, resultHeight));
        return new(width, Math.Max(1, viewHeight), Math.Max(viewHeight, result.Bottom + 16),
            hint, checkpoint, heading, rows, single, all, defeat, confirmation, result);
    }
}
