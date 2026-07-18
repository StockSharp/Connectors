namespace StockSharp.RavenPack;

static class RavenPackExtensions
{
	public const string BoardCode = "RPACK";

	private static readonly string[] _analyticsFields =
	[
		"timestamp_utc", "rp_story_id", "rp_entity_id", "entity_type",
		"entity_name", "country_code", "relevance", "event_relevance",
		"event_sentiment_score", "topic", "group", "type", "sub_type",
		"event_text", "news_type", "source_name", "provider_id",
		"provider_story_id", "headline",
	];

	private static readonly string[] _edgeFields =
	[
		"timestamp_utc", "rp_document_id", "rp_entity_id", "entity_type",
		"entity_name", "country_code", "event_relevance", "entity_sentiment",
		"event_sentiment", "topic", "group", "title",
	];

	public static Uri GetApiAddress(this RavenPackProducts product)
		=> new(product == RavenPackProducts.Edge
			? "https://api-edge.ravenpack.com/1.0/"
			: "https://api.ravenpack.com/1.0/");

	public static Uri GetFeedAddress(this RavenPackProducts product)
		=> new(product == RavenPackProducts.Edge
			? "https://feed-edge.ravenpack.com/1.0/json/"
			: "https://feed.ravenpack.com/1.0/json/");

	public static string[] GetQueryFields(this RavenPackProducts product)
		=> product == RavenPackProducts.Edge ? _edgeFields : _analyticsFields;

	public static RavenPackEntityFilters ToEntityFilters(string entityId)
		=> entityId.IsEmpty() ? null : new()
		{
			All =
			[
				new()
				{
					EntityId = new() { Values = [entityId] },
				},
			],
		};

	public static RavenPackIdentifier ToIdentifier(this SecurityId securityId,
		string name = null)
	{
		var symbol = securityId.SecurityCode?.Trim();
		var board = securityId.BoardCode?.Trim();
		if (board.EqualsIgnoreCase(BoardCode))
			board = null;
		var identifier = new RavenPackIdentifier
		{
			Ticker = symbol,
			Name = name?.Trim(),
			Isin = securityId.Isin?.Trim(),
			Cusip = securityId.Cusip?.Trim(),
			Sedol = securityId.Sedol?.Trim(),
			Listing = board.IsEmpty() || symbol.IsEmpty() ? null : $"{board}:{symbol}",
		};
		return identifier.IsEmpty() ? null : identifier;
	}

	public static bool IsEmpty(this RavenPackIdentifier identifier)
		=> identifier == null || identifier.Ticker.IsEmpty() && identifier.Name.IsEmpty() &&
			identifier.Isin.IsEmpty() && identifier.Cusip.IsEmpty() &&
			identifier.Sedol.IsEmpty() && identifier.Listing.IsEmpty();

	public static bool HasWildcards(this RavenPackIdentifier identifier)
		=> new[] { identifier?.Ticker, identifier?.Name, identifier?.Isin,
			identifier?.Cusip, identifier?.Sedol, identifier?.Listing }
			.Any(value => value?.Any(character => character is '*' or '?' ||
				char.IsControl(character)) == true);

	public static string GetDocumentId(this RavenPackAnalyticsRecord record)
		=> record?.DocumentId.IsEmpty(record.StoryId).IsEmpty(record.ProviderStoryId);

	public static string GetEventKey(this RavenPackAnalyticsRecord record)
	{
		if (record == null)
			return null;
		var document = record.GetDocumentId();
		if (document.IsEmpty() && record.TimestampUtc.IsEmpty())
			return null;
		return $"{document}|{record.EntityId}|{record.TimestampUtc}";
	}

	public static bool TryGetTimestamp(this RavenPackAnalyticsRecord record,
		out DateTime result)
		=> TryParseUtc(record?.TimestampUtc, out result);

