#if WINDOWS
using System.Runtime.InteropServices;
using WinRT.Interop;

namespace ComfyUI_Nexus.Platform.Windows;

public sealed class WindowsCursorService : IPlatformCursorService
{
	private const int IDC_ARROW = 32512;
	private const int IDC_IBEAM = 32513;
	private const int IDC_WAIT = 32514;
	private const int IDC_CROSS = 32515;
	private const int IDC_SIZENWSE = 32642;
	private const int IDC_SIZENESW = 32643;
	private const int IDC_SIZEWE = 32644;
	private const int IDC_SIZENS = 32645;
	private const int IDC_SIZEALL = 32646;
	private const int IDC_NO = 32648;
	private const int IDC_HAND = 32649;
	private const int IDC_APPSTARTING = 32650;
	private const int VK_LBUTTON = 0x01;
	private const uint WM_SETCURSOR = 0x0020;
	private static readonly UIntPtr CursorSubclassId = new(0x4E435552);
	private static readonly SubclassProc CursorSubclassProc = HandleCursorSubclassMessage;
	private static readonly Dictionary<NexusCursorShape, string> CustomCursorFiles = new()
	{
		[NexusCursorShape.Hand] = "hand_open.cur",
		[NexusCursorShape.Grabbing] = "hand_closed.cur",
		[NexusCursorShape.ZoomIn] = "zoom_in.cur",
	};
	private static readonly Dictionary<NexusCursorShape, IntPtr> CustomCursorHandles = new();
	private static readonly Dictionary<NexusCursorShape, Microsoft.UI.Input.InputCursor?> CustomInputCursors = new();
	private static readonly HashSet<Microsoft.UI.Xaml.FrameworkElement> DynamicCursorSurfaces = [];
	private static readonly object CursorGate = new();
	private static IntPtr _subclassedHwnd;
	private static IntPtr _activeCustomCursorHandle;

	// WinUI owns the visible cursor through UIElement.ProtectedCursor. A raw
	// HCURSOR/SetCursor call is not enough for MAUI controls, so custom .cur
	// handles must be converted into Microsoft.UI.Input.InputCursor first.
	[System.Runtime.InteropServices.DllImport("api-ms-win-core-winrt-l1-1-0.dll", ExactSpelling = true)]
	private static extern int RoGetActivationFactory(IntPtr classId, ref Guid iid, out IntPtr factory);

