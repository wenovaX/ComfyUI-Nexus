namespace ComfyUI_Nexus.Setup.Runtime;

using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;

internal sealed class DownloadService
{
	private const string PartialFileSuffix = ".partial";

	internal static async Task DownloadFileAsync(
		string url,
		string destinationPath,
		Action<double, long, long?>? onProgress,
		int bufferSize,
		CancellationToken cancellationToken)
	{
		string? directory = Path.GetDirectoryName(destinationPath);
		if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
		{
			Directory.CreateDirectory(directory);
		}

		using var httpClient = new HttpClient();
		using CancellationTokenRegistration cancellationRegistration = cancellationToken.Register(
			static state => ((HttpClient)state!).CancelPendingRequests(),
			httpClient);

		long? totalBytes = await TryGetRemoteLengthAsync(httpClient, url, cancellationToken);
		string partialPath = destinationPath + PartialFileSuffix;
		DownloadStagingState staging = PrepareStagingFile(destinationPath, partialPath, totalBytes);
		if (staging.IsComplete)
		{
			onProgress?.Invoke(1.0, staging.ExistingLength, totalBytes);
			return;
		}

		long existingLength = staging.ExistingLength;
		if (totalBytes.HasValue && totalBytes.Value > 0 && existingLength > 0)
		{
			onProgress?.Invoke((double)existingLength / totalBytes.Value, existingLength, totalBytes);
		}

		using var response = await SendDownloadRequestAsync(httpClient, url, existingLength, cancellationToken);
		response.EnsureSuccessStatusCode();

		if (existingLength > 0 && response.StatusCode != HttpStatusCode.PartialContent)
		{
			existingLength = 0;
		}

		long? expectedLength = response.Content.Headers.ContentRange?.Length
			?? totalBytes
			?? response.Content.Headers.ContentLength;
		using var contentStream = await response.Content.ReadAsStreamAsync(cancellationToken);
		long totalRead = existingLength;
		using (var fileStream = new FileStream(partialPath, existingLength > 0 ? FileMode.Append : FileMode.Create, FileAccess.Write, FileShare.None, bufferSize, true))
		{
			var buffer = new byte[bufferSize];
			var stopwatch = System.Diagnostics.Stopwatch.StartNew();
			int read;
			while ((read = await contentStream.ReadAsync(buffer.AsMemory(), cancellationToken)) > 0)
			{
				await fileStream.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
				totalRead += read;

				if (stopwatch.ElapsedMilliseconds > 100 || (expectedLength.HasValue && totalRead == expectedLength.Value))
				{
					stopwatch.Restart();
					double progress = (expectedLength.HasValue && expectedLength.Value > 0) ? (double)totalRead / expectedLength.Value : 0;
					onProgress?.Invoke(progress, totalRead, expectedLength);
				}
			}
		}

		if (expectedLength.HasValue && totalRead != expectedLength.Value)
		{
			throw new IOException($"Download ended before the expected length was reached. Expected {expectedLength.Value} bytes, received {totalRead} bytes.");
		}

		File.Move(partialPath, destinationPath, true);
		onProgress?.Invoke(1.0, totalRead, expectedLength);
	}

	private static DownloadStagingState PrepareStagingFile(string destinationPath, string partialPath, long? totalBytes)
	{
		if (File.Exists(destinationPath))
		{
			long finalLength = new FileInfo(destinationPath).Length;
			if (totalBytes.HasValue && finalLength == totalBytes.Value)
			{
				return new DownloadStagingState(true, finalLength);
			}

			if (!totalBytes.HasValue)
			{
				File.Delete(destinationPath);
			}
			else if (!File.Exists(partialPath) || finalLength > new FileInfo(partialPath).Length)
			{
				File.Move(destinationPath, partialPath, true);
			}
			else
			{
				File.Delete(destinationPath);
			}
		}

		if (!File.Exists(partialPath))
		{
			return new DownloadStagingState(false, 0);
		}

		long partialLength = new FileInfo(partialPath).Length;
		if (!totalBytes.HasValue || partialLength > totalBytes.Value)
		{
			File.Delete(partialPath);
			return new DownloadStagingState(false, 0);
		}

		if (partialLength == totalBytes.Value)
		{
			File.Move(partialPath, destinationPath, true);
			return new DownloadStagingState(true, partialLength);
		}

		return new DownloadStagingState(false, partialLength);
	}

	internal static async Task<long?> TryGetRemoteLengthAsync(HttpClient httpClient, string url, CancellationToken cancellationToken)
	{
		try
		{
			using var request = new HttpRequestMessage(HttpMethod.Head, url);
			using var response = await httpClient.SendAsync(request, cancellationToken);
			return response.IsSuccessStatusCode ? response.Content.Headers.ContentLength : null;
		}
		catch (HttpRequestException)
		{
			return null;
		}
	}

	private static async Task<HttpResponseMessage> SendDownloadRequestAsync(HttpClient httpClient, string url, long existingLength, CancellationToken cancellationToken)
	{
		var request = new HttpRequestMessage(HttpMethod.Get, url);
		if (existingLength > 0)
		{
			request.Headers.Range = new RangeHeaderValue(existingLength, null);
		}

		return await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
	}

	private readonly record struct DownloadStagingState(bool IsComplete, long ExistingLength);
}
