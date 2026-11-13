// Native stat readers are outside this controlled snapshot test boundary.
// Snapshot assembly, favor lookup, candidate selection and empty state use the
// actual linked Plugin/CheatCharacterSnapshot.cs implementation.
using ExcelData;

namespace BeastSagaBetter;

internal static partial class CheatEditor
{
    private static float ReadPlayerBase(PlayerData player, int type) => 10;
    private static int ReadPlayerFinal(PlayerDataManager manager, int type) => 10;
    private static int ReadPlayerMartial(PlayerData player, PlayerDataManager manager, KFType type) => 1;
    private static int ReadPlayerLife(PlayerData player, PlayerDataManager manager, LifeType type) => 1;
    private static float ReadFriendBase(FriendData friend, int type) => 10;
    private static int ReadFriendFinal(FriendData friend, int type) => 10;
    private static int ReadFriendMartial(FriendData friend, KFType type) => 1;
    private static int ReadFriendLife(FriendData friend, LifeType type) => 1;
}
