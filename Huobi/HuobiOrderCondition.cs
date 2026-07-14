namespace StockSharp.Huobi;

using System.ComponentModel.DataAnnotations;
using System.Runtime.Serialization;

/// <summary>
/// <see cref="Huobi"/> order condition.
/// </summary>
[Serializable]
[DataContract]
[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.HuobiKey)]
public class HuobiOrderCondition : BaseWithdrawOrderCondition, IStopLossOrderCondition
{
	/// <summary>
	/// Initializes a new instance of the <see cref="HuobiOrderCondition"/>.
	/// </summary>
	public HuobiOrderCondition()
	{
	}

	/// <summary>
	/// Activation price, when reached an order will be placed.
	/// </summary>
	[DataMember]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.StopPriceKey,
		Description = LocalizedStrings.StopPriceDescKey,
		GroupName = LocalizedStrings.StopLossKey,
		Order = 0)]
	public decimal? StopPrice
	{
		get => (decimal?)Parameters.TryGetValue(nameof(StopPrice));
		set => Parameters[nameof(StopPrice)] = value;
	}

	/// <summary>
	/// Operator for <see cref="StopPrice"/>.
	/// </summary>
	public bool? IsGreaterThan
	{
		get => (bool?)Parameters.TryGetValue(nameof(IsGreaterThan));
		set => Parameters[nameof(IsGreaterThan)] = value;
	}

	/// <summary>
	/// Opponent.
	/// </summary>
	public bool? Opponent
	{
		get => (bool?)Parameters.TryGetValue(nameof(Opponent));
		set => Parameters[nameof(Opponent)] = value;
	}

	/// <summary>
	/// Opponent.
	/// </summary>
	public int? Optimal
	{
		get => (int?)Parameters.TryGetValue(nameof(Optimal));
		set => Parameters[nameof(Optimal)] = value;
	}

	/// <summary>
	/// Offset.
	/// </summary>
	public bool? Offset
	{
		get => (bool?)Parameters.TryGetValue(nameof(Offset));
		set => Parameters[nameof(Offset)] = value;
	}

	decimal? IStopLossOrderCondition.ClosePositionPrice { get; set; }

	decimal? IStopLossOrderCondition.ActivationPrice
	{
		get => StopPrice;
		set => StopPrice = value;
	}

	bool IStopLossOrderCondition.IsTrailing
	{
		get => false;
		set { }
	}
}