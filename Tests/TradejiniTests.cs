namespace StockSharp.Connectors.Tests;

using System;
using System.Collections.Generic;
using System.IO;
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

using Newtonsoft.Json.Linq;

using StockSharp.Messages;
using StockSharp.Tradejini;
using StockSharp.Tradejini.Native;

[TestClass]
public class TradejiniTests : BaseTestClass
{
    [TestMethod]
    public void SettingsRoundTripKeepsIndividualCredentials()
    {
        var source = new TradejiniMessageAdapter(
            new IncrementalIdGenerator())
        {
            ApiKey = "api-key".Secure(),
            Password = "password".Secure(),
            TwoFactorCode = "123456".Secure(),
            TwoFactorType = TradejiniTwoFactorTypes.Otp,
            Token = "access-token".Secure(),
            PortfolioName = "JNDEMO01",
            DefaultProduct = TradejiniProducts.Normal,
            Address = new("https://api.example.test/v2/"),
            PollingInterval = TimeSpan.FromSeconds(17),
        };
        var storage = new SettingsStorage();
        source.Save(storage);

        var target = new TradejiniMessageAdapter(
            new IncrementalIdGenerator());
        target.Load(storage);

        AreEqual(source.ApiKey.UnSecure(), target.ApiKey.UnSecure());
        AreEqual(source.Password.UnSecure(), target.Password.UnSecure());
        AreEqual(
            source.TwoFactorCode.UnSecure(),
            target.TwoFactorCode.UnSecure());
        AreEqual(source.TwoFactorType, target.TwoFactorType);
        AreEqual(source.Token.UnSecure(), target.Token.UnSecure());
        AreEqual(source.PortfolioName, target.PortfolioName);
        AreEqual(source.DefaultProduct, target.DefaultProduct);
        AreEqual(source.Address, target.Address);
        AreEqual(source.PollingInterval, target.PollingInterval);
    }

    [TestMethod]
    public async Task PublicSecuritiesCsvHandlesQuotedDescriptions()
    {
        const string csv =
            "id,dispName,excToken,lot,tick,asset,freezeQty,isin,desc\n" +
            "EQT_RELIANCE_EQ_NSE,RELIANCE,2885,1,0.05,equity,67662,INE002A01018,\"RELIANCE INDUSTRIES, LTD\"\n";
        await using var stream = new MemoryStream(
            Encoding.UTF8.GetBytes(csv));

        var instruments =
            await TradejiniRestClient.ParseInstrumentCsv(
                "Securities",
                stream,
                CancellationToken.None);

        AreEqual(1, instruments.Length);
        var instrument = instruments[0];
        AreEqual("EQT_RELIANCE_EQ_NSE", instrument.Id);
        AreEqual("RELIANCE", instrument.Symbol);
        AreEqual("EQ", instrument.Series);
        AreEqual("NSE", instrument.Exchange);
        AreEqual("RELIANCE INDUSTRIES, LTD", instrument.Description);
        AreEqual(SecurityTypes.Stock, instrument.ToSecurityType());
        AreEqual("EQT_RELIANCE_EQ_NSE", instrument.ToSecurityId().Native);
    }

    [TestMethod]
    public void OfficialDerivativeIdsDecodeContractFields()
    {
        var option = TradejiniRestClient.ParseInstrument(
            "NSEOptions",
            Values(
                "OPTIDX_NIFTY_NFO_2026-07-28_21100_CE",
                "NIFTY 28JUL 21100 CE",
                "63811",
                "65",
                "0.05",
                "option",
                "1755",
                "IDX_-1_NSE"));
        var future = TradejiniRestClient.ParseInstrument(
            "FutureContracts",
            Values(
                "FUTSTK_RELIANCE_NFO_2026-08-27",
                "RELIANCE 27AUG FUT",
                "12345",
                "250",
                "0.05",
                "future",
                "10000",
                "2885_NSE"));

        AreEqual("NIFTY", option.Symbol);
        AreEqual("NFO", option.Exchange);
        AreEqual(21100m, option.Strike);
        AreEqual(OptionTypes.Call, option.OptionType.ToOptionType());
        AreEqual(SecurityTypes.Option, option.ToSecurityType());
        AreEqual("NFO", option.ToSecurityId().BoardCode);
        AreEqual(
            new DateTime(2026, 7, 27, 18, 30, 0, DateTimeKind.Utc),
            option.Expiry);
        AreEqual("RELIANCE", future.Symbol);
        AreEqual("NFO", future.Exchange);
        AreEqual(SecurityTypes.Future, future.ToSecurityType());
        AreEqual(250m, future.LotSize);
    }

