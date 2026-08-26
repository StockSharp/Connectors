namespace StockSharp.Connectors.Tests;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Security;
using System.Threading.Tasks;

using Ecng.Common;
using Ecng.Serialization;
using Ecng.UnitTesting;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using Newtonsoft.Json;

using StockSharp.Alpaca;
using StockSharp.Alpaca.Native;
using StockSharp.Alpaca.Native.Model;
using StockSharp.Messages;

/// <summary>
/// The Alpaca connector: what it makes of a contract, what connecting requires, and what the venue
/// really answers.
/// </summary>
/// <remarks>
/// The tests are in two parts, and the split is the point rather than an accident of history.
///
/// The offline ones read a verbatim response from a live paper account, so the parser is held to the
/// shape the venue actually sends. Alpaca sends every number of a contract as a string — the strike,
/// the multiplier, the lot size — and a parser written against a prettier shape reads them all as zero
/// without complaining.
///
/// The live ones are marked Integration and run only when a key is supplied through the environment,
/// because a contract list that parses in a unit test but comes back empty from the venue has told
/// nobody anything. Most of them speak to the REST clients directly rather than through the adapter:
/// the REST clients are the part that is new, and the option endpoints differ from the equity ones in
/// ways only a real request reveals.
/// </remarks>
[TestClass]
public class AlpacaTests : BaseTestClass
{
	private const string _underlying = "NVDA";
	private const string _tradingEndpoint = "https://paper-api.alpaca.markets";
	private const string _dataEndpoint = "https://data.alpaca.markets";

	private const string _liveContract =
		"""
		{
		  "id": "e6e17751-5869-4267-adf6-4ebb27ad0fd1",
		  "symbol": "NVDA260828C00050000",
		  "name": "NVDA Aug 28 2026 50 Call",
		  "status": "active",
		  "tradable": true,
		  "expiration_date": "2026-08-28",
		  "root_symbol": "NVDA",
		  "underlying_symbol": "NVDA",
		  "underlying_asset_id": "4ce9353c-66d1-46c2-898f-fce867ab0247",
		  "type": "call",
		  "style": "american",
		  "strike_price": "50",
		  "multiplier": "100",
		  "size": "100",
		  "open_interest": "20",
		  "open_interest_date": "2026-08-24",
		  "close_price": "162.54",
		  "close_price_date": "2026-08-25",
		  "ppind": true
		}
		""";

	private static OptionContract Parse(string json = null)
		=> JsonConvert.DeserializeObject<OptionContract>(json ?? _liveContract);

	private static AlpacaMessageAdapter OfflineAdapter()
		=> new(new IncrementalIdGenerator())
		{
			Key = "key".Secure(),
			Secret = "secret".Secure(),
			IsDemo = true,
		};

	private (SecureString Key, SecureString Secret) Credentials()
	{
		var key = Environment.GetEnvironmentVariable("ALPACA_KEY");
		var secret = Environment.GetEnvironmentVariable("ALPACA_SECRET");

		if (key.IsEmpty() || secret.IsEmpty())
			Inconclusive("Set ALPACA_KEY and ALPACA_SECRET to run the live Alpaca tests.");

		return (key.Secure(), secret.Secure());
	}

	private RestTradingClient Trading()
	{
		var (key, secret) = Credentials();

		return new(_tradingEndpoint, key, secret);
	}

	private RestOptionClient Options()
	{
		var (key, secret) = Credentials();

		return new(_dataEndpoint, key, secret);
	}

	private async Task<List<OptionContract>> ContractsAsync(string type, DateTime? from, DateTime? to, int take)
	{
		using var client = Trading();

		var contracts = new List<OptionContract>();

		await foreach (var contract in client.GetOptionContracts(_underlying, "active", type, from, to, CancellationToken))
		{
			contracts.Add(contract);

			if (contracts.Count >= take)
				break;
		}

		return contracts;
	}

	/// <summary>Every field of the live payload survives the way in.</summary>
	[TestMethod]
	public void AContractIsReadAsAlpacaSendsIt()
	{
		var contract = Parse();

		AreEqual("NVDA260828C00050000", contract.Symbol);
		AreEqual("NVDA Aug 28 2026 50 Call", contract.Name);
		AreEqual("NVDA", contract.UnderlyingSymbol);
		AreEqual("call", contract.Type);
		AreEqual("american", contract.Style);
		AreEqual(true, contract.Tradable);

		// The numbers arrive as text. Read as anything else they come out as nothing.
		AreEqual(50m, contract.StrikePrice);
		AreEqual(100m, contract.Multiplier);
		AreEqual(100m, contract.Size);
		AreEqual(new DateTime(2026, 8, 28), contract.ExpirationDate);
	}

