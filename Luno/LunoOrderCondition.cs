namespace StockSharp.Luno;

/// <summary>
/// Luno stop-limit and market-buy parameters.
/// </summary>
[Serializable]
[DataContract]
[Display(ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.LunoKey)]
public class LunoOrderCondition : OrderCondition, IStopLossOrderCondition
{
	/// <summary>
	/// Price that activates a stop-limit order.
	/// </summary>
	[DataMember]
	[Display(ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.TriggerKey,
		Description = LocalizedStrings.TriggerFieldKey,
		GroupName = LocalizedStrings.ParametersKey, Order = 0)]
	public decimal? TriggerPrice
	{
		get => (decimal?)Parameters.TryGetValue(nameof(TriggerPrice));
		set => Parameters[nameof(TriggerPrice)] = value;
	}

	/// <summary>
	/// Whether the stop-limit order is intended to take profit.
	/// </summary>
	[DataMember]
	[Display(ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.TakeProfitKey,
		Description = LocalizedStrings.TakeProfitKey,
		GroupName = LocalizedStrings.ParametersKey, Order = 1)]
	public bool IsTakeProfit
	{
		get => (bool?)Parameters.TryGetValue(nameof(IsTakeProfit)) ?? false;
		set => Parameters[nameof(IsTakeProfit)] = value;
	}

	/// <summary>
	/// Counter-currency amount used by a market buy order.
	/// </summary>
	[DataMember]
	[Display(ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.AmountKey,
		Description = LocalizedStrings.AmountKey,
		GroupName = LocalizedStrings.ParametersKey, Order = 2)]
	public decimal? QuoteAmount
	{
		get => (decimal?)Parameters.TryGetValue(nameof(QuoteAmount));
		set => Parameters[nameof(QuoteAmount)] = value;
	}

	decimal? IStopLossOrderCondition.ActivationPrice
	{
		get => TriggerPrice;
		set => TriggerPrice = value;
	}

	decimal? IStopLossOrderCondition.ClosePositionPrice
	{
		get => null;
		set { }
	}

	bool IStopLossOrderCondition.IsTrailing
	{
		get => false;
		set
		{
			if (value)
				throw new NotSupportedException(
					"Luno does not document trailing-stop orders.");
		}
	}

	/// <inheritdoc />
	public override OrderCondition Clone()
	{
		var clone = new LunoOrderCondition();
		clone.Parameters.AddRange(Parameters);
		return clone;
	}
}
