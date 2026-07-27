namespace StockSharp.Connectors.Tests;

using System;
using System.Buffers.Binary;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using Ecng.Common;
using Ecng.Serialization;
using Ecng.UnitTesting;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using Newtonsoft.Json.Linq;

using StockSharp.Mastertrust;
using StockSharp.Mastertrust.Native;
using StockSharp.Messages;

[TestClass]
public class MastertrustTests : BaseTestClass
{
    [TestMethod]
    public void SettingsRoundTripKeepsCredentialsAndEndpoints()
    {
        var source = new MastertrustMessageAdapter(new IncrementalIdGenerator())
        {
            ClientId = "091000006",
            OAuthClientId = "stocksharp",
            OAuthClientSecret = "client-secret".Secure(),
            AuthorizationCode = "authorization-code".Secure(),
            RedirectUri = new("https://app.example.test/callback"),
            Token = "access-token".Secure(),
            PortfolioName = "ACCOUNT1",
            DefaultProduct = MastertrustProducts.Intraday,
            ReconnectAttempts = 7,
            Address = new("https://api.example.test/"),
            MasterAddress = new("https://static.example.test/master.zip"),
            WebSocketAddress = new("wss://socket.example.test/ws"),
            PollingInterval = TimeSpan.FromSeconds(17),
        };
        var storage = new SettingsStorage();
        source.Save(storage);

        var target = new MastertrustMessageAdapter(new IncrementalIdGenerator());
        target.Load(storage);

        AreEqual(source.ClientId, target.ClientId);
        AreEqual(source.OAuthClientId, target.OAuthClientId);
        AreEqual(
            source.OAuthClientSecret.UnSecure(),
            target.OAuthClientSecret.UnSecure());
        AreEqual(
            source.AuthorizationCode.UnSecure(),
            target.AuthorizationCode.UnSecure());
        AreEqual(source.RedirectUri, target.RedirectUri);
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
    public async Task PublicMasterShapeMapsStockOptionAndFuture()
    {
        const string csv =
            "exchange_token,trading_symbol,company_name,close_price,expiry,strike,tick_size,lot_size,instrument_name,option_type,segment,exchange,fin_instrm_pdct_tp_cd,asset_code\n" +
            "14537,DISHTV-EQ,DISH TV INDIA LTD.,5.83,,0,0.01,1,EQ,,EQ,NSE,,DISHTV\n" +
            "111921,ICICIGI26SEP1120PE,\"ICICI Lombard, Ltd\",5.50,29-Sep-2026,1120,0.05,325,OPTSTK,PE,XX,NFO,,ICICIGI\n" +
            "441305,CRUDEOIL26JULFUT,CRUDE OIL,6000,20-Jul-2026,0,1,100,FUTCOM,XX,XX,MCX,,CRUDEOIL\n";
        await using var stream = new MemoryStream(Encoding.UTF8.GetBytes(csv));

        var instruments = await MastertrustRestClient.ParseInstrumentCsv(
            stream,
            CancellationToken.None);

        AreEqual(3, instruments.Length);
        AreEqual("NSE:14537", instruments[0].ToSecurityId().Native);
        AreEqual(SecurityTypes.Stock, instruments[0].ToSecurityType());
        AreEqual("ICICI Lombard, Ltd", instruments[1].CompanyName);
        AreEqual(SecurityTypes.Option, instruments[1].ToSecurityType());
        AreEqual(OptionTypes.Put, instruments[1].OptionType.ToOptionType());
        AreEqual(1120m, instruments[1].Strike);
        AreEqual(325m, instruments[1].LotSize);
        AreEqual(
            new DateTime(2026, 9, 28, 18, 30, 0, DateTimeKind.Utc),
            instruments[1].Expiry);
        AreEqual(SecurityTypes.Future, instruments[2].ToSecurityType());
    }

    [TestMethod]
    public async Task PublicMasterZipFindsCompactCsv()
    {
        const string csv =
            "exchange_token,trading_symbol,company_name,close_price,expiry,strike,tick_size,lot_size,instrument_name,option_type,segment,exchange,fin_instrm_pdct_tp_cd,asset_code\n" +
            "14537,DISHTV-EQ,DISH TV INDIA LTD.,5.83,,0,0.01,1,EQ,,EQ,NSE,,DISHTV\n";
        using var archiveStream = new MemoryStream();
        using (var archive = new ZipArchive(
            archiveStream,
            ZipArchiveMode.Create,
            true))
        {
            var entry = archive.CreateEntry("CompactScrip.csv");
            await using var entryStream = entry.Open();
            await using var writer = new StreamWriter(
                entryStream,
                new UTF8Encoding(false),
                1024,
                true);
            await writer.WriteAsync(csv);
        }
        archiveStream.Position = 0;

        var instruments = await MastertrustRestClient.ParseInstrumentArchive(
            archiveStream,
            CancellationToken.None);

        AreEqual(1, instruments.Length);
        AreEqual("NSE", instruments[0].Exchange);
        AreEqual("DISHTV-EQ", instruments[0].TradingSymbol);
    }

    [TestMethod]
    public void ResponseUnwrapsDataResultAndReportsApiError()
    {
        var profile = MastertrustRestClient.ParseResponse(
            """{"data":{"client_id":"A1"},"message":"","status":"success"}""",
            "profile");
        AreEqual("A1", profile.GetText("client_id"));

        var scrip = MastertrustRestClient.ParseResponse(
            """{"error":{"code":0,"message":""},"result":{"instrument_token":14537}}""",
            "scrip");
        AreEqual("14537", scrip.GetText("instrument_token"));

        ThrowsExactly<InvalidOperationException>(() =>
            MastertrustRestClient.ParseResponse(
                """{"status":"error","message":"bad request"}""",
                "orders"));
        ThrowsExactly<InvalidOperationException>(() =>
            MastertrustRestClient.ParseResponse(
                """{"error":{"code":42,"message":"bad token"}}""",
                "scrip"));
    }

    [TestMethod]
    public void CashPositionRowsMapAvailableAndMargins()
    {
        var fund = MastertrustRestClient.ParseFunds(JToken.Parse(
            """
            {
              "values":[
                ["Available","74.06"],
                ["Margin Used","1.93"],
                ["Cash Margin","75.99"],
                ["Collateral","2.00"],
                ["Pay In","3"],
                ["Pay Out","1"]
              ]
            }
            """));

        AreEqual(74.06m, fund.Available);
        AreEqual(1.93m, fund.MarginUsed);
        AreEqual(75.99m, fund.CashMargin);
        AreEqual(2m, fund.Collateral);
        AreEqual(3m, fund.PayIn);
        AreEqual(1m, fund.PayOut);
    }

    [TestMethod]
    public void NativeCodesAndQuantitiesFollowOfficialContract()
    {
        AreEqual((byte)1, "NSE".ToExchangeCode());
        AreEqual((byte)4, "MCX".ToExchangeCode());
        AreEqual("BFO", ((byte)7).ToExchange());
        AreEqual(100m, "MCX".FromNativeQuantity(1, 100));
        AreEqual(1L, "MCX".ToNativeQuantity(100, 100));
        AreEqual(75L, "NFO".ToNativeQuantity(75, 75));
        AreEqual(75m, "NFO".FromNativeQuantity(75, 75));
        AreEqual("NRML", MastertrustProducts.Normal.ToNative());
        AreEqual("MIS", MastertrustProducts.Intraday.ToNative());
        AreEqual("CNC", MastertrustProducts.Delivery.ToNative());
        AreEqual("SL", OrderTypes.Conditional.ToNative(100m));
        AreEqual("SLM", OrderTypes.Conditional.ToNative(0m));
        AreEqual("IOC", ((TimeInForce?)TimeInForce.CancelBalance).ToValidity());
        AreEqual(OrderStates.Active, "partially filled".ToOrderState());
        AreEqual(OrderStates.Pending, "cancel pending".ToOrderState());
        ThrowsExactly<ArgumentOutOfRangeException>(() =>
            "MCX".ToNativeQuantity(101, 100));
    }

    [TestMethod]
    public void OfficialDetailedPacketDecodesBigEndianQuote()
    {
        var packet = new byte[102];
        packet[0] = 1;
        packet[1] = 1;
        WriteInt32(packet, 2, 14537);
        WriteInt32(packet, 6, 588);
        WriteInt32(packet, 10, 1743137537);
        WriteInt32(packet, 14, 1);
        WriteInt32(packet, 18, 100);
        WriteInt32(packet, 22, 587);
        WriteInt32(packet, 26, 3);
        WriteInt32(packet, 30, 589);
        WriteInt32(packet, 34, 4);
        WriteInt64(packet, 38, 500);
        WriteInt64(packet, 46, 600);
        WriteInt32(packet, 54, 588);
        WriteInt32(packet, 58, 1743137538);
        WriteInt32(packet, 62, 583);
        WriteInt32(packet, 66, 590);
        WriteInt32(packet, 70, 580);
        WriteInt32(packet, 74, 583);
        WriteInt32(packet, 86, 466);
        WriteInt32(packet, 90, 699);
        WriteInt32(packet, 94, 42);

        var quote = MastertrustSocketClient.DecodeMarketData(packet);

        AreEqual("NSE:14537", quote.InstrumentKey);
        AreEqual(5.88m, quote.LastPrice);
        AreEqual(5.87m, quote.BestBidPrice);
        AreEqual(5.89m, quote.BestAskPrice);
        AreEqual(1m, quote.LastVolume);
        AreEqual(42m, quote.OpenInterest);
        AreEqual(4.66m, quote.LowerCircuit);
        AreEqual(6.99m, quote.UpperCircuit);
    }

    [TestMethod]
    public void OfficialSnapquotePacketDecodesFiveLevelDepth()
    {
        var packet = new byte[166];
        packet[0] = 4;
        packet[1] = 2;
        WriteInt32(packet, 2, 54452);
        WriteInt32(packet, 6, 2);
        WriteInt32(packet, 10, 3);
        WriteInt32(packet, 26, 2370025);
        WriteInt32(packet, 30, 2370000);
        WriteInt32(packet, 46, 75);
        WriteInt32(packet, 50, 150);
        WriteInt32(packet, 66, 4);
        WriteInt32(packet, 70, 5);
        WriteInt32(packet, 86, 2370050);
        WriteInt32(packet, 90, 2370075);
        WriteInt32(packet, 106, 225);
        WriteInt32(packet, 110, 300);
        WriteInt32(packet, 126, 2370030);
        WriteInt64(packet, 146, 1000);
        WriteInt64(packet, 154, 2000);
        WriteInt32(packet, 162, 5000);

        var quote = MastertrustSocketClient.DecodeMarketData(packet);

        AreEqual("NFO:54452", quote.InstrumentKey);
        IsTrue(quote.IsDepth);
        AreEqual(2, quote.Bids.Length);
        AreEqual(2, quote.Asks.Length);
        AreEqual(23700.25m, quote.Bids[0].Price);
        AreEqual(23700.50m, quote.Asks[0].Price);
        AreEqual(75m, quote.Bids[0].Volume);
        AreEqual(4, quote.Asks[0].OrdersCount);
    }

    [TestMethod]
    public void BinaryOrderUpdateUnwrapsDataPayload()
    {
        var packet = UpdatePacket(
            11,
            """
            {"data":{"oms_order_id":"250328000022151","client_id":"A1","exchange":"NSE","instrument_token":"14537","trading_symbol":"DISHTV-EQ","order_status":"open","order_side":"BUY","order_type":"LIMIT","product":"NRML","quantity":1,"remaining_quantity":1,"price":"5.50","validity":"DAY","user_order_id":"100"}}
            """);

        var update = MastertrustSocketClient.DecodeUpdate(packet);

        AreEqual(11, update.PacketCode);
        AreEqual("A1", update.ClientId);
        AreEqual("250328000022151", update.Order.OrderId);
        AreEqual("14537", update.Order.Token);
        AreEqual(5.50m, update.Order.Price);
        AreEqual("100", update.Order.UserOrderId);
    }

    [TestMethod]
    public void BinaryTradeUpdateUnwrapsNestedResult()
    {
        var packet = UpdatePacket(
            51,
            """
            {"result":{"trade":{"oms_order_id":"250328000022151","exchange":"NSE","instrument_token":14537,"trade_number":"202485785","trade_price":5.88,"trade_quantity":1,"trade_time":1743137537,"order_side":"BUY","trading_symbol":"DISHTV-EQ"}}}
            """);

        var update = MastertrustSocketClient.DecodeUpdate(packet);

        AreEqual(51, update.PacketCode);
        AreEqual("202485785", update.Trade.TradeId);
        AreEqual("14537", update.Trade.Token);
        AreEqual(5.88m, update.Trade.Price);
        AreEqual(1m, update.Trade.Quantity);
    }

    [TestMethod]
    public void WebSocketAcknowledgementAllowsExplicitFalseError()
    {
        MastertrustSocketClient.ProcessControlMessage(
            """{"status":"success","error":false}""");
        ThrowsExactly<InvalidOperationException>(() =>
            MastertrustSocketClient.ProcessControlMessage(
                """{"status":"error","error":true,"message":"bad token"}"""));
    }

    [TestMethod]
    public void TruncatedAndUnknownPacketsAreRejected()
    {
        ThrowsExactly<InvalidDataException>(() =>
            MastertrustSocketClient.DecodeMarketData([1, 1]));
        ThrowsExactly<InvalidDataException>(() =>
            MastertrustSocketClient.DecodeMarketData([99]));
        ThrowsExactly<InvalidDataException>(() =>
            MastertrustSocketClient.DecodeUpdate([11, 0, 0]));
    }

    private static byte[] UpdatePacket(byte code, string json)
    {
        var payload = Encoding.UTF8.GetBytes(json);
        var packet = new byte[5 + payload.Length + 1];
        packet[0] = code;
        BinaryPrimitives.WriteInt32BigEndian(
            packet.AsSpan(1, 4),
            payload.Length);
        payload.CopyTo(packet, 5);
        return packet;
    }

    private static void WriteInt32(byte[] data, int offset, int value)
        => BinaryPrimitives.WriteInt32BigEndian(
            data.AsSpan(offset, 4),
            value);

    private static void WriteInt64(byte[] data, int offset, long value)
        => BinaryPrimitives.WriteInt64BigEndian(
            data.AsSpan(offset, 8),
            value);
}
