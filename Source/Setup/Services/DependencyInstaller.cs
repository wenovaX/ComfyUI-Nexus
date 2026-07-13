namespace ComfyUI_Nexus.Setup.Services;

using ComfyUI_Nexus.Setup.Models;

internal sealed class DependencyInstaller
{
	private readonly ComfyVenvManager _venvManager;

	internal DependencyInstaller(ComfyVenvManager venvManager)
	{
		_venvManager = venvManager;
	}

	internal async Task<SetupStepResult> InstallDependenciesAsync(CancellationToken cancellationToken)
		=> await RepairRuntimeDependenciesAsync(cancellationToken);

	internal async Task<SetupStepResult> RepairRuntimeDependenciesAsync(CancellationToken cancellationToken)
	{
		cancellationToken.ThrowIfCancellationRequested();
		return await _venvManager.RepairServerRuntimeDependenciesAsync(cancellationToken);
	}
}
