using System;
using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using System.Drawing;

namespace Defender_Cab_Verification_Tool
{
    public static class ThemeManager
    {
        // P/Invoke for accessing Windows Registry to detect dark mode
        [DllImport("advapi32.dll", EntryPoint = "RegOpenKeyExW", SetLastError = true)]
        private static extern int RegOpenKeyEx(IntPtr hKey, string lpSubKey, int ulOptions, int samDesired, out IntPtr phkResult);

        [DllImport("advapi32.dll", EntryPoint = "RegQueryValueExW", SetLastError = true)]
        private static extern int RegQueryValueEx(IntPtr hKey, string lpValueName, IntPtr lpReserved, out uint lpType, out int lpData, ref int lpcbData);

        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern int RegCloseKey(IntPtr hKey);

        private const int HKEY_CURRENT_USER = unchecked((int)0x80000001);
        private const int KEY_READ = 0x20019;
        private const string RegistryPath = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
        private const string RegistryValueName = "AppsUseLightTheme";
        private static string ThemeConfigPath => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "theme.cfg");

        private static ThemeMode _currentTheme = ThemeMode.Light;

        public enum ThemeMode
        {
            Light,
            Dark
        }

        public static ThemeMode GetSystemTheme()
        {
            // Try multiple methods to detect Windows theme

            // Method 1: Try the Apps light theme setting (most common)
            try
            {
                using (var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(RegistryPath))
                {
                    if (key != null)
                    {
                        var value = key.GetValue(RegistryValueName);
                        if (value != null && int.TryParse(value.ToString(), out int result))
                        {
                            // If AppsUseLightTheme is 1, use Light mode; if 0, use Dark mode
                            return result == 1 ? ThemeMode.Light : ThemeMode.Dark;
                        }
                    }
                }
            }
            catch
            {
                // Try next method
            }

            // Method 2: Try P/Invoke as fallback
            try
            {
                IntPtr hKey;
                if (RegOpenKeyEx(new IntPtr(HKEY_CURRENT_USER), RegistryPath, 0, KEY_READ, out hKey) == 0)
                {
                    int value = 0;
                    int size = sizeof(int);
                    uint type;

                    if (RegQueryValueEx(hKey, RegistryValueName, IntPtr.Zero, out type, out value, ref size) == 0)
                    {
                        RegCloseKey(hKey);
                        return value == 1 ? ThemeMode.Light : ThemeMode.Dark;
                    }

                    RegCloseKey(hKey);
                }
            }
            catch
            {
                // Fall through to default
            }

            // Default to Light mode if all detection methods fail
            return ThemeMode.Light;
        }

        public static void SaveThemePreference(ThemeMode theme)
        {
            try
            {
                File.WriteAllText(ThemeConfigPath, theme.ToString());
            }
            catch
            {
                // Silently fail if we can't save
            }
        }

        public static ThemeMode? LoadThemePreference()
        {
            try
            {
                if (File.Exists(ThemeConfigPath))
                {
                    var content = File.ReadAllText(ThemeConfigPath).Trim();
                    if (Enum.TryParse<ThemeMode>(content, out var theme))
                    {
                        return theme;
                    }
                }
            }
            catch
            {
                // Silently fail if we can't load
            }

            return null;
        }

        public static void ApplyTheme(Form form, ThemeMode theme)
        {
            _currentTheme = theme;
            if (theme == ThemeMode.Dark)
            {
                ApplyDarkTheme(form);
            }
            else
            {
                ApplyLightTheme(form);
            }
        }

        public static ThemeMode GetCurrentTheme()
        {
            return _currentTheme;
        }

        private static void ApplyLightTheme(Form form)
        {
            form.BackColor = Color.White;
            form.ForeColor = Color.Black;

            ApplyThemeToControls(form.Controls, ThemeMode.Light);
        }

        private static void ApplyDarkTheme(Form form)
        {
            form.BackColor = Color.FromArgb(32, 32, 32);
            form.ForeColor = Color.White;

            ApplyThemeToControls(form.Controls, ThemeMode.Dark);
        }

