namespace StockSharp.Connectors.Tests;

using System;

using Ecng.Common;
using Ecng.Serialization;
using Ecng.UnitTesting;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using StockSharp.Exante;
using StockSharp.Exante.Native;
using StockSharp.Exante.Native.Model;
using StockSharp.Messages;

[TestClass]
public class ExanteTests : BaseTestClass
{
    [TestMethod]
    public void SettingsRoundTripKeepsConnectionOptions()
    {
        var source = new ExanteMessageAdapter(
            new IncrementalIdGenerator())
        {
            Key = "api-key".Secure(),
            Secret = "api-secret".Secure(),
            IsDemo = false,
            SummaryCurrency = "USD",
            PollingInterval = TimeSpan.FromSeconds(9),
            MaxMarketDepth = 42,
            HistoryRequestSize = 777,
            LiveAddress = new("https://live.example.test/"),
            DemoAddress = new("https://demo.example.test/"),
        };
        var storage = new SettingsStorage();
        source.Save(storage);

        var target = new ExanteMessageAdapter(
            new IncrementalIdGenerator());
        target.Load(storage);

        AreEqual("api-key", target.Key.UnSecure());
        AreEqual("api-secret", target.Secret.UnSecure());
        AreEqual(source.IsDemo, target.IsDemo);
        AreEqual(source.SummaryCurrency, target.SummaryCurrency);
        AreEqual(source.PollingInterval, target.PollingInterval);
        AreEqual(source.MaxMarketDepth, target.MaxMarketDepth);
        AreEqual(source.HistoryRequestSize,
            target.HistoryRequestSize);
        AreEqual(source.LiveAddress, target.LiveAddress);
        AreEqual(source.DemoAddress, target.DemoAddress);
    }

    [TestMethod]
    public void OrderPayloadUsesOfficialCamelCaseFields()
    {
        var json = ExanteRestClient.SerializeBody(
            new ExantePlaceOrder
            {
                AccountId = "ACC.001",
                SymbolId = "AAPL.NASDAQ",
                Side = "buy",
                Quantity = "12.5",
                OrderType = "limit",
                LimitPrice = "205.75",
                Duration = "good_till_cancel",
                ClientTag = "123",
            });

        IsTrue(json.Contains(
            "\"accountId\":\"ACC.001\"",
            StringComparison.Ordinal));
        IsTrue(json.Contains(
            "\"symbolId\":\"AAPL.NASDAQ\"",
            StringComparison.Ordinal));
        IsTrue(json.Contains(
            "\"limitPrice\":\"205.75\"",
            StringComparison.Ordinal));
        IsFalse(json.Contains(
            "\"AccountId\":", StringComparison.Ordinal));
        IsFalse(json.Contains(
            "\"stopPrice\":", StringComparison.Ordinal));
    }

    [TestMethod]
    public void NativeValuesMapToStockSharpTypes()
    {
        AreEqual(SecurityTypes.Stock,
            "STOCK".ToSecurityType());
        AreEqual(SecurityTypes.Future,
            "CALENDAR_SPREAD".ToSecurityType());
        AreEqual(SecurityTypes.Currency,
            "FX_SPOT".ToSecurityType());
        AreEqual(OrderStates.Active,
            "working".ToOrderState());
        AreEqual(OrderStates.Done,
            "filled".ToOrderState());
        AreEqual(OrderStates.Failed,
            "rejected".ToOrderState());
        AreEqual(OrderTypes.Conditional,
            "stop_limit".ToOrderType());
    }

    [TestMethod]
    public void CandleTimeFramesUseDocumentedDurations()
    {
        AreEqual(60L,
            TimeSpan.FromMinutes(1).ToNativeDuration());
        AreEqual(3600L,
            TimeSpan.FromHours(1).ToNativeDuration());
        AreEqual(21600L,
            TimeSpan.FromHours(6).ToNativeDuration());
        AreEqual(86400L,
            TimeSpan.FromDays(1).ToNativeDuration());
        ThrowsExactly<ArgumentOutOfRangeException>(() =>
            TimeSpan.FromMinutes(2).ToNativeDuration());
    }

    [TestMethod]
    public void SymbolAndFillsPreserveNativeValues()
    {
        var symbol = new ExanteSymbol
        {
            SymbolId = "AAPL.NASDAQ",
            Ticker = "AAPL",
            Exchange = "NASDAQ",
            Identifiers = new()
            {
                Isin = "US0378331005",
            },
        };

        var securityId = symbol.ToSecurityId();
        AreEqual("AAPL", securityId.SecurityCode);
        AreEqual("NASDAQ", securityId.BoardCode);
        AreEqual("AAPL.NASDAQ", securityId.Native);
        AreEqual("US0378331005", securityId.Isin);
        AreEqual("AAPL.NASDAQ",
            securityId.ToNativeSymbol());

        var order = new ExanteOrder
        {
            OrderState = new()
            {
                Fills =
                [
                    new() { Quantity = "2", Price = "100" },
                    new() { Quantity = "1", Price = "103" },
                ],
            },
        };
        AreEqual(3m, order.GetExecutedVolume());
        AreEqual(101m, order.GetAveragePrice());
    }
}
