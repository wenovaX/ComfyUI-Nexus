namespace ComfyUI_Nexus.Views.Rail.Tools.Assets;

internal sealed class AssetClipboardController
{
	private readonly List<string> _paths = [];

	internal bool IsCutMode { get; private set; }
	internal int Count => _paths.Count;

	internal void SetCopy(IEnumerable<string> paths)
	{
		_paths.Clear();
		_paths.AddRange(paths);
		IsCutMode = false;
	}

	internal void SetCut(IEnumerable<string> paths)
	{
		_paths.Clear();
		_paths.AddRange(paths);
		IsCutMode = true;
	}

	internal IReadOnlyList<string> Snapshot()
		=> _paths.ToArray();

	internal bool ShouldDim(string path)
		=> IsCutMode && _paths.Contains(path, StringComparer.OrdinalIgnoreCase);

	internal void Clear()
	{
		_paths.Clear();
		IsCutMode = false;
	}
}
