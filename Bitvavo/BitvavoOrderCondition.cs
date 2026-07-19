namespace StockSharp.Bitvavo;

/// <summary>
/// Bitvavo trigger-order parameters.
/// </summary>
[Serializable]
[DataContract]
[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.BitvavoKey)]
public class BitvavoOrderCondition : OrderCondition, IStopLossOrderCondition,
	ITakeProfitOrderCondition
{
	/// <summary>
	/// Trigger activation price. A null value creates a regular order.
	/// </summary>
	[DataMember]
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.TriggerKey,
		Description = LocalizedStrings.TriggerFieldKey,
		GroupName = LocalizedStrings.ParametersKey, Order = 0)]
	public decimal? TriggerPrice
	{
		get => (decimal?)Parameters.TryGetValue(nameof(TriggerPrice));
		set => Parameters[nameof(TriggerPrice)] = value;
	}

	/// <summary>
	/// Use a take-profit trigger instead of stop-loss.
	/// </summary>
	[DataMember]
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.TakeProfitKey,
		Description = LocalizedStrings.TakeProfitKey,
		GroupName = LocalizedStrings.ParametersKey, Order = 1)]
	public bool IsTakeProfit
	{
		get => (bool?)Parameters.TryGetValue(nameof(IsTakeProfit)) ?? false;
		set => Parameters[nameof(IsTakeProfit)] = value;
	}

	decimal? IStopLossOrderCondition.ActivationPrice
	{
		get => IsTakeProfit ? null : TriggerPrice;
		set
		{
			IsTakeProfit = false;
			TriggerPrice = value;
		}
	}

	decimal? ITakeProfitOrderCondition.ActivationPrice
	{
		get => IsTakeProfit ? TriggerPrice : null;
		set
		{
			IsTakeProfit = true;
			TriggerPrice = value;
		}
	}

	decimal? IStopLossOrderCondition.ClosePositionPrice
	{
		get => null;
		set { }
	}

	decimal? ITakeProfitOrderCondition.ClosePositionPrice
	{
		get => null;
		set { }
	}

	bool IStopLossOrderCondition.IsTrailing
	{
		get => false;
		set { }
	}
}
