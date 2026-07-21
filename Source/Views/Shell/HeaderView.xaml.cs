using System;
using System.Threading.Tasks;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;
using ComfyUI_Nexus.Diagnostics;
using ComfyUI_Nexus.Ui;
using System.Windows.Input;

namespace ComfyUI_Nexus.Views;

public partial class HeaderView : ContentView
{
	private const double LogoHoverGlowScale = 1.2;
	private const double LogoHiddenGlowScale = 0.5;
	private const double LogoHoverRotation = 90;
	private const int LogoSecretClickTarget = 5;
	private const uint LogoHoverGlowLength = 250;
	private const uint LogoHoverRotationLength = 300;
	private const uint LogoExitGlowLength = 200;
	private const uint LogoExitRotationLength = 250;
	private static Color GpuTotalVramTextColor => ResourceColor("HeaderGpuLabelColor", "#8fb9d8");
	private static Color UsageHighTextColor => ResourceColor("HeaderUsageHighTextColor", "#ffd4db");
	private static Color UsageMediumTextColor => ResourceColor("HeaderUsageMediumTextColor", "#ffe6b0");
	private static Color UsageNormalTextColor => NexusColors.TextPrimary;
	private int _logoSecretClickCount;
	private bool _isLogoSecretArmed;
#if WINDOWS
	private Microsoft.UI.Xaml.UIElement? _logoSecretPointerElement;
#endif
	internal event EventHandler? LogoClicked;
	internal event EventHandler? LogoFiveClicked;
	internal event EventHandler? GpuVisualSurfaceLoaded;

	public static readonly BindableProperty MainActionCommandProperty = CreateToolbarCommandProperty(nameof(MainActionCommand), (tray, command) => tray.MainActionCommand = command);
	public static readonly BindableProperty StopActionCommandProperty = CreateToolbarCommandProperty(nameof(StopActionCommand), (tray, command) => tray.StopActionCommand = command);
	public static readonly BindableProperty RunModeCommandProperty = CreateToolbarCommandProperty(nameof(RunModeCommand), (tray, command) => tray.RunModeCommand = command);
	public static readonly BindableProperty ViewQueueCommandProperty = CreateToolbarCommandProperty(nameof(ViewQueueCommand), (tray, command) => tray.ViewQueueCommand = command);
	public static readonly BindableProperty TogglePropertiesCommandProperty = CreateToolbarCommandProperty(nameof(TogglePropertiesCommand), (tray, command) => tray.TogglePropertiesCommand = command);
	public static readonly BindableProperty ShowManagerCommandProperty = CreateToolbarCommandProperty(nameof(ShowManagerCommand), (tray, command) => tray.ShowManagerCommand = command);
	public static readonly BindableProperty ShowFavoritesCommandProperty = CreateToolbarCommandProperty(nameof(ShowFavoritesCommand), (tray, command) => tray.ShowFavoritesCommand = command);
	public static readonly BindableProperty UnloadModelsCommandProperty = CreateToolbarCommandProperty(nameof(UnloadModelsCommand), (tray, command) => tray.UnloadModelsCommand = command);
	public static readonly BindableProperty FreeCacheCommandProperty = CreateToolbarCommandProperty(nameof(FreeCacheCommand), (tray, command) => tray.FreeCacheCommand = command);
	public static readonly BindableProperty ShareCommandProperty = CreateToolbarCommandProperty(nameof(ShareCommand), (tray, command) => tray.ShareCommand = command);
	public static readonly BindableProperty EnterAppModeCommandProperty = CreateToolbarCommandProperty(nameof(EnterAppModeCommand), (tray, command) => tray.EnterAppModeCommand = command);
	public static readonly BindableProperty MainMenuCommandProperty = CreateToolbarCommandProperty(nameof(MainMenuCommand), (tray, command) => tray.MainMenuCommand = command);
	public static readonly BindableProperty WorkflowActionsCommandProperty = CreateToolbarCommandProperty(nameof(WorkflowActionsCommand), (tray, command) => tray.WorkflowActionsCommand = command);

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

