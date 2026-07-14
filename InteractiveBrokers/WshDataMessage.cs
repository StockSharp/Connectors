namespace StockSharp.InteractiveBrokers;

/// <summary>
/// Wall Street Horizon data message.
/// </summary>
public abstract class WshDataMessage<TMessage> : BaseSubscriptionIdMessage<TMessage>
	where TMessage : WshDataMessage<TMessage>, new()
{
	/// <summary>
	/// Initializes a new instance of the <see cref="WshDataMessage{TMessage}"/>.
	/// </summary>
	/// <param name="type"><see cref="MessageTypes"/></param>
	protected WshDataMessage(MessageTypes type)
		: base(type)
	{
	}

	/// <summary>
	/// Data (JSON format).
	/// </summary>
	public string Data { get; set; }

	/// <inheritdoc />
	public override void CopyTo(TMessage destination)
	{
		destination.Data = Data;
		base.CopyTo(destination);
	}
}

/// <summary>
/// Wall Street Horizon meta data message.
/// </summary>
public class WshMetaDataMessage : WshDataMessage<WshMetaDataMessage>
{
	/// <summary>
	/// Initializes a new instance of the <see cref="WshMetaDataMessage"/>.
	/// </summary>
	public WshMetaDataMessage()
		: base(ExtendedMessageTypes.WshMetaData)
	{
	}

	/// <inheritdoc />
	public override DataType DataType => ExtendedDataTypes.WshMetaData;

	/// <inheritdoc />
	public override Message Clone()
	{
		var clone = new WshMetaDataMessage();
		CopyTo(clone);
		return clone;
	}
}

/// <summary>
/// Wall Street Horizon event data message.
/// </summary>
public class WshEventDataMessage : WshDataMessage<WshEventDataMessage>
{
	/// <summary>
	/// Initializes a new instance of the <see cref="WshEventDataMessage"/>.
	/// </summary>
	public WshEventDataMessage()
		: base(ExtendedMessageTypes.WshEventData)
	{
	}

	/// <inheritdoc />
	public override DataType DataType => ExtendedDataTypes.WshEventData;

	/// <inheritdoc />
	public override Message Clone()
	{
		var clone = new WshEventDataMessage();
		CopyTo(clone);
		return clone;
	}
}