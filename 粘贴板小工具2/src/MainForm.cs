using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using Timer = System.Windows.Forms.Timer;

namespace QuietClip
{
    internal sealed class MainForm : Form
    {
        private const int HotkeyId = 0x4A21;
        private const int MaxClipboardCharacters = 1000000;
        private const int ResizeGrip = 7;

        private readonly AppState _state;
        private readonly TableLayoutPanel _root;
        private readonly Panel _header;
        private readonly Panel _toolbar;
        private readonly Panel _settingsPanel;
        private readonly FlowLayoutPanel _historyPanel;
        private readonly Panel _footer;
        private readonly BadgeLabel _countLabel;
        private readonly Label _statusLabel;
        private readonly Label _hotkeyHint;
        private readonly RoundButton _copyAllButton;
        private readonly RoundButton _clearButton;
        private readonly RoundButton _pinButton;
        private readonly RoundButton _settingsButton;
        private readonly RoundButton _hotkeyRecorder;
        private readonly RoundButton _colorPicker;
        private readonly RoundButton _statusActionButton;
        private readonly NumericUpDown _maxItemsInput;
        private readonly Timer _clipboardRetryTimer;
        private readonly Timer _statusTimer;
        private readonly NotifyIcon _trayIcon;
        private readonly ContextMenuStrip _colorMenu;
        private readonly ToolTip _headerToolTip;
        private readonly HashSet<string> _selectedEntryIds;
        private Icon _applicationIcon;

        private bool _settingsVisible;
        private bool _loadingSettings;
        private bool _capturingHotkey;
        private int _heightBeforeSettings;
        private bool _allowExit;
        private bool _hotkeyRegistered;
        private bool _clipboardListenerRegistered;
        private int _clipboardRetryCount;
        private string _suppressedClipboardText;
        private uint _registeredModifiers;
        private Keys _registeredKey;
        private List<ClipEntry> _undoBuffer;
        private Action _statusAction;

        public MainForm()
        {
            _state = StateStore.Load();
            _selectedEntryIds = new HashSet<string>(StringComparer.Ordinal);
            _headerToolTip = new ToolTip();
            _headerToolTip.InitialDelay = 250;
            _headerToolTip.ReshowDelay = 100;
            _headerToolTip.AutoPopDelay = 3000;
            Theme.Apply(_state.ThemeName);
            _applicationIcon = CreateApplicationIcon();

            Text = "拾贴 · 剪贴板";
            Icon = _applicationIcon;
            BackColor = Theme.Faint;
            StartPosition = FormStartPosition.Manual;
            Size = new Size(_state.WindowWidth, _state.WindowHeight);
            MinimumSize = new Size(280, 340);
            FormBorderStyle = FormBorderStyle.None;
            Padding = new Padding(1);
            KeyPreview = true;
            AutoScaleMode = AutoScaleMode.Dpi;
            TopMost = _state.AlwaysOnTop;

            RestoreWindowPosition();

            _root = new TableLayoutPanel();
            _root.ColumnCount = 1;
            _root.RowCount = 5;
            _root.Dock = DockStyle.Fill;
            _root.Margin = new Padding(0);
            _root.Padding = new Padding(0);
            _root.BackColor = Theme.Canvas;
            _root.RowStyles.Add(new RowStyle(SizeType.Absolute, 46F));
            _root.RowStyles.Add(new RowStyle(SizeType.Absolute, 56F));
            _root.RowStyles.Add(new RowStyle(SizeType.Absolute, 0F));
            _root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            _root.RowStyles.Add(new RowStyle(SizeType.Absolute, 34F));
            Controls.Add(_root);

            _header = BuildHeader(out _countLabel, out _pinButton, out _settingsButton);
            _toolbar = BuildToolbar(out _copyAllButton, out _clearButton);
            _settingsPanel = BuildSettings(out _maxItemsInput, out _hotkeyRecorder, out _colorPicker);
            _colorMenu = BuildColorMenu();
            _historyPanel = BuildHistoryPanel();
            _footer = BuildFooter(out _statusLabel, out _statusActionButton, out _hotkeyHint);

            _root.Controls.Add(_header, 0, 0);
            _root.Controls.Add(_toolbar, 0, 1);
            _root.Controls.Add(_settingsPanel, 0, 2);
            _root.Controls.Add(_historyPanel, 0, 3);
            _root.Controls.Add(_footer, 0, 4);

            _settingsButton.Click += delegate { ToggleSettings(); };
            _pinButton.Click += delegate { ToggleAlwaysOnTop(); };
            _copyAllButton.Click += delegate { CopyPrimaryItems(); };
            _clearButton.Click += delegate { DeletePrimaryItems(); };
            _statusActionButton.Click += delegate
            {
                Action action = _statusAction;
                if (action != null)
                    action();
            };

            _maxItemsInput.ValueChanged += MaxItemsChanged;
            _hotkeyRecorder.Click += delegate { BeginHotkeyCapture(); };
            _colorPicker.Click += delegate
            {
                _colorMenu.Show(_colorPicker, new Point(0, _colorPicker.Height + 4));
            };
            KeyDown += HotkeyRecorderKeyDown;
            _hotkeyRecorder.KeyDown += HotkeyRecorderKeyDown;
            Deactivate += delegate
            {
                if (_capturingHotkey)
                    CancelHotkeyCapture();
            };

            _clipboardRetryTimer = new Timer();
            _clipboardRetryTimer.Interval = 80;
            _clipboardRetryTimer.Tick += delegate
            {
                _clipboardRetryTimer.Stop();
                CaptureClipboardText();
            };

            _statusTimer = new Timer();
            _statusTimer.Interval = 3200;
            _statusTimer.Tick += delegate
            {
                _statusTimer.Stop();
                SetListeningStatus();
            };

            _trayIcon = BuildTrayIcon();

            Resize += FormResized;
            ResizeEnd += delegate { SaveWindowGeometry(); };
            FormClosing += MainFormClosing;
            Shown += delegate
            {
                RefreshHistoryView();
                SetListeningStatus();
            };

            LoadSettingsControls();
            UpdatePinButtonAppearance();
            RefreshHistoryView();
            UpdateWindowChrome();
        }

        protected override CreateParams CreateParams
        {
            get
            {
                const int CS_DROPSHADOW = 0x00020000;
                CreateParams parameters = base.CreateParams;
                parameters.ClassStyle |= CS_DROPSHADOW;
                return parameters;
            }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            _clipboardListenerRegistered = NativeMethods.AddClipboardFormatListener(Handle);
            if (!TryApplyHotkey(_state.HotkeyModifiers, _state.HotkeyKey))
                ShowStatus("快捷键被其他程序占用，请在设置中更换", null, null);
        }