	public HeaderView()
	{
		InitializeComponent();
		GpuIdleEnergyCoreSurface.Loaded += OnGpuVisualSurfaceLoaded;
		GpuRunningEnergyCoreSurface.Loaded += OnGpuVisualSurfaceLoaded;
		GpuLoadFrameGaugeSurface.Loaded += OnGpuVisualSurfaceLoaded;
		VramFrameGaugeSurface.Loaded += OnGpuVisualSurfaceLoaded;
		CpuFrameGaugeSurface.Loaded += OnGpuVisualSurfaceLoaded;
		WireLogoSecretPointerHandler();
	}

	protected override void OnParentSet()
	{
		base.OnParentSet();
		if (Parent is null)
		{
			UnwireLogoSecretPointerHandler();
		}
	}

	private void OnGpuVisualSurfaceLoaded(object? sender, EventArgs e)
		=> GpuVisualSurfaceLoaded?.Invoke(this, EventArgs.Empty);

	private static BindableProperty CreateToolbarCommandProperty(string propertyName, Action<HeaderToolbarTrayView, ICommand?> apply)
		=> BindableProperty.Create(
			propertyName,
			typeof(ICommand),
			typeof(HeaderView),
			default(ICommand),
			propertyChanged: (bindable, _, newValue) =>
			{
				if (bindable is HeaderView { ToolbarTrayControl: { } tray })
				{
					apply(tray, (ICommand?)newValue);
				}
			});

	private void OnLogoTapped(object? sender, TappedEventArgs e)
	{
		if (_isLogoSecretArmed)
		{
			_isLogoSecretArmed = false;
			_logoSecretClickCount = 0;
			LogoFiveClicked?.Invoke(this, EventArgs.Empty);
			return;
		}

		LogoClicked?.Invoke(this, EventArgs.Empty);
	}

	internal void SetExecutionState(bool isRunning)
	{
		ToolbarTrayControl.SetExecutionState(isRunning);
	}

	internal bool IsExecutingState()
	{
		return ToolbarTrayControl.IsExecutingState();
	}

	internal void SetInstantQueueButtonStop(bool isStopButton)
	{
		ToolbarTrayControl.SetInstantQueueButtonStop(isStopButton);
	}

	internal void SetQueueCount(int count)
	{
		ToolbarTrayControl.SetQueueCount(count);
	}

	internal int GetQueueCount()
	{
		return ToolbarTrayControl.GetQueueCount();
	}

	internal void SetRunMode(string mode)
	{
		ToolbarTrayControl.SetRunMode(mode);
	}

	internal void SetViewQueueActive(bool isActive)
	{
		ToolbarTrayControl.SetViewQueueActive(isActive);
	}

	internal bool IsViewQueueActive()
	{
		return ToolbarTrayControl.IsViewQueueActive();
	}

	internal void DismissRunModeOptionsFromGlobalPointerRelease()
	{
		ToolbarTrayControl.DismissRunModeOptionsFromGlobalPointerRelease();
	}

	internal void InvalidateShellLayout()
	{
		ToolbarTrayControl.InvalidateShellLayout();
	}

	internal void ClearWorkflowSummary()
	{
		ActiveWorkflowNameLabel.Text = "No workflow selected";
		ActiveWorkflowBadgesStack.IsVisible = false;
		ActiveSavedStateLabel.Text = string.Empty;
		ActiveModifiedStateBadge.IsVisible = false;
	}

