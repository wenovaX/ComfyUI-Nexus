using ComfyUI_Nexus.Dialogs;
using ComfyUI_Nexus.Diagnostics;
using ComfyUI_Nexus.Platform;
using ComfyUI_Nexus.Ui;
using ComfyUI_Nexus.Views.Rail.Tools;
using Microsoft.Maui.Controls.Shapes;

namespace ComfyUI_Nexus.Views.Overlays;

public partial class NexusDialogOverlayView : ContentView
{
	private const double ChoiceButtonHeight = 42;
	private const double ChoiceButtonCornerRadius = 14;
	private const double ChoiceButtonFontSize = 13;
	private const int ThumbnailChoicesPerPage = 3;
	private const double ThumbnailChoiceWidth = 136;
	private const double ThumbnailChoiceHeight = 174;
	private const double ThumbnailImageWidth = 126;
	private const double ThumbnailImageHeight = 144;
	private const double ThumbnailChoiceMargin = 5;
	private const double DialogScaleThresholdWidth = 1080;
	private const double DialogScaleThresholdHeight = 760;
	private const double DefaultDialogPanelWidth = 460;
	private const double ThumbnailDialogPanelWidth = 680;
	private const uint ShowFadeLength = 120;
	private static readonly Color ChoiceTextColor = Color.FromArgb("#E6F8FF");
	private static readonly Color ChoiceNormalBackgroundColor = Color.FromArgb("#132236");
	private static readonly Color ChoiceHoverBackgroundColor = Color.FromArgb("#1B334F");
	private static readonly Color ChoiceStrokeColor = Color.FromArgb("#3E6D8C");
	private static readonly Color ThumbnailNormalBackgroundColor = Color.FromArgb("#101E2E");
	private static readonly Color ThumbnailHoverBackgroundColor = Color.FromArgb("#18334C");
	private static readonly Color ThumbnailSelectedBackgroundColor = Color.FromArgb("#17485B");
	private static readonly Color ThumbnailPageButtonBackgroundColor = Color.FromArgb("#142436");
	private static readonly Color ThumbnailPageButtonHoverColor = Color.FromArgb("#1C3851");
	private static readonly Color ThumbnailPageButtonDisabledColor = Color.FromArgb("#0D1724");
	private static readonly Color OkNormalBackgroundColor = Color.FromArgb("#173044");
	private static readonly Color OkHoverBackgroundColor = Color.FromArgb("#204866");
	private static readonly Color OkStrokeColor = Color.FromArgb("#5A8DE7FF");
	private static readonly Color OkTextColor = Color.FromArgb("#E6F8FF");
	private static readonly Color DangerNormalBackgroundColor = Color.FromArgb("#441722");
	private static readonly Color DangerHoverBackgroundColor = Color.FromArgb("#662033");
	private static readonly Color DangerStrokeColor = Color.FromArgb("#FF6A8A");
	private static readonly Color DangerTextColor = Color.FromArgb("#FFD7DF");
	private static readonly Color PromptNormalBackgroundColor = Color.FromArgb("#102132");
	private static readonly Color PromptFocusedBackgroundColor = Color.FromArgb("#132A40");
	private static readonly Color PromptNormalStrokeColor = Color.FromArgb("#3D8CC9");
	private static readonly Color PromptFocusedStrokeColor = Color.FromArgb("#31D8FF");
	private readonly Queue<PendingDialog> _queue = new();
	private PendingDialog? _activePending;
	private TaskCompletionSource<NexusDialogResult>? _completion;
	private NexusDialogRequest? _request;
	private bool _isResolving;
	private Color _okNormalBackgroundColor = OkNormalBackgroundColor;
	private Color _okHoverBackgroundColor = OkHoverBackgroundColor;
	private int _thumbnailChoicePageIndex;
	private NexusDialogThumbnailChoice? _selectedThumbnailChoice;
	private readonly List<ThumbnailChoiceCardState> _thumbnailChoiceCards = new();
	private AdaptiveOverlayScaleHelper? _scaleHelper;
	private readonly NexusEntryTextController _promptEntryTextController;

