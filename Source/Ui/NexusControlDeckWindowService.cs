using ComfyUI_Nexus.Diagnostics;
using Microsoft.Maui.Controls;

namespace ComfyUI_Nexus.Ui;

/// <summary>
/// Opens the optional diagnostics deck in its own window without creating a hard shell-to-view dependency.
/// </summary>
internal sealed class NexusControlDeckWindowService
{
	private const string DeckViewTypeName = "ComfyUI_Nexus.Views.ControlDeckView";
	private Window? _window;
	private INexusControlDeck? _deck;

	internal INexusControlDeck? CurrentDeck => _deck;
	internal bool IsOpen => _window != null;

	internal void Show(Action<INexusControlDeck> configure)
	{
		ArgumentNullException.ThrowIfNull(configure);
		if (_window != null)
		{
			return;
		}

		Type? viewType = typeof(NexusControlDeckWindowService).Assembly.GetType(DeckViewTypeName, throwOnError: false);
		if (viewType == null || Activator.CreateInstance(viewType) is not View view || view is not INexusControlDeck deck)
		{
			NexusLog.Warning("[CONTROL_DECK] Optional diagnostics deck is unavailable in this build.");
			return;
		}

		_deck = deck;
		configure(deck);
		var window = new Window(new ContentPage { Content = view, Padding = 0 })
		{
			Title = "Nexus Control Deck",
			Width = 380,
			Height = 820,
		};
		window.Destroying += OnWindowDestroying;
		_window = window;
		Application.Current?.OpenWindow(window);
		NexusLog.Info("[CONTROL_DECK] Diagnostics window opened.");
	}

	internal void Close()
	{
		Window? window = _window;
		if (window == null)
		{
			return;
		}

		ReleaseWindow(window);
		Application.Current?.CloseWindow(window);
	}

	private void OnWindowDestroying(object? sender, EventArgs e)
	{
		if (sender is Window window)
		{
			ReleaseWindow(window);
		}
	}

	private void ReleaseWindow(Window window)
	{
		if (!ReferenceEquals(_window, window))
		{
			return;
		}

		window.Destroying -= OnWindowDestroying;
		_window = null;
		_deck = null;
		NexusLog.Info("[CONTROL_DECK] Diagnostics window closed.");
	}
}