	internal void UpdateWorkflowSummary(string name, bool hasFile, bool modified, bool bookmarked)
	{
		ActiveWorkflowNameLabel.Text = name;
		ActiveWorkflowBadgesStack.IsVisible = true;

		ActiveSavedStateLabel.Text = hasFile ? "SAVED" : "UNSAVED";
		ActiveSavedStateLabel.TextColor = hasFile
			? ResourceColor("HeaderSavedTextColor", "#7fe7ff")
			: NexusColors.AccentText;
		ActiveSavedStateBadge.BackgroundColor = hasFile
			? ResourceColor("HeaderSavedSurfaceColor", "#0f1d2c")
			: ResourceColor("HeaderUnsavedSurfaceColor", "#15202b");
		ActiveSavedStateBadge.StrokeThickness = 0;
		ActiveSavedStateBadge.Opacity = bookmarked ? 1 : 0.92;
		ActiveModifiedStateBadge.IsVisible = modified;
	}

	internal void SetWorkflowStatusBarState(bool isVisible, bool inputTransparent, double opacity = 1)
	{
		WorkflowStatusBarBorder.IsVisible = isVisible;
		WorkflowStatusBarBorder.InputTransparent = inputTransparent;
		WorkflowStatusBarBorder.Opacity = opacity;
	}

	internal void SetLoadingHaloState(double opacity, double scale)
	{
		LoadingHalo.Opacity = opacity;
		LoadingHalo.Scale = scale;
	}

	internal Task FadeLoadingHaloAsync(double opacity, uint length)
	{
		return SafeAnimation.FadeToAsync(LoadingHalo, opacity, length, source: "Header.LoadingHalo");
	}

	internal void SetRightPaneOpacity(double opacity)
	{
		HeaderRightGrid.Opacity = opacity;
	}

	internal void SetLogoInputTransparent(bool inputTransparent)
	{
		LogoHostBorder.InputTransparent = inputTransparent;
	}

	internal double GetRootHeight()
	{
		return NativeHeaderGrid.Height;
	}

	internal void SetTabRowHeights(double height)
	{
		TabRowsGrid.HeightRequest = height;
		TabRowsGrid.Padding = Thickness.Zero;
		PrimaryTabsGrid.HeightRequest = height;
	}

	internal void SetTabRowsOpacity(double opacity)
	{
		PrimaryTabsGrid.Opacity = opacity;
	}

	internal void ResetTabSurface()
	{
		using var operation = XamlUnhandledExceptionDiagnostics.EnterUiOperation("Header.Tabs.Reset");
		SafeAnimation.CancelAnimations(PrimaryTabsGrid, "Header.Tabs.Reset");
		PrimaryTabsGrid.Children.Clear();
		PrimaryTabsGrid.ColumnDefinitions.Clear();
	}

	internal void RemoveTabSurfaceChild(View child)
	{
		using var operation = XamlUnhandledExceptionDiagnostics.EnterUiOperation("Header.Tabs.RemoveChild");
		PrimaryTabsGrid.Children.Remove(child);
	}

	internal void AddTabColumn(GridLength width)
	{
		PrimaryTabsGrid.ColumnDefinitions.Add(new ColumnDefinition(width));
	}

	internal void AddTabSurfaceChild(View child, int column)
	{
		using var operation = XamlUnhandledExceptionDiagnostics.EnterUiOperation("Header.Tabs.AddChild");
		Grid.SetColumn(child, column);
		PrimaryTabsGrid.Children.Add(child);
	}

	internal void InvalidateTabSurface()
	{
		using var operation = XamlUnhandledExceptionDiagnostics.EnterUiOperation("Header.Tabs.Invalidate");
		PrimaryTabsGrid.InvalidateMeasure();
		TabRowsGrid.InvalidateMeasure();
		Dispatcher.Dispatch(() =>
		{
			using var operation = XamlUnhandledExceptionDiagnostics.EnterUiOperation("Header.Tabs.Invalidate.Delayed");
			PrimaryTabsGrid.InvalidateMeasure();
			TabRowsGrid.InvalidateMeasure();
		});
	}

	internal void SetGpuVisibility(bool isVisible)
	{
		SystemStatusStack.IsVisible = isVisible;
	}

