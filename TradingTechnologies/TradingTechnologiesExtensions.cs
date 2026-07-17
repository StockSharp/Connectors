namespace StockSharp.TradingTechnologies;

using Native;

internal static class TradingTechnologiesExtensions
{
	public static SecurityId ToSecurityId(this TradingTechnologiesInstrument instrument)
		=> new()
		{
			SecurityCode = instrument.Alias.IsEmpty(instrument.Name),
			BoardCode = instrument.Market.IsEmpty(BoardCodes.TradingTechnologies).ToUpperInvariant(),
			Native = instrument.Id,
		};

	public static SecurityTypes? ToSecurityType(this string value)
		=> value?.ToUpperInvariant() switch
		{
			"FUTURE" => SecurityTypes.Future,
			"OPTION" or "OPTIONSONEQUITIES" or "OPTIONSONFUTURES" or "OPTIONSONPHYSICAL" or "OPTIONSTRATEGY" => SecurityTypes.Option,
			"COMMONSTOCK" or "PREFERREDSTOCK" => SecurityTypes.Stock,
			"INDEX" => SecurityTypes.Index,
			"CURRENCIES" or "FOREIGNEXCHANGECONTRACT" or "FXFORWARD" or "FXSPOT" or "FXSWAP" => SecurityTypes.Currency,
			"MUTUALFUND" => SecurityTypes.Fund,
			"CORPORATEBOND" or "CONVERTIBLEBOND" or "USTREASURYBOND" or "USTREASURYNOTEUST" => SecurityTypes.Bond,
			"SYNTHETIC" or "MULTILEGINSTRUMENT" => SecurityTypes.Future,
			_ => null,
		};

	public static OptionTypes? ToOptionType(this string value)
		=> value?.ToUpperInvariant() switch
		{
			"CALL" => OptionTypes.Call,
			"PUT" => OptionTypes.Put,
			_ => null,
		};

	public static OrderStates ToOrderState(this string value)
		=> value?.ToUpperInvariant() switch
		{
			"FILLED" or "CANCELED" or "DONEFORDAY" or "EXPIRED" or "CALCULATED" or "INACTIVE" => OrderStates.Done,
			"REJECTED" => OrderStates.Failed,
			"NEW" or "PARTIALLYFILLED" or "ACCEPTEDFORBIDDING" or "PLANNED" or "STOPPED" or "SUSPENDED" => OrderStates.Active,
			_ => OrderStates.Pending,
		};

	public static OrderTypes ToOrderType(this string value)
		=> value?.ToUpperInvariant() switch
		{
			"MARKET" or "MARKETCLOSETODAY" or "MARKETREDUCEONLY" or "MARKETWITHLEFTOVERASLIMIT" or "MARKETLIMITMARKETLEFTOVERASLIMIT" => OrderTypes.Market,
			"STOP" or "IFTOUCHEDMARKET" => OrderTypes.Conditional,
			_ => OrderTypes.Limit,
		};

	public static TimeInForce ToTimeInForce(this string value)
		=> value?.ToUpperInvariant() switch
		{
			"IMMEDIATEORCANCEL" or "IMMEDIATEORCANCELPLUS" => TimeInForce.CancelBalance,
			"FILLORKILL" or "FILLORKILLPLUS" => TimeInForce.MatchOrCancel,
			_ => TimeInForce.PutInQueue,
		};

	public static Sides ToSide(this string value)
		=> value?.StartsWith("Buy", StringComparison.OrdinalIgnoreCase) == true ? Sides.Buy : Sides.Sell;

	public static OrderPositionEffects? ToPositionEffect(this string value)
		=> value?.ToUpperInvariant() switch
		{
			"OPEN" => OrderPositionEffects.OpenOnly,
			"CLOSE" or "CLOSEBUTNOTIFYONOPEN" => OrderPositionEffects.CloseOnly,
			_ => null,
		};

	public static ulong? GetNativeId(this SecurityId securityId)
	{
		if (securityId.Native is ulong id)
			return id;
		if (securityId.Native != null && ulong.TryParse(securityId.Native.ToString(), out id))
			return id;
		return null;
	}
}
