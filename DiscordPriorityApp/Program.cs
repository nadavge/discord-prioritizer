using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace DiscordPriorityApp
{

    internal class Program
    {

        private static readonly UInt32 ABOVE_NORMAL_PRIORITY_CLASS = 0x00008000;
        private static readonly UInt32 BELOW_NORMAL_PRIORITY_CLASS = 0x00004000;
        private static readonly UInt32 HIGH_PRIORITY_CLASS = 0x00000080;
        private static readonly UInt32 IDLE_PRIORITY_CLASS = 0x00000040;
        private static readonly UInt32 NORMAL_PRIORITY_CLASS = 0x00000020;
        private static readonly UInt32 PROCESS_MODE_BACKGROUND_BEGIN = 0x00100000;
        private static readonly UInt32 PROCESS_MODE_BACKGROUND_END = 0x00200000;
        private static readonly UInt32 REALTIME_PRIORITY_CLASS = 0x00000100;

        private static readonly UInt32[] PriorityClassesOrder = {
            IDLE_PRIORITY_CLASS,
            BELOW_NORMAL_PRIORITY_CLASS,
            NORMAL_PRIORITY_CLASS,
            ABOVE_NORMAL_PRIORITY_CLASS,
            HIGH_PRIORITY_CLASS,
            REALTIME_PRIORITY_CLASS
        };

        [DllImport("Psapi.dll", SetLastError = true)]
        static extern bool EnumProcesses(
            [MarshalAs(UnmanagedType.LPArray, ArraySubType = UnmanagedType.U4)][In][Out] UInt32[] processIds,
            UInt32 arraySizeBytes,
            [MarshalAs(UnmanagedType.U4)] out UInt32 bytesCopied
        );

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern IntPtr OpenProcess(
            [In] UInt32 processAccess,
            [In] bool bInheritHandle,
            [In] UInt32 processId
        );

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool CloseHandle(
            [In] IntPtr hObject
        );

        [DllImport("kernel32.dll")]
        public static extern uint GetLastError();

        [DllImport("kernel32.dll")]
        public static extern bool QueryFullProcessImageName(
         [In] IntPtr hProcess,
         [In] UInt32 dwFlags,
         [Out] StringBuilder lpExeName,
         [In, Out] ref int lpdwSize
        );

        [DllImport("kernel32.dll")]
        public static extern UInt32 GetPriorityClass(
            [In] IntPtr hProcess
        );

        [DllImport("kernel32.dll")]
        public static extern bool SetPriorityClass(
            [In] IntPtr hProcess,
            [In] UInt32 dwPriorityClass
        );

        static UInt32[] GetProcessIds()
        {
            UInt32 maxProcesses = 1024;
            UInt32 arrayBytesSize = maxProcesses * sizeof(UInt32);
            UInt32[] processIds = new UInt32[maxProcesses];
            UInt32 bytesCopied;

            bool success = EnumProcesses(processIds, arrayBytesSize, out bytesCopied);

            if (!success)
            {
                Console.WriteLine("Boo!");
                // TODO Throw exception
            }
            if (0 == bytesCopied)
            {
                Console.WriteLine("Nobody home!");
                // TODO throw exception
            }

            UInt32 numIdsCopied = bytesCopied >> 2; ;

            if (0 != (bytesCopied & 3))
            {
                UInt32 partialDwordBytes = bytesCopied & 3;

                Console.WriteLine("EnumProcesses copied {0} and {1}/4th DWORDS...  Please ask it for the other {2}/4th DWORD",
                    numIdsCopied, partialDwordBytes, 4 - partialDwordBytes);
                // TODO throw exception...
            }

            Array.Resize(ref processIds, (int)numIdsCopied);

            return processIds;
        }

        static bool isDestHigherPriority(UInt32 source, UInt32 dest)
        {
            var orderSource = Array.IndexOf(PriorityClassesOrder, source);
            var orderDest = Array.IndexOf(PriorityClassesOrder, dest);

            return orderSource < orderDest;
        }

        static void SetProcessPriorityByName(string name, UInt32 destPriority, bool overwriteOnlyLower=true)
        {
            var procIds = GetProcessIds();
            const int maxPath = 4096;
            const UInt32 PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;
            const UInt32 PROCESS_SET_INFORMATION = 0x0200;
            IntPtr handle = IntPtr.Zero;

            foreach (var procId in procIds)
            {
                try
                {
                    handle = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION | PROCESS_SET_INFORMATION, true, procId);
                    if (handle == IntPtr.Zero)
                    {
                        //Console.WriteLine("Failed to open {0}, error = {1}", procId, GetLastError());
                        continue;
                    }

                    int capacity = maxPath;
                    StringBuilder sb = new StringBuilder(maxPath);
                    bool success = QueryFullProcessImageName(handle, 1, sb, ref capacity);
                    if (!success)
                    {
                        Console.WriteLine("Failed querying {0}", procId);
                    }
                    if (capacity == maxPath)
                    {
                        Console.WriteLine("Got a looong path! {0}", sb.ToString(0, 2));
                        continue;
                    }

                    string fileName = Path.GetFileName(sb.ToString(0, capacity));
                    //Console.WriteLine("Found exe ({0}): {1}", procId, fileName);
                    if (fileName != name)
                    {
                        continue;
                    }

                    UInt32 prePriority = GetPriorityClass(handle);
                    if (prePriority == 0)
                    {
                        Console.WriteLine("Failed getting priority of {0}", procId);
                        continue;
                    }

                    if (overwriteOnlyLower && !isDestHigherPriority(prePriority, destPriority))
                    {
                        Console.WriteLine("Won't overwrite priority {0:x} of pid {1}", prePriority, procId);
                        continue;
                    }

                    success = SetPriorityClass(handle, destPriority);
                    if (!success)
                    {
                        Console.WriteLine("Failed setting priority of {0} to {1:x}, error = {2}", procId, destPriority, GetLastError());
                        continue;
                    }

                    UInt32 postPriority = GetPriorityClass(handle);
                    Console.WriteLine("Proc {0} priority = {1:x}, before = {2:x}", procId, postPriority, prePriority);

                }
                finally
                {
                    if (handle != IntPtr.Zero)
                    {
                        CloseHandle(handle);
                    }
                }
            }
        }

        static void Main(string[] args)
        {
            SetProcessPriorityByName("Discord.exe", ABOVE_NORMAL_PRIORITY_CLASS);
        }
    }
}