        protected override void OnHandleDestroyed(EventArgs e)
        {
            if (_clipboardListenerRegistered)
            {
                NativeMethods.RemoveClipboardFormatListener(Handle);
                _clipboardListenerRegistered = false;
            }
            if (_hotkeyRegistered)
            {
                NativeMethods.UnregisterHotKey(Handle, HotkeyId);
                _hotkeyRegistered = false;
            }
            base.OnHandleDestroyed(e);
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == NativeMethods.WM_NCHITTEST && WindowState == FormWindowState.Normal)
            {
                base.WndProc(ref m);
                long packed = m.LParam.ToInt64();
                Point screenPoint = new Point((short)(packed & 0xFFFF), (short)((packed >> 16) & 0xFFFF));
                Point clientPoint = PointToClient(screenPoint);
                bool left = clientPoint.X <= ResizeGrip;
                bool right = clientPoint.X >= ClientSize.Width - ResizeGrip;
                bool top = clientPoint.Y <= ResizeGrip;
                bool bottom = clientPoint.Y >= ClientSize.Height - ResizeGrip;

                if (left && top) m.Result = (IntPtr)NativeMethods.HTTOPLEFT;
                else if (right && top) m.Result = (IntPtr)NativeMethods.HTTOPRIGHT;
                else if (left && bottom) m.Result = (IntPtr)NativeMethods.HTBOTTOMLEFT;
                else if (right && bottom) m.Result = (IntPtr)NativeMethods.HTBOTTOMRIGHT;
                else if (left) m.Result = (IntPtr)NativeMethods.HTLEFT;
                else if (right) m.Result = (IntPtr)NativeMethods.HTRIGHT;
                else if (top) m.Result = (IntPtr)NativeMethods.HTTOP;
                else if (bottom) m.Result = (IntPtr)NativeMethods.HTBOTTOM;
                return;
            }

            if (m.Msg == NativeMethods.WM_CLIPBOARDUPDATE)
            {
                _clipboardRetryCount = 0;
                BeginInvoke(new Action(CaptureClipboardText));
            }
            else if (m.Msg == NativeMethods.WM_HOTKEY && m.WParam.ToInt32() == HotkeyId)
            {
                ToggleVisibility();
            }
            base.WndProc(ref m);
        }

        private Panel BuildHeader(out BadgeLabel countLabel, out RoundButton pinButton,
            out RoundButton settingsButton)
        {
            Panel panel = new Panel();
            panel.Dock = DockStyle.Fill;
            panel.BackColor = Theme.Canvas;

            Panel mark = new Panel();
            mark.SetBounds(12, 10, 25, 25);
            mark.BackColor = Theme.Canvas;
            mark.Paint += delegate(object sender, PaintEventArgs e)
            {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                using (SolidBrush brush = new SolidBrush(Theme.Accent))
                using (GraphicsPath path = Theme.RoundedRectangle(new Rectangle(0, 0, 24, 24), 6))
                    e.Graphics.FillPath(brush, path);
                using (Pen pen = new Pen(Theme.AccentText, 1.7F))
                {
                    pen.StartCap = LineCap.Round;
                    pen.EndCap = LineCap.Round;
                    e.Graphics.DrawLine(pen, 7, 8, 18, 8);
                    e.Graphics.DrawLine(pen, 7, 13, 16, 13);
                    e.Graphics.DrawLine(pen, 7, 18, 13, 18);
                }
            };
            panel.Controls.Add(mark);

            Label title = new Label();
            title.Text = "拾贴";
            title.Font = Theme.Font(10.5F, FontStyle.Bold);
            title.ForeColor = Theme.Ink;
            title.AutoSize = true;
            title.Location = new Point(43, 12);
            panel.Controls.Add(title);

            countLabel = new BadgeLabel();
            countLabel.Size = new Size(52, 26);
            countLabel.AccessibleName = "当前历史条数";
            panel.Controls.Add(countLabel);

            pinButton = new RoundButton();
            pinButton.Icon = RoundButtonIcon.Pin;
            pinButton.Size = new Size(32, 30);
            pinButton.NormalColor = Theme.Paper;
            pinButton.HoverColor = Theme.AccentSoft;
            pinButton.PressedColor = Theme.AccentSoftPressed;
            pinButton.TextColor = Theme.Muted;
            pinButton.BorderColor = Theme.Faint;
            pinButton.Font = Theme.Font(8F, FontStyle.Regular);
            pinButton.AccessibleName = "开启窗口置顶";
            _headerToolTip.SetToolTip(pinButton, "置顶");
            panel.Controls.Add(pinButton);

            settingsButton = new RoundButton();
            settingsButton.Icon = RoundButtonIcon.Settings;
            settingsButton.Size = new Size(32, 30);
            settingsButton.NormalColor = Theme.Paper;
            settingsButton.HoverColor = Theme.AccentSoft;
            settingsButton.PressedColor = Theme.AccentSoftPressed;
            settingsButton.TextColor = Theme.Muted;
            settingsButton.BorderColor = Theme.Faint;
            settingsButton.Font = Theme.Font(8F, FontStyle.Regular);
            settingsButton.AccessibleName = "打开设置";
            _headerToolTip.SetToolTip(settingsButton, "设置");
            panel.Controls.Add(settingsButton);

            RoundButton minimizeButton = BuildWindowButton("—", "隐藏到系统托盘");
            minimizeButton.Font = Theme.UtilityFont(10F, FontStyle.Regular);
            minimizeButton.Click += delegate { Hide(); };
            panel.Controls.Add(minimizeButton);

            RoundButton closeButton = BuildWindowButton("×", "关闭到系统托盘");
            closeButton.Font = Theme.UtilityFont(13F, FontStyle.Regular);
            closeButton.HoverColor = Color.FromArgb(241, 91, 101);
            closeButton.PressedColor = Color.FromArgb(211, 66, 78);
            closeButton.Click += delegate { Close(); };
            panel.Controls.Add(closeButton);

            MouseEventHandler dragHandler = delegate(object sender, MouseEventArgs e)
            {
                if (e.Button == MouseButtons.Left)
                {
                    NativeMethods.ReleaseCapture();
                    NativeMethods.SendMessage(Handle, NativeMethods.WM_NCLBUTTONDOWN,
                        (IntPtr)NativeMethods.HTCAPTION, IntPtr.Zero);
                }
            };
            EventHandler maximizeHandler = delegate
            {
                WindowState = WindowState == FormWindowState.Maximized ?
                    FormWindowState.Normal : FormWindowState.Maximized;
            };
            panel.MouseDown += dragHandler;
            mark.MouseDown += dragHandler;
            title.MouseDown += dragHandler;
            panel.DoubleClick += maximizeHandler;
            mark.DoubleClick += maximizeHandler;
            title.DoubleClick += maximizeHandler;

            BadgeLabel countForLayout = countLabel;
            RoundButton pinForLayout = pinButton;
            RoundButton settingsForLayout = settingsButton;
            panel.Resize += delegate
            {
                closeButton.SetBounds(panel.ClientSize.Width - 39, 7, 32, 30);
                minimizeButton.SetBounds(panel.ClientSize.Width - 75, 7, 32, 30);
                settingsForLayout.Location = new Point(panel.ClientSize.Width - 111, 7);
                pinForLayout.Location = new Point(panel.ClientSize.Width - 147, 7);
                countForLayout.Visible = panel.ClientSize.Width >= 380;
                countForLayout.Location = new Point(panel.ClientSize.Width - 205, 9);
            };

            return panel;
        }

