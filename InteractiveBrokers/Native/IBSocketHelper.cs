namespace StockSharp.InteractiveBrokers.Native;

static class IBSocketHelper
{
	private static readonly SynchronizedPairSet<Sides, string> _sides = new()
	{
		{ Sides.Buy, "BUY" },
		{ Sides.Sell, "SELL" },
	};

	private static readonly SynchronizedPairSet<InteractiveBrokersOrderCondition.ClearingIntents, string> _intents = [];
	private static readonly SynchronizedPairSet<InteractiveBrokersOrderCondition.AgentDescriptions, string> _agents = [];
	private static readonly SynchronizedPairSet<InteractiveBrokersOrderCondition.FinancialAdvisorAllocations, string> _financialAdvisors = [];
	private static readonly SynchronizedPairSet<FundamentalReports, string> _fundamentals = [];
	private static readonly SynchronizedPairSet<AccountSummaryTag, string> _accountTags = [];
	private static readonly SynchronizedPairSet<ScannerFilterTypes, string> _scanCodes = [];

	private const string _yyyyMMddFormat = "yyyyMMdd";

	private static void FillMap<T>(SynchronizedPairSet<T, string> map)
	{
		if (map == null)
			throw new ArgumentNullException(nameof(map));

		foreach (var name in typeof(T).GetNames())
		{
			var field = typeof(T).GetField(name);
			map.Add((T)field.GetValue(null), field.GetAttribute<NativeValueAttribute>().Value);
		}
	}

	static IBSocketHelper()
	{
		FillMap(_intents);
		FillMap(_agents);
		FillMap(_financialAdvisors);
		FillMap(_fundamentals);
		FillMap(_accountTags);
		FillMap(_scanCodes);
	}

	public static IBSocket SendMessage(this IBSocket socket, RequestMessages message)
	{
		if (socket.UseV100Plus)
			socket.ClearSendBuffer();

		return socket.Send((int)message);
	}

	public static IBSocket SendVersion(this IBSocket socket, ServerVersions version)
	{
		return socket.Send((int)version);
	}

	public static IBSocket SendIfLess(this IBSocket socket, ServerVersions minVersion, Action<IBSocket> handler)
	{
		if (handler == null)
			throw new ArgumentNullException(nameof(handler));

		if (socket.ServerVersion < minVersion)
			handler(socket);

		return socket;
	}

	public static IBSocket SendIfEqualOrMore(this IBSocket socket, ServerVersions minVersion, Action<IBSocket> handler)
	{
		if (handler == null)
			throw new ArgumentNullException(nameof(handler));

		if (socket.ServerVersion >= minVersion)
			handler(socket);

		return socket;
	}

	public static IBSocket SendSecurityType(this IBSocket socket, SecurityTypes? securityType, SecurityTypes? underlyingSecurityType)
	{
		//if (securityType == null)
		//	return socket.Send(string.Empty);

		switch (securityType)
		{
			case SecurityTypes.Stock:
				return socket.Send("STK");
			case SecurityTypes.Future:
				return socket.Send("FUT");
			case SecurityTypes.Option:
			{
				switch (underlyingSecurityType)
				{
					case SecurityTypes.Future:
						return socket.Send("FOP");
					default:
						return socket.Send("OPT");
				}
			}
			case SecurityTypes.Index:
				return socket.Send("IND");
			case SecurityTypes.Currency:
				return socket.Send("CASH");
			case SecurityTypes.Bond:
				return socket.Send("BOND");
			case SecurityTypes.Warrant:
				return socket.Send("WAR");
			default:
				return socket.Send(string.Empty);
		}
	}

	public static IBSocket SendOptionType(this IBSocket socket, OptionTypes? type)
	{
		return socket.Send(type == null
			            ? string.Empty
						: type == OptionTypes.Call ? "C" : "P");
	}

	public static IBSocket SendCurrency(this IBSocket socket, CurrencyTypes? currency)
	{
		return socket.Send(currency.To<string>());
	}

	public static IBSocket SendContractId(this IBSocket socket, SecurityId id)
	{
		return socket.Send(id.InteractiveBrokers ?? 0);
	}

	public static IBSocket SendStrike(this IBSocket socket, decimal? strike)
	{
		return socket.Send(strike ?? 0);
	}

