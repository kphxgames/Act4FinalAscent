//=============================================================================
// HookModifyHandDrawPatch.cs | Act4Placeholder - Slay the Spire 2 Mod
// EN: Patches Hook.ModifyHandDraw to grant +3 extra cards per draw to a player when that player's admin or debug mode is enabled.
// ZH: 补丁修改Hook.ModifyHandDraw，在管理员/调试模式开启时为对应玩家每次抽牌额外抽3张。
//=============================================================================
using System;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Hooks;

namespace Act4Placeholder;

[HarmonyPatch(typeof(Hook), "ModifyHandDraw")]
internal static class HookModifyHandDrawPatch
{
	private static void Postfix(Player player, ref decimal __result)
	{
		try
		{
			if (ModSupport.IsAdminEnabled(player))
			{
				__result += 3m;
			}
		}
		catch (Exception exception)
		{
			_ = exception;
		}
	}
}
