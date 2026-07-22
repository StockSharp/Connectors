namespace StockSharp.TradingTechnologies;

using System.ComponentModel.DataAnnotations;
using System.Runtime.Serialization;

/// <summary>Trading Technologies stop-order condition.</summary>
[Serializable]
[DataContract]
public sealed class TradingTechnologiesOrderCondition : OrderCondition, IStopLossOrderCondition
{
	private decimal? _stopPrice;

	/// <summary>Stop or trigger price.</summary>
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
