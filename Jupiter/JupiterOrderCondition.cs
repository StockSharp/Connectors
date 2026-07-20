namespace StockSharp.Jupiter;

/// <summary>Jupiter perpetual-order action.</summary>
[DataContract]
public enum JupiterOrderActions
{
	/// <summary>Open or increase a position.</summary>
	[Display(ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.OpenKey)]
	[EnumMember]
	Open,

	/// <summary>Close or decrease a position.</summary>
	[Display(ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.ClosePositionKey)]
	[EnumMember]
	Close,

	/// <summary>Create a take-profit request.</summary>
	[Display(ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.TakeProfitKey)]
	[EnumMember]
	TakeProfit,

	/// <summary>Create a stop-loss request.</summary>
	[Display(ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.StopLossKey)]
	[EnumMember]
	StopLoss,
}

/// <summary>Collateral and settlement tokens accepted by Jupiter Perps.</summary>
[DataContract]
[JsonConverter(typeof(StringEnumConverter))]
public enum JupiterCollateralTokens
{
	/// <summary>USD Coin.</summary>
	[EnumMember(Value = "USDC")]
	Usdc,

	/// <summary>Solana.</summary>
	[EnumMember(Value = "SOL")]
	Sol,

	/// <summary>Wrapped Bitcoin.</summary>
	[EnumMember(Value = "BTC")]
	Bitcoin,

	/// <summary>Wrapped Ether.</summary>
	[EnumMember(Value = "ETH")]
	Ethereum,
}

/// <summary>Jupiter-specific order parameters.</summary>
[Serializable]
[DataContract]
[Display(ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.JupiterKey)]
public class JupiterOrderCondition : OrderCondition
{
	/// <summary>Perpetual-order action.</summary>
	[DataMember]
	[Display(ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.ActionKey,
		Description = LocalizedStrings.ActionKey,
		GroupName = LocalizedStrings.ParametersKey, Order = 0)]
	public JupiterOrderActions Action
	{
		get => (JupiterOrderActions?)Parameters.TryGetValue(nameof(Action)) ??
			JupiterOrderActions.Open;
		set => Parameters[nameof(Action)] = value;
	}

	/// <summary>Target leverage used to size collateral.</summary>
	[DataMember]
	[Display(ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.LeverageKey,
		Description = LocalizedStrings.LeverageKey,
		GroupName = LocalizedStrings.ParametersKey, Order = 1)]
	public decimal Leverage
	{
		get => (decimal?)Parameters.TryGetValue(nameof(Leverage)) ?? 2m;
		set => Parameters[nameof(Leverage)] = value;
	}

	/// <summary>Token deposited as collateral.</summary>
	[DataMember]
	[Display(ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.TokenKey,
		Description = LocalizedStrings.TokenKey,
		GroupName = LocalizedStrings.ParametersKey, Order = 2)]
	public JupiterCollateralTokens CollateralToken
	{
		get => (JupiterCollateralTokens?)Parameters.TryGetValue(
			nameof(CollateralToken)) ?? JupiterCollateralTokens.Usdc;
		set => Parameters[nameof(CollateralToken)] = value;
	}

	/// <summary>Token received when a position is reduced.</summary>
	[DataMember]
	[Display(ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.SettlementKey,
		Description = LocalizedStrings.SettlementKey,
		GroupName = LocalizedStrings.ParametersKey, Order = 3)]
	public JupiterCollateralTokens ReceiveToken
	{
		get => (JupiterCollateralTokens?)Parameters.TryGetValue(
			nameof(ReceiveToken)) ?? JupiterCollateralTokens.Usdc;
		set => Parameters[nameof(ReceiveToken)] = value;
	}

	/// <summary>On-chain Jupiter Perps position public key.</summary>
	[DataMember]
	[Display(ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.PositionKey,
		Description = LocalizedStrings.PositionKey,
		GroupName = LocalizedStrings.ParametersKey, Order = 4)]
	public string PositionId
	{
		get => (string)Parameters.TryGetValue(nameof(PositionId));
		set => Parameters[nameof(PositionId)] = value;
	}

	/// <summary>Whether a close or trigger applies to the entire position.</summary>
	[DataMember]
	[Display(ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.PositionKey,
		Description = LocalizedStrings.ClosePositionKey,
		GroupName = LocalizedStrings.ParametersKey, Order = 5)]
	public bool IsEntirePosition
	{
		get => (bool?)Parameters.TryGetValue(nameof(IsEntirePosition)) ?? true;
		set => Parameters[nameof(IsEntirePosition)] = value;
	}

	/// <summary>Optional take-profit trigger attached to a market open.</summary>
	[DataMember]
	[Display(ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.TakeProfitKey,
		Description = LocalizedStrings.StopPriceDescKey,
		GroupName = LocalizedStrings.ParametersKey, Order = 6)]
	public decimal? TakeProfitPrice
	{
		get => (decimal?)Parameters.TryGetValue(nameof(TakeProfitPrice));
		set => Parameters[nameof(TakeProfitPrice)] = value;
	}

	/// <summary>Optional stop-loss trigger attached to a market open.</summary>
	[DataMember]
	[Display(ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.StopLossKey,
		Description = LocalizedStrings.StopPriceDescKey,
		GroupName = LocalizedStrings.ParametersKey, Order = 7)]
	public decimal? StopLossPrice
	{
		get => (decimal?)Parameters.TryGetValue(nameof(StopLossPrice));
		set => Parameters[nameof(StopLossPrice)] = value;
	}
}
