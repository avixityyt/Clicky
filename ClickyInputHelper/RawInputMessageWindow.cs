namespace ClickyInputHelper;

using System;
using System.Collections.Concurrent;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Windows.Forms;

internal sealed class RawInputMessageWindow : NativeWindow, IDisposable
{
    private const int RIM_TYPEMOUSE = 0;
    private const int WM_INPUT = 0x00FF;
    private const int RID_INPUT = 0x10000003;
    private const uint RIDI_DEVICENAME = 0x20000007;
    private const uint RIDI_DEVICEINFO = 0x2000000B;
    private const uint RIDEV_INPUTSINK = 0x00000100;
    private const ushort HID_USAGE_PAGE_GENERIC = 0x01;
    private const ushort HID_USAGE_GENERIC_MOUSE = 0x02;
    private const ushort RI_MOUSE_LEFT_BUTTON_DOWN = 0x0001;
    private const ushort RI_MOUSE_RIGHT_BUTTON_DOWN = 0x0004;
    private const ushort RI_MOUSE_MIDDLE_BUTTON_DOWN = 0x0010;

    private static readonly Regex VidPidPattern = new(
        @"(?:DEV_)?VID(?:_|&)([0-9A-F]{4,6}).*PID(?:_|&)([0-9A-F]{4})",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private readonly ClickyBridgeClient _bridgeClient;
    private readonly PointerDeviceMatcher _deviceMatcher;
    private readonly ConcurrentDictionary<nint, RawDeviceDescriptor> _descriptorCache = new();
    private bool _disposed;

    public RawInputMessageWindow(ClickyBridgeClient bridgeClient, PointerDeviceMatcher deviceMatcher)
    {
        this._bridgeClient = bridgeClient;
        this._deviceMatcher = deviceMatcher;

        this.CreateHandle(new CreateParams
        {
            Caption = "ClickyInputHelperWindow",
            X = -32000,
            Y = -32000,
            Width = 1,
            Height = 1,
            Style = unchecked((int)0x80000000),
        });

        RegisterForRawMouseInput(this.Handle);
    }

    public void Dispose()
    {
        if (this._disposed)
        {
            return;
        }

        this._disposed = true;
        this.DestroyHandle();
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WM_INPUT)
        {
            this.ProcessRawInput(m.LParam);
        }

        base.WndProc(ref m);
    }

    private void ProcessRawInput(nint rawInputHandle)
    {
        var dataSize = 0u;
        var headerSize = (uint)Marshal.SizeOf<RAWINPUTHEADER>();
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

            var rawInput = Marshal.PtrToStructure<RAWINPUT>(buffer);
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
            var payload = new ClickyInputEventPayload
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
            };

            this._bridgeClient.SendInputEvent(payload);
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
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
            return new RawDeviceDescriptor
            {
                DevicePath = devicePath,
                DeviceLabel = $"External pointer ({vendorId}:{productId})",
                VendorId = vendorId,
                ProductId = productId,
                ConnectionType = "external",
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
            new RAWINPUTDEVICE
            {
                usUsagePage = HID_USAGE_PAGE_GENERIC,
                usUsage = HID_USAGE_GENERIC_MOUSE,
                dwFlags = RIDEV_INPUTSINK,
                hwndTarget = targetHandle,
            },
        };

        if (!RegisterRawInputDevices(devices, (uint)devices.Length, (uint)Marshal.SizeOf<RAWINPUTDEVICE>()))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "RegisterRawInputDevices failed.");
        }
    }

    [DllImport("User32.dll", SetLastError = true)]
    private static extern bool RegisterRawInputDevices(RAWINPUTDEVICE[] pRawInputDevices, uint uiNumDevices, uint cbSize);

    [DllImport("User32.dll", SetLastError = true)]
    private static extern uint GetRawInputData(nint hRawInput, uint uiCommand, nint pData, ref uint pcbSize, uint cbSizeHeader);

    [DllImport("User32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint GetRawInputDeviceInfo(nint hDevice, uint uiCommand, nint pData, ref uint pcbSize);

    [StructLayout(LayoutKind.Sequential)]
    private struct RAWINPUTDEVICE
    {
        public ushort usUsagePage;
        public ushort usUsage;
        public uint dwFlags;
        public nint hwndTarget;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RAWINPUTHEADER
    {
        public uint dwType;
        public uint dwSize;
        public nint hDevice;
        public nint wParam;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct RAWMOUSEBUTTONS
    {
        [FieldOffset(0)]
        public uint ulButtons;

        [FieldOffset(0)]
        public ushort usButtonFlags;

        [FieldOffset(2)]
        public ushort usButtonData;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RAWMOUSE
    {
        public ushort usFlags;
        public RAWMOUSEBUTTONS Anonymous;
        public uint ulRawButtons;
        public int lLastX;
        public int lLastY;
        public uint ulExtraInformation;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RAWINPUT
    {
        public RAWINPUTHEADER Header;
        public RAWMOUSE Mouse;
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
