namespace StockSharp.InteractiveBrokers;

using System.Text.RegularExpressions;
using TagValue = System.Tuple<string, string>;

public partial class InteractiveBrokersMessageAdapter
{
	private readonly SynchronizedDictionary<long, SecurityId> _requestSecIdMap = [];
	private readonly CachedSynchronizedSet<string> _newsProviders = [];
	private readonly SynchronizedDictionary<string, string> _newsProviders2 = [];
	private readonly SynchronizedDictionary<int, Tuple<List<Tuple<decimal, decimal, string>>, List<Tuple<decimal, decimal, string>>>> _depths = [];
	private readonly SynchronizedDictionary<long, SecurityId> _histContracts = [];
	private readonly SynchronizedDictionary<long, bool> _realTimeSubscriptions = [];
	private readonly SynchronizedSet<long> _mdRequests = [];
	private readonly SynchronizedSet<long> _mdCancellingRequests = [];

	private static readonly TimeSpan _sec5Tf = TimeSpan.FromSeconds(5);

	/// <inheritdoc />
	protected override async ValueTask MarketDataAsync(MarketDataMessage mdMsg, CancellationToken cancellationToken)
	{
		if (mdMsg.IsSubscribe)
			_mdRequests.Add(mdMsg.TransactionId);

		if (mdMsg.DataType2 == DataType.Level1)
		{
			await SendSubscriptionReplyAsync(mdMsg.TransactionId, cancellationToken);

			if (mdMsg.IsSubscribe)
			{
				_requestSecIdMap.Add(mdMsg.TransactionId, mdMsg.SecurityId);

				if (mdMsg.From == null)
				{
					await SubscribeMarketData(mdMsg, mdMsg.Fields ?? [], false, false, [], cancellationToken);

					await SendSubscriptionResultAsync(mdMsg, cancellationToken);
				}
				else
					await RequestHistoricalTicks(mdMsg, mdMsg.IsRegularTradingHours == true, true, [], cancellationToken);
			}
			else
				await UnSubscribeMarketData(mdMsg.OriginalTransactionId, cancellationToken);
		}
		else if (mdMsg.DataType2 == DataType.MarketDepth)
		{
			await SendSubscriptionReplyAsync(mdMsg.TransactionId, cancellationToken);

			if (mdMsg.IsSubscribe)
			{
				_requestSecIdMap.Add(mdMsg.TransactionId, mdMsg.SecurityId);

				await SubscribeMarketDepth(mdMsg, GetExchange(mdMsg).EqualsIgnoreCase(BoardCodes.Smart), [], cancellationToken);

				await SendSubscriptionResultAsync(mdMsg, cancellationToken);
			}
			else
				await UnSubscribeMarketDepth(mdMsg.OriginalTransactionId, false, cancellationToken);
		}
		else if (mdMsg.DataType2 == DataType.News)
		{
			await SendSubscriptionReplyAsync(mdMsg.TransactionId, cancellationToken);

			if (mdMsg.IsSubscribe)
			{
				if (mdMsg.NewsId.IsEmpty())
				{
					if (mdMsg.From != null)
						await RequestHistoricalNews(mdMsg.TransactionId, mdMsg.SecurityId.InteractiveBrokers, _newsProviders.Cache.Join("+"), mdMsg.From.Value, mdMsg.To ?? DateTime.UtcNow, mdMsg.Count ?? 100, [], cancellationToken);
					else
					{
						await SubscribeNewsBulletins(true, cancellationToken);

						await SendSubscriptionResultAsync(mdMsg, cancellationToken);
					}
				}
				else
					await RequestNewsArticle(mdMsg.TransactionId, _newsProviders2[mdMsg.NewsId], mdMsg.NewsId, [], cancellationToken);
			}
			else
				await UnSubscribeNewsBulletins(cancellationToken);
		}
		else if (mdMsg.DataType2.IsCandles)
		{
			await SendSubscriptionReplyAsync(mdMsg.TransactionId, cancellationToken);

			if (mdMsg.IsSubscribe)
			{
				_requestSecIdMap.Add(mdMsg.TransactionId, mdMsg.SecurityId);

				var buildFrom = GetBuildCandlesField(mdMsg);
				var timeFrame = mdMsg.GetTimeFrame();

				if (mdMsg.To == null)
				{
					var allowKeepUpdated = Session.ServerVersion >= ServerVersions.SyntRealtimeBars;

					if (allowKeepUpdated || timeFrame != _sec5Tf)
					{
						if (allowKeepUpdated)
							_realTimeSubscriptions.Add(mdMsg.TransactionId, true);

						await SubscribeHistoricalCandles(mdMsg, timeFrame, buildFrom, mdMsg.IsRegularTradingHours == true, [], cancellationToken);
					}
					else
					{
						_realTimeSubscriptions.Add(mdMsg.TransactionId, false);
						await SubscribeRealTimeCandles(mdMsg, buildFrom, mdMsg.IsRegularTradingHours == true, [], cancellationToken);
					}
				}
				else
				{
					await SubscribeHistoricalCandles(mdMsg, timeFrame, buildFrom, mdMsg.IsRegularTradingHours == true, [], cancellationToken);
				}

				await SendSubscriptionResultAsync(mdMsg, cancellationToken);
			}
			else
			{
				var isKeepUpdate = _realTimeSubscriptions.TryGetValue2(mdMsg.OriginalTransactionId);
				_realTimeSubscriptions.Remove(mdMsg.OriginalTransactionId);

				if (isKeepUpdate == false)
					await UnSubscribeRealTimeCandles(mdMsg.OriginalTransactionId, cancellationToken);
				else
				{
					_mdCancellingRequests.Add(mdMsg.OriginalTransactionId);
					await UnSubscribeHistoricalCandles(mdMsg.OriginalTransactionId, cancellationToken);
				}
			}
		}
		else if (mdMsg.DataType2 == ExtendedDataTypes.Scanner)
		{
			await SendSubscriptionReplyAsync(mdMsg.TransactionId, cancellationToken);

			if (mdMsg.IsSubscribe)
			{
				var scannerMsg = (ScannerMarketDataMessage)mdMsg;

				if (scannerMsg.IsParametersRequest)
					await RequestScannerParameters(cancellationToken);
				else
				{
					await SubscribeScanner(scannerMsg, [], [], cancellationToken);
					await SendSubscriptionResultAsync(mdMsg, cancellationToken);
				}
			}
			else
				await UnSubscribeScanner(mdMsg.OriginalTransactionId, cancellationToken);
		}
		else if (mdMsg.DataType2 == ExtendedDataTypes.WshMetaData)
		{
			await SendSubscriptionReplyAsync(mdMsg.TransactionId, cancellationToken);

			if (mdMsg.IsSubscribe)
				await RequestWshMetaData((WshMetaMarketDataMessage)mdMsg, cancellationToken);
			else
				await CancelWshMetaData(mdMsg.OriginalTransactionId, cancellationToken);
		}
		else if (mdMsg.DataType2 == ExtendedDataTypes.WshEventData)
		{
			await SendSubscriptionReplyAsync(mdMsg.TransactionId, cancellationToken);

			if (mdMsg.IsSubscribe)
				await RequestWshEventData((WshEventMarketDataMessage)mdMsg, cancellationToken);
			else
				await CancelWshEventData(mdMsg.OriginalTransactionId, cancellationToken);
		}
		else if (mdMsg.DataType2 == ExtendedDataTypes.OptionCalc)
		{
			await SendSubscriptionReplyAsync(mdMsg.TransactionId, cancellationToken);

			if (mdMsg.IsSubscribe)
			{
				_requestSecIdMap.Add(mdMsg.TransactionId, mdMsg.SecurityId);

				var optionMsg = (OptionCalcMarketDataMessage)mdMsg;

				await SubscribeCalculateOptionPrice(optionMsg, [], cancellationToken);
				await SubscribeCalculateImpliedVolatility(optionMsg, [], cancellationToken);

				await SendSubscriptionResultAsync(mdMsg, cancellationToken);
			}
			else
			{
				await UnSubscribeCalculateOptionPrice(mdMsg.OriginalTransactionId, cancellationToken);
				await UnSubscribeCalculateImpliedVolatility(mdMsg.OriginalTransactionId, cancellationToken);
			}
		}
		else if (mdMsg.DataType2 == ExtendedDataTypes.OptionParameters)
		{
			await SendSubscriptionReplyAsync(mdMsg.TransactionId, cancellationToken);

			if (mdMsg.IsSubscribe)
			{
				_requestSecIdMap.Add(mdMsg.TransactionId, mdMsg.SecurityId);
				await RequestSecurityDefinitionOptionParams(mdMsg.TransactionId, GetSymbol(mdMsg), GetExchange(mdMsg), mdMsg.UnderlyingSecurityType, mdMsg.SecurityId.InteractiveBrokers, cancellationToken);
			}
		}
		else if (mdMsg.DataType2 == ExtendedDataTypes.SoftDollarTier)
		{
			await SendSubscriptionReplyAsync(mdMsg.TransactionId, cancellationToken);

			if (mdMsg.IsSubscribe)
				await RequestSoftDollarTiers(mdMsg.TransactionId, cancellationToken);
		}
		else if (mdMsg.DataType2 == ExtendedDataTypes.Histogram)
		{
			await SendSubscriptionReplyAsync(mdMsg.TransactionId, cancellationToken);

			if (mdMsg.IsSubscribe)
			{
				_requestSecIdMap.Add(mdMsg.TransactionId, mdMsg.SecurityId);
				await RequestHistogramData(mdMsg.TransactionId, mdMsg, mdMsg.IsRegularTradingHours == true, ConvertPeriod(mdMsg), cancellationToken);
			}
			else
				await CancelHistogramData(mdMsg.OriginalTransactionId, cancellationToken);
		}
		else
		{
			await SendSubscriptionNotSupportedAsync(mdMsg.TransactionId, cancellationToken);
		}
	}

	private static Level1Fields GetBuildCandlesField(MarketDataMessage message)
	{
		var field = message.BuildField;

		if (field != null)
			return field.Value;

		if (message.SecurityType == SecurityTypes.Currency)
			return CandleDataTypes.Midpoint;

		return CandleDataTypes.Trades;
	}

	/// <inheritdoc />
	protected override ValueTask SecurityLookupAsync(SecurityLookupMessage lookupMsg, CancellationToken cancellationToken)
	{
		var secCode = GetSymbol(lookupMsg);
		var exchange = GetExchange(lookupMsg);

		if (secCode.IsEmpty())
			secCode = GetLocalSymbol(lookupMsg);

		_messageRequests.Add(lookupMsg.TransactionId, lookupMsg.Type);

		if (!secCode.IsEmpty() && lookupMsg.SecurityId.InteractiveBrokers == null && (lookupMsg.SecurityType == null || lookupMsg.SecurityType == SecurityTypes.Stock) &&
			lookupMsg.ExpiryDate == null && lookupMsg.Strike == null && lookupMsg.OptionType == null &&
			lookupMsg.Multiplier == null && lookupMsg.Class.IsEmpty() && exchange.IsEmpty() && lookupMsg.Currency == null)
			return RequestMatchingSymbols(lookupMsg.TransactionId, secCode, cancellationToken);
		else
			return RequestSecurityInfo(lookupMsg, cancellationToken);
	}

	private ValueTask ProcessFinancialAdvise(FinancialAdviseMessage message, CancellationToken cancellationToken)
	{
		if (message.IsReplace)
			return ReplaceFinancialAdvisor(message.TransactionId, message.AdviseType, message.Data, cancellationToken);
		else
			return RequestFinancialAdvisor(message.AdviseType, cancellationToken);
	}

	/// <summary>
	/// To start the instrument scanner based on specified parameters.
	/// </summary>
	/// <param name="message">Instrument scanner filter settings.</param>
	/// <param name="tags"></param>
	/// <param name="filterOptions"></param>
	/// <param name="cancellationToken"><see cref="CancellationToken"/></param>
	private ValueTask SubscribeScanner(ScannerMarketDataMessage message, TagValue[] tags, TagValue[] filterOptions, CancellationToken cancellationToken)
	{
		if (message == null)
			throw new ArgumentNullException(nameof(message));

		var socket = Session;

		socket
			.SendMessage(RequestMessages.SubscribeScanner)
			.SendIfLess(ServerVersions.ScannerGenericOpts, s => s.SendVersion(ServerVersions.V4))
			.Send(message.TransactionId)
			.Send(message.Filter.RowCount)
			.Send(message.Filter.SecurityType)
			.Send(message.Filter.BoardCode)
			.SendScannerCode(message.Filter.ScanCode)
			.Send(message.Filter.AbovePrice)
			.Send(message.Filter.BelowPrice)
			.Send(message.Filter.AboveVolume)
			.Send(message.Filter.MarketCapAbove)
			.Send(message.Filter.MarketCapBelow)
			.Send(message.Filter.MoodyRatingAbove)
			.Send(message.Filter.MoodyRatingBelow)
			.Send(message.Filter.SpRatingAbove)
			.Send(message.Filter.SpRatingBelow)
			.SendDate(message.Filter.MaturityDateAbove)
			.SendDate(message.Filter.MaturityDateBelow)
			.Send(message.Filter.CouponRateAbove)
			.Send(message.Filter.CouponRateBelow)
			.Send(message.Filter.ExcludeConvertibleBonds);

		if (socket.ServerVersion >= ServerVersions.V25)
		{
			socket
				.Send(message.Filter.AverageOptionVolumeAbove)
				.Send(message.Filter.ScannerSettingPairs);
		}

		if (socket.ServerVersion >= ServerVersions.V27)
		{
			switch (message.Filter.StockTypeExclude)
			{
				case ScannerFilterStockExcludes.All:
					socket.Send("ALL");
					break;
				case ScannerFilterStockExcludes.Stock:
					socket.Send("STOCK");
					break;
				case ScannerFilterStockExcludes.Etf:
					socket.Send("ETF");
					break;
				default:
					throw new ArgumentOutOfRangeException();
			}
		}

		if (socket.ServerVersion >= ServerVersions.ScannerGenericOpts)
		{
			socket.SendTagsNoCount(filterOptions);
		}

		// send scannerSubscriptionOptions parameter
		if (socket.ServerVersion >= ServerVersions.Linking)
		{
			socket.SendTagsNoCount(tags);
		}

		return socket.SendAsync(cancellationToken);
	}

	/// <summary>
	/// To stop the instrument scanner previously started via <see cref="SubscribeScanner"/>.
	/// </summary>
	private ValueTask UnSubscribeScanner(long requestId, CancellationToken cancellationToken)
	{
		return Session
			.SendMessage(RequestMessages.UnSubscribeScanner)
			.SendVersion(ServerVersions.V1)
			.Send(requestId)
			.SendAsync(cancellationToken);
	}

	/// <summary>
	/// Call the reqScannerParameters() method to receive an XML document that describes the valid parameters that a scanner subscription can have.
	/// </summary>
	private ValueTask RequestScannerParameters(CancellationToken cancellationToken)
	{
		return Session
			.SendMessage(RequestMessages.RequestScannerParameters)
			.SendVersion(ServerVersions.V1)
			.SendAsync(cancellationToken);
	}

