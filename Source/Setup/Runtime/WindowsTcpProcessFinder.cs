#if WINDOWS
namespace ComfyUI_Nexus.Setup.Runtime;

using System.Net;
using System.Runtime.InteropServices;

internal static class WindowsTcpProcessFinder
{
	private const int AfInet = 2;

	internal static IEnumerable<ComfyServerProcessRegistry.IPEndPointInfo> GetListeners(int port)
	{
		int bufferSize = 0;
		uint result = GetExtendedTcpTable(
			IntPtr.Zero,
			ref bufferSize,
			true,
			AfInet,
			TcpTableClass.TcpTableOwnerPidListener,
			0);

		IntPtr buffer = Marshal.AllocHGlobal(bufferSize);
		try
		{
			result = GetExtendedTcpTable(
				buffer,
				ref bufferSize,
				true,
				AfInet,
				TcpTableClass.TcpTableOwnerPidListener,
				0);

			if (result != 0)
			{
				yield break;
			}

			int rowCount = Marshal.ReadInt32(buffer);
			IntPtr rowPtr = IntPtr.Add(buffer, sizeof(int));
			int rowSize = Marshal.SizeOf<TcpRowOwnerPid>();

			for (int i = 0; i < rowCount; i++)
			{
				var row = Marshal.PtrToStructure<TcpRowOwnerPid>(rowPtr);
				int localPort = IPAddress.NetworkToHostOrder((short)row.LocalPort);
				if (localPort == port)
				{
					yield return new ComfyServerProcessRegistry.IPEndPointInfo((int)row.OwningPid);
				}

				rowPtr = IntPtr.Add(rowPtr, rowSize);
			}
		}
		finally
		{
			Marshal.FreeHGlobal(buffer);
		}
	}

	[DllImport("iphlpapi.dll", SetLastError = true)]
	private static extern uint GetExtendedTcpTable(
		IntPtr tcpTable,
		ref int tcpTableLength,
		bool sort,
		int ipVersion,
		TcpTableClass tableClass,
		uint reserved);

	private enum TcpTableClass
	{
		TcpTableOwnerPidListener = 3,
	}

	[StructLayout(LayoutKind.Sequential)]
	private struct TcpRowOwnerPid
	{
		public uint State;
		public uint LocalAddress;
		public uint LocalPort;
		public uint RemoteAddress;
		public uint RemotePort;
		public uint OwningPid;
	}
}
#endif
