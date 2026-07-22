namespace StockSharp.CoinsPh;

/// <summary>
/// Coins.ph conditional order types.
/// </summary>
[DataContract]
public enum CoinsPhConditionalOrderTypes
{
	/// <summary>
	/// Stop-loss.
	/// </summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.StopLossKey)]
	[EnumMember]
	StopLoss,

	/// <summary>
	/// Take-profit.
	/// </summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.TakeProfitKey)]
	[EnumMember]
	TakeProfit,
}

/// <summary>
/// Coins.ph order condition.
/// </summary>
[Serializable]
[DataContract]
[Display(
	ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.CoinsPhKey)]
public sealed class CoinsPhOrderCondition : BaseWithdrawOrderCondition
{
	/// <summary>
	/// Conditional order type.
	/// </summary>
	[DataMember]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.StopTypeKey,
		Description = LocalizedStrings.StopTypeDescKey,
		GroupName = LocalizedStrings.ParametersKey,
		Order = 0)]
	public CoinsPhConditionalOrderTypes Type
	{
		get => (CoinsPhConditionalOrderTypes?)Parameters.TryGetValue(nameof(Type)) ??
			CoinsPhConditionalOrderTypes.StopLoss;
		set => Parameters[nameof(Type)] = value;
	}

	/// <summary>
	/// Trigger price.
	/// </summary>
	[DataMember]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.StopPriceKey,
		Description = LocalizedStrings.StopPriceDescKey,
		GroupName = LocalizedStrings.ParametersKey,
		Order = 1)]
	public decimal? StopPrice
	{
		get => (decimal?)Parameters.TryGetValue(nameof(StopPrice));
		set => Parameters[nameof(StopPrice)] = value;
	}

	/// <summary>
	/// Quote-currency amount used instead of base quantity for market orders.
	/// </summary>
	[DataMember]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.AmountKey,
		Description = LocalizedStrings.AmountKey,
		GroupName = LocalizedStrings.ParametersKey,
		Order = 2)]
	public decimal? QuoteAmount
	{
		get => (decimal?)Parameters.TryGetValue(nameof(QuoteAmount));
		set => Parameters[nameof(QuoteAmount)] = value;
	}

	/// <inheritdoc />
	public override OrderCondition Clone()
	{
		var clone = new CoinsPhOrderCondition();
		clone.Parameters.AddRange(Parameters);
		return clone;
	}
}
