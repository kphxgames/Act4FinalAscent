//=============================================================================
// EventSynchronizerArchitectHostVotePatch.cs | Act4Placeholder - Slay the Spire 2 Mod
// EN: Forces the host's Act 4 Architect choice to resolve immediately in co-op instead of waiting for every player to vote.
// ZH: 在联机中强制由房主的建筑师第四幕选择立即生效，而不是等待所有玩家投票。
//=============================================================================
using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Events;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Events;
using MegaCrit.Sts2.Core.Multiplayer;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Runs;

namespace Act4Placeholder;

[HarmonyPatch(typeof(EventSynchronizer), "PlayerVotedForSharedOptionIndex")]
internal static class EventSynchronizerArchitectHostVotePatch
{
	private const string NormalAct4OptionKey = "ACT4_PLACEHOLDER.ACT4_OPTION.NORMAL";

	private const string BrutalAct4OptionKey = "ACT4_PLACEHOLDER.ACT4_OPTION.BRUTAL";

	private static void Postfix(EventSynchronizer __instance, Player player, uint optionIndex, uint pageIndex)
	{
		if (!IsAct4ArchitectChoiceActive(__instance))
		{
			return;
		}
		INetGameService netService = Traverse.Create((object)__instance).Field<INetGameService>("_netService").Value;
		if (netService == null || netService.Type == NetGameType.Client)
		{
			return;
		}
		ulong localPlayerId = Traverse.Create((object)__instance).Field<ulong>("_localPlayerId").Value;
		if (player == null || player.NetId != localPlayerId)
		{
			return;
		}
		uint currentPageIndex = Traverse.Create((object)__instance).Field<uint>("_pageIndex").Value;
		if (pageIndex != currentPageIndex)
		{
			return;
		}
		IPlayerCollection? playerCollection = Traverse.Create((object)__instance).Field<IPlayerCollection>("_playerCollection").Value;
		List<uint?>? playerVotes = Traverse.Create((object)__instance).Field<List<uint?>>("_playerVotes").Value;
		if (playerCollection == null || playerVotes == null)
		{
			return;
		}
		Player? hostPlayer = playerCollection.GetPlayer(localPlayerId);
		if (hostPlayer == null)
		{
			return;
		}
		int hostSlotIndex = playerCollection.GetPlayerSlotIndex(hostPlayer);
		uint? hostVote = playerVotes.ElementAtOrDefault(hostSlotIndex);
		if (!hostVote.HasValue || hostVote.Value != optionIndex)
		{
			return;
		}
		AccessTools.Method(typeof(EventSynchronizer), "ChooseSharedEventOption")?.Invoke(__instance, Array.Empty<object>());
	}

	private static bool IsAct4ArchitectChoiceActive(EventSynchronizer synchronizer)
	{
		EventModel? canonicalEvent = Traverse.Create((object)synchronizer).Field<EventModel>("_canonicalEvent").Value;
		if (canonicalEvent is not TheArchitect)
		{
			return false;
		}
		RunState? runState = RunManager.Instance?.DebugOnlyGetState();
		if (runState == null || runState.CurrentActIndex != 2 || ((IReadOnlyCollection<ActModel>)runState.Acts).Count > 3)
		{
			return false;
		}
		// Match ALL Architect event pages during the Act 3 → 4 transition, not just the
		// page that contains the Normal/Brutal Act 4 options. Non-host has no clickable
		// options (UI blocked by NEventRoomAct4HostChoicePatch), so host must be the sole
		// voter on every page including early dialogue steps.
		return true;
	}

	private static bool HasAct4ChoiceOption(EventOption option)
	{
		return option.TextKey == NormalAct4OptionKey || option.TextKey == BrutalAct4OptionKey;
	}
}