        private static RoundButton BuildWindowButton(string text, string accessibleName)
        {
            RoundButton button = new RoundButton();
            button.Text = text;
            button.Size = new Size(32, 30);
            button.Radius = 8;
            button.NormalColor = Theme.Canvas;
            button.HoverColor = Theme.AccentSoft;
            button.PressedColor = Theme.AccentSoftPressed;
            button.TextColor = Theme.Muted;
            button.BorderColor = Color.Transparent;
            button.AccessibleName = accessibleName;
            return button;
        }

        private Panel BuildToolbar(out RoundButton copyAllButton, out RoundButton clearButton)
        {
            Panel panel = new Panel();
            panel.Dock = DockStyle.Fill;
            panel.BackColor = Theme.Canvas;

            copyAllButton = new RoundButton();
            copyAllButton.Text = "复制全部";
            copyAllButton.SetBounds(18, 8, 132, 40);
            copyAllButton.NormalColor = Theme.Accent;
            copyAllButton.HoverColor = Theme.AccentHover;
            copyAllButton.PressedColor = Theme.AccentPressed;
            copyAllButton.TextColor = Theme.AccentText;
            copyAllButton.BorderColor = Color.Transparent;
            copyAllButton.Font = Theme.Font(9F, FontStyle.Bold);
            copyAllButton.AccessibleName = "复制全部历史内容";
            panel.Controls.Add(copyAllButton);

            clearButton = new RoundButton();
            clearButton.Text = "清空";
            clearButton.SetBounds(158, 8, 78, 40);
            clearButton.NormalColor = Theme.Paper;
            clearButton.HoverColor = Color.FromArgb(251, 239, 241);
            clearButton.PressedColor = Color.FromArgb(247, 225, 228);
            clearButton.TextColor = Theme.Danger;
            clearButton.BorderColor = Theme.Faint;
            clearButton.Font = Theme.Font(9F, FontStyle.Regular);
            clearButton.AccessibleName = "清空全部历史内容";
            panel.Controls.Add(clearButton);

            return panel;
        }

        private Panel BuildSettings(out NumericUpDown maxInput, out RoundButton hotkeyRecorder,
            out RoundButton colorPicker)
        {
            Panel panel = new Panel();
            panel.Dock = DockStyle.Fill;
            panel.BackColor = Theme.Canvas;

            Panel card = new Panel();
            card.BackColor = Theme.Paper;
            card.SetBounds(12, 2, 400, 180);
            card.Paint += delegate(object sender, PaintEventArgs e)
            {
                using (Pen pen = new Pen(Theme.Faint))
                    e.Graphics.DrawLine(pen, 0, card.Height - 1, card.Width, card.Height - 1);
            };
            panel.Controls.Add(card);

            Label maxLabel = BuildSettingLabel("最多保留");
            maxLabel.Location = new Point(14, 13);
            card.Controls.Add(maxLabel);

            maxInput = new NumericUpDown();
            maxInput.Minimum = 1;
            maxInput.Maximum = 200;
            maxInput.SetBounds(90, 9, 70, 27);
            maxInput.Font = Theme.UtilityFont(9F, FontStyle.Regular);
            maxInput.BorderStyle = BorderStyle.FixedSingle;
            maxInput.AccessibleName = "最多保留条数";
            card.Controls.Add(maxInput);

            Label unit = new Label();
            unit.Text = "条";
            unit.AutoSize = true;
            unit.Font = Theme.Font(8F, FontStyle.Regular);
            unit.ForeColor = Theme.Muted;
            unit.Location = new Point(166, 14);
            card.Controls.Add(unit);

            Label hotkeyLabel = BuildSettingLabel("唤醒 / 隐藏快捷键");
            hotkeyLabel.Location = new Point(14, 48);
            card.Controls.Add(hotkeyLabel);

            hotkeyRecorder = new RoundButton();
            hotkeyRecorder.SetBounds(14, 70, 250, 34);
            hotkeyRecorder.NormalColor = Theme.AccentSoft;
            hotkeyRecorder.HoverColor = Theme.AccentSoftHover;
            hotkeyRecorder.PressedColor = Theme.AccentSoftPressed;
            hotkeyRecorder.TextColor = Theme.Accent;
            hotkeyRecorder.BorderColor = Color.Transparent;
            hotkeyRecorder.Font = Theme.UtilityFont(8.5F, FontStyle.Bold);
            hotkeyRecorder.AccessibleName = "设置唤醒和隐藏快捷键";
            card.Controls.Add(hotkeyRecorder);

            Label colorLabel = BuildSettingLabel("界面颜色");
            colorLabel.Location = new Point(14, 115);
            card.Controls.Add(colorLabel);

            colorPicker = new RoundButton();
            colorPicker.SetBounds(90, 109, 174, 34);
            colorPicker.NormalColor = Theme.AccentSoft;
            colorPicker.HoverColor = Theme.AccentSoftHover;
            colorPicker.PressedColor = Theme.AccentSoftPressed;
            colorPicker.TextColor = Theme.Accent;
            colorPicker.BorderColor = Color.Transparent;
            colorPicker.Font = Theme.Font(8.5F, FontStyle.Bold);
            colorPicker.AccessibleName = "选择界面颜色";
            card.Controls.Add(colorPicker);

            Label saveHint = new Label();
            saveHint.Text = "点击快捷键按钮录入；设置自动保存";
            saveHint.AutoSize = true;
            saveHint.Font = Theme.Font(7.5F, FontStyle.Regular);
            saveHint.ForeColor = Theme.Muted;
            saveHint.Location = new Point(14, 154);
            card.Controls.Add(saveHint);

            RoundButton recorderForLayout = hotkeyRecorder;
            RoundButton colorForLayout = colorPicker;
            panel.Resize += delegate
            {
                card.SetBounds(12, 2, Math.Max(236, panel.ClientSize.Width - 24), 180);
                recorderForLayout.Width = Math.Max(190, card.ClientSize.Width - 28);
                colorForLayout.Width = Math.Max(140, card.ClientSize.Width - 104);
            };

            return panel;
        }

