namespace ComfyUI_Nexus.Views.Overlays;

using System.Diagnostics;
using System.Globalization;
using ComfyUI_Nexus.Configuration;
using ComfyUI_Nexus.Diagnostics;
using ComfyUI_Nexus.Help;
using ComfyUI_Nexus.Localization;
using ComfyUI_Nexus.Setup.Services;
using ComfyUI_Nexus.Ui;
using ComfyUI_Nexus.Ui.Popups;
using Microsoft.Maui.Layouts;

public partial class HelpOverlayView : ContentView, INexusPopupSurface
{
	// Developer-only hot reload for help catalog/article text while editing local files.
	private const bool ReloadContentOnTabChange = false;
	private const double PanelViewportScale = 0.8;
	private const double MinimumPanelWidth = 1024;
	private const double MinimumPanelHeight = 640;
	private const double ScaleThresholdWidth = 1280;
	private const double ScaleThresholdHeight = 720;
	private const double HiddenPanelScale = 0.98;
	private const double HiddenPanelOffsetY = 12;
	private const double HidePanelOffsetY = 10;
	private const uint ShowAnimationLength = 170;
	private const uint ContentFadeInAnimationLength = 120;
	private const uint HideAnimationLength = 120;
	private const double NavigationLabelFontSize = 10;
	private const double BodyHeadingFontSize = 18;
	private const double BodyTextFontSize = 15;
	private const double BodyCodeFontSize = 14;
	private const double CopyBlockFontSize = 14;
	private const double CopyBlockHeaderFontSize = 11;
	private const double CopyBlockCornerRadius = 15;
	private const int CopyFeedbackResetDelayMs = 1200;
	private static readonly Color NavigationSectionColor = NexusColors.AccentBright;
	private static readonly Color NavigationDescriptionColor = Color.FromArgb("#5f788d");
	private static readonly Color NavigationSelectedBackgroundColor = Color.FromArgb("#2631d8ff");
	private static readonly Color NavigationTransparentColor = NexusColors.Transparent;
	private static readonly Color NavigationSelectedTextColor = NexusColors.White;
	private static readonly Color NavigationTextColor = Color.FromArgb("#b8dff2");
	private static readonly Color DetailAccentFallbackColor = NexusColors.Accent;
	private static readonly Color BodyHeadingColor = NexusColors.TextStrong;
	private static readonly Color BodyCodeColor = NexusColors.TextSoft;
	private static readonly Color BodyAccentColor = NexusColors.AccentBright;
	private static readonly Color BodyTextColor = NexusColors.TextMuted;
	private static readonly Color BodyDividerColor = Color.FromArgb("#1f4f65");
	private static readonly Color CopyHintColor = Color.FromArgb("#86a9bd");
	private static readonly Color CopyLinkColor = NexusColors.AccentBright;
	private static readonly Color CopyLinkHoverColor = NexusColors.White;
	private static readonly Color CopyBlockBackgroundColor = NexusColors.SurfaceOverlay;
	private static readonly Color CopyBlockStrokeColor = Color.FromArgb("#2b7e9f");
	private static readonly Color FolderLinkColor = Color.FromArgb("#b8f7d4");
	private static readonly Color FolderLinkBackgroundColor = NexusColors.SurfaceOverlay;
	private static readonly Color FolderLinkHoverBackgroundColor = Color.FromArgb("#2a2d6b48");
	private static readonly string[] FormattingTags = ["[h]", "[/h]", "[b]", "[/b]", "[accent]", "[/accent]", "[code]", "[/code]"];
	// Keep article variables small and explicit; they are resolved before tag parsing.
	private static readonly Dictionary<string, Func<string>> HelpVariables = new(StringComparer.OrdinalIgnoreCase)
	{
		["comfyui.path"] = () => ComfyPathResolver.ResolveConfiguredComfyPath(),
	};

	private readonly List<HelpNavigationEntry> _entries = [];
	private readonly Dictionary<HelpNavigationEntry, Button> _entryButtons = [];
	private HelpNavigationEntry? _selectedEntry;
	private bool _isRenderingContent;
	private bool _isLayoutPrewarmed;
	private bool _isContentDirty = true;
	private string _renderedLanguage = string.Empty;

	internal event EventHandler? CloseRequested;

	public string PopupKey => "Help";
	public string PopupGroup => "Overlay";
	public VisualElement PopupRoot => this;

	public HelpOverlayView()
	{
		InitializeComponent();
		SizeChanged += OnSizeChanged;
		LocalizationManager.LanguageChanged += OnLanguageChanged;
		RenderContentIfNeeded(force: true);
	}

	public bool IsShown(bool isVisible)
		=> IsVisible == isVisible && Math.Abs(Opacity - (isVisible ? 1 : 0)) < 0.01;

	internal async Task PrewarmLayoutAsync()
	{
		if (_isLayoutPrewarmed)
		{
			return;
		}

		try
		{
			RenderContentIfNeeded();
			HelpPanelHost.InvalidateMeasure();
			HelpPanelBorder.InvalidateMeasure();
			await NexusUiFrame.AwaitDispatcherTurnAsync(this, "HELP:Prewarm");
			_isLayoutPrewarmed = true;
		}
		catch (Exception ex)
		{
			NexusLog.Trace($"[HELP] Layout prewarm skipped: {ex.Message}");
		}
	}

	public void PrepareShowShell(NexusPopupOpenContext context)
	{
		IsVisible = true;
		Opacity = 0;
		InputTransparent = true;
		HelpPanelPlaceholder.IsVisible = true;
		HelpPanelPlaceholder.Opacity = 1;
		HelpContentRoot.IsVisible = false;
		HelpContentRoot.Opacity = 0;
		HelpContentRoot.InputTransparent = true;
		Scale = HiddenPanelScale;
		TranslationY = HiddenPanelOffsetY;
	}

