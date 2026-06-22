using Microsoft.Maui.Controls;

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
			_ = SelectedHoverGlow.FadeToAsync(1, 130, Easing.CubicOut);
			_ = Chevron.FadeToAsync(0.95, 130, Easing.CubicOut);
			_ = SeamGlow.FadeToAsync(0.75, 150, Easing.CubicOut);
		}
		else if (sender == CandidateOptionA)
		{
			_ = CandidateHoverGlowA.FadeToAsync(1, 130, Easing.CubicOut);
		}
		else if (sender == CandidateOptionB)
		{
			_ = CandidateHoverGlowB.FadeToAsync(1, 130, Easing.CubicOut);
		}
	}

	private void OnOptionPointerExited(object? sender, PointerEventArgs e)
	{
		if (sender == SelectedOption)
		{
			_ = SelectedHoverGlow.FadeToAsync(0, 150, Easing.CubicIn);
			_ = Chevron.FadeToAsync(_isExpanded ? 0.9 : 0.55, 150, Easing.CubicIn);
			_ = SeamGlow.FadeToAsync(_isExpanded ? 0.45 : 0, 170, Easing.CubicIn);
		}
		else if (sender == CandidateOptionA)
		{
			_ = CandidateHoverGlowA.FadeToAsync(0, 150, Easing.CubicIn);
		}
		else if (sender == CandidateOptionB)
		{
			_ = CandidateHoverGlowB.FadeToAsync(0, 150, Easing.CubicIn);
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

		var heightAnimation = new Animation(
			SetOptionsHeight,
			OptionsClip.HeightRequest,
			targetHeight,
			Easing.CubicOut);
		heightAnimation.Commit(OptionsClip, "RunModeOptionsHeight", length: 160);

		_ = Chevron.RotateToAsync(isExpanded ? -90 : 90, 150, Easing.CubicOut);
		_ = Chevron.FadeToAsync(isExpanded ? 0.9 : 0.55, 130, Easing.CubicOut);
		_ = SeamGlow.FadeToAsync(isExpanded ? 0.45 : 0, 150, Easing.CubicOut);

		_ = Task.WhenAll(
			FirstDivider.FadeToAsync(isExpanded ? 0.65 : 0, 120, Easing.CubicOut),
			CandidateOptionA.FadeToAsync(isExpanded ? 1 : 0, 120, Easing.CubicOut),
			SecondDivider.FadeToAsync(isExpanded ? 0.65 : 0, 120, Easing.CubicOut),
			CandidateOptionB.FadeToAsync(isExpanded ? 1 : 0, 120, Easing.CubicOut))
			.ContinueWith(_ =>
			{
				MainThread.BeginInvokeOnMainThread(() =>
				{
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
				});
			});
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
