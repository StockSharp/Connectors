namespace StockSharp.Connectors.Tests;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using Ecng.Common;
using Ecng.Serialization;
using Ecng.UnitTesting;

using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

using StockSharp.Messages;
using StockSharp.Nubra;
using StockSharp.Nubra.Native;
using StockSharp.Nubra.Native.Protocol;

[TestClass]
public class NubraTests : BaseTestClass
{
    [TestMethod]
    public void SettingsRoundTripKeepsAuthenticationAndEndpoints()
    {
        var source = new NubraMessageAdapter(new IncrementalIdGenerator())
        {
            Token = "SESSION".Secure(),
            Phone = "919876543210",
            Mpin = "1234".Secure(),
            TotpSecret = "JBSWY3DPEHPK3PXP".Secure(),
            DeviceId = "DEVICE-1",
            IsDemo = true,
            PortfolioName = "CLIENT-1",
            DefaultProduct = NubraProducts.Iday,
            PollingInterval = TimeSpan.FromSeconds(21),
            ReconnectAttempts = 7,
            RestAddress = new("https://rest.example.test/"),
            UatRestAddress = new("https://uat.example.test/"),
            MarketDataAddress = new("wss://stream.example.test/ws"),
            UatMarketDataAddress = new("wss://uatstream.example.test/ws"),
        };
        var storage = new SettingsStorage();
        source.Save(storage);

        var target = new NubraMessageAdapter(new IncrementalIdGenerator());
        target.Load(storage);

        AreEqual(source.Token.UnSecure(), target.Token.UnSecure());
        AreEqual(source.Phone, target.Phone);
        AreEqual(source.Mpin.UnSecure(), target.Mpin.UnSecure());
        AreEqual(source.TotpSecret.UnSecure(), target.TotpSecret.UnSecure());
        AreEqual(source.DeviceId, target.DeviceId);
        AreEqual(source.IsDemo, target.IsDemo);
        AreEqual(source.PortfolioName, target.PortfolioName);
        AreEqual(source.DefaultProduct, target.DefaultProduct);
        AreEqual(source.PollingInterval, target.PollingInterval);
        AreEqual(source.ReconnectAttempts, target.ReconnectAttempts);
        AreEqual(source.RestAddress, target.RestAddress);
        AreEqual(source.UatRestAddress, target.UatRestAddress);
        AreEqual(source.MarketDataAddress, target.MarketDataAddress);
        AreEqual(source.UatMarketDataAddress, target.UatMarketDataAddress);
        AreEqual(target.UatRestAddress, target.EffectiveRestAddress);
        AreEqual(target.UatMarketDataAddress, target.EffectiveMarketDataAddress);
        IsTrue(target is ITokenAdapter);
        IsTrue(target is IDemoAdapter);
    }

    [TestMethod]
    public void DefaultsIntervalsAndTotpMatchPublishedProtocol()
    {
        var adapter = new NubraMessageAdapter(new IncrementalIdGenerator());

        AreEqual("https://api.nubra.io/", adapter.RestAddress.AbsoluteUri);
        AreEqual("https://uatapi.nubra.io/", adapter.UatRestAddress.AbsoluteUri);
        AreEqual(
            "wss://api.nubra.io/apibatch/ws",
            adapter.MarketDataAddress.AbsoluteUri);
        AreEqual(
            "wss://uatapi.nubra.io/apibatch/ws",
            adapter.UatMarketDataAddress.AbsoluteUri);
        AreEqual(11, NubraMessageAdapter.AllTimeFrames.Count());
        AreEqual("1s", TimeSpan.FromSeconds(1).ToNativeInterval());
        AreEqual("1mth", TimeSpan.FromDays(30).ToNativeInterval());
        AreEqual(
            287082,
            NubraRestClient.GenerateTotp(
                "GEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQ",
                new DateTime(1970, 1, 1, 0, 0, 59, DateTimeKind.Utc)));
        ThrowsExactly<FormatException>(() =>
            NubraRestClient.GenerateTotp(
                "INVALID!",
                DateTime.UnixEpoch));
    }

