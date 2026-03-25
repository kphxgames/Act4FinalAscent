//=============================================================================
// ShortAct4Map.cs | Act4Placeholder - Slay the Spire 2 Mod
// EN: Defines the procedurally-generated 9-row Act 4 map layout: Empyreal Cache event, monster room, 3-way branch, elite, Royal Treasury event, shop, rest site, Grand Library event, and the Architect boss point.
// ZH: 定义第四幕程序生成的9行地图布局：帝国宝库、怪物房、三叉分支、精英房、皇家金库、商店、休息点、秘典馆事件，最终为建筑师Boss点。
//=============================================================================
using System.Collections.Generic;
using System;
using MegaCrit.Sts2.Core.Entities.Ascension;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Random;
using MegaCrit.Sts2.Core.Runs;

namespace Act4Placeholder;

public sealed class ShortAct4Map : ActMap
{
	private static readonly MapPointType[] FlexibleRoomTypes = new MapPointType[4]
	{
		MapPointType.Treasure,
		MapPointType.Unknown,
		MapPointType.RestSite,
		MapPointType.Shop
	};

		public override MapPoint BossMapPoint { get; }

		public override MapPoint StartingMapPoint { get; }

		protected override MapPoint?[,] Grid { get; }

	public ShortAct4Map(RunState? runState = null)
	{
		// Grid rows 0-8 (GetRowCount()=9). BossMapPoint is at row 9, one beyond the grid,
		// so that the vanilla RecalculateTravelability "last row" check fires at row 8 (Grand Library)
		// and enables the boss point via the direct _bossPointNode path.
		//
		// IMPORTANT: no gaps between consecutive rows.  If any row is skipped, the Flight
		// modifier (which uses GetPointsInRow(currentRow+1) instead of Children) returns an
		// empty set, leaving the next room un-highlighted and permanently unreachable.
		//
		// Row layout:
		//   0  StartingMapPoint (entry, not in Grid)
		//   1  Unknown  → Act4EmpyrealCache event
		//   2  Monster
		//   3  Branch ×3 (one of each: Treasure, Unknown, RestSite, Shop minus one)
		//   4  Elite
		//   5  Unknown  → Act4RoyalTreasury event
		//   6  Shop
		//   7  RestSite
		//   8  Unknown  → Act4GrandLibraryEvent   (= GetRowCount()-1 → boss auto-enabled)
		//   9  BossMapPoint (outside Grid)
		Grid = new MapPoint[7, 9];
		BossMapPoint = new MapPoint(3, 9)
		{
			PointType = (MapPointType)7,
			CanBeModified = false
		};
		StartingMapPoint = new MapPoint(3, 0)
		{
			PointType = (MapPointType)8,
			CanBeModified = false
		};
		List<MapPointType> row3Branch = RollWeightedDistinct(runState, "row3_branch", 3);
		MapPoint val = CreatePoint(3, 1, MapPointType.Unknown);
		MapPoint val2 = CreatePoint(3, 2, MapPointType.Monster);
		MapPoint val3 = CreatePoint(1, 3, row3Branch[0]);
		MapPoint val4 = CreatePoint(3, 3, row3Branch[1]);
		MapPoint val5 = CreatePoint(5, 3, row3Branch[2]);
		MapPoint val6 = CreatePoint(3, 4, MapPointType.Elite);         // row 4
		MapPoint val7 = CreatePoint(3, 5, MapPointType.Unknown);       // row 5
		MapPoint valShop = CreatePoint(3, 6, MapPointType.Shop);       // row 6
		MapPoint val8 = CreatePoint(3, 7, MapPointType.RestSite);      // row 7
		MapPoint valLibrary = CreatePoint(3, 8, MapPointType.Unknown); // row 8 → Grand Library
		base.startMapPoints.Add(val);
		StartingMapPoint.AddChildPoint(val);
		val.AddChildPoint(val2);
		val2.AddChildPoint(val3);
		val2.AddChildPoint(val4);
		val2.AddChildPoint(val5);
		val3.AddChildPoint(val6);
		val4.AddChildPoint(val6);
		val5.AddChildPoint(val6);
		val6.AddChildPoint(val7);
		val7.AddChildPoint(valShop);
		valShop.AddChildPoint(val8);
		val8.AddChildPoint(valLibrary);
		valLibrary.AddChildPoint(BossMapPoint);
	}

	private MapPoint CreatePoint(int col, int row, MapPointType type)
	{
		MapPoint val = new MapPoint(col, row)
		{
			PointType = type
		};
		Grid[col, row] = val;
		return val;
	}

	private static List<MapPointType> RollWeightedDistinct(RunState? runState, string streamKey, int count)
	{
		// EN: Row 3 should feel random, but co-op clients must still land on the same branch trio.
		//     The seed mixes run seed, ascension, player count, and a tiny stream key so we get
		//     deterministic flavor without needing extra network sync just for map generation.
		// ZH: 第3行要看起来像随机分支，但联机客户端又必须摇出同一组结果。
		//     这里把 run seed、升华、玩家数和 stream key 混进种子里，不用额外网络同步也能一致。
		uint runSeed = runState?.Rng?.Seed ?? 0u;
		int ascension = runState?.AscensionLevel ?? (AscensionHelper.HasAscension((AscensionLevel)1) ? 1 : 0);
		int playerCount = ((runState != null) ? ((IReadOnlyCollection<Player>)runState.Players).Count : 1);
		Rng rng = new Rng(StableHash32($"{runSeed}:{ascension}:{playerCount}:{streamKey}"));
		List<MapPointType> pool = new List<MapPointType>(FlexibleRoomTypes);
		List<MapPointType> result = new List<MapPointType>(count);
		count = Math.Min(count, pool.Count);
		for (int i = 0; i < count; i++)
		{
			int totalWeight = 0;
			for (int j = 0; j < pool.Count; j++)
			{
				totalWeight += GetTypeWeight(pool[j]);
			}
			int roll = rng.NextInt(totalWeight);
			int cumulative = 0;
			int pickedIndex = 0;
			for (int k = 0; k < pool.Count; k++)
			{
				cumulative += GetTypeWeight(pool[k]);
				if (roll < cumulative)
				{
					pickedIndex = k;
					break;
				}
			}
			result.Add(pool[pickedIndex]);
			pool.RemoveAt(pickedIndex);
		}
		return result;
	}

	private static int GetTypeWeight(MapPointType type)
	{
		return 24;
	}

	private static uint StableHash32(string value)
	{
		// EN: Tiny stable hash for ad-hoc RNG streams. Nothing fancy, just deterministic.
		// ZH: 给临时 RNG stream 用的小稳定哈希，不高级，只求可复现。
		uint num = 2166136261u;
		foreach (char c in value)
		{
			num ^= c;
			num *= 16777619;
		}
		return (num == 0) ? 1u : num;
	}
}
