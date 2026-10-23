using System;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;

namespace MonitorShade
{
    // SDK assemblies are embedded; the Evergreen Chromium runtime remains system-managed.
    internal static class WebRuntime
    {
        internal static readonly string DirectoryPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MonitorShade", "WebView2SDK", "1.0.4258.31");
        internal static void Register()
        {
            AppDomain.CurrentDomain.AssemblyResolve += delegate(object sender, ResolveEventArgs e) {
                string name = new AssemblyName(e.Name).Name;
                if (name != "Microsoft.Web.WebView2.Core" && name != "Microsoft.Web.WebView2.Wpf") return null;
                return Assembly.LoadFrom(Extract(name + ".dll"));
            };
        }
        internal static string Extract(string name)
        {
            byte[] bytes;
            using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("MonitorShade.WebRuntime." + name))
            using (var memory = new MemoryStream()) { stream.CopyTo(memory); bytes = memory.ToArray(); }
            Directory.CreateDirectory(DirectoryPath);
            string path = Path.Combine(DirectoryPath, name);
            using (var sha = SHA256.Create())
            {
                if (File.Exists(path) && Convert.ToBase64String(sha.ComputeHash(File.ReadAllBytes(path))) == Convert.ToBase64String(sha.ComputeHash(bytes))) return path;
            }
            string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            File.WriteAllBytes(temporary, bytes);
            try { if (File.Exists(path)) File.Replace(temporary, path, null); else File.Move(temporary, path); }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
            return path;
        }
    }
}
