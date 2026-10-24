using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace ExtractorFotogramas
{
    public static class LongPathFileSystem
    {
        const string ExtendedPrefix = @"\\?\";
        const string ExtendedUncPrefix = @"\\?\UNC\";
        const string UncPrefix = @"\\";
        const int MaximumExtendedPathLength = 32767;
        const uint InvalidFileAttributes = 0xFFFFFFFF;
        const uint DirectoryAttribute = 0x10;
        const uint GenericRead = 0x80000000;
        const uint GenericWrite = 0x40000000;
        const uint ShareRead = 0x1;
        const uint CreateNew = 1;
        const uint OpenExisting = 3;
        const int ErrorFileNotFound = 2;
        const uint ReadOnlyVolumeFlag = 0x80000;

        public static string GetFullPath(string path)
        {
            var buffer = new StringBuilder(MaximumExtendedPathLength);
            var length = GetFullPathNameW(path, (uint)buffer.Capacity, buffer, IntPtr.Zero);
            if (length == 0 || length > buffer.Capacity)
            {
                throw LastError(path);
            }
            return buffer.ToString();
        }

        public static string GetDirectoryWithSeparator(string fullFilePath)
        {
            return fullFilePath.Substring(0, fullFilePath.LastIndexOf('\\') + 1);
        }

        public static long? FreeBytesAvailable(string fullPath)
        {
            ulong freeBytes, totalBytes, totalFreeBytes;
            var available = GetDiskFreeSpaceExW(VolumeRoot(fullPath), out freeBytes, out totalBytes, out totalFreeBytes);
            return available ? (long?)freeBytes : null;
        }

        public static bool IsOnReadOnlyVolume(string fullPath)
        {
            uint serialNumber, maximumComponentLength, fileSystemFlags;
            var known = GetVolumeInformationW(
                VolumeRoot(fullPath), null, 0, out serialNumber, out maximumComponentLength, out fileSystemFlags, null, 0);
            return known && (fileSystemFlags & ReadOnlyVolumeFlag) != 0;
        }

        public static string WithSeparator(string directory)
        {
            return directory.EndsWith("\\", StringComparison.Ordinal) ? directory : directory + "\\";
        }

        public static string ForDisplay(string directoryWithSeparator)
        {
            return directoryWithSeparator.Length > 3 ? directoryWithSeparator.TrimEnd('\\') : directoryWithSeparator;
        }

        public static bool FileExists(string path)
        {
            var attributes = GetFileAttributesW(Extended(path));
            return attributes != InvalidFileAttributes && (attributes & DirectoryAttribute) == 0;
        }

        public static bool DirectoryExists(string path)
        {
            var attributes = GetFileAttributesW(Extended(path));
            return attributes != InvalidFileAttributes && (attributes & DirectoryAttribute) != 0;
        }

        public static void CreateDirectory(string path)
        {
            if (!CreateDirectoryW(Extended(path), IntPtr.Zero))
            {
                throw LastError(path);
            }
        }

        public static void DeleteEmptyDirectory(string path)
        {
            if (!RemoveDirectoryW(Extended(path)))
            {
                throw LastError(path);
            }
        }

        public static IList<string> FindFiles(string directory, string extension)
        {
            var files = new List<string>();
            Win32FindData entry;
            using (var search = FindFirstFileW(Extended(Path.Combine(directory, "*")), out entry))
            {
                if (search.IsInvalid)
                {
                    if (Marshal.GetLastWin32Error() == ErrorFileNotFound) return files;
                    throw LastError(directory);
                }
                do
                {
                    var isFile = (entry.FileAttributes & DirectoryAttribute) == 0;
                    if (isFile && entry.FileName.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
                    {
                        files.Add(Path.Combine(directory, entry.FileName));
                    }
                }
                while (FindNextFileW(search, out entry));
            }
            return files;
        }

        public static FileStream OpenRead(string path)
        {
            return Open(path, GenericRead, ShareRead, OpenExisting, FileAccess.Read);
        }

        public static FileStream CreateNewForWriting(string path)
        {
            return Open(path, GenericWrite, 0, CreateNew, FileAccess.Write);
        }

        static FileStream Open(string path, uint desiredAccess, uint shareMode, uint creationDisposition, FileAccess access)
        {
            var handle = CreateFileW(Extended(path), desiredAccess, shareMode, IntPtr.Zero, creationDisposition, 0, IntPtr.Zero);
            if (handle.IsInvalid)
            {
                var error = LastError(path);
                handle.Dispose();
                throw error;
            }
            return new FileStream(handle, access);
        }

        static string VolumeRoot(string fullPath)
        {
            if (!fullPath.StartsWith(UncPrefix, StringComparison.Ordinal))
            {
                return fullPath.Substring(0, 3);
            }
            var serverEnd = fullPath.IndexOf('\\', UncPrefix.Length);
            var shareEnd = serverEnd < 0 ? -1 : fullPath.IndexOf('\\', serverEnd + 1);
            return shareEnd < 0 ? fullPath + "\\" : fullPath.Substring(0, shareEnd + 1);
        }

        static string Extended(string path)
        {
            if (path.StartsWith(ExtendedPrefix, StringComparison.Ordinal)) return path;
            if (path.StartsWith(UncPrefix, StringComparison.Ordinal)) return ExtendedUncPrefix + path.Substring(UncPrefix.Length);
            return ExtendedPrefix + path;
        }

        static IOException LastError(string path)
        {
            return new IOException(new Win32Exception(Marshal.GetLastWin32Error()).Message + "\n" + path);
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct Win32FindData
        {
            public uint FileAttributes;
            public System.Runtime.InteropServices.ComTypes.FILETIME CreationTime;
            public System.Runtime.InteropServices.ComTypes.FILETIME LastAccessTime;
            public System.Runtime.InteropServices.ComTypes.FILETIME LastWriteTime;
            public uint FileSizeHigh;
            public uint FileSizeLow;
            public uint Reserved0;
            public uint Reserved1;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
            public string FileName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 14)]
            public string AlternateFileName;
        }

        sealed class SafeFindHandle : SafeHandleZeroOrMinusOneIsInvalid
        {
            SafeFindHandle() : base(true)
            {
            }

            protected override bool ReleaseHandle()
            {
                return FindClose(handle);
            }
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, ExactSpelling = true)]
        static extern uint GetFullPathNameW(string fileName, uint bufferLength, StringBuilder buffer, IntPtr filePart);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, ExactSpelling = true)]
        static extern uint GetFileAttributesW(string fileName);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, ExactSpelling = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        static extern bool GetDiskFreeSpaceExW(string directoryName, out ulong freeBytesAvailable, out ulong totalBytes, out ulong totalFreeBytes);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, ExactSpelling = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        static extern bool GetVolumeInformationW(
            string rootPathName, StringBuilder volumeName, uint volumeNameSize, out uint serialNumber,
            out uint maximumComponentLength, out uint fileSystemFlags, StringBuilder fileSystemName, uint fileSystemNameSize);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, ExactSpelling = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        static extern bool CreateDirectoryW(string pathName, IntPtr securityAttributes);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, ExactSpelling = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        static extern bool RemoveDirectoryW(string pathName);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, ExactSpelling = true)]
        static extern SafeFileHandle CreateFileW(
            string fileName, uint desiredAccess, uint shareMode, IntPtr securityAttributes,
            uint creationDisposition, uint flagsAndAttributes, IntPtr templateFile);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, ExactSpelling = true)]
        static extern SafeFindHandle FindFirstFileW(string fileName, out Win32FindData findData);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, ExactSpelling = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        static extern bool FindNextFileW(SafeFindHandle findHandle, out Win32FindData findData);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        static extern bool FindClose(IntPtr findHandle);
    }
}
