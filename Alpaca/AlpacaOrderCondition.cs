namespace StockSharp.Alpaca;

using System.ComponentModel.DataAnnotations;
using System.Runtime.Serialization;

/// <summary>
/// Order classes.
/// </summary>
[DataContract]
[Serializable]
public enum AlpacaOrderClasses
{
	/// <summary>
	/// Simple.
	/// </summary>
	[EnumMember]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.SimpleKey)]
	Simple,

	/// <summary>
	/// Bracket.
	/// </summary>
	[EnumMember]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.BracketKey)]
	Bracket,

	/// <summary>
	/// One cancels other.
	/// </summary>
	[EnumMember]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.OcoKey,
		Description = LocalizedStrings.OcoDescKey)]
	OneCancelsOther,

	/// <summary>
	/// One triggers other.
	/// </summary>
	[EnumMember]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.OtoKey,
		Description = LocalizedStrings.OtoDescKey)]
	OneTriggersOther,
}

/// <summary>
/// <see cref="Alpaca"/> order condition.
/// </summary>
[Serializable]
[DataContract]
[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.AlpacaKey)]
public class AlpacaOrderCondition : OrderCondition, IStopLossOrderCondition, ITakeProfitOrderCondition
{
	/// <summary>
	/// Initializes a new instance of the <see cref="AlpacaOrderCondition"/>.
	/// </summary>
	public AlpacaOrderCondition()
	{
	}

	/// <summary>
	/// Close position price.
	/// </summary>
	[DataMember]
	public decimal? Price
	{
		get => (decimal?)Parameters.TryGetValue(nameof(Price));
		set => Parameters[nameof(Price)] = value;
	}

	/// <summary>
	/// Trail price.
	/// </summary>
	[DataMember]
	public decimal? Trail
	{
		get => (decimal?)Parameters.TryGetValue(nameof(Trail));
		set => Parameters[nameof(Trail)] = value;
	}

	/// <summary>
	/// Trail price.
	/// </summary>
	[DataMember]
	public decimal? TrailPercent
	{
		get => (decimal?)Parameters.TryGetValue(nameof(TrailPercent));
		set => Parameters[nameof(TrailPercent)] = value;
	}

	/// <summary>
	/// Order class.
	/// </summary>
	[DataMember]
	public AlpacaOrderClasses? OrderClass
	{
		get => (AlpacaOrderClasses?)Parameters.TryGetValue(nameof(OrderClass));
		set => Parameters[nameof(OrderClass)] = value;
	}

	/// <summary>
	/// Indicates whether or not conditions will also be valid outside Regular Trading Hours.
	/// </summary>
	[DataMember]
	public bool? IgnoreRth
	{
		get => (bool?)Parameters.TryGetValue(nameof(IgnoreRth));
		set => Parameters[nameof(IgnoreRth)] = value;
	}

	decimal? IStopLossOrderCondition.ClosePositionPrice { get => Price; set => Price = value; }
	decimal? ITakeProfitOrderCondition.ClosePositionPrice { get => Price; set => Price = value; }

	decimal? IStopLossOrderCondition.ActivationPrice { get; set; }
	decimal? ITakeProfitOrderCondition.ActivationPrice { get; set; }

	bool IStopLossOrderCondition.IsTrailing { get => false; set { } }
}