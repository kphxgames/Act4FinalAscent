//=============================================================================
// LinkedShadowIronclad.cs | Act4Placeholder - Slay the Spire 2 Mod
// EN: Phase 4 Linked Shadow, Ironclad variant. Damage same as ShadowIronclad
//     base values; 50% multiplier applied externally before summoning.
// ZH: 四阶段连结之影——铁甲战士变体。基础伤害与ShadowIronclad相同，召唤前从外部应用50%倍率。
//=============================================================================
namespace Act4Placeholder;

public sealed class LinkedShadowIronclad : Phase4LinkedShadow
{
	public override int BaseLinkedShadowHp => Act4Config.LinkedShadowIroncladHp;
	protected override string ShadowVisualsPath => "res://scenes/creature_visuals/ironclad.tscn";
	protected override int BaseMultiDamage => Act4Config.LinkedShadowIroncladBaseMulti;
	protected override int MultiHits       => Act4Config.LinkedShadowIroncladMultiHits;
	protected override int BaseHeavyDamage => Act4Config.LinkedShadowIroncladBaseHeavy;
	// 2-hit warrior: strong per-hit, 2× per-hit vs Silent's 4-hit. Starts on HEAVY.
}
