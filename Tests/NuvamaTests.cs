namespace StockSharp.Connectors.Tests;

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
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

using Microsoft.VisualStudio.TestTools.UnitTesting;

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

using StockSharp.Messages;
using StockSharp.Nuvama;
using StockSharp.Nuvama.Native;

[TestClass]
public class NuvamaTests : BaseTestClass
{
    [TestMethod]
    public void SettingsRoundTripKeepsSessionsAndEndpoints()
    {
        var source = new NuvamaMessageAdapter(new IncrementalIdGenerator())
        {
            Key = "SOURCE".Secure(),
            Secret = "SECRET".Secure(),
            RequestId = "REQUEST".Secure(),
            VendorToken = "VENDOR".Secure(),
            Token = "AUTH".Secure(),
            AppIdKey = "APP-ID".Secure(),
            AccountId = "ACCOUNT",
            UserId = "USER",
            AccountType = "COMEQ",
            PublicIpAddress = "203.0.113.10",
            EmployeeOrDependent = "N",
            PortfolioName = "PORTFOLIO",
            DefaultProduct = NuvamaProducts.Mtf,
            PollingInterval = TimeSpan.FromSeconds(19),
            ReconnectAttempts = 6,
            RestAddress = new("https://rest.example.test/"),
            InstrumentAddress = new("https://static.example.test/master.zip"),
            IpAddressService = new("https://ip.example.test/"),
            StreamHost = "stream.example.test",
            StreamPort = 19443,
        };
        var storage = new SettingsStorage();
        source.Save(storage);

        var target = new NuvamaMessageAdapter(new IncrementalIdGenerator());
        target.Load(storage);

        AreEqual(source.Key.UnSecure(), target.Key.UnSecure());
        AreEqual(source.Secret.UnSecure(), target.Secret.UnSecure());
        AreEqual(source.RequestId.UnSecure(), target.RequestId.UnSecure());
        AreEqual(source.VendorToken.UnSecure(), target.VendorToken.UnSecure());
        AreEqual(source.Token.UnSecure(), target.Token.UnSecure());
        AreEqual(source.AppIdKey.UnSecure(), target.AppIdKey.UnSecure());
        AreEqual(source.AccountId, target.AccountId);
        AreEqual(source.UserId, target.UserId);
        AreEqual(source.AccountType, target.AccountType);
        AreEqual(source.PublicIpAddress, target.PublicIpAddress);
        AreEqual(source.EmployeeOrDependent, target.EmployeeOrDependent);
        AreEqual(source.PortfolioName, target.PortfolioName);
        AreEqual(source.DefaultProduct, target.DefaultProduct);
        AreEqual(source.PollingInterval, target.PollingInterval);
        AreEqual(source.ReconnectAttempts, target.ReconnectAttempts);
        AreEqual(source.RestAddress, target.RestAddress);
        AreEqual(source.InstrumentAddress, target.InstrumentAddress);
        AreEqual(source.IpAddressService, target.IpAddressService);
        AreEqual(source.StreamHost, target.StreamHost);
        AreEqual(source.StreamPort, target.StreamPort);
    }

