//=============================================================================
// NBossMapPointReadyPatch.cs | Act4Placeholder - Slay the Spire 2 Mod
// EN: Patches NBossMapPoint._Ready to offset the Architect boss icon's sprite container position when the custom Act 4 architect icon texture is detected on the map.
// ZH: 补丁修改NBossMapPoint._Ready，当检测到地图上使用自定义建筑师图标时，微调Boss地图节点的精灵容器位置。
//=============================================================================
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;

namespace Act4Placeholder;

[HarmonyPatch(typeof(NBossMapPoint), "_Ready")]
internal static class NBossMapPointReadyPatch
{
	private const string ArchitectBossEncounterId = "ACT4_ARCHITECT_BOSS_ENCOUNTER";

	private static readonly AccessTools.FieldRef<NBossMapPoint, IRunState> RunStateField = AccessTools.FieldRefAccess<NBossMapPoint, IRunState>("_runState");

	private static void Postfix(NBossMapPoint __instance)
	{
		TextureRect placeholderImage = __instance.GetNodeOrNull<TextureRect>("%PlaceholderImage");
		TextureRect placeholderOutline = __instance.GetNodeOrNull<TextureRect>("%PlaceholderOutline");
		Node2D spriteContainer = __instance.GetNodeOrNull<Node2D>("%SpriteContainer");
		if (placeholderImage == null || placeholderOutline == null || spriteContainer == null)
		{
			return;
		}
		IRunState runState = RunStateField(__instance);
		if (runState == null || runState.CurrentActIndex != 3 || runState.Act?.BossEncounter?.Id.Entry != ArchitectBossEncounterId || __instance.Point != runState.Map.BossMapPoint)
		{
			return;
		}
		if (placeholderImage.Texture != null && placeholderImage.Texture.ResourcePath.Contains("act4_architect_icon.png"))
		{
			spriteContainer.Position += new Vector2(8f, 10f);
		}
	}
}
