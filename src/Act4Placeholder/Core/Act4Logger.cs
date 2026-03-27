//=============================================================================
// Act4Logger.cs | Act4Placeholder - Slay the Spire 2 Mod
// EN: Mod-specific logger that filters out common engine noise and exposes named section/info/warn/error helpers used throughout the codebase.
// ZH: Mod专用日志工具，过滤引擎常见噪音警告，提供贯穿整个代码库使用的section/info/warn/error辅助方法。
//=============================================================================
using System;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Godot;

namespace Act4Placeholder;

/// <summary>
/// Writes a privacy-safe mod log to Act4Placeholder_Logs.txt in the STS2 logs directory.
///
/// Contains:
///   - Filtered STS2 startup lines (game version, FMOD, Steam init, atlas loads, mod load)
///   - All Act4Placeholder patch results and status
///   - Main-menu SaveSync / RunSlots patch status
///   - Real game/engine ERRORs and WARNINGs (minus known-harmless noise)
///
/// Deliberately excludes from godot.log:
///   - PATH environment variable
///   - Device model / machine name
///   - Executable and data directory paths (contain username)
///   - Steam profile ID
///   - Processor name, memory sizes, screen details
///   - Graphics adapter version strings
///   - Teardown RID leak reports ("leaked at exit", "never freed") - normal Godot shutdown
///   - "Asset not cached" warnings - very common, not actionable
///   - D3D12 PSO caching warning - always present on Windows, expected
///
/// Share THIS file (not godot.log) when reporting issues.
/// </summary>
internal static class Act4Logger
{
	private static string? _logPath;
	private static readonly object _lock = new();
	private static string? _godotLogPath;
	private static volatile int _godotLogOffsetHi; // high 32 bits of offset
	private static volatile int _godotLogOffsetLo; // low  32 bits of offset

	private static long GodotLogOffset
	{
		get => ((long)(uint)_godotLogOffsetHi << 32) | (uint)_godotLogOffsetLo;
		set { _godotLogOffsetHi = (int)(value >> 32); _godotLogOffsetLo = (int)(value & 0xFFFFFFFFL); }
	}

	// Lines matching ANY of these patterns are excluded even when they would otherwise pass
	// both the allow-list and PII filter.  Covers teardown / shutdown noise and high-volume
	// informational lines that are never actionable.
	private static readonly Regex _noisePattern = new(
		@"were leaked at exit" +
		@"|RIDs? of type .+ were leaked" +
		@"|shaders? of type .+ were never freed" +
		@"|RID allocations? of type .+ were leaked" +
		@"|Asset not cached:" +          // [WARN] Asset not cached: res://... - very common, not actionable
		@"|PSO caching is not implemented",  // D3D12 driver warning, always present on Windows
		RegexOptions.Compiled | RegexOptions.IgnoreCase);

	// Substrings that make a godot.log line eligible for inclusion.
	// Order doesn't matter – any single match admits the line (subject to the PII filter and noise filter below).
	private static readonly string[] _allowedSubstrings =
	{
		"MegaDot v",                           // engine version header
		"FMOD Sound System:",                  // audio init
		"[Sentry.NET] Initialized:",           // crash-reporting env (no user info)
		"Steamworks:",                         // Steam SDK init
		"Steam is running:",                   // Steam running bool
		"Steam is enabled,",                   // Steam save mode
		"Registered ",                         // "Registered N migrations"
		"Current save versions:",              // schema versions
		"Found mod pck file",                  // mod detected
		"Loading assembly DLL",                // DLL loaded
		"Calling initializer method",          // mod entry point called
		"[Act4Placeholder]",                   // all our Init() logs
		"Finished mod initialization",         // loader confirmation
		"--- RUNNING MODDED!",                 // modded flag
		"Loading locale",                      // locale load (res:// only)
		"Found loc table from mod:",           // our locale merges
		"ModelIdSerializationCache initialized", // model DB ready
		"AtlasManager: Loaded",                // atlas loads
		"Time to main menu",                   // startup timing
		"[Act4Placeholder.",                   // UnifiedSavePath / RunSlots patch logs
		// ── Errors & warnings ───────────────────────────────────────────
		// Real game/engine ERRORs (teardown noise is blocked by _noisePattern later)
		"ERROR:",
		"[ERROR]",
		// Real WARNING lines ("Asset not cached" and D3D12 PSO blocked by _noisePattern)
		"WARNING:",
		"[WARN]",
	};

