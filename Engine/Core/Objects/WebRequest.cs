// UnityWebRequest on HttpClient (and file:// URLs read from disk), with AsyncOperation completion like Unity's.
// Network access is OFF by default: a request to another machine fails with ConnectionError "network disabled by MiniUnity"
// unless MU_NETWORK=1 or its host is listed in MU_ALLOW_NETWORK (comma separated; `ua … --allow-network h1,h2` sets it).
// Loopback (localhost / 127.0.0.1 / ::1) and file:// are always allowed. In Play mode the request runs in the background and
// completes on the player loop (WebRequests.Pump, before coroutines resume); outside Play mode (editor commands, menu items)
// SendWebRequest runs it to completion before returning, so `completed` handlers added afterwards fire at once — the
// AsyncOperation contract.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MiniUnity;

namespace UnityEngine
{
    public partial class AsyncOperation
    {
        internal bool m_Done;
        internal float m_Progress;
        /// <summary>Lets an operation finish itself when polled (UnityWebRequest's background task).</summary>
        internal Action m_Poll;
        Action<AsyncOperation> m_Completed;
        int m_Priority; bool m_AllowSceneActivation = true;

        public AsyncOperation() { }
        public bool isDone { get { if (!m_Done) m_Poll?.Invoke(); return m_Done; } }
        public float progress { get { if (!m_Done) m_Poll?.Invoke(); return m_Done ? 1 : m_Progress; } }
        public int priority { get => m_Priority; set => m_Priority = value; }
        public bool allowSceneActivation { get => m_AllowSceneActivation; set => m_AllowSceneActivation = value; }
        /// <summary>Like Unity: a handler added after completion is called immediately.</summary>
        public event Action<AsyncOperation> completed
        {
            add { if (m_Done) value?.Invoke(this); else m_Completed += value; }
            remove => m_Completed -= value;
        }
        internal void InvokeCompletionEvent()
        {
            if (m_Done) return;
            m_Done = true; m_Progress = 1;
            var c = m_Completed; m_Completed = null;
            try { c?.Invoke(this); } catch (Exception e) { Debug.LogException(e); }
        }
    }
}

namespace UnityEngine.Networking
{
    public partial class CertificateHandler
    {
        protected CertificateHandler() { }
        protected virtual bool ValidateCertificate(byte[] certificateData) => false;
        public void Dispose() { }
    }

    public partial class DownloadHandler
    {
        internal bool m_Done; internal string m_Error;
        internal DownloadHandler() { }
        public virtual void Dispose() { }
        protected virtual Unity.Collections.NativeArray<byte> GetNativeData() => default;
        protected virtual byte[] GetData() => null;
        protected virtual string GetText()
        {
            var d = GetData();
            return d == null ? null : DecodeText(d);
        }
        internal string m_Charset;
        internal string DecodeText(byte[] d)
        {
            Encoding enc = Encoding.UTF8;
            if (!string.IsNullOrEmpty(m_Charset)) try { enc = Encoding.GetEncoding(m_Charset); } catch (ArgumentException) { }
            return enc.GetString(d);
        }
        protected virtual bool ReceiveData(byte[] data, int dataLength) => true;
        protected virtual void ReceiveContentLengthHeader(ulong contentLength) => ReceiveContentLength((int)Math.Min(int.MaxValue, contentLength));
        protected virtual void ReceiveContentLength(int contentLength) { }
        protected virtual void CompleteContent() { }
        protected virtual float GetProgress() => 0;
        protected static T GetCheckedDownloader<T>(UnityWebRequest www) where T : DownloadHandler
        {
            if (www == null) throw new NullReferenceException("Cannot get content from a null UnityWebRequest object");
            if (!www.isDone) throw new InvalidOperationException("Cannot get content from an unfinished UnityWebRequest object");
            if (www.result == UnityWebRequest.Result.ProtocolError) throw new InvalidOperationException(www.error);
            return www.downloadHandler as T ?? throw new InvalidOperationException("Expected a " + typeof(T).Name);
        }
        public bool isDone => m_Done;
        public string error => m_Error;
        public Unity.Collections.NativeArray<byte>.ReadOnly nativeData => default;
        public byte[] data => GetData();
        public string text => GetText();

