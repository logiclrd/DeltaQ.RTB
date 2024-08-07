using System;
using System.ComponentModel;
using System.IO;

using DQD.RealTimeBackup.Utility;

namespace DQD.RealTimeBackup.StateCache
{
	public class FileState
	{
		public string Path;
		public string ContentKey;
		public long FileSize;
		public DateTime LastModifiedUTC;
		public string Checksum;

		[EditorBrowsable(EditorBrowsableState.Never)]
		public FileState()
		{
			// Dummy constructor.
			Path = ContentKey = Checksum = "";
		}

		public static FileState FromFile(string path, IChecksum checksum)
		{
			var ret = new FileState();

			ret.Path = path;
			ret.LastModifiedUTC = File.GetLastWriteTimeUtc(path);

			using (var stream = File.OpenRead(path))
			{
				ret.FileSize = stream.Length;
				ret.Checksum = checksum.ComputeChecksum(stream);
			}

			return ret;
		}

		public bool IsMatch(IChecksum checksum)
		{
			if (!File.Exists(Path))
				return false;

			using (var stream = File.OpenRead(Path))
			{
				if (stream.Length != FileSize)
					return false;

				if (checksum.ComputeChecksum(stream) != Checksum)
					return false;

				return true;
			}
		}

		const string EmptyContentKeyToken = "\"\"";

		public static FileState Parse(string serialized)
		{
			string[] parts = serialized.Split(' ', 5);

			var ret = new FileState();

			ret.Path = parts[4];
			ret.ContentKey = parts[3];
			ret.LastModifiedUTC = new DateTime(ticks: long.Parse(parts[2]), DateTimeKind.Utc);
			ret.Checksum = parts[0];
			ret.FileSize = long.Parse(parts[1]);

			if (ret.ContentKey == EmptyContentKeyToken)
				ret.ContentKey = "";

			return ret;
		}

		public override string ToString()
		{
			if (Path.IndexOf('\n') >= 0)
				throw new Exception("Sanity failure: Path contains a newline character.");

			string contentKeySerialized = ContentKey;

			if (string.IsNullOrWhiteSpace(contentKeySerialized))
				contentKeySerialized = EmptyContentKeyToken;

			return $"{Checksum} {FileSize} {LastModifiedUTC.Ticks} {contentKeySerialized} {Path}";
		}
	}
}

