using A0Utils.Wpf.Helpers;
using CSharpFunctionalExtensions;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text.RegularExpressions;

namespace A0Utils.Wpf.Models
{
    public class UpdateModel : INotifyPropertyChanged
    {
        public string Name { get; set; }
        public string Key_type { get; set; } = string.Empty;
        public string Category { get; set; }
        public IEnumerable<string> Urls { get; set; }
        public string Index { get; set; } = string.Empty;
        public string Date { get; set; } = string.Empty;
        public string Region { get; set; } = string.Empty;
        public string Tag { get; set; } = string.Empty;

        // Самая свежая версия базы со своим тегом (например последняя ФСНБ-2022).
        // Показывается обычной строкой, остальные версии — в свёрнутой группе под ней.
        public bool IsLatestInTag { get; set; }

        // Элемент относится к самому свежему году (в своём регионе для справочников цен,
        // среди всех индексов для индексов). Такая группа года показывается раскрытой.
        public bool IsNewestYear { get; set; }

        // Год, по которому группируется элемент (null — не группируется по году)
        public int? GroupYear
        {
            get
            {
                if (!string.IsNullOrWhiteSpace(Tag))
                {
                    return null;
                }

                string year = Category == "Справочники цен" ? Group2
                    : Category == "Индексы к ФЕР/ТЕР" ? Group1
                    : null;

                return int.TryParse(year, out var value) ? value : (int?)null;
            }
        }

        // Группировка внутри категории (два уровня):
        //   справочники цен — регион, внутри годы;
        //   индексы к ФЕР/ТЕР — год;
        //   базы с тегом — последняя версия без группы, предыдущие в группе.
        // Пустая строка или пробел — без заголовка группы. Разные значения нужны,
        // чтобы последняя ФСНБ и прочие базы не попали в одну группу.
        public string Group1
        {
            get
            {
                if (!string.IsNullOrWhiteSpace(Tag))
                {
                    return IsLatestInTag ? string.Empty : $"{Tag}: предыдущие изменения";
                }

                if (Category == "Справочники цен")
                {
                    return string.IsNullOrWhiteSpace(Region) ? "Прочие" : Region;
                }

                if (Category == "Индексы к ФЕР/ТЕР")
                {
                    return FindYear(Name) ?? " ";
                }

                return " ";
            }
        }

        public string Group2
        {
            get
            {
                if (Category == "Справочники цен" && string.IsNullOrWhiteSpace(Tag))
                {
                    return FindYear(Date) ?? FindYear(Name) ?? string.Empty;
                }

                return string.Empty;
            }
        }

        private static string FindYear(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return null;
            }

            var match = Regex.Match(text, @"(?<!\d)(20\d{2})(?!\d)");
            return match.Success ? match.Groups[1].Value : null;
        }

        private bool _isSelected;
        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                _isSelected = value;
                OnPropertyChanged(nameof(IsSelected));
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged(string propertyName) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    public enum LicenseStatus
    {
        None = 0,
        Warning
    }

    public sealed record YandexUpdateModel
    {
        public string Name { get; private set; }
        public string Key_type { get; private set; }
        public string Category { get; private set; }
        public IEnumerable<string> Urls { get; private set; }
        public string Index { get; private set; }
        public string Date { get; private set; }
        public string Region { get; private set; }
        public string Tag { get; private set; }

        public YandexUpdateModel(string name, string key_type, string category, IEnumerable<string> urls, string index, string date, string region, string tag)
        {
            Region = region;
            Tag = tag;
            Name = name;
            Key_type = key_type;
            Category = category;
            Urls = urls;
            Index = index;
            Date = date;
        }
    }

    public static class UpdateModelExtensions
    {
        public static IEnumerable<UpdateModel> MapToUpdateModels(this IEnumerable<YandexUpdateModel> yandexUpdates)
        {
            return yandexUpdates.Select(y => new UpdateModel
            {
                Name = y.Name,
                Key_type = y.Key_type,
                Category = y.Category,
                Urls = y.Urls,
                Index = y.Index,
                Date = y.Date,
                Region = y.Region,
                Tag = y.Tag
            });
        }

        public static Result<(IEnumerable<UpdateModel> AllLicenses, IEnumerable<UpdateModel> FilteredLicenses)> ApplyFilter(IEnumerable<UpdateModel> models, LicenseInfoModel licenseInfo)
        {
            string[] data = licenseInfo.Content?.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.RemoveEmptyEntries) ?? Array.Empty<string>();

