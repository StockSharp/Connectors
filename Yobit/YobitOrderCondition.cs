namespace StockSharp.Yobit;

/// <summary>
/// <see cref="Yobit"/> order condition.
/// </summary>
[Serializable]
[DataContract]
[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.YobitKey)]
public class YobitOrderCondition : BaseWithdrawOrderCondition
{
	/// <summary>
	/// Initializes a new instance of the <see cref="YobitOrderCondition"/>.
	/// </summary>
	public YobitOrderCondition()
	{
	}
}