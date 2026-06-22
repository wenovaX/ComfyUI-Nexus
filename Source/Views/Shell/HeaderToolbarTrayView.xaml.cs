using ComfyUI_Nexus.Platform;
using ComfyUI_Nexus.Configuration;
using ComfyUI_Nexus.Ui;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;

namespace ComfyUI_Nexus.Views;

public partial class HeaderToolbarTrayView : ContentView
{
	private const double CommandDeckWidth = 308;
	private const double ToolbarSideColumnsWidth = 560;
	private const double CommandDeckComfortPadding = 12;
	private const double CommandDeckRaisedY = -1;
	private const double CommandDeckLoweredY = 26;
	private const double ToolbarNormalHeight = 30;
	private const double ToolbarLoweredHeight = 60;
	private const double QueuePanStepPixels = 12.0;
	private const double QueueButtonHoverScale = 1.03;
	private const double StopActionDisabledOpacity = 0.35;
	private const double MainActionHoverGlowOpacity = 0.35;
	private const double ViewQueueInactiveHoverSheenOpacity = 0.18;
	private const double ViewQueueActiveHoverSheenOpacity = 0.62;
	private const double ViewQueueInactiveHoverGlowOpacity = 0.88;
	private const double ViewQueueActiveHoverGlowOpacity = 1;
	private const double ViewQueueActiveSheenOpacity = 0.38;
	private const double ViewQueueActiveGlowOpacity = 0.68;
	private const double ViewQueueActiveFillOpacity = 0.96;
	private const int QueueCountMin = 1;
	private const int QueueCountMax = 100;
	private const int CommandDeckLayoutRetryDelayMs = 16;
	private const uint MainActionHoverLength = 150;
	private const uint QueueHoverLength = 150;
	private const uint QueueGlowShowLength = 120;
	private const uint QueueGlowHideLength = 140;
	private const uint ViewQueueHoverOutLength = 180;
	private const int QueueHoldInitialDelayMs = 360;
	private const int QueueHoldStartIntervalMs = 170;
	private const int QueueHoldMinIntervalMs = 42;
	private const int QueueHoldAccelerationStepMs = 12;
	internal event EventHandler? MainActionRequested;
	internal event EventHandler? StopActionRequested;
	internal event Action<string>? RunModeRequested;
	internal event EventHandler? ViewQueueRequested;
	internal event EventHandler? TogglePropertiesRequested;
	internal event EventHandler? ShowManagerRequested;
	internal event EventHandler? ShowFavoritesRequested;
	internal event EventHandler? UnloadModelsRequested;
	internal event EventHandler? FreeCacheRequested;
	internal event EventHandler? ShareRequested;
	internal event EventHandler? EnterAppModeRequested;
	internal event EventHandler? WorkflowActionsRequested;

	private bool _isExecuting;
	private bool _isDraggingQueueCount;
	private bool _isPropertiesActive;
	private bool _isViewQueueActive;
	private bool _isCommandDeckPlacementUpdateQueued;
	private double _panStartValue;
	private double _panAccumulator;
	private CancellationTokenSource? _queueAdjustHoldCts;

	public HeaderToolbarTrayView()
	{
		InitializeComponent();
		RunModeOptionDeck.SelectionChanged += OnRunModeOptionDeckSelectionChanged;

		StopActionButton.IsEnabled = false;
		StopActionIcon.Opacity = StopActionDisabledOpacity;

		SizeChanged += OnToolbarLayoutInvalidated;
		ApplyToolbarButtonDefaults();
		ApplyRunMode(_currentRunMode);
		SetRunModeOptionsExpanded(false, animate: false);
		QueueCommandDeckPlacementUpdate();
	}

	internal void SetExecutionState(bool isRunning)
	{
		_isExecuting = isRunning;

		MainThread.BeginInvokeOnMainThread(() =>
		{
			StopActionButton.IsEnabled = isRunning;
			StopActionIcon.Opacity = isRunning ? 1 : StopActionDisabledOpacity;

			if (!isRunning)
			{
				StopActionIconHover.Opacity = 0;
				StopActionHoverGlow.Opacity = 0;
			}
		});
	}

	internal bool IsExecutingState() => _isExecuting;

	internal void SetInstantQueueButtonStop(bool isStopButton)
	{
		MainThread.BeginInvokeOnMainThread(() =>
		{
			_isInstantQueueButtonStop = isStopButton;
			UpdateMainActionVisualState();
		});
	}

