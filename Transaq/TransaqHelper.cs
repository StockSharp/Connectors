namespace StockSharp.Transaq;

static class TransaqHelper
{
	public static DateTime ToDto(this DateTime utc)
	{
		if (utc == DateTime.MinValue)
			return utc;

		return utc.UtcKind();
	}

	public static NewOrderUnfilleds ToTransaq(this TimeInForce? tif)
	{
        return tif switch
        {
            TimeInForce.CancelBalance => NewOrderUnfilleds.CancelBalance,
            TimeInForce.MatchOrCancel => NewOrderUnfilleds.ImmOrCancel,
            TimeInForce.PutInQueue or null => NewOrderUnfilleds.PutInQueue,
            _ => throw new ArgumentOutOfRangeException(nameof(tif), tif, LocalizedStrings.InvalidValue),
        };
    }

	public static BuySells ToTransaq(this Sides side)
	{
        return side switch
        {
            Sides.Buy => BuySells.B,
            Sides.Sell => BuySells.S,
            _ => throw new ArgumentOutOfRangeException(nameof(side), side, LocalizedStrings.InvalidValue),
        };
    }

	public static Sides FromTransaq(this BuySells side)
	{
        return side switch
        {
            BuySells.B => Sides.Buy,
            BuySells.S => Sides.Sell,
            _ => throw new ArgumentOutOfRangeException(nameof(side), side, LocalizedStrings.InvalidValue),
        };
    }

	public static NewStopOrderElement CreateStopLoss(TransaqOrderCondition cond, string brokerRef)
	{
		if (cond == null)
			throw new ArgumentNullException(nameof(cond));

		return new NewStopOrderElement
		{
			ActivationPrice = (double?)cond.StopLossActivationPrice,
			OrderPrice = cond.StopLossOrderPrice.To<string>(),
			ByMarket = cond.StopLossByMarket,
			Quantity = cond.StopLossVolume.To<string>(),
			UseCredit = cond.StopLossUseCredit,
			GuardTime = cond.StopLossProtectionTime,
			BrokerRef = brokerRef
		};
	}

	public static NewStopOrderElement CreateTakeProfit(TransaqOrderCondition cond, string brokerRef)
	{
		if (cond == null)
			throw new ArgumentNullException(nameof(cond));

		return new NewStopOrderElement
		{
			ActivationPrice = (double?)cond.TakeProfitActivationPrice,
			ByMarket = cond.TakeProfitByMarket,
			Quantity = cond.TakeProfitVolume.To<string>(),
			UseCredit = cond.TakeProfitUseCredit,
			GuardTime = cond.TakeProfitProtectionTime,
			BrokerRef = brokerRef,
			Correction = cond.TakeProfitCorrection.To<string>(),
			Spread = cond.TakeProfitProtectionSpread.To<string>()
		};
	}

	public static bool CheckConditionUnitType(this TransaqOrderCondition cond)
	{
		if (cond == null)
			throw new ArgumentNullException(nameof(cond));

		if ((cond.StopLossOrderPrice != null && cond.StopLossOrderPrice.Type != UnitTypes.Absolute & cond.StopLossOrderPrice.Type != UnitTypes.Percent) ||
			(cond.StopLossVolume != null && cond.StopLossVolume.Type != UnitTypes.Absolute & cond.StopLossVolume.Type != UnitTypes.Percent) ||
			(cond.TakeProfitVolume != null && cond.TakeProfitVolume.Type != UnitTypes.Absolute & cond.TakeProfitVolume.Type != UnitTypes.Percent) ||
			(cond.TakeProfitCorrection != null && cond.TakeProfitCorrection.Type != UnitTypes.Absolute & cond.TakeProfitCorrection.Type != UnitTypes.Percent) ||
			(cond.TakeProfitProtectionSpread != null && cond.TakeProfitProtectionSpread.Type != UnitTypes.Absolute & cond.TakeProfitProtectionSpread.Type != UnitTypes.Percent))
		{
			return false;
		}

		return true;
	}

