namespace ComfyUI_Nexus.Ui.Popups;

using ComfyUI_Nexus.Diagnostics;
using ComfyUI_Nexus.Ui;

internal sealed class NexusPopupManager
{
	private readonly Action _captureFocus;
	private readonly Action _restoreFocus;
	private readonly Dictionary<string, INexusPopupSurface> _surfaces = new(StringComparer.Ordinal);
	private readonly Dictionary<string, SemaphoreSlim> _gates = new(StringComparer.Ordinal);

	internal NexusPopupManager(Action captureFocus, Action restoreFocus)
	{
		_captureFocus = captureFocus;
		_restoreFocus = restoreFocus;
	}

	internal void Register(INexusPopupSurface surface)
	{
		_surfaces[surface.PopupKey] = surface;
		_gates.TryAdd(surface.PopupKey, new SemaphoreSlim(1, 1));
	}

	internal bool IsShown(string key, bool isVisible)
		=> _surfaces.TryGetValue(key, out var surface) && surface.IsShown(isVisible);

	internal Task CloseAllAsync(bool restoreFocusOnClose = false)
	{
		var targets = _surfaces.Values
			.Where(surface => !surface.IsShown(false))
			.ToList();

		if (targets.Count == 0)
		{
			return Task.CompletedTask;
		}

		var context = new NexusPopupOpenContext(RestoreFocusOnClose: restoreFocusOnClose);
		return Task.WhenAll(targets.Select(surface => HideAsync(surface, context)));
	}

	internal async Task SetVisibleAsync(
		string key,
		bool isVisible,
		NexusPopupOpenContext? context = null,
		params string[] closeGroupsBeforeShow)
	{
		if (!_surfaces.TryGetValue(key, out var surface))
		{
			return;
		}

		if (surface.IsShown(isVisible))
		{
			return;
		}

		context ??= new NexusPopupOpenContext();
		if (isVisible)
		{
			Task closeGroupsTask = CloseGroupsAsync(key, closeGroupsBeforeShow);
			await ShowAsync(surface, context);
			await ObservePeerCloseAsync(closeGroupsTask, key);
		}
		else
		{
			await HideAsync(surface, context);
		}
	}

	private async Task CloseGroupsAsync(string exceptKey, IReadOnlyCollection<string> groups)
	{
		if (groups.Count == 0)
		{
			return;
		}

		var targets = _surfaces.Values
			.Where(surface => !string.Equals(surface.PopupKey, exceptKey, StringComparison.Ordinal)
				&& groups.Contains(surface.PopupGroup, StringComparer.Ordinal)
				&& !surface.IsShown(false))
			.ToList();

		foreach (var target in targets)
		{
			await HideAsync(target, new NexusPopupOpenContext(RestoreFocusOnClose: false));
		}
	}

	private async Task ShowAsync(INexusPopupSurface surface, NexusPopupOpenContext context)
	{
		var gate = _gates[surface.PopupKey];
		await gate.WaitAsync();
		try
		{
			if (surface.IsShown(true))
			{
				return;
			}

			if (context.CaptureFocus)
			{
				_captureFocus();
			}

			surface.PrepareShowShell(context);
			XamlLifetimeDiagnostics.RecordSurface($"popup:{surface.PopupKey}", "showing");
			await NexusUiFrame.AwaitShellReadyAsync(surface.PopupRoot, $"POPUP:{surface.PopupKey}");
			surface.ActivateInput(context);
			await surface.AnimateShowAsync(context);
			await surface.RefreshContentAsync(context);
			XamlLifetimeDiagnostics.RecordSurface($"popup:{surface.PopupKey}", "shown");
			if (context.CaptureFocus && context.RefocusAfterShow)
			{
				_captureFocus();
			}
		}
		finally
		{
			gate.Release();
		}
	}

	private async Task HideAsync(INexusPopupSurface surface, NexusPopupOpenContext context)
	{
		var gate = _gates[surface.PopupKey];
		await gate.WaitAsync();
		try
		{
			if (surface.IsShown(false))
			{
				return;
			}

			surface.PrepareHide();
			XamlLifetimeDiagnostics.RecordSurface($"popup:{surface.PopupKey}", "hiding");
			await surface.AnimateHideAsync(context);
			surface.ResetHiddenState();
			XamlLifetimeDiagnostics.RemoveSurface($"popup:{surface.PopupKey}");
			if (context.RestoreFocusOnClose)
			{
				_restoreFocus();
			}
		}
		finally
		{
			gate.Release();
		}
	}

	private static async Task ObservePeerCloseAsync(Task closeGroupsTask, string key)
	{
		try
		{
			await closeGroupsTask;
		}
		catch (Exception ex) when (ex is not OperationCanceledException)
		{
			NexusLog.Exception(ex, $"[POPUP] Peer close failed while opening {key}.");
		}
	}
}
