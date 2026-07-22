namespace StockSharp.DXtrade;

using System.ComponentModel.DataAnnotations;
using System.Runtime.Serialization;

/// <summary>
/// <see cref="DXtrade"/> order condition.
/// </summary>
[Serializable]
[DataContract]
[Display(
	ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.DXTradeKey)]
public class DXtradeOrderCondition : OrderCondition, IStopLossOrderCondition
{
	/// <summary>
	/// Initializes a new instance of the <see cref="DXtradeOrderCondition"/>.
	/// </summary>
	public DXtradeOrderCondition()
	{
	}

	/// <summary>
	/// Stop-price.
	/// </summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.StopPriceKey,
		Description = LocalizedStrings.StopPriceValueKey,
		GroupName = LocalizedStrings.ParametersKey,
		Order = 1)]
	public decimal? StopPrice
	{
		get => (decimal?)Parameters.TryGetValue(nameof(StopPrice));
		set => Parameters[nameof(StopPrice)] = value;
	}

	/// <summary>
	/// Offset.
	/// </summary>
	public decimal? Offset
	{
		get => (decimal?)Parameters.TryGetValue(nameof(Offset));
		set => Parameters[nameof(Offset)] = value;
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

	bool IStopLossOrderCondition.IsTrailing
	{
		get => false;
		set { }
	}
}