	public void ActivateInput(NexusPopupOpenContext context)
	{
		InputTransparent = false;
	}

	public void PrepareHide()
	{
		InputTransparent = true;
		HelpContentRoot.InputTransparent = true;
	}

	public void ResetHiddenState()
	{
		IsVisible = false;
		Opacity = 0;
		Scale = 1;
		TranslationY = 0;
		HelpPanelPlaceholder.IsVisible = true;
		HelpPanelPlaceholder.Opacity = 1;
		HelpContentRoot.IsVisible = false;
		HelpContentRoot.Opacity = 0;
		HelpContentRoot.InputTransparent = true;
	}

	public Task AnimateShowAsync(NexusPopupOpenContext context)
		=> AnimateShowCoreAsync();

	public async Task RefreshContentAsync(NexusPopupOpenContext context)
	{
		try
		{
			RenderContentIfNeeded();
		}
		catch (Exception ex)
		{
			NexusLog.Exception(ex, "[HELP] Popup content refresh failed.");
		}

		await RevealContentAsync();
	}

	private async Task RevealContentAsync()
	{
		HelpContentRoot.IsVisible = true;
		HelpContentRoot.Opacity = 0;
		HelpContentRoot.InputTransparent = true;
		await NexusUiFrame.AwaitShellReadyAsync(HelpContentRoot, "HELP:Content");
		await Task.WhenAll(
			SafeAnimation.FadeToAsync(HelpContentRoot, 1, ContentFadeInAnimationLength, Easing.CubicOut, "Help.Content"),
			SafeAnimation.FadeToAsync(HelpPanelPlaceholder, 0, ContentFadeInAnimationLength, Easing.CubicOut, "Help.Placeholder"));
		HelpContentRoot.InputTransparent = false;
		HelpPanelPlaceholder.IsVisible = false;
	}

	private Task AnimateShowCoreAsync()
		=> AnimatePanelShowAsync();

	private Task AnimatePanelShowAsync()
		=> SafeAnimation.FadeTranslateScaleToAsync(this, "Help.Show", 1, 0, 1, ShowAnimationLength, Easing.CubicOut, "Help.Show");

	public Task AnimateHideAsync(NexusPopupOpenContext context)
	{
		return SafeAnimation.FadeTranslateScaleToAsync(this, "Help.Hide", 0, HidePanelOffsetY, HiddenPanelScale, HideAnimationLength, Easing.CubicIn, "Help.Hide");
	}

	private void OnSizeChanged(object? sender, EventArgs e)
		=> UpdatePanelSize();

	private void UpdatePanelSize()
	{
		if (Width <= 0 || Height <= 0)
		{
			return;
		}

		double designWidth = Math.Max(MinimumPanelWidth, Width * PanelViewportScale);
		double designHeight = Math.Max(MinimumPanelHeight, Height * PanelViewportScale);

		HelpPanelBorder.WidthRequest = designWidth;
		HelpPanelBorder.HeightRequest = designHeight;
		HelpPanelBorder.AnchorX = 0;
		HelpPanelBorder.AnchorY = 0;
		HelpPanelBorder.Scale = Math.Min(
			1,
			Math.Min(Width / ScaleThresholdWidth, Height / ScaleThresholdHeight));
		HelpPanelHost.WidthRequest = designWidth;
		HelpPanelHost.HeightRequest = designHeight;
		HelpPanelBorder.TranslationX = designWidth * (1 - HelpPanelBorder.Scale) / 2;
		HelpPanelBorder.TranslationY = designHeight * (1 - HelpPanelBorder.Scale) / 2;
	}

	private void OnLanguageChanged(object? sender, EventArgs e)
	{
		_isContentDirty = true;
	}

	private void RenderContentIfNeeded(bool force = false, int selectedIndex = 0)
	{
		string activeLanguage = LocalizationManager.ActiveLanguage;
		if (!force
			&& !ReloadContentOnTabChange
			&& !_isContentDirty
			&& string.Equals(_renderedLanguage, activeLanguage, StringComparison.Ordinal))
		{
			return;
		}

		RenderContent(selectedIndex);
		_renderedLanguage = activeLanguage;
		_isContentDirty = false;
	}

	private void RenderContent(int selectedIndex = 0)
	{
		_isRenderingContent = true;
		try
		{
			HelpContent content = HelpContentLoader.Load(preferFileSystem: ReloadContentOnTabChange);
			if (!string.IsNullOrWhiteSpace(content.DisplayTitle))
			{
				HelpTitleLabel.Text = content.DisplayTitle;
			}

			if (!string.IsNullOrWhiteSpace(content.DisplaySubtitle))
			{
				HelpSubtitleLabel.Text = content.DisplaySubtitle;
			}

			HelpItemsStack.Clear();
			_entries.Clear();
			_entryButtons.Clear();
			foreach (HelpSection section in content.Sections)
			{
				AddNavigationSection(section);
			}

			if (_entries.Count > 0)
			{
				_selectedEntry = null;
				SelectEntry(_entries[Math.Clamp(selectedIndex, 0, _entries.Count - 1)]);
			}
		}
		finally
		{
			_isRenderingContent = false;
		}
	}

	private void AddNavigationSection(HelpSection section)
	{
		HelpItemsStack.Add(new Label
		{
			Text = section.DisplayTitle,
			TextColor = NavigationSectionColor,
			FontSize = NavigationLabelFontSize,
			FontAttributes = FontAttributes.Bold,
			CharacterSpacing = 2,
			Margin = new Thickness(4, _entries.Count == 0 ? 0 : 12, 4, 3)
		});

		if (!string.IsNullOrWhiteSpace(section.DisplayDescription))
		{
			HelpItemsStack.Add(new Label
			{
				Text = section.DisplayDescription,
				TextColor = NavigationDescriptionColor,
				FontSize = NavigationLabelFontSize,
				LineHeight = 1.12,
				Margin = new Thickness(4, 0, 4, 4)
			});
		}

		foreach (HelpItem item in section.Items)
		{
			var entry = new HelpNavigationEntry(section, item);
			_entries.Add(entry);

			var button = new Button
			{
				Text = item.DisplayTitle,
				Style = GetStyle("HelpNavButtonStyle")
			};
			button.Clicked += (_, _) => SelectEntry(entry);
			_entryButtons[entry] = button;
			HelpItemsStack.Add(button);
		}
	}