        private ContextMenuStrip BuildColorMenu()
        {
            ContextMenuStrip menu = new ContextMenuStrip();
            menu.Font = Theme.Font(9F, FontStyle.Regular);
            menu.ShowImageMargin = true;
            menu.ShowCheckMargin = true;
            menu.BackColor = Theme.Paper;

            foreach (ThemeChoice choice in Theme.Choices)
            {
                ToolStripMenuItem item = new ToolStripMenuItem(choice.Name);
                item.Tag = choice.Name;
                item.Image = CreateColorSwatch(choice.Swatch);
                item.Checked = String.Equals(choice.Name, _state.ThemeName, StringComparison.Ordinal);
                item.Click += ColorMenuItemClick;
                menu.Items.Add(item);
            }
            return menu;
        }

        private static Bitmap CreateColorSwatch(Color color)
        {
            Bitmap bitmap = new Bitmap(18, 18);
            using (Graphics graphics = Graphics.FromImage(bitmap))
            {
                graphics.SmoothingMode = SmoothingMode.AntiAlias;
                graphics.Clear(Color.Transparent);
                using (SolidBrush brush = new SolidBrush(color))
                    graphics.FillEllipse(brush, 2, 2, 14, 14);
                using (Pen border = new Pen(Color.FromArgb(65, Theme.Ink)))
                    graphics.DrawEllipse(border, 2, 2, 14, 14);
            }
            return bitmap;
        }

        private void ColorMenuItemClick(object sender, EventArgs e)
        {
            ToolStripMenuItem item = sender as ToolStripMenuItem;
            if (item == null || item.Tag == null)
                return;
            ApplyColorTheme(item.Tag.ToString());
        }

        private void ApplyColorTheme(string themeName)
        {
            if (!Theme.Apply(themeName))
                return;

            _state.ThemeName = Theme.CurrentName;
            _colorPicker.Text = Theme.CurrentName + "  ▾";
            _colorPicker.NormalColor = Theme.AccentSoft;
            _colorPicker.HoverColor = Theme.AccentSoftHover;
            _colorPicker.PressedColor = Theme.AccentSoftPressed;
            _colorPicker.TextColor = Theme.Accent;

            _countLabel.FillColor = Theme.AccentSoft;
            _countLabel.TextColor = Theme.Accent;
            _settingsButton.HoverColor = Theme.AccentSoft;
            _settingsButton.PressedColor = Theme.AccentSoftPressed;
            _settingsButton.TextColor = Theme.Muted;
            _pinButton.HoverColor = Theme.AccentSoftHover;
            _pinButton.PressedColor = Theme.AccentSoftPressed;
            _copyAllButton.NormalColor = Theme.Accent;
            _copyAllButton.HoverColor = Theme.AccentHover;
            _copyAllButton.PressedColor = Theme.AccentPressed;
            _copyAllButton.TextColor = Theme.AccentText;
            _hotkeyRecorder.NormalColor = Theme.AccentSoft;
            _hotkeyRecorder.HoverColor = Theme.AccentSoftHover;
            _hotkeyRecorder.PressedColor = Theme.AccentSoftPressed;
            _hotkeyRecorder.TextColor = Theme.Accent;
            _statusActionButton.NormalColor = Theme.AccentSoft;
            _statusActionButton.HoverColor = Theme.AccentSoftHover;
            _statusActionButton.PressedColor = Theme.AccentSoftPressed;
            _statusActionButton.TextColor = Theme.Accent;

            foreach (ToolStripMenuItem colorItem in _colorMenu.Items.OfType<ToolStripMenuItem>())
                colorItem.Checked = String.Equals(colorItem.Tag as string, Theme.CurrentName, StringComparison.Ordinal);

            UpdateWindowButtonColors(_header);
            UpdatePinButtonAppearance();
            RefreshApplicationIcon();
            RefreshHistoryView();
            _root.Invalidate(true);
            StateStore.Save(_state);
            ShowStatus("已切换为" + Theme.CurrentName, null, null);
        }

        private void ToggleAlwaysOnTop()
        {
            _state.AlwaysOnTop = !_state.AlwaysOnTop;
            TopMost = _state.AlwaysOnTop;
            UpdatePinButtonAppearance();
            StateStore.Save(_state);
            ShowStatus(_state.AlwaysOnTop ? "窗口已置顶" : "已取消窗口置顶", null, null);
        }

        private void UpdatePinButtonAppearance()
        {
            if (_pinButton == null)
                return;

            _pinButton.AccessibleName = _state.AlwaysOnTop ? "取消窗口置顶" : "开启窗口置顶";
            _pinButton.NormalColor = _state.AlwaysOnTop ? Theme.AccentSoft : Theme.Paper;
            _pinButton.HoverColor = Theme.AccentSoftHover;
            _pinButton.PressedColor = Theme.AccentSoftPressed;
            _pinButton.TextColor = _state.AlwaysOnTop ? Theme.Accent : Theme.Muted;
            _pinButton.BorderColor = _state.AlwaysOnTop ? Theme.Accent : Theme.Faint;
            _headerToolTip.SetToolTip(_pinButton, "置顶");
            _pinButton.Invalidate();
        }

        private static void UpdateWindowButtonColors(Control parent)
        {
            foreach (Control child in parent.Controls)
            {
                RoundButton button = child as RoundButton;
                if (button != null && (button.AccessibleName == "隐藏到系统托盘" ||
                    button.AccessibleName == "关闭到系统托盘"))
                {
                    if (button.AccessibleName == "隐藏到系统托盘")
                    {
                        button.HoverColor = Theme.AccentSoft;
                        button.PressedColor = Theme.AccentSoftPressed;
                    }
                    button.Invalidate();
                }
                if (child.HasChildren)
                    UpdateWindowButtonColors(child);
            }
        }

        private void RefreshApplicationIcon()
        {
            Icon previous = _applicationIcon;
            _applicationIcon = CreateApplicationIcon();
            Icon = _applicationIcon;
            if (_trayIcon != null)
                _trayIcon.Icon = _applicationIcon;
            if (previous != null)
                previous.Dispose();
        }

        private static Label BuildSettingLabel(string text)
        {
            Label label = new Label();
            label.Text = text;
            label.AutoSize = true;
            label.Font = Theme.Font(8F, FontStyle.Bold);
            label.ForeColor = Theme.Ink;
            return label;
        }

