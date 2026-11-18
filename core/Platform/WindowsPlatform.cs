using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace JustNeko.Core.Platform;

[SupportedOSPlatform("windows")]
public sealed partial class WindowsPlatform(string dataDirectory) : IPlatformServices
{
    private const int ExtendedStyle = -20;
    private const long ToolWindow = 0x80;
    private const long AppWindow = 0x40000;
    private const int JobObjectExtendedLimitInformation = 9;
    private const uint KillOnJobClose = 0x2000;
    private const int HideWindow = 0;
    private const int ShowNoActivate = 4;
    private static readonly Guid TaskbarListClass = new("56FDF344-FD6D-11d0-958A-006097C9A090");
    private nint _job;

    public string Name => "windows";

    public string LocalDataDirectory { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "JustNeko");

    public ISecretStore Secrets { get; } = new DpapiSecretStore(Path.Combine(dataDirectory, "secrets.json"));

    // 加上 WS_EX_TOOLWINDOW 以后，资源管理器不会再给它建任务栏按钮；已经建好的按钮用 ITaskbarList 删掉。
    // 不用先隐藏再显示的办法，免得窗口闪一下。
    public void HideFromTaskbar(long windowHandle)
    {
        var window = (nint)windowHandle;
        if (window == 0)
            return;
        var style = (long)GetWindowLongPtr(window, ExtendedStyle);
        var next = (style | ToolWindow) & ~AppWindow;
        if (next == style)
            return;
        SetWindowLongPtr(window, ExtendedStyle, (nint)next);
        try
        {
            if (Activator.CreateInstance(Type.GetTypeFromCLSID(TaskbarListClass)!) is ITaskbarList taskbar)
            {
                taskbar.HrInit();
                taskbar.DeleteTab(window);
                Marshal.ReleaseComObject(taskbar);
            }
        }
        catch (Exception error) when (error is COMException or NotImplementedException)
        {
            // 资源管理器没在跑（比如后台会话）时没有任务栏，样式已经设好，下次显示就不会出现按钮。
        }
    }

    public bool SetWindowShown(long windowHandle, bool shown)
    {
        if (windowHandle == 0)
            return false;
        ShowWindow((nint)windowHandle, shown ? ShowNoActivate : HideWindow);
        return true;
    }

    public bool IsFullscreenAppActive() =>
        SHQueryUserNotificationState(out var state) == 0 && state is 2 or 3 or 4;

    public void TieToProcessLifetime(Process process)
    {
        if (_job == 0)
        {
            _job = CreateJobObject(0, null);
            var limits = new ExtendedLimitInformation { Basic = new BasicLimitInformation { LimitFlags = KillOnJobClose } };
            SetInformationJobObject(_job, JobObjectExtendedLimitInformation, ref limits, (uint)Marshal.SizeOf<ExtendedLimitInformation>());
        }
        AssignProcessToJobObject(_job, process.Handle);
    }

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern nint GetWindowLongPtr(nint window, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern nint SetWindowLongPtr(nint window, int index, nint value);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(nint window, int command);

    [DllImport("shell32.dll")]
    private static extern int SHQueryUserNotificationState(out int state);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern nint CreateJobObject(nint attributes, string? name);

    [DllImport("kernel32.dll")]
    private static extern bool SetInformationJobObject(nint job, int infoClass, ref ExtendedLimitInformation info, uint length);

    [DllImport("kernel32.dll")]
    private static extern bool AssignProcessToJobObject(nint job, nint process);

    [StructLayout(LayoutKind.Sequential)]
    private struct BasicLimitInformation
    {
        public long PerProcessUserTimeLimit;
        public long PerJobUserTimeLimit;
        public uint LimitFlags;
        public nuint MinimumWorkingSetSize;
        public nuint MaximumWorkingSetSize;
        public uint ActiveProcessLimit;
        public nuint Affinity;
        public uint PriorityClass;
        public uint SchedulingClass;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IoCounters
    {
        public ulong ReadOperationCount;
        public ulong WriteOperationCount;
        public ulong OtherOperationCount;
        public ulong ReadTransferCount;
        public ulong WriteTransferCount;
        public ulong OtherTransferCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ExtendedLimitInformation
    {
        public BasicLimitInformation Basic;
        public IoCounters Io;
        public nuint ProcessMemoryLimit;
        public nuint JobMemoryLimit;
        public nuint PeakProcessMemoryUsed;
        public nuint PeakJobMemoryUsed;
    }

    [ComImport]
    [Guid("56FDF342-FD6D-11d0-958A-006097C9A090")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ITaskbarList
    {
        void HrInit();
        void AddTab(nint window);
        void DeleteTab(nint window);
        void ActivateTab(nint window);
        void SetActiveAlt(nint window);
    }
}

/// <summary>用当前 Windows 用户的 DPAPI 加密后存成 JSON，换用户或换电脑都解不开。</summary>
[SupportedOSPlatform("windows")]
public sealed class DpapiSecretStore(string path) : ISecretStore
{
    private static readonly byte[] Entropy = "JustNeko.Secrets.v1"u8.ToArray();
    private Dictionary<string, string>? _protected;

    public bool IsPersistent => true;

    public string? Get(string name)
    {
        if (!Read().TryGetValue(name, out var value))
            return null;
        try
        {
            return Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(value), Entropy, DataProtectionScope.CurrentUser));
        }
        catch (Exception error) when (error is CryptographicException or FormatException)
        {
            return null;
        }
    }

    public string? Set(string name, string? value)
    {
        var values = Read();
        if (value is null)
            values.Remove(name);
        else
            values[name] = Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(value), Entropy, DataProtectionScope.CurrentUser));
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var temporary = path + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(values), new UTF8Encoding(false));
            File.Move(temporary, path, overwrite: true);
            return null;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return $"无法保存 Key：{error.Message}";
        }
    }

    private Dictionary<string, string> Read()
    {
        if (_protected is not null)
            return _protected;
        try
        {
            _protected = File.Exists(path)
                ? JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path, Encoding.UTF8)) ?? []
                : [];
        }
        catch (Exception error) when (error is JsonException or IOException or UnauthorizedAccessException)
        {
            _protected = [];
        }
        return _protected;
    }
}
