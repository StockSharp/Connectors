namespace StockSharp.Mexc;

/// <summary>
/// <see cref="Mexc"/> order condition.
/// </summary>
[Serializable]
[DataContract]
[Display(
	ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.MexcKey)]
public class MexcOrderCondition : BaseWithdrawOrderCondition
{
	/// <summary>
	/// Initializes a new instance of the <see cref="MexcOrderCondition"/>.
	/// </summary>
	public MexcOrderCondition()
	{
	}
}