	public NexusDialogOverlayView()
	{
		InitializeComponent();
		_scaleHelper = new AdaptiveOverlayScaleHelper(
			this,
			DialogPanelBorder,
			new AdaptiveOverlayScaleOptions(DialogScaleThresholdWidth, DialogScaleThresholdHeight, MinimumScale: 0.76));
		SizeChanged += OnDialogSizeChanged;
		WireDynamicHoverState(DialogOkButton, () => _okNormalBackgroundColor, () => _okHoverBackgroundColor);
		WireHoverState(DialogCancelButton, "#151E2B", "#1E2A3A");
		WireHoverState(DialogThumbnailPrevButton, ThumbnailPageButtonBackgroundColor, ThumbnailPageButtonHoverColor);
		WireHoverState(DialogThumbnailNextButton, ThumbnailPageButtonBackgroundColor, ThumbnailPageButtonHoverColor);
		_promptEntryTextController = new NexusEntryTextController(DialogPromptEntry, DialogPromptFrame);
		RailSearchClearButtonVisuals.Attach(DialogPromptClearButton, DialogPromptClearLabel);
	}

	internal bool IsDialogOpen => _completion != null;

	internal event EventHandler<NexusDialogReturnFocusTarget>? Closed;

	internal Task<NexusDialogResult> ShowAsync(NexusDialogRequest request)
	{
		string signature = CreateRequestSignature(request);
		if (_activePending?.Signature == signature)
		{
			return _activePending.Completion.Task;
		}

		foreach (var queued in _queue)
		{
			if (queued.Signature == signature)
			{
				return queued.Completion.Task;
			}
		}

		var pending = new PendingDialog(request, signature);
		_queue.Enqueue(pending);
		_ = DrainQueueAsync();
		return pending.Completion.Task;
	}

	internal async Task<bool> TryHandleShortcutAsync(NexusKey key)
	{
		if (_completion == null)
		{
			return false;
		}

		if (_request?.Kind == NexusDialogKind.Prompt)
		{
			if (key == NexusKey.Enter)
			{
				await ResolveOkAsync();
				return true;
			}
			else if (key == NexusKey.Escape)
			{
				await ResolveCancelAsync();
				return true;
			}
			return false;
		}

		switch (key)
		{
			case NexusKey.Enter:
				await ResolveOkAsync();
				return true;
			case NexusKey.Delete:
				return true;
			case NexusKey.Escape:
				await ResolveCancelAsync();
				return true;
			default:
				return true;
		}
	}

	private async Task DrainQueueAsync()
	{
		if (_completion != null)
		{
			return;
		}

		while (_queue.Count > 0)
		{
			var pending = _queue.Dequeue();
			bool isPrompt = pending.Request.Kind == NexusDialogKind.Prompt;
			NexusDialogResult resolvedResult = NexusDialogResult.Cancelled;
			_activePending = pending;
			_request = pending.Request;
			_completion = new TaskCompletionSource<NexusDialogResult>(TaskCreationOptions.RunContinuationsAsynchronously);
			ApplyRequest(pending.Request);

			try
			{
				using var operation = XamlUnhandledExceptionDiagnostics.EnterUiOperation("NexusDialog.Show");
				InputTransparent = false;
				IsVisible = true;
				Opacity = isPrompt ? 1 : 0;
				if (!isPrompt)
				{
					await AnimateShowAsync();
				}
				FocusDialogInput(pending.Request.Kind);

				resolvedResult = await _completion.Task;
			}
			catch (Exception ex)
			{
				NexusLog.Exception(ex, "[DIALOG] Dialog failed");
			}
			finally
			{
				using var operation = XamlUnhandledExceptionDiagnostics.EnterUiOperation("NexusDialog.Hide");
				DialogPromptEntry.Unfocus();
				InputTransparent = true;
				if (isPrompt)
				{
					DialogPromptFrame.IsVisible = false;
				}
				Opacity = 0;
				IsVisible = false;
				_completion = null;
				_request = null;
				_activePending = null;
				_isResolving = false;
				_selectedThumbnailChoice = null;
				_thumbnailChoiceCards.Clear();
				_thumbnailChoicePageIndex = 0;
				Closed?.Invoke(this, pending.Request.ReturnFocusTarget);
			}

			pending.Completion.TrySetResult(resolvedResult);
		}
	}

