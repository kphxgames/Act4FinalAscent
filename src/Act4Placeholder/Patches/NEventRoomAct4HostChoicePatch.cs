//=============================================================================
// NEventRoomAct4HostChoicePatch.cs | Act4Placeholder - Slay the Spire 2 Mod
// EN: Replaces the non-host UI for the Architect Act 4 choice with a locked waiting state so only the host can decide.
// ZH: 将非房主在建筑师第四幕选择界面的 UI 替换为锁定的等待状态，确保只有房主可以决定。
//=============================================================================
using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Events;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Events;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Runs;

namespace Act4Placeholder;

internal static class NEventRoomAct4HostChoicePatch
{
	private const string NormalAct4OptionKey = "ACT4_PLACEHOLDER.ACT4_OPTION.NORMAL";

	private const string BrutalAct4OptionKey = "ACT4_PLACEHOLDER.ACT4_OPTION.BRUTAL";

	private const string WaitingForHostTextKey = "ACT4_PLACEHOLDER.ACT4_OPTION.WAITING_FOR_HOST";

	[HarmonyPatch(typeof(NEventRoom), "SetOptions")]
	[HarmonyPostfix]
	private static void NEventRoomSetOptionsPostfix(NEventRoom __instance, EventModel eventModel)
	{
		if (!ShouldShowWaitingForHost(eventModel) || __instance.Layout == null)
		{
			return;
		}
		__instance.Layout.ClearOptions();
		__instance.Layout.AddOptions(new EventOption[1] { CreateWaitingForHostOption(eventModel) });
		__instance.Layout.DisableEventOptions();
	}

	[HarmonyPatch(typeof(NEventRoom), "OptionButtonClicked")]
	[HarmonyPrefix]
	private static bool NEventRoomOptionButtonClickedPrefix(NEventRoom __instance)
	{
		EventModel? eventModel = Traverse.Create((object)__instance).Field<EventModel>("_event").Value;
		if (eventModel == null)
		{
			return true;
		}
		return !ShouldShowWaitingForHost(eventModel);
	}

	private static bool ShouldShowWaitingForHost(EventModel eventModel)
	{
		if (eventModel is not TheArchitect || eventModel.IsFinished)
		{
			return false;
		}
		RunState? runState = eventModel.Owner?.RunState as RunState ?? RunManager.Instance?.DebugOnlyGetState();
		if (runState == null
			|| ((IReadOnlyCollection<Player>)runState.Players).Count <= 1
			|| ModSupport.IsLocalPlayerHost(runState)
			|| runState.CurrentActIndex != 2
			|| ((IReadOnlyCollection<ActModel>)runState.Acts).Count > 3)
		{
			return false;
		}
		// EN: We lock non-host players out for the whole Architect event, not just the final fork.
		//     Letting a client click earlier dialogue pages is enough to scramble the shared event flow
		//     before the Normal/Brutal choice even appears.
		// ZH: 这里锁的不是最后那个分支页，而是整个建筑师事件。
		//     客户端只要能先点前面的对白页，就足够把共享事件流程点乱。
		return true;
	}

	private static EventOption CreateWaitingForHostOption(EventModel eventModel)
	{
		return new EventOption(eventModel, null, PlainText(ModLoc.T("Waiting for host", "等待房主")), PlainText(ModLoc.T("Only the host can choose Normal or Brutal Act 4.", "只有房主可以选择普通或残酷第四幕。")), WaitingForHostTextKey, Array.Empty<IHoverTip>()).ThatWontSaveToChoiceHistory();
	}

	private static LocString PlainText(string text)
	{
		ModSupport.EnsureAct4DynamicTextLocalizationReady();
		LocString locString = new LocString("events", "ACT4_DYNAMIC_TEXT");
		locString.Add("text", text);
		return locString;
	}
}
