using System.Collections;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.TestTools;

// UnityWebRequest against a local HttpListener (loopback is always allowed); other hosts are blocked unless MU_NETWORK=1 /
// MU_ALLOW_NETWORK, so the test never leaves the machine.
public class WebRequestTests
{
    HttpListener server; string baseUrl; Thread loop;

    [OneTimeSetUp] public void Serve()
    {
        var probe = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0); probe.Start();
        int port = ((IPEndPoint)probe.LocalEndpoint).Port; probe.Stop();
        baseUrl = $"http://127.0.0.1:{port}/";
        server = new HttpListener(); server.Prefixes.Add(baseUrl); server.Start();
        loop = new Thread(() =>
        {
            while (server.IsListening)
            {
                HttpListenerContext ctx;
                try { ctx = server.GetContext(); } catch { return; }
                var path = ctx.Request.Url.AbsolutePath;
                byte[] body;
                if (path == "/missing") { ctx.Response.StatusCode = 404; body = Encoding.UTF8.GetBytes("nope"); }
                else if (path == "/echo")
                {
                    using var r = new StreamReader(ctx.Request.InputStream);
                    body = Encoding.UTF8.GetBytes($"{ctx.Request.HttpMethod} {ctx.Request.Headers["X-Token"]} {ctx.Request.ContentType} {r.ReadToEnd()}");
                    ctx.Response.AddHeader("X-Reply", "yes");
                }
                else if (path == "/moved") { ctx.Response.StatusCode = 302; ctx.Response.RedirectLocation = baseUrl + "hello"; body = new byte[0]; }
                else body = Encoding.UTF8.GetBytes("hello world");
                ctx.Response.ContentType = "text/plain; charset=utf-8";
                ctx.Response.OutputStream.Write(body, 0, body.Length);
                ctx.Response.Close();
            }
        }) { IsBackground = true };
        loop.Start();
    }

    [OneTimeTearDown] public void Stop() { server.Stop(); server.Close(); }

    static void Wait(UnityWebRequestAsyncOperation op) { for (int i = 0; i < 1000 && !op.isDone; i++) Thread.Sleep(5); }

    [Test] public void GetReturnsBodyAndStatus()
    {
        using var req = UnityWebRequest.Get(baseUrl + "hello");
        var op = req.SendWebRequest(); Wait(op);
        Assert.AreEqual(UnityWebRequest.Result.Success, req.result, req.error);
        Assert.AreEqual(200, req.responseCode);
        Assert.AreEqual("hello world", req.downloadHandler.text);
        Assert.AreEqual(1f, op.progress);
    }

    [Test] public void PostSendsBodyHeadersAndReadsResponseHeaders()
    {
        using var req = new UnityWebRequest(baseUrl + "echo", "POST");
        req.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes("{\"a\":1}"));
        req.downloadHandler = new DownloadHandlerBuffer();
        req.SetRequestHeader("Content-Type", "application/json");
        req.SetRequestHeader("X-Token", "abc");
        Wait(req.SendWebRequest());
        Assert.AreEqual("POST abc application/json {\"a\":1}", req.downloadHandler.text);
        Assert.AreEqual("yes", req.GetResponseHeader("X-Reply"));
    }

    [Test] public void HttpErrorIsProtocolError()
    {
        using var req = UnityWebRequest.Get(baseUrl + "missing");
        Wait(req.SendWebRequest());
        Assert.AreEqual(UnityWebRequest.Result.ProtocolError, req.result);
        Assert.AreEqual(404, req.responseCode);
        StringAssert.StartsWith("HTTP/1.1 404", req.error);
        Assert.AreEqual("nope", req.downloadHandler.text, "the body of an error response is still downloaded");
    }

    [Test] public void RedirectsAreFollowed()
    {
        using var req = UnityWebRequest.Get(baseUrl + "moved");
        Wait(req.SendWebRequest());
        Assert.AreEqual("hello world", req.downloadHandler.text);
    }

    [Test] public void OtherHostsAreBlockedByDefault()
    {
        using var req = UnityWebRequest.Get("http://example.com/");
        var op = req.SendWebRequest(); Wait(op);
        Assert.IsTrue(op.isDone);
        Assert.AreEqual(UnityWebRequest.Result.ConnectionError, req.result);
        StringAssert.Contains("network disabled by MiniUnity", req.error);
    }

    [Test] public void CompletedFiresAndFileHandlersWork()
    {
        var path = Path.Combine(Path.GetTempPath(), "mu_web_" + System.Guid.NewGuid().ToString("N") + ".txt");
        using (var req = new UnityWebRequest(baseUrl + "hello", "GET", new DownloadHandlerFile(path), null))
        {
            int fired = 0;
            var op = req.SendWebRequest();
            op.completed += _ => fired++;
            Wait(op);
            if (!op.isDone) Assert.Fail("not done");
            Assert.AreEqual(1, fired);
            int late = 0; op.completed += _ => late++;
            Assert.AreEqual(1, late, "a handler added after completion is called at once");
        }
        Assert.AreEqual("hello world", File.ReadAllText(path));
        using var local = UnityWebRequest.Get(new System.Uri(path).AbsoluteUri);
        Wait(local.SendWebRequest());
        Assert.AreEqual("hello world", local.downloadHandler.text, "file:// URLs");
        File.Delete(path);
    }

    [UnityTest] public IEnumerator CoroutineYieldsUntilDone()
    {
        using var req = UnityWebRequest.Get(baseUrl + "hello");
        yield return req.SendWebRequest();
        Assert.IsTrue(req.isDone);
        Assert.AreEqual("hello world", req.downloadHandler.text);
    }
}
