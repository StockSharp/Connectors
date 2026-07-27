namespace StockSharp.Connectors.Tests;

using System;
using System.Buffers.Binary;
using System.Globalization;
using System.Threading.Tasks;

using Ecng.Common;
using Ecng.Serialization;
using Ecng.UnitTesting;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using Newtonsoft.Json.Linq;

using StockSharp.Messages;
using StockSharp.PaytmMoney;
using StockSharp.PaytmMoney.Native;

[TestClass]
public class PaytmMoneyTests : BaseTestClass
{
    [TestMethod]
    public void SettingsRoundTripKeepsCredentialsAndEndpoints()
    {
        var source = new PaytmMoneyMessageAdapter(
            new IncrementalIdGenerator())
        {
            Key = "api-key".Secure(),
            Secret = "api-secret".Secure(),
            Token = "access-token".Secure(),
            ReadAccessToken = "read-token".Secure(),
            PublicAccessToken = "public-token".Secure(),
            RequestToken = "request-token".Secure(),
            DefaultProduct = PaytmMoneyProducts.Margin,
            PortfolioName = "portfolio",
            Address = new("https://api.example.test/"),
            WebSocketAddress =
                new("wss://stream.example.test/feed"),
            SecurityMasterFile = "master.csv",
            PollingInterval = TimeSpan.FromSeconds(17),
        };
        var storage = new SettingsStorage();
        source.Save(storage);

        var target = new PaytmMoneyMessageAdapter(
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
            source.ReadAccessToken.UnSecure(),
            target.ReadAccessToken.UnSecure());
        AreEqual(
            source.PublicAccessToken.UnSecure(),
            target.PublicAccessToken.UnSecure());
        AreEqual(
            source.RequestToken.UnSecure(),
            target.RequestToken.UnSecure());
        AreEqual(
            source.DefaultProduct, target.DefaultProduct);
        AreEqual(source.PortfolioName, target.PortfolioName);
        AreEqual(source.Address, target.Address);
        AreEqual(
            source.WebSocketAddress,
            target.WebSocketAddress);
        AreEqual(
            source.SecurityMasterFile,
            target.SecurityMasterFile);
        AreEqual(
            source.PollingInterval,
            target.PollingInterval);
    }

    [TestMethod]
    public void LoginUrlEscapesApiKeyAndState()
    {
        var adapter = new PaytmMoneyMessageAdapter(
            new IncrementalIdGenerator())
        {
            Key = "key +/=".Secure(),
        };
        var uri = adapter.GetLoginUri("state +/=");

        AreEqual(
            "https://login.paytmmoney.com/merchant-login?apiKey=key%20%2B%2F%3D&state=state%20%2B%2F%3D",
            uri.AbsoluteUri);
    }

    [TestMethod]
    public void OrderRequestUsesDocumentedNativeCodes()
    {
        var condition = new PaytmMoneyOrderCondition
        {
            Product = PaytmMoneyProducts.Bracket,
            TriggerPrice = 97.5m,
            AfterMarket = true,
            ProfitValue = 8,
            StopLossValue = 4,
        };
        var message = new OrderRegisterMessage
        {
            TransactionId = 42,
            SecurityId = new()
            {
                SecurityCode = "RELIANCE",
                BoardCode = "NSE_EQ",
                Native =
                    "NSE|E|2885|EQUITY|ES",
            },
            Side = Sides.Buy,
            OrderType = OrderTypes.Limit,
            TimeInForce = TimeInForce.CancelBalance,
            Price = 100.25m,
            Volume = 7,
            Condition = condition,
        };

        var request =
            PaytmMoneyMessageAdapter.CreateOrderRequest(
                message,
                condition,
                PaytmMoneyProducts.Bracket);

        AreEqual("B", request.TransactionType);
        AreEqual("NSE", request.Exchange);
        AreEqual("E", request.Segment);
        AreEqual("B", request.Product);
        AreEqual("2885", request.SecurityId);
        AreEqual("SL", request.OrderType);
        AreEqual("IOC", request.Validity);
        AreEqual(97.5m, request.TriggerPrice);
        AreEqual(8m, request.ProfitValue);
        AreEqual(4m, request.StopLossValue);
        AreEqual("42", request.Remarks);
        AreEqual(
            "orders/v1/place/bracket",
            PaytmMoneyRestClient.GetOrderPath(
                "place", PaytmMoneyProducts.Bracket));
        AreEqual(
            "orders/v1/exit/bracket",
            PaytmMoneyRestClient.GetOrderPath(
                "cancel", PaytmMoneyProducts.Bracket));
    }

    [TestMethod]
    public async Task SecurityMasterUsesHeadersAndInfersTypes()
    {
        const string csv =
            "security_id,exchange,segment,trading_symbol,name,instrument_type,isin,expiry_date,strike_price,option_type,lot_size,tick_size\n" +
            "2885,NSE,E,RELIANCE,Reliance Industries,ES,INE002A01018,,,,1,0.05\n" +
            "50123,NSE,D,NIFTY26JULFUT,Nifty Future,FUTIDX,,2026-07-30,,,25,0.05\n";

        var instruments =
            await PaytmMoneyRestClient.ParseInstruments(csv);

        AreEqual(2, instruments.Length);
        AreEqual("EQUITY", instruments[0].ScripType);
        AreEqual("ES", instruments[0].HistoryType);
        AreEqual(0.05m, instruments[0].TickSize);
        AreEqual("FUTURE", instruments[1].ScripType);
        AreEqual("FUTIDX", instruments[1].HistoryType);
        AreEqual(25m, instruments[1].LotSize);
        AreEqual(
            new DateTime(
                2026, 7, 30, 0, 0, 0,
                DateTimeKind.Utc),
            instruments[1].ExpiryDate);
    }

