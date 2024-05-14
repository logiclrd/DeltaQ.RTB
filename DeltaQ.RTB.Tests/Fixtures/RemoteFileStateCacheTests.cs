using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using NUnit.Framework;

using NSubstitute;

using Bogus;

using AutoBogus;

using FluentAssertions;
using NSubstitute.ExceptionExtensions;

namespace DeltaQ.RTB.Tests
{
	[TestFixture]
	public class RemoteFileStateCacheTests
	{
		class DummyStorage : IRemoteFileStateCacheStorage
		{
			public Dictionary<int, MemoryStream> BatchData = new Dictionary<int, MemoryStream>();
			public Dictionary<int, MemoryStream> NewBatchData = new Dictionary<int, MemoryStream>();

			public void InitializeWithBatches(params IEnumerable<FileState>[] batches)
			{
				foreach (var batch in batches)
				{
					int batchNumber = BatchData.Count + 1;

					using (var writer = OpenBatchFileWriter(batchNumber))
						foreach (var fileState in batch)
							writer.WriteLine(fileState);
				}
			}

			public IEnumerable<int> EnumerateBatches()
			{
				return BatchData.Keys;
			}

			public StreamWriter OpenBatchFileWriter(int batchNumber)
			{
				var stream = BatchData[batchNumber]= new MemoryStream();

				return new StreamWriter(stream) { AutoFlush = true };
			}

			public StreamReader OpenBatchFileReader(int batchNumber)
			{
				return new StreamReader(OpenBatchFileStream(batchNumber), leaveOpen: true);
			}

			public Stream OpenBatchFileStream(int batchNumber)
			{
				var stream = BatchData[batchNumber];

				return new MemoryStream(stream.ToArray());
			}

			public StreamWriter OpenNewBatchFileWriter(int batchNumber)
			{
				if (!NewBatchData.TryGetValue(batchNumber, out var stream))
					NewBatchData[batchNumber] = stream = new MemoryStream();

				return new StreamWriter(stream);
			}

			public void SwitchToConsolidatedFile(int oldBatchNumber, int mergeIntoBatchNumber)
			{
				BatchData.Remove(oldBatchNumber);
				BatchData[mergeIntoBatchNumber] = NewBatchData[mergeIntoBatchNumber];
				NewBatchData.Remove(mergeIntoBatchNumber);
			}
		}

		IAutoFaker CreateAutoFaker()
		{
			return AutoFaker.Create(
				config =>
				{
					config.WithOverride(
						(FileState fs) => fs.Checksum,
						ctx => ctx.Faker.Random.Hash(length: 32));
				});
		}

		[Test]
		public void Constructor_should_load_cache_from_local_storage()
		{
			// Arrange
			var faker = CreateAutoFaker();

			var dummyStorage = new DummyStorage();

			var batch1FileStates = new List<FileState>();
			var batch2FileStates = new List<FileState>();
			var batch3FileStates = new List<FileState>();

			for (int i=0; i < 10; i++)
				batch1FileStates.Add(faker.Generate<FileState>());
			for (int i=0; i < 20; i++)
				batch2FileStates.Add(faker.Generate<FileState>());
			for (int i=0; i < 5; i++)
				batch3FileStates.Add(faker.Generate<FileState>());

			var expectedCache = new Dictionary<string, FileState>();

			foreach (var fileState in batch1FileStates.Concat(batch2FileStates).Concat(batch3FileStates))
				expectedCache[fileState.Path] = fileState;

			dummyStorage.InitializeWithBatches(
				batch1FileStates,
				batch2FileStates,
				batch3FileStates);

			var parameters = new OperatingParameters();
			var timer = Substitute.For<ITimer>();
			var remoteStorage = Substitute.For<IRemoteStorage>();

			// Act
			var result = new RemoteFileStateCache(
				parameters,
				timer,
				dummyStorage,
				remoteStorage);

			// Assert
			result.GetCacheForTest().Should().BeEquivalentTo(expectedCache);
		}

