//=============================================================================
// Act4ArchitectBossEncounter.cs | Act4Placeholder - Slay the Spire 2 Mod
// EN: Defines the Architect boss encounter entry: sets it as a boss-room type with a custom map icon and BGM, and registers Act4ArchitectBoss as the sole monster.
// ZH: 定义建筑师Boss遭遇配置：设为Boss房类型，指定自定义地图图标和背景音乐，并注册Act4ArchitectBoss为唯一敌人。
//=============================================================================
using System;
using System.Collections.Generic;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Rooms;

namespace Act4Placeholder;

public sealed class Act4ArchitectBossEncounter : EncounterModel
{
	public override RoomType RoomType => (RoomType)3;

	public override string BossNodePath => "res://images/map/placeholder/act4_architect_icon";

	public override string CustomBgm => "event:/music/act3_boss_test_subject";

	public override IEnumerable<MonsterModel> AllPossibleMonsters => new MonsterModel[]
	{
		ModelDb.Monster<Act4ArchitectBoss>(),
		// Phase 3 shadows (registered so ModelDb initialises their visuals + name)
		ModelDb.Monster<ShadowIronclad>(),
		ModelDb.Monster<ShadowSilent>(),
		ModelDb.Monster<ShadowDefect>(),
		ModelDb.Monster<ShadowRegent>(),
		ModelDb.Monster<ShadowNecrobinder>(),
		// Phase 4 linked shadows (must be listed so name/animations/targeting work)
		ModelDb.Monster<LinkedShadowIronclad>(),
		ModelDb.Monster<LinkedShadowSilent>(),
		ModelDb.Monster<LinkedShadowDefect>(),
		ModelDb.Monster<LinkedShadowRegent>(),
		ModelDb.Monster<LinkedShadowNecrobinder>(),
	};

	protected override IReadOnlyList<ValueTuple<MonsterModel, string?>> GenerateMonsters()
	{
		return new ValueTuple<MonsterModel, string>[1]
		{
			new ValueTuple<MonsterModel, string>(((MonsterModel)ModelDb.Monster<Act4ArchitectBoss>()).ToMutable(), (string)null)
		};
	}
}
