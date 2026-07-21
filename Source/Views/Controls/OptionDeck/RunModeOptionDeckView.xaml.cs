using Microsoft.Maui.Controls;
using ComfyUI_Nexus.Ui;

namespace ComfyUI_Nexus.Views.Controls.OptionDeck;

/// <summary>
/// Compact three-option deck used by the header run-mode selector.
/// </summary>
/// <remarks>
/// The selected item remains pinned at the top while the remaining candidates unfold downward.
/// </remarks>
public partial class RunModeOptionDeckView : ContentView
{
	private const double CollapsedHeight = 48;
	private const double ExpandedHeight = 96;
	private const string HoverAnimationName = "RunModeOptionDeck.Hover";
	private const string ExpansionAnimationName = "RunModeOptionDeck.Expansion";

	private readonly List<RunModeOptionDeckItem> _items = [];
	private bool _isExpanded;
	private bool _isAnimating;
	private bool _isPointerOverHost;
	private RunModeOptionDeckItem? _selectedItem;
	private RunModeOptionDeckItem? _candidateA;
	private RunModeOptionDeckItem? _candidateB;

	internal event Action<string>? SelectionChanged;

	public RunModeOptionDeckView()
	{
		InitializeComponent();
		Unloaded += OnUnloaded;
		SetExpanded(false, animate: false);
	}

	/// <summary>
	/// Replaces the option set and pins the selected value to the first visible row.
	/// </summary>
	/// <param name="items">Available options. The current implementation expects one selected item plus up to two candidates.</param>
	/// <param name="selectedValue">Stable value that should be shown as selected.</param>
	internal void SetOptions(IEnumerable<RunModeOptionDeckItem> items, string selectedValue)
	{
		_items.Clear();
		_items.AddRange(items);

		ApplySelection(selectedValue);
	}

	/// <summary>
	/// Collapses the deck after an outside pointer release when the pointer is not over the deck.
	/// </summary>
	internal void DismissFromGlobalPointerRelease()
	{
		if (_isAnimating || !_isExpanded || _isPointerOverHost)
		{
			return;
		}

		SetExpanded(false, animate: true);
	}

	private void OnOptionPointerEntered(object? sender, PointerEventArgs e)
	{
		if (sender == SelectedOption)
		{
			PlayHoverAnimation(1, 0.95, 0.75, CandidateHoverGlowA.Opacity, CandidateHoverGlowB.Opacity, 150, Easing.CubicOut);
		}
		else if (sender == CandidateOptionA)
		{
			PlayHoverAnimation(SelectedHoverGlow.Opacity, Chevron.Opacity, SeamGlow.Opacity, 1, CandidateHoverGlowB.Opacity, 130, Easing.CubicOut);
		}
		else if (sender == CandidateOptionB)
		{
			PlayHoverAnimation(SelectedHoverGlow.Opacity, Chevron.Opacity, SeamGlow.Opacity, CandidateHoverGlowA.Opacity, 1, 130, Easing.CubicOut);
		}
	}

	private void OnOptionPointerExited(object? sender, PointerEventArgs e)
	{
		if (sender == SelectedOption)
		{
			PlayHoverAnimation(0, _isExpanded ? 0.9 : 0.55, _isExpanded ? 0.45 : 0, CandidateHoverGlowA.Opacity, CandidateHoverGlowB.Opacity, 170, Easing.CubicIn);
		}
		else if (sender == CandidateOptionA)
		{
			PlayHoverAnimation(SelectedHoverGlow.Opacity, Chevron.Opacity, SeamGlow.Opacity, 0, CandidateHoverGlowB.Opacity, 150, Easing.CubicIn);
		}
		else if (sender == CandidateOptionB)
		{
			PlayHoverAnimation(SelectedHoverGlow.Opacity, Chevron.Opacity, SeamGlow.Opacity, CandidateHoverGlowA.Opacity, 0, 150, Easing.CubicIn);
		}
	}

