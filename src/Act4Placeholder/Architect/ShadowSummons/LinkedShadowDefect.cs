//=============================================================================
// LinkedShadowDefect.cs | Act4Placeholder - Slay the Spire 2 Mod
// EN: Phase 4 Linked Shadow, Defect variant. No debuffs on attacks.
// ZH: 四阶段连结之影——机甲变体。攻击无额外减益。
//=============================================================================
namespace Act4Placeholder;

public sealed class LinkedShadowDefect : Phase4LinkedShadow
{
	public override int BaseLinkedShadowHp => Act4Config.LinkedShadowDefectHp;
	protected override string StartingMoveStateId => "BUFF"; // buff → single → multi → loop
	protected override string ShadowVisualsPath => "res://scenes/creature_visuals/defect.tscn";
	// Positioned near the Architect (right side) → needs → arrow not ← arrow.
	protected override bool IsRightSideShadow => true;
	protected override int BaseMultiDamage => Act4Config.LinkedShadowDefectBaseMulti;
	protected override int MultiHits       => Act4Config.LinkedShadowDefectMultiHits;
	protected override int BaseHeavyDamage => Act4Config.LinkedShadowDefectBaseHeavy;
	// 3-hit tech spray: same per-hit as 2-hit warriors, higher multi total.
}