	/// <summary>
	/// Call this method to request market data. The market data will be returned by the tickPrice, tickSize, tickOptionComputation(), tickGeneric(), tickString() and tickEFP() methods.
	/// </summary>
	/// <param name="message">this structure contains a description of the contract for which market data is being requested.</param>
	/// <param name="genericFields">comma delimited list of generic tick types. Tick types can be found here: (new Generic Tick Types page).</param>
	/// <param name="snapshot">Allows client to request snapshot market data.</param>
	/// <param name="regulatorySnaphsot">Regulatory snapshot requests NBBO snapshots for users which have "US Securities Snapshot Bundle" subscription but not corresponding Network A, B, or C subscription necessary for streaming.</param>
	/// <param name="mktDataOptions">Market Data Off - used in conjunction with RTVolume Generic tick type causes only volume data to be sent.</param>
	/// <param name="cancellationToken"><see cref="CancellationToken"/></param>
	private ValueTask SubscribeMarketData(MarketDataMessage message, IEnumerable<Level1Fields> genericFields, bool snapshot, bool regulatorySnaphsot, IEnumerable<TagValue> mktDataOptions, CancellationToken cancellationToken)
	{
		if (message == null)
			throw new ArgumentNullException(nameof(message));

		var socket = Session;

		var security = (SecurityMessage)message;

		socket
			.SendMessage(RequestMessages.SubscribeMarketData)
			.SendVersion(ServerVersions.V11)
			.Send(message.TransactionId)
			.SendIfEqualOrMore(ServerVersions.ContractConId, s => s.SendContractId(message.SecurityId))
			.Send(GetSymbol(security))
			.SendSecurityType(security.SecurityType, security.UnderlyingSecurityType)
			.SendDate(security.ExpiryDate)
			.SendStrike(security.Strike)
			.SendOptionType(security.OptionType)
			.SendIfEqualOrMore(ServerVersions.V15, s => s.Send(security.Multiplier))
			.Send(GetExchange(security))
			.SendIfEqualOrMore(ServerVersions.V14, s => s.Send(GetPrimaryExchange(security)))
			.SendCurrency(security.Currency)
			.SendIfEqualOrMore(ServerVersions.V2, s => s.Send(GetLocalSymbol(security)))
			.SendIfEqualOrMore(ServerVersions.TradingClass, s => socket.Send(message.Class));

		if (socket.ServerVersion >= ServerVersions.V8 && message.IsCombo())
		{
			socket.SendCombo(message);
		}

		if (socket.ServerVersion >= ServerVersions.ScaleOrders2)
		{
			//if (contract.UnderlyingComponent != null)
			//{
			//	UnderlyingComponent underComp = contract.UnderlyingComponent;
			//	send(true);
			//	send(underComp.ContractId);
			//	send(underComp.Delta);
			//	send(underComp.Price);
			//}
			//else
			//{
			socket.Send(false);
			//}
		}

		if (socket.ServerVersion >= ServerVersions.V31)
		{
			socket.Send(genericFields.Select(t => ((int)t).To<string>()).JoinComma());
		}

		if (socket.ServerVersion >= ServerVersions.SShortComboLegs)
		{
			socket.Send(snapshot);
		}

		if (socket.ServerVersion >= ServerVersions.SmartComponents)
		{
			socket.Send(regulatorySnaphsot);
		}

		if (socket.ServerVersion >= ServerVersions.Linking)
		{
			socket.SendTagsNoCount(mktDataOptions);
		}

		return socket.SendAsync(cancellationToken);
	}

	/// <summary>
	/// After calling this method, market data for the specified Id will stop flowing.
	/// </summary>
	private ValueTask UnSubscribeMarketData(long requestId, CancellationToken cancellationToken)
	{
		return Session
			.SendMessage(RequestMessages.UnSubscribeMarketData)
			.SendVersion(ServerVersions.V1)
			.Send(requestId)
			.SendAsync(cancellationToken);
	}

	private static string ConvertPeriod(MarketDataMessage mdMsg)
	{
		if (mdMsg == null)
			throw new ArgumentNullException(nameof(mdMsg));

		var from = mdMsg.From;
		var to = mdMsg.To ?? DateTime.UtcNow;

		if (from == null)
			from = to.AddTicks(-mdMsg.GetTimeFrame().Ticks * 100);

		return ConvertPeriod(from.Value, to);
	}

	/// <summary>
	/// used for reqHistoricalData.
	/// </summary>
	private static string ConvertPeriod(DateTime startTime, DateTime endTime)
	{
		if (startTime > endTime)
			throw new InvalidOperationException(LocalizedStrings.StartCannotBeMoreEnd.Put(startTime, endTime));

		var period = endTime - startTime;
		var secs = (long)period.TotalSeconds;

		if (secs < 1)
			throw new ArgumentOutOfRangeException(nameof(endTime), endTime, "Period cannot be less than 1 second.");

		var ticks = secs * TimeSpan.TicksPerSecond;

		var years = ticks / TimeHelper.TicksPerYear;

		if (years >= 1)
			return years + " Y";

		var months = ticks / TimeHelper.TicksPerMonth;

		if (months >= 1)
			return months + " M";

		var weeks = ticks / TimeHelper.TicksPerWeek;

		if (weeks >= 1)
			return weeks + " W";

		var days = ticks / TimeSpan.TicksPerDay;

		if (days >= 1)
			return days + " D";

		return secs + " S";
	}

	/// <summary>
	/// To subscribe for instrument historical values getting at specified intervals.
	/// </summary>
	/// <param name="message">The message about subscription or unsubscription for market data.</param>
	/// <param name="timeFrame"></param>
	/// <param name="field">The market data field. Following values are supported:
	/// <list type="number">
	/// <item><description><see cref="F:StockSharp.InteractiveBrokers.CandleDataTypes.Trades" />.</description></item>
	/// <item><description><see cref="F:StockSharp.InteractiveBrokers.CandleDataTypes.Bid" />.</description></item>
	/// <item><description><see cref="F:StockSharp.InteractiveBrokers.CandleDataTypes.Ask" />.</description></item>
	/// <item><description><see cref="F:StockSharp.InteractiveBrokers.CandleDataTypes.Midpoint" />.</description></item>
	/// <item><description><see cref="F:StockSharp.InteractiveBrokers.CandleDataTypes.BidAsk" />.</description></item>
	/// <item><description><see cref="F:StockSharp.InteractiveBrokers.CandleDataTypes.ImpliedVolatility" />.</description></item>
	/// <item><description><see cref="F:StockSharp.InteractiveBrokers.CandleDataTypes.HistoricalVolatility" />.</description></item>
	/// </list>.
	/// </param>
	/// <param name="useRth">To get data only by the trading time. By default the trading time is used.</param>
	/// <param name="tags"></param>
	/// <param name="cancellationToken"><see cref="CancellationToken"/></param>
	private ValueTask SubscribeHistoricalCandles(MarketDataMessage message, TimeSpan timeFrame, Level1Fields field, bool useRth, TagValue[] tags, CancellationToken cancellationToken)
	{
		if (message == null)
			throw new ArgumentNullException(nameof(message));

		//if (message.CandleType != typeof(TimeFrameCandle))
		//	throw new ArgumentException("Interactive Brokers не поддерживает свечи типа {0}.".Put(series.CandleType), "series");

		// Whether a subscription is made to return updates of unfinished real time bars as they are available (True),
		// or all data is returned on a one-time basis (False). Available starting with API v973.03+ and TWS v965+.
		var keepUpToDate = message.To == null;

		var socket = Session;
		var security = (SecurityMessage)message;

		socket
			.SendMessage(RequestMessages.SubscribeHistoricalData)
			.SendIfLess(ServerVersions.SyntRealtimeBars, s => s.SendVersion(ServerVersions.V6))
			.Send(message.TransactionId)
			.SendIfEqualOrMore(ServerVersions.TradingClass, s => socket.SendContractId(message.SecurityId))
			.Send(GetSymbol(security))
			.SendSecurityType(security.SecurityType, security.UnderlyingSecurityType)
			.SendDate(security.ExpiryDate)
			.SendStrike(security.Strike)
			.SendOptionType(security.OptionType)
			.Send(security.Multiplier)
			.Send(GetExchange(security))
			.Send(GetPrimaryExchange(security))
			.SendCurrency(security.Currency)
			.Send(GetLocalSymbol(security))
			.SendIfEqualOrMore(ServerVersions.TradingClass, s => socket.Send(message.Class))
			.SendIncludeExpired(security.ExpiryDate);

		if (socket.ServerVersion >= ServerVersions.SyntRealtimeBars && keepUpToDate)
		{
			// If keepUpToDate=True, and endDateTime cannot be specified.
			socket.Send(string.Empty);
		}
		else
		{
			socket.Send(message.To ?? DateTime.UtcNow);
		}

		socket
			.Send(timeFrame.ToNative())
			.Send(ConvertPeriod(message))
			.Send(useRth)
			.SendLevel1Field(field)

			// set to 1 to obtain the bars' time as yyyyMMdd HH:mm:ss,
			// set to 2 to obtain it like system time format in seconds
			.Send(2);

		if (message.IsCombo())
		{
			socket.SendCombo(message);
		}

		if (socket.ServerVersion >= ServerVersions.SyntRealtimeBars)
		{
			socket.Send(keepUpToDate);
		}

		if (socket.ServerVersion >= ServerVersions.Linking)
		{
			socket.SendTagsNoCount(tags);
		}

		return socket.SendAsync(cancellationToken);
	}

	private ValueTask UnSubscribeHistoricalCandles(long requestId, CancellationToken cancellationToken)
	{
		return Session
			.SendMessage(RequestMessages.UnSubscribeHistoricalData)
			.SendVersion(ServerVersions.V1)
			.Send(requestId)
			.SendAsync(cancellationToken);
	}

	/// <summary>
	/// To subscribe for real-time candles receiving.
	/// </summary>
	/// <param name="message"></param>
	/// <param name="field">Market data fields upon which candles will be created. The following values are supported:
	/// <list type="number">
	/// <item><description><see cref="F:StockSharp.InteractiveBrokers.CandleDataTypes.Trades" />.</description></item>
	/// <item><description><see cref="F:StockSharp.InteractiveBrokers.CandleDataTypes.Bid" />.</description></item>
	/// <item><description><see cref="F:StockSharp.InteractiveBrokers.CandleDataTypes.Ask" />.</description></item>
	/// <item><description><see cref="F:StockSharp.InteractiveBrokers.CandleDataTypes.Midpoint" />.</description></item>
	/// </list>.</param>
	/// <param name="useRth">To create candles only by the trading time. By default the trading time is used.</param>
	/// <param name="tags"></param>
	/// <param name="cancellationToken"><see cref="CancellationToken"/></param>
	private ValueTask SubscribeRealTimeCandles(MarketDataMessage message, Level1Fields field, bool useRth, TagValue[] tags, CancellationToken cancellationToken)
	{
		if (message == null)
			throw new ArgumentNullException(nameof(message));

		var socket = Session;
		var security = (SecurityMessage)message;

		return socket
			.SendMessage(RequestMessages.SubscribeRealTimeCandles)
			.SendVersion(ServerVersions.V3)
			.Send(message.TransactionId)
			.SendIfEqualOrMore(ServerVersions.TradingClass, s => s.SendContractId(message.SecurityId))
			.Send(GetSymbol(security))
			.SendSecurityType(security.SecurityType, security.UnderlyingSecurityType)
			.SendDate(security.ExpiryDate)
			.SendStrike(security.Strike)
			.SendOptionType(security.OptionType)
			.Send(security.Multiplier)
			.Send(GetExchange(security))
			.Send(GetPrimaryExchange(security))
			.SendCurrency(security.Currency)
			.Send(GetLocalSymbol(security))
			.SendIfEqualOrMore(ServerVersions.TradingClass, s => s.Send(message.Class))
			.Send(5) // Поддерживается только 5 секундный тайм-фрейм.
			.SendLevel1Field(field)
			.Send(useRth)
			.SendIfEqualOrMore(ServerVersions.Linking, s => s.SendTagsNoCount(tags))
			.SendAsync(cancellationToken);
	}

	/// <summary>
	/// To stop the candles receiving subscription, previously created by <see cref="SubscribeRealTimeCandles"/>.
	/// </summary>
	/// <param name="requestId"></param>
	/// <param name="cancellationToken"><see cref="CancellationToken"/></param>
	private ValueTask UnSubscribeRealTimeCandles(long requestId, CancellationToken cancellationToken)
	{
		return Session
			.SendMessage(RequestMessages.UnSubscribeRealTimeCandles)
			.SendVersion(ServerVersions.V1)
			.Send(requestId)
			.SendAsync(cancellationToken);
	}

	/// <summary>
	/// Call this method to request market depth for a specific contract. The market depth will be returned by the updateMktDepth() and updateMktDepthL2() methods.
	/// </summary>
	/// <param name="message">this structure contains a description of the contract for which market depth data is being requested.</param>
	/// <param name="isSmartDepth">Flag indicates that this is smart depth request.</param>
	/// <param name="tags"></param>
	/// <param name="cancellationToken"><see cref="CancellationToken"/></param>
	private ValueTask SubscribeMarketDepth(MarketDataMessage message, bool isSmartDepth, TagValue[] tags, CancellationToken cancellationToken)
	{
		if (message == null)
			throw new ArgumentNullException(nameof(message));

		var socket = Session;
		var security = (SecurityMessage)message;

		return socket
			.SendMessage(RequestMessages.SubscribeMarketDepth)
			.SendVersion(ServerVersions.V5)
			.Send(message.TransactionId)
			.SendIfEqualOrMore(ServerVersions.TradingClass, s => s.SendContractId(message.SecurityId))
			.Send(GetSymbol(security))
			.SendSecurityType(security.SecurityType, security.UnderlyingSecurityType)
			.SendDate(security.ExpiryDate)
			.SendStrike(security.Strike)
			.SendOptionType(security.OptionType)
			.SendIfEqualOrMore(ServerVersions.V15, s => s.Send(security.Multiplier))
			.Send(GetExchange(security))
			.SendIfEqualOrMore(ServerVersions.MarketDepthPrimeExchange, s => s.Send(GetPrimaryExchange(security)))
			.SendCurrency(security.Currency)
			.Send(GetLocalSymbol(security))
			.SendIfEqualOrMore(ServerVersions.TradingClass, s => s.Send(message.Class))
			.SendIfEqualOrMore(ServerVersions.V19, s => s.Send(message.MaxDepth ?? 5))
			.SendIfEqualOrMore(ServerVersions.SmartDepth, s => s.Send(isSmartDepth))
			.SendIfEqualOrMore(ServerVersions.Linking, s => s.SendTagsNoCount(tags))
			.SendAsync(cancellationToken);
	}

	/// <summary>
	/// After calling this method, market depth data for the specified Id will stop flowing.
	/// </summary>
	private ValueTask UnSubscribeMarketDepth(long requestId, bool isSmartDepth, CancellationToken cancellationToken)
	{
		return Session
			.SendMessage(RequestMessages.UnSubscribeMarketDepth)
			.SendVersion(ServerVersions.V1)
			.Send(requestId)
			.SendIfEqualOrMore(ServerVersions.SmartDepth, s => s.Send(isSmartDepth))
			.SendAsync(cancellationToken);
	}

	/// <summary>
	/// Call this method to start receiving news bulletins. Each bulletin will be returned by the updateNewsBulletin() method.
	/// </summary>
	/// <param name="allMessages">if set to <see langword="true" />, returns all the existing bulletins for the current day and any new ones. IF set to <see langword="false" />, will only return new bulletins.</param>
	/// <param name="cancellationToken"><see cref="CancellationToken"/></param>
	private ValueTask SubscribeNewsBulletins(bool allMessages, CancellationToken cancellationToken)
	{
		return Session
			.SendMessage(RequestMessages.SubscribeNewsBulletins)
			.SendVersion(ServerVersions.V1)
			.Send(allMessages)
			.SendAsync(cancellationToken);
	}

	/// <summary>
	/// Call this method to stop receiving news bulletins.
	/// </summary>
	private ValueTask UnSubscribeNewsBulletins(CancellationToken cancellationToken)
	{
		return Session
			.SendMessage(RequestMessages.UnSubscribeNewsBulletins)
			.SendVersion(ServerVersions.V1)
			.SendAsync(cancellationToken);
	}

