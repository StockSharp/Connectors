namespace StockSharp.Pyth;

public partial class PythMessageAdapter
{
	private static readonly PythProperties[] _priceProperties =
	[
		PythProperties.Price,
		PythProperties.BestBidPrice,
		PythProperties.BestAskPrice,
		PythProperties.Exponent,
		PythProperties.MarketSession,
		PythProperties.FeedUpdateTimestamp,
	];

	private readonly record struct HistoryRange(DateTime From, DateTime To,
		int Limit);

	/// <inheritdoc />
	protected override async ValueTask SecurityLookupAsync(
		SecurityLookupMessage message, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(message.TransactionId, cancellationToken);
		if (message.Count is <= 0)
		{
			await SendSubscriptionResultAsync(message, cancellationToken);
			return;
		}

		var value = (message.SecurityId.Native as string)
			.IsEmpty(message.SecurityId.SecurityCode).IsEmpty(message.Name)?.Trim();
		var securityTypes = message.GetSecurityTypes();
		var skip = Math.Max(0L, message.Skip ?? 0);
		var left = Math.Max(0L,
			Math.Min(message.Count ?? MaximumItems, MaximumItems));
		foreach (var instrument in GetInstruments()
			.Where(instrument => Matches(instrument, value))
			.OrderBy(static instrument => instrument.Symbol,
				StringComparer.OrdinalIgnoreCase))
		{
			var security = ToSecurityMessage(instrument, message.TransactionId);
			if (!security.IsMatch(message, securityTypes))
				continue;
			if (skip > 0)
			{
				skip--;
				continue;
			}
			if (left <= 0)
				break;
			await SendOutMessageAsync(security, cancellationToken);
			left--;
		}
		await SendSubscriptionResultAsync(message, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask OnLevel1SubscriptionAsync(
		MarketDataMessage message, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(message.TransactionId, cancellationToken);
		if (!message.IsSubscribe)
		{
			RemoveLiveSubscription(message.OriginalTransactionId);
			if (_pool is { } existingPool)
				await existingPool.UnsubscribeAsync(message.OriginalTransactionId,
					cancellationToken);
			await SendSubscriptionResultAsync(message, cancellationToken);
			return;
		}
		if (message.Count is <= 0)
		{
			await FinishAsync(message, cancellationToken);
			return;
		}

		var instrument = ResolveInstrument(message.SecurityId);
		var securityId = ToSecurityId(instrument);
		var channel = Channel.SelectChannel(instrument.MinimumChannel);
		var remaining = message.Count;
		var lastUpdateKey = string.Empty;

		if (message.From is not null || message.To is not null ||
			message.IsHistoryOnly())
		{
			var timeFrame = TimeSpan.FromMinutes(1);
			var range = GetRange(message, timeFrame);
			var rows = await GetHistoryAsync(instrument, channel, timeFrame, range,
				cancellationToken);
			IEnumerable<KeyValuePair<DateTime, PythHistoryCandle>> selected = rows;
			selected = message.From is null
				? selected.TakeLast(range.Limit)
				: selected.Take(range.Limit);
			foreach (var pair in selected)
			{
				await SendOutMessageAsync(new Level1ChangeMessage
				{
					OriginalTransactionId = message.TransactionId,
					SecurityId = securityId,
					ServerTime = pair.Key,
				}.TryAdd(Level1Fields.ClosePrice, pair.Value.ClosePrice),
					cancellationToken);
				if (remaining is > 0 && --remaining == 0)
					break;
			}
		}

		if (message.IsHistoryOnly() || message.To is not null || remaining == 0)
		{
			await FinishAsync(message, cancellationToken);
			return;
		}

		var latest = await SafeRest().GetLatestPriceAsync(
			CreateLatestRequest(instrument, channel), cancellationToken);
		if (TryCreateLevel1(latest, instrument, message.TransactionId, securityId,
			out var snapshot, out lastUpdateKey))
		{
			await SendOutMessageAsync(snapshot, cancellationToken);
			if (remaining is > 0)
				remaining--;
		}
		if (remaining == 0)
		{
			await FinishAsync(message, cancellationToken);
			return;
		}

		var pool = await EnsurePoolAsync(cancellationToken);
		AddLiveSubscription(new()
		{
			TransactionId = message.TransactionId,
			SecurityId = securityId,
			Instrument = instrument,
			Channel = channel,
			Remaining = remaining,
			LastUpdateKey = lastUpdateKey,
		});
		try
		{
			await pool.SubscribeAsync(CreateSubscribeRequest(message.TransactionId,
				instrument, channel), cancellationToken);
		}
		catch
		{
			RemoveLiveSubscription(message.TransactionId);
			throw;
		}
		await SendSubscriptionResultAsync(message, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask OnTFCandlesSubscriptionAsync(
		MarketDataMessage message, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(message.TransactionId, cancellationToken);
		if (!message.IsSubscribe)
		{
			await SendSubscriptionResultAsync(message, cancellationToken);
			return;
		}
		if (message.Count is <= 0)
		{
			await FinishAsync(message, cancellationToken);
			return;
		}

		var timeFrame = message.GetTimeFrame();
		_ = timeFrame.ToResolution();
		if (!PythExtensions.TimeFrames.Contains(timeFrame))
			throw new NotSupportedException(
				$"Pyth connector does not advertise {timeFrame} candles.");
		var instrument = ResolveInstrument(message.SecurityId);
		var channel = Channel.SelectChannel(instrument.MinimumChannel);
		var range = GetRange(message, timeFrame);
		var rows = await GetHistoryAsync(instrument, channel, timeFrame, range,
			cancellationToken);
		IEnumerable<KeyValuePair<DateTime, PythHistoryCandle>> selected = rows;
		selected = message.From is null
			? selected.TakeLast(range.Limit)
			: selected.Take(range.Limit);
		var securityId = ToSecurityId(instrument);
		foreach (var pair in selected)
		{
			var row = pair.Value;
			ValidateCandle(row);
			await SendOutMessageAsync(new TimeFrameCandleMessage
			{
				OriginalTransactionId = message.TransactionId,
				SecurityId = securityId,
				DataType = message.DataType2,
				TypedArg = timeFrame,
				OpenTime = pair.Key,
				CloseTime = AddClamped(pair.Key, timeFrame),
				OpenPrice = row.OpenPrice,
				HighPrice = row.HighPrice,
				LowPrice = row.LowPrice,
				ClosePrice = row.ClosePrice,
				TotalVolume = row.Volume,
				State = CandleStates.Finished,
			}, cancellationToken);
		}
		await FinishAsync(message, cancellationToken);
	}

	private async ValueTask<SortedDictionary<DateTime, PythHistoryCandle>>
		GetHistoryAsync(PythSymbol instrument, PythChannels channel,
		TimeSpan timeFrame, HistoryRange range,
		CancellationToken cancellationToken)
	{
		var result = new SortedDictionary<DateTime, PythHistoryCandle>();
		await foreach (var candle in SafeRest().GetHistoryAsync(instrument, channel,
			timeFrame, range.From, range.To, MaximumBarsPerRequest,
			cancellationToken))
		{
			if (candle is null || candle.OpenTime < range.From ||
				candle.OpenTime > range.To ||
				AddClamped(candle.OpenTime, timeFrame) > CurrentTime.EnsureUtc())
				continue;
			result[candle.OpenTime] = candle;
		}
		return result;
	}

	private HistoryRange GetRange(MarketDataMessage message, TimeSpan timeFrame)
	{
		_ = timeFrame.ToResolution();
		var limit = checked((int)Math.Min(message.Count ?? HistoryLimit,
			HistoryLimit).Max(1));
		var to = (message.To ?? CurrentTime).EnsureUtc();
		if (to <= DateTime.UnixEpoch)
			throw new ArgumentOutOfRangeException(nameof(message), to,
				"Pyth history end must be after the Unix epoch.");
		var maximumSpan = TimeSpan.FromTicks(checked(timeFrame.Ticks *
			(limit + 2L)));
		DateTime from;
		if (message.From is DateTime requestedFrom)
		{
			from = requestedFrom.EnsureUtc();
			if (from >= to)
				throw new ArgumentOutOfRangeException(nameof(message), from,
					"Pyth history start must be earlier than its end.");
			var cappedTo = AddClamped(from, maximumSpan);
			if (cappedTo < to)
				to = cappedTo;
		}
		else
		{
			var span = HistoryLookback < maximumSpan
				? HistoryLookback
				: maximumSpan;
			from = SubtractClamped(to, span);
		}
		if (from < DateTime.UnixEpoch)
			from = DateTime.UnixEpoch;
		return new(from, to, limit);
	}

	private static PythLatestPriceRequest CreateLatestRequest(
		PythSymbol instrument, PythChannels channel)
		=> new()
		{
			PriceFeedIds = [instrument.Id],
			Properties = _priceProperties,
			Formats = [],
			Channel = channel,
			IsParsed = true,
		};

	private static PythSubscribeRequest CreateSubscribeRequest(
		long transactionId, PythSymbol instrument, PythChannels channel)
		=> new()
		{
			SubscriptionId = transactionId,
			PriceFeedIds = [instrument.Id],
			Properties = _priceProperties,
			Formats = [],
			Channel = channel,
			IsParsed = true,
			IsIgnoreInvalidFeeds = true,
		};

	private static bool TryCreateLevel1(PythUpdate update,
		PythSymbol instrument, long transactionId, SecurityId securityId,
		out Level1ChangeMessage message, out string updateKey)
	{
		message = null;
		updateKey = null;
		var parsed = update?.Parsed;
		if (parsed is null)
			return false;
		if (!long.TryParse(parsed.TimestampMicroseconds, NumberStyles.Integer,
			CultureInfo.InvariantCulture, out var updateTime) || updateTime <= 0)
			throw new InvalidDataException(
				"Pyth update has an invalid timestamp.");
		var matches = (parsed.PriceFeeds ?? [])
			.Where(feed => feed is not null && feed.PriceFeedId == instrument.Id)
			.Take(2).ToArray();
		if (matches.Length == 0)
			return false;
		if (matches.Length != 1)
			throw new InvalidDataException(
				"Pyth update contains a duplicate feed payload.");
		var feed = matches[0];
		var exponent = feed.Exponent ?? instrument.Exponent;
		if (exponent != instrument.Exponent)
			throw new InvalidDataException(
				"Pyth update exponent differs from symbol metadata.");
		var serverMicroseconds = feed.FeedUpdateTimestamp ?? updateTime;
		if (serverMicroseconds <= 0 || serverMicroseconds > updateTime)
			throw new InvalidDataException(
				"Pyth feed update timestamp is invalid.");

		var price = ParseOptionalPrice(feed.Price, exponent, "price");
		var bid = ParseOptionalPrice(feed.BestBidPrice, exponent, "bestBidPrice");
		var ask = ParseOptionalPrice(feed.BestAskPrice, exponent, "bestAskPrice");
		if (price == 0 || bid == 0 || ask == 0 ||
			bid is decimal bidValue && ask is decimal askValue && bidValue >= askValue)
			throw new InvalidDataException("Pyth returned invalid Level 1 prices.");
		var serverTime = serverMicroseconds.FromUnixMicroseconds();
		message = new Level1ChangeMessage
		{
			OriginalTransactionId = transactionId,
			SecurityId = securityId,
			ServerTime = serverTime,
		}
		.TryAdd(Level1Fields.LastTradePrice, price)
		.TryAdd(Level1Fields.LastTradeTime, price is null ? null : serverTime)
		.TryAdd(Level1Fields.BestBidPrice, bid)
		.TryAdd(Level1Fields.BestBidTime, bid is null ? null : serverTime)
		.TryAdd(Level1Fields.BestAskPrice, ask)
		.TryAdd(Level1Fields.BestAskTime, ask is null ? null : serverTime)
		.TryAdd(Level1Fields.State, feed.MarketSession == PythMarketSessions.Unknown
			? null
			: feed.MarketSession == PythMarketSessions.Closed
				? SecurityStates.Stoped
				: SecurityStates.Trading);
		updateKey = string.Join(":", serverMicroseconds, feed.Price,
			feed.BestBidPrice, feed.BestAskPrice, feed.MarketSession.ToString());
		return message.Changes.Count > 0;
	}

	private static decimal? ParseOptionalPrice(string value, short exponent,
		string name)
		=> value.IsEmpty() ? null : PythExtensions.ScalePythValue(value, exponent,
			name);

	private static void ValidateCandle(PythHistoryCandle value)
	{
		ArgumentNullException.ThrowIfNull(value);
		if (value.OpenPrice == 0 || value.HighPrice == 0 || value.LowPrice == 0 ||
			value.ClosePrice == 0 || value.LowPrice > value.HighPrice ||
			value.HighPrice < value.OpenPrice || value.HighPrice < value.ClosePrice ||
			value.LowPrice > value.OpenPrice || value.LowPrice > value.ClosePrice ||
			value.Volume < 0)
			throw new InvalidDataException("Pyth returned an invalid OHLCV candle.");
	}

	private static bool Matches(PythSymbol instrument, string value)
	{
		if (value.IsEmpty())
			return true;
		return instrument.Key.ContainsIgnoreCase(value) ||
			instrument.Name.ContainsIgnoreCase(value) ||
			instrument.Symbol.ContainsIgnoreCase(value) ||
			instrument.Description.ContainsIgnoreCase(value) ||
			instrument.AssetType.ToWire().ContainsIgnoreCase(value) ||
			instrument.InstrumentType != PythInstrumentTypes.Unknown &&
			instrument.InstrumentType.ToWire().ContainsIgnoreCase(value) ||
			instrument.NasdaqSymbol.ContainsIgnoreCase(value) ||
			instrument.SymbolChainId.ContainsIgnoreCase(value);
	}

	private static SecurityId ToSecurityId(PythSymbol instrument)
		=> new()
		{
			SecurityCode = instrument.Symbol.ToUpperInvariant(),
			BoardCode = BoardCodes.Pyth,
			Native = instrument.Key,
		};

	private static SecurityMessage ToSecurityMessage(PythSymbol instrument,
		long originalTransactionId)
		=> new()
		{
			OriginalTransactionId = originalTransactionId,
			SecurityId = ToSecurityId(instrument),
			Name = instrument.Description.IsEmpty(instrument.Symbol),
			ShortName = instrument.Name,
			Class = instrument.AssetType.ToWire().ToUpperInvariant(),
			SecurityType = instrument.ToSecurityType(),
			Currency = instrument.QuoteCurrency.ToCurrency(),
			PriceStep = instrument.Exponent.ToPriceStep(),
			ExpiryDate = instrument.ExpirationTime.ParsePythExpiration(),
		};

	private static DateTime AddClamped(DateTime value, TimeSpan interval)
	{
		value = value.EnsureUtc();
		var ticks = Math.Min(interval.Ticks, DateTime.MaxValue.Ticks - value.Ticks);
		return new(value.Ticks + ticks, DateTimeKind.Utc);
	}

	private static DateTime SubtractClamped(DateTime value, TimeSpan interval)
	{
		value = value.EnsureUtc();
		var ticks = Math.Max(DateTime.MinValue.Ticks, value.Ticks - interval.Ticks);
		return new(ticks, DateTimeKind.Utc);
	}

	private async ValueTask FinishAsync(MarketDataMessage message,
		CancellationToken cancellationToken)
	{
		await SendSubscriptionResultAsync(message, cancellationToken);
		await SendSubscriptionFinishedAsync(message.TransactionId,
			cancellationToken);
	}
}
