namespace ComfyUI_Nexus;

using ComfyUI_Nexus.Configuration;
using ComfyUI_Nexus.Localization;
using ComfyUI_Nexus.Ui;

public partial class MainPage
{
	private async void OnToolbarModeToggled(object? sender, EventArgs e)
	{
		await SetCanvasModeMenuVisible(!CanvasModeMenuControl.IsOpen);
	}

	private async void OnCanvasModeMenuModeRequested(string mode)
	{
		await SetCanvasModeMenuVisible(false);
		ToolbarControl.SetMode(string.Equals(mode, CanvasModeOptions.Hand, StringComparison.OrdinalIgnoreCase));
		await _webViewBridge.SetCanvasModeAsync(mode);
	}

	private async void OnToolbarFitViewRequested(object? sender, EventArgs e)
	{
		await _webViewBridge.FitViewAsync();
	}

	private async void OnToolbarZoomRequested(object? sender, EventArgs e)
	{
		await _webViewBridge.OpenZoomControlsAsync();
	}

	private async void OnToolbarMinimapToggled(object? sender, EventArgs e)
	{
		await _webViewBridge.ToggleMinimapAsync();
	}

	private async void OnToolbarLinksToggled(object? sender, EventArgs e)
	{
		await _webViewBridge.ToggleLinksAsync();
	}

	private async void OnHeaderMainActionRequested(object? sender, EventArgs e)
	{
		if (string.Equals(_currentRunMode, RunModeOptions.Instant, StringComparison.OrdinalIgnoreCase))
		{
			bool? isStopButton = await _webViewBridge.ToggleInstantRunAsync();
			if (isStopButton.HasValue)
			{
				HeaderControl.SetInstantQueueButtonStop(isStopButton.Value);
				RefreshControlDeckRunPulse(isInstantStop: isStopButton.Value);
			}

			return;
		}

		int batchCount = HeaderControl.GetQueueCount();
		await _webViewBridge.QueuePromptAsync(batchCount);
	}

	private async void OnHeaderStopActionRequested(object? sender, EventArgs e)
	{
		await _webViewBridge.InterruptAsync();
		ShowToast(LocalizationManager.Text("toast.interrupting_job"), "warn");
	}

	private async void OnHeaderRunModeRequested(string mode)
	{
		_currentRunMode = mode;
		HeaderControl.SetRunMode(mode);
		await _webViewBridge.SetRunModeAsync(mode);
		await SyncInstantQueueButtonVisualAsync();
	}

	private async Task SyncHeaderRunModeFromWebAsync()
	{
		string? mode = await _webViewBridge.GetRunModeAsync();
		if (string.IsNullOrWhiteSpace(mode))
		{
			return;
		}

		_currentRunMode = mode;
		HeaderControl.SetRunMode(mode);
		await SyncInstantQueueButtonVisualAsync();
	}

	private async Task SyncInstantQueueButtonVisualAsync()
	{
		if (!string.Equals(_currentRunMode, RunModeOptions.Instant, StringComparison.OrdinalIgnoreCase))
		{
			HeaderControl.SetInstantQueueButtonStop(false);
			RefreshControlDeckRunPulse(isInstantStop: false);
			return;
		}

		bool? isStopButton = await _webViewBridge.GetQueueButtonIsStopAsync();
		if (isStopButton.HasValue)
		{
			HeaderControl.SetInstantQueueButtonStop(isStopButton.Value);
			RefreshControlDeckRunPulse(isInstantStop: isStopButton.Value);
		}
	}

	private async void OnHeaderViewQueueRequested(object? sender, EventArgs e)
	{
		await SetWorkflowActionsMenuVisible(false);
		await _webViewBridge.ViewQueueAsync();
		await SyncViewQueueButtonVisualAsync();
	}

	private async Task SyncViewQueueButtonVisualAsync()
	{
		bool? isOpen = await _webViewBridge.GetViewQueueOpenAsync();
		if (!isOpen.HasValue)
		{
			return;
		}

		HeaderControl.SetViewQueueActive(isOpen.Value);
	}

