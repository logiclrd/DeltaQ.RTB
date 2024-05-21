﻿//#define EXTRADEBUG

using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;

namespace DeltaQ.RTB.Interop
{
	public class FileAccessNotify : IFileAccessNotify, IDisposable
	{
		int _fd;

		public FileAccessNotify()
		{
			_fd = NativeMethods.fanotify_init(
				FileAccessNotifyFlags.Class.Notification |
				FileAccessNotifyFlags.Report.UniqueFileID |
				FileAccessNotifyFlags.Report.UniqueDirectoryID |
				FileAccessNotifyFlags.Report.IncludeName,
				0);

			if (_fd < 0)
				throw new Exception("Cannot initialize fanotify");
		}

		public void Dispose()
		{
			if (_fd > 0)
			{
				NativeMethods.close(_fd);
				_fd = 0;
			}
		}

		public void MarkPath(string path)
		{
			var mask =
				FileAccessNotifyEventMask.Modified |
				FileAccessNotifyEventMask.ChildDeleted |
				FileAccessNotifyEventMask.ChildMoved;

			int result = NativeMethods.fanotify_mark(
				_fd,
				NativeMethods.FAN_MARK_ADD | NativeMethods.FAN_MARK_FILESYSTEM,
				(long)mask,
				NativeMethods.AT_FDCWD,
				path);

			if (result < 0)
				throw new Exception("[" + Marshal.GetLastWin32Error() + "] Failed to add watch for " + path);
		}

		const int BufferSize = 256 * 1024;

