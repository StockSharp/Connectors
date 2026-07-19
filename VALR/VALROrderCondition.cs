namespace StockSharp.VALR;

/// <summary>
/// VALR margin, futures, and conditional-order parameters.
/// </summary>
[Serializable]
[DataContract]
[Display(ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.ValrKey)]
public class VALROrderCondition : OrderCondition, IStopLossOrderCondition
{
	/// <summary>
	/// Price that activates a stop-loss or take-profit limit order.
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
	/// Whether the conditional order is a take-profit order.
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
	/// Allow VALR to borrow funds for a spot margin order.
	/// </summary>
	[DataMember]
	[Display(ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.IsMarginKey,
		Description = LocalizedStrings.IsMarginKey,
		GroupName = LocalizedStrings.ParametersKey, Order = 2)]
	public bool IsMargin
	{
		get => (bool?)Parameters.TryGetValue(nameof(IsMargin)) ?? false;
		set => Parameters[nameof(IsMargin)] = value;
	}

	/// <summary>
	/// Restrict a perpetual order to reducing an existing position.
	/// </summary>
	[DataMember]
	[Display(ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.PosConditionReduceOnlyKey,
		Description = LocalizedStrings.PosConditionReduceOnlyDetailsKey,
		GroupName = LocalizedStrings.ParametersKey, Order = 3)]
	public bool IsReduceOnly
	{
		get => (bool?)Parameters.TryGetValue(nameof(IsReduceOnly)) ?? false;
		set => Parameters[nameof(IsReduceOnly)] = value;
	}

	/// <summary>
	/// Optional quote-currency amount for a market order. When omitted,
	/// <see cref="OrderRegisterMessage.Volume"/> is sent as base amount.
	/// </summary>
	[DataMember]
	[Display(ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.AmountKey,
		Description = LocalizedStrings.AmountKey,
		GroupName = LocalizedStrings.ParametersKey, Order = 4)]
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
					"VALR does not document trailing-stop orders.");
		}
	}

	/// <inheritdoc />
	public override OrderCondition Clone()
	{
		var clone = new VALROrderCondition();
		clone.Parameters.AddRange(Parameters);
		return clone;
	}
}
