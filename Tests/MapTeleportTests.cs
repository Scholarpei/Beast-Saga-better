using BeastSagaBetter.Core;

namespace BeastSagaBetter.Tests;

internal static class MapTeleportTests
{
    private static int checks;
    private static int failures;

    public static (int Checks, int Failures) Run()
    {
        checks = 0;
        failures = 0;

        void Require(bool condition, string message)
        {
            checks++;
            if (!condition) failures++;
            if (!condition) Console.WriteLine("FAIL: " + message);
        }

        Test("map teleport mode opens and closes cleanly", () =>
        {
            MapTeleportMode.Reset();
            MapTeleportMode.Open();
            Require(MapTeleportMode.Active, "mode should open");
            MapTeleportMode.Close();
            Require(!MapTeleportMode.Active, "mode should close");
            MapTeleportMode.Reset();
        });

        Test("short left clicks teleport and drags browse instead", () =>
        {
            MapTeleportMode.Reset();
            MapTeleportMode.Open();
            MapTeleportMode.BeginPointer(100, 100);
            Require(MapTeleportMode.IsClickReleased(106, 104), "short click should teleport");
            MapTeleportMode.BeginPointer(100, 100);
            Require(!MapTeleportMode.IsClickReleased(130, 120), "drag beyond threshold should browse");
            MapTeleportMode.Reset();
        });

        Test("map teleport candidates use exact point before fixed nudge fallback", () =>
        {
            bool Exact(float x, float y) => x == 10f && y == 20f;
            var exact = MapTeleportCandidate.Evaluate(10f, 20f, Exact);
            Require(exact.IsValid && exact.Point == new MapTeleportPoint(10f, 20f), "exact point should pass");

            bool Nudged(float x, float y) => x == 12f && y == 20f;
            var nudged = MapTeleportCandidate.Evaluate(10f, 20f, (_, _) => false, Nudged);
            Require(nudged.IsValid && nudged.Point == new MapTeleportPoint(12f, 20f), "fixed nudge should pass");

            var blocked = MapTeleportCandidate.Evaluate(10f, 20f, (_, _) => false, (_, _) => false);
            Require(!blocked.IsValid, "all-blocked point should stay red");
        });
        Test("map teleport safety rechecks the player-sized neighborhood", () =>
        {
            bool CenterOnly(float x, float y) => x == 10f && y == 10f;
            Require(!MapTeleportSafety.AreaSafe(10f, 10f, 0.35f, CenterOnly),
                "a safe center surrounded by blocked offsets must stay red");
            bool OpenGround(float x, float y) => true;
            Require(MapTeleportSafety.AreaSafe(10f, 10f, 0.35f, OpenGround), "open ground should be safe");
            Require(!MapTeleportSafety.AreaSafe(float.NaN, 10f, 0.35f, OpenGround), "NaN should stay unsafe");
        });

        Test("teleport collision ignores actors but blocks world obstacles", () =>
        {
            Require(MapTeleportCollisionPolicy.Blocks(new MapTeleportColliderInfo(false, "Obstacle", "Untagged")),
                "solid obstacles should block");
            Require(MapTeleportCollisionPolicy.Blocks(new MapTeleportColliderInfo(false, "Default", "Untagged")),
                "solid default geometry should block");
            Require(!MapTeleportCollisionPolicy.Blocks(new MapTeleportColliderInfo(true, "Obstacle", "NPC")),
                "trigger volumes should not block");
            Require(!MapTeleportCollisionPolicy.Blocks(new MapTeleportColliderInfo(false, "Player", "Player")),
                "the player collider should not block its own target");
            Require(!MapTeleportCollisionPolicy.Blocks(new MapTeleportColliderInfo(false, "NPC", "NPC")),
                "actors should not block teleport targets");
            Require(!MapTeleportCollisionPolicy.Blocks(new MapTeleportColliderInfo(false, "ToRoof", "Untagged")),
                "roof markers should not block teleport targets");
        });

        Test("screen edge panning is zero inside the dead zone", () =>
        {
            MapTeleportMode.Reset();
            MapTeleportMode.Open();
            Require(MapTeleportMode.EdgePan(100, 100, 1280, 720) == new MapTeleportPoint(0f, 0f),
                "center should not pan");
            var right = MapTeleportMode.EdgePan(1275, 360, 1280, 720);
            Require(right.X > 0f && right.Y == 0f, "right edge should pan the camera right");
            var innerRight = MapTeleportMode.EdgePan(1280 - MapTeleportMode.EdgeWidth + 0.01f, 360, 1280, 720);
            Require(innerRight.X is >= 8f and <= 8.05f, $"inner edge should start near 8 units/s: {innerRight}");
            var outerRight = MapTeleportMode.EdgePan(1280f, 360, 1280, 720);
            Require(outerRight.X is >= 14.99f and <= 15f, $"outer edge should reach 15 units/s: {outerRight}");
            MapTeleportMode.Reset();
        });

        Test("screen edge panning stays bounded near every screen edge", () =>
        {
            MapTeleportMode.Reset();
            MapTeleportMode.Open();
            MapTeleportPoint Pan(float x, float y) => MapTeleportMode.EdgePan(x, y, 1280, 720);
            Require(Pan(0, 360).X < 0f && Pan(0, 360).Y == 0f, "left edge should pan the camera left");
            Require(Pan(640, 0).Y < 0f && Pan(640, 0).X == 0f, "bottom edge should pan the camera down");
            Require(Pan(640, 720).Y > 0f && Pan(640, 720).X == 0f, "top edge should pan the camera up");
            foreach (var point in new[] { Pan(0, 0), Pan(1280, 0), Pan(1280, 720), Pan(0, 720) })
            {
                Require(point.X >= -15f && point.X <= 15f && point.Y >= -15f && point.Y <= 15f,
                    $"corner pan must stay bounded: {point}");
            }
            MapTeleportMode.Reset();
        });

        Test("completed teleports preserve the camera offset at the new position", () =>
        {
            var end = MapTeleportCamera.EndPosition(
                new MapTeleportPoint(10f, 10f), new MapTeleportPoint(12f, 15f),
                new MapTeleportPoint(50f, 40f));
            Require(end == new MapTeleportPoint(52f, 45f), $"camera end mismatch: {end}");
        });

        Test("map teleport marker uses the same screen coordinates as the click", () =>
        {
            var gui = MapTeleportScreen.ToGui(new MapTeleportPoint(640f, 100f), 720f);
            Require(gui == new MapTeleportPoint(640f, 620f), $"GUI conversion mismatch: {gui}");
            var input = MapTeleportScreen.ToInput(new MapTeleportPoint(640f, 620f), 720f);
            Require(input == new MapTeleportPoint(640f, 100f), $"Input conversion mismatch: {input}");
        });

        Test("camera drag ignores stationary pointer jitter", () =>
        {
            Require(!MapTeleportDrag.ShouldMove(1f, -1f), "sub-pixel pointer jitter should not move the camera");
            Require(MapTeleportDrag.ShouldMove(3f, 0f), "real pointer movement should move the camera");
        });

        Test("map streaming follows camera movement without running every frame", () =>
        {
            var origin = new MapTeleportPoint(10f, 10f);
            Require(!MapTeleportStreaming.ShouldUpdate(origin, new MapTeleportPoint(10.5f, 10f)),
                "sub-threshold camera movement should keep current chunks");
            Require(MapTeleportStreaming.ShouldUpdate(origin, new MapTeleportPoint(11f, 10f)),
                "camera movement should load chunks around the new view");
        });

        Test("world cheat section is available", () =>
        {
            Require(CheatSectionCatalog.IsAvailable(CheatSection.World), "world section should be available");
        });

        return (checks, failures);
    }

    private static void Test(string name, Action action)
    {
        try
        {
            action();
        }
        catch (Exception error)
        {
            failures++;
            Console.WriteLine($"FAIL: {name}: {error.Message}");
        }
    }
}
