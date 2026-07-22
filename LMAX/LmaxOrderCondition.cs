namespace StockSharp.LMAX;

using System.ComponentModel.DataAnnotations;
using System.Runtime.Serialization;

/// <summary>
/// <see cref="LMAX"/> order condition.
/// </summary>
[Serializable]
[DataContract]
[Display(
	ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.LmaxKey)]
public class LmaxOrderCondition : OrderCondition, IStopLossOrderCondition, ITakeProfitOrderCondition
{
	/// <summary>
	/// Initializes a new instance of the <see cref="LmaxOrderCondition"/>.
	/// </summary>
	public LmaxOrderCondition()
	{
	}

	/// <summary>
	/// Stop price for stop/stop-limit orders.
	/// </summary>
	[DataMember]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.StopPriceKey,
		Description = LocalizedStrings.StopPriceKey,
		GroupName = LocalizedStrings.StopLossKey,
		Order = 0)]
	public decimal? StopPrice
	{
		get => (decimal?)Parameters.TryGetValue(nameof(StopPrice));
		set => Parameters[nameof(StopPrice)] = value;
	}

	/// <summary>
	/// Stop-loss offset.
	/// </summary>
	[DataMember]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.PriceOffsetKey,
		Description = LocalizedStrings.PriceOffsetKey,
		GroupName = LocalizedStrings.StopLossKey,
		Order = 1)]
	public decimal? StopLossOffset
	{
		get => (decimal?)Parameters.TryGetValue(nameof(StopLossOffset));
		set => Parameters[nameof(StopLossOffset)] = value;
	}

	/// <summary>
	/// Take-profit offset.
	/// </summary>
	[DataMember]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.PriceOffsetKey,
		Description = LocalizedStrings.PriceOffsetKey,
		GroupName = LocalizedStrings.TakeProfitKey,
		Order = 2)]
	public decimal? TakeProfitOffset
	{
		get => (decimal?)Parameters.TryGetValue(nameof(TakeProfitOffset));
		set => Parameters[nameof(TakeProfitOffset)] = value;
	}

	decimal? IStopLossOrderCondition.ClosePositionPrice { get; set; }

	decimal? IStopLossOrderCondition.ActivationPrice
	{
		get => StopLossOffset;
		set => StopLossOffset = value;
	}

	bool IStopLossOrderCondition.IsTrailing
	{
		get => false;
		set {  }
	}

	decimal? ITakeProfitOrderCondition.ClosePositionPrice { get; set; }

	decimal? ITakeProfitOrderCondition.ActivationPrice
	{
		get => TakeProfitOffset;
		set => TakeProfitOffset = value;
	}
}