	internal void SetQueueCount(int count)
	{
		MainThread.BeginInvokeOnMainThread(() =>
		{
			int clamped = Math.Clamp(count, QueueCountMin, QueueCountMax);
			QueueCountLabel.Text = clamped.ToString();
			QueueCountEntry.Text = clamped.ToString();
		});
	}

	internal int GetQueueCount()
	{
		return int.TryParse(QueueCountLabel.Text, out int value) ? value : QueueCountMin;
	}

	internal void SetPropertiesActive(bool isActive)
	{
		_isPropertiesActive = isActive;
		MainThread.BeginInvokeOnMainThread(() =>
		{
			ApplyToolbarGlassBackground(PropertiesButton, isActive ? ToolbarGlassState.Active : ToolbarGlassState.Normal);
			PropertiesActiveAccent.IsVisible = isActive;
		});
	}

	internal void SetViewQueueActive(bool isActive)
	{
		_isViewQueueActive = isActive;
		MainThread.BeginInvokeOnMainThread(UpdateViewQueueVisualState);
	}

	internal bool IsViewQueueActive() => _isViewQueueActive;

	internal void SetManagerActionsVisible(bool isVisible)
	{
		MainThread.BeginInvokeOnMainThread(() =>
		{
			ManagerActionsGroup.IsVisible = isVisible;
		});
	}

	internal void SetRunMode(string mode)
	{
		MainThread.BeginInvokeOnMainThread(() =>
		{
			ApplyRunMode(mode);
			SetRunModeOptionsExpanded(false, animate: false);
		});
	}

	internal void InvalidateShellLayout()
	{
		QueueCommandDeckPlacementUpdate();
	}

	private void OnToolbarLayoutInvalidated(object? sender, EventArgs e)
	{
		QueueCommandDeckPlacementUpdate();
	}

