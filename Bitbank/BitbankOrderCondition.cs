namespace StockSharp.Bitbank;

using System.ComponentModel.DataAnnotations;
using System.Runtime.Serialization;

/// <summary>
/// <see cref="Bitbank"/> order condition.
/// </summary>
[Serializable]
[DataContract]
[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.BitbankKey)]
public class BitbankOrderCondition : BaseWithdrawOrderCondition
{
	/// <summary>
	/// Initializes a new instance of the <see cref="BitbankOrderCondition"/>.
	/// </summary>
	public BitbankOrderCondition()
	{
	}
}