using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace VolumeKeeper
{
    // アプリの表示名とアイコン（ミキサーと設定画面で共用）
    static class AppInfo
    {
        static readonly Dictionary<string, Icon> iconCache = new Dictionary<string, Icon>(StringComparer.OrdinalIgnoreCase);
        static readonly Dictionary<string, string> titleCache = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public static Icon IconFor(Session session)
        {
            string path = session.ProcessId == 0 ? Path.Combine(Environment.SystemDirectory, "SndVol.exe") : session.ExePath;
            Icon icon;
            if (iconCache.TryGetValue(path, out icon)) return icon;
            try { icon = string.IsNullOrEmpty(path) ? SystemIcons.Application : Icon.ExtractAssociatedIcon(path); }
            catch { icon = SystemIcons.Application; }
            iconCache[path] = icon;
            return icon;
        }

        public static string TitleFor(Session session)
        {
            if (session.ProcessId == 0) return "システム音";
            string title;
            if (titleCache.TryGetValue(session.ExePath, out title)) return title;
            title = session.ProcessName;
            try
            {
                string desc = FileVersionInfo.GetVersionInfo(session.ExePath).FileDescription;
                if (!string.IsNullOrWhiteSpace(desc)) title = desc.Trim();
            }
            catch { }
            titleCache[session.ExePath] = title;
            return title;
        }
    }

    // 1行分（アイコン・名前・スライダー・%表示）。アイコンのクリックでミュート切り替え
    class VolumeRow : Control
    {
        readonly float s;
        float volume;
        bool muted, hover;

        public Icon AppIcon;
        public string Title = "";
        public string Key;
        public List<Session> Sessions = new List<Session>();
        public bool Dragging { get; private set; }
        public event EventHandler VolumeChanged;
        public event EventHandler MuteToggled;

        public VolumeRow(float scale)
        {
            s = scale;
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            Height = (int)(52 * s);
            BackColor = Theme.Background;
        }

        public float Volume
        {
            get { return volume; }
            set { if (Math.Abs(volume - value) > 0.0001f) { volume = value; Invalidate(); } }
        }

        public bool Muted
        {
            get { return muted; }
            set { if (muted != value) { muted = value; Invalidate(); } }
        }

        Rectangle IconRect { get { return new Rectangle((int)(14 * s), (int)(12 * s), (int)(28 * s), (int)(28 * s)); } }
        int TrackLeft { get { return (int)(56 * s); } }
        int TrackRight { get { return Width - (int)(60 * s); } }
        int TrackY { get { return (int)(36 * s); } }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(hover ? Theme.Hover : Theme.Background);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;

            var ir = IconRect;
            if (AppIcon != null) g.DrawIcon(AppIcon, ir);
            if (muted)
            {
                using (var shade = new SolidBrush(Color.FromArgb(150, hover ? Theme.Hover : Theme.Background)))
                    g.FillRectangle(shade, ir);
                using (var pen = new Pen(Color.FromArgb(255, 90, 90), 2.5f * s))
                    g.DrawLine(pen, ir.Left + 3 * s, ir.Bottom - 3 * s, ir.Right - 3 * s, ir.Top + 3 * s);
            }

            TextRenderer.DrawText(g, Title, Theme.TitleFont,
                new Rectangle(TrackLeft, (int)(5 * s), Width - TrackLeft - (int)(14 * s), (int)(22 * s)),
                Theme.Text, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);

            float x = TrackLeft + (TrackRight - TrackLeft) * volume;
            using (var pen = new Pen(Theme.Track, 4 * s) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                g.DrawLine(pen, TrackLeft, TrackY, TrackRight, TrackY);
            var fill = muted ? Theme.MutedFill : Theme.Accent;
            if (volume > 0)
                using (var pen = new Pen(fill, 4 * s) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                    g.DrawLine(pen, TrackLeft, TrackY, x, TrackY);
            float r = 8 * s;
            using (var b = new SolidBrush(Theme.ThumbRing))
                g.FillEllipse(b, x - r, TrackY - r, r * 2, r * 2);
            if (!Theme.IsDark)
                using (var pen = new Pen(Theme.Separator, 1))
                    g.DrawEllipse(pen, x - r, TrackY - r, r * 2, r * 2);
            float ri = (Dragging ? 5 : 4) * s;
            using (var b = new SolidBrush(fill))
                g.FillEllipse(b, x - ri, TrackY - ri, ri * 2, ri * 2);

            TextRenderer.DrawText(g, Math.Round(volume * 100).ToString(), Theme.TitleFont,
                new Rectangle(TrackRight + (int)(10 * s), TrackY - (int)(11 * s), Width - TrackRight - (int)(22 * s), (int)(22 * s)),
                Theme.Text, TextFormatFlags.Right | TextFormatFlags.VerticalCenter);
        }

        void SetFromX(int mouseX)
        {
            float v = (mouseX - TrackLeft) / (float)(TrackRight - TrackLeft);
            SetVolumeByUser((float)Math.Round(Math.Min(1, Math.Max(0, v)), 2));
        }

        void SetVolumeByUser(float v)
        {
            v = Math.Min(1, Math.Max(0, v));
            if (Math.Abs(v - volume) < 0.0001f) return;
            Volume = v;
            if (VolumeChanged != null) VolumeChanged(this, EventArgs.Empty);
        }

        // ホイール1目盛りで2%動かす
        public void ApplyWheel(int delta)
        {
            SetVolumeByUser((float)Math.Round(volume + (delta > 0 ? 0.02f : -0.02f), 2));
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            if (IconRect.Contains(e.Location))
            {
                Muted = !muted;
                if (MuteToggled != null) MuteToggled(this, EventArgs.Empty);
                return;
            }
            if (e.Y >= TrackY - 14 * s && e.X >= TrackLeft - 10 * s && e.X <= TrackRight + 10 * s)
            {
                Dragging = true;
                Capture = true;
                SetFromX(e.X);
                Invalidate();
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (Dragging) SetFromX(e.X);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            if (!Dragging) return;
            Dragging = false;
            Capture = false;
            Invalidate();
        }

        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); }
    }

    // ホットキーで出す音量ミキサー。対象デバイスのマスター音量とアプリ別音量を表示する
    class MixerForm : Form, IMessageFilter
    {
        [DllImport("dwmapi.dll")]
        static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

        const int WM_MOUSEWHEEL = 0x020A;
        readonly float s;
        readonly Timer refreshTimer = new Timer { Interval = 400 };
        readonly List<VolumeRow> rows = new List<VolumeRow>();
        // アプリ別の行を入れる領域。画面に収まらないときはスクロールする
        readonly Panel body = new Panel { AutoScroll = true };
        EndpointControl endpoint;
        string signature;
        string headerText = "";
        int separatorY = -1;
        DateTime hiddenAt = DateTime.MinValue;

        public string EndpointPattern;

        // ユーザーがミキサーでアプリの音量やミュートを操作したとき（対象セッションのインスタンスID）
        public event Action<IEnumerable<string>> SessionsAdjusted;

        void RaiseAdjusted(VolumeRow row)
        {
            if (SessionsAdjusted != null) SessionsAdjusted(row.Sessions.Select(x => x.InstanceId).ToList());
        }

        // 画面外クリックで閉じた直後のトレイクリックで、すぐ開き直さないための判定
        public bool JustHidden { get { return (DateTime.Now - hiddenAt).TotalMilliseconds < 300; } }

        public MixerForm(string endpointPattern)
        {
            EndpointPattern = endpointPattern;
            using (var g = CreateGraphics()) s = g.DpiX / 96f;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            BackColor = Theme.Background;
            KeyPreview = true;
            Width = (int)(360 * s);
            Text = "VolumeKeeper";
            DoubleBuffered = true;
            refreshTimer.Tick += delegate { RefreshRows(); };
            Controls.Add(body);
            Theme.Changed += OnThemeChanged;
            OnThemeChanged(this, EventArgs.Empty);
        }

        [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
        static extern int SetWindowTheme(IntPtr hwnd, string appName, string idList);

        void OnThemeChanged(object sender, EventArgs e)
        {
            BackColor = Theme.Background;
            body.BackColor = Theme.Background;
            SetWindowTheme(body.Handle, Theme.IsDark ? "DarkMode_Explorer" : "Explorer", null);
            foreach (var row in rows) row.BackColor = Theme.Background;
            Invalidate(true);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) Theme.Changed -= OnThemeChanged;
            base.Dispose(disposing);
        }

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= 0x80;        // WS_EX_TOOLWINDOW: Alt+Tabに出さない
                cp.ClassStyle |= 0x20000;  // CS_DROPSHADOW
                return cp;
            }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            int round = 2; // DWMWA_WINDOW_CORNER_PREFERENCE = DWMWCP_ROUND（Windows 11）
            DwmSetWindowAttribute(Handle, 33, ref round, sizeof(int));
        }

        public void ShowMixer()
        {
            // 前回と別のモニター（作業領域の高さが違う）で開く場合もあるので、毎回組み立て直す
            signature = null;
            RefreshRows();
            PlaceNearTray();
            Show();
            Activate();
            refreshTimer.Start();
            Application.AddMessageFilter(this);
        }

        public void HideMixer()
        {
            if (!Visible) return;
            refreshTimer.Stop();
            Application.RemoveMessageFilter(this);
            Hide();
            hiddenAt = DateTime.Now;
            // 閉じている間は音量オブジェクトを持ち続けない
            endpoint = null;
            foreach (var row in rows) row.Sessions.Clear();
        }

        protected override void OnDeactivate(EventArgs e)
        {
            base.OnDeactivate(e);
            HideMixer();
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Escape) HideMixer();
            base.OnKeyDown(e);
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; HideMixer(); return; }
            base.OnFormClosing(e);
        }

        void PlaceNearTray()
        {
            var area = Screen.FromPoint(Cursor.Position).WorkingArea;
            int margin = (int)(12 * s);
            Location = new Point(area.Right - Width - margin, area.Bottom - Height - margin);
        }

        // ホイールはフォーカスのある部品に届くため、カーソル下の行へ振り分ける。
        // 行の外（スクロールバーの上など）では通常どおりスクロールさせる
        public bool PreFilterMessage(ref Message m)
        {
            if (m.Msg != WM_MOUSEWHEEL || !Visible) return false;
            var p = Cursor.Position;
            var bodyArea = body.RectangleToScreen(body.ClientRectangle);
            foreach (var row in rows)
            {
                var r = row.RectangleToScreen(row.ClientRectangle);
                if (!r.Contains(p)) continue;
                if (row.Parent == body && !bodyArea.Contains(p)) continue;
                row.ApplyWheel((short)((long)m.WParam >> 16));
                return true;
            }
            return false;
        }

        void RefreshRows()
        {
            try
            {
                endpoint = Audio.FindEndpoint(EndpointPattern);
                var groups = new List<KeyValuePair<string, List<Session>>>();
                if (endpoint != null)
                {
                    // 同じアプリの複数プロセス（Discord、Chromeなど）は1行にまとめる
                    groups = Audio.GetSessions(EndpointPattern)
                        .GroupBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
                        .OrderBy(x => x.Key == "System" ? 0 : 1)
                        .ThenBy(x => AppInfo.TitleFor(x.First()), StringComparer.CurrentCultureIgnoreCase)
                        .Select(x => new KeyValuePair<string, List<Session>>(x.Key, x.ToList()))
                        .ToList();
                }
                string sig = (endpoint == null ? "" : endpoint.Name) + "|" + string.Join("|", groups.Select(x => x.Key).ToArray());
                if (sig != signature) Rebuild(groups, sig);

                if (endpoint != null && rows.Count > 0)
                {
                    var master = rows[0];
                    if (!master.Dragging) { master.Volume = endpoint.Volume; master.Muted = endpoint.Muted; }
                    for (int i = 0; i < groups.Count; i++)
                    {
                        var row = rows[i + 1];
                        row.Sessions = groups[i].Value;
                        if (row.Dragging) continue;
                        row.Volume = groups[i].Value.Max(x => x.Volume);
                        row.Muted = groups[i].Value.All(x => x.Muted);
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Write("ミキサー更新エラー: " + ex.Message);
            }
        }

        void Rebuild(List<KeyValuePair<string, List<Session>>> groups, string sig)
        {
            signature = sig;
            SuspendLayout();
            foreach (var row in rows) { row.Parent.Controls.Remove(row); row.Dispose(); }
            rows.Clear();
            body.AutoScrollPosition = Point.Empty;

            int y = (int)(34 * s);
            separatorY = -1;
            if (endpoint == null)
            {
                headerText = string.IsNullOrEmpty(EndpointPattern)
                    ? "既定の再生デバイスが見つかりません"
                    : "「" + EndpointPattern + "」の再生デバイスが見つかりません";
                body.Visible = false;
                Height = (int)(64 * s);
            }
            else
            {
                headerText = endpoint.Name;
                var master = AddRow(this, null, "全体の音量", LoadAppIcon(), y, ClientSize.Width);
                master.VolumeChanged += delegate { if (endpoint != null) endpoint.Volume = master.Volume; };
                master.MuteToggled += delegate { if (endpoint != null) endpoint.Muted = master.Muted; };
                y += master.Height;
                separatorY = y + (int)(4 * s);
                y += (int)(9 * s);

                // 作業領域の高さを超える分はスクロールにする
                int rowHeight = (int)(52 * s);
                int contentHeight = groups.Count * rowHeight;
                int maxBodyHeight = Math.Max(rowHeight, Screen.FromPoint(Cursor.Position).WorkingArea.Height - (int)(24 * s) - y - (int)(8 * s));
                int bodyHeight = Math.Min(contentHeight, maxBodyHeight);
                int rowWidth = ClientSize.Width - (contentHeight > bodyHeight ? SystemInformation.VerticalScrollBarWidth : 0);
                body.SetBounds(0, y, ClientSize.Width, bodyHeight);
                body.Visible = groups.Count > 0;

                int rowY = 0;
                foreach (var group in groups)
                {
                    var first = group.Value[0];
                    var row = AddRow(body, group.Key, AppInfo.TitleFor(first), AppInfo.IconFor(first), rowY, rowWidth);
                    row.VolumeChanged += delegate { foreach (var x in row.Sessions) x.SetVolume(row.Volume); RaiseAdjusted(row); };
                    row.MuteToggled += delegate { foreach (var x in row.Sessions) x.SetMute(row.Muted); RaiseAdjusted(row); };
                    rowY += row.Height;
                }
                Height = y + bodyHeight + (int)(8 * s);
            }
            ResumeLayout();
            if (Visible) PlaceNearTray();
            Invalidate();
        }

        VolumeRow AddRow(Control parent, string key, string title, Icon icon, int y, int width)
        {
            var row = new VolumeRow(s) { Key = key, Title = title, AppIcon = icon, Left = 0, Top = y, Width = width };
            parent.Controls.Add(row);
            rows.Add(row);
            return row;
        }

        Icon appIcon;
        Icon LoadAppIcon()
        {
            if (appIcon != null) return appIcon;
            using (var stream = typeof(MixerForm).Assembly.GetManifestResourceStream("VolumeKeeper.ico"))
                appIcon = stream != null ? new Icon(stream, (int)(32 * s), (int)(32 * s)) : SystemIcons.Application;
            return appIcon;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            TextRenderer.DrawText(e.Graphics, headerText, Theme.SmallFont,
                new Rectangle((int)(14 * s), (int)(8 * s), Width - (int)(28 * s), (int)(20 * s)),
                Theme.SubText, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            if (separatorY >= 0)
                using (var pen = new Pen(Theme.Separator, 1))
                    e.Graphics.DrawLine(pen, 14 * s, separatorY, Width - 14 * s, separatorY);
        }
    }
}
