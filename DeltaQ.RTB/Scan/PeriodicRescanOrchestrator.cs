using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;

using DeltaQ.RTB.Agent;
using DeltaQ.RTB.FileSystem;
using DeltaQ.RTB.Interop;
using DeltaQ.RTB.StateCache;
using DeltaQ.RTB.SurfaceArea;

namespace DeltaQ.RTB.Scan
{
	public class PeriodicRescanOrchestrator : Scanner, IPeriodicRescanOrchestrator
	{
		OperatingParameters _parameters;

		IBackupAgent _backupAgent;
		IRemoteFileStateCache _remoteFileStateCache;

		public PeriodicRescanOrchestrator(OperatingParameters parameters, ISurfaceArea surfaceArea, IBackupAgent backupAgent, IRemoteFileStateCache remoteFileStateCache, IZFS zfs, IStat stat)
			: base(surfaceArea, stat)
		{
			_parameters = parameters;

			_backupAgent = backupAgent;
			_remoteFileStateCache = remoteFileStateCache;
		}

		public void PerformPeriodicRescan(CancellationToken cancellationToken)
		{
			NonQuietDiagnosticOutput("[PR] Beginning periodic rescan");

			var deletedPaths = new HashSet<string>();

			deletedPaths.UnionWith(_remoteFileStateCache.EnumeratePaths());

			NonQuietDiagnosticOutput("[PR] => {0} path{1} currently being tracked", deletedPaths.Count, deletedPaths.Count == 1 ? "" : "s");

			foreach (var path in EnumerateAllFilesInSurfaceArea())
			{
				if (cancellationToken.IsCancellationRequested)
					break;

				deletedPaths.Remove(path);

				var fileState = _remoteFileStateCache.GetFileState(path);

				bool checkPath = false;

				if (fileState == null)
				{
					NonQuietDiagnosticOutput("[PR] - New path: {0}", path);
					checkPath = true;
				}
				else
				{
					var fileInfo = new FileInfo(fileState.Path);
					
					if ((fileState.FileSize != fileInfo.Length)
					 || (fileState.LastModifiedUTC != fileInfo.LastWriteTimeUtc))
					{
						NonQuietDiagnosticOutput("[PR] - Changed path: {0}", path);
						checkPath = true;
					}
				}

				if (checkPath)
				{
					_backupAgent.CheckPath(path);

					if (_backupAgent.OpenFilesCount >= _parameters.QueueHighWaterMark)
					{
						while (_backupAgent.OpenFilesCount >= _parameters.QueueLowWaterMark)
							Thread.Sleep(TimeSpan.FromSeconds(10));
					}
				}
			}

			if (deletedPaths.Count < 50)
				foreach (var deletedPath in deletedPaths)
					NonQuietDiagnosticOutput("[PR] - Deleted path: {0}", deletedPath);
			else
				NonQuietDiagnosticOutput("[PR] - Detected {0:##0,0} deleted paths", deletedPaths.Count);

			_backupAgent.CheckPaths(deletedPaths);

			NonQuietDiagnosticOutput("[PR] Periodic rescan complete");
		}
	}
}