using DeltaQ.RTB.Bridge.Serialization;

namespace DeltaQ.RTB.Scan
{
	public class RescanStatus : ScanStatus
	{
		[FieldOrder(100)]
		public int RescanNumber;

		[FieldOrder(101)]
		public bool IsRunning;

		[FieldOrder(102)]
		public int NumberOfFilesLeftToMatch;
	}
}