    [TestMethod]
    public async Task TotpLoginUsesDeviceHeaderAndBearerMpinExchange()
    {
        var handler = new CaptureHandler(
            new ResponseSpec("""{"data":{"auth_token":"AUTH-TOKEN"}}"""),
            new ResponseSpec(
                """{"data":{"session_token":"SESSION-TOKEN","user_id":"CLIENT-7"}}"""));
        using var client = new NubraRestClient(
            new("https://api.example.test/"),
            "DEVICE-7",
            null,
            handler);

        var result = await client.LoginWithTotp(
            "919876543210",
            "1234".Secure(),
            "GEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQ".Secure(),
            new DateTime(1970, 1, 1, 0, 0, 59, DateTimeKind.Utc),
            CancellationToken.None);

        AreEqual("SESSION-TOKEN", result.SessionToken);
        AreEqual("CLIENT-7", result.UserId);
        AreEqual("SESSION-TOKEN", client.Token);

        var login = handler.Requests[0];
        AreEqual(
            "https://api.example.test/totp/login",
            login.Uri.AbsoluteUri);
        AreEqual(HttpMethod.Post, login.Method);
        AreEqual("DEVICE-7", login.Header("x-device-id"));
        IsNull(login.Header("Authorization"));
        var loginBody = JObject.Parse(login.Body);
        AreEqual("919876543210", loginBody["phone"].Value<string>());
        AreEqual("287082", loginBody["totp"].Value<string>());
        AreEqual(string.Empty, loginBody["otp"].Value<string>());

        var verify = handler.Requests[1];
        AreEqual(
            "https://api.example.test/verifypin",
            verify.Uri.AbsoluteUri);
        AreEqual("Bearer AUTH-TOKEN", verify.Header("Authorization"));
        AreEqual("DEVICE-7", verify.Header("x-device-id"));
        AreEqual("1234", JObject.Parse(verify.Body)["pin"].Value<string>());
    }

    [TestMethod]
    public void InstrumentMasterMapsPublishedFieldsAndExchangeUnits()
    {
        var instruments = NubraRestClient.ParseInstruments(
            "NSE",
            JToken.Parse(
                """
				{
				  "exchange":"NSE",
				  "is_trading_on":true,
				  "refdata":[
				    {
				      "ref_id":2885,
				      "token":2885,
				      "stock_name":" RELIANCE ",
				      "series":"EQ",
				      "zanskar_name":"RELIANCE-EQ",
				      "lot_size":1,
				      "exchange":"NSE",
				      "derivative_type":"STOCK",
				      "isin":"INE002A01018",
				      "asset_type":"EQUITY",
				      "tick_size":5,
				      "underlying_prev_close":127000
				    },
				    {
				      "ref_id":9001,
				      "stock_name":"NIFTY26SEP25000CE",
				      "asset":"NIFTY",
				      "expiry":20260924,
				      "derivative_type":"OPT",
				      "option_type":"CE",
				      "strike_price":2500000,
				      "asset_type":"INDEX"
				    },
				    {
				      "ref_id":9002,
				      "stock_name":"NIFTY26SEPFUT",
				      "derivative_type":"FUT"
				    },
				    {
				      "ref_id":26000,
				      "stock_name":"NIFTY 50",
				      "derivative_type":"INDEX",
				      "isin":"N/A"
				    }
				  ]
				}
				"""));

        AreEqual(4, instruments.Length);
        AreEqual(SecurityTypes.Stock, instruments[0].ToSecurityType());
        AreEqual(SecurityTypes.Option, instruments[1].ToSecurityType());
        AreEqual(SecurityTypes.Future, instruments[2].ToSecurityType());
        AreEqual(SecurityTypes.Index, instruments[3].ToSecurityType());
        AreEqual(OptionTypes.Call, instruments[1].OptionType.ToOptionType());
        AreEqual(
            new DateTime(2026, 9, 24, 0, 0, 0, DateTimeKind.Utc),
            instruments[1].Expiry.ToExpiry());
        AreEqual("RELIANCE", instruments[0].StockName);
        AreEqual("NSE", instruments[1].Exchange);
        AreEqual("2885", instruments[0].ToSecurityId().Native);
        IsNull(instruments[3].Isin);
        AreEqual(1270m, instruments[0].PreviousClose.ToPrice());
        AreEqual(127075L, 1270.75m.ToNativePrice("price"));
        ThrowsExactly<ArgumentOutOfRangeException>(() =>
            1.001m.ToNativePrice("price"));
    }