	private void SelectEntry(HelpNavigationEntry entry)
	{
		if (ReferenceEquals(entry, _selectedEntry))
		{
			return;
		}

		if (ReloadContentOnTabChange && !_isRenderingContent && _entries.Count > 0)
		{
			int selectedIndex = _entries.IndexOf(entry);
			RenderContentIfNeeded(force: true, selectedIndex: selectedIndex < 0 ? 0 : selectedIndex);
			return;
		}

		_selectedEntry = entry;
		UpdateNavigationSelection();

		HelpItem item = entry.Item;
		HelpDetailTitleLabel.Text = item.DisplayTitle;
		ApplyDetailAccent(item.AccentColor);
		BuildBodyContent(item);
		HelpDetailHintLabel.Text = item.DisplayHint;
		HelpDetailHintLabel.IsVisible = !string.IsNullOrWhiteSpace(item.DisplayHint);
		ResetDetailScrollPosition();
	}

	private void ResetDetailScrollPosition()
	{
		_ = MainThread.InvokeOnMainThreadAsync(async () =>
		{
			await Task.Yield();
			await HelpDetailScrollView.ScrollToAsync(0, 0, false);
		});
	}

	private void UpdateNavigationSelection()
	{
		foreach ((HelpNavigationEntry entry, Button button) in _entryButtons)
		{
			bool isSelected = ReferenceEquals(entry, _selectedEntry);
			button.BackgroundColor = isSelected ? NavigationSelectedBackgroundColor : NavigationTransparentColor;
			button.TextColor = isSelected ? NavigationSelectedTextColor : NavigationTextColor;
		}
	}

	private Style GetStyle(string key)
		=> (Style)Resources[key];

	private void ApplyDetailAccent(string accentColor)
	{
		Color color = TryParseColor(accentColor) ?? DetailAccentFallbackColor;
		HelpDetailAccentStroke.BackgroundColor = color;
	}

	private static Color? TryParseColor(string color)
	{
		if (string.IsNullOrWhiteSpace(color))
		{
			return null;
		}

		try
		{
			return Color.FromArgb(color);
		}
		catch (ArgumentException)
		{
			return null;
		}
	}

	private void BuildBodyContent(HelpItem item)
	{
		HelpDetailBodyStack.Clear();
		if (string.IsNullOrWhiteSpace(item.Body))
		{
			return;
		}

		bool isHeading = false;
		bool isBold = false;
		bool isAccent = false;
		bool isCode = false;
		bool isCopyBlock = false;
		var copyBlockLines = new List<string>();
		CopyBlockOptions copyBlockOptions = CopyBlockOptions.Default;
		foreach (string line in ResolveHelpVariables(item.Body).Replace("\r\n", "\n").Split('\n'))
		{
			string normalizedLine = line.TrimEnd('\r');
			string trimmedLine = normalizedLine.Trim();
			if (isCopyBlock)
			{
				if (string.Equals(trimmedLine, "[/copy]", StringComparison.OrdinalIgnoreCase))
				{
					AddBodyCopyBlock(copyBlockLines, copyBlockOptions);
					copyBlockLines.Clear();
					copyBlockOptions = CopyBlockOptions.Default;
					isCopyBlock = false;
				}
				else
				{
					copyBlockLines.Add(normalizedLine);
				}

				continue;
			}

			if (TryReadCopyBlockTag(trimmedLine, out copyBlockOptions))
			{
				isCopyBlock = true;
				copyBlockLines.Clear();
				continue;
			}

			AddBodyLine(normalizedLine, ref isHeading, ref isBold, ref isAccent, ref isCode);
		}

		if (isCopyBlock)
		{
			AddBodyCopyBlock(copyBlockLines, copyBlockOptions);
		}
	}

	private static string ResolveHelpVariables(string text)
	{
		foreach ((string key, Func<string> resolveValue) in HelpVariables)
		{
			string value = resolveValue();
			// {{name}} is safe inside tags such as [folder:{{comfyui.path}}|...].
			text = text.Replace($"{{{{{key}}}}}", value, StringComparison.OrdinalIgnoreCase);
			text = text.Replace($"[var:{key}]", value, StringComparison.OrdinalIgnoreCase);
		}

		return text;
	}

	private void AddBodyLine(string text, ref bool isHeading, ref bool isBold, ref bool isAccent, ref bool isCode)
	{
		text = text.TrimEnd('\r');
		string trimmed = text.Trim();

		if (trimmed == "---")
		{
			AddBodyDivider();
			return;
		}

		if (TryReadImageTag(trimmed, out string? image, out double? maxHeight))
		{
			AddBodyImage(image, maxHeight);
			return;
		}

		if (text == trimmed && TryReadLinkTag(trimmed, out string? label, out string? url))
		{
			AddBodyLink(label, url);
			return;
		}

		if (text == trimmed && TryReadFolderTag(trimmed, out label, out string? folderPath))
		{
			AddBodyFolderLink(label, folderPath);
			return;
		}

		if (text == trimmed &&
			TryReadArticleTag(trimmed, out label, out string? slug) &&
			TryFindEntryBySlug(slug, out HelpNavigationEntry? linkedEntry))
		{
			AddBodyArticleLink(label, linkedEntry!);
			return;
		}

		if (string.IsNullOrEmpty(text))
		{
			HelpDetailBodyStack.Add(CreateBodySpacer(8));
			return;
		}

		if (TryAddInlineActionLine(text, ref isHeading, ref isBold, ref isAccent, ref isCode))
		{
			return;
		}

		AddFormattedBodyLine(text, ref isHeading, ref isBold, ref isAccent, ref isCode);
	}