	public static bool TryParseUtc(string value, out DateTime result)
	{
		result = default;
		if (value.IsEmpty())
			return false;
		if (DateTime.TryParseExact(value,
			["yyyy-MM-dd HH:mm:ss.FFFFFFF", "yyyy-MM-dd HH:mm:ss"],
			CultureInfo.InvariantCulture,
			DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
			out result))
		{
			return true;
		}
		if (!DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture,
			DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.AssumeUniversal,
			out var parsed))
		{
			return false;
		}
		result = parsed.UtcDateTime;
		return true;
	}

	public static DateTime ToUtc(this DateTime value)
		=> value.Kind switch
		{
			DateTimeKind.Utc => value,
			DateTimeKind.Local => value.ToUniversalTime(),
			_ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
		};

	public static string FormatApiTime(this DateTime value)
		=> value.ToUtc().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);

	public static bool MatchesEntity(this RavenPackAnalyticsRecord record,
		string entityId)
		=> entityId.IsEmpty() || record?.EntityId.EqualsIgnoreCase(entityId) == true;

	public static string GetHeadline(this RavenPackAnalyticsRecord record)
		=> record?.Title.IsEmpty(record.Headline)
			.IsEmpty(record.EventText)
			.IsEmpty(record.Topic)
			.IsEmpty(record.EntityName)
			.IsEmpty("RavenPack analytics");

	public static string GetSource(this RavenPackAnalyticsRecord record)
		=> record?.SourceName.IsEmpty(record.ProviderId).IsEmpty("RavenPack");

	public static string GetStory(this RavenPackAnalyticsRecord record)
	{
		if (record == null)
			return null;
		var values = new List<string>();
		if (!record.EventText.IsEmpty() &&
			!record.EventText.EqualsIgnoreCase(record.GetHeadline()))
		{
			values.Add(record.EventText);
		}
		Add(values, "Entity", record.EntityName);
		Add(values, "Topic", record.Topic);
		Add(values, "Group", record.Group);
		Add(values, "Type", record.SubType.IsEmpty(record.Type));
		Add(values, "Event relevance", FormatNumber(record.EventRelevance));
		Add(values, "Relevance", FormatNumber(record.Relevance));
		Add(values, "Event sentiment", FormatNumber(
			record.EventSentiment ?? record.EventSentimentScore));
		Add(values, "Entity sentiment", FormatNumber(record.EntitySentiment));
		return values.Count == 0 ? null : string.Join("; ", values);
	}

	public static SecurityId GetSecurityId(this RavenPackAnalyticsRecord record,
		SecurityId requested)
	{
		if (!requested.SecurityCode.IsEmpty())
			return requested;
		var symbol = record?.Ticker;
		var board = BoardCode;
		if (TryParseListing(record?.Listing, out var listingBoard, out var listingSymbol))
		{
			board = listingBoard;
			symbol = symbol.IsEmpty(listingSymbol);
		}
		if (symbol.IsEmpty())
			return default;
		return new()
		{
			SecurityCode = symbol,
			BoardCode = board,
			Native = record.EntityId,
		};
	}

	public static SecurityMessage ToSecurityMessage(this RavenPackMappingCandidate candidate,
		RavenPackEntityReference reference, SecurityId requested, long transactionId)
	{
		if (candidate?.EntityId.IsEmpty() != false)
			return null;

		var listing = reference?.Listings.GetCurrent();
		var symbol = requested.SecurityCode.IsEmpty(reference?.Tickers.GetCurrent())
			.IsEmpty(reference?.Symbols.GetCurrent());
		var board = requested.BoardCode;
		if (board.EqualsIgnoreCase(BoardCode))
			board = null;
		if (TryParseListing(listing, out var listingBoard, out var listingSymbol))
		{
			board = board.IsEmpty(listingBoard);
			symbol = symbol.IsEmpty(listingSymbol);
		}
		board = board.IsEmpty(reference?.Mics.GetCurrent()).IsEmpty(BoardCode);
		symbol = symbol.IsEmpty(candidate.EntityId);

		var securityId = requested;
		securityId.SecurityCode = symbol;
		securityId.BoardCode = board;
		securityId.Native = candidate.EntityId;
		securityId.Isin = securityId.Isin.IsEmpty(reference?.Isins.GetCurrent());
		securityId.Cusip = securityId.Cusip.IsEmpty(reference?.Cusips.GetCurrent());
		securityId.Sedol = securityId.Sedol.IsEmpty(reference?.Sedols.GetCurrent());

		return new()
		{
			OriginalTransactionId = transactionId,
			SecurityId = securityId,
			Name = reference?.Names.GetCurrent()
				.IsEmpty(reference?.EntityNames.GetCurrent())
				.IsEmpty(candidate.EntityName).IsEmpty(symbol),
			ShortName = candidate.EntityName.IsEmpty(symbol),
			SecurityType = candidate.EntityType
				.IsEmpty(reference?.Types.GetCurrent()).ToSecurityType(),
			VolumeStep = 1,
		};
	}

	public static bool TryParseListing(string value, out string board, out string symbol)
	{
		board = symbol = null;
		if (value.IsEmpty())
			return false;
		var separator = value.IndexOf(':');
		if (separator <= 0 || separator == value.Length - 1)
			return false;
		board = value[..separator].Trim().ToUpperInvariant();
		symbol = value[(separator + 1)..].Trim().ToUpperInvariant();
		return !board.IsEmpty() && !symbol.IsEmpty();
	}

	private static string GetCurrent(this RavenPackReferenceValue[] values)
	{
		if (values?.Length is not > 0)
			return null;
		var today = DateTime.UtcNow.Date;
		var current = values.Where(value => value != null && !value.Value.IsEmpty() &&
			(!TryParseDate(value.Start, out var start) || start <= today) &&
			(!TryParseDate(value.End, out var end) || today < end))
			.OrderBy(value => TryParseDate(value.Start, out var start) ? start : DateTime.MinValue)
			.LastOrDefault();
		return current?.Value ?? values.LastOrDefault(value => value?.Value.IsEmpty() == false)?.Value;
	}

	private static bool TryParseDate(string value, out DateTime result)
		=> DateTime.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture,
			DateTimeStyles.AssumeUniversal, out result);

	private static SecurityTypes? ToSecurityType(this string value)
		=> value?.Trim().ToUpperInvariant() switch
		{
			"COMP" or "COMPANY" => SecurityTypes.Stock,
			"CURR" or "CURRENCY" => SecurityTypes.Currency,
			"COMM" or "COMMODITY" => SecurityTypes.Commodity,
			"CRYP" or "CRYPTO" => SecurityTypes.CryptoCurrency,
			"INDX" or "INDEX" => SecurityTypes.Index,
			_ => null,
		};

	private static void Add(List<string> values, string name, string value)
	{
		if (!value.IsEmpty())
			values.Add($"{name}: {value}");
	}

	private static string FormatNumber(decimal? value)
		=> value?.ToString("0.####", CultureInfo.InvariantCulture);
}
