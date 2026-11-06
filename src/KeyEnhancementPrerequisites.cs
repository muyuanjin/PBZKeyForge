using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

namespace PBZKeyForge
{
    internal static class KeyEnhancementPrerequisites
    {
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern IntPtr LoadLibraryEx(string path, IntPtr file, uint flags);
        [DllImport("kernel32.dll")]
        static extern bool FreeLibrary(IntPtr module);
        [DllImport("kernel32.dll", CharSet = CharSet.Ansi, ExactSpelling = true)]
        static extern IntPtr GetProcAddress(IntPtr module, string name);
        [DllImport("kernel32.dll", EntryPoint = "GetProcAddress", ExactSpelling = true)]
        static extern IntPtr GetProcAddressOrdinal(IntPtr module, IntPtr ordinal);
        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool SetThreadErrorMode(uint mode, out uint oldMode);

        internal static void EnsureAvailable(string gameBinaryDirectory, params byte[][] runtimeLibraries)
        {
            if (IntPtr.Size != 8) throw new IOException(I18n.Str("ue4ss.requires64"));
            var imports = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
            foreach (byte[] library in runtimeLibraries) ReadImports(library, imports);
            var failures = new List<string>();
            var modules = new List<IntPtr>();
            uint previousMode;
            bool changedMode = SetThreadErrorMode(0x0001 | 0x8000, out previousMode);
            try
            {
                foreach (string name in imports.Keys.OrderBy(name => name.StartsWith("VCRUNTIME", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
                    .ThenBy(name => name, StringComparer.OrdinalIgnoreCase))
                {
                    // The game searches beside PBZ.exe before the system runtime.
                    string local = Path.Combine(gameBinaryDirectory, name);
                    string path = File.Exists(local) ? local : Path.Combine(Environment.SystemDirectory, name);
                    // Load only CRT libraries, never the game, proxy or UE4SS.
                    IntPtr module = LoadLibraryEx(path, IntPtr.Zero, 0x00000100 | 0x00000800);
                    if (module == IntPtr.Zero)
                    {
                        int error = Marshal.GetLastWin32Error();
                        failures.Add(path + " (" + error + ": " + new Win32Exception(error).Message + ")");
                    }
                    else
                    {
                        modules.Add(module);
                        foreach (string symbol in imports[name])
                        {
                            IntPtr address = symbol.StartsWith("#", StringComparison.Ordinal)
                                ? GetProcAddressOrdinal(module, new IntPtr(int.Parse(symbol.Substring(1)))) : GetProcAddress(module, symbol);
                            if (address == IntPtr.Zero) failures.Add(path + ": " + symbol);
                        }
                    }
                }
            }
            finally
            {
                for (int i = modules.Count - 1; i >= 0; i--) FreeLibrary(modules[i]);
                uint ignored;
                if (changedMode) SetThreadErrorMode(previousMode, out ignored);
            }
            if (failures.Count != 0)
                throw new IOException(I18n.Str("ue4ss.vcredist") + Environment.NewLine
                    + "https://aka.ms/vc14/vc_redist.x64.exe" + Environment.NewLine + string.Join(Environment.NewLine, failures));
        }

        // Inspect imports without executing UE4SS or its proxy in the editor process.
        static void ReadImports(byte[] bytes, Dictionary<string, HashSet<string>> imports)
        {
            using (var stream = new MemoryStream(bytes))
            using (var reader = new BinaryReader(stream))
            {
                if (bytes.Length < 64 || reader.ReadUInt16() != 0x5a4d) throw InvalidLibrary();
                stream.Position = 60;
                int pe = reader.ReadInt32();
                if (pe < 64 || pe > bytes.Length - 24) throw InvalidLibrary();
                stream.Position = pe;
                if (reader.ReadUInt32() != 0x4550 || reader.ReadUInt16() != 0x8664) throw InvalidLibrary();
                int sections = reader.ReadUInt16();
                stream.Position = pe + 20;
                int optionalSize = reader.ReadUInt16(), optional = pe + 24;
                if (optionalSize < 128 || optional > bytes.Length - optionalSize || BitConverter.ToUInt16(bytes, optional) != 0x20b)
                    throw InvalidLibrary();
                int sectionTable = optional + optionalSize;
                if (sections > (bytes.Length - sectionTable) / 40) throw InvalidLibrary();
                Func<uint, int> offset = rva => {
                    for (int i = 0; i < sections; i++)
                    {
                        int section = sectionTable + i * 40;
                        uint start = BitConverter.ToUInt32(bytes, section + 12), size = BitConverter.ToUInt32(bytes, section + 16);
                        if (rva < start || rva - start >= size) continue;
                        long position = (long)BitConverter.ToUInt32(bytes, section + 20) + rva - start;
                        if (position >= bytes.Length) throw InvalidLibrary();
                        return (int)position;
                    }
                    throw InvalidLibrary();
                };
                uint directory = BitConverter.ToUInt32(bytes, optional + 120);
                if (directory == 0) return;
                for (int descriptor = offset(directory); ; descriptor += 20)
                {
                    if (descriptor > bytes.Length - 20) throw InvalidLibrary();
                    uint nameRva = BitConverter.ToUInt32(bytes, descriptor + 12);
                    if (nameRva == 0) break;
                    string name = ReadName(bytes, offset(nameRva));
                    if (!name.StartsWith("MSVCP", StringComparison.OrdinalIgnoreCase)
                        && !name.StartsWith("VCRUNTIME", StringComparison.OrdinalIgnoreCase)) continue;
                    HashSet<string> symbols;
                    if (!imports.TryGetValue(name, out symbols)) imports.Add(name, symbols = new HashSet<string>(StringComparer.Ordinal));
                    uint thunk = BitConverter.ToUInt32(bytes, descriptor);
                    if (thunk == 0) thunk = BitConverter.ToUInt32(bytes, descriptor + 16);
                    for (int entry = offset(thunk); ; entry += 8)
                    {
                        if (entry > bytes.Length - 8) throw InvalidLibrary();
                        ulong value = BitConverter.ToUInt64(bytes, entry);
                        if (value == 0) break;
                        symbols.Add((value & 0x8000000000000000UL) != 0
                            ? "#" + (value & 0xffff) : ReadName(bytes, offset(checked((uint)value)) + 2));
                    }
                }
            }
        }

        static string ReadName(byte[] bytes, int offset)
        {
            if (offset < 0 || offset >= bytes.Length) throw InvalidLibrary();
            int end = Array.IndexOf(bytes, (byte)0, offset);
            if (end < 0) throw InvalidLibrary();
            return Encoding.ASCII.GetString(bytes, offset, end - offset);
        }

        static InvalidDataException InvalidLibrary()
        { return new InvalidDataException(I18n.Str("ue4ss.invalid")); }
    }
}
