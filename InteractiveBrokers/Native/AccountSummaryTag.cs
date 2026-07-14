namespace StockSharp.InteractiveBrokers.Native;

[Obfuscation(Exclude = true)]
enum AccountSummaryTag
{
	/// <summary>
	/// Identifies the IB account structure.
	/// </summary>
	[NativeValue(nameof(AccountType))]
	AccountType,

	/// <summary>
	/// The basis for determining the price of the assets in your account. Total cash value + stock value + options value + bond value.
	/// </summary>
	[NativeValue(nameof(NetLiquidation))]
	NetLiquidation,

	/// <summary>
	/// Total cash balance recognized at the time of trade + futures PNL.
	/// </summary>
	[NativeValue(nameof(TotalCashValue))]
	TotalCashValue,

	/// <summary>
	/// Cash recognized at the time of settlement - purchases at the time of trade - commissions - taxes - fees.
	/// </summary>
	[NativeValue(nameof(SettledCash))]
	SettledCash,

	/// <summary>
	/// Total accrued cash value of stock, commodities and securities.
	/// </summary>
	[NativeValue(nameof(AccruedCash))]
	AccruedCash,

	/// <summary>
	/// Buying power serves as a measurement of the dollar value of securities that one may purchase in a securities account without depositing additional funds.
	/// </summary>
	[NativeValue(nameof(BuyingPower))]
	BuyingPower,

	/// <summary>
	/// Forms the basis for determining whether a client has the necessary assets to either initiate or maintain security positions. Cash + stocks + bonds + mutual funds.
	/// </summary>
	[NativeValue(nameof(EquityWithLoanValue))]
	EquityWithLoanValue,

	/// <summary>
	/// Marginable Equity with Loan value as of 16:00 ET the previous day.
	/// </summary>
	[NativeValue(nameof(PreviousEquityWithLoanValue))]
	PreviousEquityWithLoanValue,

	/// <summary>
	/// The sum of the absolute value of all stock and equity option positions.
	/// </summary>
	[NativeValue(nameof(GrossPositionValue))]
	GrossPositionValue,

	/// <summary>
	/// Regulation T equity for universal account.
	/// </summary>
	[NativeValue(nameof(ReqTEquity))]
	ReqTEquity,

	/// <summary>
	/// Regulation T margin for universal account.
	/// </summary>
	[NativeValue(nameof(ReqTMargin))]
	ReqTMargin,

	/// <summary>
	/// Special Memorandum Account: Line of credit created when the market value of securities in a Regulation T account increase in value.
	/// </summary>
	[NativeValue(nameof(SMA))]
	SMA,

	/// <summary>
	/// Initial Margin requirement of whole portfolio.
	/// </summary>
	[NativeValue(nameof(InitMarginReq))]
	InitMarginReq,

	/// <summary>
	/// Maintenance Margin requirement of whole portfolio.
	/// </summary>
	[NativeValue(nameof(MaintMarginReq))]
	MaintMarginReq,

	/// <summary>
	/// This value tells what you have available for trading.
	/// </summary>
	[NativeValue(nameof(AvailableFunds))]
	AvailableFunds,

	/// <summary>
	/// This value shows your margin cushion, before liquidation.
	/// </summary>
	[NativeValue(nameof(ExcessLiquidity))]
	ExcessLiquidity,

	/// <summary>
	/// Excess liquidity as a percentage of net liquidation value.
	/// </summary>
	[NativeValue(nameof(Cushion))]
	Cushion,

	/// <summary>
	/// Initial Margin of whole portfolio with no discounts or intraday credits.
	/// </summary>
	[NativeValue(nameof(FullInitMarginReq))]
	FullInitMarginReq,

	/// <summary>
	/// Maintenance Margin of whole portfolio with no discounts or intraday credits.
	/// </summary>
	[NativeValue(nameof(FullMaintMarginReq))]
	FullMaintMarginReq,

	/// <summary>
	/// Available funds of whole portfolio with no discounts or intraday credits.
	/// </summary>
	[NativeValue(nameof(FullAvailableFunds))]
	FullAvailableFunds,

	/// <summary>
	/// Excess liquidity of whole portfolio with no discounts or intraday credits.
	/// </summary>
	[NativeValue(nameof(FullExcessLiquidity))]
	FullExcessLiquidity,

	/// <summary>
	/// Time when look-ahead values take effect.
	/// </summary>
	[NativeValue(nameof(LookAheadNextChange))]
	LookAheadNextChange,

	/// <summary>
	/// Initial Margin requirement of whole portfolio as of next period's margin change.
	/// </summary>
	[NativeValue(nameof(LookAheadInitMarginReq))]
	LookAheadInitMarginReq,

	/// <summary>
	/// Maintenance Margin requirement of whole portfolio as of next period's margin change.
	/// </summary>
	[NativeValue(nameof(LookAheadMaintMarginReq))]
	LookAheadMaintMarginReq,

	/// <summary>
	/// This value reflects your available funds at the next margin change.
	/// </summary>
	[NativeValue(nameof(LookAheadAvailableFunds))]
	LookAheadAvailableFunds,

	/// <summary>
	/// This value reflects your excess liquidity at the next margin change.
	/// </summary>
	[NativeValue(nameof(LookAheadExcessLiquidity))]
	LookAheadExcessLiquidity,

	/// <summary>
	/// A measure of how close the account is to liquidation.
	/// </summary>
	[NativeValue(nameof(HighestSeverity))]
	HighestSeverity,

	/// <summary>
	/// The Number of Open/Close trades a user could put on before Pattern Day Trading is detected. A value of "-1" means that the user can put on unlimited day trades.
	/// </summary>
	[NativeValue(nameof(DayTradesRemaining))]
	DayTradesRemaining,

	/// <summary>
	/// GrossPositionValue / NetLiquidation.
	/// </summary>
	[NativeValue(nameof(Leverage))]
	Leverage,
}