namespace ComfyUI_Nexus.Platform;

public sealed record PlatformFeatureResult<T>(
	bool IsSupported,
	bool IsSuccess,
	T? Value,
	string? Message)
{
	public static PlatformFeatureResult<T> Success(T value)
		=> new(true, true, value, null);

	public static PlatformFeatureResult<T> Canceled()
		=> new(true, false, default, null);

	public static PlatformFeatureResult<T> NotSupported(string message)
		=> new(false, false, default, message);

	public static PlatformFeatureResult<T> Failed(string message)
		=> new(true, false, default, message);
}