	private void ApplyRequest(NexusDialogRequest request)
	{
		using var operation = XamlUnhandledExceptionDiagnostics.EnterUiOperation($"NexusDialog.Apply.{request.Kind}");
		DialogPanelBorder.Scale = 1;
		DialogPanelBorder.WidthRequest = request.Kind == NexusDialogKind.ThumbnailChoice
			? ThumbnailDialogPanelWidth
			: DefaultDialogPanelWidth;
		DialogTitleLabel.Text = request.Title;
		DialogMessageLabel.Text = request.Message;
		DialogMessageLabel.IsVisible = !string.IsNullOrWhiteSpace(request.Message);
		DialogInlineMessageLabel.IsVisible = false;
		DialogInlineMessageLabel.Text = string.Empty;

		DialogPromptFrame.IsVisible = request.Kind == NexusDialogKind.Prompt;
		DialogPromptEntry.Text = request.InitialValue;
		DialogPromptEntry.Placeholder = request.Placeholder;
		DialogPromptEntry.MaxLength = request.MaxLength > 0 ? request.MaxLength : int.MaxValue;
		DialogPromptEntry.Keyboard = request.Keyboard;
		DialogPromptClearButton.IsVisible = !string.IsNullOrEmpty(request.InitialValue);
		ApplyPromptFocusState(false);

		using (XamlUnhandledExceptionDiagnostics.EnterUiOperation("NexusDialog.Choices.Rebuild"))
		{
			DialogChoicesStack.Children.Clear();
		}
		DialogChoicesScroll.IsVisible = request.Kind == NexusDialogKind.Choice;
		if (request.Kind == NexusDialogKind.Choice)
		{
			foreach (var choice in request.Choices)
			{
				using var choicesOperation = XamlUnhandledExceptionDiagnostics.EnterUiOperation("NexusDialog.Choices.Add");
				DialogChoicesStack.Children.Add(CreateChoiceButton(choice));
			}
		}

		_thumbnailChoicePageIndex = 0;
		_selectedThumbnailChoice = request.ThumbnailChoices.FirstOrDefault(choice => choice.IsPrimary) ??
			request.ThumbnailChoices.FirstOrDefault();
		DialogThumbnailPickerPanel.IsVisible = request.Kind == NexusDialogKind.ThumbnailChoice;
		DialogThumbnailPagerGrid.IsVisible = request.Kind == NexusDialogKind.ThumbnailChoice;
		if (request.Kind == NexusDialogKind.ThumbnailChoice)
		{
			RefreshThumbnailChoicePage();
		}
		else
		{
			using var thumbnailOperation = XamlUnhandledExceptionDiagnostics.EnterUiOperation("Dialog.ThumbnailChoice.Clear");
			DialogThumbnailChoiceGrid.Children.Clear();
			DialogThumbnailPageLabel.Text = string.Empty;
			DialogThumbnailChoiceGrid.HeightRequest = -1;
		}

		bool hasCancel = request.Kind is NexusDialogKind.Confirm or NexusDialogKind.Prompt or NexusDialogKind.Choice or NexusDialogKind.ThumbnailChoice;
		bool hasOk = request.Kind is NexusDialogKind.Alert or NexusDialogKind.Confirm or NexusDialogKind.Prompt or NexusDialogKind.ThumbnailChoice;
		DialogCancelButton.IsVisible = hasCancel;
		DialogOkButton.IsVisible = hasOk;
		DialogCancelButtonLabel.Text = request.CancelText;
		DialogOkButtonLabel.Text = request.OkText;
		ApplyOkButtonStyle(request.OkIsDanger);
		DialogButtonGrid.ColumnDefinitions[0].Width = hasCancel ? GridLength.Star : new GridLength(0);
		DialogButtonGrid.ColumnDefinitions[1].Width = hasOk ? GridLength.Star : new GridLength(0);
		QueueDialogScaleUpdate();
	}

