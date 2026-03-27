//=============================================================================
// Phase4LinkedShadow.cs | Act4Placeholder - Slay the Spire 2 Mod
// EN: Abstract base for Phase 4 Linked Shadow summons. 3-state attack loop:
//       HEAVY (single) → MULTI → BUFF (block 5% max HP) → loop.
//     Starting state varies per subclass. No debuffs, no HexMove, no BlockPiercer.
//     50% damage via BonusDamageMultiplier set before summon.
//     On death, the Architect loses 10% of its max HP (non-lethal).
//     Concurrent deaths are safe: each shadow calls AccumulateLinkedShadowDrainHp()
//     on the boss, which batches all pending drains into a single deferred apply.
// ZH: 四阶段「连结之影」抽象基类。3态循环（单击→多段→增益/格挡→循环）。
//     起始位根据子类而定。无减益、无诅咒、无穿刺。50%伤害由外部倍率设置。
//     死亡时建筑师损失10%最大HP（不致命），并发死亡由蓄积机制安全处理。
//=============================================================================
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Commands.Builders;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.MonsterMoves;
using MegaCrit.Sts2.Core.MonsterMoves.Intents;
using MegaCrit.Sts2.Core.MonsterMoves.MonsterMoveStateMachine;
using MegaCrit.Sts2.Core.ValueProps;

namespace Act4Placeholder;

public abstract class Phase4LinkedShadow : ArchitectShadowChampion
{
	// --- Per-subclass base HP (solo, unscaled). ---
	public abstract int BaseLinkedShadowHp { get; }

	// --- Starting state: "HEAVY", "MULTI", or "BUFF". Overridden per subclass. ---
	protected virtual string StartingMoveStateId => "HEAVY";

	// --- 3-state machine: HEAVY → MULTI → BUFF → HEAVY (loop) ---
	protected override MonsterMoveStateMachine GenerateMoveStateMachine()
	{
		MoveState heavy = new MoveState("HEAVY", (Func<IReadOnlyList<Creature>, Task>)HeavyMove,
			new AbstractIntent[1] { new SingleAttackIntent((Func<decimal>)(() => (decimal)HeavyDamage)) });
		MoveState multi = new MoveState("MULTI", (Func<IReadOnlyList<Creature>, Task>)MultiMove,
			new AbstractIntent[1] { new DynamicMultiAttackIntent(() => (decimal)MultiDamage, () => MultiHits) });
		MoveState buff  = new MoveState("BUFF",  (Func<IReadOnlyList<Creature>, Task>)LinkedShadowBuffMove,
			GetLinkedShadowBuffIntents());

		// HEAVY → MULTI → BUFF → HEAVY (loop)
		heavy.FollowUpState = multi;
		multi.FollowUpState = buff;
		buff.FollowUpState  = heavy;

		MonsterState startState = StartingMoveStateId switch
		{
			"MULTI" => multi,
			"BUFF"  => buff,
			_       => heavy,
		};

		return new MonsterMoveStateMachine(new MonsterState[3] { heavy, multi, buff }, startState);
	}

	// --- Buff turn intents (can be overridden by subclasses to show extra debuff icon). ---
	protected virtual AbstractIntent[] GetLinkedShadowBuffIntents()
		=> new AbstractIntent[] { new BuffIntent(), new DefendIntent() };

	// --- Called after the block grant during the buff turn. Override to add debuffs. ---
	protected virtual Task OnLinkedShadowBuffAsync() => Task.CompletedTask;

	// --- Block buff: give self 5% of max HP as block, then call optional subclass hook. ---
	private async Task LinkedShadowBuffMove(IReadOnlyList<Creature> _)
	{
		Creature self = ((MonsterModel)this).Creature;
		int blockAmount = Math.Max(1, (int)Math.Floor(self.MaxHp * 0.05m));
		await CreatureCmd.GainBlock(self, (decimal)blockAmount, ValueProp.Move, null, false);
		await OnLinkedShadowBuffAsync();
	}

	// Whether this shadow stands on the right side of the arena (near the Architect).
	// Left-side shadows (Ironclad, Silent, Necrobinder) get ← arrow.
	// Right-side shadows (Defect, Regent) stand near the Architect and get → arrow.
	protected virtual bool IsRightSideShadow => false;

	public override async Task AfterAddedToRoom()
	{
		await base.AfterAddedToRoom();
		// The base class (ArchitectShadowChampion) flips the body to face left (Phase 3 behaviour).
		// Left-side Phase 4 shadows are positioned on the far-left margin and must face RIGHT
		// (toward the Architect/players), so we unflip the body here.
		if (!IsRightSideShadow)
		{
			NCombatRoom? room = NCombatRoom.Instance;
			NCreature? creatureNode = room?.GetCreatureNode(((MonsterModel)this).Creature);
			Node2D body = ModSupport.TryGetCreatureBodyNode(creatureNode);
			if (body != null)
			{
				Vector2 bodyScale = body.Scale;
				bodyScale.X = Math.Abs(bodyScale.X); // unflip: face right
				body.Scale = bodyScale;
			}
		}
		if (IsRightSideShadow)
			await PowerCmd.Apply<BackAttackRightPower>(((MonsterModel)this).Creature, 1m, ((MonsterModel)this).Creature, (CardModel)null, false);
		else
			await PowerCmd.Apply<BackAttackLeftPower>(((MonsterModel)this).Creature, 1m, ((MonsterModel)this).Creature, (CardModel)null, false);
	}

	// No HEX debuffs, no per-attack side effects.
	protected override Task BuffMove(IReadOnlyList<Creature> _)     => Task.CompletedTask;
	protected override Task HexMove(IReadOnlyList<Creature> _)      => Task.CompletedTask;
	protected override Task AfterHeavyAttackAsync(AttackCommand _)  => Task.CompletedTask;
	protected override Task AfterMultiAttackAsync(AttackCommand _)  => Task.CompletedTask;

	// --- Death trigger: drain Architect by 10% max HP (non-lethal, concurrent-safe). ---
	public override void BeforeRemovedFromRoom()
	{
		base.BeforeRemovedFromRoom(); // handles SyncSummonLinkedThornsAsync (no-op in Phase 4)

		// Only trigger on actual death, not on combat-end cleanup of living shadows.
		Creature? self = ((MonsterModel)this).Creature;
		if (self == null || self.IsAlive) return;

		Act4ArchitectBoss? architect = (((MonsterModel)this).CombatState?.Enemies ?? Array.Empty<Creature>())
			.FirstOrDefault(c => c.Monster is Act4ArchitectBoss)?.Monster as Act4ArchitectBoss;
		Creature? archCreature = architect?.Creature;
		if (archCreature == null || !archCreature.IsAlive) return;

		// 10% of Architect's MAX HP per shadow death. Uses the boss accumulator
		// so that concurrent deaths (multiple shadows dying in same update) are
		// batched and applied together without race conditions.
		int drainAmount = Math.Max(1, (int)Math.Ceiling(archCreature.MaxHp * 0.10m));
		architect.AccumulateLinkedShadowDrainHp(drainAmount);
	}
}