        private FlowLayoutPanel BuildHistoryPanel()
        {
            FlowLayoutPanel panel = new FlowLayoutPanel();
            panel.Dock = DockStyle.Fill;
            panel.BackColor = Theme.Canvas;
            panel.FlowDirection = FlowDirection.TopDown;
            panel.WrapContents = false;
            panel.AutoScroll = true;
            panel.Padding = new Padding(18, 8, 18, 8);
            panel.Resize += delegate { LayoutHistoryItems(); };
            return panel;
        }

        private Panel BuildFooter(out Label statusLabel, out RoundButton actionButton, out Label hotkeyHint)
        {
            Panel panel = new Panel();
            panel.Dock = DockStyle.Fill;
            panel.BackColor = Theme.Canvas;

            statusLabel = new Label();
            statusLabel.AutoSize = false;
            statusLabel.SetBounds(18, 4, 190, 24);
            statusLabel.Font = Theme.Font(7.5F, FontStyle.Regular);
            statusLabel.ForeColor = Theme.Muted;
            statusLabel.TextAlign = ContentAlignment.MiddleLeft;
            panel.Controls.Add(statusLabel);

            actionButton = new RoundButton();
            actionButton.Visible = false;
            actionButton.SetBounds(130, 3, 52, 26);
            actionButton.NormalColor = Theme.AccentSoft;
            actionButton.HoverColor = Theme.AccentSoftHover;
            actionButton.PressedColor = Theme.AccentSoftPressed;
            actionButton.TextColor = Theme.Accent;
            actionButton.BorderColor = Color.Transparent;
            actionButton.Font = Theme.Font(7.5F, FontStyle.Bold);
            panel.Controls.Add(actionButton);

            hotkeyHint = new Label();
            hotkeyHint.AutoSize = false;
            hotkeyHint.Font = Theme.UtilityFont(7.5F, FontStyle.Regular);
            hotkeyHint.ForeColor = Theme.Muted;
            hotkeyHint.TextAlign = ContentAlignment.MiddleRight;
            hotkeyHint.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            panel.Controls.Add(hotkeyHint);

            Label hotkeyHintForLayout = hotkeyHint;
            Label statusForLayout = statusLabel;
            panel.Resize += delegate
            {
                bool compact = panel.ClientSize.Width < 360;
                hotkeyHintForLayout.Visible = !compact;
                statusForLayout.Width = compact ? Math.Max(120, panel.ClientSize.Width - 36) : 190;
                if (!compact)
                    hotkeyHintForLayout.SetBounds(panel.ClientSize.Width - 205, 4, 190, 24);
            };
            return panel;
        }

        private NotifyIcon BuildTrayIcon()
        {
            ContextMenuStrip menu = new ContextMenuStrip();
            menu.Font = Theme.Font(9F, FontStyle.Regular);

            ToolStripMenuItem toggle = new ToolStripMenuItem("显示 / 隐藏拾贴");
            toggle.Click += delegate { ToggleVisibility(); };
            menu.Items.Add(toggle);

            ToolStripMenuItem copyAll = new ToolStripMenuItem("复制全部");
            copyAll.Click += delegate { CopyAllItems(); };
            menu.Items.Add(copyAll);

            menu.Items.Add(new ToolStripSeparator());
            ToolStripMenuItem exit = new ToolStripMenuItem("退出");
            exit.Click += delegate
            {
                _allowExit = true;
                Close();
            };
            menu.Items.Add(exit);

            NotifyIcon icon = new NotifyIcon();
            icon.Icon = _applicationIcon;
            icon.Text = "拾贴 · 正在记录文字剪贴板";
            icon.Visible = true;
            icon.ContextMenuStrip = menu;
            icon.DoubleClick += delegate { ToggleVisibility(); };
            return icon;
        }

        private static Icon CreateApplicationIcon()
        {
            using (Bitmap bitmap = new Bitmap(32, 32))
            using (Graphics graphics = Graphics.FromImage(bitmap))
            {
                graphics.SmoothingMode = SmoothingMode.AntiAlias;
                graphics.Clear(Color.Transparent);
                using (SolidBrush brush = new SolidBrush(Theme.Accent))
                using (GraphicsPath path = Theme.RoundedRectangle(new Rectangle(2, 2, 28, 28), 7))
                    graphics.FillPath(brush, path);
                using (Pen pen = new Pen(Theme.AccentText, 2.4F))
                {
                    pen.StartCap = LineCap.Round;
                    pen.EndCap = LineCap.Round;
                    graphics.DrawLine(pen, 9, 11, 23, 11);
                    graphics.DrawLine(pen, 9, 16, 20, 16);
                    graphics.DrawLine(pen, 9, 21, 17, 21);
                }

                IntPtr handle = bitmap.GetHicon();
                try
                {
                    using (Icon temporary = Icon.FromHandle(handle))
                        return (Icon)temporary.Clone();
                }
                finally
                {
                    NativeMethods.DestroyIcon(handle);
                }
            }
        }

        private void LoadSettingsControls()
        {
            _loadingSettings = true;
            _maxItemsInput.Value = _state.MaxItems;
            _hotkeyRecorder.Text = FormatHotkey(_state.HotkeyModifiers, _state.HotkeyKey);
            _colorPicker.Text = _state.ThemeName + "  ▾";
            _loadingSettings = false;
            UpdateHotkeyHint();
        }

        private void ToggleSettings()
        {
            if (_settingsVisible && _capturingHotkey)
                CancelHotkeyCapture();

            if (!_settingsVisible)
            {
                _heightBeforeSettings = Height;
                MinimumSize = new Size(280, 430);
                if (Height < 430)
                    Height = 430;
            }
            else
            {
                MinimumSize = new Size(280, 340);
            }

            _settingsVisible = !_settingsVisible;
            _root.RowStyles[2].Height = _settingsVisible ? 188F : 0F;
            _settingsPanel.Visible = _settingsVisible;
            _settingsButton.AccessibleName = _settingsVisible ? "收起设置" : "打开设置";
            _headerToolTip.SetToolTip(_settingsButton, _settingsVisible ? "收起设置" : "设置");
            if (!_settingsVisible && _heightBeforeSettings >= 340 && _heightBeforeSettings < Height)
                Height = _heightBeforeSettings;
            LayoutHistoryItems();
        }

        private void MaxItemsChanged(object sender, EventArgs e)
        {
            if (_loadingSettings)
                return;

            _state.MaxItems = Decimal.ToInt32(_maxItemsInput.Value);
            if (_state.Items.Count > _state.MaxItems)
                _state.Items.RemoveRange(_state.MaxItems, _state.Items.Count - _state.MaxItems);
            StateStore.Save(_state);
            RefreshHistoryView();
            ShowStatus("已改为最多保留 " + _state.MaxItems + " 条", null, null);
        }

