namespace StockSharp.Marquee;

public partial class MarqueeMessageAdapter
{
	private const string _endOfDay = "End Of Day";
	private const string _realTime = "Real Time";

	private static readonly string[] _level1Fields =
	[
		"bidPrice", "askPrice", "midPrice", "tradePrice", "openPrice", "highPrice",
		"lowPrice", "closePrice", "volume",
	];

	private static readonly string[] _candlePriceFields =
	[
		"openPrice", "highPrice", "lowPrice", "closePrice",
	];

	private sealed class ProviderSelection
	{
		public string Field { get; init; }
		public MarqueeMeasureProvider Provider { get; init; }
	}

	private sealed class DataPoint
	{
		public DateTime Time { get; private set; }
		public decimal? Bid { get; private set; }
		public decimal? Ask { get; private set; }
		public decimal? Mid { get; private set; }
		public decimal? Trade { get; private set; }
		public decimal? Open { get; private set; }
		public decimal? High { get; private set; }
		public decimal? Low { get; private set; }
		public decimal? Close { get; private set; }
		public decimal? Volume { get; private set; }

		public bool HasValues => Bid != null || Ask != null || Mid != null || Trade != null ||
			Open != null || High != null || Low != null || Close != null || Volume != null;

		public bool HasOhlc => Open != null && High != null && Low != null && Close != null;

		public void Apply(MarqueeDataRow row)
		{
			var time = row.GetTime();
			if (time != null && time > Time)
				Time = time.Value;
			Bid = row.BidPrice ?? Bid;
			Ask = row.AskPrice ?? Ask;
			Mid = row.MidPrice ?? Mid;
			Trade = row.TradePrice ?? Trade;
			Open = row.OpenPrice ?? Open;
			High = row.HighPrice ?? High;
			Low = row.LowPrice ?? Low;
			Close = row.ClosePrice ?? Close;
			Volume = row.Volume ?? Volume;
		}
	}