            var allNsi = models.Where(x => x.Category == "Базы НСИ").ToList();
            var matchedNsi = allNsi.Where(item => ParseHelpers.FindNsi(data, item.Name)).ToList();
            var remainingNsi = allNsi.Except(matchedNsi).ToList();

            var prices = new List<UpdateModel>();
            var pResult = ParseHelpers.FindPrices(data);
            var allPrices = models.Where(x => x.Category == "Справочники цен").ToList();
            if (pResult.Count > 0)
            {
                foreach (var priceUpdate in allPrices)
                {
                    var r = pResult.FirstOrDefault(x => x.Name == priceUpdate.Index);
                    if (r is not null && ParseHelpers.TryParseDate(priceUpdate.Date, out DateTime date))
                    {
                        foreach (var d in r.Dates)
                        {
                            if (d.Item1 <= date && date <= d.Item2)
                            {
                                prices.Add(priceUpdate);
                            }
                        }
                    }
                }

                foreach (var priceUpdate in prices)
                {
                    allPrices.Remove(priceUpdate);
                }
            }

            var allLicenses = new List<UpdateModel>();
            allLicenses.AddRange(remainingNsi);
            allLicenses.AddRange(allPrices);

            var licenseTypeResult = ParseHelpers.FindLicenseType(data);
            if (licenseTypeResult.IsFailure)
            {
                return Result.Failure<(IEnumerable<UpdateModel> AllLicenses, IEnumerable<UpdateModel> FilteredLicenses)>(licenseTypeResult.Error);
            }

            var a0 = models.Where(x => x.Key_type == licenseTypeResult.Value && x.Category == "A0" && licenseInfo.A0LicenseExpAt != default).ToList();
            var pir = models.Where(x => x.Key_type == licenseTypeResult.Value && x.Category == "ПИР" && licenseInfo.PIRLicenseExpAt != default).ToList();
            var tables = models.Where(x => x.Category == "Таблицы").ToList();
            var indexes = models.Where(x => x.Category == "Индексы к ФЕР/ТЕР").ToList();

            var filteredCollection = new List<UpdateModel>();
            filteredCollection.AddRange(a0.UpdateText(licenseInfo.A0LicenseExpAt));
            filteredCollection.AddRange(pir.UpdateText(licenseInfo.PIRLicenseExpAt));
            filteredCollection.AddRange(matchedNsi);
            filteredCollection.AddRange(prices);
            filteredCollection.AddRange(tables);
            filteredCollection.AddRange(indexes);
            MarkLatestInTag(allLicenses);
            MarkLatestInTag(filteredCollection);
            MarkNewestYear(allLicenses);
            MarkNewestYear(filteredCollection);
            return (allLicenses, filteredCollection);
        }

        // Отмечает элементы самого свежего года внутри каждой группы (регион справочников, индексы)
        private static void MarkNewestYear(IEnumerable<UpdateModel> updates)
        {
            var groups = updates
                .Where(x => x.GroupYear.HasValue)
                .GroupBy(x => x.Category == "Справочники цен" ? x.Category + "|" + x.Group1 : x.Category);

            foreach (var group in groups)
            {
                var newestYear = group.Max(x => x.GroupYear.Value);
                foreach (var update in group)
                {
                    update.IsNewestYear = update.GroupYear == newestYear;
                }
            }
        }

        // Обновления в списке идут от новых к старым, поэтому последняя версия — первая с этим тегом
        private static void MarkLatestInTag(IEnumerable<UpdateModel> updates)
        {
            var seenTags = new HashSet<string>();
            foreach (var update in updates.Where(x => !string.IsNullOrWhiteSpace(x.Tag)))
            {
                update.IsLatestInTag = seenTags.Add(update.Tag);
            }
        }

        private static List<UpdateModel> UpdateText(this List<UpdateModel> updates, DateTime licenseExpDate)
        {
            foreach (var update in updates)
            {
                if (ParseHelpers.TryParseDate(update.Date, out DateTime updateDate))
                {
                    if (updateDate > licenseExpDate)
                    {
                        update.Name += " (Лицензия истекла)";
                    }
                }
            }

            return updates;
        }
    }
}