		[Test]
		public void GetFileState_should_return_states_from_cache()
		{
			// Arrange
			var faker = CreateAutoFaker();

			var dummyStorage = new DummyStorage();

			var fileStates = new List<FileState>();

			for (int i=0; i < 10; i++)
				fileStates.Add(faker.Generate<FileState>());

			dummyStorage.InitializeWithBatches(fileStates);

			var parameters = new OperatingParameters();
			var timer = Substitute.For<ITimer>();
			var remoteStorage = Substitute.For<IRemoteStorage>();

			// Act
			var result = new RemoteFileStateCache(
				parameters,
				timer,
				dummyStorage,
				remoteStorage);

			// Assert
			foreach (var fileState in fileStates)
				result.GetFileState(fileState.Path).Should().BeEquivalentTo(fileState);
		}

		[Test]
		public void GetFileState_should_return_null_for_uncached_path()
		{
			// Arrange
			var faker = new Faker();

			var dummyStorage = new DummyStorage();

			var parameters = new OperatingParameters();
			var timer = Substitute.For<ITimer>();
			var remoteStorage = Substitute.For<IRemoteStorage>();

			// Act
			var result = new RemoteFileStateCache(
				parameters,
				timer,
				dummyStorage,
				remoteStorage);

			// Assert
			result.GetFileState(faker.System.FilePath()).Should().BeNull();
		}

		[Test]
		public void UpdateFileState_should_update_cache()
		{
			// Arrange
			var faker = CreateAutoFaker();

			var fileState = faker.Generate<FileState>();
			var newFileState = faker.Generate<FileState>();

			var dummyStorage = new DummyStorage();

			dummyStorage.InitializeWithBatches(new[] { fileState });

			var parameters = new OperatingParameters();
			var timer = Substitute.For<ITimer>();
			var remoteStorage = Substitute.For<IRemoteStorage>();

			var sut = new RemoteFileStateCache(
				parameters,
				timer,
				dummyStorage,
				remoteStorage);

			// Act
			var before = sut.GetFileState(fileState.Path);

			sut.UpdateFileState(fileState.Path, newFileState);

			var after = sut.GetFileState(fileState.Path);

			// Assert
			before.Should().BeEquivalentTo(fileState);
			after.Should().BeEquivalentTo(newFileState);
		}

		[Test]
		public void UpdateFileState_should_append_to_batch()
		{
			// Arrange
			var faker = CreateAutoFaker();

			var fileState = faker.Generate<FileState>();
			var newFileState = faker.Generate<FileState>();

			var dummyStorage = new DummyStorage();

			dummyStorage.InitializeWithBatches(new[] { fileState });

			var parameters = new OperatingParameters();
			var timer = Substitute.For<ITimer>();
			var remoteStorage = Substitute.For<IRemoteStorage>();

			var sut = new RemoteFileStateCache(
				parameters,
				timer,
				dummyStorage,
				remoteStorage);

			// Act
			var maxBatchNumberBefore = dummyStorage.EnumerateBatches().Max();

			sut.UpdateFileState(fileState.Path, newFileState);

			var maxBatchNumberAfter = dummyStorage.EnumerateBatches().Max();

			// Assert
			maxBatchNumberAfter.Should().Be(maxBatchNumberBefore + 1);

			using (var reader = dummyStorage.OpenBatchFileReader(maxBatchNumberAfter))
			{
				var persistedFileState = FileState.Parse(reader.ReadLine()!);

				persistedFileState.Should().BeEquivalentTo(newFileState);
			}
		}

