namespace BeastSagaBetter.Core;

public readonly record struct MapTeleportPoint(float X, float Y)
{
    public bool IsFinite => float.IsFinite(X) && float.IsFinite(Y);
}

public readonly record struct MapTeleportEvaluation(bool IsValid, MapTeleportPoint Point)
{
    public (float X, float Y) Target => (Point.X, Point.Y);
}

public static class MapTeleportSafety
{
    private static readonly (float X, float Y)[] Offsets =
    {
        (0f, 0f), (-1f, -1f), (0f, -1f), (1f, -1f),
        (-1f, 0f), (1f, 0f), (-1f, 1f), (0f, 1f), (1f, 1f)
    };

    public static bool AreaSafe(float x, float y, float radius, Func<float, float, bool> isPointSafe)
    {
        if (!float.IsFinite(x) || !float.IsFinite(y) || !float.IsFinite(radius) || radius < 0f) return false;
        return Offsets.All(offset => isPointSafe(x + offset.X * radius, y + offset.Y * radius));
    }
}


public static class MapTeleportCandidate
{
    public static MapTeleportEvaluation Evaluate(
        float x,
        float y,
        Func<float, float, bool> isSafe,
        Func<float, float, bool>? nudgedIsSafe = null,
        float nudge = 2f)
    {
        if (!float.IsFinite(x) || !float.IsFinite(y) || nudge < 0f) return new(false, new MapTeleportPoint(x, y));
        if (isSafe(x, y)) return new(true, new MapTeleportPoint(x, y));
        nudgedIsSafe ??= isSafe;
        var nudgedX = x + nudge;
        return nudgedIsSafe(nudgedX, y)
            ? new(true, new MapTeleportPoint(nudgedX, y))
            : new(false, new MapTeleportPoint(x, y));
    }
}

public static class MapTeleportMode
{
    public const float DragThreshold = 8f;
    public const float EdgeWidth = 24f;

    private static bool active;
    private static bool pointerDown;
    private static float pointerDownX;
    private static float pointerDownY;

    public static bool Active => active;

    public static void Open()
    {
        active = true;
        pointerDown = false;
    }

    public static void Close()
    {
        active = false;
        pointerDown = false;
    }

    public static void ForceClose() => Close();

    public static void Reset() => Close();

    public static void BeginPointer(float x, float y)
    {
        pointerDown = true;
        pointerDownX = x;
        pointerDownY = y;
    }

    public static bool IsClickReleased(float x, float y)
    {
        if (!active || !pointerDown) return false;
        pointerDown = false;
        var squaredDistance = (double)(x - pointerDownX) * (x - pointerDownX) + (double)(y - pointerDownY) * (y - pointerDownY);
        return squaredDistance <= (double)DragThreshold * DragThreshold;
    }

    public static MapTeleportPoint EdgePan(float x, float y, float width, float height)
    {
        if (!active || !float.IsFinite(x) || !float.IsFinite(y) ||
            !float.IsFinite(width) || !float.IsFinite(height) || width <= 0f || height <= 0f)
            return new(0f, 0f);

        var clampedX = Math.Clamp(x, 0f, width);
        var clampedY = Math.Clamp(y, 0f, height);
        var xDistance = Math.Min(clampedX, width - clampedX);
        var yDistance = Math.Min(clampedY, height - clampedY);
        var xDirection = x <= width / 2f ? -1f : 1f;
        var yDirection = y <= height / 2f ? -1f : 1f;
        return new(
            xDistance < EdgeWidth ? xDirection * EdgeStrength(xDistance) : 0f,
            yDistance < EdgeWidth ? yDirection * EdgeStrength(yDistance) : 0f);
    }

    private static float EdgeStrength(float distance) =>
        8f + 7f * (1f - distance / EdgeWidth);
}

public static class MapTeleportCamera
{
    public static MapTeleportPoint EndPosition(
        MapTeleportPoint playerStart, MapTeleportPoint cameraStart, MapTeleportPoint playerTarget)
    {
        if (!playerStart.IsFinite || !cameraStart.IsFinite || !playerTarget.IsFinite)
            return cameraStart;
        return new(playerTarget.X + cameraStart.X - playerStart.X,
            playerTarget.Y + cameraStart.Y - playerStart.Y);
    }
}

public static class MapTeleportScreen
{
    public static MapTeleportPoint ToGui(MapTeleportPoint input, float screenHeight) =>
        new(input.X, screenHeight - input.Y);

    public static MapTeleportPoint ToInput(MapTeleportPoint gui, float screenHeight) =>
        new(gui.X, screenHeight - gui.Y);
}

public static class MapTeleportDrag
{
    public const float PixelDeadZone = 2f;

    public static bool ShouldMove(float deltaX, float deltaY) =>
        float.IsFinite(deltaX) && float.IsFinite(deltaY) &&
        deltaX * deltaX + deltaY * deltaY >= PixelDeadZone * PixelDeadZone;
}

public static class MapTeleportStreaming
{
    public const float MovementThreshold = 0.75f;

    public static bool ShouldUpdate(MapTeleportPoint last, MapTeleportPoint current)
    {
        if (!current.IsFinite) return false;
        if (!last.IsFinite) return true;
        var dx = (double)current.X - last.X;
        var dy = (double)current.Y - last.Y;
        return dx * dx + dy * dy >= (double)MovementThreshold * MovementThreshold;
    }
}
