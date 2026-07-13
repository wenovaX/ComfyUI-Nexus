namespace ComfyUI_Nexus.Views.Rail.Chrome;

public partial class RailResizeHandleView : ContentView
{
	public event EventHandler<PanUpdatedEventArgs>? PanUpdated;

	public Border HandleElement => HandleBorder;

	public BoxView GripElement => GripLine;

	public bool IsHandleVisible
	{
		get => HandleBorder.IsVisible;
		set => HandleBorder.IsVisible = value;
	}

	public double HandleOpacity
	{
		get => HandleBorder.Opacity;
		set => HandleBorder.Opacity = value;
	}

	public Color GripColor
	{
		get => GripLine.Color;
		set => GripLine.Color = value;
	}

	public double GripOpacity
	{
		get => GripLine.Opacity;
		set => GripLine.Opacity = value;
	}

	public RailResizeHandleView()
	{
		InitializeComponent();
	}

	private void OnPanUpdated(object? sender, PanUpdatedEventArgs e)
	{
		PanUpdated?.Invoke(this, e);
	}
}
