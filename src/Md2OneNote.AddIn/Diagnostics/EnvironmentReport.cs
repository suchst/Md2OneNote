using System;
using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Text;
using Md2OneNote.Diagrams;
using Microsoft.Win32;

namespace Md2OneNote.AddIn.Diagnostics
{
    /// <summary>
    /// The versions and settings a bug report needs: what the About dialog shows and what the
    /// diagnostic bundle stamps into <c>environment.txt</c> (NFR-17).
    /// </summary>
    /// <remarks>
    /// Every value is collected on its own and falls back to "unknown". A report that cannot be
    /// produced because one registry key is missing would defeat its purpose.
    /// </remarks>
    internal sealed class EnvironmentReport
    {
        public string AddInVersion { get; private set; }
        public string OneNoteVersion { get; private set; }
        public string WebView2Version { get; private set; }
        public string Windows { get; private set; }
        public string Culture { get; private set; }
        public string Host { get; private set; }
        public DateTime CollectedAt { get; private set; }

        public static EnvironmentReport Collect()
        {
            return new EnvironmentReport
            {
                AddInVersion = Try(ReadAddInVersion),
                OneNoteVersion = Try(ReadOneNoteVersion),
                WebView2Version = Try(() => WebViewDiagramRenderer.AvailableRuntimeVersion() ?? "not installed"),
                Windows = Try(ReadWindows),
                Culture = Try(() => CultureInfo.CurrentUICulture.Name + " (UI), " + CultureInfo.CurrentCulture.Name + " (format)"),
                Host = Try(ReadHost),
                CollectedAt = DateTime.Now
            };
        }

        public override string ToString()
        {
            var text = new StringBuilder();
            text.Append("Md2OneNote ").AppendLine(AddInVersion);
            text.Append("OneNote:   ").AppendLine(OneNoteVersion);
            text.Append("WebView2:  ").AppendLine(WebView2Version);
            text.Append("Windows:   ").AppendLine(Windows);
            text.Append("Culture:   ").AppendLine(Culture);
            text.Append("Host:      ").AppendLine(Host);
            text.Append("Collected: ").AppendLine(CollectedAt.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
            return text.ToString();
        }

        /// <summary>The product version as people see it: <c>1.0.0-preview.1</c>, no commit hash.</summary>
        public static string ReadAddInVersion()
        {
            var assembly = typeof(EnvironmentReport).Assembly;
            var informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>();
            var version = informational == null ? assembly.GetName().Version.ToString() : informational.InformationalVersion;

            var plus = version.IndexOf('+');
            return plus < 0 ? version : version.Substring(0, plus);
        }

        private static string ReadOneNoteVersion()
        {
            // The add-in runs in a surrogate of the same bitness as OneNote, so the main module
            // of the OneNote process is readable from here.
            var processes = Process.GetProcessesByName("ONENOTE");
            try
            {
                if (processes.Length == 0)
                {
                    return "not running";
                }

                var info = processes[0].MainModule.FileVersionInfo;
                return info.FileVersion + (Environment.Is64BitProcess ? " (64-bit)" : " (32-bit)");
            }
            finally
            {
                foreach (var process in processes)
                {
                    process.Dispose();
                }
            }
        }

        private static string ReadWindows()
        {
            using (var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion"))
            {
                if (key == null)
                {
                    return Environment.OSVersion.VersionString;
                }

                var product = key.GetValue("ProductName") as string ?? "Windows";
                var display = key.GetValue("DisplayVersion") as string ?? key.GetValue("ReleaseId") as string ?? "";
                var build = key.GetValue("CurrentBuildNumber") as string ?? "";
                var revision = key.GetValue("UBR");

                int buildNumber;
                if (int.TryParse(build, NumberStyles.Integer, CultureInfo.InvariantCulture, out buildNumber) && buildNumber >= 22000)
                {
                    // The registry still says "Windows 10" on Windows 11; the build number does not.
                    product = product.Replace("Windows 10", "Windows 11");
                }

                return string.Format(
                    CultureInfo.InvariantCulture,
                    "{0} {1} (build {2}{3})",
                    product,
                    display,
                    build,
                    revision == null ? "" : "." + revision);
            }
        }

        private static string ReadHost()
        {
            using (var process = Process.GetCurrentProcess())
            {
                return process.ProcessName + " pid " + process.Id.ToString(CultureInfo.InvariantCulture)
                    + ", .NET " + Environment.Version
                    + (Environment.Is64BitProcess ? ", 64-bit" : ", 32-bit");
            }
        }

        private static string Try(Func<string> read)
        {
            try
            {
                return read() ?? "unknown";
            }
            catch (Exception ex)
            {
                return "unknown (" + ex.GetType().Name + ")";
            }
        }
    }
}
