using System;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Windows.Forms;
using Microsoft.Win32;

namespace ip2id
{
    static class Program
    {
        static NotifyIcon trayIcon = null!;
        static TrayAppContext context = null!;

        [STAThread]
        static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            context = new TrayAppContext();
            Application.Run(context);
        }

        class TrayAppContext : ApplicationContext
        {
            private ContextMenuStrip menu;
            private ToolStripMenuItem startWithWindowsMenuItem;
            private ToolStripMenuItem lightIconMenuItem;
            private ToolStripMenuItem darkIconMenuItem;
            private ToolStripMenuItem exitMenuItem;
            private ToolStripMenuItem adapterMenu; 

            private const string registryRunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
            private const string startupValueName = "IPv4InTray";
            private const string registrySettingsKey = @"Software\IPv4InTray";
            private const string registryIconColorValue = "UseDarkIcon";
            private const string registryAdapterValue = "SelectedAdapter";

            public TrayAppContext()
            {
                trayIcon = new NotifyIcon();
                menu = new ContextMenuStrip();

                var headerItem = new ToolStripMenuItem("IP2ID v0.3 | zdnsk") { Enabled = false };
                menu.Items.Add(headerItem);

                startWithWindowsMenuItem = new ToolStripMenuItem("Indítás rendszerindítással")
                {
                    CheckOnClick = true,
                    Checked = IsStartupEnabled()
                };
                startWithWindowsMenuItem.Click += StartWithWindowsMenuItem_Click;
                menu.Items.Add(startWithWindowsMenuItem);

                lightIconMenuItem = new ToolStripMenuItem("Világos ikon")
                {
                    CheckOnClick = true,
                    Checked = !GetUseDarkIconFromRegistry()
                };
                darkIconMenuItem = new ToolStripMenuItem("Sötét ikon")
                {
                    CheckOnClick = true,
                    Checked = GetUseDarkIconFromRegistry()
                };
                lightIconMenuItem.Click += IconColorMenuItem_Click;
                darkIconMenuItem.Click += IconColorMenuItem_Click;

                var iconColorMenu = new ToolStripMenuItem("Ikon szín");
                iconColorMenu.DropDownItems.Add(lightIconMenuItem);
                iconColorMenu.DropDownItems.Add(darkIconMenuItem);
                menu.Items.Add(iconColorMenu);

                adapterMenu = new ToolStripMenuItem("Hálózati adapter választás");
                PopulateAdapterMenu();
                menu.Items.Add(adapterMenu);

                menu.Items.Add(new ToolStripSeparator());

                exitMenuItem = new ToolStripMenuItem("Kilépés");
                exitMenuItem.Click += ExitMenuItem_Click;
                menu.Items.Add(exitMenuItem);

                trayIcon.ContextMenuStrip = menu;
                UpdateIcon();

                trayIcon.Text = GetLocalIPv4ForSelectedAdapter();
                trayIcon.Visible = true;
            }

            private void PopulateAdapterMenu()
            {
                adapterMenu.DropDownItems.Clear();
                string selectedAdapterName = GetSelectedAdapterNameFromRegistry();

                var adapters = NetworkInterface.GetAllNetworkInterfaces()
                    .Where(ni => ni.OperationalStatus == OperationalStatus.Up &&
                                 (ni.NetworkInterfaceType == NetworkInterfaceType.Ethernet ||
                                  ni.NetworkInterfaceType == NetworkInterfaceType.Wireless80211) &&
                                 !string.IsNullOrEmpty(ni.Name))
                    .ToList();

                if (adapters.Count == 0)
                {
                    var noAdapterItem = new ToolStripMenuItem("Nincs elérhetõ adapter") { Enabled = false };
                    adapterMenu.DropDownItems.Add(noAdapterItem);
                    return;
                }

                foreach (var adapter in adapters)
                {
                    var item = new ToolStripMenuItem(adapter.Name)
                    {
                        CheckOnClick = true,
                        Checked = adapter.Name == selectedAdapterName
                    };
                    item.Click += AdapterMenuItem_Click;
                    adapterMenu.DropDownItems.Add(item);
                }
            }

            private void AdapterMenuItem_Click(object? sender, EventArgs e)
            {
                if (sender is ToolStripMenuItem clickedItem)
                {
                    foreach (ToolStripMenuItem item in adapterMenu.DropDownItems)
                    {
                        if (item != clickedItem)
                            item.Checked = false;
                    }

                    string newAdapter = clickedItem.Text;
                    SetSelectedAdapterNameToRegistry(newAdapter);
                    RestartApplication();
                }
            }

            private void UpdateIcon()
            {
                string ip = GetLocalIPv4ForSelectedAdapter();
                string lastOctet = GetLastOctet(ip);
                bool useDark = GetUseDarkIconFromRegistry();
                trayIcon.Icon = CreateIconFromText(lastOctet, useDark);
                trayIcon.Text = GetSelectedAdapterNameFromRegistry() + ip;
            }

