//=============================================================================
// Act4ArchitectRevivalPower.cs | Act4Placeholder - Slay the Spire 2 Mod
// EN: Hidden power attached to the Architect that intercepts its death to trigger a phase-transition revival sequence, preventing combat from ending prematurely between phases.
// ZH: 附加在建筑师身上的隐藏能力，在其死亡时触发阶段转换复活流程，防止战斗在阶段间过早结束。
//=============================================================================
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;

namespace Act4Placeholder;

internal sealed class Act4ArchitectRevivalPower : PowerModel
{
	private bool _isReviving;

	public override PowerType Type => (PowerType)1;

	public override PowerStackType StackType => (PowerStackType)2;

	protected override bool IsVisibleInternal => false;

	public override bool ShouldAllowHitting(Creature creature)
	{
		if (creature != ((PowerModel)this).Owner)
		{
			return true;
		}
		return !_isReviving;
	}

	public override bool ShouldDie(Creature creature)
	{
		return true;
	}

	public override bool ShouldStopCombatFromEnding()
	{
		return !(((PowerModel)this).Owner.Monster is Act4ArchitectBoss act4ArchitectBoss) || !act4ArchitectBoss.IsPhaseFour || _isReviving;
	}

	public override bool ShouldCreatureBeRemovedFromCombatAfterDeath(Creature creature)
	{
		if (creature != ((PowerModel)this).Owner)
		{
			return true;
		}
		return ((PowerModel)this).Owner.Monster is Act4ArchitectBoss act4ArchitectBoss && act4ArchitectBoss.IsPhaseFour;
	}

	public override bool ShouldPowerBeRemovedAfterOwnerDeath()
	{
		return false;
	}

	public override async Task AfterDeath(PlayerChoiceContext choiceContext, Creature creature, bool wasRemovalPrevented, float deathAnimLength)
	{
		if (wasRemovalPrevented || creature != ((PowerModel)this).Owner)
		{
			return;
		}
		Act4ArchitectBoss architect = ((PowerModel)this).Owner.Monster as Act4ArchitectBoss;
		if (architect == null)
		{
			return;
		}
		if (architect.IsPhaseFour)
		{
			architect.ShowPhaseFourDeathSpeech();
			return;
		}
		_isReviving = true;
		await Task.CompletedTask;
		Act4AudioHelper.PlayTmp("plasma_orb_evoke.mp3");
		architect.BeginAwaitingPhaseTransition(architect.IsPhaseThree ? 4 : (architect.IsPhaseTwo ? 3 : 2));
	}

	public void FinishRevive()
	{
		_isReviving = false;
	}
}
