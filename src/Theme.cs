using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Win32;

namespace VolumeKeeper
{
    // 配色。テーマ設定（System / Light / Dark）と Windows のアプリモードから決まる
    static class Theme
    {
        class Palette
        {
            public bool Dark;
            public Color Background, Hover, Text, SubText, Track, Accent, MutedFill, Separator, ThumbRing, Input, Button, Border, Selection;
        }

        static readonly Palette DarkPalette = new Palette
        {
            Dark = true,
            Background = Color.FromArgb(32, 32, 32),
            Hover = Color.FromArgb(45, 45, 45),
            Text = Color.FromArgb(240, 240, 240),
            SubText = Color.FromArgb(160, 160, 160),
            Track = Color.FromArgb(95, 95, 95),
            Accent = Color.FromArgb(76, 194, 255),
            MutedFill = Color.FromArgb(120, 120, 120),
            Separator = Color.FromArgb(60, 60, 60),
            ThumbRing = Color.FromArgb(69, 69, 69),
            Input = Color.FromArgb(45, 45, 45),
            Button = Color.FromArgb(55, 55, 55),
            Border = Color.FromArgb(85, 85, 85),
            Selection = Color.FromArgb(0, 90, 158),
        };

        static readonly Palette LightPalette = new Palette
        {
            Dark = false,
            Background = Color.FromArgb(243, 243, 243),
            Hover = Color.FromArgb(232, 232, 232),
            Text = Color.FromArgb(26, 26, 26),
            SubText = Color.FromArgb(96, 96, 96),
            Track = Color.FromArgb(160, 160, 160),
            Accent = Color.FromArgb(0, 103, 192),
            MutedFill = Color.FromArgb(150, 150, 150),
            Separator = Color.FromArgb(220, 220, 220),
            ThumbRing = Color.FromArgb(255, 255, 255),
            Input = SystemColors.Window,
            Button = SystemColors.Control,
            Border = Color.FromArgb(200, 200, 200),
            Selection = SystemColors.Highlight,
        };

        static Palette current = LightPalette;
        static string mode = "System";

        public static readonly Font TitleFont = new Font("Yu Gothic UI", 9f);
        public static readonly Font SmallFont = new Font("Yu Gothic UI", 8.5f);

        // テーマが切り替わったとき（設定変更、または System 指定で Windows 側が変わったとき）
        public static event EventHandler Changed;

        public static bool IsDark { get { return current.Dark; } }
        public static Color Background { get { return current.Background; } }
        public static Color Hover { get { return current.Hover; } }
        public static Color Text { get { return current.Text; } }
        public static Color SubText { get { return current.SubText; } }
        public static Color Track { get { return current.Track; } }
        public static Color Accent { get { return current.Accent; } }
        public static Color MutedFill { get { return current.MutedFill; } }
        public static Color Separator { get { return current.Separator; } }
        public static Color ThumbRing { get { return current.ThumbRing; } }

        static Theme()
        {
            SystemEvents.UserPreferenceChanged += (s, e) =>
            {
                if (e.Category == UserPreferenceCategory.General) Update();
            };
        }

        // "System" / "Light" / "Dark"
        public static void SetMode(string value)
        {
            mode = string.IsNullOrEmpty(value) ? "System" : value;
            Update();
        }

        static void Update()
        {
            bool dark = mode.Equals("Dark", StringComparison.OrdinalIgnoreCase)
                || (!mode.Equals("Light", StringComparison.OrdinalIgnoreCase) && WindowsAppsUseDark());
            var next = dark ? DarkPalette : LightPalette;
            if (next == current) return;
            current = next;
            if (Changed != null) Changed(null, EventArgs.Empty);
        }