        // called by the request on the main thread
        internal void Deliver(byte[] body, ulong contentLength)
        {
            ReceiveContentLengthHeader(contentLength);
            if (body != null && body.Length > 0) ReceiveData(body, body.Length);
            m_Done = true;
            CompleteContent();
        }
        internal float Progress => GetProgress();
    }

    public sealed partial class DownloadHandlerBuffer
    {
        readonly MemoryStream m_Buffer = new();
        public DownloadHandlerBuffer() { }
        protected override Unity.Collections.NativeArray<byte> GetNativeData() => default;
        protected override byte[] GetData() => m_Buffer.ToArray();
        protected override bool ReceiveData(byte[] data, int dataLength) { m_Buffer.Write(data, 0, dataLength); return true; }
        protected override float GetProgress() => m_Done ? 1 : 0;
        public override void Dispose() { }
        public static string GetContent(UnityWebRequest www) => GetCheckedDownloader<DownloadHandlerBuffer>(www).text;
    }

    public partial class DownloadHandlerScript
    {
        public DownloadHandlerScript() { }
        public DownloadHandlerScript(byte[] preallocatedBuffer) { }
    }

    public sealed partial class DownloadHandlerFile
    {
        readonly string m_Path; readonly bool m_Append;
        public DownloadHandlerFile(string path) : this(path, false) { }
        public DownloadHandlerFile(string path, bool append) { m_Path = path; m_Append = append; }
        public bool removeFileOnAbort { get; set; }
        protected override Unity.Collections.NativeArray<byte> GetNativeData() => throw new NotSupportedException("Raw data access is not supported");
        protected override byte[] GetData() => throw new NotSupportedException("Raw data access is not supported");
        protected override string GetText() => throw new NotSupportedException("String access is not supported");
        protected override bool ReceiveData(byte[] data, int dataLength)
        {
            var dir = Path.GetDirectoryName(Path.GetFullPath(m_Path));
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            using var f = new FileStream(m_Path, m_Append ? FileMode.Append : FileMode.Create, FileAccess.Write);
            f.Write(data, 0, dataLength);
            return true;
        }
        internal void Abort() { if (removeFileOnAbort && File.Exists(m_Path)) File.Delete(m_Path); }
    }

    public partial class UploadHandler
    {
        internal byte[] m_Data; internal string m_ContentType;
        internal UploadHandler() { }
        public virtual void Dispose() { }
        public byte[] data => m_Data;
        public string contentType { get => m_ContentType; set => m_ContentType = value; }
        public float progress => 0;
    }

    public sealed partial class UploadHandlerRaw
    {
        public UploadHandlerRaw(byte[] data) { m_Data = data; }
        public UploadHandlerRaw(Unity.Collections.NativeArray<byte> data, bool transferOwnership) { m_Data = data.ToArray(); }
        public UploadHandlerRaw(Unity.Collections.NativeArray<byte>.ReadOnly data) { m_Data = data.ToArray(); }
        public override void Dispose() { }
    }

    public sealed partial class UploadHandlerFile
    {
        public UploadHandlerFile(string filePath) { m_Data = File.ReadAllBytes(filePath); m_ContentType = "application/octet-stream"; }
    }

    public partial class UnityWebRequestAsyncOperation
    {
        internal UnityWebRequest m_Request;
        public UnityWebRequestAsyncOperation() { }
        public UnityWebRequest webRequest { get => m_Request; internal set => m_Request = value; }
    }