	private void ApplyOkButtonStyle(bool isDanger)
	{
		_okNormalBackgroundColor = isDanger ? DangerNormalBackgroundColor : OkNormalBackgroundColor;
		_okHoverBackgroundColor = isDanger ? DangerHoverBackgroundColor : OkHoverBackgroundColor;
		DialogOkButton.BackgroundColor = _okNormalBackgroundColor;
		DialogOkButton.Stroke = isDanger ? DangerStrokeColor : OkStrokeColor;
		DialogOkButtonLabel.TextColor = isDanger ? DangerTextColor : OkTextColor;
	}

	private static string CreateRequestSignature(NexusDialogRequest request)
	{
		string choices = request.Choices.Count == 0
			? string.Empty
			: string.Join('\u001F', request.Choices.Select(choice => choice.Text));
		string thumbnails = request.ThumbnailChoices.Count == 0
			? string.Empty
			: string.Join('\u001F', request.ThumbnailChoices.Select(choice => $"{choice.Text}|{choice.ImagePath}|{choice.IsPrimary}"));
		return string.Join('\u001E',
			request.Kind,
			request.Title,
			request.Message,
			request.OkText,
			request.OkIsDanger,
			request.CancelText,
			request.Placeholder,
			request.InitialValue,
			request.MaxLength,
			request.ReturnFocusTarget,
			choices,
			thumbnails);
	}

	private View CreateChoiceButton(NexusDialogChoice choice)
	{
		var label = new Label
		{
			Text = choice.Text,
			TextColor = choice.IsDanger ? DangerTextColor : ChoiceTextColor,
			FontSize = ChoiceButtonFontSize,
			FontAttributes = FontAttributes.Bold,
			HorizontalOptions = LayoutOptions.Center,
			VerticalTextAlignment = TextAlignment.Center,
			VerticalOptions = LayoutOptions.Center
		};
		Color normalBackground = choice.IsDanger ? DangerNormalBackgroundColor : ChoiceNormalBackgroundColor;
		Color hoverBackground = choice.IsDanger ? DangerHoverBackgroundColor : ChoiceHoverBackgroundColor;
		var border = new Border
		{
			HeightRequest = ChoiceButtonHeight,
			BackgroundColor = normalBackground,
			StrokeThickness = 0,
			StrokeShape = new RoundRectangle { CornerRadius = ChoiceButtonCornerRadius },
			Content = label
		};
		WireHoverState(border, normalBackground, hoverBackground);
		border.GestureRecognizers.Add(new TapGestureRecognizer
		{
			Command = new Command(async () => await ResolveChoiceAsync(choice))
		});
		return border;
	}

	private void RefreshThumbnailChoicePage()
	{
		using var operation = XamlUnhandledExceptionDiagnostics.EnterUiOperation("Dialog.ThumbnailChoice.Update");
		DialogThumbnailChoiceGrid.Children.Clear();
		_thumbnailChoiceCards.Clear();
		var choices = _request?.ThumbnailChoices ?? [];
		int pageCount = Math.Max(1, (int)Math.Ceiling(choices.Count / (double)ThumbnailChoicesPerPage));
		_thumbnailChoicePageIndex = Math.Clamp(_thumbnailChoicePageIndex, 0, pageCount - 1);
		foreach (var choice in choices
			.Skip(_thumbnailChoicePageIndex * ThumbnailChoicesPerPage)
			.Take(ThumbnailChoicesPerPage))
		{
			var card = CreateThumbnailChoiceCard(choice);
			_thumbnailChoiceCards.Add(card);
			DialogThumbnailChoiceGrid.Children.Add(card.Card);
		}

		DialogThumbnailPageLabel.Text = string.Format(
			System.Globalization.CultureInfo.CurrentCulture,
			ComfyUI_Nexus.Localization.LocalizationManager.Text("model_thumbnail.page_indicator"),
			_thumbnailChoicePageIndex + 1,
			pageCount,
			choices.Count);
		DialogThumbnailChoiceGrid.HeightRequest = ThumbnailChoiceHeight + (ThumbnailChoiceMargin * 2);
		SetThumbnailPageButtonState(DialogThumbnailPrevButton, DialogThumbnailPrevIcon, _thumbnailChoicePageIndex > 0);
		SetThumbnailPageButtonState(DialogThumbnailNextButton, DialogThumbnailNextIcon, _thumbnailChoicePageIndex < pageCount - 1);
		QueueDialogScaleUpdate();
	}

