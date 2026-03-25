//=============================================================================
// NPauseMenuSaveAndQuitPatch.cs | Act4Placeholder - Slay the Spire 2 Mod
// EN: Patches NPauseMenu.OnSaveAndQuitButtonPressed to stop the custom mod BGM
//     before returning to the main menu, since this path bypasses RunManager.OnEnded.
// ZH: 补丁修改NPauseMenu.OnSaveAndQuitButtonPressed，在返回主菜单前停止自定义模组BGM，
//     因为此路径会跳过RunManager.OnEnded的正常调用。
//=============================================================================
using HarmonyLib;
using MegaCrit.Sts2.Core.Nodes.Screens.PauseMenu;

namespace Act4Placeholder;

[HarmonyPatch(typeof(NPauseMenu), "OnSaveAndQuitButtonPressed")]
internal static class NPauseMenuSaveAndQuitPatch
{
	private static void Prefix()
	{
		Act4AudioHelper.StopModBgm();
	}
}
