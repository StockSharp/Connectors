namespace StockSharp.Saxo.Native;

static class SaxoExtensions
{
	private static readonly TimeSpan[] _timeFrames =
	[
		TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(2), TimeSpan.FromMinutes(3), TimeSpan.FromMinutes(5),
		TimeSpan.FromMinutes(10), TimeSpan.FromMinutes(15), TimeSpan.FromMinutes(30), TimeSpan.FromHours(1),
		TimeSpan.FromHours(2), TimeSpan.FromHours(3), TimeSpan.FromHours(4), TimeSpan.FromHours(5),
		TimeSpan.FromHours(6), TimeSpan.FromHours(8), TimeSpan.FromDays(1), TimeSpan.FromDays(7),
		TimeSpan.FromDays(30), TimeSpan.FromDays(90), TimeSpan.FromDays(360),
	];

	public static IEnumerable<TimeSpan> TimeFrames => _timeFrames;

	public static SaxoInstrument ToInstrument(this SaxoInstrumentDetails details)
	{
		if (details == null)
			return null;
		var instrument = new SaxoInstrument
		{
			Uic = details.Uic,
			AssetType = details.AssetType,
			Symbol = details.Symbol,
			Description = details.Description,
			Currency = details.CurrencyCode,
			Exchange = details.Exchange?.ExchangeId,
			ExpiryDate = details.ExpiryDate?.UtcKind(),
			Strike = details.StrikePrice,
			PutCall = details.PutCall,
			Multiplier = details.ContractSize,
			PriceStep = details.TickSizeLimitOrder ?? details.TickSize ?? ToPriceStep(details.Format?.OrderDecimals ?? details.Format?.Decimals),
			SupportedOrderTypeSettings = details.SupportedOrderTypeSettings ?? [],
		};
		return instrument;
	}

	public static SecurityId ToSecurityId(this SaxoInstrument instrument)
		=> new()
		{
			SecurityCode = instrument.Symbol.IsEmpty(instrument.Uic.ToString(CultureInfo.InvariantCulture)),
			BoardCode = instrument.Exchange.IsEmpty("SAXO"),
			Native = $"{instrument.Uic.ToString(CultureInfo.InvariantCulture)}|{instrument.AssetType}",
		};

