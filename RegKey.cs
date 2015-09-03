// Copyright (c) 2015, Dijji, and released under Ms-PL.  This can be found in the root of this distribution. 

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;

namespace RepairTasks
{
    // Where we put nasty PInvoke stuff for exporting and reimporting registry keys
    class RegKey
    {
        [DllImport("advapi32.dll", CharSet = CharSet.Auto)]
        public static extern uint RegOpenKeyEx(
          UIntPtr hKey,
          string subKey,
          int ulOptions,
          int samDesired,
          out UIntPtr hkResult);

        [DllImport("advapi32.dll", SetLastError = true)]
        public static extern int RegCloseKey(
            UIntPtr hKey);

        public static UIntPtr HKEY_LOCAL_MACHINE = new UIntPtr(0x80000002u);
        public static int KEY_READ = 0x20019; 
        public static int KEY_ALL_ACCESS = 0xF003F;

        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        public static extern uint RegSaveKey(UIntPtr hKey, string lpFile, IntPtr lpSecurityAttributes);

        public static uint ExportRegKeys(Dictionary<string, string> dictRegBackups)
        {
            // Need to copy the keys so we can modify the collection
            List<string> keys = new List<string>(dictRegBackups.Keys);
            uint result;

            foreach (string key in keys)
            {
                UIntPtr hKey = UIntPtr.Zero;

                result = RegOpenKeyEx(HKEY_LOCAL_MACHINE, key, 0, KEY_READ, out hKey);
                if (result == 0)
                {
                    string tempFile = Path.GetTempFileName();
                    dictRegBackups[key] = tempFile;

                    File.Delete(tempFile);  // ensure no collisions
                    result = RegSaveKey(hKey, tempFile, IntPtr.Zero);

                    if (result == 1314)  // ERROR_PRIVILEGE_NOT_HELD
                    {
                        EnableDisablePrivilege("SeBackupPrivilege", true);  // throws on failure
                        File.Delete(tempFile);  // clean up first attempt
                        result = RegSaveKey(hKey, tempFile, IntPtr.Zero);
                    }

                    RegCloseKey(hKey);

                    if (result != 0)
                    {
                        File.Delete(tempFile);  // clean up
                        return result;
                    }
                }
                else
                    return result;
            }

            return 0;
        }

        [Flags]
        public enum RegOption
        {
            NonVolatile = 0x0,
            Volatile = 0x1,
            CreateLink = 0x2,
            BackupRestore = 0x4,
            OpenLink = 0x8
        }

        [Flags]
        public enum RegSAM
        {
            QueryValue = 0x0001,
            SetValue = 0x0002,
            CreateSubKey = 0x0004,
            EnumerateSubKeys = 0x0008,
            Notify = 0x0010,
            CreateLink = 0x0020,
            WOW64_32Key = 0x0200,
            WOW64_64Key = 0x0100,
            WOW64_Res = 0x0300,
            Read = 0x00020019,
            Write = 0x00020006,
            Execute = 0x00020019,
            AllAccess = 0x000f003f
        }

        public enum RegResult
        {
            CreatedNewKey = 0x00000001,
            OpenedExistingKey = 0x00000002
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct SECURITY_ATTRIBUTES
        {
            public int nLength;
            public IntPtr lpSecurityDescriptor;
            public int bInheritHandle;
        }

        [DllImport("advapi32.dll", SetLastError = true)]
        static extern uint RegCreateKeyEx(
                    UIntPtr hKey,
                    string lpSubKey,
                    int Reserved,
                    string lpClass,
                    RegOption dwOptions,
                    RegSAM samDesired,
                    ref SECURITY_ATTRIBUTES lpSecurityAttributes,
                    out UIntPtr phkResult,
                    out RegResult lpdwDisposition);

        [DllImport("advapi32.dll", SetLastError = true)]
        static extern uint RegLoadKey(UIntPtr hKey, String lpSubKey, String lpFile);

        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        public static extern uint RegRestoreKey(UIntPtr hKey, string lpFile, uint dwFlags);

        public static uint RestoreRegKeys(Dictionary<string, string> dictRegBackups)
        {
            uint error = 0;

            foreach (string key in dictRegBackups.Keys)
            {
                string backupFile = dictRegBackups[key];
                string parentKey = Path.GetDirectoryName(key);
                string childKey = Path.GetFileName(key);
                uint result;

                if (backupFile != null)
                {
                    UIntPtr hKey = UIntPtr.Zero;
                    SECURITY_ATTRIBUTES sec = new SECURITY_ATTRIBUTES { nLength = 0, bInheritHandle = 0 };
                    RegResult disp;

                    result = RegCreateKeyEx(HKEY_LOCAL_MACHINE, key, 0, null, RegOption.BackupRestore, RegSAM.AllAccess, ref sec, out hKey, out disp);

                    if (result == 0)
                    {
                        result = RegRestoreKey(hKey, backupFile, 0);

                        if (result == 1314)  // ERROR_PRIVILEGE_NOT_HELD
                        {
                            EnableDisablePrivilege("SeRestorePrivilege", true);  // throws on failure
                            result = RegRestoreKey(hKey, backupFile, 0);
                        }

                        RegCloseKey(hKey);

                        if (result != 0)
                            error = result;
                    }
                    else
                        error = result;
                }
            }

            return error;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct LUID
        {
            public uint LowPart;
            public int HighPart;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct TOKEN_PRIVILEGES
        {
            public UInt32 PrivilegeCount;
            public LUID Luid;
            public UInt32 Attributes;
        }

        [DllImport("advapi32", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool OpenProcessToken(IntPtr ProcessHandle, TokenAccessLevels DesiredAccess, out IntPtr TokenHandle);

        [DllImport("advapi32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool AdjustTokenPrivileges(IntPtr TokenHandle, [MarshalAs(UnmanagedType.Bool)]bool DisableAllPrivileges, 
                    ref TOKEN_PRIVILEGES NewState, uint BufferLength, out TOKEN_PRIVILEGES PreviousState, out uint ReturnLength);

        [DllImport("advapi32.dll", SetLastError = true)]
        public static extern bool LookupPrivilegeValue(string lpSystemName, string lpName, out LUID lpLuid);


        private static void EnableDisablePrivilege(string PrivilegeName, bool EnableDisable)
        {
            var htok = IntPtr.Zero;
            if (!OpenProcessToken(Process.GetCurrentProcess().Handle, TokenAccessLevels.AdjustPrivileges | TokenAccessLevels.Query, out htok))
            {
                Marshal.ThrowExceptionForHR(Marshal.GetHRForLastWin32Error());
                return;
            }
            var tkp = new TOKEN_PRIVILEGES { PrivilegeCount = 1 };
            LUID luid;
            if (!LookupPrivilegeValue(null, PrivilegeName, out luid))
            {
                Marshal.ThrowExceptionForHR(Marshal.GetHRForLastWin32Error());
                return;
            }
            tkp.Luid = luid;
            tkp.Attributes = (uint)(EnableDisable ? 2 : 0);
            TOKEN_PRIVILEGES prv;
            uint rb;
            if (!AdjustTokenPrivileges(htok, false, ref tkp, 256, out prv, out rb))
            {
                Marshal.ThrowExceptionForHR(Marshal.GetHRForLastWin32Error());
                return;
            }
        }
    }
}