    public partial class UnityWebRequest
    {
        string m_Url, m_Method = kHttpVerbGET;
        readonly Dictionary<string, string> m_Headers = new(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, string> m_ResponseHeaders;
        UnityWebRequestAsyncOperation m_Op;
        Task<WebRequests.Response> m_Task;
        CancellationTokenSource m_Cancel;
        bool m_Sent, m_Done, m_Disposed;
        string m_Error; long m_ResponseCode; Result m_Result = Result.InProgress;
        ulong m_Uploaded, m_Downloaded;
        int m_Timeout, m_RedirectLimit = 32;
        DownloadHandler m_Download; UploadHandler m_Upload; CertificateHandler m_Cert;

        public UnityWebRequest() { }
        public UnityWebRequest(string url) { m_Url = url; }
        public UnityWebRequest(Uri uri) { m_Url = uri?.AbsoluteUri; }
        public UnityWebRequest(string url, string method) { m_Url = url; this.method = method; }
        public UnityWebRequest(Uri uri, string method) : this(uri?.AbsoluteUri, method) { }
        public UnityWebRequest(string url, string method, DownloadHandler downloadHandler, UploadHandler uploadHandler)
        { m_Url = url; this.method = method; m_Download = downloadHandler; m_Upload = uploadHandler; }
        public UnityWebRequest(Uri uri, string method, DownloadHandler downloadHandler, UploadHandler uploadHandler)
            : this(uri?.AbsoluteUri, method, downloadHandler, uploadHandler) { }

        public void Dispose()
        {
            if (m_Disposed) return;
            m_Disposed = true;
            if (!m_Done) Abort();
            if (disposeDownloadHandlerOnDispose) m_Download?.Dispose();
            if (disposeUploadHandlerOnDispose) m_Upload?.Dispose();
            if (disposeCertificateHandlerOnDispose) m_Cert?.Dispose();
        }

        public static void ClearCookieCache() { }
        public static void ClearCookieCache(Uri uri) { }

        public AsyncOperation Send() => SendWebRequest();
        public UnityWebRequestAsyncOperation SendWebRequest()
        {
            if (m_Sent) throw new InvalidOperationException("UnityWebRequest has already been sent; cannot begin sending the request again");
            m_Sent = true;
            m_Op = new UnityWebRequestAsyncOperation { webRequest = this };
            m_Op.m_Poll = Poll;
            if (WebRequests.Blocked(m_Url, out var why)) { Finish(new WebRequests.Response { error = why, result = Result.ConnectionError }); return m_Op; }
            m_Cancel = new CancellationTokenSource();
            if (m_Timeout > 0) m_Cancel.CancelAfter(TimeSpan.FromSeconds(m_Timeout));
            var body = m_Upload?.data; var contentType = m_Upload?.contentType;
            var headers = new Dictionary<string, string>(m_Headers);
            var url = m_Url; var method = m_Method; var redirects = m_RedirectLimit; var ct = m_Cancel.Token;
            // off the play-mode SynchronizationContext: the transfer's continuations must not wait for the player loop
            m_Task = Task.Run(() => WebRequests.Run(url, method, headers, body, contentType, redirects, ct));
            if (MiniUnity.Play.IsPlaying) WebRequests.Track(this);
            else Poll(block: true);   // no player loop: finish now (completed handlers added later fire immediately)
            return m_Op;
        }

        void Poll() => Poll(false);
        internal void Poll(bool block)
        {
            if (m_Done || m_Task == null) return;
            if (!m_Task.IsCompleted)
            {
                if (!block) return;
                try { m_Task.Wait(); } catch (AggregateException) { }
            }
            WebRequests.Response r;
            try { r = m_Task.Result; }
            catch (Exception e) { r = new WebRequests.Response { error = (e.InnerException ?? e).Message, result = Result.ConnectionError }; }
            if (r.result == Result.ConnectionError && m_Cancel?.IsCancellationRequested == true && r.error == null) r.error = m_Aborted ? "Request aborted" : "Request timeout";
            Finish(r);
        }

        bool m_Aborted;
        void Finish(WebRequests.Response r)
        {
            if (m_Done) return;
            m_Done = true;
            m_ResponseCode = r.code; m_ResponseHeaders = r.headers; m_Error = r.error; m_Result = r.result;
            m_Uploaded = (ulong)(m_Upload?.data?.Length ?? 0); m_Downloaded = (ulong)(r.body?.Length ?? 0);
            if (m_Download != null)
            {
                if (r.headers != null && r.headers.TryGetValue("Content-Type", out var ct))
                {
                    int i = ct.IndexOf("charset=", StringComparison.OrdinalIgnoreCase);
                    if (i >= 0) m_Download.m_Charset = ct[(i + 8)..].Trim(' ', '"', ';');
                }
                try { if (r.result != Result.ConnectionError) m_Download.Deliver(r.body, (ulong)(r.body?.Length ?? 0)); else m_Download.m_Done = true; }
                catch (Exception e) { m_Result = Result.DataProcessingError; m_Error = e.Message; m_Download.m_Error = e.Message; }
                if (m_Aborted && m_Download is DownloadHandlerFile f) f.Abort();
            }
            m_Op?.InvokeCompletionEvent();
        }

        public void Abort()
        {
            if (m_Done) return;
            m_Aborted = true;
            m_Cancel?.Cancel();
            Finish(new WebRequests.Response { error = "Request aborted", result = Result.ConnectionError });
        }

        public string GetRequestHeader(string name) => m_Headers.TryGetValue(name, out var v) ? v : null;
        public void SetRequestHeader(string name, string value)
        {
            if (string.IsNullOrEmpty(name)) throw new ArgumentException("Cannot set a Request Header with a null or empty name");
            if (value == null) throw new ArgumentException("Cannot set a Request header with a null");
            if (m_Sent) throw new InvalidOperationException("UnityWebRequest has already been sent and its request headers cannot be altered");
            m_Headers[name] = value;
        }
        public string GetResponseHeader(string name) => m_ResponseHeaders != null && m_ResponseHeaders.TryGetValue(name, out var v) ? v : null;
        public Dictionary<string, string> GetResponseHeaders() => m_ResponseHeaders == null ? null : new Dictionary<string, string>(m_ResponseHeaders);

        // -------------------------------------------------------------------- factory methods (UnityWebRequest's static helpers)
        public static UnityWebRequest Get(string uri) => new(uri, kHttpVerbGET, new DownloadHandlerBuffer(), null);
        public static UnityWebRequest Get(Uri uri) => Get(uri?.AbsoluteUri);
        public static UnityWebRequest Delete(string uri) => new(uri, kHttpVerbDELETE);
        public static UnityWebRequest Delete(Uri uri) => Delete(uri?.AbsoluteUri);
        public static UnityWebRequest Head(string uri) => new(uri, kHttpVerbHEAD);
        public static UnityWebRequest Head(Uri uri) => Head(uri?.AbsoluteUri);
        public static UnityWebRequest Put(string uri, byte[] bodyData) => new(uri, kHttpVerbPUT, new DownloadHandlerBuffer(), new UploadHandlerRaw(bodyData) { contentType = "application/octet-stream" });
        public static UnityWebRequest Put(Uri uri, byte[] bodyData) => Put(uri?.AbsoluteUri, bodyData);
        public static UnityWebRequest Put(string uri, string bodyData) => Put(uri, Encoding.UTF8.GetBytes(bodyData ?? ""));
        public static UnityWebRequest Put(Uri uri, string bodyData) => Put(uri?.AbsoluteUri, bodyData);
        public static UnityWebRequest Post(string uri, string postData) => PostWwwForm(uri, postData);
        public static UnityWebRequest Post(Uri uri, string postData) => PostWwwForm(uri, postData);
        public static UnityWebRequest PostWwwForm(string uri, string form) => Body(uri, Encoding.UTF8.GetBytes(EscapeForm(form)), "application/x-www-form-urlencoded");
        public static UnityWebRequest PostWwwForm(Uri uri, string form) => PostWwwForm(uri?.AbsoluteUri, form);
        public static UnityWebRequest Post(string uri, string postData, string contentType) => Body(uri, Encoding.UTF8.GetBytes(postData ?? ""), contentType);
        public static UnityWebRequest Post(Uri uri, string postData, string contentType) => Post(uri?.AbsoluteUri, postData, contentType);
        public static UnityWebRequest Post(string uri, WWWForm formData) => Body(uri, formData?.data ?? Array.Empty<byte>(), "application/x-www-form-urlencoded");
        public static UnityWebRequest Post(Uri uri, WWWForm formData) => Post(uri?.AbsoluteUri, formData);
        public static UnityWebRequest Post(string uri, List<IMultipartFormSection> multipartFormSections) => Post(uri, multipartFormSections, GenerateBoundary());
        public static UnityWebRequest Post(Uri uri, List<IMultipartFormSection> multipartFormSections) => Post(uri?.AbsoluteUri, multipartFormSections);
        public static UnityWebRequest Post(string uri, List<IMultipartFormSection> multipartFormSections, byte[] boundary)
            => Body(uri, SerializeFormSections(multipartFormSections, boundary), "multipart/form-data; boundary=" + Encoding.UTF8.GetString(boundary));
        public static UnityWebRequest Post(Uri uri, List<IMultipartFormSection> multipartFormSections, byte[] boundary) => Post(uri?.AbsoluteUri, multipartFormSections, boundary);
        public static UnityWebRequest Post(string uri, Dictionary<string, string> formFields) => Body(uri, SerializeSimpleForm(formFields), "application/x-www-form-urlencoded");
        public static UnityWebRequest Post(Uri uri, Dictionary<string, string> formFields) => Post(uri?.AbsoluteUri, formFields);
        static UnityWebRequest Body(string uri, byte[] body, string contentType)
            => new(uri, kHttpVerbPOST, new DownloadHandlerBuffer(), new UploadHandlerRaw(body.Length == 0 ? null : body) { contentType = contentType });
        static string EscapeForm(string form) => form ?? "";

        public static string EscapeURL(string s) => EscapeURL(s, Encoding.UTF8);
        public static string EscapeURL(string s, Encoding e)
        {
            if (s == null) return null;
            if (s == "") return "";
            if (e == null) return null;
            var sb = new StringBuilder();
            foreach (var b in e.GetBytes(s))
            {
                char c = (char)b;
                if (c == ' ') sb.Append('+');
                else if (char.IsAsciiLetterOrDigit(c) || "-_.*".IndexOf(c) >= 0) sb.Append(c);
                else sb.Append('%').Append(((int)b).ToString("x2"));
            }
            return sb.ToString();
        }
        public static string UnEscapeURL(string s) => UnEscapeURL(s, Encoding.UTF8);
        public static string UnEscapeURL(string s, Encoding e) => s == null ? null : Uri.UnescapeDataString(s.Replace('+', ' '));
        public static byte[] SerializeFormSections(List<IMultipartFormSection> multipartFormSections, byte[] boundary)
        {
            if (multipartFormSections == null || multipartFormSections.Count == 0) return null;
            var ms = new MemoryStream(); var b = Encoding.UTF8.GetString(boundary);
            void W(string t) { var x = Encoding.UTF8.GetBytes(t); ms.Write(x, 0, x.Length); }
            foreach (var sec in multipartFormSections)
            {
                W("\r\n--" + b + "\r\n");
                var disp = "form-data";
                if (!string.IsNullOrEmpty(sec.sectionName)) disp += $"; name=\"{sec.sectionName}\"";
                if (!string.IsNullOrEmpty(sec.fileName)) disp += $"; filename=\"{sec.fileName}\"";
                W("Content-Disposition: " + disp + "\r\n");
                if (!string.IsNullOrEmpty(sec.contentType)) W("Content-Type: " + sec.contentType + "\r\n");
                W("\r\n");
                var d = sec.sectionData; if (d != null) ms.Write(d, 0, d.Length);
            }
            W("\r\n--" + b + "--");
            return ms.ToArray();
        }
        public static byte[] GenerateBoundary()
        {
            var r = new System.Random(); var bytes = new byte[40];
            const string alphabet = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz";
            for (int i = 0; i < bytes.Length; i++) bytes[i] = (byte)alphabet[r.Next(alphabet.Length)];
            return bytes;
        }
        public static byte[] SerializeSimpleForm(Dictionary<string, string> formFields)
        {
            if (formFields == null) return null;
            var s = string.Join("&", formFields.Select(kv => EscapeURL(kv.Key) + "=" + EscapeURL(kv.Value)));
            return Encoding.UTF8.GetBytes(s);
        }

        // -------------------------------------------------------------------- state
        public bool disposeCertificateHandlerOnDispose { get; set; } = true;
        public bool disposeDownloadHandlerOnDispose { get; set; } = true;
        public bool disposeUploadHandlerOnDispose { get; set; } = true;
        public string method
        {
            get => m_Method;
            set
            {
                if (string.IsNullOrEmpty(value)) throw new ArgumentException("Cannot set a UnityWebRequest's method to an empty or null string");
                if (m_Sent) throw new InvalidOperationException("UnityWebRequest has already been sent; cannot modify the method");
                m_Method = value.ToUpperInvariant();
            }
        }
        public string error => m_Error;
        public bool useHttpContinue { get; set; } = true;
        public string url { get => m_Url; set { if (m_Sent) throw new InvalidOperationException("UnityWebRequest has already been sent; cannot modify the URL"); m_Url = value; } }
        public Uri uri { get => m_Url == null ? null : new Uri(m_Url); set => url = value?.AbsoluteUri; }
        public long responseCode => m_ResponseCode;
        public float uploadProgress => m_Done ? 1 : m_Sent ? 0 : -1;
        public bool isModifiable => !m_Sent;
        public bool isDone { get { Poll(false); return m_Done; } }
        public bool isNetworkError => m_Result == Result.ConnectionError;
        public bool isHttpError => m_Result == Result.ProtocolError;
        public Result result { get { Poll(false); return m_Result; } }
        public float downloadProgress => m_Done ? 1 : m_Sent ? 0 : -1;
        public ulong uploadedBytes => m_Uploaded;
        public ulong downloadedBytes => m_Downloaded;
        public int redirectLimit { get => m_RedirectLimit; set => m_RedirectLimit = value; }
        public bool chunkedTransfer { get => false; set { } }
        public UploadHandler uploadHandler { get => m_Upload; set { if (m_Sent) throw new InvalidOperationException("UnityWebRequest has already been sent; cannot modify the upload handler"); m_Upload = value; } }
        public DownloadHandler downloadHandler { get => m_Download; set { if (m_Sent) throw new InvalidOperationException("UnityWebRequest has already been sent; cannot modify the download handler"); m_Download = value; } }
        public CertificateHandler certificateHandler { get => m_Cert; set => m_Cert = value; }
        public int timeout { get => m_Timeout; set => m_Timeout = Math.Max(0, value); }
        public bool isError => m_Result is Result.ConnectionError or Result.ProtocolError or Result.DataProcessingError;
    }
}

namespace MiniUnity
{
    using UnityEngine.Networking;