	/// <summary>
	/// Call this function to download all details for a particular underlying. the contract details will be received via the contractDetails() function on the EWrapper.
	/// </summary>
	/// <param name="criteria">summary description of the contract being looked up.</param>
	/// <param name="cancellationToken"><see cref="CancellationToken"/></param>
	private ValueTask RequestSecurityInfo(SecurityLookupMessage criteria, CancellationToken cancellationToken)
	{
		if (criteria == null)
			throw new ArgumentNullException(nameof(criteria));

		var socket = Session;

		socket
			.SendMessage(RequestMessages.RequestContractData)
			.SendVersion(ServerVersions.V8);

		if (socket.ServerVersion >= ServerVersions.ScaleOrders2)
			socket.Send(criteria.TransactionId);

		if (socket.ServerVersion >= ServerVersions.ContractConId)
			socket.SendContractId(criteria.SecurityId);

		socket
			.Send(GetSymbol(criteria))
			.SendSecurityType(criteria.SecurityType, criteria.UnderlyingSecurityType)
			.Send(criteria.ExpiryDate, "yyyyMM")
			.SendStrike(criteria.Strike)
			.SendOptionType(criteria.OptionType)
			.SendIfEqualOrMore(ServerVersions.V15, s => s.Send(criteria.Multiplier));

		var exch = GetExchange(criteria);
		var primExch = GetPrimaryExchange(criteria);

		if (socket.ServerVersion >= ServerVersions.PrimaryExch)
		{
			socket
				.Send(exch)
				.Send(primExch);
		}
		else if (socket.ServerVersion >= ServerVersions.Linking)
		{
			if (!primExch.IsEmpty() && (exch == "BEST" || exch.EqualsIgnoreCase(BoardCodes.Smart)))
				socket.Send(exch + ":" + primExch);
			else
				socket.Send(exch);
		}

		return socket
			.SendCurrency(criteria.Currency)
			.Send(GetLocalSymbol(criteria))
			.SendIfEqualOrMore(ServerVersions.TradingClass, s => s.Send(criteria.Class))
			.SendIfEqualOrMore(ServerVersions.V31, s => s.SendIncludeExpired(criteria.ExpiryDate))
			.SendIfEqualOrMore(ServerVersions.SecIdType, s => s.SendSecurityId(criteria.SecurityId))
			.SendIfEqualOrMore(ServerVersions.MinServerVerBondIssuerId, s => s.Send(string.Empty))
			.SendAsync(cancellationToken);
	}

	///// <summary>
	///// Requests the contract's Reuters or Wall Street Horizons fundamental data.
	///// </summary>
	///// <param name="message"></param>
	///// <param name="tags">Reserved for future use, must be blank.</param>
	///// <param name="cancellationToken"><see cref="CancellationToken"/></param>
	//private ValueTask SubscribeFundamentalReport(FundamentalReportMarketDataMessage message, TagValue[] tags, CancellationToken cancellationToken)
	//{
	//	if (message == null)
	//		throw new ArgumentNullException(nameof(message));

	//	var security = (SecurityMessage)message;

	//	return Session
	//		.SendMessage(RequestMessages.SubscribeFundamentalData)
	//		.SendVersion(ServerVersions.V3)
	//		.Send(message.TransactionId)
	//		.SendIfEqualOrMore(ServerVersions.TradingClass, s => s.SendContractId(security.SecurityId))
	//		.Send(GetSymbol(security))
	//		.SendSecurityType(security.SecurityType, security.UnderlyingSecurityType)
	//		.Send(GetExchange(security))
	//		.Send(GetPrimaryExchange(security))
	//		.SendCurrency(security.Currency)
	//		.Send(GetLocalSymbol(security))
	//		.SendFundamental(message.Report)
	//		.SendIfEqualOrMore(ServerVersions.Linking, s => s.SendTagsNoCount(tags))
	//		.SendAsync(cancellationToken);
	//}

	///// <summary>
	///// To stop subscription for receiving of market reports by the specified instrument, created earlier via <see cref="SubscribeFundamentalReport"/>.
	///// </summary>
	///// <param name="requestId"></param>
	///// <param name="cancellationToken"><see cref="CancellationToken"/></param>
	//private ValueTask UnSubscribeFundamentalReport(long requestId, CancellationToken cancellationToken)
	//{
	//	return Session
	//		.SendMessage(RequestMessages.UnSubscribeFundamentalData)
	//		.SendVersion(ServerVersions.V1)
	//		.Send(requestId)
	//		.SendAsync(cancellationToken);
	//}

	/// <summary>
	/// To subscribe for receiving of the implied volatility for the specified instrument.
	/// </summary>
	/// <param name="message"></param>
	/// <param name="tags">Reserved for future use, must be blank.</param>
	/// <param name="cancellationToken"><see cref="CancellationToken"/></param>
	private ValueTask SubscribeCalculateImpliedVolatility(OptionCalcMarketDataMessage message, TagValue[] tags, CancellationToken cancellationToken)
	{
		if (message == null)
			throw new ArgumentNullException(nameof(message));

		var socket = Session;
		var security = (SecurityMessage)message;

		return socket
			.SendMessage(RequestMessages.SubscribeCalcImpliedVolatility)
			.SendVersion(ServerVersions.V3)
			.Send(message.TransactionId)
			.SendContractId(message.SecurityId)
			.Send(GetSymbol(security))
			.SendSecurityType(security.SecurityType, security.UnderlyingSecurityType)
			.SendDate(security.ExpiryDate)
			.SendStrike(security.Strike)
			.SendOptionType(security.OptionType)
			.Send(security.Multiplier)
			.Send(GetExchange(security))
			.Send(GetPrimaryExchange(security))
			.SendCurrency(security.Currency)
			.Send(GetLocalSymbol(security))
			.SendIfEqualOrMore(ServerVersions.TradingClass, s => s.Send(message.Class))
			.Send(message.OptionPrice)
			.Send(message.AssetPrice)
			.SendIfEqualOrMore(ServerVersions.Linking, s => s.SendTagsNoCount(tags))
			.SendAsync(cancellationToken);
	}

	/// <summary>
	/// To stop subscription for receiving of the implied volatility for the specified instrument, created earlier via <see cref="SubscribeCalculateImpliedVolatility"/>.
	/// </summary>
	/// <param name="requestId"></param>
	/// <param name="cancellationToken"><see cref="CancellationToken"/></param>
	private ValueTask UnSubscribeCalculateImpliedVolatility(long requestId, CancellationToken cancellationToken)
	{
		return Session
			.SendMessage(RequestMessages.UnSubscribeCalcImpliedVolatility)
			.SendVersion(ServerVersions.V1)
			.Send(requestId)
			.SendAsync(cancellationToken);
	}

	/// <summary>
	/// To subscribe for Greeks getting for the specified instrument.
	/// </summary>
	/// <param name="message">Security.</param>
	/// <param name="tags">Reserved for future use, must be blank.</param>
	/// <param name="cancellationToken"><see cref="CancellationToken"/></param>
	private ValueTask SubscribeCalculateOptionPrice(OptionCalcMarketDataMessage message, TagValue[] tags, CancellationToken cancellationToken)
	{
		if (message == null)
			throw new ArgumentNullException(nameof(message));

		var socket = Session;
		var security = (SecurityMessage)message;

		return socket
			.SendMessage(RequestMessages.SubscribeCalcOptionPrice)
			.SendVersion(ServerVersions.V3)
			.Send(message.TransactionId)
			.SendContractId(message.SecurityId)
			.Send(GetSymbol(security))
			.SendSecurityType(security.SecurityType, security.UnderlyingSecurityType)
			.SendDate(security.ExpiryDate)
			.SendStrike(security.Strike)
			.SendOptionType(security.OptionType)
			.Send(security.Multiplier)
			.Send(GetExchange(security))
			.Send(GetPrimaryExchange(security))
			.SendCurrency(security.Currency)
			.Send(GetLocalSymbol(security))
			.SendIfEqualOrMore(ServerVersions.TradingClass, s => s.Send(message.Class))
			.Send(message.ImpliedVolatility)
			.Send(message.AssetPrice)
			.SendIfEqualOrMore(ServerVersions.Linking, s => s.SendTagsNoCount(tags))
			.SendAsync(cancellationToken);
	}

	/// <summary>
	/// To stop subscription for receiving of the Greeks for the specified instrument, created earlier via <see cref="SubscribeCalculateOptionPrice"/>.
	/// </summary>
	/// <param name="requestId"></param>
	/// <param name="cancellationToken"><see cref="CancellationToken"/></param>
	private ValueTask UnSubscribeCalculateOptionPrice(long requestId, CancellationToken cancellationToken)
	{
		return Session
			.SendMessage(RequestMessages.UnSubscribeCalcOptionPrice)
			.SendVersion(ServerVersions.V1)
			.Send(requestId)
			.SendAsync(cancellationToken);
	}

	/// <summary>
	/// indicates the TWS to enable "frozen", "delayed" or "delayed-frozen" market data. Requires TWS/IBG v963+.
	/// The API can receive frozen market data from Trader Workstation. Frozen market data is the last data recorded in
	/// our system. During normal trading hours, the API receives real-time market data.Invoking this function with
	/// argument 2 requests a switch to frozen data immediately or after the close. When the market reopens the next
	/// data the market data type will automatically switch back to real time if available.
	/// </summary>
	private ValueTask RequestMarketDataType(CancellationToken cancellationToken)
	{
		return Session
			.SendMessage(RequestMessages.RequestMarketDataType)
			.SendVersion(ServerVersions.V1)
			// by default only real-time (1) market data is enabled
			// sending 1(real-time) disables frozen, delayed and delayed-frozen market data
			// sending 2(frozen) enables frozen market data
			// sending 3(delayed) enables delayed and disables delayed - frozen market data
			// sending 4(delayed-frozen) enables delayed and delayed-frozen market data
			.Send((int)MarketDataType)
			.SendAsync(cancellationToken);
	}

	/// <summary>
	/// Requests the FA configuration.
	/// </summary>
	/// <param name="dataType">
	/// Specifies the type of Financial Advisor configuration data being requested. Valid values include:
	/// Groups
	/// Profiles
	/// Account Aliases
	/// </param>
	/// <param name="cancellationToken"><see cref="CancellationToken"/></param>
	private ValueTask RequestFinancialAdvisor(string dataType, CancellationToken cancellationToken)
	{
		return Session
			.SendMessage(RequestMessages.RequestFinancialAdvisor)
			.SendVersion(ServerVersions.V1)
			.Send(dataType)
			.SendAsync(cancellationToken);
	}

	/// <summary>
	/// Replaces Financial Advisor's settings.
	/// </summary>
	/// <param name="requestId"></param>
	/// <param name="dataType">
	/// Specifies the type of Financial Advisor configuration data being requested. Valid values include:
	/// Groups
	/// Profiles
	/// Account Aliases</param>
	/// <param name="config">The XML string containing the new FA configuration information.</param>
	/// <param name="cancellationToken"><see cref="CancellationToken"/></param>
	private ValueTask ReplaceFinancialAdvisor(long requestId, string dataType, string config, CancellationToken cancellationToken)
	{
		return Session
			.SendMessage(RequestMessages.ReplaceFinancialAdvisor)
			.SendVersion(ServerVersions.V1)
			.Send(dataType)
			.Send(config)
			.SendIfEqualOrMore(ServerVersions.ReplaceFaEnd, s => s.Send(requestId))
			.SendAsync(cancellationToken);
	}

	/// <summary>
	/// Requests family codes for an account, for instance if it is a FA, IBroker, or associated account.
	/// </summary>
	private ValueTask RequestFamilyCodes(CancellationToken cancellationToken)
	{
		return Session
			.SendMessage(RequestMessages.RequestFamilyCodes)
			.SendAsync(cancellationToken);
	}

	/// <summary>
	/// Requests matching symbols (implements 'google-like' suggestions as user starts typing symbol or contract name).
	/// </summary>
	/// <param name="requestId"></param>
	/// <param name="pattern">User typed string pattern.</param>
	/// <param name="cancellationToken"><see cref="CancellationToken"/></param>
	private ValueTask RequestMatchingSymbols(long requestId, string pattern, CancellationToken cancellationToken)
	{
		return Session
			.SendMessage(RequestMessages.RequestMatchingSymbols)
			.Send(requestId)
			.Send(pattern)
			.SendAsync(cancellationToken);
	}

	/// <summary>
	/// Requests venues for which market data is returned to updateMktDepthL2 (those with market makers).
	/// </summary>
	private ValueTask RequestMarketDepthExchanges(CancellationToken cancellationToken)
	{
		return Session
			.SendMessage(RequestMessages.RequestMktDepthExchanges)
			.SendAsync(cancellationToken);
	}

	/// <summary>
	/// Returns the mapping of single letter codes to exchange names given the mapping identifier.
	/// </summary>
	/// <param name="requestId"></param>
	/// <param name="bboExchange"></param>
	/// <param name="cancellationToken"><see cref="CancellationToken"/></param>
	private ValueTask RequestSmartComponents(long requestId, string bboExchange, CancellationToken cancellationToken)
	{
		return Session
			.SendMessage(RequestMessages.RequestSmartComponents)
			.Send(requestId)
			.Send(bboExchange)
			.SendAsync(cancellationToken);
	}

	/// <summary>
	/// Requests news providers which the user has subscribed to.
	/// </summary>
	private ValueTask RequestNewsProviders(CancellationToken cancellationToken)
	{
		return Session
			.SendMessage(RequestMessages.RequestNewsProviders)
			.SendAsync(cancellationToken);
	}

	/// <summary>
	/// Requests news article body given articleId.
	/// </summary>
	/// <param name="requestId">Id of the request.</param>
	/// <param name="providerCode">Short code indicating news provider, e.g. FLY.</param>
	/// <param name="articleId">Id of the specific article.</param>
	/// <param name="tags">Reserved for internal use. Should be defined as empty.</param>
	/// <param name="cancellationToken"><see cref="CancellationToken"/></param>
	private ValueTask RequestNewsArticle(long requestId, string providerCode, string articleId, TagValue[] tags, CancellationToken cancellationToken)
	{
		return Session
			.SendMessage(RequestMessages.RequestNewsArticle)
			.Send(requestId)
			.Send(providerCode)
			.Send(articleId)
			.SendIfEqualOrMore(ServerVersions.NewsQueryOrigins, s => s.SendTagsNoCount(tags))
			.SendAsync(cancellationToken);
	}

	/// <summary>
	/// Requests historical news headlines.
	/// </summary>
	/// <param name="requestId"></param>
	/// <param name="contractId">Contract id of ticker.</param>
	/// <param name="providerCodes">A '+'-separated list of provider codes.</param>
	/// <param name="startDateTime">Marks the (exclusive) start of the date range. The format is yyyy-MM-dd HH:mm:ss.0.</param>
	/// <param name="endDateTime">Marks the (inclusive) end of the date range. The format is yyyy-MM-dd HH:mm:ss.0.</param>
	/// <param name="totalResults">The maximum number of headlines to fetch (1 - 300).</param>
	/// <param name="tags">Reserved for internal use. Should be defined as empty.</param>
	/// <param name="cancellationToken"><see cref="CancellationToken"/></param>
	private ValueTask RequestHistoricalNews(long requestId, int? contractId, string providerCodes, DateTime startDateTime, DateTime endDateTime, long totalResults, TagValue[] tags, CancellationToken cancellationToken)
	{
		return Session
			.SendMessage(RequestMessages.RequestHistoricalNews)
			.Send(requestId)
			.Send(contractId)
			.Send(providerCodes)
			.Send(startDateTime)
			.Send(endDateTime)
			.Send(totalResults)
			.SendIfEqualOrMore(ServerVersions.NewsQueryOrigins, s => s.SendTagsNoCount(tags))
			.SendAsync(cancellationToken);
	}

