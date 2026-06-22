namespace ComfyUI_Nexus.Setup.Diagnostics.Nodes;

using ComfyUI_Nexus.Localization;
using ComfyUI_Nexus.Setup.Models;
using ComfyUI_Nexus.Setup.Services;

internal sealed class PipCacheDiagnosticNode : IOptionalConfigurableDiagnosticNode, IFolderSelectionDiagnosticNode
{
	private const string PipDefaultOption = "pip-cache:pip-default";
	private const string NexusDefaultOption = "pip-cache:nexus-default";
	private const string CustomOption = "pip-cache:custom";

	public string NodeId => "pip-cache";
	public string DisplayName => Text("setup.pip_cache.title");
	public string Description => Text("setup.pip_cache.description");
	public bool IsCritical => false;
	public string EnvironmentDetails { get; private set; } = string.Empty;
	public string EnvironmentPath { get; private set; } = string.Empty;
	public IReadOnlyList<DiagnosticOption> AvailableOptions { get; private set; } = [];
	public string SelectedOptionId { get; private set; } = NexusDefaultOption;

	public Task ProbeEnvironmentAsync(CancellationToken cancellationToken)
	{
		cancellationToken.ThrowIfCancellationRequested();
		RefreshState();
		return Task.CompletedTask;
	}

	public void SelectOption(string optionId)
	{
		SelectedOptionId = optionId;
		var settings = SetupSettingsService.Instance.Settings;
		settings.PipCacheMode = optionId switch
		{
			PipDefaultOption => PipCacheModes.PipDefault,
			NexusDefaultOption => PipCacheModes.NexusDefault,
			_ => settings.PipCacheMode
		};

		SetupSettingsService.Instance.Save();
		RefreshState();
	}

	public bool RequiresFolderSelection(string optionId)
		=> optionId == CustomOption;

	public RecoveryResult ApplySelectedFolder(string optionId, string folderPath)
	{
		if (optionId != CustomOption)
		{
			return new RecoveryResult(false, Text("setup.pip_cache.unsupported_option"));
		}

		string normalized = NormalizeFolderPath(folderPath);
		if (normalized.Length == 0)
		{
			return new RecoveryResult(false, Text("setup.pip_cache.invalid_folder"));
		}

		try
		{
			Directory.CreateDirectory(normalized);
			var settings = SetupSettingsService.Instance.Settings;
			settings.PipCacheMode = PipCacheModes.Custom;
			settings.PipCachePath = normalized;
			if (!SetupSettingsService.Instance.TrySave())
			{
				return new RecoveryResult(false, LocalizationManager.Text("settings.pip_cache.save_failed"));
			}

			RefreshState();
			return new RecoveryResult(true, EnvironmentDetails);
		}
		catch (Exception ex)
		{
			return new RecoveryResult(false, ex.Message);
		}
	}

	public Task<HealthState> CheckHealthAsync(CancellationToken cancellationToken)
	{
		cancellationToken.ThrowIfCancellationRequested();
		RefreshState();
		return Task.FromResult(HealthState.Healthy);
	}

	public Task<RecoveryResult> RecoverAsync(IProgress<double>? progress, CancellationToken cancellationToken)
	{
		cancellationToken.ThrowIfCancellationRequested();
		progress?.Report(1);
		return Task.FromResult(new RecoveryResult(true, EnvironmentDetails));
	}

	private void RefreshState()
	{
		var settings = SetupSettingsService.Instance.Settings;
		string mode = PipCacheService.GetMode(settings);
		SelectedOptionId = mode switch
		{
			PipCacheModes.PipDefault => PipDefaultOption,
			PipCacheModes.Custom => CustomOption,
			_ => NexusDefaultOption
		};
		EnvironmentPath = PipCacheService.GetEffectiveCachePath(settings);
		EnvironmentDetails = BuildDetails(mode, EnvironmentPath);
		RefreshOptions(mode);
	}

	private static string BuildDetails(string mode, string path)
	{
		if (string.Equals(mode, PipCacheModes.PipDefault, StringComparison.Ordinal))
		{
			return Text("setup.pip_cache.details_pip_default");
		}

		string label = string.Equals(mode, PipCacheModes.Custom, StringComparison.Ordinal)
			? Text("setup.pip_cache.details_custom")
			: Text("setup.pip_cache.details_nexus_default");
		return $"{label}{Environment.NewLine}{path}";
	}

	private void RefreshOptions(string mode)
	{
		AvailableOptions =
		[
			DiagnosticNodeHelpers.CreateOption(
				PipDefaultOption,
				Text("setup.pip_cache.option_pip_default"),
				Text("setup.pip_cache.option_pip_default_description"),
				isRecommended: string.Equals(mode, PipCacheModes.PipDefault, StringComparison.Ordinal)),
			DiagnosticNodeHelpers.CreateOption(
				NexusDefaultOption,
				Text("setup.pip_cache.option_nexus_default"),
				Text("setup.pip_cache.option_nexus_default_description"),
				isRecommended: string.Equals(mode, PipCacheModes.NexusDefault, StringComparison.Ordinal)),
			DiagnosticNodeHelpers.CreateOption(
				CustomOption,
				Text("setup.pip_cache.option_custom"),
				Text("setup.pip_cache.option_custom_description"),
				isRecommended: string.Equals(mode, PipCacheModes.Custom, StringComparison.Ordinal))
		];
	}

	private static string Text(string key)
		=> LocalizationManager.Text(key);

	private static string NormalizeFolderPath(string folderPath)
	{
		if (string.IsNullOrWhiteSpace(folderPath))
		{
			return string.Empty;
		}

		return Path.GetFullPath(folderPath.Trim());
	}
}
