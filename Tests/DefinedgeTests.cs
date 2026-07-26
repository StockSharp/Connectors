namespace StockSharp.Connectors.Tests;

using System;
using System.Globalization;
using System.Threading.Tasks;

using Ecng.Common;
using Ecng.Serialization;
using Ecng.UnitTesting;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

using StockSharp.Definedge;
using StockSharp.Definedge.Native;
using StockSharp.Messages;

[TestClass]
public class DefinedgeTests : BaseTestClass
{
    [TestMethod]
    public void SettingsRoundTripKeepsCredentialsAndEndpoints()
    {
        var source = new DefinedgeMessageAdapter(
            new IncrementalIdGenerator())
        {
            Key = "api-token".Secure(),
            Secret = "api-secret".Secure(),
            Token = "api-session".Secure(),
            WebSocketToken = "stream-session".Secure(),
            OneTimePassword = "123456".Secure(),
            UserId = "USER1",
            AccountId = "ACCOUNT1",
            DefaultProduct = DefinedgeProducts.Normal,
            AlgoId = "ALGO42",
            Address = new("https://api.example.test/v1/"),
            LoginAddress =
                new("https://login.example.test/auth/"),
            HistoryAddress =
                new("https://history.example.test/"),
            InstrumentMasterAddress =
                new("https://static.example.test/master.zip"),
            WebSocketAddress =
                new("wss://stream.example.test/feed"),
            PollingInterval = TimeSpan.FromSeconds(17),
        };
        var storage = new SettingsStorage();
        source.Save(storage);

        var target = new DefinedgeMessageAdapter(
            new IncrementalIdGenerator());
        target.Load(storage);

        AreEqual(source.Key.UnSecure(), target.Key.UnSecure());
        AreEqual(
            source.Secret.UnSecure(),
            target.Secret.UnSecure());
        AreEqual(
            source.Token.UnSecure(),
            target.Token.UnSecure());
        AreEqual(
            source.WebSocketToken.UnSecure(),
            target.WebSocketToken.UnSecure());
        AreEqual(
            source.OneTimePassword.UnSecure(),
            target.OneTimePassword.UnSecure());
        AreEqual(source.UserId, target.UserId);
        AreEqual(source.AccountId, target.AccountId);
        AreEqual(
            source.DefaultProduct, target.DefaultProduct);
        AreEqual(source.AlgoId, target.AlgoId);
        AreEqual(source.Address, target.Address);
        AreEqual(source.LoginAddress, target.LoginAddress);
        AreEqual(
            source.HistoryAddress, target.HistoryAddress);
        AreEqual(
            source.InstrumentMasterAddress,
            target.InstrumentMasterAddress);
        AreEqual(
            source.WebSocketAddress,
            target.WebSocketAddress);
        AreEqual(
            source.PollingInterval,
            target.PollingInterval);
    }

    [TestMethod]
    public void AuthCodeMatchesOfficialSdkAlgorithm()
    {
        AreEqual(
            "dd0398057713f96b8d0501f9e3762a91709d71f8d8929192751eb06feed28736",
            DefinedgeRestClient.CreateAuthCode(
                "otp-token",
                "123456",
                "api-secret"));
    }

    [TestMethod]
    public async Task MasterFileScalesTickAndStrike()
    {
        const string csv =
            "NFO,156870,ZYDUSLIFE,ZYDUSLIFE29SEP26C1600,OPTSTK,29092026,5,900,CE,160000,2,1,,1.000000,\n" +
            "NSE,22,ACC,ACC-EQ,EQ,,5,1,,0,2,1,INE012A01025,1.000000,ACC LIMITED\n";

        var instruments =
            await DefinedgeRestClient.ParseInstruments(csv);

        AreEqual(2, instruments.Length);
        AreEqual("NFO", instruments[0].Exchange);
        AreEqual(0.05m, instruments[0].TickSize);
        AreEqual(900m, instruments[0].LotSize);
        AreEqual(1600m, instruments[0].StrikePrice);
        AreEqual(
            new DateTime(
                2026, 9, 28, 18, 30, 0,
                DateTimeKind.Utc),
            instruments[0].Expiry);
        AreEqual(
            SecurityTypes.Option,
            instruments[0].ToSecurityType());
        AreEqual(
            SecurityTypes.Stock,
            instruments[1].ToSecurityType());
    }

    [TestMethod]
    public void CandleCsvPreservesOhlcvAndIndiaTime()
    {
        const string csv =
            "300620230915,1819.95,1822.95,1807.9,1820.2,9367,45100\n" +
            "300620230916,1820.2,1821,1818,1819.5,1200\n";

        var rows =
            DefinedgeRestClient.ParseHistory(csv, false);

        AreEqual(2, rows.Length);
        AreEqual(
            new DateTime(
                2023, 6, 30, 3, 45, 0,
                DateTimeKind.Utc),
            rows[0].Time);
        AreEqual(1819.95m, rows[0].Open);
        AreEqual(1822.95m, rows[0].High);
        AreEqual(1807.9m, rows[0].Low);
        AreEqual(1820.2m, rows[0].Close);
        AreEqual(9367m, rows[0].Volume);
        AreEqual(45100m, rows[0].OpenInterest);
        IsNull(rows[1].OpenInterest);
    }

