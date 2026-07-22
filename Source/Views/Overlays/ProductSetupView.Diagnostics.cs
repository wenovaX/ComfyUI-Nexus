namespace ComfyUI_Nexus.Views.Overlays;

using System.Collections.ObjectModel;
using ComfyUI_Nexus.Configuration;
using ComfyUI_Nexus.Diagnostics;
using ComfyUI_Nexus.Dialogs;
using ComfyUI_Nexus.Localization;
using ComfyUI_Nexus.Setup.Diagnostics;
using ComfyUI_Nexus.Setup.Diagnostics.Nodes;
using ComfyUI_Nexus.Setup.Models;
using ComfyUI_Nexus.Setup.Runtime;
using ComfyUI_Nexus.Setup.Services;
using ComfyUI_Nexus.Ui;
using Microsoft.Maui.Controls.Shapes;

public partial class ProductSetupView
{	private async Task RunRepairSequenceAsync()
	{
		if (_repairCts is not null)
		{
			return;
		}

		_repairCts = new CancellationTokenSource();
		var token = _repairCts.Token;

		_currentContext = ViewContext.Repairing;

		if (ArchitectWorkspacePanel.IsVisible)
		{
			await SafeAnimation.FadeToAsync(ArchitectWorkspacePanel, 0, PanelQuickAnimationLength, source: "Setup.Repair");
			ArchitectWorkspacePanel.IsVisible = false;
			PrepareVanguardChecklist();
			VanguardPanel.IsVisible = true;
			await SafeAnimation.FadeToAsync(VanguardPanel, 1, PanelQuickAnimationLength, source: "Setup.Repair");
			ResetInitiationScrollPosition(VanguardInitiationScrollView);
		}

		_currentState = ViewState.Ready;

		try
		{
			await _initiationSequence.RunVanguardAsync(_vanguardRequiredSteps, token);
		}
		catch (OperationCanceledException)
		{
			// Sequence was cancelled by user going back
			return;
		}
		finally
		{
			_repairCts = null;
			// Ensure UI is interactive again
			DiagnosticNodesList.InputTransparent = false;
			VanguardOptionalNodesList.InputTransparent = false;
			EvaluateOverallReadiness();
			_currentState = ViewState.Ready;
			UpdateBackButtonAvailability();
		}

		if (VanguardOptionalNodes.Count > 0)
		{
			await RefreshVanguardOptionalNodesSafeAsync();
			await ScrollVanguardOptionalSectionIntoViewAsync();
		}

		// All done
		_currentState = ViewState.Ready;
		UpdateBackButtonAvailability();
	}

