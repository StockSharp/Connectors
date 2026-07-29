namespace StockSharp.Connectors.Tests;

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

using Ecng.UnitTesting;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using StockSharp.Intrinio;
using StockSharp.Intrinio.Native;

[TestClass]
public class IntrinioRealtimeProtocolTests : BaseTestClass
{
	private const string EquityTradeHex =
		"0020044141504C0658000000484164000000BCCC853DFE9C9717393000000140";
	private const string EquityAskHex =
		"011C044141504C03510000004C41C80000002491C963FE9C97170152";
	private const string EquityBidHex =
		"021B044141504C0845000000444196000000E0FC686AFE9C971700";
	private const string OptionTradeHex =
		"124141504C5F323630383231433132332E343500000000040240E201000700000015CD853DFE9C971763000000000000006CE2010008E201004D4900000013183051000000000000";
	private const string OptionQuoteHex =
		"124141504C5F323630383231433132332E34350000000103393000000B000000343000000C000000B1680871FE9C971700000000";
	private const string OptionRefreshHex =
		"124141504C5F323630383231433132332E34350000000202D2040000D2040000B0040000140500004C0400000000000000000000";

	[TestMethod]
	public void EndpointBuildersMatchOfficialSdk()
	{
		AreEqual(
			"https://realtime-mx.intrinio.com/auth?api_key=key",
			IntrinioRealtimeProtocol.GetEquityAuthUri(
				IntrinioEquityProviders.Realtime, "key").AbsoluteUri);
		AreEqual(
			"https://realtime-mx.intrinio.com/auth?api_key=key",
			IntrinioRealtimeProtocol.GetEquityAuthUri(
				IntrinioEquityProviders.Iex, "key").AbsoluteUri);
		AreEqual(
			"https://realtime-delayed-sip.intrinio.com/auth?api_key=key",
			IntrinioRealtimeProtocol.GetEquityAuthUri(
				IntrinioEquityProviders.DelayedSip, "key").AbsoluteUri);
		AreEqual(
			"https://realtime-nasdaq-basic.intrinio.com/auth?api_key=key",
			IntrinioRealtimeProtocol.GetEquityAuthUri(
				IntrinioEquityProviders.NasdaqBasic, "key").AbsoluteUri);
		AreEqual(
			"https://cboe-one.intrinio.com/auth?api_key=key",
			IntrinioRealtimeProtocol.GetEquityAuthUri(
				IntrinioEquityProviders.CboeOne, "key").AbsoluteUri);
		AreEqual(
			"https://equities-edge.intrinio.com/auth?api_key=key",
			IntrinioRealtimeProtocol.GetEquityAuthUri(
				IntrinioEquityProviders.EquitiesEdge, "key").AbsoluteUri);

		AreEqual(
			"wss://realtime-delayed-sip.intrinio.com/socket/websocket?vsn=1.0.0&token=token&delayed=true",
			IntrinioRealtimeProtocol.GetEquityWebSocketUri(
				IntrinioEquityProviders.DelayedSip, "token").AbsoluteUri);
		AreEqual(
			"wss://equities-edge.intrinio.com/socket/websocket?vsn=1.0.0&token=token",
			IntrinioRealtimeProtocol.GetEquityWebSocketUri(
				IntrinioEquityProviders.EquitiesEdge, "token").AbsoluteUri);

		AreEqual(
			"https://realtime-options.intrinio.com/auth?api_key=key",
			IntrinioRealtimeProtocol.GetOptionsAuthUri(
				IntrinioOptionProviders.Opra, "key").AbsoluteUri);
		AreEqual(
			"https://options-edge.intrinio.com/auth?api_key=key",
			IntrinioRealtimeProtocol.GetOptionsAuthUri(
				IntrinioOptionProviders.OptionsEdge, "key").AbsoluteUri);
		AreEqual(
			"wss://realtime-options.intrinio.com/socket/websocket?vsn=1.0.0&token=token&delayed=true",
			IntrinioRealtimeProtocol.GetOptionsWebSocketUri(
				IntrinioOptionProviders.Opra, "token", true).AbsoluteUri);
		AreEqual(
			"wss://options-edge.intrinio.com/socket/websocket?vsn=1.0.0&token=token",
			IntrinioRealtimeProtocol.GetOptionsWebSocketUri(
				IntrinioOptionProviders.OptionsEdge, "token", false).AbsoluteUri);
		AreEqual(
			"https://realtime-mx.intrinio.com/auth?api_key=a%2B%20%2F%3F",
			IntrinioRealtimeProtocol.GetEquityAuthUri(
				IntrinioEquityProviders.Iex, "a+ /?").AbsoluteUri);
		AreEqual(
			"wss://realtime-options.intrinio.com/socket/websocket?vsn=1.0.0&token=a%2B%20%2F%3F",
			IntrinioRealtimeProtocol.GetOptionsWebSocketUri(
				IntrinioOptionProviders.Opra, "a+ /?", false).AbsoluteUri);

		ThrowsExactly<ArgumentOutOfRangeException>(() =>
			IntrinioRealtimeProtocol.GetEquityAuthUri(
				(IntrinioEquityProviders)int.MaxValue, "key"));
		ThrowsExactly<ArgumentOutOfRangeException>(() =>
			IntrinioRealtimeProtocol.GetOptionsAuthUri(
				(IntrinioOptionProviders)int.MaxValue, "key"));
	}

