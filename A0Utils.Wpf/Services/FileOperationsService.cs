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
                        try
                        {
                            var attributes = File.GetAttributes(destinationFile);
                            if (attributes.HasFlag(FileAttributes.ReadOnly))
                            {
                                attributes &= ~FileAttributes.ReadOnly; // Убираем атрибут ReadOnly
                                File.SetAttributes(destinationFile, attributes);
                            }
                        }
                        catch (Exception ex)
                        {
                            Log.Error(ex, "Не удалось изменить атрибуты файла");
                            errors.Add("Не удалось изменить атрибуты файла. Попробуйте запустить программу с правами администратора");
                        }
                    }

                    File.Copy(downloadLicensePath, destinationFile, true); // true allows overwriting
                }
                catch (UnauthorizedAccessException ex)
                {
                    Log.Error(ex, "Отсутствует разрешение на запись файла");
                    errors.Add("Отсутствует разрешение на запись файла. Попробуйте запустить программу с правами администратора");
                }
                catch (IOException ex)
                {
                    Log.Error(ex, "Фаил лицензии занят");
                    errors.Add("Фаил лицензии занят. Попробуйте запустить программу с правами администратора");
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
