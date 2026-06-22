namespace ComfyUI_Nexus.Setup.Runtime;

using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;

internal sealed class DownloadService
{
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

		long? totalBytes = await TryGetRemoteLengthAsync(httpClient, url, cancellationToken);
		long existingLength = 0;

		if (File.Exists(destinationPath))
		{
			existingLength = new FileInfo(destinationPath).Length;
			if (totalBytes.HasValue && existingLength == totalBytes.Value)
			{
				onProgress?.Invoke(1.0, existingLength, totalBytes);
				return;
			}

			if (!totalBytes.HasValue || existingLength > totalBytes.Value)
			{
				File.Delete(destinationPath);
				existingLength = 0;
			}
		}

		using var response = await SendDownloadRequestAsync(httpClient, url, existingLength, cancellationToken);
		response.EnsureSuccessStatusCode();

		if (existingLength > 0 && response.StatusCode != HttpStatusCode.PartialContent)
		{
			File.Delete(destinationPath);
			existingLength = 0;
		}

		using var contentStream = await response.Content.ReadAsStreamAsync(cancellationToken);
		using var fileStream = new FileStream(destinationPath, existingLength > 0 ? FileMode.Append : FileMode.Create, FileAccess.Write, FileShare.None, bufferSize, true);

		var buffer = new byte[bufferSize];
		long totalRead = existingLength;
		int read;

		var stopwatch = System.Diagnostics.Stopwatch.StartNew();

		while ((read = await contentStream.ReadAsync(buffer, 0, buffer.Length, cancellationToken)) > 0)
		{
			await fileStream.WriteAsync(buffer, 0, read, cancellationToken);
			totalRead += read;

			if (stopwatch.ElapsedMilliseconds > 100 || (totalBytes.HasValue && totalRead == totalBytes.Value))
			{
				stopwatch.Restart();
				double progress = (totalBytes.HasValue && totalBytes.Value > 0) ? (double)totalRead / totalBytes.Value : 0;
				onProgress?.Invoke(progress, totalRead, totalBytes);
			}
		}

		// Ensure final update
		double finalProgress = (totalBytes.HasValue && totalBytes.Value > 0) ? (double)totalRead / totalBytes.Value : 0;
		onProgress?.Invoke(finalProgress, totalRead, totalBytes);
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
}