    [TestMethod]
    public void QuotePacketUsesOfficialBinaryOffsets()
    {
        var packet = new byte[67];
        packet[0] = 62;
        WriteSingle(packet, 1, 100.5f);
        BinaryPrimitives.WriteUInt32LittleEndian(
            packet.AsSpan(5, 4), 100);
        BinaryPrimitives.WriteUInt32LittleEndian(
            packet.AsSpan(9, 4), 2885);
        packet[13] = 1;
        packet[14] = 2;
        BinaryPrimitives.WriteUInt32LittleEndian(
            packet.AsSpan(15, 4), 25);
        WriteSingle(packet, 19, 99.75f);
        BinaryPrimitives.WriteUInt32LittleEndian(
            packet.AsSpan(23, 4), 10000);
        BinaryPrimitives.WriteUInt32LittleEndian(
            packet.AsSpan(27, 4), 1500);
        BinaryPrimitives.WriteUInt32LittleEndian(
            packet.AsSpan(31, 4), 1800);
        WriteSingle(packet, 35, 98);
        WriteSingle(packet, 39, 97);
        WriteSingle(packet, 43, 102);
        WriteSingle(packet, 47, 96);

        var tick =
            PaytmMoneyWebSocketClient.Decode(packet)[0];

        AreEqual("2885", tick.SecurityId);
        AreEqual(100.5m, tick.LastPrice);
        AreEqual(25m, tick.LastQuantity);
        AreEqual(10000m, tick.Volume);
        AreEqual(1500m, tick.TotalBuyQuantity);
        AreEqual(1800m, tick.TotalSellQuantity);
        AreEqual(
            DateTimeOffset.FromUnixTimeSeconds(315532900)
                .UtcDateTime,
            tick.LastTradeTime);
    }

    [TestMethod]
    public void FullPacketContainsFiveLevelDepthAndOpenInterest()
    {
        var packet = new byte[175];
        packet[0] = 63;
        for (var index = 0; index < 5; index++)
        {
            var offset = 1 + index * 20;
            BinaryPrimitives.WriteUInt32LittleEndian(
                packet.AsSpan(offset, 4),
                (uint)(100 + index));
            BinaryPrimitives.WriteUInt32LittleEndian(
                packet.AsSpan(offset + 4, 4),
                (uint)(200 + index));
            BinaryPrimitives.WriteUInt16LittleEndian(
                packet.AsSpan(offset + 8, 2),
                (ushort)(2 + index));
            BinaryPrimitives.WriteUInt16LittleEndian(
                packet.AsSpan(offset + 10, 2),
                (ushort)(3 + index));
            WriteSingle(
                packet, offset + 12, 100 - index);
            WriteSingle(
                packet, offset + 16, 101 + index);
        }
        WriteSingle(packet, 101, 100.5f);
        BinaryPrimitives.WriteUInt32LittleEndian(
            packet.AsSpan(105, 4), 200);
        BinaryPrimitives.WriteUInt32LittleEndian(
            packet.AsSpan(109, 4), 50123);
        BinaryPrimitives.WriteUInt32LittleEndian(
            packet.AsSpan(115, 4), 10);
        BinaryPrimitives.WriteUInt32LittleEndian(
            packet.AsSpan(167, 4), 45000);
        BinaryPrimitives.WriteInt32LittleEndian(
            packet.AsSpan(171, 4), -125);

        var tick =
            PaytmMoneyWebSocketClient.Decode(packet)[0];

        AreEqual("50123", tick.SecurityId);
        AreEqual(5, tick.Bids.Length);
        AreEqual(5, tick.Asks.Length);
        AreEqual(100m, tick.Bids[0].Price);
        AreEqual(100m, tick.Bids[0].Quantity);
        AreEqual(2, tick.Bids[0].Orders);
        AreEqual(101m, tick.Asks[0].Price);
        AreEqual(200m, tick.Asks[0].Quantity);
        AreEqual(45000m, tick.OpenInterest);
        AreEqual(-125m, tick.OpenInterestChange);
    }

    [TestMethod]
    public void CandleRowsPreserveOhlcvAndOpenInterest()
    {
        var rows = JArray.Parse(
            "[[1719811800,100.5,102,99.75,101.25,15000,4200]," +
            "[1719811860,\"101.25\",\"103\",\"101\",\"102.5\",\"9000\",null]]");

        var candles =
            PaytmMoneyRestClient.ParseCandles(rows);

        AreEqual(2, candles.Length);
        AreEqual(
            DateTimeOffset.FromUnixTimeSeconds(1719811800)
                .UtcDateTime,
            candles[0].Time);
        AreEqual(100.5m, candles[0].Open);
        AreEqual(102m, candles[0].High);
        AreEqual(99.75m, candles[0].Low);
        AreEqual(101.25m, candles[0].Close);
        AreEqual(15000m, candles[0].Volume);
        AreEqual(4200m, candles[0].OpenInterest);
    }

    [TestMethod]
    public void NativeStatusesMapToStockSharpStates()
    {
        AreEqual(
            OrderStates.Active,
            "OPEN".ToOrderState());
        AreEqual(
            OrderStates.Done,
            "fully traded".ToOrderState());
        AreEqual(
            OrderStates.Done,
            "CANCELLED".ToOrderState());
        AreEqual(
            OrderStates.Failed,
            "REJECTED".ToOrderState());
    }

    private static void WriteSingle(
        byte[] target, int offset, float value)
        => BinaryPrimitives.WriteInt32LittleEndian(
            target.AsSpan(offset, 4),
            BitConverter.SingleToInt32Bits(value));
}