	/// <summary>A contract becomes an option security, with everything a strategy needs to price it.</summary>
	[TestMethod]
	public void AContractBecomesAnOptionSecurity()
	{
		var message = Parse().ToSecurityMessage();

		AreEqual(SecurityTypes.Option, message.SecurityType);
		AreEqual("NVDA260828C00050000", message.SecurityId.SecurityCode);
		AreEqual(OptionTypes.Call, message.OptionType);
		AreEqual(OptionStyles.American, message.OptionStyle);
		AreEqual(50m, message.Strike);
		AreEqual(100m, message.Multiplier);
		AreEqual(new DateTime(2026, 8, 28), message.ExpiryDate?.Date);
		AreEqual("NVDA", message.UnderlyingSecurityId.SecurityCode);
		AreEqual("NVDA Aug 28 2026 50 Call", message.Name);
	}

	/// <summary>A put is a put. Reading the right off the wrong field is silent and total.</summary>
	[TestMethod]
	public void APutIsReadAsAPut()
	{
		var message = Parse(_liveContract.Replace("\"call\"", "\"put\"", StringComparison.Ordinal)).ToSecurityMessage();

		AreEqual(OptionTypes.Put, message.OptionType);
	}

	/// <summary>A European contract is not quietly called American.</summary>
	[TestMethod]
	public void TheExerciseStyleIsCarriedThrough()
	{
		var message = Parse(_liveContract.Replace("\"american\"", "\"european\"", StringComparison.Ordinal)).ToSecurityMessage();

		AreEqual(OptionStyles.European, message.OptionStyle);
	}

	/// <summary>
	/// A right nobody recognises is refused rather than defaulted. A contract silently read as a call
	/// would be sold instead of bought, and nothing downstream would ever say so.
	/// </summary>
	[TestMethod]
	public void AnUnknownRightIsRefused()
		=> Throws<ArgumentOutOfRangeException>(()
			=> Parse(_liveContract.Replace("\"call\"", "\"straddle\"", StringComparison.Ordinal)).ToSecurityMessage());

	/// <summary>The board is the consolidated options tape, not the board of the underlying.</summary>
	[TestMethod]
	public void OptionsCarryTheirOwnBoard()
		=> AreEqual(BoardCodes.Opra, Parse().ToSecurityMessage().SecurityId.BoardCode);

	/// <summary>Options are one of the sections the adapter can be pointed at.</summary>
	[TestMethod]
	public void OptionsAreASection()
	{
		var adapter = new AlpacaMessageAdapter(new IncrementalIdGenerator());

		IsTrue(adapter.Sections.Contains(AlpacaSections.Option),
			$"the adapter offers only {adapter.Sections.Select(s => s.To<string>()).JoinComma()}.");
	}

	/// <summary>A chosen set of sections survives being saved and loaded.</summary>
	[TestMethod]
	public void TheChosenSectionsSurviveARoundTrip()
	{
		var source = new AlpacaMessageAdapter(new IncrementalIdGenerator())
		{
			Sections = [AlpacaSections.Option],
		};

		var storage = new SettingsStorage();

		source.Save(storage);

		var target = new AlpacaMessageAdapter(new IncrementalIdGenerator());

		target.Load(storage);

		AreEqual(1, target.Sections.Count());
		AreEqual(AlpacaSections.Option, target.Sections.First());
	}

	/// <summary>
	/// Connecting to download history opens no live stream, because history does not use one.
	/// </summary>
	/// <remarks>
	/// Alpaca sells its live streams separately from its history, so an account entitled to years of bars
	/// can be entitled to no stream at all. Such an account answers the stream with "insufficient
	/// subscription" and closes it, the socket reconnects, and it is closed again — and a connect that
	/// waits for every stream to be up never reports success, leaving a history request behind it waiting
	/// with it, receiving neither the bars nor an error saying why.
	/// </remarks>
	[TestMethod]
	public async Task ConnectingForMarketDataOpensNoStream()
	{
		using var adapter = OfflineAdapter();

		adapter.RemoveTransactionalSupport();

		await adapter.ConnectAsync(CancellationToken);

		AreEqual(0, adapter.OpenStreamsCount, "connecting opened a live stream that nothing had asked for.");
	}

