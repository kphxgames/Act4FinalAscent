//=============================================================================
// Act4PlaceholderMapTemplate.cs | Act4Placeholder - Slay the Spire 2 Mod
// EN: ActModel defining the Act 4 map template: delegates visuals and audio to the Glory act, restricts the boss slot to the Architect encounter, and sets the base room count to 9 (matching ShortAct4Map rows 1-8 plus the starting node).
// ZH: 第四幕地图模板的ActModel：将视觉和音频委托给荣耀幕，Boss位限制为建筑师遭遇，基础房间数为9（与ShortAct4Map的8行加起始节点对应）。
//=============================================================================
using System.Collections.Generic;
using System.Reflection;
using Godot;
using MegaCrit.Sts2.Core.Entities.Ascension;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Acts;
using MegaCrit.Sts2.Core.Random;
using MegaCrit.Sts2.Core.Unlocks;

namespace Act4Placeholder;

public sealed class Act4PlaceholderMapTemplate : ActModel
{
	private static Glory GloryAct => ModelDb.Act<Glory>();

	public override string ChestOpenSfx => ((ActModel)GloryAct).ChestOpenSfx;

	public override IEnumerable<EncounterModel> BossDiscoveryOrder => new Act4ArchitectBossEncounter[1] { ModelDb.Encounter<Act4ArchitectBossEncounter>() };

	public override IEnumerable<AncientEventModel> AllAncients => ((ActModel)GloryAct).AllAncients;

	public override IEnumerable<EventModel> AllEvents => ((ActModel)GloryAct).AllEvents;

	protected override int NumberOfWeakEncounters => 1;

	protected override int BaseNumberOfRooms => 9;

	public override string[] BgMusicOptions => ((ActModel)GloryAct).BgMusicOptions;

	public override string[] MusicBankPaths => ((ActModel)GloryAct).MusicBankPaths;

	public override string AmbientSfx => ((ActModel)GloryAct).AmbientSfx;

	public override string ChestSpineSkinNameNormal => ((ActModel)GloryAct).ChestSpineSkinNameNormal;

	public override string ChestSpineSkinNameStroke => ((ActModel)GloryAct).ChestSpineSkinNameStroke;

	public override Color MapTraveledColor => ((ActModel)GloryAct).MapTraveledColor;

	public override Color MapUntraveledColor => ((ActModel)GloryAct).MapUntraveledColor;

	public override Color MapBgColor => ((ActModel)GloryAct).MapBgColor;

	public override IEnumerable<EncounterModel> GenerateAllEncounters()
	{
		return ((ActModel)GloryAct).AllEncounters;
	}

	public override IEnumerable<AncientEventModel> GetUnlockedAncients(UnlockState unlockState)
	{
		return ((ActModel)GloryAct).GetUnlockedAncients(unlockState);
	}

	protected override void ApplyActDiscoveryOrderModifications(UnlockState unlockState)
	{
	}

	public override MapPointTypeCounts GetMapPointTypes(Rng mapRng)
	{
		int elites = AscensionHelper.HasAscension((AscensionLevel)1) ? 3 : 2;
		int shops = 2;
		int unknowns = 3;
		int rests = AscensionHelper.HasAscension((AscensionLevel)6) ? 1 : 2;
		// Stable branch constructor takes Rng; beta branch changed it to ActMap.
		// Use reflection to find whichever constructor is present at runtime.
		MapPointTypeCounts counts = CreateMapPointTypeCounts(mapRng);
		// Beta branch made some properties read-only; use reflection as a fallback.
		TrySetProperty(counts, nameof(MapPointTypeCounts.NumOfElites), elites);
		TrySetProperty(counts, nameof(MapPointTypeCounts.NumOfShops), shops);
		TrySetProperty(counts, nameof(MapPointTypeCounts.NumOfUnknowns), unknowns);
		TrySetProperty(counts, nameof(MapPointTypeCounts.NumOfRests), rests);
		return counts;
	}

	private static MapPointTypeCounts CreateMapPointTypeCounts(Rng mapRng)
	{
		// Try Rng constructor (stable), then single-arg of any type (beta), then parameterless.
		foreach (ConstructorInfo ctor in typeof(MapPointTypeCounts).GetConstructors())
		{
			ParameterInfo[] parameters = ctor.GetParameters();
			if (parameters.Length == 1 && parameters[0].ParameterType == typeof(Rng))
				return (MapPointTypeCounts)ctor.Invoke(new object[] { mapRng });
		}
		// Beta: constructor takes ActMap or another type — pass mapRng anyway via Activator
		// which will find a compatible overload, or fall back to first single-arg ctor.
		foreach (ConstructorInfo ctor in typeof(MapPointTypeCounts).GetConstructors())
		{
			ParameterInfo[] parameters = ctor.GetParameters();
			if (parameters.Length == 1)
				return (MapPointTypeCounts)ctor.Invoke(new object[] { System.Activator.CreateInstance(parameters[0].ParameterType, mapRng) });
		}
		return (MapPointTypeCounts)System.Activator.CreateInstance(typeof(MapPointTypeCounts))!;
	}

	private static void TrySetProperty(MapPointTypeCounts counts, string propertyName, int value)
	{
		PropertyInfo? prop = typeof(MapPointTypeCounts).GetProperty(propertyName);
		if (prop == null) return;
		MethodInfo? setter = prop.GetSetMethod() ?? prop.GetSetMethod(true);
		if (setter != null)
		{
			setter.Invoke(counts, new object[] { value });
			return;
		}
		// init-only or truly read-only: write the backing field directly.
		FieldInfo? field = typeof(MapPointTypeCounts).GetField($"<{propertyName}>k__BackingField",
			BindingFlags.Instance | BindingFlags.NonPublic);
		field?.SetValue(counts, value);
	}
}
