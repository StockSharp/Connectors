namespace StockSharp.Kucoin;

using System.ComponentModel.DataAnnotations;
using System.Runtime.Serialization;

/// <summary>
/// <see cref="Kucoin"/> order condition.
/// </summary>
[Serializable]
[DataContract]
[Display(
	ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.KucoinKey)]
public class KucoinOrderCondition : BaseWithdrawOrderCondition, IStopLossOrderCondition
{
	/// <summary>
	/// Initializes a new instance of the <see cref="KucoinOrderCondition"/>.
	/// </summary>
	public KucoinOrderCondition()
	{
	}

	/// <summary>
	/// Activation price, when reached an order will be placed.
	/// </summary>
	[DataMember]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.StopPriceKey,
		Description = LocalizedStrings.StopPriceDescKey,
		GroupName = LocalizedStrings.StopLossKey,
		Order = 1)]
	public decimal? StopPrice
	{
		get => (decimal?)Parameters.TryGetValue(nameof(StopPrice));
		set => Parameters[nameof(StopPrice)] = value;
	}

	decimal? IStopLossOrderCondition.ClosePositionPrice
	{
		get => null;
		set { }
	}

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