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
using System.Threading.Tasks;

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

        public async Task<Result<IEnumerable<UpdateModel>>> GetUpdates()
        {
            var updateModels = new List<UpdateModel>();
            if (!_memoryCache.TryGetValue(UpdatesKey, out IEnumerable<YandexUpdateModel> yandexUpdateModels))
            {
                var result = await GetUpdatesByHttp();
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

        public async Task<Result> DownloadUpdates(IEnumerable<UpdateModel> updates, string downloadPath)
        {
            try
            {
                // Папку могли удалить после запуска программы — создаём заново
                Directory.CreateDirectory(downloadPath);

                var httpClient = _httpClientFactory.CreateClient("yandexClient");
                var jsonOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

                // Сначала получаем сведения обо всех файлах, чтобы знать общий размер загрузки
                var items = new List<YandexItem>();
                foreach (var update in updates)
                {
                    foreach (var url in update.Urls)
                    {
                        var response = await httpClient.GetAsync($"{_settings.YandexUrl}{url}", HttpCompletionOption.ResponseHeadersRead);
                        using (var contentStream = await response.Content.ReadAsStreamAsync())
                        {
                            items.Add(await JsonSerializer.DeserializeAsync<YandexItem>(contentStream, jsonOptions));
                        }
                    }
                }

                long totalBytes = items.Sum(x => x.Size);
                long totalRead = 0;
                int lastProgress = 0;
                DownloadUpdatesProgressChanged?.Invoke(this, 0);

                for (int i = 0; i < items.Count; i++)
                {
                    var yandexItem = items[i];
                    var path = Path.Combine(downloadPath, yandexItem.Name);

                    using (var responseStream = await httpClient.GetStreamAsync(yandexItem.File))
                    {
                        using (var fileStream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, bufferSize: 81920, useAsync: false))
                        {
                            byte[] buffer = new byte[81920];
                            int bytesRead;

                            while ((bytesRead = await responseStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                            {
                                await fileStream.WriteAsync(buffer, 0, bytesRead);
                                totalRead += bytesRead;

                                if (totalBytes > 0)
                                {
                                    var progress = (int)Math.Min(100, totalRead * 100 / totalBytes);
                                    if (progress != lastProgress)
                                    {
                                        lastProgress = progress;
                                        DownloadUpdatesProgressChanged?.Invoke(this, progress);
                                    }
                                }
                            }
                        }
                    }

                    // Если размеры файлов неизвестны, считаем прогресс по количеству скачанных файлов
                    if (totalBytes <= 0)
                    {
                        DownloadUpdatesProgressChanged?.Invoke(this, (i + 1) * 100 / items.Count);
                    }
                }

                DownloadUpdatesProgressChanged?.Invoke(this, 100);

                return Result.Success();
            }
            catch (System.Exception ex)
            {
                Log.Error("Ошибка при скачивании обновлений: {Error}", ex.Message);
                return Result.Failure("Ошибка при скачивании обновлений");
            }
        }

        public async Task<Result<DownloadModel>> DownloadLicense(string licenseName)
        {
            try
            {
                var httpClient = _httpClientFactory.CreateClient("yandexClient");
                if (!_memoryCache.TryGetValue(LicenseKey, out YandexEmbedded yandexResource))
                {
                    var yandexResourceResult = await GetLicenseResource(httpClient);
                    if (yandexResourceResult.IsFailure)
                    {
                        return Result.Failure<DownloadModel>(yandexResourceResult.Error);
                    }

                    var cacheEntryOptions = new MemoryCacheEntryOptions()
                        .SetAbsoluteExpiration(TimeSpan.FromHours(1));

                    _memoryCache.Set(LicenseKey, yandexResourceResult.Value, cacheEntryOptions);

                    yandexResource = yandexResourceResult.Value;
                }

                var licensePath = await DownloadLicenseFile(licenseName, yandexResource, httpClient);
                if(licensePath.IsFailure)
                {
                    return Result.Failure<DownloadModel>(licensePath.Error);
                }

                var descriptionPath = await DownloadLicenseDescriptionFile(licenseName, yandexResource, httpClient);
                if(descriptionPath.IsFailure)
                {
                    return Result.Failure<DownloadModel>(descriptionPath.Error);
                }

                return new DownloadModel { LicensePath = licensePath.Value, DescriptionPath = descriptionPath.Value };

            }
            catch (System.Exception ex)
            {
                Log.Error("Ошибка при получении лицензии: {Error}", ex);
                return Result.Failure<DownloadModel>("Ошибка при получении лицензии");
            }
        }

        public async Task<Result<LicenseInfoModel>> GetLicensesInfo(string licenseName)
        {
            try
            {
                var httpClient = _httpClientFactory.CreateClient("yandexClient");
                var yandexResourceResult = await GetLicenseResource(httpClient);
                if (yandexResourceResult.IsFailure)
                {
                    return Result.Failure<LicenseInfoModel>(yandexResourceResult.Error);
                }

                var licenseResult = await ParseLicenseDescriptionFile(licenseName, yandexResourceResult.Value, httpClient);
                if (licenseResult.IsFailure)
                {
                    return Result.Failure<LicenseInfoModel>(licenseResult.Error);
                }

                var subscriptionResult = await GetSubscription(licenseName, httpClient);
                if (subscriptionResult.IsFailure)
                {
                    return Result.Failure<LicenseInfoModel>(subscriptionResult.Error);
                }
                licenseResult.Value.SubscriptionLicenseExpAt = subscriptionResult.Value;

                return licenseResult;

            }
            catch (System.Exception ex)
            {
                Log.Error("Ошибка при получении лицензии: {Error}", ex);
                return Result.Failure<LicenseInfoModel>("Ошибка при получении лицензии");
            }
        }

        private async Task<Result<IEnumerable<YandexUpdateModel>>> GetUpdatesByHttp()
        {
            try
            {
                var httpClient = _httpClientFactory.CreateClient("yandexClient");
                var response = await httpClient.GetAsync($"{_settings.YandexUrl}{_settings.UpdatesUrl}", HttpCompletionOption.ResponseHeadersRead);
                using (var contentStream = await response.Content.ReadAsStreamAsync())
                {
                    var yandexItem = await JsonSerializer.DeserializeAsync<YandexItem>(contentStream, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                    using (var responseStream = await httpClient.GetStreamAsync(yandexItem.File))
                    {
                        var updates = await JsonSerializer.DeserializeAsync<IEnumerable<YandexUpdateModel>>(responseStream, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                        return Result.Success(updates);
                    }
                }
            }
            catch (System.Exception ex)
            {
                Log.Error("Ошибка при получении обновленй {Error}", ex);
                return Result.Failure<IEnumerable<YandexUpdateModel>>($"Ошибка при получении обновленй");
            }
        }

        private async Task<Result<DateTime>> GetSubscription(string licenseName, HttpClient httpClient)
        {
            try
            {
                if (licenseName.EndsWith(".ISL"))
                {
                    licenseName = licenseName.Substring(0, licenseName.Length - 4);
                }

                var response = await httpClient.GetAsync($"{_settings.YandexUrl}{_settings.SubscriptionUrl}", HttpCompletionOption.ResponseHeadersRead);
                using (var contentStream = await response.Content.ReadAsStreamAsync())
                {
                    var yandexItem = await JsonSerializer.DeserializeAsync<YandexItem>(contentStream, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                    var path = Path.Combine(appPath, yandexItem.Name);
                    using (var responseStream = await httpClient.GetStreamAsync(yandexItem.File))
                    {
                        var subscriptions = await JsonSerializer.DeserializeAsync<IEnumerable<SubscriptionModel>>(responseStream, new JsonSerializerOptions { PropertyNameCaseInsensitive = true, Converters = { new JsonDateTimeConverter() } });
                        var subscription = subscriptions.FirstOrDefault(x => x.Number == licenseName.TrimStart('0'));
                        if (subscription is null)
                        {
                            return default;
                        }

                        return subscription.Date.AddDays(365);
                    }

                }
            }
            catch (System.Exception ex)
            {
                Log.Error("Ошибка при получении файла подписки {Error}", ex);
                return Result.Failure<DateTime>("Ошибка при получении файла подписки");
            }
        }

        // Яндекс отдаёт список файлов папки порциями. Запрашиваем по 1000 штук, пока не получим
        // все: в каждом ответе Яндекс сообщает общее число файлов (Total).
        private async Task<Result<YandexEmbedded>> GetLicenseResource(HttpClient httpClient)
        {
            const int pageSize = 1000;
            try
            {
                var items = new List<YandexItem>();
                int total;
                do
                {
                    var response = await httpClient.GetAsync($"{_settings.YandexUrl}{_settings.LicenseUrl}&limit={pageSize}&offset={items.Count}", HttpCompletionOption.ResponseHeadersRead);
                    using (var contentStream = await response.Content.ReadAsStreamAsync())
                    {
                        var yandexResource = await JsonSerializer.DeserializeAsync<YandexResource>(contentStream, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                        var page = yandexResource._Embedded;
                        total = page.Total;
                        if (page.Items == null || page.Items.Length == 0)
                        {
                            break;
                        }

                        items.AddRange(page.Items);
                    }
                }
                while (items.Count < total);

                return new YandexEmbedded { Items = items.ToArray(), Limit = items.Count, Offset = 0, Total = total };
            }
            catch (System.Exception ex)
            {
                Log.Error("Ошибка при получении списка лицензий {Error}", ex);
                return Result.Failure<YandexEmbedded>($"Ошибка при получении списка лицензий");
            }
        }

        private async Task<Result<string>> DownloadLicenseFile(string licenseName, YandexEmbedded yandexResource, HttpClient httpClient)
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

                Directory.CreateDirectory(appPath);
                var path = Path.Combine(appPath, license.Name);

                using (var responseStream = await httpClient.GetStreamAsync(license.File))
                {
                    using (var fileStream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, bufferSize: 8192, useAsync: true))
                    {
                        byte[] buffer = new byte[8192];
                        long totalBytes = license.Size;
                        long totalRead = 0;
                        int bytesRead;

                        while ((bytesRead = await responseStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                        {
                            await fileStream.WriteAsync(buffer, 0, bytesRead);
                            totalRead += bytesRead;

                            if (totalBytes > 0)
                            {
                                DownloadLicenseProgressChanged?.Invoke(this, (int)((totalRead * 100) / totalBytes));
                            }
                        }
                    }
                }

                return path;
            }
            catch (System.Exception ex)
            {
                Log.Error("Ошибка при скачивании лицензии: {Error}", ex.Message);
                return Result.Failure<string>("Ошибка при скачивании лицензии");
            }

        }

        private async Task<Result<string>> DownloadLicenseDescriptionFile(string licenseName, YandexEmbedded yandexResource, HttpClient httpClient)
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

                Directory.CreateDirectory(appPath);
                var path = Path.Combine(appPath, description.Name);

                using (var responseStream = await httpClient.GetStreamAsync(description.File))
                {
                    using (var fileStream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, bufferSize: 8192, useAsync: true))
                    {
                        byte[] buffer = new byte[8192];
                        long totalBytes = description.Size;
                        long totalRead = 0;
                        int bytesRead;

                        while ((bytesRead = await responseStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                        {
                            await fileStream.WriteAsync(buffer, 0, bytesRead);
                            totalRead += bytesRead;

                            if (totalBytes > 0)
                            {
                                DownloadLicenseProgressChanged?.Invoke(this, (int)((totalRead * 100) / totalBytes));
                            }
                        }
                    }
                }

                return path;
            }
            catch (System.Exception ex)
            {
                Log.Error("Ошибка при сохранении фаила с описанием лицензий: {Error}", ex);
                return Result.Failure<string>("Ошибка при сохранеии фаила с описанием лицензий");
            }
        }

        private async Task<Result<LicenseInfoModel>> ParseLicenseDescriptionFile(string licenseName, YandexEmbedded yandexResource, HttpClient httpClient)
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

                var path = Path.Combine(appPath, description.Name);

                using (var responseStream = await httpClient.GetStreamAsync(description.File))
                {
                    using (var reader = new StreamReader(responseStream, Encoding.GetEncoding("windows-1251")))
                    {
                        string content = await reader.ReadToEndAsync();

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
            catch (System.Exception ex)
            {
                Log.Error("Ошибка при скачивании фаила с описанием лицензий: {Error}", ex);
                return Result.Failure<LicenseInfoModel>("Ошибка при скачивании фаила с описанием лицензий");
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
        public int Offset { get; set; }
        public int Total { get; set; }
    }

    public sealed class YandexItem
    {
        public string Name { get; set; }
        public string File { get; set; }
        public long Size { get; set; }
        public long Revision { get; set; }
    }
}
