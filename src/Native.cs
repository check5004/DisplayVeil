using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;

namespace DisplayVeil
{
    internal static class Native
    {
        internal const int WM_HOTKEY = 0x0312, WM_MOUSEACTIVATE = 0x0021, WM_DPICHANGED = 0x02E0;
        internal const int WS_EX_NOACTIVATE = 0x08000000, WS_EX_TOOLWINDOW = 0x80;
        internal const uint SWP_NOACTIVATE = 0x10, SWP_SHOWWINDOW = 0x40;
        internal static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);

        [StructLayout(LayoutKind.Sequential)]
        internal struct RECT
        {
            public int Left, Top, Right, Bottom;
            public Rectangle ToRectangle() { return Rectangle.FromLTRB(Left, Top, Right, Bottom); }
        }
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct MONITORINFOEX
        {
            public int Size;
            public RECT Monitor, Work;
            public uint Flags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string Device;
        }
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct DISPLAY_DEVICE
        {
            public int Size;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string DeviceName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceString;
            public int StateFlags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceID;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceKey;
        }
        private delegate bool MonitorEnumProc(IntPtr monitor, IntPtr hdc, ref RECT rect, IntPtr data);
        [DllImport("user32.dll", SetLastError = true)] private static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr clip, MonitorEnumProc callback, IntPtr data);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool GetMonitorInfo(IntPtr monitor, ref MONITORINFOEX info);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool EnumDisplayDevices(string device, uint number, ref DISPLAY_DEVICE info, uint flags);
        [DllImport("shcore.dll")] private static extern int GetDpiForMonitor(IntPtr monitor, int type, out uint dpiX, out uint dpiY);
        [DllImport("user32.dll")] private static extern bool SetProcessDpiAwarenessContext(IntPtr context);
        [DllImport("user32.dll")] private static extern bool SetProcessDPIAware();
        [DllImport("user32.dll", SetLastError = true)] internal static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int width, int height, uint flags);
        [DllImport("user32.dll", SetLastError = true)] internal static extern bool RegisterHotKey(IntPtr window, int id, uint modifiers, uint key);
        [DllImport("user32.dll")] internal static extern bool UnregisterHotKey(IntPtr window, int id);
        [DllImport("user32.dll")] internal static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] internal static extern bool SetForegroundWindow(IntPtr window);
        [DllImport("user32.dll")] internal static extern bool IsWindow(IntPtr window);
        [DllImport("user32.dll")] internal static extern IntPtr GetWindow(IntPtr window, uint command);
        [DllImport("user32.dll")] internal static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
        [DllImport("user32.dll")] internal static extern bool GetWindowRect(IntPtr window, out RECT rect);
        [DllImport("user32.dll", EntryPoint = "GetWindowLongW")] internal static extern int GetWindowLong(IntPtr window, int index);

        public static void EnableDpiAwareness()
        {
            try { SetProcessDpiAwarenessContext(new IntPtr(-4)); }
            catch (EntryPointNotFoundException) { SetProcessDPIAware(); }
        }

        public static List<DisplayInfo> GetDisplays()
        {
            var result = new List<DisplayInfo>();
            bool success = EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, delegate(IntPtr monitor, IntPtr hdc, ref RECT rect, IntPtr data)
            {
                var info = new MONITORINFOEX { Size = Marshal.SizeOf(typeof(MONITORINFOEX)) };
                if (!GetMonitorInfo(monitor, ref info)) return true;
                var device = new DISPLAY_DEVICE { Size = Marshal.SizeOf(typeof(DISPLAY_DEVICE)) };
                string id = info.Device, name = "ディスプレイ";
                for (uint index = 0; EnumDisplayDevices(info.Device, index, ref device, 1); index++)
                {
                    if ((device.StateFlags & 1) != 0)
                    {
                        if (!String.IsNullOrEmpty(device.DeviceID)) id = device.DeviceID;
                        if (!String.IsNullOrEmpty(device.DeviceString)) name = device.DeviceString;
                        break;
                    }
                    device.Size = Marshal.SizeOf(typeof(DISPLAY_DEVICE));
                }
                uint dx = 96, dy = 96;
                try { if (GetDpiForMonitor(monitor, 0, out dx, out dy) != 0) dx = 96; }
                catch (DllNotFoundException) { }
                catch (EntryPointNotFoundException) { }
                var bounds = info.Monitor.ToRectangle();
                if (bounds.Width > 0 && bounds.Height > 0)
                    result.Add(new DisplayInfo { Id = id, DeviceName = info.Device, Name = name,
                        Bounds = bounds, WorkArea = info.Work.ToRectangle(), IsPrimary = (info.Flags & 1) != 0, Dpi = (int)dx });
                return true;
            }, IntPtr.Zero);
            if (!success) throw new Win32Exception(Marshal.GetLastWin32Error());
            // Keep Windows' DISPLAYn order where possible. UI numbers are explicitly app-local.
            result = result.OrderBy(d => DeviceNumber(d.DeviceName)).ThenBy(d => d.DeviceName, StringComparer.Ordinal).ToList();
            for (int i = 0; i < result.Count; i++) result[i].Number = i + 1;
            foreach (var group in result.GroupBy(d => d.Id).Where(g => g.Count() > 1))
                foreach (var display in group) display.Id += "|" + display.DeviceName;
            return result;
        }

        private static int DeviceNumber(string name)
        {
            int number;
            string digits = new string(name.Where(Char.IsDigit).ToArray());
            return Int32.TryParse(digits, out number) ? number : Int32.MaxValue;
        }
    }
}
