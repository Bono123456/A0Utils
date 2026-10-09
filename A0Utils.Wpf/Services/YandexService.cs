using A0Utils.Wpf.Converters;
using A0Utils.Wpf.Helpers;
using A0Utils.Wpf.Models;
using CSharpFunctionalExtensions;
using Microsoft.Extensions.Caching.Memory;
using Serilog;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Text.Json.Serialization;

namespace A0Utils.Wpf.Services
{
    public sealed class YandexService
    {
        private const string UpdatesKey = "updates";
        private const string LicenseKey = "license";

        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IMemoryCache _memoryCache;
        private readonly SettingsService _settingsService;

        // Скачанные лицензии сохраняются рядом с exe — это запасная копия, которую клиент
        // может сам положить в папку А0. Если рядом с exe писать нельзя (например, Program Files),
        // используется временная папка Windows.
        private readonly string appPath = GetLicenseFolder();

        private static string GetLicenseFolder()
        {
            var exeFolder = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            try
            {
                var probeFile = Path.Combine(exeFolder, Path.GetRandomFileName());
                using (new FileStream(probeFile, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1, FileOptions.DeleteOnClose)) { }
                return exeFolder;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Нет прав на запись в папку программы {Folder}, лицензии будут сохранены во временную папку", exeFolder);
                return Path.Combine(Path.GetTempPath(), "A0Utils");
            }
        }

        private readonly SettingsModel _settings;

        public event EventHandler<int> DownloadUpdatesProgressChanged;
        public event EventHandler<int> DownloadLicenseProgressChanged;

        public YandexService(
            IHttpClientFactory httpClientFactory,
            SettingsService settingsService,
            IMemoryCache memoryCache)
        {
            _httpClientFactory = httpClientFactory;
            _memoryCache = memoryCache;
            _settingsService = settingsService;

            _settings = _settingsService.GetSettings();
        }

        public async Task<Result<IEnumerable<UpdateModel>>> GetUpdates(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!_memoryCache.TryGetValue(UpdatesKey, out IEnumerable<YandexUpdateModel> yandexUpdateModels))
            {
                var result = await GetUpdatesByHttp(cancellationToken);
                if (result.IsFailure)
                {
                    return Result.Failure<IEnumerable<UpdateModel>>(result.Error);
                }

                var cacheEntryOptions = new MemoryCacheEntryOptions()
                    .SetAbsoluteExpiration(TimeSpan.FromHours(1));

                _memoryCache.Set(UpdatesKey, result.Value, cacheEntryOptions);

                yandexUpdateModels = result.Value;
            }

            return Result.Success(yandexUpdateModels.MapToUpdateModels());
        }

        public async Task<Result> DownloadUpdates(IEnumerable<UpdateModel> updates, string downloadPath, CancellationToken cancellationToken = default)
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                using var httpClient = _httpClientFactory.CreateClient("yandexClient");
                var urls = updates.SelectMany(x => x.Urls).Distinct(StringComparer.Ordinal).ToList();
                var items = new Dictionary<string, YandexItem>(StringComparer.OrdinalIgnoreCase);
                foreach (var url in urls)
                {
                    var item = await ReadJsonAsync<YandexItem>(httpClient, $"{_settings.YandexUrl}{url}", cancellationToken);
                    ValidateDownloadItem(item);
                    if (items.TryGetValue(item.Name, out var existing))
                    {
                        if (!string.Equals(existing.File, item.File, StringComparison.Ordinal) || existing.Size != item.Size)
                            throw new InvalidDataException($"Разные загрузки используют одно имя: {item.Name}");
                    }
                    else
                    {
                        items.Add(item.Name, item);
                    }
                }

