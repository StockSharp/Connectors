namespace StockSharp.Connectors.Tests;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using Ecng.Common;
using Ecng.ComponentModel;
using Ecng.Serialization;
using Ecng.UnitTesting;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using Newtonsoft.Json.Linq;

using StockSharp.Messages;
using StockSharp.Noren;
using StockSharp.Zebu;
using StockSharp.Zebu.Native;

[TestClass]
public class ZebuTests : BaseTestClass
{
    [TestMethod]
    public void SettingsRoundTripKeepsOAuthAndNorenConfiguration()
    {
        var expires = new DateTime(
            2026,
            7,
            26,
            12,
            0,
            0,
            DateTimeKind.Utc);
        var source = new ZebuMessageAdapter(new IncrementalIdGenerator())
        {
            Key = "CLIENT-ID".Secure(),
            Secret = "CLIENT-SECRET".Secure(),
            AuthorizationCode = "AUTH-CODE".Secure(),
            RefreshToken = "REFRESH".Secure(),
            Token = "ACCESS".Secure(),
            UserId = "ZP001",
            AccountId = "ZP001-A",
            TokenExpiresAt = expires,
            DefaultProduct = NorenProducts.Normal,
            ReconnectAttempts = 7,
            AuthorizationAddress =
                new("https://oauth.example.test/authorize"),
            RestEndpoint = "https://rest.example.test/",
            InstrumentEndpointTemplate =
                "https://static.example.test/{0}.zip",
            WebSocketEndpoint = "wss://stream.example.test/",
        };
        var storage = new SettingsStorage();
        source.Save(storage);

        var target = new ZebuMessageAdapter(new IncrementalIdGenerator());
        target.Load(storage);

        AreEqual(source.Key.UnSecure(), target.Key.UnSecure());
        AreEqual(source.Secret.UnSecure(), target.Secret.UnSecure());
        AreEqual(
            source.AuthorizationCode.UnSecure(),
            target.AuthorizationCode.UnSecure());
        AreEqual(
            source.RefreshToken.UnSecure(),
            target.RefreshToken.UnSecure());
        AreEqual(source.Token.UnSecure(), target.Token.UnSecure());
        AreEqual(source.UserId, target.UserId);
        AreEqual(source.AccountId, target.AccountId);
        AreEqual(source.TokenExpiresAt, target.TokenExpiresAt);
        AreEqual(source.DefaultProduct, target.DefaultProduct);
        AreEqual(source.ReconnectAttempts, target.ReconnectAttempts);
        AreEqual(
            source.AuthorizationAddress,
            target.AuthorizationAddress);
        AreEqual(source.RestEndpoint, target.RestEndpoint);
        AreEqual(
            source.InstrumentEndpointTemplate,
            target.InstrumentEndpointTemplate);
        AreEqual(source.WebSocketEndpoint, target.WebSocketEndpoint);
        IsTrue(target is IKeySecretAdapter);
        IsTrue(target is ITokenAdapter);
    }

    [TestMethod]
    public void DefaultsAndAuthorizationUriUseOfficialOAuthEndpoints()
    {
        var adapter = new ZebuMessageAdapter(new IncrementalIdGenerator())
        {
            Key = "ZP00 1/U".Secure(),
        };

        AreEqual(
            "https://go.mynt.in/NorenWClientAPI/",
            adapter.RestEndpoint);
        AreEqual(
            "https://go.mynt.in/{0}_symbols.txt.zip",
            adapter.InstrumentEndpointTemplate);
        AreEqual(
            "wss://go.mynt.in/NorenWSAPI/",
            adapter.WebSocketEndpoint);
        AreEqual(
            "https://go.mynt.in/OAuthlogin/authorize/oauth?client_id=ZP00%201%2FU",
            adapter.CreateAuthorizationUri().AbsoluteUri);
        IsTrue(ZebuMessageAdapter.AllTimeFrames.Any());
    }

