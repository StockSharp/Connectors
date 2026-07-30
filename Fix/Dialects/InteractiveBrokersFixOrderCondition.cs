namespace StockSharp.Fix.Dialects;

using System.Runtime.Serialization;

/// <summary>
/// InteractiveBrokers (FIX CTCI) order condition.
/// </summary>
[Serializable]
[DataContract]
[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.InteractiveBrokersKey)]
public class InteractiveBrokersFixOrderCondition : FixOrderCondition
{
	/// <summary>
	/// Initializes a new instance of the <see cref="InteractiveBrokersFixOrderCondition"/>.
	/// </summary>
	public InteractiveBrokersFixOrderCondition()
	{
	}

	/// <summary>
	/// Tag 5700 is required for short sale orders involving U.S. stocks to indicate the present location of the shares that are to be delivered in connection with customer's short sale order New Order.
	/// </summary>
	[DataMember]
	public string LocateBroker
	{
		get => (string)Parameters.TryGetValue(nameof(LocateBroker));
		set => Parameters[nameof(LocateBroker)] = value;
	}

	/// <summary>
	/// Used for IBKR Algo Orders New Order (IBKR Algos).
	/// </summary>
	[DataMember]
	public int? NoStrategyParameters
	{
		get => (int?)Parameters.TryGetValue(nameof(NoStrategyParameters));
		set => Parameters[nameof(NoStrategyParameters)] = value;
	}

	/// <summary>
	/// Used for IBKR Algo Orders Valid Values: riskAversion pctVol forceCompletion New Order (IBKR Algos).
	/// </summary>
	[DataMember]
	public string StrategyParameterName
	{
		get => (string)Parameters.TryGetValue(nameof(StrategyParameterName));
		set => Parameters[nameof(StrategyParameterName)] = value;
	}

	/// <summary>
	/// Used for IBKR Algo Orders Valid Values: Aggressive Passive Neutral Get Done New Order (IBKR Algos).
	/// </summary>
	[DataMember]
	public string StrategyParameterValue
	{
		get => (string)Parameters.TryGetValue(nameof(StrategyParameterValue));
		set => Parameters[nameof(StrategyParameterValue)] = value;
	}

	/// <summary>
	/// New Order.
	/// </summary>
	[DataMember]
	public string ContractID
	{
		get => (string)Parameters.TryGetValue(nameof(ContractID));
		set => Parameters[nameof(ContractID)] = value;
	}

	/// <summary>
	/// Extra user-defined field for additional identification for customer orders. New Order-Single.
	/// </summary>
	[DataMember]
	public string OrderReferenceAccount
	{
		get => (string)Parameters.TryGetValue(nameof(OrderReferenceAccount));
		set => Parameters[nameof(OrderReferenceAccount)] = value;
	}

	/// <summary>
	/// For US Equity Options, the OCC 21-character OSI symbol is used. Format: Option root [6 char] Yr (2 char] Mo [2 char] Day [2 char] c/p [1 char] dollar strike [5 char] decimal strike [3 char] Example: MSFT 200117C00140000 I New Order-Single.
	/// </summary>
	[DataMember]
	public string IBKRLocalSymbol
	{
		get => (string)Parameters.TryGetValue(nameof(IBKRLocalSymbol));
		set => Parameters[nameof(IBKRLocalSymbol)] = value;
	}

	/// <summary>
	/// Used for product identification for Options only. This represents the option 'class' Example: The underlying symbol for Microsoft is 'MSFT' The option class symbol for Microsoft is 'MSQ' New Order-Single.
	/// </summary>
	[DataMember]
	public string TradingClass
	{
		get => (string)Parameters.TryGetValue(nameof(TradingClass));
		set => Parameters[nameof(TradingClass)] = value;
	}

	/// <summary>
	/// The valid values are either '1' or '2'. New Order.
	/// </summary>
	[DataMember]
	public int? ShortSaleRule
	{
		get => (int?)Parameters.TryGetValue(nameof(ShortSaleRule));
		set => Parameters[nameof(ShortSaleRule)] = value;
	}

	/// <summary>
	/// WhatIf: 6091 = 1 New Order - Single.
	/// </summary>
	[DataMember]
	public int? WhatIf
	{
		get => (int?)Parameters.TryGetValue(nameof(WhatIf));
		set => Parameters[nameof(WhatIf)] = value;
	}