	private async Task WaitForStepCompleteAsync(SetupDiagnosticStep step, CancellationToken token)
	{
		if (step.CountsAsReady)
		{
			return;
		}

		var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		using var reg = token.Register(() => tcs.TrySetCanceled());

		void handler(object? sender, EventArgs e)
		{
			if (step.CountsAsReady)
			{
				tcs.TrySetResult();
			}
		}

		void stepHandler(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
		{
			if (e.PropertyName == nameof(SetupDiagnosticStep.State) && step.CountsAsReady)
			{
				tcs.TrySetResult();
			}
		}

		step.ViewModel.CompletionSignalChanged += handler;
		step.PropertyChanged += stepHandler;
		try
		{
			if (step.CountsAsReady)
			{
				return;
			}

			await tcs.Task;
		}
		finally
		{
			step.ViewModel.CompletionSignalChanged -= handler;
			step.PropertyChanged -= stepHandler;
		}
	}

	private Task RequestDiagnosticScrollAsync(SetupDiagnosticStep step, SetupScrollReason reason)
	{
		_focusedRequiredStep = step.IsRequired ? step : null;
		_focusedDiagnosticNode = step.ViewModel;
		return RequestSetupScrollAsync(step, SetupScrollPivot.Top, reason, animated: true);
	}

	private void EvaluateCurrentInitiationReadiness()
	{
		if (_currentContext == ViewContext.Architect)
		{
			CheckArchitectInitiationStatus();
			return;
		}

		EvaluateOverallReadiness();
	}

	private static void UpdateDiagnosticProgress(DiagnosticNodeViewModel vm, double progress, string message)
	{
		UiThread.TryBeginInvoke(() =>
		{
			vm.UpdateProgressDisplay(progress, message);
		}, "PRODUCT_SETUP:DIAGNOSTIC_PROGRESS");
	}

	internal ObservableCollection<DiagnosticNodeViewModel> ArchitectNodes { get; } = new();
	internal ObservableCollection<DiagnosticNodeViewModel> ArchitectOptionalNodes { get; } = new();
	internal ObservableCollection<DiagnosticNodeViewModel> VanguardNodes { get; } = new();
	internal ObservableCollection<DiagnosticNodeViewModel> VanguardOptionalNodes { get; } = new();

	private void UpdateVanguardOptionalSectionVisibility()
	{
		bool hasOptionalNodes = VanguardOptionalNodes.Count > 0;
		VanguardOptionalHeader.IsVisible = hasOptionalNodes;
		VanguardOptionalNodesList.IsVisible = hasOptionalNodes;
	}

	private void UpdateArchitectOptionalSectionVisibility()
	{
		bool hasOptionalNodes = ArchitectOptionalNodes.Count > 0;
		ArchitectOptionalHeader.IsVisible = hasOptionalNodes;
		ArchitectOptionalNodesList.IsVisible = hasOptionalNodes;
	}

	private void UpdateArchitectRequiredStatus()
	{
		UpdateRequiredStatus(ArchitectRequiredStatusLabel, _architectRequiredSteps);
	}

	private void UpdateVanguardRequiredStatus()
	{
		UpdateRequiredStatus(VanguardRequiredStatusLabel, _vanguardRequiredSteps);
	}

	private static void UpdateRequiredStatus(Label label, IReadOnlyCollection<SetupDiagnosticStep> steps)
	{
		int totalCount = steps.Count;
		int readyCount = steps.Count(step => step.CountsAsReady);
		label.Text = $"{readyCount}/{totalCount} READY";
		label.TextColor = readyCount == totalCount && totalCount > 0
			? DiagnosticRequiredReadyColor
			: DiagnosticRequiredPendingColor;
	}

	private void PrepareVanguardChecklist()
	{
		VanguardNodes.Clear();
		VanguardOptionalNodes.Clear();
		_vanguardRequiredSteps.Clear();
		_vanguardOptionalSteps.Clear();

		AddRequiredVanguardStep(GetDiagnosticNode<GitDiagnosticNode>());
		AddRequiredVanguardStep(GetDiagnosticNode<PythonDiagnosticNode>());
		AddRequiredVanguardStep(GetDiagnosticNode<ComfyCoreDiagnosticNode>());
		AddRequiredVanguardStep(GetDiagnosticNode<BaseResourceDiagnosticNode>());
		AddRequiredVanguardStep(GetDiagnosticNode<ManagerExtensionDiagnosticNode>());

		AddOptionalVanguardStep(GetDiagnosticNode<ModelLibraryDiagnosticNode>());

		BindableLayout.SetItemsSource(DiagnosticNodesList, VanguardNodes);
		BindableLayout.SetItemsSource(VanguardOptionalNodesList, VanguardOptionalNodes);
		UpdateVanguardOptionalSectionVisibility();
		UpdateVanguardRequiredStatus();
		EvaluateOverallReadiness();
	}

	private void AddRequiredVanguardStep(IRuntimeDiagnosticNode node)
	{
		var step = CreateDiagnosticStep(node, isRequired: true);
		_vanguardRequiredSteps.Add(step);
		VanguardNodes.Add(step.ViewModel);
	}

	private void AddOptionalVanguardStep(IRuntimeDiagnosticNode node)
	{
		var step = CreateDiagnosticStep(node, isRequired: false);
		_vanguardOptionalSteps.Add(step);
		VanguardOptionalNodes.Add(step.ViewModel);
		if (node is PythonEnvironmentDiagnosticNode)
		{
			step.ViewModel.EnvironmentDetails = LocalizationManager.Text("setup.venv.requires_python");
			step.ViewModel.EnvironmentPath = _appManager.Paths.ActiveVenvPath;
		}
	}

	private void PopulateInlineActions(DiagnosticNodeViewModel vm, IConfigurableDiagnosticNode confNode)
	{
		vm.Actions.Clear();
		foreach (var opt in confNode.AvailableOptions)
		{
			var actionColors = GetDiagnosticActionColors(opt.Id, opt.IsRecommended);
			vm.Actions.Add(new DiagnosticActionViewModel
			{
				Id = opt.Id,
				DisplayName = opt.DisplayName,
				Description = opt.Description,
				WorkingHint = opt.WorkingHint,
				NormalBackground = actionColors.Normal,
				HoverBackground = actionColors.Hover,
				TextColor = actionColors.Text,
				Command = new Command(async () => await ExecuteNodeActionAsync(vm, confNode, opt.Id))
			});
		}
		vm.NotifyActionsChanged();
	}

	private void PopulatePersistentInlineActionsIfNeeded(DiagnosticNodeViewModel vm)
	{
		if (!ShouldKeepInlineActionsVisible(vm)) return;
		if (vm.Node is not IConfigurableDiagnosticNode configurableNode) return;

		PopulateInlineActions(vm, configurableNode);
		vm.ActionText = GetInteractiveActionText(vm);
		SetDiagnosticNodeInteraction(vm, true);
	}

	private void EnableDiagnosticNodeInteraction(DiagnosticNodeViewModel vm)
	{
		SetDiagnosticNodeInteraction(vm, true);
		SetDiagnosticActionNavigationBlocked(false);
	}

	private void OnGlassItemHovered(object? sender, PointerEventArgs e)
	{
		if (sender is Border b && b.BindingContext is DiagnosticNodeViewModel nodeVm)
		{
			if (nodeVm.IsLoading) return;

			b.BackgroundColor = NexusColors.SurfaceSubtle;
		}
		else if (sender is Border b2 && b2.BindingContext is DiagnosticActionViewModel actionVm)
		{
			b2.BackgroundColor = actionVm.HoverBackground;
		}
	}

	private void OnDiagnosticLoadingRingLoaded(object? sender, EventArgs e)
	{
		if (sender is not Image ring || ring.BindingContext is not DiagnosticNodeViewModel vm)
		{
			return;
		}

		vm.PropertyChanged -= OnDiagnosticLoadingRingViewModelChanged;
		vm.PropertyChanged += OnDiagnosticLoadingRingViewModelChanged;
		UpdateDiagnosticLoadingRing(ring, vm.IsLoading);
	}

	private void OnDiagnosticLoadingRingUnloaded(object? sender, EventArgs e)
	{
		if (sender is not Image ring)
		{
			return;
		}

		if (ring.BindingContext is DiagnosticNodeViewModel vm)
		{
			vm.PropertyChanged -= OnDiagnosticLoadingRingViewModelChanged;
		}

		DisposeDiagnosticLoadingRingClip(ring);
	}

	private void OnDiagnosticLoadingRingViewModelChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
	{
		if (e.PropertyName != nameof(DiagnosticNodeViewModel.IsLoading)) return;
		if (sender is not DiagnosticNodeViewModel vm) return;

		UiThread.TryBeginInvoke(() =>
		{
			UpdateLoadingRingInList(DiagnosticNodesList, vm);
			UpdateLoadingRingInList(VanguardOptionalNodesList, vm);
			UpdateLoadingRingInList(ArchitectNodesList, vm);
			UpdateLoadingRingInList(ArchitectOptionalNodesList, vm);
		}, "PRODUCT_SETUP:DIAGNOSTIC_RING");
	}

	private void UpdateLoadingRingInList(Layout list, DiagnosticNodeViewModel vm)
	{
		foreach (Image ring in FindVisualChildren<Image>(list))
		{
			if (ring.BindingContext == vm && ring.StyleId == "DiagnosticLoadingRing")
			{
				UpdateDiagnosticLoadingRing(ring, vm.IsLoading);
			}
		}
	}

	private void UpdateDiagnosticLoadingRing(Image ring, bool isLoading)
	{
		if (isLoading)
		{
			StartDiagnosticLoadingRing(ring);
			return;
		}

		StopDiagnosticLoadingRing(ring);
	}

	private void StartDiagnosticLoadingRing(Image ring)
	{
		if (_isDisposed || !IsVisible || !ring.IsVisible)
		{
			return;
		}

		ring.Opacity = 1;
		GetOrCreateDiagnosticLoadingRingClip(ring).PlayLoop(() => CanRunDiagnosticLoadingRing(ring));
	}

	private void StopDiagnosticLoadingRing(Image ring)
	{
		if (_diagnosticLoadingRingClips.TryGetValue(ring, out NexusAnimatedWebpClip? clip))
		{
			clip.Stop();
		}

		ring.Opacity = 0;
	}

	private NexusAnimatedWebpClip GetOrCreateDiagnosticLoadingRingClip(Image ring)
	{
		if (_diagnosticLoadingRingClips.TryGetValue(ring, out NexusAnimatedWebpClip? clip))
		{
			return clip;
		}

		string motionName = $"{DiagnosticLoadingRingWebpAnimationName}.{System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(ring):X8}";
		clip = new NexusAnimatedWebpClip(_motion, _appManager.AnimatedWebpFrames, ring, motionName, NexusAnimatedWebpCacheCatalog.SetupDiagnosticLoadingRing);
		_diagnosticLoadingRingClips.Add(ring, clip);
		return clip;
	}

	private bool CanRunDiagnosticLoadingRing(Image ring)
		=> !_isDisposed
			&& IsVisible
			&& Handler is not null
			&& ring.IsVisible
			&& ring.Opacity > 0;

	private void DisposeDiagnosticLoadingRingClip(Image ring)
	{
		if (_diagnosticLoadingRingClips.Remove(ring, out NexusAnimatedWebpClip? clip))
		{
			clip.Dispose();
		}

		ring.Opacity = 0;
	}

	private void StopDiagnosticLoadingRingAnimations()
	{
		foreach ((Image ring, NexusAnimatedWebpClip clip) in _diagnosticLoadingRingClips)
		{
			clip.Stop();
			ring.Opacity = 0;
		}
	}

	private void DisposeDiagnosticLoadingRingClips()
	{
		foreach (NexusAnimatedWebpClip clip in _diagnosticLoadingRingClips.Values)
		{
			clip.Dispose();
		}

		_diagnosticLoadingRingClips.Clear();
	}

	private static IEnumerable<T> FindVisualChildren<T>(Element parent)
		where T : Element
	{
		if (parent is not IVisualTreeElement visualParent)
		{
			yield break;
		}

		foreach (IVisualTreeElement visualChild in visualParent.GetVisualChildren())
		{
			if (visualChild is not Element child)
			{
				continue;
			}

			if (child is T typed)
			{
				yield return typed;
			}

			foreach (T descendant in FindVisualChildren<T>(child))
			{
				yield return descendant;
			}
		}
	}

	private void OnGlassItemUnhovered(object? sender, PointerEventArgs e)
	{
		if (sender is Border b && b.BindingContext is DiagnosticNodeViewModel nodeVm)
		{
			if (nodeVm.IsLoading) return;

			b.BackgroundColor = Colors.Transparent;
		}
		else if (sender is Border b2 && b2.BindingContext is DiagnosticActionViewModel actionVm)
		{
			b2.BackgroundColor = actionVm.NormalBackground;
		}
	}

	private async void OnDiagnosticNodeTapped(object? sender, TappedEventArgs e)
	{
		if (_isDiagnosticActionRunning) return;
		if (sender is not View { BindingContext: DiagnosticNodeViewModel nodeVm } editTrigger) return;
		if (!CanReconfigureReadyNode(nodeVm)) return;
		if (nodeVm.Node is not IConfigurableDiagnosticNode configurableNode) return;

		if (ShouldKeepInlineActionsVisible(nodeVm))
		{
			await configurableNode.ProbeEnvironmentAsync(_repairCts?.Token ?? CancellationToken.None);
			nodeVm.EnvironmentDetails = configurableNode.EnvironmentDetails;
			nodeVm.EnvironmentPath = configurableNode.EnvironmentPath;
			nodeVm.SecondaryEnvironmentPath = configurableNode.SecondaryEnvironmentPath;
			PopulateInlineActions(nodeVm, configurableNode);
			nodeVm.ActionText = GetInteractiveActionText(nodeVm);
			SetDiagnosticNodeInteraction(nodeVm, true);
			return;
		}

		if (nodeVm.HasActions)
		{
			nodeVm.Actions.Clear();
			nodeVm.NotifyActionsChanged();
			nodeVm.ActionText = GetRestingActionText(nodeVm);
			SetDiagnosticNodeInteraction(nodeVm, false);
			return;
		}

		PopulateInlineActions(nodeVm, configurableNode);
		nodeVm.ActionText = GetInteractiveActionText(nodeVm);
		SetDiagnosticNodeInteraction(nodeVm, true);
	}

	private void OnDiagnosticEditHovered(object? sender, PointerEventArgs e)
	{
		if (sender is Label label)
		{
			label.TextColor = DiagnosticActionDefaultTextColor;
		}
	}

	private void OnDiagnosticEditUnhovered(object? sender, PointerEventArgs e)
	{
		if (sender is Label label)
		{
			label.TextColor = ConsoleAccentColor;
		}
	}

	private async void OnDiagnosticRetryTapped(object? sender, TappedEventArgs e)
	{
		if (_isDiagnosticActionRunning) return;
		if (sender is not View { BindingContext: DiagnosticNodeViewModel nodeVm }) return;
		if (!nodeVm.CanRetry) return;
		if (nodeVm.Node is not IConfigurableDiagnosticNode configurableNode) return;

		if (configurableNode.AvailableOptions.Count == 0)
		{
			await configurableNode.ProbeEnvironmentAsync(_repairCts?.Token ?? CancellationToken.None);
		}

		nodeVm.EnvironmentDetails = configurableNode.EnvironmentDetails;
		nodeVm.EnvironmentPath = configurableNode.EnvironmentPath;
		nodeVm.SecondaryEnvironmentPath = configurableNode.SecondaryEnvironmentPath;
		PopulateInlineActions(nodeVm, configurableNode);
		SetDiagnosticNodeInteraction(nodeVm, true);

	}

	private void OnDiagnosticRetryHovered(object? sender, PointerEventArgs e)
	{
		if (sender is Border border)
		{
			border.BackgroundColor = DiagnosticActionDefaultHoverColor;
		}
	}

	private void OnDiagnosticRetryUnhovered(object? sender, PointerEventArgs e)
	{
		if (sender is Border border)
		{
			border.BackgroundColor = DiagnosticActionDefaultNormalColor;
		}
	}

	private void OnDiagnosticCancelTapped(object? sender, TappedEventArgs e)
	{
		if (sender is not View { BindingContext: DiagnosticNodeViewModel nodeVm } || !nodeVm.CanCancel)
		{
			return;
		}

		CancellationTokenSource? actionCts = _diagnosticActionCts;
		if (actionCts is null || actionCts.IsCancellationRequested)
		{
			NexusLog.Warning("[PRODUCT_SETUP] Download cancellation was requested without an active download token.");
			return;
		}

		nodeVm.CanCancel = false;
		nodeVm.IsCanceling = true;
		nodeVm.WorkingHint = nodeVm.CancellationWorkingHint;
		NexusLog.Info("[PRODUCT_SETUP] Cancelling the active diagnostic action.");
		actionCts.Cancel();
	}

	private async Task ScrollArchitectOptionalSectionIntoViewAsync()
	{
		if (_currentContext != ViewContext.Architect || ArchitectOptionalNodes.Count == 0)
		{
			return;
		}

		await InvokeOnMainThreadSafeAsync(async () =>
		{
			if (!ArchitectOptionalHeader.IsVisible) return;

			_focusedRequiredStep = null;
			_focusedDiagnosticNode = null;
			await RequestSetupScrollAsync(ArchitectOptionalHeader, SetupScrollPivot.Top, SetupScrollReason.OptionalSectionFocused, animated: true);
		});
	}

	private async Task ScrollVanguardOptionalSectionIntoViewAsync()
	{
		if (_currentContext is not (ViewContext.Vanguard or ViewContext.Repairing)
			|| VanguardOptionalNodes.Count == 0)
		{
			return;
		}

		await InvokeOnMainThreadSafeAsync(async () =>
		{
			if (!VanguardOptionalHeader.IsVisible) return;

			_focusedRequiredStep = null;
			_focusedDiagnosticNode = null;
			await RequestSetupScrollAsync(VanguardOptionalHeader, SetupScrollPivot.Top, SetupScrollReason.OptionalSectionFocused, animated: true);
		});
	}

	private Task RequestSetupScrollAsync(
		SetupDiagnosticStep step,
		SetupScrollPivot pivot,
		SetupScrollReason reason,
		bool animated)
		=> RequestSetupScrollAsync(step.ViewModel, pivot, reason, animated);

	private Task RequestSetupScrollAsync(
		DiagnosticNodeViewModel nodeVm,
		SetupScrollPivot pivot,
		SetupScrollReason reason,
		bool animated)
	{
		if (FindDiagnosticNodeContainer(nodeVm) is not { } target)
		{
			return Task.CompletedTask;
		}

		return RequestSetupScrollAsync(target, pivot, reason, animated);
	}

	private async Task NotifyDiagnosticItemUpdatedAsync(DiagnosticNodeViewModel nodeVm)
	{
		if (!IsOptionalTailDiagnosticNode(nodeVm))
		{
			return;
		}

		await RequestActiveSetupScrollToBottomAsync(SetupScrollReason.ItemUpdated, animated: true);
	}

	private Task RequestSetupScrollAsync(
		View target,
		SetupScrollPivot pivot,
		SetupScrollReason reason,
		bool animated)
	{
		return InvokeOnMainThreadSafeAsync(async () =>
		{
			try
			{
				if (_isDisposed) return;

				ScrollView? scrollView = GetActiveInitiationScrollView();
				if (scrollView == null) return;

				await WaitForLayoutPassAsync();
				if (_isDisposed) return;

				await ScrollTargetByPivotAsync(scrollView, target, pivot, animated);
			}
			catch (ObjectDisposedException)
			{
			}
			catch (InvalidOperationException)
			{
			}
			catch (Exception ex)
			{
				NexusLog.Exception(ex, $"[SETUP:UI] Setup scroll failed: {reason}");
			}
		});
	}

	private ScrollView? GetActiveInitiationScrollView()
		=> _currentContext switch
		{
			ViewContext.Vanguard or ViewContext.Repairing => VanguardInitiationScrollView,
			ViewContext.Architect => ArchitectInitiationScrollView,
			_ => null
		};

	private Task RequestActiveSetupScrollToBottomAsync(SetupScrollReason reason, bool animated)
	{
		return InvokeOnMainThreadSafeAsync(async () =>
		{
			try
			{
				if (_isDisposed) return;

				ScrollView? scrollView = GetActiveInitiationScrollView();
				if (scrollView == null) return;

				await WaitForLayoutPassAsync();
				if (_isDisposed) return;

				double maxScrollY = Math.Max(0, scrollView.ContentSize.Height - scrollView.Height);
				await ScrollToYAndSettleAsync(scrollView, maxScrollY, animated);
			}
			catch (ObjectDisposedException)
			{
			}
			catch (InvalidOperationException)
			{
			}
			catch (Exception ex)
			{
				NexusLog.Exception(ex, $"[SETUP:UI] Setup scroll-to-bottom failed: {reason}");
			}
		});
	}

	private bool IsOptionalTailDiagnosticNode(DiagnosticNodeViewModel nodeVm)
	{
		return _currentContext switch
		{
			ViewContext.Vanguard or ViewContext.Repairing => VanguardOptionalNodes.Count > 0
				&& ReferenceEquals(VanguardOptionalNodes[^1], nodeVm),
			ViewContext.Architect => ArchitectOptionalNodes.Count > 0
				&& ReferenceEquals(ArchitectOptionalNodes[^1], nodeVm),
			_ => false
		};
	}

	private static async Task ScrollTargetByPivotAsync(
		ScrollView scrollView,
		View target,
		SetupScrollPivot pivot,
		bool animated)
	{
		double? targetY = GetElementYRelativeToScrollContent(target, scrollView);
		if (targetY == null || scrollView.Height <= 0)
		{
			await scrollView.ScrollToAsync(
				target,
				pivot == SetupScrollPivot.Top ? ScrollToPosition.Start : ScrollToPosition.MakeVisible,
				animated);
			return;
		}

		double maxScrollY = Math.Max(0, scrollView.ContentSize.Height - scrollView.Height);
		double desiredY = pivot == SetupScrollPivot.Top
			? targetY.Value
			: targetY.Value + target.Height - scrollView.Height;
		double settledY = Math.Min(maxScrollY, Math.Max(0, desiredY));
		await ScrollToYAndSettleAsync(scrollView, settledY, animated);
	}

	private static double? GetElementYRelativeToScrollContent(View element, ScrollView scrollView)
	{
		if (scrollView.Content is not View content)
		{
			return null;
		}

		double y = 0;
		Element? current = element;
		while (current != null)
		{
			if (ReferenceEquals(current, content))
			{
				return y;
			}

			if (current is VisualElement visual)
			{
				y += visual.Y;
			}

			current = current.Parent;
		}

		return null;
	}

	private static void ResetInitiationScrollPosition(ScrollView scrollView)
	{
		if (!scrollView.IsVisible)
		{
			return;
		}

		try
		{
			_ = scrollView.ScrollToAsync(0, 0, false);
		}
		catch
		{
		}
	}

	private static async Task WaitForLayoutPassAsync()
	{
		await Task.Yield();
		await Task.Yield();
	}

	private static async Task ScrollToYAndSettleAsync(ScrollView scrollView, double targetY, bool animated)
	{
		const double ScrollPositionTolerance = 1;

		double maxScrollY = Math.Max(0, scrollView.ContentSize.Height - scrollView.Height);
		double settledY = Math.Min(maxScrollY, Math.Max(0, targetY));

		if (Math.Abs(scrollView.ScrollY - settledY) <= ScrollPositionTolerance)
		{
			return;
		}

		await scrollView.ScrollToAsync(0, settledY, animated);

		if (Math.Abs(scrollView.ScrollY - settledY) > ScrollPositionTolerance)
		{
			await scrollView.ScrollToAsync(0, settledY, false);
		}
	}

	private View? FindDiagnosticNodeContainer(DiagnosticNodeViewModel nodeVm)
	{
		Layout? list = _currentContext switch
		{
			ViewContext.Vanguard or ViewContext.Repairing when VanguardNodes.Contains(nodeVm) => DiagnosticNodesList,
			ViewContext.Vanguard or ViewContext.Repairing when VanguardOptionalNodes.Contains(nodeVm) => VanguardOptionalNodesList,
			ViewContext.Architect when ArchitectNodes.Contains(nodeVm) => ArchitectNodesList,
			ViewContext.Architect when ArchitectOptionalNodes.Contains(nodeVm) => ArchitectOptionalNodesList,
			_ => null
		};

		if (list == null)
		{
			return null;
		}

		return FindVisualChildren<View>(list).FirstOrDefault(view => ReferenceEquals(view.BindingContext, nodeVm));
	}

	private SetupDiagnosticStep? FindDiagnosticStep(DiagnosticNodeViewModel nodeVm)
	{
		return _vanguardRequiredSteps
			.Concat(_vanguardOptionalSteps)
			.Concat(_architectRequiredSteps)
			.Concat(_architectOptionalSteps)
			.FirstOrDefault(step => ReferenceEquals(step.ViewModel, nodeVm));
	}

	private static void MarkStepFromHealth(SetupDiagnosticStep? step, DiagnosticNodeViewModel vm)
	{
		if (step == null) return;

		step.State = vm.CurrentHealth switch
		{
			HealthState.Healthy => SetupDiagnosticStepState.Verified,
			HealthState.OptionalMissing => SetupDiagnosticStepState.Skipped,
			_ => SetupDiagnosticStepState.Failed
		};
	}

	private static bool CanReconfigureReadyNode(DiagnosticNodeViewModel nodeVm)
		=> nodeVm.CurrentHealth is HealthState.Healthy or HealthState.OptionalMissing
			&& nodeVm.Node is IConfigurableDiagnosticNode;

	private static bool ShouldKeepInlineActionsVisible(DiagnosticNodeViewModel nodeVm)
		=> nodeVm.Node is IConfigurableDiagnosticNode configurableNode
			&& configurableNode.KeepInlineActionsVisibleAfterCompletion
			&& nodeVm.CurrentHealth is (HealthState.Healthy or HealthState.OptionalMissing);

	private static bool ShouldStartOptionalNodeCollapsed(DiagnosticNodeViewModel nodeVm)
		=> nodeVm.Node is PythonEnvironmentDiagnosticNode;

	private static string GetRestingActionText(DiagnosticNodeViewModel nodeVm)
	{
		if (nodeVm.CurrentHealth == HealthState.OptionalMissing)
		{
			return nodeVm.Node is IOptionalConfigurableDiagnosticNode
				? LocalizationManager.Text("setup.status.setup")
				: LocalizationManager.Text("setup.status.optional");
		}

		return LocalizationManager.Text("setup.status.ready");
	}

	private static string GetInteractiveActionText(DiagnosticNodeViewModel nodeVm)
		=> nodeVm.Node is IOptionalConfigurableDiagnosticNode
			? GetRestingActionText(nodeVm)
			: nodeVm.HasActions
				? LocalizationManager.Text("setup.status.config")
				: LocalizationManager.Text("setup.status.edit");

	private static void SetDiagnosticNodeInteraction(DiagnosticNodeViewModel nodeVm, bool isActive)
	{
		nodeVm.HighlightBackground = Colors.Transparent;
		nodeVm.InteractionOverlayOpacity = isActive ? 1.0 : 0.0;
	}

	private void SetDiagnosticActionNavigationBlocked(bool isBlocked)
	{
		bool isInteractionBlocked = isBlocked || _isInitiationSequenceRunning;
		SetInitiationUserScrollBlocked(isInteractionBlocked || IsAnyDiagnosticStepWorking());
		UpdateBackButtonAvailability();

		if (isInteractionBlocked)
		{
			PrimaryActionButton.IsEnabled = false;
			PrimaryActionButton.InputTransparent = true;
			PrimaryActionButton.Opacity = 0.35;
			StopPrimaryActionReadyPulse();
			return;
		}

		EvaluateCurrentInitiationReadiness();
	}

	private void UpdateInitiationUserScrollBlock()
	{
		SetInitiationUserScrollBlocked(IsInitiationInteractionBlocked || IsAnyDiagnosticStepWorking());
	}

	private bool IsInitiationInteractionBlocked
		=> _isDiagnosticActionRunning || _isInitiationSequenceRunning;

	private void SetInitiationSequenceInteractionBlocked(bool isBlocked)
	{
		if (_isInitiationSequenceRunning == isBlocked)
		{
			return;
		}

		_isInitiationSequenceRunning = isBlocked;
		SetDiagnosticActionNavigationBlocked(_isDiagnosticActionRunning);
	}

	private bool IsAnyDiagnosticStepWorking()
	{
		return _vanguardRequiredSteps
			.Concat(_vanguardOptionalSteps)
			.Concat(_architectRequiredSteps)
			.Concat(_architectOptionalSteps)
			.Any(step => step.State == SetupDiagnosticStepState.Working);
	}

	private void SetInitiationUserScrollBlocked(bool isBlocked)
	{
		if (_isInitiationUserScrollBlocked == isBlocked)
		{
			return;
		}

		_isInitiationUserScrollBlocked = isBlocked;
#if WINDOWS
		if (isBlocked)
		{
			_isNativeInitiationScrollDragging = false;
			_nativeDraggedInitiationScrollViewer = null;
			_ = ReturnNativeInitiationOverscrollAsync(_nativeDraggedInitiationScrollContent);
			_nativeDraggedInitiationScrollContent = null;
		}

		SetNativeInitiationScrollMode(VanguardInitiationScrollView, isBlocked);
		SetNativeInitiationScrollMode(ArchitectInitiationScrollView, isBlocked);
#endif
	}

#if WINDOWS
	private static void SetNativeInitiationScrollMode(ScrollView scrollView, bool isBlocked)
	{
		if (scrollView.Handler?.PlatformView is not Microsoft.UI.Xaml.Controls.ScrollViewer nativeScrollViewer)
		{
			return;
		}

		nativeScrollViewer.VerticalScrollMode = isBlocked
			? Microsoft.UI.Xaml.Controls.ScrollMode.Disabled
			: Microsoft.UI.Xaml.Controls.ScrollMode.Enabled;
	}
#endif

	private static (Color Normal, Color Hover, Color Text) GetDiagnosticActionColors(string actionId, bool isRecommended)
	{
		if (actionId.Contains("delete", StringComparison.OrdinalIgnoreCase)
			|| actionId.Contains("remove", StringComparison.OrdinalIgnoreCase))
		{
			return (DiagnosticActionDeleteNormalColor, DiagnosticActionDeleteHoverColor, DiagnosticActionDeleteTextColor);
		}

		return isRecommended
			? (ConsoleBootNormalColor, ConsoleBootHoverColor, ConsoleAccentColor)
			: (DiagnosticActionDefaultNormalColor, DiagnosticActionDefaultHoverColor, DiagnosticActionDefaultTextColor);
	}

	private void OnPathHovered(object? sender, PointerEventArgs e)
	{
		if (sender is Label label)
		{
			label.TextColor = DiagnosticActionDefaultTextColor;
		}
	}

	private void OnPathUnhovered(object? sender, PointerEventArgs e)
	{
		if (sender is Label label)
		{
			label.TextColor = ConsoleAccentColor;
		}
	}

	private async Task ExecuteNodeActionAsync(DiagnosticNodeViewModel vm, IConfigurableDiagnosticNode confNode, string actionId)
	{
		if (_isDiagnosticActionRunning)
		{
			return;
		}

		SetupDiagnosticStep? step = FindDiagnosticStep(vm);
		DiagnosticOption? option = confNode.AvailableOptions.FirstOrDefault(candidate => candidate.Id == actionId);
		DiagnosticActionOutcome outcome = new(DiagnosticActionOutcomeKind.AwaitingUserChoice);
		CancellationTokenSource? actionCts = null;
		_isDiagnosticActionRunning = true;
		SetDiagnosticActionNavigationBlocked(true);
		try
		{
			bool canCancelAction = option?.CanCancel == true;
			if (canCancelAction && !await ConfirmLargeModelDownloadAsync())
			{
				outcome = new DiagnosticActionOutcome(DiagnosticActionOutcomeKind.AwaitingUserChoice);
				return;
			}

			BeginDiagnosticAction(vm, option, step);
			if (canCancelAction)
			{
				actionCts = CancellationTokenSource.CreateLinkedTokenSource(_repairCts?.Token ?? CancellationToken.None);
				_diagnosticActionCts = actionCts;
			}

			await YieldDiagnosticWorkingStateAsync();
			outcome = await RunDiagnosticActionCoreAsync(vm, confNode, option, actionId, step, actionCts?.Token ?? _repairCts?.Token ?? CancellationToken.None);
			ApplyDiagnosticActionOutcome(vm, confNode, step, outcome);
		}
		catch (Exception ex)
		{
			NexusLog.Exception(ex, $"[PRODUCT_SETUP] Diagnostic action failed. node={confNode.NodeId}, action={actionId}");
			outcome = new DiagnosticActionOutcome(DiagnosticActionOutcomeKind.Failed, ex.Message);
			ApplyDiagnosticActionOutcome(vm, confNode, step, outcome);
		}
		finally
		{
			if (ReferenceEquals(_diagnosticActionCts, actionCts))
			{
				_diagnosticActionCts = null;
			}

			actionCts?.Dispose();
			vm.CanCancel = false;
			vm.IsCanceling = false;
			vm.ShowProgress = false;
			vm.IsLoading = false;
			vm.WorkingHint = string.Empty;
			vm.CancellationWorkingHint = string.Empty;
			vm.CancellationResultDetails = string.Empty;
			_isDiagnosticActionRunning = false;
			SetDiagnosticActionNavigationBlocked(false);
			if (outcome.Kind is DiagnosticActionOutcomeKind.AwaitingUserChoice or DiagnosticActionOutcomeKind.Cancelled)
			{
				PopulateInlineActions(vm, confNode);
				vm.ActionText = GetInteractiveActionText(vm);
				SetDiagnosticNodeInteraction(vm, true);
			}

			if (outcome.RequestTailScroll)
			{
				await NotifyDiagnosticItemUpdatedAsync(vm);
			}
		}
	}

	private void BeginDiagnosticAction(
		DiagnosticNodeViewModel vm,
		DiagnosticOption? option,
		SetupDiagnosticStep? step)
	{
		vm.WorkingHint = option?.WorkingHint ?? string.Empty;
		vm.IsLoading = true;
		vm.CanCancel = option?.CanCancel == true;
		vm.IsCanceling = false;
		vm.CancellationWorkingHint = option?.CancellationWorkingHint ?? string.Empty;
		vm.CancellationResultDetails = option?.CancellationResultDetails ?? string.Empty;
		vm.ShowProgress = false;
		vm.ProgressValue = 0;
		if (step != null)
		{
			step.State = SetupDiagnosticStepState.Working;
		}

		SetDiagnosticNodeInteraction(vm, false);
		vm.Actions.Clear();
		vm.NotifyActionsChanged();
	}

	private async Task<DiagnosticActionOutcome> RunDiagnosticActionCoreAsync(
		DiagnosticNodeViewModel vm,
		IConfigurableDiagnosticNode node,
		DiagnosticOption? option,
		string actionId,
		SetupDiagnosticStep? step,
		CancellationToken cancellationToken)
	{
		DiagnosticActionCompletionPolicy completionPolicy = option?.CompletionPolicy
			?? DiagnosticActionCompletionPolicy.VerifyHealth;
		DiagnosticActionOutcome? selectionOutcome = await ApplyDiagnosticActionSelectionAsync(vm, node, actionId);
		if (selectionOutcome != null)
		{
			return selectionOutcome;
		}

		vm.EnvironmentDetails = node.EnvironmentDetails;
		vm.EnvironmentPath = node.EnvironmentPath;
		vm.SecondaryEnvironmentPath = node.SecondaryEnvironmentPath;
		if (!ShouldRecoverDiagnosticAction(option))
		{
			// Configuration-only actions still update the optional card and its scroll position.
			return await CreateCompletedDiagnosticActionOutcomeAsync(vm, node, completionPolicy, notifyItemUpdated: true);
		}

		if (step != null)
		{
			await RequestDiagnosticScrollAsync(step, SetupScrollReason.WorkStarted);
		}

		vm.ShowProgress = true;
		RecoveryResult recoveryResult;
		try
		{
			recoveryResult = await RecoverDiagnosticNodeAsync(vm, node, option, cancellationToken);
		}
		catch (OperationCanceledException)
		{
			return new DiagnosticActionOutcome(
				DiagnosticActionOutcomeKind.Cancelled,
				string.IsNullOrWhiteSpace(vm.CancellationResultDetails)
					? node.EnvironmentDetails
					: vm.CancellationResultDetails);
		}

		if (!recoveryResult.IsSuccess)
		{
			return new DiagnosticActionOutcome(DiagnosticActionOutcomeKind.Failed, recoveryResult.Message);
		}

		return await CreateCompletedDiagnosticActionOutcomeAsync(vm, node, completionPolicy, notifyItemUpdated: true);
	}

	private async Task<DiagnosticActionOutcome?> ApplyDiagnosticActionSelectionAsync(
		DiagnosticNodeViewModel vm,
		IConfigurableDiagnosticNode node,
		string actionId)
	{
		if (node is IFolderSelectionDiagnosticNode folderNode && folderNode.RequiresFolderSelection(actionId))
		{
			var folderResult = await NexusAppManager.Instance.Platform.FilePicker.PickFolderAsync(folderNode.FolderPickerTitle);
			if (!folderResult.IsSupported || !folderResult.IsSuccess || string.IsNullOrWhiteSpace(folderResult.Value))
			{
				return new DiagnosticActionOutcome(DiagnosticActionOutcomeKind.AwaitingUserChoice);
			}

			RecoveryResult selectionResult = folderNode.ApplySelectedFolder(actionId, folderResult.Value);
			if (!selectionResult.IsSuccess)
			{
				return new DiagnosticActionOutcome(
					DiagnosticActionOutcomeKind.AwaitingUserChoice,
					selectionResult.Message,
					HealthState.OptionalMissing);
			}

			return null;
		}

		node.SelectOption(actionId);
		if (node is not IExecutableSelectionDiagnosticNode executableNode
			|| !executableNode.RequiresExecutableSelection(actionId))
		{
			return null;
		}

#if WINDOWS
		var picker = new Windows.Storage.Pickers.FileOpenPicker
		{
			ViewMode = Windows.Storage.Pickers.PickerViewMode.List,
			SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.ComputerFolder
		};
		picker.FileTypeFilter.Add(".exe");

		var window = Microsoft.Maui.Controls.Application.Current?.Windows.FirstOrDefault()?.Handler.PlatformView as MauiWinUIWindow;
		if (window == null)
		{
			return new DiagnosticActionOutcome(DiagnosticActionOutcomeKind.AwaitingUserChoice);
		}

		WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(window));
		var file = await picker.PickSingleFileAsync();
		if (file == null)
		{
			return new DiagnosticActionOutcome(DiagnosticActionOutcomeKind.AwaitingUserChoice);
		}

		RecoveryResult executableResult = await executableNode.ApplySelectedExecutableAsync(actionId, file.Path, CancellationToken.None);
		if (!executableResult.IsSuccess)
		{
			return new DiagnosticActionOutcome(
				DiagnosticActionOutcomeKind.AwaitingUserChoice,
				executableResult.Message,
				HealthState.NeedsRecovery);
		}
#endif

		return null;
	}

