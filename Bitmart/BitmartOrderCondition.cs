namespace StockSharp.Bitmart;

using System.ComponentModel.DataAnnotations;
using System.Runtime.Serialization;

/// <summary>
/// <see cref="Bitmart"/> order condition.
/// </summary>
[Serializable]
[DataContract]
[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.BitmartKey)]
public class BitmartOrderCondition : BaseWithdrawOrderCondition
{
	/// <summary>
	/// Initializes a new instance of the <see cref="BitmartOrderCondition"/>.
	/// </summary>
	public BitmartOrderCondition()
	{
	}

	/// <summary>
	/// Whether order is placed at best counter party price. (ordType=optimal_limit_ioc)
	/// </summary>
	[DataMember]
	public bool? MatchPrice
	{
		get => (bool?)Parameters.TryGetValue(nameof(MatchPrice));
		set => Parameters[nameof(MatchPrice)] = value;
	}

	/// <summary>
	/// Indicates whether the resulting position after a trade should be a closing position.
	/// </summary>
	[DataMember]
	public bool? ClosePosition
	{
		get => (bool?)Parameters.TryGetValue(nameof(ClosePosition));
		set => Parameters[nameof(ClosePosition)] = value;
	}
}