	private void AddFormattedBodyLine(string text, ref bool isHeading, ref bool isBold, ref bool isAccent, ref bool isCode)
	{
		FormattedString formatted = CreateFormattedBodyText(text, ref isHeading, ref isBold, ref isAccent, ref isCode);
		if (formatted.Spans.Count == 0)
		{
			return;
		}

		HelpDetailBodyStack.Add(new Label
		{
			FormattedText = formatted,
			LineHeight = isCode ? 1.22 : 1.34,
			LineBreakMode = LineBreakMode.WordWrap,
			Margin = IsHeadingLine(formatted) ? new Thickness(0, 8, 0, 6) : Thickness.Zero
		});
	}

	private static FormattedString CreateFormattedBodyText(string text, ref bool isHeading, ref bool isBold, ref bool isAccent, ref bool isCode)
	{
		var formatted = new FormattedString();
		int index = 0;
		while (index < text.Length)
		{
			if (TryReadFormattingTag(text, index, ref isHeading, ref isBold, ref isAccent, ref isCode, out int advance))
			{
				index += advance;
				continue;
			}

			int nextTag = FindNextFormattingTag(text, index);
			if (nextTag == index)
			{
				AddBodySpan(formatted, text[index].ToString(), isHeading, isBold, isAccent, isCode);
				index++;
				continue;
			}

			string spanText = nextTag < 0 ? text[index..] : text[index..nextTag];
			AddBodySpan(formatted, spanText, isHeading, isBold, isAccent, isCode);
			index = nextTag < 0 ? text.Length : nextTag;
		}

		return formatted;
	}

	private void AddFormattedInlineLabel(FlexLayout lineLayout, string text, ref bool isHeading, ref bool isBold, ref bool isAccent, ref bool isCode)
	{
		FormattedString formatted = CreateFormattedBodyText(text, ref isHeading, ref isBold, ref isAccent, ref isCode);
		if (formatted.Spans.Count == 0)
		{
			return;
		}

		lineLayout.Add(new Label
		{
			FormattedText = formatted,
			LineHeight = isCode ? 1.22 : 1.34,
			LineBreakMode = LineBreakMode.WordWrap,
			VerticalOptions = LayoutOptions.Center
		});
	}

	private bool TryAddInlineActionLine(string text, ref bool isHeading, ref bool isBold, ref bool isAccent, ref bool isCode)
	{
		// Action tags can live beside formatted text, e.g. "Path: [folder:...|...]".
		int openIndex = text.IndexOf('[', StringComparison.Ordinal);
		while (openIndex >= 0)
		{
			int closeIndex = text.IndexOf(']', openIndex);
			if (closeIndex <= openIndex)
			{
				return false;
			}

			string tag = NormalizeInlineTag(text[openIndex..(closeIndex + 1)]);
			bool isLink = TryReadLinkTag(tag, out string? linkLabel, out string? url);
			bool isFolder = TryReadFolderTag(tag, out string? folderLabel, out string? folderPath);
			HelpNavigationEntry? linkedEntry = null;
			bool isArticle = TryReadArticleTag(tag, out string? articleLabel, out string? articleSlug) &&
				TryFindEntryBySlug(articleSlug, out linkedEntry);
			if (!isLink && !isFolder && !isArticle)
			{
				openIndex = text.IndexOf('[', closeIndex + 1);
				continue;
			}

			var lineLayout = new FlexLayout
			{
				Direction = FlexDirection.Row,
				Wrap = FlexWrap.Wrap,
				AlignItems = FlexAlignItems.Center,
				Margin = new Thickness(0, 0, 0, 2)
			};

			string prefixText = text[..openIndex];
			int spacerCharacters = prefixText.Length - prefixText.TrimEnd().Length;
			AddFormattedInlineLabel(lineLayout, prefixText.TrimEnd(), ref isHeading, ref isBold, ref isAccent, ref isCode);
			AddInlineActionSpacer(lineLayout, spacerCharacters);
			HelpNavigationEntry? articleEntry = linkedEntry;
			lineLayout.Add(CreateBodyActionLinkLabel(
				isFolder ? folderLabel : isArticle ? articleLabel : linkLabel,
				isFolder
					? async () => await OpenFolderAsync(folderPath)
					: isArticle
						? () => NavigateToArticleAsync(articleEntry!)
						: async () => await OpenExternalAsync(url),
				isFolder));
			AddFormattedInlineLabel(lineLayout, text[(closeIndex + 1)..], ref isHeading, ref isBold, ref isAccent, ref isCode);
			HelpDetailBodyStack.Add(lineLayout);
			return true;
		}

		return false;
	}

	private static void AddInlineActionSpacer(FlexLayout lineLayout, int characterCount)
	{
		if (characterCount <= 0)
		{
			return;
		}

		lineLayout.Add(new BoxView
		{
			WidthRequest = Math.Min(24, characterCount * 5),
			HeightRequest = 1,
			Opacity = 0,
		});
	}

	private static bool IsHeadingLine(FormattedString formatted)
		=> formatted.Spans.Count > 0 && formatted.Spans.All(span => span.FontSize >= 16);

