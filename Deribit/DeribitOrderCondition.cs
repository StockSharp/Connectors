namespace StockSharp.Deribit;

using System.ComponentModel.DataAnnotations;
using System.Runtime.Serialization;

/// <summary>
/// Extended orders types which are specific to <see cref="Deribit"/>.
/// </summary>
[Serializable]
[DataContract]
public enum DeribitOrderAdvancedTypes
{
	/// <summary>
	/// Volatility (implied).
	/// </summary>
	[EnumMember]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.ImpliedVolatilityKey)]
	ImpliedVolatility,

	/// <summary>
	/// USD.
	/// </summary>
	[EnumMember]
	Usd,
}

/// <summary>
/// Triggers.
/// </summary>
[Serializable]
[DataContract]
public enum DeribitOrderTriggers
{
	/// <summary>
	/// Index price.
	/// </summary>
	[EnumMember]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.IndexKey)]
	Index,

	/// <summary>
	/// Mark price.
	/// </summary>
	[EnumMember]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.MarkKey)]
	Mark,

	/// <summary>
	/// By last price.
	/// </summary>
	[EnumMember]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.LastKey)]
	Last
}

/// <summary>
/// <see cref="Deribit"/> order condition.
/// </summary>
[Serializable]
[DataContract]
[Display(
	ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.DeribitKey)]
public class DeribitOrderCondition : BaseWithdrawOrderCondition, IStopLossOrderCondition
{
	/// <summary>
	/// Initializes a new instance of the <see cref="DeribitOrderCondition"/>.
	/// </summary>
	public DeribitOrderCondition()
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
	/// Trigger field.
	/// </summary>
	[DataMember]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.TriggerKey,
		Description = LocalizedStrings.TriggerFieldKey,
		GroupName = LocalizedStrings.StopLossKey,
		Order = 1)]
	public DeribitOrderTriggers? Trigger
	{
		get => (DeribitOrderTriggers?)Parameters.TryGetValue(nameof(Trigger));
		set => Parameters[nameof(Trigger)] = value;
	}

	/// <summary>
	/// Extended type of order.
	/// </summary>
	[DataMember]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.ExtendedInfoKey,
		Description = LocalizedStrings.ExtendedInfoKey,
		GroupName = LocalizedStrings.AdditionalKey,
		Order = 10)]
	public DeribitOrderAdvancedTypes? Advanced
	{
		get => (DeribitOrderAdvancedTypes?)Parameters.TryGetValue(nameof(Advanced));
		set => Parameters[nameof(Advanced)] = value;
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
		set {  }
	}
}