        private static void ApplyThemeToControls(Control.ControlCollection controls, ThemeMode theme)
        {
            foreach (Control control in controls)
            {
                if (theme == ThemeMode.Dark)
                {
                    if (control is TextBox || control is ComboBox || control is ListBox)
                    {
                        control.BackColor = Color.FromArgb(45, 45, 48);
                        control.ForeColor = Color.White;
                    }
                    else if (control is Button)
                    {
                        control.BackColor = Color.FromArgb(60, 60, 60);
                        control.ForeColor = Color.White;
                        ((Button)control).FlatStyle = FlatStyle.Flat;
                    }
                    else if (control is Label || control is CheckBox || control is RadioButton)
                    {
                        control.ForeColor = Color.White;
                        control.BackColor = Color.Transparent;
                    }
                    else if (control is ListView)
                    {
                        control.BackColor = Color.FromArgb(45, 45, 48);
                        control.ForeColor = Color.White;
                    }
                    else if (control is Panel || control is GroupBox)
                    {
                        control.BackColor = Color.FromArgb(32, 32, 32);
                        control.ForeColor = Color.White;
                    }
                    else if (control is MenuStrip)
                    {
                        control.BackColor = Color.FromArgb(45, 45, 48);
                        control.ForeColor = Color.White;
                        ApplyThemeToMenuStrip((MenuStrip)control, theme);
                    }
                    else if (control is StatusStrip)
                    {
                        control.BackColor = Color.FromArgb(45, 45, 48);
                        control.ForeColor = Color.White;
                    }
                    else if (control is ProgressBar)
                    {
                        control.BackColor = Color.FromArgb(45, 45, 48);
                        control.ForeColor = Color.White;
                    }
                }
                else
                {
                    if (control is TextBox || control is ComboBox || control is ListBox)
                    {
                        control.BackColor = Color.White;
                        control.ForeColor = Color.Black;
                    }
                    else if (control is Button)
                    {
                        control.BackColor = SystemColors.Control;
                        control.ForeColor = Color.Black;
                        ((Button)control).FlatStyle = FlatStyle.Standard;
                    }
                    else if (control is Label || control is CheckBox || control is RadioButton)
                    {
                        control.ForeColor = Color.Black;
                        control.BackColor = Color.Transparent;
                    }
                    else if (control is ListView)
                    {
                        control.BackColor = Color.White;
                        control.ForeColor = Color.Black;
                    }
                    else if (control is Panel || control is GroupBox)
                    {
                        control.BackColor = SystemColors.Control;
                        control.ForeColor = Color.Black;
                    }
                    else if (control is MenuStrip)
                    {
                        control.BackColor = SystemColors.Menu;
                        control.ForeColor = Color.Black;
                        ApplyThemeToMenuStrip((MenuStrip)control, theme);
                    }
                    else if (control is StatusStrip)
                    {
                        control.BackColor = SystemColors.Control;
                        control.ForeColor = Color.Black;
                    }
                    else if (control is ProgressBar)
                    {
                        control.BackColor = SystemColors.Control;
                        control.ForeColor = Color.Black;
                    }
                }

                // Recursively apply theme to child controls
                if (control.HasChildren)
                {
                    ApplyThemeToControls(control.Controls, theme);
                }
            }
        }

        private static void ApplyThemeToMenuStrip(MenuStrip menuStrip, ThemeMode theme)
        {
            foreach (ToolStripMenuItem item in menuStrip.Items)
            {
                ApplyThemeToToolStripItem(item, theme);
            }
        }

        private static void ApplyThemeToToolStripItem(ToolStripItem item, ThemeMode theme)
        {
            if (theme == ThemeMode.Dark)
            {
                item.BackColor = Color.FromArgb(45, 45, 48);
                item.ForeColor = Color.White;
            }
            else
            {
                item.BackColor = SystemColors.Menu;
                item.ForeColor = Color.Black;
            }

            if (item is ToolStripMenuItem menuItem)
            {
                foreach (ToolStripItem subItem in menuItem.DropDownItems)
                {
                    ApplyThemeToToolStripItem(subItem, theme);
                }
            }
        }
    }
}