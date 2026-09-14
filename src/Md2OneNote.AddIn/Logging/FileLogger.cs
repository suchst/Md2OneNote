using System;
using System.Globalization;
using System.IO;
using System.Text;

namespace Md2OneNote.AddIn.Logging
{
    /// <summary>
    /// The add-in's log: <c>%LOCALAPPDATA%\Md2OneNote\log.txt</c>, size-capped with one rollover
    /// (DESIGN.md §10). Boundaries only — ribbon callbacks, gateway calls, diagram renders.
    /// </summary>
    /// <remarks>
    /// Every method swallows its own failures. A logger that throws inside a ribbon callback would
    /// take down the very handler whose failure it exists to record, and OneNote disables an add-in
    /// that throws during startup (NFR-2, NFR-3).
    /// </remarks>
    internal sealed class FileLogger
    {
        private const long MaxBytes = 1024 * 1024;

        private readonly object _gate = new object();
        private readonly string _path;

        public FileLogger(string path)
        {
            _path = path;
        }

        public static FileLogger Default()
        {
            var folder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Md2OneNote");

            return new FileLogger(Path.Combine(folder, "log.txt"));
        }

        public void Info(string message)
        {
            Write("INFO ", message);
        }

        public void Error(string message, Exception exception)
        {
            Write("ERROR", exception == null ? message : message + Environment.NewLine + exception);
        }

        private void Write(string level, string message)
        {
            try
            {
                lock (_gate)
                {
                    var folder = Path.GetDirectoryName(_path);
                    if (!string.IsNullOrEmpty(folder))
                    {
                        Directory.CreateDirectory(folder);
                    }

                    Roll();

                    var line = string.Format(
                        CultureInfo.InvariantCulture,
                        "{0:yyyy-MM-dd HH:mm:ss.fff} {1} {2}{3}",
                        DateTime.Now,
                        level,
                        message,
                        Environment.NewLine);

                    File.AppendAllText(_path, line, new UTF8Encoding(false));
                }
            }
            catch (Exception)
            {
                // Logging is never worth a failure of its own.
            }
        }

        /// <summary>One rollover, so the log cannot grow without bound but the previous run survives.</summary>
        private void Roll()
        {
            var info = new FileInfo(_path);
            if (!info.Exists || info.Length < MaxBytes)
            {
                return;
            }

            var previous = _path + ".1";
            if (File.Exists(previous))
            {
                File.Delete(previous);
            }

            File.Move(_path, previous);
        }
    }
}
