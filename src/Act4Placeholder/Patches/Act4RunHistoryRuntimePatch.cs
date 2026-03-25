//=============================================================================
// Act4RunHistoryRuntimePatch.cs | Act4Placeholder - Slay the Spire 2 Mod
// EN: Patches NRun.ShowGameOverScreen to correct RunTime and WinTime on Act 4 runs by recalculating elapsed time from the run's start timestamp when stored values are stale.
// ZH: 补丁修改NRun.ShowGameOverScreen，从跑图开始时间戳重新计算已用时间，修正第四幕跑图结束界面中RunTime和WinTime的过期值。
//=============================================================================
using System;
using HarmonyLib;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Saves;

namespace Act4Placeholder;

[HarmonyPatch(typeof(NRun), nameof(NRun.ShowGameOverScreen))]
internal static class Act4RunHistoryRuntimePatch
{
	private static void Prefix(SerializableRun serializableRun)
	{
		if (serializableRun == null || serializableRun.Acts == null || serializableRun.Acts.Count <= 3 || serializableRun.StartTime <= 0)
		{
			return;
		}
		long unixTimeSeconds = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
		long num = Math.Max(0L, unixTimeSeconds - serializableRun.StartTime);
		if (num <= 0)
		{
			return;
		}
		if (num > serializableRun.RunTime)
		{
			serializableRun.RunTime = num;
		}
		if (serializableRun.WinTime > 0 && num > serializableRun.WinTime)
		{
			serializableRun.WinTime = num;
		}
	}
}