	/// <summary>
	/// Sets the Stop Trigger Method for stops, stop limits, and trailing stops. New Order.
	/// </summary>
	[DataMember]
	public string TriggerMethod
	{
		get => (string)Parameters.TryGetValue(nameof(TriggerMethod));
		set => Parameters[nameof(TriggerMethod)] = value;
	}

	/// <summary>
	/// Specifies the order capacity. This tag take precedence over all other order capacity tags. Valid Values: c = Customer f = Firm m = Market Maker b = Broker Dealer n = Away Market Maker y = Specialist in Underlying j = Joint Back Office New Order.
	/// </summary>
	[DataMember]
	public string OptionAcct
	{
		get => (string)Parameters.TryGetValue(nameof(OptionAcct));
		set => Parameters[nameof(OptionAcct)] = value;
	}

	/// <summary>
	/// IBKR's internal contract ID New Order-Single.
	/// </summary>
	[DataMember]
	public string ConditionConID
	{
		get => (string)Parameters.TryGetValue(nameof(ConditionConID));
		set => Parameters[nameof(ConditionConID)] = value;
	}

	/// <summary>
	/// The condition needs to be met based upon market data from this exchange New Order-Single.
	/// </summary>
	[DataMember]
	public string ConditionExchange
	{
		get => (string)Parameters.TryGetValue(nameof(ConditionExchange));
		set => Parameters[nameof(ConditionExchange)] = value;
	}

	/// <summary>
	/// The trigger price for the condition New Order-Single.
	/// </summary>
	[DataMember]
	public decimal? ConditionTriggerPrice
	{
		get => (decimal?)Parameters.TryGetValue(nameof(ConditionTriggerPrice));
		set => Parameters[nameof(ConditionTriggerPrice)] = value;
	}

	/// <summary>
	/// The condition operation Valid Values: &lt;= &gt;= New Order-Single.
	/// </summary>
	[DataMember]
	public string ConditionOperand
	{
		get => (string)Parameters.TryGetValue(nameof(ConditionOperand));
		set => Parameters[nameof(ConditionOperand)] = value;
	}

	/// <summary>
	/// Trigger method for the condition Valid Values: 1 = Double Bid/Ask 2 = Last 3 = Double Last 4 = Bid/Ask New Order-Single.
	/// </summary>
	[DataMember]
	public int? ConditionTriggerMethod
	{
		get => (int?)Parameters.TryGetValue(nameof(ConditionTriggerMethod));
		set => Parameters[nameof(ConditionTriggerMethod)] = value;
	}

	/// <summary>
	/// Setting to allow for the triggering of conditional orders outside regular market hours. Valid Values: 1 = Allow triggering outside of regular trading hours If tag is omitted, triggering will be limited to regular trading hours. New Order-Single.
	/// </summary>
	[DataMember]
	public int? ConditionIgnoreRegularTradingHours
	{
		get => (int?)Parameters.TryGetValue(nameof(ConditionIgnoreRegularTradingHours));
		set => Parameters[nameof(ConditionIgnoreRegularTradingHours)] = value;
	}

	/// <summary>
	/// The number of conditions in the message New Order-Single.
	/// </summary>
	[DataMember]
	public decimal? ConditionListSize
	{
		get => (decimal?)Parameters.TryGetValue(nameof(ConditionListSize));
		set => Parameters[nameof(ConditionListSize)] = value;
	}

	/// <summary>
	/// The logical binder used with multiple conditions. a = and o = or n = non defined (should only be used with the last condition in a list) New Order-Single.
	/// </summary>
	[DataMember]
	public char? ConditionLogicOperantBinder
	{
		get => (char?)Parameters.TryGetValue(nameof(ConditionLogicOperantBinder));
		set => Parameters[nameof(ConditionLogicOperantBinder)] = value;
	}

	/// <summary>
	/// Used in conjunction with the custom Tag 18=s (peg to stock) function. This tag specifies the lower range of the underlying range for a delta order. If the underlying stock goes below this value, the order is canceled New Order-Single.
	/// </summary>
	[DataMember]
	public decimal? StockRangeLower
	{
		get => (decimal?)Parameters.TryGetValue(nameof(StockRangeLower));
		set => Parameters[nameof(StockRangeLower)] = value;
	}

	/// <summary>
	/// Used in conjunction with the custom Tag 18=s (peg to stock) function. This tag specifies the lower range of the underlying range for a delta order. If the underlying stock goes above this value, the order is canceled New Order-Single.
	/// </summary>
	[DataMember]
	public decimal? StockRangeUpper
	{
		get => (decimal?)Parameters.TryGetValue(nameof(StockRangeUpper));
		set => Parameters[nameof(StockRangeUpper)] = value;
	}