    [TestMethod]
    public void TickCsvUsesUnixTimeAndOpenInterest()
    {
        const string csv =
            "1719811800,100.5,25,4200\n" +
            "1719811801,100.55,10,4210\n";

        var rows =
            DefinedgeRestClient.ParseHistory(csv, true);

        AreEqual(2, rows.Length);
        AreEqual(
            DateTimeOffset.FromUnixTimeSeconds(1719811800)
                .UtcDateTime,
            rows[0].Time);
        AreEqual(100.5m, rows[0].LastPrice);
        AreEqual(25m, rows[0].LastVolume);
        AreEqual(4200m, rows[0].OpenInterest);
    }

    [TestMethod]
    public void OrderRequestUsesDocumentedNativeCodes()
    {
        var condition = new DefinedgeOrderCondition
        {
            Product = DefinedgeProducts.Normal,
            TriggerPrice = 17650m,
            DisclosedVolume = 25,
            IsAfterMarket = true,
            MarketProtection = 2.5m,
            Remarks = "test-order",
        };
        var message = new OrderRegisterMessage
        {
            TransactionId = 42,
            SecurityId = new()
            {
                SecurityCode = "NIFTY29SEP26C18000",
                BoardCode = "NFO",
                Native = "NFO|156870",
            },
            Side = Sides.Buy,
            OrderType = OrderTypes.Limit,
            TimeInForce = TimeInForce.CancelBalance,
            Price = 17655.25m,
            Volume = 50,
            Condition = condition,
        };
        var instrument = new DefinedgeInstrument
        {
            Exchange = "NFO",
            Token = "156870",
            TradingSymbol = "NIFTY29SEP26C18000",
        };

        var request =
            DefinedgeMessageAdapter.CreateOrderRequest(
                message,
                instrument,
                condition,
                DefinedgeProducts.Delivery,
                "99999");

        AreEqual("NFO", request.Exchange);
        AreEqual("BUY", request.Side);
        AreEqual("SL-LIMIT", request.PriceType);
        AreEqual("NORMAL", request.Product);
        AreEqual(50L, request.Quantity);
        AreEqual(25L, request.DisclosedQuantity);
        AreEqual("IOC", request.Validity);
        AreEqual("Yes", request.AfterMarket);
        AreEqual("99999", request.AlgoId);
        AreEqual(17650m, request.TriggerPrice);
        AreEqual(2.5m, request.MarketProtection);
        var json = JsonConvert.SerializeObject(request);
        IsTrue(json.Contains(
            "\"amo\":\"Yes\"",
            StringComparison.Ordinal));
    }

    [TestMethod]
    public void SocketOrderAliasesMapToRestModel()
    {
        var root = JObject.Parse(
            """
			{
			  "t":"om",
			  "norenordno":"240726000001",
			  "actid":"ACCOUNT1",
			  "exch":"NFO",
			  "tsym":"NIFTY29SEP26C18000",
			  "qty":"50",
			  "prc":"100.25",
			  "prd":"NORMAL",
			  "status":"COMPLETE",
			  "reporttype":"Fill",
			  "trantype":"B",
			  "prctyp":"LMT",
			  "ret":"DAY",
			  "fillshares":"50",
			  "avgprc":"100.20",
			  "fltm":"26-07-2026 10:15:30",
			  "flid":"FILL-1",
			  "flqty":"50",
			  "flprc":"100.20",
			  "exchordid":"EX-1"
			}
			""");

        var order =
            DefinedgeSocketClient.NormalizeOrderUpdate(root);

        AreEqual("240726000001", order.OrderId);
        AreEqual("ACCOUNT1", order.AccountId);
        AreEqual("NFO", order.Exchange);
        AreEqual("NIFTY29SEP26C18000", order.TradingSymbol);
        AreEqual("50", order.FilledQuantity);
        AreEqual("FILL-1", order.FillId);
        AreEqual("100.20", order.FillPrice);
        AreEqual(Sides.Buy, order.Side.ToSide());
        AreEqual(OrderTypes.Limit, order.ToOrderType());
        AreEqual(
            OrderStates.Done,
            order.OrderStatus.ToOrderState(order.ReportType));
    }

    [TestMethod]
    public void QuoteSnapshotCreatesFiveLevelDepth()
    {
        var quote = JObject.Parse(
            """
			{
			  "status":"SUCCESS",
			  "exchange":"NSE",
			  "token":"22",
			  "tradingsymbol":"ACC-EQ",
			  "ltp":"1858.00",
			  "last_traded_qty":"2",
			  "last_trade_time":"15:29:59",
			  "best_bid_price1":"1858.00",
			  "best_bid_qty1":"8",
			  "best_bid_orders1":"1",
			  "best_ask_price1":"1859.00",
			  "best_ask_qty1":"94",
			  "best_ask_orders1":"4",
			  "best_bid_price5":"1852.60",
			  "best_bid_qty5":"100",
			  "best_ask_price5":"1862.00",
			  "best_ask_qty5":"10"
			}
			""");
        var instrument = new DefinedgeInstrument
        {
            Exchange = "NSE",
            Token = "22",
            TradingSymbol = "ACC-EQ",
        };

        var update =
            DefinedgeMessageAdapter.CreateQuoteUpdate(
                quote, instrument);
        var bids = update.GetBids();
        var asks = update.GetAsks();

        AreEqual("NSE", update.GetText("e"));
        AreEqual("22", update.GetText("tk"));
        AreEqual(1858m, update.GetDecimal("lp"));
        AreEqual(2, bids.Length);
        AreEqual(1858m, bids[0].Price);
        AreEqual(8m, bids[0].Volume);
        AreEqual(1, bids[0].OrdersCount);
        AreEqual(2, asks.Length);
        AreEqual(1859m, asks[0].Price);
        AreEqual(94m, asks[0].Volume);
        AreEqual(4, asks[0].OrdersCount);
    }
}
