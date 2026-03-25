//=============================================================================
// NMapScreenOpenPatch.cs | Act4Placeholder - Slay the Spire 2 Mod
// EN: Patches NMapScreen.Open to apply Act 1 start settings (extra rewards flag, help potions grant) at the beginning of each run, handling both solo and co-op via direct application or synchronized GameActions.
// ZH: 补丁修改NMapScreen.Open，在跑图开始时应用第一幕起始设置（额外奖励标志、辅助药水授予），单人直接应用，联机通过同步GameAction实现。
//=============================================================================
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Models.Potions;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;
using MegaCrit.Sts2.Core.Runs;

namespace Act4Placeholder;

[HarmonyPatch(typeof(NMapScreen), "Open")]
internal static class NMapScreenOpenPatch
{
	private static void Postfix(NMapScreen __instance)
	{
		RunState? runState = RunManager.Instance?.DebugOnlyGetState();
		if (runState != null && runState.Acts.Count > 3 && runState.CurrentActIndex == 3)
			ModSupport.EnsureAct4ArchitectBossConfigured(runState, 3);

		// Apply host-authoritative Act 1 start settings (once per run, Act 0 only).
		if (runState == null || runState.CurrentActIndex != 0) return;

		int playerCount = ((IReadOnlyCollection<Player>)runState.Players).Count;
		if (playerCount <= 1)
		{
			// Solo: apply directly.
			if (Act4Settings.ExtraRewardsEnabled && !Act4Settings.ExtraRewardsActiveForCurrentRun)
				Act4Settings.ExtraRewardsActiveForCurrentRun = true;
			if (Act4Settings.HelpPotionsEnabled && !Act4Settings.HelpPotionsGivenForCurrentRun)
				TaskHelper.RunSafely(GrantHelpPotionsSoloAsync(runState));
		}
		else
		{
			// Co-op: host enqueues actions that run on BOTH machines; client does nothing.
			if (!ModSupport.IsLocalPlayerHost(runState)) return;
			Player? me = LocalContext.GetMe(runState);
			if (me == null) return;
			var aq = RunManager.Instance?.ActionQueueSynchronizer;
			if (aq == null) return;
			if (Act4Settings.ExtraRewardsEnabled && !Act4Settings.ExtraRewardsActiveForCurrentRun)
				aq.RequestEnqueue(new Act4ExtraRewardsEnableAction(me));
			if (Act4Settings.HelpPotionsEnabled && !Act4Settings.HelpPotionsGivenForCurrentRun)
				aq.RequestEnqueue(new Act4HelpPotionsGrantAction(me));
		}
	}

	private static async Task GrantHelpPotionsSoloAsync(RunState runState)
	{
		if (Act4Settings.HelpPotionsGivenForCurrentRun) return;
		Act4Settings.HelpPotionsGivenForCurrentRun = true;
		foreach (Player player in runState.Players.ToList())
		{
			await PotionCmd.TryToProcure<FairyInABottle>(player);
			await PotionCmd.TryToProcure<BloodPotion>(player);
		}
	}
}
