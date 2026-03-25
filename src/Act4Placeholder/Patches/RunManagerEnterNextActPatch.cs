//=============================================================================
// RunManagerEnterNextActPatch.cs | Act4Placeholder - Slay the Spire 2 Mod
// EN: Patches RunManager.EnterNextAct to force-append Act 4 if a pending transition was not yet completed, and redirects post-Act-4-boss run endings to a custom victory handler.
// ZH: 补丁修改RunManager.EnterNextAct，若第四幕过渡未完成则强制补全，并将击败建筑师Boss后的跑图结束重定向至自定义胜利处理器。
//=============================================================================
using System.Collections.Generic;
using System.Threading.Tasks;
using HarmonyLib;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Runs;

namespace Act4Placeholder;

[HarmonyPatch(typeof(RunManager), "EnterNextAct")]
internal static class RunManagerEnterNextActPatch
{
	private static bool Prefix(RunManager __instance, ref Task __result)
	{
		RunState state = __instance.DebugOnlyGetState();

		// Safety net: if an Act 4 transition is in progress but AppendAct4Placeholder
		// hasn't completed yet (race with stale _readyPlayers triggering MoveToNextAct
		// before the async path finishes), force it now before vanilla EnterNextAct
		// checks Acts.Count and falls into WinRun/another Architect event.
		if (state != null && ModSupport.Act4TransitionPending
			&& state.CurrentActIndex == 2
			&& ((IReadOnlyCollection<ActModel>)state.Acts).Count <= 3)
		{
			Log.Info("[Act4Placeholder] Safety net: forcing AppendAct4Placeholder before EnterNextAct (transition was pending)", 1);
			ModSupport.AppendAct4Placeholder(state);
			ModSupport.Act4TransitionPending = false;
			// Fall through to vanilla - Acts.Count is now 4, so EnterAct(3) will fire
		}

		if (!ModSupport.ShouldOverrideFinalActWin(state))
		{
			return true;
		}
		__result = ModSupport.FinishRunAfterAct4BossAsync(__instance);
		return false;
	}
}
