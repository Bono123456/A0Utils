using A0Utils.Wpf.Helpers;
using A0Utils.Wpf.Models;
using CSharpFunctionalExtensions;
using Serilog;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;

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

        public const string CopyLicensesArgument = "--copy-licenses";

        // Проверяет, хватает ли прав записать файл с таким именем во все папки
        public bool CanWriteToAllFolders(string fileName, IEnumerable<string> destinationDirectories)
        {
            foreach (var destinationDir in destinationDirectories)
            {
                try
                {
                    var destinationFile = Path.Combine(destinationDir, fileName);
                    if (File.Exists(destinationFile))
                    {
                        using (new FileStream(destinationFile, FileMode.Open, FileAccess.Write, FileShare.ReadWrite)) { }
                    }
                    else
                    {
                        var probeFile = Path.Combine(destinationDir, Path.GetRandomFileName());
                        using (new FileStream(probeFile, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1, FileOptions.DeleteOnClose)) { }
                    }
                }
                catch (UnauthorizedAccessException)
                {
                    return false;
                }
                catch (Exception ex)
                {
                    // Файл занят другой программой и т.п. — это не проблема прав
                    Log.Error(ex, "Ошибка при проверке прав на запись в {Dir}", destinationDir);
                }
            }

            return true;
        }

        // Копирует файлы с правами администратора: запускает эту же программу с запросом UAC
        // только на время копирования, остальная работа идёт без прав администратора.
        // allowedRoot — папка А0 из настроек: админ-копия будет копировать только в неё.
        public Result CopyToAllFoldersElevated(IEnumerable<string> files, IEnumerable<string> destinationDirectories, string allowedRoot)
        {
            var jobFile = Path.Combine(Path.GetTempPath(), $"A0Utils_copy_{Guid.NewGuid():N}.txt");
            try
            {
                var lines = files.Select(x => "F|" + x).Concat(destinationDirectories.Select(x => "D|" + x));
                File.WriteAllLines(jobFile, lines);

                var startInfo = new ProcessStartInfo(Process.GetCurrentProcess().MainModule.FileName, $"{CopyLicensesArgument} \"{jobFile}\" \"{allowedRoot.TrimEnd('\\')}\"")
                {
                    Verb = "runas",
                    UseShellExecute = true
                };

                using (var process = Process.Start(startInfo))
                {
                    process.WaitForExit();
                    return process.ExitCode == 0
                        ? Result.Success()
                        : Result.Failure("Не удалось скопировать лицензию с правами администратора. См. логи");
                }
            }
            catch (Win32Exception ex)
            {
                // Пользователь отказался в окне UAC
                Log.Error(ex, "Запуск с правами администратора отменён");
                return Result.Failure("Копирование отменено: нет прав администратора");
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Ошибка при копировании с правами администратора");
                return Result.Failure("Не удалось скопировать лицензию с правами администратора. См. логи");
            }
            finally
            {
                try { File.Delete(jobFile); } catch { }
            }
        }

        // Выполняется во втором экземпляре программы, запущенном с правами администратора.
        // Список файлов лежит во временной папке, и его теоретически может подменить другая программа.
        // Поэтому копируем только файлы лицензий (.isl/.ild) и только в папки внутри папки А0,
        // которая передаётся напрямую в командной строке.
        public int RunElevatedCopyJob(string jobFile, string allowedRoot)
        {
            var root = Path.GetFullPath(allowedRoot).TrimEnd('\\') + "\\";
            var lines = File.ReadAllLines(jobFile);
            var files = lines.Where(x => x.StartsWith("F|")).Select(x => x.Substring(2)).ToList();
            var dirs = lines.Where(x => x.StartsWith("D|")).Select(x => x.Substring(2)).ToList();

            var badFile = files.FirstOrDefault(x => !IsLicenseFile(x) || !File.Exists(x));
            var badDir = dirs.FirstOrDefault(x => !(Path.GetFullPath(x).TrimEnd('\\') + "\\").StartsWith(root, StringComparison.OrdinalIgnoreCase));
            if (badFile != null || badDir != null)
            {
                Log.Error("Админ-копирование отклонено: файл {File}, папка {Dir}", badFile, badDir);
                return 2;
            }

            var failed = false;
            foreach (var file in files)
            {
                var result = CopyToAllFolders(file, dirs);
                if (result.IsFailure)
                {
                    Log.Error("Ошибка копирования {File}: {Error}", file, result.Error);
                    failed = true;
                }
            }

            return failed ? 1 : 0;
        }

        private static bool IsLicenseFile(string path)
        {
            var extension = Path.GetExtension(path);
            return string.Equals(extension, ".isl", StringComparison.OrdinalIgnoreCase)
                || string.Equals(extension, ".ild", StringComparison.OrdinalIgnoreCase);
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
