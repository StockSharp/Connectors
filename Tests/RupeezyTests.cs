namespace StockSharp.Connectors.Tests;

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using Ecng.Common;
using Ecng.Serialization;
using Ecng.UnitTesting;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using Newtonsoft.Json.Linq;

using StockSharp.Messages;
using StockSharp.Rupeezy;
using StockSharp.Rupeezy.Native;

[TestClass]
public class RupeezyTests : BaseTestClass
{
    [TestMethod]
    public void SettingsRoundTripKeepsCredentialsAndEndpoints()
    {
        var source = new RupeezyMessageAdapter(new IncrementalIdGenerator())
        {
            ApplicationId = "dev_1234",
            ApiKey = "api-key".Secure(),
            AuthCode = "auth-code".Secure(),
            Token = "access-token".Secure(),
            PortfolioName = "CLIENT1",
            DefaultProduct = RupeezyProducts.Intraday,
            ReconnectAttempts = 7,
            Address = new("https://api.example.test/v2/"),
            MasterAddress = new("https://static.example.test/master.csv"),
            WebSocketAddress = new("wss://socket.example.test/ws"),
            PollingInterval = TimeSpan.FromSeconds(17),
        };
        var storage = new SettingsStorage();
        source.Save(storage);

        var target = new RupeezyMessageAdapter(new IncrementalIdGenerator());
        target.Load(storage);

        AreEqual(source.ApplicationId, target.ApplicationId);
        AreEqual(source.ApiKey.UnSecure(), target.ApiKey.UnSecure());
        AreEqual(source.AuthCode.UnSecure(), target.AuthCode.UnSecure());
        AreEqual(source.Token.UnSecure(), target.Token.UnSecure());
        AreEqual(source.PortfolioName, target.PortfolioName);
        AreEqual(source.DefaultProduct, target.DefaultProduct);
        AreEqual(source.ReconnectAttempts, target.ReconnectAttempts);
        AreEqual(source.Address, target.Address);
        AreEqual(source.MasterAddress, target.MasterAddress);
        AreEqual(source.WebSocketAddress, target.WebSocketAddress);
        AreEqual(source.PollingInterval, target.PollingInterval);
    }

    [TestMethod]
    public void SsoChecksumFollowsOfficialConcatenationContract()
    {
        AreEqual(
            "ee12ce943bcc938731b0ea032eed9066208b15b0a850e5cc2f2eb5a2b557c0e3",
            RupeezyRestClient.CreateChecksum(
                "dev_1234",
                "authcode",
                "api-key"));
    }

    [TestMethod]
    public void ResponseUnwrapsDataAndReportsApiError()
    {
        var data = RupeezyRestClient.ParseResponse(
            """
			{
			  "status": "success",
			  "message": "Order placed successfully",
			  "data": {"order_id": "NXAAE0001851"}
			}
			""",
            "trading/orders/regular");

        AreEqual("NXAAE0001851", data.GetText("order_id"));
        ThrowsExactly<InvalidOperationException>(() =>
            RupeezyRestClient.ParseResponse(
                """
				{
				  "status": "error",
				  "code": "e-103",
				  "message": "Only AMO allowed"
				}
				""",
                "trading/orders/regular"));
    }

    [TestMethod]
    public async Task PublicMasterShapeMapsEquityAndOption()
    {
        var headers = new[]
        {
            "token", "exchange", "symbol", "instrument_name", "series",
            "expiry_date", "option_type", "strike_price", "tick", "lot_size",
            "eligibility", "security_desc", "asm_gsm_stage", "last_trading_date",
            "isin_code", "ticker",
        };
        var rows = new[]
        {
            headers,
            new[]
            {
                "22", "NSE_EQ", "ACC", "EQUITIES", "EQ", "", "", "", "5",
                "1", "1", "ACC LIMITED", "", "", "INE012A01025", "NSE:ACC",
            },
            new[]
            {
                "827787", "BSE_FO", "APLAPOLLO", "OPTSTK", "XX", "20260827",
                "CE", "1900", "5", "350", "1", "APLAPOLLO26AUG1900CE", "",
                "", "", "BSE:APLAPOLLO26AUG1900CE",
            },
        };
        var csv = string.Join(
            "\n",
            rows.Select(row => string.Join(",", row)));
        await using var stream = new MemoryStream(Encoding.UTF8.GetBytes(csv));

        var instruments = await RupeezyRestClient.ParseInstrumentCsv(
            stream,
            CancellationToken.None);
        var equity = instruments[0];
        var option = instruments[1];

        AreEqual("NSE_EQ:22", equity.ToSecurityId().Native);
        AreEqual("NSE", equity.ToSecurityId().BoardCode);
        AreEqual(SecurityTypes.Stock, equity.ToSecurityType());
        AreEqual(0.05m, equity.TickSize);
        AreEqual("INE012A01025", equity.Isin);
        AreEqual("BFO", option.Exchange.ToBoardCode());
        AreEqual(SecurityTypes.Option, option.ToSecurityType());
        AreEqual(OptionTypes.Call, option.OptionType.ToOptionType());
        AreEqual(1900m, option.StrikePrice);
        AreEqual(350m, option.LotSize);
        AreEqual(
            new DateTime(2026, 8, 26, 18, 30, 0, DateTimeKind.Utc),
            option.Expiry);
    }

