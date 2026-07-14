namespace StockSharp.LATOKEN;

/// <summary>
/// <see cref="LATOKEN"/> order condition.
/// </summary>
[Serializable]
[DataContract]
[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.LatokenKey)]
public class LatokenOrderCondition : BaseWithdrawOrderCondition
{
	/// <summary>
	/// Initializes a new instance of the <see cref="LatokenOrderCondition"/>.
	/// </summary>
	public LatokenOrderCondition()
	{
	}
}