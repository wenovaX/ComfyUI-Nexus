namespace ComfyUI_Nexus.Configuration;

using System.Diagnostics;
using System.Text;
using System.Text.Json;
using ComfyUI_Nexus.Diagnostics;
using ComfyUI_Nexus.Localization;

/// <summary>
/// Creates temporary short drive aliases for setup tooling and retains an
/// instance-owned lease while a Nexus process is actively using the mapping.
/// </summary>
internal sealed class NexusToolingPathLeaseController : IDisposable
{
	private static readonly char[] PreferredDriveLetters = ['N', 'R', 'S', 'T', 'U', 'V', 'W', 'X', 'Y', 'Z'];
	private readonly ToolingPathLeaseOwner _owner = ToolingPathLeaseOwner.CreateCurrent();
	private PathAlias? _primaryAlias;
	private bool _disposed;

	internal string AcquirePrimaryRoot(string physicalRoot)
	{
		ObjectDisposedException.ThrowIf(_disposed, this);
		string normalizedRoot = NormalizePath(physicalRoot);
		Directory.CreateDirectory(normalizedRoot);
		if (_primaryAlias is not null)
		{
			if (!string.Equals(_primaryAlias.PhysicalRoot, normalizedRoot, StringComparison.OrdinalIgnoreCase))
			{
				throw new InvalidOperationException("A tooling session can acquire only one primary path.");
			}

			return _primaryAlias.AliasRoot;
		}

#if WINDOWS
		using (NexusToolingPathLeaseRegistry.EnterOperation())
		{
			foreach ((string driveRoot, string targetRoot) in GetMappings())
			{
				if (!IsWithin(normalizedRoot, targetRoot))
				{
					continue;
				}

				bool nexusManaged = NexusToolingPathLeaseRegistry.IsNexusManagedUnsafe(driveRoot, targetRoot);
				if (nexusManaged)
				{
					NexusToolingPathLeaseRegistry.AddOwnerUnsafe(driveRoot, targetRoot, _owner);
				}

				var reused = new PathAlias(
					normalizedRoot,
					Translate(targetRoot, driveRoot, normalizedRoot),
					driveRoot,
					targetRoot,
					nexusManaged);
				_primaryAlias = reused;
				EnsureDriveLeaseNotice(normalizedRoot);
				NexusLog.Info(nexusManaged
					? $"[TOOLING_PATH] Joined Nexus tooling drive lease {driveRoot}."
					: $"[TOOLING_PATH] Reused an existing user tooling drive {driveRoot}.");
				return reused.AliasRoot;
			}

			foreach (char letter in PreferredDriveLetters)
			{
				string driveRoot = $"{letter}:\\";
				if (Directory.Exists(driveRoot))
				{
					continue;
				}

				RunSubst($"{letter}: {QuoteArgument(normalizedRoot)}");
				if (!Directory.Exists(driveRoot))
				{
					continue;
				}

				var created = new PathAlias(
					normalizedRoot,
					NormalizePath(driveRoot),
					driveRoot,
					normalizedRoot,
					NexusManaged: true);
				try
				{
					NexusToolingPathLeaseRegistry.AddOwnerUnsafe(created.DriveRoot, created.TargetRoot, _owner);
				}
				catch
				{
					RunSubst($"{created.DriveRoot[..2]} /d");
					throw;
				}

				_primaryAlias = created;
				EnsureDriveLeaseNotice(normalizedRoot);
				NexusLog.Info($"[TOOLING_PATH] Acquired Nexus tooling drive lease {driveRoot}.");
				return created.AliasRoot;
			}
		}

		throw new NexusToolingPathLeaseUnavailableException(
			"No temporary tooling drive is available. Close or unmap one of N:, R:, S:, T:, U:, V:, W:, X:, Y:, or Z:, then retry.");
#else
		return normalizedRoot;
#endif
	}

	internal string TranslatePath(string physicalPath)
	{
		string normalizedPath = NormalizePath(physicalPath);
		return _primaryAlias is not null && IsWithin(normalizedPath, _primaryAlias.PhysicalRoot)
			? _primaryAlias.Translate(normalizedPath)
			: normalizedPath;
	}

