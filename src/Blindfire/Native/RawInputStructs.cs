using System;
using System.Runtime.InteropServices;

namespace Blindfire.Native;

[StructLayout(LayoutKind.Sequential)]
internal struct RAWINPUTDEVICE
{
    public ushort usUsagePage;
    public ushort usUsage;
    public uint dwFlags;
    public IntPtr hwndTarget;
}

[StructLayout(LayoutKind.Sequential)]
internal struct RAWINPUTHEADER
{
    public uint dwType;
    public uint dwSize;
    public IntPtr hDevice;
    public IntPtr wParam;
}

// usButtonFlags + usButtonData together occupy the same 4 bytes as the native
// union's ulButtons field; we never need ulButtons, so the sequential layout
// below matches the real Win32 memory layout without needing FieldOffset.
[StructLayout(LayoutKind.Sequential)]
internal struct RAWMOUSE
{
    public ushort usFlags;
    public ushort usButtonFlags;
    public ushort usButtonData;
    public uint ulRawButtons;
    public int lLastX;
    public int lLastY;
    public uint ulExtraInformation;
}

[StructLayout(LayoutKind.Sequential)]
internal struct RAWINPUT
{
    public RAWINPUTHEADER header;
    public RAWMOUSE mouse;
}