	/// <summary>
	/// Returns the timestamp of earliest available historical data for a contract and data type.
	/// </summary>
	/// <param name="requestId">An identifier for the request.</param>
	/// <param name="security">Contract object for which head timestamp is being requested.</param>
	/// <param name="whatToShow">Type of data for head timestamp - "BID", "ASK", "TRADES", etc.</param>
	/// <param name="useRth">Use regular trading hours only.</param>
	/// <param name="formatDate">Set to 1 to obtain the bars' time as yyyyMMdd HH:mm:ss, set to 2 to obtain it like system time format in seconds.</param>
	/// <param name="cancellationToken"><see cref="CancellationToken"/></param>
	private ValueTask RequestHeadTimestamp(long requestId, SecurityMessage security, string whatToShow, int useRth, int formatDate, CancellationToken cancellationToken)
	{
		return Session
			.SendMessage(RequestMessages.RequestHeadTimestamp)
			.Send(requestId)
			.SendContractId(security.SecurityId)
			.Send(GetSymbol(security))
			.SendSecurityType(security.SecurityType, security.UnderlyingSecurityType)
			.SendDate(security.ExpiryDate)
			.SendStrike(security.Strike)
			.SendOptionType(security.OptionType)
			.Send(security.Multiplier)
			.Send(GetExchange(security))
			.Send(GetPrimaryExchange(security))
			.SendCurrency(security.Currency)
			.Send(GetLocalSymbol(security))
			.Send(security.Class)
			.SendIncludeExpired(security.ExpiryDate)
			.Send(useRth)
			.Send(whatToShow)
			.Send(formatDate)
			.SendAsync(cancellationToken);
	}

	private ValueTask CancelHeadTimeStamp(long requestId, CancellationToken cancellationToken)
	{
		return Session
			.SendMessage(RequestMessages.CancelHeadTimestamp)
			.Send(requestId)
			.SendAsync(cancellationToken);
	}

	/// <summary>
	/// Returns data histogram of specified contract.
	/// </summary>
	/// <param name="requestId">An identifier for the request.</param>
	/// <param name="security">Contract object for which histogram is being requested.</param>
	/// <param name="useRth">Use regular trading hours only.</param>
	/// <param name="period">Period of which data is being requested, e.g. "3 days".</param>
	/// <param name="cancellationToken"><see cref="CancellationToken"/></param>
	private ValueTask RequestHistogramData(long requestId, SecurityMessage security, bool useRth, string period, CancellationToken cancellationToken)
	{
		return Session
			.SendMessage(RequestMessages.RequestHistogramData)
			.Send(requestId)
			.SendContractId(security.SecurityId)
			.Send(GetSymbol(security))
			.SendSecurityType(security.SecurityType, security.UnderlyingSecurityType)
			.SendDate(security.ExpiryDate)
			.SendStrike(security.Strike)
			.SendOptionType(security.OptionType)
			.Send(security.Multiplier)
			.Send(GetExchange(security))
			.Send(GetPrimaryExchange(security))
			.SendCurrency(security.Currency)
			.Send(GetLocalSymbol(security))
			.Send(security.Class)
			.SendIncludeExpired(security.ExpiryDate)
			.Send(useRth)
			.Send(period)
			.SendAsync(cancellationToken);
	}

	/// <summary>
	/// Cancels an active data histogram request.
	/// </summary>
	/// <param name="requestId">Identifier specified in <see cref="RequestHistogramData"/> request.</param>
	/// <param name="cancellationToken"><see cref="CancellationToken"/></param>
	private ValueTask CancelHistogramData(long requestId, CancellationToken cancellationToken)
	{
		return Session
			.SendMessage(RequestMessages.CancelHistogramData)
			.Send(requestId)
			.SendAsync(cancellationToken);
	}

	/// <summary>
	/// Requests details about a given market rule.
	/// The market rule for an instrument on a particular exchange provides details about how the minimum
	/// price increment changes with price.
	/// A list of market rule ids can be obtained by invoking reqContractDetails on a particular contract.
	/// The returned market rule ID list will provide the market rule ID for the instrument in the correspond
	/// valid exchange list in contractDetails.
	/// </summary>
	private ValueTask RequestMarketRule(int marketRuleId, CancellationToken cancellationToken)
	{
		return Session
			.SendMessage(RequestMessages.RequestMarketRule)
			.Send(marketRuleId)
			.SendAsync(cancellationToken);
	}

	/// <summary>
	/// Requests historical Time&amp;Sales data for an instrument.
	/// </summary>
	/// <param name="message"></param>
	/// <param name="useRth">Data from regular trading hours, or all available hours.</param>
	/// <param name="ignoreSize"></param>
	/// <param name="miscOptions">Should be defined as empty, reserved for internal use.</param>
	/// <param name="cancellationToken"><see cref="CancellationToken"/></param>
	private ValueTask RequestHistoricalTicks(MarketDataMessage message, bool useRth, bool ignoreSize, TagValue[] miscOptions, CancellationToken cancellationToken)
	{
		if (message == null)
			throw new ArgumentNullException(nameof(message));

		_histContracts.Add(message.TransactionId, message.SecurityId);

		var security = (SecurityMessage)message;

		return Session
			.SendMessage(RequestMessages.ReqHistoricalTicks)
			.Send(message.TransactionId)
			.SendContractId(security.SecurityId)
			.Send(GetSymbol(security))
			.SendSecurityType(security.SecurityType, security.UnderlyingSecurityType)
			.SendDate(security.ExpiryDate)
			.SendStrike(security.Strike)
			.SendOptionType(security.OptionType)
			.Send(security.Multiplier)
			.Send(GetExchange(security))
			.Send(GetPrimaryExchange(security))
			.SendCurrency(security.Currency)
			.Send(GetLocalSymbol(security))
			.Send(security.Class)
			.SendIncludeExpired(security.ExpiryDate)
			.Send(message.From)
			.Send(message.From == null ? message.To : null)
			.Send(message.Count ?? 1000) // Number of distinct data points. Max currently 1000 per request.
			.SendLevel1Field(GetBuildCandlesField(message))
			.Send(useRth)
			.Send(ignoreSize)
			.SendTagsNoCount(miscOptions)
			.SendAsync(cancellationToken);
	}

	/// <summary>
	/// Requests metadata from the WSH calendar.
	/// </summary>
	private ValueTask RequestWshMetaData(MarketDataMessage message, CancellationToken cancellationToken)
	{
		if (message is null)
			throw new ArgumentNullException(nameof(message));

		return Session
			.SendMessage(RequestMessages.ReqWshMetaData)
			.Send(message.TransactionId)
			.SendAsync(cancellationToken);
	}

	/// <summary>
	/// Cancels pending request for WSH metadata.
	/// </summary>
	private ValueTask CancelWshMetaData(long requestId, CancellationToken cancellationToken)
	{
		return Session
			.SendMessage(RequestMessages.CancelWshMetaData)
			.Send(requestId)
			.SendAsync(cancellationToken);
	}

	/// <summary>
	/// Requests event data from the WSH calendar.
	/// </summary>
	private ValueTask RequestWshEventData(MarketDataMessage message, CancellationToken cancellationToken)
	{
		if (message is null)
			throw new ArgumentNullException(nameof(message));

		return Session
			.SendMessage(RequestMessages.ReqWshEventData)
			.Send(message.TransactionId)
			.SendContractId(message.SecurityId)
			.SendIfEqualOrMore(ServerVersions.MinServerVerWshEventDataFilters, s =>
			{
				s.Send(string.Empty); // Filter
				s.Send(true); // FillWatchlist
				s.Send(true); // FillPortfolio
				s.Send(true); // FillCompetitors
			})
			.SendIfEqualOrMore(ServerVersions.MinServerVerWshEventDataFiltersDate, s =>
			{
				s.Send(message.From?.ToString("yyyyMMdd"));
				s.Send(message.To?.ToString("yyyyMMdd"));
				s.Send(message.Count ?? 100);
			})
			.SendAsync(cancellationToken);
	}

	/// <summary>
	/// Cancels pending WSH event data request.
	/// </summary>
	private ValueTask CancelWshEventData(long requestId, CancellationToken cancellationToken)
	{
		return Session
			.SendMessage(RequestMessages.CancelWshEventData)
			.Send(requestId)
			.SendAsync(cancellationToken);
	}

	/// <summary>
	/// Requests security definition option parameters for viewing a contract's option chain.
	/// </summary>
	/// <param name="requestId">The ID chosen for the request.</param>
	/// <param name="underlyingSymbol"></param>
	/// <param name="futFopExchange">The exchange on which the returned options are trading. Can be set to the empty string "" for all exchanges.</param>
	/// <param name="underlyingSecType">The type of the underlying security.</param>
	/// <param name="underlyingContractId">The contract ID of the underlying security.</param>
	/// <param name="cancellationToken"><see cref="CancellationToken"/></param>
	private ValueTask RequestSecurityDefinitionOptionParams(long requestId, string underlyingSymbol, string futFopExchange, SecurityTypes? underlyingSecType, int? underlyingContractId, CancellationToken cancellationToken)
	{
		return Session
			.SendMessage(RequestMessages.RequestSecurityDefinitionOptionParameters)
			.Send(requestId)
			.Send(underlyingSymbol)
			.Send(futFopExchange)
			.SendSecurityType(SecurityTypes.Option, underlyingSecType)
			.Send(underlyingContractId)
			.SendAsync(cancellationToken);
	}

	/// <summary>
	/// Requests pre-defined Soft Dollar Tiers. This is only supported for registered professional advisors and hedge
	/// and mutual funds who have configured Soft Dollar Tiers in Account Management.
	/// Refer to: https://www.interactivebrokers.com/en/software/am/am/manageaccount/requestsoftdollars.htm?Highlight=soft%20dollar%20tier
	/// </summary>
	private ValueTask RequestSoftDollarTiers(long requestId, CancellationToken cancellationToken)
	{
		return Session
			.SendMessage(RequestMessages.RequestSoftDollarTiers)
			.Send(requestId)
			.SendAsync(cancellationToken);
	}

	/// <summary>
	/// Cancels tick-by-tick data.
	/// </summary>
	private ValueTask CancelTickByTickData(long requestId, CancellationToken cancellationToken)
	{
		return Session
			.SendMessage(RequestMessages.CancelTickByTickData)
			.Send(requestId)
			.SendAsync(cancellationToken);
	}

	/// <summary>
	/// Requests tick-by-tick data.
	/// </summary>
	/// <param name="requestId">unique identifier of the request.</param>
	/// <param name="security">the contract for which tick-by-tick data is requested.</param>
	/// <param name="tickType">tick-by-tick data type: "Last", "AllLast", "BidAsk" or "MidPoint".</param>
	/// <param name="numberOfTicks">Number of ticks.</param>
	/// <param name="ignoreSize">Ignore size.</param>
	/// <param name="cancellationToken"><see cref="CancellationToken"/></param>
	private ValueTask RequestTickByTickData(long requestId, SecurityMessage security, Level1Fields tickType, int numberOfTicks, bool ignoreSize, CancellationToken cancellationToken)
	{
		string tickTypeStr;

		switch (tickType)
		{
			case TickByTickDataTypes.Last:
				tickTypeStr = "Last";
				break;

			case TickByTickDataTypes.AllLast:
				tickTypeStr = "AllLast";
				break;

			case TickByTickDataTypes.BidAsk:
				tickTypeStr = "BidAsk";
				break;

			case TickByTickDataTypes.Midpoint:
				tickTypeStr = "MidPoint";
				break;

			default:
				throw new ArgumentOutOfRangeException(nameof(tickType), tickType, LocalizedStrings.InvalidValue);
		}

		return Session
			.SendMessage(RequestMessages.ReqTickByTickData)
			.Send(requestId)
			.SendContractId(security.SecurityId)
			.Send(GetSymbol(security))
			.SendSecurityType(security.SecurityType, security.UnderlyingSecurityType)
			.SendDate(security.ExpiryDate)
			.SendStrike(security.Strike)
			.SendOptionType(security.OptionType)
			.Send(security.Multiplier)
			.Send(GetExchange(security))
			.Send(GetPrimaryExchange(security))
			.SendCurrency(security.Currency)
			.Send(GetLocalSymbol(security))
			.Send(security.Class)
			.Send(tickTypeStr)
			.SendIfEqualOrMore(ServerVersions.TickByTickIgnoreSize, s =>
			{
				s.Send(numberOfTicks);
				s.Send(ignoreSize);
			})
			.SendAsync(cancellationToken);
	}

	private SecurityId? TryGetSecurityId(long requestId)
	{
		return _requestSecIdMap.TryGetValue(requestId);
	}

	private Level1ChangeMessage GetLevel1Message(long requestId)
	{
		var secId = TryGetSecurityId(requestId);

		if (secId == null)
			return null;

		return new Level1ChangeMessage
		{
			SecurityId = secId.Value,
			ServerTime = CurrentTime,
			OriginalTransactionId = requestId,
		};
	}

	private async ValueTask ProcessTickAsync(long requestId, FieldTypes field, decimal price, decimal? volume, CancellationToken cancellationToken)
	{
		var l1Msg = GetLevel1Message(requestId);

		if (l1Msg == null)
			return;

		if (price == -1)
			price = 0;

		switch (field)
		{
			case FieldTypes.BidPrice:
			case FieldTypes.BidVolume:
				l1Msg.TryAdd(Level1Fields.BestBidPrice, price);
				l1Msg.TryAdd(Level1Fields.BestBidVolume, volume);
				break;
			case FieldTypes.AskPrice:
			case FieldTypes.AskVolume:
				l1Msg.TryAdd(Level1Fields.BestAskPrice, price);
				l1Msg.TryAdd(Level1Fields.BestAskVolume, volume);
				break;
			case FieldTypes.LastPrice:
			case FieldTypes.LastVolume:
				l1Msg.TryAdd(Level1Fields.LastTradePrice, price);
				l1Msg.TryAdd(Level1Fields.LastTradeVolume, volume);
				break;
			case FieldTypes.OpenPrice:
				l1Msg.TryAdd(Level1Fields.OpenPrice, price);
				break;
			case FieldTypes.HighPrice:
				l1Msg.TryAdd(Level1Fields.HighPrice, price);
				break;
			case FieldTypes.LowPrice:
				l1Msg.TryAdd(Level1Fields.LowPrice, price);
				break;
			case FieldTypes.ClosePrice:
				l1Msg.TryAdd(Level1Fields.ClosePrice, price);
				break;
			case FieldTypes.Volume:
				l1Msg.TryAdd(Level1Fields.Volume, volume);
				break;
			case FieldTypes.OpenInterest:
				l1Msg.TryAdd(Level1Fields.OpenInterest, volume);
				break;
			case FieldTypes.OptionHistoricalVolatility:
				l1Msg.TryAdd(Level1Fields.HistoricalVolatility, price);
				break;
			case FieldTypes.OptionImpliedVolatility:
				l1Msg.TryAdd(Level1Fields.ImpliedVolatility, price);
				break;
			case FieldTypes.LastYield:
				l1Msg.TryAdd(Level1Fields.Yield, price);
				break;
			case FieldTypes.CustOptionComputation:
				break;
			case FieldTypes.TradeCount:
				l1Msg.TryAdd(Level1Fields.TradesCount, (int)(volume ?? 0));
				break;
			//default:
			//	throw new InvalidOperationException("Неизвестный тип поля {0} для инструмента {1}.".Put(priceField, security));
		}

		await SendOutMessageAsync(l1Msg, cancellationToken);
	}