	/// <summary>
	/// Ends the current app-owned tooling session while keeping this controller
	/// available for the next serialized session.
	/// </summary>
	internal void ReleasePrimaryRoot()
	{
		ReleasePrimaryRootCore();
	}

	public void Dispose()
	{
		if (_disposed)
		{
			return;
		}

		_disposed = true;
		ReleasePrimaryRootCore();
	}

	private void ReleasePrimaryRootCore()
	{
#if WINDOWS
		if (_primaryAlias is { NexusManaged: true } alias)
		{
			try
			{
				using (NexusToolingPathLeaseRegistry.EnterOperation())
				{
					string? currentTarget = GetMappings()
						.Where(mapping => string.Equals(mapping.DriveRoot, alias.DriveRoot, StringComparison.OrdinalIgnoreCase))
						.Select(static mapping => mapping.TargetRoot)
						.FirstOrDefault();
					bool lastOwner = NexusToolingPathLeaseRegistry.RemoveOwnerUnsafe(alias.DriveRoot, alias.TargetRoot, _owner);
					if (!string.Equals(currentTarget, alias.TargetRoot, StringComparison.OrdinalIgnoreCase))
					{
						NexusToolingPathLeaseRegistry.RemoveMappingUnsafe(alias.DriveRoot, alias.TargetRoot);
					}
					else if (lastOwner)
					{
						RunSubst($"{alias.DriveRoot[..2]} /d");
						NexusToolingPathLeaseRegistry.RemoveMappingUnsafe(alias.DriveRoot, alias.TargetRoot);
						NexusLog.Info($"[TOOLING_PATH] Released Nexus tooling drive lease {alias.DriveRoot}.");
					}
				}
			}
			catch (Exception ex)
			{
				NexusLog.Warning($"[TOOLING_PATH] Failed to release tooling drive {alias.DriveRoot}: {ex.Message}");
			}
		}
#endif
		_primaryAlias = null;
	}

	internal static void ReleaseStaleOwnedMappings(string? managedRuntimeRoot = null)
	{
#if WINDOWS
		using (NexusToolingPathLeaseRegistry.EnterOperation())
		{
			Dictionary<string, string> mappings = GetMappings()
				.ToDictionary(static mapping => mapping.DriveRoot, static mapping => mapping.TargetRoot, StringComparer.OrdinalIgnoreCase);
			foreach (ToolingPathLeaseRecord record in NexusToolingPathLeaseRegistry.ReadUnsafe().ToArray())
			{
				if (!mappings.TryGetValue(record.Drive, out string? currentTarget) ||
					!string.Equals(currentTarget, record.Target, StringComparison.OrdinalIgnoreCase))
				{
					NexusToolingPathLeaseRegistry.RemoveMappingUnsafe(record.Drive, record.Target);
					continue;
				}

				List<ToolingPathLeaseOwner> liveOwners = record.Owners
					.Where(ToolingPathLeaseOwner.IsProcessAlive)
					.ToList();
				if (liveOwners.Count != record.Owners.Count)
				{
					NexusToolingPathLeaseRegistry.ReplaceOwnersUnsafe(record.Drive, record.Target, liveOwners);
				}

				if (liveOwners.Count > 0)
				{
					continue;
				}

				try
				{
					RunSubst($"{record.Drive[..2]} /d");
					NexusToolingPathLeaseRegistry.RemoveMappingUnsafe(record.Drive, record.Target);
					NexusLog.Info($"[TOOLING_PATH] Released stale Nexus tooling drive {record.Drive}.");
				}
				catch (Exception ex)
				{
					NexusLog.Warning($"[TOOLING_PATH] Stale tooling drive cleanup skipped for {record.Drive}: {ex.Message}");
				}
			}

			ReleaseLegacyOwnedMappings(GetMappings(), managedRuntimeRoot);
		}
#endif
	}

