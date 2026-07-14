namespace StockSharp.Bithumb;

/// <summary>
/// <see cref="Bithumb"/> order condition.
/// </summary>
[Serializable]
[DataContract]
[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.BithumbKey)]
public class BithumbOrderCondition : BaseWithdrawOrderCondition
{
	/// <summary>
	/// Initializes a new instance of the <see cref="BithumbOrderCondition"/>.
	/// </summary>
	public BithumbOrderCondition()
	{
	}
}