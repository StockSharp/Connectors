namespace StockSharp.Poloniex;

/// <summary>
/// <see cref="Poloniex"/> order condition.
/// </summary>
[Serializable]
[DataContract]
[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.PoloniexKey)]
public class PoloniexOrderCondition : BaseWithdrawOrderCondition
{
	/// <summary>
	/// Initializes a new instance of the <see cref="PoloniexOrderCondition"/>.
	/// </summary>
	public PoloniexOrderCondition()
	{
	}
}