	private void QueueCommandDeckPlacementUpdate()
	{
		if (_isCommandDeckPlacementUpdateQueued)
		{
			return;
		}

		_isCommandDeckPlacementUpdateQueued = true;
		Dispatcher.Dispatch(() =>
		{
			_isCommandDeckPlacementUpdateQueued = false;
			UpdateCommandDeckVerticalPlacement();
			Dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(CommandDeckLayoutRetryDelayMs), UpdateCommandDeckVerticalPlacement);
		});
	}

	private void UpdateCommandDeckVerticalPlacement()
	{
		double toolbarWidth = WorkflowToolBarStack.Width > 0 ? WorkflowToolBarStack.Width : Width;
		if (toolbarWidth <= 0)
		{
			return;
		}

		double centerWidth = toolbarWidth - ToolbarSideColumnsWidth;
		bool hasEnoughCenterSpace = centerWidth >= CommandDeckWidth + CommandDeckComfortPadding;
		CommandDeckHost.TranslationY = hasEnoughCenterSpace ? CommandDeckRaisedY : CommandDeckLoweredY;
		WorkflowToolBarStack.HeightRequest = hasEnoughCenterSpace ? ToolbarNormalHeight : ToolbarLoweredHeight;
	}

	private void OnQueueCountPanUpdated(object? sender, PanUpdatedEventArgs e)
	{
		switch (e.StatusType)
		{
			case GestureStatus.Started:
				_panStartValue = GetQueueCount();
				_panAccumulator = 0;
				break;
			case GestureStatus.Running:
				_panAccumulator = e.TotalX;
				int delta = (int)(_panAccumulator / QueuePanStepPixels);
				SetQueueCount(Math.Clamp((int)_panStartValue + delta, QueueCountMin, QueueCountMax));
				break;
			case GestureStatus.Completed:
			case GestureStatus.Canceled:
				ResetQueueCountDragState(sender as VisualElement);
				break;
		}
	}

	private void OnQueueAdjustPointerPressed(object? sender, PointerEventArgs e)
	{
		int delta = sender == QueueIncrementButton ? 1 : -1;
		_queueAdjustHoldCts?.Cancel();
		var cts = new CancellationTokenSource();
		_queueAdjustHoldCts = cts;
		ApplyQueueStep(delta);
		_ = RepeatQueueStepWhileHeldAsync(delta, cts);
	}

	private void OnQueueAdjustPointerReleased(object? sender, PointerEventArgs e)
	{
		StopQueueAdjustHold();
	}

	private async Task RepeatQueueStepWhileHeldAsync(int delta, CancellationTokenSource cancellationTokenSource)
	{
		var cancellationToken = cancellationTokenSource.Token;
		try
		{
			await Task.Delay(QueueHoldInitialDelayMs, cancellationToken);
			int repeatCount = 0;
			while (!cancellationToken.IsCancellationRequested)
			{
				ApplyQueueStep(delta);
				int intervalMs = Math.Max(
					QueueHoldMinIntervalMs,
					QueueHoldStartIntervalMs - (repeatCount * QueueHoldAccelerationStepMs));
				repeatCount++;
				await Task.Delay(intervalMs, cancellationToken);
			}
		}
		catch (OperationCanceledException)
		{
		}
		finally
		{
			if (ReferenceEquals(_queueAdjustHoldCts, cancellationTokenSource))
			{
				_queueAdjustHoldCts = null;
			}

			cancellationTokenSource.Dispose();
		}
	}

	private void StopQueueAdjustHold()
	{
		_queueAdjustHoldCts?.Cancel();
	}

	private void ApplyQueueStep(int delta)
	{
		int currentCount = GetQueueCount();
		int nextCount = Math.Clamp(currentCount + delta, QueueCountMin, QueueCountMax);
		if (nextCount == currentCount)
		{
			return;
		}

		SetQueueCount(nextCount);
	}

	private void OnMainActionClicked(object? sender, TappedEventArgs e) => MainActionRequested?.Invoke(this, EventArgs.Empty);
	private void OnStopActionClicked(object? sender, TappedEventArgs e) => StopActionRequested?.Invoke(this, EventArgs.Empty);
	private void OnViewQueueClicked(object? sender, TappedEventArgs e) => ViewQueueRequested?.Invoke(this, EventArgs.Empty);
	private void OnTogglePropertiesClicked(object? sender, TappedEventArgs e) => TogglePropertiesRequested?.Invoke(this, EventArgs.Empty);
	private void OnShowManagerClicked(object? sender, TappedEventArgs e) => ShowManagerRequested?.Invoke(this, EventArgs.Empty);
	private void OnShowFavoritesClicked(object? sender, TappedEventArgs e) => ShowFavoritesRequested?.Invoke(this, EventArgs.Empty);
	private void OnUnloadModelsClicked(object? sender, TappedEventArgs e) => UnloadModelsRequested?.Invoke(this, EventArgs.Empty);
	private void OnFreeCacheClicked(object? sender, TappedEventArgs e) => FreeCacheRequested?.Invoke(this, EventArgs.Empty);
	private void OnShareClicked(object? sender, TappedEventArgs e) => ShareRequested?.Invoke(this, EventArgs.Empty);
	private void OnEnterAppModeClicked(object? sender, TappedEventArgs e) => EnterAppModeRequested?.Invoke(this, EventArgs.Empty);
	private void OnWorkflowActionsClicked(object? sender, TappedEventArgs e) => WorkflowActionsRequested?.Invoke(this, EventArgs.Empty);

	private async void OnMainActionPointerEntered(object? sender, PointerEventArgs e)
	{
		await Task.WhenAll(
			MainActionIcon.FadeToAsync(0, MainActionHoverLength, Easing.CubicOut),
			MainActionIconHover.FadeToAsync(1, MainActionHoverLength, Easing.CubicOut),
			MainActionHoverGlow.FadeToAsync(MainActionHoverGlowOpacity, MainActionHoverLength, Easing.CubicOut));
	}

	private async void OnMainActionPointerExited(object? sender, PointerEventArgs e)
	{
		await Task.WhenAll(
			MainActionIcon.FadeToAsync(1, MainActionHoverLength, Easing.CubicIn),
			MainActionIconHover.FadeToAsync(0, MainActionHoverLength, Easing.CubicIn),
			MainActionHoverGlow.FadeToAsync(0, MainActionHoverLength, Easing.CubicIn));
	}

	private async void OnStopActionPointerEntered(object? sender, PointerEventArgs e)
	{
		if (!StopActionButton.IsEnabled) return;

		await Task.WhenAll(
			StopActionIcon.FadeToAsync(0, MainActionHoverLength, Easing.CubicOut),
			StopActionIconHover.FadeToAsync(1, MainActionHoverLength, Easing.CubicOut),
			StopActionHoverGlow.FadeToAsync(MainActionHoverGlowOpacity, MainActionHoverLength, Easing.CubicOut));
	}

	private async void OnStopActionPointerExited(object? sender, PointerEventArgs e)
	{
		double targetOpacity = StopActionButton.IsEnabled ? 1 : StopActionDisabledOpacity;
		await Task.WhenAll(
			StopActionIcon.FadeToAsync(targetOpacity, MainActionHoverLength, Easing.CubicIn),
			StopActionIconHover.FadeToAsync(0, MainActionHoverLength, Easing.CubicIn),
			StopActionHoverGlow.FadeToAsync(0, MainActionHoverLength, Easing.CubicIn));
	}

	private void OnQueueButtonPointerEntered(object? sender, PointerEventArgs e)
	{
		if (sender is Border border && sender != ViewQueueButton)
		{
			_ = border.ScaleToAsync(QueueButtonHoverScale, QueueHoverLength, Easing.CubicOut);
		}

		if (sender == ViewQueueButton)
		{
			ApplyViewQueueGlassBackground(isHovered: true);
			_ = ViewQueueActiveSheen.FadeToAsync(_isViewQueueActive ? ViewQueueActiveHoverSheenOpacity : ViewQueueInactiveHoverSheenOpacity, QueueHoverLength, Easing.CubicOut);
			_ = ViewQueueHoverGlow.FadeToAsync(_isViewQueueActive ? ViewQueueActiveHoverGlowOpacity : ViewQueueInactiveHoverGlowOpacity, QueueHoverLength, Easing.CubicOut);
		}
		else if (sender == QueueIncrementButton)
		{
			_ = QueueIncrementHoverGlow.FadeToAsync(1, QueueGlowShowLength, Easing.CubicOut);
		}
		else if (sender == QueueDecrementButton)
		{
			_ = QueueDecrementHoverGlow.FadeToAsync(1, QueueGlowShowLength, Easing.CubicOut);
		}
	}

	private void OnQueueButtonPointerExited(object? sender, PointerEventArgs e)
	{
		if (sender is Border border && sender != ViewQueueButton)
		{
			_ = border.ScaleToAsync(1.0, QueueHoverLength, Easing.CubicIn);
		}

		if (sender == ViewQueueButton)
		{
			ApplyViewQueueGlassBackground(isHovered: false);
			_ = ViewQueueActiveSheen.FadeToAsync(_isViewQueueActive ? ViewQueueActiveSheenOpacity : 0, ViewQueueHoverOutLength, Easing.CubicIn);
			_ = ViewQueueHoverGlow.FadeToAsync(_isViewQueueActive ? ViewQueueActiveGlowOpacity : 0, ViewQueueHoverOutLength, Easing.CubicIn);
		}
		else if (sender == QueueIncrementButton)
		{
			StopQueueAdjustHold();
			_ = QueueIncrementHoverGlow.FadeToAsync(0, QueueGlowHideLength, Easing.CubicIn);
		}
		else if (sender == QueueDecrementButton)
		{
			StopQueueAdjustHold();
			_ = QueueDecrementHoverGlow.FadeToAsync(0, QueueGlowHideLength, Easing.CubicIn);
		}
	}

	private void OnQueueCountTextChanged(object? sender, TextChangedEventArgs e)
	{
		if (int.TryParse(e.NewTextValue, out int count) && count is >= QueueCountMin and <= QueueCountMax)
		{
			QueueCountLabel.Text = count.ToString();
		}
	}

	private void OnQueueCountPointerPressed(object? sender, PointerEventArgs e)
	{
		_isDraggingQueueCount = true;
		PlatformManager.Current.Cursor.SetCursor(sender as VisualElement, NexusCursorShape.SizeWestEast);
	}

	private void OnQueueCountPointerReleased(object? sender, PointerEventArgs e)
	{
		ResetQueueCountDragState(sender as VisualElement, hideGlow: false);
	}

	private void OnQueueCountPointerEntered(object? sender, PointerEventArgs e)
	{
		_ = QueueCountHoverGlow.FadeToAsync(1, QueueHoverLength, Easing.CubicOut);
		PlatformManager.Current.Cursor.SetCursor(sender as VisualElement, NexusCursorShape.SizeWestEast);
	}

	private void OnQueueCountPointerExited(object? sender, PointerEventArgs e)
	{
		if (_isDraggingQueueCount) return;

		_ = QueueCountHoverGlow.FadeToAsync(0, ViewQueueHoverOutLength, Easing.CubicIn);
		PlatformManager.Current.Cursor.SetCursor(sender as VisualElement, NexusCursorShape.Arrow);
	}

	private void ResetQueueCountDragState(VisualElement? target, bool hideGlow = true)
	{
		_isDraggingQueueCount = false;
		PlatformManager.Current.Cursor.SetCursor(target, NexusCursorShape.Arrow);

		if (hideGlow)
		{
			_ = QueueCountHoverGlow.FadeToAsync(0, ViewQueueHoverOutLength, Easing.CubicIn);
		}
	}

	private void OnToolbarButtonPointerEntered(object? sender, PointerEventArgs e)
	{
		if (sender is not Border border)
		{
			return;
		}

		ApplyToolbarGlassBackground(border, ToolbarGlassState.Hover);
		border.Opacity = 1;
	}

	private void OnToolbarButtonPointerExited(object? sender, PointerEventArgs e)
	{
		if (sender is not Border border)
		{
			return;
		}

		if (IsManagerActionButton(border))
		{
			ApplyToolbarGlassBackground(border, ToolbarGlassState.Normal);
			border.Opacity = 1;
			return;
		}

		if (border == PropertiesButton)
		{
			ApplyToolbarGlassBackground(border, _isPropertiesActive ? ToolbarGlassState.Active : ToolbarGlassState.Normal);
			PropertiesActiveAccent.IsVisible = _isPropertiesActive;
			border.Opacity = 1;
			return;
		}

		ApplyToolbarGlassBackground(border, ToolbarGlassState.Normal);
		border.Opacity = 1;
	}

	private enum ToolbarGlassState
	{
		Normal,
		Hover,
		Active,
	}

	private void ApplyToolbarButtonDefaults()
	{
		foreach (Border button in new[]
		{
			ManagerButton,
			FavoritesButton,
			UnloadModelsButton,
			FreeCacheButton,
			ShareButton,
			PropertiesButton,
		})
		{
			ApplyToolbarGlassBackground(button, ToolbarGlassState.Normal);
		}

		UpdateViewQueueVisualState();
	}

	private void UpdateViewQueueVisualState()
	{
		ViewQueueIcon.Source = _isViewQueueActive ? "jobs_nexus_active.png" : "jobs_nexus.png";
		ViewQueueLabel.TextColor = _isViewQueueActive
			? Color.FromArgb("#e8fbff")
			: NexusColors.White;
		ApplyViewQueueGlassBackground(isHovered: false);
		_ = ViewQueueActiveSheen.FadeToAsync(_isViewQueueActive ? ViewQueueActiveSheenOpacity : 0, QueueGlowShowLength, Easing.CubicOut);
		ViewQueueButton.Opacity = 1;
		_ = ViewQueueHoverGlow.FadeToAsync(_isViewQueueActive ? ViewQueueActiveGlowOpacity : 0, QueueGlowShowLength, Easing.CubicOut);
	}

	private void ApplyViewQueueGlassBackground(bool isHovered)
	{
		if (!_isViewQueueActive)
		{
			ViewQueueButton.Background = new SolidColorBrush(Colors.Transparent);
			ViewQueueActiveFill.Background = new SolidColorBrush(Colors.Transparent);
			ViewQueueActiveFill.Opacity = 0;
			return;
		}

		(string start, string middle, string end) = isHovered
			? ("#2d587d", "#1b3955", "#0b1827")
			: ("#264c6d", "#17314a", "#091524");

		var backgroundBrush = new LinearGradientBrush
		{
			StartPoint = Point.Zero,
			EndPoint = new Point(1, 1),
			GradientStops =
			[
				new GradientStop(Color.FromArgb(start), 0),
				new GradientStop(Color.FromArgb(middle), 0.46f),
				new GradientStop(Color.FromArgb(end), 1),
			],
		};

		ViewQueueButton.Background = new SolidColorBrush(Colors.Transparent);
		ViewQueueActiveFill.Background = backgroundBrush;
		ViewQueueActiveFill.Opacity = isHovered ? 1 : ViewQueueActiveFillOpacity;
	}

	private void ApplyToolbarGlassBackground(Border border, ToolbarGlassState state)
	{
		if (state == ToolbarGlassState.Normal && IsManagerActionButton(border))
		{
			border.Background = new SolidColorBrush(Colors.Transparent);
			return;
		}

		(string start, string middle, string end) = state switch
		{
			ToolbarGlassState.Hover => ("#24425f", "#17324c", "#0a1624"),
			ToolbarGlassState.Active => ("#245f7e", "#164765", "#0a1c2b"),
			_ => ("#172a3f", "#102135", "#07111d"),
		};

		border.Background = new LinearGradientBrush
		{
			StartPoint = Point.Zero,
			EndPoint = new Point(1, 1),
			GradientStops =
			[
				new GradientStop(Color.FromArgb(start), 0),
				new GradientStop(Color.FromArgb(middle), 0.48f),
				new GradientStop(Color.FromArgb(end), 1),
			],
		};
	}

	private bool IsManagerActionButton(Border border)
	{
		return border == ManagerButton ||
			border == FavoritesButton ||
			border == UnloadModelsButton ||
			border == FreeCacheButton ||
			border == ShareButton;
	}
}
