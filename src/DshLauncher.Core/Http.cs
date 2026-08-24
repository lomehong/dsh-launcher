// DshLauncher.Core — 共享 HttpClient（超时可控），替代 WebClient（无限等待、不支持续传）
namespace DshLauncher
{
    using System.Net;

    /// <summary>
    /// 全进程共享的 HttpClient。要点：
    /// - 单例复用连接池（WebClient 每次新建连接）；
    /// - 每请求超时可指定（WebClient 默认无限等待，弱网下启动器假死）；
    /// - 走系统代理（与 WebClient 行为一致，公司代理用户不受影响）；
    /// - TLS 由 SocketsHttpHandler 与 OS 协商（Win10/11 自动 1.2/1.3）。
    /// </summary>
    public static class Http
    {
        public static readonly HttpClient Client = CreateClient();

        private static HttpClient CreateClient()
        {
            var handler = new HttpClientHandler
            {
                UseProxy = true,
                AutomaticDecompression = DecompressionMethods.All,
            };
            var client = new HttpClient(handler)
            {
                // 总超时仅作为最后保险；逐请求用 CancellationToken 控制更细粒度的超时。
                Timeout = Timeout.InfiniteTimeSpan,
            };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("dsh-launcher/" + AppMain.Version);
            return client;
        }

        /// <summary>
        /// GET 文本。失败（网络错误 / 非 2xx / 超时）返回 null。
        /// 调用方按"可失败探测"语义处理 null，与旧 WebClient + try/catch 行为对齐。
        /// </summary>
        /// <param name="url">绝对 URL</param>
        /// <param name="timeoutSeconds">整个请求（含响应体）的时限，默认 12 秒</param>
        /// <param name="accept">可选 Accept 头（如 GitHub API 的 application/vnd.github+json）</param>
        public static string GetString(string url, int timeoutSeconds = 12, string accept = null)
        {
            if (string.IsNullOrEmpty(url)) return null;
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
                using var req = new HttpRequestMessage(HttpMethod.Get, url);
                if (!string.IsNullOrEmpty(accept)) req.Headers.Accept.ParseAdd(accept);
                using HttpResponseMessage resp = Client.Send(req, HttpCompletionOption.ResponseContentRead, cts.Token);
                if (!resp.IsSuccessStatusCode) return null;
                return resp.Content.ReadAsStringAsync(cts.Token).GetAwaiter().GetResult();
            }
            catch
            {
                return null;
            }
        }
    }
}