	private async Task<RecoveryResult> RecoverDiagnosticNodeAsync(
		DiagnosticNodeViewModel vm,
		IConfigurableDiagnosticNode node,
		DiagnosticOption? option,
		CancellationToken cancellationToken)
	{
		var originalOnProgress = _appManager.ComfyInstall.OnProgress;
		_appManager.ComfyInstall.OnProgress = (progress, message) =>
		{
			UiThread.TryBeginInvoke(
				() => vm.UpdateProgressDisplay(progress, message),
				"PRODUCT_SETUP:RECOVERY_PROGRESS");
		};

		try
		{
			var progress = new Progress<double>(value => vm.ProgressValue = value);
			return option?.RequiresToolingLease == true
				? await _appManager.Tooling.RunToolingAsync(
					_ => node.RecoverAsync(progress, cancellationToken),
					cancellationToken)
				: await node.RecoverAsync(progress, cancellationToken);
		}
		finally
		{
			_appManager.ComfyInstall.OnProgress = originalOnProgress;
		}
	}

	private async Task<DiagnosticActionOutcome> CreateCompletedDiagnosticActionOutcomeAsync(
		DiagnosticNodeViewModel vm,
		IConfigurableDiagnosticNode node,
		DiagnosticActionCompletionPolicy completionPolicy,
		bool notifyItemUpdated)
	{
		HealthState health = completionPolicy switch
		{
			DiagnosticActionCompletionPolicy.AssumeHealthy => HealthState.Healthy,
			DiagnosticActionCompletionPolicy.AssumeOptionalMissing => HealthState.OptionalMissing,
			_ => await node.CheckHealthAsync(CancellationToken.None)
		};
		return new DiagnosticActionOutcome(
			DiagnosticActionOutcomeKind.Completed,
			node.EnvironmentDetails,
			health,
			notifyItemUpdated);
	}

