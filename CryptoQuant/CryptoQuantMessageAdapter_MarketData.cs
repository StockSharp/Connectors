namespace StockSharp.CryptoQuant;

public partial class CryptoQuantMessageAdapter
{
	/// <inheritdoc />
	protected override async ValueTask SecurityLookupAsync(
		SecurityLookupMessage message, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(message.TransactionId, cancellationToken);
		var securityTypes = message.GetSecurityTypes();
		if (securityTypes.Count > 0 &&
			!securityTypes.Contains(SecurityTypes.CryptoCurrency))
		{
			await SendSubscriptionResultAsync(message, cancellationToken);
			return;
		}

		var value = (message.SecurityId.Native as string)
			.IsEmpty(message.SecurityId.SecurityCode).IsEmpty(message.Name)?.Trim();
		var skip = Math.Max(0L, message.Skip ?? 0);
		var left = Math.Max(0L,
			Math.Min(message.Count ?? MaximumItems, MaximumItems));
		foreach (var instrument in GetInstruments()
			.Where(instrument => Matches(instrument, value))
			.OrderBy(static instrument => instrument.Code,
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
		var timeFrame = PriceTimeFrame;
		var window = timeFrame.ToWindow();
		EnsureWindow(instrument, window);
		var (from, to, limit) = GetRange(message, timeFrame);
		var rows = await SafeRest().GetOhlcvAsync(instrument, window, from, to,
			limit, cancellationToken) ?? [];
		var values = new SortedDictionary<DateTime, decimal>();
		foreach (var row in rows.Where(static row => row is not null))
		{
			if (row.Close is null)
				continue;
			if (row.Close <= 0)
				throw new InvalidDataException(
					"CryptoQuant returned a non-positive USD close price.");
			var time = row.ParseCryptoQuantTime();
			if (time >= from && time <= to)
				values[time] = row.Close.Value;
		}

		IEnumerable<KeyValuePair<DateTime, decimal>> selected = values;
		selected = message.From is null
			? selected.TakeLast(limit)
			: selected.Take(limit);
		foreach (var pair in selected)
		{
			await SendOutMessageAsync(new Level1ChangeMessage
			{
				OriginalTransactionId = message.TransactionId,
				SecurityId = securityId,
				ServerTime = pair.Key,
			}
			.TryAdd(Level1Fields.ClosePrice, pair.Value), cancellationToken);
		}
		await FinishAsync(message, cancellationToken);
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
		var window = timeFrame.ToWindow();
		var instrument = ResolveInstrument(message.SecurityId);
		EnsureWindow(instrument, window);
		var securityId = ToSecurityId(instrument);
		var (from, to, limit) = GetRange(message, timeFrame);
		var rows = await SafeRest().GetOhlcvAsync(instrument, window, from, to,
			limit, cancellationToken) ?? [];
		var values = new SortedDictionary<DateTime, CryptoQuantOhlcv>();
		foreach (var row in rows.Where(static row => row is not null))
		{
			if (!row.IsComplete)
				continue;
			ValidateOhlcv(row);
			var time = row.ParseCryptoQuantTime();
			if (time >= from && time <= to)
				values[time] = row;
		}

		IEnumerable<KeyValuePair<DateTime, CryptoQuantOhlcv>> selected = values;
		selected = message.From is null
			? selected.TakeLast(limit)
			: selected.Take(limit);
		foreach (var pair in selected)
		{
			var row = pair.Value;
			await SendOutMessageAsync(new TimeFrameCandleMessage
			{
				OriginalTransactionId = message.TransactionId,
				SecurityId = securityId,
				DataType = message.DataType2,
				TypedArg = timeFrame,
				OpenTime = pair.Key,
				OpenPrice = row.Open.Value,
				HighPrice = row.High.Value,
				LowPrice = row.Low.Value,
				ClosePrice = row.Close.Value,
				TotalVolume = row.Volume ?? 0,
				State = CandleStates.Finished,
			}, cancellationToken);
		}
		await FinishAsync(message, cancellationToken);
	}

	private (DateTime From, DateTime To, int Limit) GetRange(
		MarketDataMessage message, TimeSpan timeFrame)
	{
		_ = timeFrame.ToWindow();
		var limit = checked((int)Math.Min(message.Count ?? HistoryLimit,
			HistoryLimit).Max(1));
		var to = (message.To ?? CurrentTime).EnsureUtc();
		if (to <= DateTime.UnixEpoch)
			throw new ArgumentOutOfRangeException(nameof(message.To), to,
				"CryptoQuant history end time must be after the Unix epoch.");

		var maximumSpan = TimeSpan.FromTicks(checked(timeFrame.Ticks *
			(limit + 4L)));
		DateTime from;
		if (message.From is DateTime requestedFrom)
		{
			from = requestedFrom.EnsureUtc();
			var cappedTo = AddClamped(from, maximumSpan);
			if (cappedTo < to)
				to = cappedTo;
		}
		else
		{
			var span = message.Count is null && HistoryLookback < maximumSpan
				? HistoryLookback
				: maximumSpan;
			from = SubtractClamped(to, span);
		}

		if (from < DateTime.UnixEpoch)
			from = DateTime.UnixEpoch;
		if (from >= to)
			throw new ArgumentOutOfRangeException(nameof(message.From), from,
				"CryptoQuant history start time must be earlier than its end time.");
		return (from, to, limit);
	}

	private static DateTime AddClamped(DateTime value, TimeSpan interval)
	{
		value = value.EnsureUtc();
		var ticks = Math.Min(interval.Ticks, DateTime.MaxValue.Ticks - value.Ticks);
		return new DateTime(value.Ticks + ticks, DateTimeKind.Utc);
	}

	private static DateTime SubtractClamped(DateTime value, TimeSpan interval)
	{
		value = value.EnsureUtc();
		var ticks = Math.Max(DateTime.MinValue.Ticks, value.Ticks - interval.Ticks);
		return new DateTime(ticks, DateTimeKind.Utc);
	}

	private static void EnsureWindow(CryptoQuantInstrument instrument,
		CryptoQuantWindows window)
	{
		if (!(instrument.Windows ?? []).Contains(window))
			throw new NotSupportedException(
				$"CryptoQuant does not advertise the {window.ToWire()} window for '{instrument.Code}'.");
	}

	private static void ValidateOhlcv(CryptoQuantOhlcv row)
	{
		if (row.Open <= 0 || row.High <= 0 || row.Low <= 0 || row.Close <= 0 ||
			row.Volume < 0 || row.Low > row.High || row.High < row.Open ||
			row.High < row.Close || row.Low > row.Open || row.Low > row.Close)
			throw new InvalidDataException(
				"CryptoQuant returned an invalid USD OHLCV value.");
	}

	private static bool Matches(CryptoQuantInstrument instrument, string value)
	{
		if (value.IsEmpty())
			return true;
		return instrument.Key.ContainsIgnoreCase(value) ||
			instrument.Code.ContainsIgnoreCase(value) ||
			instrument.Symbol.ContainsIgnoreCase(value) ||
			instrument.Namespace.ContainsIgnoreCase(value) ||
			instrument.Token.ContainsIgnoreCase(value);
	}

	private static SecurityId ToSecurityId(CryptoQuantInstrument instrument)
		=> new()
		{
			SecurityCode = instrument.Code,
			BoardCode = BoardCodes.CryptoQuant,
			Native = instrument.Key,
		};

	private static SecurityMessage ToSecurityMessage(
		CryptoQuantInstrument instrument, long originalTransactionId)
		=> new()
		{
			OriginalTransactionId = originalTransactionId,
			SecurityId = ToSecurityId(instrument),
			Name = instrument.Symbol + " CryptoQuant index / USD",
			ShortName = instrument.Symbol,
			Class = instrument.Namespace.ToUpperInvariant(),
			SecurityType = SecurityTypes.CryptoCurrency,
			Currency = CurrencyTypes.USD,
		};

	private async ValueTask FinishAsync(MarketDataMessage message,
		CancellationToken cancellationToken)
	{
		await SendSubscriptionResultAsync(message, cancellationToken);
		await SendSubscriptionFinishedAsync(message.TransactionId,
			cancellationToken);
	}
}
