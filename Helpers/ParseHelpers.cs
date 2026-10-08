using A0Utils.Wpf.Models;
using CSharpFunctionalExtensions;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;

namespace A0Utils.Wpf.Helpers
{
    public static class ParseHelpers
    {
        // Даты в файлах лицензий (01.02.2026) и в списке обновлений (25-08-2026) всегда в формате
        // "день, месяц, год". Читаем их строго по формату, чтобы результат не зависел
        // от региональных настроек Windows у клиента.
        private static readonly string[] DateFormats = { "dd.MM.yyyy", "dd-MM-yyyy", "d.M.yyyy", "d-M-yyyy" };

        public static bool TryParseDate(string value, out DateTime result)
        {
            return DateTime.TryParseExact(value?.Trim(), DateFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out result);
        }

        public static Result<DateTime> FindA0LicenseExp(string data)
        {
            Match match = Regex.Match(data, @"\[А0\]\s*Поддержка до:\s*(\d{2}\.\d{2}\.\d{4})");

            if (match.Success)
            {
                if (TryParseDate(match.Groups[1].Value, out DateTime result))
                {
                    return result;
                }
                else
                {
                    return Result.Failure<DateTime>("Failed to parse date.");
                }
            }
            else
            {
                return default;
            }
        }

        public static Result<DateTime> FindPIRLicenseExp(string data)
        {
            Match match = Regex.Match(data, @"\[ПИР\]\s*Поддержка до:\s*(\d{2}\.\d{2}\.\d{4})");

            if (match.Success)
            {
                if (TryParseDate(match.Groups[1].Value, out DateTime result))
                {
                    return result;
                }
                else
                {
                    return Result.Failure<DateTime>("Failed to parse date.");
                }
            }
            else
            {
                return default;
            }
        }

        public static Result<string> FindLicenseType(string[] data)
        {
            string pattern = @"Sentinel(?:\s+HL)?";
            Regex regex = new Regex(pattern, RegexOptions.Compiled);

            foreach (string line in data)
            {
                Match match = regex.Match(line);
                if (match.Success)
                {
                    return Result.Success(match.Value);
                }
            }

            return Result.Failure<string>("Failed to parse license type.");
        }

        public static bool FindNsi(string[] data, string template)
        {
            string pattern = @"^\s*" + Regex.Escape(template);
            Regex regex = new Regex(pattern, RegexOptions.Compiled | RegexOptions.Multiline);

            foreach (string line in data)
            {
                Match match = regex.Match(line);
                if (match.Success)
                {
                    return true;
                }
            }

            return false;
        }

        public static List<PriceModel> FindPrices(string[] data)
        {
            string fileContent = string.Join("\n", data);
            string patternBlock = @"Лицензированы справочники:\s*((?:.|\r?\n)*?)(?=\n\S|$)";
            Match matchBlock = Regex.Match(fileContent, patternBlock, RegexOptions.Singleline);
            var priceModels = new List<PriceModel>();
            if (matchBlock != null && matchBlock.Success)
            {
                string block = matchBlock.Groups[1].Value;
                
                var entryRegex = new Regex(@"Справочник цен\s+""([^""]+)""(.*?)(?=Справочник цен|$)", RegexOptions.Singleline);
                var dateRegex = new Regex(@"с:\s*(\d{2}\.\d{2}\.\d{4})\s*до:\s*(\d{2}\.\d{2}\.\d{4})");

                foreach (Match entry in entryRegex.Matches(block))
                {
                    var model = new PriceModel
                    {
                        Name = entry.Groups[1].Value,
                        Dates = new List<(DateTime, DateTime)>()
                    };

                    foreach (Match dateMatch in dateRegex.Matches(entry.Groups[2].Value))
                    {
                        if (TryParseDate(dateMatch.Groups[1].Value, out DateTime start) &&
                            TryParseDate(dateMatch.Groups[2].Value, out DateTime end))
                        {
                            model.Dates.Add((start, end));
                        }
                    }

                    priceModels.Add(model);
                }                
            }

            return priceModels;
        }
    }
}
