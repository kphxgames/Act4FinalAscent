//=============================================================================
// ModLoc.cs | Act4Placeholder - Slay the Spire 2 Mod
// EN: Lightweight runtime localization helper that returns the correct translation for the current game language, falling back to English for unsupported locales.
// ZH: 轻量级运行时本地化工具，根据当前游戏语言返回对应翻译，未支持的语言回退至英语。
//=============================================================================
using MegaCrit.Sts2.Core.Localization;

namespace Act4Placeholder;

/// <summary>
/// Lightweight runtime localization helper for hardcoded C# strings in the mod.
/// Falls back to <c>eng</c> for any language without an explicit translation.
/// <para>Game language codes: eng, zhs/zht (Chinese), fra (French), deu (German),
/// jpn (Japanese), kor (Korean), por/ptb (Portuguese/Brazilian),
/// rus (Russian), spa/esp (Spanish/Latin American).</para>
/// </summary>
internal static class ModLoc
{
	private static string Language => LocManager.Instance?.Language ?? "eng";

	/// <summary>True when the current language is Simplified or Traditional Chinese.</summary>
	public static bool IsZhs => Language is "zhs" or "zht";

	/// <summary>
	/// Returns the translation matching the current game language.
	/// Use named arguments for each language; any omitted language falls back to <paramref name="eng"/>.
	/// </summary>
	public static string T(
		string  eng,
		string? zhs = null,
		string? fra = null,
		string? deu = null,
		string? jpn = null,
		string? kor = null,
		string? por = null,
		string? rus = null,
		string? spa = null)
	{
		return Language switch
		{
			"zhs" or "zht" => zhs ?? eng,
			"fra"          => fra ?? eng,
			"deu"          => deu ?? eng,
			"jpn"          => jpn ?? eng,
			"kor"          => kor ?? eng,
			"por" or "ptb" => por ?? eng,
			"rus"          => rus ?? eng,
			"spa" or "esp" => spa ?? eng,
			_              => eng,
		};
	}
}