	// Lines matching ANY of these patterns are excluded even if they pass the allow-list.
	// Covers personal/machine-identifying information.
	private static readonly Regex _piiPattern = new(
		@"[Uu]sers[/\\]" +
		@"|AppData[/\\]" +
		@"|user://steam/\d" +
		@"|PATH:" +
		@"|Device Model:" +
		@"|Executable Path:" +
		@"|Data Directory:" +
		@"|User Data Directory:" +
		@"|Processor Name:" +
		@"|Processor Count:" +
		@"|Command Line" +
		@"|Memory Info:" +
		@"|  physical:" +
		@"|  free:" +
		@"|  available:" +
		@"|[Ss]tatic [Mm]emory" +
		@"|Important Environment" +
		@"|Wrote \d+ bytes to path=user://" +
		@"|76561\d{12}" +           // Steam 64-bit ID pattern
		@"|=== Godot OS" +
		@"|Architecture:" +
		@"|Screen info \(" +
		@"|[Vv]ideo [Mm]emory" +
		@"|Is Debug Build:" +
		@"|Is Sandboxed:" +
		@"|Is Stdout Verbose:" +
		@"|Is Low Processor" +
		@"|Release Commit:" +
		@"|Graphics adapter" +
		@"|Timestamp:" +
		@"|OS Version:" +
		@"|Distribution Name:" +
		@"|Is UserFS Persistent:",
		RegexOptions.Compiled);

	// Replaces absolute filesystem paths with just the trailing file name.
	// Keeps res:// and user:// virtual paths intact.
	private static readonly Regex _absPathPattern = new(
		@"[A-Za-z]:[/\\][^""'\s\r\n]*[/\\]([^/\\""'\s\r\n]+)",
		RegexOptions.Compiled);

	// ─────────────────────────────────────────────────────────────────────────
	// Public API
	// ─────────────────────────────────────────────────────────────────────────

	/// <summary>
	/// Call once at the very start of ModEntry.Init().
	/// Writes the log file header and backfills safe lines from godot.log up to this point.
	/// </summary>
	public static void Initialize(string modVersion)
	{
		try
		{
			var logDir = ProjectSettings.GlobalizePath("user://logs");
			if (!Directory.Exists(logDir))
				Directory.CreateDirectory(logDir);

			_logPath = Path.Combine(logDir, "Act4Placeholder_Logs.txt");

			var sb = new StringBuilder();
			sb.AppendLine("=================================================================");
			sb.AppendLine($"  Act4Placeholder v{modVersion} - Mod Log");
			sb.AppendLine($"  Session: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC");
			sb.AppendLine("  Share THIS file (not godot.log) when reporting issues.");
			sb.AppendLine("  godot.log contains personal system info, this one does not.");
			sb.AppendLine("=================================================================");
			sb.AppendLine();

			// Backfill safe startup lines from the current godot.log.
			// By the time Init() runs, godot.log already has everything up to
			// "Calling initializer method of type Act4Placeholder.ModEntry".
			sb.AppendLine("=== STS2 Startup (filtered, no personal info) ===");
			var godotLog = Path.Combine(logDir, "godot.log");
			if (File.Exists(godotLog))
			{
				try
				{
					// FileShare.ReadWrite so we don't block Godot's own logger.
				using var fs = new FileStream(godotLog, FileMode.Open, System.IO.FileAccess.Read, FileShare.ReadWrite);
					using var reader = new StreamReader(fs, Encoding.UTF8);
					string? line;
					while ((line = reader.ReadLine()) != null)
					{
						if (IsSafe(line))
							sb.AppendLine(Sanitize(line));
					}
					// Record where we stopped so the background poller can pick up new lines later.
					GodotLogOffset = fs.Length;
					_godotLogPath = godotLog;
				}
				catch (IOException)
				{
					sb.AppendLine("(Could not read godot.log for backfill, file may be locked)");
				}
			}
			else
			{
				sb.AppendLine("(godot.log not found in expected location)");
			}

			sb.AppendLine();
			sb.AppendLine("=== Mod Init ===");

			File.WriteAllText(_logPath, sb.ToString(), Encoding.UTF8);

			// Start background poller to capture engine ERRORs/WARNINGs written to godot.log
			// during gameplay (e.g. mid-boss-fight crashes that wouldn't be in the backfill).
			_ = Task.Run(PollGodotLogLoopAsync);
		}
		catch (Exception ex)
		{
			// Never crash mod init because logging failed.
			GD.PrintErr($"[Act4Placeholder] Act4Logger.Initialize failed: {ex.Message}");
		}
	}

