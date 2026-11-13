using BeastSagaBetter.Core;

namespace BeastSagaBetter.Tests;

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
                Require(metrics.ArchiveRowHeight >= 96 * metrics.ContentScale,
                    $"row={metrics.ArchiveRowHeight}, required={96 * metrics.ContentScale}");
            });
            Test($"status columns and measured rows remain distinct at {width}x{height}", () =>
            {
                var scale = CommandPanelMetrics.Create(CommandPanelLayout.Create(width, height)).Scale;
                var contentScale = CommandPanelMetrics.Create(CommandPanelLayout.Create(width, height)).ContentScale;
                var body = PanelBodyLayout.Create(width, height, scale);
                Inside(body.View, new(0, 0, width, height));
                Inside(body.Footer, new(0, 0, width, height));
                Require(body.View.Y >= 140 * scale + 16 * scale, "body overlaps shared header/tabs");
                Require(body.View.Bottom < body.Footer.Y, "body overlaps footer");
                Require(body.Footer.Height <= 18 * contentScale + .01f, "footer hint row is too tall");
                Require(width - body.View.Right >= 19 * scale, "body no longer respects the screen edge");
                var actionWidths = new[] { 180f, 190f, 150f }.Select(value => value * contentScale).ToArray();
                var shortText = StatusPanelGeometry.Create(body.View.Width - 20 * contentScale, contentScale,
                    new[] { 30 * contentScale, 30 * contentScale, 30 * contentScale, 30 * contentScale },
                    actionWidths);
                var longText = StatusPanelGeometry.Create(body.View.Width - 20 * contentScale, contentScale,
                    new[] { 30 * contentScale, 60 * contentScale, 600 * contentScale, 240 * contentScale },
                    actionWidths);
                for (var index = 0; index < longText.Values.Count; index++)
                {
                    Inside(longText.Labels[index], longText.Card);
                    Inside(longText.Values[index], longText.Card);
                    Require(longText.Labels[index].Width >= 4 * 18 * contentScale + 16 * contentScale, "four-character label cannot stay on one line");
                    Require(longText.Labels[index].Right < longText.Values[index].X, "label overlaps value");
                    if (index > 0) Require(longText.Values[index - 1].Bottom < longText.Values[index].Y, "measured rows overlap");
                }
                Require(longText.Actions.Count == 3, "quick actions lost their buttons");
                for (var index = 0; index < longText.Actions.Count; index++)
                {
                    Inside(longText.Actions[index], new(0, 0, body.View.Width - 20 * contentScale, longText.Height));
                    if (index > 0)
                    {
                        var previous = longText.Actions[index - 1];
                        var current = longText.Actions[index];
                        Require(previous.Bottom <= current.Y + .01f || previous.Right <= current.X + .01f,
                            "quick action buttons overlap");
                    }
                }
                Require(longText.Actions.Select(action => action.Y).Distinct().Count() == 1,
                    "desktop quick actions should share one compact row");
                Require(longText.Actions[0].Y - shortText.Actions[0].Y >= 239 * contentScale, "long text did not push buttons down");
                Require(longText.Card.Bottom < longText.Actions[0].Y &&
                    longText.Actions[^1].Bottom < longText.Toggle.Y);
                Require(longText.Height > body.View.Height, "long text must scroll within body, above footer");
            });
            Test($"archive measured row rectangles fit and do not overlap at {width}x{height}", () =>
            {
                var scale = CommandPanelMetrics.Create(CommandPanelLayout.Create(width, height)).Scale;
                var contentScale = CommandPanelMetrics.Create(CommandPanelLayout.Create(width, height)).ContentScale;
                var body = PanelBodyLayout.Create(width, height, scale);
                var columns = ArchiveColumns.Create(body.View.Width - 20 * contentScale, body.View.Height, contentScale);
                var page = new PanelRect(0, 0, body.View.Width - 20 * contentScale, columns.Height);
                Inside(columns.List, page);
                Inside(columns.Detail, page);
                Require(columns.List.Right < columns.Detail.X, "list/detail columns overlap");
                foreach (var extra in new[] { 0, 40, 120 })
                {
                    var row = ArchiveRowGeometry.Create(columns.List.Width - 20 * contentScale, contentScale,
                        27 * contentScale, 24 * contentScale, (30 + extra) * contentScale, (28 + extra) * contentScale);
                    var rowBounds = new PanelRect(0, 0, columns.List.Width - 20 * contentScale, row.Height);
                    Inside(row.Time, rowBounds); Inside(row.Badge, rowBounds); Inside(row.Detail, rowBounds); Inside(row.Note, rowBounds);
                    Require(row.Time.Right < row.Badge.X, "time overlaps badge");
                    Require(Math.Max(row.Time.Bottom, row.Badge.Bottom) < row.Detail.Y, "top line overlaps detail");
                    Require(row.Detail.Bottom < row.Note.Y, "detail overlaps note");
                }
            });
            Test($"archive detail long fields push notes and buttons into scroll content at {width}x{height}", () =>
            {
                var scale = CommandPanelMetrics.Create(CommandPanelLayout.Create(width, height)).Scale;
                var contentScale = CommandPanelMetrics.Create(CommandPanelLayout.Create(width, height)).ContentScale;
                var body = PanelBodyLayout.Create(width, height, scale);
                var columns = ArchiveColumns.Create(body.View.Width - 20 * contentScale, body.View.Height, contentScale);
                var ordinary = PanelVerticalStack.Create(columns.Detail.Width - 20 * contentScale, contentScale,
                    new[] { 200 * contentScale, 26 * contentScale, 30 * contentScale, 30 * contentScale, 30 * contentScale,
                        24 * contentScale, 26 * contentScale, 46 * contentScale, 36 * contentScale });
                var longText = PanelVerticalStack.Create(columns.Detail.Width - 20 * contentScale, contentScale,
                    new[] { 200 * contentScale, 26 * contentScale, 120 * contentScale, 30 * contentScale, 90 * contentScale,
                        24 * contentScale, 26 * contentScale, 600 * contentScale, 36 * contentScale });
                var content = new PanelRect(0, 0, columns.Detail.Width - 20 * contentScale, longText.Height);
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
                var contentScale = CommandPanelMetrics.Create(CommandPanelLayout.Create(width, height)).ContentScale;
                var body = PanelBodyLayout.Create(width, height, scale);
                Inside(body.View, new(0, 0, width, height));
                Require(body.View.Bottom < body.Footer.Y);
                var columns = ArchiveColumns.Create(body.View.Width - 20 * contentScale, body.View.Height, contentScale);
                Inside(columns.List, new(0, 0, body.View.Width - 20 * contentScale, columns.Height));
                Inside(columns.Detail, new(0, 0, body.View.Width - 20 * contentScale, columns.Height));
                if (width == 320) Require(columns.Detail.Y > columns.List.Bottom && columns.Height > body.View.Height, "narrow columns must stack and scroll");
                var detail = PanelVerticalStack.Create(columns.Detail.Width - 20 * contentScale, contentScale,
                    new[] { 200 * contentScale, 90 * contentScale, 140 * contentScale, 300 * contentScale, 36 * contentScale });
                Require(detail.Height > columns.Detail.Height);
                var actionWidths = new[] { 220f, 220f, 220f }.Select(value => value * contentScale).ToArray();
                var status = StatusPanelGeometry.Create(body.View.Width - 20 * contentScale, contentScale,
                    new[] { 30 * contentScale, 90 * contentScale, 180 * contentScale, 140 * contentScale },
                    actionWidths);
                foreach (var action in status.Actions)
                    Inside(action, new(0, 0, body.View.Width - 20 * contentScale, status.Height));
                for (var index = 1; index < status.Actions.Count; index++)
                    Require(status.Actions[index - 1].Y < status.Actions[index].Y ||
                        status.Actions[index - 1].Right <= status.Actions[index].X + .01f,
                        "wrapped quick actions overlap");
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