	private void ApplyDiagnosticActionOutcome(
		DiagnosticNodeViewModel vm,
		IConfigurableDiagnosticNode node,
		SetupDiagnosticStep? step,
		DiagnosticActionOutcome outcome)
	{
		switch (outcome.Kind)
		{
			case DiagnosticActionOutcomeKind.Completed:
				vm.UpdateState(outcome.Health ?? HealthState.NeedsRecovery);
				PopulatePersistentInlineActionsIfNeeded(vm);
				vm.EnvironmentDetails = node.EnvironmentDetails;
				vm.EnvironmentPath = node.EnvironmentPath;
				vm.SecondaryEnvironmentPath = node.SecondaryEnvironmentPath;
				MarkStepFromHealth(step, vm);
				vm.SignalCompletionChanged();
				break;

			case DiagnosticActionOutcomeKind.AwaitingUserChoice:
				if (outcome.Health != null)
				{
					vm.UpdateState(outcome.Health.Value);
				}

				if (!string.IsNullOrWhiteSpace(outcome.Details))
				{
					vm.EnvironmentDetails = outcome.Details;
				}

				if (step != null)
				{
					step.State = SetupDiagnosticStepState.WaitingForUser;
				}
				break;

			case DiagnosticActionOutcomeKind.Cancelled:
				vm.UpdateState(HealthState.NeedsRecovery);
				vm.EnvironmentDetails = outcome.Details;
				if (step != null)
				{
					step.State = SetupDiagnosticStepState.WaitingForUser;
				}
				break;

			case DiagnosticActionOutcomeKind.Failed:
				vm.UpdateState(HealthState.CriticalError);
				vm.EnvironmentDetails = outcome.Details;
				if (step != null)
				{
					step.State = SetupDiagnosticStepState.Failed;
				}
				break;
		}

		DiagnosticNodesList.InputTransparent = false;
		VanguardOptionalNodesList.InputTransparent = false;
		EvaluateCurrentInitiationReadiness();
	}

