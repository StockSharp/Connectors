namespace StockSharp.InteractiveBrokers;

/// <summary>
/// Request Soft Dollar Tier information message.
/// </summary>
public class SoftDollarTierMarketDataMessage : MarketDataMessage
{
	/// <summary>
	/// Initializes a new instance of the <see cref="SoftDollarTierMarketDataMessage"/>.
	/// </summary>
	public SoftDollarTierMarketDataMessage()
	{
		DataType2 = ExtendedDataTypes.SoftDollarTier;
	}

	/// <summary>
	/// Create a copy of <see cref="SoftDollarTierMarketDataMessage"/>.
	/// </summary>
	/// <returns>Copy.</returns>
	public override Message Clone()
	{
		var clone = new SoftDollarTierMarketDataMessage();
		CopyTo(clone);
		return clone;
	}
}

/// <summary>
/// Soft Dollar Tier message.
/// </summary>
public class SoftDollarTierMessage : BaseSubscriptionIdMessage<SoftDollarTierMessage>
{
	/// <summary>
	/// Initializes a new instance of the <see cref="SoftDollarTierMessage"/>.
	/// </summary>
	public SoftDollarTierMessage()
		: base(ExtendedMessageTypes.SoftDollarTier)
	{
	}

	/// <summary>
	/// Tiers.
	/// </summary>
	public IEnumerable<SoftDollarTier> Tiers { get; set; }

	/// <inheritdoc />
	public override DataType DataType => ExtendedDataTypes.SoftDollarTier;

	/// <summary>
	/// Create a copy of <see cref="SoftDollarTierMessage"/>.
	/// </summary>
	/// <returns>Copy.</returns>
	public override Message Clone()
	{
		var copy = new SoftDollarTierMessage
		{
			Tiers = Tiers?.Select(t => t.Clone()).ToArray(),
		};

		CopyTo(copy);

		return copy;
	}
}