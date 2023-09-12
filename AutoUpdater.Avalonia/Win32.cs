using System;
using System.Runtime.InteropServices;
using System.Text;

namespace CarinaStudio.AutoUpdater;

static class Win32
{
    [DllImport("Kernel32")]
    public static extern uint GetModuleFileName(IntPtr hModule, StringBuilder lpFilename, uint nSize);
}