	/// <inheritdoc />
	protected override async ValueTask SecurityLookupAsync(SecurityLookupMessage lookupMsg,
		CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(lookupMsg.TransactionId, cancellationToken);
		var securityTypes = lookupMsg.GetSecurityTypes();
		var assetClasses = ToAssetClasses(securityTypes);
		var left = lookupMsg.Count ?? long.MaxValue;

		await foreach (var asset in SafeClient().LookupAssets(
			lookupMsg.SecurityId.SecurityCode, assetClasses, cancellationToken)
			.WithEnforcedCancellation(cancellationToken))
		{
			CacheAsset(asset);
			var security = asset.ToSecurityMessage(lookupMsg.TransactionId);
			if (!security.IsMatch(lookupMsg, securityTypes))
				continue;

			await SendOutMessageAsync(security, cancellationToken);
			if (--left <= 0)
				break;
		}

		await SendSubscriptionResultAsync(lookupMsg, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask OnLevel1SubscriptionAsync(MarketDataMessage mdMsg,
		CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(mdMsg.TransactionId, cancellationToken);
		if (!mdMsg.IsSubscribe)
		{
			await SendSubscriptionResultAsync(mdMsg, cancellationToken);
			return;
		}

		var asset = await ResolveAsset(mdMsg.SecurityId, cancellationToken);
		var availability = await SafeClient().GetAvailability(asset.Id, cancellationToken);
		var providers = SelectProviders(availability, _level1Fields, preferRealTime: true);
		if (providers.Length == 0)
			throw new InvalidOperationException(
				$"No entitled Marquee price measures are available for '{asset.GetSecurityCode()}'.");

		var point = new DataPoint();
		foreach (var group in providers.GroupBy(selection =>
			(selection.Provider.DatasetId, selection.Provider.Frequency)))
		{
			var fields = group.Select(selection => selection.Field).Distinct().ToArray();
			var rows = await SafeClient().GetLastData(group.Key.DatasetId, asset.Id, fields,
				group.Key.Frequency.EqualsIgnoreCase(_realTime), cancellationToken);
			foreach (var row in rows.OrderBy(row => row.GetTime() ?? DateTime.MinValue))
				point.Apply(row);
		}

		if (!point.HasValues)
			throw new InvalidOperationException(
				$"Marquee returned no entitled price values for '{asset.GetSecurityCode()}'.");

		await SendOutMessageAsync(new Level1ChangeMessage
		{
			OriginalTransactionId = mdMsg.TransactionId,
			SecurityId = mdMsg.SecurityId,
			ServerTime = point.Time == default ? DateTime.UtcNow : point.Time,
		}
		.TryAdd(Level1Fields.BestBidPrice, point.Bid)
		.TryAdd(Level1Fields.BestAskPrice, point.Ask)
		.TryAdd(Level1Fields.SpreadMiddle, point.Mid)
		.TryAdd(Level1Fields.LastTradePrice, point.Trade)
		.TryAdd(Level1Fields.OpenPrice, point.Open)
		.TryAdd(Level1Fields.HighPrice, point.High)
		.TryAdd(Level1Fields.LowPrice, point.Low)
		.TryAdd(Level1Fields.ClosePrice, point.Close)
		.TryAdd(Level1Fields.Volume, point.Volume), cancellationToken);

		await SendSubscriptionResultAsync(mdMsg, cancellationToken);
		await SendSubscriptionFinishedAsync(mdMsg.TransactionId, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask OnTFCandlesSubscriptionAsync(MarketDataMessage mdMsg,
		CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(mdMsg.TransactionId, cancellationToken);
		if (!mdMsg.IsSubscribe)
		{
			await SendSubscriptionResultAsync(mdMsg, cancellationToken);
			return;
		}

		var timeFrame = mdMsg.GetTimeFrame();
		if (timeFrame != TimeSpan.FromDays(1))
			throw new NotSupportedException(
				"Goldman Sachs Marquee dataset candles are available only at the native daily frequency.");

		var asset = await ResolveAsset(mdMsg.SecurityId, cancellationToken);
		var availability = await SafeClient().GetAvailability(asset.Id, cancellationToken);
		var requestedFields = _candlePriceFields.Concat(["volume"]).ToArray();
		var providers = SelectProviders(availability, requestedFields,
			preferRealTime: false, endOfDayOnly: true);
		var missing = _candlePriceFields.Except(providers.Select(item => item.Field),
			StringComparer.OrdinalIgnoreCase).ToArray();
		if (missing.Length > 0)
			throw new InvalidOperationException(
				$"Marquee does not provide the entitled daily fields {missing.JoinComma()} for '{asset.GetSecurityCode()}'.");

		var to = (mdMsg.To ?? DateTime.UtcNow).ToUniversalTime().Date;
		var from = mdMsg.From?.ToUniversalTime().Date ?? GetDefaultFrom(to, mdMsg.Count);
		if (from > to)
			throw new ArgumentOutOfRangeException(nameof(mdMsg.From), from, "The start date is after the end date.");

		var candles = new SortedDictionary<DateTime, DataPoint>();
		foreach (var group in providers.GroupBy(selection => selection.Provider.DatasetId))
		{
			var fields = group.Select(selection => selection.Field).Distinct().ToArray();
			await foreach (var row in SafeClient().GetData(group.Key, asset.Id, fields, from, to,
				cancellationToken).WithEnforcedCancellation(cancellationToken))
			{
				var time = row.GetTime()?.Date;
				if (time == null || time < from || time > to)
					continue;
				if (!candles.TryGetValue(time.Value, out var point))
					candles.Add(time.Value, point = new());
				point.Apply(row);
			}
		}

		IEnumerable<KeyValuePair<DateTime, DataPoint>> values =
			candles.Where(pair => pair.Value.HasOhlc);
		if (mdMsg.Count is long count)
		{
			var take = checked((int)Math.Min(count.Max(0), int.MaxValue));
			values = mdMsg.From == null ? values.TakeLast(take) : values.Take(take);
		}

		foreach (var pair in values)
		{
			var point = pair.Value;
			await SendOutMessageAsync(new TimeFrameCandleMessage
			{
				OriginalTransactionId = mdMsg.TransactionId,
				SecurityId = mdMsg.SecurityId,
				DataType = mdMsg.DataType2,
				TypedArg = timeFrame,
				OpenTime = pair.Key,
				OpenPrice = point.Open.Value,
				HighPrice = point.High.Value,
				LowPrice = point.Low.Value,
				ClosePrice = point.Close.Value,
				TotalVolume = point.Volume ?? 0m,
				State = CandleStates.Finished,
			}, cancellationToken);
		}

		await SendSubscriptionResultAsync(mdMsg, cancellationToken);
		await SendSubscriptionFinishedAsync(mdMsg.TransactionId, cancellationToken);
	}

	private static DateTime GetDefaultFrom(DateTime to, long? count)
	{
		if (count == null)
			return to.AddYears(-1);
		var days = Math.Min(count.Value.Max(1), 18250) * 2;
		return to.AddDays(-days);
	}

	private static ProviderSelection[] SelectProviders(MarqueeAvailabilityResponse availability,
		IEnumerable<string> fields, bool preferRealTime, bool endOfDayOnly = false)
	{
		var result = new List<ProviderSelection>();
		foreach (var field in fields)
		{
			var candidates = (availability?.Data ?? [])
				.Where(provider => provider.DatasetField.EqualsIgnoreCase(field) &&
					!provider.DatasetId.IsEmpty() &&
					(!endOfDayOnly || provider.Frequency.IsEmpty() ||
						provider.Frequency.EqualsIgnoreCase(_endOfDay)))
				.OrderByDescending(provider => preferRealTime &&
					provider.Frequency.EqualsIgnoreCase(_realTime))
				.ThenByDescending(provider => provider.Rank ?? 0);
			var selected = candidates.FirstOrDefault();
			if (selected != null)
				result.Add(new() { Field = field, Provider = selected });
		}
		return result.ToArray();
	}

	private static string[] ToAssetClasses(IEnumerable<SecurityTypes> securityTypes)
	{
		var types = securityTypes?.ToArray() ?? [];
		if (types.Length == 0)
			return null;

		var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		foreach (var type in types)
		{
			switch (type)
			{
				case SecurityTypes.Stock:
				case SecurityTypes.Index:
				case SecurityTypes.Etf:
				case SecurityTypes.Adr:
				case SecurityTypes.Gdr:
					result.Add("Equity");
					break;
				case SecurityTypes.Future:
				case SecurityTypes.Option:
					result.Add("ListedDerivative");
					break;
				case SecurityTypes.Currency:
				case SecurityTypes.Forward:
					result.Add("FX");
					break;
				case SecurityTypes.Bond:
					result.Add("Debt");
					result.Add("Credit");
					result.Add("Rates");
					break;
				case SecurityTypes.Commodity:
					result.Add("Commod");
					break;
				case SecurityTypes.Fund:
					result.Add("Fund");
					break;
				case SecurityTypes.CryptoCurrency:
					result.Add("Digital Asset");
					result.Add("Cryptocurrency");
					break;
				default:
					return null;
			}
		}
		return result.ToArray();
	}
}
