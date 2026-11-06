using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using PBZKeyForge;

static class AuthenticatedSchemeFixture
{
    static readonly byte[] Key = Enumerable.Range(1, 32).Select(i => (byte)(i * 3)).ToArray();
    public static string Shared(string root) { return Path.Combine(root, @"ProjectPBZ\Saved\PBZSharedSetting.sav"); }
    public static void Create(string root, string scheme)
    {
        var image = new byte[0x2400];
        image[0] = 77; image[1] = 90; Put(image, 60, 0x80); Put(image, 0x80, 0x4550);
        image[0x84] = 0x64; image[0x85] = 0x86; image[0x86] = 2; image[0x94] = 0xf0; image[0x98] = 0x0b; image[0x99] = 2;
        Section(image, 0x188, 0x400, 0x1000, true); Section(image, 0x1b0, 0x1400, 0x2000, false);
        byte[] pattern = { 0xf3,0x0f,0x6f,0x05,0,0,0,0,0xf3,0x0f,0x6f,0x0d,0,0,0,0,
            0xf3,0x0f,0x6f,0x15,0,0,0,0,0x0f,0x57,0xc8,0xf3,0x0f,0x6f,0x05,0,0,0,0,
            0xf3,0x0f,0x7f,0x0a,0x0f,0x57,0xd0,0xf3,0x0f,0x7f,0x52,0x10,0xb0,0x01,0xc3 };
        pattern.CopyTo(image, 0x480);
        int[] displacements = { 4, 12, 20, 31 };
        for (int part = 0; part < 4; part++)
        {
            Put(image, 0x480 + displacements[part], (uint)(0x2000 + part * 16 - 0x1080 - displacements[part] - 4));
            for (int i = 0; i < 16; i++) image[0x1400 + part * 16 + i] = part % 2 == 0 ? (byte)0 : Key[part / 2 * 16 + i];
        }
        string exe = Path.Combine(root, @"platform\steam\PBZ.exe"); Directory.CreateDirectory(Path.GetDirectoryName(exe)); File.WriteAllBytes(exe, image);
        Write(root, scheme);
        GameSchemeSettings.Load(Shared(root), exe);
    }
    public static void Write(string root, string scheme)
    {
        byte[] text = Encoding.UTF8.GetBytes("{\"Unrelated\":123,\"KeyBindingScheme\":\"" + scheme + "\"}");
        byte[] plain = new byte[text.Length + 5]; Put(plain, 0, (uint)(text.Length + 1)); text.CopyTo(plain, 4);
        byte[] nonce = Enumerable.Range(1, 12).Select(i => (byte)i).ToArray(), tag = new byte[16];
        byte[] cipher = (byte[])typeof(GameSchemeSettings).GetMethod("Gcm", BindingFlags.Static | BindingFlags.NonPublic)
            .Invoke(null, new object[] { plain, Key, nonce, tag, true });
        byte[] encoded = new byte[cipher.Length + 36]; new byte[] { 80,66,90,83,1,1,0,0 }.CopyTo(encoded, 0);
        nonce.CopyTo(encoded, 8); tag.CopyTo(encoded, 20); cipher.CopyTo(encoded, 36);
        Directory.CreateDirectory(Path.GetDirectoryName(Shared(root))); File.WriteAllBytes(Shared(root), encoded);
    }
    static void Section(byte[] image, int at, uint raw, uint rva, bool executable)
    {
        Encoding.ASCII.GetBytes(executable ? "code" : "data").CopyTo(image, at);
        Put(image, at + 8, 0x1000); Put(image, at + 12, rva); Put(image, at + 16, 0x1000); Put(image, at + 20, raw);
        Put(image, at + 36, executable ? 0x60000020u : 0x40000040u);
    }
    static void Put(byte[] bytes, int at, uint value) { BitConverter.GetBytes(value).CopyTo(bytes, at); }
}