	[System.Runtime.InteropServices.DllImport("api-ms-win-core-winrt-string-l1-1-0.dll", CallingConvention = System.Runtime.InteropServices.CallingConvention.StdCall, ExactSpelling = true)]
	private static extern int WindowsCreateString([System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.LPWStr)] string sourceString, int length, out IntPtr hstring);

	[System.Runtime.InteropServices.DllImport("api-ms-win-core-winrt-string-l1-1-0.dll", CallingConvention = System.Runtime.InteropServices.CallingConvention.StdCall, ExactSpelling = true)]
	private static extern int WindowsDeleteString(IntPtr hstring);

	[System.Runtime.InteropServices.ComImport]
	[System.Runtime.InteropServices.Guid("ac6f5065-90c4-46ce-beb7-05e138e54117")]
	[System.Runtime.InteropServices.InterfaceType(System.Runtime.InteropServices.ComInterfaceType.InterfaceIsIUnknown)]
	public interface IInputCursorStaticsInterop
	{
		void GetIids();
		void GetRuntimeClassName();
		void GetTrustLevel();
		[System.Runtime.InteropServices.PreserveSig]
		int CreateFromHCursor(IntPtr cursor, out IntPtr inputCursor);
	}

	private static readonly Dictionary<string, int> CssCursorToWin32 = new(StringComparer.OrdinalIgnoreCase)
	{
		["default"] = IDC_ARROW,
		["auto"] = IDC_ARROW,
		["pointer"] = IDC_HAND,
		["text"] = IDC_IBEAM,
		["crosshair"] = IDC_CROSS,
		["move"] = IDC_SIZEALL,
		["grab"] = IDC_HAND,
		["grabbing"] = IDC_HAND,
		["not-allowed"] = IDC_NO,
		["no-drop"] = IDC_NO,
		["wait"] = IDC_WAIT,
		["progress"] = IDC_APPSTARTING,
		["ew-resize"] = IDC_SIZEWE,
		["ns-resize"] = IDC_SIZENS,
		["nesw-resize"] = IDC_SIZENESW,
		["nwse-resize"] = IDC_SIZENWSE,
		["col-resize"] = IDC_SIZEWE,
		["row-resize"] = IDC_SIZENS,
		["n-resize"] = IDC_SIZENS,
		["s-resize"] = IDC_SIZENS,
		["e-resize"] = IDC_SIZEWE,
		["w-resize"] = IDC_SIZEWE,
		["ne-resize"] = IDC_SIZENESW,
		["nw-resize"] = IDC_SIZENWSE,
		["se-resize"] = IDC_SIZENWSE,
		["sw-resize"] = IDC_SIZENESW,
		["all-scroll"] = IDC_SIZEALL,
	};

	public void SetCursor(NexusCursorShape shape)
	{
		if (TryGetCustomCursorHandle(shape, out IntPtr cursorHandle))
		{
			ActivateCustomCursor(cursorHandle);
		}
		else
		{
			ClearActiveCustomCursor();
			SetWin32Cursor(ToWin32CursorId(shape));
		}
	}

	public void SetCursor(VisualElement? element, NexusCursorShape shape)
	{
		bool hasCustom = TryGetCustomCursorHandle(shape, out IntPtr cursorHandle);

		if (element?.Handler?.PlatformView is Microsoft.UI.Xaml.UIElement platformElement)
		{
			if (hasCustom && TryGetCustomInputCursor(shape, cursorHandle, out var inputCursor))
			{
				SetProtectedCursor(platformElement, inputCursor);
			}
			else
			{
				SetProtectedCursor(platformElement, ToInputCursorShape(shape));
			}
		}

		if (hasCustom)
		{
			ActivateCustomCursor(cursorHandle);
		}
		else
		{
			ClearActiveCustomCursor();
			SetWin32Cursor(ToWin32CursorId(shape));
		}
	}

	public void SetCssCursor(string cssCursor)
	{
		ClearActiveCustomCursor();
		SetCssCursor(null, cssCursor);
	}

	public void SetCssCursor(VisualElement? element, string cssCursor)
	{
		ClearActiveCustomCursor();
		if (!CssCursorToWin32.TryGetValue(cssCursor, out int cursorId))
		{
			cursorId = IDC_ARROW;
		}

		if (element?.Handler?.PlatformView is Microsoft.UI.Xaml.UIElement platformElement)
		{
			SetProtectedCursor(platformElement, ToInputSystemCursorShape(cursorId));
		}

		SetWin32Cursor(cursorId);
	}

	public bool IsPointerOver(VisualElement? element)
	{
		try
		{
			if (!TryGetPointerAndBounds(element, out var pointer, out var bounds))
			{
				return false;
			}

			return pointer.X >= bounds.X
				&& pointer.X <= bounds.X + bounds.Width
				&& pointer.Y >= bounds.Y
				&& pointer.Y <= bounds.Y + bounds.Height;
		}
		catch
		{
			return false;
		}
	}

	public Point? GetPointerPositionRelativeTo(VisualElement? element)
	{
		try
		{
			if (!TryGetPointerAndBounds(element, out var pointer, out var bounds))
			{
				return null;
			}

			return new Point(pointer.X - bounds.X, pointer.Y - bounds.Y);
		}
		catch
		{
			return null;
		}
	}

	public bool IsPrimaryPointerPressed()
	{
		try
		{
			return (GetAsyncKeyState(VK_LBUTTON) & 0x8000) != 0;
		}
		catch
		{
			return false;
		}
	}

	public void AttachResizeHandleCursor(
		VisualElement handleElement,
		Action pointerEntered,
		Action pointerExited,
		Action pointerPressed)
	{
		if (handleElement.Handler?.PlatformView is not Microsoft.UI.Xaml.FrameworkElement platformHandle)
		{
			return;
		}

		platformHandle.PointerEntered += (sender, args) =>
		{
			SetProtectedCursor(platformHandle, Microsoft.UI.Input.InputSystemCursorShape.SizeWestEast);
			SetWin32Cursor(IDC_SIZEWE);
			pointerEntered();
		};
		platformHandle.PointerExited += (sender, args) =>
		{
			SetProtectedCursor(platformHandle, Microsoft.UI.Input.InputSystemCursorShape.Arrow);
			SetWin32Cursor(IDC_ARROW);
			pointerExited();
		};
		platformHandle.PointerPressed += (sender, args) =>
		{
			SetProtectedCursor(platformHandle, Microsoft.UI.Input.InputSystemCursorShape.SizeWestEast);
			SetWin32Cursor(IDC_SIZEWE);
			pointerPressed();
		};
	}

	public void AttachDynamicCursorSurface(
		VisualElement element,
		Func<NexusCursorShape> cursorShapeProvider,
		Action pointerEntered,
		Action pointerMoved,
		Action pointerExited,
		Action pointerPressed,
		Action pointerReleased)
	{
		if (element.Handler?.PlatformView is not Microsoft.UI.Xaml.FrameworkElement platformElement)
		{
			return;
		}

		lock (CursorGate)
		{
			if (!DynamicCursorSurfaces.Add(platformElement))
			{
				return;
			}
		}

		void ApplyCurrentCursor()
		{
			SetCursor(element, cursorShapeProvider());
		}

		platformElement.PointerEntered += (sender, args) =>
		{
			pointerEntered();
			ApplyCurrentCursor();
		};
		platformElement.PointerMoved += (sender, args) =>
		{
			pointerMoved();
			ApplyCurrentCursor();
		};
		platformElement.PointerExited += (sender, args) =>
		{
			pointerExited();
			SetProtectedCursor(platformElement, Microsoft.UI.Input.InputSystemCursorShape.Arrow);
			ClearActiveCustomCursor();
			SetWin32Cursor(IDC_ARROW);
		};
		platformElement.PointerPressed += (sender, args) =>
		{
			pointerPressed();
			ApplyCurrentCursor();
		};
		platformElement.PointerReleased += (sender, args) =>
		{
			pointerReleased();
			ApplyCurrentCursor();
		};
	}

	private static Microsoft.UI.Input.InputSystemCursorShape ToInputCursorShape(NexusCursorShape shape)
		=> shape switch
		{
			NexusCursorShape.Hand => Microsoft.UI.Input.InputSystemCursorShape.Hand,
			NexusCursorShape.Grabbing => Microsoft.UI.Input.InputSystemCursorShape.SizeAll,
			NexusCursorShape.ZoomIn => Microsoft.UI.Input.InputSystemCursorShape.Cross,
			NexusCursorShape.ZoomOut => Microsoft.UI.Input.InputSystemCursorShape.Cross,
			NexusCursorShape.SizeWestEast => Microsoft.UI.Input.InputSystemCursorShape.SizeWestEast,
			NexusCursorShape.Forbidden => Microsoft.UI.Input.InputSystemCursorShape.Cross,
			_ => Microsoft.UI.Input.InputSystemCursorShape.Arrow,
		};

	private static int ToWin32CursorId(NexusCursorShape shape)
		=> shape switch
		{
			NexusCursorShape.Hand => IDC_HAND,
			NexusCursorShape.Grabbing => IDC_SIZEALL,
			NexusCursorShape.ZoomIn => IDC_CROSS,
			NexusCursorShape.ZoomOut => IDC_CROSS,
			NexusCursorShape.Forbidden => IDC_NO,
			NexusCursorShape.SizeWestEast => IDC_SIZEWE,
			_ => IDC_ARROW,
		};

	private static bool TryGetPointerAndBounds(
		VisualElement? element,
		out global::Windows.Foundation.Point pointer,
		out global::Windows.Foundation.Rect bounds)
	{
		pointer = default;
		bounds = default;

		if (element?.Handler?.PlatformView is not Microsoft.UI.Xaml.FrameworkElement platformElement)
		{
			return false;
		}

		if (platformElement.XamlRoot is null || platformElement.ActualWidth <= 0 || platformElement.ActualHeight <= 0)
		{
			return false;
		}

		var hwnd = TryGetAppWindowHandle();
		if (hwnd == IntPtr.Zero || !GetCursorPos(out var cursor))
		{
			return false;
		}

		if (!ScreenToClient(hwnd, ref cursor))
		{
			return false;
		}

		double scale = Math.Max(platformElement.XamlRoot.RasterizationScale, 0.001);
		pointer = new global::Windows.Foundation.Point(cursor.X / scale, cursor.Y / scale);
		bounds = platformElement.TransformToVisual(null).TransformBounds(
			new global::Windows.Foundation.Rect(0, 0, platformElement.ActualWidth, platformElement.ActualHeight));
		return true;
	}

	private static void SetProtectedCursor(Microsoft.UI.Xaml.UIElement element, Microsoft.UI.Input.InputCursor? cursor)
	{
		try
		{
			var property = typeof(Microsoft.UI.Xaml.UIElement).GetProperty("ProtectedCursor", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
			property?.SetValue(element, cursor);
		}
		catch
		{
		}
	}

	private static void SetProtectedCursor(Microsoft.UI.Xaml.UIElement element, Microsoft.UI.Input.InputSystemCursorShape shape)
	{
		try
		{
			var cursor = Microsoft.UI.Input.InputSystemCursor.Create(shape);
			var property = typeof(Microsoft.UI.Xaml.UIElement).GetProperty("ProtectedCursor", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
			property?.SetValue(element, cursor);
		}
		catch
		{
		}
	}

	private static void SetWin32Cursor(int cursorId)
	{
		try
		{
			IntPtr cursorHandle = LoadCursor(IntPtr.Zero, cursorId);
			if (cursorHandle != IntPtr.Zero)
			{
				Win32SetCursor(cursorHandle);
			}
		}
		catch
		{
		}
	}

	private static bool TryGetCustomInputCursor(NexusCursorShape shape, IntPtr cursorHandle, out Microsoft.UI.Input.InputCursor? inputCursor)
	{
		inputCursor = null;
		lock (CursorGate)
		{
			if (CustomInputCursors.TryGetValue(shape, out inputCursor))
			{
				return inputCursor != null;
			}

			try
			{
				IntPtr hstring = IntPtr.Zero;
				IntPtr factoryPtr = IntPtr.Zero;
				try
				{
					string className = "Microsoft.UI.Input.InputCursor";
					WindowsCreateString(className, className.Length, out hstring);
					Guid iid = new Guid("ac6f5065-90c4-46ce-beb7-05e138e54117");
					int hr = RoGetActivationFactory(hstring, ref iid, out factoryPtr);

					if (hr == 0 && factoryPtr != IntPtr.Zero)
					{
						var factory = System.Runtime.InteropServices.Marshal.GetObjectForIUnknown(factoryPtr) as IInputCursorStaticsInterop;
						if (factory != null)
						{
							// This is the critical bridge: ProtectedCursor only accepts
							// WinUI InputCursor objects, not raw Win32 cursor handles.
							hr = factory.CreateFromHCursor(cursorHandle, out IntPtr cursorAbi);
							if (hr == 0 && cursorAbi != IntPtr.Zero)
							{
								inputCursor = WinRT.MarshalInterface<Microsoft.UI.Input.InputCursor>.FromAbi(cursorAbi);
							}
						}
					}
				}
				finally
				{
					if (factoryPtr != IntPtr.Zero)
					{
						System.Runtime.InteropServices.Marshal.Release(factoryPtr);
					}
					if (hstring != IntPtr.Zero)
					{
						WindowsDeleteString(hstring);
					}
				}

				CustomInputCursors[shape] = inputCursor;
				return inputCursor != null;
			}
			catch (Exception ex)
			{
				ComfyUI_Nexus.Diagnostics.NexusLog.Warning($"Custom cursor interop failed: {ex.GetType().Name} - {ex.Message}");
				CustomInputCursors[shape] = null;
				return false;
			}
		}
	}

	private static bool TryGetCustomCursorHandle(NexusCursorShape shape, out IntPtr cursorHandle)
	{
		cursorHandle = IntPtr.Zero;

		try
		{
			if (!CustomCursorFiles.TryGetValue(shape, out string? fileName))
			{
				return false;
			}

			lock (CursorGate)
			{
				if (CustomCursorHandles.TryGetValue(shape, out cursorHandle) && cursorHandle != IntPtr.Zero)
				{
					return true;
				}

				string cursorPath = Path.Combine(AppContext.BaseDirectory, "Resources", "Cursor", fileName);
				if (!File.Exists(cursorPath))
				{
					return false;
				}

				cursorHandle = LoadCursorFromFile(cursorPath);
				if (cursorHandle == IntPtr.Zero)
				{
					return false;
				}

				CustomCursorHandles[shape] = cursorHandle;
				return true;
			}
		}
		catch (Exception ex)
		{
			ComfyUI_Nexus.Diagnostics.NexusLog.Warning($"Custom cursor load failed: {ex.GetType().Name} - {ex.Message}");
			cursorHandle = IntPtr.Zero;
			return false;
		}
	}

	private static void ActivateCustomCursor(IntPtr cursorHandle)
	{
		lock (CursorGate)
		{
			_activeCustomCursorHandle = cursorHandle;
		}

		EnsureCursorSubclass();
		Win32SetCursor(cursorHandle);
	}

	private static void ClearActiveCustomCursor()
	{
		lock (CursorGate)
		{
			_activeCustomCursorHandle = IntPtr.Zero;
		}
	}

	private static void EnsureCursorSubclass()
	{
		try
		{
			IntPtr hwnd = TryGetAppWindowHandle();
			if (hwnd == IntPtr.Zero)
			{
				return;
			}

			lock (CursorGate)
			{
				if (_subclassedHwnd == hwnd)
				{
					return;
				}

				if (SetWindowSubclass(hwnd, CursorSubclassProc, CursorSubclassId, UIntPtr.Zero))
				{
					_subclassedHwnd = hwnd;
				}
			}
		}
		catch
		{
		}
	}

	private static IntPtr HandleCursorSubclassMessage(
		IntPtr hWnd,
		uint uMsg,
		IntPtr wParam,
		IntPtr lParam,
		UIntPtr uIdSubclass,
		UIntPtr dwRefData)
	{
		if (uMsg == WM_SETCURSOR)
		{
			IntPtr cursorHandle;
			lock (CursorGate)
			{
				cursorHandle = _activeCustomCursorHandle;
			}

			if (cursorHandle != IntPtr.Zero)
			{
				Win32SetCursor(cursorHandle);
				return new IntPtr(1);
			}
		}

		return DefSubclassProc(hWnd, uMsg, wParam, lParam);
	}

	private static IntPtr TryGetAppWindowHandle()
	{
		try
		{
			var window = Microsoft.Maui.Controls.Application.Current?.Windows.Count > 0
				? Microsoft.Maui.Controls.Application.Current.Windows[0].Handler?.PlatformView as Microsoft.UI.Xaml.Window
				: null;
			return window is null ? IntPtr.Zero : WindowNative.GetWindowHandle(window);
		}
		catch
		{
			return IntPtr.Zero;
		}
	}

	private static Microsoft.UI.Input.InputSystemCursorShape ToInputSystemCursorShape(int cursorId)
	{
		return cursorId switch
		{
			IDC_HAND => Microsoft.UI.Input.InputSystemCursorShape.Hand,
			IDC_IBEAM => Microsoft.UI.Input.InputSystemCursorShape.IBeam,
			IDC_SIZEWE => Microsoft.UI.Input.InputSystemCursorShape.SizeWestEast,
			IDC_SIZENS => Microsoft.UI.Input.InputSystemCursorShape.SizeNorthSouth,
			IDC_SIZENWSE => Microsoft.UI.Input.InputSystemCursorShape.SizeNorthwestSoutheast,
			IDC_SIZENESW => Microsoft.UI.Input.InputSystemCursorShape.SizeNortheastSouthwest,
			IDC_SIZEALL => Microsoft.UI.Input.InputSystemCursorShape.SizeAll,
			IDC_NO => Microsoft.UI.Input.InputSystemCursorShape.UniversalNo,
			IDC_WAIT => Microsoft.UI.Input.InputSystemCursorShape.Wait,
			IDC_CROSS => Microsoft.UI.Input.InputSystemCursorShape.Cross,
			_ => Microsoft.UI.Input.InputSystemCursorShape.Arrow,
		};
	}

	[DllImport("user32.dll", CharSet = CharSet.Auto)]
	private static extern IntPtr LoadCursor(IntPtr hInstance, int lpCursorName);

	[DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
	private static extern IntPtr LoadCursorFromFile(string lpFileName);

	[DllImport("user32.dll", EntryPoint = "SetCursor")]
	private static extern IntPtr Win32SetCursor(IntPtr hCursor);

	[DllImport("comctl32.dll", SetLastError = true)]
	private static extern bool SetWindowSubclass(IntPtr hWnd, SubclassProc pfnSubclass, UIntPtr uIdSubclass, UIntPtr dwRefData);

	[DllImport("comctl32.dll")]
	private static extern IntPtr DefSubclassProc(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam);

	[DllImport("user32.dll")]
	private static extern bool GetCursorPos(out Win32Point lpPoint);

	[DllImport("user32.dll")]
	private static extern bool ScreenToClient(IntPtr hWnd, ref Win32Point lpPoint);

	[DllImport("user32.dll")]
	private static extern short GetAsyncKeyState(int vKey);

	[StructLayout(LayoutKind.Sequential)]
	private struct Win32Point
	{
		public int X;
		public int Y;
	}

	private delegate IntPtr SubclassProc(
		IntPtr hWnd,
		uint uMsg,
		IntPtr wParam,
		IntPtr lParam,
		UIntPtr uIdSubclass,
		UIntPtr dwRefData);
}
#endif
