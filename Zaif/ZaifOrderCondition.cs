namespace StockSharp.Zaif;

/// <summary>
/// <see cref="Zaif"/> order condition.
/// </summary>
[Serializable]
[DataContract]
[Display(
	ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.ZaifKey)]
public class ZaifOrderCondition : BaseWithdrawOrderCondition
{
	/// <summary>
	/// Initializes a new instance of the <see cref="ZaifOrderCondition"/>.
	/// </summary>
	public ZaifOrderCondition()
	{
	}
}