	private async void OnHeaderTogglePropertiesRequested(object? sender, EventArgs e)
	{
		await ExecuteHeaderBridgeActionAsync(BridgeActions.PanelToggle);
	}

	private async void OnHeaderShowManagerRequested(object? sender, EventArgs e)
	{
		await ExecuteHeaderBridgeActionAsync(BridgeActions.ShowManager);
	}

	private async void OnHeaderShowFavoritesRequested(object? sender, EventArgs e)
	{
		await ExecuteHeaderBridgeActionAsync(BridgeActions.ShowFavorites);
	}

	private async void OnHeaderUnloadModelsRequested(object? sender, EventArgs e)
	{
		await ExecuteHeaderBridgeActionAsync(BridgeActions.UnloadModels);
	}

	private async void OnHeaderFreeCacheRequested(object? sender, EventArgs e)
	{
		await ExecuteHeaderBridgeActionAsync(BridgeActions.FreeCache);
	}

	private async void OnHeaderShareRequested(object? sender, EventArgs e)
	{
		await ExecuteHeaderBridgeActionAsync(BridgeActions.Share);
	}

	private async void OnHeaderEnterAppModeRequested(object? sender, EventArgs e)
	{
		await ExecuteHeaderBridgeActionAsync(BridgeActions.EnterAppMode);
	}

	private async Task ExecuteHeaderBridgeActionAsync(string bridgeAction)
	{
		await SetWorkflowActionsMenuVisible(false);
		await _webViewBridge.InvokeActionAsync(bridgeAction);
	}

	private async void OnHeaderWorkflowActionsRequested(object? sender, EventArgs e)
	{
		await SetWorkflowActionsMenuVisible(!WorkflowActionsMenuControl.IsOpen);
	}

	private async void OnWorkflowActionsMenuActionRequested(WorkflowActionKind actionKind)
	{
		await ExecuteWorkflowActionAsync(actionKind);
	}

	private async Task ExecuteWorkflowActionAsync(WorkflowActionKind kind)
	{
		await SetWorkflowActionsMenuVisible(false);
		switch (kind)
		{
			case WorkflowActionKind.Bookmark:
				{
					var active = _tabController.ActiveWorkflow;
					if (active != null)
					{
						await ToggleBookmarkAsync(active.Name);
					}
					return;
				}
			case WorkflowActionKind.Rename:
				if (_tabController.ActiveWorkflow is { } renameWorkflow)
				{
					await HandleWorkflowTabActionAsync(renameWorkflow, WorkflowActionKind.Rename);
				}
				return;
			case WorkflowActionKind.Duplicate:
				await InvokeWorkflowMenuActionAsync(WorkflowMenuActions.Duplicate);
				return;
			case WorkflowActionKind.Save:
				await InvokeWorkflowMenuActionAsync(WorkflowMenuActions.Save);
				return;
			case WorkflowActionKind.SaveAs:
				await InvokeWorkflowMenuActionAsync(WorkflowMenuActions.SaveAs);
				return;
			case WorkflowActionKind.Export:
				await InvokeWorkflowMenuActionAsync(WorkflowMenuActions.Export);
				return;
			case WorkflowActionKind.ExportApi:
				await InvokeWorkflowMenuActionAsync(WorkflowMenuActions.ExportApi);
				return;
			case WorkflowActionKind.Clear:
				await InvokeWorkflowMenuActionAsync(WorkflowMenuActions.Clear);
				return;
			case WorkflowActionKind.Delete:
				await InvokeWorkflowMenuActionAsync(WorkflowMenuActions.Delete);
				return;
		}
	}

	private Task InvokeWorkflowMenuActionAsync(string workflowAction)
		=> _webViewBridge.InvokeActionAsync(BridgeActions.WorkflowMenuAction, $"{{ action: '{workflowAction}' }}");

}
