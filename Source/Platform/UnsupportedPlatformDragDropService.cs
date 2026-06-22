namespace ComfyUI_Nexus.Platform;

public class UnsupportedPlatformDragDropService : IPlatformDragDropService
{
	public virtual Task<bool> ContainsFolderAsync(DragEventArgs e)
		=> Task.FromResult(false);

	public virtual Task<IReadOnlyList<string>> GetDroppedPathsAsync(DragEventArgs e)
		=> Task.FromResult(ReadDataProperties(e.Data));

	public virtual Task<IReadOnlyList<string>> GetDroppedPathsAsync(DropEventArgs e)
		=> Task.FromResult(ReadDataProperties(e.Data));

	public virtual Task SetDragStartingPathsAsync(DragStartingEventArgs e, IReadOnlyList<string> paths)
		=> Task.CompletedTask;

	public virtual Task SetDragStartingTextAsync(DragStartingEventArgs e, string text)
		=> Task.CompletedTask;

	protected static IReadOnlyList<string> ReadDataProperties(object? dataPackage)
	{
		var results = new List<string>();
		try
		{
			var properties = dataPackage?.GetType().GetProperty("Properties")?.GetValue(dataPackage);
			if (properties is not IDictionary<string, object> values)
			{
				return results;
			}

			if (values.TryGetValue("paths", out object? rawPaths) && rawPaths is IReadOnlyList<string> paths)
			{
				results.AddRange(paths);
			}
			else if (values.TryGetValue("path", out object? rawPath) && rawPath is string path && !string.IsNullOrWhiteSpace(path))
			{
				results.Add(path);
			}
		}
		catch
		{
		}

		return results;
	}
}
