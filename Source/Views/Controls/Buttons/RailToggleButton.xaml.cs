using System;
using ComfyUI_Nexus.Ui;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;

namespace ComfyUI_Nexus.Views.Controls.Buttons;

public partial class RailToggleButton : ContentView, IRailHoverParticipant
{
	private static readonly Color TransparentBackgroundColor = Color.FromRgba(0, 0, 0, 0.01);
	private const double AnimationSnapThreshold = 0.001;
	private const string GlowAnimationName = "RailToggleButton.Glow";
	private bool _isPointerOver;

	public static readonly BindableProperty IconSourceProperty =
		BindableProperty.Create(nameof(IconSource), typeof(ImageSource), typeof(RailToggleButton), default(ImageSource), propertyChanged: OnIconSourceChanged);

	public static readonly BindableProperty ActiveIconSourceProperty =
		BindableProperty.Create(nameof(ActiveIconSource), typeof(ImageSource), typeof(RailToggleButton), default(ImageSource), propertyChanged: OnIconSourceChanged);

	public static readonly BindableProperty TooltipTextProperty =
		BindableProperty.Create(nameof(TooltipText), typeof(string), typeof(RailToggleButton), string.Empty, propertyChanged: OnTooltipTextChanged);

	public static readonly BindableProperty IsSelectedProperty =
		BindableProperty.Create(nameof(IsSelected), typeof(bool), typeof(RailToggleButton), false, propertyChanged: OnIsSelectedChanged);

	public event EventHandler? Clicked;

	public ImageSource? IconSource
	{
		get => (ImageSource?)GetValue(IconSourceProperty);
		set => SetValue(IconSourceProperty, value);
	}

	public ImageSource? ActiveIconSource
	{
		get => (ImageSource?)GetValue(ActiveIconSourceProperty);
		set => SetValue(ActiveIconSourceProperty, value);
	}

	public string TooltipText
	{
		get => (string)GetValue(TooltipTextProperty);
		set => SetValue(TooltipTextProperty, value);
	}

	public bool IsSelected
	{
		get => (bool)GetValue(IsSelectedProperty);
		set => SetValue(IsSelectedProperty, value);
	}

	public RailToggleButton()
	{
		InitializeComponent();

		var tap = new TapGestureRecognizer();
		tap.Tapped += (s, e) => OnTapped();
		GestureRecognizers.Add(tap);

		var pointer = new PointerGestureRecognizer();
		pointer.PointerEntered += (s, e) => OnPointerEntered();
		pointer.PointerExited += (s, e) => OnPointerExited();
		GestureRecognizers.Add(pointer);

		ApplyTooltip();
		UpdateVisualState();
		Loaded += OnLoaded;
		Unloaded += OnUnloaded;
	}

	private void OnTapped()
	{
		Clicked?.Invoke(this, EventArgs.Empty);
		RailButtonVisuals.FlashOpacity(
			this,
			GlowAnimationName,
			GlowSurface,
			IsSelected ? 0.16 : 0,
			0.7,
			190,
			Easing.CubicOut,
			"RailToggleButton",
			AnimationSnapThreshold);
	}

	private void OnPointerEntered()
	{
		if (_isPointerOver)
		{
			return;
		}

		ApplyHoverState(isPointerOver: true);
	}

	private void OnPointerExited()
	{
		if (!_isPointerOver)
		{
			return;
		}

		ApplyHoverState(isPointerOver: false);
	}

	void IRailHoverParticipant.ResetRailHover()
	{
		ApplyHoverState(isPointerOver: false, force: true);
	}

	private static void OnIsSelectedChanged(BindableObject bindable, object oldValue, object newValue)
	{
		if (bindable is RailToggleButton button)
		{
			button.UpdateVisualState();
		}
	}

	private void UpdateVisualState()
	{
		ApplyIconSource();
		ApplyHoverState(isPointerOver: false, force: true);
	}

	private void ApplyHoverState(bool isPointerOver, bool force = false)
	{
		if (!force && _isPointerOver == isPointerOver)
		{
			return;
		}

		_isPointerOver = isPointerOver;
		ButtonBorder.BackgroundColor = TransparentBackgroundColor;
		SafeAnimation.AbortAnimation(this, GlowAnimationName, "RailToggleButton");
		GlowSurface.Opacity = isPointerOver ? 0.62 : 0;
	}

	private void OnLoaded(object? sender, EventArgs e)
		=> GetHoverRegistry()?.Register(this);

	private void OnUnloaded(object? sender, EventArgs e)
	{
		GetHoverRegistry()?.Unregister(this);
		SafeAnimation.AbortAnimation(this, GlowAnimationName, "RailToggleButton");
	}

	private static NexusRailHoverRegistry? GetHoverRegistry()
		=> NexusAppManager.Instance.RailHoverRegistry;

	private static void OnIconSourceChanged(BindableObject bindable, object oldValue, object newValue)
	{
		if (bindable is RailToggleButton button)
		{
			button.ApplyIconSource();
		}
	}

	private static void OnTooltipTextChanged(BindableObject bindable, object oldValue, object newValue)
	{
		if (bindable is RailToggleButton button)
		{
			button.ApplyTooltip();
		}
	}

	private void ApplyTooltip()
	{
		RailButtonVisuals.ApplyTooltip(this, TooltipText);
	}

	private void ApplyIconSource()
	{
		RailButtonVisuals.ApplyIconPair(NormalIconImage, ActiveIconImage, IsSelected, ActiveIconSource);
	}
}
