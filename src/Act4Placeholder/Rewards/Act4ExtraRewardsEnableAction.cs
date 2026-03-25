//=============================================================================
// Act4ExtraRewardsEnableAction.cs | Act4Placeholder - Slay the Spire 2 Mod
// EN: Networked GameAction that enables the extra-option reward menus for Empyreal Cache and Royal Treasury events; paired with a serializable struct for multiplayer synchronization.
// ZH: 网络同步GameAction，开启帝国宝库和皇家金库事件的额外奖励选项扩展；附带序列化结构体用于多人联机同步。
//=============================================================================
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Multiplayer.Serialization;

namespace Act4Placeholder;

/// <summary>
/// Game action that enables extra reward choices (3 options instead of 2)
/// for Empyreal Cache and Royal Treasury events for the current run.
/// Sent by host; runs on BOTH host and client via ActionQueueSynchronizer.
/// </summary>
public class Act4ExtraRewardsEnableAction : GameAction
{
	private readonly Player _player;

	public override ulong OwnerId => _player.NetId;

	public override GameActionType ActionType => GameActionType.NonCombat;

	public Act4ExtraRewardsEnableAction(Player player) { _player = player; }

	protected override Task ExecuteAction()
	{
		Act4Settings.ExtraRewardsActiveForCurrentRun = true;
		return Task.CompletedTask;
	}

	public override INetAction ToNetAction() => default(NetAct4ExtraRewardsEnableAction);
}

/// <summary>
/// Network-serializable counterpart. No payload needed - the action just sets the runtime flag.
/// </summary>
[StructLayout(LayoutKind.Sequential, Size = 1)]
public struct NetAct4ExtraRewardsEnableAction : INetAction, IPacketSerializable
{
	public GameAction ToGameAction(Player player) => new Act4ExtraRewardsEnableAction(player);
	public void Serialize(PacketWriter writer) { }
	public void Deserialize(PacketReader reader) { }
}
