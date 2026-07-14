namespace StockSharp.Bitget;

/// <summary>
/// <see cref="Bitget"/> order condition.
/// </summary>
[Serializable]
[DataContract]
[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.BitgetKey)]
public class BitgetOrderCondition : BaseWithdrawOrderCondition
{
	/// <summary>
	/// Initializes a new instance of the <see cref="BitgetOrderCondition"/>.
	/// </summary>
	public BitgetOrderCondition()
	{
	}
}