	internal static string ResolvePhysicalPath(string path)
	{
		string normalizedPath = NormalizePath(path);
#if WINDOWS
		using (NexusToolingPathLeaseRegistry.EnterOperation())
		{
			Dictionary<string, string> mappings = GetMappings()
				.ToDictionary(static mapping => mapping.DriveRoot, static mapping => mapping.TargetRoot, StringComparer.OrdinalIgnoreCase);
			foreach (ToolingPathLeaseRecord record in NexusToolingPathLeaseRegistry.ReadUnsafe())
			{
				if (mappings.TryGetValue(record.Drive, out string? targetRoot) && IsWithin(normalizedPath, record.Drive))
				{
					return Translate(record.Drive, targetRoot, normalizedPath);
				}
			}
		}
#endif
		return normalizedPath;
	}

#if WINDOWS
	private static void EnsureDriveLeaseNotice(string physicalRoot)
	{
		try
		{
			string readmePath = Path.Combine(physicalRoot, "WHY_THIS_DRIVE_EXISTS.txt");
			string unmountScriptPath = Path.Combine(physicalRoot, "UNMOUNT_THIS_NEXUS_DRIVE.ps1");
			string unmountCommandPath = Path.Combine(physicalRoot, "UNMOUNT_THIS_NEXUS_DRIVE.cmd");
			File.WriteAllText(readmePath, LocalizationManager.Text("tooling.path.drive_notice"), new UTF8Encoding(false));
			File.WriteAllText(unmountScriptPath, BuildDriveLeaseUnmountScript(physicalRoot), new UTF8Encoding(false));
			File.WriteAllText(unmountCommandPath, BuildDriveLeaseUnmountCommand(), new UTF8Encoding(false));
		}
		catch (Exception ex)
		{
			NexusLog.Warning($"[TOOLING_PATH] Could not write tooling drive notice: {ex.Message}");
		}
	}

	private static string BuildDriveLeaseUnmountScript(string physicalRoot)
	{
		string escapedRoot = NormalizePath(physicalRoot).Replace("'", "''", StringComparison.Ordinal);
		return """
# Removes only Nexus temporary tooling drive aliases for this runtime folder.
# Close Nexus for ComfyUI before running this script.

$expectedTarget = '__NEXUS_RUNTIME_ROOT__'
$normalizedExpectedTarget = $expectedTarget.Trim().TrimEnd('\', '/')
$temporaryMappings = @()

& subst.exe | ForEach-Object {
    if ($_ -match '^\s*([A-Za-z]):\\:\s*=>\s*(.+?)\s*$') {
        $drive = "$($Matches[1].ToUpperInvariant()):"
        $target = $Matches[2].Trim().TrimEnd('\', '/')
        if ([string]::Equals($target, $normalizedExpectedTarget, [System.StringComparison]::OrdinalIgnoreCase)) {
            $temporaryMappings += [PSCustomObject]@{ Drive = $drive; Target = $target }
        }
    }
}

if ($temporaryMappings.Count -eq 0) {
    Write-Host '[Nexus] No temporary Nexus tooling drive is currently mapped for this runtime folder.'
    exit 0
}

$confirmation = (New-Object -ComObject WScript.Shell).Popup(
    "Nexus created this temporary drive for installation tooling.`n`nOnly remove it after Nexus for ComfyUI has closed.`n`nDo you really want to unmount it?",
    0,
    'Nexus temporary tooling drive',
    36)
if ($confirmation -ne 6) {
    Write-Host '[Nexus] Temporary tooling drive removal was cancelled.'
    exit 0
}

foreach ($mapping in $temporaryMappings) {
    & subst.exe $mapping.Drive /d
    if ($LASTEXITCODE -eq 0) {
        Write-Host "[Nexus] Removed temporary tooling drive $($mapping.Drive) -> $($mapping.Target)"
    }
    else {
        Write-Warning "[Nexus] Could not remove $($mapping.Drive). Close any process using it and retry."
    }
}
""".Replace("__NEXUS_RUNTIME_ROOT__", escapedRoot, StringComparison.Ordinal);
	}

	private static string BuildDriveLeaseUnmountCommand()
		=> """
@echo off
setlocal
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0UNMOUNT_THIS_NEXUS_DRIVE.ps1"
set "exitCode=%ERRORLEVEL%"
if not "%exitCode%"=="0" echo [Nexus] Unmount script ended with exit code %exitCode%.
pause
exit /b %exitCode%
""";