	[TestMethod]
	public void HeaderBuildersSelectV2Formats()
	{
		var equityAuth = IntrinioRealtimeProtocol.GetEquityAuthHeaders();
		AreEqual(2, equityAuth.Count);
		AreEqual("StockSharp.Intrinio/1.0",
			equityAuth["Client-Information"]);
		AreEqual("v2", equityAuth["UseNewEquitiesFormat"]);

		var equitySocket = IntrinioRealtimeProtocol.GetEquityWebSocketHeaders();
		AreEqual(1, equitySocket.Count);
		AreEqual("v2", equitySocket["UseNewEquitiesFormat"]);

		var optionsAuth = IntrinioRealtimeProtocol.GetOptionsAuthHeaders(true);
		AreEqual(3, optionsAuth.Count);
		AreEqual("StockSharp.Intrinio/1.0",
			optionsAuth["Client-Information"]);
		AreEqual("v2", optionsAuth["UseNewOptionsFormat"]);
		AreEqual("true", optionsAuth["delay"]);

		var optionsSocket =
			IntrinioRealtimeProtocol.GetOptionsWebSocketHeaders(false);
		AreEqual(1, optionsSocket.Count);
		AreEqual("v2", optionsSocket["UseNewOptionsFormat"]);
		IsTrue(!optionsSocket.ContainsKey("delay"));
	}

	[TestMethod]
	public void SubscriptionMessagesMatchOfficialSdk()
	{
		AreBytesEqual("4A004141504C",
			IntrinioRealtimeProtocol.EncodeEquityJoin("AAPL", false));
		AreBytesEqual("4A014141504C",
			IntrinioRealtimeProtocol.EncodeEquityJoin("AAPL", true));
		AreBytesEqual("4C4141504C",
			IntrinioRealtimeProtocol.EncodeEquityLeave("AAPL"));
		AreBytesEqual("4A002446495245484F5345",
			IntrinioRealtimeProtocol.EncodeEquityJoin("lobby", false));
		AreBytesEqual("4C2446495245484F5345",
			IntrinioRealtimeProtocol.EncodeEquityLeave("lobby"));

		AreBytesEqual("4A074141504C5F323630383231433132332E3435",
			IntrinioRealtimeProtocol.EncodeOptionsJoin(
				"AAPL__260821C00123450", false));
		AreBytesEqual("4A054141504C5F323630383231433132332E3435",
			IntrinioRealtimeProtocol.EncodeOptionsJoin(
				"AAPL260821C00123450", true));
		AreBytesEqual("4C004141504C5F323630383231433132332E3435",
			IntrinioRealtimeProtocol.EncodeOptionsLeave(
				"AAPL__260821C00123450"));
		AreBytesEqual("4A072446495245484F5345",
			IntrinioRealtimeProtocol.EncodeOptionsJoin("lobby", false));
		AreBytesEqual("4C2446495245484F5345",
			IntrinioRealtimeProtocol.EncodeOptionsLeave("lobby"));
		AreBytesEqual("4A074142435F323630383231503132332E303035",
			IntrinioRealtimeProtocol.EncodeOptionsJoin(
				"ABC___260821P00123005", false));
		AreBytesEqual("4A074141504C",
			IntrinioRealtimeProtocol.EncodeOptionsJoin("AAPL", false));

		ThrowsExactly<ArgumentException>(() =>
			IntrinioRealtimeProtocol.EncodeEquityJoin("", false));
		ThrowsExactly<ArgumentException>(() =>
			IntrinioRealtimeProtocol.EncodeOptionsJoin("A\u0080PL", false));
	}

