namespace StockSharp.Nado.Native;

[JsonConverter(typeof(StringEnumConverter))]
enum NadoProductTypes
{
	[EnumMember(Value = "spot")]
	Spot,

	[EnumMember(Value = "perp")]
	Perpetual,
}

/// <summary>Nado order execution modes.</summary>
[DataContract]
[Serializable]
public enum NadoOrderExecutionTypes
{
	/// <summary>Resting limit order.</summary>
	[EnumMember]
	Default,

	/// <summary>Immediate-or-cancel order.</summary>
	[EnumMember]
	ImmediateOrCancel,

	/// <summary>Fill-or-kill order.</summary>
	[EnumMember]
	FillOrKill,

	/// <summary>Post-only order.</summary>
	[EnumMember]
	PostOnly,
}

[JsonConverter(typeof(StringEnumConverter))]
enum NadoOrderUpdateReasons
{
	[EnumMember(Value = "placed")]
	Placed,

	[EnumMember(Value = "filled")]
	Filled,

	[EnumMember(Value = "cancelled")]
	Cancelled,
}

[JsonConverter(typeof(StringEnumConverter))]
enum NadoPositionChangeReasons
{
	[EnumMember(Value = "deposit_collateral")]
	DepositCollateral,

	[EnumMember(Value = "match_orders")]
	MatchOrders,

	[EnumMember(Value = "withdraw_collateral")]
	WithdrawCollateral,

	[EnumMember(Value = "withdraw_collateral_v2")]
	WithdrawCollateralV2,

	[EnumMember(Value = "transfer_quote")]
	TransferQuote,

	[EnumMember(Value = "settle_pnl")]
	SettlePnl,

	[EnumMember(Value = "mint_nlp")]
	MintNlp,

	[EnumMember(Value = "burn_nlp")]
	BurnNlp,

	[EnumMember(Value = "liquidate_subaccount")]
	LiquidateSubaccount,
}

enum NadoStreamTypes
{
	Trade,
	BestBidOffer,
	BookDepth,
	LatestCandlestick,
	FundingRate,
	Fill,
	PositionChange,
	OrderUpdate,
}

readonly record struct NadoSubscriptionKey(NadoStreamTypes Type,
	int ProductId, int Granularity, string Subaccount);
