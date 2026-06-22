namespace ComfyUI_Nexus.Views.Overlays;

public partial class BookmarkHudView : ContentView
{
	internal event EventHandler? CloseRequested;
	internal event EventHandler<TextChangedEventArgs>? SearchChanged;

	public BookmarkHudView()
	{
		InitializeComponent();
	}

	internal void ClearItems()
	{
		BookmarkListStack.Children.Clear();
	}

	internal void AddItem(View view)
	{
		BookmarkListStack.Children.Add(view);
	}

	internal void SetCountText(string text)
	{
		BookmarkCountLabel.Text = text;
	}

	internal void SetOverlayState(bool isVisible, double opacity)
	{
		IsVisible = isVisible;
		Opacity = opacity;
	}

	internal void FocusSearch()
	{
		BookmarkSearchEntry.Focus();
	}

	internal void ClearSearch()
	{
		BookmarkSearchEntry.Text = string.Empty;
	}

	internal string GetSearchText()
	{
		return BookmarkSearchEntry.Text ?? string.Empty;
	}

	internal void ScrollToTop()
	{
		_ = BookmarkHudScrollView.ScrollToAsync(0, 0, false);
	}

	private void OnCloseClicked(object? sender, EventArgs e) => CloseRequested?.Invoke(sender, e);

	private void OnSearchChanged(object? sender, TextChangedEventArgs e) => SearchChanged?.Invoke(sender, e);
}
