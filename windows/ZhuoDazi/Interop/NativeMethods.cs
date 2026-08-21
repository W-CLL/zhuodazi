using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Drawing;
using System.Windows.Interop;

namespace ZhuoDazi.Interop;

internal static class NativeMethods
{
    private const int GwlStyle = -16;
    private const int GwlExStyle = -20;
    private const long WsCaption = 0x00C00000;
    private const long WsThickFrame = 0x00040000;
    private const long WsSysMenu = 0x00080000;
    private const long WsMinimizeBox = 0x00020000;
    private const long WsMaximizeBox = 0x00010000;
    private const long WsChild = 0x40000000;
    private const long WsExAppWindow = 0x00040000;
    private const long WsExToolWindow = 0x00000080;
    private const int VkLeftButton = 0x01;
    private const int WsExTransparent = 0x00000020;
    private const int WsExLayered = 0x00080000;
    private const uint WmClose = 0x0010;
    private static readonly nint HwndTopmost = new(-1);
    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoMove = 0x0002;
    private const uint SwpNoZorder = 0x0004;
    private const uint SwpFrameChanged = 0x0020;
    internal const uint SwpNoActivate = 0x0010;
    internal const uint SwpShowWindow = 0x0040;
    internal const int SwShowNoActivate = 4;

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern nint GetWindowLongPtrNative(nint hWnd, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern nint SetWindowLongPtrNative(nint hWnd, int index, nint value);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool EnumWindows(EnumWindowsProc callback, nint lParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool IsWindowVisible(nint handle);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool IsWindow(nint handle);

    [DllImport("user32.dll")]
    internal static extern nint GetParent(nint handle);

    [DllImport("user32.dll")]
    internal static extern uint GetWindowThreadProcessId(nint handle, out uint processId);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern nint SetParent(nint child, nint parent);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetWindowRect(nint handle, out NativeRect rect);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetWindowPos(nint handle, nint insertAfter, int x, int y, int width, int height, uint flags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool ShowWindow(nint handle, int command);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostMessage(nint handle, uint message, nint wParam, nint lParam);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int virtualKey);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool RegisterHotKey(nint hWnd, int id, uint modifiers, uint virtualKey);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool UnregisterHotKey(nint hWnd, int id);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool DestroyIcon(nint handle);

    internal delegate bool EnumWindowsProc(nint handle, nint lParam);

    [StructLayout(LayoutKind.Sequential)]
    internal struct NativeRect
    {
        internal int Left;
        internal int Top;
        internal int Right;
        internal int Bottom;

        internal int Width => Math.Max(0, Right - Left);
        internal int Height => Math.Max(0, Bottom - Top);
        internal Rectangle ToRectangle() => new(Left, Top, Width, Height);
    }

    internal readonly record struct EmbeddedWindowState(nint Parent, NativeRect Bounds, long Style, long ExStyle);

    internal static IReadOnlyList<nint> FindDouyinWindows()
    {
        var processIds = Process.GetProcessesByName("douyin")
            .Select(process =>
            {
                try { return process.Id; }
                finally { process.Dispose(); }
            })
            .ToHashSet();
        if (processIds.Count == 0) return [];

        var found = new List<nint>();
        EnumWindows((handle, _) =>
        {
            if (!IsWindowVisible(handle)) return true;
            GetWindowThreadProcessId(handle, out var processId);
            if (processIds.Contains((int)processId)
                && GetWindowRect(handle, out var bounds)
                && bounds.Width >= 160 && bounds.Height >= 100)
                found.Add(handle);
            return true;
        }, nint.Zero);
        return found;
    }

    internal static bool TrySetParent(nint child, nint parent, out int error)
    {
        Marshal.SetLastPInvokeError(0);
        SetParent(child, parent);
        error = Marshal.GetLastPInvokeError();
        return GetParent(child) == parent;
    }

    internal static EmbeddedWindowState? CaptureEmbeddedWindowState(nint handle)
    {
        if (!IsWindow(handle) || !GetWindowRect(handle, out var bounds)) return null;
        return new EmbeddedWindowState(
            GetParent(handle),
            bounds,
            (long)GetWindowLongPtrNative(handle, GwlStyle),
            (long)GetWindowLongPtrNative(handle, GwlExStyle));
    }

    internal static void SetEmbeddedWindowStyle(nint handle)
    {
        SetWindowPos(handle, HwndTopmost, 0, 0, 0, 0,
            SwpNoMove | SwpNoSize | SwpNoActivate);
        var style = (long)GetWindowLongPtrNative(handle, GwlStyle);
        style = (style | WsChild) & ~(WsCaption | WsThickFrame | WsSysMenu | WsMinimizeBox | WsMaximizeBox);
        SetWindowLongPtrNative(handle, GwlStyle, (nint)style);

        var exStyle = (long)GetWindowLongPtrNative(handle, GwlExStyle);
        exStyle = (exStyle & ~WsExAppWindow) | WsExToolWindow;
        SetWindowLongPtrNative(handle, GwlExStyle, (nint)exStyle);
        SetWindowPos(handle, nint.Zero, 0, 0, 0, 0,
            SwpNoMove | SwpNoSize | SwpNoZorder | SwpNoActivate | SwpFrameChanged);
    }

    internal static void RestoreEmbeddedWindowStyle(nint handle, EmbeddedWindowState state)
    {
        SetWindowLongPtrNative(handle, GwlStyle, (nint)state.Style);
        SetWindowLongPtrNative(handle, GwlExStyle, (nint)state.ExStyle);
    }

    internal static bool IsLeftMouseButtonDown() => (GetAsyncKeyState(VkLeftButton) & 0x8000) != 0;

    internal static void CloseWindow(nint handle) => PostMessage(handle, WmClose, nint.Zero, nint.Zero);

    internal static void SetClickThrough(System.Windows.Window window, bool enabled)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == nint.Zero) return;
        var style = (long)GetWindowLongPtrNative(handle, GwlExStyle);
        style = enabled ? style | WsExTransparent | WsExLayered : style & ~WsExTransparent;
        SetWindowLongPtrNative(handle, GwlExStyle, (nint)style);
    }
}
