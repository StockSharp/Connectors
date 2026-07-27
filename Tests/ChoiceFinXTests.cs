namespace StockSharp.Connectors.Tests;

using System;

using Ecng.Common;
using Ecng.Serialization;
using Ecng.UnitTesting;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using Newtonsoft.Json.Linq;

using StockSharp.ChoiceFinX;
using StockSharp.ChoiceFinX.Native;
using StockSharp.Messages;

[TestClass]
public class ChoiceFinXTests : BaseTestClass
{
    [TestMethod]
    public void SettingsRoundTripKeepsCredentialsAndEndpoints()
    {
        var source = new ChoiceFinXMessageAdapter(
            new IncrementalIdGenerator())
        {
            Token = "session-id".Secure(),
            AuthorizationHeader = "Bearer",
            AuthorizationScheme = "Bearer",
            VendorId = "VENDOR",
            VendorKey = "vendor-key".Secure(),
            WebSocketToken = "socket-jwt".Secure(),
            DefaultProduct =
                ChoiceFinXProducts.Intraday,
            PortfolioName = "CLIENT1",
            ModeType = "DESKTOP",
            Mode = 7,
            DeviceId = "device-42",
            PriceDivisor = 1000,
            Address =
                new("https://api.example.test/"),
            WebSocketAddress =
                new("wss://stream.example.test/ws/"),
            PollingInterval =
                TimeSpan.FromSeconds(17),
        };
        var storage = new SettingsStorage();
        source.Save(storage);

        var target = new ChoiceFinXMessageAdapter(
            new IncrementalIdGenerator());
        target.Load(storage);

        AreEqual(
            source.Token.UnSecure(),
            target.Token.UnSecure());
        AreEqual(
            source.AuthorizationHeader,
            target.AuthorizationHeader);
        AreEqual(
            source.AuthorizationScheme,
            target.AuthorizationScheme);
        AreEqual(source.VendorId, target.VendorId);
        AreEqual(
            source.VendorKey.UnSecure(),
            target.VendorKey.UnSecure());
        AreEqual(
            source.WebSocketToken.UnSecure(),
            target.WebSocketToken.UnSecure());
        AreEqual(
            source.DefaultProduct,
            target.DefaultProduct);
        AreEqual(
            source.PortfolioName,
            target.PortfolioName);
        AreEqual(source.ModeType, target.ModeType);
        AreEqual(source.Mode, target.Mode);
        AreEqual(source.DeviceId, target.DeviceId);
        AreEqual(
            source.PriceDivisor,
            target.PriceDivisor);
        AreEqual(source.Address, target.Address);
        AreEqual(
            source.WebSocketAddress,
            target.WebSocketAddress);
        AreEqual(
            source.PollingInterval,
            target.PollingInterval);
    }

    [TestMethod]
    public void NativeIdentifiersMapSegmentsAndBoards()
    {
        var native =
            new SecurityId
            {
                SecurityCode = "RELIANCE",
                BoardCode = "NSE",
                Native = "1@2885",
            }.ToInstrumentKey();
        AreEqual("1|2885", native);
        AreEqual((1, 2885L), native.ParseInstrumentKey());
        AreEqual("NFO", 2.ToBoardCode());
        AreEqual("BSE", 3.ToBoardCode());
        AreEqual("MCX", 5.ToBoardCode());
        AreEqual(13, "CDS".ToSegmentId());
    }