	private async ValueTask ReadTickPrice(IBSocket socket, CancellationToken cancellationToken)
	{
		// http://www.interactivebrokers.com/en/software/api/apiguide/java/tickprice.htm

		var version = await socket.ReadVersionAsync(cancellationToken);
		var requestId = await socket.ReadIntAsync(cancellationToken);
		var priceField = (FieldTypes)await socket.ReadIntAsync(cancellationToken);
		var price = await socket.ReadDecimalAsync(cancellationToken);
		var volume = version >= ServerVersions.V2 ? await socket.ReadDecimalAsync(cancellationToken) : (decimal?)null;
		var attr = version >= ServerVersions.V3 ? await socket.ReadIntAsync(cancellationToken) : (int?)null;

		if (attr != null)
		{
			//attr.CanAutoExecute = attr.Value == 1;

			if (socket.ServerVersion >= ServerVersions.PastLimit)
			{
				//BitMask mask = new BitMask(attr.Value);

				//attr.CanAutoExecute = mask[0];
				//attr.PastLimit = mask[1];

				if (socket.ServerVersion >= ServerVersions.PreOpenBidAsk)
				{
					//attr.PreOpen = mask[2];
				}
			}
		}

		await ProcessTickAsync(requestId, priceField, price, volume, cancellationToken);
	}

	private async ValueTask ReadTickVolume(IBSocket socket, CancellationToken cancellationToken)
	{
		// http://www.interactivebrokers.com/en/software/api/apiguide/java/ticksize.htm

		/*var version = */
		await socket.ReadVersionAsync(cancellationToken);
		var requestId = await socket.ReadIntAsync(cancellationToken);
		var volumeType = (FieldTypes)await socket.ReadIntAsync(cancellationToken);
		var volume = await socket.ReadDecimalAsync(cancellationToken);

		await ProcessTickAsync(requestId, volumeType, 0, volume, cancellationToken);
	}

	private async ValueTask ReadTickOptionComputation(IBSocket socket, CancellationToken cancellationToken)
	{
		// http://www.interactivebrokers.com/en/software/api/apiguide/java/tickoptioncomputation.htm

		var version = socket.ServerVersion >= ServerVersions.PriceBasedVolatility ? (ServerVersions)int.MaxValue : await socket.ReadVersionAsync(cancellationToken);
		var requestId = await socket.ReadIntAsync(cancellationToken);
		var fieldType = (FieldTypes)await socket.ReadIntAsync(cancellationToken);
		var tickAttrib = int.MaxValue;
		if (socket.ServerVersion >= ServerVersions.PriceBasedVolatility)
		{
			tickAttrib = await socket.ReadIntAsync(cancellationToken);
		}
		decimal? impliedVol = await socket.ReadDecimalAsync(cancellationToken);
		if (impliedVol == -1)
		{
			// -1 is the "not yet computed" indicator
			impliedVol = null;
		}

		decimal? delta = await socket.ReadDecimalAsync(cancellationToken);
		if (delta == -2)
		{
			// -2 is the "not yet computed" indicator
			delta = null;
		}

		decimal? optPrice = null;
		decimal? pvDividend = null;
		decimal? gamma = null;
		decimal? vega = null;
		decimal? theta = null;
		decimal? underlyingPrice;

		if (version >= ServerVersions.V6 || fieldType == FieldTypes.ModelOption || fieldType == FieldTypes.DelayedModelOption)
		{
			// introduced in version == 5
			optPrice = await socket.ReadDecimalAsync(cancellationToken);
			if (optPrice == -1)
			{
				// -1 is the "not yet computed" indicator
				optPrice = null;
			}

			pvDividend = await socket.ReadDecimalAsync(cancellationToken);
			if (pvDividend == -1)
			{
				// -1 is the "not yet computed" indicator
				pvDividend = null;
			}
		}

		if (version >= ServerVersions.V6)
		{
			gamma = await socket.ReadDecimalAsync(cancellationToken);
			if (gamma.Value == -2)
			{
				// -2 is the "not yet computed" indicator
				gamma = null;
			}

			vega = await socket.ReadDecimalAsync(cancellationToken);
			if (vega.Value == -2)
			{
				// -2 is the "not yet computed" indicator
				vega = null;
			}

			theta = await socket.ReadDecimalAsync(cancellationToken);
			if (theta.Value == -2)
			{
				// -2 is the "not yet computed" indicator
				theta = null;
			}

			underlyingPrice = await socket.ReadDecimalAsync(cancellationToken);
			if (underlyingPrice == -1)
			{
				// -1 is the "not yet computed" indicator
				underlyingPrice = null;
			}
		}

		var l1Msg = GetLevel1Message(requestId);

		if (l1Msg == null)
			return;

		l1Msg
			.TryAdd(Level1Fields.Delta, delta)
			.TryAdd(Level1Fields.Gamma, gamma)
			.TryAdd(Level1Fields.Vega, vega)
			.TryAdd(Level1Fields.Theta, theta)
			.TryAdd(Level1Fields.ImpliedVolatility, impliedVol)
			.TryAdd(Level1Fields.TheorPrice, optPrice)
			.TryAdd(Level1Fields.Yield, pvDividend);

		await SendOutMessageAsync(l1Msg, cancellationToken);

		//tickOptionComputation(requestId, tickType, tickAttrib, impliedVol, delta, optPrice, pvDividend, gamma, vega, theta,
		//                      undPrice);
	}

	private async ValueTask ReadTickGeneric(IBSocket socket, CancellationToken cancellationToken)
	{
		// http://www.interactivebrokers.com/en/software/api/apiguide/java/tickgeneric.htm

		/*var version = */
		await socket.ReadVersionAsync(cancellationToken);
		var requestId = await socket.ReadIntAsync(cancellationToken);
		var field = (FieldTypes)await socket.ReadIntAsync(cancellationToken);
		var valueRenamed = await socket.ReadDecimalAsync(cancellationToken);

		await ProcessTickAsync(requestId, field, valueRenamed, null, cancellationToken);

		//tickGeneric(requestId, (FieldTypes) tickType, valueRenamed);
	}

	private async ValueTask ReadTickString(IBSocket socket, CancellationToken cancellationToken)
	{
		// http://www.interactivebrokers.com/en/software/api/apiguide/java/tickstring.htm

		/*var version = */await socket.ReadVersionAsync(cancellationToken);
		var requestId = await socket.ReadIntAsync(cancellationToken);
		var field = (FieldTypes)await socket.ReadIntAsync(cancellationToken);
		var value = await socket.ReadStringAsync(cancellationToken);

		//GetLevel1Message(requestId);
		//tickString(requestId, fieldType, value);
	}

	private async ValueTask ReadTickEfp(IBSocket socket, CancellationToken cancellationToken)
	{
		// http://www.interactivebrokers.com/en/software/api/apiguide/java/tickefp.htm

		/*var version = */
		await socket.ReadVersionAsync(cancellationToken);
		var requestId = await socket.ReadIntAsync(cancellationToken);
		var fieldType = (FieldTypes)await socket.ReadIntAsync(cancellationToken);
		var basisPoints = await socket.ReadDecimalAsync(cancellationToken);
		var formattedBasisPoints = await socket.ReadStringAsync(cancellationToken);
		var impliedFuturesPrice = await socket.ReadDecimalAsync(cancellationToken);
		var holdDays = await socket.ReadIntAsync(cancellationToken);
		var futureExpiry = await socket.ReadStringAsync(cancellationToken);
		var dividendImpact = await socket.ReadDecimalAsync(cancellationToken);
		var dividendsToExpiry = await socket.ReadDecimalAsync(cancellationToken);
		//tickEfp(requestId, fieldType, basisPoints, formattedBasisPoints, impliedFuturesPrice,
		//        holdDays, futureExpiry, dividendImpact, dividendsToExpiry);

		var l1Msg = GetLevel1Message(requestId);

		if (l1Msg == null)
			return;

		l1Msg
			.TryAdd(Level1Fields.StepPrice, basisPoints)
			.TryAdd(Level1Fields.Yield, dividendsToExpiry);

		await SendOutMessageAsync(l1Msg, cancellationToken);
	}

	private async ValueTask ReadScannerData(IBSocket socket, CancellationToken cancellationToken)
	{
		var version = await socket.ReadVersionAsync(cancellationToken);
		var requestId = await socket.ReadIntAsync(cancellationToken);
		var count = await socket.ReadIntAsync(cancellationToken);

		var tmp = new List<(int Rank, int? ContractId, string Symbol, string LocalSymbol, string Type, string ExpiryDate, decimal? Strike, OptionTypes? OptionType, string Exchange, string Currency, string MarketName, string SecClass, string Distance, string Benchmark, string Projection, string Legs)>();

		for (int i = 0; i < count; i++)
		{
			var rank = await socket.ReadIntAsync(cancellationToken);
			var contractId = version >= ServerVersions.V3 ? await socket.ReadIntAsync(cancellationToken) : (int?)null;

			var symbol = await socket.ReadStringAsync(cancellationToken);
			var type = await socket.ReadStringAsync(cancellationToken);
			var expiryDate = await socket.ReadStringAsync(cancellationToken);
			var strike = await socket.ReadStrikeAsync(cancellationToken);
			var optionType = await socket.ReadOptionTypeAsync(cancellationToken);
			var exchange = await socket.ReadStringAsync(cancellationToken);
			var currency = await socket.ReadStringAsync(cancellationToken);
			var localSymbol = await socket.ReadStringAsync(cancellationToken);
			var marketName = await socket.ReadStringAsync(cancellationToken);
			var secClass = await socket.ReadStringAsync(cancellationToken);

			var distance = await socket.ReadStringAsync(cancellationToken);
			var benchmark = await socket.ReadStringAsync(cancellationToken);
			var projection = await socket.ReadStringAsync(cancellationToken);
			var legs = version >= ServerVersions.V2 ? await socket.ReadStringAsync(cancellationToken) : null;

			tmp.Add((rank, contractId, symbol, localSymbol, type, expiryDate, strike, optionType, exchange, currency, marketName, secClass, distance, benchmark, projection, legs));
		}

		var results = new ScannerResult[tmp.Count];

		for (int i = 0; i < tmp.Count; i++)
		{
			var t = tmp[i];

			var secId = new SecurityId
			{
				SecurityCode = GetSecurityCode(t.Symbol, t.Type, t.Currency, t.LocalSymbol, t.ExpiryDate),
				BoardCode = GetBoardCode(t.Exchange),
				InteractiveBrokers = t.ContractId,
			};

			await SendOutMessageAsync(new SecurityMessage
			{
				SecurityId = secId,
				SecurityType = t.Type.ToSecurityType(this, out var underlyingSecurityType),
				UnderlyingSecurityType = underlyingSecurityType,
				ExpiryDate = t.ExpiryDate.ReadDateTime(this, out _),
				Strike = t.Strike,
				OptionType = t.OptionType,
				Currency = ToCurrency(t.Currency),
				Class = t.SecClass,
				PrimaryId = new SecurityId
				{
					SecurityCode = t.LocalSymbol,
					BoardCode = GetBoardCode(null),
				}
			}, cancellationToken);

			results[i] = new ScannerResult
			{
				Rank = t.Rank,
				SecurityId = secId,
				Distance = t.Distance,
				Benchmark = t.Benchmark,
				Projection = t.Projection,
				Legs = t.Legs
			};
		}

		await SendOutMessageAsync(new ScannerResultMessage
		{
			Results = results,
			OriginalTransactionId = requestId,
		}, cancellationToken);
	}

