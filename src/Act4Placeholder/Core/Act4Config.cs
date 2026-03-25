//=============================================================================
// Act4Config.cs | Act4Placeholder - Slay the Spire 2 Mod
// EN: Central configuration file. All tunable Act 4 balance constants live here  - 
//     elite HP, multiplayer scaling, brutal mode, and all Architect boss numbers.
//     Change these values to retune difficulty without hunting through multiple files.
// ZH: 集中配置文件。所有第四幕可调整的平衡常量均在此处——
//     精英HP、多人缩放、残酷模式以及建筑师Boss的全部数值。
//     修改这些数值即可调整难度，无需在多个文件中逐一查找。
//=============================================================================

namespace Act4Placeholder;

internal static class Act4Config
{
	// =========================================================================
	// Section 1: ACT 4 ELITE MONSTER BASE HP (1-Player, Normal Mode)
	// EN: These are the exact HP values shown in a 1-player normal-mode Act 4 run.
	//     All other player counts and brutal mode are derived multiplicatively from these.
	//     Formula: finalHp = ceil(baseHp × mpMult × brutalMult × ascensionMult)
	// ZH: 这些是1人普通模式第四幕运行中精英的确切HP值。
	//     所有其他人数和残酷模式均从这些值乘法推导。
	//     公式：最终HP = 上取整(基础HP × 联机倍率 × 残酷倍率 × 升华倍率)
	// =========================================================================

	/// EN: Flail Knight HP (1p normal).       | ZH: 连枷骑士HP（1人普通）。
	internal const int EliteFlailKnightHp    = 147;
	/// EN: Spectral Knight HP (1p normal).    | ZH: 幽灵骑士HP（1人普通）。
	internal const int EliteSpectralKnightHp = 124;
	/// EN: Magi Knight HP (1p normal).        | ZH: 法术骑士HP（1人普通）。
	internal const int EliteMagiKnightHp     = 91;
	/// EN: Mecha Knight HP (1p normal), intentionally higher than Soul Nexus. | ZH: 机械骑士HP（1人普通）——刻意高于灵魂枢纽。
	internal const int EliteMechaKnightHp    = 580;
	/// EN: Soul Nexus HP (1p normal).         | ZH: 灵魂枢纽HP（1人普通）。
	internal const int EliteSoulNexusHp      = 500;

	// =========================================================================
	// Section 2: MULTIPLAYER HP MULTIPLIERS
	// EN: Applied to all Act 4 non-boss enemies when player count > 1.
	//     5p+ uses per-player scaling: finalMult = playerCount × MpPerPlayerScaling.
	// ZH: 玩家数>1时应用于所有第四幕非Boss敌人。
	//     5人以上使用每人缩放：最终倍率 = 人数 × MpPerPlayerScaling。
	// =========================================================================

	/// EN: 2-player HP multiplier.                          | ZH: 2人联机HP倍率。
	internal const decimal Mp2pHpMult         = 2.0m;
	/// EN: 3-player HP multiplier.                          | ZH: 3人联机HP倍率。
	internal const decimal Mp3pHpMult         = 3.9m;
	/// EN: 4-player HP multiplier.                          | ZH: 4人联机HP倍率。
	internal const decimal Mp4pHpMult         = 5.2m;
	/// EN: Per-player scaling factor for 5+ players (HP = playerCount × this). | ZH: 5人以上时每位玩家的HP缩放系数（HP = 人数 × 本值）。
	internal const decimal MpPerPlayerScaling = 1.3m;

	// =========================================================================
	// Section 3: BRUTAL MODE HP MULTIPLIERS (by player count)
	// EN: Applied on top of the multiplayer multiplier in Brutal runs. Caps at 5p+.
	// ZH: 残酷模式下叠加在联机倍率之上，5人以上封顶。
	// =========================================================================

