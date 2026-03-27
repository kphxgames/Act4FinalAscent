//=============================================================================
// ArchitectSummonedFogmog.cs | Act4Placeholder - Slay the Spire 2 Mod
// EN: Defines the Fogmog minion summoned by the Architect; alternates between a
//     two-hit Spore Flurry and a single-hit Spore Slam. On death it heals the
//     killer; each enemy turn it heals the Architect. Enters with MysteriousSpores
//     and SporeNourishment powers (set in AfterAddedToRoom).
// ZH: 定义建筑师召唤的雾魔随从，交替使用「孢子乱舞」（两连击）和「孢子重击」。
//     死亡时治疗击杀玩家，每个敌方回合治疗建筑师。入场时自动获得神秘孢子和孢子滋养能力。
//=============================================================================
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Animation;
using MegaCrit.Sts2.Core.Audio;
using MegaCrit.Sts2.Core.Bindings.MegaSpine;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Commands.Builders;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.MonsterMoves;
using MegaCrit.Sts2.Core.MonsterMoves.Intents;
using MegaCrit.Sts2.Core.MonsterMoves.MonsterMoveStateMachine;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Settings;
using MegaCrit.Sts2.Core.ValueProps;

namespace Act4Placeholder;

internal sealed class ArchitectSummonedFogmog : MonsterModel
{
	// =========================================================================
	// Tuning constants, edit these to adjust Fogmog behaviour.
	// 调整这些常量来修改雾魔的数值。
	// =========================================================================

	/// <summary>Base HP of the Fogmog. / 雾魔的基础HP。</summary>
	private const int FogmogHp = 40;

	/// <summary>Damage per hit of Spore Flurry (2 hits). / 孢子乱舞每次攻击伤害（共2次）。</summary>
	private const int FlurryHitDamage = 4;

	/// <summary>Damage of Spore Slam (1 hit). / 孢子重击伤害（1次）。</summary>
	private const int SlamHitDamage = 8;

	/// <summary>Initial MysteriousSpores stacks applied on spawn. / 入场时施加的神秘孢子初始层数。</summary>
	private const int InitialSporeStacks = 30;

	/// <summary>Initial SporeNourishment stacks applied on spawn. / 入场时施加的孢子滋养初始层数。</summary>
	private const int InitialNourishmentStacks = 5;

	/// <summary>Heal given to the Architect each Fogmog enemy-turn (percent of Architect max HP). / 每个敌方回合治疗建筑师的比例（建筑师最大HP的百分比）。</summary>
	private const decimal ArchitectHealPercent = 0.05m;

	/// <summary>Heal given to the killing player when the Fogmog dies (percent of their max HP). / 雾魔死亡时治疗击杀玩家的比例（击杀者最大HP的百分比）。</summary>
	private const decimal DeathHealPercent = 0.30m;

	// =========================================================================

	private const string AttackTrigger = "Attack";

	private const string SummonSfx = "event:/sfx/enemy/enemy_attacks/fogmog/fogmog_summon";

	private bool _triggeredDeathHeal;

	private Creature? _lastEligibleKiller;

	protected override string VisualsPath => SceneHelper.GetScenePath("creature_visuals/fogmog");

	public override int MinInitialHp => FogmogHp;

	public override int MaxInitialHp => MinInitialHp;

	// Instant mode: NMonsterDeathVfx.Create returns null, and AnimDie's MoveChild crashes on null.
	// Guard here so normal/fast gameplay still gets the fade VFX.
	public override bool ShouldFadeAfterDeath =>
		SaveManager.Instance?.PrefsSave.FastMode != FastModeType.Instant;

	private int FlurryDamage => FlurryHitDamage;

	private int SlamDamage => SlamHitDamage;

	protected override MonsterMoveStateMachine GenerateMoveStateMachine()
	{
		List<MonsterState> list = new List<MonsterState>();
		MoveState moveState = new MoveState("SPORE_FLURRY", SporeFlurryMove, new MultiAttackIntent(FlurryDamage, 2), new DebuffIntent());
		MoveState moveState2 = new MoveState("SPORE_SLAM", SporeSlamMove, new SingleAttackIntent(SlamDamage), new DebuffIntent());
		moveState.FollowUpState = moveState2;
		moveState2.FollowUpState = moveState;
		list.Add(moveState);
		list.Add(moveState2);
		return new MonsterMoveStateMachine(list, moveState);
	}

	public override async Task AfterAddedToRoom()
	{
		await base.AfterAddedToRoom();
		await PowerCmd.Apply<MysteriousSporesPower>(base.Creature, (decimal)InitialSporeStacks, base.Creature, null, false);
		await PowerCmd.Apply<SporeNourishmentPower>(base.Creature, (decimal)InitialNourishmentStacks, base.Creature, null, false);
	}

	public override async Task AfterTurnEnd(PlayerChoiceContext choiceContext, CombatSide side)
	{
		await base.AfterTurnEnd(choiceContext, side);
		if (side != CombatSide.Enemy || !base.Creature.IsAlive)
		{
			return;
		}
		Creature architect = FindLivingArchitect();
		if (architect == null || !architect.IsAlive)
		{
			return;
		}
		decimal healAmount = System.Math.Max(1m, System.Math.Ceiling((decimal)architect.MaxHp * ArchitectHealPercent));
		await CreatureCmd.Heal(architect, healAmount);
	}

