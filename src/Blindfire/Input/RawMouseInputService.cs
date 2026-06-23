using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using Blindfire.Native;

namespace Blindfire.Input;

// Registers for Win32 raw mouse input and feeds every relative-mode WM_INPUT
// packet into a MouseDeltaAccumulator. This is the only source of truth used
// for sensitivity measurement - it is unaffected by Windows pointer
// acceleration and does not clamp at screen edges, unlike OS cursor position.
public sealed class RawMouseInputService : IDisposable
{
    private readonly MouseDeltaAccumulator _accumulator;
    private HwndSource? _hwndSource;
    private IntPtr _buffer = IntPtr.Zero;
    private int _bufferSize;

    public RawMouseInputService(MouseDeltaAccumulator accumulator)
    {
        _accumulator = accumulator;
    }

    public MouseDeltaAccumulator Accumulator => _accumulator;

    // Safe to call more than once, including re-targeting a window it was
    // previously attached to: removes the old hook before adding a new one,
    // and re-registers the raw input device against the new hwnd. Win32 raw
    // input registration for a given usage page/usage is process-wide, so a
    // second window registering silently redirects all future WM_INPUT
    // delivery to it - callers that need to hand input back and forth between
    // two windows (e.g. MainWindow and a secondary fullscreen window) must
    // call Attach again on the window they want input redirected back to.
    public void Attach(Window window)
    {
        _hwndSource?.RemoveHook(WndProc);

        var helper = new WindowInteropHelper(window);
        var hwnd = helper.EnsureHandle();

        _hwndSource = HwndSource.FromHwnd(hwnd);
        _hwndSource?.AddHook(WndProc);

        var device = new RAWINPUTDEVICE
        {
            usUsagePage = RawInputConstants.HID_USAGE_PAGE_GENERIC,
            usUsage = RawInputConstants.HID_USAGE_GENERIC_MOUSE,
            dwFlags = RawInputConstants.RIDEV_INPUTSINK,
            hwndTarget = hwnd,
        };

        var registered = RawInputNativeMethods.RegisterRawInputDevices(
            new[] { device }, 1, (uint)Marshal.SizeOf<RAWINPUTDEVICE>());

        if (!registered)
        {
            throw new InvalidOperationException("Failed to register for raw mouse input.");
        }
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg != RawInputConstants.WM_INPUT)
        {
            return IntPtr.Zero;
        }

        uint requiredSize = 0;
        RawInputNativeMethods.GetRawInputData(
            lParam, RawInputConstants.RID_INPUT, IntPtr.Zero, ref requiredSize, (uint)Marshal.SizeOf<RAWINPUTHEADER>());

        if (requiredSize == 0)
        {
            return IntPtr.Zero;
        }

        EnsureBufferCapacity((int)requiredSize);

        var size = requiredSize;
        var read = RawInputNativeMethods.GetRawInputData(
            lParam, RawInputConstants.RID_INPUT, _buffer, ref size, (uint)Marshal.SizeOf<RAWINPUTHEADER>());

        if (read != requiredSize)
        {
            return IntPtr.Zero;
        }

        var raw = Marshal.PtrToStructure<RAWINPUT>(_buffer);
        if (raw.header.dwType != RawInputConstants.RIM_TYPEMOUSE)
        {
            return IntPtr.Zero;
        }

        var isAbsolute = (raw.mouse.usFlags & RawInputConstants.MOUSE_MOVE_ABSOLUTE) != 0;
        if (isAbsolute)
        {
            _accumulator.NoteAbsoluteModePacketSkipped();
            return IntPtr.Zero;
        }

        _accumulator.Add(raw.mouse.lLastX, raw.mouse.lLastY);
        return IntPtr.Zero;
    }

    private void EnsureBufferCapacity(int requiredSize)
    {
        if (_buffer != IntPtr.Zero && _bufferSize >= requiredSize)
        {
            return;
        }

        if (_buffer != IntPtr.Zero)
        {
            Marshal.FreeHGlobal(_buffer);
        }

        _buffer = Marshal.AllocHGlobal(requiredSize);
        _bufferSize = requiredSize;
    }

    public void Dispose()
    {
        _hwndSource?.RemoveHook(WndProc);

        if (_buffer != IntPtr.Zero)
        {
            Marshal.FreeHGlobal(_buffer);
            _buffer = IntPtr.Zero;
        }
    }
}
