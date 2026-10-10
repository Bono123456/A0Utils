using System;
using System.IO;
using System.Net.Http;
using Serilog;
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
            // Validate untrusted manifest data before making a request or touching the disk.
            var destinationPath = GetDestinationPath(downloadPath, fileName);
            var directory = Path.GetDirectoryName(destinationPath);
            var temporaryPath = Path.Combine(directory, $".a0utils-update-{Guid.NewGuid():N}.tmp");

            using var client = _httpClientFactory.CreateClient();
            using HttpResponseMessage response = await client.GetAsync(fileUrl);
            response.EnsureSuccessStatusCode();
            Directory.CreateDirectory(directory);

            var temporaryFileCreated = false;
            try
            {
                using (var fileStream = new FileStream(temporaryPath, FileMode.CreateNew,
                    FileAccess.Write, FileShare.None, 81920, useAsync: true))
                {
                    temporaryFileCreated = true;
                    using (var contentStream = await response.Content.ReadAsStreamAsync())
                    {
                        await contentStream.CopyToAsync(fileStream);
                    }

                    var expectedLength = response.Content.Headers.ContentLength;
                    if (expectedLength.HasValue && fileStream.Length != expectedLength.Value)
                        throw new InvalidDataException("Файл обновления загружен не полностью.");

                    await fileStream.FlushAsync();
                }

                // Same-directory staging keeps replacement on the same volume.
                // A failed download must never truncate the previous executable.
                if (File.Exists(destinationPath))
                    File.Replace(temporaryPath, destinationPath, null);
                else
                    File.Move(temporaryPath, destinationPath);
            }
            finally
            {
                if (temporaryFileCreated)
                {
                    try { File.Delete(temporaryPath); }
                    catch (Exception ex) { Log.Warning(ex, "Не удалось удалить временный файл обновления {Path}", temporaryPath); }
                }
            }
        }

        private static string GetDestinationPath(string downloadPath, string fileName)
        {
            if (string.IsNullOrWhiteSpace(fileName)
                || fileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
                || Path.IsPathRooted(fileName)
                || fileName != Path.GetFileName(fileName)
                || fileName.EndsWith(".", StringComparison.Ordinal)
                || fileName.EndsWith(" ", StringComparison.Ordinal))
                throw new InvalidDataException("Недопустимое имя файла обновления.");

            // Windows device names remain reserved even when followed by an extension.
            var stem = fileName.Split('.')[0].TrimEnd(' ').ToUpperInvariant();
            if (stem == "CON" || stem == "PRN" || stem == "AUX" || stem == "NUL"
                || stem == "CONIN$" || stem == "CONOUT$"
                || (stem.Length == 4 && (stem.StartsWith("COM", StringComparison.Ordinal)
                    || stem.StartsWith("LPT", StringComparison.Ordinal))
                    && "123456789\u00b9\u00b2\u00b3".IndexOf(stem[3]) >= 0))
                throw new InvalidDataException("Зарезервированное имя файла обновления.");

            if (string.IsNullOrWhiteSpace(downloadPath))
                throw new ArgumentException("Не указана папка загрузки.", nameof(downloadPath));

            var directory = Path.GetFullPath(downloadPath)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                + Path.DirectorySeparatorChar;
            var destinationPath = Path.GetFullPath(Path.Combine(directory, fileName));
            if (!destinationPath.StartsWith(directory, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Файл обновления должен находиться в папке загрузки.");

            return destinationPath;
        }
    }

    public sealed class  AppVersion
    {
        public string Name { get; set; }
        public string LastVersion { get; set; }
        public string ReleaseUrl { get; set; }
    }
}