	public static IBSocket SendSecurityId(this IBSocket socket, SecurityId id)
	{
		if (!id.Cusip.IsEmpty())
		{
			socket.Send("CUSIP");
			socket.Send(id.Cusip);
		}
		else if (!id.Isin.IsEmpty())
		{
			socket.Send("ISIN");
			socket.Send(id.Isin);
		}
		else if (!id.Sedol.IsEmpty())
		{
			socket.Send("SEDOL");
			socket.Send(id.Sedol);
		}
		else if (!id.Ric.IsEmpty())
		{
			socket.Send("RIC");
			socket.Send(id.Ric);
		}
		else
		{
			socket.Send(string.Empty);
			socket.Send(string.Empty);
		}

		return socket;
	}

	public static IBSocket SendOrderSide(this IBSocket socket, Sides side)
	{
		return socket.Send(_sides[side]);
	}

	public static IBSocket SendTradeSide(this IBSocket socket, Sides? side)
	{
		if (side == null)
			return socket.Send(string.Empty);

		return socket.Send(_sides[side.Value]);
	}

	public static IBSocket SendOrderType(this IBSocket socket, OrderTypes orderType, InteractiveBrokersOrderCondition.ExtendedOrderTypes? extendedOrderType)
	{
		switch (orderType)
		{
			case OrderTypes.Limit:
				return socket.Send("LMT");
			case OrderTypes.Market:
				return socket.Send("MKT");
			case OrderTypes.Conditional:
			{
				if (extendedOrderType == null)
					return socket.Send(string.Empty);

				return extendedOrderType switch
				{
					InteractiveBrokersOrderCondition.ExtendedOrderTypes.MarketOnClose => socket.Send("MOC"),
					InteractiveBrokersOrderCondition.ExtendedOrderTypes.LimitOnClose => socket.Send("LMTCLS"),
					InteractiveBrokersOrderCondition.ExtendedOrderTypes.PeggedToMarket => socket.Send("PEGMKT"),
					InteractiveBrokersOrderCondition.ExtendedOrderTypes.Stop => socket.Send("STP"),
					InteractiveBrokersOrderCondition.ExtendedOrderTypes.StopLimit => socket.Send("STP LMT"),
					InteractiveBrokersOrderCondition.ExtendedOrderTypes.TrailingStop => socket.Send("TRAIL"),
					InteractiveBrokersOrderCondition.ExtendedOrderTypes.Relative => socket.Send("REL"),
					InteractiveBrokersOrderCondition.ExtendedOrderTypes.VolumeWeightedAveragePrice => socket.Send("VWAP"),
					InteractiveBrokersOrderCondition.ExtendedOrderTypes.TrailingStopLimit => socket.Send("TRAILLIMIT"),
					InteractiveBrokersOrderCondition.ExtendedOrderTypes.Volatility => socket.Send("VOL"),
					InteractiveBrokersOrderCondition.ExtendedOrderTypes.None => socket.Send("NONE"),
					InteractiveBrokersOrderCondition.ExtendedOrderTypes.Default => socket.Send("Default"),
					InteractiveBrokersOrderCondition.ExtendedOrderTypes.Scale => socket.Send("SCALE"),
					InteractiveBrokersOrderCondition.ExtendedOrderTypes.MarketIfTouched => socket.Send("MIT"),
					InteractiveBrokersOrderCondition.ExtendedOrderTypes.LimitIfTouched => socket.Send("LIT"),
					_ => throw new ArgumentOutOfRangeException(nameof(extendedOrderType), extendedOrderType, LocalizedStrings.InvalidValue),
				};
			}
			default:
				throw new ArgumentOutOfRangeException(nameof(orderType), orderType, LocalizedStrings.InvalidValue);
		}
	}

	public static IBSocket SendOrderExpiration(this IBSocket socket, OrderRegisterMessage msg)
	{
		if (msg == null)
			throw new ArgumentNullException(nameof(msg));

		if (msg.OrderType != OrderTypes.Conditional)
		{
			switch (msg.TimeInForce)
			{
				case TimeInForce.PutInQueue:
				case null:
				{
					if (msg.TillDate == null)
						return socket.Send("GTC");
					else if (msg.TillDate.Value.IsToday())
						return socket.Send("GTD");
					else
						return socket.Send("DAY");
				}
				case TimeInForce.MatchOrCancel:
					return socket.Send("FOK");
				case TimeInForce.CancelBalance:
					return socket.Send("IOC");
				default:
					throw new ArgumentOutOfRangeException();
			}
		}
		else if (msg.OrderType == OrderTypes.Conditional)
		{
			var ibCon = (InteractiveBrokersOrderCondition)msg.Condition;

			return socket.Send(ibCon.IsMarketOnOpen == true ? "OPG" : "DAY");
		}
		else
			throw new ArgumentException(LocalizedStrings.UnsupportedType.Put(msg.Type), nameof(msg));
	}

