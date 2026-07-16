namespace StockSharp.DukasCopy;

/// <summary>Dukascopy JForex order parameters.</summary>
[Serializable]
[DataContract]
[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.DukasCopyKey)]
public class DukasCopyOrderCondition : OrderCondition, IStopLossOrderCondition, ITakeProfitOrderCondition
{
	/// <summary>Optional native command. Auto maps the StockSharp side and order type.</summary>
	[DataMember]
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.OrderTypeKey,
		Description = LocalizedStrings.OrderConditionDescKey,
		GroupName = LocalizedStrings.ParametersKey, Order = 0)]
	public DukasCopyOrderCommands NativeCommand
	{
		get => Parameters.TryGetValue(nameof(NativeCommand))?.To<DukasCopyOrderCommands?>() ??
			DukasCopyOrderCommands.Auto;
		set => Parameters[nameof(NativeCommand)] = value;
	}

	/// <summary>Maximum slippage in pips. A negative value uses the JForex default.</summary>
	[DataMember]
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.SlippageKey,
		Description = LocalizedStrings.SlippageSizeKey,
		GroupName = LocalizedStrings.ParametersKey, Order = 1)]
	public decimal? Slippage
	{
		get => Parameters.TryGetValue(nameof(Slippage))?.To<decimal?>();
		set => Parameters[nameof(Slippage)] = value;
	}

	/// <summary>Stop-loss price.</summary>
	[DataMember]
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.StopLossKey,
		Description = LocalizedStrings.StopPriceDescKey,
		GroupName = LocalizedStrings.ParametersKey, Order = 2)]
	public decimal? StopLoss
	{
		get => Parameters.TryGetValue(nameof(StopLoss))?.To<decimal?>();
		set => Parameters[nameof(StopLoss)] = value;
	}

	/// <summary>Take-profit price.</summary>
	[DataMember]
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.TakeProfitKey,
		Description = LocalizedStrings.TakeProfitDescKey,
		GroupName = LocalizedStrings.ParametersKey, Order = 3)]
	public decimal? TakeProfit
	{
		get => Parameters.TryGetValue(nameof(TakeProfit))?.To<decimal?>();
		set => Parameters[nameof(TakeProfit)] = value;
	}

	/// <summary>Order comment stored by JForex.</summary>
	[DataMember]
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.CommentKey,
		Description = LocalizedStrings.OrderConditionDescKey,
		GroupName = LocalizedStrings.ParametersKey, Order = 4)]
	public string Comment
	{
		get => Parameters.TryGetValue(nameof(Comment))?.To<string>();
		set => Parameters[nameof(Comment)] = value;
	}

	decimal? IStopLossOrderCondition.ClosePositionPrice
	{
		get => null;
		set { }
	}

	decimal? IStopLossOrderCondition.ActivationPrice
	{
		get => StopLoss;
		set => StopLoss = value;
	}

	bool IStopLossOrderCondition.IsTrailing
	{
		get => false;
		set
		{
			if (value)
				throw new NotSupportedException("JForex trailing stops are not exposed by this condition.");
		}
	}

	decimal? ITakeProfitOrderCondition.ClosePositionPrice
	{
		get => null;
		set { }
	}

	decimal? ITakeProfitOrderCondition.ActivationPrice
	{
		get => TakeProfit;
		set => TakeProfit = value;
	}
}