	public static void Info(string message) => Append($"[{Ts()}] [INFO] {message}");
	public static void Warn(string message) => Append($"[{Ts()}] [WARN] {message}");
	public static void Error(string message) => Append($"[{Ts()}] [ERROR] {message}");

	/// <summary>Writes a blank line followed by a section header.</summary>
	public static void Section(string title) => Append($"\n=== {title} ===");

	// ─────────────────────────────────────────────────────────────────────────
	// Internal helpers
	// ─────────────────────────────────────────────────────────────────────────

	private static void Append(string line)
	{
		if (_logPath == null) return;
		try
		{
			lock (_lock)
				File.AppendAllText(_logPath, line + System.Environment.NewLine, Encoding.UTF8);
		}
		catch { /* never crash on log write */ }
	}

	private static string Ts() => DateTime.Now.ToString("HH:mm:ss");

	private static bool IsSafe(string line)
	{
		if (string.IsNullOrWhiteSpace(line)) return false;
		if (_piiPattern.IsMatch(line)) return false;
		if (_noisePattern.IsMatch(line)) return false;

		foreach (var sub in _allowedSubstrings)
		{
			if (line.Contains(sub, StringComparison.OrdinalIgnoreCase))
				return true;
		}
		return false;
	}

	private static string Sanitize(string line)
	{
		// Replace absolute OS paths (C:\...\file) with just the filename.
		// Virtual paths like res:// and user:// are left intact.
		return _absPathPattern.Replace(line, m => m.Groups[1].Value);
	}

	/// <summary>
	/// Reads any new lines appended to godot.log since the last poll and appends
	/// safe/filtered ones to our mod log. Called every 3 seconds by the background poller.
	/// </summary>
	private static void FlushGodotLog()
	{
		if (_godotLogPath == null || _logPath == null) return;
		try
		{
			string output;
			long newOffset;
			using (var fs = new FileStream(_godotLogPath, FileMode.Open, System.IO.FileAccess.Read, FileShare.ReadWrite))
			{
				newOffset = fs.Length;
				long current = GodotLogOffset;
				if (newOffset <= current) return;
				fs.Seek(current, SeekOrigin.Begin);
				using var reader = new StreamReader(fs, Encoding.UTF8);
				var sb = new StringBuilder();
				string? line;
				while ((line = reader.ReadLine()) != null)
				{
					if (IsSafe(line))
						sb.AppendLine(Sanitize(line));
				}
				output = sb.ToString();
			}
			lock (_lock)
			{
				if (output.Length > 0)
					File.AppendAllText(_logPath, output, Encoding.UTF8);
				GodotLogOffset = newOffset;
			}
		}
		catch { /* never crash on log poll */ }
	}

	private static async Task PollGodotLogLoopAsync()
	{
		while (true)
		{
			await Task.Delay(6000);
			FlushGodotLog();
		}
	}
}