	private async ValueTask ReadSecurityInfo(IBSocket socket, CancellationToken cancellationToken)
	{
		var version = socket.ServerVersion < ServerVersions.SizeRules ? await socket.ReadVersionAsync(cancellationToken) : ServerVersions.V8;
		var requestId = version >= ServerVersions.V3 ? await socket.ReadIntAsync(cancellationToken) : (int?)null;

		var symbol = await socket.ReadStringAsync(cancellationToken);
		var type = await socket.ReadStringAsync(cancellationToken);
		var expiryDate = await socket.ReadStringAsync(cancellationToken);
		var lastTradeDate = (socket.ServerVersion >= ServerVersions.MinServerVerLastTradeDate) ? await socket.ReadStringAsync(cancellationToken) : string.Empty;
		var strike = await socket.ReadStrikeAsync(cancellationToken);
		var optionType = await socket.ReadOptionTypeAsync(cancellationToken);
		var exchange = await socket.ReadStringAsync(cancellationToken);
		var currency = await socket.ReadStringAsync(cancellationToken);
		var localSymbol = await socket.ReadStringAsync(cancellationToken);
		var marketName = await socket.ReadStringAsync(cancellationToken);
		var secClass = await socket.ReadStringAsync(cancellationToken);
		var contractId = await socket.ReadIntAsync(cancellationToken);
		var priceStep = await socket.ReadDecimalAsync(cancellationToken);
		var mdSizeMultiplier = (socket.ServerVersion >= ServerVersions.MarketDepthMultiplier && socket.ServerVersion < ServerVersions.SizeRules) ? await socket.ReadIntAsync(cancellationToken) : (decimal?)null;
		var multiplier = await socket.ReadNullDecimalAsync(cancellationToken);
		var orderTypes = await socket.ReadStringAsync(cancellationToken);
		var validExchanges = await socket.ReadStringAsync(cancellationToken);
		var priceMagnifier = version >= ServerVersions.V2 ? await socket.ReadIntAsync(cancellationToken) : (int?)null;
		var underlyingSecurityNativeId = version >= ServerVersions.V4 ? await socket.ReadIntAsync(cancellationToken) : (int?)null;

		var longName = version >= ServerVersions.V5 ? await socket.ReadStringAsync(cancellationToken) : null;

		if (longName != null)
		{
			if (socket.ServerVersion >= ServerVersions.EncodeMsgASCII7)
				longName = Regex.Unescape(longName);
		}

		var primaryExch = version >= ServerVersions.V5 ? await socket.ReadStringAsync(cancellationToken) : null;

		var contractMonth = version >= ServerVersions.V6 ? await socket.ReadStringAsync(cancellationToken) : null;
		var industry = version >= ServerVersions.V6 ? await socket.ReadStringAsync(cancellationToken) : null;
		var category = version >= ServerVersions.V6 ? await socket.ReadStringAsync(cancellationToken) : null;
		var subCategory = version >= ServerVersions.V6 ? await socket.ReadStringAsync(cancellationToken) : null;
		var timeZoneId = version >= ServerVersions.V6 ? await socket.ReadStringAsync(cancellationToken) : null;
		var tradingHours = version >= ServerVersions.V6 ? await socket.ReadStringAsync(cancellationToken) : null;
		var liquidHours = version >= ServerVersions.V6 ? await socket.ReadStringAsync(cancellationToken) : null;

		var evRule = version >= ServerVersions.V8 ? await socket.ReadStringAsync(cancellationToken) : null;
		var evMultiplier = version >= ServerVersions.V8 ? await socket.ReadDecimalAsync(cancellationToken) : (decimal?)null;

		var secId = new SecurityId
		{
			SecurityCode = GetSecurityCode(symbol, type, currency, localSymbol, expiryDate),
			BoardCode = GetBoardCode(exchange),
			InteractiveBrokers = contractId,
		};

		if (version >= ServerVersions.V7)
			await socket.ReadSecurityIdAsync(secId, cancellationToken);

		if (socket.ServerVersion >= ServerVersions.AggGroup)
		{
			/*contract.AggGroup = */await socket.ReadIntAsync(cancellationToken);
		}

		if (socket.ServerVersion >= ServerVersions.UnderlyingInfo)
		{
			/*contract.UnderSymbol = */await socket.ReadStringAsync(cancellationToken);
			/*contract.UnderSecType = */await socket.ReadStringAsync(cancellationToken);
		}
		if (socket.ServerVersion >= ServerVersions.MarketRules)
		{
			/*contract.MarketRuleIds = */await socket.ReadStringAsync(cancellationToken);
		}
		if (socket.ServerVersion >= ServerVersions.RealExpDate)
		{
			/*contract.RealExpirationDate = */await socket.ReadStringAsync(cancellationToken);
		}

		if (socket.ServerVersion >= ServerVersions.StockType)
		{
			/*contract.StockType = */await socket.ReadStringAsync(cancellationToken);
		}

		decimal? volumeStep = null;

		if (socket.ServerVersion >= ServerVersions.FractionalSizeSupport && socket.ServerVersion < ServerVersions.SizeRules)
		{
			volumeStep = await socket.ReadDecimalAsync(cancellationToken);
		}

		decimal? minSize = null;
		decimal? sizeIncrement = null;
		decimal? suggestedSizeIncrement = null;

		if (socket.ServerVersion >= ServerVersions.SizeRules)
		{
			minSize = await socket.ReadDecimalAsync(cancellationToken);
			sizeIncrement = await socket.ReadDecimalAsync(cancellationToken);
			suggestedSizeIncrement = await socket.ReadDecimalAsync(cancellationToken);
		}

		if (socket.ServerVersion >= ServerVersions.MinServerVerFundDataFields && type == "FUND")
		{
			/*contract.FundName = */await socket.ReadStringAsync(cancellationToken);
			/*contract.FundFamily = */await socket.ReadStringAsync(cancellationToken);
			/*contract.FundType = */await socket.ReadStringAsync(cancellationToken);
			/*contract.FundFrontLoad = */await socket.ReadStringAsync(cancellationToken);
			/*contract.FundBackLoad = */await socket.ReadStringAsync(cancellationToken);
			/*contract.FundBackLoadTimeInterval = */await socket.ReadStringAsync(cancellationToken);
			/*contract.FundManagementFee = */await socket.ReadStringAsync(cancellationToken);
			/*contract.FundClosed = */await socket.ReadBoolAsync(cancellationToken);
			/*contract.FundClosedForNewInvestors = */await socket.ReadBoolAsync(cancellationToken);
			/*contract.FundClosedForNewMoney = */await socket.ReadBoolAsync(cancellationToken);
			/*contract.FundNotifyAmount = */await socket.ReadStringAsync(cancellationToken);
			/*contract.FundMinimumInitialPurchase = */await socket.ReadStringAsync(cancellationToken);
			/*contract.FundSubsequentMinimumPurchase = */await socket.ReadStringAsync(cancellationToken);
			/*contract.FundBlueSkyStates = */await socket.ReadStringAsync(cancellationToken);
			/*contract.FundBlueSkyTerritories = */await socket.ReadStringAsync(cancellationToken);
			/*contract.FundDistributionPolicyIndicator = */await socket.ReadStringAsync(cancellationToken);
			/*contract.FundAssetType = */await socket.ReadStringAsync(cancellationToken);
		}

		if (socket.ServerVersion >= ServerVersions.MinServerVerIneligibilityReasons)
		{
			var ineligibilityReasonCount = await socket.ReadIntAsync(cancellationToken);

			if (ineligibilityReasonCount > 0)
			{
				for (var i = 0; i < ineligibilityReasonCount; ++i)
				{
					await socket.ReadStringAsync(cancellationToken);
					await socket.ReadStringAsync(cancellationToken);
					//var ineligibilityReason = new IneligibilityReason
					//{
					//	Id = socket.ReadString(),
					//	Description = socket.ReadString()
					//};

					//contract.IneligibilityReasonList.Add(ineligibilityReason);
				}
			}
		}

		var secMsg = new SecurityMessage
		{
			SecurityId = secId,
			SecurityType = type.ToSecurityType(this, out var underlyingSecurityType),
			UnderlyingSecurityType = underlyingSecurityType,
			ExpiryDate = expiryDate.ReadDateTime(this, out _),
			Strike = strike,
			OptionType = optionType,
			Currency = ToCurrency(currency),
			Multiplier = multiplier ?? 0,
			Class = secClass,
			OriginalTransactionId = requestId ?? 0,
			PriceStep = priceStep,
			PrimaryId = new SecurityId
			{
				SecurityCode = localSymbol,
				BoardCode = GetBoardCode(primaryExch),
			},
			Name = longName,
			VolumeStep = volumeStep,
			MinVolume = minSize,
		};

		//secMsg.SetMarketName(marketName);
		//secMsg.SetOrderTypes(orderTypes);
		//secMsg.SetValidExchanges(validExchanges);

		//if (priceMagnifier != null)
		//	secMsg.SetPriceMagnifier(priceMagnifier.Value);

		//if (contractMonth != null)
		//	secMsg.SetContractMonth(contractMonth);

		//if (industry != null)
		//	secMsg.SetIndustry(industry);

		//if (category != null)
		//	secMsg.SetCategory(category);

		//if (subCategory != null)
		//	secMsg.SetSubCategory(subCategory);

		//if (timeZoneId != null)
		//	secMsg.SetTimeZoneId(timeZoneId);

		//if (tradingHours != null)
		//	secMsg.SetTradingHours(tradingHours);

		//if (liquidHours != null)
		//	secMsg.SetLiquidHours(liquidHours);

		//if (evRule != null)
		//	secMsg.SetEvRule(evRule);

		//if (evMultiplier != null)
		//	secMsg.SetEvMultiplier(evMultiplier.Value);

		// TODO
		//if (underlyingSecurityNativeId != null)
		//	ProcessSecurityAction(null, SecurityIdGenerator.GenerateId(underlyingSecurityNativeId.Value.To<string>(), exchangeBoard), underSec => security.UnderlyingSecurityId = underSec.Id);

		await SendOutMessageAsync(secMsg, cancellationToken);
	}

	private async ValueTask ReadBondInfo(IBSocket socket, CancellationToken cancellationToken)
	{
		// http://www.interactivebrokers.com/en/software/api/apiguide/java/bondcontractdetails.htm

		var version = socket.ServerVersion < ServerVersions.SizeRules ? await socket.ReadVersionAsync(cancellationToken) : ServerVersions.V6;

		var requestId = version >= ServerVersions.V3 ? await socket.ReadIntAsync(cancellationToken) : (int?)null;

		var symbol = await socket.ReadStringAsync(cancellationToken);
		var type = await socket.ReadStringAsync(cancellationToken);
		var cusip = await socket.ReadStringAsync(cancellationToken);
		var coupon = await socket.ReadDecimalAsync(cancellationToken);
		var maturity = await socket.ReadStringAsync(cancellationToken);
		var issueDate = await socket.ReadNullDateAsync(cancellationToken);
		var ratings = await socket.ReadStringAsync(cancellationToken);
		var bondType = await socket.ReadStringAsync(cancellationToken);
		var couponType = await socket.ReadStringAsync(cancellationToken);
		var convertible = await socket.ReadBoolAsync(cancellationToken);
		var callable = await socket.ReadBoolAsync(cancellationToken);
		var putable = await socket.ReadBoolAsync(cancellationToken);
		var description = await socket.ReadStringAsync(cancellationToken);
		var exchange = await socket.ReadStringAsync(cancellationToken);
		var currency = await socket.ReadStringAsync(cancellationToken);
		var marketName = await socket.ReadStringAsync(cancellationToken);
		var secClass = await socket.ReadStringAsync(cancellationToken);
		var contractId = await socket.ReadIntAsync(cancellationToken);
		var priceStep = await socket.ReadDecimalAsync(cancellationToken);
		var mdSizeMultiplier = socket.ServerVersion >= ServerVersions.MarketDepthMultiplier ? await socket.ReadIntAsync(cancellationToken) : (decimal?)null;
		var orderTypes = await socket.ReadStringAsync(cancellationToken);
		var validExchanges = await socket.ReadStringAsync(cancellationToken);

		var nextOptionDate = version >= ServerVersions.V2 ? await socket.ReadStringAsync(cancellationToken) : null;
		var nextOptionType = version >= ServerVersions.V2 ? await socket.ReadStringAsync(cancellationToken) : null;
		var nextOptionPartial = version >= ServerVersions.V2 ? await socket.ReadBoolAsync(cancellationToken) : (bool?)null;
		var notes = version >= ServerVersions.V2 ? await socket.ReadStringAsync(cancellationToken) : null;

		var longName = version >= ServerVersions.V4 ? await socket.ReadStringAsync(cancellationToken) : null;

		var evRule = version >= ServerVersions.V6 ? await socket.ReadStringAsync(cancellationToken) : null;
		var evMultiplier = version >= ServerVersions.V6 ? await socket.ReadDecimalAsync(cancellationToken) : (decimal?)null;

		var secId = new SecurityId
		{
			SecurityCode = GetSecurityCode(symbol, type, currency, null, maturity),
			BoardCode = GetBoardCode(exchange),
			InteractiveBrokers = contractId,
			Cusip = cusip,
		};

		if (version >= ServerVersions.V5)
			await socket.ReadSecurityIdAsync(secId, cancellationToken);

		if (socket.ServerVersion >= ServerVersions.AggGroup)
		{
			/*contract.AggGroup = */await socket.ReadIntAsync(cancellationToken);
		}

		if (socket.ServerVersion >= ServerVersions.MarketRules)
		{
			/*contract.MarketRuleIds = */await socket.ReadStringAsync(cancellationToken);
		}

		decimal? minSize = null;
		decimal? sizeIncrement = null;
		decimal? suggestedSizeIncrement = null;

		if (socket.ServerVersion >= ServerVersions.SizeRules)
		{
			minSize = await socket.ReadDecimalAsync(cancellationToken);
			sizeIncrement = await socket.ReadDecimalAsync(cancellationToken);
			suggestedSizeIncrement = await socket.ReadDecimalAsync(cancellationToken);
		}

		var secMsg = new SecurityMessage
		{
			SecurityId = secId,
			SecurityType = type.ToSecurityType(this, out var underlyingSecurityType),
			UnderlyingSecurityType = underlyingSecurityType,
			Currency = ToCurrency(currency),
			Class = secClass,
			PriceStep = priceStep,
			Multiplier = mdSizeMultiplier,
			Name = description,
			OriginalTransactionId = requestId ?? 0,
			IssueDate = issueDate,
			ExpiryDate = maturity.ReadDateTime(this, out _),
			MinVolume = minSize,
		};

		// TODO
		//secMsg.SetMarketName(marketName);
		//secMsg.SetOrderTypes(orderTypes);
		//secMsg.SetValidExchanges(validExchanges);
		//secMsg.SetCoupon(coupon);
		//secMsg.SetMaturity(maturity);
		//secMsg.SetIssueDate(issueDate);
		//secMsg.SetRatings(ratings);
		//secMsg.SetBondType(bondType);
		//secMsg.SetCouponType(couponType);
		//secMsg.SetConvertible(convertible);
		//secMsg.SetCallable(callable);
		//secMsg.SetPutable(putable);

		//if (nextOptionDate != null)
		//	secMsg.SetNextOptionDate(nextOptionDate);

		//if (nextOptionType != null)
		//	secMsg.SetNextOptionType(nextOptionType);

		//if (nextOptionPartial != null)
		//	secMsg.SetNextOptionPartial(nextOptionPartial.Value);

		//if (notes != null)
		//	secMsg.SetNotes(notes);

		//if (evRule != null)
		//	secMsg.SetEvRule(evRule);

		//if (evMultiplier != null)
		//	secMsg.SetEvMultiplier(evMultiplier.Value);

		await SendOutMessageAsync(secMsg, cancellationToken);
	}

	private async ValueTask ReadMarketDepth(IBSocket socket, ResponseMessages message, CancellationToken cancellationToken)
	{
		// http://www.interactivebrokers.com/en/software/api/apiguide/java/updatemktdepth.htm
		// http://www.interactivebrokers.com/en/software/api/apiguide/java/updatemktdepthl2.htm

		/*var version = */await socket.ReadVersionAsync(cancellationToken);
		var requestId = await socket.ReadIntAsync(cancellationToken);

		var secId = TryGetSecurityId(requestId);

		var pos = await socket.ReadIntAsync(cancellationToken);

		var mm = message == ResponseMessages.MarketDepthL2 ? await socket.ReadStringAsync(cancellationToken) : null;

		var operation = await socket.ReadIntAsync(cancellationToken);
		var side = await socket.ReadBoolAsync(cancellationToken) ? Sides.Buy : Sides.Sell;
		var price = await socket.ReadDecimalAsync(cancellationToken);
		var volume = await socket.ReadDecimalAsync(cancellationToken);

		if (message == ResponseMessages.MarketDepthL2)
		{
			var isSmartDepth = socket.ServerVersion >= ServerVersions.SmartDepth && await socket.ReadBoolAsync(cancellationToken);
		}

		if (pos < 0)
			throw new InvalidOperationException($"pos={pos}");

		var prevQuotes = _depths.SafeAdd(requestId, key =>
			Tuple.Create(new List<Tuple<decimal, decimal, string>>(), new List<Tuple<decimal, decimal, string>>()));

		var quotes = side == Sides.Buy ? prevQuotes.Item1 : prevQuotes.Item2;

		this.AddVerboseLog("MD {0} {1} POS {2} PRICE {3} VOL {4}", secId, operation, pos, price, volume);

		var todo = false;

		if (operation == 0 || operation == 1)
		{
			var mult = side == Sides.Buy ? -1 : 1;

			if (pos > 0 && quotes.Count > (pos - 1) && quotes[pos - 1].Item1 * mult >= price * mult)
			{
				todo = true;
			}

			if (pos < (quotes.Count - 1) && price * mult >= quotes[pos + 1].Item1 * mult)
			{
				todo = true;
			}
		}

		switch (operation)
		{
			case 0: // insert
			{
				var level = Tuple.Create(price, (decimal)volume, mm);

				if (quotes.Count <= pos)
					quotes.Add(level);
				else
					quotes.Insert(pos, level);

				break;
			}
			case 1: // update
			{
				var level = Tuple.Create(price, (decimal)volume, mm);

				if (quotes.Count <= pos)
					quotes.Add(level);
				else
					quotes[pos] = level;

				break;
			}
			case 2: // delete
			{
				if (quotes.Count > pos)
					quotes.RemoveAt(pos);
				else if (quotes.Count > 0)
					quotes.RemoveAt(quotes.Count - 1);

				break;
			}

			default:
				throw new InvalidOperationException($"operation={operation}");
		}

		if (todo || secId == null)
			return;

		static QuoteChange[] ToQuotes(IEnumerable<Tuple<decimal, decimal, string>> list, bool orderBy)
		{
			var quotes = list
				   .GroupBy(t => t.Item1)
				   .Select(g => new QuoteChange(g.Key, g.Sum(t => t.Item2))
				   {
					   BoardCode = g.Select(t => t.Item3).First()
				   });

			quotes = orderBy ? quotes.OrderBy(q => q.Price) : quotes.OrderByDescending(q => q.Price);

			return [.. quotes];
		}

		await SendOutMessageAsync(new QuoteChangeMessage
		{
			SecurityId = secId.Value,
			Bids = ToQuotes(prevQuotes.Item1, false),
			Asks = ToQuotes(prevQuotes.Item2, true),
			ServerTime = CurrentTime,
		}, cancellationToken);
	}

