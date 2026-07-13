namespace ComfyUI_Nexus.Setup.Services;

using System.Text.Json;
using System.Text.Json.Serialization;

internal sealed class RuntimePackageSpecService
{
	internal const string SpecRelativePath = "LocalRuntime\\Packages\\runtime-package-spec.json";

	private static readonly JsonSerializerOptions SerializerOptions = new()
	{
		PropertyNameCaseInsensitive = true,
		ReadCommentHandling = JsonCommentHandling.Skip,
		AllowTrailingCommas = true
	};

	internal static RuntimePackageSpec Load()
		=> LoadFromRoot(ComfyInstallService.RootPath);

	internal static RuntimePackageSpec LoadFromRoot(string rootPath)
	{
		string specPath = Path.Combine(rootPath, SpecRelativePath);
		if (!File.Exists(specPath))
		{
			return RuntimePackageSpec.CreateFallback();
		}

		using var stream = File.OpenRead(specPath);
		var spec = JsonSerializer.Deserialize<RuntimePackageSpec>(stream, SerializerOptions)
			?? throw new InvalidOperationException("Package spec is empty or invalid.");
		spec.Validate();
		return spec;
	}

	internal static string GetPackageRoot(string rootPath)
		=> Path.Combine(rootPath, "LocalRuntime", "Packages");
}

internal sealed record RuntimePackageSpec(
	[property: JsonPropertyName("version")] int Version,
	[property: JsonPropertyName("git")] RuntimePackageFileSpec Git,
	[property: JsonPropertyName("python")] PythonRuntimePackageSpec Python,
	[property: JsonPropertyName("comfy")] RuntimeOptionalPackageSpec? Comfy,
	[property: JsonPropertyName("bridge")] BridgePackageSpec Bridge)
{
	internal static RuntimePackageSpec CreateFallback()
		=> new(
			1,
			new RuntimePackageFileSpec(
				"Git",
				"MinGit-2.55.0-rc0-64-bit.zip",
				"42825BAE66FA4B5580060E914CFF04C63085C2CDC440CC59A61E28B1091A41B7"),
			new PythonRuntimePackageSpec(
				"Python",
				"python-3.13.14-win-x64-runtime.zip",
				"7C444C215626E02C11778A5BBB6BBCC370CA0BD06CDC9C4760CAC06C54769D0E",
				"python-3.13.14-win-x64-runtime.manifest.json",
				"3.13.14",
				"win-x64",
				"python",
				"https://www.python.org/downloads/release/python-31314/",
				"python-3.13.14-amd64.exe",
				"C54D9B9BBB8A36E6489363DDD01139707FD781D72F1F9E90C7EC65D0061368E0",
				"Python Software Foundation"),
			new RuntimeOptionalPackageSpec(
				"ComfyUI",
				"ComfyUI-v0.27.0-source.zip",
				"62A2E70E143869B1C5B768F874622D402FE53B9D69F99A14CF83E6C2A97BB09C",
				"v0.27.0",
				"bb131be",
				"https://github.com/Comfy-Org/ComfyUI/releases/tag/v0.27.0"),
			new BridgePackageSpec(HudBridgeInstaller.NexusBridgePackageFolderName, "__init__.py"));

	internal void Validate()
	{
		if (Version <= 0)
		{
			throw new InvalidOperationException("Package spec version must be positive.");
		}

		Git.Validate("Git");
		Python.Validate("Python");
		Comfy?.Validate("ComfyUI");
		Bridge.Validate();
	}

	internal string GetGitPackagePath(string rootPath)
		=> Path.Combine(RuntimePackageSpecService.GetPackageRoot(rootPath), Git.Folder, Git.File);

	internal string GetPythonPackagePath(string rootPath)
		=> Path.Combine(RuntimePackageSpecService.GetPackageRoot(rootPath), Python.Folder, Python.File);

	internal string GetPythonManifestPath(string rootPath)
		=> Path.Combine(RuntimePackageSpecService.GetPackageRoot(rootPath), Python.Folder, Python.Manifest);

	internal IReadOnlyList<string> GetRequiredReleaseRelativePaths()
	{
		var paths = new List<string>
		{
			RuntimePackageSpecService.SpecRelativePath,
			Path.Combine("LocalRuntime", "Packages", Git.Folder, Git.File),
			Path.Combine("LocalRuntime", "Packages", Python.Folder, Python.File),
			Path.Combine("LocalRuntime", "Packages", Python.Folder, Python.Manifest),
			Path.Combine("LocalRuntime", "Packages", Bridge.Folder, Bridge.RequiredFile.Replace('/', Path.DirectorySeparatorChar))
		};
		if (Comfy is not null)
		{
			paths.Add(Path.Combine("LocalRuntime", "Packages", Comfy.Folder, Comfy.File));
		}

		return paths;
	}

	internal IReadOnlyList<string> GetRequiredPackageRelativePaths()
	{
		var paths = new List<string>
		{
			"runtime-package-spec.json",
			Path.Combine(Git.Folder, Git.File),
			Path.Combine(Python.Folder, Python.File),
			Path.Combine(Python.Folder, Python.Manifest),
			Path.Combine(Bridge.Folder, Bridge.RequiredFile.Replace('/', Path.DirectorySeparatorChar))
		};
		if (Comfy is not null)
		{
			paths.Add(Path.Combine(Comfy.Folder, Comfy.File));
		}

		return paths;
	}
}