	[TestMethod]
	public void DecodesEquityGoldenBatch()
	{
		var events = IntrinioRealtimeProtocol.DecodeEquity(Batch(
			Hex(EquityTradeHex), Hex(EquityAskHex), Hex(EquityBidHex)));

		AreEqual(3, events.Count);

		var tradeEvent = events[0];
		AreEqual(IntrinioDecodedEventTypes.EquityTrade, tradeEvent.Type);
		AreEqual("AAPL", tradeEvent.Symbol);
		IsTrue(!tradeEvent.IsOption);
		var trade = tradeEvent.EquityTrade;
		AreEqual("AAPL", trade.Symbol);
		AreEqual(12.5d, trade.Price);
		AreEqual(100u, trade.Size);
		AreEqual(12345ul, trade.TotalVolume);
		AreEqual(Utc(1_700_000_000, 1_234_567), trade.Timestamp);
		AreEqual((byte)6, trade.SubProvider);
		AreEqual('X', trade.MarketCenter);
		AreEqual("@", trade.Condition);

		var askEvent = events[1];
		AreEqual(IntrinioDecodedEventTypes.EquityQuote, askEvent.Type);
		var ask = askEvent.EquityQuote;
		AreEqual(IntrinioEquityQuoteTypes.Ask, ask.Type);
		AreEqual("AAPL", ask.Symbol);
		AreEqual(12.75d, ask.Price);
		AreEqual(200u, ask.Size);
		AreEqual(Utc(1_700_000_000, 7_654_321), ask.Timestamp);
		AreEqual((byte)3, ask.SubProvider);
		AreEqual('Q', ask.MarketCenter);
		AreEqual("R", ask.Condition);

		var bid = events[2].EquityQuote;
		AreEqual(IntrinioEquityQuoteTypes.Bid, bid.Type);
		AreEqual(12.25d, bid.Price);
		AreEqual(150u, bid.Size);
		AreEqual(Utc(1_700_000_000, 8_765_432), bid.Timestamp);
		AreEqual((byte)8, bid.SubProvider);
		AreEqual('E', bid.MarketCenter);
		AreEqual("", bid.Condition);
	}

	[TestMethod]
	public void DecodesOptionsGoldenBatch()
	{
		var events = IntrinioRealtimeProtocol.DecodeOptions(Batch(
			Hex(OptionTradeHex), Hex(OptionQuoteHex), Hex(OptionRefreshHex)));

		AreEqual(3, events.Count);

		var tradeEvent = events[0];
		AreEqual(IntrinioDecodedEventTypes.OptionTrade, tradeEvent.Type);
		AreEqual("AAPL__260821C00123450", tradeEvent.Symbol);
		IsTrue(tradeEvent.IsOption);
		var trade = tradeEvent.OptionTrade;
		AreEqual("AAPL__260821C00123450", trade.Contract);
		AreEqual(12.3456d, trade.Price);
		AreEqual(7u, trade.Size);
		AreEqual(1_700_000_000.1234567d, trade.Timestamp);
		AreEqual(99ul, trade.TotalVolume);
		AreEqual(12.35d, trade.AskPriceAtExecution);
		AreEqual(12.34d, trade.BidPriceAtExecution);
		AreEqual(187.65d, trade.UnderlyingPriceAtExecution);
		IsTrue(trade.Qualifiers.SequenceEqual(
			new byte[] { 0, 19, 24, 48 }));
		AreEqual('Q', trade.Exchange);

		var quote = events[1].OptionQuote;
		AreEqual("AAPL__260821C00123450", quote.Contract);
		AreEqual(12.345d, quote.AskPrice);
		AreEqual(11u, quote.AskSize);
		AreEqual(12.34d, quote.BidPrice);
		AreEqual(12u, quote.BidSize);
		AreEqual(1_700_000_000.9876544d, quote.Timestamp);

		var refresh = events[2].OptionRefresh;
		AreEqual("AAPL__260821C00123450", refresh.Contract);
		AreEqual(1234u, refresh.OpenInterest);
		AreEqual(12.34d, refresh.OpenPrice);
		AreEqual(12d, refresh.ClosePrice);
		AreEqual(13d, refresh.HighPrice);
		AreEqual(11d, refresh.LowPrice);
	}

