using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Windows.Forms;
using Md2OneNote.AddIn.Diagnostics;

namespace Md2OneNote.AddIn
{
    /// <summary>
    /// The About dialog: version and environment, the project links, and the two things a user
    /// with a problem needs — the log folder and a diagnostic bundle to attach (NFR-17).
    /// </summary>
    /// <remarks>
    /// Nothing here is a setting. Built in code rather than with the designer so it stays one
    /// readable file; it is shown on a dedicated STA thread like the file picker, owned by
    /// OneNote's window (see <c>Connect.PickMarkdownFiles</c> for why).
    /// </remarks>
    internal sealed class AboutForm : Form
    {
        private readonly EnvironmentReport _report;
        private readonly string _logPath;

        public AboutForm(EnvironmentReport report, string logPath)
        {
            _report = report;
            _logPath = logPath;

            Text = "About Md2OneNote";
            Font = SystemFonts.MessageBoxFont;
            AutoScaleMode = AutoScaleMode.Dpi;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(470, 330);

            var logo = new PictureBox
            {
                Image = LoadLogo(),
                SizeMode = PictureBoxSizeMode.Zoom,
                Bounds = new Rectangle(16, 16, 64, 64)
            };

            var title = new Label
            {
                Text = "Md2OneNote",
                Font = new Font(Font.FontFamily, Font.Size + 6, FontStyle.Bold),
                AutoSize = true,
                Location = new Point(94, 16)
            };

            var version = new Label
            {
                Text = "Version " + report.AddInVersion,
                AutoSize = true,
                Location = new Point(96, 48)
            };

            var tagline = new Label
            {
                Text = "Imports Markdown files as styled OneNote pages.",
                AutoSize = true,
                Location = new Point(96, 66)
            };

            var details = new TextBox
            {
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical,
                Font = new Font("Consolas", Font.Size),
                Text = report.ToString().TrimEnd().Replace("\n", "\r\n").Replace("\r\r", "\r"),
                Bounds = new Rectangle(16, 96, 438, 112),
                BackColor = SystemColors.Window
            };

            var links = new FlowLayoutPanel
            {
                Bounds = new Rectangle(12, 220, 446, 24),
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false
            };
            links.Controls.Add(Link("Project page", ProductLinks.Project));
            links.Controls.Add(Link("Report a problem", ProductLinks.Issues));
            links.Controls.Add(Link("Buy me a coffee", ProductLinks.Sponsor));

            var openLog = new Button
            {
                Text = "Open log folder",
                Bounds = new Rectangle(16, 286, 130, 28)
            };
            openLog.Click += (s, e) => Guard(OpenLogFolder);

            var saveBundle = new Button
            {
                Text = "Save diagnostics…",
                Bounds = new Rectangle(154, 286, 150, 28)
            };
            saveBundle.Click += (s, e) => Guard(SaveBundle);

            var close = new Button
            {
                Text = "Close",
                DialogResult = DialogResult.Cancel,
                Bounds = new Rectangle(366, 286, 88, 28)
            };

            Controls.AddRange(new Control[] { logo, title, version, tagline, details, links, openLog, saveBundle, close });
            AcceptButton = close;
            CancelButton = close;
        }

        private static LinkLabel Link(string text, string url)
        {
            var link = new LinkLabel { Text = text, AutoSize = true, Margin = new Padding(4, 4, 16, 4) };
            link.LinkClicked += (s, e) => Guard(() => Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }));
            return link;
        }

        private void OpenLogFolder()
        {
            var folder = Path.GetDirectoryName(_logPath);
            if (string.IsNullOrEmpty(folder))
            {
                return;
            }

            Directory.CreateDirectory(folder);
            Process.Start("explorer.exe", "\"" + folder + "\"");
        }

        private void SaveBundle()
        {
            using (var dialog = new SaveFileDialog
            {
                Title = "Save diagnostic bundle",
                Filter = "Zip archive (*.zip)|*.zip",
                FileName = DiagnosticBundle.SuggestedFileName(),
                InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                OverwritePrompt = true
            })
            {
                if (dialog.ShowDialog(this) != DialogResult.OK)
                {
                    return;
                }

                DiagnosticBundle.Write(dialog.FileName, _logPath, _report);
                Process.Start("explorer.exe", "/select,\"" + dialog.FileName + "\"");
            }
        }

        /// <summary>Whatever goes wrong in a click stays in this dialog.</summary>
        private static void Guard(Action action)
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Md2OneNote", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private static Image LoadLogo()
        {
            try
            {
                var name = typeof(AboutForm).Namespace + ".Images.Logo64.png";
                using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(name))
                {
                    if (stream == null)
                    {
                        return null;
                    }

                    using (var copy = new MemoryStream())
                    {
                        stream.CopyTo(copy);
                        copy.Position = 0;
                        return new Bitmap(copy);
                    }
                }
            }
            catch (Exception)
            {
                return null;
            }
        }
    }
}
