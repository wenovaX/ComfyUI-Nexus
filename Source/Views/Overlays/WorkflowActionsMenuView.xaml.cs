namespace ComfyUI_Nexus.Views.Overlays;

using ComfyUI_Nexus.Ui;
using ComfyUI_Nexus.Ui.Popups;
using Microsoft.Maui.Controls.Shapes;

public partial class WorkflowActionsMenuView : ContentView, INexusPopupSurface
{
	private const double HiddenMenuOffsetY = -6;
	private const double HideMenuOffsetY = -4;
	private const double HiddenMenuScaleY = 0.92;
	private const double HideMenuScaleY = 0.96;
	private const double SeparatorHeight = 1;
	private const double SeparatorOpacity = 0.65;
	private const double ActionRowHeight = 23;
	private const double ActionRowCornerRadius = 7;
	private const double AccentWidth = 3;
	private const double AccentHeight = 10;
	private const double AccentCornerRadius = 1.5;
	private const double EnabledAccentOpacity = 0.92;
	private const double DisabledAccentOpacity = 0.36;
	private const double DisabledRowOpacity = 0.58;
	private const double ActionLabelFontSize = 10;
	private const double ActionLabelCharacterSpacing = 0.9;
	private const uint ShowAnimationLength = 120;
	private const uint HideAnimationLength = 100;

	internal event EventHandler? DismissRequested;
	internal event Action<WorkflowActionKind>? ActionRequested;

	public WorkflowActionsMenuView()
	{
		InitializeComponent();
	}

	internal bool IsOpen => WorkflowActionsOverlayRoot.IsVisible;

	public string PopupKey => "WorkflowActions";
	public string PopupGroup => "SmallMenu";
	public VisualElement PopupRoot => WorkflowActionsOverlayRoot;

	public bool IsShown(bool isVisible)
		=> WorkflowActionsOverlayRoot.IsVisible == isVisible && Math.Abs(WorkflowActionsMenuBorder.Opacity - (isVisible ? 1 : 0)) < 0.01;

	private double _topOffset;

	public void PrepareShowShell(NexusPopupOpenContext context)
	{
		_topOffset = context.TopOffset ?? 0;
		WorkflowActionsOverlayRoot.IsVisible = true;
		WorkflowActionsOverlayRoot.InputTransparent = true;
		WorkflowActionsMenuBorder.InputTransparent = true;
		WorkflowActionsMenuBorder.TranslationY = _topOffset + HiddenMenuOffsetY;
		WorkflowActionsMenuBorder.ScaleY = HiddenMenuScaleY;
		WorkflowActionsMenuBorder.Opacity = 0;
	}

	public void ActivateInput(NexusPopupOpenContext context)
	{
		WorkflowActionsOverlayRoot.InputTransparent = false;
		WorkflowActionsMenuBorder.InputTransparent = false;
	}

	public void PrepareHide()
	{
		WorkflowActionsOverlayRoot.InputTransparent = true;
		WorkflowActionsMenuBorder.InputTransparent = true;
	}

	public void ResetHiddenState()
	{
		WorkflowActionsOverlayRoot.IsVisible = false;
		WorkflowActionsMenuBorder.Opacity = 0;
		WorkflowActionsMenuBorder.TranslationY = 0;
		WorkflowActionsMenuBorder.ScaleY = 1;
	}

	internal void SetActions(IReadOnlyList<WorkflowActionMenuItem> actions)
	{
		WorkflowActionsStack.Children.Clear();

		foreach (var action in actions)
		{
			if (action.StartsNewSection)
			{
				WorkflowActionsStack.Children.Add(new BoxView
				{
					HeightRequest = SeparatorHeight,
					BackgroundColor = Color.FromArgb("#22334a"),
					Margin = new Thickness(8, 3),
					Opacity = SeparatorOpacity,
				});
			}

			WorkflowActionsStack.Children.Add(CreateActionRow(action));
		}
	}

