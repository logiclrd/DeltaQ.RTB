using System.Collections.Generic;

using DQD.RealTimeBackup.Agent;

using DQD.CommandLineParser;

namespace DQD.RealTimeBackup
{
#pragma warning disable 649
	public class CommandLineArguments
	{
		[Switch("/QUIET", Description = "Disables most output.")]
		public bool Quiet;
		[Switch("/VERBOSE", Description = "Enables additional informational output, typically for debugging. Takes precedence over Quiet.")]
		public bool Verbose;

		[Switch("/DISABLEFAN", Description = "Disables the fanotify integration, which shuts off realtime change detection.")]
		public bool DisableFAN;

		[Switch("/INITIALBACKUPTHENMONITOR", Description = "Performs an initial backup of everything found in the surface area. Any cached file state is erased and recreated from scratch. After the initial backup is complete, fanotify integration is enabled and continuous backup operation is enabled.")]
		public bool InitialBackupThenMonitor;
		[Switch("/INITIALBACKUPTHENEXIT", Description = "Performs an initial backup of everything found in the surface area, and then exits. Any cached file state is erased and recreated from scratch. This switch implies /DISABLEFAN.")]
		public bool InitialBackupThenExit;

		public const string DefaultConfigurationPath = "/etc/DQD.RealTimeBackup.xml";

		[Argument("/CONFIG", Description = "Path to the configuration file. The default is " + DefaultConfigurationPath)]
		public string ConfigurationPath = DefaultConfigurationPath;

		[Argument("/CHECK", Description = "Indicates a path that should be immediately considered for Update or Deletion. Use /MOVE for moves & renames.")]
		public List<string> PathsToCheck = new List<string>();

		[Argument("/MOVE", Properties = ["FromPath", "ToPath"],
			Description =
				"Indicates a path that should be immediately considered as having been moved. Note that the file contents are not checked to ensure that they have not " +
				"changed. If the file contents might have changed, you should also indicate the \"to\" path with the /CHECK argument.")]
		public List<MoveAction> PathsToMove = new List<MoveAction>();

		[Argument("/LOGFILE", Description = "Path to a log file to which the same output being sent to the console is written.")]
		public string? LogFilePath = null;

		[Argument("/LOGFILEMAXLINES", Description = "Maximum number of lines to write to the log file. When the file reaches this many lines, it is rotated to '.old' and a new file is started. The number of lines retained is always between this number and twice this number.")]
		public int LogFileMaxLines = 10000;

		[Argument("/ERRORLOGFILE", Description = "Path to a log file to which important error information is written. This file should be monitored to ensure that there are no consistency issues with the backup. The default value is: /var/log/DQD.RealTimeBackup.error.log")]
		public string? ErrorLogFilePath = null;

		[Argument("/FANDEBUG", Description = "For debugging, a separate file that contains detailed logging of File Access Notify operations.")]
		public string? FileAccessNotifyDebugLogPath = null;

		[Argument("/RFSCDEBUG", Description = "For debugging, a separate file that contains detailed logging of Remote File State Cache operations.")]
		public string? RemoteFileStateCacheDebugLogPath = null;

		[Argument("/ZFSDEBUG", Description = "For debugging, a separate file that contains information relating to ZFS snapshots and their lifetime.")]
		public string? ZFSDebugLogPath = null;

		[Argument("/WRITECONFIG", Description = "Writes the Operating Parameters that were built up during initialization to the specified path. Can be used to create a baseline for a persistent configuration file.")]
		public string? WriteConfig = null;

		[Switch("/?")]
		public bool ShowUsage;
	}
#pragma warning restore 649
}