                var totalBytes = items.Values.All(x => x.Size.HasValue) ? items.Values.Sum(x => x.Size.Value) : 0;
                long totalRead = 0;
                int lastProgress = 0;
                int completed = 0;
                DownloadUpdatesProgressChanged?.Invoke(this, 0);
                foreach (var item in items.Values)
                {
                    await DownloadFileAsync(httpClient, item, downloadPath, bytesRead =>
                    {
                        totalRead += bytesRead;
                        if (totalBytes > 0)
                        {
                            // 100 means the last file has been validated and committed.
                            var progress = (int)Math.Min(99, (double)totalRead * 100 / totalBytes);
                            if (progress != lastProgress)
                            {
                                lastProgress = progress;
                                DownloadUpdatesProgressChanged?.Invoke(this, progress);
                            }
                        }
                    }, cancellationToken);
                    completed++;
                    if (totalBytes <= 0)
                        DownloadUpdatesProgressChanged?.Invoke(this, Math.Min(99, completed * 100 / items.Count));
                }
                cancellationToken.ThrowIfCancellationRequested();
                DownloadUpdatesProgressChanged?.Invoke(this, 100);
                return Result.Success();
            }
            catch (Exception) when (cancellationToken.IsCancellationRequested) { throw new OperationCanceledException(cancellationToken); }
            catch (Exception ex)
            {
                Log.Error(ex, "Ошибка при скачивании обновлений");
                return Result.Failure("Ошибка при скачивании обновлений");
            }
        }

        public async Task<Result<DownloadModel>> DownloadLicense(string licenseName, CancellationToken cancellationToken = default)
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                using var httpClient = _httpClientFactory.CreateClient("yandexClient");
                if (!_memoryCache.TryGetValue(LicenseKey, out YandexEmbedded yandexResource))
                {
                    var yandexResourceResult = await GetLicenseResource(httpClient, cancellationToken);
                    if (yandexResourceResult.IsFailure)
                    {
                        return Result.Failure<DownloadModel>(yandexResourceResult.Error);
                    }

                    var cacheEntryOptions = new MemoryCacheEntryOptions()
                        .SetAbsoluteExpiration(TimeSpan.FromHours(1));

                    _memoryCache.Set(LicenseKey, yandexResourceResult.Value, cacheEntryOptions);

                    yandexResource = yandexResourceResult.Value;
                }

                var licensePath = await DownloadLicenseFile(licenseName, yandexResource, httpClient, cancellationToken);
                if(licensePath.IsFailure)
                {
                    return Result.Failure<DownloadModel>(licensePath.Error);
                }

                var descriptionPath = await DownloadLicenseDescriptionFile(licenseName, yandexResource, httpClient, cancellationToken);
                if(descriptionPath.IsFailure)
                {
                    return Result.Failure<DownloadModel>(descriptionPath.Error);
                }

                return new DownloadModel { LicensePath = licensePath.Value, DescriptionPath = descriptionPath.Value };

            }
            catch (Exception) when (cancellationToken.IsCancellationRequested) { throw new OperationCanceledException(cancellationToken); }
            catch (Exception ex)
            {
                Log.Error("Ошибка при получении лицензии: {Error}", ex);
                return Result.Failure<DownloadModel>("Ошибка при получении лицензии");
            }
        }

        public async Task<Result<LicenseInfoModel>> GetLicensesInfo(string licenseName, CancellationToken cancellationToken = default)
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                using var httpClient = _httpClientFactory.CreateClient("yandexClient");
                var yandexResourceResult = await GetLicenseResource(httpClient, cancellationToken);
                if (yandexResourceResult.IsFailure)
                {
                    return Result.Failure<LicenseInfoModel>(yandexResourceResult.Error);
                }

                var licenseResult = await ParseLicenseDescriptionFile(licenseName, yandexResourceResult.Value, httpClient, cancellationToken);
                if (licenseResult.IsFailure)
                {
                    return Result.Failure<LicenseInfoModel>(licenseResult.Error);
                }

                var subscriptionResult = await GetSubscription(licenseName, httpClient, cancellationToken);
                if (subscriptionResult.IsFailure)
                {
                    return Result.Failure<LicenseInfoModel>(subscriptionResult.Error);
                }
                licenseResult.Value.SubscriptionLicenseExpAt = subscriptionResult.Value;

                return licenseResult;

            }
            catch (Exception) when (cancellationToken.IsCancellationRequested) { throw new OperationCanceledException(cancellationToken); }
            catch (Exception ex)
            {
                Log.Error("Ошибка при получении лицензии: {Error}", ex);
                return Result.Failure<LicenseInfoModel>("Ошибка при получении лицензии");
            }
        }

        private async Task<Result<IEnumerable<YandexUpdateModel>>> GetUpdatesByHttp(CancellationToken cancellationToken)
        {
            try
            {
                using var httpClient = _httpClientFactory.CreateClient("yandexClient");
                var item = await ReadJsonAsync<YandexItem>(httpClient, $"{_settings.YandexUrl}{_settings.UpdatesUrl}", cancellationToken);
                ValidateDownloadItem(item);
                return Result.Success(await ReadJsonAsync<IEnumerable<YandexUpdateModel>>(httpClient, item.File, cancellationToken));
            }
            catch (Exception) when (cancellationToken.IsCancellationRequested) { throw new OperationCanceledException(cancellationToken); }
            catch (Exception ex)
            {
                Log.Error(ex, "Ошибка при получении обновлений");
                return Result.Failure<IEnumerable<YandexUpdateModel>>("Ошибка при получении обновлений");
            }
        }

        private async Task<Result<DateTime>> GetSubscription(string licenseName, HttpClient httpClient, CancellationToken cancellationToken)
        {
            try
            {
                if (licenseName.EndsWith(".ISL", StringComparison.OrdinalIgnoreCase))
                    licenseName = licenseName.Substring(0, licenseName.Length - 4);

                var item = await ReadJsonAsync<YandexItem>(httpClient, $"{_settings.YandexUrl}{_settings.SubscriptionUrl}", cancellationToken);
                ValidateDownloadItem(item);
                var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true, Converters = { new JsonDateTimeConverter() } };
                var subscriptions = await ReadJsonAsync<IEnumerable<SubscriptionModel>>(httpClient, item.File, cancellationToken, options);
                var subscription = subscriptions.FirstOrDefault(x => x.Number == licenseName.TrimStart('0'));
                return subscription == null ? default(DateTime) : subscription.Date.AddDays(365);
            }
            catch (Exception) when (cancellationToken.IsCancellationRequested) { throw new OperationCanceledException(cancellationToken); }
            catch (Exception ex)
            {
                Log.Error(ex, "Ошибка при получении файла подписки");
                return Result.Failure<DateTime>("Ошибка при получении файла подписки");
            }
        }

        // Яндекс отдаёт список файлов папки порциями. Запрашиваем по 1000 штук, пока не получим
        // все: в каждом ответе Яндекс сообщает общее число файлов (Total).
        private async Task<Result<YandexEmbedded>> GetLicenseResource(HttpClient httpClient, CancellationToken cancellationToken)
        {
            const int pageSize = 1000;
            try
            {
                var items = new List<YandexItem>();
                var names = new HashSet<string>(StringComparer.Ordinal);
                int? expectedTotal = null;
                do
                {
                    var url = $"{_settings.YandexUrl}{_settings.LicenseUrl}";
                    url += (url.Contains("?") ? "&" : "?") + $"limit={pageSize}&offset={items.Count}";
                    var resource = await ReadJsonAsync<YandexResource>(httpClient, url, cancellationToken);
                    var page = resource._Embedded;
                    if (page == null || page.Items == null || page.Total < 0 || page.Offset != items.Count
                        || page.Items.Length > pageSize || (expectedTotal.HasValue && page.Total != expectedTotal.Value))
                        throw new InvalidDataException("Некорректная страница списка лицензий.");

                    expectedTotal = page.Total;
                    if (page.Items.Length > expectedTotal.Value - items.Count
                        || (page.Items.Length == 0 && items.Count < expectedTotal.Value))
                        throw new InvalidDataException("Получен неполный список лицензий.");

                    foreach (var item in page.Items)
                    {
                        if (item == null || string.IsNullOrWhiteSpace(item.Name) || !names.Add(item.Name))
                            throw new InvalidDataException("Повторяющаяся или некорректная запись списка лицензий.");
                    }
                    items.AddRange(page.Items);
                }
                while (items.Count < expectedTotal.Value);

                cancellationToken.ThrowIfCancellationRequested();
                return new YandexEmbedded { Items = items.ToArray(), Limit = items.Count, Offset = 0, Total = expectedTotal.Value };
            }
            catch (Exception) when (cancellationToken.IsCancellationRequested) { throw new OperationCanceledException(cancellationToken); }
            catch (Exception ex)
            {
                Log.Error(ex, "Ошибка при получении списка лицензий");
                return Result.Failure<YandexEmbedded>("Не удалось получить полный список лицензий. Повторите операцию.");
            }
        }

        private async Task<Result<string>> DownloadLicenseFile(string licenseName, YandexEmbedded yandexResource, HttpClient httpClient, CancellationToken cancellationToken)
        {
            try
            {
                if (!licenseName.EndsWith(".ISL", StringComparison.OrdinalIgnoreCase))
                {
                    licenseName = licenseName + ".ISL";
                }

                if (licenseName.Split('.').Length > 2)
                {
                    return Result.Failure<string>("Лицензия должна быть в формате .ISL");
                }

                var license = yandexResource.Items.FirstOrDefault(x => x.Name.Equals(licenseName, StringComparison.OrdinalIgnoreCase));
                if (license == null)
                {
                    Log.Error($"Лицензия {licenseName} не найдена");
                    return Result.Failure<string>($"Лицензия {licenseName} не найдена");
                }

                long read = 0;
                DownloadLicenseProgressChanged?.Invoke(this, 0);
                var path = await DownloadFileAsync(httpClient, license, appPath, bytesRead =>
                {
                    read += bytesRead;
                    if (license.Size > 0)
                        DownloadLicenseProgressChanged?.Invoke(this, (int)Math.Min(99, (double)read * 100 / license.Size.Value));
                }, cancellationToken);
                DownloadLicenseProgressChanged?.Invoke(this, 100);

                return path;
            }
            catch (Exception) when (cancellationToken.IsCancellationRequested) { throw new OperationCanceledException(cancellationToken); }
            catch (Exception ex)
            {
                Log.Error("Ошибка при скачивании лицензии: {Error}", ex.Message);
                return Result.Failure<string>("Ошибка при скачивании лицензии");
            }

        }

        private async Task<Result<string>> DownloadLicenseDescriptionFile(string licenseName, YandexEmbedded yandexResource, HttpClient httpClient, CancellationToken cancellationToken)
        {
            try
            {
                if (licenseName.EndsWith(".ISL", StringComparison.OrdinalIgnoreCase))
                {
                    licenseName = licenseName.Substring(0, licenseName.Length - 4) + ".ild";
                }
                else
                {
                    licenseName = licenseName + ".ild";
                }

                if (licenseName.Split('.').Length > 2)
                {
                    return Result.Failure<string>("Фаил должен быть в формате .ild");
                }

                var description = yandexResource.Items.FirstOrDefault(x => x.Name.Equals(licenseName, StringComparison.OrdinalIgnoreCase));
                if (description == null)
                {
                    Log.Error($"Фаил с описанием лицензий {licenseName} не найден");
                    return Result.Failure<string>($"Фаил с описанием лицензий {licenseName} не найден");
                }

                long read = 0;
                DownloadLicenseProgressChanged?.Invoke(this, 0);
                var path = await DownloadFileAsync(httpClient, description, appPath, bytesRead =>
                {
                    read += bytesRead;
                    if (description.Size > 0)
                        DownloadLicenseProgressChanged?.Invoke(this, (int)Math.Min(99, (double)read * 100 / description.Size.Value));
                }, cancellationToken);
                DownloadLicenseProgressChanged?.Invoke(this, 100);

                return path;
            }
            catch (Exception) when (cancellationToken.IsCancellationRequested) { throw new OperationCanceledException(cancellationToken); }
            catch (Exception ex)
            {
                Log.Error("Ошибка при сохранении фаила с описанием лицензий: {Error}", ex);
                return Result.Failure<string>("Ошибка при сохранеии фаила с описанием лицензий");
            }
        }

        private async Task<Result<LicenseInfoModel>> ParseLicenseDescriptionFile(string licenseName, YandexEmbedded yandexResource, HttpClient httpClient, CancellationToken cancellationToken)
        {
            try
            {
                if (licenseName.EndsWith(".ISL", StringComparison.OrdinalIgnoreCase))
                {
                    licenseName = licenseName.Substring(0, licenseName.Length - 4) + ".ild";
                }
                else
                {
                    licenseName = licenseName + ".ild";
                }

                if (licenseName.Split('.').Length > 2)
                {
                    return Result.Failure<LicenseInfoModel>("Фаил должен быть в формате .ild");
                }

                var description = yandexResource.Items.FirstOrDefault(x => x.Name.Equals(licenseName, StringComparison.OrdinalIgnoreCase));
                if (description == null)
                {
                    Log.Error($"Фаил с описанием лицензий {licenseName} не найден");
                    return Result.Failure<LicenseInfoModel>($"Фаил с описанием лицензий {licenseName} не найден");
                }

                ValidateDownloadItem(description);
                using (var response = await httpClient.GetAsync(description.File, HttpCompletionOption.ResponseHeadersRead, cancellationToken))
                using (cancellationToken.Register(response.Dispose))
                {
                    response.EnsureSuccessStatusCode();
                    using (var responseStream = await response.Content.ReadAsStreamAsync())
                    using (var buffer = new MemoryStream())
                    {
                        await responseStream.CopyToAsync(buffer, 81920, cancellationToken);
                        cancellationToken.ThrowIfCancellationRequested();
                        var content = Encoding.GetEncoding("windows-1251").GetString(buffer.ToArray());

                        var a0LicenseResult = ParseHelpers.FindA0LicenseExp(content);
                        if (a0LicenseResult.IsFailure)
                        {
                            return Result.Failure<LicenseInfoModel>(a0LicenseResult.Error);
                        }

                        var pirLicenseResult = ParseHelpers.FindPIRLicenseExp(content);
                        if (pirLicenseResult.IsFailure)
                        {
                            return Result.Failure<LicenseInfoModel>(pirLicenseResult.Error);
                        }

                        return new LicenseInfoModel
                        {
                            Content = content,
                            A0LicenseExpAt = a0LicenseResult.Value,
                            PIRLicenseExpAt = pirLicenseResult.Value
                        };
                    }
                }

            }
            catch (Exception) when (cancellationToken.IsCancellationRequested) { throw new OperationCanceledException(cancellationToken); }
            catch (Exception ex)
            {
                Log.Error("Ошибка при скачивании фаила с описанием лицензий: {Error}", ex);
                return Result.Failure<LicenseInfoModel>("Ошибка при скачивании фаила с описанием лицензий");
            }
        }
        private static async Task<T> ReadJsonAsync<T>(HttpClient client, string url,
            CancellationToken cancellationToken, JsonSerializerOptions options = null) where T : class
        {
            using (var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken))
            using (cancellationToken.Register(response.Dispose))
            {
                response.EnsureSuccessStatusCode();
                using (var stream = await response.Content.ReadAsStreamAsync())
                {
                    var value = await JsonSerializer.DeserializeAsync<T>(stream,
                        options ?? new JsonSerializerOptions { PropertyNameCaseInsensitive = true }, cancellationToken);
                    cancellationToken.ThrowIfCancellationRequested();
                    return value ?? throw new InvalidDataException("Сервер вернул пустые данные.");
                }
            }
        }

        private static void ValidateDownloadItem(YandexItem item)
        {
            if (item == null || string.IsNullOrWhiteSpace(item.Name)
                || item.Name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
                || item.Name != Path.GetFileName(item.Name) || item.Name.EndsWith(".") || item.Name.EndsWith(" ")
                || item.Size < 0)
                throw new InvalidDataException("Некорректное имя или размер файла.");

            var stem = item.Name.Split('.')[0].ToUpperInvariant();
            if (stem == "CON" || stem == "PRN" || stem == "AUX" || stem == "NUL"
                || (stem.Length == 4 && (stem.StartsWith("COM") || stem.StartsWith("LPT"))
                    && stem[3] >= '1' && stem[3] <= '9'))
                throw new InvalidDataException("Зарезервированное имя файла.");

            if (!Uri.TryCreate(item.File, UriKind.Absolute, out var uri)
                || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
                throw new InvalidDataException("Некорректная ссылка на файл.");
        }

        private static async Task<string> DownloadFileAsync(HttpClient client, YandexItem item,
            string directory, Action<int> onBytesRead, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ValidateDownloadItem(item);
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, item.Name);
            // Same directory keeps the final move/replacement on the same volume.
            var temporaryPath = Path.Combine(directory, $".a0utils-{Guid.NewGuid():N}.tmp");
            try
            {
                using (var response = await client.GetAsync(item.File, HttpCompletionOption.ResponseHeadersRead, cancellationToken))
                using (cancellationToken.Register(response.Dispose))
                {
                    response.EnsureSuccessStatusCode();
                    var contentLength = response.Content.Headers.ContentLength;
                    if (item.Size.HasValue && contentLength.HasValue && item.Size.Value != contentLength.Value)
                        throw new InvalidDataException("Размер ответа не совпадает с размером файла.");

                    using (var input = await response.Content.ReadAsStreamAsync())
                    using (var output = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write,
                        FileShare.None, 81920, useAsync: true))
                    {
                        var buffer = new byte[81920];
                        long totalRead = 0;
                        int bytesRead;
                        while ((bytesRead = await input.ReadAsync(buffer, 0, buffer.Length, cancellationToken)) > 0)
                        {
                            totalRead += bytesRead;
                            if ((item.Size.HasValue && totalRead > item.Size.Value)
                                || (contentLength.HasValue && totalRead > contentLength.Value))
                                throw new InvalidDataException("Получено больше данных, чем ожидалось.");
                            await output.WriteAsync(buffer, 0, bytesRead, cancellationToken);
                            onBytesRead?.Invoke(bytesRead);
                        }
                        if ((item.Size.HasValue && totalRead != item.Size.Value)
                            || (contentLength.HasValue && totalRead != contentLength.Value))
                            throw new InvalidDataException("Файл загружен не полностью.");
                        await output.FlushAsync(cancellationToken);
                    }
                }

                cancellationToken.ThrowIfCancellationRequested();
                if (File.Exists(path))
                    File.Replace(temporaryPath, path, null);
                else
                    File.Move(temporaryPath, path);
                return path;
            }
            finally
            {
                try { File.Delete(temporaryPath); }
                catch (Exception ex) { Log.Warning(ex, "Не удалось удалить временный файл {Path}", temporaryPath); }
            }
        }

    }

    public sealed class YandexResource
    {
        public string Name { get; set; }
        public YandexEmbedded _Embedded { get; set; }
    }

    public sealed class YandexEmbedded
    {
        public YandexItem[] Items { get; set; }
        public int Limit { get; set; }
        [JsonRequired]
        public int Offset { get; set; }
        [JsonRequired]
        public int Total { get; set; }
    }

    public sealed class YandexItem
    {
        public string Name { get; set; }
        public string File { get; set; }
        public long? Size { get; set; }
        public long Revision { get; set; }
    }
}