	private static void ReleaseLegacyOwnedMappings(IEnumerable<(string DriveRoot, string TargetRoot)> mappings, string? managedRuntimeRoot)
	{
		if (HasAnotherNexusProcess())
		{
			return;
		}

		var mappingList = mappings.ToList();
		var legacyOwnedDrives = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

		foreach ((string driveRoot, string targetRoot) in mappingList)
		{
			string legacyRegistryPath = Path.Combine(targetRoot, "State", "tooling-path-leases.json");
			if (!TryReadLegacyRecord(legacyRegistryPath, driveRoot, targetRoot))
			{
				continue;
			}

			legacyOwnedDrives.Add(driveRoot);
			try
			{
				RunSubst($"{driveRoot[..2]} /d");
				File.Delete(legacyRegistryPath);
				NexusLog.Info($"[TOOLING_PATH] Released a legacy stale Nexus tooling drive {driveRoot}.");
			}
			catch (Exception ex)
			{
				NexusLog.Warning($"[TOOLING_PATH] Legacy tooling drive cleanup skipped for {driveRoot}: {ex.Message}");
			}
		}

		ReleaseUnregisteredManagedRuntimeMapping(
			mappingList.Where(mapping => !legacyOwnedDrives.Contains(mapping.DriveRoot)),
			managedRuntimeRoot);
	}

	private static void ReleaseUnregisteredManagedRuntimeMapping(
		IEnumerable<(string DriveRoot, string TargetRoot)> mappings,
		string? managedRuntimeRoot)
	{
		if (string.IsNullOrWhiteSpace(managedRuntimeRoot))
		{
			return;
		}

		string expectedRoot = NormalizePath(managedRuntimeRoot);
		foreach ((string driveRoot, string targetRoot) in mappings)
		{
			if (!string.Equals(targetRoot, expectedRoot, StringComparison.OrdinalIgnoreCase) ||
				NexusToolingPathLeaseRegistry.IsNexusManagedUnsafe(driveRoot, targetRoot))
			{
				continue;
			}

			try
			{
				RunSubst($"{driveRoot[..2]} /d");
				NexusLog.Info($"[TOOLING_PATH] Released an unregistered legacy Nexus runtime drive {driveRoot}.");
			}
			catch (Exception ex)
			{
				NexusLog.Warning($"[TOOLING_PATH] Legacy runtime drive cleanup skipped for {driveRoot}: {ex.Message}");
			}
		}
	}

	private static bool TryReadLegacyRecord(string path, string drive, string target)
	{
		if (!File.Exists(path))
		{
			return false;
		}

		try
		{
			return JsonSerializer.Deserialize<List<LegacyToolingPathLeaseRecord>>(File.ReadAllText(path, Encoding.UTF8))?
				.Any(record =>
					string.Equals(record.Drive, drive, StringComparison.OrdinalIgnoreCase) &&
					string.Equals(record.Target, target, StringComparison.OrdinalIgnoreCase)) == true;
		}
		catch
		{
			return false;
		}
	}

	private static bool HasAnotherNexusProcess()
	{
		string? processPath = Environment.ProcessPath;
		string processName = string.IsNullOrWhiteSpace(processPath)
			? string.Empty
			: Path.GetFileNameWithoutExtension(processPath);
		if (string.IsNullOrWhiteSpace(processName))
		{
			return true;
		}

		int currentProcessId = Environment.ProcessId;
		return Process.GetProcessesByName(processName).Any(process =>
		{
			using (process)
			{
				return process.Id != currentProcessId && !process.HasExited;
			}
		});
	}
#endif
	private static bool IsWithin(string path, string root)
		=> string.Equals(path, root, StringComparison.OrdinalIgnoreCase)
			|| path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
			|| path.StartsWith(root + Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

	private static string Translate(string sourceRoot, string targetRoot, string path)
	{
		string relative = Path.GetRelativePath(sourceRoot, path);
		return relative is "." or ""
			? NormalizePath(targetRoot)
			: Path.Combine(targetRoot, relative);
	}

	private static string NormalizePath(string path)
		=> Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

#if WINDOWS
	private static IEnumerable<(string DriveRoot, string TargetRoot)> GetMappings()
	{
		string output = RunSubst(string.Empty);
		foreach (string line in output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
		{
			const string separator = ":\\: => ";
			int separatorIndex = line.IndexOf(separator, StringComparison.OrdinalIgnoreCase);
			if (separatorIndex != 1)
			{
				continue;
			}

			yield return ($"{char.ToUpperInvariant(line[0])}:\\", NormalizePath(line[(separatorIndex + separator.Length)..]));
		}
	}

	private static string RunSubst(string arguments)
	{
		using var process = Process.Start(new ProcessStartInfo
		{
			FileName = "subst.exe",
			Arguments = arguments,
			UseShellExecute = false,
			CreateNoWindow = true,
			RedirectStandardOutput = true,
			RedirectStandardError = true
		}) ?? throw new InvalidOperationException("Unable to start subst.exe.");

		string output = process.StandardOutput.ReadToEnd();
		string error = process.StandardError.ReadToEnd();
		process.WaitForExit();
		if (process.ExitCode != 0)
		{
			throw new IOException($"Unable to manage a temporary tooling drive. {error.Trim()}");
		}

		return output;
	}

	private static string QuoteArgument(string value)
		=> $"\"{value.Replace("\"", "\\\"")}\"";
#endif

