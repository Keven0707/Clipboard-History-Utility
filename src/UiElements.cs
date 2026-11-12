using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Windows.Forms;

namespace QuietClip
{
    internal sealed class BufferedFlowLayoutPanel : FlowLayoutPanel
    {
        public BufferedFlowLayoutPanel()
        {
            DoubleBuffered = true;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw, true);
            UpdateStyles();
        }
    }

    internal sealed class ThemeChoice
    {
        public string Name { get; private set; }
        public Color Swatch { get; private set; }

        public ThemeChoice(string name, Color swatch)
        {
            Name = name;
            Swatch = swatch;
        }
    }

    internal static class Theme
    {
        internal static readonly Color Canvas = Color.FromArgb(244, 246, 249);
        internal static readonly Color Paper = Color.FromArgb(255, 255, 255);
        internal static readonly Color Ink = Color.FromArgb(31, 36, 48);
        internal static readonly Color Muted = Color.FromArgb(112, 121, 140);
        internal static readonly Color Faint = Color.FromArgb(225, 229, 237);
        internal static Color Accent = Color.FromArgb(102, 85, 140);
        internal static Color AccentText = Color.White;
        internal static Color AccentHover = Color.FromArgb(84, 70, 115);
        internal static Color AccentPressed = Color.FromArgb(72, 60, 99);
        internal static Color AccentSoft = Color.FromArgb(235, 232, 241);
        internal static Color AccentSoftHover = Color.FromArgb(224, 219, 234);
        internal static Color AccentSoftPressed = Color.FromArgb(213, 206, 226);
        internal static readonly Color Danger = Color.FromArgb(194, 73, 84);
        internal static readonly Color Success = Color.FromArgb(44, 145, 107);

        internal static readonly ThemeChoice[] Choices = new ThemeChoice[]
        {
            new ThemeChoice("同温层蓝", Color.FromArgb(74, 136, 176)),
            new ThemeChoice("南极星蓝", Color.FromArgb(39, 78, 111)),
            new ThemeChoice("星辰绿", Color.FromArgb(61, 102, 88)),
            new ThemeChoice("银河紫", Color.FromArgb(102, 85, 140)),
            new ThemeChoice("曙光金", Color.FromArgb(177, 132, 73)),
            new ThemeChoice("霞光橙", Color.FromArgb(196, 96, 65)),
            new ThemeChoice("镜空粉", Color.FromArgb(174, 105, 132)),
            new ThemeChoice("星灰", Color.FromArgb(101, 111, 123))
        };

        internal static string CurrentName { get; private set; }

        static Theme()
        {
            Apply("银河紫");
        }

        internal static bool IsChoice(string name)
        {
            return FindChoice(name) != null;
        }

        internal static bool Apply(string name)
        {
            ThemeChoice choice = FindChoice(name);
            if (choice == null)
                choice = FindChoice("银河紫");
            if (choice == null)
                return false;

            CurrentName = choice.Name;
            Accent = choice.Swatch;
            int perceivedBrightness = (choice.Swatch.R * 299 + choice.Swatch.G * 587 + choice.Swatch.B * 114) / 1000;
            AccentText = perceivedBrightness >= 136 ? Ink : Color.White;
            AccentHover = Blend(choice.Swatch, Color.Black, 0.18F);
            AccentPressed = Blend(choice.Swatch, Color.Black, 0.30F);
            AccentSoft = Blend(choice.Swatch, Color.White, 0.86F);
            AccentSoftHover = Blend(choice.Swatch, Color.White, 0.78F);
            AccentSoftPressed = Blend(choice.Swatch, Color.White, 0.69F);
            return true;
        }

        private static ThemeChoice FindChoice(string name)
        {
            foreach (ThemeChoice choice in Choices)
            {
                if (String.Equals(choice.Name, name, StringComparison.Ordinal))
                    return choice;
            }
            return null;
        }

        private static Color Blend(Color source, Color destination, float destinationAmount)
        {
            float sourceAmount = 1F - destinationAmount;
            return Color.FromArgb(
                (int)(source.R * sourceAmount + destination.R * destinationAmount),
                (int)(source.G * sourceAmount + destination.G * destinationAmount),
                (int)(source.B * sourceAmount + destination.B * destinationAmount));
        }

        internal static Font Font(float size, FontStyle style)
        {
            return new Font("Microsoft YaHei UI", size, style, GraphicsUnit.Point);
        }

        internal static Font UtilityFont(float size, FontStyle style)
        {
            return new Font("Segoe UI", size, style, GraphicsUnit.Point);
        }

        internal static GraphicsPath RoundedRectangle(Rectangle bounds, int radius)
        {
            int diameter = radius * 2;
            GraphicsPath path = new GraphicsPath();
            if (diameter <= 0)
            {
                path.AddRectangle(bounds);
                return path;
            }

            Rectangle arc = new Rectangle(bounds.Location, new Size(diameter, diameter));
            path.AddArc(arc, 180, 90);
            arc.X = bounds.Right - diameter;
            path.AddArc(arc, 270, 90);
            arc.Y = bounds.Bottom - diameter;
            path.AddArc(arc, 0, 90);
            arc.X = bounds.Left;
            path.AddArc(arc, 90, 90);
            path.CloseFigure();
            return path;
        }
    }

    internal enum RoundButtonIcon
    {
        None,
        Pin,
        Settings
    }

    internal sealed class RoundButton : Button
    {
        private bool _hovered;
        private bool _pressed;

        public Color NormalColor { get; set; }
        public Color HoverColor { get; set; }
        public Color PressedColor { get; set; }
        public Color TextColor { get; set; }
        public Color BorderColor { get; set; }
        public float BorderWidth { get; set; }
        public int Radius { get; set; }
        public RoundButtonIcon Icon { get; set; }
        public bool ShowFocusIndicator { get; set; }

        public RoundButton()
        {
            NormalColor = Theme.Paper;
            HoverColor = Color.FromArgb(246, 247, 250);
            PressedColor = Color.FromArgb(236, 238, 244);
            TextColor = Theme.Ink;
            BorderColor = Theme.Faint;
            BorderWidth = 1F;
            Radius = 9;
            Icon = RoundButtonIcon.None;
            ShowFocusIndicator = true;
            BackColor = Theme.Canvas;
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            Cursor = Cursors.Hand;
            Font = Theme.Font(9F, FontStyle.Regular);
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            _hovered = true;
            Invalidate();
            base.OnMouseEnter(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            _hovered = false;
            _pressed = false;
            Invalidate();
            base.OnMouseLeave(e);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            _pressed = true;
            Invalidate();
            base.OnMouseDown(e);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            _pressed = false;
            Invalidate();
            base.OnMouseUp(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            e.Graphics.Clear(BackColor);
            Rectangle bounds = new Rectangle(0, 0, Width - 1, Height - 1);
            Color fill = !Enabled ? Color.FromArgb(235, 237, 241) :
                (_pressed ? PressedColor : (_hovered ? HoverColor : NormalColor));

            using (GraphicsPath path = Theme.RoundedRectangle(bounds, Radius))
            using (SolidBrush brush = new SolidBrush(fill))
            using (Pen pen = new Pen(BorderColor, Math.Max(1F, BorderWidth)))
            {
                if (BorderWidth > 1F)
                    pen.Alignment = PenAlignment.Inset;
                e.Graphics.FillPath(brush, path);
                if (BorderColor.A > 0)
                    e.Graphics.DrawPath(pen, path);
            }

            Color foreground = Enabled ? TextColor : Theme.Muted;
            if (Icon == RoundButtonIcon.None)
            {
                TextRenderer.DrawText(e.Graphics, Text, Font, bounds, foreground,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter |
                    TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
            }
            else
            {
                DrawIcon(e.Graphics, bounds, foreground);
            }

            if (ShowFocusIndicator && Focused && ShowFocusCues)
            {
                Rectangle focus = Rectangle.Inflate(bounds, -4, -4);
                ControlPaint.DrawFocusRectangle(e.Graphics, focus, foreground, Color.Transparent);
            }
        }

        private void DrawIcon(Graphics graphics, Rectangle bounds, Color color)
        {
            float centerX = bounds.Left + bounds.Width / 2F;
            float centerY = bounds.Top + bounds.Height / 2F;
            float strokeWidth = Icon == RoundButtonIcon.Pin ? 1.2F : 1.45F;
            using (Pen pen = new Pen(color, strokeWidth))
            {
                pen.StartCap = LineCap.Round;
                pen.EndCap = LineCap.Round;
                if (Icon == RoundButtonIcon.Pin)
                {
                    pen.LineJoin = LineJoin.Round;
                    graphics.DrawLine(pen,
                        centerX - 4.4F, centerY - 7.8F,
                        centerX + 4.4F, centerY - 7.8F);

                    PointF[] pinBody = new PointF[]
                    {
                        new PointF(centerX - 3.1F, centerY - 7.8F),
                        new PointF(centerX - 2.55F, centerY - 2.65F),
                        new PointF(centerX - 5.1F, centerY + 0.8F),
                        new PointF(centerX + 5.1F, centerY + 0.8F),
                        new PointF(centerX + 2.55F, centerY - 2.65F),
                        new PointF(centerX + 3.1F, centerY - 7.8F)
                    };
                    graphics.DrawLines(pen, pinBody);
                    graphics.DrawLine(pen,
                        centerX, centerY + 0.8F,
                        centerX, centerY + 8.7F);
                }
                else if (Icon == RoundButtonIcon.Settings)
                {
                    PointF[] gearPoints = new PointF[16];
                    for (int index = 0; index < gearPoints.Length; index++)
                    {
                        double angle = -Math.PI / 2D + index * Math.PI / 8D;
                        float radius = index % 2 == 0 ? 8F : 6F;
                        gearPoints[index] = new PointF(
                            centerX + (float)Math.Cos(angle) * radius,
                            centerY + (float)Math.Sin(angle) * radius);
                    }
                    using (GraphicsPath gear = new GraphicsPath())
                    {
                        gear.AddPolygon(gearPoints);
                        graphics.DrawPath(pen, gear);
                    }
                    graphics.DrawEllipse(pen, centerX - 2.25F, centerY - 2.25F, 4.5F, 4.5F);
                }
            }
        }
    }

    internal sealed class BadgeLabel : Control
    {
        public Color FillColor { get; set; }
        public Color TextColor { get; set; }
        public int Radius { get; set; }

        public BadgeLabel()
        {
            FillColor = Theme.AccentSoft;
            TextColor = Theme.Accent;
            Radius = 8;
            BackColor = Theme.Canvas;
            Font = Theme.UtilityFont(8F, FontStyle.Bold);
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            e.Graphics.Clear(BackColor);
            Rectangle bounds = new Rectangle(0, 0, Width - 1, Height - 1);
            using (GraphicsPath path = Theme.RoundedRectangle(bounds, Radius))
            using (SolidBrush brush = new SolidBrush(FillColor))
                e.Graphics.FillPath(brush, path);
            TextRenderer.DrawText(e.Graphics, Text, Font, bounds, TextColor,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter |
                TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
        }
    }

    internal sealed class ClipEntryEventArgs : EventArgs
    {
        public ClipEntry Entry { get; private set; }

        public ClipEntryEventArgs(ClipEntry entry)
        {
            Entry = entry;
        }
    }

    internal sealed class ClipSelectionEventArgs : EventArgs
    {
        public ClipEntry Entry { get; private set; }
        public bool IsSelected { get; private set; }

        public ClipSelectionEventArgs(ClipEntry entry, bool isSelected)
        {
            Entry = entry;
            IsSelected = isSelected;
        }
    }

    internal sealed class ClipDragEventArgs : EventArgs
    {
        public ClipEntry Entry { get; private set; }
        public Point ScreenLocation { get; private set; }

        public ClipDragEventArgs(ClipEntry entry, Point screenLocation)
        {
            Entry = entry;
            ScreenLocation = screenLocation;
        }
    }

    internal sealed class ClipCard : UserControl
    {
        private readonly ClipEntry _entry;
        private readonly CheckBox _selectionCheck;
        private readonly Label _numberLabel;
        private readonly Label _timeLabel;
        private readonly Label _contentLabel;
        private readonly PictureBox _imagePreview;
        private readonly RoundButton _copyButton;
        private readonly string _copyAccessibleName;
        private Image _previewImage;
        private bool _settingRangeSelection;
        private bool _rangePointerDown;
        private bool _isLastCopied;

        public event EventHandler<ClipEntryEventArgs> CopyRequested;
        public event EventHandler<ClipSelectionEventArgs> SelectionChanged;
        public event EventHandler<ClipDragEventArgs> RangeSelectionStarted;
        public event EventHandler<ClipDragEventArgs> RangeSelectionMoved;
        public event EventHandler<ClipDragEventArgs> RangeSelectionEnded;

        public ClipCard(ClipEntry entry, int displayNumber, bool isSelected, bool isLastCopied)
        {
            _entry = entry;
            _isLastCopied = isLastCopied;
            Height = entry.IsImage ? 142 : 106;
            BackColor = Theme.Canvas;
            Margin = new Padding(0, 0, 0, 10);
            Padding = new Padding(0);
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);

            _selectionCheck = new CheckBox();
            _selectionCheck.AutoSize = false;
            _selectionCheck.Size = new Size(22, 22);
            _selectionCheck.Checked = isSelected;
            _selectionCheck.Text = String.Empty;
            _selectionCheck.BackColor = Color.Transparent;
            _selectionCheck.FlatStyle = FlatStyle.Flat;
            _selectionCheck.FlatAppearance.BorderColor = Theme.Faint;
            _selectionCheck.FlatAppearance.CheckedBackColor = Theme.AccentSoft;
            _selectionCheck.Cursor = Cursors.Hand;
            _selectionCheck.AccessibleName = "选择第 " + displayNumber + (entry.IsImage ? " 张图片" : " 条内容");
            _selectionCheck.CheckedChanged += SelectionCheckChanged;
            Controls.Add(_selectionCheck);

            _numberLabel = new Label();
            _numberLabel.AutoSize = false;
            _numberLabel.Text = displayNumber.ToString("00");
            _numberLabel.Font = Theme.UtilityFont(8F, FontStyle.Bold);
            _numberLabel.ForeColor = Theme.Accent;
            _numberLabel.BackColor = Color.Transparent;
            _numberLabel.TextAlign = ContentAlignment.MiddleLeft;
            Controls.Add(_numberLabel);

            _timeLabel = new Label();
            _timeLabel.AutoSize = false;
            _timeLabel.Text = FormatTime(entry.CapturedAtLocal);
            _timeLabel.Font = Theme.Font(8F, FontStyle.Regular);
            _timeLabel.ForeColor = Theme.Muted;
            _timeLabel.BackColor = Color.Transparent;
            _timeLabel.TextAlign = ContentAlignment.MiddleLeft;
            Controls.Add(_timeLabel);

            _contentLabel = new Label();
            _contentLabel.AutoSize = false;
            _contentLabel.Text = entry.IsImage ? BuildImageDescription(entry) : BuildPreview(entry.Text);
            _contentLabel.Font = Theme.Font(entry.IsImage ? 8.5F : 9.5F, FontStyle.Regular);
            _contentLabel.ForeColor = Theme.Ink;
            _contentLabel.BackColor = Color.Transparent;
            _contentLabel.AutoEllipsis = true;
            _contentLabel.UseMnemonic = false;
            Controls.Add(_contentLabel);

            if (entry.IsImage)
            {
                _imagePreview = new PictureBox();
                _imagePreview.SizeMode = PictureBoxSizeMode.CenterImage;
                _imagePreview.BackColor = Theme.Canvas;
                _imagePreview.BorderStyle = BorderStyle.FixedSingle;
                _previewImage = CreateThumbnail(entry.ImagePngBase64, 58, 58);
                _imagePreview.Image = _previewImage;
                _imagePreview.AccessibleName = "复制的图片缩略图";
                Controls.Add(_imagePreview);
            }

            _copyButton = new RoundButton();
            _copyButton.Text = "复制";
            _copyButton.Size = new Size(70, 32);
            _copyButton.NormalColor = Theme.AccentSoft;
            _copyButton.HoverColor = Theme.AccentSoftHover;
            _copyButton.PressedColor = Theme.AccentSoftPressed;
            _copyButton.TextColor = Theme.Accent;
            _copyButton.BorderColor = Color.Transparent;
            _copyButton.BackColor = Theme.Paper;
            _copyButton.Font = Theme.Font(8.5F, FontStyle.Bold);
            _copyButton.ShowFocusIndicator = false;
            _copyButton.TabStop = false;
            _copyAccessibleName = "复制第 " + displayNumber + (entry.IsImage ? " 张图片" : " 条内容");
            _copyButton.AccessibleName = _copyAccessibleName;
            _copyButton.Click += CopyButtonClick;
            Controls.Add(_copyButton);

            ApplyCopyButtonStyle();

            AttachRangeSelectionHandlers(this);
            AttachRangeSelectionHandlers(_numberLabel);
            AttachRangeSelectionHandlers(_timeLabel);
            AttachRangeSelectionHandlers(_contentLabel);
            if (_imagePreview != null)
                AttachRangeSelectionHandlers(_imagePreview);

            Resize += delegate { LayoutChildren(); };
            LayoutChildren();
        }

        public ClipEntry Entry
        {
            get { return _entry; }
        }

        public bool IsSelected
        {
            get { return _selectionCheck.Checked; }
        }

        public void SetLastCopied(bool isLastCopied)
        {
            if (_isLastCopied == isLastCopied)
                return;
            _isLastCopied = isLastCopied;
            ApplyCopyButtonStyle();
        }

        public void SetRangeSelected(bool isSelected)
        {
            if (_selectionCheck.Checked == isSelected)
            {
                Invalidate();
                return;
            }

            _settingRangeSelection = true;
            _selectionCheck.Checked = isSelected;
            _settingRangeSelection = false;
            Invalidate();
        }

        private void SelectionCheckChanged(object sender, EventArgs e)
        {
            Invalidate();
            if (_settingRangeSelection)
                return;
            EventHandler<ClipSelectionEventArgs> handler = SelectionChanged;
            if (handler != null)
                handler(this, new ClipSelectionEventArgs(_entry, _selectionCheck.Checked));
        }

        private void AttachRangeSelectionHandlers(Control control)
        {
            control.MouseDown += RangeSelectionMouseDown;
            control.MouseMove += RangeSelectionMouseMove;
            control.MouseUp += RangeSelectionMouseUp;
        }

        private void RangeSelectionMouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left)
                return;

            _rangePointerDown = true;
            Capture = true;
            RaiseRangeSelectionEvent(RangeSelectionStarted, sender as Control, e.Location);
        }

        private void RangeSelectionMouseMove(object sender, MouseEventArgs e)
        {
            if (!_rangePointerDown)
                return;
            RaiseRangeSelectionEvent(RangeSelectionMoved, sender as Control, e.Location);
        }

        private void RangeSelectionMouseUp(object sender, MouseEventArgs e)
        {
            if (!_rangePointerDown || e.Button != MouseButtons.Left)
                return;

            _rangePointerDown = false;
            Capture = false;
            RaiseRangeSelectionEvent(RangeSelectionEnded, sender as Control, e.Location);
        }

        private void RaiseRangeSelectionEvent(EventHandler<ClipDragEventArgs> handler, Control source,
            Point location)
        {
            if (handler == null)
                return;
            Point screenLocation = source == null ? PointToScreen(location) : source.PointToScreen(location);
            handler(this, new ClipDragEventArgs(_entry, screenLocation));
        }

        private void CopyButtonClick(object sender, EventArgs e)
        {
            EventHandler<ClipEntryEventArgs> handler = CopyRequested;
            if (handler != null)
                handler(this, new ClipEntryEventArgs(_entry));
        }

        private void ApplyCopyButtonStyle()
        {
            if (_isLastCopied)
            {
                _copyButton.Text = "✓ 已复制";
                _copyButton.NormalColor = Theme.AccentSoftPressed;
                _copyButton.HoverColor = Theme.AccentSoftHover;
                _copyButton.PressedColor = Theme.AccentSoftPressed;
                _copyButton.TextColor = Theme.Accent;
                _copyButton.BorderColor = Color.Transparent;
                _copyButton.AccessibleName = "上次复制的内容；" + _copyAccessibleName;
            }
            else
            {
                _copyButton.Text = "复制";
                _copyButton.NormalColor = Theme.AccentSoft;
                _copyButton.HoverColor = Theme.AccentSoftHover;
                _copyButton.PressedColor = Theme.AccentSoftPressed;
                _copyButton.TextColor = Theme.Accent;
                _copyButton.BorderColor = Color.Transparent;
                _copyButton.AccessibleName = _copyAccessibleName;
            }
            _copyButton.Invalidate();
        }

        private void LayoutChildren()
        {
            _selectionCheck.Location = new Point(22, 12);
            _numberLabel.SetBounds(49, 15, 30, 18);
            _timeLabel.SetBounds(82, 15, Math.Max(60, Width - 177), 18);
            if (_entry.IsImage)
            {
                _imagePreview.SetBounds(49, 40, 62, 62);
                _contentLabel.SetBounds(49, 108, Math.Max(60, Width - 151), 20);
                _copyButton.Location = new Point(Math.Max(22, Width - 90), 78);
            }
            else
            {
                _contentLabel.SetBounds(49, 40, Math.Max(60, Width - 151), 50);
                _copyButton.Location = new Point(Math.Max(22, Width - 90), 53);
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            Rectangle bounds = new Rectangle(1, 1, Width - 3, Height - 3);
            using (GraphicsPath path = Theme.RoundedRectangle(bounds, 12))
            using (SolidBrush brush = new SolidBrush(IsSelected ? Theme.AccentSoft : Theme.Paper))
            using (Pen border = new Pen(IsSelected ? Theme.Accent : Theme.Faint,
                IsSelected ? 1.5F : 1F))
            {
                e.Graphics.FillPath(brush, path);
                e.Graphics.DrawPath(border, path);
            }

            using (Pen accent = new Pen(Theme.Accent, 3F))
            {
                accent.StartCap = LineCap.Round;
                accent.EndCap = LineCap.Round;
                e.Graphics.DrawLine(accent, 11, 23, 11, Height - 23);
            }

            base.OnPaint(e);
        }

        private static string BuildPreview(string value)
        {
            if (String.IsNullOrEmpty(value))
                return String.Empty;
            string trimmed = value.Trim();
            if (trimmed.Length <= 320)
                return trimmed;
            return trimmed.Substring(0, 320) + "…";
        }

        private static string BuildImageDescription(ClipEntry entry)
        {
            if (entry.ImageWidth > 0 && entry.ImageHeight > 0)
                return "图片 · " + entry.ImageWidth + " × " + entry.ImageHeight;
            return "图片";
        }

        private static Image CreateThumbnail(string imagePngBase64, int maximumWidth, int maximumHeight)
        {
            if (String.IsNullOrWhiteSpace(imagePngBase64))
                return null;

            try
            {
                byte[] bytes = Convert.FromBase64String(imagePngBase64);
                using (MemoryStream stream = new MemoryStream(bytes))
                using (Image original = Image.FromStream(stream))
                {
                    float scale = Math.Min((float)maximumWidth / original.Width,
                        (float)maximumHeight / original.Height);
                    scale = Math.Min(1F, scale);
                    int width = Math.Max(1, (int)Math.Round(original.Width * scale));
                    int height = Math.Max(1, (int)Math.Round(original.Height * scale));
                    Bitmap thumbnail = new Bitmap(width, height);
                    using (Graphics graphics = Graphics.FromImage(thumbnail))
                    {
                        graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                        graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
                        graphics.DrawImage(original, new Rectangle(0, 0, width, height));
                    }
                    return thumbnail;
                }
            }
            catch
            {
                return null;
            }
        }

        private static string FormatTime(DateTime value)
        {
            DateTime today = DateTime.Today;
            if (value.Date == today)
                return "今天  " + value.ToString("HH:mm");
            if (value.Date == today.AddDays(-1))
                return "昨天  " + value.ToString("HH:mm");
            return value.ToString("MM 月 dd 日  HH:mm");
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (_previewImage != null)
                    _previewImage.Dispose();
            }
            base.Dispose(disposing);
        }
    }

    internal sealed class EmptyState : Panel
    {
        public EmptyState()
        {
            Height = 210;
            BackColor = Theme.Canvas;
            Margin = new Padding(0);

            Label glyph = new Label();
            glyph.Text = "···";
            glyph.Font = Theme.UtilityFont(22F, FontStyle.Bold);
            glyph.ForeColor = Theme.Accent;
            glyph.AutoSize = false;
            glyph.TextAlign = ContentAlignment.MiddleCenter;
            glyph.SetBounds(0, 36, 100, 40);
            glyph.Anchor = AnchorStyles.Top;
            Controls.Add(glyph);

            Label title = new Label();
            title.Text = "复制一点文字试试";
            title.Font = Theme.Font(11F, FontStyle.Bold);
            title.ForeColor = Theme.Ink;
            title.AutoSize = false;
            title.TextAlign = ContentAlignment.MiddleCenter;
            title.SetBounds(0, 88, 240, 28);
            title.Anchor = AnchorStyles.Top;
            Controls.Add(title);

            Label hint = new Label();
            hint.Text = "拾贴会自动收好，之后随时取用";
            hint.Font = Theme.Font(8.5F, FontStyle.Regular);
            hint.ForeColor = Theme.Muted;
            hint.AutoSize = false;
            hint.TextAlign = ContentAlignment.MiddleCenter;
            hint.SetBounds(0, 120, 280, 24);
            hint.Anchor = AnchorStyles.Top;
            Controls.Add(hint);

            Resize += delegate
            {
                glyph.Left = (Width - glyph.Width) / 2;
                title.Left = (Width - title.Width) / 2;
                hint.Left = (Width - hint.Width) / 2;
            };
        }
    }
}
