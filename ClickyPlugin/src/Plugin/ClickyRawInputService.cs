namespace Loupedeck.ClickyPlugin;

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading;

internal sealed class ClickyRawInputService : IDisposable
{
    private const int RIM_TYPEMOUSE = 0;
    private const int WM_CLOSE = 0x0010;
    private const int WM_DESTROY = 0x0002;
    private const int WM_INPUT = 0x00FF;
    private const uint WM_QUIT = 0x0012;
    private const int RID_INPUT = 0x10000003;
    private const uint RIDI_DEVICENAME = 0x20000007;
    private const uint RIDEV_INPUTSINK = 0x00000100;
    private const ushort HID_USAGE_PAGE_GENERIC = 0x01;
    private const ushort HID_USAGE_GENERIC_MOUSE = 0x02;
    private const ushort RI_MOUSE_LEFT_BUTTON_DOWN = 0x0001;
    private const ushort RI_MOUSE_RIGHT_BUTTON_DOWN = 0x0004;
    private const ushort RI_MOUSE_MIDDLE_BUTTON_DOWN = 0x0010;
    private const int WsPopup = unchecked((int)0x80000000);
    private static readonly Regex VidPidPattern = new(
        @"(?:DEV_)?VID(?:_|&)([0-9A-F]{4,6}).*PID(?:_|&)([0-9A-F]{4})",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Dictionary<nint, ClickyRawInputService> WindowInstances = new();
    private static WndProc? _wndProc;

    private readonly object _sync = new();
    private readonly ManualResetEventSlim _startupCompleted = new(false);
    private readonly ClickyPointerDeviceMatcher _deviceMatcher = new(ClickySupportedPointerDevicesConfig.CreateDefault());
    private readonly ConcurrentDictionary<nint, RawDeviceDescriptor> _descriptorCache = new();
    private Thread? _thread;
    private Exception? _startupException;
    private nint _windowHandle;
    private nint _previousWindowProc;
    private uint _threadId;
    private bool _isActive;
    private bool _disposed;

    public event EventHandler<ClickyInputEventRecord>? InputEventReceived;

    public bool IsActive
    {
        get
        {
            lock (this._sync)
            {
                return this._isActive;
            }
        }
    }

    public void Start()
    {
        if (!OperatingSystem.IsWindows())
        {
            Logger.Warning("Raw input capture is only supported on Windows.");
            return;
        }

        lock (this._sync)
        {
            ObjectDisposedException.ThrowIf(this._disposed, this);
            if (this._thread != null)
            {
                return;
            }

            this._startupException = null;
            this._startupCompleted.Reset();
            this._thread = new Thread(this.ThreadMain)
            {
                IsBackground = true,
                Name = "Clicky Raw Input",
            };
            this._thread.SetApartmentState(ApartmentState.STA);
            this._thread.Start();
        }

        if (!this._startupCompleted.Wait(TimeSpan.FromSeconds(5)))
        {
            throw new TimeoutException("Timed out while starting Clicky raw input.");
        }

        if (this._startupException != null)
        {
            throw new InvalidOperationException("Failed to start Clicky raw input.", this._startupException);
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
        Thread? thread;
        nint windowHandle;
        uint threadId;

        lock (this._sync)
        {
            thread = this._thread;
            windowHandle = this._windowHandle;
            threadId = this._threadId;
        }

        if (windowHandle != nint.Zero)
        {
            PostMessage(windowHandle, WM_CLOSE, nint.Zero, nint.Zero);
        }

        if (threadId != 0)
        {
            PostThreadMessage(threadId, WM_QUIT, UIntPtr.Zero, nint.Zero);
        }

        if (thread != null && thread.IsAlive)
        {
            thread.Join(TimeSpan.FromSeconds(2));
        }

        lock (this._sync)
        {
            this._thread = null;
            this._threadId = 0;
            this._windowHandle = nint.Zero;
            this._startupException = null;
            this._isActive = false;
        }
    }

    private void ThreadMain()
    {
        this._threadId = GetCurrentThreadId();

        try
        {
            this._windowHandle = CreateWindowEx(
                0,
                "STATIC",
                "ClickyRawInputWindow",
                WsPopup,
                -32000,
                -32000,
                1,
                1,
                nint.Zero,
                nint.Zero,
                GetModuleHandle(null),
                nint.Zero);
            if (this._windowHandle == nint.Zero)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "CreateWindowEx failed for Clicky raw input.");
            }

            lock (WindowInstances)
            {
                WindowInstances[this._windowHandle] = this;
            }

            _wndProc ??= WindowProcedure;
            this._previousWindowProc = SetWindowLongPtr(this._windowHandle, GwlpWndproc, Marshal.GetFunctionPointerForDelegate(_wndProc));
            if (this._previousWindowProc == nint.Zero)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "SetWindowLongPtr failed for Clicky raw input.");
            }

