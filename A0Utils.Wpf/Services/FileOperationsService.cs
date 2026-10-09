using A0Utils.Wpf.Helpers;
using A0Utils.Wpf.Models;
using CSharpFunctionalExtensions;
using Serilog;
using System;
using System.Collections.Generic;
using System.IO;

namespace A0Utils.Wpf.Services
{
    public sealed class FileOperationsService
    {
        public bool IsFolderExist(string path) => System.IO.Directory.Exists(path);

        public IEnumerable<LicenseModel> FindAllLicFiles(string path)
        {
            try
            {
                return GetAllIslFiles(path).MapToLicenseModel();
            }
            catch (DirectoryNotFoundException ex)
            {
                Log.Error(ex, "Директория не найдена");
                return [];
            }
            catch (UnauthorizedAccessException ex)
            {
                Log.Error(ex, "Отсутсвуют разрешения на доступ к файлу");
                return [];
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Ошибка при поиске файлов лицензии");
                return [];
            }
        }

        public Result CopyToAllFolders(string downloadLicensePath, IEnumerable<string> destinationDirectories)
        {
            var errors = new List<string>();
            foreach (var destinationDir in destinationDirectories)
            {
                try
                {
                    string destinationFile = Path.Combine(destinationDir, Path.GetFileName(downloadLicensePath));

                    if (File.Exists(destinationFile))
                    {
                        var attributes = File.GetAttributes(destinationFile);
                        if (attributes.HasFlag(FileAttributes.ReadOnly))
                        {
                            attributes &= ~FileAttributes.ReadOnly;
                            File.SetAttributes(destinationFile, attributes);
                        }
                    }

                    File.Copy(downloadLicensePath, destinationFile, true); // true allows overwriting
                }
                catch (UnauthorizedAccessException ex)
                {
                    Log.Error(ex, "Отсутствует разрешение на запись файла");
                    errors.Add("Недостаточно прав для копирования лицензии.\n" +
                        "Закройте «Утилиты для А0», нажмите правой кнопкой мыши на ярлык программы " +
                        "и выберите «Запуск от имени администратора». Затем повторите операцию.");
                }
                catch (IOException ex)
                {
                    Log.Error(ex, "Ошибка доступа к файлу лицензии");
                    errors.Add("Не удалось скопировать лицензию. Проверьте наличие файла и папки назначения, " +
                        "свободное место и не занят ли файл другой программой.");
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Не удалось скопировать лицензию");
                   errors.Add("Не удалось скопировать лицензию. См. логи");
                }
            }

            if (errors.Count > 0)
            {
                var errorMessage = string.Join(", ", errors);
                return Result.Failure($"При копировании возникли ошибки: {errorMessage}");
            }

            return Result.Success();
        }

        private static IEnumerable<string> GetAllIslFiles(string rootPath)
        {
            foreach (var file in Directory.EnumerateFiles(rootPath, "*", SearchOption.AllDirectories))
            {
                if (string.Equals(Path.GetExtension(file), ".isl", StringComparison.OrdinalIgnoreCase))
                {
                    yield return file;
                }
            }
        }
    }
}
