namespace Blindfire.Native;

internal static class RawInputConstants
{
    public const int WM_INPUT = 0x00FF;

    public const ushort HID_USAGE_PAGE_GENERIC = 0x01;
    public const ushort HID_USAGE_GENERIC_MOUSE = 0x02;

    // Deliberately not combined with RIDEV_NOLEGACY: we need normal WM_LBUTTONDOWN /
    // WPF click messages to keep flowing alongside raw input.
    public const uint RIDEV_INPUTSINK = 0x00000100;

    public const uint RID_INPUT = 0x10000003;

    public const uint RIM_TYPEMOUSE = 0;

    public const ushort MOUSE_MOVE_ABSOLUTE = 0x01;
}
