using System;
using DeltaQ.RTB.Bridge.Serialization;

namespace DeltaQ.RTB.Bridge.Messages
{
	public class BridgeMessage_ReceiveNotifications_Request : BridgeRequestMessage
	{
		public override BridgeMessageType MessageType => BridgeMessageType.ReceiveNotifications_Request;

		[FieldOrder(0)]
		public long LastMessageID;
		[FieldOrder(1)]
		public TimeSpan Timeout;
	}
}