	[TestMethod]
	public void OptionsDecoderPreservesMissingPriceSentinels()
	{
		var quoteBytes = Hex(OptionQuoteHex);
		Hex("FFFFFF7F").CopyTo(quoteBytes, 24);
		Hex("00000080").CopyTo(quoteBytes, 32);

		var quote = IntrinioRealtimeProtocol.DecodeOptions(
			Batch(quoteBytes))[0].OptionQuote;

		IsTrue(double.IsNaN(quote.AskPrice));
		IsTrue(double.IsNaN(quote.BidPrice));
	}

	[TestMethod]
	public void OptionsDecoderSupportsOfficialPriceScaleTable()
	{
		var quoteBytes = Hex(OptionQuoteHex);
		quoteBytes[23] = 10;
		BinaryPrimitives.WriteInt32LittleEndian(quoteBytes.AsSpan(24, 4), 512);
		BinaryPrimitives.WriteInt32LittleEndian(quoteBytes.AsSpan(32, 4), 1024);
		var quote = IntrinioRealtimeProtocol.DecodeOptions(
			Batch(quoteBytes))[0].OptionQuote;
		AreEqual(1d, quote.AskPrice);
		AreEqual(2d, quote.BidPrice);

		quoteBytes[23] = 11;
		BinaryPrimitives.WriteInt32LittleEndian(quoteBytes.AsSpan(24, 4), 1);
		BinaryPrimitives.WriteInt32LittleEndian(quoteBytes.AsSpan(32, 4), -1);
		quote = IntrinioRealtimeProtocol.DecodeOptions(
			Batch(quoteBytes))[0].OptionQuote;
		IsTrue(double.IsPositiveInfinity(quote.AskPrice));
		IsTrue(double.IsNegativeInfinity(quote.BidPrice));

		quoteBytes[23] = 15;
		quote = IntrinioRealtimeProtocol.DecodeOptions(
			Batch(quoteBytes))[0].OptionQuote;
		IsTrue(double.IsNaN(quote.AskPrice));
		IsTrue(double.IsNaN(quote.BidPrice));
	}

	[TestMethod]
	public void DecodersPreserveForwardCompatibleMetadata()
	{
		var equityBytes = Hex(EquityTradeHex);
		equityBytes[7] = 9;
		BinaryPrimitives.WriteUInt16LittleEndian(
			equityBytes.AsSpan(8, 2), '\u03a9');
		var equity = IntrinioRealtimeProtocol.DecodeEquity(
			Batch(equityBytes))[0].EquityTrade;
		AreEqual((byte)9, equity.SubProvider);
		AreEqual('\u03a9', equity.MarketCenter);

		var optionBytes = Hex(OptionTradeHex);
		optionBytes[65] = 0xff;
		var option = IntrinioRealtimeProtocol.DecodeOptions(
			Batch(optionBytes))[0].OptionTrade;
		AreEqual('\u00ff', option.Exchange);
	}

	[TestMethod]
	public void OptionContractDecoderTruncatesFourthFractionalDigit()
	{
		var quoteBytes = Hex(OptionQuoteHex);
		SetOptionContract(quoteBytes, "ABC_260821P123.0057");

		var quote = IntrinioRealtimeProtocol.DecodeOptions(
			Batch(quoteBytes))[0].OptionQuote;

		AreEqual("ABC___260821P00123005", quote.Contract);
	}

