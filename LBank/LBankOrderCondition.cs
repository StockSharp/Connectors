namespace StockSharp.LBank;

/// <summary>
/// <see cref="LBank"/> order condition.
/// </summary>
[Serializable]
[DataContract]
[Display(
	ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.LBankKey)]
public class LBankOrderCondition : BaseWithdrawOrderCondition
{
	/// <summary>
	/// Initializes a new instance of the <see cref="LBankOrderCondition"/>.
	/// </summary>
	public LBankOrderCondition()
	{
	}
}