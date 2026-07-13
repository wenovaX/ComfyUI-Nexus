namespace ComfyUI_Nexus;

using ComfyUI_Nexus.Configuration;
using ComfyUI_Nexus.Localization;
using ComfyUI_Nexus.Ui;

public partial class MainPage
{
	private async Task ExecuteToolbarModeAsync()
	{
		await SetCanvasModeMenuVisible(!CanvasModeMenuControl.IsOpen);
	}

	private async Task ExecuteCanvasModeMenuModeAsync(string mode)
	{
		await SetCanvasModeMenuVisible(false);
		ToolbarControl.SetMode(string.Equals(mode, CanvasModeOptions.Hand, StringComparison.OrdinalIgnoreCase));
		await _webViewBridge.SetCanvasModeAsync(mode);
	}

	private async Task ExecuteToolbarFitViewAsync()
	{
		await _webViewBridge.FitViewAsync();
	}

	private async Task ExecuteToolbarZoomAsync()
	{
		await _webViewBridge.OpenZoomControlsAsync();
	}

	private async Task ExecuteToolbarMinimapAsync()
	{
		await _webViewBridge.ToggleMinimapAsync();
	}

	private async Task ExecuteToolbarLinksAsync()
	{
		await _webViewBridge.ToggleLinksAsync();
	}

	private async Task ExecuteToolbarHelpAsync()
	{
		await _webViewBridge.OpenHelpCenterAsync(GetRailContentPanelWidth());
	}

	private async Task ExecuteToolbarTerminalAsync()
	{
		await _webViewBridge.ToggleBottomPanelAsync();
	}

	private async Task ExecuteToolbarShortcutsAsync()
	{
		await _webViewBridge.ToggleShortcutsAsync();
	}

	private async Task ExecuteHeaderMainActionAsync()
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

	private async Task ExecuteHeaderStopActionAsync()
	{
		await _webViewBridge.InterruptAsync();
		ShowToast(LocalizationManager.Text("toast.interrupting_job"), "warn", ToastRaisedTopMargin);
	}

	private async Task ExecuteHeaderRunModeAsync(string mode)
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

	private async Task ExecuteHeaderViewQueueAsync()
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

	private async Task ExecuteHeaderTogglePropertiesAsync()
	{
		await ExecuteHeaderBridgeActionAsync(BridgeActions.PanelToggle);
	}

	private async Task ExecuteHeaderShowManagerAsync()
	{
		await ExecuteHeaderBridgeActionAsync(BridgeActions.ShowManager);
	}

	private async Task ExecuteHeaderShowFavoritesAsync()
	{
		await ExecuteHeaderBridgeActionAsync(BridgeActions.ShowFavorites);
	}

	private async Task ExecuteHeaderUnloadModelsAsync()
	{
		await ExecuteHeaderBridgeActionAsync(BridgeActions.UnloadModels);
	}

	private async Task ExecuteHeaderFreeCacheAsync()
	{
		await ExecuteHeaderBridgeActionAsync(BridgeActions.FreeCache);
	}

	private async Task ExecuteHeaderShareAsync()
	{
		await ExecuteHeaderBridgeActionAsync(BridgeActions.Share);
	}

	private async Task ExecuteHeaderEnterAppModeAsync()
	{
		await ExecuteHeaderBridgeActionAsync(BridgeActions.EnterAppMode);
	}

	private async Task ExecuteHeaderMainMenuAsync()
	{
		await _webViewBridge.ToggleMainMenuAsync(GetRailContentPanelWidth());
	}

	private async Task ExecuteHeaderBridgeActionAsync(string bridgeAction)
	{
		await SetWorkflowActionsMenuVisible(false);
		await _webViewBridge.InvokeActionAsync(bridgeAction);
	}

	private async Task ExecuteHeaderWorkflowActionsAsync()
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
				await InvokeWorkflowCommandActionAsync(WorkflowMenuActions.Duplicate);
				return;
			case WorkflowActionKind.Save:
				await InvokeWorkflowCommandActionAsync(WorkflowMenuActions.Save);
				return;
			case WorkflowActionKind.SaveAs:
				await InvokeWorkflowCommandActionAsync(WorkflowMenuActions.SaveAs);
				return;
			case WorkflowActionKind.Export:
				await InvokeWorkflowCommandActionAsync(WorkflowMenuActions.Export);
				return;
			case WorkflowActionKind.ExportApi:
				await InvokeWorkflowCommandActionAsync(WorkflowMenuActions.ExportApi);
				return;
			case WorkflowActionKind.Clear:
				await InvokeWorkflowCommandActionAsync(WorkflowMenuActions.Clear);
				return;
			case WorkflowActionKind.Delete:
				await InvokeWorkflowMenuActionAsync(WorkflowMenuActions.Delete);
				return;
		}
	}

	private Task InvokeWorkflowMenuActionAsync(string workflowAction)
		=> _webViewBridge.InvokeActionAsync(BridgeActions.WorkflowMenuAction, $"{{ action: '{workflowAction}' }}");

	private Task InvokeWorkflowCommandActionAsync(string workflowAction)
		=> _webViewBridge.InvokeActionAsync(BridgeActions.WorkflowCommandAction, $"{{ action: '{workflowAction}' }}");

}