	/// <summary>
	/// Used in conjunction with the custom Tag 18=s (peg to stock) function. This tag specifies the delta to be used in the order. Value must be between -100 and 100. (the sign is ignored) New Order-Single.
	/// </summary>
	[DataMember]
	public decimal? Delta
	{
		get => (decimal?)Parameters.TryGetValue(nameof(Delta));
		set => Parameters[nameof(Delta)] = value;
	}

	/// <summary>
	/// Underlying symbol of the product upon which the condition exists. New Order-Single.
	/// </summary>
	[DataMember]
	public string ConditionUnderlying
	{
		get => (string)Parameters.TryGetValue(nameof(ConditionUnderlying));
		set => Parameters[nameof(ConditionUnderlying)] = value;
	}

	/// <summary>
	/// The strike price of the security if it is an option New Order-Single.
	/// </summary>
	[DataMember]
	public decimal? ConditionStrike
	{
		get => (decimal?)Parameters.TryGetValue(nameof(ConditionStrike));
		set => Parameters[nameof(ConditionStrike)] = value;
	}

	/// <summary>
	/// The right of an option (call or put) Valid Values: C = Call P = Put New Order-Single.
	/// </summary>
	[DataMember]
	public char? ConditionRight
	{
		get => (char?)Parameters.TryGetValue(nameof(ConditionRight));
		set => Parameters[nameof(ConditionRight)] = value;
	}

	/// <summary>
	/// The expiration year and month (for futures or options) Format: ( yyyymm ) New Order-Single.
	/// </summary>
	[DataMember]
	public DateTime? ConditionExpiry
	{
		get => (DateTime?)Parameters.TryGetValue(nameof(ConditionExpiry));
		set => Parameters[nameof(ConditionExpiry)] = value;
	}

	/// <summary>
	/// Specifies the security type in a conditional order New Order-Single.
	/// </summary>
	[DataMember]
	public string ConditionSecurityType
	{
		get => (string)Parameters.TryGetValue(nameof(ConditionSecurityType));
		set => Parameters[nameof(ConditionSecurityType)] = value;
	}

	/// <summary>
	/// Specifies the IBKR Local Symbol of the product you are making an order conditional upon New Order-Single.
	/// </summary>
	[DataMember]
	public string ConditionLocalSymbol
	{
		get => (string)Parameters.TryGetValue(nameof(ConditionLocalSymbol));
		set => Parameters[nameof(ConditionLocalSymbol)] = value;
	}

	/// <summary>
	/// For use with the P.I.P. order type on the BOX. Specifies the Auction Strategy for the P.I.P. order 1 = Discretionary Matching 2 = Discretionary Improving 3 = Transparent New Order-Single.
	/// </summary>
	[DataMember]
	public int? DiscretionaryType
	{
		get => (int?)Parameters.TryGetValue(nameof(DiscretionaryType));
		set => Parameters[nameof(DiscretionaryType)] = value;
	}

	/// <summary>
	/// 1=ForceOnlyRTH is ON New Order Single.
	/// </summary>
	[DataMember]
	public int? ForceOnlyRTH
	{
		get => (int?)Parameters.TryGetValue(nameof(ForceOnlyRTH));
		set => Parameters[nameof(ForceOnlyRTH)] = value;
	}

	/// <summary>
	/// Same as 114 ' Used for combination orders Valid codes = 'N' or 'Y.' Required for multi-leg short sale orders involving U.S. equity securities ('stocks'). If customer uses IBKR as its executing broker but uses a clearing broker other than IBKR (a 'NonCleared Customer') and Tag 624 contains the value '5' and Tag 6086 contains the value '1' or '2', this Tag 6215 must contain the value 'N'. New Order - Multileg.
	/// </summary>
	[DataMember]
	public bool? LegLocateReqd
	{
		get => (bool?)Parameters.TryGetValue(nameof(LegLocateReqd));
		set => Parameters[nameof(LegLocateReqd)] = value;
	}

	/// <summary>
	/// Same as 5700 ' Used for combination orders Tag 6216 (a four letter clearing broker or custodian MPID) is required for multi-leg short sale orders involving U.S. stocks to indicate the present location of the shares that are to be delivered in connection with customer's short sale order New Order ' Multileg.
	/// </summary>
	[DataMember]
	public string LegLocateBroker
	{
		get => (string)Parameters.TryGetValue(nameof(LegLocateBroker));
		set => Parameters[nameof(LegLocateBroker)] = value;
	}

