using A0Utils.Wpf.Helpers;
using A0Utils.Wpf.Services;
using CSharpFunctionalExtensions;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace A0Utils.Wpf.Models
{
    public sealed class DownloadModel
    {
        public string LicensePath { get; set; }
        public string DescriptionPath { get; set; }
    }

    public static class DownloadModelExtentions
    {
        public static Result CopyToAllFolders(this DownloadModel model, FileOperationsService fileOperationsService, string a0InstallationPath)
        {
            var licenses = fileOperationsService.FindAllLicFiles(a0InstallationPath);
            IList<string> destinationDirs = licenses.Select(x => x.DirectoryPath).Distinct().ToList();
            if (destinationDirs.Count == 0)
            {
                destinationDirs = Directory.GetDirectories(a0InstallationPath, "bin", SearchOption.AllDirectories);
            }

            var copyLicenseResult = fileOperationsService.CopyToAllFolders(model.LicensePath, destinationDirs);
            var copyDescriptionResult = copyLicenseResult.IsSuccess
                ? fileOperationsService.CopyToAllFolders(model.DescriptionPath, destinationDirs)
                : Result.Success();

            if (copyLicenseResult.IsSuccess && copyDescriptionResult.IsSuccess)
            {
                return Result.Success();
            }

            // Файлы лицензий в C:\ProgramData\InfoStroy обычно создаёт установщик А0 от администратора,
            // поэтому обычный пользователь может их читать, но не перезаписывать.
            // В этом случае копируем с правами администратора, не перезапуская всю программу.
            var files = new[] { model.LicensePath, model.DescriptionPath }.Where(x => !string.IsNullOrEmpty(x)).ToList();
            var hasAccess = files.All(x => fileOperationsService.CanWriteToAllFolders(Path.GetFileName(x), destinationDirs));
            if (hasAccess)
            {
                return Result.Failure(copyLicenseResult.IsFailure ? copyLicenseResult.Error : copyDescriptionResult.Error);
            }

            var confirm = MessageDialogHelper.Confirm(
                "Нет прав на запись лицензии в папку программы А0.\n" +
                "Скопировать лицензию с правами администратора? Windows попросит подтверждение.");
            if (confirm != DialogResult.Yes)
            {
                return Result.Failure("Лицензия не скопирована: нет прав на запись в папку программы А0");
            }

            return fileOperationsService.CopyToAllFoldersElevated(files, destinationDirs, a0InstallationPath);
        }
    }
}
