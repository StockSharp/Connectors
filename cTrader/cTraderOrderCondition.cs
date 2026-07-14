namespace StockSharp.cTrader;

using System.Runtime.Serialization;

/// <summary>
/// <see cref="cTrader"/> order condition.
/// </summary>
[Serializable]
[DataContract]
//[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.cTraderKey)]
public class cTraderOrderCondition : OrderCondition, IStopLossOrderCondition, ITakeProfitOrderCondition
{
	/// <summary>
	/// Initializes a new instance of the <see cref="cTraderOrderCondition"/>.
	/// </summary>
	public cTraderOrderCondition()
	{
	}

	/// <summary>
	/// Close position price.
	/// </summary>
	[DataMember]
	public decimal? Price
	{
		get => (decimal?)Parameters.TryGetValue(nameof(Price));
		set => Parameters[nameof(Price)] = value;
	}

	/// <summary>
	/// Stop loss price.
	/// </summary>
	public decimal? StopLoss
	{
		get => (decimal?)Parameters.TryGetValue(nameof(StopLoss));
		set => Parameters[nameof(StopLoss)] = value;
	}

	/// <summary>
	/// Take profit price.
	/// </summary>
	public decimal? TakeProfit
	{
		get => (decimal?)Parameters.TryGetValue(nameof(TakeProfit));
		set => Parameters[nameof(TakeProfit)] = value;
	}

	decimal? IStopLossOrderCondition.ClosePositionPrice { get => Price; set => Price = value; }
	decimal? ITakeProfitOrderCondition.ClosePositionPrice { get => Price; set => Price = value; }

	decimal? IStopLossOrderCondition.ActivationPrice
	{
		get => StopLoss;
		set => StopLoss = value;
	}

	decimal? ITakeProfitOrderCondition.ActivationPrice
	{
		get => TakeProfit;
		set => TakeProfit = value;
	}

	bool IStopLossOrderCondition.IsTrailing { get => false; set { } }
}