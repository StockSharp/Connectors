namespace StockSharp.Connectors.Tests;

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using Ecng.Common;
using Ecng.Serialization;
using Ecng.UnitTesting;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using Newtonsoft.Json.Linq;

using StockSharp.Bigul;
using StockSharp.Bigul.Native;
using StockSharp.Messages;

[TestClass]
public class BigulTests : BaseTestClass
{
    [TestMethod]
    public void SettingsRoundTripKeepsCredentialsAndEndpoints()
    {
        var source = new BigulMessageAdapter(new IncrementalIdGenerator())
        {
            ClientCode = "Y09012345",
            ApiKey = "app-key".Secure(),
            ApiSecret = "app-secret".Secure(),
            OneTimePassword = "123456".Secure(),
            Token = "access-token".Secure(),
            Source = "B2C",
            PortfolioName = "ACCOUNT1",
            DefaultProduct = BigulProducts.Intraday,
            MarketProtection = 2.5m,
            ReconnectAttempts = 7,
            Address = new("https://api.example.test/api/v1/"),
            MasterAddress = new("https://static.example.test/master.zip"),
            WebSocketAddress = new("wss://socket.example.test/broadcast/socket"),
            PollingInterval = TimeSpan.FromSeconds(17),
        };
        var storage = new SettingsStorage();
        source.Save(storage);

        var target = new BigulMessageAdapter(new IncrementalIdGenerator());
        target.Load(storage);

        AreEqual(source.ClientCode, target.ClientCode);
        AreEqual(source.ApiKey.UnSecure(), target.ApiKey.UnSecure());
        AreEqual(source.ApiSecret.UnSecure(), target.ApiSecret.UnSecure());
        AreEqual(source.OneTimePassword.UnSecure(), target.OneTimePassword.UnSecure());
        AreEqual(source.Token.UnSecure(), target.Token.UnSecure());
        AreEqual(source.Source, target.Source);
        AreEqual(source.PortfolioName, target.PortfolioName);
        AreEqual(source.DefaultProduct, target.DefaultProduct);
        AreEqual(source.MarketProtection, target.MarketProtection);
        AreEqual(source.ReconnectAttempts, target.ReconnectAttempts);
        AreEqual(source.Address, target.Address);
        AreEqual(source.MasterAddress, target.MasterAddress);
        AreEqual(source.WebSocketAddress, target.WebSocketAddress);
        AreEqual(source.PollingInterval, target.PollingInterval);
    }

    [TestMethod]
    public void ResponseUnwrapsDataAndReportsApiError()
    {
        var data = BigulRestClient.ParseResponse(
            """
            {
              "status": true,
              "message": "Request successful.",
              "data": {"nOrdNo": "1310111898"},
              "error": null
            }
            """,
            "order/vr-place");

        AreEqual("1310111898", data.GetText("nOrdNo"));
        ThrowsExactly<InvalidOperationException>(() =>
            BigulRestClient.ParseResponse(
                """
                {
                  "status": false,
                  "message": "Error: Invalid Request",
                  "data": null,
                  "error": {"msg": "Static IP is not authorized"}
                }
                """,
                "order/order-book"));
    }

    [TestMethod]
    public void EquityMasterPreservesTokenAndPriceStep()
    {
        var instrument = BigulRestClient.ParseInstrument(
            "nse_cm",
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["ScripCode"] = "2885",
                ["Name"] = "RELIANCE",
                ["Desc"] = "RELIANCE INDUSTRIES LTD",
                ["TradingSymbol"] = "RELIANCE-EQ",
                ["TickSize"] = "5.0",
                ["SERIES"] = "EQ",
                ["MinimumLotQty"] = "1",
                ["ISIN"] = "INE002A01018",
                ["IsFuture"] = "0",
                ["IsOption"] = "0",
            });