    [TestMethod]
    public void SpotAndIndexMapToStockSharpTypes()
    {
        var currency = TradejiniRestClient.ParseInstrument(
            "Spot",
            new Dictionary<string, string>
            {
                ["id"] = "UNDCUR_EURINR_CDS",
                ["dispName"] = "EURINR",
                ["excToken"] = "80",
                ["asset"] = "spot",
                ["symbol"] = "EURINR",
            });
        var commodity = TradejiniRestClient.ParseInstrument(
            "Spot",
            new Dictionary<string, string>
            {
                ["id"] = "UNDCOM_ALUMINI_MCX",
                ["dispName"] = "ALUMINI",
                ["excToken"] = "1001",
                ["asset"] = "spot",
                ["symbol"] = "ALUMINI",
            });
        var index = TradejiniRestClient.ParseInstrument(
            "Index",
            new Dictionary<string, string>
            {
                ["id"] = "IDX_-1_NSE",
                ["dispName"] = "Nifty 50",
                ["excToken"] = "-1",
                ["asset"] = "index",
                ["symbol"] = "NIFTY",
            });

        AreEqual(SecurityTypes.Currency, currency.ToSecurityType());
        AreEqual(SecurityTypes.Commodity, commodity.ToSecurityType());
        AreEqual(SecurityTypes.Index, index.ToSecurityType());
        AreEqual("CDS", currency.ToSecurityId().BoardCode);
        AreEqual("NSE", index.ToSecurityId().BoardCode);
    }

    [TestMethod]
    public void ResponseEnvelopeHandlesOkNoDataAndErrors()
    {
        var data = TradejiniRestClient.ParseResponse(
            """{"s":"ok","d":{"userId":"JNDEMO01"}}""",
            "profile");
        AreEqual("JNDEMO01", data.GetText("userId"));

        var noData = TradejiniRestClient.ParseResponse(
            """{"s":"no-data","msg":"no-data"}""",
            "orders");
        AreEqual(JTokenType.Null, noData.Type);

        ThrowsExactly<InvalidOperationException>(() =>
            TradejiniRestClient.ParseResponse(
                """{"s":"error","msg":"Unauthorized"}""",
                "orders"));
        ThrowsExactly<InvalidDataException>(() =>
            TradejiniRestClient.ParseResponse("<html>", "orders"));
    }

    [TestMethod]
    public void NativeCodesMatchDocumentedContract()
    {
        AreEqual("otp", TradejiniTwoFactorTypes.Otp.ToNative());
        AreEqual("totp", TradejiniTwoFactorTypes.Totp.ToNative());
        AreEqual("delivery", TradejiniProducts.Delivery.ToNative());
        AreEqual(
            TradejiniProducts.Intraday,
            "mis".ToProduct());
        AreEqual(
            TradejiniProducts.Normal,
            "nrml".ToProduct());
        AreEqual("buy", Sides.Buy.ToNative());
        AreEqual(Sides.Sell, "s".ToSide());
        AreEqual("stoplimit", OrderTypes.Conditional.ToNative(10m));
        AreEqual("stopmarket", OrderTypes.Conditional.ToNative(0m));
        AreEqual(
            OrderTypes.Conditional,
            "sl-mkt".ToOrderType());
        AreEqual(
            TradejiniValidities.Ioc,
            "IOC".ToValidity());
        AreEqual(
            TimeInForce.CancelBalance,
            TradejiniValidities.Ioc.ToTimeInForce());
        AreEqual(OrderStates.Done, "filled".ToOrderState());
        AreEqual(OrderStates.Active, "partially filled".ToOrderState());
        AreEqual(OrderStates.Pending, "cancel pending".ToOrderState());
        AreEqual(OrderStates.Failed, "rejected".ToOrderState());
        AreEqual(OrderStates.Pending, "trigger pending".ToOrderState());
    }