	/// EN: Brutal HP multipliers: 1p / 2p / 3p / 4p / 5p+ cap.
	/// ZH: 残酷模式HP倍率：1人 / 2人 / 3人 / 4人 / 5人封顶。
	internal const decimal Brutal1pHpMult    = 1.3m;
	internal const decimal Brutal2pHpMult    = 1.3m;
	internal const decimal Brutal3pHpMult    = 1.4m;
	internal const decimal Brutal4pHpMult    = 1.5m;
	internal const decimal Brutal5pCapHpMult = 1.6m;
	/// EN: Brutal-only: +1% max HP per Ascension level for all Act 4 enemies. | ZH: 仅残酷模式：每级升华为所有第四幕敌人增加1%最大HP。
	internal const decimal BrutalAscensionHpPerLevel = 0.01m;

	/// EN: Ascension 8: flat +5% HP for Architect, all summons, and Act 4 enemies (normal + brutal). | ZH: 升华8：建筑师、所有召唤物及第四幕敌人+5%HP（普通和残酷均适用）。
	internal const decimal Ascension8HpBonus = 0.05m;
	/// EN: Ascension 9: flat +5% damage for all Act 4 enemies (normal + brutal). | ZH: 升华9：所有第四幕敌人+5%伤害（普通和残酷均适用）。
	internal const decimal Ascension9DmgBonus = 0.05m;

	// =========================================================================
	// Section 4: ARCHITECT BOSS HP
	// EN: Phase 1 HP is computed from ArchitectP1SoloHp and multiplied by player-count factors.
	//     Brutal multipliers from Section 3 also apply.
	//     Phases 2–4 HP = PhaseOneMaxHpSnapshot × the corresponding multiplier.
	// ZH: 一阶段HP由ArchitectP1SoloHp按人数计算，残酷模式倍率同样适用（第3节）。
	//     二至四阶段HP = 一阶段HP快照 × 对应倍率。
	// =========================================================================

	/// EN: Architect Phase 1 HP for solo (1-player) runs. | ZH: 建筑师一阶段HP（单人）。
	internal const decimal ArchitectP1SoloHp         = 400m;
	/// EN: 2-player Phase 1 HP = SoloHp × this.           | ZH: 2人一阶段HP = 单人HP × 本值。
	internal const decimal ArchitectP1To2pMultiplier  = 2.1m;
	/// EN: 3p+ Phase 1 HP = SoloHp × playerCount × this. | ZH: 3人以上一阶段HP = 单人HP × 人数 × 本值。
	internal const decimal ArchitectMpPerPlayerScaling = 1.3m;

	/// EN: Phase 2 max HP = Phase1 snapshot × this.
	///     Scaled from old P1=525 so absolute P2 HP is slightly lower than before (525×1.4=735 → 400×1.78=712).
	/// ZH: 二阶段最大HP = 一阶段HP快照 × 本值。从原P1=525按比例换算并小幅下调（735→712）。
	internal const decimal ArchitectP2HpMultiplier   = 1.78m;
	/// EN: Phase 3 max HP = Phase1 snapshot × this.
	///     Scaled from old (525×1.6=840) → 400×2.1=840, same absolute value maintained.
	/// ZH: 三阶段最大HP = 一阶段HP快照 × 本值。保持与原来相同的绝对值（840）。
	internal const decimal ArchitectP3HpMultiplier   = 2.1m;
	/// EN: Phase 4 max HP = Phase1 snapshot × this.
	///     Scaled from old (525×3.6=1890) → 400×4.73=1892, same absolute value maintained.
	/// ZH: 四阶段最大HP = 一阶段HP快照 × 本值。保持与原来相同的绝对值（~1890）。
	internal const decimal ArchitectP4HpMultiplier   = 4.73m;