    [TestMethod]
    public async Task SnapshotUsesBearerHeadersAndRequestedRefIdFallback()
    {
        var handler = new CaptureHandler(
            new ResponseSpec(
                """
				{"orderBook":{
				  "ts":1785047100000000000,
				  "bid":[{"p":245070,"q":10,"o":2}],
				  "ask":[{"p":245080,"q":11,"o":3}],
				  "ltp":245075,
				  "ltq":4,
				  "volume":12345
				}}
				"""));
        using var client = new NubraRestClient(
            new("https://api.example.test/"),
            "DEVICE-1",
            "SESSION".Secure(),
            handler);

        var update = await client.GetMarketUpdate(
            2885,
            20,
            CancellationToken.None);

        AreEqual(2885L, update.RefId);
        AreEqual(245075L, update.LastPrice);
        AreEqual(2450.75m, update.LastPrice.ToPrice());
        AreEqual(245070L, update.Bids.Single().Price);
        AreEqual(3L, update.Asks.Single().Orders);
        var request = handler.Requests.Single();
        AreEqual(
            "https://api.example.test/orderbooks/2885?levels=20",
            request.Uri.AbsoluteUri);
        AreEqual("Bearer SESSION", request.Header("Authorization"));
        AreEqual("DEVICE-1", request.Header("x-device-id"));
    }

    [TestMethod]
    public async Task CreateOrderUsesV3EnvelopeAndPublishedFields()
    {
        var handler = new CaptureHandler(
            new ResponseSpec(
                """{"orders":[{"intentOrderId":7311,"status":"CREATED"}]}""",
                HttpStatusCode.Created));
        using var client = new NubraRestClient(
            new("https://api.example.test/"),
            "DEVICE-2",
            "SESSION".Secure(),
            handler);
        var payload = NubraMessageAdapter.CreateOrderPayload(
            2885,
            10,
            Sides.Buy,
            NubraProducts.Cnc,
            OrderTypes.Limit,
            2450.75m,
            TimeInForce.PutInQueue,
            null,
            "stocksharp");

        var order = await client.PlaceOrder(
            payload,
            CancellationToken.None);

        AreEqual(7311L, order.IntentOrderId);
        var request = handler.Requests.Single();
        AreEqual(
            "https://api.example.test/sentinel/orders/create",
            request.Uri.AbsoluteUri);
        AreEqual(HttpMethod.Post, request.Method);
        AreEqual("Bearer SESSION", request.Header("Authorization"));
        var body = JObject.Parse(request.Body);
        var native = body["orders"].Single();
        AreEqual(2885L, native["refId"].Value<long>());
        AreEqual(10L, native["qty"].Value<long>());
        AreEqual("BUY", native["side"].Value<string>());
        AreEqual("CNC", native["deliveryType"].Value<string>());
        AreEqual("LIMIT", native["priceType"].Value<string>());
        AreEqual("DAY", native["validityType"].Value<string>());
        AreEqual(245075L, native["entryPrice"].Value<long>());
        AreEqual("ENTRY", native["executionMode"].Value<string>());
        IsFalse(native["isMultiLeg"].Value<bool>());
        AreEqual("stocksharp", native["stratTags"][0].Value<string>());
    }

    [TestMethod]
    public void TriggerOrderPayloadAndValidationMatchApiRules()
    {
        var buy = NubraMessageAdapter.CreateOrderPayload(
            9001,
            25,
            Sides.Buy,
            NubraProducts.Iday,
            OrderTypes.Conditional,
            0,
            TimeInForce.CancelBalance,
            100.25m,
            null);
        var sell = NubraMessageAdapter.CreateOrderPayload(
            9001,
            25,
            Sides.Sell,
            NubraProducts.Iday,
            OrderTypes.Conditional,
            99.50m,
            TimeInForce.PutInQueue,
            99m,
            null);

        AreEqual("MARKET", buy["priceType"].Value<string>());
        AreEqual("IOC", buy["validityType"].Value<string>());
        AreEqual(
            10025L,
            buy["entryConfig"]["triggers"]["ltp"]["atOrAbove"]["value"]
                .Value<long>());
        AreEqual("LIMIT", sell["priceType"].Value<string>());
        AreEqual("DAY", sell["validityType"].Value<string>());
        AreEqual(
            9900L,
            sell["entryConfig"]["triggers"]["ltp"]["atOrBelow"]["value"]
                .Value<long>());
        ThrowsExactly<InvalidOperationException>(() =>
            NubraMessageAdapter.CreateOrderPayload(
                9001,
                1,
                Sides.Buy,
                NubraProducts.Iday,
                OrderTypes.Conditional,
                0,
                null,
                null,
                null));
        ThrowsExactly<ArgumentException>(() =>
            NubraMessageAdapter.CreateOrderPayload(
                2885,
                1,
                Sides.Buy,
                NubraProducts.Cnc,
                OrderTypes.Market,
                0,
                null,
                null,
                "bad_tag"));
    }