	/// <summary>And it really is connected afterwards, rather than quietly doing nothing.</summary>
	[TestMethod]
	public async Task ConnectingForMarketDataSucceeds()
	{
		using var adapter = OfflineAdapter();

		adapter.RemoveTransactionalSupport();

		Message connected = null;

		adapter.NewOutMessageAsync += (msg, token) =>
		{
			if (msg is ConnectMessage)
				connected = msg;

			return default;
		};

		await adapter.ConnectAsync(CancellationToken);

		IsNotNull(connected, "connecting reported neither success nor failure.");
	}

	/// <summary>Contracts really arrive, and carry everything a security needs to be built from them.</summary>
	[TestMethod]
	[TestCategory("Integration")]
	[DataRow("call")]
	[DataRow("put")]
	public async Task ContractsArriveFromTheVenue(string right)
	{
		var today = DateTime.UtcNow.Date;
		var contracts = await ContractsAsync(right, today, today.AddYears(2), 25);

		IsTrue(contracts.Count > 0, $"the {_underlying} {right} listing came back empty.");

		foreach (var contract in contracts)
		{
			var message = contract.ToSecurityMessage();

			AreEqual(SecurityTypes.Option, message.SecurityType);
			AreEqual(BoardCodes.Opra, message.SecurityId.BoardCode);
			AreEqual(_underlying, message.UnderlyingSecurityId.SecurityCode);
			AreEqual(right.ToOptionType(), message.OptionType);

			IsNotNull(message.ExpiryDate);
			IsTrue(message.Strike > 0, $"{message.SecurityId} arrived with strike {message.Strike}.");
			IsTrue(message.Multiplier > 0, $"{message.SecurityId} arrived with multiplier {message.Multiplier}.");
		}
	}

	/// <summary>
	/// A chain is the whole chain. Left without an explicit range the venue answers with the nearest
	/// expiry and nothing else, which reads like a complete chain and is a few days of one.
	/// </summary>
	[TestMethod]
	[TestCategory("Integration")]
	public async Task AChainSpansMoreThanTheNearestExpiry()
	{
		var today = DateTime.UtcNow.Date;
		var contracts = await ContractsAsync("call", today, today.AddYears(2), 500);

		var expiries = contracts
			.Select(c => c.ExpirationDate.Date)
			.Distinct()
			.OrderBy(d => d)
			.ToArray();

		IsTrue(expiries.Length > 1,
			$"the chain came back with {expiries.Length} expiry date(s): {expiries.Select(d => d.ToString("yyyy-MM-dd")).JoinComma()}.");

		IsTrue(expiries.Last() > today.AddDays(14),
			$"nothing beyond a fortnight came back; the furthest was {expiries.Last():yyyy-MM-dd}.");
	}

	/// <summary>An exact expiry asked for is the only expiry returned.</summary>
	[TestMethod]
	[TestCategory("Integration")]
	public async Task AnExactExpiryNarrowsTheChain()
	{
		var today = DateTime.UtcNow.Date;
		var chain = await ContractsAsync("call", today, today.AddYears(2), 500);

		var wanted = chain.Select(c => c.ExpirationDate.Date).Distinct().OrderBy(d => d).Last();

		var narrowed = await ContractsAsync("call", wanted, wanted, 200);

		IsTrue(narrowed.Count > 0, $"nothing came back for {wanted:yyyy-MM-dd}.");
		AreEqual(1, narrowed.Select(c => c.ExpirationDate.Date).Distinct().Count());
		AreEqual(wanted, narrowed[0].ExpirationDate.Date);
	}

	/// <summary>
	/// A contract has candle history, fetched without a feed parameter. The bar endpoint rejects one
	/// outright, so a feed leaking onto this request would answer 400 rather than data.
	/// </summary>
	[TestMethod]
	[TestCategory("Integration")]
	public async Task AContractHasCandleHistory()
	{
		var today = DateTime.UtcNow.Date;

		// The nearest expiry is the one that has actually traded; a far one can be listed and untouched.
		var contract = (await ContractsAsync("call", today, today.AddMonths(2), 300))
			.OrderBy(c => c.ExpirationDate)
			.ThenBy(c => c.StrikePrice)
			.First();

		using var client = Options();

		var candles = new List<Ohlc>();

		await foreach (var candle in client.GetOhlc(contract.Symbol, "1Day", today.AddDays(-30), today, 100, CancellationToken))
			candles.Add(candle);

		if (candles.Count == 0)
			Inconclusive($"{contract.Symbol} has not traded in the last thirty days.");

		foreach (var candle in candles)
		{
			IsTrue(candle.High >= candle.Low, $"{contract.Symbol} produced a malformed bar.");
			IsTrue(candle.Close > 0);
		}
	}

