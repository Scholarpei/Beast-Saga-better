using SafeDialogueTools.Core;

namespace SafeDialogueTools.Tests;

internal static class PanelRegressionTests
{
    internal static (int Checks, int Failures) Run()
    {
        var checks = 0;
        var failures = 0;
        void Test(string name, Action action)
        {
            checks++;
            try { action(); Console.WriteLine("PASS " + name); }
            catch (Exception error) { failures++; Console.WriteLine("FAIL " + name + ": " + error.Message); }
        }
        void Require(bool condition, string message = "Assertion failed")
        {
            if (!condition) throw new Exception(message);
        }
        void Inside(PanelRect inner, PanelRect outer)
        {
            Require(inner.IsFinitePositive, "non-finite or empty rectangle: " + inner);
            Require(inner.X >= outer.X - .01f && inner.Y >= outer.Y - .01f &&
                inner.Right <= outer.Right + .01f && inner.Bottom <= outer.Bottom + .01f,
                inner + " is outside " + outer);
        }
        Test("rounded panel texture radius is clamped to the supported geometry", () =>
        {
            Require(PanelTextureGeometry.ClampCornerRadius(16, 32) == 15);
            Require(PanelTextureGeometry.ClampCornerRadius(15, 32) == 15);
            Require(PanelTextureGeometry.ClampCornerRadius(0, 32) == 0);
            Require(PanelTextureGeometry.ClampCornerRadius(-5, 32) == 0);
        });
        foreach (var (width, height) in new[] { (1280, 720), (1920, 1080), (2560, 1440), (3840, 2160) })
        {
            Test($"archive baseline row reserves three scaled lines at {width}x{height}", () =>
            {
                var metrics = CommandPanelMetrics.Create(CommandPanelLayout.Create(width, height));
                Require(metrics.ArchiveRowHeight >= 96 * metrics.Scale,
                    $"row={metrics.ArchiveRowHeight}, required={96 * metrics.Scale}");
            });
            Test($"status columns and measured rows remain distinct at {width}x{height}", () =>
            {
                var scale = CommandPanelMetrics.Create(CommandPanelLayout.Create(width, height)).Scale;
                var body = PanelBodyLayout.Create(width, height, scale);
                Inside(body.View, new(0, 0, width, height));
                Inside(body.Footer, new(0, 0, width, height));
                Require(body.View.Y >= 140 * scale + 16 * scale, "body overlaps shared header/tabs");
                Require(body.View.Bottom < body.Footer.Y, "body overlaps footer");
                var shortText = StatusPanelGeometry.Create(body.View.Width - 20 * scale, scale, new[] { 30 * scale, 30 * scale, 30 * scale, 30 * scale });
                var longText = StatusPanelGeometry.Create(body.View.Width - 20 * scale, scale, new[] { 30 * scale, 60 * scale, 180 * scale, 90 * scale });
                for (var index = 0; index < longText.Values.Count; index++)
                {
                    Inside(longText.Labels[index], longText.Card);
                    Inside(longText.Values[index], longText.Card);
                    Require(longText.Labels[index].Width >= 4 * 18 * scale + 16 * scale, "four-character label cannot stay on one line");
                    Require(longText.Labels[index].Right < longText.Values[index].X, "label overlaps value");
                    if (index > 0) Require(longText.Values[index - 1].Bottom < longText.Values[index].Y, "measured rows overlap");
                }
                Require(longText.Save.Y - shortText.Save.Y >= 239 * scale, "long text did not push buttons down");
                Require(longText.Card.Bottom < longText.Save.Y && longText.Save.Bottom < longText.Load.Y && longText.Load.Bottom < longText.Toggle.Y);
                Require(longText.Height > body.View.Height, "long text must scroll within body, above footer");
            });
            Test($"archive measured row rectangles fit and do not overlap at {width}x{height}", () =>
            {
                var scale = CommandPanelMetrics.Create(CommandPanelLayout.Create(width, height)).Scale;
                var body = PanelBodyLayout.Create(width, height, scale);
                var columns = ArchiveColumns.Create(body.View.Width - 20 * scale, body.View.Height, scale);
                var page = new PanelRect(0, 0, body.View.Width - 20 * scale, columns.Height);
                Inside(columns.List, page);
                Inside(columns.Detail, page);
                Require(columns.List.Right < columns.Detail.X, "list/detail columns overlap");
                foreach (var extra in new[] { 0, 40, 120 })
                {
                    var row = ArchiveRowGeometry.Create(columns.List.Width - 20 * scale, scale,
                        27 * scale, 24 * scale, (30 + extra) * scale, (28 + extra) * scale);
                    var rowBounds = new PanelRect(0, 0, columns.List.Width - 20 * scale, row.Height);
                    Inside(row.Time, rowBounds); Inside(row.Badge, rowBounds); Inside(row.Detail, rowBounds); Inside(row.Note, rowBounds);
                    Require(row.Time.Right < row.Badge.X, "time overlaps badge");
                    Require(Math.Max(row.Time.Bottom, row.Badge.Bottom) < row.Detail.Y, "top line overlaps detail");
                    Require(row.Detail.Bottom < row.Note.Y, "detail overlaps note");
                }
            });
            Test($"archive detail long fields push notes and buttons into scroll content at {width}x{height}", () =>
            {
                var scale = CommandPanelMetrics.Create(CommandPanelLayout.Create(width, height)).Scale;
                var columns = ArchiveColumns.Create(width - 60 * scale, height - 206 * scale, scale);
                var ordinary = PanelVerticalStack.Create(columns.Detail.Width - 20 * scale, scale, new[] { 200 * scale, 26 * scale, 30 * scale, 30 * scale, 30 * scale, 24 * scale, 26 * scale, 46 * scale, 36 * scale });
                var longText = PanelVerticalStack.Create(columns.Detail.Width - 20 * scale, scale, new[] { 200 * scale, 26 * scale, 120 * scale, 30 * scale, 90 * scale, 24 * scale, 26 * scale, 300 * scale, 36 * scale });
                var content = new PanelRect(0, 0, columns.Detail.Width - 20 * scale, longText.Height);
                for (var index = 0; index < longText.Blocks.Count; index++)
                {
                    Inside(longText.Blocks[index], content);
                    if (index > 0) Require(longText.Blocks[index - 1].Bottom < longText.Blocks[index].Y);
                }
                Require(longText.Blocks[^1].Y > ordinary.Blocks[^1].Y, "button stayed under growing text");
                Require(longText.Height > columns.Detail.Height, "details must scroll");
            });
        }
        foreach (var (width, height) in new[] { (800, 600), (640, 360), (320, 480) })
            Test($"small viewport uses finite bounded views and scroll content at {width}x{height}", () =>
            {
                var scale = CommandPanelMetrics.Create(CommandPanelLayout.Create(width, height)).Scale;
                var body = PanelBodyLayout.Create(width, height, scale);
                Inside(body.View, new(0, 0, width, height));
                Require(body.View.Bottom < body.Footer.Y);
                var columns = ArchiveColumns.Create(body.View.Width - 20 * scale, body.View.Height, scale);
                Inside(columns.List, new(0, 0, body.View.Width - 20 * scale, columns.Height));
                Inside(columns.Detail, new(0, 0, body.View.Width - 20 * scale, columns.Height));
                if (width == 320) Require(columns.Detail.Y > columns.List.Bottom && columns.Height > body.View.Height, "narrow columns must stack and scroll");
                var detail = PanelVerticalStack.Create(columns.Detail.Width - 20 * scale, scale, new[] { 200 * scale, 90 * scale, 140 * scale, 300 * scale, 36 * scale });
                Require(detail.Height > columns.Detail.Height);
                var status = StatusPanelGeometry.Create(body.View.Width - 20 * scale, scale, new[] { 30 * scale, 90 * scale, 180 * scale, 140 * scale });
                Require(status.Height > body.View.Height);
            });
        Test("scrollbar clamps short-track thumb and out-of-range scroll within track", () =>
        {
            foreach (var height in new[] { .5f, 10f, 24f, 200f, 700f })
            foreach (var scroll in new[] { -999f, 0f, 50f, 99999f })
            {
                var view = new PanelRect(10, 20, 100, height);
                Require(PanelScrollbarGeometry.TryCreate(view, height + 500, scroll, out var result));
                Inside(result.Track, view); Inside(result.Thumb, result.Track);
                if (scroll < 0) Require(result.Thumb.Y == result.Track.Y);
                if (scroll > 9999) Require(Math.Abs(result.Thumb.Bottom - result.Track.Bottom) < .01f);
            }
        });
        Test("scrollbar rejects nonfinite empty and non-scrolling input", () =>
        {
            foreach (var invalid in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity })
            {
                Require(!PanelScrollbarGeometry.TryCreate(new(invalid, 0, 100, 100), 500, 0, out _));
                Require(!PanelScrollbarGeometry.TryCreate(new(0, 0, invalid, 100), 500, 0, out _));
                Require(!PanelScrollbarGeometry.TryCreate(new(0, 0, 100, invalid), 500, 0, out _));
                Require(!PanelScrollbarGeometry.TryCreate(new(0, 0, 100, 100), invalid, 0, out _));
                Require(!PanelScrollbarGeometry.TryCreate(new(0, 0, 100, 100), 500, invalid, out _));
            }
            Require(!PanelScrollbarGeometry.TryCreate(new(0, 0, 100, 0), 500, 0, out _));
            Require(!PanelScrollbarGeometry.TryCreate(new(0, 0, -1, 100), 500, 0, out _));
            Require(!PanelScrollbarGeometry.TryCreate(new(0, 0, 100, 100), 100, 0, out _));
            Require(!PanelScrollbarGeometry.TryCreate(new(0, 0, 100, 100), -100, 0, out _));
            Require(PanelScrollbarGeometry.TryCreate(new(0, 0, 100, 100), float.MaxValue, 0, out var result));
            Inside(result.Thumb, result.Track);
        });
        Test("style readiness completes only after construction and invalidates partial resize or lost textures", () =>
        {
            var styles = new PanelStyleGeneration();
            Require(!styles.Matches(1280, 720, 1, true), "first style alone cannot mean ready");
            styles.Complete(1280, 720, 1);
            Require(styles.Matches(1280, 720, 1, true));
            Require(!styles.Matches(1280, 720, 1, false), "destroyed textures reused");
            Require(!styles.Matches(1920, 1080, 1.5f, true), "resolution change reused old styles");
            Require(!styles.Matches(1281, 720, 1, true), "same-scale width change ignored");
            Require(!styles.Matches(1280, 720, 1.1f, true), "effective scale change ignored");
            styles.Invalidate();
            Require(!styles.Ready && !styles.Matches(1280, 720, 1, true), "partial rebuild became ready");
            styles.Complete(1920, 1080, 1.5f);
            Require(styles.Generation == 2 && styles.Matches(1920, 1080, 1.5f, true));
        });
        Test("GUI failure logs once and closes panel clearing pointer policy", () =>
        {
            var policy = new PanelGuiFailurePolicy();
            CommandPanelState.Open = true;
            CommandPanelState.SetPointerBounds(0, 0, 1280, 720);
            Require(policy.RecordFailure());
            CommandPanelState.Close();
            Require(!CommandPanelState.Open && !CommandPanelState.IsPointerBlocked(10, 10));
            for (var index = 0; index < 100; index++) Require(!policy.RecordFailure(), "same render failure flooded log");
        });
        return (checks, failures);
    }
}