	/// <summary>
	/// Used if condition symbol would be otherwise ambiguous New Order - Single.
	/// </summary>
	[DataMember]
	public string CondPrimaryExch
	{
		get => (string)Parameters.TryGetValue(nameof(CondPrimaryExch));
		set => Parameters[nameof(CondPrimaryExch)] = value;
	}

	/// <summary>
	/// Used if condition symbol would be otherwise ambiguous New Order - Single.
	/// </summary>
	[DataMember]
	public string CondCurrency
	{
		get => (string)Parameters.TryGetValue(nameof(CondCurrency));
		set => Parameters[nameof(CondCurrency)] = value;
	}

	/// <summary>
	/// Used if sending ConditionType other than Price (default if tag 6222 not specified) is desired. 1=Price, 3=Time, 4=Margin Cushion, 5=Trade, 6=Volume New Order - Single.
	/// </summary>
	[DataMember]
	public int? ConditionType
	{
		get => (int?)Parameters.TryGetValue(nameof(ConditionType));
		set => Parameters[nameof(ConditionType)] = value;
	}

	/// <summary>
	/// Required if 6222=3, format: yyyymmdd-hh:mm:ss New Order - Single.
	/// </summary>
	[DataMember]
	public DateTime? ConditionTime
	{
		get => (DateTime?)Parameters.TryGetValue(nameof(ConditionTime));
		set => Parameters[nameof(ConditionTime)] = value;
	}

	/// <summary>
	/// Required if 6222=4, format: integer New Order - Single.
	/// </summary>
	[DataMember]
	public int? ConditionMargin
	{
		get => (int?)Parameters.TryGetValue(nameof(ConditionMargin));
		set => Parameters[nameof(ConditionMargin)] = value;
	}

	/// <summary>
	/// Required if 6222=5, format: string New Order - Single.
	/// </summary>
	[DataMember]
	public string ConditionExecutionPattern
	{
		get => (string)Parameters.TryGetValue(nameof(ConditionExecutionPattern));
		set => Parameters[nameof(ConditionExecutionPattern)] = value;
	}

	/// <summary>
	/// Used with SMART routed combos to specify whether inter-exchange SMART combos are to be guaranteed or non-guaranteed. 0 = Guaranteed 1 = Non-Guaranteed 6248=1 required for STK/STK combo orders New Order - Multileg.
	/// </summary>
	[DataMember]
	public int? SmartComboGuarantee
	{
		get => (int?)Parameters.TryGetValue(nameof(SmartComboGuarantee));
		set => Parameters[nameof(SmartComboGuarantee)] = value;
	}

	/// <summary>
	/// Specifies the number of 'barriers' used in the adjustable stop order type. New Order Single.
	/// </summary>
	[DataMember]
	public int? NoBarriers
	{
		get => (int?)Parameters.TryGetValue(nameof(NoBarriers));
		set => Parameters[nameof(NoBarriers)] = value;
	}

	/// <summary>
	/// Specifies the trigger price for the barrier. (required if 6257 >0) New Order Single.
	/// </summary>
	[DataMember]
	public decimal? BarrierPrice
	{
		get => (decimal?)Parameters.TryGetValue(nameof(BarrierPrice));
		set => Parameters[nameof(BarrierPrice)] = value;
	}

	/// <summary>
	/// Specifies the new stop price once the barrier is reached. New Order Single.
	/// </summary>
	[DataMember]
	public decimal? BarrierStopPrice
	{
		get => (decimal?)Parameters.TryGetValue(nameof(BarrierStopPrice));
		set => Parameters[nameof(BarrierStopPrice)] = value;
	}

	/// <summary>
	/// Specifies the new trailing amount once the barrier is reached New Order Single.
	/// </summary>
	[DataMember]
	public decimal? BarrierTrailingAmt
	{
		get => (decimal?)Parameters.TryGetValue(nameof(BarrierTrailingAmt));
		set => Parameters[nameof(BarrierTrailingAmt)] = value;
	}

