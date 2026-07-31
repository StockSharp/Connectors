namespace StockSharp.Connectors.Tests;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using Ecng.Common;
using Ecng.Serialization;
using Ecng.UnitTesting;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using Newtonsoft.Json.Linq;

using StockSharp.Messages;
using StockSharp.Settrade;
using StockSharp.Settrade.Native;

[TestClass]
public class SettradeTests : BaseTestClass
{
	private sealed class Handler(
		Func<HttpRequestMessage, CancellationToken,
			Task<HttpResponseMessage>> callback) : HttpMessageHandler
	{
		protected override Task<HttpResponseMessage> SendAsync(
			HttpRequestMessage request,
			CancellationToken cancellationToken)
			=> callback(request, cancellationToken);
	}

	private sealed class ProtoWriter : IDisposable
	{
		private readonly MemoryStream _stream = new();

		public void VarInt(int field, ulong value)
		{
			Tag(field, 0);
			WriteVarInt(value);
		}

		public void String(int field, string value)
			=> Bytes(field, Encoding.UTF8.GetBytes(value));

		public void Money(int field, long units, int nanos = 0)
		{
			using var nested = new ProtoWriter();
			nested.VarInt(1, unchecked((ulong)units));
			if (nanos != 0)
				nested.VarInt(2, unchecked((uint)nanos));
			Bytes(field, nested.ToArray());
		}

		public void Timestamp(int field, DateTime value)
		{
			var timestamp = new DateTimeOffset(
				value.ToUniversalTime());
			using var nested = new ProtoWriter();
			nested.VarInt(1,
				unchecked((ulong)timestamp.ToUnixTimeSeconds()));
			var nanos = (int)((timestamp -
				DateTimeOffset.FromUnixTimeSeconds(
					timestamp.ToUnixTimeSeconds())).Ticks * 100);
			if (nanos != 0)
				nested.VarInt(2, unchecked((uint)nanos));
			Bytes(field, nested.ToArray());
		}

		public void TimeOfDay(int field, int hour, int minute,
			int second)
		{
			using var nested = new ProtoWriter();
			nested.VarInt(1, (ulong)hour);
			nested.VarInt(2, (ulong)minute);
			nested.VarInt(3, (ulong)second);
			Bytes(field, nested.ToArray());
		}

		public void Date(int field, int year, int month, int day)
		{
			using var nested = new ProtoWriter();
			nested.VarInt(1, (ulong)year);
			nested.VarInt(2, (ulong)month);
			nested.VarInt(3, (ulong)day);
			Bytes(field, nested.ToArray());
		}

		public byte[] ToArray() => _stream.ToArray();

		private void Bytes(int field, byte[] value)
		{
			Tag(field, 2);
			WriteVarInt((ulong)value.Length);
			_stream.Write(value);
		}

		private void Tag(int field, int wire)
			=> WriteVarInt((ulong)((field << 3) | wire));

		private void WriteVarInt(ulong value)
		{
			do
			{
				var item = (byte)(value & 0x7f);
				value >>= 7;
				if (value != 0)
					item |= 0x80;
				_stream.WriteByte(item);
			}
			while (value != 0);
		}

		public void Dispose() => _stream.Dispose();
	}

	[TestMethod]
	public void DefaultsUseOfficialProductionAndSandboxEndpoints()
	{
		var adapter = new SettradeMessageAdapter(
			new IncrementalIdGenerator());

		AreEqual("https://open-api.settrade.com",
			adapter.RestEndpoint);
		AreEqual("https://open-api-test.settrade.com",
			adapter.DemoRestEndpoint);
		AreEqual("https://marketapi.settrade.com",
			adapter.MarketDataEndpoint);
		AreEqual("https://marketapi-test.settrade.com",
			adapter.DemoMarketDataEndpoint);
		AreEqual(TimeSpan.FromSeconds(5), adapter.PollingInterval);
		AreEqual(SettradeAccountTypes.Equity, adapter.AccountType);
		AreEqual(11, SettradeMessageAdapter.AllTimeFrames.Count());
	}

