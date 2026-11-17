// Copyright (C) 2026 DaUnknown-0. GPL-3.0-or-later.
using System;
using System.Runtime.InteropServices;
namespace UnknownsSmokescreen;
internal static class NativeArguments
{
    internal static readonly string[] Values = Read();
    [DllImport("kernel32.dll", CharSet=CharSet.Unicode)] private static extern IntPtr GetCommandLineW();
    [DllImport("shell32.dll", CharSet=CharSet.Unicode)] private static extern IntPtr CommandLineToArgvW(IntPtr command, out int count);
    [DllImport("kernel32.dll")] private static extern IntPtr LocalFree(IntPtr buffer);
    private static string[] Read()
    {
        var argv=CommandLineToArgvW(GetCommandLineW(),out int count);
        if(argv==IntPtr.Zero) return Array.Empty<string>();
        try { var result=new string[count]; for(int i=0;i<count;i++) result[i]=Marshal.PtrToStringUni(Marshal.ReadIntPtr(argv,i*IntPtr.Size)); return result; }
        finally { LocalFree(argv); }
    }
}
