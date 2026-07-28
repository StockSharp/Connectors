namespace StockSharp.Xrpl.Native;

static class XrplExtensions
{
	public static readonly DateTime RippleEpoch =
		new(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc);

	public static readonly TimeSpan[] TimeFrames =
	[
		TimeSpan.FromMinutes(1),
		TimeSpan.FromMinutes(5),
		TimeSpan.FromMinutes(15),
		TimeSpan.FromMinutes(30),
		TimeSpan.FromHours(1),
		TimeSpan.FromHours(4),
		TimeSpan.FromDays(1),
	];

	public static XrplMarket[] ParseMarkets(string value, string domainId)
	{
		value = value.ThrowIfEmpty(nameof(value));
		domainId = NormalizeDomainId(domainId);
		var result = new List<XrplMarket>();
		foreach (var item in value.Split([';', ',', '\r', '\n'],
			StringSplitOptions.RemoveEmptyEntries |
				StringSplitOptions.TrimEntries))
		{
			var separator = item.IndexOf('/');
			if (separator <= 0 || separator == item.Length - 1 ||
				item.IndexOf('/', separator + 1) >= 0)
				throw new FormatException(
					$"XRPL market '{item}' must use BASE/QUOTE format.");
			var baseAsset = ParseAsset(item[..separator]);
			var quoteAsset = ParseAsset(item[(separator + 1)..]);
			if (baseAsset.Key.EqualsIgnoreCase(quoteAsset.Key))
				throw new FormatException(
					$"XRPL market '{item}' contains the same asset twice.");
			result.Add(new()
			{
				SecurityCode =
					$"{baseAsset.Symbol}/{quoteAsset.Symbol}",
				Base = baseAsset,
				Quote = quoteAsset,
				DomainId = domainId,
			});
		}
		if (result.Count == 0)
			throw new FormatException("No XRPL markets were configured.");
		foreach (var group in result.GroupBy(
			static market => market.SecurityCode,
			StringComparer.OrdinalIgnoreCase).Where(
				static group => group.Count() > 1))
		{
			foreach (var market in group)
			{
				var identity = !market.Quote.IsXrp
					? market.Quote.Issuer
					: market.Base.Issuer;
				market.SecurityCode += "@" + identity[..8];
			}
		}
		if (result.Select(static market => market.SecurityCode)
			.Distinct(StringComparer.OrdinalIgnoreCase).Count() !=
			result.Count)
			throw new FormatException(
				"XRPL market configuration creates duplicate codes.");
		return [.. result];
	}

	public static XrplAsset ParseAsset(string value)
	{
		value = value.ThrowIfEmpty(nameof(value)).Trim();
		if (value.EqualsIgnoreCase("XRP"))
			return new()
			{
				CurrencyCode = "XRP",
				Symbol = "XRP",
				IsXrp = true,
			};

		var separator = value.LastIndexOf(':');
		if (separator <= 0 || separator == value.Length - 1)
			throw new FormatException(
				$"Issued XRPL asset '{value}' must use CODE:ISSUER format.");
		var code = value[..separator].Trim();
		var issuer = value[(separator + 1)..].Trim();
		if (!XrplCodec.IsValidClassicAddress(issuer))
			throw new FormatException(
				$"XRPL issuer '{issuer}' is not a valid classic address.");
		var wireCode = EncodeCurrencyCode(code);
		if (wireCode.EqualsIgnoreCase("XRP") ||
			wireCode.All(static character => character == '0'))
			throw new FormatException(
				$"XRPL issued currency code '{code}' is invalid.");
		return new()
		{
			CurrencyCode = wireCode,
			Issuer = issuer,
			Symbol = DecodeCurrencyCode(wireCode),
		};
	}

	public static string EncodeCurrencyCode(string value)
	{
		value = value.ThrowIfEmpty(nameof(value)).Trim();
		if (Regex.IsMatch(value, "^[0-9A-Fa-f]{40}$"))
			return value.ToUpperInvariant();
		if (value.Length == 3 && value.All(IsCurrencyCharacter))
			return value;
		if (value.Length is < 4 or > 20 ||
			!value.All(IsCurrencyCharacter))
			throw new FormatException(
				$"XRPL currency code '{value}' must contain 3 to 20 " +
					"printable ASCII characters or 40 hexadecimal digits.");
		var bytes = Encoding.ASCII.GetBytes(value);
		return Convert.ToHexString(bytes).PadRight(40, '0');
	}

	public static string DecodeCurrencyCode(string value)
	{
		value = value.ThrowIfEmpty(nameof(value)).Trim();
		if (!Regex.IsMatch(value, "^[0-9A-Fa-f]{40}$"))
			return value;
		var bytes = Convert.FromHexString(value);
		var length = Array.FindLastIndex(bytes,
			static item => item != 0) + 1;
		if (length > 0 && bytes[..length].All(
			static item => item is >= 0x20 and <= 0x7e))
			return Encoding.ASCII.GetString(bytes, 0, length);
		return value.ToUpperInvariant();
	}