        private void BeginHotkeyCapture()
        {
            if (_loadingSettings)
                return;

            _capturingHotkey = true;
            _hotkeyRecorder.Text = "请按下新组合键…";
            _hotkeyRecorder.NormalColor = Theme.AccentSoftPressed;
            _hotkeyRecorder.Invalidate();
            _hotkeyRecorder.Focus();
            ShowStatus("请按下包含 Ctrl、Alt 或 Shift 的组合键", null, null);
        }

        private void CancelHotkeyCapture()
        {
            _capturingHotkey = false;
            _hotkeyRecorder.Text = FormatHotkey(_state.HotkeyModifiers, _state.HotkeyKey);
            _hotkeyRecorder.NormalColor = Theme.AccentSoft;
            _hotkeyRecorder.Invalidate();
        }

        private void HotkeyRecorderKeyDown(object sender, KeyEventArgs e)
        {
            if (!_capturingHotkey)
                return;

            e.Handled = true;
            e.SuppressKeyPress = true;
            if (e.KeyCode == Keys.Escape)
            {
                CancelHotkeyCapture();
                ShowStatus("已取消快捷键设置", null, null);
                return;
            }

            if (IsModifierKey(e.KeyCode))
            {
                _hotkeyRecorder.Text = "再按一个字母、数字或功能键";
                return;
            }

            List<string> modifierParts = new List<string>();
            if (e.Control) modifierParts.Add("Ctrl");
            if (e.Alt) modifierParts.Add("Alt");
            if (e.Shift) modifierParts.Add("Shift");
            if (modifierParts.Count == 0)
            {
                _hotkeyRecorder.Text = "需包含 Ctrl、Alt 或 Shift";
                return;
            }

            string requestedModifiers = String.Join(" + ", modifierParts.ToArray());
            string requestedKey = FormatKeyForStorage(e.KeyCode);
            if (String.IsNullOrEmpty(requestedKey))
            {
                _hotkeyRecorder.Text = "这个按键暂不支持，请换一个";
                return;
            }

            if (TryApplyHotkey(requestedModifiers, requestedKey))
            {
                _state.HotkeyModifiers = requestedModifiers;
                _state.HotkeyKey = requestedKey;
                StateStore.Save(_state);
                _capturingHotkey = false;
                _hotkeyRecorder.Text = FormatHotkey(requestedModifiers, requestedKey);
                _hotkeyRecorder.NormalColor = Theme.AccentSoft;
                _hotkeyRecorder.Invalidate();
                UpdateHotkeyHint();
                ShowStatus("快捷键已更新", null, null);
            }
            else
            {
                CancelHotkeyCapture();
                ShowStatus("这个快捷键被其他程序占用，请换一个", null, null);
            }
        }

        private static bool IsModifierKey(Keys key)
        {
            return key == Keys.ControlKey || key == Keys.LControlKey || key == Keys.RControlKey ||
                key == Keys.Menu || key == Keys.LMenu || key == Keys.RMenu ||
                key == Keys.ShiftKey || key == Keys.LShiftKey || key == Keys.RShiftKey ||
                key == Keys.LWin || key == Keys.RWin;
        }

        private static string FormatKeyForStorage(Keys key)
        {
            if (key >= Keys.A && key <= Keys.Z)
                return key.ToString();
            if (key >= Keys.D0 && key <= Keys.D9)
                return ((int)(key - Keys.D0)).ToString();
            if (key >= Keys.F1 && key <= Keys.F24)
                return key.ToString();

            switch (key)
            {
                case Keys.Space: return "Space";
                case Keys.Insert: return "Insert";
                case Keys.Home: return "Home";
                case Keys.End: return "End";
                case Keys.PageUp: return "PageUp";
                case Keys.PageDown: return "PageDown";
                case Keys.Up: return "Up";
                case Keys.Down: return "Down";
                case Keys.Left: return "Left";
                case Keys.Right: return "Right";
                default: return null;
            }
        }

        private static string FormatHotkey(string modifiers, string key)
        {
            return modifiers + " + " + key;
        }

        private bool TryApplyHotkey(string modifierText, string keyText)
        {
            uint requestedModifiers;
            Keys requestedKey;
            if (!TryParseHotkey(modifierText, keyText, out requestedModifiers, out requestedKey))
                return false;

            if (!IsHandleCreated)
                return true;

            uint previousModifiers = _registeredModifiers;
            Keys previousKey = _registeredKey;
            bool hadPrevious = _hotkeyRegistered;

            if (_hotkeyRegistered)
            {
                NativeMethods.UnregisterHotKey(Handle, HotkeyId);
                _hotkeyRegistered = false;
            }

            bool registered = NativeMethods.RegisterHotKey(Handle, HotkeyId,
                requestedModifiers | NativeMethods.MOD_NOREPEAT, (uint)requestedKey);
            if (registered)
            {
                _hotkeyRegistered = true;
                _registeredModifiers = requestedModifiers;
                _registeredKey = requestedKey;
                return true;
            }

            if (hadPrevious)
            {
                _hotkeyRegistered = NativeMethods.RegisterHotKey(Handle, HotkeyId,
                    previousModifiers | NativeMethods.MOD_NOREPEAT, (uint)previousKey);
                if (_hotkeyRegistered)
                {
                    _registeredModifiers = previousModifiers;
                    _registeredKey = previousKey;
                }
            }
            return false;
        }

        private static bool TryParseHotkey(string modifierText, string keyText,
            out uint modifiers, out Keys key)
        {
            modifiers = 0;
            key = Keys.None;
            if (String.IsNullOrWhiteSpace(modifierText) || String.IsNullOrWhiteSpace(keyText))
                return false;

            if (modifierText.IndexOf("Ctrl", StringComparison.OrdinalIgnoreCase) >= 0)
                modifiers |= NativeMethods.MOD_CONTROL;
            if (modifierText.IndexOf("Shift", StringComparison.OrdinalIgnoreCase) >= 0)
                modifiers |= NativeMethods.MOD_SHIFT;
            if (modifierText.IndexOf("Alt", StringComparison.OrdinalIgnoreCase) >= 0)
                modifiers |= NativeMethods.MOD_ALT;
            if (modifierText.IndexOf("Win", StringComparison.OrdinalIgnoreCase) >= 0)
                modifiers |= NativeMethods.MOD_WIN;

            try
            {
                if (keyText.Length == 1 && Char.IsDigit(keyText[0]))
                    key = Keys.D0 + (keyText[0] - '0');
                else
                    key = (Keys)Enum.Parse(typeof(Keys), keyText, true);
                return modifiers != 0 && key != Keys.None;
            }
            catch
            {
                return false;
            }
        }