	private async ValueTask ReadNewsBulletins(IBSocket socket, CancellationToken cancellationToken)
	{
		// http://www.interactivebrokers.com/en/software/api/apiguide/java/updatenewsbulletin.htm

		/*var version = */await socket.ReadVersionAsync(cancellationToken);
		var newsId = await socket.ReadIntAsync(cancellationToken);
		// 1 - Regular news bulletin 2 - Exchange no longer available for trading 3 - Exchange is available for trading
		var newsType = await socket.ReadIntAsync(cancellationToken);
		var newsMessage = await socket.ReadStringAsync(cancellationToken);
		var originatingExch = await socket.ReadStringAsync(cancellationToken);

		await SendOutMessageAsync(new NewsMessage
		{
			Id = newsId.To<string>(),
			BoardCode = originatingExch,
			Headline = newsMessage,
			Priority = newsType == 1 ? NewsPriorities.Regular : NewsPriorities.High,
			ServerTime = CurrentTime
		}, cancellationToken);
	}

	private async ValueTask ReadHistoricalData(IBSocket socket, CancellationToken cancellationToken)
	{
		// http://www.interactivebrokers.com/en/software/api/apiguide/java/historicaldata.htm

		var version = socket.ServerVersion < ServerVersions.SyntRealtimeBars ? await socket.ReadVersionAsync(cancellationToken) : (ServerVersions)int.MaxValue;
		var requestId = await socket.ReadIntAsync(cancellationToken);

		if (version >= ServerVersions.V2)
		{
			//Read Start Date String
			/*String startDateStr = */
			await socket.ReadStringAsync(cancellationToken);
			/*String endDateStr   = */
			await socket.ReadStringAsync(cancellationToken);
			//completedIndicator += ("-" + startDateStr + "-" + endDateStr);
		}

		var secId = TryGetSecurityId(requestId);

		var itemCount = await socket.ReadIntAsync(cancellationToken);

		for (var i = 0; i < itemCount; i++)
		{
			var time = await socket.ReadTimeExAsync(cancellationToken);
			var open = await socket.ReadDecimalAsync(cancellationToken);
			var high = await socket.ReadDecimalAsync(cancellationToken);
			var low = await socket.ReadDecimalAsync(cancellationToken);
			var close = await socket.ReadDecimalAsync(cancellationToken);
			var volume = await socket.ReadDecimalAsync(cancellationToken);
			var wap = await socket.ReadDecimalAsync(cancellationToken);

			if (socket.ServerVersion < ServerVersions.SyntRealtimeBars)
			{
				/* hasGaps */
				(await socket.ReadStringAsync(cancellationToken)).To<bool>();
			}

			int? barCount = null;

			if (version >= ServerVersions.V3)
				barCount = await socket.ReadIntAsync(cancellationToken);

			if (volume == -1)
				volume = 0;

			await SendOutMessageAsync(new TimeFrameCandleMessage
			{
				OpenPrice = open,
				HighPrice = high,
				LowPrice = low,
				ClosePrice = close,
				TotalVolume = volume,
				OpenTime = time,
				//TotalTicks = barCount,
				SecurityId = secId ?? default,
				OriginalTransactionId = requestId,
				State = (i + 1) < itemCount ? CandleStates.Finished : CandleStates.Active,
			}, cancellationToken);
		}

		if (!_realTimeSubscriptions.ContainsKey(requestId))
			await SendSubscriptionFinishedAsync(requestId, cancellationToken);
	}

	private async ValueTask ReadScannerParameters(IBSocket socket, CancellationToken cancellationToken)
	{
		// http://www.interactivebrokers.com/en/software/api/apiguide/java/scannerparameters.htm

		/*var version = */await socket.ReadVersionAsync(cancellationToken);
		var xml = await socket.ReadStringAsync(cancellationToken);
		await SendOutMessageAsync(new ScannerParametersMessage { Parameters = xml }, cancellationToken);
	}

	private async ValueTask ReadRealTimeBars(IBSocket socket, CancellationToken cancellationToken)
	{
		// http://www.interactivebrokers.com/en/software/api/apiguide/java/realtimebar.htm

		/*var version = */await socket.ReadVersionAsync(cancellationToken);

		var requestId = await socket.ReadIntAsync(cancellationToken);
		var time = await socket.ReadUnixDateTimeAsync(cancellationToken);
		var open = await socket.ReadDecimalAsync(cancellationToken);
		var high = await socket.ReadDecimalAsync(cancellationToken);
		var low = await socket.ReadDecimalAsync(cancellationToken);
		var close = await socket.ReadDecimalAsync(cancellationToken);
		var volume = await socket.ReadDecimalAsync(cancellationToken);
		var wap = await socket.ReadDecimalAsync(cancellationToken);
		var count = await socket.ReadIntAsync(cancellationToken);

		if (volume == -1)
			volume = 0;

		await SendOutMessageAsync(new TimeFrameCandleMessage
		{
			OpenPrice = open,
			HighPrice = high,
			LowPrice = low,
			ClosePrice = close,
			TotalVolume = volume,
			OpenTime = time,
			//CloseVolume = count,
			SecurityId = TryGetSecurityId(requestId) ?? default,
			OriginalTransactionId = requestId,

			State = CandleStates.Active,
		}, cancellationToken);

		//realTimeBar(requestId, time, open, high, low, close, volume, wap, count);
	}

	private async ValueTask ReadFundamentalData(IBSocket socket, CancellationToken cancellationToken)
	{
		// http://www.interactivebrokers.com/en/software/api/apiguide/java/fundamentaldata.htm

		/*var version = */await socket.ReadVersionAsync(cancellationToken);

		var requestId = await socket.ReadIntAsync(cancellationToken);
		var data = await socket.ReadStringAsync(cancellationToken);

		await SendOutMessageAsync(new FundamentalReportMessage
		{
			Data = data,
			OriginalTransactionId = requestId,
		}, cancellationToken);
	}

	private async ValueTask ReadSecurityInfoEnd(IBSocket socket, CancellationToken cancellationToken)
	{
		/*var version = */await socket.ReadVersionAsync(cancellationToken);
		var requestId = await socket.ReadIntAsync(cancellationToken);
		await SendSubscriptionFinishedAsync(requestId, cancellationToken);
	}

	private async ValueTask ReadDeltaNuetralValidation(IBSocket socket, CancellationToken cancellationToken)
	{
		/*var version = */await socket.ReadVersionAsync(cancellationToken);
		/*var requestId = */await socket.ReadIntAsync(cancellationToken);

		//DeltaNeutralContract deltaNeutralContract = new DeltaNeutralContract();
		//deltaNeutralContract.ConId =
		await socket.ReadIntAsync(cancellationToken);
		//deltaNeutralContract.Delta =
		await socket.ReadDecimalAsync(cancellationToken);
		//deltaNeutralContract.Price =
		await socket.ReadDecimalAsync(cancellationToken);

		//deltaNuetralValidation(requestId, deltaNeutralContract);
	}

	private async ValueTask ReadTickSnapshotEnd(IBSocket socket, CancellationToken cancellationToken)
	{
		// http://www.interactivebrokers.com/en/software/api/apiguide/java/ticksnapshotend.htm

		/*var version = */await socket.ReadVersionAsync(cancellationToken);
		/*var requestId = */await socket.ReadIntAsync(cancellationToken);
		//SendOutMessage(_level1Messages.GetAndRemove(requestId));
	}

	private async ValueTask ReadMarketDataType(IBSocket socket, CancellationToken cancellationToken)
	{
		// http://www.interactivebrokers.com/en/software/api/apiguide/java/marketdatatype.htm

		/*var version = */await socket.ReadVersionAsync(cancellationToken);
		/* requestId */await socket.ReadIntAsync(cancellationToken);

		MarketDataType = (InteractiveBrokersMarketDataTypes)await socket.ReadIntAsync(cancellationToken);

		//marketDataType(requestId, mdt);
	}

	private async ValueTask ReadFinancialAdvice(IBSocket socket, CancellationToken cancellationToken)
	{
		/*var version = */await socket.ReadVersionAsync(cancellationToken);
		var type = await socket.ReadStringAsync(cancellationToken);
		var xml = await socket.ReadStringAsync(cancellationToken);

		await SendOutMessageAsync(new FinancialAdviseMessage
		{
			AdviseType = type,
			Data = xml
		}, cancellationToken);
	}

	private async ValueTask ReadSoftDollarTier(IBSocket socket, CancellationToken cancellationToken)
	{
		var requestId = await socket.ReadIntAsync(cancellationToken);
		var tiers = new SoftDollarTier[await socket.ReadIntAsync(cancellationToken)];

		for (var i = 0; i < tiers.Length; i++)
		{
			tiers[i] = new SoftDollarTier
			{
				Name = await socket.ReadStringAsync(cancellationToken),
				Value = await socket.ReadStringAsync(cancellationToken),
				DisplayName = await socket.ReadStringAsync(cancellationToken)
			};
		}

		await SendOutMessageAsync(new SoftDollarTierMessage
		{
			OriginalTransactionId = requestId,
			Tiers = tiers
		}, cancellationToken);
	}

	private async ValueTask ReadSecurityDefinitionOptionParameterEnd(IBSocket socket, CancellationToken cancellationToken)
	{
		/*var requestId = */await socket.ReadIntAsync(cancellationToken);

		//eWrapper.securityDefinitionOptionParameterEnd(requestId);
	}

	private async ValueTask ReadSecurityDefinitionOptionParameter(IBSocket socket, CancellationToken cancellationToken)
	{
		var requestId = await socket.ReadIntAsync(cancellationToken);
		var exchange = await socket.ReadStringAsync(cancellationToken);
		var underlyingConId = await socket.ReadIntAsync(cancellationToken);
		var tradingClass = await socket.ReadStringAsync(cancellationToken);
		var multiplier = await socket.ReadNullDecimalAsync(cancellationToken);

		var expirationsSize = await socket.ReadIntAsync(cancellationToken);
		var expirations = new HashSet<DateTime>();

		for (var i = 0; i < expirationsSize; i++)
		{
			var expiration = (await socket.ReadStringAsync(cancellationToken)).ReadDateTime(this, out _);

			if (expiration == null)
				throw new InvalidOperationException();

			expirations.Add(expiration.Value);
		}

		var strikesSize = await socket.ReadIntAsync(cancellationToken);
		var strikes = new HashSet<decimal>();

		for (var i = 0; i < strikesSize; i++)
		{
			strikes.Add(await socket.ReadDecimalAsync(cancellationToken));
		}

		await SendOutMessageAsync(new OptionParametersMessage
		{
			SecurityId = new SecurityId
			{
				InteractiveBrokers = underlyingConId,
				BoardCode = exchange,
			},
			OriginalTransactionId = requestId,
			Class = tradingClass,
			Multiplier = multiplier,
			Strikes = strikes,
			Expirations = expirations,
		}, cancellationToken);
	}

	private async ValueTask ReadHistogramData(IBSocket socket, CancellationToken cancellationToken)
	{
		var requestId = await socket.ReadIntAsync(cancellationToken);
		var data = new ValueTuple<decimal, decimal>[await socket.ReadIntAsync(cancellationToken)];

		for (var i = 0; i < data.Length; i++)
		{
			data[i] = (await socket.ReadDecimalAsync(cancellationToken), await socket.ReadDecimalAsync(cancellationToken));
		}

		await SendOutMessageAsync(new HistogramMessage
		{
			OriginalTransactionId = requestId,
			Data = data,
		}, cancellationToken);
		//eWrapper.histogramData(requestId, data);
	}

	private async ValueTask ReadHeadTimestamp(IBSocket socket, CancellationToken cancellationToken)
	{
		var requestId = await socket.ReadIntAsync(cancellationToken);
		var headTimestamp = await socket.ReadStringAsync(cancellationToken);

		//eWrapper.headTimestamp(requestId, headTimestamp);
	}

	private async ValueTask ReadHistoricalNewsEnd(IBSocket socket, CancellationToken cancellationToken)
	{
		var requestId = await socket.ReadIntAsync(cancellationToken);
		var time = await socket.ReadStringAsync(cancellationToken);
		var providerCode = await socket.ReadStringAsync(cancellationToken);
		var articleId = await socket.ReadStringAsync(cancellationToken);
		var headline = await socket.ReadStringAsync(cancellationToken);

		//eWrapper.historicalNews(requestId, time, providerCode, articleId, headline);
	}

	private async ValueTask ReadHistoricalNews(IBSocket socket, CancellationToken cancellationToken)
	{
		var requestId = await socket.ReadIntAsync(cancellationToken);
		var hasMore = await socket.ReadIntAsync(cancellationToken) == 1;

		//eWrapper.historicalNewsEnd(requestId, hasMore);
	}

	private async ValueTask ReadNewsArticle(IBSocket socket, CancellationToken cancellationToken)
	{
		var requestId = await socket.ReadIntAsync(cancellationToken);
		var articleType = await socket.ReadIntAsync(cancellationToken);
		var articleText = await socket.ReadStringAsync(cancellationToken);

		await SendOutMessageAsync(new NewsMessage
		{
			OriginalTransactionId = requestId,
			Headline = articleText,
		}, cancellationToken);

		//eWrapper.newsArticle(requestId, articleType, articleText);
	}

	private async ValueTask ReadNewsProviders(IBSocket socket, CancellationToken cancellationToken)
	{
		var newsProviders = new Tuple<string, string>[await socket.ReadIntAsync(cancellationToken)];

		for (var i = 0; i < newsProviders.Length; ++i)
		{
			newsProviders[i] = Tuple.Create(await socket.ReadStringAsync(cancellationToken), await socket.ReadStringAsync(cancellationToken));

			_newsProviders.Add(newsProviders[i].Item1);
		}

		//eWrapper.newsProviders(newsProviders);
	}

	private async ValueTask ReadSmartComponents(IBSocket socket, CancellationToken cancellationToken)
	{
		var requestId = await socket.ReadIntAsync(cancellationToken);
		var n = await socket.ReadIntAsync(cancellationToken);
		var map = new Dictionary<int, (string, char)>();

		for (var i = 0; i < n; i++)
		{
			var bitNumber = await socket.ReadIntAsync(cancellationToken);
			var exchange = await socket.ReadStringAsync(cancellationToken);
			var exchangeLetter = await socket.ReadCharAsync(cancellationToken);

			map.Add(bitNumber, new(exchange, exchangeLetter));
		}

		//eWrapper.smartComponents(requestId, theMap);
	}

	private async ValueTask ReadTickReqParams(IBSocket socket, CancellationToken cancellationToken)
	{
		var requestId = await socket.ReadIntAsync(cancellationToken);
		var minTick = await socket.ReadDecimalAsync(cancellationToken);
		var bboExchange = await socket.ReadStringAsync(cancellationToken);
		var snapshotPermissions = await socket.ReadIntAsync(cancellationToken);

		//eWrapper.tickReqParams(requestId, minTick, bboExchange, snapshotPermissions);
	}

	private async ValueTask ReadTickNews(IBSocket socket, CancellationToken cancellationToken)
	{
		var requestId = await socket.ReadIntAsync(cancellationToken);
		var timeStamp = await socket.ReadUnixDateTimeAsync(cancellationToken);
		var providerCode = await socket.ReadStringAsync(cancellationToken);
		var articleId = await socket.ReadStringAsync(cancellationToken);
		var headline = await socket.ReadStringAsync(cancellationToken);
		var extraData = await socket.ReadStringAsync(cancellationToken);

		_newsProviders2[articleId] = providerCode;

		await SendOutMessageAsync(new NewsMessage
		{
			OriginalTransactionId = requestId,
			Headline = headline,
			Id = articleId,
			Source = providerCode,
			ServerTime = timeStamp,
			Story = extraData,
		}, cancellationToken);
		//eWrapper.tickNews(requestId, timeStamp, providerCode, articleId, headline, extraData);
	}