	public static JObject ToCurrencySpec(this XrplAsset asset)
	{
		ArgumentNullException.ThrowIfNull(asset);
		var result = new JObject
		{
			["currency"] = asset.CurrencyCode,
		};
		if (!asset.IsXrp)
			result["issuer"] = asset.Issuer;
		return result;
	}

	public static JToken ToAmount(this XrplAsset asset, decimal value)
	{
		ArgumentNullException.ThrowIfNull(asset);
		if (value <= 0)
			throw new ArgumentOutOfRangeException(nameof(value), value,
				"XRPL amount must be positive.");
		if (asset.IsXrp)
		{
			var drops = value * 1_000_000m;
			if (drops != decimal.Truncate(drops))
				throw new InvalidOperationException(
					$"XRP amount '{value}' contains fractional drops.");
			return drops.ToString("0", CultureInfo.InvariantCulture);
		}
		return new JObject
		{
			["currency"] = asset.CurrencyCode,
			["issuer"] = asset.Issuer,
			["value"] = FormatIssuedAmount(value),
		};
	}

	public static decimal ParseAmount(this XrplAsset asset, JToken value,
		string fieldName = null)
	{
		ArgumentNullException.ThrowIfNull(asset);
		if (value is null || value.Type == JTokenType.Null)
			throw new InvalidDataException(
				$"XRPL field '{fieldName ?? "amount"}' is missing.");
		if (asset.IsXrp)
		{
			if (value.Type == JTokenType.Object)
				throw new InvalidDataException(
					$"XRPL field '{fieldName ?? "amount"}' is not XRP.");
			return ParseDecimal(value, fieldName) / 1_000_000m;
		}
		if (value is not JObject amount ||
			!amount.Value<string>("currency")
				.EqualsIgnoreCase(asset.CurrencyCode) ||
			!amount.Value<string>("issuer").EqualsIgnoreCase(asset.Issuer))
			throw new InvalidDataException(
				$"XRPL field '{fieldName ?? "amount"}' has an unexpected " +
					"issued currency.");
		return ParseDecimal(amount["value"], fieldName);
	}

	public static XrplBook ParseBook(XrplMarket market, JObject asks,
		JObject bids, int depth, DateTime time)
	{
		ArgumentNullException.ThrowIfNull(market);
		ArgumentNullException.ThrowIfNull(asks);
		ArgumentNullException.ThrowIfNull(bids);
		if (depth <= 0)
			throw new ArgumentOutOfRangeException(nameof(depth));
		var askLevels = ParseBookSide(market,
			asks["offers"] as JArray, false);
		var bidLevels = ParseBookSide(market,
			bids["offers"] as JArray, true);
		return new()
		{
			Asks = AggregateLevels(askLevels, false, depth),
			Bids = AggregateLevels(bidLevels, true, depth),
			LedgerIndex = Math.Max(
				asks.Value<uint?>("ledger_index") ??
					asks.Value<uint?>("ledger_current_index") ?? 0,
				bids.Value<uint?>("ledger_index") ??
					bids.Value<uint?>("ledger_current_index") ?? 0),
			Time = time,
		};
	}

	public static XrplMarketBar ParseBookChange(XrplMarket market,
		JObject change, uint ledgerIndex, long ledgerTime)
	{
		ArgumentNullException.ThrowIfNull(market);
		ArgumentNullException.ThrowIfNull(change);
		var currencyA = change.Value<string>("currency_a");
		var currencyB = change.Value<string>("currency_b");
		var baseIsA = market.Base.BookChangeId.EqualsIgnoreCase(currencyA) &&
			market.Quote.BookChangeId.EqualsIgnoreCase(currencyB);
		var baseIsB = market.Base.BookChangeId.EqualsIgnoreCase(currencyB) &&
			market.Quote.BookChangeId.EqualsIgnoreCase(currencyA);
		if (!baseIsA && !baseIsB)
			return null;
		var currencyAAsset = baseIsA ? market.Base : market.Quote;
		var rateScale = currencyAAsset.IsXrp ? 1_000_000m : 1m;
		var openRate = ParseDecimal(change["open"], "open") / rateScale;
		var highRate = ParseDecimal(change["high"], "high") / rateScale;
		var lowRate = ParseDecimal(change["low"], "low") / rateScale;
		var closeRate = ParseDecimal(change["close"], "close") / rateScale;
		if (openRate <= 0 || highRate <= 0 || lowRate <= 0 ||
			closeRate <= 0)
			return null;
		decimal open;
		decimal high;
		decimal low;
		decimal close;
		decimal volume;
		decimal turnover;
		if (baseIsA)
		{
			open = 1m / openRate;
			high = 1m / lowRate;
			low = 1m / highRate;
			close = 1m / closeRate;
			volume = ParseBookChangeVolume(change["volume_a"],
				market.Base);
			turnover = ParseBookChangeVolume(change["volume_b"],
				market.Quote);
		}
		else
		{
			open = openRate;
			high = highRate;
			low = lowRate;
			close = closeRate;
			volume = ParseBookChangeVolume(change["volume_b"],
				market.Base);
			turnover = ParseBookChangeVolume(change["volume_a"],
				market.Quote);
		}
		if (volume <= 0 || turnover <= 0 || high < low)
			return null;
		return new()
		{
			Id = $"{ledgerIndex}:{market.SecurityCode}",
			LedgerIndex = ledgerIndex,
			Time = FromRippleTime(ledgerTime),
			Open = open,
			High = high,
			Low = low,
			Close = close,
			Volume = volume,
			Turnover = turnover,
		};
	}