	public static IBSocket SendFinancialAdvisor(this IBSocket socket, InteractiveBrokersOrderCondition.FinancialAdvisorAllocations? allocation)
	{
		return socket.Send(allocation is null ? string.Empty : _financialAdvisors[allocation.Value]);
	}

	public static IBSocket SendAgent(this IBSocket socket, InteractiveBrokersOrderCondition.AgentDescriptions? agent)
	{
		return socket.Send(agent is null ? string.Empty : _agents[agent.Value]);
	}

	public static IBSocket SendIntent(this IBSocket socket, InteractiveBrokersOrderCondition.ClearingIntents? intent)
	{
		return socket.Send(intent is null ? string.Empty : _intents[intent.Value]);
	}

	public static IBSocket SendLevel1Field(this IBSocket socket, Level1Fields field)
	{
        return field switch
        {
            CandleDataTypes.Trades => socket.Send("TRADES"),
            CandleDataTypes.Midpoint => socket.Send("MIDPOINT"),
            CandleDataTypes.Bid => socket.Send("BID"),
            CandleDataTypes.Ask => socket.Send("ASK"),
            CandleDataTypes.BidAsk => socket.Send("BID_ASK"),
            CandleDataTypes.AdjustedLast => socket.Send("ADJUSTED_LAST"),
            CandleDataTypes.HistoricalVolatility => socket.Send("HISTORICAL_VOLATILITY"),
            CandleDataTypes.ImpliedVolatility => socket.Send("OPTION_IMPLIED_VOLATILITY"),
            CandleDataTypes.RebateRate => socket.Send("REBATE_RATE"),
            CandleDataTypes.FeeRate => socket.Send("FEE_RATE"),
            CandleDataTypes.YieldAsk => socket.Send("YIELD_ASK"),
            CandleDataTypes.YieldBid => socket.Send("YIELD_BID"),
            CandleDataTypes.YieldBidAsk => socket.Send("YIELD_BID_ASK"),
            CandleDataTypes.YieldLast => socket.Send("YIELD_LAST"),
            _ => throw new ArgumentOutOfRangeException(nameof(field), field, LocalizedStrings.InvalidValue),
        };
    }

	public static IBSocket SendFundamental(this IBSocket socket, FundamentalReports? report)
	{
		return socket.Send(report is null ? string.Empty : _fundamentals[report.Value]);
	}

	public static IBSocket SendAccountTags(this IBSocket socket, IEnumerable<AccountSummaryTag> tags)
	{
		return socket.Send(tags.Select(t => _accountTags[t]).JoinComma());
	}

	public static IBSocket SendScannerCode(this IBSocket socket, ScannerFilterTypes? scanCode)
	{
		return socket.Send(scanCode is null ? string.Empty : _scanCodes[scanCode.Value]);
	}

	public static IBSocket SendTagsNoCount(this IBSocket socket, IEnumerable<Tuple<string, string>> tags)
	{
		if (tags == null)
			throw new ArgumentNullException(nameof(tags));

		return socket
			.Send(tags.Select(t => $"{t.Item1}={t.Item2}").JoinDotComma());
	}

	public static IBSocket SendComboLeg(this IBSocket socket, SecurityId innerId, decimal weight)
		=> 
			socket
				.SendContractId(innerId)
				.Send((int)weight.Abs())
				.SendOrderSide(weight >= 0 ? Sides.Buy : Sides.Sell)
				.Send(innerId.BoardCode);

	public static IBSocket SendCombo(this IBSocket socket, SecurityMessage secMsg)
	{
		var combo = secMsg.ToCombo();

		socket.Send(combo.Count);

		foreach (var pair in combo)
		{
			var innerId = pair.Key;
			var weight = pair.Value;

			socket.SendComboLeg(innerId, weight);
		}

		return socket;
	}

