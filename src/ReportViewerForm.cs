using System.Drawing;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace ExtractorFotogramas
{
    public sealed class ReportViewerForm : Form
    {
        public ReportViewerForm(string reportPath)
        {
            SuspendLayout();
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            Font = new Font("Segoe UI", 10F);
            Text = "Reporte de hashes - " + MainForm.AppName;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(960, 560);
            MinimizeBox = false;
            ShowIcon = false;
            ShowInTaskbar = false;

            var report = new TextBox
            {
                Multiline = true,
                ReadOnly = true,
                WordWrap = false,
                ScrollBars = ScrollBars.Both,
                Dock = DockStyle.Fill,
                BackColor = Color.White,
                Font = new Font("Consolas", 10F),
            };
            using (var reader = new StreamReader(LongPathFileSystem.OpenRead(reportPath), Encoding.UTF8))
            {
                report.Text = reader.ReadToEnd();
            }
            Controls.Add(report);
            Shown += (sender, e) => report.Select(0, 0);
            ResumeLayout(false);
        }
    }
}
