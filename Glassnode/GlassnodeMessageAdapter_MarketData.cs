namespace StockSharp.Glassnode;

public partial class GlassnodeMessageAdapter
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
		foreach (var asset in GetAssets()
			.Where(asset => Matches(asset, value))
			.OrderBy(static asset => asset.Id, StringComparer.OrdinalIgnoreCase))
		{
			var security = ToSecurityMessage(asset, message.TransactionId);
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

		var asset = ResolveAsset(message.SecurityId);
		var securityId = ToSecurityId(asset);
		var timeFrame = PriceTimeFrame;
		var (from, to, limit) = GetRange(message, timeFrame);
		var points = await SafeRest().GetClosePricesAsync(asset.Id,
			timeFrame.ToInterval(), from, to, cancellationToken) ?? [];
		var values = new SortedDictionary<DateTime, decimal>();
		foreach (var point in points.Where(static point => point is not null))
		{
			if (point.Value is null)
				continue;
			if (point.Value <= 0)
				throw new InvalidDataException(
					"Glassnode returned a non-positive USD close price.");
			var time = point.Timestamp.FromUnixSeconds();
			if (time >= from && time <= to)
				values[time] = point.Value.Value;
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
		var interval = timeFrame.ToInterval();
		var asset = ResolveAsset(message.SecurityId);
		var securityId = ToSecurityId(asset);
		var (from, to, limit) = GetRange(message, timeFrame);
		var points = await SafeRest().GetOhlcAsync(asset.Id, interval, from, to,
			cancellationToken) ?? [];
		var values = new SortedDictionary<DateTime, GlassnodeOhlcValue>();
		foreach (var point in points.Where(static point => point is not null))
		{
			var value = point.GetValues();
			if (value?.IsComplete != true)
				continue;
			ValidateOhlc(value);
			var time = point.Timestamp.FromUnixSeconds();
			if (time >= from && time <= to)
				values[time] = value;
		}

		IEnumerable<KeyValuePair<DateTime, GlassnodeOhlcValue>> selected = values;
		selected = message.From is null
			? selected.TakeLast(limit)
			: selected.Take(limit);
		foreach (var pair in selected)
		{
			var value = pair.Value;
			await SendOutMessageAsync(new TimeFrameCandleMessage
			{
				OriginalTransactionId = message.TransactionId,
				SecurityId = securityId,
				DataType = message.DataType2,
				TypedArg = timeFrame,
				OpenTime = pair.Key,
				OpenPrice = value.Open.Value,
				HighPrice = value.High.Value,
				LowPrice = value.Low.Value,
				ClosePrice = value.Close.Value,
				State = CandleStates.Finished,
			}, cancellationToken);
		}
		await FinishAsync(message, cancellationToken);
	}

	private (DateTime From, DateTime To, int Limit) GetRange(
		MarketDataMessage message, TimeSpan timeFrame)
	{
		_ = timeFrame.ToInterval();
		var limit = checked((int)Math.Min(message.Count ?? HistoryLimit,
			HistoryLimit).Max(1));
		var to = (message.To ?? CurrentTime).EnsureUtc();
		if (to <= DateTime.UnixEpoch)
			throw new ArgumentOutOfRangeException(nameof(message.To), to,
				"Glassnode history end time must be after the Unix epoch.");

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
				"Glassnode history start time must be earlier than its end time.");
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

	private static void ValidateOhlc(GlassnodeOhlcValue value)
	{
		if (value.Open <= 0 || value.High <= 0 || value.Low <= 0 ||
			value.Close <= 0 || value.Low > value.High ||
			value.High < value.Open || value.High < value.Close ||
			value.Low > value.Open || value.Low > value.Close)
			throw new InvalidDataException(
				"Glassnode returned an invalid USD OHLC value.");
	}

	private static bool Matches(GlassnodeAsset asset, string value)
	{
		if (value.IsEmpty())
			return true;
		return asset.Id.ContainsIgnoreCase(value) ||
			asset.Symbol.ContainsIgnoreCase(value) ||
			asset.Name.ContainsIgnoreCase(value) ||
			asset.DefaultNetwork.ContainsIgnoreCase(value) ||
			(asset.SemanticTags ?? []).Any(tag => tag.ContainsIgnoreCase(value));
	}

	private static SecurityId ToSecurityId(GlassnodeAsset asset)
		=> new()
		{
			SecurityCode = asset.Id + "/USD",
			BoardCode = BoardCodes.Glassnode,
			Native = asset.Id,
		};

	private static SecurityMessage ToSecurityMessage(GlassnodeAsset asset,
		long originalTransactionId)
		=> new()
		{
			OriginalTransactionId = originalTransactionId,
			SecurityId = ToSecurityId(asset),
			Name = asset.Name + " / USD",
			ShortName = asset.Symbol,
			Class = asset.Type.ToString(),
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