    /// <summary>Transport and policy of UnityWebRequest; requests started in Play mode complete in <see cref="Pump"/>.</summary>
    public static class WebRequests
    {
        public sealed class Response
        {
            public long code; public byte[] body; public Dictionary<string, string> headers;
            public string error; public UnityWebRequest.Result result = UnityWebRequest.Result.Success;
        }

        static readonly List<UnityWebRequest> s_Pending = new();
        internal static void Track(UnityWebRequest r) { lock (s_Pending) s_Pending.Add(r); }

        /// <summary>Called by the player loop before coroutines resume: finished requests raise their completion on the main thread.</summary>
        public static void Pump()
        {
            UnityWebRequest[] list;
            lock (s_Pending) { if (s_Pending.Count == 0) return; list = s_Pending.ToArray(); }
            foreach (var r in list)
                if (r.isDone) lock (s_Pending) s_Pending.Remove(r);
        }

        public static void Reset() { lock (s_Pending) s_Pending.Clear(); }

        /// <summary>True when policy forbids the request (another machine without MU_NETWORK=1 / MU_ALLOW_NETWORK).</summary>
        public static bool Blocked(string url, out string why)
        {
            why = null;
            if (string.IsNullOrEmpty(url)) { why = "Malformed URL"; return true; }
            if (!Uri.TryCreate(url, UriKind.Absolute, out var u)) { why = "Malformed URL"; return true; }
            if (u.IsFile) return false;
            if (u.Scheme != "http" && u.Scheme != "https") { why = "Unsupported Protocol"; return true; }
            if (u.IsLoopback) return false;
            if (Environment.GetEnvironmentVariable("MU_NETWORK") == "1") return false;
            var allow = (Environment.GetEnvironmentVariable("MU_ALLOW_NETWORK") ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (allow.Any(h => string.Equals(h, u.Host, StringComparison.OrdinalIgnoreCase) || u.Host.EndsWith("." + h, StringComparison.OrdinalIgnoreCase))) return false;
            why = $"network disabled by MiniUnity: {u.Host} (set MU_NETWORK=1 or --allow-network {u.Host})";
            return true;
        }

        static readonly HttpClient s_Http = new(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = System.Threading.Timeout.InfiniteTimeSpan };

        internal static async Task<Response> Run(string url, string method, Dictionary<string, string> headers, byte[] body, string contentType, int redirectLimit, CancellationToken ct)
        {
            var u = new Uri(url);
            if (u.IsFile)
            {
                try { return new Response { code = 0, body = await File.ReadAllBytesAsync(u.LocalPath, ct), headers = new() }; }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return new Response { error = "Cannot connect to destination host", result = UnityWebRequest.Result.ConnectionError }; }
            }
            try
            {
                for (int hop = 0; ; hop++)
                {
                    using var req = new HttpRequestMessage(new HttpMethod(method), u);
                    if (body != null)
                    {
                        req.Content = new ByteArrayContent(body);
                        if (!string.IsNullOrEmpty(contentType)) req.Content.Headers.TryAddWithoutValidation("Content-Type", contentType);
                    }
                    foreach (var (k, v) in headers)
                    {
                        if (string.Equals(k, "Content-Type", StringComparison.OrdinalIgnoreCase))
                        {
                            req.Content ??= new ByteArrayContent(Array.Empty<byte>());
                            req.Content.Headers.Remove("Content-Type");
                            req.Content.Headers.TryAddWithoutValidation(k, v);
                        }
                        else if (!req.Headers.TryAddWithoutValidation(k, v)) req.Content?.Headers.TryAddWithoutValidation(k, v);
                    }
                    using var resp = await s_Http.SendAsync(req, ct);
                    int code = (int)resp.StatusCode;
                    if (code is 301 or 302 or 303 or 307 or 308 && resp.Headers.Location != null && redirectLimit != 0)
                    {
                        if (redirectLimit > 0 && hop >= redirectLimit) return new Response { code = code, error = "Redirect limit exceeded", result = UnityWebRequest.Result.ConnectionError };
                        u = resp.Headers.Location.IsAbsoluteUri ? resp.Headers.Location : new Uri(u, resp.Headers.Location);
                        if (code == 303) { method = "GET"; body = null; }
                        continue;
                    }
                    var r = new Response { code = code, headers = new(StringComparer.OrdinalIgnoreCase) };
                    foreach (var h in resp.Headers) r.headers[h.Key] = string.Join(", ", h.Value);
                    foreach (var h in resp.Content.Headers) r.headers[h.Key] = string.Join(", ", h.Value);
                    r.body = method == "HEAD" ? Array.Empty<byte>() : await resp.Content.ReadAsByteArrayAsync(ct);
                    if (code >= 400) { r.result = UnityWebRequest.Result.ProtocolError; r.error = $"HTTP/1.1 {code} {resp.ReasonPhrase}"; }
                    return r;
                }
            }
            catch (OperationCanceledException) { return new Response { result = UnityWebRequest.Result.ConnectionError }; }
            catch (HttpRequestException e) { return new Response { error = e.InnerException is System.Net.Sockets.SocketException se && se.SocketErrorCode == System.Net.Sockets.SocketError.HostNotFound ? "Cannot resolve destination host" : "Cannot connect to destination host", result = UnityWebRequest.Result.ConnectionError }; }
        }
    }
}
