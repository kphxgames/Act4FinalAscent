//=============================================================================
// HookModifyCardRewardCreationOptionsPatch.cs | Act4Placeholder - Slay the Spire 2 Mod
// EN: Patches Hook.ModifyCardRewardCreationOptions to upgrade card rarity odds in Act 4 combat rewards, shifting Common card odds toward Uncommon and Uncommon toward Rare.
// ZH: 补丁修改Hook.ModifyCardRewardCreationOptions，在第四幕战斗奖励中提升卡牌稀有度概率，将普通卡池升至罕见，罕见卡池升至史诗。
//=============================================================================
using System.Collections.Generic;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Runs;

namespace Act4Placeholder;

[HarmonyPatch(typeof(Hook), "ModifyCardRewardCreationOptions")]
internal static class HookModifyCardRewardCreationOptionsPatch
{
	private static void Postfix(IRunState runState, Player player, ref CardCreationOptions __result)
	{
		RunState val = runState as RunState;
		if (val == null || !ModSupport.IsAct4Placeholder(val) || (object)player.RunState != val || (int)__result.Source != 1 || __result.CustomCardPool != null)
		{
			return;
		}
		CardRarityOddsType rarityOdds = __result.RarityOdds;
		CardRarityOddsType val2 = (((int)rarityOdds == 1) ? ((CardRarityOddsType)2) : (((int)rarityOdds != 2) ? __result.RarityOdds : ((CardRarityOddsType)3)));
		CardRarityOddsType val3 = val2;
		if (val3 != __result.RarityOdds)
		{
			CardCreationOptions val4 = new CardCreationOptions((IEnumerable<CardPoolModel>)__result.CardPools, __result.Source, val3, __result.CardPoolFilter);
			if ((int)__result.Flags > 0)
			{
				val4.WithFlags(__result.Flags);
			}
			if (__result.RngOverride != null)
			{
				val4.WithRngOverride(__result.RngOverride);
			}
			__result = val4;
		}
	}
}
