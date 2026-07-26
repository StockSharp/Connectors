namespace StockSharp.Connectors.Tests;

using System;

using Ecng.Common;
using Ecng.Serialization;
using Ecng.UnitTesting;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using StockSharp.Bcs;
using StockSharp.Bcs.Native;
using StockSharp.Bcs.Native.Model;
using StockSharp.Messages;

[TestClass]
public class BcsTests : BaseTestClass
{
    [TestMethod]
    public void SettingsRoundTripKeepsConnectionOptions()
    {
        var source = new BcsMessageAdapter(new IncrementalIdGenerator())
        {
            Token = "refresh-token".Secure(),
            IsReadOnly = true,
            PortfolioName = "test-account",
            PollingInterval = TimeSpan.FromSeconds(7),
            RestEndpoint = "https://api.example.test",
            WebSocketEndpoint = "wss://stream.example.test/market-data",
        };
        var storage = new SettingsStorage();
        source.Save(storage);

        var target = new BcsMessageAdapter(new IncrementalIdGenerator());
        target.Load(storage);

        AreEqual("refresh-token", target.Token.UnSecure());
        AreEqual(source.IsReadOnly, target.IsReadOnly);
        AreEqual(source.PortfolioName, target.PortfolioName);
        AreEqual(source.PollingInterval, target.PollingInterval);
        AreEqual(source.RestEndpoint, target.RestEndpoint);
        AreEqual(source.WebSocketEndpoint, target.WebSocketEndpoint);
    }

    [TestMethod]
    public void OrderRequestUsesOfficialPropertyNames()
    {
        var json = BcsRestClient.SerializeBody(new BcsCreateOrderRequest
        {
            ClientOrderId = "3fa85f64-5717-4562-b3fc-2c963f66afa6",
            Side = "1",
            OrderType = "2",
            OrderQuantity = 10,
            Ticker = "SBER",
            ClassCode = "TQBR",
            Price = 305.25m,
        });

        IsTrue(json.Contains("\"clientOrderId\":", StringComparison.Ordinal));
        IsTrue(json.Contains("\"orderQuantity\":10", StringComparison.Ordinal));
        IsTrue(json.Contains("\"classCode\":\"TQBR\"",
            StringComparison.Ordinal));
        IsFalse(json.Contains("\"ClientOrderId\":",
            StringComparison.Ordinal));
    }

    [TestMethod]
    public void OrderIdentifierTypeDistinguishesClientAndExchangeIds()
    {
        AreEqual("1", BcsRestClient.GetOrderIdType(
            "3fa85f64-5717-4562-b3fc-2c963f66afa6"));
        AreEqual("2", BcsRestClient.GetOrderIdType(
            "20260501-TQBR-79628540663"));
    }

    [TestMethod]
    public void NativeInstrumentTypesMapToStockSharpTypes()
    {
        AreEqual(SecurityTypes.Stock, "STOCK".ToSecurityType());
        AreEqual(SecurityTypes.Bond, "EURO_BONDS".ToSecurityType());
        AreEqual(SecurityTypes.Etf, "ETF".ToSecurityType());
        AreEqual(SecurityTypes.Future, "FUTURES".ToSecurityType());
        AreEqual(SecurityTypes.Option, "OPTIONS".ToSecurityType());
        AreEqual(SecurityTypes.Currency, "CURRENCY".ToSecurityType());
    }

    [TestMethod]
    public void CandleTimeFramesUseDocumentedCodes()
    {
        AreEqual("M1", TimeSpan.FromMinutes(1).ToNative());
        AreEqual("H4", TimeSpan.FromHours(4).ToNative());
        AreEqual("D", TimeSpan.FromDays(1).ToNative());
        AreEqual("MN", TimeSpan.FromDays(30).ToNative());
    }
}
