using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace PBZKeyForge
{
    /// <summary>The game's actual keyboard scheme selector, separate from Enhanced Input metadata.</summary>
    public sealed class GameSchemeSettings
    {
        static readonly byte[] Header = { 0x50, 0x42, 0x5a, 0x53, 1, 1, 0, 0 };
        static readonly BuildProfile[] VerifiedKeyProfiles = {
            new BuildProfile("Steam CL-301524_JKS-17219",
                "65b75c306a77eb2df3ed5a7e6b3f8dbef360f6abefa74c465196cfa19af9637c",
                new ReaderPath("PBZS v1/1, verified file offsets", Header, 8, 20, 36,
                    new long[] { 0xc1c7b48, 0xc1c7b28, 0xc1c7b38, 0xc1c7b58 }))
        };
        static readonly Encoding Utf8 = new UTF8Encoding(false, true);
        static readonly Regex SchemeField = new Regex("\"KeyBindingScheme\"\\s*:\\s*\"(?<value>[^\"]*)\"");
        readonly string path;
        readonly string executable;
        readonly byte[] original;
        readonly byte[] plain;
        readonly string json;
        readonly Match field;
        readonly ReaderPath readerPath;

        public string ProfileId { get; private set; }
        public string GameBuild { get; private set; }

        GameSchemeSettings(string path, string executable, byte[] original, byte[] plain, string gameBuild, ReaderPath readerPath)
        {
            this.path = path;
            this.executable = executable;
            this.original = original;
            this.plain = plain;
            GameBuild = gameBuild;
            this.readerPath = readerPath;
            if (plain.Length < 6 || BitConverter.ToInt32(plain, 0) != plain.Length - 4 || plain[plain.Length - 1] != 0)
                throw new InvalidDataException("Unsupported PBZ settings string format.");
            json = Utf8.GetString(plain, 4, plain.Length - 5);
            var matches = SchemeField.Matches(json);
            if (matches.Count != 1) throw new InvalidDataException("Expected one KeyBindingScheme field.");
            field = matches[0];
            ProfileId = ProfileFor(field.Groups["value"].Value);
        }

        public static GameSchemeSettings LoadForInputSave(string inputSave)
        {
            string dir = Path.GetDirectoryName(Path.GetFullPath(inputSave));
            string exe = FindExecutable(dir);
            return Load(Path.Combine(dir, "PBZSharedSetting.sav"), exe);
        }

        public static GameSchemeSettings Load(string settingsPath, string executablePath)
        {
            byte[] encoded = File.ReadAllBytes(settingsPath);
            BuildProfile build;
            string buildId;
            List<ReaderKey> keys = ReadKeys(executablePath, out build, out buildId);
            try
            {
                Exception last = null;
                foreach (var candidate in keys)
                {
                    byte[] plain;
                    try { plain = Unseal(encoded, candidate.Path, candidate.Key); }
                    catch (InvalidDataException ex) { last = ex; continue; }
                    catch (CryptographicException ex) { last = ex; continue; }

                    // Authentication succeeded. Never reinterpret authenticated but invalid content
                    // using another path; doing so would turn format errors into plaintext guessing.
                    return new GameSchemeSettings(settingsPath, executablePath, encoded, plain, buildId, candidate.Path);
                }
                throw new InvalidDataException("No verified PBZS reader authenticated this settings file for " + build.Name + ". The file was not changed.", last);
            }
            finally { ClearKeys(keys); }
        }

        static string FindExecutable(string dir)
        {
            // Installed Steam layout. A different build must authenticate successfully before any write.
            var parent = new DirectoryInfo(dir);
            for (int i = 0; parent != null && i < 4; i++, parent = parent.Parent)
            {
                string exe = Path.Combine(parent.FullName, @"platform\steam\PBZ.exe");
                if (File.Exists(exe)) return exe;
            }
            throw new FileNotFoundException("Cannot locate the installed game executable for PBZ settings.");
        }

        sealed class BuildProfile
        {
            public readonly string Name, KeySha256;
            public readonly ReaderPath[] Paths;
            public BuildProfile(string name, string keySha256, params ReaderPath[] paths)
            { Name = name; KeySha256 = keySha256; Paths = paths; }
        }

        sealed class ReaderPath
        {
            public readonly string Name;
            public readonly byte[] Header;
            public readonly int NonceOffset, TagOffset, CipherOffset;
            public readonly long[] KeyOffsets;
            public ReaderPath(string name, byte[] header, int nonceOffset, int tagOffset, int cipherOffset, long[] keyOffsets)
            { Name = name; Header = header; NonceOffset = nonceOffset; TagOffset = tagOffset; CipherOffset = cipherOffset; KeyOffsets = keyOffsets; }
        }

        sealed class ReaderKey
        {
            public readonly ReaderPath Path;
            public readonly byte[] Key;
            public ReaderKey(ReaderPath path, byte[] key) { Path = path; Key = key; }
        }

        static List<ReaderKey> ReadKeys(string exe, out BuildProfile build, out string buildId)
        {
            build = null;
            buildId = null;
            var keys = new List<ReaderKey>();
            try
            {
                using (var stream = new FileStream(exe, FileMode.Open, FileAccess.Read, FileShare.Read, 16, FileOptions.RandomAccess))
                {
                    // Registered locations identify a reader only when its key fingerprint matches.
                    foreach (var known in VerifiedKeyProfiles)
                    foreach (var path in known.Paths)
                    {
                        if (path.KeyOffsets.Length != 4)
                            throw new InvalidDataException("Invalid registered PBZS reader path: " + path.Name);
                        long requiredLength = path.KeyOffsets[0] + 16;
                        foreach (long offset in path.KeyOffsets) requiredLength = Math.Max(requiredLength, offset + 16);
                        if (stream.Length < requiredLength) continue;
                        var parts = new byte[4][];
                        for (int i = 0; i < path.KeyOffsets.Length; i++)
                        {
                            stream.Position = path.KeyOffsets[i];
                            parts[i] = new byte[16];
                            if (stream.Read(parts[i], 0, parts[i].Length) != parts[i].Length)
                                throw new InvalidDataException("The verified PBZ executable is truncated at a registered key location.");
                        }
                        var key = new byte[32];
                        for (int i = 0; i < 16; i++)
                        {
                            key[i] = (byte)(parts[0][i] ^ parts[1][i]);
                            key[i + 16] = (byte)(parts[2][i] ^ parts[3][i]);
                        }
                        string keyDigest;
                        using (var sha = SHA256.Create()) keyDigest = Hex(sha.ComputeHash(key));
                        if (keyDigest == known.KeySha256)
                        {
                            keys.Add(new ReaderKey(path, key));
                            if (build == null) build = known;
                        }
                        else Array.Clear(key, 0, key.Length);
                        foreach (var part in parts) Array.Clear(part, 0, part.Length);
                    }
                }
                if (keys.Count == 0)
                    throw new InvalidDataException("Unsupported PBZ game build. No verified key fingerprint matched; switch the scheme in the game. The save was not changed.");
                buildId = build.Name + " (verified PBZS reader; exact build unknown)";
                return keys;
            }
            catch { ClearKeys(keys); throw; }
        }

        static string Hex(byte[] bytes)
        {
            var result = new StringBuilder(bytes.Length * 2);
            foreach (byte value in bytes) result.Append(value.ToString("x2"));
            return result.ToString();
        }

        static void ClearKeys(List<ReaderKey> keys)
        {
            foreach (var item in keys) Array.Clear(item.Key, 0, item.Key.Length);
        }

        public static string ValueFor(string profile)
        {
            switch (profile)
            {
                case "InputUserSettings.Profiles.Default": return "DefaultPreset";
                case "PBZ.KeyProfile.Custom1": return "Custom1";
                case "PBZ.KeyProfile.Custom2": return "Custom2";
                default: throw new InvalidDataException("Unknown keyboard scheme: " + profile);
            }
        }

        static string ProfileFor(string value)
        {
            switch (value)
            {
                case "DefaultPreset": return "InputUserSettings.Profiles.Default";
                case "Custom1": return "PBZ.KeyProfile.Custom1";
                case "Custom2": return "PBZ.KeyProfile.Custom2";
                default: throw new InvalidDataException("Unknown KeyBindingScheme value: " + value);
            }
        }

        byte[] ChangedPlain(string profile)
        {
            Group value = field.Groups["value"];
            byte[] text = Utf8.GetBytes(json.Substring(0, value.Index) + ValueFor(profile) + json.Substring(value.Index + value.Length));
            byte[] changed = new byte[text.Length + 5];
            Buffer.BlockCopy(BitConverter.GetBytes(text.Length + 1), 0, changed, 0, 4);
            Buffer.BlockCopy(text, 0, changed, 4, text.Length);
            return changed;
        }

        /// <summary>Change only the selected scheme; null means the file already contains this value.</summary>
        public string Activate(string profile)
        {
            ValueFor(profile);
            EnsureGameClosed();
            if (!Same(original, File.ReadAllBytes(path)))
                throw new IOException("PBZ settings changed since loading; load the save again.");
            if (profile == ProfileId) return null;

            byte[] expected = ChangedPlain(profile);
            BuildProfile build;
            string buildId;
            List<ReaderKey> keys = ReadKeys(executable, out build, out buildId);
            if (build == null || string.IsNullOrEmpty(buildId))
            {
                ClearKeys(keys);
                throw new InvalidDataException("No verified PBZS key profile is available for this game build.");
            }
            ReaderKey selected = null;
            foreach (var candidate in keys)
                if (candidate.Path == readerPath) { selected = candidate; break; }
            if (selected == null)
            {
                ClearKeys(keys);
                throw new InvalidDataException("The authenticated PBZS reader path is no longer available for this game build.");
            }
            byte[] key = selected.Key;
            byte[] encoded;
            try
            {
                encoded = Seal(expected, readerPath, key);
                if (!Same(expected, Unseal(encoded, readerPath, key)))
                    throw new InvalidDataException("PBZ settings verification failed.");
            }
            finally { ClearKeys(keys); }

            string temp = path + ".pbzforge-" + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllBytes(temp, encoded);
                var verified = Load(temp, executable);
                if (verified.ProfileId != profile || !Same(expected, verified.plain))
                    throw new InvalidDataException("PBZ settings read-back verification failed.");
                EnsureGameClosed();
                if (!Same(original, File.ReadAllBytes(path)))
                    throw new IOException("PBZ settings changed before writing; load the save again.");
                string backup = Backup(path);
                File.Replace(temp, path, null);
                return backup;
            }
            finally { if (File.Exists(temp)) File.Delete(temp); }
        }

        public static string BackupDirectory()
        {
            string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PBZKeyForge", "backups");
            Directory.CreateDirectory(dir);
            return dir;
        }

        static string Backup(string target)
        {
            string file = Path.GetFileNameWithoutExtension(target) + "." + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff")
                          + "-" + Guid.NewGuid().ToString("N").Substring(0, 8) + ".sav";
            string backup = Path.Combine(BackupDirectory(), file);
            File.Copy(target, backup, false);
            return backup;
        }

        static void EnsureGameClosed()
        {
            var processes = Process.GetProcessesByName("PBZ");
            try { if (processes.Length > 0) throw new IOException("Close Phantom Blade Zero before activating a scheme."); }
            finally { foreach (var process in processes) process.Dispose(); }
        }

        static bool Same(byte[] a, byte[] b)
        {
            if (a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
            return true;
        }

        static byte[] Unseal(byte[] encoded, ReaderPath path, byte[] key)
        {
            if (encoded.Length < path.CipherOffset || encoded.Length > 1024 * 1024)
                throw new InvalidDataException("Unsupported PBZ settings container size.");
            for (int i = 0; i < path.Header.Length; i++)
                if (encoded[i] != path.Header[i]) throw new InvalidDataException("PBZS header does not match this verified reader path.");
            if (path.NonceOffset < path.Header.Length || path.TagOffset < path.NonceOffset + 12
                || path.CipherOffset < path.TagOffset + 16)
                throw new InvalidDataException("Invalid registered PBZS container layout.");
            byte[] nonce = new byte[12], tag = new byte[16], cipher = new byte[encoded.Length - path.CipherOffset];
            Buffer.BlockCopy(encoded, path.NonceOffset, nonce, 0, nonce.Length);
            Buffer.BlockCopy(encoded, path.TagOffset, tag, 0, tag.Length);
            Buffer.BlockCopy(encoded, path.CipherOffset, cipher, 0, cipher.Length);
            return Gcm(cipher, key, nonce, tag, false);
        }

        static byte[] Seal(byte[] plain, ReaderPath path, byte[] key)
        {
            byte[] nonce = new byte[12], tag = new byte[16];
            using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(nonce);
            byte[] cipher = Gcm(plain, key, nonce, tag, true);
            byte[] encoded = new byte[path.CipherOffset + cipher.Length];
            Buffer.BlockCopy(path.Header, 0, encoded, 0, path.Header.Length);
            Buffer.BlockCopy(nonce, 0, encoded, path.NonceOffset, 12);
            Buffer.BlockCopy(tag, 0, encoded, path.TagOffset, 16);
            Buffer.BlockCopy(cipher, 0, encoded, path.CipherOffset, cipher.Length);
            return encoded;
        }

        // Windows CNG supplies AES-GCM on Windows 10/11 without another runtime or library.
        static byte[] Gcm(byte[] input, byte[] secret, byte[] nonce, byte[] tag, bool encrypt)
        {
            IntPtr algorithm = IntPtr.Zero, key = IntPtr.Zero;
            GCHandle noncePin = default(GCHandle), tagPin = default(GCHandle);
            try
            {
                Check(BCryptOpenAlgorithmProvider(out algorithm, "AES", null, 0));
                byte[] mode = Encoding.Unicode.GetBytes("ChainingModeGCM\0");
                Check(BCryptSetProperty(algorithm, "ChainingMode", mode, mode.Length, 0));
                Check(BCryptGenerateSymmetricKey(algorithm, out key, IntPtr.Zero, 0, secret, secret.Length, 0));
                noncePin = GCHandle.Alloc(nonce, GCHandleType.Pinned);
                tagPin = GCHandle.Alloc(tag, GCHandleType.Pinned);
                var info = new AuthInfo {
                    Size = Marshal.SizeOf(typeof(AuthInfo)), Version = 1,
                    Nonce = noncePin.AddrOfPinnedObject(), NonceLength = nonce.Length,
                    Tag = tagPin.AddrOfPinnedObject(), TagLength = tag.Length
                };
                byte[] output = new byte[input.Length];
                int written;
                int status = encrypt
                    ? BCryptEncrypt(key, input, input.Length, ref info, IntPtr.Zero, 0, output, output.Length, out written, 0)
                    : BCryptDecrypt(key, input, input.Length, ref info, IntPtr.Zero, 0, output, output.Length, out written, 0);
                if (status != 0) throw new CryptographicException("PBZ settings authentication failed or this game build is unsupported.");
                if (written != output.Length) throw new CryptographicException("Unexpected PBZ settings payload length.");
                return output;
            }
            finally
            {
                if (noncePin.IsAllocated) noncePin.Free();
                if (tagPin.IsAllocated) tagPin.Free();
                if (key != IntPtr.Zero) BCryptDestroyKey(key);
                if (algorithm != IntPtr.Zero) BCryptCloseAlgorithmProvider(algorithm, 0);
            }
        }

        static void Check(int status)
        {
            if (status != 0) throw new CryptographicException("Windows AES-GCM operation failed (0x" + status.ToString("X8") + ").");
        }

        [StructLayout(LayoutKind.Sequential)]
        struct AuthInfo
        {
            public int Size, Version;
            public IntPtr Nonce;
            public int NonceLength;
            public IntPtr AuthData;
            public int AuthDataLength;
            public IntPtr Tag;
            public int TagLength;
            public IntPtr MacContext;
            public int MacContextLength, AadLength;
            public long DataLength;
            public int Flags;
        }

        [DllImport("bcrypt.dll", CharSet = CharSet.Unicode)]
        static extern int BCryptOpenAlgorithmProvider(out IntPtr algorithm, string id, string implementation, int flags);
        [DllImport("bcrypt.dll", CharSet = CharSet.Unicode)]
        static extern int BCryptSetProperty(IntPtr handle, string name, byte[] value, int length, int flags);
        [DllImport("bcrypt.dll")]
        static extern int BCryptGenerateSymmetricKey(IntPtr algorithm, out IntPtr key, IntPtr keyObject, int objectLength, byte[] secret, int secretLength, int flags);
        [DllImport("bcrypt.dll")]
        static extern int BCryptEncrypt(IntPtr key, byte[] input, int inputLength, ref AuthInfo info, IntPtr iv, int ivLength, byte[] output, int outputLength, out int written, int flags);
        [DllImport("bcrypt.dll")]
        static extern int BCryptDecrypt(IntPtr key, byte[] input, int inputLength, ref AuthInfo info, IntPtr iv, int ivLength, byte[] output, int outputLength, out int written, int flags);
        [DllImport("bcrypt.dll")]
        static extern int BCryptDestroyKey(IntPtr key);
        [DllImport("bcrypt.dll")]
        static extern int BCryptCloseAlgorithmProvider(IntPtr algorithm, int flags);
    }
}