	private static void AddBodySpan(FormattedString formatted, string text, bool isHeading, bool isBold, bool isAccent, bool isCode)
	{
		if (string.IsNullOrEmpty(text))
		{
			return;
		}

		formatted.Spans.Add(new Span
		{
			Text = text,
			TextColor = isHeading
				? BodyHeadingColor
				: isCode
					? BodyCodeColor
					: isAccent
						? BodyAccentColor
						: BodyTextColor,
			FontSize = isHeading ? BodyHeadingFontSize : isCode ? BodyCodeFontSize : BodyTextFontSize,
			FontAttributes = isHeading || isBold ? FontAttributes.Bold : FontAttributes.None,
			FontFamily = isCode ? "Consolas" : null,
			CharacterSpacing = isHeading ? 0.6 : isCode ? 0.4 : 0,
		});
	}

	private void AddBodyLink(string label, string url)
		=> AddBodyActionLink(label, async () => await OpenExternalAsync(url), isFolderLink: false);

	private void AddBodyFolderLink(string label, string folderPath)
		=> AddBodyActionLink(label, async () => await OpenFolderAsync(folderPath), isFolderLink: true);

	private void AddBodyArticleLink(string label, HelpNavigationEntry entry)
		=> AddBodyActionLink(label, () => NavigateToArticleAsync(entry), isFolderLink: false);

	private void AddBodyActionLink(string label, Func<Task> action, bool isFolderLink)
		=> HelpDetailBodyStack.Add(CreateBodyActionLinkLabel(label, action, isFolderLink));

	private Label CreateBodyActionLinkLabel(string label, Func<Task> action, bool isFolderLink)
	{
		var linkLabel = new Label
		{
			Text = label,
			Style = GetStyle(isFolderLink ? "HelpFolderLinkLabelStyle" : "HelpLinkLabelStyle"),
			HorizontalOptions = LayoutOptions.Start,
			Margin = new Thickness(0, -2, 0, -2)
		};
		var pointer = new PointerGestureRecognizer();
		pointer.PointerEntered += (_, _) => ApplyHelpLinkHover(linkLabel, isHovered: true, isFolderLink);
		pointer.PointerExited += (_, _) => ApplyHelpLinkHover(linkLabel, isHovered: false, isFolderLink);
		linkLabel.GestureRecognizers.Add(pointer);
		linkLabel.GestureRecognizers.Add(new TapGestureRecognizer
		{
			Command = new Command(async () => await action())
		});
		return linkLabel;
	}

	private void AddBodyDivider()
	{
		HelpDetailBodyStack.Add(new BoxView
		{
			HeightRequest = 1,
			Margin = new Thickness(0, 6, 0, 8),
			Color = BodyDividerColor,
			Opacity = 0.72
		});
	}

	private static BoxView CreateBodySpacer(double height)
		=> new()
		{
			HeightRequest = height,
			Opacity = 0,
		};

	private void AddBodyImage(string image, double? maxHeight)
	{
		string resolvedImage = ResolveHelpImageSource(image);
		var helpImage = new Image
		{
			Source = resolvedImage,
			Aspect = Aspect.AspectFit,
			HorizontalOptions = LayoutOptions.Start,
			Margin = new Thickness(0, 14, 0, 8)
		};
		if (maxHeight.HasValue && TryGetImageDimensions(resolvedImage, out int imageWidth, out int imageHeight))
		{
			var imageHost = new Grid
			{
				HorizontalOptions = LayoutOptions.Fill,
				Margin = helpImage.Margin
			};
			helpImage.Margin = Thickness.Zero;
			imageHost.Add(helpImage);
			imageHost.SizeChanged += (_, _) => ApplyHelpImageSize(helpImage, imageHost.Width, imageWidth, imageHeight, maxHeight.Value);
			ApplyHelpImageSize(helpImage, imageHost.Width, imageWidth, imageHeight, maxHeight.Value);
			HelpDetailBodyStack.Add(imageHost);
			return;
		}

		if (!maxHeight.HasValue)
		{
			helpImage.HorizontalOptions = LayoutOptions.Fill;
		}

		HelpDetailBodyStack.Add(helpImage);
	}

	private static void ApplyHelpImageSize(Image image, double availableWidth, int imageWidth, int imageHeight, double maxHeight)
	{
		if (imageWidth <= 0 || imageHeight <= 0 || maxHeight <= 0)
		{
			return;
		}

		double widthLimit = availableWidth > 0 ? availableWidth : imageWidth;
		double scale = Math.Min(1, Math.Min(maxHeight / imageHeight, widthLimit / imageWidth));
		image.WidthRequest = imageWidth * scale;
		image.HeightRequest = imageHeight * scale;
	}