	private void OnToggleClicked(object? sender, TappedEventArgs e)
	{
		if (_isAnimating)
		{
			return;
		}

		SetExpanded(!_isExpanded, animate: true);
	}

	private void OnOptionsHostPointerEntered(object? sender, PointerEventArgs e)
	{
		_isPointerOverHost = true;
	}

	private void OnOptionsHostPointerExited(object? sender, PointerEventArgs e)
	{
		_isPointerOverHost = false;
	}

	private void OnCandidateClicked(object? sender, TappedEventArgs e)
	{
		if (_isAnimating)
		{
			return;
		}

		RunModeOptionDeckItem? requestedItem = sender == CandidateOptionA
			? _candidateA
			: _candidateB;

		if (requestedItem is null)
		{
			return;
		}

		ApplySelection(requestedItem.Value);
		SetExpanded(false, animate: true);
		SelectionChanged?.Invoke(requestedItem.Value);
	}

	/// <summary>
	/// Expands or collapses the deck using the top row as a visual pivot.
	/// </summary>
	/// <param name="isExpanded">True to show candidate rows; false to clip to the selected row.</param>
	/// <param name="animate">True to animate height, chevron, seam, and candidate opacity.</param>
	internal void SetExpanded(bool isExpanded, bool animate)
	{
		if (_isExpanded == isExpanded && animate)
		{
			return;
		}

		_isExpanded = isExpanded;
		double targetHeight = isExpanded ? ExpandedHeight : CollapsedHeight;

		if (isExpanded)
		{
			FirstDivider.IsVisible = true;
			CandidateOptionA.IsVisible = true;
			SecondDivider.IsVisible = true;
			CandidateOptionB.IsVisible = true;
		}

		if (!animate)
		{
			_isAnimating = false;
			SetOptionsHeight(targetHeight);
			Chevron.Rotation = isExpanded ? -90 : 90;
			Chevron.Opacity = isExpanded ? 0.9 : 0.55;
			SeamGlow.Opacity = isExpanded ? 0.45 : 0;
			FirstDivider.Opacity = isExpanded ? 0.65 : 0;
			CandidateOptionA.Opacity = isExpanded ? 1 : 0;
			SecondDivider.Opacity = isExpanded ? 0.65 : 0;
			CandidateOptionB.Opacity = isExpanded ? 1 : 0;

			if (!isExpanded)
			{
				FirstDivider.IsVisible = false;
				CandidateOptionA.IsVisible = false;
				SecondDivider.IsVisible = false;
				CandidateOptionB.IsVisible = false;
			}

			return;
		}

		_isAnimating = true;
		SelectedOption.InputTransparent = true;
		CandidateOptionA.InputTransparent = true;
		CandidateOptionB.InputTransparent = true;

		Animation expansionAnimation = SafeAnimation.Composite(
			new SafeAnimation.TimelineSegment(0, 1, SetOptionsHeight, OptionsClip.HeightRequest, targetHeight, Easing.CubicOut),
			new SafeAnimation.TimelineSegment(0, 0.94, value => Chevron.Rotation = value, Chevron.Rotation, isExpanded ? -90 : 90, Easing.CubicOut),
			new SafeAnimation.TimelineSegment(0, 0.82, value => Chevron.Opacity = value, Chevron.Opacity, isExpanded ? 0.9 : 0.55, Easing.CubicOut),
			new SafeAnimation.TimelineSegment(0, 0.94, value => SeamGlow.Opacity = value, SeamGlow.Opacity, isExpanded ? 0.45 : 0, Easing.CubicOut),
			new SafeAnimation.TimelineSegment(0, 0.75, value => FirstDivider.Opacity = value, FirstDivider.Opacity, isExpanded ? 0.65 : 0, Easing.CubicOut),
			new SafeAnimation.TimelineSegment(0, 0.75, value => CandidateOptionA.Opacity = value, CandidateOptionA.Opacity, isExpanded ? 1 : 0, Easing.CubicOut),
			new SafeAnimation.TimelineSegment(0, 0.75, value => SecondDivider.Opacity = value, SecondDivider.Opacity, isExpanded ? 0.65 : 0, Easing.CubicOut),
			new SafeAnimation.TimelineSegment(0, 0.75, value => CandidateOptionB.Opacity = value, CandidateOptionB.Opacity, isExpanded ? 1 : 0, Easing.CubicOut));
		SafeAnimation.Commit(
			expansionAnimation,
			this,
			ExpansionAnimationName,
			16,
			160,
			null,
			(_, wasCancelled) => FinishExpansion(isExpanded, wasCancelled),
			null,
			"RunModeOptionDeck");
	}