	/// <summary>
	/// Specifies the order type when the barrier is reached. Valid Values: 3 = Stop 4 = Stop Limit T = Trailing Stop TSL = Trailing Stop Limit New Order Single.
	/// </summary>
	[DataMember]
	public string BarrierPriceDelimiter
	{
		get => (string)Parameters.TryGetValue(nameof(BarrierPriceDelimiter));
		set => Parameters[nameof(BarrierPriceDelimiter)] = value;
	}

	/// <summary>
	/// Specifies the new limit price once the barrier is reached. New Order Single.
	/// </summary>
	[DataMember]
	public decimal? BarrierLimitPrice
	{
		get => (decimal?)Parameters.TryGetValue(nameof(BarrierLimitPrice));
		set => Parameters[nameof(BarrierLimitPrice)] = value;
	}

	/// <summary>
	/// Required if 6222=6 New Order Single.
	/// </summary>
	[DataMember]
	public int? ConditionVolume
	{
		get => (int?)Parameters.TryGetValue(nameof(ConditionVolume));
		set => Parameters[nameof(ConditionVolume)] = value;
	}

	/// <summary>
	/// Specifies the trail method used: Valid Values: 6268=0 ' Absolute offset used 6268=100 ' Percentage offset used New Order ' Single (trailing stop orders).
	/// </summary>
	[DataMember]
	public string TrailingAmtUnit
	{
		get => (string)Parameters.TryGetValue(nameof(TrailingAmtUnit));
		set => Parameters[nameof(TrailingAmtUnit)] = value;
	}

	/// <summary>
	/// Specifies the trail method used for Barrier orders: Valid Values: 6268=0 ' Absolute offset used 6268=100 ' Percentage offset used New Order ' Single (trailing stop orders).
	/// </summary>
	[DataMember]
	public string BarrierTrailingAmtUnit
	{
		get => (string)Parameters.TryGetValue(nameof(BarrierTrailingAmtUnit));
		set => Parameters[nameof(BarrierTrailingAmtUnit)] = value;
	}

	/// <summary>
	/// Specifies whether to route non-marketable orders to exchanges that charge cancellation fees. Valid Values: 6271=1 ' Confirm route to exchanges where there are no cancellation fees. New Order.
	/// </summary>
	[DataMember]
	public int? CheapToReroute
	{
		get => (int?)Parameters.TryGetValue(nameof(CheapToReroute));
		set => Parameters[nameof(CheapToReroute)] = value;
	}

	/// <summary>
	/// Used in IBKR Volatility Orders 1=Use initial volatility calculation only 2=Continuously update the price as volatility calculation changes 3=Price for main order specified by client New Order (IBKR Volatility Orders).
	/// </summary>
	[DataMember]
	public string ContinuousUpdate
	{
		get => (string)Parameters.TryGetValue(nameof(ContinuousUpdate));
		set => Parameters[nameof(ContinuousUpdate)] = value;
	}

	/// <summary>
	/// Used in IBKR Volatility Orders 1=midpoint 2=bid or ask New Order (IBKR Volatility Orders).
	/// </summary>
	[DataMember]
	public string UnderlyingRefPrice
	{
		get => (string)Parameters.TryGetValue(nameof(UnderlyingRefPrice));
		set => Parameters[nameof(UnderlyingRefPrice)] = value;
	}

	/// <summary>
	/// ISE Facilitation Order Firm side for C1OrdID (firm equivalent to tag 11) New Order ' Single (ISE FOK only).
	/// </summary>
	[DataMember]
	public string XcrossC1OrdID
	{
		get => (string)Parameters.TryGetValue(nameof(XcrossC1OrdID));
		set => Parameters[nameof(XcrossC1OrdID)] = value;
	}

	/// <summary>
	/// ISE Facilitation Order Firm's ClearingFirm (firm equivalent to tag 439) New Order ' Single (ISE FOK only).
	/// </summary>
	[DataMember]
	public string XCrossClearingFirm
	{
		get => (string)Parameters.TryGetValue(nameof(XCrossClearingFirm));
		set => Parameters[nameof(XCrossClearingFirm)] = value;
	}

	/// <summary>
	/// ISE Facilitation Order Firm's ClearingAccount (firm equivalent to tag 440) New Order ' Single (ISE FOK only).
	/// </summary>
	[DataMember]
	public string XCrossClearingAccount
	{
		get => (string)Parameters.TryGetValue(nameof(XCrossClearingAccount));
		set => Parameters[nameof(XCrossClearingAccount)] = value;
	}

