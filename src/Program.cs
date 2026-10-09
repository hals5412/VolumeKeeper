using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace VolumeKeeper
{
    // 設定とログの保存先（%APPDATA%\VolumeKeeper）
    static class AppPaths
    {
        public static readonly string Directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "VolumeKeeper");
        public static readonly string ConfigFile = Path.Combine(Directory, "volume-keeper.ini");
        public static readonly string LogFile = Path.Combine(Directory, "volume-keeper.log");
    }

    // Windows の起動時に自動で起動する設定（HKCU の Run キー）
    static class Startup
    {
        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        const string ValueName = "VolumeKeeper";

        public static bool Enabled
        {
            get
            {
                using (var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(RunKey))
                    return key != null && key.GetValue(ValueName) != null;
            }
            set
            {
                using (var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(RunKey))
                {
                    if (value) key.SetValue(ValueName, "\"" + Application.ExecutablePath + "\"");
                    else if (key.GetValue(ValueName) != null) key.DeleteValue(ValueName);
                }
            }
        }
    }

    class Config
    {
        // 空なら既定の再生デバイス。指定するとデバイス名にこの文字列を含む再生デバイス
        public string EndpointPattern = "";
        public int IntervalSeconds = 2;
        public string Hotkey = "Ctrl+Shift+Q";
        // System（Windows に合わせる）/ Light / Dark
        public string Theme = "System";
        // キーはプロセス名。「System」はシステム音（PID 0）を表す
        public Dictionary<string, float> Levels = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);

        static readonly string[] ReservedKeys = { "EndpointPattern", "IntervalSeconds", "Hotkey", "Theme" };
        static readonly string[] Themes = { "System", "Light", "Dark" };

        // アプリ別音量のキー（プロセス名）として ini に書けるか。設定項目名や、ini の解釈を変える文字は使えない
        public static bool IsValidAppKey(string key)
        {
            if (string.IsNullOrWhiteSpace(key) || key != key.Trim()) return false;
            if (key.IndexOfAny(new[] { '=', '#', ';', '[', ']', '\r', '\n' }) >= 0) return false;
            return Array.FindIndex(ReservedKeys, x => x.Equals(key, StringComparison.OrdinalIgnoreCase)) < 0;
        }

        // 読めない行は飛ばして既定値のまま進め、行番号と理由を warnings に入れる
        public static Config Load(string path, List<string> warnings)
        {
            var config = new Config();
            string[] lines = File.ReadAllLines(path, Encoding.UTF8);
            for (int i = 0; i < lines.Length; i++)
            {
                var line = lines[i].Trim();
                if (line.Length == 0 || line.StartsWith("#") || line.StartsWith(";") || line.StartsWith("[")) continue;
                string where = (i + 1) + "行目";
                int eq = line.IndexOf('=');
                if (eq <= 0) { warnings.Add(where + ": 「キー=値」の形式ではありません"); continue; }
                string key = line.Substring(0, eq).Trim();
                string value = line.Substring(eq + 1).Trim();
                if (key.Equals("EndpointPattern", StringComparison.OrdinalIgnoreCase)) config.EndpointPattern = value;
                else if (key.Equals("Hotkey", StringComparison.OrdinalIgnoreCase)) config.Hotkey = value;
                else if (key.Equals("IntervalSeconds", StringComparison.OrdinalIgnoreCase))
                {
                    int seconds;
                    if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out seconds) && seconds >= 1 && seconds <= 3600)
                        config.IntervalSeconds = seconds;
                    else warnings.Add(where + ": IntervalSeconds は 1〜3600 の整数にしてください（" + value + "）");
                }
                else if (key.Equals("Theme", StringComparison.OrdinalIgnoreCase))
                {
                    int index = Array.FindIndex(Themes, x => x.Equals(value, StringComparison.OrdinalIgnoreCase));
                    if (index >= 0) config.Theme = Themes[index];
                    else warnings.Add(where + ": Theme は System / Light / Dark のいずれかにしてください（" + value + "）");
                }
                else
                {
                    float level;
                    if (!IsValidAppKey(key)) warnings.Add(where + ": アプリ名として使えない名前です（" + key + "）");
                    else if (float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out level) && !float.IsNaN(level) && level >= 0 && level <= 100)
                        config.Levels[key] = level / 100f;
                    else warnings.Add(where + ": " + key + " の音量は 0〜100 の数値にしてください（" + value + "）");
                }
            }
            return config;
        }

        // 設定画面からの保存。コメントは定型文で書き直す
        public void Save(string path)
        {
            var sb = new StringBuilder();
            sb.AppendLine("# volume-keeper の設定。保存すると数秒以内に自動で反映される（再起動不要）。");
            sb.AppendLine("# トレイアイコンの右クリック →「設定...」からも編集できる。");
            sb.AppendLine();
            sb.AppendLine("# 対象の再生デバイス名に含まれる文字列（空欄なら既定の再生デバイス）");
            sb.AppendLine("EndpointPattern=" + EndpointPattern);
            sb.AppendLine();
            sb.AppendLine("# 画面のテーマ（System = Windows に合わせる / Light / Dark）");
            sb.AppendLine("Theme=" + Theme);
            sb.AppendLine();
            sb.AppendLine("# ミキサーを開閉するホットキー（Ctrl / Shift / Alt / Win と キー名を + でつなぐ。空欄で無効）");
            sb.AppendLine("Hotkey=" + Hotkey);
            sb.AppendLine();
            sb.AppendLine("# 確認間隔（秒）");
            sb.AppendLine("IntervalSeconds=" + IntervalSeconds.ToString(CultureInfo.InvariantCulture));
            sb.AppendLine();
            sb.AppendLine("# アプリ別の音量（0〜100）。キーはプロセス名、System はシステム音");
            foreach (var kv in Levels)
                sb.AppendLine(kv.Key + "=" + Math.Round(kv.Value * 100).ToString(CultureInfo.InvariantCulture));
            System.IO.Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
        }
    }

    static class Log
    {
        static string logPath = AppPaths.LogFile;
        public static string PathName { get { return logPath; } }

        public static void Write(string message)
        {
            try
            {
                Directory.CreateDirectory(AppPaths.Directory);
                var info = new FileInfo(logPath);
                if (info.Exists && info.Length > 512 * 1024)
                {
                    File.Copy(logPath, logPath + ".old", true);
                    File.Delete(logPath);
                }
                File.AppendAllText(logPath, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss ") + message + "\r\n", Encoding.UTF8);
            }
            catch { }
        }

        public static string Percent(float v)
        {
            return Math.Round(v * 100).ToString(CultureInfo.InvariantCulture) + "%";
        }
    }

    // セッションが新しく現れたときだけ設定値の音量にする。
    // 再起動やUSBオーディオ機器の再接続でWindowsが音量を100%に戻す対策で、ユーザーが途中で変えた音量はできるだけ残す。
    class Keeper
    {
        // 音量の設定に失敗したときに、同じセッションへ試し直す上限（確認間隔ごとに1回）
        const int MaxAttempts = 5;

        // 処理済みのセッション（インスタンスID → アプリのキー）
        Dictionary<string, string> known = new Dictionary<string, string>();
        // 初回の設定に失敗した回数（インスタンスID → 回数）
        Dictionary<string, int> failures = new Dictionary<string, int>();
        // ミキサーでユーザーが操作したセッション（インスタンスID → アプリのキー。キーは次の確認で埋める）。
        // 初回の再試行も再確認もしない。セッションが消えるか、そのアプリの設定値や対象デバイスが変わると解除する
        Dictionary<string, string> userAdjusted = new Dictionary<string, string>();

        // 適用直後にWindowsが保存値で上書きすることがあるため、少し後にもう一度確認する
        class Recheck { public DateTime Due; public float Before; public int Attempts; }
        Dictionary<string, Recheck> recheck = new Dictionary<string, Recheck>();

        // 対象デバイスが変わったときに呼ぶ。今あるセッションすべてに設定値を適用し直す
        public void ResetAll()
        {
            known.Clear();
            failures.Clear();
            userAdjusted.Clear();
        }

        // 音量の設定値が変わった（または追加された）アプリだけ、今あるセッションにも適用し直す
        public void Reapply(ICollection<string> keys)
        {
            foreach (var kv in new List<KeyValuePair<string, string>>(known))
                if (keys.Contains(kv.Value)) known.Remove(kv.Key);
            // 設定値を明示的に変えたアプリは、ミキサーで操作したセッションにも新しい値を適用する
            foreach (var kv in new List<KeyValuePair<string, string>>(userAdjusted))
                if (kv.Value != null && keys.Contains(kv.Value)) userAdjusted.Remove(kv.Key);
        }

        // ミキサーでユーザーが音量やミュートを操作したセッションは、初回の再試行でも再確認でも上書きしない
        public void Protect(IEnumerable<string> instanceIds)
        {
            foreach (var id in instanceIds)
            {
                if (!userAdjusted.ContainsKey(id)) userAdjusted[id] = null;
                recheck.Remove(id);
                failures.Remove(id);
            }
        }

        public void Tick(Config config)
        {
            var current = new Dictionary<string, string>();
            var seen = new HashSet<string>();
            foreach (var s in Audio.GetSessions(config.EndpointPattern))
            {
                float level;
                if (!config.Levels.TryGetValue(s.Key, out level)) continue;
                seen.Add(s.InstanceId);
                if (userAdjusted.ContainsKey(s.InstanceId))
                {
                    userAdjusted[s.InstanceId] = s.Key;
                    current[s.InstanceId] = s.Key;
                    continue;
                }
                string name = s.Key + " @ " + s.EndpointName;
                Recheck pending;
                if (!known.ContainsKey(s.InstanceId))
                {
                    float before = s.Volume;
                    if (s.SetVolume(level))
                    {
                        Log.Write(name + ": " + Log.Percent(before) + " -> " + Log.Percent(level));
                        failures.Remove(s.InstanceId);
                        recheck[s.InstanceId] = new Recheck { Due = DateTime.Now.AddSeconds(6), Before = before };
                        current[s.InstanceId] = s.Key;
                    }
                    else
                    {
                        // 失敗したら処理済みにせず、次の確認で試し直す。上限に達したらあきらめる
                        int count;
                        failures.TryGetValue(s.InstanceId, out count);
                        failures[s.InstanceId] = ++count;
                        if (count >= MaxAttempts)
                        {
                            Log.Write(name + ": 音量を設定できませんでした（" + count + "回失敗したため中止）");
                            failures.Remove(s.InstanceId);
                            current[s.InstanceId] = s.Key;
                        }
                        else Log.Write(name + ": 音量を設定できませんでした（" + count + "回目、次の確認で再試行）");
                    }
                    continue;
                }
                current[s.InstanceId] = s.Key;
                if (recheck.TryGetValue(s.InstanceId, out pending) && DateTime.Now >= pending.Due)
                {
                    // Windowsが元の値に戻したとみられる場合だけ設定し直す。別の値に変わっていればユーザーの操作として残す
                    if (Math.Abs(s.Volume - pending.Before) >= 0.005f || Math.Abs(s.Volume - level) <= 0.005f)
                    {
                        recheck.Remove(s.InstanceId);
                    }
                    else if (s.SetVolume(level))
                    {
                        recheck.Remove(s.InstanceId);
                        Log.Write(name + ": 再確認で " + Log.Percent(pending.Before) + " -> " + Log.Percent(level));
                    }
                    else if (++pending.Attempts >= MaxAttempts)
                    {
                        recheck.Remove(s.InstanceId);
                        Log.Write(name + ": 再確認で音量を設定できませんでした（" + pending.Attempts + "回失敗したため中止）");
                    }
                    else Log.Write(name + ": 再確認で音量を設定できませんでした（" + pending.Attempts + "回目、次の確認で再試行）");
                }
            }
            known = current;
            foreach (var id in new List<string>(recheck.Keys))
                if (!seen.Contains(id)) recheck.Remove(id);
            foreach (var id in new List<string>(failures.Keys))
                if (!seen.Contains(id)) failures.Remove(id);
            foreach (var id in new List<string>(userAdjusted.Keys))
                if (!seen.Contains(id)) userAdjusted.Remove(id);
        }
    }
    static class Program
    {
        [DllImport("user32.dll")]
        static extern bool SetProcessDPIAware();

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        static extern int RegisterWindowMessage(string name);

        [DllImport("user32.dll")]
        static extern bool PostMessage(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam);

        // 起動中のインスタンスにミキサーの開閉を頼むメッセージ（VolumeKeeper.exe --toggle）
        public static readonly int ToggleMessage = RegisterWindowMessage("VolumeKeeper.ToggleMixer");
        // 起動中にもう一度 exe を起動したら設定画面を開く
        public static readonly int SettingsMessage = RegisterWindowMessage("VolumeKeeper.ShowSettings");

        // 終了理由（ログ用）。外から強制終了された場合は「終了しました」自体がログに残らない
        public static string ExitReason = "理由不明";

        [STAThread]
        static void Main(string[] args)
        {
            bool created;
            using (var mutex = new Mutex(true, @"Local\VolumeKeeper", out created))
            {
                if (!created)
                {
                    int msg = Array.IndexOf(args, "--toggle") >= 0 ? ToggleMessage : SettingsMessage;
                    PostMessage((IntPtr)0xFFFF /* HWND_BROADCAST */, msg, IntPtr.Zero, IntPtr.Zero);
                    return;
                }
                SetProcessDPIAware();
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);

                // 予期しない終了の原因を追えるよう、起動・終了・電源状態の変化をログに残す
                Log.Write("起動しました（バージョン " + Application.ProductVersion + "、PID " + System.Diagnostics.Process.GetCurrentProcess().Id + "）");
                Application.ThreadException += (s, e) => Log.Write("エラー: " + e.Exception);
                AppDomain.CurrentDomain.UnhandledException += (s, e) => Log.Write("想定外のエラーで終了します: " + e.ExceptionObject);
                Microsoft.Win32.SystemEvents.PowerModeChanged += (s, e) =>
                {
                    if (e.Mode == Microsoft.Win32.PowerModes.Suspend) Log.Write("スリープに入ります");
                    else if (e.Mode == Microsoft.Win32.PowerModes.Resume) Log.Write("スリープから復帰しました");
                };
                Microsoft.Win32.SystemEvents.SessionEnding += (s, e) =>
                {
                    ExitReason = e.Reason == Microsoft.Win32.SessionEndReasons.Logoff ? "Windowsのサインアウト" : "Windowsのシャットダウン";
                    Log.Write(ExitReason + "を検知しました");
                };

                Application.Run(new TrayContext());
                Log.Write("終了しました（" + ExitReason + "）");
            }
        }
    }
}