	private ThumbnailChoiceCardState CreateThumbnailChoiceCard(NexusDialogThumbnailChoice choice)
	{
		bool isSelected = _selectedThumbnailChoice != null &&
			string.Equals(_selectedThumbnailChoice.Text, choice.Text, StringComparison.OrdinalIgnoreCase);
		var image = new Image
		{
			Source = ImageSource.FromFile(choice.ImagePath),
			Aspect = Aspect.AspectFill,
			WidthRequest = ThumbnailImageWidth,
			HeightRequest = ThumbnailImageHeight,
			HorizontalOptions = LayoutOptions.Center,
			VerticalOptions = LayoutOptions.Center
		};
		var imageClip = new RoundRectangleGeometry
		{
			CornerRadius = 12,
			Rect = new Rect(0, 0, ThumbnailImageWidth, ThumbnailImageHeight)
		};
		image.Clip = imageClip;
		var imageFrame = new Border
		{
			WidthRequest = ThumbnailImageWidth,
			HeightRequest = ThumbnailImageHeight,
			BackgroundColor = Color.FromArgb("#162B3C"),
			StrokeThickness = 0,
			StrokeShape = new RoundRectangle { CornerRadius = 14 },
			Content = image
		};
		var title = new Label
		{
			Text = choice.Text,
			TextColor = isSelected ? Color.FromArgb("#E9FCFF") : Color.FromArgb("#AFC9DA"),
			FontSize = 11,
			LineBreakMode = LineBreakMode.TailTruncation,
			HorizontalTextAlignment = TextAlignment.Center,
			MaxLines = 1
		};
		var badge = new Border
		{
			IsVisible = choice.IsPrimary,
			BackgroundColor = Color.FromArgb("#D91B8C98"),
			StrokeThickness = 0,
			StrokeShape = new RoundRectangle { CornerRadius = 8 },
			Padding = new Thickness(7, 2),
			Margin = new Thickness(6),
			HorizontalOptions = LayoutOptions.Start,
			VerticalOptions = LayoutOptions.Start,
			Content = new Label
			{
				Text = ComfyUI_Nexus.Localization.LocalizationManager.Text("model_thumbnail.primary_badge"),
				TextColor = Color.FromArgb("#DFFFFF"),
				FontSize = 9,
				FontAttributes = FontAttributes.Bold,
				HorizontalTextAlignment = TextAlignment.Center
			}
		};
		var imageHost = new Grid
		{
			WidthRequest = ThumbnailImageWidth,
			HeightRequest = ThumbnailImageHeight,
			HorizontalOptions = LayoutOptions.Center,
			Children =
			{
				imageFrame,
				badge
			}
		};
		var stack = new VerticalStackLayout
		{
			Spacing = 5,
			Children =
			{
				imageHost,
				title
			}
		};
		var card = new Border
		{
			WidthRequest = ThumbnailChoiceWidth,
			HeightRequest = ThumbnailChoiceHeight,
			Margin = new Thickness(ThumbnailChoiceMargin),
			Padding = new Thickness(5, 5, 5, 4),
			BackgroundColor = isSelected ? ThumbnailSelectedBackgroundColor : ThumbnailNormalBackgroundColor,
			StrokeThickness = 0,
			StrokeShape = new RoundRectangle { CornerRadius = 16 },
			Content = stack
		};
		var state = new ThumbnailChoiceCardState(choice, card, title);
		WireThumbnailChoiceHoverState(state);
		card.GestureRecognizers.Add(new TapGestureRecognizer
		{
			Command = new Command(() =>
			{
				_selectedThumbnailChoice = choice;
				RefreshThumbnailChoiceSelectionState();
			})
		});
		return state;
	}