    [TestMethod]
    public void OrderRequestUsesDocumentedV2Codes()
    {
        var message = new OrderRegisterMessage
        {
            TransactionId = 42,
            SecurityId = new()
            {
                SecurityCode = "NIFTY",
                BoardCode = "NFO",
                Native = "2|12345",
            },
            Side = Sides.Buy,
            OrderType = OrderTypes.Conditional,
            TimeInForce = TimeInForce.CancelBalance,
            Price = 101.25m,
            Volume = 50,
        };
        var condition = new ChoiceFinXOrderCondition
        {
            Product = ChoiceFinXProducts.Intraday,
            TriggerPrice = 100.5m,
            DisclosedVolume = 10,
            IsAfterMarket = true,
            IsEdisRequired = true,
        };

        var request =
            ChoiceFinXMessageAdapter.CreateOrderRequest(
                message,
                new ChoiceFinXInstrument
                {
                    SegmentId = 2,
                    Token = 12345,
                },
                condition,
                ChoiceFinXProducts.Delivery,
                100,
                "API",
                9,
                "device");

        AreEqual(2, request.SegmentId);
        AreEqual(12345L, request.Token);
        AreEqual("SL_LIMIT", request.OrderType);
        AreEqual(1, request.Side);
        AreEqual(50, request.Quantity);
        AreEqual(10, request.DisclosedQuantity);
        AreEqual(10125L, request.Price);
        AreEqual(10050L, request.TriggerPrice);
        AreEqual(4, request.Validity);
        AreEqual("AM", request.ProductType);
        IsTrue(request.IsEdisRequired);
        AreEqual("42", request.Remarks);
        AreEqual("API", request.ModeType);
        AreEqual(9, request.Mode);
        AreEqual("device", request.DeviceId);
    }

    [TestMethod]
    public void ResponseUnwrapsStringifiedPayload()
    {
        var payload =
            ChoiceFinXRestClient.ParseResponse(
                """
                {
                  "Status": "Success",
                  "Response": "{\"ClientOrderNo\":\"ABC123\"}"
                }
                """,
                "test");

        AreEqual(
            "ABC123",
            ((JObject)payload)
                .GetText("ClientOrderNo"));
        ThrowsExactly<InvalidOperationException>(() =>
            ChoiceFinXRestClient.ParseResponse(
                """
                {
                  "Status": "Failed",
                  "Reason": "invalid session"
                }
                """,
                "test"));
    }

    [TestMethod]
    public void ScripDetailsPreserveNativeContract()
    {
        var payload = JToken.Parse(
            """
            {
              "PriceDivisor": 100,
              "ScripDetails": [{
                "SegmentId": 2,
                "Token": 12345,
                "TradingSymbol": "NIFTY26JUL24000CE",
                "InstrumentType": "OPTIDX",
                "Series": "OPTIDX",
                "TickSize": 5,
                "LotSize": 25,
                "StrikePrice": 2400000,
                "OptionType": "CE",
                "ExpiryDate": "2026-07-30"
              }]
            }
            """);

        var instrument =
            ChoiceFinXRestClient.ParseInstrument(
                payload, 0, 0, 100);

        AreEqual(2, instrument.SegmentId);
        AreEqual(12345L, instrument.Token);
        AreEqual(
            "NIFTY26JUL24000CE",
            instrument.Symbol);
        AreEqual(0.05m, instrument.TickSize);
        AreEqual(25m, instrument.LotSize);
        AreEqual(
            24000m, instrument.StrikePrice);
        AreEqual(
            SecurityTypes.Option,
            instrument.ToSecurityType());
        AreEqual(
            OptionTypes.Call,
            instrument.OptionType.ToOptionType());
    }

    [TestMethod]
    public void TouchlineUsesDivisorAndFiveLevelDepth()
    {
        var payload = JToken.Parse(
            """
            {
              "PriceDivisor": 100,
              "Touchline": [{
                "SegmentId": 1,
                "Token": 2885,
                "LTP": 252345,
                "LTQ": 12,
                "Volume": 5000,
                "OpenPrice": 250000,
                "HighPrice": 253000,
                "LowPrice": 249500,
                "ClosePrice": 251000,
                "Bids": [
                  {"Price":252300,"Qty":100,"Orders":3},
                  {"Price":252250,"Qty":200,"Orders":4}
                ],
                "Asks": [
                  {"Price":252350,"Qty":150,"Orders":2}
                ]
              }]
            }
            """);

        var tick =
            ChoiceFinXRestClient.ParseTouchlines(
                payload, 100)[0];

        AreEqual(1, tick.SegmentId);
        AreEqual(2885L, tick.Token);
        AreEqual(2523.45m, tick.LastPrice);
        AreEqual(2500m, tick.Open);
        AreEqual(2, tick.Bids.Length);
        AreEqual(2523m, tick.Bids[0].Price);
        AreEqual(100m, tick.Bids[0].Quantity);
        AreEqual(3, tick.Bids[0].Orders);
        AreEqual(2523.5m, tick.Asks[0].Price);
    }