		[Test]
		public void UploadCurrentBatch_should_send_batch_to_remote_storage()
		{
			// Arrange
			var faker = CreateAutoFaker();

			var dummyStorage = new DummyStorage();

			var fileStates = new List<FileState>();
			var newFileStates = new List<FileState>();

			for (int i=0; i < 10; i++)
				fileStates.Add(faker.Generate<FileState>());
			for (int i=0; i < 15; i++)
				newFileStates.Add(faker.Generate<FileState>());

			dummyStorage.InitializeWithBatches(fileStates);

			var parameters = new OperatingParameters();
			var timer = Substitute.For<ITimer>();
			var remoteStorage = Substitute.For<IRemoteStorage>();

			var uploads = new List<(string ServerPath, byte[] Content)>();

			remoteStorage.When(x => x.UploadFile(Arg.Any<string>(), Arg.Any<Stream>())).Do(
				x =>
				{
					var serverPath = x.Arg<string>();
					var content = x.Arg<Stream>();

					byte[] contentBytes = new byte[content.Length];

					content.Read(contentBytes, 0, contentBytes.Length);

					uploads.Add((serverPath, contentBytes));
				});

			var sut = new RemoteFileStateCache(
				parameters,
				timer,
				dummyStorage,
				remoteStorage);

			foreach (var fileState in newFileStates)
				sut.UpdateFileState(fileState.Path, fileState);

			int batchNumber = sut.GetCurrentBatchNumberForTest();

			// Act
			sut.UploadCurrentBatch();

			// Assert
			uploads.Should().HaveCount(1);

			var upload = uploads.Single();

			upload.ServerPath.Should().Be("/state/" + batchNumber);

			var stream = new MemoryStream(upload.Content);

			var uploadedFileStates = new List<FileState>();

			using (var reader = new StreamReader(stream))
			{
				while (true)
				{
					string? line = reader.ReadLine();

					if (line == null)
						break;

					uploadedFileStates.Add(FileState.Parse(line));
				}
			}

			uploadedFileStates.Should().BeEquivalentTo(newFileStates);
		}

		public void ConsolidateOldestBatch_should_merge_oldest_batch_into_next_oldest()
		{
			// Arrange
			var faker = CreateAutoFaker();

			var dummyStorage = new DummyStorage();

			var batch1FileStates = new List<FileState>();
			var batch2FileStates = new List<FileState>();
			var batch3FileStates = new List<FileState>();

			for (int i=0; i < 10; i++)
				batch1FileStates.Add(faker.Generate<FileState>());
			for (int i=0; i < 20; i++)
				batch2FileStates.Add(faker.Generate<FileState>());
			for (int i=0; i < 5; i++)
				batch3FileStates.Add(faker.Generate<FileState>());

			// Ensure that some of the paths match.
			for (int i=5; i < 10; i++)
				batch2FileStates[i].Path = batch1FileStates[i].Path;

			var expectedMergedFileStates = new List<FileState>(batch1FileStates);

			// Overlay batch 2 onto batch 1, preserving order
			foreach (var fileState in batch2FileStates)
			{
				bool found = false;

				for (int i=0; i < expectedMergedFileStates.Count; i++)
				{
					if (expectedMergedFileStates[i].Path == fileState.Path)
					{
						expectedMergedFileStates[i] = fileState;
						found = true;
						break;
					}
				}

				if (!found)
					expectedMergedFileStates.Add(fileState);
			}

			dummyStorage.InitializeWithBatches(
				batch1FileStates,
				batch2FileStates,
				batch3FileStates);

			var parameters = new OperatingParameters();
			var timer = Substitute.For<ITimer>();
			var remoteStorage = Substitute.For<IRemoteStorage>();

			var uploads = new List<(string ServerPath, byte[] Content)>();

			remoteStorage.When(x => x.UploadFile(Arg.Any<string>(), Arg.Any<Stream>())).Do(
				x =>
				{
					var serverPath = x.Arg<string>();
					var content = x.Arg<Stream>();

					byte[] contentBytes = new byte[content.Length];

					content.Read(contentBytes, 0, contentBytes.Length);

					uploads.Add((serverPath, contentBytes));
				});

			var sut = new RemoteFileStateCache(
				parameters,
				timer,
				dummyStorage,
				remoteStorage);

			// Act
			int deletedBatchNumber = sut.ConsolidateOldestBatch();

			// Assert
			deletedBatchNumber.Should().Be(1);

			int mergedBatchNumber = deletedBatchNumber + 1;

			uploads.Should().HaveCount(1);

			var upload = uploads.Single();

			upload.ServerPath.Should().Be("/state/" + mergedBatchNumber);

			var stream = new MemoryStream(upload.Content);

			var uploadedFileStates = new List<FileState>();

			using (var reader = new StreamReader(stream))
			{
				while (true)
				{
					string? line = reader.ReadLine();

					if (line == null)
						break;

					uploadedFileStates.Add(FileState.Parse(line));
				}
			}

			uploadedFileStates.Should().BeEquivalentTo(expectedMergedFileStates);
		}
	}
}