// FullOptimizer - optimizador de Windows segun el hardware.
// C# 5 / .NET Framework 4.x (viene con Windows 10/11): un solo .exe portable.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Management;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.ServiceProcess;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;

[assembly: AssemblyTitle("FullOptimizer")]
[assembly: AssemblyProduct("FullOptimizer")]
[assembly: AssemblyDescription("Optimizador de Windows segun el hardware")]
[assembly: AssemblyCopyright("FullOptimizer")]
[assembly: AssemblyVersion("1.0.0.0")]
[assembly: AssemblyFileVersion("1.0.0.0")]

namespace FullOptimizer
{
    static class Program
    {
        [STAThread]
        static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            var id = WindowsIdentity.GetCurrent();
            if (!new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator))
            {
                try { Process.Start(new ProcessStartInfo(Application.ExecutablePath) { Verb = "runas", UseShellExecute = true }); }
                catch { MessageBox.Show("FullOptimizer necesita permisos de administrador.", "FullOptimizer"); }
                return;
            }
            Application.Run(new MainForm());
        }
    }

    // ------------------------------------------------------------------ Hardware
    class HwInfo
    {
        public string Cpu = "Desconocido";
        public int Cores, Threads;
        public double RamGB;
        public bool Ssd;
        public string DiskKind = "Desconocido", DiskModel = "";
        public string Gpu = "Desconocida";
        public bool HasBattery, IsLaptop;
        public int Printers;
        public string Os = "Windows";
        public int Build;
        public int Tier; // 0 baja, 1 media, 2 alta

        public string TierName { get { return Tier == 0 ? "Gama baja" : Tier == 1 ? "Gama media" : "Gama alta"; } }
        public bool LowRam { get { return RamGB <= 4.5; } }

        static List<ManagementObject> Q(string ns, string q)
        {
            var list = new List<ManagementObject>();
            try { using (var s = new ManagementObjectSearcher(ns, q)) foreach (ManagementObject o in s.Get()) list.Add(o); }
            catch { }
            return list;
        }
        static string S(ManagementObject o, string p)
        {
            try { var v = o[p]; return v == null ? "" : v.ToString().Trim(); } catch { return ""; }
        }

        public static HwInfo Detect()
        {
            var h = new HwInfo();
            int x;
            foreach (var c in Q(@"root\cimv2", "SELECT Name,NumberOfCores,NumberOfLogicalProcessors FROM Win32_Processor"))
            {
                if (h.Cpu == "Desconocido") h.Cpu = Regex.Replace(S(c, "Name"), @"\s+", " ");
                if (int.TryParse(S(c, "NumberOfCores"), out x)) h.Cores += x;
                if (int.TryParse(S(c, "NumberOfLogicalProcessors"), out x)) h.Threads += x;
            }
            if (h.Threads == 0) h.Threads = Environment.ProcessorCount;
            if (h.Cores == 0) h.Cores = h.Threads;

            foreach (var o in Q(@"root\cimv2", "SELECT TotalPhysicalMemory FROM Win32_ComputerSystem"))
            {
                double b; if (double.TryParse(S(o, "TotalPhysicalMemory"), out b)) h.RamGB = b / 1073741824.0;
            }

            // Disco del sistema
            try
            {
                char sys = Path.GetPathRoot(Environment.SystemDirectory)[0];
                string diskNum = null;
                foreach (var p in Q(@"root\Microsoft\Windows\Storage", "SELECT DiskNumber,DriveLetter FROM MSFT_Partition"))
                {
                    string dl = S(p, "DriveLetter");
                    if (dl.Equals(sys.ToString(), StringComparison.OrdinalIgnoreCase) || dl == ((int)char.ToUpper(sys)).ToString())
                    { diskNum = S(p, "DiskNumber"); break; }
                }
                foreach (var d in Q(@"root\Microsoft\Windows\Storage", "SELECT DeviceId,MediaType,BusType,FriendlyName FROM MSFT_PhysicalDisk"))
                {
                    if (diskNum != null && S(d, "DeviceId") != diskNum) continue;
                    string mt = S(d, "MediaType"), bt = S(d, "BusType");
                    h.DiskModel = S(d, "FriendlyName");
                    if (bt == "17") { h.Ssd = true; h.DiskKind = "SSD NVMe"; }
                    else if (mt == "4") { h.Ssd = true; h.DiskKind = "SSD"; }
                    else if (mt == "3") h.DiskKind = "HDD";
                    break;
                }
            }
            catch { }

            var gpus = new List<string>();
            foreach (var g in Q(@"root\cimv2", "SELECT Name FROM Win32_VideoController"))
            {
                string n = S(g, "Name"), l = n.ToLowerInvariant();
                if (n.Length == 0 || l.Contains("virtual") || l.Contains("basic display") || l.Contains("remote") || l.Contains("mirror")) continue;
                gpus.Add(n.Replace("(R)", "").Replace("(TM)", ""));
            }
            if (gpus.Count > 0) h.Gpu = string.Join(" + ", gpus);

            h.HasBattery = Q(@"root\cimv2", "SELECT Name FROM Win32_Battery").Count > 0;
            var laptopTypes = new ushort[] { 8, 9, 10, 14, 30, 31, 32 };
            foreach (var e in Q(@"root\cimv2", "SELECT ChassisTypes FROM Win32_SystemEnclosure"))
            {
                try { var arr = e["ChassisTypes"] as ushort[]; if (arr != null && arr.Any(t => laptopTypes.Contains(t))) h.IsLaptop = true; } catch { }
            }
            if (h.HasBattery) h.IsLaptop = true;

            foreach (var p in Q(@"root\cimv2", "SELECT Name,PortName FROM Win32_Printer"))
            {
                string n = S(p, "Name").ToLowerInvariant(), port = S(p, "PortName").ToUpperInvariant();
                if (n.Contains("pdf") || n.Contains("xps") || n.Contains("onenote") || n.Contains("fax") || port == "PORTPROMPT:" || port.StartsWith("NUL")) continue;
                h.Printers++;
            }

            foreach (var o in Q(@"root\cimv2", "SELECT Caption,BuildNumber FROM Win32_OperatingSystem"))
            {
                h.Os = S(o, "Caption").Replace("Microsoft ", "");
                int.TryParse(S(o, "BuildNumber"), out h.Build);
            }

            int score = 0;
            if (h.RamGB >= 15) score += 2; else if (h.RamGB >= 7) score += 1;
            if (h.Threads >= 8) score += 2; else if (h.Threads >= 4) score += 1;
            if (h.Ssd) score += 1;
            h.Tier = score <= 2 ? 0 : score <= 4 ? 1 : 2;
            return h;
        }
    }

    // ------------------------------------------------------------------ Motor (cambios + respaldo)
    class Engine
    {
        readonly Action<string> log;
        StreamWriter bk;
        public string BackupFile;
        public Engine(Action<string> log) { this.log = log; }
        public void Log(string s) { if (log != null) log(s); }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        static extern int SHEmptyRecycleBin(IntPtr hwnd, string root, uint flags);

        public static string[] BackupDirs()
        {
            return new[] {
                Path.Combine(Path.GetDirectoryName(Application.ExecutablePath), "FullOptimizer-Respaldos"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "FullOptimizer", "Respaldos") };
        }
        static string WritableBackupDir()
        {
            foreach (var d in BackupDirs())
            {
                try { Directory.CreateDirectory(d); string t = Path.Combine(d, ".test"); File.WriteAllText(t, ""); File.Delete(t); return d; }
                catch { }
            }
            return Path.GetTempPath();
        }
        public static string LatestBackup()
        {
            var files = new List<string>();
            foreach (var d in BackupDirs()) if (Directory.Exists(d)) files.AddRange(Directory.GetFiles(d, "respaldo-*.txt"));
            return files.OrderByDescending(f => Path.GetFileName(f)).FirstOrDefault();
        }

        public void BeginBackup()
        {
            BackupFile = Path.Combine(WritableBackupDir(), "respaldo-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".txt");
            bk = new StreamWriter(BackupFile, false, new UTF8Encoding(false));
            bk.AutoFlush = true;
            bk.WriteLine("#FullOptimizer respaldo " + DateTime.Now.ToString("s"));
        }
        public void EndBackup()
        {
            if (bk == null) return;
            bk.Dispose(); bk = null;
            if (File.ReadAllLines(BackupFile).Length <= 1) { try { File.Delete(BackupFile); } catch { } }
        }
        void Backup(string line) { if (bk != null) bk.WriteLine(line); }
        public void Undo(string exe, string args) { Backup("CMD\t" + exe + "\t" + args); }

        // ---- Registro
        static RegistryKey Root(string hive)
        {
            return RegistryKey.OpenBaseKey(hive == "HKCU" ? RegistryHive.CurrentUser : RegistryHive.LocalMachine, RegistryView.Registry64);
        }
        static string Enc(object v, RegistryValueKind k)
        {
            switch (k)
            {
                case RegistryValueKind.Binary: return BitConverter.ToString((byte[])v).Replace("-", "");
                case RegistryValueKind.MultiString: return string.Join("\u0001", (string[])v);
                case RegistryValueKind.DWord: return Convert.ToInt32(v).ToString(CultureInfo.InvariantCulture);
                case RegistryValueKind.QWord: return Convert.ToInt64(v).ToString(CultureInfo.InvariantCulture);
                default: return v.ToString();
            }
        }
        static object Dec(string s, RegistryValueKind k)
        {
            switch (k)
            {
                case RegistryValueKind.DWord: return unchecked((int)long.Parse(s, CultureInfo.InvariantCulture));
                case RegistryValueKind.QWord: return long.Parse(s, CultureInfo.InvariantCulture);
                case RegistryValueKind.Binary:
                    var b = new byte[s.Length / 2];
                    for (int i = 0; i < b.Length; i++) b[i] = Convert.ToByte(s.Substring(i * 2, 2), 16);
                    return b;
                case RegistryValueKind.MultiString: return s.Split('\u0001');
                default: return s;
            }
        }
        public static object GetReg(string hive, string path, string name)
        {
            using (var r = Root(hive)) using (var k = r.OpenSubKey(path, false))
                return k == null ? null : k.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
        }
        public void SetReg(string hive, string path, string name, object value, RegistryValueKind kind)
        {
            using (var r = Root(hive))
            {
                string line;
                using (var k = r.OpenSubKey(path, false))
                {
                    object old = k == null ? null : k.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
                    if (old == null) line = "REG\t" + hive + "\t" + path + "\t" + name + "\tABSENT\t";
                    else { var ok = k.GetValueKind(name); line = "REG\t" + hive + "\t" + path + "\t" + name + "\t" + ok + "\t" + Enc(old, ok); }
                }
                using (var k = r.CreateSubKey(path)) k.SetValue(name, value, kind);
                Backup(line);
            }
        }
        public void Dw(string hive, string path, string name, int v) { SetReg(hive, path, name, v, RegistryValueKind.DWord); }
        public void Sz(string hive, string path, string name, string v) { SetReg(hive, path, name, v, RegistryValueKind.String); }

        // ---- Servicios
        static string StartWord(int start, bool delayed)
        {
            return start == 2 ? (delayed ? "delayed-auto" : "auto") : start == 3 ? "demand" : start == 4 ? "disabled" : null;
        }
        public void SetService(string name, int start)
        {
            string p = @"SYSTEM\CurrentControlSet\Services\" + name;
            object o = GetReg("HKLM", p, "Start");
            if (o == null) { Log("   · " + name + ": no existe en este equipo"); return; }
            int old = Convert.ToInt32(o);
            object dl = GetReg("HKLM", p, "DelayedAutostart");
            int delayed = dl == null ? 0 : Convert.ToInt32(dl);
            if (old == start) { Log("   · " + name + ": ya estaba bien configurado"); return; }
            if (StartWord(old, false) == null) { Log("   · " + name + ": omitido (servicio de arranque del sistema)"); return; }
            Run("sc.exe", "config \"" + name + "\" start= " + StartWord(start, false), 30);
            object now = GetReg("HKLM", p, "Start");
            if (now == null || Convert.ToInt32(now) != start) { Log("   · " + name + ": Windows no permitió cambiarlo (protegido)"); return; }
            Backup("SVC\t" + name + "\t" + old + "\t" + delayed);
            Log("   · " + name + " → " + (start == 4 ? "desactivado" : start == 3 ? "manual" : "automático"));
            if (start == 4)
            {
                try
                {
                    using (var sc = new ServiceController(name))
                        if (sc.Status == ServiceControllerStatus.Running && sc.CanStop) { sc.Stop(); sc.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(15)); }
                }
                catch { }
            }
        }

        // ---- Procesos
        public string Run(string exe, string args, int timeoutSec)
        {
            try
            {
                var enc = Encoding.GetEncoding(CultureInfo.CurrentCulture.TextInfo.OEMCodePage);
                var psi = new ProcessStartInfo(exe, args)
                {
                    UseShellExecute = false, CreateNoWindow = true,
                    RedirectStandardOutput = true, RedirectStandardError = true,
                    StandardOutputEncoding = enc, StandardErrorEncoding = enc
                };
                using (var p = Process.Start(psi))
                {
                    var o = p.StandardOutput.ReadToEndAsync();
                    var e = p.StandardError.ReadToEndAsync();
                    if (!p.WaitForExit(timeoutSec * 1000)) { try { p.Kill(); } catch { } return "(tiempo agotado)"; }
                    return (o.Result + "\n" + e.Result).Trim();
                }
            }
            catch (Exception ex) { return "Error: " + ex.Message; }
        }
        public string PS(string cmd, int timeoutSec)
        {
            return Run("powershell.exe", "-NoProfile -ExecutionPolicy Bypass -Command \"" + cmd.Replace("\"", "\\\"") + "\"", timeoutSec);
        }

        // ---- Archivos
        static void Walk(string dir, List<string> files, List<string> dirs)
        {
            try
            {
                files.AddRange(Directory.GetFiles(dir));
                foreach (var d in Directory.GetDirectories(dir))
                {
                    dirs.Add(d);
                    if ((new DirectoryInfo(d).Attributes & FileAttributes.ReparsePoint) == 0) Walk(d, files, dirs);
                }
            }
            catch { }
        }
        public long CleanDir(string dir, Func<string, bool> filter)
        {
            long freed = 0;
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return 0;
            var files = new List<string>(); var dirs = new List<string>();
            Walk(dir, files, dirs);
            foreach (var f in files)
            {
                if (filter != null && !filter(f)) continue;
                try { var fi = new FileInfo(f); long l = fi.Length; fi.Attributes = FileAttributes.Normal; fi.Delete(); freed += l; } catch { }
            }
            if (filter == null)
                foreach (var d in dirs.OrderByDescending(d => d.Length)) { try { Directory.Delete(d, false); } catch { } }
            return freed;
        }
        public static string MB(long b) { return (b / 1048576.0).ToString("N0") + " MB"; }
        public void EmptyRecycleBin() { SHEmptyRecycleBin(IntPtr.Zero, null, 7); }

        public void CreateRestorePoint()
        {
            try { Dw("HKLM", @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\SystemRestore", "SystemRestorePointCreationFrequency", 0); } catch { }
            string drive = Environment.GetEnvironmentVariable("SystemDrive") ?? "C:";
            string r = PS("Enable-ComputerRestore -Drive '" + drive + "\\'; Checkpoint-Computer -Description 'FullOptimizer' -RestorePointType MODIFY_SETTINGS -ErrorAction Stop; 'OK'", 300);
            Log(r.EndsWith("OK") ? "   Punto de restauración creado." : "   No se pudo crear el punto de restauración (se sigue con el respaldo propio).");
        }

        // ---- Restaurar
        public void Restore(string file)
        {
            var lines = File.ReadAllLines(file).Where(l => l.Length > 0 && !l.StartsWith("#")).Reverse().ToList();
            int ok = 0;
            foreach (var l in lines)
            {
                var f = l.Split('\t');
                try
                {
                    if (f[0] == "REG")
                    {
                        using (var r = Root(f[1]))
                        {
                            if (f[4] == "ABSENT") { using (var k = r.OpenSubKey(f[2], true)) if (k != null) k.DeleteValue(f[3], false); }
                            else
                            {
                                var kind = (RegistryValueKind)Enum.Parse(typeof(RegistryValueKind), f[4]);
                                using (var k = r.CreateSubKey(f[2])) k.SetValue(f[3], Dec(f.Length > 5 ? f[5] : "", kind), kind);
                            }
                        }
                    }
                    else if (f[0] == "SVC")
                    {
                        string w = StartWord(int.Parse(f[2]), f[3] == "1");
                        if (w != null) Run("sc.exe", "config \"" + f[1] + "\" start= " + w, 30);
                        Log("   · servicio " + f[1] + " restaurado");
                    }
                    else if (f[0] == "CMD") { Run(f[1], f[2], 600); Log("   · " + f[1] + " " + f[2]); }
                    ok++;
                }
                catch (Exception ex) { Log("   ! No se pudo restaurar: " + l.Replace('\t', ' ') + " (" + ex.Message + ")"); }
            }
            File.Move(file, file + ".restaurado");
            Log("Restaurados " + ok + " de " + lines.Count + " cambios.");
        }
    }

    // ------------------------------------------------------------------ Ajustes
    class Tweak
    {
        public string Cat, Name, Desc;
        public Func<HwInfo, int> Level;            // 0 = no aplica a este equipo; 1..4 = perfil mínimo
        public Action<Engine, HwInfo, int> Apply;  // int = perfil elegido
        public Tweak(string cat, string name, string desc, Func<HwInfo, int> level, Action<Engine, HwInfo, int> apply)
        { Cat = cat; Name = name; Desc = desc; Level = level; Apply = apply; }
        public override string ToString() { return Cat + "  ·  " + Name; }
    }

    static class Catalog
    {
        public const int Lite = 1, Seguro = 2, Recomendado = 3, Extremo = 4;
        const string HKCU = "HKCU", HKLM = "HKLM";

        static void Svcs(Engine e, int start, params string[] names) { foreach (var n in names) e.SetService(n, start); }

        public static List<Tweak> All()
        {
            var t = new List<Tweak>();

            // ---------------- Limpieza
            t.Add(new Tweak("Limpieza", "Archivos temporales", "Borra las carpetas temporales del usuario y de Windows. Lo que esté en uso se omite.",
                h => Lite, (e, h, p) =>
                {
                    long b = e.CleanDir(Path.GetTempPath(), null) + e.CleanDir(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Temp"), null);
                    e.Log("   Liberado: " + Engine.MB(b));
                }));
            t.Add(new Tweak("Limpieza", "Papelera de reciclaje", "Vacía la papelera de todas las unidades.",
                h => Lite, (e, h, p) => e.EmptyRecycleBin()));
            t.Add(new Tweak("Limpieza", "Caché de Windows Update", "Borra instaladores de actualizaciones ya aplicadas. Windows vuelve a descargar lo que necesite.",
                h => Lite, (e, h, p) =>
                {
                    var running = new List<ServiceController>();
                    foreach (var n in new[] { "wuauserv", "bits", "dosvc" })
                    {
                        try { var sc = new ServiceController(n); if (sc.Status == ServiceControllerStatus.Running) { sc.Stop(); sc.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(30)); running.Add(sc); } } catch { }
                    }
                    long b = e.CleanDir(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), @"SoftwareDistribution\Download"), null);
                    foreach (var sc in running) { try { sc.Start(); } catch { } }
                    e.Log("   Liberado: " + Engine.MB(b));
                }));
            t.Add(new Tweak("Limpieza", "Caché DNS", "Renueva la caché de nombres de red. Útil si alguna web carga mal.",
                h => Lite, (e, h, p) => e.Run("ipconfig.exe", "/flushdns", 30)));
            t.Add(new Tweak("Limpieza", "Caché de miniaturas e íconos", "Elimina miniaturas viejas; Windows las regenera al abrir carpetas.",
                h => Seguro, (e, h, p) =>
                {
                    string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Microsoft\Windows\Explorer");
                    long b = e.CleanDir(dir, f => { var n = Path.GetFileName(f).ToLowerInvariant(); return n.StartsWith("thumbcache_") || n.StartsWith("iconcache_"); });
                    e.Log("   Liberado: " + Engine.MB(b) + " (los archivos en uso se borran al reiniciar)");
                }));
            t.Add(new Tweak("Limpieza", "Componentes antiguos de Windows (DISM)", "Quita versiones viejas de componentes que dejan las actualizaciones. Puede tardar varios minutos.",
                h => Recomendado, (e, h, p) =>
                {
                    string r = e.Run("dism.exe", "/Online /Cleanup-Image /StartComponentCleanup /NoRestart", 3600);
                    var last = r.Split('\n').Select(s => s.Trim()).LastOrDefault(s => s.Length > 0 && !s.Contains("%"));
                    e.Log("   " + (last ?? "Terminado"));
                }));

            // ---------------- Disco
            t.Add(new Tweak("Disco", "Optimizar unidad del sistema", "TRIM en SSD / desfragmentación en HDD. Windows elige automáticamente lo correcto y sano para tu disco.",
                h => Lite, (e, h, p) =>
                {
                    string drive = Environment.GetEnvironmentVariable("SystemDrive") ?? "C:";
                    e.Run("defrag.exe", drive + " /O", 3600);
                    e.Log("   " + (h.Ssd ? "TRIM aplicado al SSD." : "Unidad optimizada."));
                }));
            t.Add(new Tweak("Disco", "Indexación de búsqueda (Windows Search)", "Detiene el indexador que lee el disco en segundo plano. Buscar en Inicio sigue funcionando, solo que más lento dentro de archivos.",
                h => (!h.Ssd || h.LowRam) ? Recomendado : Extremo, (e, h, p) => e.SetService("WSearch", 4)));
            t.Add(new Tweak("Disco", "Registro de último acceso NTFS", "Evita que Windows escriba la fecha en cada archivo que se lee. Menos escrituras al disco.",
                h => Recomendado, (e, h, p) =>
                {
                    var m = Regex.Match(e.Run("fsutil.exe", "behavior query disablelastaccess", 30), @"=\s*(\d+)");
                    string old = m.Success ? m.Groups[1].Value : "2";
                    if (old == "1" || old == "3") { e.Log("   Ya estaba desactivado."); return; }
                    e.Run("fsutil.exe", "behavior set disablelastaccess 1", 30);
                    e.Undo("fsutil.exe", "behavior set disablelastaccess " + old);
                }));

            // ---------------- Memoria
            t.Add(new Tweak("Memoria", "Compresión de memoria", "Con poca RAM la activa (cabe más en memoria y se usa menos el disco). Con 16 GB o más la desactiva para ahorrar CPU.",
                h => h.RamGB <= 8.5 ? Seguro : h.RamGB >= 15.5 ? Extremo : 0, (e, h, p) =>
                {
                    bool on = e.PS("(Get-MMAgent).MemoryCompression", 60).Trim().StartsWith("True");
                    bool want = h.RamGB <= 8.5;
                    if (on == want) { e.Log("   Ya estaba " + (on ? "activada" : "desactivada") + "."); return; }
                    e.PS((want ? "Enable" : "Disable") + "-MMAgent -MemoryCompression", 60);
                    e.Undo("powershell.exe", "-NoProfile -Command " + (want ? "Disable" : "Enable") + "-MMAgent -MemoryCompression");
                    e.Log("   Compresión " + (want ? "activada" : "desactivada") + " (efectivo al reiniciar).");
                }));
            t.Add(new Tweak("Memoria", "SysMain (Superfetch)", "En un SSD no acelera casi nada y mantiene el disco y la RAM ocupados precargando apps.",
                h => !h.Ssd ? 0 : h.Tier == 2 ? Extremo : Recomendado, (e, h, p) => e.SetService("SysMain", 4)));

            // ---------------- Segundo plano
            t.Add(new Tweak("Segundo plano", "Navegadores cerrados de verdad", "Edge y Chrome dejan de quedar abiertos en segundo plano al cerrarlos (Edge puede mostrar \"administrado por la organización\").",
                h => Seguro, (e, h, p) =>
                {
                    e.Dw(HKLM, @"SOFTWARE\Policies\Microsoft\Edge", "StartupBoostEnabled", 0);
                    e.Dw(HKLM, @"SOFTWARE\Policies\Microsoft\Edge", "BackgroundModeEnabled", 0);
                    e.Dw(HKLM, @"SOFTWARE\Policies\Google\Chrome", "BackgroundModeEnabled", 0);
                }));
            t.Add(new Tweak("Segundo plano", "Descargas compartidas P2P de Windows Update", "Windows deja de usar tu disco y red para enviar actualizaciones a otros equipos.",
                h => Seguro, (e, h, p) => e.Dw(HKLM, @"SOFTWARE\Policies\Microsoft\Windows\DeliveryOptimization", "DODownloadMode", 0)));
            t.Add(new Tweak("Segundo plano", "Apps de la Tienda en segundo plano", "Las apps de la Microsoft Store no corren si no están abiertas. En Extremo se fuerza por directiva.",
                h => Recomendado, (e, h, p) =>
                {
                    e.Dw(HKCU, @"Software\Microsoft\Windows\CurrentVersion\BackgroundAccessApplications", "GlobalUserDisabled", 1);
                    e.Dw(HKCU, @"Software\Microsoft\Windows\CurrentVersion\Search", "BackgroundAppGlobalToggle", 0);
                    if (p >= Extremo) e.Dw(HKLM, @"SOFTWARE\Policies\Microsoft\Windows\AppPrivacy", "LetAppsRunInBackground", 2);
                }));
            t.Add(new Tweak("Segundo plano", "Widgets y Copilot", "Quita el panel de Widgets (noticias/clima) y Copilot, que consumen RAM y red. El botón de Widgets desaparece de la barra.",
                h => Extremo, (e, h, p) =>
                {
                    e.Dw(HKLM, @"SOFTWARE\Policies\Microsoft\Dsh", "AllowNewsAndInterests", 0);
                    e.Dw(HKCU, @"Software\Policies\Microsoft\Windows\WindowsCopilot", "TurnOffWindowsCopilot", 1);
                }));
            t.Add(new Tweak("Segundo plano", "Grabación de juegos (Game DVR)", "Desactiva la grabación en segundo plano de la Xbox Game Bar, que consume CPU/GPU en juegos.",
                h => Seguro, (e, h, p) =>
                {
                    e.Dw(HKCU, @"System\GameConfigStore", "GameDVR_Enabled", 0);
                    e.Dw(HKCU, @"Software\Microsoft\Windows\CurrentVersion\GameDVR", "AppCaptureEnabled", 0);
                    e.Dw(HKLM, @"SOFTWARE\Policies\Microsoft\Windows\GameDVR", "AllowGameDVR", 0);
                }));

            // ---------------- Privacidad
            t.Add(new Tweak("Privacidad", "Telemetría", "Reduce los datos de diagnóstico y detiene el servicio que los envía constantemente.",
                h => Seguro, (e, h, p) =>
                {
                    bool ent = h.Os.Contains("Enterprise") || h.Os.Contains("Education");
                    e.Dw(HKLM, @"SOFTWARE\Policies\Microsoft\Windows\DataCollection", "AllowTelemetry", ent ? 0 : 1);
                    Svcs(e, 4, "DiagTrack", "dmwappushservice");
                }));
            t.Add(new Tweak("Privacidad", "Anuncios y sugerencias", "Quita publicidad, apps sugeridas que se instalan solas y recomendaciones del menú Inicio.",
                h => Seguro, (e, h, p) =>
                {
                    string cdm = @"Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager";
                    foreach (var v in new[] { "SilentInstalledAppsEnabled", "SystemPaneSuggestionsEnabled", "SoftLandingEnabled",
                        "SubscribedContent-310093Enabled", "SubscribedContent-338388Enabled", "SubscribedContent-338389Enabled",
                        "SubscribedContent-353694Enabled", "SubscribedContent-353696Enabled" })
                        e.Dw(HKCU, cdm, v, 0);
                    e.Dw(HKCU, @"Software\Microsoft\Windows\CurrentVersion\AdvertisingInfo", "Enabled", 0);
                    e.Dw(HKCU, @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "Start_IrisRecommendations", 0);
                    e.Dw(HKCU, @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "ShowSyncProviderNotifications", 0);
                }));
            t.Add(new Tweak("Privacidad", "Resultados web de Bing en Inicio", "El buscador del menú Inicio busca solo en tu PC: más rápido y sin consumir red.",
                h => Recomendado, (e, h, p) =>
                {
                    e.Dw(HKCU, @"Software\Microsoft\Windows\CurrentVersion\Search", "BingSearchEnabled", 0);
                    e.Dw(HKCU, @"Software\Policies\Microsoft\Windows\Explorer", "DisableSearchBoxSuggestions", 1);
                }));

            // ---------------- Rendimiento
            t.Add(new Tweak("Rendimiento", "Plan de energía de alto rendimiento", "El CPU sube de frecuencia sin demora. En Extremo y en PC de escritorio usa \"Máximo rendimiento\". En portátiles con batería solo se aplica en Extremo.",
                h => h.HasBattery ? Extremo : Recomendado, (e, h, p) =>
                {
                    var g = new Regex(@"[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}");
                    var cur = g.Match(e.Run("powercfg.exe", "/getactivescheme", 30));
                    string target = "8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c";
                    if (p >= Extremo && !h.IsLaptop)
                    {
                        var dup = g.Match(e.Run("powercfg.exe", "-duplicatescheme e9a42b02-d5df-448d-aa00-03f14749eb61", 30));
                        if (dup.Success) target = dup.Value;
                    }
                    string res = e.Run("powercfg.exe", "/setactive " + target, 30);
                    var now = g.Match(e.Run("powercfg.exe", "/getactivescheme", 30));
                    if (!now.Success || !now.Value.Equals(target, StringComparison.OrdinalIgnoreCase))
                    {
                        var dup = g.Match(e.Run("powercfg.exe", "-duplicatescheme 8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c", 30));
                        if (dup.Success) e.Run("powercfg.exe", "/setactive " + dup.Value, 30);
                    }
                    if (cur.Success) e.Undo("powercfg.exe", "/setactive " + cur.Value);
                    e.Log("   Plan activo: " + e.Run("powercfg.exe", "/getactivescheme", 30).Split(':').Last().Trim());
                }));
            t.Add(new Tweak("Rendimiento", "Menús y arranque de apps más rápidos", "Menús sin demora y los programas de inicio arrancan sin la espera artificial de Windows.",
                h => Seguro, (e, h, p) =>
                {
                    e.Sz(HKCU, @"Control Panel\Desktop", "MenuShowDelay", "100");
                    e.Dw(HKCU, @"Software\Microsoft\Windows\CurrentVersion\Explorer\Serialize", "StartupDelayInMSec", 0);
                }));
            t.Add(new Tweak("Rendimiento", "Prioridad a la app en primer plano (MMCSS)", "Reserva menos CPU para tareas de fondo. En Extremo también da prioridad máxima a juegos y quita el límite de red.",
                h => Recomendado, (e, h, p) =>
                {
                    string sp = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile";
                    e.Dw(HKLM, sp, "SystemResponsiveness", 10);
                    if (p >= Extremo)
                    {
                        e.Dw(HKLM, sp, "NetworkThrottlingIndex", unchecked((int)0xFFFFFFFF));
                        e.Dw(HKLM, sp + @"\Tasks\Games", "GPU Priority", 8);
                        e.Dw(HKLM, sp + @"\Tasks\Games", "Priority", 6);
                        e.Sz(HKLM, sp + @"\Tasks\Games", "Scheduling Category", "High");
                        e.Sz(HKLM, sp + @"\Tasks\Games", "SFIO Priority", "High");
                    }
                }));

            // ---------------- Servicios
            t.Add(new Tweak("Servicios", "Servicios innecesarios", "Mapas sin conexión, modo demo de tienda, fax, registro remoto e informes de errores. En Extremo también Xbox Live, seguimiento de vínculos y otros.",
                h => Recomendado, (e, h, p) =>
                {
                    Svcs(e, 4, "MapsBroker", "RetailDemo", "Fax", "RemoteRegistry");
                    Svcs(e, 3, "WerSvc");
                    if (p >= Extremo) { Svcs(e, 4, "XblAuthManager", "XblGameSave", "XboxNetApiSvc", "TrkWks", "WpcMonSvc", "PcaSvc", "wisvc"); }
                    else Svcs(e, 3, "XblAuthManager", "XblGameSave", "XboxNetApiSvc");
                }));
            t.Add(new Tweak("Servicios", "Cola de impresión", "No se detectó ninguna impresora: el servicio queda en manual. Si conectás una, vuelve a funcionar al restaurar.",
                h => h.Printers == 0 ? Extremo : 0, (e, h, p) => e.SetService("Spooler", 3)));

            // ---------------- Visual
            t.Add(new Tweak("Visual", "Efectos visuales en modo rendimiento", "Quita animaciones y sombras pero conserva fuentes suavizadas y miniaturas. Se ve al cerrar sesión.",
                h => h.Tier == 0 ? Recomendado : Extremo, (e, h, p) =>
                {
                    e.Dw(HKCU, @"Software\Microsoft\Windows\CurrentVersion\Explorer\VisualEffects", "VisualFXSetting", 3);
                    e.SetReg(HKCU, @"Control Panel\Desktop", "UserPreferencesMask", new byte[] { 0x90, 0x12, 0x03, 0x80, 0x10, 0, 0, 0 }, RegistryValueKind.Binary);
                    e.Sz(HKCU, @"Control Panel\Desktop", "FontSmoothing", "2");
                    e.Sz(HKCU, @"Control Panel\Desktop\WindowMetrics", "MinAnimate", "0");
                    e.Dw(HKCU, @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "TaskbarAnimations", 0);
                    e.Dw(HKCU, @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "IconsOnly", 0);
                    e.Dw(HKCU, @"Software\Microsoft\Windows\DWM", "EnableAeroPeek", 0);
                }));
            t.Add(new Tweak("Visual", "Transparencia de Windows", "Quita el efecto acrílico de la barra de tareas y menús (quedan opacos). Destildalo si querés conservar la estética.",
                h => h.Tier == 0 ? Recomendado : Extremo, (e, h, p) =>
                    e.Dw(HKCU, @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "EnableTransparency", 0)));

            // ---------------- Energía
            t.Add(new Tweak("Energía", "Hibernación", "Borra hiberfil.sys (varios GB) en PCs de escritorio. El apagado y el reposo normal siguen funcionando.",
                h => h.IsLaptop ? 0 : Extremo, (e, h, p) =>
                {
                    string f = Path.Combine((Environment.GetEnvironmentVariable("SystemDrive") ?? "C:") + "\\", "hiberfil.sys");
                    if (!File.Exists(f)) { e.Log("   Ya estaba desactivada."); return; }
                    e.Run("powercfg.exe", "/h off", 60);
                    e.Undo("powercfg.exe", "/h on");
                }));
            return t;
        }
    }

    // ------------------------------------------------------------------ Inicio de Windows
    class StartItem
    {
        public string Name, Command, Source, ApprovedHive, ApprovedPath;
        public bool Enabled;
    }
    static class Startup
    {
        const string SA = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\";
        static bool IsEnabled(string hive, string path, string name)
        {
            var v = Engine.GetReg(hive, path, name) as byte[];
            return v == null || v.Length == 0 || (v[0] & 1) == 0;
        }
        public static List<StartItem> Load()
        {
            var list = new List<StartItem>();
            var regs = new[] {
                new[] { "HKCU", @"Software\Microsoft\Windows\CurrentVersion\Run", "HKCU", SA + "Run", "Usuario" },
                new[] { "HKLM", @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", "HKLM", SA + "Run", "Todos los usuarios" },
                new[] { "HKLM", @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Run", "HKLM", SA + "Run32", "Todos (32 bits)" } };
            foreach (var r in regs)
            {
                try
                {
                    using (var root = RegistryKey.OpenBaseKey(r[0] == "HKCU" ? RegistryHive.CurrentUser : RegistryHive.LocalMachine, RegistryView.Registry64))
                    using (var k = root.OpenSubKey(r[1]))
                    {
                        if (k == null) continue;
                        foreach (var n in k.GetValueNames())
                        {
                            if (n.Length == 0) continue;
                            list.Add(new StartItem { Name = n, Command = Convert.ToString(k.GetValue(n)), Source = r[4], ApprovedHive = r[2], ApprovedPath = r[3], Enabled = IsEnabled(r[2], r[3], n) });
                        }
                    }
                }
                catch { }
            }
            var folders = new[] {
                new[] { Environment.GetFolderPath(Environment.SpecialFolder.Startup), "HKCU", "Carpeta Inicio" },
                new[] { Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup), "HKLM", "Carpeta Inicio (todos)" } };
            foreach (var f in folders)
            {
                try
                {
                    if (!Directory.Exists(f[0])) continue;
                    foreach (var file in Directory.GetFiles(f[0]))
                    {
                        string n = Path.GetFileName(file);
                        if (n.Equals("desktop.ini", StringComparison.OrdinalIgnoreCase)) continue;
                        list.Add(new StartItem { Name = n, Command = file, Source = f[2], ApprovedHive = f[1], ApprovedPath = SA + "StartupFolder", Enabled = IsEnabled(f[1], SA + "StartupFolder", n) });
                    }
                }
                catch { }
            }
            return list;
        }
        public static void Set(Engine e, StartItem it, bool enable)
        {
            var b = new byte[12];
            b[0] = (byte)(enable ? 2 : 3);
            if (!enable) Array.Copy(BitConverter.GetBytes(DateTime.Now.ToFileTime()), 0, b, 4, 8);
            e.SetReg(it.ApprovedHive, it.ApprovedPath, it.Name, b, RegistryValueKind.Binary);
            it.Enabled = enable;
        }
    }

    // ------------------------------------------------------------------ Interfaz
    static class Theme
    {
        public static readonly Color Bg = Color.FromArgb(14, 16, 22);
        public static readonly Color Header = Color.FromArgb(19, 22, 31);
        public static readonly Color Card = Color.FromArgb(25, 29, 40);
        public static readonly Color CardHover = Color.FromArgb(32, 37, 51);
        public static readonly Color Border = Color.FromArgb(44, 50, 66);
        public static readonly Color Text = Color.FromArgb(232, 236, 244);
        public static readonly Color Muted = Color.FromArgb(140, 150, 170);
        public static readonly Color Accent = Color.FromArgb(124, 92, 255);
        public static readonly Color Accent2 = Color.FromArgb(0, 200, 255);

        public static GraphicsPath Round(Rectangle r, int rad)
        {
            var p = new GraphicsPath(); int d = rad * 2;
            p.AddArc(r.X, r.Y, d, d, 180, 90); p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90); p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure(); return p;
        }
        public static Color Mix(Color a, Color b, float t)
        {
            return Color.FromArgb((int)(a.R * t + b.R * (1 - t)), (int)(a.G * t + b.G * (1 - t)), (int)(a.B * t + b.B * (1 - t)));
        }
    }

    class Card : Panel
    {
        public Card() { DoubleBuffered = true; BackColor = Theme.Card; }
        protected override void OnPaintBackground(PaintEventArgs e)
        {
            e.Graphics.Clear(Parent != null ? Parent.BackColor : Theme.Bg);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (var p = Theme.Round(new Rectangle(0, 0, Width - 1, Height - 1), 12))
            {
                using (var b = new SolidBrush(BackColor)) e.Graphics.FillPath(b, p);
                using (var pen = new Pen(Theme.Border)) e.Graphics.DrawPath(pen, p);
            }
        }
    }

    class ProfileCard : Control
    {
        public string Title, Sub; public Color Accent; public int Level;
        public bool Selected, Suggested; bool hover;
        public ProfileCard()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            Cursor = Cursors.Hand;
        }
        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Parent.BackColor);
            var r = new Rectangle(1, 1, Width - 3, Height - 3);
            Color fill = Selected ? Theme.Mix(Accent, Theme.Card, 0.16f) : hover ? Theme.CardHover : Theme.Card;
            using (var p = Theme.Round(r, 12))
            {
                using (var b = new SolidBrush(fill)) g.FillPath(b, p);
                using (var pen = new Pen(Selected ? Accent : Theme.Border, Selected ? 2.4f : 1f)) g.DrawPath(pen, p);
            }
            float s = ScreenDpi() / 96f;
            // Nivel en barras
            for (int i = 0; i < 4; i++)
            {
                var br = new Rectangle((int)(16 * s + i * 9 * s), (int)(18 * s), (int)(6 * s), (int)(14 * s));
                using (var b = new SolidBrush(i < Level ? Accent : Theme.Border)) g.FillRectangle(b, br);
            }
            using (var f = new Font("Segoe UI Semibold", 13f))
                TextRenderer.DrawText(g, Title, f, new Point((int)(56 * s), (int)(12 * s)), Theme.Text);
            int subY = (int)(46 * s);
            if (Suggested)
            {
                const string tag = "★ SUGERIDO PARA TU PC";
                using (var f = new Font("Segoe UI", 7f, FontStyle.Bold))
                {
                    var sz = TextRenderer.MeasureText(tag, f);
                    var br = new Rectangle((int)(14 * s), (int)(44 * s), sz.Width + (int)(12 * s), sz.Height + (int)(4 * s));
                    using (var p = Theme.Round(br, br.Height / 2)) using (var b = new SolidBrush(Accent)) g.FillPath(b, p);
                    TextRenderer.DrawText(g, tag, f, br, Color.FromArgb(20, 20, 20), TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                    subY = br.Bottom + (int)(6 * s);
                }
            }
            using (var f = new Font("Segoe UI", 8.75f))
                TextRenderer.DrawText(g, Sub, f, new Rectangle((int)(16 * s), subY, Width - (int)(28 * s), Height - subY - (int)(4 * s)), Theme.Muted, TextFormatFlags.WordBreak);
        }
        int ScreenDpi() { return dpi > 0 ? dpi : (dpi = GetDpi()); }
        int dpi;
        int GetDpi() { using (var g = CreateGraphics()) return (int)g.DpiX; }
    }

    class ThinBar : Control
    {
        int val; public Color Fill = Theme.Accent2;
        public int Value { get { return val; } set { val = Math.Max(0, Math.Min(100, value)); Invalidate(); } }
        public ThinBar() { SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true); }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias; g.Clear(Parent.BackColor);
            using (var p = Theme.Round(new Rectangle(0, 0, Width - 1, Height - 1), Height / 2 - 1)) using (var b = new SolidBrush(Theme.Border)) g.FillPath(b, p);
            int w = (int)((Width - 1) * val / 100.0);
            if (w > Height)
                using (var p = Theme.Round(new Rectangle(0, 0, w, Height - 1), Height / 2 - 1))
                using (var b = new LinearGradientBrush(new Rectangle(0, 0, Math.Max(1, w), Height), Theme.Accent, Theme.Accent2, 0f)) g.FillPath(b, p);
        }
    }

    class MainForm : Form
    {
        HwInfo hw;
        readonly List<Tweak> catalog = Catalog.All();
        readonly List<ProfileCard> cards = new List<ProfileCard>();
        readonly List<Button> navs = new List<Button>();
        readonly List<Panel> pages = new List<Panel>();
        int profile = Catalog.Recomendado;
        bool busy;

        Label lblProfileDesc, lblCount, lblDetailTitle, lblDetail, lblStatus, lblTier;
        Label[] hwVals = new Label[6];
        CheckedListBox list;
        CheckBox chkRestore;
        Button btnRun, btnRestore, btnStartApply;
        ThinBar bar;
        RichTextBox logBox;
        ListView startList;
        List<StartItem> startItems = new List<StartItem>();

        static readonly string[] PNames = { "", "Lite", "Seguro", "Recomendado", "Extremo" };
        static readonly Color[] PColors = { Color.Empty, Color.FromArgb(41, 182, 246), Color.FromArgb(76, 175, 80), Color.FromArgb(255, 167, 38), Color.FromArgb(239, 83, 80) };

        public MainForm()
        {
            SuspendLayout();
            Text = "FullOptimizer";
            FormBorderStyle = FormBorderStyle.FixedSingle; MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Theme.Bg; ForeColor = Theme.Text;
            Font = new Font("Segoe UI", 9.5f);
            ClientSize = new Size(1000, 700);
            Icon appIcon = LoadIcon(32);
            if (appIcon != null) Icon = appIcon;
            BuildHeader();
            BuildHardware();
            BuildPages();
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            ResumeLayout(false);
            Shown += async (s, e) => await DetectAsync();
        }

        static Icon LoadIcon(int size)
        {
            try
            {
                using (var st = Assembly.GetExecutingAssembly().GetManifestResourceStream("FullOptimizer.ico"))
                    return st == null ? Icon.ExtractAssociatedIcon(Application.ExecutablePath) : new Icon(st, size, size);
            }
            catch { return null; }
        }

        // Extrae la imagen PNG más grande del .ico embebido (Icon.ToBitmap no soporta entradas PNG)
        static Image LoadIconImage()
        {
            try
            {
                byte[] d;
                using (var st = Assembly.GetExecutingAssembly().GetManifestResourceStream("FullOptimizer.ico"))
                {
                    if (st == null) return null;
                    using (var ms = new MemoryStream()) { st.CopyTo(ms); d = ms.ToArray(); }
                }
                int count = BitConverter.ToUInt16(d, 4), best = -1, bestSize = 0;
                for (int i = 0; i < count; i++)
                {
                    int sz = d[6 + i * 16]; if (sz == 0) sz = 256;
                    if (sz > bestSize) { bestSize = sz; best = i; }
                }
                int len = BitConverter.ToInt32(d, 6 + best * 16 + 8), off = BitConverter.ToInt32(d, 6 + best * 16 + 12);
                using (var ms = new MemoryStream(d, off, len))
                using (var img = Image.FromStream(ms)) return new Bitmap(img);
            }
            catch { return null; }
        }

        Label L(Control parent, string text, int x, int y, int w, int h, float size, bool bold, Color color)
        {
            var l = new Label { Text = text, Location = new Point(x, y), Size = new Size(w, h), ForeColor = color, BackColor = Color.Transparent,
                Font = new Font(bold ? "Segoe UI Semibold" : "Segoe UI", size), AutoEllipsis = true };
            parent.Controls.Add(l); return l;
        }
        Button Btn(Control parent, string text, int x, int y, int w, int h, Color back, Color fore)
        {
            var b = new Button { Text = text, Location = new Point(x, y), Size = new Size(w, h), FlatStyle = FlatStyle.Flat, BackColor = back, ForeColor = fore,
                Font = new Font("Segoe UI Semibold", 10f), Cursor = Cursors.Hand, UseVisualStyleBackColor = false };
            b.FlatAppearance.BorderSize = 0;
            b.FlatAppearance.MouseOverBackColor = Theme.Mix(Color.White, back, 0.12f);
            parent.Controls.Add(b); return b;
        }

        void BuildHeader()
        {
            var head = new Panel { Location = new Point(0, 0), Size = new Size(1000, 84), BackColor = Theme.Header };
            head.Paint += (s, e) => { using (var b = new LinearGradientBrush(new Rectangle(0, head.Height - 3, head.Width, 3), Theme.Accent, Theme.Accent2, 0f)) e.Graphics.FillRectangle(b, 0, head.Height - 3, head.Width, 3); };
            Controls.Add(head);
            var pic = new PictureBox { Location = new Point(22, 14), Size = new Size(54, 54), SizeMode = PictureBoxSizeMode.Zoom, BackColor = Color.Transparent };
            pic.Image = LoadIconImage();
            head.Controls.Add(pic);
            L(head, "FullOptimizer", 86, 10, 300, 38, 20f, true, Theme.Text);
            L(head, "Optimización inteligente según tu hardware", 89, 48, 340, 22, 9.5f, false, Theme.Muted);
            string[] names = { "Optimizar", "Inicio de Windows", "Registro" };
            int x = 560;
            for (int i = 0; i < names.Length; i++)
            {
                int idx = i;
                var b = Btn(head, names[i], x, 24, i == 1 ? 160 : 120, 36, Theme.Header, Theme.Muted);
                b.Click += (s, e) => ShowPage(idx);
                navs.Add(b); x += b.Width + 8;
            }
        }

        void BuildHardware()
        {
            var c = new Card { Location = new Point(24, 98), Size = new Size(952, 96) };
            Controls.Add(c);
            string[] caps = { "PROCESADOR", "MEMORIA RAM", "DISCO DEL SISTEMA", "GRÁFICOS", "EQUIPO", "WINDOWS" };
            for (int i = 0; i < 6; i++)
            {
                int col = i % 3, row = i / 3;
                int x = 20 + col * 250, y = 12 + row * 42;
                L(c, caps[i], x, y, 240, 16, 7.5f, true, Theme.Muted);
                hwVals[i] = L(c, "Detectando…", x, y + 16, 240, 22, 9.75f, false, Theme.Text);
            }
            L(c, "NIVEL DETECTADO", 790, 20, 150, 16, 7.5f, true, Theme.Muted);
            lblTier = L(c, "…", 788, 38, 150, 34, 16f, true, Theme.Accent2);
        }

        void BuildPages()
        {
            for (int i = 0; i < 3; i++)
            {
                var p = new Panel { Location = new Point(24, 206), Size = new Size(952, 484), BackColor = Theme.Bg, Visible = i == 0 };
                Controls.Add(p); pages.Add(p);
            }
            BuildOptimizePage(pages[0]);
            BuildStartupPage(pages[1]);
            BuildLogPage(pages[2]);
            ShowPage(0);
        }

        void BuildOptimizePage(Panel pg)
        {
            string[] subs = { "", "Solo limpieza: libera espacio y caché. No cambia ninguna configuración.",
                "Limpieza + ajustes que no quitan funciones. Todo es reversible.",
                "El equilibrio ideal, calculado para tu hardware.",
                "Máximo rendimiento: apaga servicios, efectos y procesos no esenciales." };
            for (int i = 1; i <= 4; i++)
            {
                int lvl = i;
                var cd = new ProfileCard { Title = PNames[i], Sub = subs[i], Accent = PColors[i], Level = i, Location = new Point((i - 1) * 241, 0), Size = new Size(229, 122) };
                cd.Click += (s, e) => { if (!busy) SelectProfile(lvl); };
                pg.Controls.Add(cd); cards.Add(cd);
            }
            lblProfileDesc = L(pg, "", 2, 128, 948, 22, 10f, true, Theme.Text);

            var lc = new Card { Location = new Point(0, 156), Size = new Size(600, 246) };
            pg.Controls.Add(lc);
            list = new CheckedListBox { Location = new Point(12, 12), Size = new Size(576, 224), BorderStyle = BorderStyle.None, BackColor = Theme.Card, ForeColor = Theme.Text,
                Font = new Font("Segoe UI", 10f), CheckOnClick = true, IntegralHeight = false };
            list.ItemCheck += (s, e) => BeginInvoke(new Action(UpdateCount));
            list.SelectedIndexChanged += (s, e) => ShowDetail(list.SelectedItem as Tweak);
            lc.Controls.Add(list);

            var dc = new Card { Location = new Point(612, 156), Size = new Size(340, 246) };
            pg.Controls.Add(dc);
            L(dc, "DETALLE", 18, 14, 300, 16, 7.5f, true, Theme.Muted);
            lblDetailTitle = L(dc, "Seleccioná un ajuste", 18, 32, 306, 48, 11.5f, true, Theme.Text);
            lblDetail = L(dc, "Tocá cualquier ajuste de la lista para ver qué hace. Podés destildar lo que no quieras aplicar.", 18, 82, 306, 156, 9.5f, false, Theme.Muted);

            chkRestore = new CheckBox { Text = "Crear punto de restauración antes de optimizar", Checked = true, Location = new Point(2, 414), Size = new Size(420, 24), ForeColor = Theme.Text, BackColor = Theme.Bg };
            pg.Controls.Add(chkRestore);
            lblCount = L(pg, "", 2, 440, 560, 20, 9f, false, Theme.Muted);
            bar = new ThinBar { Location = new Point(2, 466), Size = new Size(560, 8) };
            pg.Controls.Add(bar);
            btnRestore = Btn(pg, "Restaurar respaldo", 590, 418, 170, 48, Theme.Card, Theme.Text);
            btnRestore.Click += async (s, e) => await RestoreAsync();
            btnRun = Btn(pg, "OPTIMIZAR AHORA", 772, 418, 180, 48, Theme.Accent, Color.White);
            btnRun.Click += async (s, e) => await OptimizeAsync();
        }

        void BuildStartupPage(Panel pg)
        {
            L(pg, "Programas que arrancan con Windows", 2, 0, 600, 28, 13f, true, Theme.Text);
            L(pg, "Destildá los que no necesites al encender. Se desactivan igual que desde el Administrador de tareas (reversible). No desactives tu antivirus ni drivers de touchpad/audio.", 2, 30, 948, 40, 9.25f, false, Theme.Muted);
            startList = new ListView { Location = new Point(0, 76), Size = new Size(952, 340), View = View.Details, CheckBoxes = true, FullRowSelect = true,
                BackColor = Theme.Card, ForeColor = Theme.Text, BorderStyle = BorderStyle.FixedSingle, HeaderStyle = ColumnHeaderStyle.Nonclickable };
            startList.Columns.Add("Programa", 220); startList.Columns.Add("Origen", 150); startList.Columns.Add("Comando", 560);
            pg.Controls.Add(startList);
            var reload = Btn(pg, "Actualizar lista", 590, 430, 170, 44, Theme.Card, Theme.Text);
            reload.Click += (s, e) => LoadStartup();
            btnStartApply = Btn(pg, "Aplicar cambios", 772, 430, 180, 44, Theme.Accent, Color.White);
            btnStartApply.Click += (s, e) => ApplyStartup();
        }

        void BuildLogPage(Panel pg)
        {
            lblStatus = L(pg, "Sin tareas en curso.", 2, 0, 948, 26, 12f, true, Theme.Text);
            logBox = new RichTextBox { Location = new Point(0, 34), Size = new Size(952, 446), ReadOnly = true, BorderStyle = BorderStyle.None, BackColor = Theme.Card,
                ForeColor = Theme.Text, Font = new Font("Consolas", 9.5f), DetectUrls = false };
            pg.Controls.Add(logBox);
        }

        void ShowPage(int i)
        {
            for (int k = 0; k < pages.Count; k++) pages[k].Visible = k == i;
            for (int k = 0; k < navs.Count; k++) { navs[k].BackColor = k == i ? Theme.Card : Theme.Header; navs[k].ForeColor = k == i ? Theme.Text : Theme.Muted; }
            if (i == 1 && startList.Items.Count == 0) LoadStartup();
        }

        async Task DetectAsync()
        {
            hw = await Task.Run(() => HwInfo.Detect());
            hwVals[0].Text = hw.Cpu; hwVals[0].Text += "  (" + hw.Cores + "N/" + hw.Threads + "H)";
            hwVals[1].Text = hw.RamGB.ToString("N1") + " GB";
            hwVals[2].Text = hw.DiskKind + (hw.DiskModel.Length > 0 ? " · " + hw.DiskModel : "");
            hwVals[3].Text = hw.Gpu;
            hwVals[4].Text = (hw.IsLaptop ? "Portátil" : "Escritorio") + (hw.HasBattery ? " con batería" : "") + " · " + (hw.Printers == 0 ? "sin impresoras" : hw.Printers + " impresora(s)");
            hwVals[5].Text = hw.Os + " (build " + hw.Build + ")";
            foreach (var l in hwVals) new ToolTip().SetToolTip(l, l.Text);
            lblTier.Text = hw.TierName;
            int sug = hw.Tier == 0 ? Catalog.Extremo : hw.Tier == 1 ? Catalog.Recomendado : Catalog.Seguro;
            foreach (var c in cards) c.Suggested = c.Level == sug;
            SelectProfile(sug);
            Log("Hardware detectado: " + hw.Cpu + " | " + hw.RamGB.ToString("N1") + " GB RAM | " + hw.DiskKind + " | " + hw.Gpu + " | " + hw.TierName);
        }

        void SelectProfile(int p)
        {
            profile = p;
            foreach (var c in cards) { c.Selected = c.Level == p; c.Invalidate(); }
            lblProfileDesc.ForeColor = PColors[p];
            string why = p == Catalog.Recomendado ? " — ajustado a tu equipo (" + (hw == null ? "" : hw.TierName.ToLower()) + ")" : "";
            lblProfileDesc.Text = "Perfil " + PNames[p] + why;
            list.Items.Clear();
            if (hw == null) return;
            foreach (var t in catalog)
            {
                int lv = t.Level(hw);
                if (lv > 0 && lv <= p) list.Items.Add(t, true);
            }
            btnRun.BackColor = PColors[p];
            btnRun.FlatAppearance.MouseOverBackColor = Theme.Mix(Color.White, PColors[p], 0.15f);
            UpdateCount();
            ShowDetail(null);
        }

        void UpdateCount()
        {
            lblCount.Text = list.CheckedItems.Count + " de " + list.Items.Count + " ajustes seleccionados · se guarda un respaldo para deshacer todo.";
        }

        void ShowDetail(Tweak t)
        {
            if (t == null) { lblDetailTitle.Text = "Seleccioná un ajuste"; lblDetail.Text = "Tocá cualquier ajuste de la lista para ver qué hace. Podés destildar lo que no quieras aplicar."; return; }
            lblDetailTitle.Text = t.Name;
            lblDetail.Text = t.Desc + "\n\nCategoría: " + t.Cat + "\nIncluido desde el perfil: " + PNames[t.Level(hw)];
        }

        void Log(string s)
        {
            if (logBox.InvokeRequired) { logBox.BeginInvoke(new Action<string>(Log), s); return; }
            logBox.AppendText("[" + DateTime.Now.ToString("HH:mm:ss") + "] " + s + "\n");
            logBox.ScrollToCaret();
        }
        void Status(string s, int pct)
        {
            if (InvokeRequired) { BeginInvoke(new Action<string, int>(Status), s, pct); return; }
            lblStatus.Text = s; if (pct >= 0) bar.Value = pct;
        }
        void SetBusy(bool b)
        {
            busy = b; btnRun.Enabled = !b; btnRestore.Enabled = !b; btnStartApply.Enabled = !b; list.Enabled = !b; chkRestore.Enabled = !b;
            UseWaitCursor = b;
        }

        async Task OptimizeAsync()
        {
            if (hw == null || busy) return;
            var sel = list.CheckedItems.Cast<Tweak>().ToList();
            if (sel.Count == 0) { MessageBox.Show(this, "No hay ajustes seleccionados.", "FullOptimizer"); return; }
            if (MessageBox.Show(this, "Se aplicará el perfil " + PNames[profile] + " con " + sel.Count + " ajustes.\n\nSe guarda un respaldo para poder deshacer todo con \"Restaurar respaldo\".\n¿Continuar?",
                "FullOptimizer", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            SetBusy(true); ShowPage(2);
            int p = profile; bool rp = chkRestore.Checked;
            Log("===== Perfil " + PNames[p] + " =====");
            string bkFile = null;
            await Task.Run(() =>
            {
                var e = new Engine(Log);
                e.BeginBackup();
                try
                {
                    if (rp) { Status("Creando punto de restauración…", 0); Log("▶ Punto de restauración"); e.CreateRestorePoint(); }
                    for (int i = 0; i < sel.Count; i++)
                    {
                        Status("(" + (i + 1) + "/" + sel.Count + ") " + sel[i].Name + "…", i * 100 / sel.Count);
                        Log("▶ " + sel[i].Cat + ": " + sel[i].Name);
                        try { sel[i].Apply(e, hw, p); }
                        catch (Exception ex) { Log("   ! Error: " + ex.Message); }
                    }
                }
                finally { e.EndBackup(); bkFile = e.BackupFile; }
            });
            Status("Optimización terminada.", 100);
            Log("✔ Listo. Respaldo: " + (File.Exists(bkFile) ? bkFile : "(no hubo cambios de configuración)"));
            SetBusy(false);
            if (MessageBox.Show(this, "Optimización completa.\n\nAlgunos cambios se aplican al reiniciar. ¿Reiniciar ahora?", "FullOptimizer",
                MessageBoxButtons.YesNo, MessageBoxIcon.Information) == DialogResult.Yes)
                Process.Start(new ProcessStartInfo("shutdown.exe", "/r /t 5") { CreateNoWindow = true, UseShellExecute = false });
        }

        async Task RestoreAsync()
        {
            if (busy) return;
            string f = Engine.LatestBackup();
            if (f == null) { MessageBox.Show(this, "No hay respaldos para restaurar.", "FullOptimizer"); return; }
            if (MessageBox.Show(this, "Se deshacerán los cambios del respaldo:\n" + Path.GetFileName(f) + "\n\n¿Continuar?", "FullOptimizer",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            SetBusy(true); ShowPage(2);
            Status("Restaurando…", 0);
            Log("===== Restaurando " + Path.GetFileName(f) + " =====");
            await Task.Run(() => { try { new Engine(Log).Restore(f); } catch (Exception ex) { Log("! " + ex.Message); } });
            Status("Restauración terminada. Reiniciá el equipo.", 100);
            SetBusy(false);
            startList.Items.Clear();
            MessageBox.Show(this, "Cambios deshechos. Reiniciá el equipo para completar.", "FullOptimizer");
        }

        void LoadStartup()
        {
            startItems = Startup.Load();
            startList.BeginUpdate(); startList.Items.Clear();
            foreach (var it in startItems)
            {
                var li = new ListViewItem(new[] { it.Name, it.Source, it.Command }) { Checked = it.Enabled, Tag = it };
                startList.Items.Add(li);
            }
            startList.EndUpdate();
        }

        void ApplyStartup()
        {
            var e = new Engine(Log);
            e.BeginBackup();
            int n = 0;
            try
            {
                foreach (ListViewItem li in startList.Items)
                {
                    var it = (StartItem)li.Tag;
                    if (li.Checked != it.Enabled)
                    {
                        Startup.Set(e, it, li.Checked); n++;
                        Log((li.Checked ? "Inicio activado: " : "Inicio desactivado: ") + it.Name);
                    }
                }
            }
            catch (Exception ex) { Log("! " + ex.Message); }
            finally { e.EndBackup(); }
            MessageBox.Show(this, n == 0 ? "No hubo cambios." : n + " programa(s) actualizados. Se aplica en el próximo inicio de sesión.", "FullOptimizer");
        }
    }
}
