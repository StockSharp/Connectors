namespace StockSharp.Connectors.Tests;

using System;

using Ecng.Common;
using Ecng.Serialization;
using Ecng.UnitTesting;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using Newtonsoft.Json.Linq;

using StockSharp.Firstock;
using StockSharp.Firstock.Native;
using StockSharp.Messages;

[TestClass]
public class FirstockTests : BaseTestClass
{
    [TestMethod]
    public void SettingsRoundTripKeepsCredentialsAndEndpoints()
    {
        var source = new FirstockMessageAdapter(new IncrementalIdGenerator())
        {
            UserId = "AB1234",
            Password = "password".Secure(),
            OneTimePassword = "123456".Secure(),
            VendorCode = "AB1234_API",
            ApiKey = "api-key".Secure(),
            Token = "session-key".Secure(),
            PortfolioName = "ACCOUNT1",
            DefaultProduct = FirstockProducts.Intraday,
            MarketProtection = 0.5m,
            PriceDivisor = 1000m,
            ReconnectAttempts = 7,
            Address = new("https://api.example.test/V1/"),
            SymbolsAddress = new("https://static.example.test/symbols/"),
            WebSocketAddress = new("wss://socket.example.test/V2/ws"),
            PollingInterval = TimeSpan.FromSeconds(17),
        };
        var storage = new SettingsStorage();
        source.Save(storage);

        var target = new FirstockMessageAdapter(new IncrementalIdGenerator());
        target.Load(storage);

        AreEqual(source.UserId, target.UserId);
        AreEqual(source.Password.UnSecure(), target.Password.UnSecure());
        AreEqual(source.OneTimePassword.UnSecure(), target.OneTimePassword.UnSecure());
        AreEqual(source.VendorCode, target.VendorCode);
        AreEqual(source.ApiKey.UnSecure(), target.ApiKey.UnSecure());
        AreEqual(source.Token.UnSecure(), target.Token.UnSecure());
        AreEqual(source.PortfolioName, target.PortfolioName);
        AreEqual(source.DefaultProduct, target.DefaultProduct);
        AreEqual(source.MarketProtection, target.MarketProtection);
        AreEqual(source.PriceDivisor, target.PriceDivisor);
        AreEqual(source.ReconnectAttempts, target.ReconnectAttempts);
        AreEqual(source.Address, target.Address);
        AreEqual(source.SymbolsAddress, target.SymbolsAddress);
        AreEqual(source.WebSocketAddress, target.WebSocketAddress);
        AreEqual(source.PollingInterval, target.PollingInterval);
    }

    [TestMethod]
    public void LoginPasswordUsesDocumentedSha256()
    {
        AreEqual(
            "5e884898da28047151d0e56f8dc6292773603d0d6aabbdd62a11ef721d1542d8",
            FirstockRestClient.HashPassword("password"));
    }

    [TestMethod]
    public void ResponseUnwrapsDataAndReportsApiError()
    {
        var data = FirstockRestClient.ParseResponse(
            """
			{
			  "status": "success",
			  "message": "Order details",
			  "data": {"orderNumber": "25042100011119"}
			}
			""",
            "placeOrder");

        AreEqual("25042100011119", data.GetText("orderNumber"));
        ThrowsExactly<InvalidOperationException>(() =>
            FirstockRestClient.ParseResponse(
                """
				{
				  "status": "failed",
				  "name": "INVALID_JKEY",
				  "error": {"message": "JKey is required"}
				}
				""",
                "orderBook"));
    }

    [TestMethod]
    public void SuccessfulEnvelopeStillReportsNativeOrderRejection()
    {
        var data = FirstockRestClient.ParseResponse(
            """
			{
			  "status": "success",
			  "message": "Order modification details",
			  "data": {
			    "orderNumber": "25042100011120",
			    "rejreason": "SAF:order is not open to modify"
			  }
			}
			""",
            "modifyOrder");

        ThrowsExactly<InvalidOperationException>(() =>
            FirstockRestClient.EnsureOrderActionAccepted(data, "modifyOrder"));
    }

    [TestMethod]
    public void PublicSymbolRowsPreserveNativeIdentifiers()
    {
        var stock = FirstockRestClient.ParseInstrument(
            "NSE",
            [
                "NSE", "14747", "1", "011NSETEST-EQ",
                "011NSETEST", "DUMMYSAN005", "0.05", "0",
            ]);
        var option = FirstockRestClient.ParseInstrument(
            "NFO",
            [
                "NFO", "35000", "30", "BANKNIFTY",
                "BANKNIFTY29SEP26C72600", "BANKNIFTY 26SEP 72600 C",
                "29-SEP-2026", "OPTIDX", "CE", "72600", "0.05", "600",
            ]);

        AreEqual("NSE:14747", stock.ToSecurityId().Native);
        AreEqual(SecurityTypes.Stock, stock.ToSecurityType());
        AreEqual(0.05m, stock.TickSize);
        AreEqual(SecurityTypes.Option, option.ToSecurityType());
        AreEqual(OptionTypes.Call, option.OptionType.ToOptionType());
        AreEqual(72600m, option.StrikePrice);
        AreEqual(30m, option.LotSize);
        AreEqual(
            new DateTime(2026, 9, 28, 18, 30, 0, DateTimeKind.Utc),
            option.Expiry);
    }

