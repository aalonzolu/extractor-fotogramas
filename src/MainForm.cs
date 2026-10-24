using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ExtractorFotogramas
{
    public sealed class MainForm : Form
    {
        public const string AppName = "Extracción de fotogramas";
        public const string ToolName = "Extractor de Fotogramas";
        public const string WindowTitle = AppName;

        static readonly Color BrandColor = Color.FromArgb(22, 55, 94);
        static readonly Color BrandTextMuted = Color.FromArgb(190, 206, 226);
        static readonly Color PageBackground = Color.FromArgb(241, 244, 248);
        static readonly Color CardBorder = Color.FromArgb(210, 217, 226);
        static readonly Color DropZoneBorder = Color.FromArgb(140, 160, 186);
        static readonly Color DropZoneBackground = Color.FromArgb(247, 249, 252);
        static readonly Color MutedText = Color.FromArgb(90, 100, 115);
        static readonly Color DisabledButton = Color.FromArgb(205, 211, 219);
        static readonly Color SuccessBackground = Color.FromArgb(226, 243, 231);
        static readonly Color SuccessText = Color.FromArgb(20, 96, 48);
        static readonly Color WarningBackground = Color.FromArgb(255, 243, 219);
        static readonly Color WarningText = Color.FromArgb(138, 62, 10);
        static readonly Color ErrorBackground = Color.FromArgb(252, 232, 232);
        static readonly Color ErrorText = Color.FromArgb(155, 28, 28);

        const int CardPadding = 18;
        const int DropZonePadding = 14;
        const int NoticePadding = 12;
        const string VideoFilter =
            "Videos|*.mp4;*.avi;*.mov;*.mkv;*.wmv;*.mpg;*.mpeg;*.m4v;*.3gp;*.ts;*.dav;*.h264;*.264;*.h265;*.265;*.hevc"
            + "|Todos los archivos|*.*";

        enum Outcome { Success, Warning, Error, Cancelled }

        readonly Panel scroller = new Panel();
        readonly TableLayoutPanel content = new TableLayoutPanel();
        readonly Dictionary<Label, int> wrappingLabels = new Dictionary<Label, int>();

        readonly Label toolsMessage = new Label();
        readonly TableLayoutPanel dropZone = new TableLayoutPanel();
        readonly Label dropTitle = new Label();
        readonly Label dropDetails = new Label();
        readonly Label dropAction = new Label();
        readonly TableLayoutPanel rawStreamPanel = new TableLayoutPanel();
        readonly NumericUpDown rawFrameRateBox = new NumericUpDown();

        readonly RadioButton momentsOption = new RadioButton();
        readonly RadioButton wholeVideoOption = new RadioButton();
        readonly TableLayoutPanel momentsPanel = new TableLayoutPanel();
        readonly Label wholeVideoDescription = new Label();
        readonly CheckBox rangeOption = new CheckBox();
        PlaybackTimePicker startPicker;
        PlaybackTimePicker endPicker;
        readonly Button addSelectionButton;
        readonly Label selectionMessage = new Label();
        readonly Label emptySelectionHint = new Label();
        readonly TableLayoutPanel selectionRow = new TableLayoutPanel();
        readonly ListBox selectionList = new ListBox();
        readonly Button removeSelectionButton;

        readonly Label estimateLabel = new Label();
        readonly Button extractButton;

        readonly Label progressStep = new Label();
        readonly ProgressBar progressBar = new ProgressBar();
        readonly Label progressDetail = new Label();
        readonly Button cancelButton;

        TableLayoutPanel toolsCard;
        TableLayoutPanel progressCard;
        TableLayoutPanel resultCard;
        readonly Label resultTitle = new Label();
        readonly Label resultMessage = new Label();
        readonly Button openFolderButton;
        readonly Button viewReportButton;
        readonly Button newExtractionButton;

        readonly LinkLabel detailsLink = new LinkLabel();
        readonly TextBox logBox = new TextBox();

        readonly List<TimeRange> ranges = new List<TimeRange>();
        string ffmpegPath;
        string ffprobePath;
        string videoPath;
        string videoError;
        VideoInfo video;
        long? freeBytes;
        bool hasEnoughSpace = true;
        bool probing;
        bool running;
        bool cancelRequested;
        Process ffmpegProcess;
        string outputDirectory;
        string reportPath;
        long expectedFrames;
        long reportedFrames;
        int decodingProblems;
        int currentStep;
        bool extractingWholeVideo;
        float dpiScale = 1F;
        int wrappedWidth;

        public MainForm(string initialVideoPath)
        {
            addSelectionButton = SecondaryButton("Agregar momento");
            removeSelectionButton = SecondaryButton("Quitar");
            extractButton = PrimaryButton("Extraer fotogramas");
            cancelButton = SecondaryButton("Cancelar");
            openFolderButton = SecondaryButton("Abrir carpeta");
            viewReportButton = SecondaryButton("Ver reporte");
            newExtractionButton = SecondaryButton("Nueva extracción");

            SuspendLayout();
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            Font = new Font("Segoe UI", 10F);
            Text = WindowTitle;
            BackColor = PageBackground;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(760, 820);
            MinimumSize = new Size(620, 560);

            scroller.Dock = DockStyle.Fill;
            scroller.AutoScroll = true;
            scroller.Padding = new Padding(18, 16, 18, 16);

            content.Dock = DockStyle.Top;
            content.AutoSize = true;
            content.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            content.ColumnCount = 1;
            content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            content.Resize += (sender, e) => AdjustWrapping();
            scroller.Controls.Add(content);

            AddSection(BuildToolsCard());
            AddSection(BuildVideoCard());
            AddSection(BuildSelectionCard());
            AddSection(BuildExtractArea());
            AddSection(BuildProgressCard());
            AddSection(BuildResultCard());
            AddSection(BuildDetails());

            Controls.Add(scroller);
            Controls.Add(BuildHeader());
            EnableFileDrop(this);
            ResumeLayout(false);
            PerformLayout();

            LocateTools();
            RefreshView();
            if (!string.IsNullOrWhiteSpace(initialVideoPath))
            {
                Shown += (sender, e) => LoadVideo(initialVideoPath);
            }
        }

        static string ApplicationDirectory
        {
            get { return AppDomain.CurrentDomain.BaseDirectory; }
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            using (var graphics = CreateGraphics())
            {
                dpiScale = graphics.DpiX / 96F;
            }
            wrappedWidth = 0;
            var workingArea = Screen.FromControl(this).WorkingArea;
            Height = Math.Min(Height, workingArea.Height - 20);
            Top = Math.Max(workingArea.Top, workingArea.Top + (workingArea.Height - Height) / 2);
            AdjustWrapping();
        }

        Control BuildHeader()
        {
            var header = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                BackColor = BrandColor,
                Padding = new Padding(20, 16, 20, 16),
                ColumnCount = 1,
            };
            header.Controls.Add(new Label
            {
                Text = AppName,
                AutoSize = true,
                ForeColor = Color.White,
                Font = new Font(Font.FontFamily, 16F),
                Margin = new Padding(0),
            });
            header.Controls.Add(new Label
            {
                Text = "Extrae imágenes de un video y genera su reporte de hashes SHA-256 y SHA-512",
                AutoSize = true,
                ForeColor = BrandTextMuted,
                Margin = new Padding(0, 2, 0, 0),
            });
            return header;
        }

        Control BuildToolsCard()
        {
            toolsCard = Card(null);
            toolsCard.BackColor = ErrorBackground;
            toolsCard.Controls.Add(new Label
            {
                Text = "Falta el programa FFmpeg",
                AutoSize = true,
                ForeColor = ErrorText,
                Font = new Font(Font.FontFamily, 11F, FontStyle.Bold),
                Margin = new Padding(0, 0, 0, 6),
            });
            toolsMessage.ForeColor = ErrorText;
            toolsMessage.Text = "Sin FFmpeg no se pueden extraer los fotogramas. Pide al área técnica que copie "
                + "ffmpeg.exe y ffprobe.exe en la carpeta:\n" + Path.Combine(ApplicationDirectory, "ffmpeg")
                + "\n\nDespués cierra y vuelve a abrir este programa.";
            toolsCard.Controls.Add(Wrap(toolsMessage, CardPadding));
            return toolsCard;
        }

        Control BuildVideoCard()
        {
            var card = Card("1.  Elige el video");

            dropZone.ColumnCount = 1;
            dropZone.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            dropZone.AutoSize = true;
            dropZone.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            dropZone.Anchor = AnchorStyles.Left | AnchorStyles.Right;
            dropZone.Padding = new Padding(DropZonePadding, 20, DropZonePadding, 20);
            dropZone.Margin = new Padding(0);
            dropZone.BackColor = DropZoneBackground;
            dropZone.Cursor = Cursors.Hand;
            dropZone.Paint += PaintDropZoneBorder;
            dropZone.Resize += (sender, e) => dropZone.Invalidate();

            dropTitle.Font = new Font(Font.FontFamily, 12F, FontStyle.Bold);
            dropTitle.ForeColor = BrandColor;
            dropDetails.ForeColor = MutedText;
            dropDetails.Margin = new Padding(3, 4, 3, 0);
            dropAction.ForeColor = BrandColor;
            dropAction.Margin = new Padding(3, 8, 3, 0);

            foreach (var label in new[] { dropTitle, dropDetails, dropAction })
            {
                label.Anchor = AnchorStyles.None;
                label.TextAlign = ContentAlignment.MiddleCenter;
                label.Cursor = Cursors.Hand;
                label.Click += (sender, e) => BrowseForVideo();
                dropZone.Controls.Add(Wrap(label, CardPadding + DropZonePadding));
            }
            dropZone.Click += (sender, e) => BrowseForVideo();
            card.Controls.Add(dropZone);
            card.Controls.Add(BuildRawStreamPanel());
            return card;
        }

        Control BuildRawStreamPanel()
        {
            rawStreamPanel.ColumnCount = 1;
            rawStreamPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            rawStreamPanel.AutoSize = true;
            rawStreamPanel.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            rawStreamPanel.Anchor = AnchorStyles.Left | AnchorStyles.Right;
            rawStreamPanel.BackColor = WarningBackground;
            rawStreamPanel.Padding = new Padding(NoticePadding, 10, NoticePadding, 12);
            rawStreamPanel.Margin = new Padding(0, 12, 0, 0);

            rawStreamPanel.Controls.Add(new Label
            {
                Text = "Indica cuántos fotogramas por segundo graba el DVR",
                AutoSize = true,
                ForeColor = WarningText,
                Font = new Font(Font, FontStyle.Bold),
                Margin = new Padding(0, 0, 0, 4),
            });
            rawStreamPanel.Controls.Add(Wrap(TextLabel(
                "Este video viene directo de un DVR, sin contenedor, y no trae ese dato. Revísalo en el reproductor o en la "
                + "configuración del DVR: de él dependen los tiempos, la duración y la cantidad de fotogramas.", WarningText),
                CardPadding + NoticePadding));

            rawFrameRateBox.Minimum = 1;
            rawFrameRateBox.Maximum = 240;
            rawFrameRateBox.Value = VideoInfo.RawStreamDefaultFrameRate;
            rawFrameRateBox.Width = 72;
            rawFrameRateBox.TextAlign = HorizontalAlignment.Center;
            rawFrameRateBox.Font = new Font(Font.FontFamily, 12F);
            rawFrameRateBox.Margin = new Padding(8, 0, 0, 0);
            rawFrameRateBox.ValueChanged += (sender, e) => OnRawFrameRateChanged();
            var frameRateRow = Row(new Label
            {
                Text = "Fotogramas por segundo:",
                AutoSize = true,
                ForeColor = WarningText,
                Margin = new Padding(0, 6, 0, 0),
            }, rawFrameRateBox);
            frameRateRow.Margin = new Padding(0, 8, 0, 0);
            rawStreamPanel.Controls.Add(frameRateRow);
            return rawStreamPanel;
        }

        Control BuildSelectionCard()
        {
            var card = Card("2.  ¿Qué quieres extraer?");

            momentsOption.Text = "Momentos o rangos del video";
            momentsOption.AutoSize = true;
            momentsOption.Checked = true;
            momentsOption.CheckedChanged += (sender, e) => OnSelectionModeChanged();
            wholeVideoOption.Text = "Todo el video";
            wholeVideoOption.AutoSize = true;
            wholeVideoOption.Margin = new Padding(24, 3, 3, 3);
            var modes = Row(momentsOption, wholeVideoOption);
            modes.Margin = new Padding(0, 0, 0, 10);
            card.Controls.Add(modes);

            momentsPanel.ColumnCount = 1;
            momentsPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            momentsPanel.AutoSize = true;
            momentsPanel.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            momentsPanel.Anchor = AnchorStyles.Left | AnchorStyles.Right;
            momentsPanel.Margin = new Padding(0);

            momentsPanel.Controls.Add(Wrap(TextLabel(
                "Escribe el tiempo tal como aparece en el reproductor de video y presiona \"Agregar\". "
                + "De cada segundo se extraen todos sus fotogramas.", MutedText), CardPadding));

            rangeOption.Text = "Extraer un rango de tiempo (desde – hasta)";
            rangeOption.AutoSize = true;
            rangeOption.Margin = new Padding(0, 10, 0, 2);
            rangeOption.CheckedChanged += (sender, e) => OnRangeOptionChanged();
            momentsPanel.Controls.Add(rangeOption);

            startPicker = new PlaybackTimePicker(Font, MutedText, BrandColor);
            startPicker.EnterPressed += (sender, e) => AddSelection();
            momentsPanel.Controls.Add(startPicker);
            endPicker = new PlaybackTimePicker(Font, MutedText, BrandColor) { Caption = "Hasta" };
            endPicker.EnterPressed += (sender, e) => AddSelection();
            momentsPanel.Controls.Add(endPicker);
            UpdateRangeCaptions();

            addSelectionButton.Margin = new Padding(0, 10, 0, 0);
            addSelectionButton.Font = new Font(Font, FontStyle.Bold);
            addSelectionButton.Click += (sender, e) => AddSelection();
            momentsPanel.Controls.Add(addSelectionButton);

            selectionMessage.Margin = new Padding(3, 6, 3, 0);
            momentsPanel.Controls.Add(Wrap(selectionMessage, CardPadding));

            emptySelectionHint.Text = "Todavía no has agregado ningún momento ni rango.";
            emptySelectionHint.AutoSize = true;
            emptySelectionHint.ForeColor = MutedText;
            emptySelectionHint.Font = new Font(Font, FontStyle.Italic);
            emptySelectionHint.Margin = new Padding(3, 10, 3, 0);
            momentsPanel.Controls.Add(emptySelectionHint);

            selectionList.IntegralHeight = false;
            selectionList.Height = 120;
            selectionList.Anchor = AnchorStyles.Left | AnchorStyles.Right;
            selectionList.Margin = new Padding(0);
            selectionList.SelectedIndexChanged += (sender, e) =>
                removeSelectionButton.Enabled = !running && selectionList.SelectedIndex >= 0;
            selectionList.KeyDown += (sender, e) =>
            {
                if (e.KeyCode == Keys.Delete) RemoveSelectedRange();
            };
            removeSelectionButton.Anchor = AnchorStyles.Top;
            removeSelectionButton.Margin = new Padding(8, 0, 0, 0);
            removeSelectionButton.Click += (sender, e) => RemoveSelectedRange();
            selectionRow.ColumnCount = 2;
            selectionRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            selectionRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            selectionRow.AutoSize = true;
            selectionRow.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            selectionRow.Anchor = AnchorStyles.Left | AnchorStyles.Right;
            selectionRow.Margin = new Padding(0, 10, 0, 0);
            selectionRow.Controls.Add(selectionList, 0, 0);
            selectionRow.Controls.Add(removeSelectionButton, 1, 0);
            momentsPanel.Controls.Add(selectionRow);

            card.Controls.Add(momentsPanel);

            wholeVideoDescription.Text = "Se extraerán todos los fotogramas del video, de principio a fin.";
            wholeVideoDescription.ForeColor = MutedText;
            card.Controls.Add(Wrap(wholeVideoDescription, CardPadding));
            return card;
        }

        Control BuildExtractArea()
        {
            var area = new TableLayoutPanel
            {
                ColumnCount = 1,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Anchor = AnchorStyles.Left | AnchorStyles.Right,
                Margin = new Padding(0, 0, 0, 12),
            };
            area.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            estimateLabel.Margin = new Padding(2, 0, 2, 8);
            area.Controls.Add(Wrap(estimateLabel, 4));
            extractButton.Click += OnExtractClick;
            area.Controls.Add(extractButton);
            return area;
        }

        Control BuildProgressCard()
        {
            progressCard = Card(null);
            progressStep.AutoSize = true;
            progressStep.Font = new Font(Font.FontFamily, 11F, FontStyle.Bold);
            progressStep.ForeColor = BrandColor;
            progressStep.Margin = new Padding(0, 0, 0, 8);
            progressCard.Controls.Add(progressStep);

            progressBar.Anchor = AnchorStyles.Left | AnchorStyles.Right;
            progressBar.Height = 22;
            progressBar.MarqueeAnimationSpeed = 30;
            progressCard.Controls.Add(progressBar);

            progressDetail.ForeColor = MutedText;
            progressDetail.Margin = new Padding(0, 6, 0, 10);
            progressCard.Controls.Add(Wrap(progressDetail, CardPadding));

            cancelButton.Click += (sender, e) => CancelExtraction();
            progressCard.Controls.Add(cancelButton);
            return progressCard;
        }

        Control BuildResultCard()
        {
            resultCard = Card(null);
            resultCard.Visible = false;
            resultTitle.AutoSize = true;
            resultTitle.Font = new Font(Font.FontFamily, 12F, FontStyle.Bold);
            resultTitle.Margin = new Padding(0, 0, 0, 6);
            resultCard.Controls.Add(resultTitle);
            resultMessage.Margin = new Padding(0, 0, 0, 12);
            resultCard.Controls.Add(Wrap(resultMessage, CardPadding));

            openFolderButton.Click += (sender, e) => OpenOutputFolder();
            viewReportButton.Click += (sender, e) => ShowReport();
            newExtractionButton.Click += (sender, e) => StartNewExtraction();
            resultCard.Controls.Add(Row(openFolderButton, viewReportButton, newExtractionButton));
            return resultCard;
        }

        Control BuildDetails()
        {
            var area = new TableLayoutPanel
            {
                ColumnCount = 1,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Anchor = AnchorStyles.Left | AnchorStyles.Right,
                Margin = new Padding(0),
            };
            area.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));

            detailsLink.Text = "Mostrar detalles técnicos";
            detailsLink.AutoSize = true;
            detailsLink.LinkColor = MutedText;
            detailsLink.ActiveLinkColor = BrandColor;
            detailsLink.LinkClicked += (sender, e) => ToggleDetails();
            area.Controls.Add(detailsLink);

            logBox.Multiline = true;
            logBox.ReadOnly = true;
            logBox.ScrollBars = ScrollBars.Vertical;
            logBox.BackColor = Color.White;
            logBox.Font = new Font("Consolas", 9F);
            logBox.Height = 170;
            logBox.Anchor = AnchorStyles.Left | AnchorStyles.Right;
            logBox.Margin = new Padding(0, 6, 0, 0);
            logBox.Visible = false;
            area.Controls.Add(logBox);
            return area;
        }

        TableLayoutPanel Card(string title)
        {
            var card = new TableLayoutPanel
            {
                ColumnCount = 1,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Anchor = AnchorStyles.Left | AnchorStyles.Right,
                BackColor = Color.White,
                Padding = new Padding(CardPadding, 14, CardPadding, 16),
                Margin = new Padding(0, 0, 0, 12),
            };
            card.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            card.Paint += PaintCardBorder;
            if (title != null)
            {
                card.Controls.Add(new Label
                {
                    Text = title,
                    AutoSize = true,
                    ForeColor = BrandColor,
                    Font = new Font(Font.FontFamily, 12F, FontStyle.Bold),
                    Margin = new Padding(0, 0, 0, 10),
                });
            }
            return card;
        }

        Button PrimaryButton(string text)
        {
            var button = new Button
            {
                Text = text,
                FlatStyle = FlatStyle.Flat,
                UseVisualStyleBackColor = false,
                BackColor = BrandColor,
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 12F, FontStyle.Bold),
                Height = 48,
                Anchor = AnchorStyles.Left | AnchorStyles.Right,
                Cursor = Cursors.Hand,
                Margin = new Padding(0),
            };
            button.FlatAppearance.BorderSize = 0;
            button.EnabledChanged += (sender, e) => button.BackColor = button.Enabled ? BrandColor : DisabledButton;
            return button;
        }

        static Button SecondaryButton(string text)
        {
            var button = new Button
            {
                Text = text,
                AutoSize = true,
                FlatStyle = FlatStyle.Flat,
                UseVisualStyleBackColor = false,
                BackColor = Color.White,
                ForeColor = BrandColor,
                Padding = new Padding(10, 3, 10, 3),
                Cursor = Cursors.Hand,
                Margin = new Padding(0, 0, 8, 0),
            };
            button.FlatAppearance.BorderColor = BrandColor;
            return button;
        }

        static FlowLayoutPanel Row(params Control[] controls)
        {
            var row = new FlowLayoutPanel { AutoSize = true, WrapContents = true, Margin = new Padding(0) };
            row.Controls.AddRange(controls);
            return row;
        }

        static Label TextLabel(string text, Color color)
        {
            return new Label { Text = text, ForeColor = color, Margin = new Padding(0) };
        }

        Label Wrap(Label label, int horizontalInset)
        {
            label.AutoSize = true;
            wrappingLabels[label] = horizontalInset;
            return label;
        }

        void AddSection(Control section)
        {
            content.RowCount++;
            content.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            content.Controls.Add(section);
        }

        void AdjustWrapping()
        {
            var available = content.ClientSize.Width;
            if (available == wrappedWidth) return;
            wrappedWidth = available;
            foreach (var entry in wrappingLabels)
            {
                var inset = (int)Math.Ceiling(entry.Value * 2 * dpiScale) + entry.Key.Margin.Horizontal;
                entry.Key.MaximumSize = new Size(Math.Max(120, available - inset), 0);
            }
        }

        static void PaintCardBorder(object sender, PaintEventArgs e)
        {
            var card = (Control)sender;
            using (var pen = new Pen(CardBorder))
            {
                e.Graphics.DrawRectangle(pen, 0, 0, card.Width - 1, card.Height - 1);
            }
        }

        void PaintDropZoneBorder(object sender, PaintEventArgs e)
        {
            var width = Math.Max(1F, 2F * dpiScale);
            using (var pen = new Pen(DropZoneBorder, width) { DashStyle = DashStyle.Dash })
            {
                e.Graphics.DrawRectangle(pen, width / 2, width / 2, dropZone.Width - width, dropZone.Height - width);
            }
        }

        void LocateTools()
        {
            ffmpegPath = FrameExtraction.LocateTool("ffmpeg", ApplicationDirectory);
            ffprobePath = FrameExtraction.LocateTool("ffprobe", ApplicationDirectory);
        }

        bool ToolsAvailable
        {
            get { return ffmpegPath != null && ffprobePath != null; }
        }

        void EnableFileDrop(Control control)
        {
            control.AllowDrop = true;
            control.DragEnter += OnFileDragEnter;
            control.DragDrop += OnFileDragDrop;
            foreach (Control child in control.Controls)
            {
                EnableFileDrop(child);
            }
        }

        void OnFileDragEnter(object sender, DragEventArgs e)
        {
            var canDrop = ToolsAvailable && !running && e.Data.GetDataPresent(DataFormats.FileDrop);
            e.Effect = canDrop ? DragDropEffects.Copy : DragDropEffects.None;
        }

        void OnFileDragDrop(object sender, DragEventArgs e)
        {
            var files = e.Data.GetData(DataFormats.FileDrop) as string[];
            if (files != null && files.Length > 0)
            {
                LoadVideo(files[0]);
            }
        }

        void BrowseForVideo()
        {
            if (!ToolsAvailable || running) return;
            using (var dialog = new OpenFileDialog { Title = "Selecciona el video", Filter = VideoFilter })
            {
                if (dialog.ShowDialog(this) == DialogResult.OK)
                {
                    LoadVideo(dialog.FileName);
                }
            }
        }

        async void LoadVideo(string selectedPath)
        {
            if (!ToolsAvailable || running) return;

            HideResult();
            video = null;
            videoError = null;
            ranges.Clear();
            ShowSelectionMessage(string.Empty, MutedText);
            startPicker.TotalSeconds = 0;
            endPicker.TotalSeconds = 0;

            var candidate = ResolveExistingFile(selectedPath);
            if (candidate == null)
            {
                videoPath = null;
                videoError = "No se encontró el archivo. Revisa que siga en su lugar y vuelve a elegirlo.";
                RefreshView();
                return;
            }

            videoPath = candidate;
            probing = true;
            RefreshView();
            try
            {
                var info = await Task.Run(() => FrameExtraction.ProbeVideo(ffprobePath, candidate));
                if (candidate != videoPath) return;
                if (info.HasVideoStream)
                {
                    info.AssumedFrameRate = (int)rawFrameRateBox.Value;
                    video = info;
                    freeBytes = LongPathFileSystem.FreeBytesAvailable(candidate);
                    UpdatePickerLimits();
                }
                else
                {
                    videoError = "Este archivo no parece ser un video, o está dañado. Elige otro archivo.";
                }
            }
            catch (Exception exception)
            {
                videoError = "No se pudo leer el archivo: " + exception.Message;
            }
            finally
            {
                if (candidate == videoPath)
                {
                    probing = false;
                    RefreshView();
                }
            }
        }

        static string ResolveExistingFile(string path)
        {
            var trimmed = (path ?? string.Empty).Trim().Trim('"');
            if (trimmed.Length == 0) return null;
            try
            {
                var fullPath = LongPathFileSystem.GetFullPath(trimmed);
                return LongPathFileSystem.FileExists(fullPath) ? fullPath : null;
            }
            catch (IOException)
            {
                return null;
            }
        }

        void OnRawFrameRateChanged()
        {
            if (video == null) return;
            video.AssumedFrameRate = (int)rawFrameRateBox.Value;
            UpdatePickerLimits();
            HideResult();
            RefreshView();
        }

        void UpdatePickerLimits()
        {
            var maximumHours = video != null && video.DurationSeconds > 0 ? (int)(video.DurationSeconds / 3600) : 99;
            startPicker.MaximumHours = maximumHours;
            endPicker.MaximumHours = maximumHours;
        }

        void OnSelectionModeChanged()
        {
            HideResult();
            RefreshView();
        }

        void OnRangeOptionChanged()
        {
            if (rangeOption.Checked)
            {
                endPicker.TotalSeconds = startPicker.TotalSeconds;
            }
            UpdateRangeCaptions();
            ShowSelectionMessage(string.Empty, MutedText);
        }

        void UpdateRangeCaptions()
        {
            startPicker.Caption = rangeOption.Checked ? "Desde" : "Momento";
            endPicker.Visible = rangeOption.Checked;
            addSelectionButton.Text = rangeOption.Checked ? "Agregar rango" : "Agregar momento";
        }

        void AddSelection()
        {
            if (video == null || running) return;
            var start = startPicker.TotalSeconds;
            var end = rangeOption.Checked ? endPicker.TotalSeconds : start;

            if (end < start)
            {
                ShowSelectionMessage("El final del rango (Hasta) debe ser igual o posterior al inicio (Desde).", ErrorText);
                return;
            }
            if (video.DurationSeconds > 0 && end >= video.DurationSeconds)
            {
                ShowSelectionMessage(
                    (rangeOption.Checked ? "El rango termina" : "Ese momento está") + " después del final del video. El video dura "
                    + FrameExtraction.FormatPlaybackTime(Math.Floor(video.DurationSeconds)) + ".",
                    ErrorText);
                return;
            }
            var candidate = new TimeRange(start, end);
            var overlapping = ranges.FirstOrDefault(range => range.Overlaps(candidate));
            if (overlapping != null)
            {
                ShowSelectionMessage("Se cruza con " + DescribeForPeople(overlapping) + ", que ya está en la lista.", WarningText);
                return;
            }

            HideResult();
            ranges.Add(candidate);
            ranges.Sort((left, right) => left.Start.CompareTo(right.Start));
            ShowSelectionMessage("Se agregó " + DescribeForPeople(candidate) + ".", SuccessText);
            RefreshView();
            selectionList.SelectedIndex = ranges.IndexOf(candidate);
        }

        static string DescribeForPeople(TimeRange range)
        {
            return range.IsSingleSecond
                ? "el momento " + FrameExtraction.FormatPlaybackTime(range.Start) + " (segundo " + range.Start + ")"
                : "el rango " + FrameExtraction.FormatPlaybackTime(range.Start) + " – " + FrameExtraction.FormatPlaybackTime(range.End)
                    + " (segundos " + range.Start + " a " + range.End + ")";
        }

        void RemoveSelectedRange()
        {
            var index = selectionList.SelectedIndex;
            if (index < 0 || running) return;
            HideResult();
            ranges.RemoveAt(index);
            ShowSelectionMessage(string.Empty, MutedText);
            RefreshView();
            if (ranges.Count > 0)
            {
                selectionList.SelectedIndex = Math.Min(index, ranges.Count - 1);
            }
        }

        void ShowSelectionMessage(string text, Color color)
        {
            selectionMessage.Text = text;
            selectionMessage.ForeColor = color;
        }

        void RefreshView()
        {
            var videoReady = video != null && !probing;
            var editable = ToolsAvailable && !running;

            toolsCard.Visible = !ToolsAvailable;
            UpdateVideoCard();
            rawStreamPanel.Visible = videoReady && video.IsRawStream;
            rawFrameRateBox.Enabled = editable;

            dropZone.Enabled = editable;
            momentsOption.Enabled = editable && videoReady;
            wholeVideoOption.Enabled = editable && videoReady;
            momentsPanel.Visible = momentsOption.Checked;
            wholeVideoDescription.Visible = wholeVideoOption.Checked;
            foreach (var input in new Control[] { rangeOption, startPicker, endPicker, addSelectionButton, selectionList })
            {
                input.Enabled = editable && videoReady;
            }

            UpdateSelectionList();
            emptySelectionHint.Visible = ranges.Count == 0;
            selectionRow.Visible = ranges.Count > 0;
            removeSelectionButton.Enabled = editable && selectionList.SelectedIndex >= 0;

            UpdateEstimate(videoReady);
            var hasSelection = wholeVideoOption.Checked || ranges.Count > 0;
            extractButton.Enabled = editable && videoReady && hasSelection && hasEnoughSpace;

            progressCard.Visible = running;
            cancelButton.Enabled = running && !cancelRequested && currentStep < 3;
        }

        void UpdateVideoCard()
        {
            dropTitle.ForeColor = BrandColor;
            dropDetails.ForeColor = MutedText;

            if (probing)
            {
                SetDropZoneText("Analizando el video...", Path.GetFileName(videoPath), string.Empty);
            }
            else if (videoError != null)
            {
                SetDropZoneText("No se pudo abrir el video", videoError, "Haz clic aquí o arrastra otro archivo");
                dropTitle.ForeColor = ErrorText;
                dropDetails.ForeColor = ErrorText;
            }
            else if (video != null)
            {
                SetDropZoneText(Path.GetFileName(videoPath), DescribeVideo(video), "Haz clic aquí o arrastra otro archivo para cambiar de video");
            }
            else
            {
                SetDropZoneText("Arrastra aquí el video", "o haz clic para buscarlo en la computadora", string.Empty);
            }
        }

        void SetDropZoneText(string title, string details, string action)
        {
            dropTitle.Text = title;
            dropDetails.Text = details;
            dropAction.Text = action;
            dropAction.Visible = action.Length > 0;
        }

        static string DescribeVideo(VideoInfo info)
        {
            var parts = new List<string>();
            if (info.DurationSeconds > 0)
            {
                parts.Add((info.IsRawStream ? "Duración aprox. " : "Duración ")
                    + FrameExtraction.FormatPlaybackTime(Math.Floor(info.DurationSeconds)));
            }
            parts.Add(info.Width + " × " + info.Height);
            if (info.FramesPerSecond > 0)
            {
                parts.Add(info.FramesPerSecond.ToString("0.##") + " fotogramas por segundo"
                    + (info.IsRawStream ? " (indicado abajo)" : string.Empty));
            }
            return string.Join("   ·   ", parts);
        }

        void UpdateSelectionList()
        {
            var selected = selectionList.SelectedIndex;
            var items = ranges.Select(DescribeListItem).ToArray();
            if (items.SequenceEqual(selectionList.Items.Cast<string>())) return;

            selectionList.BeginUpdate();
            selectionList.Items.Clear();
            selectionList.Items.AddRange(items);
            selectionList.EndUpdate();
            if (selected >= 0 && selected < items.Length)
            {
                selectionList.SelectedIndex = selected;
            }
        }

        string DescribeListItem(TimeRange range)
        {
            var text = range.IsSingleSecond
                ? FrameExtraction.FormatPlaybackTime(range.Start) + "   ·   segundo " + range.Start
                : FrameExtraction.FormatPlaybackTime(range.Start) + " – " + FrameExtraction.FormatPlaybackTime(range.End)
                    + "   ·   segundos " + range.Start + " a " + range.End;
            var framesPerSecond = video == null ? 0 : video.FramesPerSecond;
            return framesPerSecond > 0
                ? text + string.Format("   (aprox. {0:N0} fotogramas)", Math.Round(range.Seconds * framesPerSecond))
                : text;
        }

        void UpdateEstimate(bool videoReady)
        {
            hasEnoughSpace = true;
            estimateLabel.ForeColor = MutedText;
            estimateLabel.Visible = videoReady;
            if (!videoReady) return;

            if (momentsOption.Checked && ranges.Count == 0)
            {
                estimateLabel.Text = "Agrega al menos un momento o rango para poder extraer.";
                return;
            }

            var seconds = wholeVideoOption.Checked ? video.DurationSeconds : ranges.Sum(range => range.Seconds);
            var frames = seconds * video.FramesPerSecond;
            if (frames <= 0)
            {
                estimateLabel.Text = wholeVideoOption.Checked
                    ? "Se extraerán todos los fotogramas del video."
                    : "Se extraerán los fotogramas de " + ranges.Count + " momento(s) o rango(s).";
                return;
            }

            var bytes = FrameExtraction.EstimateUncompressedPngBytes(video.Width, video.Height, frames);
            var text = string.Format("Se extraerán aprox. {0:N0} fotogramas, que ocuparán aprox. {1}.", Math.Round(frames), FormatSize(bytes));
            if (freeBytes.HasValue)
            {
                hasEnoughSpace = freeBytes.Value > bytes;
                text = hasEnoughSpace
                    ? text + " Espacio libre en el disco: " + FormatSize(freeBytes.Value) + "."
                    : "No hay suficiente espacio en el disco: se necesitan aprox. " + FormatSize(bytes) + " y solo hay "
                        + FormatSize(freeBytes.Value) + " libres. Libera espacio o copia el video a otro disco.";
            }
            estimateLabel.Text = text;
            estimateLabel.ForeColor = hasEnoughSpace ? MutedText : ErrorText;
        }

        static string FormatSize(long bytes)
        {
            const double megabyte = 1024d * 1024d;
            const double gigabyte = megabyte * 1024d;
            return bytes >= gigabyte
                ? string.Format("{0:N1} GB", bytes / gigabyte)
                : string.Format("{0:N0} MB", Math.Max(1, Math.Ceiling(bytes / megabyte)));
        }

        async void OnExtractClick(object sender, EventArgs e)
        {
            running = true;
            cancelRequested = false;
            currentStep = 0;
            reportedFrames = 0;
            decodingProblems = 0;
            outputDirectory = null;
            reportPath = null;
            logBox.Clear();
            HideResult();
            RefreshView();
            try
            {
                await ExtractAsync();
            }
            catch (Exception exception)
            {
                Log("ERROR: " + exception);
                ShowResult(Outcome.Error, "No se pudo completar la extracción", exception.Message);
            }
            finally
            {
                running = false;
                ffmpegProcess = null;
                RefreshView();
            }
        }

        async Task ExtractAsync()
        {
            var startedAt = DateTime.Now;
            var sourcePath = videoPath;
            var info = video;
            var selectedRanges = wholeVideoOption.Checked ? null : (IList<TimeRange>)ranges.ToList();
            extractingWholeVideo = selectedRanges == null;
            var padding = FrameExtraction.PaddingFor(info);

            var extractionDirectory = FrameExtraction.OutputDirectoryFor(sourcePath, startedAt);
            if (LongPathFileSystem.DirectoryExists(extractionDirectory))
            {
                throw new IOException("Ya existe la carpeta de salida: " + extractionDirectory);
            }
            LongPathFileSystem.CreateDirectory(extractionDirectory);
            outputDirectory = extractionDirectory;

            var selection = extractingWholeVideo
                ? "todo el video"
                : FrameExtraction.DescribeRanges(selectedRanges) + " (cada segundo completo, desde T hasta antes de T+1)";
            var videoFormat = FrameExtraction.DescribeVideoFormat(info);
            Log("Video: " + sourcePath);
            Log("Formato: " + videoFormat);
            Log("Fotogramas solicitados: " + selection);
            Log(string.Format("Duración: {0} ({1}) | Padding: {2} dígitos",
                FrameExtraction.FormatPlaybackTime(padding.DurationSeconds), info.RawDuration, padding.Digits));
            Log("Carpeta de salida: " + extractionDirectory);

            ShowStep(1, "Calculando los hashes SHA-256 y SHA-512 del video original");
            var packetCount = extractingWholeVideo && !info.IsRawStream
                ? Task.Run(() => FrameExtraction.CountVideoPackets(ffprobePath, sourcePath))
                : Task.FromResult(extractingWholeVideo ? info.PacketCount : 0L);
            var videoHashes = await Task.Run(() => FrameExtraction.ComputeHashes(
                sourcePath, ThrottledPercent(percent => ShowPercent(percent, percent + " %"))));
            var ffmpegVersion = await Task.Run(() => FrameExtraction.FfmpegVersion(ffmpegPath));
            var framesInFile = await packetCount;
            Log("SHA-256 del video original: " + videoHashes.Sha256);
            Log("SHA-512 del video original: " + videoHashes.Sha512);
            Log(ffmpegVersion);
            if (cancelRequested)
            {
                ReportCancellation(extractionDirectory);
                return;
            }

            ShowStep(2, "Extrayendo fotogramas");
            expectedFrames = framesInFile > 0
                ? framesInFile
                : (long)Math.Round((extractingWholeVideo ? info.DurationSeconds : selectedRanges.Sum(range => range.Seconds)) * info.FramesPerSecond);
            ShowExtractionProgress(0);
            var arguments = FrameExtraction.FfmpegArguments(sourcePath, selectedRanges, info.InputFrameRate, extractionDirectory, padding.Digits);
            Log("Parámetros de FFmpeg: " + arguments);
            int exitCode;
            using (var process = StartFfmpeg(arguments))
            {
                ffmpegProcess = process;
                await Task.Run(() => process.WaitForExit());
                ffmpegProcess = null;
                exitCode = process.ExitCode;
            }
            Log("FFmpeg terminó (código " + exitCode + ").");

            var extractedCount = FrameExtraction.ExtractedFrames(extractionDirectory).Count;
            if (cancelRequested)
            {
                ReportCancellation(extractionDirectory);
                return;
            }
            if (exitCode != 0 || extractedCount == 0)
            {
                if (extractedCount == 0)
                {
                    LongPathFileSystem.DeleteEmptyDirectory(extractionDirectory);
                    outputDirectory = null;
                }
                var reason = exitCode != 0
                    ? "No se pudo procesar este video. Puede que el archivo esté dañado, en un formato no compatible, o que se haya "
                        + "llenado el disco. Revisa los detalles técnicos o consulta al área técnica."
                    : "No se encontraron fotogramas en los momentos elegidos. Revisa que estén dentro de la duración del video.";
                ShowResult(Outcome.Error, "No se extrajeron los fotogramas", reason, canOpenFolder: outputDirectory != null);
                return;
            }

            ShowStep(3, "Generando el reporte de hashes SHA-256 y SHA-512");
            var producedFrames = (int)Interlocked.Read(ref reportedFrames);
            var problems = Interlocked.CompareExchange(ref decodingProblems, 0, 0);
            var undecodableFrames = framesInFile > producedFrames ? framesInFile - producedFrames : 0;
            var header = new ReportHeader
            {
                Generator = ToolName + " v" + GetType().Assembly.GetName().Version.ToString(3),
                VideoPath = sourcePath,
                VideoHashes = videoHashes,
                VideoFormat = videoFormat,
                Timestamp = startedAt,
                Selection = selection,
                FfmpegVersion = ffmpegVersion,
                FfmpegArguments = arguments,
                ReportedFrames = producedFrames,
            };
            if (undecodableFrames > 0)
            {
                header.Notes.Add(string.Format(
                    "ADVERTENCIA: El archivo contiene {0} fotogramas codificados y FFmpeg solo pudo decodificar {1}; "
                    + "{2} están dañados y no se pudieron extraer.", framesInFile, producedFrames, undecodableFrames));
            }
            if (problems > 0)
            {
                header.Notes.Add(string.Format(
                    "ADVERTENCIA: FFmpeg reportó {0} errores al decodificar (datos dañados o que no son de video, común en archivos "
                    + "exportados de DVR). Se recuperaron todos los fotogramas posibles; algunos pueden mostrar manchas o bloques.",
                    problems));
            }
            reportPath = await Task.Run(() => FrameExtraction.WriteHashReport(extractionDirectory, header,
                (done, total) => RunOnUi(() => ShowPercent(done * 100 / Math.Max(total, 1), done + " de " + total + " fotogramas"))));
            Log("Reporte de hashes: " + reportPath);

            var warnings = new List<string>();
            var overwrittenFrames = producedFrames - extractedCount;
            if (overwrittenFrames > 0)
            {
                Log("ADVERTENCIA: " + overwrittenFrames + " fotogramas sobreescritos por marca de tiempo repetida.");
                warnings.Add(overwrittenFrames + " fotogramas no se pudieron guardar porque tenían la misma marca de tiempo "
                    + "(video de frecuencia variable).");
            }
            if (undecodableFrames > 0)
            {
                warnings.Add(undecodableFrames + " de los " + framesInFile + " fotogramas del archivo están dañados y no se pudieron extraer.");
            }
            if (problems > 0)
            {
                warnings.Add("El video tiene partes dañadas o datos que no son de video. Se recuperaron todos los fotogramas posibles, "
                    + "pero algunos pueden verse con manchas o bloques.");
            }

            var folderName = Path.GetFileName(extractionDirectory);
            var saved = "Se guardaron " + extractedCount + " fotogramas y su reporte de hashes SHA-256 y SHA-512 en la carpeta \""
                + folderName + "\", junto al video original.";
            if (warnings.Count > 0)
            {
                ShowResult(Outcome.Warning, "Extracción terminada, con advertencias",
                    saved + "\n\n" + string.Join("\n", warnings.Select(warning => "•  " + warning)) + "\n\nTodo quedó anotado en el reporte.",
                    canOpenFolder: true, canViewReport: true);
                return;
            }
            ShowResult(Outcome.Success, "Extracción terminada", saved, canOpenFolder: true, canViewReport: true);
        }

        void ReportCancellation(string extractionDirectory)
        {
            if (FrameExtraction.ExtractedFrames(extractionDirectory).Count == 0)
            {
                LongPathFileSystem.DeleteEmptyDirectory(extractionDirectory);
                outputDirectory = null;
                ShowResult(Outcome.Cancelled, "Extracción cancelada", "No se guardó ningún fotograma.");
                return;
            }
            ShowResult(Outcome.Cancelled, "Extracción cancelada",
                "La carpeta quedó incompleta y no tiene reporte de hashes, así que no debe usarse como evidencia. "
                + "Puedes borrarla.", canOpenFolder: true);
        }

        Process StartFfmpeg(string arguments)
        {
            var process = new Process
            {
                StartInfo = new ProcessStartInfo(ffmpegPath, arguments)
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                },
            };
            process.OutputDataReceived += (sender, e) => OnFfmpegProgress(e.Data);
            process.ErrorDataReceived += (sender, e) => OnFfmpegMessage(e.Data);
            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            return process;
        }

        void OnFfmpegMessage(string message)
        {
            if (string.IsNullOrEmpty(message)) return;
            if (FrameExtraction.IsDecodingProblem(message))
            {
                Interlocked.Increment(ref decodingProblems);
            }
            RunOnUi(() => Log(message));
        }

        void OnFfmpegProgress(string line)
        {
            const string framePrefix = "frame=";
            if (line == null || !line.StartsWith(framePrefix, StringComparison.Ordinal)) return;
            long extracted;
            if (!long.TryParse(line.Substring(framePrefix.Length).Trim(), out extracted)) return;
            Interlocked.Exchange(ref reportedFrames, extracted);
            RunOnUi(() => ShowExtractionProgress(extracted));
        }

        void ShowStep(int step, string description)
        {
            currentStep = step;
            progressStep.Text = "Paso " + step + " de 3  ·  " + description;
            ShowPercent(0, string.Empty);
            RefreshView();
        }

        void ShowPercent(long percent, string detail)
        {
            progressBar.Style = ProgressBarStyle.Continuous;
            progressBar.Maximum = 100;
            progressBar.Value = (int)Math.Max(0, Math.Min(100, percent));
            progressDetail.Text = detail;
        }

        void ShowExtractionProgress(long extracted)
        {
            if (extracted == 0 || expectedFrames <= 0)
            {
                progressBar.Style = ProgressBarStyle.Marquee;
                if (extracted > 0)
                {
                    progressDetail.Text = extracted + " fotogramas extraídos";
                }
                else
                {
                    progressDetail.Text = extractingWholeVideo
                        ? "Preparando la extracción..."
                        : "Buscando los momentos dentro del video... En videos largos esto puede tardar unos minutos.";
                }
                return;
            }
            ShowPercent(extracted * 100 / expectedFrames, string.Format("{0:N0} de aprox. {1:N0} fotogramas", extracted, expectedFrames));
        }

        Action<long, long> ThrottledPercent(Action<int> onPercentChanged)
        {
            var lastPercent = -1;
            return (done, total) =>
            {
                var percent = total <= 0 ? 100 : (int)(done * 100 / total);
                if (percent == lastPercent) return;
                lastPercent = percent;
                RunOnUi(() => onPercentChanged(percent));
            };
        }

        void ShowResult(Outcome outcome, string title, string message, bool canOpenFolder = false, bool canViewReport = false)
        {
            Color background, foreground;
            switch (outcome)
            {
                case Outcome.Success:
                    background = SuccessBackground;
                    foreground = SuccessText;
                    break;
                case Outcome.Warning:
                    background = WarningBackground;
                    foreground = WarningText;
                    break;
                case Outcome.Error:
                    background = ErrorBackground;
                    foreground = ErrorText;
                    break;
                default:
                    background = Color.White;
                    foreground = BrandColor;
                    break;
            }
            resultCard.BackColor = background;
            resultTitle.ForeColor = foreground;
            resultMessage.ForeColor = foreground;
            resultTitle.Text = title;
            resultMessage.Text = message;
            openFolderButton.Visible = canOpenFolder && outputDirectory != null;
            viewReportButton.Visible = canViewReport && reportPath != null;
            resultCard.Visible = true;
            scroller.ScrollControlIntoView(resultCard);
        }

        void HideResult()
        {
            resultCard.Visible = false;
        }

        void StartNewExtraction()
        {
            HideResult();
            ranges.Clear();
            ShowSelectionMessage(string.Empty, MutedText);
            startPicker.TotalSeconds = 0;
            endPicker.TotalSeconds = 0;
            RefreshView();
            scroller.AutoScrollPosition = Point.Empty;
            startPicker.FocusFirstBox();
        }

        void CancelExtraction()
        {
            if (!running || currentStep >= 3) return;
            cancelRequested = true;
            progressDetail.Text = "Cancelando...";
            RefreshView();
            var process = ffmpegProcess;
            if (process == null) return;
            try
            {
                process.Kill();
            }
            catch (InvalidOperationException)
            {
            }
            catch (Win32Exception)
            {
            }
        }

        void OpenOutputFolder()
        {
            if (outputDirectory != null && LongPathFileSystem.DirectoryExists(outputDirectory))
            {
                Process.Start("explorer.exe", "\"" + outputDirectory + "\"");
            }
        }

        void ShowReport()
        {
            if (reportPath == null) return;
            using (var viewer = new ReportViewerForm(reportPath))
            {
                viewer.ShowDialog(this);
            }
        }

        void ToggleDetails()
        {
            logBox.Visible = !logBox.Visible;
            detailsLink.Text = logBox.Visible ? "Ocultar detalles técnicos" : "Mostrar detalles técnicos";
            if (logBox.Visible) scroller.ScrollControlIntoView(logBox);
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (running)
            {
                var answer = MessageBox.Show(
                    this,
                    "Hay una extracción en curso. ¿Deseas cancelarla y salir?",
                    WindowTitle,
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning);
                if (answer != DialogResult.Yes)
                {
                    e.Cancel = true;
                    return;
                }
                CancelExtraction();
            }
            base.OnFormClosing(e);
        }

        void Log(string line)
        {
            logBox.AppendText(line + Environment.NewLine);
        }

        void RunOnUi(Action action)
        {
            if (IsDisposed || !IsHandleCreated) return;
            try
            {
                BeginInvoke(action);
            }
            catch (InvalidOperationException)
            {
            }
        }
    }
}
