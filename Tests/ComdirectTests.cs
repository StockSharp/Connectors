namespace StockSharp.Connectors.Tests;

using System;

using Ecng.Common;
using Ecng.Serialization;
using Ecng.UnitTesting;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using StockSharp.Comdirect;
using StockSharp.Comdirect.Native;
using StockSharp.Comdirect.Native.Model;
using StockSharp.Messages;

[TestClass]
public class ComdirectTests : BaseTestClass
{
    [TestMethod]
    public void SettingsRoundTripKeepsConnectionOptions()
    {
        var source = new ComdirectMessageAdapter(
            new IncrementalIdGenerator())
        {
            Key = "client-id".Secure(),
            Secret = "client-secret".Secure(),
            Login = "12345678",
            Password = "123456".Secure(),
            TanType = ComdirectTanTypes.MobileTan,
            PollingInterval = TimeSpan.FromSeconds(7),
            DefaultCurrency = "CHF",
            Address = new("https://api.example.test/"),
        };
        var storage = new SettingsStorage();
        source.Save(storage);

        var target = new ComdirectMessageAdapter(
            new IncrementalIdGenerator());
        target.Load(storage);

        AreEqual("client-id", target.Key.UnSecure());
        AreEqual("client-secret", target.Secret.UnSecure());
        AreEqual(source.Login, target.Login);
        AreEqual("123456", target.Password.UnSecure());
        AreEqual(source.TanType, target.TanType);
        AreEqual(source.PollingInterval, target.PollingInterval);
        AreEqual(source.DefaultCurrency, target.DefaultCurrency);
        AreEqual(source.Address, target.Address);
    }

    [TestMethod]
    public void OrderPayloadUsesOfficialCamelCaseFields()
    {
        var json = ComdirectRestClient.SerializeBody(new ComdirectOrder
        {
            DepotId = "depot-1",
            OrderType = "LIMIT",
            Side = "BUY",
            InstrumentId = "DE0008404005",
            Quantity = 12.5m.ToNativeAmount("XXX"),
            Limit = 234.56m.ToNativeAmount("EUR"),
            ValidityType = "GFD",
        });

        IsTrue(json.Contains(
            "\"depotId\":\"depot-1\"", StringComparison.Ordinal));
        IsTrue(json.Contains(
            "\"instrumentId\":\"DE0008404005\"",
            StringComparison.Ordinal));
        IsTrue(json.Contains(
            "\"quantity\":{\"value\":\"12.5\",\"unit\":\"XXX\"}",
            StringComparison.Ordinal));
        IsFalse(json.Contains("\"DepotId\":", StringComparison.Ordinal));
    }

    [TestMethod]
    public void RequestInfoUsesNineDigitRequestIdentifier()
    {
        var json = ComdirectRestClient.SerializeRequestInfo(
            "stocksharp-session", 123456789);

        IsTrue(json.Contains(
            "\"sessionId\":\"stocksharp-session\"",
            StringComparison.Ordinal));
        IsTrue(json.Contains(
            "\"requestId\":\"123456789\"",
            StringComparison.Ordinal));
    }

    [TestMethod]
    public void TanProceduresUseDocumentedCodes()
    {
        IsNull(ComdirectTanTypes.Preferred.ToNative());
        AreEqual("P_TAN", ComdirectTanTypes.PhotoTan.ToNative());
        AreEqual("M_TAN", ComdirectTanTypes.MobileTan.ToNative());
        AreEqual("P_TAN_PUSH",
            ComdirectTanTypes.PhotoTanPush.ToNative());
    }

    [TestMethod]
    public void NativeValuesMapToStockSharpTypes()
    {
        AreEqual(SecurityTypes.Stock, "SHARE".ToSecurityType());
        AreEqual(SecurityTypes.Bond, "BONDS".ToSecurityType());
        AreEqual(SecurityTypes.Etf, "ETF".ToSecurityType());
        AreEqual(SecurityTypes.Warrant,
            "CERTIFICATE".ToSecurityType());
        AreEqual(OrderStates.Active,
            "PARTIALLY_EXECUTED".ToOrderState());
        AreEqual(OrderStates.Done,
            "CANCELLED_USER".ToOrderState());
        AreEqual(OrderStates.Failed,
            "CANCELLED_SYSTEM".ToOrderState());
    }
}
