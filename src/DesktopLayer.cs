using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Interop;

namespace DeskDots {
    // Remain above the wallpaper, behind ordinary application windows, without taking focus.
    public static class DesktopLayer {
        delegate bool EnumProc(IntPtr hwnd,IntPtr param);
        [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc callback,IntPtr param);
        [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr hwnd);
        [DllImport("user32.dll")] static extern bool IsIconic(IntPtr hwnd);
        [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern int GetClassName(IntPtr hwnd,StringBuilder name,int length);
        [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hwnd,out uint pid);
        [DllImport("user32.dll",EntryPoint="GetWindowLongW")] static extern int GetWindowLong(IntPtr hwnd,int index);
        [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr hwnd,IntPtr after,int x,int y,int width,int height,uint flags);
        [DllImport("dwmapi.dll")] static extern int DwmGetWindowAttribute(IntPtr hwnd,int attribute,out int value,int size);
        [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hwnd,int attribute,ref int value,int size);
        const uint Quiet=0x0001|0x0002|0x0010|0x0200;
        public static void Sink(Window window) {
            if(!window.IsVisible || window.IsActive)return;
            var hwnd=new WindowInteropHelper(window).Handle;if(hwnd==IntPtr.Zero)return;
            IntPtr lowest=IntPtr.Zero;uint own=(uint)Process.GetCurrentProcess().Id;
            EnumWindows((candidate,p)=>{
                uint pid;GetWindowThreadProcessId(candidate,out pid);
                if(pid==own || !IsWindowVisible(candidate) || IsIconic(candidate) || (GetWindowLong(candidate,-20)&8)!=0)return true;
                var name=new StringBuilder(128);GetClassName(candidate,name,128);string cls=name.ToString();
                if(cls=="Progman"||cls=="WorkerW"||cls=="Shell_TrayWnd"||cls=="Shell_SecondaryTrayWnd"||cls=="tooltips_class32")return true;
                int cloaked;try{if(DwmGetWindowAttribute(candidate,14,out cloaked,4)==0&&cloaked!=0)return true;}catch{}
                lowest=candidate;return true;
            },IntPtr.Zero);
            // HWND_BOTTOM can put a widget behind Explorer's wallpaper. Insert behind the
            // lowest visible application instead, keeping it in the normal desktop band.
            SetWindowPos(hwnd,new IntPtr(-2),0,0,0,0,Quiet);
            SetWindowPos(hwnd,lowest,0,0,0,0,Quiet);
        }
        public static void Round(Window window){try{int pref=2;DwmSetWindowAttribute(new WindowInteropHelper(window).Handle,33,ref pref,4);}catch{}}
        public static bool IsTopmost(Window window){return (GetWindowLong(new WindowInteropHelper(window).Handle,-20)&8)!=0;}
    }
}
