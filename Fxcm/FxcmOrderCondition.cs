namespace StockSharp.Fxcm;

/// <summary>FXCM order parameters.</summary>
[Serializable]
[DataContract]
[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.FxcmKey)]
public class FxcmOrderCondition : OrderCondition, IStopLossOrderCondition, ITakeProfitOrderCondition
{
	/// <summary>Stop-loss price or distance in pips.</summary>
	[DataMember]
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.StopLossKey,
		Description = LocalizedStrings.FxcmStopLossDescKey,
		GroupName = LocalizedStrings.ParametersKey, Order = 0)]
	public decimal? StopLoss
	{
		get => (decimal?)Parameters.TryGetValue(nameof(StopLoss));
		set => Parameters[nameof(StopLoss)] = value;
	}

	/// <summary>Take-profit price or distance in pips.</summary>
	[DataMember]
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.TakeProfitKey,
		Description = LocalizedStrings.FxcmTakeProfitDescKey,
		GroupName = LocalizedStrings.ParametersKey, Order = 1)]
	public decimal? TakeProfit
	{
		get => (decimal?)Parameters.TryGetValue(nameof(TakeProfit));
		set => Parameters[nameof(TakeProfit)] = value;
	}

	/// <summary>Trailing-stop step.</summary>
	[DataMember]
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.TrailingDeltaKey,
		Description = LocalizedStrings.FxcmTrailingStepDescKey,
		GroupName = LocalizedStrings.ParametersKey, Order = 2)]
	public decimal? TrailingStep
	{
		get => (decimal?)Parameters.TryGetValue(nameof(TrailingStep));
		set => Parameters[nameof(TrailingStep)] = value;
	}

	/// <summary>Whether stop and limit values are expressed as pip distances.</summary>
	[DataMember]
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.FxcmInPipsKey,
		Description = LocalizedStrings.FxcmInPipsDescKey,
		GroupName = LocalizedStrings.ParametersKey, Order = 3)]
	public bool IsInPips
	{
		get => (bool?)Parameters.TryGetValue(nameof(IsInPips)) ?? false;
		set => Parameters[nameof(IsInPips)] = value;
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
		get => TrailingStep != null;
		set
		{
			if (!value)
				TrailingStep = null;
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
