namespace StockSharp.Exmo;

/// <summary>
/// <see cref="Exmo"/> order condition.
/// </summary>
[Serializable]
[DataContract]
[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.ExmoKey)]
public class ExmoOrderCondition : BaseWithdrawOrderCondition
{
	/// <summary>
	/// Initializes a new instance of the <see cref="ExmoOrderCondition"/>.
	/// </summary>
	public ExmoOrderCondition()
	{
	}
}