	public static XrplCandle[] AggregateBars(
		IEnumerable<XrplMarketBar> bars, TimeSpan timeFrame)
	{
		ArgumentNullException.ThrowIfNull(bars);
		if (timeFrame <= TimeSpan.Zero)
			throw new ArgumentOutOfRangeException(nameof(timeFrame));
		return
		[
			.. bars.Where(static bar => bar is not null)
				.OrderBy(static bar => bar.Time)
				.GroupBy(bar => new DateTime(
					bar.Time.ToUniversalTime().Ticks /
						timeFrame.Ticks * timeFrame.Ticks,
					DateTimeKind.Utc))
				.Select(group =>
				{
					var values = group.ToArray();
					return new XrplCandle
					{
						OpenTime = group.Key,
						Open = values[0].Open,
						High = values.Max(static bar => bar.High),
						Low = values.Min(static bar => bar.Low),
						Close = values[^1].Close,
						Volume = values.Sum(static bar => bar.Volume),
						Turnover = values.Sum(
							static bar => bar.Turnover),
						LedgerCount = values.Length,
					};
				})
		];
	}

	public static SecurityId ToStockSharp(this XrplMarket market)
		=> new()
		{
			SecurityCode = market.SecurityCode,
			BoardCode = BoardCodes.Xrpl,
		};

	public static DateTime FromRippleTime(long seconds)
	{
		if (seconds < 0)
			throw new InvalidDataException(
				$"Invalid XRPL ledger time '{seconds}'.");
		return RippleEpoch.AddSeconds(seconds);
	}

	public static uint ToRippleTime(this DateTime value)
	{
		var seconds = (value.ToUniversalTime() - RippleEpoch).TotalSeconds;
		if (seconds is < 0 or > uint.MaxValue)
			throw new ArgumentOutOfRangeException(nameof(value));
		return checked((uint)seconds);
	}

	public static string NormalizeDomainId(string value)
	{
		value = value?.Trim();
		if (value.IsEmpty())
			return null;
		if (!Regex.IsMatch(value, "^[0-9A-Fa-f]{64}$"))
			throw new FormatException(
				"XRPL permissioned DEX domain must be 64 hexadecimal " +
					"characters.");
		return value.ToUpperInvariant();
	}

	public static string FormatIssuedAmount(decimal value)
	{
		if (value <= 0)
			throw new ArgumentOutOfRangeException(nameof(value));
		return value.ToString("G15", CultureInfo.InvariantCulture);
	}

	public static decimal ParseDecimal(JToken value,
		string fieldName = null)
	{
		var text = value?.ToString(Formatting.None)?.Trim('"');
		if (!decimal.TryParse(text, NumberStyles.Float,
			CultureInfo.InvariantCulture, out var result))
			throw new InvalidDataException(
				$"XRPL field '{fieldName ?? "value"}' is not a decimal.");
		return result;
	}

	private static bool IsCurrencyCharacter(char character)
		=> character is >= '!' and <= '~' && character != ':';

	private static IEnumerable<XrplBookLevel> ParseBookSide(
		XrplMarket market, JArray offers, bool isBid)
	{
		foreach (var offer in offers?.OfType<JObject>() ?? [])
		{
			var gets = offer["taker_gets_funded"] ??
				offer["TakerGets"];
			var pays = offer["taker_pays_funded"] ??
				offer["TakerPays"];
			decimal volume;
			decimal quote;
			try
			{
				if (isBid)
				{
					volume = market.Base.ParseAmount(pays,
						"taker_pays");
					quote = market.Quote.ParseAmount(gets,
						"taker_gets");
				}
				else
				{
					volume = market.Base.ParseAmount(gets,
						"taker_gets");
					quote = market.Quote.ParseAmount(pays,
						"taker_pays");
				}
			}
			catch (InvalidDataException)
			{
				continue;
			}
			if (volume <= 0 || quote <= 0)
				continue;
			yield return new()
			{
				Price = quote / volume,
				Volume = volume,
				OfferId = offer.Value<string>("index"),
			};
		}
	}

	private static XrplBookLevel[] AggregateLevels(
		IEnumerable<XrplBookLevel> source, bool descending, int depth)
	{
		var values = source.GroupBy(static level => level.Price)
			.Select(static group => new XrplBookLevel
			{
				Price = group.Key,
				Volume = group.Sum(static level => level.Volume),
				OfferId = group.First().OfferId,
			});
		values = descending
			? values.OrderByDescending(static level => level.Price)
			: values.OrderBy(static level => level.Price);
		return [.. values.Take(depth)];
	}

	private static decimal ParseBookChangeVolume(JToken value,
		XrplAsset asset)
	{
		var amount = ParseDecimal(value, "volume");
		return asset.IsXrp ? amount / 1_000_000m : amount;
	}
}
