namespace StockSharp.Pacifica;

/// <summary>Pacifica trigger-price sources.</summary>
[DataContract]
[Serializable]
[JsonConverter(typeof(StringEnumConverter))]
public enum PacificaTriggerPriceTypes
{
	/// <summary>Mark price.</summary>
	[EnumMember(Value = "mark_price")]
	MarkPrice,

	/// <summary>Last trade price.</summary>
	[EnumMember(Value = "last_trade_price")]
	LastTradePrice,

	/// <summary>Mid price.</summary>
	[EnumMember(Value = "mid_price")]
	MidPrice,
}

/// <summary>Pacifica order condition.</summary>
[Serializable]
[DataContract]
[Display(
	ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.PacificaKey)]
public class PacificaOrderCondition : OrderCondition
{
	/// <summary>Restrict the order to reducing an existing position.</summary>
	[DataMember]
	public bool IsReduceOnly
	{
		get => Parameters.TryGetValue(nameof(IsReduceOnly), out var value) &&
			value is true;
		set => Parameters[nameof(IsReduceOnly)] = value;
	}

	/// <summary>Optional take-profit trigger price.</summary>
	[DataMember]
	public decimal? TakeProfitPrice
	{
		get => Parameters.TryGetValue(nameof(TakeProfitPrice), out var value)
			? (decimal?)value
			: null;
		set => Parameters[nameof(TakeProfitPrice)] = value;
	}

	/// <summary>Optional take-profit limit price.</summary>
	[DataMember]
	public decimal? TakeProfitLimitPrice
	{
		get => Parameters.TryGetValue(nameof(TakeProfitLimitPrice),
			out var value)
				? (decimal?)value
				: null;
		set => Parameters[nameof(TakeProfitLimitPrice)] = value;
	}

	/// <summary>Optional stop-loss trigger price.</summary>
	[DataMember]
	public decimal? StopLossPrice
	{
		get => Parameters.TryGetValue(nameof(StopLossPrice), out var value)
			? (decimal?)value
			: null;
		set => Parameters[nameof(StopLossPrice)] = value;
	}

	/// <summary>Optional stop-loss limit price.</summary>
	[DataMember]
	public decimal? StopLossLimitPrice
	{
		get => Parameters.TryGetValue(nameof(StopLossLimitPrice),
			out var value)
				? (decimal?)value
				: null;
		set => Parameters[nameof(StopLossLimitPrice)] = value;
	}

	/// <summary>Price source used to trigger attached stop orders.</summary>
	[DataMember]
	public PacificaTriggerPriceTypes TriggerPriceType
	{
		get => Parameters.TryGetValue(nameof(TriggerPriceType), out var value)
			? (PacificaTriggerPriceTypes)value
			: PacificaTriggerPriceTypes.MarkPrice;
		set => Parameters[nameof(TriggerPriceType)] = value;
	}

	/// <summary>Optional market-order slippage tolerance in percent.</summary>
	[DataMember]
	public decimal? SlippagePercent
	{
		get => Parameters.TryGetValue(nameof(SlippagePercent), out var value)
			? (decimal?)value
			: null;
		set => Parameters[nameof(SlippagePercent)] = value;
	}

	/// <summary>Optional Pacifica builder program code.</summary>
	[DataMember]
	public string BuilderCode
	{
		get => Parameters.TryGetValue(nameof(BuilderCode), out var value)
			? (string)value
			: null;
		set => Parameters[nameof(BuilderCode)] = value;
	}
}
