using System.Runtime.InteropServices;

namespace DeepSeekCode;

public static class NativeFolderPicker
{
    public static string? ShowDialog(IntPtr ownerHandle, string title = "选择文件夹")
    {
        var pidl = IntPtr.Zero;

        try
        {
            var browseInfo = new BROWSEINFO
            {
                hwndOwner = ownerHandle,
                lpszTitle = title,
                ulFlags = BIF.RETURNONLYFSDIRS | BIF.NEWDIALOGSTYLE,
            };

            pidl = SHBrowseForFolder(ref browseInfo);
            if (pidl == IntPtr.Zero)
                return null;

            var path = new char[260];
            if (SHGetPathFromIDList(pidl, path))
                return new string(path).TrimEnd('\0');

            return null;
        }
        finally
        {
            if (pidl != IntPtr.Zero)
                Marshal.FreeCoTaskMem(pidl);
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct BROWSEINFO
    {
        public IntPtr hwndOwner;
        public IntPtr pidlRoot;
        public IntPtr pszDisplayName;
        [MarshalAs(UnmanagedType.LPWStr)]
        public string lpszTitle;
        public uint ulFlags;
        public IntPtr lpfn;
        public IntPtr lParam;
        public int iImage;
    }

    private static class BIF
    {
        public const uint RETURNONLYFSDIRS = 0x0001;
        public const uint NEWDIALOGSTYLE = 0x0040;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SHBrowseForFolder(ref BROWSEINFO lpbi);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SHGetPathFromIDList(IntPtr pidl, char[] pszPath);
}
