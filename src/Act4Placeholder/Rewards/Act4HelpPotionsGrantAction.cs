//=============================================================================
// Act4HelpPotionsGrantAction.cs | Act4Placeholder - Slay the Spire 2 Mod
// EN: Networked GameAction that grants one BloodPotion and one ExplosiveAmpoule to every player at the start of Act 1; paired with a serializable struct for multiplayer synchronization.
// ZH: 网络同步GameAction，在第一幕开始时为每位玩家授予一瓶血液药水和一瓶爆炸安瓿；附带序列化结构体用于多人联机同步。
//=============================================================================
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models.Potions;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Multiplayer.Serialization;
using MegaCrit.Sts2.Core.Runs;

namespace Act4Placeholder;

/// <summary>
/// Game action that gives BloodPotion + ExplosiveAmpoule to all players at Act 1 start.
/// Sent through ActionQueueSynchronizer by the host; runs on BOTH host and client.
/// </summary>
public class Act4HelpPotionsGrantAction : GameAction
{
	private readonly Player _player;

	public override ulong OwnerId => _player.NetId;

	public override GameActionType ActionType => GameActionType.NonCombat;

	public Act4HelpPotionsGrantAction(Player player) { _player = player; }

	protected override async Task ExecuteAction()
	{
		// EN: This action is synchronized and runs on every machine.
		//     The once-per-run guard has to live here, not only at enqueue time, or host and client
		//     can disagree about whether the freebies were already handed out.
		// ZH: 这个 action 会在每台机器上各执行一次。
		//     “本局只发一次”的判断必须写在这里，不能只在入队时判断，不然主机和客户端容易分歧。
		if (Act4Settings.HelpPotionsGivenForCurrentRun) return;
		Act4Settings.HelpPotionsGivenForCurrentRun = true;
		RunState? runState = RunManager.Instance?.DebugOnlyGetState();
		if (runState == null) return;

		foreach (Player player in runState.Players.ToList())
		{
			await PotionCmd.TryToProcure<BloodPotion>(player);
			await PotionCmd.TryToProcure<ExplosiveAmpoule>(player);
		}
	}

	public override INetAction ToNetAction() => default(NetAct4HelpPotionsGrantAction);
}

/// <summary>
/// Network-serializable counterpart. No payload - the host's Act4Settings drive the grant decision.
/// </summary>
[StructLayout(LayoutKind.Sequential, Size = 1)]
public struct NetAct4HelpPotionsGrantAction : INetAction, IPacketSerializable
{
	public GameAction ToGameAction(Player player) => new Act4HelpPotionsGrantAction(player);
	public void Serialize(PacketWriter writer) { }
	public void Deserialize(PacketReader reader) { }
}