	private sealed record PathAlias(
		string PhysicalRoot,
		string AliasRoot,
		string DriveRoot,
		string TargetRoot,
		bool NexusManaged)
	{
		internal string Translate(string path) => NexusToolingPathLeaseController.Translate(PhysicalRoot, AliasRoot, path);
	}

}

internal sealed record LegacyToolingPathLeaseRecord(string Drive, string Target);

internal sealed class NexusToolingPathLeaseUnavailableException(string message) : IOException(message);

internal sealed record ToolingPathLeaseOwner(int ProcessId, long ProcessStartTimeUtcTicks, string InstanceId)
{
	internal static ToolingPathLeaseOwner CreateCurrent()
	{
		using Process process = Process.GetCurrentProcess();
		return new ToolingPathLeaseOwner(
			process.Id,
			process.StartTime.ToUniversalTime().Ticks,
			Guid.NewGuid().ToString("N"));
	}

	internal static bool IsProcessAlive(ToolingPathLeaseOwner owner)
	{
		try
		{
			using Process process = Process.GetProcessById(owner.ProcessId);
			if (process.HasExited)
			{
				return false;
			}

			try
			{
				return process.StartTime.ToUniversalTime().Ticks == owner.ProcessStartTimeUtcTicks;
			}
			catch
			{
				return true;
			}
		}
		catch (ArgumentException)
		{
			return false;
		}
		catch
		{
			return true;
		}
	}
}

internal sealed class ToolingPathLeaseRecord
{
	internal const string CurrentSchema = "Nexus.ToolingPathLease.v2";

	public string Schema { get; init; } = CurrentSchema;
	public string Drive { get; init; } = string.Empty;
	public string Target { get; init; } = string.Empty;
	public List<ToolingPathLeaseOwner> Owners { get; init; } = [];
}

