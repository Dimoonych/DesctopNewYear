using System.Runtime.InteropServices;

namespace DesctopNewYear;

internal sealed class ExitShortcut : NativeWindow, IDisposable
{
    public const string Display = "Ctrl+Alt+Пробел";
    private const int Id = 1;
    private const int WmHotkey = 0x0312;
    private const uint AltControlNoRepeat = 0x4003;
    private readonly Action exit;

    public bool Registered { get; private set; }

    public ExitShortcut(Action exit)
    {
        this.exit = exit;
        CreateHandle(new CreateParams { Parent = new nint(-3) }); // HWND_MESSAGE
        Registered = RegisterHotKey(Handle, Id, AltControlNoRepeat, (uint)Keys.Space);
    }

    protected override void WndProc(ref Message message)
    {
        if (message.Msg == WmHotkey && message.WParam == Id && Registered)
        {
            exit();
            return;
        }

        base.WndProc(ref message);
    }

    public void Dispose()
    {
        if (Registered)
        {
            UnregisterHotKey(Handle, Id);
            Registered = false;
        }
        DestroyHandle();
    }

    [DllImport("user32.dll")]
    private static extern bool RegisterHotKey(nint window, int id, uint modifiers, uint key);
    [DllImport("user32.dll")]
    private static extern bool UnregisterHotKey(nint window, int id);
}
