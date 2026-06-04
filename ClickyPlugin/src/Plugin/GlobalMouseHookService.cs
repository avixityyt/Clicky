namespace Loupedeck.ClickyPlugin;

using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;

public sealed class GlobalMouseHookService : IDisposable
{
    private const int WhMouseLl = 14;
    private const int WmLButtonDown = 0x0201;
    private const int WmRButtonDown = 0x0204;
    private const int WmMButtonDown = 0x0207;
    private const uint WmQuit = 0x0012;

    private readonly object _sync = new();
    private readonly ManualResetEventSlim _startupCompleted = new(false);
    private HookProc? _hookProc;
    private Thread? _hookThread;
    private IntPtr _hookHandle = IntPtr.Zero;
    private Exception? _startupException;
    private uint _hookThreadId;
    private bool _hookActive;
    private bool _disposed;

    public event EventHandler<GlobalMouseClickEventArgs>? MouseClicked;

    public bool IsActive
    {
        get
        {
            lock (this._sync)
            {
                return this._hookActive;
            }
        }
    }

    public void Start()
    {
        if (!OperatingSystem.IsWindows())
        {
            Logger.Warning("Global mouse hook is only supported on Windows.");
            return;
        }

        lock (this._sync)
        {
            ObjectDisposedException.ThrowIf(this._disposed, this);

            if (this._hookThread != null)
            {
                return;
            }

            this._startupException = null;
            this._startupCompleted.Reset();
            this._hookThread = new Thread(this.HookThreadMain)
            {
                IsBackground = true,
                Name = "Clicky Global Mouse Hook",
            };
            // Run the low-level hook on its own message thread.
            this._hookThread.Start();
        }

        if (!this._startupCompleted.Wait(TimeSpan.FromSeconds(5)))
        {
            throw new TimeoutException("Timed out while starting the global mouse hook.");
        }

        if (this._startupException != null)
        {
            throw new InvalidOperationException("Failed to start the global mouse hook.", this._startupException);
        }
    }

    public void Dispose()
    {
        lock (this._sync)
        {
            if (this._disposed)
            {
                return;
            }

            this._disposed = true;
        }

        this.Stop();
    }

    private void Stop()
    {
        Thread? hookThread;
        uint hookThreadId;

        lock (this._sync)
        {
            hookThread = this._hookThread;
            hookThreadId = this._hookThreadId;
        }

        if (hookThreadId != 0)
        {
            PostThreadMessage(hookThreadId, WmQuit, UIntPtr.Zero, IntPtr.Zero);
        }

        if (hookThread != null && hookThread.IsAlive)
        {
            hookThread.Join(TimeSpan.FromSeconds(2));
        }

        lock (this._sync)
        {
            this._hookThread = null;
            this._hookThreadId = 0;
            this._hookProc = null;
            this._startupException = null;
            this._hookActive = false;
        }
    }

    private void HookThreadMain()
    {
        this._hookThreadId = GetCurrentThreadId();
        this._hookProc = this.HookCallback;

        try
        {
            this._hookHandle = this.InstallHook(this._hookProc);
            lock (this._sync)
            {
                this._hookActive = this._hookHandle != IntPtr.Zero;
            }

            Logger.Info("Global mouse hook started.");
        }
        catch (Exception ex)
        {
            this._startupException = ex;
            Logger.Error(ex, "Unable to start the global mouse hook.");
            this._startupCompleted.Set();
            return;
        }

        this._startupCompleted.Set();

        try
        {
            while (GetMessage(out var message, IntPtr.Zero, 0, 0) > 0)
            {
                TranslateMessage(ref message);
                DispatchMessage(ref message);
            }
        }
        finally
        {
            if (this._hookHandle != IntPtr.Zero)
            {
                if (!UnhookWindowsHookEx(this._hookHandle))
                {
                    Logger.Warning(new Win32Exception(Marshal.GetLastWin32Error()), "UnhookWindowsHookEx returned false.");
                }

                this._hookHandle = IntPtr.Zero;
            }

            lock (this._sync)
            {
                this._hookActive = false;
            }

            Logger.Info("Global mouse hook stopped.");
        }
    }

    private IntPtr InstallHook(HookProc proc)
    {
        using var currentProcess = Process.GetCurrentProcess();
        using var currentModule = currentProcess.MainModule ?? throw new InvalidOperationException("Failed to locate the current process module.");

        var moduleHandle = GetModuleHandle(currentModule.ModuleName);
        var hookHandle = SetWindowsHookEx(WhMouseLl, proc, moduleHandle, 0);

        if (hookHandle == IntPtr.Zero)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "SetWindowsHookEx failed.");
        }

        return hookHandle;
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && TryMapButton(wParam, out var button))
        {
            var handlers = this.MouseClicked;
            if (handlers != null)
            {
                var args = new GlobalMouseClickEventArgs(button);
                // Hand the event off quickly so the hook callback stays lightweight.
                ThreadPool.QueueUserWorkItem(
                    static state =>
                    {
                        var dispatch = (MouseClickDispatch)state!;
                        dispatch.Service.RaiseMouseClicked(dispatch.Args);
                    },
                    new MouseClickDispatch(this, args));
            }
        }

        return CallNextHookEx(IntPtr.Zero, nCode, wParam, lParam);
    }

    private void RaiseMouseClicked(GlobalMouseClickEventArgs args)
    {
        try
        {
            this.MouseClicked?.Invoke(this, args);
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Mouse click subscriber threw an exception.");
        }
    }

    private static bool TryMapButton(IntPtr wParam, out GlobalMouseButton button)
    {
        switch (wParam.ToInt32())
        {
            case WmLButtonDown:
                button = GlobalMouseButton.Left;
                return true;
            case WmRButtonDown:
                button = GlobalMouseButton.Right;
                return true;
            case WmMButtonDown:
                button = GlobalMouseButton.Middle;
                return true;
            default:
                button = default;
                return false;
        }
    }

    private sealed class MouseClickDispatch
    {
        public MouseClickDispatch(GlobalMouseHookService service, GlobalMouseClickEventArgs args)
        {
            this.Service = service;
            this.Args = args;
        }

        public GlobalMouseHookService Service { get; }

        public GlobalMouseClickEventArgs Args { get; }
    }

    private delegate IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct Point
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Message
    {
        public IntPtr HWnd;
        public uint MessageId;
        public UIntPtr WParam;
        public IntPtr LParam;
        public uint Time;
        public Point Point;
    }

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, HookProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern sbyte GetMessage(out Message lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

    [DllImport("user32.dll")]
    private static extern bool TranslateMessage([In] ref Message lpMsg);

    [DllImport("user32.dll")]
    private static extern IntPtr DispatchMessage([In] ref Message lpMsg);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostThreadMessage(uint idThread, uint msg, UIntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr GetModuleHandle(string lpModuleName);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();
}