	public static IBSocket SendIncludeExpired(this IBSocket socket, DateTime? expiryDate)
	{
		return socket.Send(expiryDate != null && expiryDate < DateTime.UtcNow);
	}

	public static async ValueTask<ServerVersions> ReadVersionAsync(this IBSocket socket, CancellationToken cancellationToken)
	{
		var v = await socket.ReadIntAsync(cancellationToken).NoWait();
		return (ServerVersions)v;
	}

	public static async ValueTask<OptionTypes?> ReadOptionTypeAsync(this IBSocket socket, CancellationToken cancellationToken)
	{
		var str = await socket.ReadStringAsync(cancellationToken).NoWait();

		if (str.IsEmpty())
			return null;

        return str switch
        {
            "?" or "0" => null,
            "C" => (OptionTypes?)OptionTypes.Call,
            "P" => (OptionTypes?)OptionTypes.Put,
            _ => throw new InvalidOperationException(str),
        };
    }

	public static async ValueTask<Sides?> ReadTradeSideAsync(this IBSocket socket, CancellationToken cancellationToken)
	{
        return (await socket.ReadStringAsync(cancellationToken).NoWait()) switch
        {
            "BOT" => (Sides?)Sides.Buy,
            "SLD" => (Sides?)Sides.Sell,
            _ => null,
        };
    }

	public static SecurityTypes? ToSecurityType(this string str, ILogReceiver logs, out SecurityTypes? underlyingSecurityType)
	{
		if (logs is null)
			throw new ArgumentNullException(nameof(logs));

		underlyingSecurityType = null;

		if (str.IsEmpty())
			return null;

		switch (str)
		{
			case "STK":
			case "SLB": // short stock
				return SecurityTypes.Stock;
			case "FUT":
			case "ICU":
			case "ICS":
				return SecurityTypes.Future;
			case "FOP": // option on fut
				underlyingSecurityType = SecurityTypes.Future;
				return SecurityTypes.Option;
			case "OPT":
				return SecurityTypes.Option;
			case "IND":
			case "BAG":
			case "BSK":
				return SecurityTypes.Index;
			case "CASH":
				return SecurityTypes.Currency;
			case "BOND":
			case "BILL":
			case "FIXED":
				return SecurityTypes.Bond;
			case "FUND":
				return SecurityTypes.Fund;
			case "WAR":
			case "IOPT": // dutch warrants
				return SecurityTypes.Warrant;
			case "CFD":
				return SecurityTypes.Cfd;
			case "FWD":
				return SecurityTypes.Forward;
			case "NEWS":
				return SecurityTypes.News;
			case "CMDTY":
				return SecurityTypes.Commodity;
			case "CRYPTO":
				return SecurityTypes.CryptoCurrency;
			case "UNK":
				return null;
			default:
				logs.AddWarningLog(LocalizedStrings.UnknownType.Put(str));
				return null;
		}
	}

	public static OrderStatus ToOrderStatus(this string str)
	{
        return str switch
        {
            "PendingSubmit" => OrderStatus.SentToServer,
            "PendingCancel" => OrderStatus.SentToCanceled,
            "PreSubmitted" => OrderStatus.ReceiveByServer,
            "Submitted" => OrderStatus.Accepted,
            "Cancelled" => OrderStatus.Cancelled,
            "Filled" => OrderStatus.Matched,
            "Inactive" => OrderStatus.GateError,
            _ => throw new ArgumentOutOfRangeException(nameof(str), str, LocalizedStrings.InvalidValue),
        };
    }

	public static async ValueTask<OrderStatus> ReadOrderStatusAsync(this IBSocket socket, CancellationToken cancellationToken)
	{
		var str = await socket.ReadStringAsync(cancellationToken).NoWait();
		return str.ToOrderStatus();
	}