	private View CreateActionRow(WorkflowActionMenuItem action)
	{
		var label = new Label
		{
			Text = action.Label.ToUpperInvariant(),
			TextColor = action.IsEnabled ? Color.FromArgb("#d9f7ff") : Color.FromArgb("#5f788b"),
			FontSize = ActionLabelFontSize,
			FontAttributes = FontAttributes.Bold,
			CharacterSpacing = ActionLabelCharacterSpacing,
			VerticalOptions = LayoutOptions.Center,
			LineBreakMode = LineBreakMode.TailTruncation,
		};

		var rowContent = new Grid
		{
			ColumnDefinitions =
			{
				new ColumnDefinition { Width = AccentWidth },
				new ColumnDefinition { Width = GridLength.Star },
			},
			ColumnSpacing = 8,
			Padding = new Thickness(8, 0),
		};

		rowContent.Add(new BoxView
		{
			Color = GetActionAccentColor(action.Kind, action.IsEnabled),
			WidthRequest = AccentWidth,
			HeightRequest = AccentHeight,
			CornerRadius = AccentCornerRadius,
			Opacity = action.IsEnabled ? EnabledAccentOpacity : DisabledAccentOpacity,
			VerticalOptions = LayoutOptions.Center,
		}, 0);
		rowContent.Add(label, 1);

		var row = new Border
		{
			HeightRequest = ActionRowHeight,
			StrokeThickness = 0,
			BackgroundColor = Colors.Transparent,
			HorizontalOptions = LayoutOptions.Fill,
			InputTransparent = !action.IsEnabled,
			Opacity = action.IsEnabled ? 1 : DisabledRowOpacity,
			Content = rowContent,
		};
		row.StrokeShape = new RoundRectangle { CornerRadius = ActionRowCornerRadius };

		VisualStateManager.SetVisualStateGroups(row, new VisualStateGroupList
		{
			new VisualStateGroup
			{
				Name = "CommonStates",
				States =
				{
					new VisualState { Name = "Normal" },
					new VisualState
					{
						Name = "PointerOver",
						Setters =
						{
							new Setter { Property = VisualElement.BackgroundColorProperty, Value = GetHoverColor(action.Kind) },
						},
					},
					new VisualState
					{
						Name = "Pressed",
						Setters =
						{
							new Setter { Property = VisualElement.BackgroundColorProperty, Value = GetPressedColor(action.Kind) },
						},
					},
				},
			},
		});

		if (action.IsEnabled)
		{
			row.GestureRecognizers.Add(new TapGestureRecognizer
			{
				Command = new Command(() => ActionRequested?.Invoke(action.Kind)),
			});
		}

		return row;
	}

	private static Color GetActionAccentColor(WorkflowActionKind kind, bool isEnabled)
	{
		if (!isEnabled)
		{
			return Color.FromArgb("#496070");
		}

		return kind switch
		{
			WorkflowActionKind.Delete => Color.FromArgb("#ff4c6f"),
			WorkflowActionKind.Clear => Color.FromArgb("#ff9f45"),
			WorkflowActionKind.Save or WorkflowActionKind.SaveAs => Color.FromArgb("#26d7ff"),
			WorkflowActionKind.Export or WorkflowActionKind.ExportApi => Color.FromArgb("#7be495"),
			WorkflowActionKind.Bookmark => Color.FromArgb("#ffd86b"),
			_ => Color.FromArgb("#71c8ff"),
		};
	}

	private static Color GetHoverColor(WorkflowActionKind kind)
		=> kind switch
		{
			WorkflowActionKind.Delete => Color.FromArgb("#24ff3d5f"),
			WorkflowActionKind.Clear => Color.FromArgb("#22ff9f45"),
			_ => Color.FromArgb("#1f3aa4c7"),
		};

	private static Color GetPressedColor(WorkflowActionKind kind)
		=> kind switch
		{
			WorkflowActionKind.Delete => Color.FromArgb("#36ff3d5f"),
			WorkflowActionKind.Clear => Color.FromArgb("#30ff9f45"),
			_ => Color.FromArgb("#2b1b7ea6"),
		};

	public Task AnimateShowAsync(NexusPopupOpenContext context)
		=> SafeAnimation.FadeTranslateScaleYToAsync(WorkflowActionsMenuBorder, "WorkflowActionsMenu.Show", 1, _topOffset, 1, ShowAnimationLength, Easing.CubicOut, "WorkflowActionsMenu.Show");

	public Task RefreshContentAsync(NexusPopupOpenContext context)
		=> Task.CompletedTask;

	public Task AnimateHideAsync(NexusPopupOpenContext context)
		=> SafeAnimation.FadeTranslateScaleYToAsync(WorkflowActionsMenuBorder, "WorkflowActionsMenu.Hide", 0, _topOffset + HideMenuOffsetY, HideMenuScaleY, HideAnimationLength, Easing.CubicIn, "WorkflowActionsMenu.Hide");

	private void OnBackdropTapped(object? sender, TappedEventArgs e) => DismissRequested?.Invoke(this, EventArgs.Empty);
}
