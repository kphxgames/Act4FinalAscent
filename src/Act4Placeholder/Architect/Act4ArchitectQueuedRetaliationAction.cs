using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Multiplayer.Serialization;

namespace Act4Placeholder;

public sealed class Act4ArchitectQueuedRetaliationAction : GameAction
{
	private readonly Player _player;

	public override ulong OwnerId => _player.NetId;

	public override GameActionType ActionType => GameActionType.CombatPlayPhaseOnly;

	public Act4ArchitectQueuedRetaliationAction(Player player)
	{
		_player = player;
	}

	protected override async Task ExecuteAction()
	{
		CombatState combatState = CombatManager.Instance.DebugOnlyGetState();
		if (combatState == null || combatState.CurrentSide != CombatSide.Player || !CombatManager.Instance.IsPlayPhase)
		{
			return;
		}
		Act4ArchitectBoss architect = combatState.Enemies.Select(enemy => enemy?.Monster).OfType<Act4ArchitectBoss>().FirstOrDefault();
		if (architect == null || architect.IsAwaitingPhaseTransition)
		{
			return;
		}
		await architect.ExecuteQueuedRetaliationActionAsync();
	}

	public override INetAction ToNetAction() => default(NetAct4ArchitectQueuedRetaliationAction);
}

[StructLayout(LayoutKind.Sequential, Size = 1)]
public struct NetAct4ArchitectQueuedRetaliationAction : INetAction, IPacketSerializable
{
	public GameAction ToGameAction(Player player) => new Act4ArchitectQueuedRetaliationAction(player);

	public void Serialize(PacketWriter writer) { }

	public void Deserialize(PacketReader reader) { }
}