	internal void UpdateGpuSummary(
		string modelName,
		double loadPercent,
		double activePercent,
		double allocatedGiB,
		double cachedGiB,
		double totalGiB,
		bool allocatedOverflow,
		bool cachedOverflow,
		Color accent,
		Color accentSoft)
	{
		GpuStatusStack.IsVisible = true;
		GpuModelLabel.Text = modelName.ToUpperInvariant();
		GpuUtilizationLabel.Text = $"{Math.Round(loadPercent):0}%";
		GpuActiveVramLabel.Text = FormatVramGiB(allocatedGiB, allocatedOverflow);
		GpuCachedVramLabel.Text = $"/ {FormatVramGiB(totalGiB)}";
		GpuTotalVramLabel.Text = $"{FormatVramGiB(cachedGiB, cachedOverflow)} reserved";
		GpuUtilizationLabel.TextColor = GetUsageTextColor(loadPercent);
		GpuModelLabel.TextColor = accentSoft;
		GpuActiveVramLabel.TextColor = Colors.White;
		GpuCachedVramLabel.TextColor = accentSoft;
		GpuTotalVramLabel.TextColor = GpuTotalVramTextColor;
	}

	private static string FormatVramGiB(double value)
		=> $"{Math.Max(0, value):0.0}GB";

	private static string FormatVramGiB(double value, bool overflow)
		=> $"{FormatVramGiB(value)}{(overflow ? "+" : string.Empty)}";

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

	internal void UpdateSystemUsageSummary(double cpuPercent)
	{
		SystemStatusStack.IsVisible = true;
		CpuUsageValueLabel.Text = $"{Math.Round(cpuPercent):0}%";

		CpuUsageValueLabel.TextColor = GetUsageTextColor(cpuPercent);
	}

	internal Task AwaitSystemStatusLayoutAsync()
		=> NexusUiFrame.AwaitShellReadyAsync(SystemStatusStack, "HEADER:SYSTEM_STATUS");

	internal Image GpuIdleEnergyCoreImage => GpuIdleEnergyCoreSurface;
	internal Image GpuRunningEnergyCoreImage => GpuRunningEnergyCoreSurface;
	internal Image GpuLoadFrameGaugeImage => GpuLoadFrameGaugeSurface;
	internal Image VramFrameGaugeImage => VramFrameGaugeSurface;
	internal Image CpuFrameGaugeImage => CpuFrameGaugeSurface;

	internal void ShowGpuEnergyCore(bool isRunning)
	{
		GpuRunningEnergyCoreSurface.Opacity = isRunning ? 1 : 0;
		GpuIdleEnergyCoreSurface.Opacity = isRunning ? 0 : 1;
	}

	internal void HideGpuEnergyCores()
	{
		GpuRunningEnergyCoreSurface.Opacity = 0;
		GpuIdleEnergyCoreSurface.Opacity = 0;
	}

	private static Color GetUsageTextColor(double usagePercent)
	{
		if (usagePercent >= 85)
		{
			return UsageHighTextColor;
		}

		if (usagePercent >= 55)
		{
			return UsageMediumTextColor;
		}

		return UsageNormalTextColor;
	}

	private void OnLogoPointerEntered(object? sender, PointerEventArgs e)
	{
		using var operation = XamlUnhandledExceptionDiagnostics.EnterUiOperation("Header.Logo.HoverIn");
		_ = SafeAnimation.FadeToAsync(LogoGlow, 1, LogoHoverGlowLength, Easing.CubicOut, "Header.Logo");
		_ = SafeAnimation.ScaleToAsync(LogoGlow, LogoHoverGlowScale, LogoHoverGlowLength, Easing.CubicOut, "Header.Logo");
		_ = SafeAnimation.RotateToAsync(LogoImage, LogoHoverRotation, LogoHoverRotationLength, Easing.CubicOut, "Header.Logo");
	}