	private void RefreshThumbnailChoiceSelectionState()
	{
		foreach (var card in _thumbnailChoiceCards)
		{
			bool isSelected = _selectedThumbnailChoice != null &&
				string.Equals(_selectedThumbnailChoice.Text, card.Choice.Text, StringComparison.OrdinalIgnoreCase);
			card.Card.BackgroundColor = isSelected ? ThumbnailSelectedBackgroundColor : ThumbnailNormalBackgroundColor;
			card.Title.TextColor = isSelected ? Color.FromArgb("#E9FCFF") : Color.FromArgb("#AFC9DA");
		}
	}

	private void WireThumbnailChoiceHoverState(ThumbnailChoiceCardState state)
	{
		var pointer = new PointerGestureRecognizer();
		pointer.PointerEntered += (_, _) => state.Card.BackgroundColor = ThumbnailHoverBackgroundColor;
		pointer.PointerExited += (_, _) =>
		{
			bool isSelected = _selectedThumbnailChoice != null &&
				string.Equals(_selectedThumbnailChoice.Text, state.Choice.Text, StringComparison.OrdinalIgnoreCase);
			state.Card.BackgroundColor = isSelected ? ThumbnailSelectedBackgroundColor : ThumbnailNormalBackgroundColor;
		};
		state.Card.GestureRecognizers.Add(pointer);
	}

	private static void SetThumbnailPageButtonState(Border button, Shape icon, bool isEnabled)
	{
		button.IsEnabled = isEnabled;
		button.BackgroundColor = isEnabled ? ThumbnailPageButtonBackgroundColor : ThumbnailPageButtonDisabledColor;
		button.Opacity = isEnabled ? 1 : 0.55;
		icon.Stroke = new SolidColorBrush(isEnabled ? Color.FromArgb("#DFFAFF") : Color.FromArgb("#5F7486"));
	}

	private void OnDialogThumbnailPreviousTapped(object? sender, TappedEventArgs e)
	{
		if (_thumbnailChoicePageIndex <= 0)
		{
			return;
		}

		_thumbnailChoicePageIndex--;
		RefreshThumbnailChoicePage();
	}

	private void OnDialogThumbnailNextTapped(object? sender, TappedEventArgs e)
	{
		var choices = _request?.ThumbnailChoices ?? [];
		int pageCount = Math.Max(1, (int)Math.Ceiling(choices.Count / (double)ThumbnailChoicesPerPage));
		if (_thumbnailChoicePageIndex >= pageCount - 1)
		{
			return;
		}

		_thumbnailChoicePageIndex++;
		RefreshThumbnailChoicePage();
	}

	private void OnDialogSizeChanged(object? sender, EventArgs e)
		=> QueueDialogScaleUpdate();

	private void QueueDialogScaleUpdate()
	{
		Dispatcher.Dispatch(async () =>
		{
			await Task.Yield();
			_scaleHelper?.Apply();
		});
	}

	private async Task ResolveChoiceAsync(NexusDialogChoice choice)
	{
		if (_isResolving)
		{
			return;
		}

		_isResolving = true;
		try
		{
			if (choice.Callback != null && await choice.Callback() == NexusDialogActionResult.KeepOpen)
			{
				_isResolving = false;
				return;
			}

			_completion?.TrySetResult(new NexusDialogResult(true, Choice: choice.Text));
		}
		catch (Exception ex)
		{
			_isResolving = false;
			ShowInlineMessage(ex.Message);
		}
	}

	private async Task ResolveOkAsync()
	{
		if (_isResolving || _request == null)
		{
			return;
		}

		_isResolving = true;
		try
		{
			string value = DialogPromptEntry.Text ?? string.Empty;
			NexusDialogActionResult actionResult = NexusDialogActionResult.Close;
			if (_request.Kind == NexusDialogKind.Prompt && _request.PromptOkCallback != null)
			{
				actionResult = await _request.PromptOkCallback(value);
			}
			else if (_request.Kind == NexusDialogKind.ThumbnailChoice)
			{
				if (_selectedThumbnailChoice == null)
				{
					_isResolving = false;
					ShowInlineMessage(ComfyUI_Nexus.Localization.LocalizationManager.Text("model_thumbnail.no_selection_message"));
					return;
				}

				_completion?.TrySetResult(new NexusDialogResult(true, Choice: _selectedThumbnailChoice.Text));
				return;
			}
			else if (_request.OkCallback != null)
			{
				actionResult = await _request.OkCallback();
			}

			if (actionResult == NexusDialogActionResult.KeepOpen)
			{
				_isResolving = false;
				return;
			}

			_completion?.TrySetResult(new NexusDialogResult(true, value));
		}
		catch (Exception ex)
		{
			_isResolving = false;
			ShowInlineMessage(ex.Message);
		}
	}

