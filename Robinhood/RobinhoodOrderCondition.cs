namespace StockSharp.Robinhood;

using System.ComponentModel.DataAnnotations;

/// <summary>
/// Robinhood stop-order condition.
/// </summary>
[Serializable]
[DataContract]
public sealed class RobinhoodOrderCondition : OrderCondition, IStopLossOrderCondition
{
	private decimal? _stopPrice;

	/// <inheritdoc />
	[DataMember]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.StopPriceKey,
		Description = LocalizedStrings.StopPriceDescKey,
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
