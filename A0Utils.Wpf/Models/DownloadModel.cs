using A0Utils.Wpf.Services;
using CSharpFunctionalExtensions;
using System.Collections.Generic;
using System.IO;
using System.Linq;

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

            return Result.Failure(copyLicenseResult.IsFailure ? copyLicenseResult.Error : copyDescriptionResult.Error);
        }
    }
}
