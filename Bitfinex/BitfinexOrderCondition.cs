namespace StockSharp.Bitfinex;

using System.ComponentModel.DataAnnotations;
using System.Runtime.Serialization;

/// <summary>
/// <see cref="Bitfinex"/> order condition.
/// </summary>
[Serializable]
[DataContract]
[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.BitfinexKey)]
public class BitfinexOrderCondition : BaseWithdrawOrderCondition, IStopLossOrderCondition
{
	/// <summary>
	/// Initializes a new instance of the <see cref="BitfinexOrderCondition"/>.
	/// </summary>
	public BitfinexOrderCondition()
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
	/// Trailing stop-loss.
	/// </summary>
	[DataMember]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.TrailingKey,
		Description = LocalizedStrings.TrailingStopLossKey + LocalizedStrings.Dot,
		GroupName = LocalizedStrings.StopLossKey,
		Order = 1)]
	public bool IsTrailing
	{
		get => (bool?)Parameters.TryGetValue(nameof(IsTrailing)) ?? false;
		set => Parameters[nameof(IsTrailing)] = value;
	}

	/// <summary>
	/// Trailing price.
	/// </summary>
	[DataMember]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		//Name = LocalizedStrings.TrailingPriceKey,
		//Description = LocalizedStrings.TrailingPriceKey,
		GroupName = LocalizedStrings.StopLossKey,
		Order = 2)]
	public decimal? TrailingPrice
	{
		get => (decimal?)Parameters.TryGetValue(nameof(TrailingPrice));
		set => Parameters[nameof(TrailingPrice)] = value;
	}

	decimal? IStopLossOrderCondition.ClosePositionPrice
	{
		get => null;
		set { }
	}
	decimal? IStopLossOrderCondition.ActivationPrice
	{
		get => StopPrice;
		set => StopPrice = value;
	}

	/// <summary>
	/// OCO stop price.
	/// </summary>
	[DataMember]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		//Name = LocalizedStrings.OcoPriceKey,
		//Description = LocalizedStrings.OcoPriceKey,
		GroupName = LocalizedStrings.AdditionalKey,
		Order = 20)]
	public decimal? OcoPrice
	{
		get => (decimal?)Parameters.TryGetValue(nameof(OcoPrice));
		set => Parameters[nameof(OcoPrice)] = value;
	}

	/// <summary>
	/// Close position.
	/// </summary>
	[DataMember]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.CloseKey,
		Description = LocalizedStrings.ClosePositionKey,
		GroupName = LocalizedStrings.AdditionalKey,
		Order = 12)]
	public bool? Close
	{
		get => (bool?)Parameters.TryGetValue(nameof(Close));
		set => Parameters[nameof(Close)] = value;
	}

	/// <summary>
	/// One Cancels Other.
	/// </summary>
	[DataMember]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.OcoKey,
		Description = LocalizedStrings.OcoDescKey,
		GroupName = LocalizedStrings.AdditionalKey,
		Order = 14)]
	public bool? OneCancelOther
	{
		get => (bool?)Parameters.TryGetValue(nameof(OneCancelOther));
		set => Parameters[nameof(OneCancelOther)] = value;
	}
}