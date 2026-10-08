using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;

namespace VolumeKeeper
{
    // 設定画面。対象デバイス、ホットキー、起動時・接続時に戻すアプリ別音量を編集して ini に保存する
    class SettingsForm : Form
    {
        // 「追加するアプリ」の候補
        class Candidate
        {
            public string Key;
            public string Title;
            public Icon Icon;
            public override string ToString() { return Key == "System" ? Title : Title + "（" + Key + "）"; }
        }

        readonly TrayContext tray;
        readonly int intervalSeconds;
        readonly float s;
        readonly Dictionary<string, Bitmap> iconCache = new Dictionary<string, Bitmap>(StringComparer.OrdinalIgnoreCase);
        const string DefaultDeviceLabel = "（既定の再生デバイス）";
        static readonly string[] ThemeValues = { "System", "Light", "Dark" };
        static readonly string[] ThemeLabels = { "Windows の設定に合わせる", "ライト", "ダーク" };

        ComboBox deviceBox, addBox, themeBox;
        CheckBox startupBox;
        TextBox hotkeyBox;
        NumericUpDown addLevel;
        DataGridView grid;

        public SettingsForm(TrayContext tray, Config config)
        {
            this.tray = tray;
            intervalSeconds = config.IntervalSeconds;
            Font = new Font("Yu Gothic UI", 9f);
            using (var g = CreateGraphics()) s = g.DpiX / 96f;
            Text = "VolumeKeeper の設定";
            Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(S(640), S(540));
            MinimumSize = new Size(S(480), S(420));
            Padding = new Padding(S(12));

            var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3 };
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            Controls.Add(root);

            root.Controls.Add(BuildGeneral(config), 0, 0);
            root.Controls.Add(BuildLevels(config), 0, 1);
            root.Controls.Add(BuildButtons(), 0, 2);

            RefreshCandidates();
            Shown += delegate { grid.ClearSelection(); };
            // CreateGraphics でハンドルは作成済みなので、部品を並べ終えたここで配色を当てる
            OnThemeChanged(this, EventArgs.Empty);
            Theme.Changed += OnThemeChanged;
        }

