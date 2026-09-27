using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;

namespace ZeroCompanyRGB
{
    // Layouts and signatures match the bundled official iCUE SDK 4.0.84 headers.
    internal static class Native
    {
        private const string Dll = "iCUESDK.x64_2019.dll";
        private static IntPtr module;
        [DllImport("kernel32", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr LoadLibraryEx(string path, IntPtr file, uint flags);
        internal static void Load(string folder)
        {
            if (module != IntPtr.Zero) return;
            module = LoadLibraryEx(Path.Combine(folder, Dll), IntPtr.Zero, 0x00000100 | 0x00001000);
            if (module == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error(), "Cannot load the bundled iCUE SDK");
        }
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
        internal struct Device
        {
            public int Type;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Id;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Serial;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Model;
            public int LedCount, ChannelCount;
        }
        [StructLayout(LayoutKind.Sequential)]
        internal struct Position { public uint Id; public double X, Y; }
        [StructLayout(LayoutKind.Sequential)]
        internal struct Color { public uint Id; public byte R, G, B, A; }
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        internal delegate void StateCallback(IntPtr context, IntPtr state);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] internal static extern int CorsairConnect(StateCallback callback, IntPtr context);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] internal static extern int CorsairDisconnect();
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] internal static extern int CorsairGetDevices(ref int filter, int capacity, [Out] Device[] devices, out int count);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)] internal static extern int CorsairGetLedPositions(string id, int capacity, [Out] Position[] positions, out int count);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)] internal static extern int CorsairSetLedColors(string id, int count, [In] Color[] colors);
    }

}
