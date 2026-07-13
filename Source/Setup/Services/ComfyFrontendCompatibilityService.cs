namespace ComfyUI_Nexus.Setup.Services;

using System.IO;

internal static class ComfyFrontendCompatibilityService
{
	private const string BridgeTag = "[Bridge]";

	internal static int DeleteLegacyHudBackups(string? customNodesPath, Action<string> log)
	{
		if (string.IsNullOrWhiteSpace(customNodesPath) || !Directory.Exists(customNodesPath)) return 0;

		string disabledPath = Path.Combine(customNodesPath, ".disabled");
		int deleted = 0;

		foreach (string backupPath in EnumerateLegacyHudBackups(customNodesPath, disabledPath))
		{
			try
			{
				Directory.Delete(backupPath, recursive: true);
				deleted++;
				log($"{BridgeTag} Removed legacy HUD backup: {backupPath}");
			}
			catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
			{
				log($"{BridgeTag} Legacy HUD backup cleanup skipped: {ex.Message}");
			}
		}

		return deleted;
	}

	internal static int PatchHudDuplicateExtensionGuard(string? hudPath, CancellationToken cancellationToken)
	{
		if (string.IsNullOrWhiteSpace(hudPath)) return 0;

		string hudJsPath = Path.Combine(hudPath, "js");
		if (!Directory.Exists(hudJsPath)) return 0;

		string safeRegisterPath = Path.Combine(hudJsPath, "safe_register.js");
		string safeRegisterSource = """
			import { app } from "/scripts/app.js";

			function findExistingExtension(name) {
			    if (!name || !Array.isArray(app.extensions)) return null;
			    return app.extensions.find((extension) => extension?.name === name) ?? null;
			}

			export function safeRegisterExtension(extension) {
			    const name = extension?.name;
			    const existing = findExistingExtension(name);
			    if (existing && typeof existing === "object") {
			        for (const key of Object.keys(existing)) {
			            if (!(key in extension)) {
			                delete existing[key];
			            }
			        }

			        Object.assign(existing, extension);
			        console.warn(`[ComfyUI_HUD] Replaced extension before registration after reload: ${name}`);
			        return true;
			    }

			    try {
			        app.registerExtension(extension);
			        return true;
			    } catch (error) {
			        if (name && String(error?.message || error).includes(`Extension named '${name}' already registered`)) {
			            const registered = findExistingExtension(name);
			            if (registered && typeof registered === "object") {
			                Object.assign(registered, extension);
			                console.warn(`[ComfyUI_HUD] Replaced extension after duplicate registration error: ${name}`);
			                return true;
			            }
			        }

			        throw error;
			    }
			}
			""";

		int changed = 0;
		if (!File.Exists(safeRegisterPath) || File.ReadAllText(safeRegisterPath) != safeRegisterSource)
		{
			File.WriteAllText(safeRegisterPath, safeRegisterSource);
			changed++;
		}

		foreach (string filePath in Directory.GetFiles(hudJsPath, "*.js", SearchOption.TopDirectoryOnly))
		{
			cancellationToken.ThrowIfCancellationRequested();
			if (string.Equals(Path.GetFileName(filePath), "safe_register.js", StringComparison.OrdinalIgnoreCase))
			{
				continue;
			}

			string source = File.ReadAllText(filePath);
			if (!source.Contains("app.registerExtension({", StringComparison.Ordinal))
			{
				continue;
			}

			string updated = source;
			if (!updated.Contains("./safe_register.js", StringComparison.Ordinal))
			{
				updated = InsertSafeRegisterImport(updated);
			}

			updated = updated.Replace("app.registerExtension({", "safeRegisterExtension({", StringComparison.Ordinal);
			if (!string.Equals(source, updated, StringComparison.Ordinal))
			{
				File.WriteAllText(filePath, updated);
				changed++;
			}
		}

		return changed;
	}

	internal static void CleanupLegacyHudBridgeOverlay(string? hudPath, Action<string> log)
	{
		if (string.IsNullOrWhiteSpace(hudPath)) return;

		string hudJsPath = Path.Combine(hudPath, "js");
		string legacyEntryPath = Path.Combine(hudJsPath, "nexus_bridge.js");
		string legacyModulePath = Path.Combine(hudJsPath, "nexus");

		try
		{
			if (File.Exists(legacyEntryPath))
			{
				File.Delete(legacyEntryPath);
				log($"{BridgeTag} Removed legacy HUD bridge entry: {legacyEntryPath}");
			}

			if (Directory.Exists(legacyModulePath))
			{
				Directory.Delete(legacyModulePath, recursive: true);
				log($"{BridgeTag} Removed legacy HUD bridge module folder: {legacyModulePath}");
			}
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
		{
			log($"{BridgeTag} Legacy HUD bridge cleanup skipped: {ex.Message}");
		}
	}

	private static IEnumerable<string> EnumerateLegacyHudBackups(string customNodesPath, string disabledPath)
	{
		foreach (string backupPath in Directory.GetDirectories(customNodesPath, "ComfyUI-HUD.broken-*", SearchOption.TopDirectoryOnly))
		{
			yield return backupPath;
		}

		if (!Directory.Exists(disabledPath))
		{
			yield break;
		}

		foreach (string backupPath in Directory.GetDirectories(disabledPath, "ComfyUI-HUD.broken-*", SearchOption.TopDirectoryOnly))
		{
			yield return backupPath;
		}
	}

	private static string InsertSafeRegisterImport(string source)
	{
		const string importLine = "import { safeRegisterExtension } from \"./safe_register.js\";\n";

		int insertAt = 0;
		int cursor = 0;
		while (cursor < source.Length)
		{
			int lineEnd = source.IndexOf('\n', cursor);
			int nextCursor = lineEnd >= 0 ? lineEnd + 1 : source.Length;
			string line = source[cursor..nextCursor];
			if (!line.StartsWith("import ", StringComparison.Ordinal))
			{
				break;
			}

			insertAt = nextCursor;
			cursor = nextCursor;
		}

		return source.Insert(insertAt, importLine);
	}
}