    [TestMethod]
    public void OrderFormUsesOnlyDocumentedFields()
    {
        var form = TradejiniRestClient.CreateOrderForm(
            "OPTIDX_NIFTY_NFO_2026-07-28_21100_CE",
            65,
            Sides.Buy,
            OrderTypes.Conditional,
            TradejiniProducts.Normal,
            100,
            95,
            TradejiniValidities.Gtc,
            65,
            true,
            0,
            "S123");

        AreEqual("65", form["qty"]);
        AreEqual("buy", form["side"]);
        AreEqual("stoplimit", form["type"]);
        AreEqual("normal", form["product"]);
        AreEqual("100", form["limitPrice"]);
        AreEqual("95", form["trigPrice"]);
        AreEqual("gtc", form["validity"]);
        AreEqual("65", form["discQty"]);
        AreEqual("true", form["amo"]);
        AreEqual("S123", form["remarks"]);

        var market = TradejiniRestClient.CreateOrderForm(
            "EQT_RELIANCE_EQ_NSE",
            10,
            Sides.Sell,
            OrderTypes.Market,
            TradejiniProducts.Intraday,
            0,
            0,
            TradejiniValidities.Day,
            0,
            false,
            2.5m,
            null);
        AreEqual("market", market["type"]);
        AreEqual("2.5", market["mktProt"]);
        IsFalse(market.ContainsKey("limitPrice"));
    }

    [TestMethod]
    public async Task IndividualAuthenticationUsesApiKeyBearerAndForm()
    {
        var handler = new CaptureHandler(
            """
            {"access_token":"ACCESS","token_type":"bearer","expires_in":86400,"scope":"general"}
            """);
        using var client = new TradejiniRestClient(
            "APIKEY".Secure(),
            (SecureString)null,
            new("https://api.tradejini.com/v2/"),
            handler);

        var login = await client.Authenticate(
            "password".Secure(),
            "123456".Secure(),
            TradejiniTwoFactorTypes.Totp,
            CancellationToken.None);

        AreEqual("ACCESS", login.AccessToken);
        AreEqual(
            "https://api.tradejini.com/v2/api-gw/oauth/individual-token-v2",
            handler.RequestUri.AbsoluteUri);
        AreEqual(HttpMethod.Post, handler.Method);
        AreEqual("Bearer", handler.Scheme);
        AreEqual("APIKEY", handler.Parameter);
        IsTrue(handler.Body.Contains("password=password"));
        IsTrue(handler.Body.Contains("twoFa=123456"));
        IsTrue(handler.Body.Contains("twoFaTyp=totp"));
    }

    [TestMethod]
    public async Task TradingRequestUsesCombinedBearer()
    {
        var handler = new CaptureHandler(
            """{"s":"ok","d":{"orderId":"210115000000001"}}""");
        using var client = new TradejiniRestClient(
            "APIKEY".Secure(),
            "TOKEN".Secure(),
            new("https://api.tradejini.com/v2/"),
            handler);

        var orderId = await client.PlaceOrder(
            "EQT_RELIANCE_EQ_NSE",
            10,
            Sides.Buy,
            OrderTypes.Limit,
            TradejiniProducts.Delivery,
            2450.75m,
            0,
            TradejiniValidities.Day,
            0,
            false,
            0,
            null,
            CancellationToken.None);

        AreEqual("210115000000001", orderId);
        AreEqual("APIKEY:TOKEN", handler.Parameter);
        IsTrue(handler.Body.Contains("symId=EQT_RELIANCE_EQ_NSE"));
        IsTrue(handler.Body.Contains("limitPrice=2450.75"));
        IsFalse(handler.Body.Contains("trigPrice="));
    }

