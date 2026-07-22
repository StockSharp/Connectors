namespace StockSharp.NinjaTrader;

using System.ComponentModel.DataAnnotations;

/// <summary>
/// NinjaTrader order condition.
/// </summary>
[Serializable]
[DataContract]
public sealed class NinjaTraderOrderCondition : OrderCondition, IStopLossOrderCondition
{
	private decimal? _stopPrice;

	/// <summary>
	/// Stop price.
	/// </summary>
	[DataMember]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.StopPriceKey,
		Description = LocalizedStrings.StopPriceKey + LocalizedStrings.Dot,
		GroupName = LocalizedStrings.GeneralKey)]
	public decimal? StopPrice
	{
		get => _stopPrice;
		set
		{
			_stopPrice = value;
			Parameters[nameof(StopPrice)] = value;
		}
	}

	decimal? IStopLossOrderCondition.ClosePositionPrice { get; set; }

	decimal? IStopLossOrderCondition.ActivationPrice
	{
		get => StopPrice;
		set => StopPrice = value;
	}

	bool IStopLossOrderCondition.IsTrailing
	{
		get => false;
		set { }
	}
}