	[TestMethod]
	public void OptionsDecoderSkipsKnownUnusualActivityChunks()
	{
		var unusualActivity = new byte[74];
		var contract = Encoding.ASCII.GetBytes("AAPL_260821C123.45");
		unusualActivity[0] = checked((byte)contract.Length);
		contract.CopyTo(unusualActivity, 1);
		unusualActivity[22] = 3;

		var events = IntrinioRealtimeProtocol.DecodeOptions(
			Batch(unusualActivity, Hex(OptionQuoteHex)));

		AreEqual(1, events.Count);
		AreEqual(IntrinioDecodedEventTypes.OptionQuote, events[0].Type);
	}

	[TestMethod]
	public void RejectsMalformedEquityBatches()
	{
		ExpectInvalidEquity([]);
		ExpectInvalidEquity([1]);
		ExpectInvalidEquity([0, 0]);
		ExpectInvalidEquity([1, 3, 2]);

		var wrongLength = Hex(EquityTradeHex);
		wrongLength[1]--;
		ExpectInvalidEquity(Batch(wrongLength));

		var invalidSymbolLength = Hex(EquityTradeHex);
		invalidSymbolLength[2] = 30;
		ExpectInvalidEquity(Batch(invalidSymbolLength));

		var nonAsciiSymbol = Hex(EquityTradeHex);
		nonAsciiSymbol[3] = 0x80;
		ExpectInvalidEquity(Batch(nonAsciiSymbol));

	}

	[TestMethod]
	public void RejectsMalformedOptionsBatches()
	{
		ExpectInvalidOptions([]);
		ExpectInvalidOptions([1]);
		ExpectInvalidOptions([0, 0]);

		var truncated = Hex(OptionTradeHex)[..^1];
		ExpectInvalidOptions(Batch(truncated));

		var invalidContractLength = Hex(OptionQuoteHex);
		invalidContractLength[0] = 22;
		ExpectInvalidOptions(Batch(invalidContractLength));

		var malformedContract = Hex(OptionQuoteHex);
		malformedContract[5] = (byte)'X';
		ExpectInvalidOptions(Batch(malformedContract));

		var invalidPriceType = Hex(OptionQuoteHex);
		invalidPriceType[23] = 16;
		ExpectInvalidOptions(Batch(invalidPriceType));

		var unknownType = Hex(OptionQuoteHex);
		unknownType[22] = 7;
		ExpectInvalidOptions(Batch(unknownType));
	}

	private void ExpectInvalidEquity(byte[] bytes)
		=> ThrowsExactly<InvalidDataException>(() =>
			IntrinioRealtimeProtocol.DecodeEquity(bytes));

	private void ExpectInvalidOptions(byte[] bytes)
		=> ThrowsExactly<InvalidDataException>(() =>
			IntrinioRealtimeProtocol.DecodeOptions(bytes));

	private void AreBytesEqual(string expectedHex, byte[] actual)
		=> IsTrue(Hex(expectedHex).SequenceEqual(actual));

	private static byte[] Batch(params byte[][] chunks)
	{
		var result = new byte[1 + chunks.Sum(chunk => chunk.Length)];
		result[0] = checked((byte)chunks.Length);
		var offset = 1;
		foreach (var chunk in chunks)
		{
			chunk.CopyTo(result, offset);
			offset += chunk.Length;
		}
		return result;
	}

	private static byte[] Hex(string value)
		=> Convert.FromHexString(value);

	private static void SetOptionContract(byte[] chunk, string contract)
	{
		Array.Clear(chunk, 1, 21);
		var bytes = Encoding.ASCII.GetBytes(contract);
		chunk[0] = checked((byte)bytes.Length);
		bytes.CopyTo(chunk, 1);
	}

	private static DateTime Utc(long seconds, long ticks)
		=> DateTime.UnixEpoch.AddSeconds(seconds).AddTicks(ticks);
}