        static bool WindowsAppsUseDark()
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                {
                    object v = key == null ? null : key.GetValue("AppsUseLightTheme");
                    return v is int && (int)v == 0;
                }
            }
            catch { return false; }
        }

        [DllImport("dwmapi.dll")]
        static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

        [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
        static extern int SetWindowTheme(IntPtr hwnd, string appName, string idList);

        [DllImport("user32.dll")]
        static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, int flags);

        // タイトルバーをダーク／ライトにする（Windows 10 20H1 以降）
        public static void ApplyTitleBar(Form form)
        {
            int dark = current.Dark ? 1 : 0;
            DwmSetWindowAttribute(form.Handle, 20 /* DWMWA_USE_IMMERSIVE_DARK_MODE */, ref dark, sizeof(int));
            // 枠を描き直させる（SWP_NOMOVE | NOSIZE | NOZORDER | NOACTIVATE | FRAMECHANGED）
            SetWindowPos(form.Handle, IntPtr.Zero, 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0004 | 0x0010 | 0x0020);
        }

        // 標準コントロールで組んだ画面（設定画面など）に配色を当てる
        public static void ApplyTo(Control control)
        {
            var p = current;
            if (control is Form)
            {
                control.BackColor = p.Dark ? p.Background : SystemColors.Control;
                control.ForeColor = p.Dark ? p.Text : SystemColors.ControlText;
            }
            else if (control is TextBox || control is ComboBox || control is NumericUpDown)
            {
                control.BackColor = p.Dark ? p.Input : SystemColors.Window;
                control.ForeColor = p.Dark ? p.Text : SystemColors.WindowText;
                var combo = control as ComboBox;
                if (control.IsHandleCreated) SetWindowTheme(control.Handle, p.Dark ? "DarkMode_CFD" : null, null);
            }
            else if (control is Button)
            {
                var b = (Button)control;
                if (p.Dark)
                {
                    b.FlatStyle = FlatStyle.Flat;
                    b.FlatAppearance.BorderColor = p.Border;
                    b.BackColor = p.Button;
                    b.ForeColor = p.Text;
                }
                else
                {
                    b.FlatStyle = FlatStyle.Standard;
                    b.BackColor = SystemColors.Control;
                    b.ForeColor = SystemColors.ControlText;
                    b.UseVisualStyleBackColor = true;
                }
            }
            else if (control is DataGridView)
            {
                ApplyToGrid((DataGridView)control, p);
            }
            else if (control is GroupBox)
            {
                ((GroupBox)control).FlatStyle = p.Dark ? FlatStyle.Flat : FlatStyle.Standard;
                control.ForeColor = p.Dark ? p.Text : SystemColors.ControlText;
            }
            else if (control is Label)
            {
                // Tag="hint" の補足文は薄い色にする
                bool hint = "hint".Equals(control.Tag);
                control.ForeColor = hint ? (p.Dark ? p.SubText : SystemColors.GrayText) : (p.Dark ? p.Text : SystemColors.ControlText);
            }
            else if (control is CheckBox)
            {
                control.ForeColor = p.Dark ? p.Text : SystemColors.ControlText;
            }
            foreach (Control child in control.Controls) ApplyTo(child);
        }

        static void ApplyToGrid(DataGridView grid, Palette p)
        {
            grid.EnableHeadersVisualStyles = !p.Dark;
            grid.BackgroundColor = p.Dark ? p.Input : SystemColors.Window;
            grid.GridColor = p.Dark ? p.Border : SystemColors.ControlDark;
            grid.DefaultCellStyle.BackColor = p.Dark ? p.Input : SystemColors.Window;
            grid.DefaultCellStyle.ForeColor = p.Dark ? p.Text : SystemColors.WindowText;
            grid.DefaultCellStyle.SelectionBackColor = p.Selection;
            grid.DefaultCellStyle.SelectionForeColor = p.Dark ? p.Text : SystemColors.HighlightText;
            grid.ColumnHeadersDefaultCellStyle.BackColor = p.Dark ? p.Button : SystemColors.Control;
            grid.ColumnHeadersDefaultCellStyle.ForeColor = p.Dark ? p.Text : SystemColors.ControlText;
            grid.ColumnHeadersDefaultCellStyle.SelectionBackColor = grid.ColumnHeadersDefaultCellStyle.BackColor;
            grid.ColumnHeadersBorderStyle = p.Dark ? DataGridViewHeaderBorderStyle.Single : DataGridViewHeaderBorderStyle.Raised;
            if (grid.IsHandleCreated) SetWindowTheme(grid.Handle, p.Dark ? "DarkMode_Explorer" : "Explorer", null);
            foreach (DataGridViewColumn column in grid.Columns)
            {
                var button = column as DataGridViewButtonColumn;
                if (button == null) continue;
                button.FlatStyle = p.Dark ? FlatStyle.Flat : FlatStyle.Standard;
                button.DefaultCellStyle.BackColor = p.Dark ? p.Button : SystemColors.Control;
                button.DefaultCellStyle.ForeColor = p.Dark ? p.Text : SystemColors.ControlText;
            }
        }

        // トレイの右クリックメニュー
        public static void ApplyTo(ToolStrip strip)
        {
            strip.Renderer = current.Dark ? (ToolStripRenderer)new ToolStripProfessionalRenderer(new DarkMenuColors()) : new ToolStripProfessionalRenderer();
            strip.BackColor = current.Dark ? current.Background : SystemColors.Menu;
            foreach (ToolStripItem item in strip.Items) item.ForeColor = current.Dark ? current.Text : SystemColors.MenuText;
        }

        class DarkMenuColors : ProfessionalColorTable
        {
            public override Color ToolStripDropDownBackground { get { return DarkPalette.Background; } }
            public override Color ImageMarginGradientBegin { get { return DarkPalette.Background; } }
            public override Color ImageMarginGradientMiddle { get { return DarkPalette.Background; } }
            public override Color ImageMarginGradientEnd { get { return DarkPalette.Background; } }
            public override Color MenuBorder { get { return DarkPalette.Border; } }
            public override Color MenuItemBorder { get { return DarkPalette.Hover; } }
            public override Color MenuItemSelected { get { return DarkPalette.Hover; } }
            public override Color SeparatorDark { get { return DarkPalette.Separator; } }
            public override Color SeparatorLight { get { return DarkPalette.Separator; } }
        }
    }
}