            RegisterForRawMouseInput(this._windowHandle);
            lock (this._sync)
            {
                this._isActive = true;
            }

            Logger.Info("Clicky raw input started.");
        }
        catch (Exception ex)
        {
            this._startupException = ex;
            Logger.Error(ex, "Unable to start Clicky raw input.");
            this._startupCompleted.Set();
            return;
        }

        this._startupCompleted.Set();

        try
        {
            while (GetMessage(out var message, nint.Zero, 0, 0) > 0)
            {
                TranslateMessage(ref message);
                DispatchMessage(ref message);
            }
        }
        finally
        {
            if (this._windowHandle != nint.Zero)
            {
                lock (WindowInstances)
                {
                    WindowInstances.Remove(this._windowHandle);
                }

                DestroyWindow(this._windowHandle);
                this._windowHandle = nint.Zero;
            }

            lock (this._sync)
            {
                this._isActive = false;
            }

            Logger.Info("Clicky raw input stopped.");
        }
    }

    private static nint WindowProcedure(nint hwnd, uint message, nint wParam, nint lParam)
    {
        ClickyRawInputService? service;
        lock (WindowInstances)
        {
            WindowInstances.TryGetValue(hwnd, out service);
        }

        switch (message)
        {
            case WM_INPUT when service != null:
                service.ProcessRawInput(lParam);
                break;
            case WM_CLOSE:
                DestroyWindow(hwnd);
                return nint.Zero;
            case WM_DESTROY:
                PostQuitMessage(0);
                return nint.Zero;
        }

        if (service != null && service._previousWindowProc != nint.Zero)
        {
            return CallWindowProc(service._previousWindowProc, hwnd, message, wParam, lParam);
        }

        return DefWindowProc(hwnd, message, wParam, lParam);
    }

    private void ProcessRawInput(nint rawInputHandle)
    {
        var dataSize = 0u;
        var headerSize = (uint)Marshal.SizeOf<RawInputHeader>();
        if (GetRawInputData(rawInputHandle, RID_INPUT, nint.Zero, ref dataSize, headerSize) != 0 || dataSize == 0)
        {
            return;
        }

        var buffer = Marshal.AllocHGlobal((int)dataSize);
        try
        {
            if (GetRawInputData(rawInputHandle, RID_INPUT, buffer, ref dataSize, headerSize) != dataSize)
            {
                return;
            }

            var rawInput = Marshal.PtrToStructure<RawInput>(buffer);
            if (rawInput.Header.dwType != RIM_TYPEMOUSE)
            {
                return;
            }

            var button = MapButton(rawInput.Mouse.Anonymous.usButtonFlags);
            if (button == null)
            {
                return;
            }

            var descriptor = this._descriptorCache.GetOrAdd(rawInput.Header.hDevice, this.DescribeDevice);
            var record = new ClickyInputEventRecord
            {
                OccurredAtUtc = DateTimeOffset.UtcNow,
                Button = button,
                DeviceLabel = descriptor.DeviceLabel,
                DevicePath = descriptor.DevicePath,
                VendorId = descriptor.VendorId,
                ProductId = descriptor.ProductId,
                ConnectionType = descriptor.ConnectionType,
                IsMxMaster4 = descriptor.IsMxMaster4,
                AllowedForHaptics = descriptor.IsMxMaster4,
                Source = "plugin",
            };

            this.DispatchInputEvent(record);
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private void DispatchInputEvent(ClickyInputEventRecord record)
    {
        var handlers = this.InputEventReceived;
        if (handlers == null)
        {
            return;
        }

        ThreadPool.QueueUserWorkItem(
            static state =>
            {
                var dispatch = (InputEventDispatch)state!;
                try
                {
                    dispatch.Service.InputEventReceived?.Invoke(dispatch.Service, dispatch.Record);
                }
                catch (Exception ex)
                {
                    Logger.Error(ex, "Raw input subscriber threw an exception.");
                }
            },
            new InputEventDispatch(this, record));
    }

    private RawDeviceDescriptor DescribeDevice(nint deviceHandle)
    {
        var devicePath = GetDevicePath(deviceHandle);
        var (vendorId, productId) = ParseVidPid(devicePath);
        var supportedMatch = this._deviceMatcher.Match(devicePath, vendorId, productId);
        if (supportedMatch != null)
        {
            return new RawDeviceDescriptor
            {
                DevicePath = devicePath,
                DeviceLabel = supportedMatch.Label,
                VendorId = supportedMatch.VendorId,
                ProductId = supportedMatch.ProductId,
                ConnectionType = supportedMatch.ConnectionType,
                IsMxMaster4 = string.Equals(supportedMatch.Name, "mx_master_4", StringComparison.OrdinalIgnoreCase),
            };
        }

        return BuildFallbackDescriptor(devicePath, vendorId, productId);
    }

    private static string? MapButton(ushort buttonFlags)
    {
        if ((buttonFlags & RI_MOUSE_LEFT_BUTTON_DOWN) != 0)
        {
            return "left";
        }

        if ((buttonFlags & RI_MOUSE_RIGHT_BUTTON_DOWN) != 0)
        {
            return "right";
        }

        if ((buttonFlags & RI_MOUSE_MIDDLE_BUTTON_DOWN) != 0)
        {
            return "middle";
        }

        return null;
    }

    private static string GetDevicePath(nint deviceHandle)
    {
        var size = 0u;
        _ = GetRawInputDeviceInfo(deviceHandle, RIDI_DEVICENAME, nint.Zero, ref size);
        if (size == 0)
        {
            return "Unresolved device path";
        }

        var buffer = Marshal.AllocHGlobal((int)(size * sizeof(char)));
        try
        {
            var result = GetRawInputDeviceInfo(deviceHandle, RIDI_DEVICENAME, buffer, ref size);
            if (result == unchecked((uint)-1))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "GetRawInputDeviceInfo(RIDI_DEVICENAME) failed.");
            }

            return Marshal.PtrToStringUni(buffer) ?? "Unresolved device path";
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static (string VendorId, string ProductId) ParseVidPid(string devicePath)
    {
        var match = VidPidPattern.Match(devicePath);
        if (!match.Success)
        {
            return (string.Empty, string.Empty);
        }

        var vendorId = match.Groups[1].Value.ToUpperInvariant();
        if (vendorId.Length > 4)
        {
            vendorId = vendorId[^4..];
        }

        return (vendorId, match.Groups[2].Value.ToUpperInvariant());
    }

    private static RawDeviceDescriptor BuildFallbackDescriptor(string devicePath, string vendorId, string productId)
    {
        if (string.Equals(devicePath, "Unresolved device path", StringComparison.OrdinalIgnoreCase))
        {
            return new RawDeviceDescriptor
            {
                DevicePath = devicePath,
                DeviceLabel = "Built-in touchpad or synthetic pointer",
                VendorId = vendorId,
                ProductId = productId,
                ConnectionType = "synthetic",
                IsMxMaster4 = false,
            };
        }

        if (devicePath.Contains("I2C", StringComparison.OrdinalIgnoreCase)
            || devicePath.Contains("ACPI", StringComparison.OrdinalIgnoreCase)
            || devicePath.Contains("ELAN", StringComparison.OrdinalIgnoreCase)
            || devicePath.Contains("SYN", StringComparison.OrdinalIgnoreCase))
        {
            return new RawDeviceDescriptor
            {
                DevicePath = devicePath,
                DeviceLabel = "Built-in touchpad or internal pointer",
                VendorId = vendorId,
                ProductId = productId,
                ConnectionType = "internal",
                IsMxMaster4 = false,
            };
        }

        if (!string.IsNullOrWhiteSpace(vendorId) || !string.IsNullOrWhiteSpace(productId))
        {
            var isSharedLogitechReceiver = string.Equals(vendorId, "046D", StringComparison.OrdinalIgnoreCase)
                && string.Equals(productId, "C548", StringComparison.OrdinalIgnoreCase);
            return new RawDeviceDescriptor
            {
                DevicePath = devicePath,
                DeviceLabel = isSharedLogitechReceiver
                    ? "Logitech receiver pointer (shared)"
                    : $"External pointer ({vendorId}:{productId})",
                VendorId = vendorId,
                ProductId = productId,
                ConnectionType = isSharedLogitechReceiver ? "receiver-shared" : "external",
                IsMxMaster4 = false,
            };
        }

        return new RawDeviceDescriptor
        {
            DevicePath = devicePath,
            DeviceLabel = "Unidentified pointer device",
            VendorId = vendorId,
            ProductId = productId,
            ConnectionType = "unknown",
            IsMxMaster4 = false,
        };
    }

    private static void RegisterForRawMouseInput(nint targetHandle)
    {
        var devices = new[]
        {
            new RawInputDevice
            {
                usUsagePage = HID_USAGE_PAGE_GENERIC,
                usUsage = HID_USAGE_GENERIC_MOUSE,
                dwFlags = RIDEV_INPUTSINK,
                hwndTarget = targetHandle,
            },
        };

        if (!RegisterRawInputDevices(devices, (uint)devices.Length, (uint)Marshal.SizeOf<RawInputDevice>()))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "RegisterRawInputDevices failed.");
        }
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint CreateWindowEx(
        int dwExStyle,
        string lpClassName,
        string lpWindowName,
        int dwStyle,
        int x,
        int y,
        int nWidth,
        int nHeight,
        nint hWndParent,
        nint hMenu,
        nint hInstance,
        nint lpParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyWindow(nint hWnd);

    [DllImport("user32.dll")]
    private static extern nint DefWindowProc(nint hWnd, uint msg, nint wParam, nint lParam);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    private static extern nint SetWindowLongPtr(nint hWnd, int nIndex, nint dwNewLong);

    [DllImport("user32.dll", EntryPoint = "CallWindowProcW")]
    private static extern nint CallWindowProc(nint lpPrevWndFunc, nint hWnd, uint msg, nint wParam, nint lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int GetMessage(out Message lpMsg, nint hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

    [DllImport("user32.dll")]
    private static extern bool TranslateMessage([In] ref Message lpMsg);

    [DllImport("user32.dll")]
    private static extern nint DispatchMessage([In] ref Message lpmsg);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterRawInputDevices(RawInputDevice[] pRawInputDevices, uint uiNumDevices, uint cbSize);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetRawInputData(nint hRawInput, int uiCommand, nint pData, ref uint pcbSize, uint cbSizeHeader);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint GetRawInputDeviceInfo(nint hDevice, uint uiCommand, nint pData, ref uint pcbSize);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool PostThreadMessage(uint idThread, uint msg, UIntPtr wParam, nint lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool PostMessage(nint hWnd, int msg, nint wParam, nint lParam);

    [DllImport("user32.dll")]
    private static extern void PostQuitMessage(int nExitCode);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint GetModuleHandle(string? lpModuleName);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    private delegate nint WndProc(nint hWnd, uint msg, nint wParam, nint lParam);

    private const int GwlpWndproc = -4;

    [StructLayout(LayoutKind.Sequential)]
    private struct Message
    {
        public nint HWnd;
        public uint MessageId;
        public UIntPtr WParam;
        public nint LParam;
        public uint Time;
        public Point Point;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Point
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RawInputDevice
    {
        public ushort usUsagePage;
        public ushort usUsage;
        public uint dwFlags;
        public nint hwndTarget;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RawInputHeader
    {
        public uint dwType;
        public uint dwSize;
        public nint hDevice;
        public nint wParam;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct RawMouseButtons
    {
        [FieldOffset(0)]
        public uint ulButtons;

        [FieldOffset(0)]
        public ushort usButtonFlags;

        [FieldOffset(2)]
        public ushort usButtonData;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RawMouse
    {
        public ushort usFlags;
        public RawMouseButtons Anonymous;
        public uint ulRawButtons;
        public int lLastX;
        public int lLastY;
        public uint ulExtraInformation;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RawInput
    {
        public RawInputHeader Header;
        public RawMouse Mouse;
    }

    private sealed class InputEventDispatch
    {
        public InputEventDispatch(ClickyRawInputService service, ClickyInputEventRecord record)
        {
            this.Service = service;
            this.Record = record;
        }

        public ClickyRawInputService Service { get; }

        public ClickyInputEventRecord Record { get; }
    }

    private sealed class RawDeviceDescriptor
    {
        public string DeviceLabel { get; init; } = string.Empty;

        public string DevicePath { get; init; } = string.Empty;

        public string VendorId { get; init; } = string.Empty;

        public string ProductId { get; init; } = string.Empty;

        public string ConnectionType { get; init; } = string.Empty;

        public bool IsMxMaster4 { get; init; }
    }
}