	/// <summary>
	/// Trades come back too, and they parse. The option endpoints do not send every field in the shape
	/// the equity ones do — conditions arrive as a single code where equities send a list — so a model
	/// written against the equity payload throws here on data no equity test would ever produce.
	/// </summary>
	[TestMethod]
	[TestCategory("Integration")]
	public async Task AContractHasTradeHistory()
	{
		var today = DateTime.UtcNow.Date;

		var contract = (await ContractsAsync("call", today, today.AddMonths(2), 300))
			.OrderBy(c => c.ExpirationDate)
			.ThenBy(c => c.StrikePrice)
			.First();

		using var client = Options();

		var trades = new List<Tick>();

		await foreach (var trade in client.GetTicks(contract.Symbol, today.AddDays(-30), today, 50, CancellationToken))
		{
			trades.Add(trade);

			if (trades.Count >= 50)
				break;
		}

		if (trades.Count == 0)
			Inconclusive($"{contract.Symbol} has not traded in the last thirty days.");

		foreach (var trade in trades)
			IsTrue(trade.Price > 0, $"{contract.Symbol} produced a trade at {trade.Price}.");
	}

	/// <summary>The current quote of a contract, which is the only quote an option has here.</summary>
	[TestMethod]
	[TestCategory("Integration")]
	public async Task AContractShowsItsCurrentQuote()
	{
		var today = DateTime.UtcNow.Date;
		var contracts = await ContractsAsync("call", today, today.AddMonths(2), 10);

		using var client = Options();

		var quotes = await client.GetLatestQuotes(contracts.Select(c => c.Symbol), null, CancellationToken);

		IsTrue(quotes.Count > 0, "no contract showed a quote.");

		foreach (var pair in quotes)
			IsTrue(pair.Value.BidPrice > 0 || pair.Value.AskPrice > 0, $"{pair.Key} showed neither a bid nor an ask.");
	}

	/// <summary>
	/// A feed the account has no agreement for is refused rather than quietly downgraded, and the
	/// refusal names the agreement that is missing.
	/// </summary>
	[TestMethod]
	[TestCategory("Integration")]
	public async Task AFeedTheAccountCannotReadIsRefused()
	{
		var today = DateTime.UtcNow.Date;
		var contracts = await ContractsAsync("call", today, today.AddMonths(2), 3);

		using var client = Options();

		var refused = false;

		try
		{
			await client.GetLatestQuotes(contracts.Select(c => c.Symbol), "opra", CancellationToken);
		}
		catch (Exception error)
		{
			refused = true;

			IsTrue(error.ToString().ContainsIgnoreCase("OPRA"),
				$"the refusal did not name the missing agreement: {error.Message}");
		}

		if (!refused)
			Inconclusive("this account is entitled to the consolidated tape, so there was nothing to refuse.");
	}

	/// <summary>
	/// The adapter itself returns options to a lookup that asks for them, and none to one that does not.
	/// One session, because this is the only check that needs the adapter rather than the clients.
	/// </summary>
	[TestMethod]
	[TestCategory("Integration")]
	public async Task TheAdapterReturnsOptionsOnlyWhenAsked()
	{
		// A session per lookup: an adapter that has connected once refuses to connect again until the
		// first connection has fully gone, and these two lookups are separate questions anyway.
		async Task<List<SecurityMessage>> lookup(SecurityLookupMessage criteria)
		{
			var (key, secret) = Credentials();

			using var adapter = new AlpacaMessageAdapter(new IncrementalIdGenerator())
			{
				Key = key,
				Secret = secret,
				IsDemo = true,
			};

			adapter.Sections = [AlpacaSections.Stock, AlpacaSections.Option];

			var found = new List<SecurityMessage>();

			await foreach (var message in adapter.ConnectAndDownloadAsync<SecurityMessage>(criteria).WithCancellation(CancellationToken))
				found.Add(message);

			return found;
		}

		var options = await lookup(new()
		{
			TransactionId = 1,
			SecurityTypes = [SecurityTypes.Option],
			UnderlyingSecurityId = new() { SecurityCode = _underlying },
			OptionType = OptionTypes.Call,
			Count = 20,
		});

		IsTrue(options.Count > 0, "the option lookup came back empty.");
		IsTrue(options.All(o => o.SecurityType == SecurityTypes.Option));

		var equities = await lookup(new()
		{
			TransactionId = 2,
			SecurityTypes = [SecurityTypes.Stock],
			Count = 100,
		});

		IsTrue(equities.Count > 0, "the equity lookup came back empty.");
		AreEqual(0, equities.Count(s => s.SecurityType == SecurityTypes.Option));
	}
}
