namespace StockSharp.Ctp;

/// <summary>CTP topic recovery mode.</summary>
public enum CtpResumeTypes
{
	/// <summary>Replay all data available for the current trading day.</summary>
	Restart = 0,

	/// <summary>Resume after the last sequence recorded in the flow directory.</summary>
	Resume = 1,

	/// <summary>Receive data generated after this login.</summary>
	Quick = 2,
}

/// <summary>CTP order price instruction.</summary>
public enum CtpOrderPriceTypes
{
	/// <summary>Any price (market).</summary>
	AnyPrice = '1',

	/// <summary>Limit price.</summary>
	LimitPrice = '2',

	/// <summary>Best available price.</summary>
	BestPrice = '3',

	/// <summary>Five-level price.</summary>
	FiveLevelPrice = 'G',
}

/// <summary>CTP position offset instruction.</summary>
public enum CtpOffsetFlags
{
	/// <summary>Open a position.</summary>
	Open = '0',

	/// <summary>Close a position.</summary>
	Close = '1',

	/// <summary>Force-close a position.</summary>
	ForceClose = '2',

	/// <summary>Close today's position.</summary>
	CloseToday = '3',

	/// <summary>Close a previous-day position.</summary>
	CloseYesterday = '4',

	/// <summary>Force liquidation.</summary>
	ForceOff = '5',

	/// <summary>Local force-close.</summary>
	LocalForceClose = '6',
}

/// <summary>CTP hedge instruction.</summary>
public enum CtpHedgeFlags
{
	/// <summary>Speculation.</summary>
	Speculation = '1',

	/// <summary>Arbitrage.</summary>
	Arbitrage = '2',

	/// <summary>Hedge.</summary>
	Hedge = '3',

	/// <summary>Market maker.</summary>
	MarketMaker = '5',
}

/// <summary>CTP time condition.</summary>
public enum CtpTimeConditions
{
	/// <summary>Immediate or cancel.</summary>
	ImmediateOrCancel = '1',

	/// <summary>Good for session.</summary>
	GoodForSession = '2',

	/// <summary>Good for day.</summary>
	GoodForDay = '3',

	/// <summary>Good till date.</summary>
	GoodTillDate = '4',

	/// <summary>Good till canceled.</summary>
	GoodTillCanceled = '5',

	/// <summary>Good for auction.</summary>
	GoodForAuction = '6',
}

/// <summary>CTP volume condition.</summary>
public enum CtpVolumeConditions
{
	/// <summary>Any volume.</summary>
	Any = '1',

	/// <summary>At least the specified minimum volume.</summary>
	Minimum = '2',

	/// <summary>Complete volume.</summary>
	Complete = '3',
}

/// <summary>CTP contingent trigger.</summary>
public enum CtpContingentConditions
{
	/// <summary>Submit immediately.</summary>
	Immediately = '1',

	/// <summary>Touch the stop price.</summary>
	Touch = '2',

	/// <summary>Touch a profit price.</summary>
	TouchProfit = '3',

	/// <summary>Last price is greater than or equal to the stop price.</summary>
	LastPriceGreaterOrEqual = '6',

	/// <summary>Last price is less than or equal to the stop price.</summary>
	LastPriceLessOrEqual = '8',
}

/// <summary>CTP force-close reason.</summary>
public enum CtpForceCloseReasons
{
	/// <summary>Not a force-close order.</summary>
	None = '0',

	/// <summary>Insufficient deposit.</summary>
	InsufficientDeposit = '1',

	/// <summary>Client position limit exceeded.</summary>
	ClientPositionLimit = '2',

	/// <summary>Violation.</summary>
	Violation = '5',

	/// <summary>Other reason.</summary>
	Other = '6',
}

internal enum CtpChannels
{
	MarketData = 1,
	Trader = 2,
}

internal enum CtpNativeConnectionStates
{
	Failed = -1,
	Disconnected = 0,
	Connected = 1,
	Authenticated = 2,
	Ready = 3,
}

internal enum CtpDirections
{
	Buy = '0',
	Sell = '1',
}

internal enum CtpProductClasses
{
	Futures = '1',
	Options = '2',
	Combination = '3',
	Spot = '4',
	Efp = '5',
	SpotOption = '6',
	Tas = '7',
}

internal enum CtpOptionTypes
{
	Call = '1',
	Put = '2',
}

internal enum CtpPositionDirections
{
	Net = '1',
	Long = '2',
	Short = '3',
}

internal enum CtpOrderStatuses
{
	AllTraded = '0',
	PartTradedQueueing = '1',
	PartTradedNotQueueing = '2',
	NoTradeQueueing = '3',
	NoTradeNotQueueing = '4',
	Canceled = '5',
	Unknown = 'a',
	NotTouched = 'b',
	Touched = 'c',
}

internal enum CtpOrderSubmitStatuses
{
	InsertSubmitted = '0',
	CancelSubmitted = '1',
	ModifySubmitted = '2',
	Accepted = '3',
	InsertRejected = '4',
	CancelRejected = '5',
	ModifyRejected = '6',
}