    [TestMethod]
    public async Task AuthorizationCodeExchangeUsesPublishedChecksum()
    {
        var handler = new CaptureHandler(
            new ResponseSpec(
                """
				{
				  "stat":"Ok",
				  "access_token":"ACCESS",
				  "refresh_token":"REFRESH",
				  "expires_in":"3600",
				  "uid":"ZP001",
				  "actid":"ZP001-A"
				}
				"""));
        using var client = new ZebuOAuthClient(
            new("https://go.example.test/NorenWClientAPI/"),
            handler);

        var result = await client.ExchangeCode(
            "ABC".Secure(),
            "123".Secure(),
            "x1y2z3".Secure(),
            CancellationToken.None);

        AreEqual(
            "7b482d7b380a3067eaba4c9c909b19253c4fa0edb5833e246401b8497c99a9c3",
            ZebuOAuthClient.ComputeChecksum("ABC", "123", "x1y2z3"));
        AreEqual("ACCESS", result.AccessToken);
        AreEqual("REFRESH", result.RefreshToken);
        AreEqual("ZP001", result.UserId);
        AreEqual("ZP001-A", result.AccountId);
        AreEqual(3600, result.ExpiresIn);

        var request = handler.Requests.Single();
        AreEqual(
            "https://go.example.test/NorenWClientAPI/GenAcsTok",
            request.Uri.AbsoluteUri);
        AreEqual("text/plain; charset=utf-8", request.ContentType);
        IsNull(request.Authorization);
        var body = JObject.Parse(request.Body["jData=".Length..]);
        AreEqual("x1y2z3", body["code"].Value<string>());
        AreEqual(
            "7b482d7b380a3067eaba4c9c909b19253c4fa0edb5833e246401b8497c99a9c3",
            body["checksum"].Value<string>());
    }

    [TestMethod]
    public async Task RefreshAcceptsCurrentSessionTokenAlias()
    {
        var handler = new CaptureHandler(
            new ResponseSpec(
                """
				{"stat":"Ok","susertoken":"ACCESS-2",
				 "refresh_token":"REFRESH-2","expires_in":"1800","uid":"ZP002"}
				"""));
        using var client = new ZebuOAuthClient(
            new("https://go.example.test/NorenWClientAPI/"),
            handler);

        var result = await client.Refresh(
            "REFRESH-1".Secure(),
            CancellationToken.None);

        AreEqual("ACCESS-2", result.AccessToken);
        AreEqual("REFRESH-2", result.RefreshToken);
        AreEqual("ZP002", result.AccountId);
        AreEqual(
            "https://go.example.test/NorenWClientAPI/RefreshToken",
            handler.Requests[0].Uri.AbsoluteUri);
        AreEqual(
            "REFRESH-1",
            JObject.Parse(
                handler.Requests[0].Body["jData=".Length..])
                ["refresh_token"]
                .Value<string>());
    }

    [TestMethod]
    public void OAuthErrorsAndInvalidJsonAreRejected()
    {
        ThrowsExactly<InvalidOperationException>(() =>
            ZebuOAuthClient.ParseToken(
                "GenAcsTok",
                """{"stat":"Not_Ok","emsg":"Invalid authorization code"}"""));
        ThrowsExactly<InvalidDataException>(() =>
            ZebuOAuthClient.ParseToken("GenAcsTok", "<html>"));
        ThrowsExactly<InvalidOperationException>(() =>
            ZebuOAuthClient.ParseToken(
                "GenAcsTok",
                """{"stat":"Ok","uid":"ZP001"}"""));
    }

    private sealed record ResponseSpec(
        string Json,
        HttpStatusCode StatusCode = HttpStatusCode.OK);

    private sealed record CapturedRequest(
        Uri Uri,
        HttpMethod Method,
        string Authorization,
        string ContentType,
        string Body);

    private sealed class CaptureHandler : HttpMessageHandler
    {
        private readonly Queue<ResponseSpec> _responses;

        public CaptureHandler(params ResponseSpec[] responses)
        {
            _responses = new(responses);
        }

        public List<CapturedRequest> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Requests.Add(new(
                request.RequestUri,
                request.Method,
                request.Headers.Authorization?.ToString(),
                request.Content?.Headers.ContentType?.ToString(),
                request.Content == null
                    ? null
                    : await request.Content.ReadAsStringAsync(
                        cancellationToken)));
            if (_responses.Count == 0)
            {
                throw new InvalidOperationException(
                    "No fake Zebu response was configured.");
            }
            var spec = _responses.Dequeue();
            return new(spec.StatusCode)
            {
                Content = new StringContent(
                    spec.Json,
                    Encoding.UTF8,
                    "application/json"),
            };
        }
    }
}
