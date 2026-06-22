using System.Net.Sockets;

namespace ComfyUI_Nexus.Setup.Runtime;

internal sealed class PortProbeService
{
	private static readonly HttpClient HttpClient = new()
	{
		Timeout = TimeSpan.FromSeconds(2)
	};

	internal static async Task<bool> WaitUntilOpenAsync(
		string host,
		int port,
		TimeSpan timeout,
		TimeSpan pollingDelay,
		CancellationToken cancellationToken)
	{
		using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		timeoutCts.CancelAfter(timeout);

		while (!timeoutCts.Token.IsCancellationRequested)
		{
			try
			{
				using var client = new TcpClient();
				var connectTask = client.ConnectAsync(host, port, timeoutCts.Token);
				await connectTask;
				return true;
			}
			catch
			{
				await Task.Delay(pollingDelay, cancellationToken);
			}
		}

		return false;
	}

	internal static async Task<bool> WaitUntilClosedAsync(
		string host,
		int port,
		TimeSpan timeout,
		TimeSpan pollingDelay,
		CancellationToken cancellationToken)
	{
		using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		timeoutCts.CancelAfter(timeout);

		while (!timeoutCts.Token.IsCancellationRequested)
		{
			try
			{
				using var client = new TcpClient();
				await client.ConnectAsync(host, port, timeoutCts.Token);
			}
			catch
			{
				return true;
			}

			await Task.Delay(pollingDelay, cancellationToken);
		}

		return false;
	}

	internal static async Task<bool> WaitUntilHttpReadyAsync(
		string baseUrl,
		TimeSpan timeout,
		TimeSpan pollingDelay,
		CancellationToken cancellationToken)
	{
		using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		timeoutCts.CancelAfter(timeout);

		string normalizedBaseUrl = baseUrl.TrimEnd('/');
		string[] readinessUrls =
		[
			$"{normalizedBaseUrl}/system_stats",
			$"{normalizedBaseUrl}/"
		];

		while (!timeoutCts.Token.IsCancellationRequested)
		{
			foreach (string url in readinessUrls)
			{
				try
				{
					using var response = await HttpClient.GetAsync(url, timeoutCts.Token);
					if ((int)response.StatusCode is >= 200 and < 500)
					{
						return true;
					}
				}
				catch
				{
				}
			}

			await Task.Delay(pollingDelay, cancellationToken);
		}

		return false;
	}
}