	private static bool ShouldRecoverDiagnosticAction(DiagnosticOption? option)
		=> option?.RequiresRecovery ?? true;

	private static async Task YieldDiagnosticWorkingStateAsync()
	{
		await Task.Yield();
	}

	private async Task<bool> ConfirmLargeModelDownloadAsync()
	{
		var page = GetPromptPage();
		return page == null || await page.DisplayAlertAsync(
			LocalizationManager.Text("setup.base_model.download_confirm_title"),
			LocalizationManager.Text("setup.base_model.download_confirm_message"),
			LocalizationManager.Text("setup.base_model.download_confirm_accept"),
			LocalizationManager.Text("common.cancel"));
	}

	private void EvaluateOverallReadiness()
	{
		if (_vanguardRequiredSteps.Count == 0) return;

		UpdateVanguardRequiredStatus();
		bool allReady = _vanguardRequiredSteps.All(step => step.CountsAsReady);

		if (_currentContext == ViewContext.Vanguard || _currentContext == ViewContext.Repairing)
		{
			EnsureActionBottomBarReady();
			bool primaryEnabled = allReady && !IsInitiationInteractionBlocked;
			PrimaryActionButton.IsEnabled = primaryEnabled;
			PrimaryActionButton.InputTransparent = !primaryEnabled;
			PrimaryActionButton.Opacity = primaryEnabled ? 1.0 : 0.3;
			PrimaryActionLabel.Text = allReady
				? LocalizationManager.Text("common.next")
				: LocalizationManager.Text("setup.status.requirements_pending");

			if (primaryEnabled) StartPrimaryActionReadyPulse();
			else StopPrimaryActionReadyPulse();
		}
	}