	// =========================================================================
	// Section 5: ARCHITECT BOSS DAMAGE
	// EN: Base damage values before co-op bonuses and the solo heavy-attack nerf.
	//     Heavy attacks in solo get ×ArchitectSoloHeavyNerfFactor (ceiling).
	//     Co-op bonuses: 3p → +CoOpSingleBonus3p / 4p+ → +CoOpSingleBonus4p for heavy;
	//                    3p → +CoOpMultiBonus3p  / 4p+ → +CoOpMultiBonus4p  for multi-hit.
	// ZH: 应用联机加成和单人重击削弱前的基础伤害值。
	//     单人重击 × ArchitectSoloHeavyNerfFactor（上取整）。
	//     联机加成：3人 → +CoOpSingleBonus3p / 4人+ → +CoOpSingleBonus4p（重击）；
	//              3人 → +CoOpMultiBonus3p  / 4人+ → +CoOpMultiBonus4p（多段攻击）。
	// =========================================================================

	// -- Heavy-attack (single-target) damage bases ----------------------------
	internal const int ArchitectP1HeavyDamage = 9;
	internal const int ArchitectP2HeavyDamage = 15;
	internal const int ArchitectP3HeavyDamage = 16;
	internal const int ArchitectP4HeavyDamage = 11;

	// -- Multi-hit attack per-hit damage bases --------------------------------
	internal const int ArchitectP1MultiDamage = 3;
	internal const int ArchitectP2MultiDamage = 5;
	internal const int ArchitectP3MultiDamage = 5;
	internal const int ArchitectP4MultiDamage = 3;

	// -- Multi-hit attack hit counts ------------------------------------------
	internal const int ArchitectP1MultiHits   = 3;
	internal const int ArchitectP2MultiHits   = 4;
	internal const int ArchitectP3MultiHits   = 4;
	internal const int ArchitectP4MultiHits   = 3;

	// -- Co-op damage bonuses (added to base damage per attack) ---------------
	/// EN: Extra single-target damage per attack in 4+ player sessions.    | ZH: 4人以上联机时单体攻击每次额外伤害。
	internal const int CoOpSingleBonus4p = 4;
	/// EN: Extra single-target damage per attack in 3-player sessions.     | ZH: 3人联机时单体攻击每次额外伤害。
	internal const int CoOpSingleBonus3p = 2;
	/// EN: Extra per-hit damage for multi-hit attacks in 4+ player sessions. | ZH: 4人以上联机时多段攻击每段额外伤害。
	internal const int CoOpMultiBonus4p  = 2;
	/// EN: Extra per-hit damage for multi-hit attacks in 3-player sessions.  | ZH: 3人联机时多段攻击每段额外伤害。
	internal const int CoOpMultiBonus3p  = 1;

	/// EN: Solo heavy-attack nerf multiplier (ceiling applied). 1p heavy attacks deal ×this damage.
	/// ZH: 单人重击削弱系数（上取整）。单人重击伤害 × 本值。
	internal const decimal SoloHeavyNerfFactor = 0.8m;

	// =========================================================================
	// Section 6: ARCHITECT STRENGTH SYSTEM
	// EN: Strength caps and gain cadences per phase.
	//     Under-cap: gain 1 Strength every UnderCapCadence rounds.
	//     Over-cap:  gain 1 Strength every OverCapCadence rounds (all phases).
	// ZH: 各阶段的力量上限与获取节奏。
	//     低于上限：每UnderCapCadence轮获得1点力量。
	//     超出上限：每OverCapCadence轮获得1点力量（所有阶段相同）。
	// =========================================================================

	internal const int ArchitectP1StrengthCap = 3;
	internal const int ArchitectP2StrengthCap = 6;
	internal const int ArchitectP3StrengthCap = 9;
	internal const int ArchitectP4StrengthCap = 12; // Pretty sure Act 4 doesn't have Strength

	/// EN: Under-cap strength cadence for phases 1–2 (gain every N rounds). | ZH: 一二阶段低于上限时的力量节奏（每N轮获得）。
	internal const int ArchitectP1P2UnderCapCadence = 3;
	/// EN: Under-cap strength cadence for phase 3 (every N rounds).         | ZH: 三阶段低于上限时的力量节奏（每N轮获得）。
	internal const int ArchitectP3UnderCapCadence   = 1;
	/// EN: Over-cap strength cadence for all phases (every N rounds).        | ZH: 所有阶段超出上限时的力量节奏（每N轮获得）。
	internal const int ArchitectOverCapCadence       = 3;

