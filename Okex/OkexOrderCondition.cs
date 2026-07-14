namespace StockSharp.Okex;

using System.ComponentModel.DataAnnotations;
using System.Runtime.Serialization;

/// <summary>
/// <see cref="Okex"/> order condition.
/// </summary>
[Serializable]
[DataContract]
[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.OkexKey)]
public class OkexOrderCondition : BaseWithdrawOrderCondition
{
	/// <summary>
	/// Initializes a new instance of the <see cref="OkexOrderCondition"/>.
	/// </summary>
	public OkexOrderCondition()
	{
	}

	/// <summary>
	/// Leverage.
	/// </summary>
	[DataMember]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.LeverageKey,
		Description = LocalizedStrings.MarginLeverageKey + LocalizedStrings.Dot,
		GroupName = LocalizedStrings.ParametersKey,
		Order = 1)]
	public decimal? Leverage
	{
		get => (decimal?)Parameters.TryGetValue(nameof(Leverage));
		set => Parameters[nameof(Leverage)] = value;
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
	/// SPOT leading mode.
	/// </summary>
	[DataMember]
	public bool? Leading
	{
		get => (bool?)Parameters.TryGetValue(nameof(Leading));
		set => Parameters[nameof(Leading)] = value;
	}
}