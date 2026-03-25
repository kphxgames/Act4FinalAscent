//=============================================================================
// EventSynchronizerArchitectChoicePatch.cs | Act4Placeholder - Slay the Spire 2 Mod
// EN: Patches EventSynchronizer.ChooseSharedEventOption to correctly detect and handle the Act 4 difficulty-selection options on the Architect event in multiplayer co-op.
// ZH: 补丁修改EventSynchronizer.ChooseSharedEventOption，在多人联机中正确检测并处理建筑师事件上的第四幕难度选择选项。
//=============================================================================
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Events;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Events;
using MegaCrit.Sts2.Core.Multiplayer;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Multiplayer.Messages.Game.Sync;
using MegaCrit.Sts2.Core.Runs;

namespace Act4Placeholder;

[HarmonyPatch(typeof(EventSynchronizer), "ChooseSharedEventOption")]
internal static class EventSynchronizerArchitectChoicePatch
{
	private const string NormalAct4OptionKey = "ACT4_PLACEHOLDER.ACT4_OPTION.NORMAL";

	private const string BrutalAct4OptionKey = "ACT4_PLACEHOLDER.ACT4_OPTION.BRUTAL";

	private static bool Prefix(EventSynchronizer __instance)
	{
		EventModel canonicalEvent = Traverse.Create((object)__instance).Field<EventModel>("_canonicalEvent").Value;
		if (canonicalEvent is not TheArchitect)
		{
			return true;
		}

		// Use RunManager state directly - the canonical EventModel never has BeginEvent()
		// called, so Owner is always null and CurrentOptions lacks our injected Act 4 options.
		RunState runState = RunManager.Instance?.DebugOnlyGetState();
		if (runState == null || runState.CurrentActIndex != 2
			|| ((IReadOnlyCollection<ActModel>)runState.Acts).Count > 3)
		{
			return true;
		}

		// Apply host-decides logic to ALL pages of the Architect event in the Act 3 → 4
		// transition, not just the page containing Normal/Brutal Act 4 options. Non-host
		// has no clickable options (UI blocked), so host's vote must resolve every page.

		INetGameService netService = Traverse.Create((object)__instance).Field<INetGameService>("_netService").Value;
		if (netService == null || netService.Type == NetGameType.Client)
		{
			return true;
		}
		IPlayerCollection playerCollection = Traverse.Create((object)__instance).Field<IPlayerCollection>("_playerCollection").Value;
		ulong localPlayerId = Traverse.Create((object)__instance).Field<ulong>("_localPlayerId").Value;
		List<uint?> playerVotes = Traverse.Create((object)__instance).Field<List<uint?>>("_playerVotes").Value;
		RunLocationTargetedMessageBuffer messageBuffer = Traverse.Create((object)__instance).Field<RunLocationTargetedMessageBuffer>("_messageBuffer").Value;
		if (playerCollection == null || playerVotes == null || messageBuffer == null)
		{
			return true;
		}
		Player hostPlayer = playerCollection.GetPlayer(localPlayerId);
		if (hostPlayer == null)
		{
			return true;
		}
		int hostSlotIndex = playerCollection.GetPlayerSlotIndex(hostPlayer);
		uint? hostVote = playerVotes.ElementAtOrDefault(hostSlotIndex);
		if (!hostVote.HasValue)
		{
			return false;
		}
		uint chosenOption = hostVote.Value;
		uint pageIndex = Traverse.Create((object)__instance).Field<uint>("_pageIndex").Value;
		netService.SendMessage(new SharedEventOptionChosenMessage
		{
			optionIndex = chosenOption,
			pageIndex = pageIndex,
			location = messageBuffer.CurrentLocation
		});
		AccessTools.Method(typeof(EventSynchronizer), "ChooseOptionForSharedEvent")?.Invoke(__instance, new object[1] { chosenOption });
		return false;
	}
}