	// =========================================================================
	// Section 7: ARCHITECT PHASE BEHAVIOR
	// =========================================================================

	/// EN: Number of hits in Phase 2 retaliation multi-attack. | ZH: 二阶段反击多段攻击的打击次数。
	internal const int ArchitectP2RetaliationHits    = 2;
	/// EN: Strength stacks the Architect enters Phase 2 with.  | ZH: 进入二阶段时的初始力量层数。
	internal const int ArchitectP2OpeningStrength    = 4;
	/// EN: Block Piercer stacks applied at Phase 3 start.      | ZH: 三阶段开始时的穿透格挡层数。
	internal const int ArchitectP3BlockPiercerStacks = 5;

	/// EN: Phase 4 revival minimum HP floor as a fraction of max HP (0.35 = 35%). | ZH: 四阶段复活后的最低HP比例（0.35 = 35%）。
	internal const decimal ArchitectP4ReviveHpFloor       = 0.35m;
	/// EN: Phase 4 opening Oblivion damage scale factor.                            | ZH: 四阶段开场遗忘伤害缩放系数。
	internal const decimal ArchitectP4OblivionOpeningScale = 0.5m;

	// -- Phase 4 Oblivion round thresholds ------------------------------------
	/// EN: Rounds ≤ this → low Oblivion stack count.  | ZH: 轮数 ≤ 此值 → 低层数遗忘。
	internal const int ArchitectP4OblivionLowThreshold = 15;
	/// EN: Rounds ≤ this → mid Oblivion stack count.  | ZH: 轮数 ≤ 此值 → 中层数遗忘。
	internal const int ArchitectP4OblivionMidThreshold = 25;

	// -- Phase 4 Oblivion stacks (Brutal mode) --------------------------------
	internal const int ArchitectP4BrutalOblivionStacksLow     = 6;
	internal const int ArchitectP4BrutalOblivionStacksMid     = 7;
	internal const int ArchitectP4BrutalOblivionStacksDefault = 8;

	// -- Phase 4 Oblivion stacks (Normal mode) --------------------------------
	internal const int ArchitectP4NormalOblivionStacksLow     = 7;
	internal const int ArchitectP4NormalOblivionStacksMid     = 8;
	internal const int ArchitectP4NormalOblivionStacksDefault = 8;

	// =========================================================================
	// Section 8: ARCHITECT SHADOW CHAMPION SUMMON
	// EN: The Shadow Champion is a mid-combat summon. At 2+ players two shadows appear,
	//     so each shadow's HP scales more slowly than standard multiplayer to stay balanced.
	//     2p HP = soloHp × ShadowMp2pBase; 3p+ HP = soloHp × (ShadowMp2pBase + (players−2) × ShadowMpStepPerPlayer).
	// ZH: 暗影冠军是战斗中召唤的小怪。2人以上时召唤2个暗影，
	//     每个暗影HP增长比标准联机缩放更缓慢。
	//     2人HP = 单人HP × ShadowMp2pBase；3人以上HP = 单人HP × (ShadowMp2pBase + (人数−2) × ShadowMpStepPerPlayer)。
	// =========================================================================

	/// EN: Shadow Champion target HP (solo).                      | ZH: 单人暗影冠军目标HP。
	internal const int     ArchitectShadowHp              = 485;
	/// EN: Phase 4 Linked Shadow HP per character type (solo, 5 summoned simultaneously). | ZH: 四阶段连结之影各角色单人HP（同时5只）。
	internal const int     LinkedShadowIroncladHp         = 133;
	internal const int     LinkedShadowSilentHp           = 111;
	internal const int     LinkedShadowDefectHp           = 124;
	internal const int     LinkedShadowNecrobinderHp      = 120;
	internal const int     LinkedShadowRegentHp           = 128;
	/// EN: Shadow HP multiplier at 2 players (base).              | ZH: 2人联机暗影HP倍率（基础值）。
	internal const decimal ShadowMp2pBase            = 0.8m;
	/// EN: Additional shadow HP multiplier per player above 2.    | ZH: 超过2人时每位玩家增加的暗影HP倍率。
	internal const decimal ShadowMpStepPerPlayer     = 0.6m;

