using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Tail;

internal readonly record struct FileIdentity(ulong Volume, ulong Index)
{
    public static FileIdentity Read(FileStream stream)
    {
        if (OperatingSystem.IsLinux())
        {
            var syscall = RuntimeInformation.ProcessArchitecture switch
            {
                Architecture.X64 => 332,
                Architecture.Arm64 => 291,
                Architecture.Arm => 397,
                _ => throw new PlatformNotSupportedException("Unsupported Linux architecture.")
            };
            const int atEmptyPath = 0x1000;
            const int statxInode = 0x100;
            if (StatX(syscall, stream.SafeFileHandle, "", atEmptyPath, statxInode, out var status) != 0)
                throw new IOException("Could not identify the file.", new Win32Exception(Marshal.GetLastWin32Error()));
            if ((status.Mask & statxInode) == 0)
                throw new IOException("The file system did not provide the file's inode.");
            return new(((ulong)status.DeviceMajor << 32) | status.DeviceMinor, status.Inode);
        }
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("tail requires Windows or Linux.");
        if (!GetFileInformationByHandle(stream.SafeFileHandle, out var information))
            throw new IOException("Could not identify the file.", new Win32Exception(Marshal.GetLastWin32Error()));
        return new(information.VolumeSerialNumber,
            ((ulong)information.FileIndexHigh << 32) | information.FileIndexLow);
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandle(SafeFileHandle file, out HandleInformation information);

    [DllImport("libc", EntryPoint = "syscall", SetLastError = true)]
    private static extern nint StatX(nint syscallNumber, SafeFileHandle file,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string path, nint flags, nint mask, out LinuxStatX status);

    // statx has a fixed kernel ABI, unlike libc fstat layouts. The syscall also works on musl.
    // https://man7.org/linux/man-pages/man2/statx.2.html
    [StructLayout(LayoutKind.Explicit, Size = 256)]
    private struct LinuxStatX
    {
        [FieldOffset(0)] public uint Mask;
        [FieldOffset(32)] public ulong Inode;
        [FieldOffset(136)] public uint DeviceMajor;
        [FieldOffset(140)] public uint DeviceMinor;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct HandleInformation
    {
        public uint Attributes;
        public System.Runtime.InteropServices.ComTypes.FILETIME CreationTime;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastAccessTime;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastWriteTime;
        public uint VolumeSerialNumber;
        public uint FileSizeHigh;
        public uint FileSizeLow;
        public uint NumberOfLinks;
        public uint FileIndexHigh;
        public uint FileIndexLow;
    }
}
