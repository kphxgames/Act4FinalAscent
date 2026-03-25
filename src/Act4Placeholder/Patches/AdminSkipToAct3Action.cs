//=============================================================================
// AdminSkipToAct3Action.cs | Act4Placeholder - Slay the Spire 2 Mod
// EN: Networked GameAction that skips the run to Act 3 on all machines in sync, applying admin stat buffs and suppressing checksums during the transition to prevent false state-divergence errors.
// ZH: 网络同步GameAction，在所有机器上同步将跑图跳转至第三幕，施加管理员属性加成并在过渡期间禁用校验和，以防止伪状态分歧报错。
//=============================================================================
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Multiplayer.Messages.Game.Checksums;
using MegaCrit.Sts2.Core.Multiplayer.Serialization;
using MegaCrit.Sts2.Core.Runs;

namespace Act4Placeholder;

/// <summary>
/// Game action that skips to Act 3 on all machines in sync.
/// Sent through the ActionQueueSynchronizer so host and client both execute it.
/// </summary>
public class AdminSkipToAct3Action : GameAction
{
	/// <summary>
	/// When true, checksum generation is suppressed. Used during admin skip to
	/// prevent divergence from partially-synced Neow event state (e.g. client
	/// chose a Neow blessing granting potions that the host hasn't received yet).
	/// </summary>
	internal static bool SuppressChecksums { get; set; }

	private readonly Player _player;

	public override ulong OwnerId => _player.NetId;

	public override GameActionType ActionType => GameActionType.NonCombat;

	public AdminSkipToAct3Action(Player player)
	{
		_player = player;
	}

	protected override async Task ExecuteAction()
	{
		RunState? runState = RunManager.Instance?.DebugOnlyGetState();
		if (runState == null || RunManager.Instance == null)
			return;
		if (runState.CurrentActIndex >= 2)
			return;

		// Apply admin buffs to ALL players here - runs on BOTH host and client through the
		// action queue, so game state mutations are identical on both machines.
		// Do NOT do this outside the action queue (e.g. in OnAdminButtonPressed) or the
		// resulting MaxEnergy/MaxHP diff will cause an immediate state divergence.
		foreach (Player player in runState.Players.ToList())
			ModSupport.ApplyAdminStatBuffsOutOfCombat(player);

		// Suppress checksums during the act transition. EnterAct exits the current
		// rooms (e.g. the Neow event room) which triggers a checksum, but host and
		// client may have processed different Neow choices (e.g. client gained potions
		// the host doesn't know about). Suppressing prevents a false state-divergence
		// disconnect. Both machines suppress, so no comparison occurs.
		SuppressChecksums = true;
		try
		{
			await RunManager.Instance.EnterAct(2, true);
		}
		finally
		{
			SuppressChecksums = false;
		}
	}

	public override INetAction ToNetAction()
	{
		return default(NetAdminSkipToAct3Action);
	}
}

/// <summary>
/// Network-serializable counterpart. The struct name is used for type registration
/// (sorted alphabetically), so both host and client must have the same mod DLL.
/// </summary>
[StructLayout(LayoutKind.Sequential, Size = 1)]
public struct NetAdminSkipToAct3Action : INetAction, IPacketSerializable
{
	public GameAction ToGameAction(Player player)
	{
		return new AdminSkipToAct3Action(player);
	}

	public void Serialize(PacketWriter writer)
	{
	}

	public void Deserialize(PacketReader reader)
	{
	}
}

/// <summary>
/// Suppresses checksum generation while <see cref="AdminSkipToAct3Action.SuppressChecksums"/>
/// is true, preventing false divergence during admin-triggered act transitions.
/// Runs on both host and client so neither side produces a checksum to compare.
/// </summary>
[HarmonyPatch(typeof(ChecksumTracker), nameof(ChecksumTracker.GenerateChecksum),
	new Type[] { typeof(string), typeof(GameAction) })]
static class ChecksumTrackerAdminSuppressPatch
{
	static bool Prefix(ref NetChecksumData __result)
	{
		if (!AdminSkipToAct3Action.SuppressChecksums)
			return true;
		__result = default;
		return false;
	}
}