	private void AddBodyCopyBlock(IReadOnlyList<string> lines, CopyBlockOptions options)
	{
		string copyText = TrimCopyBlock(lines);
		if (copyText.Length == 0)
		{
			return;
		}

		var hintLabel = new Label
		{
			Text = options.Hint,
			TextColor = CopyHintColor,
			FontSize = CopyBlockHeaderFontSize,
			LineHeight = 1.12,
			LineBreakMode = LineBreakMode.WordWrap,
			VerticalOptions = LayoutOptions.Center
		};

		var copyActionLabel = new Label
		{
			Text = options.Button,
			TextColor = CopyLinkColor,
			FontSize = CopyBlockHeaderFontSize,
			FontAttributes = FontAttributes.Bold,
			TextDecorations = TextDecorations.Underline,
			HorizontalOptions = LayoutOptions.End,
			VerticalOptions = LayoutOptions.Center,
			Padding = new Thickness(4, 2)
		};
		ToolTipProperties.SetText(copyActionLabel, options.Tooltip);
		copyActionLabel.GestureRecognizers.Add(new TapGestureRecognizer
		{
			Command = new Command(async () => await CopyHelpBlockAsync(copyText, options, copyActionLabel, hintLabel))
		});
		var pointer = new PointerGestureRecognizer();
		pointer.PointerEntered += (_, _) => copyActionLabel.TextColor = CopyLinkHoverColor;
		pointer.PointerExited += (_, _) => copyActionLabel.TextColor = CopyLinkColor;
		copyActionLabel.GestureRecognizers.Add(pointer);

		var header = new Grid
		{
			ColumnDefinitions =
			{
				new ColumnDefinition { Width = GridLength.Star },
				new ColumnDefinition { Width = GridLength.Auto },
			},
			ColumnSpacing = 12
		};
		header.Add(hintLabel, 0);
		header.Add(copyActionLabel, 1);

		var bodyLabel = new Label
		{
			Text = copyText,
			TextColor = BodyCodeColor,
			FontSize = CopyBlockFontSize,
			FontFamily = "Consolas",
			LineHeight = 1.2,
			LineBreakMode = LineBreakMode.WordWrap,
			VerticalOptions = LayoutOptions.Center,
			Margin = new Thickness(0, 2, 0, 0)
		};

		var content = new VerticalStackLayout
		{
			Spacing = 9
		};
		content.Add(header);
		content.Add(bodyLabel);

		HelpDetailBodyStack.Add(new Border
		{
			BackgroundColor = CopyBlockBackgroundColor,
			Stroke = CopyBlockStrokeColor,
			StrokeThickness = 1,
			Padding = new Thickness(14, 12),
			Margin = new Thickness(0, 8, 0, 10),
			StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = CopyBlockCornerRadius },
			Content = content
		});
	}

	private static bool TryReadCopyBlockTag(string text, out CopyBlockOptions options)
	{
		const string Prefix = "[copy";
		options = CopyBlockOptions.Default;
		if (!text.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase) || !text.EndsWith(']'))
		{
			return false;
		}

		if (string.Equals(text, "[copy]", StringComparison.OrdinalIgnoreCase))
		{
			return true;
		}

		if (!text.StartsWith("[copy:", StringComparison.OrdinalIgnoreCase))
		{
			return false;
		}

		string[] parts = text["[copy:".Length..^1].Split('|');
		options = new CopyBlockOptions(
			GetPart(parts, 0, CopyBlockOptions.Default.Button),
			GetPart(parts, 1, CopyBlockOptions.Default.Hint),
			GetPart(parts, 2, CopyBlockOptions.Default.Tooltip),
			GetPart(parts, 3, CopyBlockOptions.Default.CopiedButton),
			GetPart(parts, 4, CopyBlockOptions.Default.CopiedHint));
		return true;
	}

	private static string GetPart(string[] parts, int index, string fallback)
		=> index < parts.Length && !string.IsNullOrWhiteSpace(parts[index])
			? parts[index].Trim()
			: fallback;

	private static string TrimCopyBlock(IReadOnlyList<string> lines)
	{
		int start = 0;
		int end = lines.Count - 1;
		while (start <= end && string.IsNullOrWhiteSpace(lines[start]))
		{
			start++;
		}

		while (end >= start && string.IsNullOrWhiteSpace(lines[end]))
		{
			end--;
		}

		return start > end
			? string.Empty
			: string.Join(Environment.NewLine, lines.Skip(start).Take(end - start + 1));
	}

	private static async Task CopyHelpBlockAsync(string copyText, CopyBlockOptions options, Label copyActionLabel, Label hintLabel)
	{
		try
		{
			await Clipboard.Default.SetTextAsync(copyText);
			copyActionLabel.Text = options.CopiedButton;
			hintLabel.Text = options.CopiedHint;
			hintLabel.TextColor = CopyLinkColor;
			await Task.Delay(CopyFeedbackResetDelayMs);
			if (copyActionLabel.Handler is not null)
			{
				copyActionLabel.Text = options.Button;
				hintLabel.Text = options.Hint;
				hintLabel.TextColor = CopyHintColor;
			}
		}
		catch (Exception ex)
		{
			NexusLog.Exception(ex, "[HELP] Failed to copy help block.");
		}
	}

	private static string ResolveHelpImageSource(string image)
	{
		if (string.IsNullOrWhiteSpace(image) ||
			image.IndexOfAny(['/', '\\']) >= 0)
		{
			return image;
		}

		string extension = Path.GetExtension(image);
		string baseName = extension.Length == 0 ? image : image[..^extension.Length];
		foreach (string languageCode in LocalizationManager.GetLanguageCandidates(LocalizationManager.ActiveLanguage))
		{
			if (string.Equals(languageCode, "en", StringComparison.OrdinalIgnoreCase))
			{
				continue;
			}

			string candidate = $"{baseName}.{GetHelpImageLanguageSuffix(languageCode)}{extension}";
			if (TryResolveHelpImagePath(candidate, out string resolvedImage))
			{
				return resolvedImage;
			}
		}

		return TryResolveHelpImagePath(image, out string fallbackImage)
			? fallbackImage
			: image;
	}

	private static string GetHelpImageLanguageSuffix(string languageCode)
		=> languageCode switch
		{
			"zh-Hans" => "zhHans",
			"zh-Hant" => "zhHant",
			_ => languageCode
		};

	private static bool TryResolveHelpImagePath(string image, out string resolvedImage)
	{
		foreach (string root in GetHelpImageRoots())
		{
			foreach (string candidate in EnumerateHelpImageCandidates(root, image))
			{
				if (File.Exists(candidate))
				{
					resolvedImage = candidate;
					return true;
				}
			}
		}

		resolvedImage = image;
		return false;
	}

	private static bool TryGetImageDimensions(string imagePath, out int width, out int height)
	{
		width = 0;
		height = 0;
		if (!File.Exists(imagePath))
		{
			return false;
		}

		try
		{
			using FileStream stream = File.OpenRead(imagePath);
			Span<byte> header = stackalloc byte[24];
			if (stream.Read(header) < header.Length)
			{
				return false;
			}

			if (TryReadPngDimensions(header, out width, out height))
			{
				return true;
			}

			stream.Position = 0;
			return TryReadJpegDimensions(stream, out width, out height);
		}
		catch (IOException)
		{
			return false;
		}
		catch (UnauthorizedAccessException)
		{
			return false;
		}
	}

	private static bool TryReadPngDimensions(ReadOnlySpan<byte> header, out int width, out int height)
	{
		width = 0;
		height = 0;
		ReadOnlySpan<byte> pngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
		if (header.Length < 24 || !header[..8].SequenceEqual(pngSignature))
		{
			return false;
		}

		width = ReadBigEndianInt32(header[16..20]);
		height = ReadBigEndianInt32(header[20..24]);
		return width > 0 && height > 0;
	}

	private static bool TryReadJpegDimensions(Stream stream, out int width, out int height)
	{
		width = 0;
		height = 0;
		if (stream.ReadByte() != 0xFF || stream.ReadByte() != 0xD8)
		{
			return false;
		}

		while (stream.Position < stream.Length)
		{
			if (stream.ReadByte() != 0xFF)
			{
				continue;
			}

			int marker = stream.ReadByte();
			if (marker < 0)
			{
				return false;
			}

			while (marker == 0xFF)
			{
				marker = stream.ReadByte();
			}

			if (marker is 0xD8 or 0xD9)
			{
				continue;
			}

			int segmentLength = ReadBigEndianUInt16(stream);
			if (segmentLength < 2)
			{
				return false;
			}

			if (IsJpegStartOfFrame(marker))
			{
				stream.ReadByte();
				height = ReadBigEndianUInt16(stream);
				width = ReadBigEndianUInt16(stream);
				return width > 0 && height > 0;
			}

			stream.Seek(segmentLength - 2, SeekOrigin.Current);
		}

		return false;
	}

	private static bool IsJpegStartOfFrame(int marker)
		=> marker is >= 0xC0 and <= 0xCF and not 0xC4 and not 0xC8 and not 0xCC;

	private static int ReadBigEndianInt32(ReadOnlySpan<byte> bytes)
		=> (bytes[0] << 24) | (bytes[1] << 16) | (bytes[2] << 8) | bytes[3];

	private static int ReadBigEndianUInt16(Stream stream)
	{
		int high = stream.ReadByte();
		int low = stream.ReadByte();
		return high < 0 || low < 0
			? 0
			: (high << 8) | low;
	}

	private static IEnumerable<string> GetHelpImageRoots()
	{
		if (ReloadContentOnTabChange && TryFindWorkspaceRoot(out string workspaceRoot))
		{
			yield return workspaceRoot;
		}

		yield return AppContext.BaseDirectory;
		yield return Directory.GetCurrentDirectory();
	}

	private static bool TryFindWorkspaceRoot(out string workspaceRoot)
	{
		for (string? root = Directory.GetCurrentDirectory(); root != null; root = Path.GetDirectoryName(root))
		{
			if (File.Exists(Path.Combine(root, "ComfyUI-Nexus.csproj")))
			{
				workspaceRoot = root;
				return true;
			}
		}

		for (string? root = AppContext.BaseDirectory; root != null; root = Path.GetDirectoryName(root))
		{
			if (File.Exists(Path.Combine(root, "ComfyUI-Nexus.csproj")))
			{
				workspaceRoot = root;
				return true;
			}
		}

		workspaceRoot = string.Empty;
		return false;
	}

	private static IEnumerable<string> EnumerateHelpImageCandidates(string root, string image)
	{
		yield return Path.Combine(root, image);
		yield return Path.Combine(root, "Resources", "Help", "Images", image);

		string extension = Path.GetExtension(image);
		if (extension.Length == 0)
		{
			yield break;
		}

		string baseName = image[..^extension.Length];
		yield return Path.Combine(root, $"{baseName}.scale-100{extension}");
		yield return Path.Combine(root, "Resources", "Help", "Images", $"{baseName}.scale-100{extension}");
	}

	private static bool TryReadFormattingTag(
		string text,
		int index,
		ref bool isHeading,
		ref bool isBold,
		ref bool isAccent,
		ref bool isCode,
		out int advance)
	{
		if (TryReadFormattingTag(text, index, "[h]", ref isHeading, true, out advance) ||
			TryReadFormattingTag(text, index, "[/h]", ref isHeading, false, out advance) ||
			TryReadFormattingTag(text, index, "[b]", ref isBold, true, out advance) ||
			TryReadFormattingTag(text, index, "[/b]", ref isBold, false, out advance) ||
			TryReadFormattingTag(text, index, "[accent]", ref isAccent, true, out advance) ||
			TryReadFormattingTag(text, index, "[/accent]", ref isAccent, false, out advance) ||
			TryReadFormattingTag(text, index, "[code]", ref isCode, true, out advance) ||
			TryReadFormattingTag(text, index, "[/code]", ref isCode, false, out advance))
		{
			return true;
		}

		advance = 0;
		return false;
	}

	private static bool TryReadFormattingTag(string text, int index, string tag, ref bool flag, bool value, out int advance)
	{
		if (text.IndexOf(tag, index, StringComparison.Ordinal) == index)
		{
			flag = value;
			advance = tag.Length;
			return true;
		}

		advance = 0;
		return false;
	}

	private static int FindNextFormattingTag(string text, int startIndex)
	{
		int nextIndex = -1;
		foreach (string tag in FormattingTags)
		{
			int index = text.IndexOf(tag, startIndex, StringComparison.Ordinal);
			if (index >= 0 && (nextIndex < 0 || index < nextIndex))
			{
				nextIndex = index;
			}
		}

		return nextIndex;
	}

	private static bool TryReadImageTag(string text, out string image, out double? maxHeight)
	{
		const string Prefix = "[img:";
		image = string.Empty;
		maxHeight = null;
		if (!text.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase) || !text.EndsWith(']'))
		{
			return false;
		}

		string[] parts = text[Prefix.Length..^1].Split('|', 2);
		image = parts[0].Trim();
		if (parts.Length == 2 &&
			double.TryParse(parts[1].Trim(), CultureInfo.InvariantCulture, out double parsedHeight) &&
			parsedHeight > 0)
		{
			maxHeight = parsedHeight;
		}

		return image.Length > 0;
	}

	private static bool TryReadLinkTag(string text, out string label, out string url)
	{
		const string LinkPrefix = "[link:";
		const string UrlPrefix = "[url:";
		label = string.Empty;
		url = string.Empty;

		string prefix = text.StartsWith(LinkPrefix, StringComparison.OrdinalIgnoreCase)
			? LinkPrefix
			: text.StartsWith(UrlPrefix, StringComparison.OrdinalIgnoreCase)
				? UrlPrefix
				: string.Empty;
		if (prefix.Length == 0 || !text.EndsWith(']'))
		{
			return false;
		}

		string payload = text[prefix.Length..^1];
		string[] parts = payload.Split('|', 2);
		if (parts.Length != 2)
		{
			return false;
		}

		label = parts[0];
		url = parts[1].Trim();
		if (string.IsNullOrWhiteSpace(label))
		{
			label = url;
		}

		return url.Length > 0;
	}

	private static bool TryReadFolderTag(string text, out string label, out string folderPath)
	{
		const string Prefix = "[folder:";
		label = string.Empty;
		folderPath = string.Empty;
		if (!text.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase) || !text.EndsWith(']'))
		{
			return false;
		}

		string payload = text[Prefix.Length..^1];
		string[] parts = payload.Split('|', 2);
		if (parts.Length != 2)
		{
			return false;
		}

		label = parts[0].Trim();
		folderPath = parts[1].Trim();
		if (label.Length == 0)
		{
			label = folderPath;
		}

		return folderPath.Length > 0;
	}

	private static bool TryReadArticleTag(string text, out string label, out string slug)
	{
		const string Prefix = "[article:";
		label = string.Empty;
		slug = string.Empty;
		if (!text.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase) || !text.EndsWith(']'))
		{
			return false;
		}

		string payload = text[Prefix.Length..^1];
		string[] parts = payload.Split('|', 2);
		if (parts.Length != 2)
		{
			return false;
		}

		label = parts[0];
		slug = parts[1].Trim();
		return !string.IsNullOrWhiteSpace(label) && slug.Length > 0;
	}

	private static string NormalizeInlineTag(string tag)
	{
		if (tag.Length < 2 || tag[0] != '[' || tag[^1] != ']')
		{
			return tag;
		}

		return $"[{tag[1..^1].Trim()}]";
	}

	private void OnCloseClicked(object? sender, EventArgs e) => CloseRequested?.Invoke(this, e);

	private Task NavigateToArticleAsync(HelpNavigationEntry entry)
	{
		SelectEntry(entry);
		return Task.CompletedTask;
	}

	private bool TryFindEntryBySlug(string slug, out HelpNavigationEntry? entry)
	{
		entry = _entries.FirstOrDefault(candidate =>
			string.Equals(candidate.Item.Slug, slug, StringComparison.OrdinalIgnoreCase));
		return entry is not null;
	}

	private static void ApplyHelpLinkHover(Label label, bool isHovered, bool isFolderLink)
	{
		label.TextColor = isHovered
			? CopyLinkHoverColor
			: isFolderLink
				? FolderLinkColor
				: CopyLinkColor;
		label.BackgroundColor = isFolderLink
			? isHovered ? FolderLinkHoverBackgroundColor : FolderLinkBackgroundColor
			: Colors.Transparent;
	}

	private static async Task OpenExternalAsync(string url)
	{
		try
		{
			await Launcher.Default.OpenAsync(url);
		}
		catch (Exception ex)
		{
			NexusLog.Exception(ex, $"[HELP] Failed to open external link: {url}");
		}
	}

	private static async Task OpenFolderAsync(string folderPath)
	{
		try
		{
			string resolvedPath = ResolveFolderPath(folderPath);
			Directory.CreateDirectory(resolvedPath);
			if (OperatingSystem.IsWindows())
			{
				Process.Start(new ProcessStartInfo
				{
					FileName = "explorer.exe",
					Arguments = $"\"{resolvedPath}\"",
					UseShellExecute = true,
				});
				return;
			}

			await Launcher.Default.OpenAsync(new Uri(resolvedPath));
		}
		catch (Exception ex)
		{
			NexusLog.Exception(ex, $"[HELP] Failed to open folder: {folderPath}");
		}
	}

	private static string ResolveFolderPath(string folderPath)
	{
		// Help articles store ComfyUI-relative folders; resolve them at click time.
		string normalizedPath = folderPath
			.Trim()
			.Replace('/', Path.DirectorySeparatorChar);
		if (!Path.IsPathRooted(normalizedPath))
		{
			normalizedPath = normalizedPath.TrimStart('.', Path.DirectorySeparatorChar);
		}

		return Path.GetFullPath(Path.IsPathRooted(normalizedPath)
			? normalizedPath
			: Path.Combine(ComfyPathResolver.ResolveConfiguredComfyPath(), normalizedPath));
	}

	private sealed record HelpNavigationEntry(HelpSection Section, HelpItem Item);

	private sealed record CopyBlockOptions(
		string Button,
		string Hint,
		string Tooltip,
		string CopiedButton,
		string CopiedHint)
	{
		public static CopyBlockOptions Default { get; } = new(
			"Copy text",
			"Copy this block and keep it in your own notes.",
			"Copy this tutorial block to the clipboard.",
			"Copied",
			"Copied to clipboard.");
	}
}