	/// <summary>
	/// ISE Facilitation Order Firm's OpenClose (firm equivalent to tag 77) New Order ' Single (ISE FOK only).
	/// </summary>
	[DataMember]
	public string XCrossOpenClose
	{
		get => (string)Parameters.TryGetValue(nameof(XCrossOpenClose));
		set => Parameters[nameof(XCrossOpenClose)] = value;
	}

	/// <summary>
	/// ISE Facilitation Order Firm's OptionAcct (firm equivalent to tag6122) New Order ' Single (ISE FOK only).
	/// </summary>
	[DataMember]
	public string XCrossOptionAcct
	{
		get => (string)Parameters.TryGetValue(nameof(XCrossOptionAcct));
		set => Parameters[nameof(XCrossOptionAcct)] = value;
	}

	/// <summary>
	/// ISE Facilitation Order Desired percentage New Order ' Single (ISE FOK only).
	/// </summary>
	[DataMember]
	public decimal? FacilitationPercentage
	{
		get => (decimal?)Parameters.TryGetValue(nameof(FacilitationPercentage));
		set => Parameters[nameof(FacilitationPercentage)] = value;
	}

	/// <summary>
	/// Required with a value of '1' on all new orders (35=D) for stock quoting customers Valid Values: ( 0 = false , 1 = true ) New Order ' Single.
	/// </summary>
	[DataMember]
	public bool? NotHeld
	{
		get => (bool?)Parameters.TryGetValue(nameof(NotHeld));
		set => Parameters[nameof(NotHeld)] = value;
	}

	/// <summary>
	/// Used in IBKR Volatility Orders Valid Values: -1=No Hedging 1=MKT Hedging 2=Limit Order Hedge E=Relative Order Hedge New Order (IBKR Volatility Orders).
	/// </summary>
	[DataMember]
	public string HedgingType
	{
		get => (string)Parameters.TryGetValue(nameof(HedgingType));
		set => Parameters[nameof(HedgingType)] = value;
	}

	/// <summary>
	/// Used in trailing stop limit orders to specify the offset of the limit price. Can be positive, negative, or zero New Order - Single.
	/// </summary>
	[DataMember]
	public decimal? TrailLimitOffset
	{
		get => (decimal?)Parameters.TryGetValue(nameof(TrailLimitOffset));
		set => Parameters[nameof(TrailLimitOffset)] = value;
	}

	/// <summary>
	/// Used to identify if an order will be staged to the TWS Blotter screen Valid Values: 1=Yes 0=No (defaults to 0 if omitted) New Order - Single.
	/// </summary>
	[DataMember]
	public decimal? StagedOrder
	{
		get => (decimal?)Parameters.TryGetValue(nameof(StagedOrder));
		set => Parameters[nameof(StagedOrder)] = value;
	}

	/// <summary>
	/// Used in IBKR Algo Orders to deactivate an order at the close of the current trading day. 1=deactivate the order at the end fo the day. 0=do not deactivate at the end of the day. (defaults to 0 if omitted) New Order (IBKR Algo Orders).
	/// </summary>
	[DataMember]
	public int? DeactivateOnClose
	{
		get => (int?)Parameters.TryGetValue(nameof(DeactivateOnClose));
		set => Parameters[nameof(DeactivateOnClose)] = value;
	}

	/// <summary>
	/// Used in IBKR Volatility Orders Valid Format: Yyyymmdd/value,yyyymmdd/value etc. New Order (IBKR Volatility Orders).
	/// </summary>
	[DataMember]
	public string DividendSchedule
	{
		get => (string)Parameters.TryGetValue(nameof(DividendSchedule));
		set => Parameters[nameof(DividendSchedule)] = value;
	}

	/// <summary>
	/// Used in IBKR Volatility Orders Valid Format: Yyyymmdd/value,yyyymmdd/value etc. New Order (IBKR Volatility Orders).
	/// </summary>
	[DataMember]
	public string InterestSchedule
	{
		get => (string)Parameters.TryGetValue(nameof(InterestSchedule));
		set => Parameters[nameof(InterestSchedule)] = value;
	}

	/// <summary>
	/// Used in IBKR Volatility Orders Valid Values: 1=Hedging Order Anything else or omitted=NOT hedging order New Order (IBKR Volatility Orders).
	/// </summary>
	[DataMember]
	public string IsDeltaHedge
	{
		get => (string)Parameters.TryGetValue(nameof(IsDeltaHedge));
		set => Parameters[nameof(IsDeltaHedge)] = value;
	}

