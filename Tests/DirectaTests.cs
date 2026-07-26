namespace StockSharp.Connectors.Tests;

using System;
using System.Net;

using Ecng.Common;
using Ecng.Serialization;
using Ecng.UnitTesting;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using StockSharp.Directa;
using StockSharp.Directa.Native;
using StockSharp.Messages;

[TestClass]
public class DirectaTests : BaseTestClass
{
    [TestMethod]
    public void SettingsRoundTripKeepsSocketOptions()
    {
        var source = new DirectaMessageAdapter(
            new IncrementalIdGenerator())
        {
            Address =
                new DnsEndPoint("trade.example.test", 11002),
            DataAddress =
                new DnsEndPoint("data.example.test", 11001),
            HistoryAddress =
                new DnsEndPoint("history.example.test", 11003),
            RequestTimeout = TimeSpan.FromSeconds(45),
            AutoConfirmOrders = false,
            MaxMarketDepth = 15,
            TimeZoneId = "UTC",
        };
        var storage = new SettingsStorage();
        source.Save(storage);

        var target = new DirectaMessageAdapter(
            new IncrementalIdGenerator());
        target.Load(storage);

        AreEqual(source.Address.ToString(),
            target.Address.ToString());
        AreEqual(source.DataAddress.ToString(),
            target.DataAddress.ToString());
        AreEqual(source.HistoryAddress.ToString(),
            target.HistoryAddress.ToString());
        AreEqual(source.RequestTimeout,
            target.RequestTimeout);
        AreEqual(source.AutoConfirmOrders,
            target.AutoConfirmOrders);
        AreEqual(source.MaxMarketDepth,
            target.MaxMarketDepth);
        AreEqual(source.TimeZoneId,
            target.TimeZoneId);
    }

    [TestMethod]
    public void OrderCommandsMatchDocumentedProtocol()
    {
        AreEqual(
            "ACQAZ ORD001,FCA,10,4.75",
            DirectaProtocol.CreateOrderCommand(
                "ORD001", "FCA", Sides.Buy,
                OrderTypes.Limit, 10, 4.75m, null));
        AreEqual(
            "VENMARKET ORD002,FCA,3",
            DirectaProtocol.CreateOrderCommand(
                "ORD002", "FCA", Sides.Sell,
                OrderTypes.Market, 3, 0, null));
        AreEqual(
            "ACQSTOPLIMIT ORD003,FCA,2,6,5.75",
            DirectaProtocol.CreateOrderCommand(
                "ORD003", "FCA", Sides.Buy,
                OrderTypes.Conditional, 2, 6, 5.75m));
        AreEqual(
            "MODORD ORD001,4.5",
            DirectaProtocol.CreateReplaceCommand(
                "ORD001", 4.5m, null));
    }

    [TestMethod]
    public void RealtimeRecordsUseDocumentedFields()
    {
        var price = DirectaProtocol.ParsePrice(
            "PRICE;FCA;16:18:11;6.73;10;17917975;10150;6.57;6.93",
            TimeZoneInfo.Utc);
        AreEqual("FCA", price.Ticker);
        AreEqual(6.73m, price.Price);
        AreEqual(10m, price.Volume);
        AreEqual(17917975L, price.TradeId);
        AreEqual(10150L, price.ExchangeTradeId);
        AreEqual(6.57m, price.LowPrice);
        AreEqual(6.93m, price.HighPrice);

        var quote = DirectaProtocol.ParseBidAsk(
            "BIDASK;FCA;16:41:21;14381;6;6.795;5458;3;6.805",
            TimeZoneInfo.Utc);
        AreEqual(14381m, quote.BidVolume);
        AreEqual(6, quote.BidOrders);
        AreEqual(6.795m, quote.BidPrice);
        AreEqual(5458m, quote.AskVolume);
        AreEqual(6.805m, quote.AskPrice);
    }

    [TestMethod]
    public void BookRecordProducesFiveLevelsPerSide()
    {
        var book = DirectaProtocol.ParseBook(
            "BOOK_5;FCA;16:26:09;" +
            "17743;6;6.755;31230;6;6.750;36723;11;6.745;48250;14;6.740;56771;11;6.735;" +
            "7600;3;6.765;18795;8;6.770;15358;8;6.775;21212;10;6.780;12522;5;6.785",
            TimeZoneInfo.Utc);

        AreEqual("FCA", book.Ticker);
        AreEqual(1, book.FirstLevel);
        AreEqual(10, book.Levels.Length);
        AreEqual(Sides.Buy, book.Levels[0].Side);
        AreEqual(6.755m, book.Levels[0].Price);
        AreEqual(6, book.Levels[0].Orders);
        AreEqual(Sides.Sell, book.Levels[5].Side);
        AreEqual(6.765m, book.Levels[5].Price);
    }

    [TestMethod]
    public void HistoryRecordsPreserveOhlcAndTicks()
    {
        var candle = DirectaProtocol.ParseCandle(
            "CANDLE;REY;20140618;09:00:00;57.65000;57.35000;58.10000;57.55000;1115",
            TimeZoneInfo.Utc);
        AreEqual(
            new DateTime(
                2014, 6, 18, 9, 0, 0,
                DateTimeKind.Utc),
            candle.Time);
        AreEqual(57.65m, candle.Open);
        AreEqual(57.35m, candle.Low);
        AreEqual(58.10m, candle.High);
        AreEqual(57.55m, candle.Close);
        AreEqual(1115m, candle.Volume);

        var tick = DirectaProtocol.ParseTick(
            "TBT;REY;20140618;09:09:21;57.55000;12",
            TimeZoneInfo.Utc);
        AreEqual(57.55m, tick.Price);
        AreEqual(12L, tick.ProgressiveVolume);
    }

    [TestMethod]
    public void TradingRecordsMapStatusesAndExecutionFields()
    {
        var order = DirectaProtocol.ParseOrder(
            "ORDER;A2A;10:51:32;ORD105037;ACQAZ;1.345;0.0;1;2003;1.3400;1.3440;0;P3710505738518",
            TimeZoneInfo.Utc);
        AreEqual("ORD105037", order.OrderId);
        AreEqual(OrderStates.Done,
            DirectaProtocol.ToOrderState(order.Status));
        AreEqual(Sides.Buy,
            DirectaProtocol.ToSide(order.Operation));
        AreEqual(1.344m, order.ExecutionPrice);
        AreEqual("P3710505738518", order.DirectaId);

        var result = DirectaProtocol.ParseTradeResult(
            "TRADOK;A2A;ORD105037;3001;ACQAZ;1;1.345;0.0;1.3440;1;0;P3710505738518");
        AreEqual(1.344m, result.ExecutionPrice);
        AreEqual(1m, result.ExecutedQuantity);
        AreEqual(0m, result.RemainingQuantity);
    }
}