        private void CaptureClipboardText()
        {
            try
            {
                if (!Clipboard.ContainsText(TextDataFormat.UnicodeText))
                    return;

                string text = Clipboard.GetText(TextDataFormat.UnicodeText);
                if (String.IsNullOrWhiteSpace(text))
                    return;

                if (_suppressedClipboardText != null && String.Equals(text, _suppressedClipboardText, StringComparison.Ordinal))
                {
                    _suppressedClipboardText = null;
                    return;
                }
                _suppressedClipboardText = null;

                if (text.Length > MaxClipboardCharacters)
                {
                    ShowStatus("内容超过 100 万字，已跳过以节省内存", null, null);
                    return;
                }

                ClipEntry existing = _state.Items.FirstOrDefault(delegate(ClipEntry item)
                {
                    return String.Equals(item.Text, text, StringComparison.Ordinal);
                });
                if (existing != null)
                    _state.Items.Remove(existing);

                _state.Items.Insert(0, new ClipEntry
                {
                    Id = Guid.NewGuid().ToString("N"),
                    Text = text,
                    CapturedAtUtcTicks = DateTime.UtcNow.Ticks
                });

                if (_state.Items.Count > _state.MaxItems)
                    _state.Items.RemoveRange(_state.MaxItems, _state.Items.Count - _state.MaxItems);

                _undoBuffer = null;
                StateStore.Save(_state);
                RefreshHistoryView();
                ShowStatus("已收好一条新内容", null, null);
            }
            catch (ExternalException)
            {
                if (_clipboardRetryCount < 4)
                {
                    _clipboardRetryCount++;
                    _clipboardRetryTimer.Stop();
                    _clipboardRetryTimer.Start();
                }
            }
        }

        private void CopyEntry(ClipEntry entry)
        {
            if (entry == null || String.IsNullOrEmpty(entry.Text))
                return;
            if (TrySetClipboardText(entry.Text))
                ShowStatus("已复制这一条", null, null);
            else
                ShowStatus("剪贴板正忙，请再试一次", null, null);
        }

        private void CopyPrimaryItems()
        {
            if (_selectedEntryIds.Count > 0)
                CopySelectedItems();
            else
                CopyAllItems();
        }

        private void CopySelectedItems()
        {
            List<ClipEntry> selectedItems = GetSelectedEntriesInCurrentOrder();
            if (selectedItems.Count == 0)
            {
                _selectedEntryIds.Clear();
                UpdateToolbarState();
                ShowStatus("请先勾选要复制的内容", null, null);
                return;
            }

            string combined = String.Join(Environment.NewLine,
                selectedItems.Select(delegate(ClipEntry item) { return item.Text; }).ToArray());
            if (TrySetClipboardText(combined))
                ShowStatus("已按当前顺序复制所选 " + selectedItems.Count + " 条", null, null);
            else
                ShowStatus("剪贴板正忙，请再试一次", null, null);
        }

        private void CopyAllItems()
        {
            if (_state.Items.Count == 0)
            {
                ShowStatus("还没有可复制的内容", null, null);
                return;
            }

            string combined = String.Join(Environment.NewLine + Environment.NewLine,
                _state.Items.Select(delegate(ClipEntry item) { return item.Text; }).ToArray());
            if (TrySetClipboardText(combined))
                ShowStatus("已按当前顺序复制全部 " + _state.Items.Count + " 条", null, null);
            else
                ShowStatus("剪贴板正忙，请再试一次", null, null);
        }

        private bool TrySetClipboardText(string text)
        {
            _suppressedClipboardText = text;
            for (int attempt = 0; attempt < 3; attempt++)
            {
                try
                {
                    Clipboard.SetText(text, TextDataFormat.UnicodeText);
                    return true;
                }
                catch (ExternalException)
                {
                    Thread.Sleep(20);
                }
            }
            _suppressedClipboardText = null;
            return false;
        }

        private void DeletePrimaryItems()
        {
            if (_selectedEntryIds.Count > 0)
                DeleteSelectedItems();
            else
                ClearHistory();
        }

        private void DeleteSelectedItems()
        {
            List<ClipEntry> selectedItems = GetSelectedEntriesInCurrentOrder();
            if (selectedItems.Count == 0)
            {
                _selectedEntryIds.Clear();
                UpdateToolbarState();
                ShowStatus("请先勾选要删除的内容", null, null);
                return;
            }

            _undoBuffer = _state.Items.Select(delegate(ClipEntry item) { return item.Clone(); }).ToList();
            HashSet<string> selectedIds = new HashSet<string>(
                selectedItems.Select(delegate(ClipEntry item) { return item.Id; }), StringComparer.Ordinal);
            _state.Items.RemoveAll(delegate(ClipEntry item)
            {
                return item != null && selectedIds.Contains(item.Id);
            });
            _selectedEntryIds.Clear();
            StateStore.Save(_state);
            RefreshHistoryView();
            ShowStatus("已删除所选 " + selectedItems.Count + " 条", "撤销", RestoreClearedHistory);
        }

        private List<ClipEntry> GetSelectedEntriesInCurrentOrder()
        {
            return _state.Items.Where(delegate(ClipEntry item)
            {
                return item != null && !String.IsNullOrEmpty(item.Id) &&
                    _selectedEntryIds.Contains(item.Id);
            }).ToList();
        }

        private void ClearHistory()
        {
            if (_state.Items.Count == 0)
            {
                ShowStatus("列表已经是空的", null, null);
                return;
            }

            _undoBuffer = _state.Items.Select(delegate(ClipEntry item) { return item.Clone(); }).ToList();
            int removedCount = _undoBuffer.Count;
            _state.Items.Clear();
            _selectedEntryIds.Clear();
            StateStore.Save(_state);
            RefreshHistoryView();
            ShowStatus("已清空 " + removedCount + " 条", "撤销", RestoreClearedHistory);
        }

        private void RestoreClearedHistory()
        {
            if (_undoBuffer == null || _undoBuffer.Count == 0)
                return;

            _state.Items = _undoBuffer.Take(_state.MaxItems)
                .Select(delegate(ClipEntry item) { return item.Clone(); }).ToList();
            _undoBuffer = null;
            _selectedEntryIds.Clear();
            StateStore.Save(_state);
            RefreshHistoryView();
            ShowStatus("已恢复", null, null);
        }