	[TestMethod]
	public void SettingsRoundTripKeepsCredentialsAndEndpoints()
	{
		var source = new SettradeMessageAdapter(
			new IncrementalIdGenerator())
		{
			Key = "app-id".Secure(),
			Secret = "AQ==".Secure(),
			AppCode = "APP",
			BrokerId = "038",
			Account = "ACC",
			Pin = "123456".Secure(),
			AccountType = SettradeAccountTypes.Derivatives,
			LoginParameters = "scope",
			IsDemo = true,
			RestEndpoint = "https://trade.example",
			DemoRestEndpoint = "https://trade-test.example",
			MarketDataEndpoint = "https://market.example",
			DemoMarketDataEndpoint = "https://market-test.example",
			PollingInterval = TimeSpan.FromSeconds(9),
		};
		var storage = new SettingsStorage();
		source.Save(storage);

		var target = new SettradeMessageAdapter(
			new IncrementalIdGenerator());
		target.Load(storage);

		AreEqual("app-id", target.Key.UnSecure());
		AreEqual("AQ==", target.Secret.UnSecure());
		AreEqual("APP", target.AppCode);
		AreEqual("038", target.BrokerId);
		AreEqual("ACC", target.Account);
		AreEqual("123456", target.Pin.UnSecure());
		AreEqual(SettradeAccountTypes.Derivatives,
			target.AccountType);
		AreEqual("scope", target.LoginParameters);
		IsTrue(target.IsDemo);
		AreEqual("https://trade.example", target.RestEndpoint);
		AreEqual("https://trade-test.example",
			target.DemoRestEndpoint);
		AreEqual("https://market.example",
			target.MarketDataEndpoint);
		AreEqual("https://market-test.example",
			target.DemoMarketDataEndpoint);
		AreEqual(TimeSpan.FromSeconds(9), target.PollingInterval);
	}

	[TestMethod]
	public void LoginSignatureIsValidDerEncodedP256Ecdsa()
	{
		const string appId = "application";
		const string parameters = "scope";
		const long timestamp = 1_722_000_000_123;
		var signature = Convert.FromHexString(new SettradeSigner(
			"AQ==".Secure()).Sign(appId, parameters, timestamp));

		var content = Encoding.UTF8.GetBytes(
			$"{appId}.{parameters}.{timestamp}");
		using var verifier = ECDsa.Create(new ECParameters
		{
			Curve = ECCurve.NamedCurves.nistP256,
			Q = new()
			{
				X = Convert.FromHexString(
					"6B17D1F2E12C4247F8BCE6E563A440F277037D812DEB33A0F4A13945D898C296"),
				Y = Convert.FromHexString(
					"4FE342E2FE1A7F9B8EE7EB4A7C0F9E162BCE33576B315ECECBB6406837BF51F5"),
			},
		});

		IsTrue(verifier.VerifyData(content, signature,
			HashAlgorithmName.SHA256,
			DSASignatureFormat.Rfc3279DerSequence));
	}

