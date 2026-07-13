namespace ComfyUI_Nexus.Setup.Diagnostics.Nodes;

using ComfyUI_Nexus.Localization;
using ComfyUI_Nexus.Setup.Models;
using ComfyUI_Nexus.Setup.Services;

internal sealed class ModelLibraryDiagnosticNode : IOptionalConfigurableDiagnosticNode, IFolderSelectionDiagnosticNode
{
	private const string ConnectOption = "connect-model-library";
	private const string AddOption = "add-model-library";
	private const string ReplacePrefix = "replace-model-library:";
	private const string RemovePrefix = "remove-model-library:";
	private readonly bool _allowMultipleRoots;
	private string _lastApplyError = string.Empty;

	internal ModelLibraryDiagnosticNode(bool allowMultipleRoots)
	{
		_allowMultipleRoots = allowMultipleRoots;
	}

	public string NodeId => "model-library";
	public string DisplayName => Text("setup.model_library.title");
	public string Description => Text("setup.model_library.description");
	public bool IsCritical => false;
	public string EnvironmentDetails { get; private set; } = string.Empty;
	public string EnvironmentPath { get; private set; } = string.Empty;
	public IReadOnlyList<DiagnosticOption> AvailableOptions { get; private set; } = [];
	public string SelectedOptionId { get; private set; } = ConnectOption;

	public async Task ProbeEnvironmentAsync(CancellationToken cancellationToken)
	{
		await CheckHealthAsync(cancellationToken);
		RefreshOptions();
	}

	public void SelectOption(string optionId)
	{
		SelectedOptionId = optionId;
		if (!TryGetIndexedOption(optionId, RemovePrefix, out int index))
		{
			return;
		}

		RecoveryResult result = RemoveLibrary(index);
		_lastApplyError = result.IsSuccess ? string.Empty : result.Message;
	}

	public bool RequiresFolderSelection(string optionId)
		=> optionId is ConnectOption or AddOption
			|| optionId.StartsWith(ReplacePrefix, StringComparison.Ordinal);

	public RecoveryResult ApplySelectedFolder(string optionId, string folderPath)
	{
		var settings = SetupSettingsService.Instance.Settings;
		List<string> previousRoots = settings.ModelLibraryRoots.ToList();
		string normalized = ExtraModelPathsService.NormalizeFileSystemPath(folderPath);
		if (normalized.Length == 0)
		{
			return new RecoveryResult(false, Text("setup.model_library.invalid_folder"));
		}

		int replaceIndex = TryGetIndexedOption(optionId, ReplacePrefix, out int parsedIndex)
			? parsedIndex
			: -1;
		string currentPath = replaceIndex >= 0 && replaceIndex < settings.ModelLibraryRoots.Count
			? ExtraModelPathsService.NormalizeFileSystemPath(settings.ModelLibraryRoots[replaceIndex])
			: string.Empty;
		if (string.Equals(currentPath, normalized, StringComparison.OrdinalIgnoreCase))
		{
			return ExtraModelPathsService.NeedsSynchronization(settings, GetEffectiveComfyPath(settings))
				? ApplySettings(settings)
				: new RecoveryResult(true, Text("setup.model_library.already_connected_selected"));
		}

		if (settings.ModelLibraryRoots
			.Select(ExtraModelPathsService.NormalizeFileSystemPath)
			.Any(path => string.Equals(path, normalized, StringComparison.OrdinalIgnoreCase)))
		{
			return new RecoveryResult(false, Text("setup.model_library.already_connected"));
		}

		if (replaceIndex >= 0 && replaceIndex < settings.ModelLibraryRoots.Count)
		{
			settings.ModelLibraryRoots[replaceIndex] = normalized;
		}
		else if (!_allowMultipleRoots && settings.ModelLibraryRoots.Count > 0)
		{
			settings.ModelLibraryRoots[0] = normalized;
		}
		else
		{
			settings.ModelLibraryRoots.Add(normalized);
		}

		RecoveryResult result = ApplySettings(settings);
		if (!result.IsSuccess)
		{
			settings.ModelLibraryRoots = previousRoots;
		}

		_lastApplyError = result.IsSuccess ? string.Empty : result.Message;
		return result;
	}

	public Task<HealthState> CheckHealthAsync(CancellationToken cancellationToken)
	{
		cancellationToken.ThrowIfCancellationRequested();
		var settings = SetupSettingsService.Instance.Settings;
		IReadOnlyList<string> roots = ExtraModelPathsService.NormalizeRoots(settings);
		EnvironmentPath = roots.FirstOrDefault() ?? string.Empty;
		RefreshOptions();

		if (_lastApplyError.Length > 0)
		{
			EnvironmentDetails = _lastApplyError;
			return Task.FromResult(HealthState.OptionalMissing);
		}

		if (roots.Count == 0)
		{
			EnvironmentDetails = Text("setup.model_library.not_connected");
			return Task.FromResult(HealthState.OptionalMissing);
		}

		string comfyPath = GetEffectiveComfyPath(settings);
		ExtraModelPathsResult status = ExtraModelPathsService.Inspect(settings, comfyPath);
		EnvironmentDetails = status.IsSuccess
			? BuildConnectedDetails(roots)
			: BuildUnavailableDetails(roots);
		return Task.FromResult(status.IsSuccess ? HealthState.Healthy : HealthState.OptionalMissing);
	}

