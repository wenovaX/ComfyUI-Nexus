using ComfyUI_Nexus.Configuration;
using ComfyUI_Nexus.Diagnostics;
using ComfyUI_Nexus.Ui;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;
using System.Windows.Input;

namespace ComfyUI_Nexus.Views;

public partial class HeaderToolbarTrayView : ContentView
{
	private const double CommandDeckWidth = 308;
	private const double ToolbarSideColumnsFallbackWidth = 560;
	private const double ToolbarSideColumnsMinMeasuredWidth = 80;
	private const double ToolbarMeasuredSidePadding = 112;
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
	private static readonly Brush TransparentBrush = new SolidColorBrush(Colors.Transparent);
	public static readonly BindableProperty MainActionCommandProperty = BindableProperty.Create(nameof(MainActionCommand), typeof(ICommand), typeof(HeaderToolbarTrayView));
	public static readonly BindableProperty StopActionCommandProperty = BindableProperty.Create(nameof(StopActionCommand), typeof(ICommand), typeof(HeaderToolbarTrayView));
	public static readonly BindableProperty RunModeCommandProperty = BindableProperty.Create(nameof(RunModeCommand), typeof(ICommand), typeof(HeaderToolbarTrayView));
	public static readonly BindableProperty ViewQueueCommandProperty = BindableProperty.Create(nameof(ViewQueueCommand), typeof(ICommand), typeof(HeaderToolbarTrayView));
	public static readonly BindableProperty TogglePropertiesCommandProperty = BindableProperty.Create(nameof(TogglePropertiesCommand), typeof(ICommand), typeof(HeaderToolbarTrayView));
	public static readonly BindableProperty ShowManagerCommandProperty = BindableProperty.Create(nameof(ShowManagerCommand), typeof(ICommand), typeof(HeaderToolbarTrayView));
	public static readonly BindableProperty ShowFavoritesCommandProperty = BindableProperty.Create(nameof(ShowFavoritesCommand), typeof(ICommand), typeof(HeaderToolbarTrayView));
	public static readonly BindableProperty UnloadModelsCommandProperty = BindableProperty.Create(nameof(UnloadModelsCommand), typeof(ICommand), typeof(HeaderToolbarTrayView));
	public static readonly BindableProperty FreeCacheCommandProperty = BindableProperty.Create(nameof(FreeCacheCommand), typeof(ICommand), typeof(HeaderToolbarTrayView));
	public static readonly BindableProperty ShareCommandProperty = BindableProperty.Create(nameof(ShareCommand), typeof(ICommand), typeof(HeaderToolbarTrayView));
	public static readonly BindableProperty EnterAppModeCommandProperty = BindableProperty.Create(nameof(EnterAppModeCommand), typeof(ICommand), typeof(HeaderToolbarTrayView));
	public static readonly BindableProperty MainMenuCommandProperty = BindableProperty.Create(nameof(MainMenuCommand), typeof(ICommand), typeof(HeaderToolbarTrayView));
	public static readonly BindableProperty WorkflowActionsCommandProperty = BindableProperty.Create(nameof(WorkflowActionsCommand), typeof(ICommand), typeof(HeaderToolbarTrayView));

	private bool _isExecuting;
	private bool _isDraggingQueueCount;
	private bool _isPropertiesActive;
	private bool _isViewQueueActive;
	private bool _isCommandDeckPlacementUpdateQueued;
	private bool _isUnloaded;
	private double _panStartValue;
	private double _panAccumulator;
	private readonly Dictionary<ToolbarGlassState, Brush> _toolbarGlassBackgroundCache = [];
	private readonly Dictionary<bool, Brush> _viewQueueActiveBackgroundCache = [];
	private IDispatcherTimer? _queueAdjustHoldTimer;
	private int _queueAdjustHoldDelta;
	private int _queueAdjustRepeatCount;

	public ICommand? MainActionCommand
	{
		get => (ICommand?)GetValue(MainActionCommandProperty);
		set => SetValue(MainActionCommandProperty, value);
	}

	public ICommand? StopActionCommand
	{
		get => (ICommand?)GetValue(StopActionCommandProperty);
		set => SetValue(StopActionCommandProperty, value);
	}

