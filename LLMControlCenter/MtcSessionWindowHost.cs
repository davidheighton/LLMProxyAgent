using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;

namespace MTC;

internal sealed record MtcDockedSessionInfo(
    string AgentId,
    string GameName,
    int ProcessId,
    bool IsDocked,
    bool IsReady,
    string? Error);

internal sealed class MtcSessionWindowHost : IDisposable
{
    private const int GwlStyle = -16;
    private const int GwlExStyle = -20;
    private const long WsChild = 0x40000000L;
    private const long WsPopup = 0x80000000L;
    private const long WsCaption = 0x00C00000L;
    private const long WsThickFrame = 0x00040000L;
    private const long WsSysMenu = 0x00080000L;
    private const long WsMinimizeBox = 0x00020000L;
    private const long WsMaximizeBox = 0x00010000L;
    private const long WsExAppWindow = 0x00040000L;
    private const long WsExToolWindow = 0x00000080L;
    private const long WsClipSiblings = 0x04000000L;
    private const uint SwHide = 0;
    private const uint SwShow = 5;
    private const uint SwShowNoActivate = 8;
    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpFrameChanged = 0x0020;
    private const uint SwpShowWindow = 0x0040;
    private const uint GwOwner = 4;

    private readonly Window _owner;
    private readonly Control _pane;
    private readonly DispatcherTimer _pollTimer;
    private readonly Dictionary<string, SessionWindow> _sessions =
        new(StringComparer.OrdinalIgnoreCase);
    private string? _activeAgentId;
    private bool _disposed;