    [TestMethod]
    public void ChartHistoryUsesChoice1980Epoch()
    {
        var payload = JToken.Parse(
            """
            {
              "PriceDivisor": 100,
              "lstChartHistory": [
                {
                  "PriceDate": 60,
                  "OpenPrice": 10000,
                  "HighPrice": 10200,
                  "LowPrice": 9900,
                  "ClosePrice": 10100,
                  "VolumeTraded": 500,
                  "OpenInterest": 42
                }
              ]
            }
            """);

        var candle =
            ChoiceFinXRestClient.ParseCandles(
                payload, 100)[0];

        AreEqual(
            new DateTime(
                1980, 1, 1, 0, 1, 0,
                DateTimeKind.Utc),
            candle.Time);
        AreEqual(100m, candle.Open);
        AreEqual(102m, candle.High);
        AreEqual(99m, candle.Low);
        AreEqual(101m, candle.Close);
        AreEqual(500m, candle.Volume);
        AreEqual(42m, candle.OpenInterest);
    }

    [TestMethod]
    public void OrdersAndTradesMapCurrentFields()
    {
        var orders =
            ChoiceFinXRestClient.ParseOrders(
                JToken.Parse(
                    """
                    [{
                      "ClientOrderNo": "C42",
                      "ExchangeOrderNo": "E99",
                      "SegmentId": 1,
                      "Token": 2885,
                      "BS": 2,
                      "OrderType": "RL_LIMIT",
                      "ProductType": "D",
                      "Validity": 1,
                      "Qty": 10,
                      "PendingQty": 4,
                      "TradedQty": 6,
                      "Price": 2523.45,
                      "OrderStatus": "PENDING",
                      "Remarks": "42"
                    }]
                    """));
        var trades =
            ChoiceFinXRestClient.ParseTrades(
                JToken.Parse(
                    """
                    [{
                      "TradeNumber": "T7",
                      "ClientOrderNo": "C42",
                      "SegmentId": 1,
                      "Token": 2885,
                      "BS": 2,
                      "TradedPrice": 2523.4,
                      "TradeQty": 6
                    }]
                    """));

        AreEqual("C42", orders[0].OrderId);
        AreEqual("E99", orders[0].ExchangeOrderId);
        AreEqual(
            OrderStates.Active,
            orders[0].Status.ToOrderState());
        AreEqual(4m, orders[0].PendingQuantity);
        AreEqual("T7", trades[0].TradeId);
        AreEqual("C42", trades[0].OrderId);
        AreEqual(2523.4m, trades[0].Price);
        AreEqual(Sides.Sell, trades[0].Side.ToSide());
    }

    [TestMethod]
    public void InteractiveSocketPayloadMergesEnvelope()
    {
        var payload =
            ChoiceFinXSocketClient.GetPayload(
                JObject.Parse(
                    """
                    {
                      "MessageType": "TRD_MSG",
                      "Data": {
                        "TradeNumber": "T1",
                        "UniqueCode": "C1"
                      }
                    }
                    """));

        AreEqual(
            "TRD_MSG",
            payload.GetText("MessageType"));
        AreEqual(
            "T1",
            payload.GetText("TradeNumber"));
        AreEqual(
            "C1",
            payload.GetText("UniqueCode"));
    }
}
