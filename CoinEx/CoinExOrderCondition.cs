namespace StockSharp.CoinEx;

using System.ComponentModel.DataAnnotations;
using System.Runtime.Serialization;

/// <summary>
/// <see cref="CoinEx"/> order condition.
/// </summary>
[Serializable]
[DataContract]
[Display(
	ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.CoinExKey)]
public class CoinExOrderCondition : BaseWithdrawOrderCondition
{
	/// <summary>
	/// Initializes a new instance of the <see cref="CoinExOrderCondition"/>.
	/// </summary>
	public CoinExOrderCondition()
	{
	}
}