    [TestMethod]
    public void HistoricalArraysMapToCandles()
    {
        var candles = RupeezyRestClient.ParseCandles(JObject.Parse(
            """
			{
			  "s": "ok",
			  "t": [1683540900, 1683537300],
			  "o": [1765.9, 1760.65],
			  "h": [1765.95, 1769.35],
			  "l": [1764.7, 1760.65],
			  "c": [1765.7, 1766.7],
			  "v": [850, 3625]
			}
			"""));

        AreEqual(2, candles.Length);
        AreEqual(1765.9m, candles[0].Open);
        AreEqual(1765.95m, candles[0].High);
        AreEqual(1764.7m, candles[0].Low);
        AreEqual(1765.7m, candles[0].Close);
        AreEqual(850m, candles[0].Volume);
        AreEqual(
            new DateTime(2023, 5, 8, 10, 15, 0, DateTimeKind.Utc),
            candles[0].Time);
    }

    [TestMethod]
    public void NativeCodesAndOrdersFollowPublicContract()
    {
        var key = new SecurityId
        {
            SecurityCode = "ACC",
            BoardCode = "NSE",
            Native = "NSE_EQ:22",
        }.ToInstrumentKey();

        AreEqual("NSE_EQ:22", key);
        AreEqual(("NSE_EQ", "22"), key.ParseInstrumentKey());
        AreEqual("BFO", "BSE_FO".ToBoardCode());
        AreEqual("CDS", "NSE_CUR".ToBoardCode());
        AreEqual("INTRADAY", RupeezyProducts.Intraday.ToNative());
        AreEqual("DELIVERY", RupeezyProducts.Delivery.ToNative());
        AreEqual("BTST", RupeezyProducts.Btst.ToNative());
        AreEqual("MTF", RupeezyProducts.Mtf.ToNative());
        AreEqual("RL", OrderTypes.Limit.ToVariety(100m));
        AreEqual("RL-MKT", OrderTypes.Market.ToVariety(0m));
        AreEqual("SL", OrderTypes.Conditional.ToVariety(100m));
        AreEqual("SL-MKT", OrderTypes.Conditional.ToVariety(0m));
        AreEqual("IOC", ((TimeInForce?)TimeInForce.CancelBalance).ToValidity());
        AreEqual(100L, "NSE_FO".ToNativeQuantity(100m, 50m));
        AreEqual(2L, "MCX_FO".ToNativeQuantity(60m, 30m));
        AreEqual(60m, "MCX_FO".FromNativeQuantity(2m, 30m));
        AreEqual(
            120m,
            "NSE_CUR".FromPositionQuantity(2m, 30m, 2m));
        ThrowsExactly<ArgumentOutOfRangeException>(() =>
            "MCX_FO".ToNativeQuantity(31m, 30m));
    }

    [TestMethod]
    public void OfficialFullPacketDecodesPricesAndDepth()
    {
        var ticks = RupeezySocketClient.Decode(BuildFullPacket());
        var tick = ticks.Single();

        AreEqual("NSE_EQ:22", tick.InstrumentKey);
        AreEqual(1740.95m, tick.LastPrice);
        AreEqual(407043m, tick.Volume);
        AreEqual(100m, tick.LastVolume);
        AreEqual(1736.52m, tick.AveragePrice);
        AreEqual(586m, tick.TotalBuyVolume);
        AreEqual(700m, tick.TotalSellVolume);
        AreEqual(1740.95m, tick.Bids[0].Price);
        AreEqual(1741.05m, tick.Asks[0].Price);
        AreEqual(1883.2m, tick.UpperCircuit);
        AreEqual(1540.8m, tick.LowerCircuit);
    }