	public static long ToUic(this SecurityId securityId)
	{
		var uic = securityId.Native switch
		{
			long value => value,
			int value => value,
			decimal value when value <= long.MaxValue => (long)value,
			string value when long.TryParse(value.Split('|')[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) => parsed,
			_ => 0,
		};
		if (uic <= 0 && !long.TryParse(securityId.SecurityCode, NumberStyles.Integer, CultureInfo.InvariantCulture, out uic))
			throw new InvalidOperationException("Saxo security requires a numeric UIC in SecurityId.Native or SecurityCode.");
		return uic;
	}

	public static string ToSaxoAssetType(this SecurityId securityId)
	{
		var native = securityId.Native as string;
		if (native.IsEmpty())
			return null;
		var parts = native.Split('|');
		return parts.Length > 1 ? parts[1] : null;
	}

	public static SecurityTypes ToSecurityType(this string assetType)
		=> assetType?.ToUpperInvariant() switch
		{
			"STOCK" or "SRDONSTOCK" => SecurityTypes.Stock,
			"ETF" or "ETN" => SecurityTypes.Etf,
			"MUTUALFUND" => SecurityTypes.Fund,
			"CONTRACTFUTURES" => SecurityTypes.Future,
			"STOCKOPTION" or "STOCKINDEXOPTION" or "FUTURESOPTION" or "FXVANILLAOPTION" => SecurityTypes.Option,
			"FXSPOT" => SecurityTypes.Currency,
			"FXFORWARDS" => SecurityTypes.Forward,
			"FXSWAP" => SecurityTypes.Swap,
			"BOND" => SecurityTypes.Bond,
			"WARRANT" or "CERTIFICATEBONUS" or "CERTIFICATEEXPRESS" or "CERTIFICATETRACKER" or
				"MINIFUTURE" or "TURBO" or "WARRANTKNOCKOUT" => SecurityTypes.Warrant,
			"STOCKINDEX" => SecurityTypes.Index,
			_ when assetType?.StartsWithIgnoreCase("Cfd") == true => SecurityTypes.Cfd,
			_ => SecurityTypes.Stock,
		};

	public static string ToSaxoAssetTypes(this IEnumerable<SecurityTypes> securityTypes)
	{
		var values = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		foreach (var securityType in securityTypes)
		{
			foreach (var value in securityType switch
			{
				SecurityTypes.Stock => new[] { "Stock" },
				SecurityTypes.Etf => ["Etf", "Etn"],
				SecurityTypes.Fund => ["MutualFund"],
				SecurityTypes.Future => ["ContractFutures"],
				SecurityTypes.Option => ["StockOption", "StockIndexOption", "FuturesOption", "FxVanillaOption"],
				SecurityTypes.Currency => ["FxSpot"],
				SecurityTypes.Forward => ["FxForwards"],
				SecurityTypes.Swap => ["FxSwap"],
				SecurityTypes.Bond => ["Bond"],
				SecurityTypes.Warrant => ["Warrant", "WarrantKnockOut", "Turbo", "MiniFuture"],
				SecurityTypes.Index => ["StockIndex"],
				SecurityTypes.Cfd => ["CfdOnStock", "CfdOnIndex", "CfdOnFutures", "CfdOnEtf", "CfdOnEtc"],
				_ => [],
			})
				values.Add(value);
		}
		return values.JoinComma();
	}

	public static string ToSaxoOrderType(this OrderTypes orderType, decimal price, SaxoOrderCondition condition)
	{
		if (condition?.TrailingDistance is > 0)
			return "TrailingStopIfTraded";
		if (orderType == OrderTypes.Conditional)
			return condition?.StopPrice is > 0 && price > 0 ? "StopLimit" : "StopIfTraded";
		return orderType == OrderTypes.Market ? "Market" : "Limit";
	}

	public static OrderTypes ToOrderType(this string orderType)
		=> orderType?.ToUpperInvariant() switch
		{
			"MARKET" => OrderTypes.Market,
			"STOP" or "STOPIFBID" or "STOPIFOFFERED" or "STOPIFTRADED" or "STOPLIMIT" or "TRAILINGSTOP" or
				"TRAILINGSTOPIFBID" or "TRAILINGSTOPIFOFFERED" or "TRAILINGSTOPIFTRADED" => OrderTypes.Conditional,
			_ => OrderTypes.Limit,
		};

	public static SaxoOrderDuration ToSaxoDuration(this TimeInForce? timeInForce, DateTimeOffset? tillDate,
		SaxoOrderCondition condition)
	{
		if (tillDate is not null)
		{
			return new()
			{
				DurationType = "GoodTillDate",
				ExpirationDateContainsTime = true,
				ExpirationDateTime = tillDate.Value.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ss'Z'", CultureInfo.InvariantCulture),
			};
		}
		var duration = timeInForce switch
		{
			TimeInForce.CancelBalance => SaxoOrderDurations.ImmediateOrCancel,
			TimeInForce.MatchOrCancel => SaxoOrderDurations.FillOrKill,
			_ => condition?.Duration ?? SaxoOrderDurations.Day,
		};
		return new()
		{
			DurationType = duration switch
			{
				SaxoOrderDurations.GoodTillCancel => "GoodTillCancel",
				SaxoOrderDurations.ImmediateOrCancel => "ImmediateOrCancel",
				SaxoOrderDurations.FillOrKill => "FillOrKill",
				_ => "DayOrder",
			},
		};
	}

	public static TimeInForce ToTimeInForce(this SaxoOrderDuration duration)
		=> duration?.DurationType?.ToUpperInvariant() switch
		{
			"IMMEDIATEORCANCEL" => TimeInForce.CancelBalance,
			"FILLORKILL" => TimeInForce.MatchOrCancel,
			_ => TimeInForce.PutInQueue,
		};

	public static OrderStates ToOrderState(this string status, string subStatus = null)
	{
		if (subStatus.EqualsIgnoreCase("Rejected"))
			return status.EqualsIgnoreCase("Cancelled") || status.EqualsIgnoreCase("Changed")
				? OrderStates.Active : OrderStates.Failed;
		return status?.Replace("_", string.Empty, StringComparison.Ordinal).ToUpperInvariant() switch
		{
			"CANCELLED" or "EXPIRED" or "FINALFILL" or "DONEFORDAY" => OrderStates.Done,
			"REJECTED" => OrderStates.Failed,
			"PLACED" or "CHANGED" or "WORKING" or "FILL" or "PARKED" => OrderStates.Active,
			_ => OrderStates.Pending,
		};
	}

	public static Sides ToSide(this string side)
		=> side.EqualsIgnoreCase("Buy") ? Sides.Buy : Sides.Sell;

	public static string ToSaxoSide(this Sides side)
		=> side == Sides.Buy ? "Buy" : "Sell";

	public static int ToSaxoHorizon(this TimeSpan timeFrame)
	{
		var minutes = timeFrame.TotalMinutes;
		if (minutes is 1 or 2 or 3 or 5 or 10 or 15 or 30 or 60 or 120 or 180 or 240 or 300 or 360 or 480 or
			1440 or 10080 or 43200 or 129600 or 518400)
			return checked((int)minutes);
		throw new ArgumentOutOfRangeException(nameof(timeFrame), timeFrame, "Unsupported Saxo chart horizon.");
	}

	public static decimal? Mid(decimal? bid, decimal? ask)
		=> bid is not null && ask is not null ? (bid.Value + ask.Value) / 2m : bid ?? ask;

	public static decimal? Open(this SaxoChartSample sample)
		=> sample.Open ?? Mid(sample.OpenBid, sample.OpenAsk);

	public static decimal? High(this SaxoChartSample sample)
		=> sample.High ?? Mid(sample.HighBid, sample.HighAsk);

	public static decimal? Low(this SaxoChartSample sample)
		=> sample.Low ?? Mid(sample.LowBid, sample.LowAsk);

	public static decimal? Close(this SaxoChartSample sample)
		=> sample.Close ?? Mid(sample.CloseBid, sample.CloseAsk);

	public static SaxoInfoPrice Apply(this SaxoInfoPrice target, SaxoInfoPrice update)
	{
		if (target == null)
			return update;
		if (update == null)
			return target;
		if (update.Uic != 0)
			target.Uic = update.Uic;
		if (!update.AssetType.IsEmpty())
			target.AssetType = update.AssetType;
		target.LastUpdated = update.LastUpdated ?? target.LastUpdated;
		target.Quote = Apply(target.Quote, update.Quote);
		target.PriceInfo = Apply(target.PriceInfo, update.PriceInfo);
		target.PriceInfoDetails = Apply(target.PriceInfoDetails, update.PriceInfoDetails);
		target.MarketDepth = Apply(target.MarketDepth, update.MarketDepth);
		return target;
	}

	public static SaxoChartSample Apply(this SaxoChartSample target, SaxoChartSample update)
	{
		if (target == null)
			return update;
		if (update == null)
			return target;
		if (update.Time != default)
			target.Time = update.Time;
		target.Open = update.Open ?? target.Open;
		target.High = update.High ?? target.High;
		target.Low = update.Low ?? target.Low;
		target.Close = update.Close ?? target.Close;
		target.OpenAsk = update.OpenAsk ?? target.OpenAsk;
		target.HighAsk = update.HighAsk ?? target.HighAsk;
		target.LowAsk = update.LowAsk ?? target.LowAsk;
		target.CloseAsk = update.CloseAsk ?? target.CloseAsk;
		target.OpenBid = update.OpenBid ?? target.OpenBid;
		target.HighBid = update.HighBid ?? target.HighBid;
		target.LowBid = update.LowBid ?? target.LowBid;
		target.CloseBid = update.CloseBid ?? target.CloseBid;
		target.Volume = update.Volume ?? target.Volume;
		target.Interest = update.Interest ?? target.Interest;
		return target;
	}

	public static SaxoBalance Apply(this SaxoBalance target, SaxoBalance update)
	{
		if (target == null)
			return update;
		if (update == null)
			return target;
		if (!update.AccountKey.IsEmpty())
			target.AccountKey = update.AccountKey;
		if (!update.Currency.IsEmpty())
			target.Currency = update.Currency;
		target.CashBalance = update.CashBalance ?? target.CashBalance;
		target.TotalValue = update.TotalValue ?? target.TotalValue;
		target.NetEquityForMargin = update.NetEquityForMargin ?? target.NetEquityForMargin;
		target.MarginAvailableForTrading = update.MarginAvailableForTrading ?? target.MarginAvailableForTrading;
		target.MarginUsedByCurrentPositions = update.MarginUsedByCurrentPositions ?? target.MarginUsedByCurrentPositions;
		target.UnrealizedMarginProfitLoss = update.UnrealizedMarginProfitLoss ?? target.UnrealizedMarginProfitLoss;
		target.UnrealizedPositionsValue = update.UnrealizedPositionsValue ?? target.UnrealizedPositionsValue;
		if (!update.CalculationReliability.IsEmpty())
			target.CalculationReliability = update.CalculationReliability;
		return target;
	}

	public static SaxoNetPosition Apply(this SaxoNetPosition target, SaxoNetPosition update)
	{
		if (target == null)
			return update;
		if (update == null)
			return target;
		if (!update.NetPositionId.IsEmpty())
			target.NetPositionId = update.NetPositionId;
		target.NetPositionBase = Apply(target.NetPositionBase, update.NetPositionBase);
		target.NetPositionView = Apply(target.NetPositionView, update.NetPositionView);
		return target;
	}

	private static SaxoQuote Apply(SaxoQuote target, SaxoQuote update)
	{
		if (target == null)
			return update;
		if (update == null)
			return target;
		target.Ask = update.Ask ?? target.Ask;
		target.AskSize = update.AskSize ?? target.AskSize;
		target.Bid = update.Bid ?? target.Bid;
		target.BidSize = update.BidSize ?? target.BidSize;
		target.Mid = update.Mid ?? target.Mid;
		target.DelayedByMinutes = update.DelayedByMinutes ?? target.DelayedByMinutes;
		return target;
	}

	private static SaxoPriceInfo Apply(SaxoPriceInfo target, SaxoPriceInfo update)
	{
		if (target == null)
			return update;
		if (update == null)
			return target;
		target.High = update.High ?? target.High;
		target.Low = update.Low ?? target.Low;
		target.NetChange = update.NetChange ?? target.NetChange;
		target.PercentChange = update.PercentChange ?? target.PercentChange;
		return target;
	}

	private static SaxoPriceInfoDetails Apply(SaxoPriceInfoDetails target, SaxoPriceInfoDetails update)
	{
		if (target == null)
			return update;
		if (update == null)
			return target;
		target.Open = update.Open ?? target.Open;
		target.LastClose = update.LastClose ?? target.LastClose;
		target.LastTraded = update.LastTraded ?? target.LastTraded;
		target.LastTradedSize = update.LastTradedSize ?? target.LastTradedSize;
		target.Volume = update.Volume ?? target.Volume;
		target.OpenInterest = update.OpenInterest ?? target.OpenInterest;
		return target;
	}

	private static SaxoMarketDepth Apply(SaxoMarketDepth target, SaxoMarketDepth update)
	{
		if (target == null)
			return update;
		if (update == null)
			return target;
		target.Ask = update.Ask ?? target.Ask;
		target.AskSize = update.AskSize ?? target.AskSize;
		target.AskOrders = update.AskOrders ?? target.AskOrders;
		target.Bid = update.Bid ?? target.Bid;
		target.BidSize = update.BidSize ?? target.BidSize;
		target.BidOrders = update.BidOrders ?? target.BidOrders;
		return target;
	}

	private static SaxoNetPositionBase Apply(SaxoNetPositionBase target, SaxoNetPositionBase update)
	{
		if (target == null)
			return update;
		if (update == null)
			return target;
		if (!update.AccountId.IsEmpty())
			target.AccountId = update.AccountId;
		target.Amount = update.Amount ?? target.Amount;
		target.AmountLong = update.AmountLong ?? target.AmountLong;
		target.AmountShort = update.AmountShort ?? target.AmountShort;
		if (!update.AssetType.IsEmpty())
			target.AssetType = update.AssetType;
		target.ExpiryDate = update.ExpiryDate ?? target.ExpiryDate;
		if (update.Uic != 0)
			target.Uic = update.Uic;
		return target;
	}

	private static SaxoNetPositionView Apply(SaxoNetPositionView target, SaxoNetPositionView update)
	{
		if (target == null)
			return update;
		if (update == null)
			return target;
		target.AverageOpenPrice = update.AverageOpenPrice ?? target.AverageOpenPrice;
		target.CurrentPrice = update.CurrentPrice ?? target.CurrentPrice;
		target.Exposure = update.Exposure ?? target.Exposure;
		target.ProfitLossOnTrade = update.ProfitLossOnTrade ?? target.ProfitLossOnTrade;
		if (!update.Status.IsEmpty())
			target.Status = update.Status;
		return target;
	}

	public static QuoteChange[] ToQuotes(decimal[] prices, decimal[] volumes, int[] orders, bool bids)
	{
		var count = Math.Min(prices?.Length ?? 0, volumes?.Length ?? 0);
		var result = new List<QuoteChange>(count);
		for (var i = 0; i < count; i++)
		{
			if (prices[i] <= 0)
				continue;
			result.Add(new(prices[i], volumes[i])
			{
				OrdersCount = orders != null && i < orders.Length ? orders[i] : null,
			});
		}
		return [.. (bids ? result.OrderByDescending(q => q.Price) : result.OrderBy(q => q.Price))];
	}

	private static decimal? ToPriceStep(int? decimals)
	{
		if (decimals is null or < 0 or > 12)
			return null;
		var value = 1m;
		for (var i = 0; i < decimals; i++)
			value /= 10m;
		return value;
	}
}