internal record RuntimePackageFileSpec(
	[property: JsonPropertyName("folder")] string Folder,
	[property: JsonPropertyName("file")] string File,
	[property: JsonPropertyName("sha256")] string Sha256)
{
	internal virtual void Validate(string label)
	{
		if (string.IsNullOrWhiteSpace(Folder) ||
			string.IsNullOrWhiteSpace(File) ||
			string.IsNullOrWhiteSpace(Sha256))
		{
			throw new InvalidOperationException($"{label} package spec is incomplete.");
		}

		ValidateRelativeSegment(Folder, $"{label} package folder");
		ValidateRelativeSegment(File, $"{label} package file");
	}

	internal static void ValidateRelativeSegment(string value, string label)
	{
		if (Path.IsPathRooted(value) ||
			value.Contains("..", StringComparison.Ordinal) ||
			value.IndexOfAny(Path.GetInvalidPathChars()) >= 0)
		{
			throw new InvalidOperationException($"{label} must be a safe relative path.");
		}
	}
}

internal sealed record RuntimeOptionalPackageSpec(
	[property: JsonPropertyName("folder")] string Folder,
	[property: JsonPropertyName("file")] string File,
	[property: JsonPropertyName("sha256")] string? Sha256 = null,
	[property: JsonPropertyName("version")] string? Version = null,
	[property: JsonPropertyName("revision")] string? Revision = null,
	[property: JsonPropertyName("source")] string? Source = null)
{
	internal void Validate(string label)
	{
		if (string.IsNullOrWhiteSpace(Folder) || string.IsNullOrWhiteSpace(File))
		{
			throw new InvalidOperationException($"{label} package spec is incomplete.");
		}

		RuntimePackageFileSpec.ValidateRelativeSegment(Folder, $"{label} package folder");
		RuntimePackageFileSpec.ValidateRelativeSegment(File, $"{label} package file");
		if (Sha256 is not null && string.IsNullOrWhiteSpace(Sha256))
		{
			throw new InvalidOperationException($"{label} package SHA-256 is empty.");
		}
	}
}

internal sealed record PythonRuntimePackageSpec(
	[property: JsonPropertyName("folder")] string Folder,
	[property: JsonPropertyName("file")] string File,
	[property: JsonPropertyName("sha256")] string Sha256,
	[property: JsonPropertyName("manifest")] string Manifest,
	[property: JsonPropertyName("version")] string Version,
	[property: JsonPropertyName("platform")] string Platform,
	[property: JsonPropertyName("layout_root")] string LayoutRoot,
	[property: JsonPropertyName("source")] string Source,
	[property: JsonPropertyName("source_installer")] string SourceInstaller,
	[property: JsonPropertyName("source_installer_sha256")] string SourceInstallerSha256,
	[property: JsonPropertyName("publisher")] string Publisher)
{
	internal void Validate(string label)
	{
		if (string.IsNullOrWhiteSpace(Folder) ||
			string.IsNullOrWhiteSpace(File) ||
			string.IsNullOrWhiteSpace(Sha256))
		{
			throw new InvalidOperationException($"{label} package spec is incomplete.");
		}

		if (string.IsNullOrWhiteSpace(Manifest) ||
			string.IsNullOrWhiteSpace(Version) ||
			string.IsNullOrWhiteSpace(Platform) ||
			string.IsNullOrWhiteSpace(LayoutRoot) ||
			string.IsNullOrWhiteSpace(Source) ||
			string.IsNullOrWhiteSpace(SourceInstaller) ||
			string.IsNullOrWhiteSpace(SourceInstallerSha256) ||
			string.IsNullOrWhiteSpace(Publisher))
		{
			throw new InvalidOperationException("Python package spec is incomplete.");
		}

		RuntimePackageFileSpec.ValidateRelativeSegment(Folder, "Python package folder");
		RuntimePackageFileSpec.ValidateRelativeSegment(File, "Python package file");
		RuntimePackageFileSpec.ValidateRelativeSegment(Manifest, "Python package manifest");
	}
}

internal sealed record BridgePackageSpec(
	[property: JsonPropertyName("folder")] string Folder,
	[property: JsonPropertyName("required_file")] string RequiredFile)
{
	internal void Validate()
	{
		if (string.IsNullOrWhiteSpace(Folder) || string.IsNullOrWhiteSpace(RequiredFile))
		{
			throw new InvalidOperationException("Bridge package spec is incomplete.");
		}

		RuntimePackageFileSpec.ValidateRelativeSegment(Folder, "Bridge package folder");
		RuntimePackageFileSpec.ValidateRelativeSegment(RequiredFile.Replace('/', Path.DirectorySeparatorChar), "Bridge required file");
	}
}