	private async ValueTask ReadMktDepthExchanges(IBSocket socket, CancellationToken cancellationToken)
	{
		var depthMktDataDescriptions = new Tuple<string, string, string, string, int?>[await socket.ReadIntAsync(cancellationToken)];

		for (var i = 0; i < depthMktDataDescriptions.Length; i++)
		{
			if (socket.ServerVersion >= ServerVersions.ServiceDataType)
			{
				depthMktDataDescriptions[i] = Tuple.Create(await socket.ReadStringAsync(cancellationToken), await socket.ReadStringAsync(cancellationToken), await socket.ReadStringAsync(cancellationToken), await socket.ReadStringAsync(cancellationToken), await socket.ReadNullIntAsync(cancellationToken));
			}
			else
			{
				depthMktDataDescriptions[i] = Tuple.Create(await socket.ReadStringAsync(cancellationToken), await socket.ReadStringAsync(cancellationToken), string.Empty, await socket.ReadIntAsync(cancellationToken) == 1 ? "Deep2" : "Deep", (int?)null);
			}
		}

		//eWrapper.mktDepthExchanges(depthMktDataDescriptions);
	}

	private async ValueTask ReadSymbolSamples(IBSocket socket, CancellationToken cancellationToken)
	{
		var requestId = await socket.ReadIntAsync(cancellationToken);
		var contractDescriptions = new SecurityMessage[await socket.ReadIntAsync(cancellationToken)];

		for (var i = 0; i < contractDescriptions.Length; ++i)
		{
			var ibContractId = await socket.ReadIntAsync(cancellationToken);
			var symbol = await socket.ReadStringAsync(cancellationToken);
			var type = await socket.ReadStringAsync(cancellationToken);
			var exchange = await socket.ReadStringAsync(cancellationToken);
			var currency = await socket.ReadStringAsync(cancellationToken);

			var contract = new SecurityMessage
			{
				SecurityId = new SecurityId
				{
					SecurityCode = GetSecurityCode(symbol, type, currency, null, null),
					BoardCode = exchange,
					InteractiveBrokers = ibContractId
				},

				SecurityType = type.ToSecurityType(this, out var underlyingSecurityType),
				UnderlyingSecurityType = underlyingSecurityType,
				Currency = ToCurrency(currency),

				OriginalTransactionId = requestId,
			};

			// read derivative sec types list
			var derivativeSecTypes = new string[await socket.ReadIntAsync(cancellationToken)];
			for (var j = 0; j < derivativeSecTypes.Length; ++j)
			{
				derivativeSecTypes[j] = await socket.ReadStringAsync(cancellationToken);
			}

			if (socket.ServerVersion >= ServerVersions.MinServerVerBondIssuerId)
			{
				contract.Name = await socket.ReadStringAsync(cancellationToken);
				/*contract.IssuerId =*/ await socket.ReadStringAsync(cancellationToken);
			}

			if (ibContractId > 0)
				await SendOutMessageAsync(contract, cancellationToken);
		}

		await SendSubscriptionFinishedAsync(requestId, cancellationToken);
	}

	private async ValueTask ReadFamilyCodes(IBSocket socket, CancellationToken cancellationToken)
	{
		var familyCodes = new Tuple<string, string>[await socket.ReadIntAsync(cancellationToken)];

		for (var i = 0; i < familyCodes.Length; i++)
		{
			familyCodes[i] = Tuple.Create(await socket.ReadStringAsync(cancellationToken), await socket.ReadStringAsync(cancellationToken));
		}

		//eWrapper.familyCodes(familyCodes);
	}

	private async ValueTask ReadHistoricalTickLast(IBSocket socket, CancellationToken cancellationToken)
	{
		var requestId = await socket.ReadLongAsync(cancellationToken);
		var tickCount = await socket.ReadIntAsync(cancellationToken);

		var secId = _histContracts.TryGetValue2(requestId);

		for (var i = 0; i < tickCount; i++)
		{
			var time = await socket.ReadUnixDateTimeAsync(cancellationToken);
			var mask = await socket.ReadIntAsync(cancellationToken);
			var price = await socket.ReadDecimalAsync(cancellationToken);
			var size = await socket.ReadDecimalAsync(cancellationToken);
			var exchange = await socket.ReadStringAsync(cancellationToken);
			var specialConditions = await socket.ReadStringAsync(cancellationToken);

			if (secId != null)
			{
				await SendOutMessageAsync(new Level1ChangeMessage
				{
					SecurityId = secId.Value,
					ServerTime = time,
					OriginalTransactionId = requestId,
				}
				.Add(Level1Fields.LastTradePrice, price)
				.Add(Level1Fields.LastTradeVolume, (decimal)size), cancellationToken);
			}
		}

		var done = await socket.ReadBoolAsync(cancellationToken);

		if (done)
			await SendSubscriptionFinishedAsync(requestId, cancellationToken);
	}

	private async ValueTask ReadHistoricalTickBidAsk(IBSocket socket, CancellationToken cancellationToken)
	{
		var requestId = await socket.ReadLongAsync(cancellationToken);
		var tickCount = await socket.ReadIntAsync(cancellationToken);

		var secId = _histContracts.TryGetValue2(requestId);

		for (var i = 0; i < tickCount; i++)
		{
			var time = await socket.ReadUnixDateTimeAsync(cancellationToken);
			var mask = await socket.ReadIntAsync(cancellationToken);
			var priceBid = await socket.ReadDecimalAsync(cancellationToken);
			var priceAsk = await socket.ReadDecimalAsync(cancellationToken);
			var sizeBid = await socket.ReadDecimalAsync(cancellationToken);
			var sizeAsk = await socket.ReadDecimalAsync(cancellationToken);

			if (secId != null)
			{
				await SendOutMessageAsync(new Level1ChangeMessage
				{
					SecurityId = secId.Value,
					ServerTime = time,
					OriginalTransactionId = requestId,
				}
				.Add(Level1Fields.BestBidPrice, priceBid)
				.Add(Level1Fields.BestBidVolume, (decimal)sizeBid)
				.Add(Level1Fields.BestAskPrice, priceAsk)
				.Add(Level1Fields.BestAskVolume, (decimal)sizeAsk), cancellationToken);
			}
		}

		var done = await socket.ReadBoolAsync(cancellationToken);

		if (done)
			await SendSubscriptionFinishedAsync(requestId, cancellationToken);
	}

	private async ValueTask ReadHistoricalTick(IBSocket socket, CancellationToken cancellationToken)
	{
		var requestId = await socket.ReadLongAsync(cancellationToken);
		var tickCount = await socket.ReadIntAsync(cancellationToken);

		var secId = _histContracts.TryGetValue2(requestId);

		for (var i = 0; i < tickCount; i++)
		{
			var time = await socket.ReadUnixDateTimeAsync(cancellationToken);
			await socket.ReadIntAsync(cancellationToken); // for consistency
			var price = await socket.ReadDecimalAsync(cancellationToken);
			var size = await socket.ReadDecimalAsync(cancellationToken);

			if (secId != null)
			{
				await SendOutMessageAsync(new Level1ChangeMessage
				{
					SecurityId = secId.Value,
					ServerTime = time,
					OriginalTransactionId = requestId,
				}
				.Add(Level1Fields.LastTradePrice, price)
				.Add(Level1Fields.LastTradeVolume, (decimal)size), cancellationToken);
			}
		}

		var done = await socket.ReadBoolAsync(cancellationToken);

		if (done)
			await SendSubscriptionFinishedAsync(requestId, cancellationToken);
	}

	private async ValueTask ReadMarketRule(IBSocket socket, CancellationToken cancellationToken)
	{
		var marketRuleId = await socket.ReadLongAsync(cancellationToken);
		var priceIncrementCount = await socket.ReadIntAsync(cancellationToken);
		var priceIncrements = new Tuple<decimal, decimal>[priceIncrementCount];

		for (var i = 0; i < priceIncrementCount; ++i)
		{
			priceIncrements[i] = Tuple.Create(await socket.ReadDecimalAsync(cancellationToken), await socket.ReadDecimalAsync(cancellationToken));
		}

		// TODO
		//eWrapper.marketRule(marketRuleId, priceIncrements);
	}

	private async ValueTask ReadRerouteMktDepthReq(IBSocket socket, CancellationToken cancellationToken)
	{
		var requestId = await socket.ReadIntAsync(cancellationToken);
		var contractId = await socket.ReadIntAsync(cancellationToken);
		var exchange = await socket.ReadStringAsync(cancellationToken);

		//eWrapper.rerouteMktDepthReq(requestId, contractId, exchange);
	}

	private async ValueTask ReadRerouteMktDataReq(IBSocket socket, CancellationToken cancellationToken)
	{
		var requestId = await socket.ReadLongAsync(cancellationToken);
		var contractId = await socket.ReadIntAsync(cancellationToken);
		var exchange = await socket.ReadStringAsync(cancellationToken);

		//eWrapper.rerouteMktDataReq(requestId, conId, exchange);
	}

	private async ValueTask ReadHistoricalDataUpdate(IBSocket socket, CancellationToken cancellationToken)
	{
		var requestId = await socket.ReadLongAsync(cancellationToken);
		var barCount = await socket.ReadIntAsync(cancellationToken);
		var time = await socket.ReadTimeExAsync(cancellationToken);
		var open = await socket.ReadDecimalAsync(cancellationToken);
		var close = await socket.ReadDecimalAsync(cancellationToken);
		var high = await socket.ReadDecimalAsync(cancellationToken);
		var low = await socket.ReadDecimalAsync(cancellationToken);
		var wap = await socket.ReadDecimalAsync(cancellationToken);
		var volume = await socket.ReadDecimalAsync(cancellationToken);

		if (volume == -1)
			volume = 0;

		await SendOutMessageAsync(new TimeFrameCandleMessage
		{
			OriginalTransactionId = requestId,
			OpenTime = time,
			OpenPrice = open,
			ClosePrice = close,
			HighPrice = high,
			LowPrice = low,
			TotalVolume = volume,
			State = CandleStates.Active,
		}, cancellationToken);
		//eWrapper.historicalDataUpdate(requestId, new Bar(date, open, high, low,close, volume, barCount, wap));
	}

	private async ValueTask ReadTickByTick(IBSocket socket, CancellationToken cancellationToken)
	{
		var requestId = await socket.ReadIntAsync(cancellationToken);
		var tickType = await socket.ReadIntAsync(cancellationToken);
		var time = await socket.ReadTimeExAsync(cancellationToken);
		//BitMask mask;
		//TickAttrib attribs;

		switch (tickType)
		{
			case 0: // None
				break;
			case 1: // Last
			case 2: // AllLast
			{
				var price = await socket.ReadDecimalAsync(cancellationToken);
				var size = await socket.ReadDecimalAsync(cancellationToken);
				//mask = new BitMask(socket.ReadInt());
				//attribs = new TickAttrib();
				//attribs.PastLimit = mask[0];
				//attribs.Unreported = mask[1];
				var exchange = await socket.ReadStringAsync(cancellationToken);
				var specialConditions = await socket.ReadStringAsync(cancellationToken);
				var secId = TryGetSecurityId(requestId);

				if (secId != null)
				{
					if (!exchange.IsEmpty())
					{
						secId = new SecurityId
						{
							SecurityCode = secId.Value.SecurityCode,
							BoardCode = exchange
						};
					}

					await SendOutMessageAsync(new Level1ChangeMessage
					{
						SecurityId = secId.Value,
						ServerTime = time,
						OriginalTransactionId = requestId,
					}
					.TryAdd(Level1Fields.LastTradePrice, price)
					.TryAdd(Level1Fields.LastTradeVolume, size), cancellationToken);
				}

				//eWrapper.tickByTickAllLast(reqId, tickType, time, price, size, attribs, exchange, specialConditions);
				break;
			}
			case 3: // BidAsk
			{
				var bidPrice = await socket.ReadDecimalAsync(cancellationToken);
				var askPrice = await socket.ReadDecimalAsync(cancellationToken);
				var bidSize = await socket.ReadDecimalAsync(cancellationToken);
				var askSize = await socket.ReadDecimalAsync(cancellationToken);
				//mask = new BitMask(socket.ReadInt());
				//attribs = new TickAttrib();
				//attribs.BidPastLow = mask[0];
				//attribs.AskPastHigh = mask[1];
				//eWrapper.tickByTickBidAsk(reqId, time, bidPrice, askPrice, bidSize, askSize, attribs);
				var secId = TryGetSecurityId(requestId);

				if (secId != null)
				{
					await SendOutMessageAsync(new Level1ChangeMessage
					{
						SecurityId = secId.Value,
						ServerTime = time,
						OriginalTransactionId = requestId,
					}
					.TryAdd(Level1Fields.BestBidPrice, bidPrice)
					.TryAdd(Level1Fields.BestAskPrice, askPrice)
					.TryAdd(Level1Fields.BestBidVolume, bidSize)
					.TryAdd(Level1Fields.BestAskVolume, askSize), cancellationToken);
				}

				break;
			}
			case 4: // MidPoint
			{
				var midPoint = await socket.ReadDecimalAsync(cancellationToken);
				//eWrapper.tickByTickMidPoint(reqId, time, midPoint);

				var secId = TryGetSecurityId(requestId);

				if (secId != null)
				{
					await SendOutMessageAsync(new Level1ChangeMessage
					{
						SecurityId = secId.Value,
						ServerTime = time,
						OriginalTransactionId = requestId,
					}
					.TryAdd(Level1Fields.SpreadMiddle, midPoint), cancellationToken);
				}

				break;
			}
		}
	}

	private async ValueTask ReadReplaceFAEnd(IBSocket socket, CancellationToken cancellationToken)
	{
		/*var reqId = */await socket.ReadLongAsync(cancellationToken);
		/*var text = */await socket.ReadStringAsync(cancellationToken);
	}

	private async ValueTask ReadWshMetaData(IBSocket socket, CancellationToken cancellationToken)
	{
		var reqId = await socket.ReadLongAsync(cancellationToken);
		var dataJson = await socket.ReadStringAsync(cancellationToken);

		await SendOutMessageAsync(new WshMetaDataMessage
		{
			OriginalTransactionId = reqId,
			Data = dataJson,
		}, cancellationToken);
	}

	private async ValueTask ReadWshEventData(IBSocket socket, CancellationToken cancellationToken)
	{
		var reqId = await socket.ReadLongAsync(cancellationToken);
		var dataJson = await socket.ReadStringAsync(cancellationToken);

		await SendOutMessageAsync(new WshEventDataMessage
		{
			OriginalTransactionId = reqId,
			Data = dataJson,
		}, cancellationToken);
	}

	private async ValueTask ReadHistoricalScheduleEvent(IBSocket socket, CancellationToken cancellationToken)
	{
		/*var reqId = */await socket.ReadLongAsync(cancellationToken);
		/*var startDateTime = */await socket.ReadStringAsync(cancellationToken);
		/*var endDateTime = */await socket.ReadStringAsync(cancellationToken);
		/*var timeZone = */await socket.ReadStringAsync(cancellationToken);

		var sessionsCount = await socket.ReadIntAsync(cancellationToken);
		//var sessions = new HistoricalSession[sessionsCount];

		for (var i = 0; i < sessionsCount; i++)
		{
			/*var sessionStartDateTime = */await socket.ReadStringAsync(cancellationToken);
			/*var sessionEndDateTime = */await socket.ReadStringAsync(cancellationToken);
			/*var sessionRefDate = */await socket.ReadStringAsync(cancellationToken);

			//sessions[i] = new HistoricalSession(sessionStartDateTime, sessionEndDateTime, sessionRefDate);
		}
	}
}
