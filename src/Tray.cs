using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace VolumeKeeper
{
    // グローバルホットキーを受け取るための見えないウィンドウ
    class HotkeyWindow : NativeWindow, IDisposable
    {
        [DllImport("user32.dll", SetLastError = true)]
        static extern bool RegisterHotKey(IntPtr hWnd, int id, int modifiers, int vk);

        [DllImport("user32.dll")]
        static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        const int WM_HOTKEY = 0x0312;
        const int MOD_ALT = 1, MOD_CONTROL = 2, MOD_SHIFT = 4, MOD_WIN = 8, MOD_NOREPEAT = 0x4000;
        bool registered;

        public event EventHandler Pressed;
        public event EventHandler SettingsRequested;

        public HotkeyWindow()
        {
            CreateHandle(new CreateParams());
        }

        // "Ctrl+Shift+Q" 形式。登録できなければ理由を返す
        public string Register(string text)
        {
            Unregister();
            if (string.IsNullOrWhiteSpace(text)) return null;
            int mods = MOD_NOREPEAT;
            Keys key = Keys.None;
            foreach (var raw in text.Split('+'))
            {
                string part = raw.Trim();
                switch (part.ToLowerInvariant())
                {
                    case "ctrl": case "control": mods |= MOD_CONTROL; break;
                    case "shift": mods |= MOD_SHIFT; break;
                    case "alt": mods |= MOD_ALT; break;
                    case "win": mods |= MOD_WIN; break;
                    default:
                        if (part.Length == 1 && char.IsDigit(part[0])) part = "D" + part;
                        try { key = (Keys)Enum.Parse(typeof(Keys), part, true); }
                        catch (ArgumentException) { return "キー名を解釈できません: " + raw; }
                        break;
                }
            }
            if (key == Keys.None) return "キーが指定されていません";
            if (!RegisterHotKey(Handle, 1, mods, (int)key))
                return "他のアプリが使用中の可能性があります（エラー " + Marshal.GetLastWin32Error() + "）";
            registered = true;
            return null;
        }

        void Unregister()
        {
            if (registered) UnregisterHotKey(Handle, 1);
            registered = false;
        }

        protected override void WndProc(ref Message m)
        {
            if ((m.Msg == WM_HOTKEY || m.Msg == Program.ToggleMessage) && Pressed != null) Pressed(this, EventArgs.Empty);
            if (m.Msg == Program.SettingsMessage && SettingsRequested != null) SettingsRequested(this, EventArgs.Empty);
            base.WndProc(ref m);
        }

        public void Dispose()
        {
            Unregister();
            DestroyHandle();
        }
    }

    // タスクトレイ常駐。音量維持の定期処理、ホットキー、ミキサー表示をまとめる
    class TrayContext : ApplicationContext
    {
        static string configPath = AppPaths.ConfigFile;

        Config config;
        DateTime configStamp = DateTime.MinValue;
        Keeper keeper = new Keeper();
        Timer timer = new Timer();
        HotkeyWindow hotkey = new HotkeyWindow();
        NotifyIcon tray = new NotifyIcon();
        ContextMenuStrip menu = new ContextMenuStrip();
        ToolStripMenuItem openItem;
        MixerForm mixer;
        SettingsForm settings;
        string hotkeyError;

        public string ConfigPath { get { return configPath; } }

        public TrayContext()
        {
            openItem = new ToolStripMenuItem("ミキサーを開く", null, delegate { ShowMixer(); });
            menu.Items.Add(openItem);
            menu.Items.Add("設定...", null, delegate { ShowSettings(); });
            menu.Items.Add("ログを開く", null, delegate { OpenFile(Log.PathName); });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("終了", null, delegate { Program.ExitReason = "トレイメニューの「終了」"; ExitThread(); });

            tray.Icon = LoadTrayIcon();
            tray.Text = "VolumeKeeper";
            tray.ContextMenuStrip = menu;
            tray.MouseClick += (s, e) => { if (e.Button == MouseButtons.Left) ToggleMixer(); };
            tray.Visible = true;

            hotkey.Pressed += delegate { ToggleMixer(); };
            hotkey.SettingsRequested += delegate { ShowSettings(); };
            Theme.Changed += delegate { Theme.ApplyTo(menu); };
            timer.Tick += delegate { Tick(); };

            // 初回起動時は既定の設定ファイルを作り、設定画面を開く
            bool firstRun = !File.Exists(configPath);
            if (firstRun)
            {
                new Config().Save(configPath);
                Log.Write("設定ファイルを作成しました: " + configPath);
            }
            Tick();
            Theme.ApplyTo(menu);
            timer.Start();
            if (firstRun) ShowSettings();
        }

        static Icon LoadTrayIcon()
        {
            using (var stream = typeof(TrayContext).Assembly.GetManifestResourceStream("VolumeKeeper.ico"))
                return stream != null ? new Icon(stream, SystemInformation.SmallIconSize) : SystemIcons.Application;
        }

        void Tick()
        {
            try
            {
                // 読み込みに成功したときだけ時刻を記録し、失敗したら次の確認で読み直す
                DateTime stamp = File.GetLastWriteTimeUtc(configPath);
                if (stamp != configStamp && ReloadConfig()) configStamp = stamp;
                if (config != null) keeper.Tick(config);
                // 止まった時刻を後から絞り込めるよう、1時間ごとに稼働中であることを記録する
                if (DateTime.Now - lastHeartbeat >= TimeSpan.FromHours(1))
                {
                    if (lastHeartbeat != DateTime.MinValue) Log.Write("稼働中");
                    lastHeartbeat = DateTime.Now;
                }
            }
            catch (Exception ex)
            {
                Log.Write("エラー: " + ex.Message);
            }
        }

        DateTime lastHeartbeat = DateTime.MinValue;

        // 読み込みに失敗している間、ログと通知を繰り返さないための印
        bool loadFailureReported;

        // 設定ファイルを読み直す。読めない行は飛ばし、ファイル自体を読めなければ直前の設定（初回は既定値）を使い続けて false を返す
        bool ReloadConfig()
        {
            var warnings = new List<string>();
            Config next;
            bool loaded = true;
            try
            {
                next = Config.Load(configPath, warnings);
                if (loadFailureReported) Log.Write("設定ファイルを読み込めるようになりました");
                loadFailureReported = false;
            }
            catch (Exception ex)
            {
                if (!loadFailureReported)
                {
                    Log.Write("設定ファイルを読み込めません（" + (config == null ? "既定値" : "直前の設定") + "で動作し、読み込めるまで再試行します）: " + ex.Message);
                    Notify("設定ファイルを読み込めませんでした。読み込めるまで再試行します。トレイメニューの「設定...」から設定し直すこともできます。");
                    loadFailureReported = true;
                }
                if (config != null) return false;
                next = new Config();
                loaded = false;
            }
            foreach (var w in warnings) Log.Write("設定ファイルの " + w);
            if (warnings.Count > 0) Notify("設定ファイルに読み込めない行が " + warnings.Count + " 件ありました。詳しくはログを確認してください。");

            var previous = config;
            config = next;
            // 対象デバイスが変わったら全セッション、音量の設定値が変わったアプリはそのセッションだけ適用し直す。
            // テーマやホットキーだけの変更では、手動で変えた音量に触らない
            if (previous == null || previous.EndpointPattern != next.EndpointPattern) keeper.ResetAll();
            else
            {
                var changed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var kv in next.Levels)
                {
                    float old;
                    if (!previous.Levels.TryGetValue(kv.Key, out old) || Math.Abs(old - kv.Value) > 0.0001f) changed.Add(kv.Key);
                }
                if (changed.Count > 0) keeper.Reapply(changed);
            }

            timer.Interval = next.IntervalSeconds * 1000;
            var parts = new List<string>();
            foreach (var kv in next.Levels) parts.Add(kv.Key + "=" + Log.Percent(kv.Value));
            Log.Write("設定読み込み endpoint=" + (next.EndpointPattern.Length == 0 ? "(既定)" : next.EndpointPattern) + " hotkey=" + next.Hotkey + " theme=" + next.Theme + " " + string.Join(" ", parts.ToArray()));
            Theme.SetMode(next.Theme);
            if (previous == null || previous.Hotkey != next.Hotkey) RegisterHotkey();
            if (mixer != null) mixer.EndpointPattern = next.EndpointPattern;
            return loaded;
        }

        void Notify(string message)
        {
            tray.ShowBalloonTip(5000, "VolumeKeeper", message, ToolTipIcon.Warning);
        }

        void RegisterHotkey()
        {
            hotkeyError = hotkey.Register(config.Hotkey);
            if (hotkeyError != null) Log.Write("ホットキー " + config.Hotkey + " を登録できません: " + hotkeyError);
            openItem.ShortcutKeyDisplayString = hotkeyError == null ? config.Hotkey : "";
        }

        // 設定画面で保存した直後に呼ぶ。ホットキーを登録できなかった場合は理由を返す
        public string ReloadNow()
        {
            configStamp = DateTime.MinValue;
            Tick();
            // 同じキーのままでも、他のアプリが手放していれば登録できるよう試し直す
            if (config != null) RegisterHotkey();
            return hotkeyError;
        }

        // 設定画面でホットキーを入力している間は、今のホットキーを外しておく
        public void SuspendHotkey() { hotkey.Register(""); }
        public void ResumeHotkey() { if (config != null) RegisterHotkey(); }

        void ShowSettings()
        {
            if (config == null) return;
            if (settings == null || settings.IsDisposed) settings = new SettingsForm(this, config);
            if (settings.WindowState == FormWindowState.Minimized) settings.WindowState = FormWindowState.Normal;
            settings.Show();
            settings.Activate();
        }

        void ShowMixer()
        {
            if (config == null) return;
            if (mixer == null)
            {
                mixer = new MixerForm(config.EndpointPattern);
                mixer.SessionsAdjusted += ids => keeper.Protect(ids);
            }
            mixer.ShowMixer();
        }

        void ToggleMixer()
        {
            if (mixer != null && (mixer.Visible || mixer.JustHidden)) mixer.HideMixer();
            else ShowMixer();
        }

        static void OpenFile(string path)
        {
            try { Process.Start(path); } catch (Exception ex) { Log.Write("ファイルを開けません: " + path + " " + ex.Message); }
        }

        protected override void ExitThreadCore()
        {
            tray.Visible = false;
            tray.Dispose();
            hotkey.Dispose();
            timer.Dispose();
            if (mixer != null) mixer.Dispose();
            if (settings != null) settings.Dispose();
            base.ExitThreadCore();
        }
    }
}
