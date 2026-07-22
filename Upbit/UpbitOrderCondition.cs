namespace StockSharp.Upbit;

/// <summary>
/// <see cref="Upbit"/> order condition.
/// </summary>
[Serializable]
[DataContract]
[Display(
	ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.UpbitKey)]
public class UpbitOrderCondition : BaseWithdrawOrderCondition
{
	/// <summary>
	/// Initializes a new instance of the <see cref="UpbitOrderCondition"/>.
	/// </summary>
	public UpbitOrderCondition()
	{
	}
}