	public static async ValueTask<(OrderTypes type, InteractiveBrokersOrderCondition.ExtendedOrderTypes? extendedType)> ReadOrderTypeAsync(this IBSocket socket, CancellationToken cancellationToken)
	{
		InteractiveBrokersOrderCondition.ExtendedOrderTypes? extendedType = null;
		var str = (await socket.ReadStringAsync(cancellationToken).NoWait()).ToUpperInvariant();
		OrderTypes type;

		switch (str)
		{
			case "":
				type = OrderTypes.Conditional;
				break;
			case "LMT":
				type = OrderTypes.Limit;
				break;
			case "MKT":
				type = OrderTypes.Market;
				break;
			case "MOC":
				type = OrderTypes.Conditional;
				extendedType = InteractiveBrokersOrderCondition.ExtendedOrderTypes.MarketOnClose;
				break;
			case "LMTCLS":
				type = OrderTypes.Conditional;
				extendedType = InteractiveBrokersOrderCondition.ExtendedOrderTypes.LimitOnClose;
				break;
			case "PEGMKT":
				type = OrderTypes.Conditional;
				extendedType = InteractiveBrokersOrderCondition.ExtendedOrderTypes.PeggedToMarket;
				break;
			case "PEG BENCH":
				type = OrderTypes.Conditional;
				extendedType = InteractiveBrokersOrderCondition.ExtendedOrderTypes.PeggedBench;
				break;
			case "STP":
				type = OrderTypes.Conditional;
				extendedType = InteractiveBrokersOrderCondition.ExtendedOrderTypes.Stop;
				break;
			case "STP LMT":
				type = OrderTypes.Conditional;
				extendedType = InteractiveBrokersOrderCondition.ExtendedOrderTypes.StopLimit;
				break;
			case "TRAIL":
				type = OrderTypes.Conditional;
				extendedType = InteractiveBrokersOrderCondition.ExtendedOrderTypes.TrailingStop;
				break;
			case "REL":
				type = OrderTypes.Conditional;
				extendedType = InteractiveBrokersOrderCondition.ExtendedOrderTypes.Relative;
				break;
			case "VWAP":
				type = OrderTypes.Conditional;
				extendedType = InteractiveBrokersOrderCondition.ExtendedOrderTypes.VolumeWeightedAveragePrice;
				break;
			case "TRAILLIMIT":
				type = OrderTypes.Conditional;
				extendedType = InteractiveBrokersOrderCondition.ExtendedOrderTypes.TrailingStopLimit;
				break;
			case "VOL":
				type = OrderTypes.Conditional;
				extendedType = InteractiveBrokersOrderCondition.ExtendedOrderTypes.Volatility;
				break;
			case "NONE":
				type = OrderTypes.Conditional;
				extendedType = InteractiveBrokersOrderCondition.ExtendedOrderTypes.None;
				break;
			case "DEFAULT":
				type = OrderTypes.Conditional;
				extendedType = InteractiveBrokersOrderCondition.ExtendedOrderTypes.Default;
				break;
			case "SCALE":
				type = OrderTypes.Conditional;
				extendedType = InteractiveBrokersOrderCondition.ExtendedOrderTypes.Scale;
				break;
			case "MIT":
				type = OrderTypes.Conditional;
				extendedType = InteractiveBrokersOrderCondition.ExtendedOrderTypes.MarketIfTouched;
				break;
			case "LIT":
				type = OrderTypes.Conditional;
				extendedType = InteractiveBrokersOrderCondition.ExtendedOrderTypes.LimitIfTouched;
				break;
			default:
				throw new InvalidOperationException(LocalizedStrings.UnknownType.Put(str));
		}

		return (type, extendedType);
	}

	public static async ValueTask<InteractiveBrokersOrderCondition.VolatilityTimeFrames?> ReadVolatilityTypeAsync(this IBSocket socket, CancellationToken cancellationToken)
	{
		var str = await socket.ReadStringAsync(cancellationToken).NoWait();

		if (str.IsEmpty())
			return null;

		var value = str.To<int>();

		if (value == 0)
			return null;

		return (InteractiveBrokersOrderCondition.VolatilityTimeFrames)value;
	}

	public static async ValueTask<InteractiveBrokersOrderCondition.FinancialAdvisorAllocations?> ReadFinancialAdvisorAsync(this IBSocket socket, CancellationToken cancellationToken)
	{
		var str = await socket.ReadStringAsync(cancellationToken).NoWait();

		if (str.IsEmpty())
			return null;

		if (!_financialAdvisors.TryGetKey(str, out var allocation))
			throw new InvalidOperationException(LocalizedStrings.UnknownType.Put(str));

		return allocation;
	}

