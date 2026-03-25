//=============================================================================
// NMapScreenReadyPatch.cs | Act4Placeholder - Slay the Spire 2 Mod
// EN: Harmony postfix on NMapScreen._Ready; creates the admin button on the map screen for the host player.
// ZH: NMapScreen._Ready的Harmony postfix补丁；为主机玩家在地图界面创建管理员按钮。
//=============================================================================
using HarmonyLib;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;

namespace Act4Placeholder;

[HarmonyPatch(typeof(NMapScreen), "_Ready")]
internal static class NMapScreenReadyPatch
{
	private static void Postfix(NMapScreen __instance)
	{
		ModSupport.EnsureAdminButton(__instance);
		// Fallback: restore book choices if they weren't loaded during FromSerializable
		// (e.g. SaveManager not yet active, or multiplayer client path).
		ModSupport.TryRestoreBookChoicesForActiveRun();
	}
}