internal static class NexusToolingPathLeaseRegistry
{
	private static readonly object Sync = new();
	private static readonly Mutex CrossProcessGate = new(false, "NexusForComfyUI.ToolingPathLeaseRegistry");
	private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = true };

	internal static IDisposable EnterOperation()
	{
		Monitor.Enter(Sync);
		try
		{
			try
			{
				CrossProcessGate.WaitOne();
			}
			catch (AbandonedMutexException)
			{
				// The previous owner ended unexpectedly. The current operation now owns cleanup.
			}

			return new RegistryOperation();
		}
		catch
		{
			Monitor.Exit(Sync);
			throw;
		}
	}

	internal static bool IsNexusManagedUnsafe(string drive, string target)
		=> ReadUnsafe().Any(record => IsMatch(record, drive, target));

	internal static void AddOwnerUnsafe(string drive, string target, ToolingPathLeaseOwner owner)
	{
		List<ToolingPathLeaseRecord> records = ReadUnsafe();
		ToolingPathLeaseRecord? record = records.FirstOrDefault(record => IsMatch(record, drive, target));
		if (record is null)
		{
			record = new ToolingPathLeaseRecord { Drive = drive, Target = target };
			records.Add(record);
		}

		record.Owners.RemoveAll(candidate => string.Equals(candidate.InstanceId, owner.InstanceId, StringComparison.Ordinal));
		record.Owners.Add(owner);
		WriteUnsafe(records);
	}

	internal static bool RemoveOwnerUnsafe(string drive, string target, ToolingPathLeaseOwner owner)
	{
		List<ToolingPathLeaseRecord> records = ReadUnsafe();
		ToolingPathLeaseRecord? record = records.FirstOrDefault(record => IsMatch(record, drive, target));
		if (record is null)
		{
			return false;
		}

		record.Owners.RemoveAll(candidate => string.Equals(candidate.InstanceId, owner.InstanceId, StringComparison.Ordinal));
		WriteUnsafe(records);
		return record.Owners.Count == 0;
	}

	internal static void ReplaceOwnersUnsafe(string drive, string target, IReadOnlyList<ToolingPathLeaseOwner> owners)
	{
		List<ToolingPathLeaseRecord> records = ReadUnsafe();
		ToolingPathLeaseRecord? record = records.FirstOrDefault(record => IsMatch(record, drive, target));
		if (record is null)
		{
			return;
		}

		record.Owners.Clear();
		record.Owners.AddRange(owners);
		WriteUnsafe(records);
	}

	internal static void RemoveMappingUnsafe(string drive, string target)
	{
		List<ToolingPathLeaseRecord> records = ReadUnsafe()
			.Where(record => !IsMatch(record, drive, target))
			.ToList();
		WriteUnsafe(records);
	}

	internal static List<ToolingPathLeaseRecord> ReadUnsafe()
	{
		string path = GetRegistryPath();
		if (!File.Exists(path))
		{
			return [];
		}

		try
		{
			return JsonSerializer.Deserialize<List<ToolingPathLeaseRecord>>(File.ReadAllText(path, Encoding.UTF8), SerializerOptions)?
				.Where(static record =>
					string.Equals(record.Schema, ToolingPathLeaseRecord.CurrentSchema, StringComparison.Ordinal) &&
					!string.IsNullOrWhiteSpace(record.Drive) &&
					!string.IsNullOrWhiteSpace(record.Target))
				.ToList() ?? [];
		}
		catch (Exception ex)
		{
			NexusLog.Warning($"[TOOLING_PATH] Unable to read the tooling lease registry: {ex.Message}");
			return [];
		}
	}

	private static bool IsMatch(ToolingPathLeaseRecord record, string drive, string target)
		=> string.Equals(record.Schema, ToolingPathLeaseRecord.CurrentSchema, StringComparison.Ordinal) &&
			string.Equals(record.Drive, drive, StringComparison.OrdinalIgnoreCase) &&
			string.Equals(record.Target, target, StringComparison.OrdinalIgnoreCase);

	private static void WriteUnsafe(IReadOnlyList<ToolingPathLeaseRecord> records)
	{
		string path = GetRegistryPath();
		if (records.Count == 0)
		{
			if (File.Exists(path))
			{
				File.Delete(path);
			}

			DeleteEmptyRegistryDirectories(path);
			return;
		}

		Directory.CreateDirectory(Path.GetDirectoryName(path)!);
		string temporaryPath = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
		File.WriteAllText(temporaryPath, JsonSerializer.Serialize(records, SerializerOptions), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
		File.Move(temporaryPath, path, overwrite: true);
	}

	private static void DeleteEmptyRegistryDirectories(string registryPath)
	{
		string? toolingDirectory = Path.GetDirectoryName(registryPath);
		string? productDirectory = toolingDirectory is null ? null : Path.GetDirectoryName(toolingDirectory);
		foreach (string? directory in new[] { toolingDirectory, productDirectory })
		{
			if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
			{
				continue;
			}

			if (!Directory.EnumerateFileSystemEntries(directory).Any())
			{
				Directory.Delete(directory);
			}
		}
	}

	private static string GetRegistryPath()
		=> Path.Combine(
			Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
			"NexusForComfyUI",
			"Tooling",
			"tooling-path-leases.json");

	private sealed class RegistryOperation : IDisposable
	{
		private bool _disposed;

		public void Dispose()
		{
			if (_disposed)
			{
				return;
			}

			_disposed = true;
			CrossProcessGate.ReleaseMutex();
			Monitor.Exit(Sync);
		}
	}

}