	[TestMethod]
	public async Task RestClientUsesOfficialPathsAndBearerToken()
	{
		var paths = new List<string>();
		var handler = new Handler(async (request, cancellationToken) =>
		{
			paths.Add(request.RequestUri.PathAndQuery);
			if (request.RequestUri.AbsolutePath.EndsWith("/login",
				StringComparison.Ordinal))
			{
				var body = JObject.Parse(await request.Content
					.ReadAsStringAsync(cancellationToken));
				AreEqual("application", body.Value<string>("apiKey"));
				AreEqual("scope", body.Value<string>("params"));
				IsTrue(body.Value<string>("signature").Length > 100);
				return Json(
					"""{"token_type":"Bearer","access_token":"access","refresh_token":"refresh","expires_in":3600}""");
			}
			AreEqual("Bearer",
				request.Headers.Authorization.Scheme);
			AreEqual("access",
				request.Headers.Authorization.Parameter);
			if (request.RequestUri.AbsolutePath.Contains("/quote/",
				StringComparison.Ordinal))
				return Json(
					"""{"symbol":"AOT","last":44.25,"totalVolume":1000}""");
			if (request.RequestUri.AbsolutePath.Contains(
				"/candlesticks", StringComparison.Ordinal))
				return Json(
					"""{"t":[1722000000],"o":[44],"h":[45],"l":[43],"c":[44.5],"v":[100]}""");
			if (request.RequestUri.AbsolutePath.EndsWith("/token",
				StringComparison.Ordinal))
				return Json(
					"""{"hosts":["mqtt.settrade.test"],"token":"stream"}""");
			return new(HttpStatusCode.NotFound);
		});
		using var client = new SettradeRestClient(
			"https://open-api.example", "https://market.example",
			"application", "AQ==".Secure(), "APP", "038", "scope",
			handler);

		await client.LoginAsync(CancellationToken);
		var quote = await client.GetQuoteAsync("AOT",
			CancellationToken);
		var candles = await client.GetCandlesAsync("AOT", "1m", 10,
			null, null, CancellationToken);
		var dispatcher = await client.GetDispatcherAsync(
			CancellationToken);

		AreEqual(44.25m, quote.Value<decimal>("last"));
		AreEqual(1, candles.ToCandles().Length);
		AreEqual("mqtt.settrade.test", dispatcher.Host);
		IsTrue(paths.Any(path => path ==
			"/api/oam/v1/038/broker-apps/APP/login"));
		IsTrue(paths.Any(path => path ==
			"/api/marketdata/v3/038/quote/AOT"));
		IsTrue(paths.Any(path =>
			path.StartsWith(
				"/api/techchart/v3/038/candlesticks?",
				StringComparison.Ordinal)));
		IsTrue(paths.Any(path =>
			path == "/api/dispatcher/v3/038/token"));
	}

	[TestMethod]
	public async Task RestClientRelogsAndRetriesAfterUnauthorized()
	{
		var loginCount = 0;
		var quoteCount = 0;
		var handler = new Handler((request, _) =>
		{
			if (request.RequestUri.AbsolutePath.EndsWith("/login",
				StringComparison.Ordinal))
			{
				loginCount++;
				return Task.FromResult(Json(
					$@"{{""accessToken"":""access-{loginCount}"",""refreshToken"":""refresh"",""expiresIn"":3600}}"));
			}
			quoteCount++;
			if (quoteCount == 1)
			{
				AreEqual("access-1",
					request.Headers.Authorization.Parameter);
				return Task.FromResult(new HttpResponseMessage(
					HttpStatusCode.Unauthorized)
				{
					Content = new StringContent(
						"""{"message":"session expired"}"""),
				});
			}
			AreEqual("access-2",
				request.Headers.Authorization.Parameter);
			return Task.FromResult(Json(
				"""{"symbol":"AOT","last":44.25}"""));
		});
		using var client = new SettradeRestClient(
			"https://open-api.example", "https://market.example",
			"application", "AQ==".Secure(), "APP", "038", "scope",
			handler);

		var quote = await client.GetQuoteAsync("AOT",
			CancellationToken);

		AreEqual(2, loginCount);
		AreEqual(2, quoteCount);
		AreEqual(44.25m, quote.Value<decimal>("last"));
	}

	[TestMethod]
	public void DerivativeRestOrderUsesOfficialQuantityAliases()
	{
		var order = JObject.Parse(
			"""{"orderNo":"123","seriesId":"S50U26","longShort":"Long","priceType":"Limit","validity":"Day","status":"E","price":812.5,"qty":5,"matchQty":2,"balanceQty":1,"cancelQty":2,"transactionTime":"2026-07-28T10:30:05Z"}""")
			.ToSettradeOrder();

		AreEqual(5m, order.Volume);
		AreEqual(2m, order.MatchedVolume);
		AreEqual(1m, order.BalanceVolume);
		AreEqual(2m, order.CancelledVolume);
		AreEqual(new DateTime(2026, 7, 28, 10, 30, 5,
			DateTimeKind.Utc), order.Time);
		AreEqual(OrderStates.Done, order.Status.ToOrderState());
	}

