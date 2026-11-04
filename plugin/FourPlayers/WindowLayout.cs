using System;
using System.Runtime.InteropServices;
using System.Text;
using UnityEngine;

namespace FourPlayers;

/// Test copies: put this copy's window where the launcher wants it (Debug.WindowRect = "x,y,w,h", client size) and
/// title it (Debug.WindowTitle). The game applies its saved resolution at startup and ignores -screen-width/height,
/// so this is re-checked every second rather than set once.
internal static class WindowLayout
{
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumProc f, IntPtr l);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr h, StringBuilder s, int n);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] private static extern bool GetClientRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool SetWindowText(IntPtr h, string s);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
    private delegate bool EnumProc(IntPtr h, IntPtr l);
    [StructLayout(LayoutKind.Sequential)] private struct RECT { public int L, T, R, B; }

    private static float _next;
    private static IntPtr _hwnd;
    private static readonly uint Pid = (uint)Environment.ProcessId;

    internal static void Tick()
    {
        string spec = Plugin.WindowRect.Value.Trim();
        if (spec.Length == 0 || Time.unscaledTime < _next) return;
        _next = Time.unscaledTime + 1f;
        var p = spec.Split(',');
        if (p.Length != 4 || !int.TryParse(p[0], out int x) || !int.TryParse(p[1], out int y) ||
            !int.TryParse(p[2], out int w) || !int.TryParse(p[3], out int h)) return;

        if (Screen.fullScreenMode != FullScreenMode.Windowed || Screen.width != w || Screen.height != h)
        {
            Screen.SetResolution(w, h, FullScreenMode.Windowed);
            Plugin.Log.LogInfo($"Window: resolution -> {w}x{h} windowed");
            return;   // move once the resize has happened
        }

        if (_hwnd == IntPtr.Zero) _hwnd = FindOwnWindow();
        if (_hwnd == IntPtr.Zero) return;
        GetWindowRect(_hwnd, out var wr);
        if (wr.L != x || wr.T != y) SetWindowPos(_hwnd, IntPtr.Zero, x, y, 0, 0, 0x0001 | 0x0004);   // NOSIZE | NOZORDER

        string title = Plugin.WindowTitle.Value;
        if (title.Length > 0)
        {
            var sb = new StringBuilder(256);
            GetWindowText(_hwnd, sb, 256);
            if (sb.ToString() != title) SetWindowText(_hwnd, title);
        }
    }

    private static IntPtr FindOwnWindow()
    {
        IntPtr found = IntPtr.Zero;
        EnumWindows((h, _) =>
        {
            GetWindowThreadProcessId(h, out uint pid);
            if (pid != Pid || !IsWindowVisible(h)) return true;
            var cls = new StringBuilder(64);
            GetClassName(h, cls, 64);
            if (cls.ToString() != "UnityWndClass") return true;
            found = h;
            return false;
        }, IntPtr.Zero);
        return found;
    }
}