	public static async ValueTask<Sides> ReadOrderSideAsync(this IBSocket socket, CancellationToken cancellationToken)
	{
		var str = await socket.ReadStringAsync(cancellationToken).NoWait();

		if (!_sides.TryGetKey(str, out var side))
			throw new InvalidOperationException(LocalizedStrings.UnsupportedType.Put(str));

		return side;
	}

	public static async ValueTask<InteractiveBrokersOrderCondition.AgentDescriptions?> ReadAgentAsync(this IBSocket socket, CancellationToken cancellationToken)
	{
		var str = await socket.ReadStringAsync(cancellationToken).NoWait();

		if (str.IsEmpty() || str == "0")
			return null;

		if (!_agents.TryGetKey(str, out var agent))
			throw new InvalidOperationException(LocalizedStrings.UnknownType.Put(str));

		return agent;
	}

	public static async ValueTask ReadSecurityIdAsync(this IBSocket socket, SecurityId securityId, CancellationToken cancellationToken)
	{
		var count = await socket.ReadIntAsync(cancellationToken).NoWait();

		for (var i = 0; i < count; i++)
		{
			var idType = await socket.ReadStringAsync(cancellationToken).NoWait();

			switch (idType)
			{
				case "CUSIP":
					securityId.Cusip = await socket.ReadStringAsync(cancellationToken).NoWait();
					break;
				case "ISIN":
					securityId.Isin = await socket.ReadStringAsync(cancellationToken).NoWait();
					break;
				case "SEDOL":
					securityId.Sedol = await socket.ReadStringAsync(cancellationToken).NoWait();
					break;
				case "RIC":
					securityId.Ric = await socket.ReadStringAsync(cancellationToken).NoWait();
					break;
				default:
					throw new InvalidOperationException(LocalizedStrings.UnknownType.Put(idType));
			}
		}
	}

	public static async ValueTask<InteractiveBrokersOrderCondition.ClearingIntents> ReadIntentAsync(this IBSocket socket, CancellationToken cancellationToken)
	{
		var str = await socket.ReadStringAsync(cancellationToken).NoWait();

		if (!_intents.TryGetKey(str, out var intent))
			throw new InvalidOperationException(LocalizedStrings.UnknownType.Put(str));

		return intent;
	}

	public static AccountSummaryTag ToAccountTag(this string str)
	{
		if (!_accountTags.TryGetKey(str, out var tag))
			throw new InvalidOperationException(LocalizedStrings.UnknownType.Put(str));

		return tag;
	}

	public static async ValueTask<decimal?> ReadStrikeAsync(this IBSocket socket, CancellationToken cancellationToken)
	{
		var strike = await socket.ReadNullDecimalAsync(cancellationToken).NoWait();

		if (strike == 0)
			strike = null;

		return strike;
	}

	public static async ValueTask<DateTime> ReadTimeExAsync(this IBSocket socket, CancellationToken cancellationToken)
	{
		var timeStr = await socket.ReadStringAsync(cancellationToken).NoWait();

		if (long.TryParse(timeStr, out var timeNum))
		{
			return (timeNum > 202012120) ? timeNum.FromUnix() : timeNum.To<string>().ToDateTime(_yyyyMMddFormat).UtcKind();
		}

		if (timeStr.Contains(':'))
		{
			if (timeStr.Contains(' '))
				return timeStr.ToDateTime("yyyyMMdd HH\\:mm\\:ss").UtcKind();
			else
				return DateTime.UtcNow.Date + timeStr.ToTimeSpan("hh\\:mm\\:ss");
		}

		return timeStr.ToDateTime(_yyyyMMddFormat);
	}

	public static DateTime? ReadDateTime(this string str, ILogReceiver logs, out TimeZoneInfo timeZone)
	{
		timeZone = TimeZoneInfo.Local;

		if (str.IsNoDateTime())
			return null;

		try
		{
			var parts = str.SplitBySpace(false);

			if (parts.Length == 1)
			{
				return parts[0].ToDateTime(_yyyyMMddFormat).UtcKind();
			}

			try
			{
				var tz = parts.Skip(2).JoinSpace();

				var i = tz.IndexOf('+');

				if (i == -1)
					i = tz.IndexOf('-');

				if (i != -1 && tz.Contains(':'))
				{
					//var part1 = tz.Substring(0, i);
					var offset = tz.Substring(i + 1).To<TimeSpan>();

					if (tz.Contains('-'))
						offset = TimeSpan.Zero - offset;

					timeZone = TimeZoneInfo.GetSystemTimeZones().FirstOrDefault(tzi => tzi.BaseUtcOffset == offset);
				}

				timeZone ??= tz.To<TimeZoneInfo>();
			}
			catch (Exception ex)
			{
				logs.AddErrorLog(ex);
			}

			var format = parts[1].IndexOf(':') != parts[1].LastIndexOf(':')
				? "yyyyMMdd HH:mm:ss"
				: "yyyyMMdd HH:mm";

			return $"{parts[0]} {parts[1]}".ToDateTime(format).ApplyTimeZone(timeZone).UtcDateTime;
		}
		catch (Exception ex)
		{
			logs.AddErrorLog($"Error parse '{str}':\n{ex}");
			return null;
		}
	}