    public MtcSessionWindowHost(Window owner, Control pane)
    {
        _owner = owner ?? throw new ArgumentNullException(nameof(owner));
        _pane = pane ?? throw new ArgumentNullException(nameof(pane));
        _pollTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(300),
        };
        _pollTimer.Tick += (_, _) => PollSessions();
    }

    public event Action? SessionsChanged;
    public event Action<string?>? SelectionChanged;

    public IReadOnlyList<MtcDockedSessionInfo> GetSessions() =>
        _sessions.Values
            .OrderBy(session => session.GameName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(session => session.AgentId, StringComparer.OrdinalIgnoreCase)
            .Select(session => new MtcDockedSessionInfo(
                session.AgentId,
                session.GameName,
                session.ProcessId,
                session.IsDocked,
                session.WindowHandle != IntPtr.Zero,
                session.Error))
            .ToArray();

    public string? ActiveAgentId => _activeAgentId;

    public void Register(string agentId, string gameName, int processId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(agentId);
        ArgumentException.ThrowIfNullOrWhiteSpace(gameName);
        if (processId <= 0)
            throw new ArgumentOutOfRangeException(nameof(processId));

        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(() => Register(agentId, gameName, processId));
            return;
        }

        ObjectDisposedException.ThrowIf(_disposed, this);
        var session = new SessionWindow(agentId, gameName, processId);
        _sessions[agentId] = session;
        _pollTimer.Start();
        SessionsChanged?.Invoke();
        if (!OperatingSystem.IsWindows())
        {
            session.Error = "Docked MTC sessions are supported on Windows only.";
            SessionsChanged?.Invoke();
            return;
        }

        Activate(agentId);
    }

    public void ActivateLocal()
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(ActivateLocal);
            return;
        }

        _activeAgentId = null;
        HideInactiveWindows();
        SelectionChanged?.Invoke(null);
    }

    public void HideAll()
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(HideAll);
            return;
        }

        _activeAgentId = null;
        HideInactiveWindows();
    }

    public void Activate(string agentId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(agentId);
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(() => Activate(agentId));
            return;
        }

        if (!_sessions.TryGetValue(agentId, out SessionWindow? session))
            throw new InvalidOperationException($"MTC session '{agentId}' is not registered.");

        _activeAgentId = session.AgentId;
        if (session.WindowHandle != IntPtr.Zero && !session.IsDocked)
        {
            TryDock(session);
            if (session.Error is not null)
            {
                _activeAgentId = null;
                ShowWindow(session.WindowHandle, SwShow);
                SelectionChanged?.Invoke(null);
                return;
            }
        }
        ShowActiveWindow();
        SelectionChanged?.Invoke(session.AgentId);
    }

    public void UndockActive()
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(UndockActive);
            return;
        }

        if (_activeAgentId is not null &&
            _sessions.TryGetValue(_activeAgentId, out SessionWindow? session))
        {
            try
            {
                RestoreTopLevelWindow(session);
                session.Error = null;
            }
            catch (Win32Exception exception)
            {
                session.Error = exception.Message;
                TWXProxy.Core.GlobalModules.DebugLog(
                    $"[AgentSessions] could not undock '{session.AgentId}': {exception}\n");
                SessionsChanged?.Invoke();
                return;
            }
        }

        _activeAgentId = null;
        HideInactiveWindows();
        SelectionChanged?.Invoke(null);
        SessionsChanged?.Invoke();
    }

    public void RestoreAll()
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(RestoreAll);
            return;
        }

        foreach (SessionWindow session in _sessions.Values)
        {
            try
            {
                RestoreTopLevelWindow(session);
            }
            catch (Win32Exception exception)
            {
                TWXProxy.Core.GlobalModules.DebugLog(
                    $"[AgentSessions] could not restore '{session.AgentId}' as a separate window: {exception}\n");
            }
        }
        _activeAgentId = null;
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        RestoreAll();
        _pollTimer.Stop();
        _disposed = true;
    }

    private void PollSessions()
    {
        if (_disposed)
            return;

        if (!OperatingSystem.IsWindows())
        {
            foreach (SessionWindow session in _sessions.Values)
                session.Error = "Docked MTC sessions are supported on Windows only.";
            SessionsChanged?.Invoke();
            _pollTimer.Stop();
            return;
        }

        bool changed = false;
        foreach (SessionWindow session in _sessions.Values.ToArray())
        {
            if (!IsProcessRunning(session.ProcessId))
            {
                if (string.Equals(_activeAgentId, session.AgentId, StringComparison.OrdinalIgnoreCase))
                    _activeAgentId = null;
                _sessions.Remove(session.AgentId);
                changed = true;
                continue;
            }

            if (session.WindowHandle != IntPtr.Zero && !IsWindow(session.WindowHandle))
            {
                session.WindowHandle = IntPtr.Zero;
                session.IsDocked = false;
                session.StylesCaptured = false;
                session.BoundsCaptured = false;
                changed = true;
            }

            if (session.WindowHandle == IntPtr.Zero)
            {
                IntPtr windowHandle = FindMainWindow(session.ProcessId);
                if (windowHandle != IntPtr.Zero)
                {
                    session.WindowHandle = windowHandle;
                    if (string.Equals(_activeAgentId, session.AgentId, StringComparison.OrdinalIgnoreCase))
                    {
                        TryDock(session);
                        if (session.Error is not null)
                        {
                            _activeAgentId = null;
                            ShowWindow(session.WindowHandle, SwShow);
                        }
                    }
                    changed = true;
                }
            }
        }

        if (_activeAgentId is not null &&
            _sessions.TryGetValue(_activeAgentId, out SessionWindow? active) &&
            active.IsDocked)
        {
            try
            {
                PositionActiveWindow(active);
            }
            catch (Win32Exception exception)
            {
                active.Error = exception.Message;
                TWXProxy.Core.GlobalModules.DebugLog(
                    $"[AgentSessions] could not position '{active.AgentId}': {exception}\n");
                try
                {
                    RestoreTopLevelWindow(active);
                }
                catch (Win32Exception restoreException)
                {
                    active.Error = $"{exception.Message} {restoreException.Message}";
                    TWXProxy.Core.GlobalModules.DebugLog(
                        $"[AgentSessions] could not restore '{active.AgentId}': {restoreException}\n");
                }

                _activeAgentId = null;
                changed = true;
            }
        }
        else
        {
            HideInactiveWindows();
        }

        if (changed)
        {
            SessionsChanged?.Invoke();
            if (_activeAgentId is null)
                SelectionChanged?.Invoke(null);
        }

        if (_sessions.Count == 0)
            _pollTimer.Stop();
    }

    private void TryDock(SessionWindow session)
    {
        if (!OperatingSystem.IsWindows() || session.WindowHandle == IntPtr.Zero)
            return;

        IntPtr parentHandle = _owner.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
        if (parentHandle == IntPtr.Zero)
            return;

        try
        {
            session.Error = null;
            if (!GetWindowRect(session.WindowHandle, out NativeRect originalBounds))
            {
                throw new Win32Exception(
                    Marshal.GetLastPInvokeError(),
                    "Could not read the MTC window bounds before docking.");
            }

            session.OriginalBounds = originalBounds;
            session.BoundsCaptured = true;
            session.OriginalStyle = GetWindowLongPtrChecked(session.WindowHandle, GwlStyle);
            session.OriginalExtendedStyle =
                GetWindowLongPtrChecked(session.WindowHandle, GwlExStyle);
            session.StylesCaptured = true;
            SetWindowLongPtrChecked(
                session.WindowHandle,
                GwlStyle,
                new IntPtr((session.OriginalStyle.ToInt64() &
                    ~(WsPopup | WsCaption | WsThickFrame | WsSysMenu |
                      WsMinimizeBox | WsMaximizeBox)) | WsChild | WsClipSiblings));
            SetWindowLongPtrChecked(
                session.WindowHandle,
                GwlExStyle,
                new IntPtr((session.OriginalExtendedStyle.ToInt64() & ~WsExAppWindow) |
                    WsExToolWindow));
            Marshal.SetLastPInvokeError(0);
            IntPtr previousParent = SetParent(session.WindowHandle, parentHandle);
            int parentError = Marshal.GetLastPInvokeError();
            session.IsDocked = GetParent(session.WindowHandle) == parentHandle;
            if (!session.IsDocked || previousParent == IntPtr.Zero && parentError != 0)
                throw new Win32Exception(
                    parentError,
                    "Could not dock the MTC window.");

            PositionActiveWindow(session, frameChanged: true);
            SessionsChanged?.Invoke();
        }
        catch (Win32Exception exception)
        {
            session.Error = exception.Message;
            TWXProxy.Core.GlobalModules.DebugLog(
                $"[AgentSessions] could not dock '{session.AgentId}': {exception}\n");
            try
            {
                RestoreTopLevelWindow(session);
            }
            catch (Win32Exception restoreException)
            {
                session.Error = $"{exception.Message} {restoreException.Message}";
                TWXProxy.Core.GlobalModules.DebugLog(
                    $"[AgentSessions] could not restore '{session.AgentId}': {restoreException}\n");
            }
            SessionsChanged?.Invoke();
        }
    }

    private void RestoreTopLevelWindow(SessionWindow session)
    {
        if (!OperatingSystem.IsWindows() || session.WindowHandle == IntPtr.Zero ||
            !session.StylesCaptured)
        {
            return;
        }

        Marshal.SetLastPInvokeError(0);
        IntPtr previousParent = SetParent(session.WindowHandle, IntPtr.Zero);
        int parentError = Marshal.GetLastPInvokeError();
        if (previousParent == IntPtr.Zero && parentError != 0)
            throw new Win32Exception(parentError, "Could not detach the MTC window from its host.");
        SetWindowLongPtrChecked(session.WindowHandle, GwlStyle, session.OriginalStyle);
        SetWindowLongPtrChecked(
            session.WindowHandle,
            GwlExStyle,
            session.OriginalExtendedStyle);
        EnsureWindowPosition(
            session.WindowHandle,
            session.BoundsCaptured ? session.OriginalBounds.Left : 100,
            session.BoundsCaptured ? session.OriginalBounds.Top : 100,
            session.BoundsCaptured
                ? Math.Max(1, session.OriginalBounds.Right - session.OriginalBounds.Left)
                : 1000,
            session.BoundsCaptured
                ? Math.Max(1, session.OriginalBounds.Bottom - session.OriginalBounds.Top)
                : 700,
            SwpNoZOrder | SwpFrameChanged | SwpShowWindow);
        ShowWindow(session.WindowHandle, SwShow);
        session.IsDocked = false;
        session.StylesCaptured = false;
        session.BoundsCaptured = false;
    }

    private void ShowActiveWindow()
    {
        HideInactiveWindows();
        if (_activeAgentId is null ||
            !_sessions.TryGetValue(_activeAgentId, out SessionWindow? session))
        {
            return;
        }

        if (session.WindowHandle != IntPtr.Zero && session.IsDocked)
        {
            PositionActiveWindow(session);
            ShowWindow(session.WindowHandle, SwShowNoActivate);
        }
    }

    private void HideInactiveWindows()
    {
        foreach (SessionWindow session in _sessions.Values)
        {
            if (session.WindowHandle != IntPtr.Zero && session.IsDocked &&
                !string.Equals(_activeAgentId, session.AgentId, StringComparison.OrdinalIgnoreCase))
            {
                ShowWindow(session.WindowHandle, SwHide);
            }
        }
    }

    private void PositionActiveWindow(SessionWindow session, bool frameChanged = false)
    {
        if (session.WindowHandle == IntPtr.Zero || !session.IsDocked ||
            _owner.TryGetPlatformHandle()?.Handle is not { } parentHandle ||
            parentHandle == IntPtr.Zero)
        {
            return;
        }

        PixelPoint topLeft = _pane.PointToScreen(new Point(0, 0));
        PixelPoint bottomRight = _pane.PointToScreen(new Point(_pane.Bounds.Width, _pane.Bounds.Height));
        var clientTopLeft = new NativePoint(topLeft.X, topLeft.Y);
        var clientBottomRight = new NativePoint(bottomRight.X, bottomRight.Y);
        if (!ScreenToClient(parentHandle, ref clientTopLeft) ||
            !ScreenToClient(parentHandle, ref clientBottomRight))
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError(), "Could not position the docked MTC window.");
        }

        EnsureWindowPosition(
            session.WindowHandle,
            clientTopLeft.X,
            clientTopLeft.Y,
            Math.Max(1, clientBottomRight.X - clientTopLeft.X),
            Math.Max(1, clientBottomRight.Y - clientTopLeft.Y),
            SwpShowWindow | (frameChanged ? SwpFrameChanged : 0));
    }

    private static IntPtr GetWindowLongPtrChecked(IntPtr windowHandle, int index)
    {
        Marshal.SetLastPInvokeError(0);
        IntPtr value = GetWindowLongPtr(windowHandle, index);
        int error = Marshal.GetLastPInvokeError();
        if (value == IntPtr.Zero && error != 0)
            throw new Win32Exception(error, "Could not read MTC window styles.");
        return value;
    }

    private static void SetWindowLongPtrChecked(IntPtr windowHandle, int index, IntPtr value)
    {
        Marshal.SetLastPInvokeError(0);
        IntPtr previous = SetWindowLongPtrNative(windowHandle, index, value);
        int error = Marshal.GetLastPInvokeError();
        if (previous == IntPtr.Zero && error != 0)
            throw new Win32Exception(error, "Could not update MTC window styles.");
    }

    private static void EnsureWindowPosition(
        IntPtr windowHandle,
        int x,
        int y,
        int width,
        int height,
        uint flags)
    {
        if (!SetWindowPos(windowHandle, IntPtr.Zero, x, y, width, height, flags))
            throw new Win32Exception(Marshal.GetLastPInvokeError(), "Could not position the MTC window.");
    }

    private static bool IsProcessRunning(int processId)
    {
        try
        {
            using Process process = Process.GetProcessById(processId);
            return !process.HasExited;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static IntPtr FindMainWindow(int processId)
    {
        IntPtr selected = IntPtr.Zero;
        long selectedArea = 0;
        EnumWindows((windowHandle, _) =>
        {
            GetWindowThreadProcessId(windowHandle, out uint windowProcessId);
            if (windowProcessId != processId || !IsWindowVisible(windowHandle) ||
                GetWindow(windowHandle, GwOwner) != IntPtr.Zero ||
                !GetWindowRect(windowHandle, out NativeRect rectangle))
            {
                return true;
            }

            long width = Math.Max(0, rectangle.Right - rectangle.Left);
            long height = Math.Max(0, rectangle.Bottom - rectangle.Top);
            long area = width * height;
            if (area > selectedArea)
            {
                selected = windowHandle;
                selectedArea = area;
            }

            return true;
        }, IntPtr.Zero);
        return selected;
    }

    private sealed class SessionWindow(string agentId, string gameName, int processId)
    {
        public string AgentId { get; } = agentId;
        public string GameName { get; } = gameName;
        public int ProcessId { get; } = processId;
        public IntPtr WindowHandle { get; set; }
        public IntPtr OriginalStyle { get; set; }
        public IntPtr OriginalExtendedStyle { get; set; }
        public NativeRect OriginalBounds { get; set; }
        public bool IsDocked { get; set; }
        public bool StylesCaptured { get; set; }
        public bool BoundsCaptured { get; set; }
        public string? Error { get; set; }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint(int x, int y)
    {
        public int X = x;
        public int Y = y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    private delegate bool EnumWindowsCallback(IntPtr windowHandle, IntPtr parameter);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumWindows(EnumWindowsCallback callback, IntPtr parameter);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetWindowThreadProcessId(IntPtr windowHandle, out uint processId);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(IntPtr windowHandle);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindow(IntPtr windowHandle);

    [DllImport("user32.dll")]
    private static extern IntPtr GetWindow(IntPtr windowHandle, uint command);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr windowHandle, out NativeRect rectangle);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetParent(IntPtr childHandle, IntPtr newParentHandle);

    [DllImport("user32.dll")]
    private static extern IntPtr GetParent(IntPtr windowHandle);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
    private static extern IntPtr GetWindowLongPtr(IntPtr windowHandle, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    private static extern IntPtr SetWindowLongPtrNative(
        IntPtr windowHandle,
        int index,
        IntPtr value);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(
        IntPtr windowHandle,
        IntPtr insertAfter,
        int x,
        int y,
        int width,
        int height,
        uint flags);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(IntPtr windowHandle, uint command);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ScreenToClient(IntPtr windowHandle, ref NativePoint point);
}