	[TestMethod]
	public void ProtobufDecodersPreservePricesVolumesAndCandleTime()
	{
		using var infoWriter = new ProtoWriter();
		infoWriter.String(1, "AOT");
		infoWriter.Money(2, 45, 250_000_000);
		infoWriter.Money(3, 43);
		infoWriter.Money(4, 44, 500_000_000);
		infoWriter.VarInt(5, 12_345);
		infoWriter.Money(8, 543_210);
		infoWriter.VarInt(9, 5);
		var info = SettradeProtoDecoder.DecodeLevel1(
			infoWriter.ToArray());

		using var bookWriter = new ProtoWriter();
		bookWriter.String(1, "AOT");
		bookWriter.Money(2, 44, 250_000_000);
		bookWriter.VarInt(12, 500);
		bookWriter.Money(7, 44, 500_000_000);
		bookWriter.VarInt(17, 700);
		bookWriter.Money(24, 44);
		bookWriter.VarInt(34, 300);
		var book = SettradeProtoDecoder.DecodeOrderBook(
			bookWriter.ToArray());

		var time = new DateTime(2026, 7, 28, 3, 15, 0,
			DateTimeKind.Utc);
		using var candleWriter = new ProtoWriter();
		candleWriter.String(1, "AOT");
		candleWriter.String(2, "1m");
		candleWriter.VarInt(3, 99);
		candleWriter.Timestamp(4, time);
		candleWriter.Money(5, 44);
		candleWriter.Money(6, 45);
		candleWriter.Money(7, 43);
		candleWriter.Money(8, 44, 750_000_000);
		candleWriter.VarInt(9, 1_000);
		candleWriter.Money(10, 44_750);
		var candle = SettradeProtoDecoder.DecodeCandle(
			candleWriter.ToArray());

		AreEqual("AOT", info.Symbol);
		AreEqual(45.25m, info.High);
		AreEqual(44.5m, info.Last);
		AreEqual(12_345m, info.TotalVolume);
		AreEqual(2, book.Bids.Length);
		AreEqual(44.25m, book.Bids[0].Price);
		AreEqual(500m, book.Bids[0].Volume);
		AreEqual(44.5m, book.Asks.Single().Price);
		AreEqual(time, candle.Time);
		AreEqual(44.75m, candle.Close);
		AreEqual(1_000m, candle.Volume);
		AreEqual(44_750m, candle.Turnover);
	}

	[TestMethod]
	public void EquityOrderProtobufMapsStatusAndValidity()
	{
		using var writer = new ProtoWriter();
		writer.VarInt(1, 3);
		writer.String(2, "12345");
		writer.String(4, "ACC");
		writer.TimeOfDay(5, 10, 30, 5);
		writer.Date(6, 2026, 7, 28);
		writer.String(7, "AOT");
		writer.Money(9, 44, 250_000_000);
		writer.VarInt(10, 1);
		writer.VarInt(11, 1);
		writer.VarInt(12, 1_000);
		writer.VarInt(13, 400);
		writer.VarInt(14, 600);
		writer.String(16, "MP");
		writer.VarInt(17, 1);
		writer.VarInt(20, 5);

		var order = SettradeProtoDecoder.DecodeEquityOrder(
			writer.ToArray());

		AreEqual("12345", order.OrderNo);
		AreEqual("ACC", order.AccountNo);
		AreEqual("AOT", order.Symbol);
		AreEqual("Buy", order.Side);
		AreEqual("Limit", order.PriceType);
		AreEqual("Day", order.Validity);
		AreEqual(44.25m, order.Price);
		AreEqual(1_000m, order.Volume);
		AreEqual(400m, order.MatchedVolume);
		AreEqual(600m, order.BalanceVolume);
		AreEqual(new DateTime(2026, 7, 28, 10, 30, 5,
			DateTimeKind.Utc), order.Time);
		AreEqual(OrderStates.Active,
			order.Status.ToOrderState());
		IsTrue(order.CanCancel);
	}

	private static HttpResponseMessage Json(string content)
		=> new(HttpStatusCode.OK)
		{
			Content = new StringContent(content, Encoding.UTF8,
				"application/json"),
		};
}