	public ICommand? RunModeCommand
	{
		get => (ICommand?)GetValue(RunModeCommandProperty);
		set => SetValue(RunModeCommandProperty, value);
	}

	public ICommand? ViewQueueCommand
	{
		get => (ICommand?)GetValue(ViewQueueCommandProperty);
		set => SetValue(ViewQueueCommandProperty, value);
	}

	public ICommand? TogglePropertiesCommand
	{
		get => (ICommand?)GetValue(TogglePropertiesCommandProperty);
		set => SetValue(TogglePropertiesCommandProperty, value);
	}

	public ICommand? ShowManagerCommand
	{
		get => (ICommand?)GetValue(ShowManagerCommandProperty);
		set => SetValue(ShowManagerCommandProperty, value);
	}

	public ICommand? ShowFavoritesCommand
	{
		get => (ICommand?)GetValue(ShowFavoritesCommandProperty);
		set => SetValue(ShowFavoritesCommandProperty, value);
	}

	public ICommand? UnloadModelsCommand
	{
		get => (ICommand?)GetValue(UnloadModelsCommandProperty);
		set => SetValue(UnloadModelsCommandProperty, value);
	}

	public ICommand? FreeCacheCommand
	{
		get => (ICommand?)GetValue(FreeCacheCommandProperty);
		set => SetValue(FreeCacheCommandProperty, value);
	}

	public ICommand? ShareCommand
	{
		get => (ICommand?)GetValue(ShareCommandProperty);
		set => SetValue(ShareCommandProperty, value);
	}

	public ICommand? EnterAppModeCommand
	{
		get => (ICommand?)GetValue(EnterAppModeCommandProperty);
		set => SetValue(EnterAppModeCommandProperty, value);
	}

	public ICommand? MainMenuCommand
	{
		get => (ICommand?)GetValue(MainMenuCommandProperty);
		set => SetValue(MainMenuCommandProperty, value);
	}

	public ICommand? WorkflowActionsCommand
	{
		get => (ICommand?)GetValue(WorkflowActionsCommandProperty);
		set => SetValue(WorkflowActionsCommandProperty, value);
	}

	public ICommand GuardedMainMenuCommand { get; }

	public HeaderToolbarTrayView()
	{
		GuardedMainMenuCommand = new Command(ExecuteMainMenuCommand);
		InitializeComponent();
		_mainActionMotion = new NexusMotionController("header-toolbar-main-action", "HeaderToolbar.MainActionPulse", Dispatcher);
		RunModeOptionDeck.SelectionChanged += OnRunModeOptionDeckSelectionChanged;

		StopActionButton.IsEnabled = false;
		StopActionIcon.Opacity = StopActionDisabledOpacity;

		Loaded += OnToolbarLoaded;
		Unloaded += OnToolbarUnloaded;
		SizeChanged += OnToolbarLayoutInvalidated;
		ApplyToolbarButtonDefaults();
		ApplyRunMode(_currentRunMode);
		SetRunModeOptionsExpanded(false, animate: false);
		QueueCommandDeckPlacementUpdate();
	}

	private void OnToolbarLoaded(object? sender, EventArgs e)
	{
		_isUnloaded = false;
	}

	private void OnToolbarUnloaded(object? sender, EventArgs e)
	{
		_isUnloaded = true;
		StopQueueAdjustHold();
		_mainActionMotion.StopAll();
		StopMainActionPulse();
		ResetQueueCountDragState(null, hideGlow: false);
		CancelHeaderAnimations();
	}