	private void ShowActionBottomBar(string primaryText, bool primaryEnabled)
	{
		EnsureActionBottomBarReady(restoreOpacity: false);
		ActionBottomBar.Opacity = 0;
		PrimaryActionLabel.Text = primaryText;
		PrimaryActionButton.IsEnabled = primaryEnabled;
		PrimaryActionButton.InputTransparent = !primaryEnabled;
		PrimaryActionButton.Opacity = primaryEnabled ? 1 : 0.4;

		if (primaryEnabled) StartPrimaryActionReadyPulse();
		else StopPrimaryActionReadyPulse();

		// Reset Back button text to default
		var label = BackButton.Content as Label;
		if (label == null && BackButton.Content is Grid g)
		{
			label = g.Children.OfType<Label>().FirstOrDefault();
		}
		if (label != null) label.Text = "BACK";
	}

	private void EnsureActionBottomBarReady(bool restoreOpacity = true)
	{
		ActionBottomBar.IsVisible = true;
		ActionBottomBar.InputTransparent = false;
		if (restoreOpacity && ActionBottomBar.Opacity <= 0)
		{
			ActionBottomBar.Opacity = 1;
		}

		BackButton.IsVisible = true;
		UpdateBackButtonAvailability();
	}

	private void UpdateBackButtonAvailability()
	{
		bool isServerOnlineInConsole = ServerConsolePanel.IsVisible
			&& _consoleBootActionState == ConsoleBootActionState.Online;
		bool isBlocked = IsInitiationInteractionBlocked || _consoleBootActionState == ConsoleBootActionState.Booting;
		BackButton.IsVisible = !isServerOnlineInConsole;
		BackButton.IsEnabled = !isServerOnlineInConsole && !isBlocked;
		BackButton.InputTransparent = isServerOnlineInConsole || isBlocked;
		BackButton.Opacity = isBlocked ? 0.35 : 1.0;
	}

	private void StartPrimaryActionReadyPulse()
	{
		if (_isDisposed)
		{
			return;
		}

		PrimaryActionPulseSurface.Opacity = 1;
		_primaryActionReadyPulseClip.PlayLoop(CanRepeatPrimaryActionPulse);
	}

	private void StopPrimaryActionReadyPulse()
	{
		_primaryActionReadyPulseClip.Stop();
		PrimaryActionPulseSurface.Opacity = 0;
	}

	private bool CanRepeatPrimaryActionPulse()
		=> !_isDisposed
			&& IsVisible
			&& Handler is not null
			&& ActionBottomBar.IsVisible
			&& PrimaryActionButton.IsEnabled
			&& !IsInitiationInteractionBlocked;

	private static bool IsValidComfyPath(string? path)
		=> !string.IsNullOrWhiteSpace(path) && Directory.Exists(path) && File.Exists(System.IO.Path.Combine(path, "main.py"));

	// ------------------------------------------------------------
	// POINTER & STATE ENGINE
	// ------------------------------------------------------------
}