        void OnThemeChanged(object sender, EventArgs e)
        {
            Theme.ApplyTo(this);
            Theme.ApplyTitleBar(this);
            Invalidate(true);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                Theme.Changed -= OnThemeChanged;
                // 表に表示した縮小アイコンと、タイトルバー用に読み込んだアイコンを破棄する
                foreach (var bmp in iconCache.Values) bmp.Dispose();
                iconCache.Clear();
                if (Icon != null) Icon.Dispose();
            }
            base.Dispose(disposing);
        }

        string DevicePattern()
        {
            string text = deviceBox.Text.Trim();
            return text == DefaultDeviceLabel ? "" : text;
        }

        int S(int px) { return (int)(px * s); }

        GroupBox BuildGeneral(Config config)
        {
            var box = new GroupBox { Text = "全般", Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(S(10)) };
            var table = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, AutoSize = true };
            table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

            deviceBox = new ComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDown };
            deviceBox.Items.Add(DefaultDeviceLabel);
            foreach (var e in Audio.GetEndpoints())
                if (e.State == 1 && !string.IsNullOrEmpty(e.Name)) deviceBox.Items.Add(e.Name);
            deviceBox.Text = config.EndpointPattern.Length == 0 ? DefaultDeviceLabel : config.EndpointPattern;
            deviceBox.TextChanged += delegate { RefreshCandidates(); };
            AddField(table, 0, "対象の再生デバイス", deviceBox, "一覧から選ぶか、デバイス名の一部を入力（例：USB）");

            hotkeyBox = new TextBox { Dock = DockStyle.Fill, ReadOnly = true, Text = config.Hotkey };
            hotkeyBox.KeyDown += HotkeyKeyDown;
            hotkeyBox.Enter += delegate { tray.SuspendHotkey(); };
            hotkeyBox.Leave += delegate { tray.ResumeHotkey(); };
            AddField(table, 1, "ミキサーのホットキー", hotkeyBox, "欄をクリックしてキーを押す。Backspace で無効");

            themeBox = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = S(220) };
            themeBox.Items.AddRange(ThemeLabels);
            int themeIndex = Array.FindIndex(ThemeValues, x => x.Equals(config.Theme, StringComparison.OrdinalIgnoreCase));
            themeBox.SelectedIndex = Math.Max(0, themeIndex);
            AddField(table, 2, "テーマ", themeBox, "ミキサーと設定画面の配色");

            startupBox = new CheckBox { Text = "Windows の起動時に自動で起動する", AutoSize = true, Checked = Startup.Enabled, Margin = new Padding(S(3), S(4), 0, S(4)) };
            AddField(table, 3, "", startupBox, null);

            box.Controls.Add(table);
            return box;
        }

        void AddField(TableLayoutPanel table, int index, string label, Control input, string hint)
        {
            table.RowCount = (index + 1) * 2;
            table.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, S(6), S(10), 0) }, 0, index * 2);
            table.Controls.Add(input, 1, index * 2);
            if (hint != null)
                table.Controls.Add(new Label { Text = hint, Tag = "hint", AutoSize = true, Margin = new Padding(S(3), 0, 0, S(8)) }, 1, index * 2 + 1);
        }

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        static extern short GetKeyState(int vk);

        // 押したキーを "Ctrl+Shift+Q" 形式で記録する
        void HotkeyKeyDown(object sender, KeyEventArgs e)
        {
            e.SuppressKeyPress = true;
            e.Handled = true;
            var key = e.KeyCode;
            if (key == Keys.Back || key == Keys.Delete) { hotkeyBox.Text = ""; return; }
            if (key == Keys.ControlKey || key == Keys.ShiftKey || key == Keys.Menu || key == Keys.LWin || key == Keys.RWin) return;
            var parts = new List<string>();
            if (e.Control) parts.Add("Ctrl");
            if (e.Shift) parts.Add("Shift");
            if (e.Alt) parts.Add("Alt");
            if ((GetKeyState(0x5B) & 0x8000) != 0 || (GetKeyState(0x5C) & 0x8000) != 0) parts.Add("Win");
            string name = key.ToString();
            if (key >= Keys.D0 && key <= Keys.D9) name = name.Substring(1);
            parts.Add(name);
            hotkeyBox.Text = string.Join("+", parts.ToArray());
        }

        GroupBox BuildLevels(Config config)
        {
            var box = new GroupBox { Text = "起動時・接続時に戻すアプリ別の音量", Dock = DockStyle.Fill, Padding = new Padding(S(10)) };
            var table = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3 };
            // 下の行の中身に引きずられて表が枠より広がらないよう、列幅は枠に合わせる
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            table.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            table.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            grid = new DataGridView
            {
                Dock = DockStyle.Fill,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = true,
                AllowUserToResizeRows = false,
                RowHeadersVisible = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                MultiSelect = false,
                BackgroundColor = SystemColors.Window,
                BorderStyle = BorderStyle.FixedSingle,
                EditMode = DataGridViewEditMode.EditOnEnter,
            };
            grid.RowTemplate.Height = S(26);
            grid.Columns.Add(new DataGridViewImageColumn { Name = "Icon", HeaderText = "", Width = S(30), ImageLayout = DataGridViewImageCellLayout.Zoom, ReadOnly = true });
            grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Title", HeaderText = "アプリ", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill, ReadOnly = true });
            grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Key", HeaderText = "プロセス名", Width = S(120), ReadOnly = true });
            grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Level", HeaderText = "音量（%）", Width = S(76) });
            grid.Columns.Add(new DataGridViewButtonColumn { Name = "Remove", HeaderText = "", Text = "削除", UseColumnTextForButtonValue = true, Width = S(60) });
            grid.Columns["Level"].DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
            grid.CellValidating += GridCellValidating;
            grid.CellContentClick += delegate(object sender, DataGridViewCellEventArgs e)
            {
                if (e.RowIndex >= 0 && grid.Columns[e.ColumnIndex].Name == "Remove")
                {
                    grid.Rows.RemoveAt(e.RowIndex);
                    RefreshCandidates();
                }
            };
            grid.UserDeletedRow += delegate { RefreshCandidates(); };

            // 今鳴っているアプリから表示名とアイコンを引く。起動していないアプリはプロセス名で表示
            var running = RunningApps(config.EndpointPattern);
            foreach (var kv in config.Levels)
            {
                Candidate c;
                if (!running.TryGetValue(kv.Key, out c)) c = new Candidate { Key = kv.Key, Title = kv.Key == "System" ? "システム音" : kv.Key };
                AddGridRow(c, (int)Math.Round(kv.Value * 100));
            }
            table.Controls.Add(grid, 0, 0);

            var addRow = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = true, Margin = new Padding(0, S(8), 0, 0) };
            addBox = new ComboBox { DropDownStyle = ComboBoxStyle.DropDown, Width = S(220) };
            addLevel = new NumericUpDown { Minimum = 0, Maximum = 100, Value = 40, Width = S(60), TextAlign = HorizontalAlignment.Right };
            var addButton = new Button { Text = "追加", AutoSize = true };
            addButton.Click += delegate { AddSelected(); };
            var refreshButton = new Button { Text = "一覧を更新", AutoSize = true };
            refreshButton.Click += delegate { RefreshCandidates(); };
            addRow.Controls.Add(new Label { Text = "追加するアプリ", AutoSize = true, Margin = new Padding(0, S(6), S(6), 0) });
            addRow.Controls.Add(addBox);
            addRow.Controls.Add(addLevel);
            addRow.Controls.Add(new Label { Text = "%", AutoSize = true, Margin = new Padding(0, S(6), S(10), 0) });
            addRow.Controls.Add(addButton);
            addRow.Controls.Add(refreshButton);
            table.Controls.Add(addRow, 0, 1);
            var hint = new Label
            {
                Text = "候補は、今この再生デバイスで音量を変えられるアプリです。一覧にないアプリはプロセス名を入力して追加できます。",
                Tag = "hint",
                AutoSize = true,
                Margin = new Padding(0, S(4), 0, 0),
            };
            table.Resize += delegate { hint.MaximumSize = new Size(Math.Max(S(100), table.ClientSize.Width - S(6)), 0); };
            table.Controls.Add(hint, 0, 2);

            box.Controls.Add(table);
            return box;
        }

        FlowLayoutPanel BuildButtons()
        {
            var panel = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Margin = new Padding(0, S(8), 0, 0) };
            var cancel = new Button { Text = "キャンセル", AutoSize = true, DialogResult = DialogResult.Cancel };
            cancel.Click += delegate { Close(); };
            var apply = new Button { Text = "適用", AutoSize = true };
            apply.Click += delegate { Save(); };
            var ok = new Button { Text = "保存して閉じる", AutoSize = true };
            ok.Click += delegate { if (Save()) Close(); };
            panel.Controls.Add(cancel);
            panel.Controls.Add(apply);
            panel.Controls.Add(ok);
            CancelButton = cancel;
            return panel;
        }

        Bitmap SmallIcon(Icon icon)
        {
            if (icon == null) return null;
            string key = icon.Handle.ToString();
            Bitmap bmp;
            if (iconCache.TryGetValue(key, out bmp)) return bmp;
            using (var full = icon.ToBitmap()) bmp = new Bitmap(full, S(20), S(20));
            iconCache[key] = bmp;
            return bmp;
        }

        void AddGridRow(Candidate c, int level)
        {
            int i = grid.Rows.Add(SmallIcon(c.Icon), c.Title, c.Key, level.ToString(CultureInfo.InvariantCulture));
            grid.Rows[i].Tag = c.Key;
        }

        // 今その再生デバイスに音量セッションがあるアプリ（キーはプロセス名、システム音は System）
        static Dictionary<string, Candidate> RunningApps(string pattern)
        {
            var result = new Dictionary<string, Candidate>(StringComparer.OrdinalIgnoreCase);
            try
            {
                foreach (var session in Audio.GetSessions(pattern))
                {
                    if (string.IsNullOrEmpty(session.Key) || result.ContainsKey(session.Key)) continue;
                    result[session.Key] = new Candidate { Key = session.Key, Title = AppInfo.TitleFor(session), Icon = AppInfo.IconFor(session) };
                }
            }
            catch (Exception ex)
            {
                Log.Write("設定画面でアプリ一覧を取得できません: " + ex.Message);
            }
            return result;
        }

        HashSet<string> ConfiguredKeys()
        {
            var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (DataGridViewRow row in grid.Rows) keys.Add((string)row.Tag);
            return keys;
        }

        void RefreshCandidates()
        {
            if (addBox == null) return;
            var configured = ConfiguredKeys();
            var running = RunningApps(DevicePattern());
            if (!running.ContainsKey("System"))
                running["System"] = new Candidate { Key = "System", Title = "システム音" };
            addBox.BeginUpdate();
            addBox.Items.Clear();
            foreach (var c in running.Values
                .Where(x => !configured.Contains(x.Key))
                .OrderBy(x => x.Key == "System" ? 0 : 1)
                .ThenBy(x => x.Title, StringComparer.CurrentCultureIgnoreCase))
                addBox.Items.Add(c);
            addBox.EndUpdate();
            if (addBox.Items.Count > 0) addBox.SelectedIndex = 0;
            else addBox.Text = "";
        }

        void AddSelected()
        {
            var c = addBox.SelectedItem as Candidate;
            if (c == null || addBox.Text != c.ToString())
            {
                // 一覧にないアプリはプロセス名（.exe なし）を直接入力して追加する
                string typed = addBox.Text.Trim();
                if (typed.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) typed = typed.Substring(0, typed.Length - 4);
                if (typed.Length == 0) return;
                if (!Config.IsValidAppKey(typed))
                {
                    MessageBox.Show(this, "「" + typed + "」はアプリ名として登録できません。\n設定項目と同じ名前や、= # ; [ ] を含む名前は使えません。", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                c = new Candidate { Key = typed, Title = typed };
            }
            if (ConfiguredKeys().Contains(c.Key))
            {
                MessageBox.Show(this, c.Title + " は登録済みです。", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            AddGridRow(c, (int)addLevel.Value);
            RefreshCandidates();
        }

        void GridCellValidating(object sender, DataGridViewCellValidatingEventArgs e)
        {
            if (grid.Columns[e.ColumnIndex].Name != "Level") return;
            int v;
            if (!int.TryParse(Convert.ToString(e.FormattedValue), out v) || v < 0 || v > 100)
            {
                grid.Rows[e.RowIndex].ErrorText = "0〜100 の数値を入力してください";
                e.Cancel = true;
            }
            else grid.Rows[e.RowIndex].ErrorText = "";
        }

        bool Save()
        {
            if (!grid.EndEdit()) return false;
            var config = new Config
            {
                EndpointPattern = DevicePattern(),
                Hotkey = hotkeyBox.Text.Trim(),
                IntervalSeconds = intervalSeconds,
                Theme = ThemeValues[Math.Max(0, themeBox.SelectedIndex)],
            };
            foreach (DataGridViewRow row in grid.Rows)
                config.Levels[(string)row.Tag] = int.Parse(Convert.ToString(row.Cells["Level"].Value), CultureInfo.InvariantCulture) / 100f;
            try
            {
                config.Save(tray.ConfigPath);
                if (startupBox.Checked != Startup.Enabled) Startup.Enabled = startupBox.Checked;
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "保存できませんでした: " + ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }
            string error = tray.ReloadNow();
            if (error != null)
                MessageBox.Show(this, "保存しましたが、ホットキー " + config.Hotkey + " を登録できませんでした。\n" + error, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return true;
        }
    }
}