	/// <summary>
	/// Used in IBKR Volatility Orders Percentage in decimal form New Order (IBKR Volatility Orders).
	/// </summary>
	[DataMember]
	public decimal? VolatCapPercentage
	{
		get => (decimal?)Parameters.TryGetValue(nameof(VolatCapPercentage));
		set => Parameters[nameof(VolatCapPercentage)] = value;
	}

	/// <summary>
	/// Used in IBKR Volatility Orders Price tick offset in decimal form New Order (IBKR Volatility Orders).
	/// </summary>
	[DataMember]
	public string VolatCapTicks
	{
		get => (string)Parameters.TryGetValue(nameof(VolatCapTicks));
		set => Parameters[nameof(VolatCapTicks)] = value;
	}

	/// <summary>
	/// Only available for clients with unbundled commissions 1 = Highest Rebate 2 = Primary Exchange 3 = Highest Volume Exchange with Rebate 4 = Highest Volume Exchange with Lowest Fee New Order Single.
	/// </summary>
	[DataMember]
	public int? ConsiderExecCost
	{
		get => (int?)Parameters.TryGetValue(nameof(ConsiderExecCost));
		set => Parameters[nameof(ConsiderExecCost)] = value;
	}

	/// <summary>
	/// Specifies if the order should be submitted or canceled if the condition is met 0 = Submit 1 = Cancel New Order - Single.
	/// </summary>
	[DataMember]
	public int? CondSubmitCancel
	{
		get => (int?)Parameters.TryGetValue(nameof(CondSubmitCancel));
		set => Parameters[nameof(CondSubmitCancel)] = value;
	}

	/// <summary>
	/// Stock reference price for pegged to stock orders. (i.e. Option order Price = auxPrice + (NBBO - stockRefPrice) * delta ) New Order - Single.
	/// </summary>
	[DataMember]
	public string StockRefPrice
	{
		get => (string)Parameters.TryGetValue(nameof(StockRefPrice));
		set => Parameters[nameof(StockRefPrice)] = value;
	}

	/// <summary>
	/// Allows routing firms to designate an order as being routed by a 'professional customer' as determined by the order routing firm. IBKR will pass this designation on to destination option exchange. 1 = True 0 = False (default) New Order - Single.
	/// </summary>
	[DataMember]
	public int? ProfessionalCustomer
	{
		get => (int?)Parameters.TryGetValue(nameof(ProfessionalCustomer));
		set => Parameters[nameof(ProfessionalCustomer)] = value;
	}

	/// <summary>
	/// Used in Pair Trade 6665 = 3 (Pair Trade) New Order Single.
	/// </summary>
	[DataMember]
	public int? HedgeType
	{
		get => (int?)Parameters.TryGetValue(nameof(HedgeType));
		set => Parameters[nameof(HedgeType)] = value;
	}

	/// <summary>
	/// Used in Pair Trade 6666 = Rate used to compute child order size Example: 1.8 (parent order size = 100, 180 will be used for child) New Order Single.
	/// </summary>
	[DataMember]
	public int? HedgeRatio
	{
		get => (int?)Parameters.TryGetValue(nameof(HedgeRatio));
		set => Parameters[nameof(HedgeRatio)] = value;
	}

	/// <summary>
	/// Per-Leg clearing for combo orders. Order should contain blank values for all legs except stock legs New Order - Multileg.
	/// </summary>
	[DataMember]
	public string LegClearingFirm
	{
		get => (string)Parameters.TryGetValue(nameof(LegClearingFirm));
		set => Parameters[nameof(LegClearingFirm)] = value;
	}

	/// <summary>
	/// Specifies whether net pricing should be used over standard raw pricing. 1 = Use net price 0 = Use raw price (default) New Order Single (Fixed Income Orders).
	/// </summary>
	[DataMember]
	public int? UseNetPrice
	{
		get => (int?)Parameters.TryGetValue(nameof(UseNetPrice));
		set => Parameters[nameof(UseNetPrice)] = value;
	}

	/// <summary>
	/// Used in Imbalance Orders, use case is 6737=1 New Order Single.
	/// </summary>
	[DataMember]
	public int? ImbalanceOnly
	{
		get => (int?)Parameters.TryGetValue(nameof(ImbalanceOnly));
		set => Parameters[nameof(ImbalanceOnly)] = value;
	}

