namespace StockSharp.GateIO;

using System.ComponentModel.DataAnnotations;
using System.Runtime.Serialization;

/// <summary>
/// <see cref="GateIO"/> order condition.
/// </summary>
[Serializable]
[DataContract]
[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.GateIOKey)]
public class GateIOOrderCondition : BaseWithdrawOrderCondition
{
	/// <summary>
	/// Initializes a new instance of the <see cref="GateIOOrderCondition"/>.
	/// </summary>
	public GateIOOrderCondition()
	{
	}
}