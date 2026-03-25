//=============================================================================
// LinkedShadowNecrobinder.cs | Act4Placeholder - Slay the Spire 2 Mod
// EN: Phase 4 Linked Shadow, Necrobinder variant. No debuffs on attacks.
// ZH: 四阶段连结之影——亡灵缚者变体。攻击无额外减益。
//=============================================================================
namespace Act4Placeholder;

public sealed class LinkedShadowNecrobinder : Phase4LinkedShadow
{
	public override int BaseLinkedShadowHp => Act4Config.LinkedShadowNecrobinderHp;
	protected override string ShadowVisualsPath => "res://scenes/creature_visuals/necrobinder.tscn";
	protected override int BaseMultiDamage => Act4Config.LinkedShadowNecrobinderBaseMulti;
	protected override int MultiHits       => Act4Config.LinkedShadowNecrobinderMultiHits;
	protected override int BaseHeavyDamage => Act4Config.LinkedShadowNecrobinderBaseHeavy;
	// 3-hit curse spread: same per-hit as 2-hit warriors, higher multi total.
}