        AreEqual("nse_cm:2885", instrument.ToSecurityId().Native);
        AreEqual("NSE", instrument.ToSecurityId().BoardCode);
        AreEqual(SecurityTypes.Stock, instrument.ToSecurityType());
        AreEqual(0.05m, instrument.TickSize);
        AreEqual(1m, instrument.LotSize);
        AreEqual("INE002A01018", instrument.Isin);
    }

    [TestMethod]
    public void DerivativeMasterMapsExpiryOptionAndCurrencyTick()
    {
        var option = BigulRestClient.ParseInstrument(
            "nse_fo",
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["ScripCode"] = "35000",
                ["Name"] = "BANKNIFTY",
                ["Desc"] = "BANKNIFTY 29SEP26 72600 CE",
                ["TradingSymbol"] = "BANKNIFTY26SEP72600CE",
                ["TickSize"] = "5.0",
                ["SERIES"] = "OPTIDX",
                ["OPTION_TYPE"] = "CE",
                ["MinimumLotQuantity"] = "35",
                ["StrikePrice"] = "72600",
            });
        var currency = BigulRestClient.ParseInstrument(
            "cde_fo",
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["ScripCode"] = "10210",
                ["Name"] = "USDINR",
                ["Desc"] = "USDINR 31JUL26 95.125 CE",
                ["TradingSymbol"] = "USDINR2673195.125CE",
                ["TickSize"] = "25000",
                ["SERIES"] = "OPTCUR",
                ["OPTION_TYPE"] = "CE",
                ["MinimumLotQuantity"] = "1",
                ["StrikePrice"] = "95.125",
            });

        AreEqual(SecurityTypes.Option, option.ToSecurityType());
        AreEqual(OptionTypes.Call, option.OptionType.ToOptionType());
        AreEqual(72600m, option.StrikePrice);
        AreEqual(35m, option.LotSize);
        AreEqual(
            new DateTime(2026, 9, 28, 18, 30, 0, DateTimeKind.Utc),
            option.Expiry);
        AreEqual("CDS", currency.Segment.ToBoardCode());
        AreEqual(0.0025m, currency.TickSize);
    }

    [TestMethod]
    public void NativeCodesFollowPublicOrderContract()
    {
        var native = new SecurityId
        {
            SecurityCode = "RELIANCE-EQ",
            BoardCode = "NSE",
            Native = "nse_cm:2885",
        }.ToInstrumentKey();

        AreEqual("nse_cm:2885", native);
        AreEqual(("nse_cm", "2885"), native.ParseInstrumentKey());
        AreEqual("NFO", "nse_fo".ToBoardCode());
        AreEqual("MCX", "mcx_fo".ToBoardCode());
        AreEqual("CNC", BigulProducts.Delivery.ToNative());
        AreEqual("MIS", BigulProducts.Intraday.ToNative());
        AreEqual("NRML", BigulProducts.Normal.ToNative());
        AreEqual("L", OrderTypes.Limit.ToPriceType(100m));
        AreEqual("MKT", OrderTypes.Market.ToPriceType(0m));
        AreEqual("SL", OrderTypes.Conditional.ToPriceType(100m));
        AreEqual("SL-M", OrderTypes.Conditional.ToPriceType(0m));
        AreEqual("IOC", ((TimeInForce?)TimeInForce.CancelBalance).ToRetention());
    }

    [TestMethod]
    public void AuthenticationFrameUsesOfficialHsmFields()
    {
        var packet = BigulSocketClient.CreateAuthentication("token01", "SDK");

        AreEqual(packet.Length - 2, BinaryPrimitives.ReadUInt16BigEndian(packet));
        AreEqual((byte)1, packet[2]);
        AreEqual((byte)4, packet[3]);
        AreEqual((byte)1, packet[4]);
        AreEqual(7, BinaryPrimitives.ReadUInt16BigEndian(packet.AsSpan(5, 2)));
        AreEqual("token01", Encoding.UTF8.GetString(packet.AsSpan(7, 7)));
        IsTrue(packet.Contains((byte)'N'));
        AreEqual("SDK", Encoding.UTF8.GetString(packet.AsSpan(packet.Length - 3)));
    }

    [TestMethod]
    public void SubscriptionFrameUsesOfficialTopicAndChannel()
    {
        const string topic = "sf|nse_cm|11536";
        var packet = BigulSocketClient.CreateSubscription(
            true,
            [topic],
            100,
            20);

        AreEqual((byte)4, packet[2]);
        AreEqual((byte)2, packet[3]);
        AreEqual((byte)1, packet[4]);
        AreEqual(1, BinaryPrimitives.ReadUInt16BigEndian(packet.AsSpan(7, 2)));
        AreEqual(topic.Length, packet[9]);
        AreEqual(topic, Encoding.ASCII.GetString(packet.AsSpan(10, topic.Length)));
        AreEqual((byte)1, packet[^1]);
    }

    [TestMethod]
    public void OfficialHsmSnapshotDecodesPricesAndDepth()
    {
        using var adapter = new BigulMessageAdapter(new IncrementalIdGenerator());
        using var socket = new BigulSocketClient(
            "token",
            0,
            adapter.ReConnectionSettings.WorkingTime,
            new("wss://socket.example.test/broadcast/socket"));
        var result = socket.Decode(BuildSnapshot());
        var tick = result.Ticks.Single();

        AreEqual("nse_cm:11536", tick.InstrumentKey);
        AreEqual(1905.65m, tick.LastPrice);
        AreEqual(4241579m, tick.Volume);
        AreEqual(1905.7m, tick.BidPrice);
        AreEqual(1907m, tick.AskPrice);
        AreEqual(125m, tick.OpenInterest);
        AreEqual(1890m, tick.OpenPrice);
        AreEqual(1900m, tick.ClosePrice);
    }

    [TestMethod]
    public void JsonStreamingFallbackMapsOfficialExamples()
    {
        var scrip = BigulSocketClient.ParseJsonMarketData(JObject.Parse(
            """
            {
              "tk": "11536",
              "e": "nse_cm",
              "name": "sf",
              "ltp": "1905.65",
              "v": "4241579",
              "ts": "TCS-EQ"
            }
            """));
        var depth = BigulSocketClient.ParseJsonMarketData(JObject.Parse(
            """
            {
              "tk": "11536",
              "e": "nse_cm",
              "name": "dp",
              "bp": "1905.70",
              "sp": "1907.00",
              "ts": "TCS-EQ"
            }
            """));

        AreEqual("nse_cm:11536", scrip.InstrumentKey);
        AreEqual(1905.65m, scrip.LastPrice);
        AreEqual(4241579m, scrip.Volume);
        AreEqual(1905.7m, depth.Bids[0].Price);
        AreEqual(1907m, depth.Asks[0].Price);
    }

    [TestMethod]
    public void TradingModelsMapStatusAndIndianTime()
    {
        var order = BigulRestClient.ParseArray<BigulOrder>(JArray.Parse(
            """
            [
              {
                "nOrdNo": "1310033536",
                "exSeg": "nse_cm",
                "tok": "2885",
                "trdSym": "RELIANCE-EQ",
                "prc": "1564.90",
                "prcTp": "L",
                "qty": 100,
                "fldQty": 0,
                "ordSt": "open pending",
                "trnsTp": "B",
                "ordDtTm": "24-Dec-2025 11:49:18"
              }
            ]
            """))[0];

        AreEqual(OrderStates.Pending, order.Status.ToOrderState());
        AreEqual(OrderTypes.Limit, order.ToOrderType());
        AreEqual(Sides.Buy, order.Side.ToSide());
        AreEqual(100m, order.Quantity.ToDecimal());
        AreEqual(
            new DateTime(2025, 12, 24, 6, 19, 18, DateTimeKind.Utc),
            order.OrderTime.ToBigulTime());
        AreEqual(OrderStates.Done, "complete".ToOrderState());
        AreEqual(OrderStates.Failed, "rejected".ToOrderState());
    }

    private static byte[] BuildSnapshot()
    {
        var packet = new List<byte> { 0, 0, 6, 0, 0, 0, 1, 0, 1, 83, 1, 0 };
        AddString(packet, "sf|nse_cm|11536");
        var values = new[]
        {
            190565, 4241579, 1769767199, 1769767377,
            50, 80, 190570, 190700, 1, 5000, 6000,
            190124, 125, 187500, 192000, 210000, 160000,
            170000, 220000, 189000, 190000,
        };
        packet.Add((byte)values.Length);
        foreach (var value in values)
            AddInt32(packet, value);
        packet.AddRange([0, 0, 0, 1, 2]);
        AddString(packet, "nse_cm");
        AddString(packet, "11536");
        AddString(packet, "TCS-EQ");
        var result = packet.ToArray();
        BinaryPrimitives.WriteUInt16BigEndian(
            result.AsSpan(0, 2),
            checked((ushort)(result.Length - 2)));
        return result;
    }

    private static void AddString(List<byte> target, string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        target.Add(checked((byte)bytes.Length));
        target.AddRange(bytes);
    }

    private static void AddInt32(List<byte> target, int value)
    {
        var bytes = new byte[4];
        BinaryPrimitives.WriteInt32BigEndian(bytes, value);
        target.AddRange(bytes);
    }
}