	private async Task ResolveCancelAsync()
	{
		if (_isResolving)
		{
			return;
		}

		_isResolving = true;
		try
		{
			if (_request?.CancelCallback != null)
			{
				await _request.CancelCallback();
			}
		}
		catch (Exception ex)
		{
			NexusLog.Warning($"Dialog cancel callback failed: {ex.GetType().Name} - {ex.Message}");
		}

		_completion?.TrySetResult(NexusDialogResult.Cancelled);
	}

	private void ShowInlineMessage(string message)
	{
		DialogInlineMessageLabel.Text = string.IsNullOrWhiteSpace(message) ? "Unable to complete this action." : message;
		DialogInlineMessageLabel.IsVisible = true;
	}

	private async void OnOkTapped(object? sender, TappedEventArgs e) => await ResolveOkAsync();

	private async void OnCancelTapped(object? sender, TappedEventArgs e) => await ResolveCancelAsync();

	private Task AnimateShowAsync()
		=> SafeAnimation.FadeToAsync(this, 1, ShowFadeLength, Easing.CubicOut, "NexusDialog.Show");

	private void FocusDialogInput(NexusDialogKind kind)
	{
		if (kind == NexusDialogKind.Prompt)
		{
			DialogPromptEntry.Focus();
			return;
		}

		DialogKeyboardFocusSink.Focus();
	}

	private void OnDialogPromptEntryFocused(object? sender, FocusEventArgs e)
	{
		ApplyPromptFocusState(true);
		_promptEntryTextController.RefreshNativeSelectionColors();
	}

	private void OnDialogPromptEntryUnfocused(object? sender, FocusEventArgs e) => ApplyPromptFocusState(false);

	private void OnDialogPromptEntryTextChanged(object? sender, TextChangedEventArgs e)
		=> DialogPromptClearButton.IsVisible = !string.IsNullOrEmpty(e.NewTextValue);

	private void OnDialogPromptClearTapped(object? sender, TappedEventArgs e)
		=> DialogPromptEntry.Text = string.Empty;

	private void ApplyPromptFocusState(bool isFocused)
	{
		DialogPromptFrame.BackgroundColor = isFocused ? PromptFocusedBackgroundColor : PromptNormalBackgroundColor;
		DialogPromptFrame.Stroke = isFocused ? PromptFocusedStrokeColor : PromptNormalStrokeColor;
	}

	private static void WireHoverState(View view, string normalColor, string hoverColor)
		=> WireHoverState(view, Color.FromArgb(normalColor), Color.FromArgb(hoverColor));

	private static void WireHoverState(View view, Color normalColor, Color hoverColor)
	{
		view.BackgroundColor = normalColor;
		var pointer = new PointerGestureRecognizer();
		pointer.PointerEntered += (_, _) => view.BackgroundColor = hoverColor;
		pointer.PointerExited += (_, _) => view.BackgroundColor = normalColor;
		view.GestureRecognizers.Add(pointer);
	}

	private static void WireDynamicHoverState(View view, Func<Color> normalColor, Func<Color> hoverColor)
	{
		view.BackgroundColor = normalColor();
		var pointer = new PointerGestureRecognizer();
		pointer.PointerEntered += (_, _) => view.BackgroundColor = hoverColor();
		pointer.PointerExited += (_, _) => view.BackgroundColor = normalColor();
		view.GestureRecognizers.Add(pointer);
	}

	private sealed class PendingDialog(NexusDialogRequest request, string signature)
	{
		internal NexusDialogRequest Request { get; } = request;
		internal string Signature { get; } = signature;
		internal TaskCompletionSource<NexusDialogResult> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
	}

	private sealed record ThumbnailChoiceCardState(
		NexusDialogThumbnailChoice Choice,
		Border Card,
		Label Title);
}
