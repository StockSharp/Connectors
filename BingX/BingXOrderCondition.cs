namespace StockSharp.BingX;

/// <summary>
/// <see cref="BingX"/> order condition.
/// </summary>
[Serializable]
[DataContract]
[Display(
	ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.BingXKey)]
public class BingXOrderCondition : BaseWithdrawOrderCondition
{
	/// <summary>
	/// Initializes a new instance of the <see cref="BingXOrderCondition"/>.
	/// </summary>
	public BingXOrderCondition()
	{
	}
}