	// =========================================================================
	// Section 9: PHASE 4 LINKED SHADOW DAMAGE
	// EN: Damage and hit counts for the 5 linked shadows summoned at Phase 4 start.
	//     All base values are multiplied by LinkedShadowDamageMultiplier (0.5×) on summon.
	//     Formula: effectiveDmg = ceil(BaseDmg × DamageMult) + CoOpBonus (if ≥ 3 players)
	//     Silent: 4-hit assassin. Per-hit ≈ half of 2-hit warriors; total multi = equal (4×1 = 2×2).
	//     Ironclad/Regent: 2-hit warriors. Defect/Necrobinder: 3-hit mid-range.
	// ZH: 四阶段开场召唤的5个连结之影的伤害和连击次数。
	//     所有基础值在召唤时乘以LinkedShadowDamageMultiplier（0.5×）。
	//     公式：实际伤害 = 上取整(基础×倍率) + 联机加成（≥3人時）
	// =========================================================================

	/// EN: Global damage multiplier applied to all Phase 4 linked shadows on summon (65%). | ZH: 召唤时应用于所有四阶段连结之影的全局伤害倍率（65%）。
	internal const decimal LinkedShadowDamageMultiplier = 0.65m;
	/// EN: Extra multi-hit damage per hit at 3+ players (no scaling exists otherwise). | ZH: 3人以上联机时多段攻击每段额外伤害（原本无玩家缩放）。
	internal const int LinkedShadowCoOpMultiBonus3p  = 1;
	/// EN: Extra heavy-attack damage at 3+ players.                                    | ZH: 3人以上联机时单体攻击额外伤害。
	internal const int LinkedShadowCoOpHeavyBonus3p  = 2;

	// -- Ironclad: 2 hits (warrior, fewer, harder hits; per-hit = 2× Silent's per-hit) ------
	internal const int LinkedShadowIroncladMultiHits  = 2;
	internal const int LinkedShadowIroncladBaseMulti  = 4;   // eff: ceil(4×0.5)=2/hit, total=4
	internal const int LinkedShadowIroncladBaseHeavy  = 12;  // eff: ceil(12×0.5)=6

	// -- Silent: 4 hits (assassin, rapid jabs; per-hit = half of 2-hit, same total multi) ---
	internal const int LinkedShadowSilentMultiHits    = 4;
	internal const int LinkedShadowSilentBaseMulti    = 3;   // eff: ceil(3×0.5)=2/hit, total=4
	internal const int LinkedShadowSilentBaseHeavy    = 8;   // eff: ceil(8×0.5)=4

	// -- Defect: 3 hits (tech spray, mid-range cadence) -----------------------------------
	internal const int LinkedShadowDefectMultiHits    = 3;
	internal const int LinkedShadowDefectBaseMulti    = 4;   // eff: ceil(4×0.5)=2/hit, total=6
	internal const int LinkedShadowDefectBaseHeavy    = 10;  // eff: ceil(10×0.5)=5

	// -- Regent: 2 hits (commanding, focused blows) -----------------------------------------
	internal const int LinkedShadowRegentMultiHits    = 2;
	internal const int LinkedShadowRegentBaseMulti    = 4;   // eff: ceil(4×0.5)=2/hit, total=4
	internal const int LinkedShadowRegentBaseHeavy    = 12;  // eff: ceil(12×0.5)=6

	// -- Necrobinder: 3 hits (curse spread) ------------------------------------------------
	internal const int LinkedShadowNecrobinderMultiHits = 3;
	internal const int LinkedShadowNecrobinderBaseMulti = 4;   // eff: ceil(4×0.5)=2/hit, total=6
	internal const int LinkedShadowNecrobinderBaseHeavy = 10;  // eff: ceil(10×0.5)=5
}
