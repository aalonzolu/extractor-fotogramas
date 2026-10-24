using System;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;

namespace ExtractorFotogramas
{
    public sealed class PlaybackTimePicker : TableLayoutPanel
    {
        const int CaptionWidth = 70;

        readonly Label caption = new Label();
        readonly NumericUpDown hours = new NumericUpDown();
        readonly NumericUpDown minutes = new NumericUpDown();
        readonly NumericUpDown seconds = new NumericUpDown();
        readonly Label equivalent = new Label();

        public PlaybackTimePicker(Font font, Color mutedColor, Color accentColor)
        {
            ColumnCount = 5;
            RowCount = 2;
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            Margin = new Padding(0, 6, 0, 0);

            caption.AutoSize = false;
            caption.Width = CaptionWidth;
            caption.Height = 30;
            caption.TextAlign = ContentAlignment.MiddleLeft;
            caption.Font = new Font(font, FontStyle.Bold);
            caption.Margin = new Padding(0);

            equivalent.AutoSize = true;
            equivalent.Font = new Font(font.FontFamily, 12F, FontStyle.Bold);
            equivalent.ForeColor = accentColor;
            equivalent.Margin = new Padding(0, 4, 0, 0);

            Controls.Add(new Label { AutoSize = false, Width = CaptionWidth, Height = 1, Margin = new Padding(0) }, 0, 0);
            Controls.Add(SmallCaption("Horas", font, mutedColor), 1, 0);
            Controls.Add(SmallCaption("Minutos", font, mutedColor), 2, 0);
            Controls.Add(SmallCaption("Segundos", font, mutedColor), 3, 0);
            Controls.Add(SmallCaption("Equivale a", font, mutedColor), 4, 0);
            Controls.Add(caption, 0, 1);
            Controls.Add(ConfigureBox(hours, 99, font), 1, 1);
            Controls.Add(ConfigureBox(minutes, 59, font), 2, 1);
            Controls.Add(ConfigureBox(seconds, 59, font), 3, 1);
            Controls.Add(equivalent, 4, 1);
            UpdateEquivalent();
        }

        public event EventHandler EnterPressed;

        public string Caption
        {
            get { return caption.Text; }
            set { caption.Text = value; }
        }

        public double TotalSeconds
        {
            get { return (double)(hours.Value * 3600 + minutes.Value * 60 + seconds.Value); }
            set
            {
                var whole = (long)Math.Max(0, value);
                hours.Value = Math.Min(hours.Maximum, whole / 3600);
                minutes.Value = whole % 3600 / 60;
                seconds.Value = whole % 60;
            }
        }

        public decimal MaximumHours
        {
            set { hours.Maximum = value; }
        }

        public void FocusFirstBox()
        {
            hours.Focus();
        }

        NumericUpDown ConfigureBox(NumericUpDown box, int maximum, Font font)
        {
            box.Minimum = 0;
            box.Maximum = maximum;
            box.Width = 72;
            box.TextAlign = HorizontalAlignment.Center;
            box.Font = new Font(font.FontFamily, 12F);
            box.Margin = new Padding(0, 0, 10, 0);
            box.Enter += (sender, e) => box.Select(0, box.Text.Length);
            box.ValueChanged += (sender, e) => UpdateEquivalent();
            box.TextChanged += (sender, e) => UpdateEquivalent();
            box.KeyDown += (sender, e) =>
            {
                if (e.KeyCode != Keys.Enter) return;
                e.SuppressKeyPress = true;
                var handler = EnterPressed;
                if (handler != null) handler(this, EventArgs.Empty);
            };
            return box;
        }

        void UpdateEquivalent()
        {
            var total = TypedValue(hours) * 3600 + TypedValue(minutes) * 60 + TypedValue(seconds);
            equivalent.Text = "segundo " + total;
        }

        static decimal TypedValue(NumericUpDown box)
        {
            decimal typed;
            if (!decimal.TryParse(box.Text, NumberStyles.None, CultureInfo.CurrentCulture, out typed))
            {
                return box.Value;
            }
            return Math.Max(box.Minimum, Math.Min(box.Maximum, typed));
        }

        static Label SmallCaption(string text, Font font, Color color)
        {
            return new Label
            {
                Text = text,
                AutoSize = true,
                ForeColor = color,
                Font = new Font(font.FontFamily, 9F),
                Margin = new Padding(0, 0, 0, 2),
            };
        }
    }
}
