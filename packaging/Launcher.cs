using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Threading;
using System.Windows.Forms;

namespace VoiceMagicPackage
{
    static class Launcher
    {
        static string Hash(Stream stream)
        {
            using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
        }
        static bool SameFile(string first, string second)
        {
            if (!File.Exists(second) || new FileInfo(first).Length != new FileInfo(second).Length) return false;
            using (var a = File.OpenRead(first)) using (var b = File.OpenRead(second)) return Hash(a) == Hash(b);
        }
        static string InstallLauncher(string folder)
        {
            string source = Assembly.GetExecutingAssembly().Location;
            string destination = Path.Combine(folder, "VoiceMagic.exe");
            if (string.Equals(source, destination, StringComparison.OrdinalIgnoreCase)) return destination;
            if (!SameFile(source, destination))
            {
                string temporary = destination + "." + Guid.NewGuid().ToString("N") + ".new";
                File.Copy(source, temporary);
                if (File.Exists(destination)) File.Replace(temporary, destination, null); else File.Move(temporary, destination);
            }
            return destination;
        }
        static string ExtractPayload(string folder, bool verificationOnly)
        {
            using (var payload = Assembly.GetExecutingAssembly().GetManifestResourceStream("VoiceMagic.Package.zip"))
            {
                if (payload == null || Hash(payload) != PackageIdentity.PayloadSha256) throw new InvalidDataException("程序包完整性检查失败，请重新获取 EXE。");
                payload.Position = 0;
                string runtime = Path.Combine(folder, "runtime", PackageIdentity.PayloadSha256);
                string marker = Path.Combine(runtime, ".complete");
                if (verificationOnly) { ValidateArchive(payload, null); return runtime; }
                bool complete = File.Exists(marker) && File.ReadAllText(marker) == PackageIdentity.PayloadSha256
                    && File.Exists(Path.Combine(runtime, "VoiceMagic.exe")) && File.Exists(Path.Combine(runtime, "NAudio.dll"))
                    && File.Exists(Path.Combine(runtime, "tools", "SoundVolumeView.exe"));
                if (!complete)
                {
                    Directory.CreateDirectory(runtime);
                    ValidateArchive(payload, runtime);
                    File.WriteAllText(marker, PackageIdentity.PayloadSha256);
                }
                return runtime;
            }
        }
        static void ValidateArchive(Stream payload, string output)
        {
            using (var archive = new ZipArchive(payload, ZipArchiveMode.Read, true))
            {
                string root = output == null ? null : Path.GetFullPath(output) + Path.DirectorySeparatorChar;
                bool application = false, audio = false, routing = false;
                foreach (var entry in archive.Entries)
                {
                    string relative = entry.FullName.Replace('/', Path.DirectorySeparatorChar);
                    if (Path.IsPathRooted(relative) || relative.Split(Path.DirectorySeparatorChar).Any(part => part == "..")) throw new InvalidDataException("程序包包含无效路径。");
                    if (relative == "VoiceMagic.exe") application = true;
                    if (relative == "NAudio.dll") audio = true;
                    if (relative == "tools" + Path.DirectorySeparatorChar + "SoundVolumeView.exe") routing = true;
                    if (output == null || entry.Name.Length == 0) continue;
                    string destination = Path.GetFullPath(Path.Combine(output, relative));
                    if (!destination.StartsWith(root, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("程序包路径超出运行目录。");
                    Directory.CreateDirectory(Path.GetDirectoryName(destination));
                    using (var source = entry.Open()) using (var target = File.Create(destination)) source.CopyTo(target);
                }
                if (!application || !audio || !routing) throw new InvalidDataException("程序包缺少必要组件。");
            }
        }
        static string Quote(string argument)
        {
            // Windows CRT quoting, including embedded quotes and trailing backslashes.
            var value = new System.Text.StringBuilder("\""); int slashes = 0;
            foreach (char character in argument)
            {
                if (character == '\\') { slashes++; continue; }
                if (character == '"') { value.Append('\\', slashes * 2 + 1); value.Append('"'); }
                else { value.Append('\\', slashes); value.Append(character); }
                slashes = 0;
            }
            value.Append('\\', slashes * 2); value.Append('"'); return value.ToString();
        }
        [STAThread]
        static int Main(string[] args)
        {
            bool checking = args.Contains("--package-check"), installOnly = args.Contains("--install-only");
            try
            {
                string folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VoiceMagic");
                using (var mutex = new Mutex(false, "Local\\VoiceMagic.Package"))
                {
                    bool owner;
                    try { owner = mutex.WaitOne(15000); } catch (AbandonedMutexException) { owner = true; }
                    if (!owner) throw new IOException("另一个 VoiceMagic 正在初始化，请稍后重试。");
                    try
                    {
                        if (checking) { ExtractPayload(folder, true); return 0; }
                        Directory.CreateDirectory(folder);
                        string launcher = InstallLauncher(folder);
                        string runtime = ExtractPayload(folder, false);
                        if (installOnly) return 0;
                        var start = new ProcessStartInfo(Path.Combine(runtime, "VoiceMagic.exe"), string.Join(" ", args.Select(Quote))) {
                            UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Normal, WorkingDirectory = runtime };
                        start.EnvironmentVariables["VOICEMAGIC_LAUNCHER"] = launcher;
                        using (var process = Process.Start(start))
                        {
                            if (args.Contains("--restore")) { process.WaitForExit(); return process.ExitCode; }
                        }
                        return 0;
                    }
                    finally { mutex.ReleaseMutex(); }
                }
            }
            catch (Exception ex)
            {
                if (!checking && !installOnly && !args.Contains("--startup")) MessageBox.Show(ex.Message, "VoiceMagic 启动失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return 1;
            }
        }
    }
}
