#if WINDOWS
using Microsoft.UI.Input;

namespace ComfyUI_Nexus.Platform.Windows;

public sealed class WindowsKeyboardState : IPlatformKeyboardState
{
	public bool IsCtrlPressed()
		=> IsKeyDown(global::Windows.System.VirtualKey.Control);

	public bool IsShiftPressed()
		=> IsKeyDown(global::Windows.System.VirtualKey.Shift);

	public bool IsAltPressed()
		=> IsKeyDown(global::Windows.System.VirtualKey.Menu);

	public bool IsNativeTextInputFocused(Element? scope)
	{
		var xamlRoot = (scope?.Handler?.PlatformView as Microsoft.UI.Xaml.UIElement)?.XamlRoot;
		var focused = Microsoft.UI.Xaml.Input.FocusManager.GetFocusedElement(xamlRoot);
		return focused is Microsoft.UI.Xaml.Controls.TextBox
			|| focused is Microsoft.UI.Xaml.Controls.PasswordBox
			|| focused is Microsoft.UI.Xaml.Controls.RichEditBox
			|| focused is Microsoft.UI.Xaml.Controls.AutoSuggestBox;
	}

	public NexusKey ToNexusKey(object? platformKey)
	{
		if (platformKey is not global::Windows.System.VirtualKey key)
		{
			return NexusKey.Unknown;
		}

		return key switch
		{
			global::Windows.System.VirtualKey.A => NexusKey.A,
			global::Windows.System.VirtualKey.B => NexusKey.B,
			global::Windows.System.VirtualKey.C => NexusKey.C,
			global::Windows.System.VirtualKey.D => NexusKey.D,
			global::Windows.System.VirtualKey.H => NexusKey.H,
			global::Windows.System.VirtualKey.L => NexusKey.L,
			global::Windows.System.VirtualKey.M => NexusKey.M,
			global::Windows.System.VirtualKey.O => NexusKey.O,
			global::Windows.System.VirtualKey.S => NexusKey.S,
			global::Windows.System.VirtualKey.V => NexusKey.V,
			global::Windows.System.VirtualKey.W => NexusKey.W,
			global::Windows.System.VirtualKey.X => NexusKey.X,
			global::Windows.System.VirtualKey.Decimal => NexusKey.Period,
			global::Windows.System.VirtualKey.Enter => NexusKey.Enter,
			global::Windows.System.VirtualKey.Space => NexusKey.Space,
			global::Windows.System.VirtualKey.Escape => NexusKey.Escape,
			global::Windows.System.VirtualKey.Back => NexusKey.Backspace,
			global::Windows.System.VirtualKey.Tab => NexusKey.Tab,
			global::Windows.System.VirtualKey.Delete => NexusKey.Delete,
			global::Windows.System.VirtualKey.Left => NexusKey.Left,
			global::Windows.System.VirtualKey.Up => NexusKey.Up,
			global::Windows.System.VirtualKey.Down => NexusKey.Down,
			global::Windows.System.VirtualKey.Right => NexusKey.Right,
			global::Windows.System.VirtualKey.F2 => NexusKey.F2,
			global::Windows.System.VirtualKey.F5 => NexusKey.F5,
			_ when (int)key == 190 => NexusKey.Period,
			_ => NexusKey.Unknown,
		};
	}

	private static bool IsKeyDown(global::Windows.System.VirtualKey key)
		=> (InputKeyboardSource.GetKeyStateForCurrentThread(key)
			& global::Windows.UI.Core.CoreVirtualKeyStates.Down) == global::Windows.UI.Core.CoreVirtualKeyStates.Down;
}
#endif