    [TestMethod]
    public void OrderBucketsAndNativeStatesAreNormalized()
    {
        var orders = NubraRestClient.ParseOrders(
            JToken.Parse(
                """
				{"orders":{
				  "open":[
				    {
				      "intentOrderId":1,
				      "refId":2885,
				      "status":"OPEN",
				      "side":"BUY",
				      "orderQty":10,
				      "filledQty":2,
				      "entryPrice":245075,
				      "deliveryType":"CNC",
				      "priceType":"LIMIT",
				      "validityType":"DAY"
				    }
				  ],
				  "executed":[
				    {
				      "intentOrderId":2,
				      "refId":2885,
				      "status":"EXECUTED",
				      "qty":5,
				      "filledQty":5,
				      "filledPrice":245100,
				      "priceType":"MARKET",
				      "validityType":"IOC"
				    }
				  ]
				}}
				"""));

        AreEqual(2, orders.Length);
        AreEqual(10m, orders[0].EffectiveQuantity());
        AreEqual(245075L, orders[0].EffectiveOrderPrice());
        AreEqual(OrderStates.Active, orders[0].Status.ToOrderState());
        AreEqual(OrderStates.Done, orders[1].Status.ToOrderState());
        AreEqual(OrderTypes.Market, orders[1].ToOrderType());
        AreEqual(TimeInForce.CancelBalance, orders[1].ValidityType.ToTimeInForce());
        AreEqual(NubraProducts.Cnc, orders[0].DeliveryType.ToProduct());
        AreEqual(NubraProducts.Iday, "IDAY".ToProduct());
    }

    [TestMethod]
    public void HistoricalFieldArraysMergeByTimestamp()
    {
        var candles = NubraRestClient.ParseCandles(
            JToken.Parse(
                """
				{"result":[{"values":[{"RELIANCE":{
				  "open":[
				    {"ts":1785047100000,"v":245000},
				    {"ts":1785047160000,"v":245100}
				  ],
				  "high":[
				    {"ts":1785047100000,"v":245200},
				    {"ts":1785047160000,"v":245300}
				  ],
				  "low":[
				    {"ts":1785047100000,"v":244900},
				    {"ts":1785047160000,"v":245000}
				  ],
				  "close":[
				    {"ts":1785047100000,"v":245150},
				    {"ts":1785047160000,"v":245250}
				  ],
				  "tick_volume":[
				    {"ts":1785047100000,"v":1000},
				    {"ts":1785047160000,"v":1200}
				  ]
				}}]}]}
				"""),
            "RELIANCE");

        AreEqual(2, candles.Length);
        AreEqual(245000L, candles[0].Open);
        AreEqual(245250L, candles[1].Close);
        AreEqual(1200L, candles[1].Volume);
        AreEqual(2450m, candles[0].Open.ToPrice());
    }

    [TestMethod]
    public void PortfolioModelsDeserializePublishedEnvelopes()
    {
        var position = JsonConvert.DeserializeObject<NubraPositionEnvelope>(
            """
			{"portfolio":{"clientCode":"CLIENT-1","positions":[{
			  "refId":2885,
			  "symbol":"RELIANCE",
			  "exchange":"NSE",
			  "deliveryType":"IDAY",
			  "netQuantity":7,
			  "avgPrice":245075,
			  "lastTradedPrice":245100,
			  "pnl":175
			}]}}
			""");
        var holding = JsonConvert.DeserializeObject<NubraHoldingEnvelope>(
            """
			{"portfolio":{"clientCode":"CLIENT-1","holdings":[{
			  "refId":2885,
			  "symbol":"RELIANCE",
			  "quantity":12,
			  "pledgedQty":2,
			  "t1Qty":1,
			  "avgPrice":230000
			}]}}
			""");
        var funds = JsonConvert.DeserializeObject<NubraFundsEnvelope>(
            """
			{"portFundsAndMargin":{
			  "clientCode":"CLIENT-1",
			  "startOfDayFunds":5000000,
			  "netMarginAvailable":3200000,
			  "totalMarginBlocked":1800000
			}}
			""");

        AreEqual("CLIENT-1", position.Portfolio.ClientCode);
        AreEqual(7m, position.Portfolio.Positions.Single().NetQuantity);
        AreEqual(2450.75m, position.Portfolio.Positions[0].AveragePrice.ToPrice());
        AreEqual(12m, holding.Portfolio.Holdings.Single().Quantity);
        AreEqual(2m, holding.Portfolio.Holdings[0].PledgedQuantity);
        AreEqual(32000m, funds.Funds.AvailableMargin.ToPrice());
        AreEqual(18000m, funds.Funds.BlockedMargin.ToPrice());
    }

