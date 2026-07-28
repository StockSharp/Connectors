namespace StockSharp.DeltaExchangeIndia;

/// <summary>
/// Delta Exchange India order condition.
/// </summary>
[Display(
	ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.ConditionKey,
	Description = LocalizedStrings.ConditionKey)]
public sealed class DeltaExchangeIndiaOrderCondition : OrderCondition
{
	private const string _stopPriceKey = "StopPrice";
	private const string _reduceOnlyKey = "ReduceOnly";

	/// <summary>
	/// Stop trigger price.
	/// </summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.StopPriceKey,
		Description = LocalizedStrings.StopPriceKey,
		Order = 0)]
	public decimal? StopPrice
	{
		get => Parameters.TryGetValue(_stopPriceKey, out var value)
			? (decimal?)value
			: null;
		set => Parameters[_stopPriceKey] = value;
	}

	/// <summary>
	/// Whether the order may only reduce a position.
	/// </summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.ReducePositionKey,
		Description = LocalizedStrings.ReducePositionKey,
		Order = 1)]
	public bool IsReduceOnly
	{
		get => Parameters.TryGetValue(_reduceOnlyKey, out var value) &&
			value.To<bool>();
		set => Parameters[_reduceOnlyKey] = value;
	}
}