        private void RefreshHistoryView()
        {
            if (_historyPanel == null)
                return;

            HashSet<string> validIds = new HashSet<string>(_state.Items
                .Where(delegate(ClipEntry item) { return item != null && !String.IsNullOrEmpty(item.Id); })
                .Select(delegate(ClipEntry item) { return item.Id; }), StringComparer.Ordinal);
            _selectedEntryIds.RemoveWhere(delegate(string id) { return !validIds.Contains(id); });

            _historyPanel.SuspendLayout();
            while (_historyPanel.Controls.Count > 0)
                _historyPanel.Controls[0].Dispose();

            if (_state.Items.Count == 0)
            {
                EmptyState empty = new EmptyState();
                _historyPanel.Controls.Add(empty);
            }
            else
            {
                for (int index = 0; index < _state.Items.Count; index++)
                {
                    ClipEntry item = _state.Items[index];
                    ClipCard card = new ClipCard(item, index + 1, _selectedEntryIds.Contains(item.Id));
                    card.CopyRequested += delegate(object sender, ClipEntryEventArgs args)
                    {
                        CopyEntry(args.Entry);
                    };
                    card.SelectionChanged += delegate(object sender, ClipSelectionEventArgs args)
                    {
                        if (args.IsSelected)
                            _selectedEntryIds.Add(args.Entry.Id);
                        else
                            _selectedEntryIds.Remove(args.Entry.Id);
                        UpdateToolbarState();
                    };
                    _historyPanel.Controls.Add(card);
                }
            }
            _historyPanel.ResumeLayout(true);
            LayoutHistoryItems();

            _countLabel.Text = _state.Items.Count + " / " + _state.MaxItems;
            UpdateToolbarState();
        }

        private void UpdateToolbarState()
        {
            if (_copyAllButton == null || _clearButton == null)
                return;

            int selectedCount = GetSelectedEntriesInCurrentOrder().Count;
            bool hasSelection = selectedCount > 0;
            bool hasItems = _state.Items.Count > 0;

            _copyAllButton.Text = hasSelection ? "复制所选 (" + selectedCount + ")" : "复制全部";
            _copyAllButton.AccessibleName = hasSelection ?
                "复制所选的 " + selectedCount + " 条内容" : "复制全部历史内容";
            _clearButton.Text = hasSelection ? "删除所选" : "清空";
            _clearButton.AccessibleName = hasSelection ?
                "删除所选的 " + selectedCount + " 条内容" : "清空全部历史内容";
            _copyAllButton.Enabled = hasItems;
            _clearButton.Enabled = hasItems;
        }

        private void LayoutHistoryItems()
        {
            if (_historyPanel == null)
                return;
            int width = Math.Max(150, _historyPanel.ClientSize.Width - _historyPanel.Padding.Horizontal - 24);
            foreach (Control control in _historyPanel.Controls)
                control.Width = width;
        }

        private void ShowStatus(string message, string actionText, Action action)
        {
            if (_statusLabel == null)
                return;

            _statusTimer.Stop();
            _statusLabel.Text = message;
            _statusLabel.ForeColor = Theme.Muted;
            _statusAction = action;
            _statusActionButton.Visible = action != null && !String.IsNullOrEmpty(actionText);
            if (_statusActionButton.Visible)
            {
                _statusActionButton.Text = actionText;
                Size measured = TextRenderer.MeasureText(message, _statusLabel.Font);
                _statusActionButton.Left = Math.Min(210, 18 + measured.Width + 8);
            }
            _statusTimer.Start();
        }

        private void SetListeningStatus()
        {
            if (_statusLabel == null)
                return;
            _statusAction = null;
            _statusActionButton.Visible = false;
            _statusLabel.Text = _clipboardListenerRegistered ? "● 正在监听文字剪贴板" : "剪贴板监听不可用";
            _statusLabel.ForeColor = _clipboardListenerRegistered ? Theme.Success : Theme.Danger;
        }

        private void UpdateHotkeyHint()
        {
            _hotkeyHint.Text = _state.HotkeyModifiers + " + " + _state.HotkeyKey + "  唤醒 / 隐藏";
        }

        private void ToggleVisibility()
        {
            if (Visible && WindowState != FormWindowState.Minimized && ContainsFocus)
            {
                Hide();
                return;
            }

            if (!Visible)
                Show();
            if (WindowState == FormWindowState.Minimized)
                WindowState = FormWindowState.Normal;
            ShowInTaskbar = true;
            BringToFront();
            Activate();
        }

        private void FormResized(object sender, EventArgs e)
        {
            UpdateWindowChrome();
            LayoutHistoryItems();
            if (WindowState == FormWindowState.Minimized)
            {
                BeginInvoke(new Action(delegate
                {
                    Hide();
                    ShowInTaskbar = true;
                }));
            }
        }

        private void UpdateWindowChrome()
        {
            Region previousRegion = Region;
            if (WindowState == FormWindowState.Normal && Width > 0 && Height > 0)
            {
                using (GraphicsPath path = Theme.RoundedRectangle(new Rectangle(0, 0, Width, Height), 11))
                    Region = new Region(path);
                Padding = new Padding(1);
            }
            else
            {
                Region = null;
                Padding = new Padding(0);
            }
            if (previousRegion != null)
                previousRegion.Dispose();
        }

        private void MainFormClosing(object sender, FormClosingEventArgs e)
        {
            if (!_allowExit && e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                Hide();
                return;
            }

            SaveWindowGeometry();
            StateStore.Save(_state);
            _trayIcon.Visible = false;
        }

        private void RestoreWindowPosition()
        {
            Rectangle proposed = new Rectangle(_state.WindowX, _state.WindowY,
                _state.WindowWidth, _state.WindowHeight);
            bool visible = Screen.AllScreens.Any(delegate(Screen screen)
            {
                return screen.WorkingArea.IntersectsWith(proposed);
            });

            if (_state.WindowX >= 0 && _state.WindowY >= 0 && visible)
                Location = proposed.Location;
            else
            {
                Rectangle area = Screen.PrimaryScreen.WorkingArea;
                Location = new Point(area.Right - Width - 28, area.Bottom - Height - 28);
            }
        }

        private void SaveWindowGeometry()
        {
            if (WindowState != FormWindowState.Normal)
                return;
            _state.WindowWidth = Width;
            _state.WindowHeight = Height;
            _state.WindowX = Left;
            _state.WindowY = Top;
            StateStore.Save(_state);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (_trayIcon != null)
                    _trayIcon.Dispose();
                if (_clipboardRetryTimer != null)
                    _clipboardRetryTimer.Dispose();
                if (_statusTimer != null)
                    _statusTimer.Dispose();
                if (_colorMenu != null)
                    _colorMenu.Dispose();
                if (_headerToolTip != null)
                    _headerToolTip.Dispose();
                if (_applicationIcon != null)
                    _applicationIcon.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
