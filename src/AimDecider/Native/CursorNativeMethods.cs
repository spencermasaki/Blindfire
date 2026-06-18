using System.Runtime.InteropServices;

namespace AimDecider.Native;

internal static class CursorNativeMethods
{
    [DllImport("user32.dll")]
    public static extern bool SetCursorPos(int x, int y);
}
