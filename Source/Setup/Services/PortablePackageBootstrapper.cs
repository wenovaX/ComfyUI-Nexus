namespace ComfyUI_Nexus.Setup.Services;

using System.IO.Compression;
using System.Text;
using ComfyUI_Nexus.Configuration;

internal static class PortablePackageBootstrapper
{
	private const int FooterLength = sizeof(long) + 8;
	private static readonly byte[] FooterMagic = Encoding.ASCII.GetBytes("NEXUSPKG");

	internal static void Materialize()
	{
		if (NexusStorageLayout.IsStoreDistribution)
		{
			ValidatePackagedRuntimePackages();
			return;
		}

		string rootPath = ComfyInstallService.PackageRoot;
		if (File.Exists(Path.Combine(rootPath, "ComfyUI-Nexus.csproj")))
		{
			return;
		}

		if (HasMaterializedPackageSet())
		{
			return;
		}

		string? executablePath = Environment.ProcessPath;
		if (string.IsNullOrWhiteSpace(executablePath) || !File.Exists(executablePath))
		{
			throw new InvalidOperationException("Portable executable path is unavailable.");
		}

		using var executable = new FileStream(executablePath, FileMode.Open, FileAccess.Read, FileShare.Read);
		if (!TryReadPayloadLocation(executable, out long payloadOffset, out long payloadLength))
		{
			throw new InvalidOperationException("Portable runtime packages are missing. Reinstall from the release ZIP and keep LocalRuntime\\Packages beside ComfyUI-Nexus.exe.");
		}

		using var payload = new BoundedReadStream(executable, payloadOffset, payloadLength);
		using var archive = new ZipArchive(payload, ZipArchiveMode.Read, leaveOpen: false);
		foreach (ZipArchiveEntry entry in archive.Entries)
		{
			if (string.IsNullOrEmpty(entry.Name))
			{
				continue;
			}

			string relativePath = entry.FullName.Replace('/', Path.DirectorySeparatorChar);
			string destinationPath = Path.Combine(
				ComfyInstallService.LocalRuntimePath,
				"Packages",
				relativePath);

			if (File.Exists(destinationPath) && new FileInfo(destinationPath).Length == entry.Length)
			{
				continue;
			}

			Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
			string temporaryPath = destinationPath + ".tmp";
			using (Stream source = entry.Open())
			using (var target = new FileStream(temporaryPath, FileMode.Create, FileAccess.Write, FileShare.None))
			{
				source.CopyTo(target);
			}

			File.Move(temporaryPath, destinationPath, overwrite: true);
		}
	}

	private static bool HasMaterializedPackageSet()
	{
		string packageRoot = ComfyInstallService.RuntimePackagesPath;
		var packageSpec = RuntimePackageSpecService.LoadFromPackageRoot(ComfyInstallService.PackageRoot);
		return packageSpec.GetRequiredPackageRelativePaths()
			.All(relativePath => File.Exists(Path.Combine(packageRoot, relativePath)));
	}

	private static void ValidatePackagedRuntimePackages()
	{
		if (!HasMaterializedPackageSet())
		{
			throw new InvalidOperationException("Store runtime packages are missing from the app package.");
		}
	}

	private static bool TryReadPayloadLocation(Stream executable, out long payloadOffset, out long payloadLength)
	{
		payloadOffset = 0;
		payloadLength = 0;
		if (executable.Length < FooterLength)
		{
			return false;
		}

		executable.Seek(-FooterLength, SeekOrigin.End);
		using var reader = new BinaryReader(executable, Encoding.UTF8, leaveOpen: true);
		payloadLength = reader.ReadInt64();
		byte[] magic = reader.ReadBytes(FooterMagic.Length);
		if (!magic.SequenceEqual(FooterMagic) || payloadLength <= 0)
		{
			return false;
		}

		payloadOffset = executable.Length - FooterLength - payloadLength;
		return payloadOffset >= 0;
	}

	private sealed class BoundedReadStream : Stream
	{
		private readonly Stream _source;
		private readonly long _offset;
		private readonly long _length;
		private long _position;

		internal BoundedReadStream(Stream source, long offset, long length)
		{
			_source = source;
			_offset = offset;
			_length = length;
			_source.Position = offset;
		}

		public override bool CanRead => true;
		public override bool CanSeek => true;
		public override bool CanWrite => false;
		public override long Length => _length;

		public override long Position
		{
			get => _position;
			set => Seek(value, SeekOrigin.Begin);
		}

		public override int Read(byte[] buffer, int offset, int count)
		{
			int available = (int)Math.Min(count, _length - _position);
			if (available <= 0)
			{
				return 0;
			}

			_source.Position = _offset + _position;
			int read = _source.Read(buffer, offset, available);
			_position += read;
			return read;
		}

		public override long Seek(long offset, SeekOrigin origin)
		{
			long target = origin switch
			{
				SeekOrigin.Begin => offset,
				SeekOrigin.Current => _position + offset,
				SeekOrigin.End => _length + offset,
				_ => throw new ArgumentOutOfRangeException(nameof(origin))
			};

			if (target < 0 || target > _length)
			{
				throw new IOException("Attempted to seek outside the portable payload.");
			}

			_position = target;
			return _position;
		}

		public override void Flush()
		{
		}

		public override void SetLength(long value)
			=> throw new NotSupportedException();

		public override void Write(byte[] buffer, int offset, int count)
			=> throw new NotSupportedException();
	}
}
