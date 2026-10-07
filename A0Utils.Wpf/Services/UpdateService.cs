using System;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace A0Utils.Wpf.Services
{
    public sealed class UpdateService
    {
        private const string VersionUrl = "https://raw.githubusercontent.com/kaanlab/A0Utils/main/last-version.json";

        private readonly IHttpClientFactory _httpClientFactory;

        public UpdateService(IHttpClientFactory httpClientFactory)
        {
            _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
        }

        public async Task<AppVersion> CheckForUpdates()
        {
            var client = _httpClientFactory.CreateClient();
            string json = await client.GetStringAsync(VersionUrl);
            return JsonSerializer.Deserialize<AppVersion>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }

        public async Task DownloadLastVersion(string fileUrl, string downloadPath, string fileName)
        {
            var client = _httpClientFactory.CreateClient();
            using HttpResponseMessage response = await client.GetAsync(fileUrl);
            response.EnsureSuccessStatusCode();
            Directory.CreateDirectory(downloadPath);
            var destinationPath = Path.Combine(downloadPath, fileName);

            using Stream contentStream = await response.Content.ReadAsStreamAsync(),
                fileStream = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None);

            await contentStream.CopyToAsync(fileStream);
        }
    }

    public sealed class  AppVersion
    {
        public string Name { get; set; }
        public string LastVersion { get; set; }
        public string ReleaseUrl { get; set; }
    }
}