	private void RegisterLogoSecretRightClick()
	{
		if (_isLogoSecretArmed)
		{
			return;
		}

		_logoSecretClickCount++;
		if (_logoSecretClickCount < LogoSecretClickTarget)
		{
			return;
		}

		_isLogoSecretArmed = true;
	}

#if WINDOWS
	private void WireLogoSecretPointerHandler()
	{
		LogoHostBorder.HandlerChanged += OnLogoHostBorderHandlerChanged;
	}

	private void UnwireLogoSecretPointerHandler()
	{
		LogoHostBorder.HandlerChanged -= OnLogoHostBorderHandlerChanged;
		UnregisterLogoNativePointerHandler();
	}

	private void UnregisterLogoNativePointerHandler()
	{
		if (_logoSecretPointerElement is null)
		{
			return;
		}

		try
		{
			_logoSecretPointerElement.PointerPressed -= OnLogoNativePointerPressed;
		}
		catch (Exception ex) when (IsNativePointerShutdownSafe(ex))
		{
			NexusLog.Trace($"[Header.Logo] Native pointer unregister skipped during shutdown: {ex.Message}");
		}

		_logoSecretPointerElement = null;
	}

	private void OnLogoHostBorderHandlerChanged(object? sender, EventArgs e)
	{
		UnregisterLogoNativePointerHandler();

		if (LogoHostBorder.Handler?.PlatformView is Microsoft.UI.Xaml.UIElement element)
		{
			try
			{
				_logoSecretPointerElement = element;
				_logoSecretPointerElement.PointerPressed += OnLogoNativePointerPressed;
			}
			catch (Exception ex) when (IsNativePointerShutdownSafe(ex))
			{
				_logoSecretPointerElement = null;
				NexusLog.Trace($"[Header.Logo] Native pointer register skipped during shutdown: {ex.Message}");
			}
		}
	}

	private void OnLogoNativePointerPressed(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
	{
		try
		{
			if (sender is not Microsoft.UI.Xaml.UIElement element)
			{
				return;
			}

			var pointerPoint = e.GetCurrentPoint(element);
			if (!pointerPoint.Properties.IsRightButtonPressed)
			{
				return;
			}

			RegisterLogoSecretRightClick();
			e.Handled = true;
		}
		catch (Exception ex) when (IsNativePointerShutdownSafe(ex))
		{
			NexusLog.Trace($"[Header.Logo] Native pointer event skipped during shutdown: {ex.Message}");
		}
	}

	private static bool IsNativePointerShutdownSafe(Exception ex)
		=> ex is ObjectDisposedException
			or InvalidOperationException
			or System.Runtime.InteropServices.COMException;
#else
	private void WireLogoSecretPointerHandler()
	{
	}

	private void UnwireLogoSecretPointerHandler()
	{
	}
#endif

	private void OnLogoPointerExited(object? sender, PointerEventArgs e)
	{
		using var operation = XamlUnhandledExceptionDiagnostics.EnterUiOperation("Header.Logo.HoverOut");
		_ = SafeAnimation.FadeToAsync(LogoGlow, 0, LogoExitGlowLength, Easing.CubicIn, "Header.Logo");
		_ = SafeAnimation.ScaleToAsync(LogoGlow, LogoHiddenGlowScale, LogoExitGlowLength, Easing.CubicIn, "Header.Logo");
		_ = SafeAnimation.RotateToAsync(LogoImage, 0, LogoExitRotationLength, Easing.CubicIn, "Header.Logo");
	}

	internal void SetPropertiesActive(bool isActive)
	{
		ToolbarTrayControl.SetPropertiesActive(isActive);
	}

	internal void SetManagerActionsVisible(bool isVisible)
	{
		ToolbarTrayControl.SetManagerActionsVisible(isVisible);
	}

	internal void SetMainMenuAvailable(bool isAvailable)
	{
		ToolbarTrayControl.SetMainMenuAvailable(isAvailable);
	}
}
