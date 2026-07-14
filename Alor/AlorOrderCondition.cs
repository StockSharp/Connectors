namespace StockSharp.Alor;

using System.Runtime.Serialization;

/// <summary>
/// <see cref="Alor"/> order condition.
/// </summary>
[Serializable]
[DataContract]
//[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.AlorKey)]
public class AlorOrderCondition : OrderCondition, IStopLossOrderCondition, ITakeProfitOrderCondition
{
	/// <summary>
	/// Initializes a new instance of the <see cref="AlorOrderCondition"/>.
	/// </summary>
	public AlorOrderCondition()
	{
	}

	/// <summary>
	/// Trigger price.
	/// </summary>
	[DataMember]
	public decimal? TriggerPrice
	{
		get => (decimal?)Parameters.TryGetValue(nameof(TriggerPrice));
		set => Parameters[nameof(TriggerPrice)] = value;
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

	internal bool IsTakeProfit { get; set; }

	decimal? IStopLossOrderCondition.ClosePositionPrice { get => Price; set => Price = value; }
	decimal? ITakeProfitOrderCondition.ClosePositionPrice { get => Price; set => Price = value; }

	decimal? IStopLossOrderCondition.ActivationPrice
	{
		get => TriggerPrice;
		set => TriggerPrice = value;
	}

	decimal? ITakeProfitOrderCondition.ActivationPrice
	{
		get => TriggerPrice;
		set
		{
			TriggerPrice = value;
			IsTakeProfit = true;
		}
	}

	bool IStopLossOrderCondition.IsTrailing { get => false; set { } }
}