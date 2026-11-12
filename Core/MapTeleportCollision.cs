namespace BeastSagaBetter.Core;

public readonly record struct MapTeleportColliderInfo(bool IsTrigger, string LayerName, string Tag);

public static class MapTeleportCollisionPolicy
{
    public static bool Blocks(MapTeleportColliderInfo collider) =>
        !collider.IsTrigger && !IsIgnoredLayer(collider.LayerName);

    private static bool IsIgnoredLayer(string layerName) =>
        layerName is "Player" or "NPC" or "ToRoof";
}