	public static OrderStates ToStockSharpState(this TransaqOrderStatus status)
	{
        return status switch
        {
            //case TransaqOrderStatus.none:
            //	return OrderStates.None;
            TransaqOrderStatus.active or TransaqOrderStatus.wait or
			TransaqOrderStatus.linkwait or TransaqOrderStatus.watching or
			TransaqOrderStatus.sl_guardtime or TransaqOrderStatus.tp_guardtime or
			TransaqOrderStatus.tp_correction or TransaqOrderStatus.tp_correction_guardtime
				=> OrderStates.Active,

            TransaqOrderStatus.forwarding or TransaqOrderStatus.sl_forwarding or
			TransaqOrderStatus.tp_forwarding => OrderStates.Pending,
            TransaqOrderStatus.rejected or TransaqOrderStatus.refused or
			TransaqOrderStatus.failed or TransaqOrderStatus.denied or
			TransaqOrderStatus.removed
				=> OrderStates.Failed,

            TransaqOrderStatus.expired or TransaqOrderStatus.disabled or
			TransaqOrderStatus.cancelled or TransaqOrderStatus.matched or
			TransaqOrderStatus.sl_executed or TransaqOrderStatus.tp_executed
				=> OrderStates.Done,

            _ => OrderStates.None,
        };
    }

	public static SecurityTypes? FromTransaq(this string type)
	{
        return type.ToUpperInvariant() switch
        {
            "SHARE" => (SecurityTypes?)SecurityTypes.Stock,
            "FUT" or "FOB" or "NYSE" => (SecurityTypes?)SecurityTypes.Future,
            "ADR" => (SecurityTypes?)SecurityTypes.Adr,
            "QUOTES" => (SecurityTypes?)SecurityTypes.Indicator,
            "OPT" => (SecurityTypes?)SecurityTypes.Option,
            "IDX" => (SecurityTypes?)SecurityTypes.Index,
            "GKO" or "BOND" => (SecurityTypes?)SecurityTypes.Bond,
            "ETS_SWAP" => (SecurityTypes?)SecurityTypes.Swap,
            "ETS_CURRENCY" or "CURRENCY" or "MCT" => (SecurityTypes?)SecurityTypes.Currency,
            "OIL" or "METAL" => (SecurityTypes?)SecurityTypes.Commodity,
            "ERROR" => throw new ArgumentOutOfRangeException(nameof(type), type, LocalizedStrings.InvalidValue),
            _ => null,
        };
    }

	public static OptionTypes FromTransaq(this SecInfoPutCalls type)
	{
        return type switch
        {
            SecInfoPutCalls.C => OptionTypes.Call,
            SecInfoPutCalls.P => OptionTypes.Put,
            _ => throw new ArgumentOutOfRangeException(nameof(type), type, LocalizedStrings.InvalidValue),
        };
    }

	public static SecurityStates? FromTransaq(this TransaqSecurityStatus status)
	{
        return status switch
        {
            TransaqSecurityStatus.A => (SecurityStates?)SecurityStates.Trading,
            TransaqSecurityStatus.S => (SecurityStates?)SecurityStates.Stoped,
            TransaqSecurityStatus.N or TransaqSecurityStatus.undefined => null,
            _ => throw new ArgumentOutOfRangeException(nameof(status), status, LocalizedStrings.InvalidValue),
        };
    }

	public static CurrencyTypes? ToCurrency(this string name, Action<Exception> errorHandler)
	{
		if (name.IsEmpty() || name.EqualsIgnoreCase("NA"))
			return null;
		// http://stocksharp.com/forum/yaf_postst5355_API-4-2-25---Oshibka-i-zamiechaniia.aspx
		else if (name.EqualsIgnoreCase("RURC"))
			return CurrencyTypes.RUB;
		else
			return name.FromMicexCurrencyName(errorHandler);
	}

	public static TPlusLimits? ToPositionLimit(this string register, Action<Exception> errorHandler)
	{
		var type = register?.ToLowerInvariant();

		switch (type)
		{
			case null:
				return null;

			case "c":
			case "t0":
				return TPlusLimits.T0;

			case "y1":
			case "t1":
				return TPlusLimits.T1;

			case "y2":
			case "t2":
				return TPlusLimits.T2;

			case "y3":
				return TPlusLimits.Tx;

			default:
				errorHandler?.Invoke(new ArgumentOutOfRangeException(nameof(type), type, LocalizedStrings.InvalidValue));
				return null;
		}
	}
}