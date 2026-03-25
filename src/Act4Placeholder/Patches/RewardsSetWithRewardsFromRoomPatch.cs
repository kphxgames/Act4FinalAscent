//=============================================================================
// RewardsSetWithRewardsFromRoomPatch.cs | Act4Placeholder - Slay the Spire 2 Mod
// EN: Patches RewardsSet.WithRewardsFromRoom to append gold rewards to Act 4 non-boss combat rooms: 55-90 gold for normal fights and 110-160 gold for elite fights.
// ZH: 补丁修改RewardsSet.WithRewardsFromRoom，在第四幕非Boss战斗奖励中追加金币：普通战55-90金，精英战110-160金。
//=============================================================================
using System;
using HarmonyLib;
using MegaCrit.Sts2.Core.Rewards;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;

namespace Act4Placeholder;

[HarmonyPatch(typeof(RewardsSet), "WithRewardsFromRoom")]
internal static class RewardsSetWithRewardsFromRoomPatch
{
	private static void Postfix(RewardsSet __instance, AbstractRoom room)
	{
		IRunState runState = __instance.Player.RunState;
		RunState val = runState as RunState;
		if (val != null && ModSupport.IsAct4Placeholder(val) && room is CombatRoom && (int)room.RoomType != 3)
		{
			RoomType roomType = room.RoomType;
			ValueTuple<int, int> val2 = (((int)roomType == 1) ? new ValueTuple<int, int>(55, 90) : (((int)roomType != 2) ? new ValueTuple<int, int>(0, 0) : new ValueTuple<int, int>(110, 160)));
			ValueTuple<int, int> val3 = val2;
			if (val3.Item1 > 0)
			{
				__instance.Rewards.Add((Reward)new GoldReward(val3.Item1, val3.Item2, __instance.Player, false));
			}
		}
	}
}
