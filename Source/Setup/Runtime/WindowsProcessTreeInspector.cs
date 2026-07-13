#if WINDOWS
namespace ComfyUI_Nexus.Setup.Runtime;

using System.Runtime.InteropServices;

/// <summary>
/// Reads a stable snapshot of a Windows process tree without opening child
/// process handles. Lifecycle shutdown uses this to avoid interrupting short
/// lived utility processes while they are still starting.
/// </summary>
internal static class WindowsProcessTreeInspector
{
	private const uint Th32csSnapProcess = 0x00000002;
	private static readonly IntPtr InvalidHandleValue = new(-1);

	internal static IReadOnlyList<ProcessTreeEntry> GetDescendants(int rootProcessId)
	{
		IntPtr snapshot = CreateToolhelp32Snapshot(Th32csSnapProcess, 0);
		if (snapshot == InvalidHandleValue)
		{
			return [];
		}

		try
		{
			var entries = new List<ProcessTreeEntry>();
			var current = new ProcessEntry32
			{
				dwSize = (uint)Marshal.SizeOf<ProcessEntry32>(),
			};

			if (!Process32First(snapshot, ref current))
			{
				return entries;
			}

			do
			{
				entries.Add(new ProcessTreeEntry(
					(int)current.th32ProcessID,
					(int)current.th32ParentProcessID,
					current.szExeFile ?? string.Empty));
				current.dwSize = (uint)Marshal.SizeOf<ProcessEntry32>();
			}
			while (Process32Next(snapshot, ref current));

			var descendants = new List<ProcessTreeEntry>();
			var parentIds = new HashSet<int> { rootProcessId };
			while (parentIds.Count > 0)
			{
				var nextParentIds = new HashSet<int>();
				foreach (ProcessTreeEntry entry in entries)
				{
					if (!parentIds.Contains(entry.ParentProcessId))
					{
						continue;
					}

					descendants.Add(entry);
					nextParentIds.Add(entry.ProcessId);
				}

				parentIds = nextParentIds;
			}

			return descendants;
		}
		finally
		{
			CloseHandle(snapshot);
		}
	}

	/// <summary>
	/// Terminates a previously captured tree without the managed Process API's
	/// live tree enumeration. The returned IDs are used by the caller for a
	/// separate, deterministic exit verification pass.
	/// </summary>
	internal static IReadOnlyList<int> TerminateTree(int rootProcessId, Action<string>? onFailure = null)
	{
		int[] targetProcessIds = [
			.. GetDescendants(rootProcessId)
				.Select(entry => entry.ProcessId)
				.Reverse(),
			rootProcessId
		];

		foreach (int processId in targetProcessIds.Distinct())
		{
			if (!TryTerminateProcess(processId, out int errorCode)
				&& errorCode is not ErrorInvalidParameter and not ErrorNotFound)
			{
				onFailure?.Invoke($"Unable to terminate process {processId}. Win32Error={errorCode}.");
			}
		}

		return targetProcessIds;
	}

	private static bool TryTerminateProcess(int processId, out int errorCode)
	{
		IntPtr processHandle = OpenProcess(ProcessTerminate, false, (uint)processId);
		if (processHandle == IntPtr.Zero)
		{
			errorCode = Marshal.GetLastWin32Error();
			return false;
		}

		try
		{
			if (TerminateProcess(processHandle, 1))
			{
				errorCode = 0;
				return true;
			}

			errorCode = Marshal.GetLastWin32Error();
			return false;
		}
		finally
		{
			CloseHandle(processHandle);
		}
	}

	[DllImport("kernel32.dll", SetLastError = true)]
	private static extern IntPtr CreateToolhelp32Snapshot(uint flags, uint processId);

	[DllImport("kernel32.dll", SetLastError = true)]
	private static extern IntPtr OpenProcess(uint desiredAccess, [MarshalAs(UnmanagedType.Bool)] bool inheritHandle, uint processId);

	[DllImport("kernel32.dll", SetLastError = true)]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static extern bool TerminateProcess(IntPtr processHandle, uint exitCode);

	[DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static extern bool Process32First(IntPtr snapshot, ref ProcessEntry32 entry);

	[DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static extern bool Process32Next(IntPtr snapshot, ref ProcessEntry32 entry);

	[DllImport("kernel32.dll", SetLastError = true)]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static extern bool CloseHandle(IntPtr handle);

	private const uint ProcessTerminate = 0x0001;
	private const int ErrorInvalidParameter = 87;
	private const int ErrorNotFound = 1168;

	[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
	private struct ProcessEntry32
	{
		public uint dwSize;
		public uint cntUsage;
		public uint th32ProcessID;
		public IntPtr th32DefaultHeapID;
		public uint th32ModuleID;
		public uint cntThreads;
		public uint th32ParentProcessID;
		public int pcPriClassBase;
		public uint dwFlags;

		[MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
		public string? szExeFile;
	}

	internal sealed record ProcessTreeEntry(int ProcessId, int ParentProcessId, string ExecutableName);
}
#endif