	public Task<RecoveryResult> RecoverAsync(IProgress<double>? progress, CancellationToken cancellationToken)
	{
		cancellationToken.ThrowIfCancellationRequested();
		progress?.Report(1);
		return Task.FromResult(new RecoveryResult(true, EnvironmentDetails));
	}

	private RecoveryResult RemoveLibrary(int index)
	{
		var settings = SetupSettingsService.Instance.Settings;
		if (index < 0 || index >= settings.ModelLibraryRoots.Count)
		{
			return new RecoveryResult(false, Text("setup.model_library.remove_failed"));
		}

		List<string> previousRoots = settings.ModelLibraryRoots.ToList();
		settings.ModelLibraryRoots.RemoveAt(index);
		RecoveryResult result = ApplySettings(settings);
		if (!result.IsSuccess)
		{
			settings.ModelLibraryRoots = previousRoots;
		}

		return result;
	}

	private static RecoveryResult ApplySettings(SetupSettings settings)
	{
		string comfyPath = GetEffectiveComfyPath(settings);
		ExtraModelPathsResult result = ExtraModelPathsService.TryApply(settings, comfyPath, out ExtraModelPathsTransaction? transaction);
		if (!result.IsSuccess)
		{
			return new RecoveryResult(false, result.Message);
		}

		if (!SetupSettingsService.Instance.TrySave())
		{
			transaction?.Rollback();
			return new RecoveryResult(
				false,
				LocalizationManager.Text("settings.model_libraries.settings_save_failed"));
		}

		transaction?.Commit();
		return new RecoveryResult(true, result.Message);
	}

	private void RefreshOptions()
	{
		var settings = SetupSettingsService.Instance.Settings;
		var options = new List<DiagnosticOption>();
		if (settings.ModelLibraryRoots.Count == 0)
		{
			options.Add(DiagnosticNodeHelpers.CreateOption(
				ConnectOption,
				Text("setup.model_library.option_connect"),
				isRecommended: true));
			AvailableOptions = options;
			return;
		}

		int visibleRootCount = _allowMultipleRoots ? settings.ModelLibraryRoots.Count : 1;
		for (int index = 0; index < visibleRootCount; index++)
		{
			options.Add(DiagnosticNodeHelpers.CreateOption(
				$"{ReplacePrefix}{index}",
				_allowMultipleRoots
					? LocalizationManager.Format("setup.model_library.option_replace_indexed", index + 1)
					: Text("setup.model_library.option_replace")));
			options.Add(DiagnosticNodeHelpers.CreateOption(
				$"{RemovePrefix}{index}",
				_allowMultipleRoots
					? LocalizationManager.Format("setup.model_library.option_remove_indexed", index + 1)
					: Text("setup.model_library.option_remove")));
		}

		if (_allowMultipleRoots)
		{
			options.Add(DiagnosticNodeHelpers.CreateOption(
				AddOption,
				Text("setup.model_library.option_add")));
		}

		AvailableOptions = options;
	}

	private static bool TryGetIndexedOption(string optionId, string prefix, out int index)
	{
		index = -1;
		return optionId.StartsWith(prefix, StringComparison.Ordinal)
			&& int.TryParse(optionId[prefix.Length..], out index);
	}

	private static string BuildConnectedDetails(IReadOnlyList<string> roots)
	{
		var lines = new List<string>
		{
			LocalizationManager.Format("setup.model_library.connected_count", roots.Count)
		};
		lines.AddRange(roots.Select((root, index) => $"{index + 1}. {root}"));
		lines.Add(Text("setup.model_library.restart_hint"));
		return string.Join(Environment.NewLine, lines);
	}

	private static string BuildUnavailableDetails(IReadOnlyList<string> roots)
	{
		string message = roots.Any(root => !Directory.Exists(root))
			? Text("setup.model_library.unavailable")
			: Text("setup.model_library.needs_sync");
		var lines = new List<string> { message };
		lines.AddRange(roots.Select((root, index) => $"{index + 1}. {root}"));
		return string.Join(Environment.NewLine, lines);
	}

	private static string GetEffectiveComfyPath(SetupSettings settings)
		=> string.IsNullOrWhiteSpace(settings.ComfyPath)
			? ComfyInstallService.DefaultComfyPath
			: settings.ComfyPath;

	private static string Text(string key)
		=> LocalizationManager.Text(key);
}