	public override async Task AfterDamageReceived(PlayerChoiceContext choiceContext, Creature target, DamageResult result, ValueProp props, Creature? dealer, CardModel? cardSource)
	{
		await base.AfterDamageReceived(choiceContext, target, result, props, dealer, cardSource);
		if (target != base.Creature || !ShouldTriggerDeathHeal())
		{
			return;
		}
		Creature? healer = ResolvePlayerHealer(choiceContext, dealer, cardSource);
		if (healer == null)
		{
			return;
		}
		_lastEligibleKiller = healer;
		if (_triggeredDeathHeal || target.CurrentHp > 0m)
		{
			return;
		}
		_triggeredDeathHeal = true;
		await CreatureCmd.Heal(healer, System.Math.Max(1m, System.Math.Ceiling((decimal)healer.MaxHp * DeathHealPercent)));
	}

	public override async Task AfterDeath(PlayerChoiceContext choiceContext, Creature creature, bool wasRemovalPrevented, float deathAnimLength)
	{
		await base.AfterDeath(choiceContext, creature, wasRemovalPrevented, deathAnimLength);
		if (_triggeredDeathHeal || creature != base.Creature || wasRemovalPrevented || !ShouldTriggerDeathHeal())
		{
			return;
		}
		Creature? healer = ResolvePlayerHealer(choiceContext, null, null) ?? ResolveEligibleKillerFromHistory() ?? _lastEligibleKiller;
		if (healer == null)
		{
			healer = base.CombatState?.Players?.Select((Player p) => p?.Creature).FirstOrDefault((Creature c) => c != null && c.IsAlive);
		}
		if (healer == null || !healer.IsPlayer || !healer.IsAlive)
		{
			return;
		}
		_triggeredDeathHeal = true;
		await CreatureCmd.Heal(healer, System.Math.Max(1m, System.Math.Ceiling((decimal)healer.MaxHp * DeathHealPercent)));
	}

	private static Creature? ResolvePlayerHealer(PlayerChoiceContext? choiceContext, Creature? dealer, CardModel? cardSource)
	{
		Creature? healer = dealer?.Player?.Creature ?? dealer?.PetOwner?.Creature ?? (dealer != null && dealer.IsPlayer ? dealer : null);
		if (healer != null && healer.IsPlayer)
		{
			return healer;
		}
		Creature? cardOwner = cardSource?.Owner?.Creature;
		if (cardOwner != null && cardOwner.IsPlayer)
		{
			return cardOwner;
		}
		CardModel contextCard = choiceContext?.LastInvolvedModel as CardModel;
		Creature contextOwner = contextCard?.Owner?.Creature;
		if (contextOwner != null && contextOwner.IsPlayer)
		{
			return contextOwner;
		}
		return null;
	}

	private Creature? ResolveEligibleKillerFromHistory()
	{
		return null;
	}

	private Creature? FindLivingArchitect()
	{
		return base.CombatState?.Enemies.FirstOrDefault((Creature c) => c != null && c.IsAlive && c.Monster is Act4ArchitectBoss);
	}

	private bool ShouldTriggerDeathHeal()
	{
		return FindLivingArchitect() != null;
	}

	private async Task SporeFlurryMove(IReadOnlyList<Creature> targets)
	{
		AttackCommand command = await DamageCmd.Attack((decimal)FlurryDamage).WithHitCount(2).FromMonster(this)
			.WithAttackerAnim(AttackTrigger, 0.5f, null)
			.WithAttackerFx(null, AttackSfx, null)
			.WithHitFx("vfx/vfx_attack_slash", null, null)
			.OnlyPlayAnimOnce()
			.Execute(null);
		await ApplyPoisonFromAttackAsync(command);
	}

	private async Task SporeSlamMove(IReadOnlyList<Creature> targets)
	{
		AttackCommand command = await DamageCmd.Attack((decimal)SlamDamage).FromMonster(this).WithAttackerAnim(AttackTrigger, 0.5f, null)
			.WithAttackerFx(null, AttackSfx, null)
			.WithHitFx("vfx/vfx_attack_slash", null, null)
			.Execute(null);
		await ApplyPoisonFromAttackAsync(command);
	}

	private async Task ApplyPoisonFromAttackAsync(AttackCommand command)
	{
		foreach (IGrouping<Creature, DamageResult> grouping in command.Results.Where((DamageResult result) => result.Receiver.IsPlayer && result.Receiver.IsAlive).GroupBy((DamageResult result) => result.Receiver))
		{
			await PowerCmd.Apply<PoisonPower>(grouping.Key, grouping.Count() * 2, base.Creature, null, false);
		}
	}

	public override CreatureAnimator GenerateAnimator(MegaSprite controller)
	{
		AnimState animState = new AnimState("idle_loop", isLooping: true);
		AnimState animState2 = new AnimState("summon");
		AnimState animState3 = new AnimState("attack");
		AnimState animState4 = new AnimState("hurt");
		AnimState state = new AnimState("die");
		animState2.NextState = animState;
		animState3.NextState = animState;
		animState4.NextState = animState;
		CreatureAnimator creatureAnimator = new CreatureAnimator(animState, controller);
		creatureAnimator.AddAnyState("Dead", state);
		creatureAnimator.AddAnyState("Hit", animState4);
		creatureAnimator.AddAnyState("Summon", animState2);
		creatureAnimator.AddAnyState("Attack", animState3);
		return creatureAnimator;
	}
}