	public static IBSocket SendDate(this IBSocket socket, DateTime? time)
	{
		return socket.Send(time, _yyyyMMddFormat);
	}

	public static IBSocket Send(this IBSocket socket, DateTime? time, string format)
	{
		if (socket == null)
			throw new ArgumentNullException(nameof(socket));

		return socket.Send(time?.ToString(format) ?? string.Empty);
	}

	public static async ValueTask<DateTime> ReadDateAsync(this IBSocket socket, CancellationToken cancellationToken)
	{
		return (await socket.ReadNullDateTimeAsync(_yyyyMMddFormat, cancellationToken).NoWait()).Value;
	}

	public static async ValueTask<DateTime> ReadDateTimeAsync(this IBSocket socket, string format, CancellationToken cancellationToken)
	{
		return (await socket.ReadNullDateTimeAsync(format, cancellationToken).NoWait()).Value;
	}

	public static async ValueTask<DateTime?> ReadNullDateAsync(this IBSocket socket, CancellationToken cancellationToken)
	{
		return await socket.ReadNullDateTimeAsync(_yyyyMMddFormat, cancellationToken).NoWait();
	}

	public static async ValueTask<DateTime?> ReadNullDateTimeAsync(this IBSocket socket, string format, CancellationToken cancellationToken)
	{
		if (socket == null)
			throw new ArgumentNullException(nameof(socket));

		var s = await socket.ReadStringAsync(cancellationToken).NoWait();
		return s.ReadNullDateTime(format, socket);
	}

	public static DateTime? ReadNullDate(this string str, ILogReceiver logs)
	{
		return str.ReadNullDateTime(_yyyyMMddFormat, logs);
	}

	public static DateTime? ReadNullDateTime(this string str, string format, ILogReceiver logs)
	{
		if (str.IsNoDateTime())
			return null;

		try
		{
			return str.ToDateTime(format).UtcKind();
		}
		catch (Exception ex)
		{
			logs.AddErrorLog(ex);
			return null;
		}
	}

	private static bool IsNoDateTime(this string str)
	{
		return str.IsEmpty() || str == "NOEXP";
	}

	public static async ValueTask<DateTime> ReadUnixDateTimeAsync(this IBSocket socket, CancellationToken cancellationToken)
	{
		var seconds = await socket.ReadLongAsync(cancellationToken).NoWait();
		return seconds.FromUnix();
		//Check if date time string or seconds
		//if (longDate < 30000000)
		//	time =
		//		new DateTime(Int32.Parse(date.Substring(0, 4)), Int32.Parse(date.Substring(4, 2)),
		//					 Int32.Parse(date.Substring(6, 2)), 0, 0, 0, DateTimeKind.Utc).ToLocalTime();
		//else
	}

	public static async ValueTask<InteractiveBrokersOrderCondition.HedgeTypes?> ReadHedgeTypeAsync(this IBSocket socket, CancellationToken cancellationToken)
	{
		var str = await socket.ReadStringAsync(cancellationToken).NoWait();

		if (str.IsEmpty())
			return null;

        return str switch
        {
            "D" => InteractiveBrokersOrderCondition.HedgeTypes.Delta,
            "B" => InteractiveBrokersOrderCondition.HedgeTypes.Beta,
            "F" => InteractiveBrokersOrderCondition.HedgeTypes.FX,
            "P" => InteractiveBrokersOrderCondition.HedgeTypes.Pair,
            _ => throw new InvalidOperationException(LocalizedStrings.UnknownType.Put(str)),
        };
    }
}