    [TestMethod]
    public void InstrumentCsvSupportsQuotedCommasQuotesAndNewlines()
    {
        var instruments = NuvamaRestClient.ParseInstrumentCsv(
            """"
			exchangetoken,tradingsymbol,symbolname,description,expiry,strikeprice,ticksize,lotsize,optiontype,series,assettype,exchange,isin,qtyunits,prcunits,prcqtn,multiplier,asmgsmflag,asmgsmmsg
			2885,RELIANCE-EQ,RELIANCE,"Reliance, Industries
			Limited",,0,0.05,1,,EQ,EQUITY,nse,INE002A01018,1,1,1,1,Y,"ASM ""Stage 1"""
			35000,BANKNIFTY29SEP26C72600,BANKNIFTY,"Bank Nifty call",29/Sep/26,72600,0.05,30,CE,,OPTIDX,NFO,,1,1,1,1,N,
			"""");

        AreEqual(2, instruments.Length);
        AreEqual("Reliance, Industries\nLimited", instruments[0].Description);
        AreEqual("ASM \"Stage 1\"", instruments[0].AsmGsmMessage);
        AreEqual("NSE", instruments[0].Exchange);
        AreEqual("BANKNIFTY29SEP26C72600", instruments[1].TradingSymbol);
    }

    [TestMethod]
    public void InstrumentArchiveRequiresAndReadsPublishedCsvEntry()
    {
        var csv =
            """
			exchangetoken,tradingsymbol,assettype,exchange
			2885,RELIANCE-EQ,EQUITY,NSE
			""";
        var instruments = NuvamaRestClient.ParseInstrumentArchive(
            CreateArchive("instruments.csv", csv));

        AreEqual(1, instruments.Length);
        AreEqual("NSE|2885", instruments[0].ToSecurityId().Native);
        ThrowsExactly<InvalidDataException>(() =>
            NuvamaRestClient.ParseInstrumentArchive(
                CreateArchive("other.csv", csv)));
    }

    [TestMethod]
    public void InstrumentClassesKeysAndExpiryAreMapped()
    {
        var stock = Instrument("NSE", "2885", "EQUITY");
        var index = Instrument("NSE", "26000", "INDEX");
        var future = Instrument("NFO", "35001", "FUTIDX");
        var option = Instrument("NFO", "35000", "OPTIDX", "CE");
        var currency = Instrument("CDS", "1001", "CURRENCY");
        var commodity = Instrument("MCX", "5001", "SPOT");

        AreEqual(SecurityTypes.Stock, stock.ToSecurityType());
        AreEqual(SecurityTypes.Index, index.ToSecurityType());
        AreEqual(SecurityTypes.Future, future.ToSecurityType());
        AreEqual(SecurityTypes.Option, option.ToSecurityType());
        AreEqual(SecurityTypes.Currency, currency.ToSecurityType());
        AreEqual(SecurityTypes.Commodity, commodity.ToSecurityType());
        AreEqual(OptionTypes.Call, option.OptionType.ToOptionType());
        AreEqual("NFO|35000", option.ToSecurityId().Native);
        AreEqual(("NFO", "35000"), "NFO|35000".ParseInstrumentKey());
        AreEqual(
            new DateTime(2026, 9, 29, 0, 0, 0, DateTimeKind.Utc),
            "29/Sep/26".ToExpiry());
        ThrowsExactly<FormatException>(() =>
            "NFO-35000".ParseInstrumentKey());
        ThrowsExactly<ArgumentOutOfRangeException>(() =>
            "UNKNOWN".ToBoardCode());
    }

    [TestMethod]
    public void NativeCodesTimesAndIntervalsMatchPublishedValues()
    {
        AreEqual("CNC", NuvamaProducts.Cnc.ToNative());
        AreEqual("MIS", NuvamaProducts.Mis.ToNative());
        AreEqual("NRML", NuvamaProducts.Nrml.ToNative());
        AreEqual("MTF", NuvamaProducts.Mtf.ToNative());
        AreEqual(NuvamaProducts.Mtf, "F".ToProduct());
        AreEqual("BUY", Sides.Buy.ToNative());
        AreEqual(Sides.Sell, "SELL".ToSide());
        AreEqual("MARKET", OrderTypes.Market.ToNative(0));
        AreEqual("STOP_LIMIT", OrderTypes.Conditional.ToNative(100));
        AreEqual("STOP_MARKET", OrderTypes.Conditional.ToNative(0));
        AreEqual(OrderTypes.Conditional, "SL-M".ToOrderType());
        AreEqual("IOC", ((TimeInForce?)TimeInForce.CancelBalance).ToValidity());
        AreEqual(TimeInForce.PutInQueue, "DAY".ToTimeInForce());
        AreEqual(OrderStates.Pending, "validation pending".ToOrderState());
        AreEqual(OrderStates.Active, "OPEN".ToOrderState());
        AreEqual(OrderStates.Done, "FULLY_EXECUTED".ToOrderState());
        AreEqual(OrderStates.Failed, "REJECTED".ToOrderState());
        AreEqual("M1", TimeSpan.FromMinutes(1).ToChartInterval());
        AreEqual("H1", TimeSpan.FromHours(1).ToChartInterval());
        AreEqual("D1", TimeSpan.FromDays(1).ToChartInterval());
        AreEqual("W1", TimeSpan.FromDays(7).ToChartInterval());
        AreEqual("MN1", TimeSpan.FromDays(30).ToChartInterval());
        AreEqual(
            new DateTime(2026, 7, 26, 3, 45, 0, DateTimeKind.Utc),
            "2026-07-26 09:15:00".ToNuvamaTime());
    }

    [TestMethod]
    public void ResponseEnvelopeHandlesSuccessNoDataAndErrors()
    {
        var response = NuvamaRestClient.ParseResponse(
            "orders",
            """{"resp":{"data":{"ord":[{"ordID":"O1"}]}}}""");
        AreEqual(
            "O1",
            response["data"]["ord"][0]["ordID"].Value<string>());
        IsNull(NuvamaRestClient.ParseResponse(
            "orders",
            """{"error":{"errMsg":"No orders found"}}""",
            true));
        ThrowsExactly<InvalidOperationException>(() =>
            NuvamaRestClient.ParseResponse(
                "orders",
                """{"error":{"errMsg":"Invalid session"}}"""));
        ThrowsExactly<InvalidDataException>(() =>
            NuvamaRestClient.ParseResponse("orders", "<html>"));
    }

    [TestMethod]
    public async Task LoginFlowUsesDocumentedHeadersAndRotatesAppIdKey()
    {
        var handler = new CaptureHandler(
            new ResponseSpec(
                """{"msg":"VENDOR-TOKEN"}""",
                AppIdKey: "APP-2"),
            new ResponseSpec(
                """
				{"data":{
				  "auth":"USER-AUTH",
				  "lgnData":{
				    "accTyp":"EQ",
				    "accs":{
				      "eqAccID":"ACCOUNT-1",
				      "uid":"USER-1",
				      "empOrDependent":"N"
				    }
				  }
				}}
				""",
                AppIdKey: "APP-3"));
        using var client = new NuvamaRestClient(
            new("https://nc.example.test/"),
            new("https://static.example.test/instruments.zip"),
            new("https://ip.example.test/"),
            handler,
            TimeSpan.Zero);
        var rotations = new List<string>();
        client.AppIdKeyChanged += rotations.Add;

        var login = await client.Authenticate(
            "SOURCE".Secure(),
            "SECRET".Secure(),
            "REQUEST-ID".Secure(),
            (SecureString)null,
            (SecureString)null,
            "APP-1".Secure(),
            "203.0.113.10",
            null,
            null,
            "EQ",
            null,
            CancellationToken.None);

        AreEqual("VENDOR-TOKEN", login.VendorToken);
        AreEqual("USER-AUTH", login.Authorization);
        AreEqual("APP-3", login.AppIdKey);
        AreEqual("ACCOUNT-1", login.AccountId);
        AreEqual("USER-1", login.UserId);
        AreEqual("N", login.EmployeeOrDependent);
        AreEqual(2, rotations.Count);
        AreEqual("APP-2", rotations[0]);
        AreEqual("APP-3", rotations[1]);

        var vendor = handler.Requests[0];
        AreEqual(
            "https://nc.example.test/edelmw-login/login/accounts/loginvendor/SOURCE",
            vendor.Uri.AbsoluteUri);
        AreEqual("SOURCE", vendor.Header("Source"));
        AreEqual("APP-1", vendor.Header("AppIdKey"));
        AreEqual("203.0.113.10", vendor.Header("X-Forwarded-For"));
        IsNull(vendor.Header("SourceToken"));
        AreEqual("SECRET", JObject.Parse(vendor.Body)["pwd"].Value<string>());

        var user = handler.Requests[1];
        AreEqual(
            "https://nc.example.test/edelmw-login/login/accounts/logindata",
            user.Uri.AbsoluteUri);
        IsNull(user.Header("Source"));
        AreEqual("VENDOR-TOKEN", user.Header("SourceToken"));
        AreEqual("APP-2", user.Header("AppIdKey"));
        AreEqual(
            "REQUEST-ID",
            JObject.Parse(user.Body)["reqId"].Value<string>());
    }

    [TestMethod]
    public async Task DirectSessionSendsAllTradingHeadersAndExactOrderBody()
    {
        var handler = new CaptureHandler(
            new ResponseSpec("""{"data":{"oid":"ORDER-1"}}"""));
        using var client = new NuvamaRestClient(
            new("https://nc.example.test/"),
            new("https://static.example.test/instruments.zip"),
            new("https://ip.example.test/"),
            handler,
            TimeSpan.Zero);
        await client.Authenticate(
            "SOURCE".Secure(),
            (SecureString)null,
            (SecureString)null,
            "VENDOR".Secure(),
            "AUTH".Secure(),
            "APP-ID".Secure(),
            "203.0.113.10",
            "ACCOUNT",
            "USER",
            "EQ",
            "N",
            CancellationToken.None);

        var orderId = await client.PlaceOrder(
            new()
            {
                TradingSymbol = "RELIANCE-EQ",
                Exchange = "NSE",
                Action = "BUY",
                Duration = "DAY",
                OrderType = "LIMIT",
                Quantity = "10",
                DisclosedQuantity = "0",
                StreamingSymbol = "2885",
                LimitPrice = "2450.75",
                TriggerPrice = "0",
                ProductCode = "CNC",
                Remark = "unit-test",
                EmployeeOrDependent = "N",
            },
            CancellationToken.None);

        AreEqual("ORDER-1", orderId);
        var request = handler.Requests.Single();
        AreEqual(HttpMethod.Post, request.Method);
        AreEqual(
            "https://nc.example.test/edelmw-eq/eq/trade/placetrade/v1/ACCOUNT",
            request.Uri.AbsoluteUri);
        AreEqual("AUTH", request.Header("Authorization"));
        AreEqual("VENDOR", request.Header("SourceToken"));
        AreEqual("APP-ID", request.Header("AppIdKey"));
        AreEqual("SOURCE", request.Header("Source"));
        AreEqual("203.0.113.10", request.Header("X-Forwarded-For"));

        var body = JObject.Parse(request.Body);
        AreEqual("RELIANCE-EQ", body["trdSym"].Value<string>());
        AreEqual("NSE", body["exc"].Value<string>());
        AreEqual("BUY", body["action"].Value<string>());
        AreEqual("LIMIT", body["ordTyp"].Value<string>());
        AreEqual("10", body["qty"].Value<string>());
        AreEqual("2885", body["sym"].Value<string>());
        AreEqual("2450.75", body["lmPrc"].Value<string>());
        AreEqual("CNC", body["prdCode"].Value<string>());
        AreEqual("API", body["ordSrc"].Value<string>());
        AreEqual("N", body["posSqr"].Value<string>());
        IsTrue(body["flQty"].Value<bool>());
    }

    [TestMethod]
    public void CandleResponsesSupportParallelAndRowArrays()
    {
        var parallel = NuvamaRestClient.ParseCandles(
            JToken.Parse(
                """
				{"data":{"pltPnts":{
				  "ltt":["2026-07-26 09:15:00","2026-07-26 09:16:00"],
				  "open":["100","101"],
				  "high":["102","103"],
				  "low":["99","100"],
				  "close":["101","102.5"],
				  "vol":["1000","1200"]
				}}}
				"""));
        var rows = NuvamaRestClient.ParseCandles(
            JToken.Parse(
                """
				{"data":[
				  ["2026-07-26 09:15:00","200","203","198","202","5000"],
				  ["2026-07-26 09:16:00","202","204","201","203","5100"]
				]}
				"""));

        AreEqual(2, parallel.Length);
        AreEqual(100m, parallel[0].Open);
        AreEqual(102.5m, parallel[1].Close);
        AreEqual(1200m, parallel[1].Volume);
        AreEqual(2, rows.Length);
        AreEqual(203m, rows[0].High);
        AreEqual(5100m, rows[1].Volume);
    }

    [TestMethod]
    public void PublishedAccountModelsDeserializeAliasesAndNestedHoldings()
    {
        var order = JsonConvert.DeserializeObject<NuvamaOrder>(
            """
			{"nstOID":"O1","exc":"NSE","sym":"2885","trdSym":"RELIANCE-EQ",
			 "action":"BUY","ordType":"LIMIT","qty":"10","flQty":"2",
			 "pdQty":"8","sts":"OPEN"}
			""");
        var trade = JsonConvert.DeserializeObject<NuvamaTrade>(
            """
			{"ordID":"O1","flID":"F1","exc":"NSE","sym":"2885",
			 "trsTyp":"BUY","flQty":"2","ntPrc":"2450.75"}
			""");
        var holding = JsonConvert.DeserializeObject<NuvamaHolding>(
            """
			{"exc":"NSE","sym":"2885","cncRmsHdg":{"qty":"7","t1HQty":"2"},
			 "mtfRmsHdg":{"totQty":"3"}}
			""");
        var limits = JsonConvert.DeserializeObject<NuvamaLimits>(
            """
			{"cshAvl":"45230.75","mrgAvl":{"mrgAvl":"60000","dayOpenBal":"75000"},
			 "mrgUtd":{"mrgUtd":"29769.25"}}
			""");

        AreEqual("O1", order.EffectiveOrderId());
        AreEqual("LIMIT", order.EffectiveOrderType());
        AreEqual(10m, order.EffectiveQuantity());
        AreEqual("F1", trade.EffectiveTradeId());
        AreEqual(2m, trade.EffectiveFilledQuantity());
        AreEqual(2450.75m, trade.EffectiveFilledPrice());
        AreEqual(12m, holding.EffectiveHoldingQuantity());
        AreEqual("45230.75", limits.CashAvailable);
        AreEqual("29769.25", limits.MarginUsed.Value);
    }

    [TestMethod]
    public void StreamSubscriptionEnvelopesMatchPublishedProtocol()
    {
        using var client = new NuvamaStreamClient(
            "stream.example.test",
            9443,
            "eq",
            "ACCOUNT",
            "USER",
            "APP-ID",
            3);
        var quote = client.CreateMarketSubscriptionRequest(
            "quote2",
            ["2885", "26000"],
            true);
        var order = client.CreateOrderSubscriptionRequest(false);

        AreEqual(
            "quote2",
            quote["request"]["streaming_type"].Value<string>());
        AreEqual(
            "EQ",
            quote["request"]["data"]["accType"].Value<string>());
        AreEqual(
            "2885",
            quote["request"]["data"]["symbols"][0]["symbol"]
                .Value<string>());
        AreEqual(
            "subscribe",
            quote["request"]["request_type"].Value<string>());
        AreEqual("APP-ID", quote["request"]["appID"].Value<string>());
        AreEqual(
            "orderFiler",
            order["request"]["streaming_type"].Value<string>());
        AreEqual(
            "ACCOUNT",
            order["request"]["data"]["accID"].Value<string>());
        AreEqual(
            "ORDER_UPDATE",
            order["request"]["data"]["responseType"][0].Value<string>());
        AreEqual(
            "TRADE_UPDATE",
            order["request"]["data"]["responseType"][1].Value<string>());
        AreEqual(
            "unsubscribe",
            order["request"]["request_type"].Value<string>());
    }

    [TestMethod]
    public void StreamFramerPreservesPartialAndConcatenatedObjects()
    {
        var buffer = new StringBuilder(
            " \n{\"message\":\"brace } and escaped \\\" quote\"}" +
            "{\"nested\":{\"value\":2");
        var first = NuvamaStreamClient.ExtractJsonFrames(buffer);

        AreEqual(1, first.Length);
        AreEqual(
            "brace } and escaped \" quote",
            JObject.Parse(first[0])["message"].Value<string>());
        IsTrue(buffer.ToString().StartsWith("{\"nested\""));

        buffer.Append("}}\n{\"last\":3}\n");
        var remaining = NuvamaStreamClient.ExtractJsonFrames(buffer);
        AreEqual(2, remaining.Length);
        AreEqual(2, JObject.Parse(remaining[0])["nested"]["value"].Value<int>());
        AreEqual(3, JObject.Parse(remaining[1])["last"].Value<int>());
        IsTrue(string.IsNullOrWhiteSpace(buffer.ToString()));
    }

    [TestMethod]
    public async Task StreamFramesDispatchQuoteDepthAndOrderUpdates()
    {
        using var client = new NuvamaStreamClient(
            "stream.example.test",
            9443,
            "EQ",
            "ACCOUNT",
            "USER",
            "APP-ID",
            0);
        NuvamaQuote quote = null;
        NuvamaDepth depth = null;
        JToken order = null;
        client.QuoteReceived += (value, _) =>
        {
            quote = value;
            return default;
        };
        client.DepthReceived += (value, _) =>
        {
            depth = value;
            return default;
        };
        client.OrderReceived += (value, _) =>
        {
            order = value;
            return default;
        };

        await client.ProcessFrame(
            """
			{"response":{"streaming_type":"quote","data":[
			  {"sym":"2885","ltp":"2450.75","ltq":"3"}
			]}}
			""",
            CancellationToken.None);
        await client.ProcessFrame(
            """
			{"response":{"streaming_type":"quote2","data":{
			  "symbol":"2885",
			  "bid":[{"price":"2450.70","qty":"10","no":"2"}],
			  "ask":[{"price":"2450.80","qty":"11","no":"3"}]
			}}}
			""",
            CancellationToken.None);
        await client.ProcessFrame(
            """
			{"response":{"streaming_type":"orderFiler","data":{
			  "ordID":"O1","sts":"OPEN"
			}}}
			""",
            CancellationToken.None);

        AreEqual("2885", quote.Symbol);
        AreEqual("2450.75", quote.LastPrice);
        AreEqual("2885", depth.Symbol);
        AreEqual("2450.70", depth.Bids[0].Price);
        AreEqual("O1", order["ordID"].Value<string>());
    }

    private static NuvamaInstrument Instrument(
        string exchange,
        string token,
        string assetType,
        string optionType = null)
        => new()
        {
            Exchange = exchange,
            ExchangeToken = token,
            TradingSymbol = $"SYMBOL-{token}",
            AssetType = assetType,
            OptionType = optionType,
        };

    private static byte[] CreateArchive(string name, string content)
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(
            stream,
            ZipArchiveMode.Create,
            true))
        using (var writer = new StreamWriter(
            archive.CreateEntry(name).Open(),
            new UTF8Encoding(false)))
            writer.Write(content);
        return stream.ToArray();
    }

    private sealed record ResponseSpec(
        string Json,
        string AppIdKey = null,
        string Authorization = null,
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
            var headers = request.Headers
                .ToDictionary(
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
                    "No fake Nuvama response was configured.");
            }
            var spec = _responses.Dequeue();
            var response = new HttpResponseMessage(spec.StatusCode)
            {
                Content = new StringContent(
                    spec.Json,
                    Encoding.UTF8,
                    "application/json"),
            };
            if (!spec.AppIdKey.IsEmpty())
            {
                response.Headers.TryAddWithoutValidation(
                    "AppIdKey",
                    spec.AppIdKey);
            }
            if (!spec.Authorization.IsEmpty())
            {
                response.Headers.TryAddWithoutValidation(
                    "Authorization",
                    spec.Authorization);
            }
            return response;
        }
    }
}