    [TestMethod]
    public void OfficialLtpPacketDecodesMinimalQuote()
    {
        var data = new byte[26];
        BinaryPrimitives.WriteUInt16LittleEndian(data, 1);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(2), 22);
        WriteExchange(data.AsSpan(4, 10), "NSE_EQ");
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(14), 26000);
        WriteDouble(data.AsSpan(18), 24550.75);

        var tick = RupeezySocketClient.Decode(data).Single();
        AreEqual("NSE_EQ:26000", tick.InstrumentKey);
        AreEqual(24550.75m, tick.LastPrice);
        AreEqual(0, tick.Bids.Length);
    }

    [TestMethod]
    public void TextTradePostbackMapsOrderAndFill()
    {
        var update = RupeezySocketClient.ParseText(
            """
			{
			  "type": "trade",
			  "client_code": "DEMO",
			  "data": {
			    "order_id": "NXAAE0001AC4",
			    "status": "COMPLETED",
			    "exchange": "NSE_EQ",
			    "token": 1660,
			    "symbol": "ITC",
			    "transaction_type": "BUY",
			    "product": "INTRADAY",
			    "variety": "RL",
			    "total_quantity": 1,
			    "pending_quantity": 0,
			    "traded_quantity": 1,
			    "traded_price": 400.20,
			    "trade_number": "27511919",
			    "trade_time": "19-Apr-2023 12.32.59",
			    "order_identifier": "42"
			  }
			}
			""");

        AreEqual("trade", update.Type);
        AreEqual("DEMO", update.ClientCode);
        AreEqual(OrderStates.Done, update.Order.Status.ToOrderState());
        AreEqual(1m, update.Order.TradedQuantity);
        AreEqual("27511919", update.Trade.TradeId);
        AreEqual(400.20m, update.Trade.Price);
        AreEqual(0m, update.Trade.Quantity);
        AreEqual(1m, update.Trade.CumulativeQuantity);
        AreEqual(
            new DateTime(2023, 4, 19, 7, 2, 59, DateTimeKind.Utc),
            update.Trade.TradedAt.ToRupeezyTime());
    }

    private static byte[] BuildFullPacket()
    {
        var data = new byte[270];
        BinaryPrimitives.WriteUInt16LittleEndian(data, 1);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(2), 266);
        var packet = data.AsSpan(4);
        WriteExchange(packet[..10], "NSE_EQ");
        BinaryPrimitives.WriteInt32LittleEndian(packet[10..], 22);
        WriteDouble(packet[14..], 1740.95);
        BinaryPrimitives.WriteInt32LittleEndian(packet[22..], 1681122520);
        WriteDouble(packet[26..], 1718);
        WriteDouble(packet[34..], 1915);
        WriteDouble(packet[42..], 1566.85);
        WriteDouble(packet[50..], 1712);
        BinaryPrimitives.WriteInt32LittleEndian(packet[58..], 407043);
        BinaryPrimitives.WriteInt32LittleEndian(packet[62..], 1681122520);
        BinaryPrimitives.WriteInt32LittleEndian(packet[66..], 100);
        WriteDouble(packet[70..], 1736.52);
        BinaryPrimitives.WriteInt64LittleEndian(packet[78..], 586);
        BinaryPrimitives.WriteInt64LittleEndian(packet[86..], 700);
        BinaryPrimitives.WriteInt32LittleEndian(packet[94..], 1250);

        var offset = 98;
        for (var index = 0; index < 5; index++)
        {
            WriteDepth(
                packet[offset..],
                1740.95 - index * 0.05,
                586 + index,
                index + 1);
            offset += 16;
        }
        for (var index = 0; index < 5; index++)
        {
            WriteDepth(
                packet[offset..],
                1741.05 + index * 0.05,
                700 + index,
                index + 1);
            offset += 16;
        }
        BinaryPrimitives.WriteInt32LittleEndian(packet[258..], 188320);
        BinaryPrimitives.WriteInt32LittleEndian(packet[262..], 154080);
        return data;
    }

    private static void WriteExchange(Span<byte> target, string exchange)
    {
        target.Clear();
        Encoding.ASCII.GetBytes(exchange).CopyTo(target);
    }

    private static void WriteDepth(
        Span<byte> target,
        double price,
        int quantity,
        int orders)
    {
        WriteDouble(target, price);
        BinaryPrimitives.WriteInt32LittleEndian(target[8..], quantity);
        BinaryPrimitives.WriteInt32LittleEndian(target[12..], orders);
    }

    private static void WriteDouble(Span<byte> target, double value)
        => BinaryPrimitives.WriteInt64LittleEndian(
            target,
            BitConverter.DoubleToInt64Bits(value));
}
