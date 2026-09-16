using System;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Windows.Forms;

namespace Md2OneNote.AddIn
{
    /// <summary>
    /// The modeless progress window: which file, which step, how far, and a Cancel button
    /// (FR-4, FR-5, NFR-19). UI only; <see cref="ProgressWindow"/> owns the thread it lives on.
    /// </summary>
    internal sealed class ProgressForm : Form
    {
        private readonly Label _headline;
        private readonly Label _file;
        private readonly Label _step;
        private readonly ProgressBar _bar;
        private readonly Button _cancel;

        /// <summary>Raised once, on the form's thread, when the user asks to stop.</summary>
        public event EventHandler CancelRequested;

        public ProgressForm()
        {
            Text = "Md2OneNote";
            Font = SystemFonts.MessageBoxFont;
            AutoScaleMode = AutoScaleMode.Dpi;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            ControlBox = true;
            ClientSize = new Size(440, 150);

            _headline = new Label
            {
                Text = "Preparing import…",
                Font = new Font(Font, FontStyle.Bold),
                AutoSize = false,
                Bounds = new Rectangle(16, 14, 408, 20)
            };

            _file = new Label
            {
                AutoSize = false,
                AutoEllipsis = true,
                Bounds = new Rectangle(16, 38, 408, 20)
            };

            _step = new Label
            {
                AutoSize = false,
                ForeColor = SystemColors.GrayText,
                Bounds = new Rectangle(16, 60, 408, 20)
            };

            _bar = new ProgressBar
            {
                Style = ProgressBarStyle.Continuous,
                Minimum = 0,
                Maximum = 1,
                Value = 0,
                Bounds = new Rectangle(16, 88, 408, 18)
            };

            _cancel = new Button
            {
                Text = "Cancel",
                Bounds = new Rectangle(336, 114, 88, 26)
            };
            _cancel.Click += (s, e) => RequestCancel();

            Controls.AddRange(new Control[] { _headline, _file, _step, _bar, _cancel });
            CancelButton = _cancel;
        }

        /// <summary>Whether the user has asked to stop; set on the form's thread only.</summary>
        public bool IsCancelRequested { get; private set; }

        public void ShowFileStarted(int index, int total, string path)
        {
            _bar.Maximum = Math.Max(1, total);
            _bar.Value = Math.Min(index, _bar.Maximum);
            _headline.Text = string.Format(CultureInfo.CurrentCulture, "Importing file {0} of {1}", index + 1, total);
            _file.Text = Path.GetFileName(path);
            _step.Text = string.Empty;
        }

        public void ShowStep(string message)
        {
            _step.Text = message ?? string.Empty;
        }

        public void ShowFileFinished(int completed)
        {
            _bar.Value = Math.Min(completed, _bar.Maximum);
        }

        /// <summary>
        /// Closing with the X means the same as Cancel: the import stops at the next checkpoint
        /// and the window stays until it has, so the summary has somewhere to come from.
        /// </summary>
        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (e.CloseReason == CloseReason.UserClosing && !AllowClose)
            {
                e.Cancel = true;
                RequestCancel();
                return;
            }

            base.OnFormClosing(e);
        }

        /// <summary>Set by the owner when the import has finished and the window may go.</summary>
        public bool AllowClose { get; set; }

        private void RequestCancel()
        {
            if (IsCancelRequested)
            {
                return;
            }

            IsCancelRequested = true;
            _cancel.Enabled = false;
            _cancel.Text = "Cancelling…";
            _step.Text = "Finishing the current file, then stopping.";

            var handler = CancelRequested;
            if (handler != null)
            {
                handler(this, EventArgs.Empty);
            }
        }
    }
}
