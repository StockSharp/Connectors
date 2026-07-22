namespace StockSharp.TradeZero;

using System.ComponentModel.DataAnnotations;

/// <summary>
/// TradeZero order condition.
/// </summary>
[Serializable]
[DataContract]
public sealed class TradeZeroOrderCondition : OrderCondition, IStopLossOrderCondition
{
	private decimal? _stopPrice;
	private string _route;

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

	/// <summary>
	/// Account trading route returned by the TradeZero routes endpoint.
	/// </summary>
	[DataMember]
	public string Route
	{
		get => _route;
		set
		{
			_route = value;
			Parameters[nameof(Route)] = value;
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