    [TestMethod]
    public void NativeIdentifiersMapSupportedBoards()
    {
        var native = new SecurityId
        {
            SecurityCode = "RELIANCE-EQ",
            BoardCode = "NSE",
            Native = "NSE:2885",
        }.ToInstrumentKey();

        AreEqual("NSE:2885", native);
        AreEqual(("NSE", "2885"), native.ParseInstrumentKey());
        AreEqual("NSE", "EQT".ToBoardCode());
        AreEqual("BFO", "BFO".ToBoardCode());
    }

    [TestMethod]
    public void V2MarketFeedUsesPaiseAndFiveLevelDepth()
    {
        var payload = JObject.Parse(
            """
			{
			  "NSE:2885": {
			    "best_buy": [
			      {"price":139690,"quantity":5,"orders":3},
			      {"price":139660,"quantity":500,"orders":10}
			    ],
			    "best_sell": [
			      {"price":139750,"quantity":880,"orders":6}
			    ],
			    "c_exch_feed_time":"30-Jan-2026 15:32:57",
			    "c_exch_seg":"NSE",
			    "c_symbol":"2885",
			    "i_average_trade_price":139124,
			    "i_closing_price":139100,
			    "i_feed_time":1769767377,
			    "i_high_price":139800,
			    "i_last_trade_quantity":1,
			    "i_last_trade_time":1769767199,
			    "i_last_traded_price":139540,
			    "i_low_price":137850,
			    "i_open_price":138260,
			    "i_total_open_interest":197996000,
			    "i_volume_traded_today":11238602
			  }
			}
			""");

        var update = FirstockSocketClient.ParseMarketFeeds(payload)[0];

        AreEqual("NSE", update.Exchange);
        AreEqual("2885", update.Token);
        AreEqual(1395.4m, update.LastPrice.ToPrice(100m));
        AreEqual(1396.9m, ((decimal?)update.Bids[0].Price).ToPrice(100m));
        AreEqual(5m, update.Bids[0].Quantity);
        AreEqual(3, update.Bids[0].Orders);
        AreEqual(1397.5m, ((decimal?)update.Asks[0].Price).ToPrice(100m));
        AreEqual(11238602m, update.Volume);
    }

    [TestMethod]
    public void V2OrderUpdateMapsNorenAliases()
    {
        var order = FirstockSocketClient.ParseOrder(JObject.Parse(
            """
			{
			  "tsym":"VIKASLIFE-EQ",
			  "rejreason":" ",
			  "pcode":"C",
			  "trantype":"B",
			  "token":"9931",
			  "prctyp":"MKT",
			  "ret":"DAY",
			  "norenordno":"25042400010260",
			  "uid":"AB1234",
			  "exch":"NSE",
			  "status":"ORDER PENDING",
			  "reporttype":"PendingNew",
			  "tm":"1745483337",
			  "actid":"AB1234",
			  "qty":"1",
			  "prc":"0.00"
			}
			"""));

        AreEqual("25042400010260", order.OrderId);
        AreEqual("NSE", order.Exchange);
        AreEqual("VIKASLIFE-EQ", order.TradingSymbol);
        AreEqual(Sides.Buy, order.Side.ToSide());
        AreEqual(OrderTypes.Market, order.ToOrderType());
        AreEqual(OrderStates.Pending, order.Status.ToOrderState(order.ReportType));
        AreEqual(
            new DateTime(2025, 4, 24, 8, 28, 57, DateTimeKind.Utc),
            order.OrderTime.ToFirstockTime());
    }

    [TestMethod]
    public void CandlesUseDirectPricesAndUnixTime()
    {
        var candles = FirstockRestClient.ParseArray<FirstockCandle>(
            JArray.Parse(
                """
				[
				  {
				    "time":"2025-02-10T09:15:00",
				    "epochTime":1739159100,
				    "open":23543.8,
				    "high":23548.65,
				    "low":23472.2,
				    "close":23480.1,
				    "volume":42,
				    "oi":7
				  }
				]
				"""));

        AreEqual(23543.8m, candles[0].Open);
        AreEqual(23548.65m, candles[0].High);
        AreEqual(23480.1m, candles[0].Close);
        AreEqual(42m, candles[0].Volume);
        AreEqual(7m, candles[0].OpenInterest);
        AreEqual(
            new DateTime(2025, 2, 10, 3, 45, 0, DateTimeKind.Utc),
            candles[0].GetCandleTime());
    }

    [TestMethod]
    public void ProductAndOrderCodesFollowPublicContract()
    {
        AreEqual("C", FirstockProducts.Delivery.ToNative());
        AreEqual("M", FirstockProducts.Margin.ToNative());
        AreEqual("I", FirstockProducts.Intraday.ToNative());
        AreEqual("LMT", OrderTypes.Limit.ToPriceType(100m));
        AreEqual("MKT", OrderTypes.Market.ToPriceType(0m));
        AreEqual("SL-LMT", OrderTypes.Conditional.ToPriceType(100m));
        AreEqual("SL-MKT", OrderTypes.Conditional.ToPriceType(0m));
        AreEqual("IOC", ((TimeInForce?)TimeInForce.CancelBalance).ToRetention());
    }
}
