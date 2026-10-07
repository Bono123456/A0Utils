using A0Utils.Wpf.Models;
using System.Collections.Generic;
using System.IO;

namespace A0Utils.Wpf.Helpers
{
    public static class PathHelpers
    {
        public static IEnumerable<LicenseModel> MapToLicenseModel(this IEnumerable<string> fullFilePaths)
        {
            foreach (var fullFilePath in fullFilePaths)
            {
                yield return new LicenseModel
                {
                    FileName = Path.GetFileName(fullFilePath),
                    DirectoryPath = Path.GetDirectoryName(fullFilePath)
                };
            }
        }
    }
}