    [TestMethod]
    public void NestedAnyEnvelopeDecodesOfficialOrderBookSchema()
    {
        var batch = new BatchWebSocketOrderbookMessage
        {
            Timestamp = 1785047100000000000,
            Instruments =
            {
                new WebSocketMsgOrderBook
                {
                    InstId = 2885,
                    RefId = 2885,
                    Timestamp = 1785047101000000000,
                    Ltp = 245075,
                    Ltq = 4,
                    Volume = 12345,
                    Bids =
                    {
                        new OrderBookLevel
                        {
                            Price = 245070,
                            Quantity = 10,
                            Orders = 2,
                        },
                    },
                    Asks =
                    {
                        new OrderBookLevel
                        {
                            Price = 245080,
                            Quantity = 11,
                            Orders = 3,
                        },
                    },
                },
            },
        };
        var inner = Any.Pack(batch);
        var outer = Any.Pack(inner);

        var update = NubraMarketDataClient.Decode(
            outer.ToByteArray()).Single();

        AreEqual(2885L, update.RefId);
        AreEqual(1785047101000000000L, update.Timestamp);
        AreEqual(245075L, update.LastPrice);
        AreEqual(2L, update.Bids.Single().Orders);
        AreEqual(245080L, update.Asks.Single().Price);
        AreEqual(
            "batch_subscribe TOKEN orderbook {\"instruments\":[2885,9001]}",
            NubraMarketDataClient.CreateSubscriptionCommand(
                true,
                "TOKEN",
                [2885, 9001, 2885]));
        AreEqual(
            "batch_unsubscribe TOKEN orderbook {\"instruments\":[2885]}",
            NubraMarketDataClient.CreateSubscriptionCommand(
                false,
                "TOKEN",
                [2885]));
        ThrowsExactly<InvalidDataException>(() =>
            NubraMarketDataClient.Decode([0xff, 0x01]));
    }

    [TestMethod]
    public async Task HttpErrorsAndInvalidJsonAreRejected()
    {
        var unauthorized = new CaptureHandler(
            new ResponseSpec(
                """{"message":"Session expired"}""",
                HttpStatusCode.RequestTimeout));
        using (var client = new NubraRestClient(
            new("https://api.example.test/"),
            "DEVICE",
            "SESSION".Secure(),
            unauthorized))
        {
            var error = await ThrowsExactlyAsync<InvalidOperationException>(
                () => client.GetOrders(CancellationToken.None));
            IsTrue(error.Message.Contains("HTTP 408"));
            IsTrue(error.Message.Contains("Session expired"));
        }

        var invalid = new CaptureHandler(new ResponseSpec("<html>"));
        using (var client = new NubraRestClient(
            new("https://api.example.test/"),
            "DEVICE",
            "SESSION".Secure(),
            invalid))
        {
            await ThrowsExactlyAsync<InvalidDataException>(
                () => client.GetOrders(CancellationToken.None));
        }
    }

    private sealed record ResponseSpec(
        string Body,
        HttpStatusCode StatusCode = HttpStatusCode.OK);

    private sealed record CapturedRequest(
        Uri Uri,
        HttpMethod Method,
        IReadOnlyDictionary<string, string> Headers,
        string Body)
    {
        public string Header(string name)
            => Headers.TryGetValue(name, out var value) ? value : null;
    }

    private sealed class CaptureHandler : HttpMessageHandler
    {
        private readonly Queue<ResponseSpec> _responses;

        public CaptureHandler(params ResponseSpec[] responses)
        {
            _responses = new(responses);
        }

        public List<CapturedRequest> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var headers = request.Headers.ToDictionary(
                header => header.Key,
                header => string.Join(",", header.Value),
                StringComparer.OrdinalIgnoreCase);
            if (request.Content != null)
            {
                foreach (var header in request.Content.Headers)
                {
                    headers[header.Key] =
                        string.Join(",", header.Value);
                }
            }
            var body = request.Content == null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken);
            Requests.Add(new(
                request.RequestUri,
                request.Method,
                headers,
                body));
            if (_responses.Count == 0)
            {
                throw new InvalidOperationException(
                    "No fake Nubra response was configured.");
            }

            var response = _responses.Dequeue();
            return new(response.StatusCode)
            {
                Content = new StringContent(
                    response.Body,
                    Encoding.UTF8,
                    "application/json"),
            };
        }
    }
}