	private void CancelHeaderAnimations()
	{
		foreach (VisualElement element in new VisualElement[]
		{
			MainActionIcon,
			MainActionIconHover,
			MainActionHoverGlow,
			MainActionPulseGlow,
			StopActionIcon,
			StopActionIconHover,
			StopActionHoverGlow,
			QueueCountHoverGlow,
			QueueIncrementHoverGlow,
			QueueDecrementHoverGlow,
			ViewQueueActiveSheen,
			ViewQueueHoverGlow,
		})
		{
			SafeAnimation.CancelAnimations(element, "HeaderToolbar.Unload");
		}
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

	internal void SetMainMenuAvailable(bool isAvailable)
	{
		MainThread.BeginInvokeOnMainThread(() =>
		{
			MainMenuButton.IsVisible = isAvailable;
			MainMenuButton.IsEnabled = isAvailable;
			MainMenuButton.InputTransparent = !isAvailable;
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

	private void ExecuteMainMenuCommand()
	{
		if (!MainMenuButton.IsEnabled || !MainMenuButton.IsVisible)
		{
			return;
		}

		if (MainMenuCommand?.CanExecute(null) != false)
		{
			MainMenuCommand?.Execute(null);
		}
	}

	private async void OnMainActionPointerEntered(object? sender, PointerEventArgs e)
	{
		if (_isUnloaded) return;

		using var operation = XamlUnhandledExceptionDiagnostics.EnterUiOperation("HeaderToolbar.MainAction.HoverIn");
		await Task.WhenAll(
			SafeAnimation.FadeToAsync(MainActionIcon, 0, MainActionHoverLength, Easing.CubicOut, "HeaderToolbar.MainAction"),
			SafeAnimation.FadeToAsync(MainActionIconHover, 1, MainActionHoverLength, Easing.CubicOut, "HeaderToolbar.MainAction"),
			SafeAnimation.FadeToAsync(MainActionHoverGlow, MainActionHoverGlowOpacity, MainActionHoverLength, Easing.CubicOut, "HeaderToolbar.MainAction"));
	}

	private async void OnMainActionPointerExited(object? sender, PointerEventArgs e)
	{
		if (_isUnloaded) return;

		using var operation = XamlUnhandledExceptionDiagnostics.EnterUiOperation("HeaderToolbar.MainAction.HoverOut");
		await Task.WhenAll(
			SafeAnimation.FadeToAsync(MainActionIcon, 1, MainActionHoverLength, Easing.CubicIn, "HeaderToolbar.MainAction"),
			SafeAnimation.FadeToAsync(MainActionIconHover, 0, MainActionHoverLength, Easing.CubicIn, "HeaderToolbar.MainAction"),
			SafeAnimation.FadeToAsync(MainActionHoverGlow, 0, MainActionHoverLength, Easing.CubicIn, "HeaderToolbar.MainAction"));
	}

	private async void OnStopActionPointerEntered(object? sender, PointerEventArgs e)
	{
		if (_isUnloaded || !StopActionButton.IsEnabled) return;

		using var operation = XamlUnhandledExceptionDiagnostics.EnterUiOperation("HeaderToolbar.StopAction.HoverIn");
		await Task.WhenAll(
			SafeAnimation.FadeToAsync(StopActionIcon, 0, MainActionHoverLength, Easing.CubicOut, "HeaderToolbar.StopAction"),
			SafeAnimation.FadeToAsync(StopActionIconHover, 1, MainActionHoverLength, Easing.CubicOut, "HeaderToolbar.StopAction"),
			SafeAnimation.FadeToAsync(StopActionHoverGlow, MainActionHoverGlowOpacity, MainActionHoverLength, Easing.CubicOut, "HeaderToolbar.StopAction"));
	}

	private async void OnStopActionPointerExited(object? sender, PointerEventArgs e)
	{
		if (_isUnloaded) return;

		double targetOpacity = StopActionButton.IsEnabled ? 1 : StopActionDisabledOpacity;
		using var operation = XamlUnhandledExceptionDiagnostics.EnterUiOperation("HeaderToolbar.StopAction.HoverOut");
		await Task.WhenAll(
			SafeAnimation.FadeToAsync(StopActionIcon, targetOpacity, MainActionHoverLength, Easing.CubicIn, "HeaderToolbar.StopAction"),
			SafeAnimation.FadeToAsync(StopActionIconHover, 0, MainActionHoverLength, Easing.CubicIn, "HeaderToolbar.StopAction"),
			SafeAnimation.FadeToAsync(StopActionHoverGlow, 0, MainActionHoverLength, Easing.CubicIn, "HeaderToolbar.StopAction"));
	}

	private void OnQueueButtonPointerEntered(object? sender, PointerEventArgs e)
	{
		if (_isUnloaded) return;

		if (sender is Border border && sender != ViewQueueButton)
		{
			_ = SafeAnimation.ScaleToAsync(border, QueueButtonHoverScale, QueueHoverLength, Easing.CubicOut, "HeaderToolbar.Queue");
		}

		if (sender == ViewQueueButton)
		{
			ApplyViewQueueGlassBackground(isHovered: true);
			_ = SafeAnimation.FadeToAsync(ViewQueueActiveSheen, _isViewQueueActive ? ViewQueueActiveHoverSheenOpacity : ViewQueueInactiveHoverSheenOpacity, QueueHoverLength, Easing.CubicOut, "HeaderToolbar.Queue");
			_ = SafeAnimation.FadeToAsync(ViewQueueHoverGlow, _isViewQueueActive ? ViewQueueActiveHoverGlowOpacity : ViewQueueInactiveHoverGlowOpacity, QueueHoverLength, Easing.CubicOut, "HeaderToolbar.Queue");
		}
		else if (sender == QueueIncrementButton)
		{
			_ = SafeAnimation.FadeToAsync(QueueIncrementHoverGlow, 1, QueueGlowShowLength, Easing.CubicOut, "HeaderToolbar.Queue");
		}
		else if (sender == QueueDecrementButton)
		{
			_ = SafeAnimation.FadeToAsync(QueueDecrementHoverGlow, 1, QueueGlowShowLength, Easing.CubicOut, "HeaderToolbar.Queue");
		}
	}

	private void OnQueueButtonPointerExited(object? sender, PointerEventArgs e)
	{
		if (_isUnloaded) return;

		if (sender is Border border && sender != ViewQueueButton)
		{
			_ = SafeAnimation.ScaleToAsync(border, 1.0, QueueHoverLength, Easing.CubicIn, "HeaderToolbar.Queue");
		}

		if (sender == ViewQueueButton)
		{
			ApplyViewQueueGlassBackground(isHovered: false);
			_ = SafeAnimation.FadeToAsync(ViewQueueActiveSheen, _isViewQueueActive ? ViewQueueActiveSheenOpacity : 0, ViewQueueHoverOutLength, Easing.CubicIn, "HeaderToolbar.Queue");
			_ = SafeAnimation.FadeToAsync(ViewQueueHoverGlow, _isViewQueueActive ? ViewQueueActiveGlowOpacity : 0, ViewQueueHoverOutLength, Easing.CubicIn, "HeaderToolbar.Queue");
		}
		else if (sender == QueueIncrementButton)
		{
			StopQueueAdjustHold();
			_ = SafeAnimation.FadeToAsync(QueueIncrementHoverGlow, 0, QueueGlowHideLength, Easing.CubicIn, "HeaderToolbar.Queue");
		}
		else if (sender == QueueDecrementButton)
		{
			StopQueueAdjustHold();
			_ = SafeAnimation.FadeToAsync(QueueDecrementHoverGlow, 0, QueueGlowHideLength, Easing.CubicIn, "HeaderToolbar.Queue");
		}
	}

	private void OnToolbarButtonPointerEntered(object? sender, PointerEventArgs e)
	{
		if (_isUnloaded) return;

		if (sender is not Border border)
		{
			return;
		}

		ApplyToolbarGlassBackground(border, ToolbarGlassState.Hover);
		border.Opacity = 1;
	}

	private void OnToolbarButtonPointerExited(object? sender, PointerEventArgs e)
	{
		if (_isUnloaded) return;

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
			? ResourceColor("ToolbarViewQueueActiveTextColor", "#e8fbff")
			: NexusColors.White;
		ApplyViewQueueGlassBackground(isHovered: false);
		_ = SafeAnimation.FadeToAsync(ViewQueueActiveSheen, _isViewQueueActive ? ViewQueueActiveSheenOpacity : 0, QueueGlowShowLength, Easing.CubicOut, "HeaderToolbar.Queue");
		ViewQueueButton.Opacity = 1;
		_ = SafeAnimation.FadeToAsync(ViewQueueHoverGlow, _isViewQueueActive ? ViewQueueActiveGlowOpacity : 0, QueueGlowShowLength, Easing.CubicOut, "HeaderToolbar.Queue");
	}

	private void ApplyViewQueueGlassBackground(bool isHovered)
	{
		if (!_isViewQueueActive)
		{
			ViewQueueButton.Background = TransparentBrush;
			ViewQueueActiveFill.Background = TransparentBrush;
			ViewQueueActiveFill.Opacity = 0;
			return;
		}

		ViewQueueButton.Background = TransparentBrush;
		ViewQueueActiveFill.Background = GetViewQueueActiveBackground(isHovered);
		ViewQueueActiveFill.Opacity = isHovered ? 1 : ViewQueueActiveFillOpacity;
	}

	private void ApplyToolbarGlassBackground(Border border, ToolbarGlassState state)
	{
		if (state == ToolbarGlassState.Normal && IsManagerActionButton(border))
		{
			border.Background = TransparentBrush;
			return;
		}

		border.Background = GetToolbarGlassBackground(state);
	}

	private Brush GetViewQueueActiveBackground(bool isHovered)
	{
		if (_viewQueueActiveBackgroundCache.TryGetValue(isHovered, out Brush? cached))
		{
			return cached;
		}

		(Color start, Color middle, Color end) = isHovered
			? (
				ResourceColor("ToolbarViewQueueHoverStartColor", "#2d587d"),
				ResourceColor("ToolbarViewQueueHoverMiddleColor", "#1b3955"),
				ResourceColor("ToolbarViewQueueHoverEndColor", "#0b1827"))
			: (
				ResourceColor("ToolbarViewQueueActiveStartColor", "#264c6d"),
				ResourceColor("ToolbarViewQueueActiveMiddleColor", "#17314a"),
				ResourceColor("ToolbarViewQueueActiveEndColor", "#091524"));

		Brush brush = CreateToolbarGradient(start, middle, end, 0.46f);
		_viewQueueActiveBackgroundCache[isHovered] = brush;
		return brush;
	}

	private Brush GetToolbarGlassBackground(ToolbarGlassState state)
	{
		if (_toolbarGlassBackgroundCache.TryGetValue(state, out Brush? cached))
		{
			return cached;
		}

		(Color start, Color middle, Color end) = state switch
		{
			ToolbarGlassState.Hover => (
				ResourceColor("ToolbarGlassHoverStartColor", "#24425f"),
				ResourceColor("ToolbarGlassHoverMiddleColor", "#17324c"),
				ResourceColor("ToolbarGlassHoverEndColor", "#0a1624")),
			ToolbarGlassState.Active => (
				ResourceColor("ToolbarGlassActiveStartColor", "#245f7e"),
				ResourceColor("ToolbarGlassActiveMiddleColor", "#164765"),
				ResourceColor("ToolbarGlassActiveEndColor", "#0a1c2b")),
			_ => (
				ResourceColor("ToolbarGlassNormalStartColor", "#172a3f"),
				ResourceColor("ToolbarGlassNormalMiddleColor", "#102135"),
				ResourceColor("ToolbarGlassNormalEndColor", "#07111d")),
		};

		Brush brush = CreateToolbarGradient(start, middle, end, 0.48f);
		_toolbarGlassBackgroundCache[state] = brush;
		return brush;
	}

	private static LinearGradientBrush CreateToolbarGradient(Color start, Color middle, Color end, float middleOffset)
	{
		return new LinearGradientBrush
		{
			StartPoint = Point.Zero,
			EndPoint = new Point(1, 1),
			GradientStops =
			[
				new GradientStop(start, 0),
				new GradientStop(middle, middleOffset),
				new GradientStop(end, 1),
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

	private static Color ResourceColor(string key, string fallback)
	{
		if (Application.Current?.Resources.TryGetValue(key, out object? value) == true)
		{
			return value switch
			{
				Color color => color,
				SolidColorBrush brush => brush.Color,
				_ => Color.FromArgb(fallback),
			};
		}

		return Color.FromArgb(fallback);
	}
}