	/// <summary>
	/// Mifid2 Code used to demine IBKR's assigned short code for decision maker for the order. New Order Single.
	/// </summary>
	[DataMember]
	public string Mifid2DecisionMakerShortCode
	{
		get => (string)Parameters.TryGetValue(nameof(Mifid2DecisionMakerShortCode));
		set => Parameters[nameof(Mifid2DecisionMakerShortCode)] = value;
	}

	/// <summary>
	/// Mifid2 Algo used to demine decision maker ALGO for the order. New Order Single.
	/// </summary>
	[DataMember]
	public string Mifid2DecisionAlgo
	{
		get => (string)Parameters.TryGetValue(nameof(Mifid2DecisionAlgo));
		set => Parameters[nameof(Mifid2DecisionAlgo)] = value;
	}

	/// <summary>
	/// Name of person or IB assigned short code who is responsible for the execution within the firm New Order Single.
	/// </summary>
	[DataMember]
	public string Mifid2ExecutionTrader
	{
		get => (string)Parameters.TryGetValue(nameof(Mifid2ExecutionTrader));
		set => Parameters[nameof(Mifid2ExecutionTrader)] = value;
	}

	/// <summary>
	/// Name of ALGO or IB assigned short code who is responsible for the execution within the firm New Order Single.
	/// </summary>
	[DataMember]
	public string Mifid2ExecutionAlgo
	{
		get => (string)Parameters.TryGetValue(nameof(Mifid2ExecutionAlgo));
		set => Parameters[nameof(Mifid2ExecutionAlgo)] = value;
	}

	/// <summary>
	/// Distinct Parameter for IBKR Algo Orders 1 = yes 0 = no NewOrder Single (IBKR ALGO Orders).
	/// </summary>
	[DataMember]
	public string AllowPastEndTime
	{
		get => (string)Parameters.TryGetValue(nameof(AllowPastEndTime));
		set => Parameters[nameof(AllowPastEndTime)] = value;
	}

	/// <summary>
	/// Distinct Parameter for IBKR Algo Orders NewOrder Single (IBKR ALGO Orders).
	/// </summary>
	[DataMember]
	public int? DisplaySize
	{
		get => (int?)Parameters.TryGetValue(nameof(DisplaySize));
		set => Parameters[nameof(DisplaySize)] = value;
	}

	/// <summary>
	/// Distinct Parameter for IBKR Algo Orders Format: yyyymmdd-hh:mm:ss NewOrder Single (IBKR ALGO Orders).
	/// </summary>
	[DataMember]
	public DateTime? EndTime
	{
		get => (DateTime?)Parameters.TryGetValue(nameof(EndTime));
		set => Parameters[nameof(EndTime)] = value;
	}

	/// <summary>
	/// Distinct Parameter for IBKR Algo Orders 1 = true 0 = false NewOrder Single (IBKR ALGO Orders).
	/// </summary>
	[DataMember]
	public string ForceCompletion
	{
		get => (string)Parameters.TryGetValue(nameof(ForceCompletion));
		set => Parameters[nameof(ForceCompletion)] = value;
	}

	/// <summary>
	/// Distinct Parameter for IBKR Algo Orders NewOrder Single (IBKR ALGO Orders).
	/// </summary>
	[DataMember]
	public int? PctVol
	{
		get => (int?)Parameters.TryGetValue(nameof(PctVol));
		set => Parameters[nameof(PctVol)] = value;
	}

	/// <summary>
	/// Distinct Parameter for IBKR Algo Orders One of these 4 values: Aggr / Pass / Neut / GetDon NewOrder Single (IBKR ALGO Orders).
	/// </summary>
	[DataMember]
	public string RiskAversion
	{
		get => (string)Parameters.TryGetValue(nameof(RiskAversion));
		set => Parameters[nameof(RiskAversion)] = value;
	}

	/// <summary>
	/// Distinct Parameter for IBKR Algo Orders Format: yyyymmdd-hh:mm:ss NewOrder Single (IBKR ALGO Orders).
	/// </summary>
	[DataMember]
	public DateTime? StartTime
	{
		get => (DateTime?)Parameters.TryGetValue(nameof(StartTime));
		set => Parameters[nameof(StartTime)] = value;
	}

	/// <summary>
	/// Used in IBKR Volatility Orders Volatility in decimal form (decimal percentage) New Order (IBKR Volatility Orders).
	/// </summary>
	[DataMember]
	public string ImpVolatility
	{
		get => (string)Parameters.TryGetValue(nameof(ImpVolatility));
		set => Parameters[nameof(ImpVolatility)] = value;
	}
}