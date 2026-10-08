using System.Reflection;

namespace A0Utils.Wpf.Helpers
{
    public static class AssemblyHelpers
    {
        public static string[] GetAssemblyInfo()
        {
            var assembly = Assembly.GetExecutingAssembly();
            var version = assembly.GetName().Version.ToString();
            var copyright = assembly.GetCustomAttribute<AssemblyCopyrightAttribute>()?.Copyright;
            var company = assembly.GetCustomAttribute<AssemblyCompanyAttribute>()?.Company;
            return [version, copyright, company];
        }
    }
}
