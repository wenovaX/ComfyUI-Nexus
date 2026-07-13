using System.Net;
#if WINDOWS
using System.Net.NetworkInformation;
#endif
using System.Diagnostics;

namespace ComfyUI_Nexus.Setup.Runtime;

internal enum LocalHttpProbeState
{
	NotListening,
	Responded,
	RequestFailed,
}

internal readonly record struct LocalHttpProbeResult(LocalHttpProbeState State, HttpStatusCode? StatusCode)
{
	internal static LocalHttpProbeResult NotListening { get; } = new(LocalHttpProbeState.NotListening, null);
	internal static LocalHttpProbeResult RequestFailed { get; } = new(LocalHttpProbeState.RequestFailed, null);

	internal bool HasResponse => State == LocalHttpProbeState.Responded && StatusCode.HasValue;
}

/// <summary>
/// Provides lifecycle-safe readiness checks for local HTTP servers. Port release
/// checks use the OS listener table; HTTP requests only start after a listener exists.
/// </summary>
internal static class LocalServerProbe
{
	private static readonly HttpClient HttpClient = new()
	{
		Timeout = Timeout.InfiniteTimeSpan,
	};

	internal static bool IsPortListening(int port)
	{
#if WINDOWS
		try
		{
			return IPGlobalProperties.GetIPGlobalProperties()
				.GetActiveTcpListeners()
				.Any(endpoint => endpoint.Port == port);
		}
		catch (NetworkInformationException)
		{
			return false;
		}
#else
		return false;
#endif
	}

	internal static async Task<bool> WaitUntilPortReleasedAsync(
		int port,
		TimeSpan timeout,
		TimeSpan pollingInterval,
		CancellationToken cancellationToken)
	{
		using var pollTimer = new PeriodicTimer(pollingInterval);
		var stopwatch = Stopwatch.StartNew();

		while (stopwatch.Elapsed < timeout)
		{
			cancellationToken.ThrowIfCancellationRequested();
			if (!IsPortListening(port))
			{
				return true;
			}

			if (!await pollTimer.WaitForNextTickAsync(cancellationToken))
			{
				break;
			}
		}

		return false;
	}

	internal static async Task<LocalHttpProbeResult> TryGetAsync(
		Uri endpoint,
		CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(endpoint);
		cancellationToken.ThrowIfCancellationRequested();
		if (!IsPortListening(endpoint.Port))
		{
			return LocalHttpProbeResult.NotListening;
		}

		try
		{
			using var request = new HttpRequestMessage(HttpMethod.Get, endpoint);
			request.Headers.ConnectionClose = true;
			using var response = await HttpClient.SendAsync(
				request,
				HttpCompletionOption.ResponseHeadersRead,
				cancellationToken);
			return new LocalHttpProbeResult(LocalHttpProbeState.Responded, response.StatusCode);
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
		{
			throw;
		}
		catch (HttpRequestException)
		{
			return LocalHttpProbeResult.RequestFailed;
		}
	}

}