	private void PlayHoverAnimation(double selectedGlowOpacity, double chevronOpacity, double seamGlowOpacity, double candidateGlowAOpacity, double candidateGlowBOpacity, uint length, Easing easing)
	{
		SafeAnimation.Timeline(
			this,
			HoverAnimationName,
			16,
			length,
			null,
			null,
			"RunModeOptionDeck",
			new SafeAnimation.TimelineSegment(0, 1, value => SelectedHoverGlow.Opacity = value, SelectedHoverGlow.Opacity, selectedGlowOpacity, easing),
			new SafeAnimation.TimelineSegment(0, 1, value => Chevron.Opacity = value, Chevron.Opacity, chevronOpacity, easing),
			new SafeAnimation.TimelineSegment(0, 1, value => SeamGlow.Opacity = value, SeamGlow.Opacity, seamGlowOpacity, easing),
			new SafeAnimation.TimelineSegment(0, 1, value => CandidateHoverGlowA.Opacity = value, CandidateHoverGlowA.Opacity, candidateGlowAOpacity, easing),
			new SafeAnimation.TimelineSegment(0, 1, value => CandidateHoverGlowB.Opacity = value, CandidateHoverGlowB.Opacity, candidateGlowBOpacity, easing));
	}

	private void FinishExpansion(bool isExpanded, bool wasCancelled)
	{
		if (wasCancelled)
		{
			return;
		}

		_isAnimating = false;
		SelectedOption.InputTransparent = false;
		CandidateOptionA.InputTransparent = false;
		CandidateOptionB.InputTransparent = false;

		if (!isExpanded)
		{
			FirstDivider.IsVisible = false;
			CandidateOptionA.IsVisible = false;
			SecondDivider.IsVisible = false;
			CandidateOptionB.IsVisible = false;
		}
	}

	private void OnUnloaded(object? sender, EventArgs e)
	{
		SafeAnimation.AbortAnimation(this, HoverAnimationName, "RunModeOptionDeck");
		SafeAnimation.AbortAnimation(this, ExpansionAnimationName, "RunModeOptionDeck");
		_isAnimating = false;
	}

	private void SetOptionsHeight(double height)
	{
		OptionsClip.HeightRequest = height;
		OptionsFrame.HeightRequest = height;
	}

	private void ApplySelection(string selectedValue)
	{
		if (_items.Count == 0)
		{
			return;
		}

		_selectedItem = _items.FirstOrDefault(item => string.Equals(item.Value, selectedValue, StringComparison.OrdinalIgnoreCase))
			?? _items[0];

		RunModeOptionDeckItem[] candidates = _items
			.Where(item => !string.Equals(item.Value, _selectedItem.Value, StringComparison.OrdinalIgnoreCase))
			.Take(2)
			.ToArray();

		_candidateA = candidates.Length > 0 ? candidates[0] : null;
		_candidateB = candidates.Length > 1 ? candidates[1] : null;

		ApplyItem(SelectedIcon, SelectedLabel, _selectedItem);
		ApplyItem(CandidateIconA, CandidateLabelA, _candidateA);
		ApplyItem(CandidateIconB, CandidateLabelB, _candidateB);
	}

	private static void ApplyItem(Image icon, Label label, RunModeOptionDeckItem? item)
	{
		if (item is null)
		{
			icon.Source = null;
			label.Text = string.Empty;
			return;
		}

		icon.Source = item.Icon;
		label.Text = item.Text;
		label.TextColor = item.TextColor;
	}
}