    [TestMethod]
    public void AccountModelsFollowPublishedExamples()
    {
        var order = TradejiniRestClient.ParseArray<TradejiniOrder>(
            JToken.Parse(
                """
                [{"symId":"EQT_RELIANCE_EQ_NSE","status":"open","qty":10,"side":"b","type":"l","product":"cnc","orderId":"O1","limitPrice":2440,"fillQty":2,"pendingQty":8}]
                """))[0];
        var trade = TradejiniRestClient.ParseArray<TradejiniTrade>(
            JToken.Parse(
                """
                [{"symId":"EQT_RELIANCE_EQ_NSE","side":"b","type":"l","product":"cnc","orderId":"O1","fillId":"F1","fillQty":2,"fillPrice":2440,"fillValue":4880,"time":"09:32:15"}]
                """))[0];
        var position =
            TradejiniRestClient.ParseArray<TradejiniPosition>(
                JToken.Parse(
                    """
                    [{"symId":"EQT_RELIANCE_EQ_NSE","product":"mis","buyQty":10,"buyAvgPrice":2450.75,"sellQty":5,"sellAvgPrice":2475.65,"netQty":5,"netAvgPrice":2450.75,"realizedPnl":124.5}]
                    """))[0];
        var holding =
            TradejiniRestClient.ParseArray<TradejiniHolding>(
                JToken.Parse(
                    """
                    [{"symId":"EQT_RELIANCE_EQ_NSE","qty":10,"avgPrice":2450.75,"saleableQty":8}]
                    """))[0];
        var fund = TradejiniRestClient.ParseArray<TradejiniFund>(
            JToken.Parse(
                """
                [{"segment":"NSE","totalCredits":75000,"availMargin":45230.75,"availCash":45230.75,"marginUsed":29769.25,"payIn":10000,"payOut":0,"realizedPnl":1250.5,"unrealizedPnL":-320.25}]
                """))[0];

        AreEqual("O1", order.OrderId);
        AreEqual(8m, order.PendingQuantity);
        AreEqual("F1", trade.FillId);
        AreEqual(2440m, trade.Price);
        AreEqual(5m, position.NetQuantity);
        AreEqual(124.5m, position.RealizedPnL);
        AreEqual(8m, holding.SaleableQuantity);
        AreEqual(45230.75m, fund.AvailableMargin);
        AreEqual(-320.25m, fund.UnrealizedPnL);
    }

    [TestMethod]
    public void CandleBarsAreSortedAndMapped()
    {
        var candles = TradejiniRestClient.ParseCandles(
            JToken.Parse(
                """
                {"bars":[
                  {"time":1705293600,"open":2451,"high":2468,"low":2447,"close":2463.25,"volume":987654},
                  {"time":1705290000,"open":2445,"high":2462.5,"low":2438.75,"close":2450.75,"volume":1234567,"oi":42}
                ],"sumUpVolume":true}
                """));

        AreEqual(2, candles.Length);
        AreEqual(1705290000L, candles[0].UnixTime);
        AreEqual(2445m, candles[0].Open);
        AreEqual(42m, candles[0].OpenInterest);
        AreEqual(2463.25m, candles[1].Close);
    }

    [TestMethod]
    public void IndiaTimesAndQuantitiesAreValidated()
    {
        AreEqual(
            new DateTime(2026, 7, 26, 3, 45, 0, DateTimeKind.Utc),
            "2026-07-26 09:15:00".ToTradejiniTime(
                new DateTime(
                    2026,
                    7,
                    26,
                    0,
                    0,
                    0,
                    DateTimeKind.Utc)));
        AreEqual(
            new DateTime(2026, 7, 26, 4, 2, 15, DateTimeKind.Utc),
            "09:32:15".ToTradejiniTime(
                new DateTime(
                    2026,
                    7,
                    26,
                    0,
                    0,
                    0,
                    DateTimeKind.Utc)));
        AreEqual(
            65m,
            TradejiniMessageAdapter.ValidateQuantity(65, "quantity"));
        ThrowsExactly<ArgumentOutOfRangeException>(() =>
            TradejiniMessageAdapter.ValidateQuantity(0, "quantity"));
        ThrowsExactly<ArgumentOutOfRangeException>(() =>
            TradejiniMessageAdapter.ValidateQuantity(1.5m, "quantity"));
    }

    private static Dictionary<string, string> Values(
        string id,
        string displayName,
        string exchangeToken,
        string lot,
        string tick,
        string asset,
        string freezeQuantity,
        string underlyingId)
        => new(StringComparer.OrdinalIgnoreCase)
        {
            ["id"] = id,
            ["dispName"] = displayName,
            ["excToken"] = exchangeToken,
            ["lot"] = lot,
            ["tick"] = tick,
            ["asset"] = asset,
            ["freezeQty"] = freezeQuantity,
            ["undId"] = underlyingId,
        };

    private sealed class CaptureHandler : HttpMessageHandler
    {
        private readonly string _response;

        public CaptureHandler(string response)
        {
            _response = response;
        }

        public Uri RequestUri { get; private set; }
        public HttpMethod Method { get; private set; }
        public string Scheme { get; private set; }
        public string Parameter { get; private set; }
        public string Body { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestUri = request.RequestUri;
            Method = request.Method;
            Scheme = request.Headers.Authorization?.Scheme;
            Parameter = request.Headers.Authorization?.Parameter;
            Body = request.Content == null
                ? null
                : await request.Content.ReadAsStringAsync(
                    cancellationToken);
            return new(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    _response,
                    Encoding.UTF8,
                    "application/json"),
            };
        }
    }
}