		public void MonitorEvents(Action<FileAccessNotifyEvent> eventCallback, CancellationToken cancellationToken)
		{
			int[] pipeFDs = new int[2];

			int result = NativeMethods.pipe(pipeFDs);

			int cancelFD = pipeFDs[0];
			int cancelSignalFD = pipeFDs[1];

			cancellationToken.Register(() => { NativeMethods.write(cancelSignalFD, new byte[1], 1); });

			IntPtr buffer = IntPtr.Zero;

			result = NativeMethods.posix_memalign(ref buffer, 4096, BufferSize);

			if ((result != 0) || (buffer == IntPtr.Zero))
				throw new Exception("Failed to allocate buffer");

			while (!cancellationToken.IsCancellationRequested)
			{
				var pollFDs = new NativeMethods.PollFD[2];

				pollFDs[0].FileDescriptor = _fd;
				pollFDs[0].RequestedEvents = NativeMethods.POLLIN;

				pollFDs[1].FileDescriptor = cancelFD;
				pollFDs[1].RequestedEvents = NativeMethods.POLLIN;

				result = NativeMethods.poll(pollFDs, 1, NativeMethods.INFTIM);

#if EXTRADEBUG
				Console.WriteLine("got a poll result, cancellation {0}", cancellationToken.IsCancellationRequested);
#endif

				// For some reason, ReturnedEvents doesn't seem to be being set as expected.
				// So instead, we use the heuristic that the only reason cancelFD would be
				// the reason poll returned is if cancellation is requested. So, if cancellation
				// isn't requested, then we should be good to do a read on _fd.

				if (cancellationToken.IsCancellationRequested)
					break;

				// The documentation implies that events cannot be split across read calls.
				int readSize = NativeMethods.read(_fd, buffer, BufferSize);

				if (readSize < 0)
					throw new Exception("Read error");

#if EXTRADEBUG
				Console.WriteLine("Read {0} bytes", readSize);
#endif

				unsafe
				{
					var bufferStream = new UnmanagedMemoryStream((byte *)buffer, readSize);

					IntPtr ptr = buffer;
					IntPtr endPtr = ptr + readSize;

					while (bufferStream.Length - bufferStream.Position >= 4) // Continue as long as there is at least an int to be read
					{
						var eventStream = new UnmanagedMemoryStream(
							bufferStream.PositionPointer,
							bufferStream.Length - bufferStream.Position,
							bufferStream.Length - bufferStream.Position,
							FileAccess.ReadWrite);

						var eventReader = new BinaryReader(eventStream);

						int eventLength = eventReader.ReadInt32();

#if EXTRADEBUG
						Console.WriteLine("-----------");
						Console.WriteLine("event length: {0}", eventLength);
#endif

						if ((eventLength < NativeMethods.EventHeaderLength) || (ptr + eventLength > endPtr))
							break;

						bufferStream.Position += eventLength;

						eventStream.SetLength(eventLength);

#if EXTRADEBUG
						eventStream.Position = 0;
						while (eventStream.Position < eventStream.Length)
						{
							Console.Write("{0:X2} ", eventStream.ReadByte());
						}
						Console.WriteLine();
						eventStream.Position = 4; // We have already read eventLength
#endif

						var metadata = new FileAccessNotifyEventMetadata();

						metadata.Version = eventReader.ReadByte();
						metadata.Reserved = eventReader.ReadByte();
						metadata.MetadataLength = eventReader.ReadInt16();
						metadata.Mask = (FileAccessNotifyEventMask)eventReader.ReadInt64();
						metadata.FileDescriptor = eventReader.ReadInt32();
						metadata.ProcessID = eventReader.ReadInt32();

#if EXTRADEBUG
						Console.WriteLine("  Version: {0}", metadata.Version);
						Console.WriteLine("  Metadata length: {0}", metadata.MetadataLength);
						Console.WriteLine("  Mask: {0}", metadata.Mask);
						Console.WriteLine("  FD: {0}", metadata.FileDescriptor);
						Console.WriteLine("  PID: {0}", metadata.ProcessID);
#endif

						var @event = new FileAccessNotifyEvent();

						@event.Metadata = metadata;

						unsafe
						{
							// TODO: refactor to allow testing
							while (eventStream.Position + 4 < eventStream.Length)
							{
#if EXTRADEBUG
								Console.WriteLine();
								Console.WriteLine("  Event stream: {0} / {1}", eventStream.Position, eventStream.Length);
#endif

								// fanotify_event_info_header
								var infoStream = new UnmanagedMemoryStream(
									eventStream.PositionPointer,
									eventStream.Length - eventStream.Position,
									eventStream.Length - eventStream.Position,
									FileAccess.ReadWrite);

								var infoReader = new BinaryReader(infoStream);

								var infoType = (FileAccessNotifyEventInfoType)infoReader.ReadByte();
								var padding = infoReader.ReadByte();
								var infoStructureLength = infoReader.ReadUInt16();

#if EXTRADEBUG
								Console.WriteLine("  Info structure length: {0}", infoStructureLength);
								Console.WriteLine("  => info structure of type: {0}", infoType);
#endif
								// Sanity check
								if ((infoStructureLength <= 0) || (infoStructureLength > infoStream.Length))
									break;

								infoStream.SetLength(infoStructureLength);

								eventStream.Position += infoStructureLength;

								var structure = new FileAccessNotifyEventInfo();

								structure.Type = infoType;

								switch (infoType)
								{
									case FileAccessNotifyEventInfoType.FileIdentifier:
									case FileAccessNotifyEventInfoType.ContainerIdentifier:
									case FileAccessNotifyEventInfoType.ContainerIdentifierAndFileName:
									case FileAccessNotifyEventInfoType.ContainerIdentifierAndFileName_From:
									case FileAccessNotifyEventInfoType.ContainerIdentifierAndFileName_To:
									{
#if EXTRADEBUG
										Console.WriteLine("######## infoType: {0}", infoType);
#endif

										structure.FileSystemID = infoReader.ReadInt64();

										// struct file_handle
										// {
										//   uint handle_bytes;
										//   int type;
										//   inline byte[] handle;
										// }
										//
										// handle_bytes is the count of bytes in the handle member alone.
										// So, the overall file_handle is of length handle_bytes + 8, to
										// account for the other fields.

										int handle_bytes = infoReader.ReadInt32();

										infoStream.Position -= 4;

										int file_handle_structure_bytes = handle_bytes + 8;

										structure.FileHandle = new byte[file_handle_structure_bytes];

										infoReader.Read(structure.FileHandle, 0, structure.FileHandle.Length);

										if ((infoType == FileAccessNotifyEventInfoType.ContainerIdentifierAndFileName)
									   || (infoType == FileAccessNotifyEventInfoType.ContainerIdentifierAndFileName_From)
										 || (infoType == FileAccessNotifyEventInfoType.ContainerIdentifierAndFileName_To))
										{
											structure.FileName = Marshal.PtrToStringAuto((IntPtr)infoStream.PositionPointer);

#if EXTRADEBUG
											Console.WriteLine("  ## {0} Got filename: {1}", infoType, structure.FileName);
#endif
										}

										@event.InformationStructures.Add(structure);

										break;
									}
								}
							}
						}

#if EXTRADEBUG
						Console.WriteLine("Raising event");
#endif

						eventCallback?.Invoke(@event);

#if EXTRADEBUG
						Console.WriteLine("Event returned");
#endif
					}
				}
			}
		}
	}
}

