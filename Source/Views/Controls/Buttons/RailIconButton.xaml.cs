using System;
using ComfyUI_Nexus.Ui;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;

namespace ComfyUI_Nexus.Views.Controls.Buttons;

public partial class RailIconButton : ContentView, IRailHoverParticipant
{
	private static readonly Color TransparentBackgroundColor = Color.FromRgba(0, 0, 0, 0.01);
	private const double AnimationSnapThreshold = 0.001;
	private const string OpacityAnimationName = "RailIconButton.Opacity";
	private const string GlowAnimationName = "RailIconButton.Glow";
	private bool _isPointerOver;

	public static readonly BindableProperty IconSourceProperty =
		BindableProperty.Create(nameof(IconSource), typeof(ImageSource), typeof(RailIconButton), default(ImageSource), propertyChanged: OnIconSourceChanged);

	public static readonly BindableProperty ActiveIconSourceProperty =
		BindableProperty.Create(nameof(ActiveIconSource), typeof(ImageSource), typeof(RailIconButton), default(ImageSource), propertyChanged: OnIconSourceChanged);

	public static readonly BindableProperty TooltipTextProperty =
		BindableProperty.Create(nameof(TooltipText), typeof(string), typeof(RailIconButton), string.Empty, propertyChanged: OnTooltipTextChanged);

	public static readonly BindableProperty IdleOpacityProperty =
		BindableProperty.Create(nameof(IdleOpacity), typeof(double), typeof(RailIconButton), 1.0, propertyChanged: OnIdleOpacityChanged);

	public static readonly BindableProperty IsSelectedProperty =
		BindableProperty.Create(nameof(IsSelected), typeof(bool), typeof(RailIconButton), false, propertyChanged: OnIsSelectedChanged);

	public static readonly BindableProperty CornerRadiusProperty =
		BindableProperty.Create(nameof(CornerRadius), typeof(CornerRadius), typeof(RailIconButton), new CornerRadius(9));

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

	public double IdleOpacity
	{
		get => (double)GetValue(IdleOpacityProperty);
		set => SetValue(IdleOpacityProperty, value);
	}

	public bool IsSelected
	{
		get => (bool)GetValue(IsSelectedProperty);
		set => SetValue(IsSelectedProperty, value);
	}

	public CornerRadius CornerRadius
	{
		get => (CornerRadius)GetValue(CornerRadiusProperty);
		set => SetValue(CornerRadiusProperty, value);
	}

	public RailIconButton()
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
		Opacity = IdleOpacity;
		ApplySelectionState();
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
			"RailIconButton",
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

	private static void OnIdleOpacityChanged(BindableObject bindable, object oldValue, object newValue)
	{
		if (bindable is RailIconButton button)
		{
			button.Opacity = (double)newValue;
		}
	}

	private static void OnTooltipTextChanged(BindableObject bindable, object oldValue, object newValue)
	{
		if (bindable is RailIconButton button)
		{
			button.ApplyTooltip();
		}
	}

	private static void OnIconSourceChanged(BindableObject bindable, object oldValue, object newValue)
	{
		if (bindable is RailIconButton button)
		{
			button.ApplyIconSource();
		}
	}

	private static void OnIsSelectedChanged(BindableObject bindable, object oldValue, object newValue)
	{
		if (bindable is RailIconButton button)
		{
			button.ApplySelectionState();
		}
	}

	private void ApplyTooltip()
	{
		RailButtonVisuals.ApplyTooltip(this, TooltipText);
	}

	private void ApplySelectionState()
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
		SafeAnimation.AbortAnimation(this, OpacityAnimationName, "RailIconButton");
		SafeAnimation.AbortAnimation(this, GlowAnimationName, "RailIconButton");
		Opacity = isPointerOver ? 1 : IdleOpacity;
		GlowSurface.Opacity = isPointerOver ? 0.62 : 0;
	}

	private void OnLoaded(object? sender, EventArgs e)
		=> RailHoverRegistry.Register(this);

	private void OnUnloaded(object? sender, EventArgs e)
	{
		RailHoverRegistry.Unregister(this);
		SafeAnimation.AbortAnimation(this, OpacityAnimationName, "RailIconButton");
		SafeAnimation.AbortAnimation(this, GlowAnimationName, "RailIconButton");
	}

	private void ApplyIconSource()
	{
		RailButtonVisuals.ApplyIconPair(NormalIconImage, ActiveIconImage, IsSelected, ActiveIconSource);
	}
}
