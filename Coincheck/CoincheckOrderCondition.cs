namespace StockSharp.Coincheck;

/// <summary>
/// <see cref="Coincheck"/> order condition.
/// </summary>
[Serializable]
[DataContract]
[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.CoincheckKey)]
public class CoincheckOrderCondition : BaseWithdrawOrderCondition
{
	/// <summary>
	/// Initializes a new instance of the <see cref="CoincheckOrderCondition"/>.
	/// </summary>
	public CoincheckOrderCondition()
	{
	}

	///// <summary>
	///// Activation price, when reached an order will be placed.
	///// </summary>
	//[DataMember]
	//[Display(
	//	ResourceType = typeof(LocalizedStrings),
	//	Name = LocalizedStrings.StopPriceKey,
	//	Description = LocalizedStrings.StopPriceDescKey,
	//	GroupName = LocalizedStrings.StopLossKey,
	//	Order = 0)]
	//public decimal? StopPrice
	//{
	//	get => (decimal?)Parameters.TryGetValue(nameof(StopPrice));
	//	set => Parameters[nameof(StopPrice)] = value;
	//}
}