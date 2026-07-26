namespace StockSharp.Connectors.Tests;

using System;

using Ecng.Common;
using Ecng.Serialization;
using Ecng.UnitTesting;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using StockSharp.Messages;
using StockSharp.Tradernet;
using StockSharp.Tradernet.Native;
using StockSharp.Tradernet.Native.Model;

[TestClass]
public class TradernetTests : BaseTestClass
{
    [TestMethod]
    public void SettingsRoundTripKeepsConnectionOptions()
    {
        var source = new TradernetMessageAdapter(
            new IncrementalIdGenerator())
        {
            Key = "public-key".Secure(),
            Secret = "private-key".Secure(),
            Address = new("https://api.example.test/"),
            WebSocketAddress =
                new("wss://stream.example.test/"),
            PollingInterval = TimeSpan.FromSeconds(7),
            MaxMarketDepth = 42,
            SecuritiesPageSize = 250,
        };
        var storage = new SettingsStorage();
        source.Save(storage);

        var target = new TradernetMessageAdapter(
            new IncrementalIdGenerator());
        target.Load(storage);

        AreEqual("public-key", target.Key.UnSecure());
        AreEqual("private-key", target.Secret.UnSecure());
        AreEqual(source.Address, target.Address);
        AreEqual(source.WebSocketAddress,
            target.WebSocketAddress);
        AreEqual(source.PollingInterval,
            target.PollingInterval);
        AreEqual(source.MaxMarketDepth,
            target.MaxMarketDepth);
        AreEqual(source.SecuritiesPageSize,
            target.SecuritiesPageSize);
    }

    [TestMethod]
    public void OrderPayloadUsesOfficialSnakeCaseFields()
    {
        var json = TradernetRestClient.SerializeBody(
            new TradernetPlaceOrder
            {
                Ticker = "AAPL.US",
                Action = 1,
                OrderType = 2,
                Quantity = 10,
                LimitPrice = 205.75m,
                Expiration = 3,
                UserOrderId = 123,
            });

        IsTrue(json.Contains(
            "\"instr_name\":\"AAPL.US\"",
            StringComparison.Ordinal));
        IsTrue(json.Contains(
            "\"action_id\":1", StringComparison.Ordinal));
        IsTrue(json.Contains(
            "\"order_type_id\":2",
            StringComparison.Ordinal));
        IsTrue(json.Contains(
            "\"limit_price\":205.75",
            StringComparison.Ordinal));
        IsTrue(json.Contains(
            "\"user_order_id\":123",
            StringComparison.Ordinal));
        IsFalse(json.Contains(
            "\"stop_price\":", StringComparison.Ordinal));
        IsFalse(json.Contains(
            "\"Ticker\":", StringComparison.Ordinal));
    }

    [TestMethod]
    public void SignatureMatchesDocumentedHmacSha256Scheme()
    {
        AreEqual(
            "f556fc12a0d920e30468c0b7a055e3fac2691677e44b0279af12eb99b5574680",
            TradernetRestClient.CreateSignature(
                "{\"foo\":\"bar\"}", 1700000000,
                "private-secret"));
    }

    [TestMethod]
    public void NativeValuesMapToStockSharpTypes()
    {
        AreEqual(SecurityTypes.Stock,
            1.ToSecurityType(1));
        AreEqual(SecurityTypes.Fund,
            1.ToSecurityType(7));
        AreEqual(SecurityTypes.Future,
            3.ToSecurityType(0));
        AreEqual(SecurityTypes.Option,
            4.ToSecurityType(0));
        AreEqual(OrderStates.Active,
            12.ToOrderState());
        AreEqual(OrderStates.Done,
            21.ToOrderState());
        AreEqual(OrderStates.Failed,
            70.ToOrderState());
        AreEqual(60,
            TimeSpan.FromHours(1).ToNativeTimeFrame());
    }

    [TestMethod]
    public void NativeTickerAndNumbersPreserveValues()
    {
        var securityId = TradernetExtensions.ToSecurityId(
            "AAPL.US", "US0378331005");

        AreEqual("AAPL", securityId.SecurityCode);
        AreEqual("US", securityId.BoardCode);
        AreEqual("AAPL.US", securityId.Native);
        AreEqual("US0378331005", securityId.Isin);
        AreEqual("AAPL.US",
            securityId.ToNativeTicker());
        AreEqual(1234.56m, "1234,56".ToDecimal());
    }
}
