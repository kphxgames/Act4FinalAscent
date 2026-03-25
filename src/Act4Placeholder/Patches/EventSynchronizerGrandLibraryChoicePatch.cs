//=============================================================================
// EventSynchronizerGrandLibraryChoicePatch.cs | Act4Placeholder - Slay the Spire 2 Mod
// EN: Grand Library book-steal vote, uses VANILLA voting so all players participate
//     and ties are broken randomly (50/50).  The old host-override code was removed
//     so the standard EventSynchronizer tallying logic runs unchanged.
// ZH: 图书馆书本窃取投票恢复为原版多人投票：所有玩家均可参与，平局随机决定。
//     此前的"房主直接决策"补丁已移除，标准EventSynchronizer票数统计逻辑正常运行。
//=============================================================================

namespace Act4Placeholder;

/// <summary>
/// Placeholder class, no Harmony patches are active.
/// Vanilla EventSynchronizer handles the Grand Library vote: each player casts a
/// vote, ties are broken by the deterministic random built into ChooseSharedEventOption.
/// </summary>
internal static class EventSynchronizerGrandLibraryChoicePatch
{
}