            private string GetLocalIPv4ForSelectedAdapter()
            {
                string adapterName = GetSelectedAdapterNameFromRegistry();
                if (string.IsNullOrEmpty(adapterName))
                {
    
                    var defaultAdapter = NetworkInterface.GetAllNetworkInterfaces()
                        .FirstOrDefault(ni => ni.OperationalStatus == OperationalStatus.Up &&
                                              (ni.NetworkInterfaceType == NetworkInterfaceType.Ethernet ||
                                               ni.NetworkInterfaceType == NetworkInterfaceType.Wireless80211));
                    if (defaultAdapter != null)
                        adapterName = defaultAdapter.Name;
                    else
                        return "0.0.0.0";
                }

                var adapter = NetworkInterface.GetAllNetworkInterfaces()
                    .FirstOrDefault(ni => ni.Name == adapterName &&
                                          ni.OperationalStatus == OperationalStatus.Up);
                if (adapter == null) return "0.0.0.0";

                var ipInfo = adapter.GetIPProperties();
                foreach (var addr in ipInfo.UnicastAddresses)
                {
                    if (addr.Address.AddressFamily == AddressFamily.InterNetwork)
                        return addr.Address.ToString();
                }
                return "0.0.0.0";
            }

            private void StartWithWindowsMenuItem_Click(object? sender, EventArgs e)
            {
                SetStartup(startWithWindowsMenuItem.Checked);
                RestartApplication();
            }

            private void IconColorMenuItem_Click(object? sender, EventArgs e)
            {
                if (sender == lightIconMenuItem)
                {
                    lightIconMenuItem.Checked = true;
                    darkIconMenuItem.Checked = false;
                    SetUseDarkIconInRegistry(false);
                }
                else if (sender == darkIconMenuItem)
                {
                    darkIconMenuItem.Checked = true;
                    lightIconMenuItem.Checked = false;
                    SetUseDarkIconInRegistry(true);
                }
                RestartApplication();
            }

            private void ExitMenuItem_Click(object? sender, EventArgs e)
            {
                trayIcon.Visible = false;
                Application.Exit();
            }

            private void RestartApplication()
            {
                trayIcon.Visible = false;
                Process.Start(Application.ExecutablePath);
                Application.Exit();
            }

            private bool GetUseDarkIconFromRegistry()
            {
                using RegistryKey? key = Registry.CurrentUser.OpenSubKey(registrySettingsKey, false);
                if (key == null) return false;
                object? val = key.GetValue(registryIconColorValue);
                return val != null && val is int intVal && intVal != 0;
            }

            private void SetUseDarkIconInRegistry(bool useDark)
            {
                using RegistryKey key = Registry.CurrentUser.CreateSubKey(registrySettingsKey)!;
                key.SetValue(registryIconColorValue, useDark ? 1 : 0, RegistryValueKind.DWord);
            }

            private string GetSelectedAdapterNameFromRegistry()
            {
                using RegistryKey? key = Registry.CurrentUser.OpenSubKey(registrySettingsKey, false);
                if (key == null) return string.Empty;
                object? val = key.GetValue(registryAdapterValue);
                return val as string ?? string.Empty;
            }

            private void SetSelectedAdapterNameToRegistry(string adapterName)
            {
                using RegistryKey key = Registry.CurrentUser.CreateSubKey(registrySettingsKey)!;
                key.SetValue(registryAdapterValue, adapterName, RegistryValueKind.String);
            }
        }

        public static bool IsStartupEnabled()
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", false);
            if (key == null) return false;
            string value = key.GetValue("IPv4InTray") as string ?? string.Empty;
            return !string.IsNullOrEmpty(value);
        }

        public static void SetStartup(bool enable)
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true);
            if (key == null) return;

            if (enable)
                key.SetValue("IPv4InTray", Application.ExecutablePath);
            else
                key.DeleteValue("IPv4InTray", false);
        }

        private static string GetLastOctet(string ip)
        {
            var parts = ip.Split('.');
            return parts.Length == 4 ? parts[3] : "?";
        }

        private static Icon CreateIconFromText(string text, bool dark)
        {
            int size = 32;
            Bitmap bmp = new Bitmap(size, size);
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.Clear(dark ? Color.Black : Color.White);

                using (Font font = new Font("Segoe UI", 16, FontStyle.Bold, GraphicsUnit.Pixel))
                {
                    string drawText = string.IsNullOrEmpty(text) ? "?" : text;
                    SizeF textSize = g.MeasureString(drawText, font);
                    float margin = 2f;
                    float x = margin + (bmp.Width - 2 * margin - textSize.Width) / 2;
                    float y = (bmp.Height - textSize.Height) / 2;

                    using (Brush brush = dark ? Brushes.White : Brushes.Black)
                    {
                        g.DrawString(drawText, font, brush, x, y);
                    }
                }
            }
            IntPtr hicon = bmp.GetHicon();
            Icon icon = Icon.FromHandle(hicon);
            return icon;
        }
    }

}
