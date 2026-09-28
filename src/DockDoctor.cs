// MyDockFinder Doctor — следит за Dock_64.exe и перезапускает его, когда он
// разъедается по памяти/хэндлам/GDI, зависает или падает сам.
//
// Окон нет: запускается из автозагрузки, всё пишет в DockDoctor.log рядом с exe.
//   DockDoctor.exe            — демон
//   DockDoctor.exe --restart  — перезапустить док прямо сейчас
//   DockDoctor.exe --stop     — остановить демона
//
// Собирается csc.exe из .NET Framework 4 (C# 5), см. build.ps1.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

[assembly: AssemblyTitle("MyDockFinder Doctor")]
[assembly: AssemblyProduct("MyDockFinder Doctor")]
[assembly: AssemblyVersion("1.0.0.0")]
[assembly: AssemblyFileVersion("1.0.0.0")]

namespace DockDoctor
{
    sealed class Config
    {
        public string DockExe = @"C:\Program Files (x86)\Steam\steamapps\common\MyDockFinder\Dock_64.exe";
        public int CheckIntervalSec = 30;
        public int SoftGpuMb = 500, HardGpuMb = 1000;
        public int SoftPrivateMb = 1024, HardPrivateMb = 2048;
        public int SoftHandles = 8000, HardHandles = 15000;
        public int SoftGdi = 3000, HardGdi = 8000;
        public int SoftUser = 3000, HardUser = 8000;
        public int SoftIdleSec = 120, HardIdleSec = 10;
        public int MinUptimeMin = 5;
        public int HangRestartSec = 90;
        public int SummaryEveryMin = 10;
        public bool RelaunchAfterCrash = true;
        public int MaxCrashRelaunches = 3;

        public const string DefaultText =
@"; MyDockFinder Doctor — настройки. Правки подхватываются на лету, перезапускать не нужно.
;
; soft_* — мягкий порог: док перезапустится, когда вы soft_idle_sec секунд
;          не будете трогать мышь и клавиатуру (то есть незаметно).
; hard_* — жёсткий порог: перезапуск после короткой паузы в hard_idle_sec секунд.
;
; Норма для Dock_64 (MyDockFinder 1.16.1, RTX 4060, 5K-монитор, сразу после старта):
; ~205 МБ видеопамяти, ~120 МБ памяти, ~1700 хэндлов, ~90 GDI, ~90 USER. Текущие
; цифры пишутся в DockDoctor.log каждые summary_every_min минут — по ним удобно
; подбирать пороги.

[limits]
; выделенная видеопамять — колонка «Выделенная память GPU» в Диспетчере задач
soft_gpu_mb = 500
hard_gpu_mb = 1000
; обычная память процесса (private bytes)
soft_private_mb = 1024
hard_private_mb = 2048
soft_handles = 8000
hard_handles = 15000
; у процесса Windows жёсткий потолок 10000 GDI/USER-объектов — дальше иконки перестают рисоваться
soft_gdi = 3000
hard_gdi = 8000
soft_user = 3000
hard_user = 8000

[timing]
check_interval_sec = 30
soft_idle_sec = 120
hard_idle_sec = 10
; процесс моложе этого не трогаем, даже если он уже выше порога
min_uptime_min = 5
; док не отвечает дольше стольких секунд — перезапуск (0 = не следить)
hang_restart_sec = 90
summary_every_min = 10

[watchdog]
; поднимать док, если он упал сам (закрытие из его меню не трогаем)
relaunch_after_crash = 1
; не больше стольких автоподъёмов за 30 минут, чтобы не зациклиться
max_crash_relaunches = 3

[dock]
; путь нужен, только если док не запущен в момент перезапуска; обычно берётся у работающего процесса
exe = C:\Program Files (x86)\Steam\steamapps\common\MyDockFinder\Dock_64.exe
";

        public static Config Load(string path)
        {
            var c = new Config();
            if (!File.Exists(path)) return c;
            foreach (string raw in File.ReadAllLines(path))
            {
                string line = raw.Trim();
                if (line.Length == 0 || line[0] == ';' || line[0] == '#' || line[0] == '[') continue;
                int eq = line.IndexOf('=');
                if (eq <= 0) continue;
                string key = line.Substring(0, eq).Trim().ToLowerInvariant();
                string val = line.Substring(eq + 1).Trim();
                int n;
                bool isNum = int.TryParse(val, NumberStyles.Integer, CultureInfo.InvariantCulture, out n);
                switch (key)
                {
                    case "exe": if (val.Length > 0) c.DockExe = val; break;
                    case "check_interval_sec": if (isNum && n >= 5) c.CheckIntervalSec = n; break;
                    case "soft_gpu_mb": if (isNum) c.SoftGpuMb = n; break;
                    case "hard_gpu_mb": if (isNum) c.HardGpuMb = n; break;
                    case "soft_private_mb": if (isNum) c.SoftPrivateMb = n; break;
                    case "hard_private_mb": if (isNum) c.HardPrivateMb = n; break;
                    case "soft_handles": if (isNum) c.SoftHandles = n; break;
                    case "hard_handles": if (isNum) c.HardHandles = n; break;
                    case "soft_gdi": if (isNum) c.SoftGdi = n; break;
                    case "hard_gdi": if (isNum) c.HardGdi = n; break;
                    case "soft_user": if (isNum) c.SoftUser = n; break;
                    case "hard_user": if (isNum) c.HardUser = n; break;
                    case "soft_idle_sec": if (isNum) c.SoftIdleSec = n; break;
                    case "hard_idle_sec": if (isNum) c.HardIdleSec = n; break;
                    case "min_uptime_min": if (isNum) c.MinUptimeMin = n; break;
                    case "hang_restart_sec": if (isNum) c.HangRestartSec = n; break;
                    case "summary_every_min": if (isNum && n > 0) c.SummaryEveryMin = n; break;
                    case "relaunch_after_crash": c.RelaunchAfterCrash = val == "1" || val.ToLowerInvariant() == "true"; break;
                    case "max_crash_relaunches": if (isNum) c.MaxCrashRelaunches = n; break;
                }
            }
            return c;
        }
    }

    sealed class Sample
    {
        public int Pid;
        public double PrivateMb;
        public double GpuMb = -1; // -1 — счётчики видеопамяти недоступны
        public int Handles, Threads, Gdi, User;
        public TimeSpan Uptime;

        public override string ToString()
        {
            return string.Format(CultureInfo.InvariantCulture,
                "pid {0}, аптайм {1}, видеопамять {2}, память {3:0} МБ, хэндлы {4}, GDI {5}, USER {6}, потоки {7}",
                Pid, Program.FormatSpan(Uptime), GpuMb >= 0 ? GpuMb.ToString("0", CultureInfo.InvariantCulture) + " МБ" : "н/д",
                PrivateMb, Handles, Gdi, User, Threads);
        }
    }

    static class Native
    {
        public const uint PROCESS_QUERY_INFORMATION = 0x0400;
        public const uint SYNCHRONIZE = 0x00100000;
        public const uint STILL_ACTIVE = 259;

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern IntPtr OpenProcess(uint access, bool inherit, int pid);
        [DllImport("kernel32.dll")]
        public static extern bool CloseHandle(IntPtr h);
        [DllImport("kernel32.dll")]
        public static extern bool GetExitCodeProcess(IntPtr h, out uint code);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        public static extern bool QueryFullProcessImageName(IntPtr h, int flags, StringBuilder sb, ref int size);
        [DllImport("user32.dll")]
        public static extern uint GetGuiResources(IntPtr h, uint flags);

        [StructLayout(LayoutKind.Sequential)]
        public struct LASTINPUTINFO { public uint cbSize; public uint dwTime; }
        [DllImport("user32.dll")]
        public static extern bool GetLastInputInfo(ref LASTINPUTINFO lii);
        [DllImport("shell32.dll")]
        public static extern int SHQueryUserNotificationState(out int state);

        public delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);
        [DllImport("user32.dll")]
        public static extern bool EnumWindows(EnumWindowsProc f, IntPtr l);
        [DllImport("user32.dll")]
        public static extern uint GetWindowThreadProcessId(IntPtr h, out int pid);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern int GetClassName(IntPtr h, StringBuilder sb, int n);
        [DllImport("user32.dll")]
        public static extern IntPtr SendMessageTimeout(IntPtr h, uint msg, IntPtr w, IntPtr l, uint flags, uint timeout, out IntPtr result);
    }

    // Процесс дока, за которым следим. Хэндл держим открытым, чтобы после
    // смерти процесса узнать код выхода и отличить падение от закрытия.
    sealed class TrackedDock : IDisposable
    {
        public readonly int Pid;
        public readonly IntPtr Handle;
        public readonly string Path;

        public TrackedDock(int pid)
        {
            Pid = pid;
            Handle = Native.OpenProcess(Native.PROCESS_QUERY_INFORMATION | Native.SYNCHRONIZE, false, pid);
            if (Handle != IntPtr.Zero)
            {
                var sb = new StringBuilder(1024);
                int size = sb.Capacity;
                if (Native.QueryFullProcessImageName(Handle, 0, sb, ref size)) Path = sb.ToString();
            }
        }

        public bool HasExited(out uint code)
        {
            code = 0;
            // Без хэндла код выхода не узнать; смену pid всё равно заметит FindDockPid.
            if (Handle == IntPtr.Zero) return false;
            if (!Native.GetExitCodeProcess(Handle, out code)) return false;
            return code != Native.STILL_ACTIVE;
        }

        public void Dispose()
        {
            if (Handle != IntPtr.Zero) Native.CloseHandle(Handle);
        }
    }

    static class Program
    {
        const string MutexName = @"Local\MyDockFinderDoctor";
        const string StopEventName = @"Local\MyDockFinderDoctor.Stop";
        const string RestartEventName = @"Local\MyDockFinderDoctor.Restart";
        const string DockWindowClass = "MyDockIconsCwnd";
        static readonly string[] SettingsFiles = { "ico.ini", "config.ini", "padconfig.ini" };

        static string baseDir, logPath, iniPath;
        static Config cfg;
        static DateTime iniStamp;
        static readonly object logLock = new object();

        static int Main(string[] args)
        {
            baseDir = AppDomain.CurrentDomain.BaseDirectory;
            logPath = Path.Combine(baseDir, "DockDoctor.log");
            iniPath = Path.Combine(baseDir, "DockDoctor.ini");
            if (!File.Exists(iniPath)) File.WriteAllText(iniPath, Config.DefaultText, new UTF8Encoding(true));
            ReloadConfigIfChanged();

            string cmd = args.Length > 0 ? args[0].ToLowerInvariant() : "";
            if (cmd == "--stop") return SignalDaemon(StopEventName) ? 0 : 1;
            if (cmd == "--restart")
            {
                if (SignalDaemon(RestartEventName)) return 0;
                Log("Ручной перезапуск (демон не запущен).");
                return RestartDock("по команде --restart", null) != 0 ? 0 : 1;
            }

            bool created;
            using (var mutex = new Mutex(true, MutexName, out created))
            {
                if (!created) return 1;
                using (var stop = new EventWaitHandle(false, EventResetMode.AutoReset, StopEventName))
                using (var restart = new EventWaitHandle(false, EventResetMode.AutoReset, RestartEventName))
                {
                    try { Run(stop, restart); }
                    catch (Exception e) { Log("Демон упал: " + e); return 2; }
                }
            }
            return 0;
        }

        static bool SignalDaemon(string eventName)
        {
            EventWaitHandle ev;
            if (!EventWaitHandle.TryOpenExisting(eventName, out ev)) return false;
            using (ev) ev.Set();
            return true;
        }

        static void Run(EventWaitHandle stop, EventWaitHandle restart)
        {
            Log(string.Format(CultureInfo.InvariantCulture,
                "Демон запущен (pid {0}). Пороги: видеопамять {1}/{2} МБ, память {3}/{4} МБ, хэндлы {5}/{6}, GDI {7}/{8}, USER {9}/{10}.",
                Process.GetCurrentProcess().Id, cfg.SoftGpuMb, cfg.HardGpuMb, cfg.SoftPrivateMb, cfg.HardPrivateMb,
                cfg.SoftHandles, cfg.HardHandles, cfg.SoftGdi, cfg.HardGdi, cfg.SoftUser, cfg.HardUser));

            TrackedDock tracked = null;
            var crashTimes = new List<DateTime>();
            DateTime lastSummary = DateTime.MinValue;
            DateTime hungSince = DateTime.MinValue;
            int lastLevel = 0;
            var handles = new WaitHandle[] { stop, restart };

            while (true)
            {
                int w = WaitHandle.WaitAny(handles, cfg.CheckIntervalSec * 1000);
                if (w == 0) { Log("Демон остановлен по команде --stop."); break; }
                ReloadConfigIfChanged();

                if (w == 1)
                {
                    Retrack(ref tracked, RestartDock("по команде --restart", tracked));
                    lastLevel = 0; hungSince = DateTime.MinValue;
                    continue;
                }

                // Прежний процесс умер — разбираемся, сам ли.
                uint exitCode;
                if (tracked != null && tracked.HasExited(out exitCode))
                {
                    int oldPid = tracked.Pid;
                    string oldExe = !string.IsNullOrEmpty(tracked.Path) ? tracked.Path : cfg.DockExe;
                    Retrack(ref tracked, 0);
                    lastLevel = 0; hungSince = DateTime.MinValue;
                    int running = FindDockPid();
                    if (running != 0)
                    {
                        Log(string.Format("Док pid {0} завершился (код 0x{1:X8}), уже запущен новый pid {2}.", oldPid, exitCode, running));
                    }
                    else if (!IsCrash(exitCode))
                    {
                        Log(string.Format("Док pid {0} закрыт (код 0x{1:X8}) — похоже, вручную. Не поднимаю.", oldPid, exitCode));
                    }
                    else if (!cfg.RelaunchAfterCrash)
                    {
                        Log(string.Format("Док pid {0} упал (код 0x{1:X8}: {2}). Автоподъём выключен в настройках.", oldPid, exitCode, DescribeCode(exitCode)));
                    }
                    else
                    {
                        crashTimes.RemoveAll(t => DateTime.Now - t > TimeSpan.FromMinutes(30));
                        if (crashTimes.Count >= cfg.MaxCrashRelaunches)
                        {
                            Log(string.Format("Док pid {0} упал (код 0x{1:X8}: {2}), но это уже {3}-е падение за 30 минут — больше не поднимаю, чтобы не зациклиться.",
                                oldPid, exitCode, DescribeCode(exitCode), crashTimes.Count + 1));
                        }
                        else
                        {
                            crashTimes.Add(DateTime.Now);
                            Log(string.Format("Док pid {0} упал (код 0x{1:X8}: {2}). Поднимаю заново.", oldPid, exitCode, DescribeCode(exitCode)));
                            Retrack(ref tracked, LaunchDock(oldExe));
                        }
                    }
                }

                int pid = FindDockPid();
                if (pid == 0) continue;
                if (tracked == null || tracked.Pid != pid)
                {
                    Retrack(ref tracked, pid);
                    lastLevel = 0; hungSince = DateTime.MinValue;
                    Log("Слежу за доком pid " + pid + ".");
                }

                Sample s = TakeSample(tracked);
                if (s == null) continue;

                if (DateTime.Now - lastSummary >= TimeSpan.FromMinutes(cfg.SummaryEveryMin))
                {
                    lastSummary = DateTime.Now;
                    Log("Сводка: " + s);
                }

                // Завис?
                if (cfg.HangRestartSec > 0)
                {
                    if (IsResponding(pid)) hungSince = DateTime.MinValue;
                    else if (hungSince == DateTime.MinValue) { hungSince = DateTime.Now; Log("Док не отвечает на сообщения окна. " + s); }
                    else if (DateTime.Now - hungSince >= TimeSpan.FromSeconds(cfg.HangRestartSec))
                    {
                        Log(string.Format("Док не отвечает уже {0:0} с — перезапускаю.", (DateTime.Now - hungSince).TotalSeconds));
                        Retrack(ref tracked, RestartDock("завис", tracked, s));
                        lastLevel = 0; hungSince = DateTime.MinValue;
                        continue;
                    }
                }

                // Течёт?
                string reason;
                int level = Evaluate(s, out reason);
                if (level == 0) { lastLevel = 0; continue; }

                bool tooYoung = s.Uptime < TimeSpan.FromMinutes(cfg.MinUptimeMin);
                int needIdle = level == 2 ? cfg.HardIdleSec : cfg.SoftIdleSec;
                int idle = IdleSeconds();
                bool fullscreen = IsFullscreenBusy();
                if (level != lastLevel)
                {
                    lastLevel = level;
                    Log(string.Format("Превышен {0} порог: {1}. {2}",
                        level == 2 ? "жёсткий" : "мягкий", reason,
                        tooYoung ? "Процесс моложе " + cfg.MinUptimeMin + " мин — пока не трогаю."
                                 : "Перезапущу после " + needIdle + " с без мыши и клавиатуры."));
                }
                if (tooYoung || idle < needIdle || fullscreen) continue;

                Retrack(ref tracked, RestartDock(reason, tracked, s));
                lastLevel = 0; hungSince = DateTime.MinValue;
            }

            Retrack(ref tracked, 0);
        }

        static void Retrack(ref TrackedDock tracked, int pid)
        {
            if (tracked != null) tracked.Dispose();
            tracked = pid != 0 ? new TrackedDock(pid) : null;
        }

        // 0 — норма, 1 — мягкий порог, 2 — жёсткий.
        static int Evaluate(Sample s, out string reason)
        {
            var hard = new List<string>();
            var soft = new List<string>();
            if (s.GpuMb >= 0) Check(s.GpuMb, cfg.SoftGpuMb, cfg.HardGpuMb, "видеопамять {0:0} МБ", soft, hard);
            Check(s.PrivateMb, cfg.SoftPrivateMb, cfg.HardPrivateMb, "память {0:0} МБ", soft, hard);
            Check(s.Handles, cfg.SoftHandles, cfg.HardHandles, "хэндлы {0}", soft, hard);
            Check(s.Gdi, cfg.SoftGdi, cfg.HardGdi, "GDI {0}", soft, hard);
            Check(s.User, cfg.SoftUser, cfg.HardUser, "USER {0}", soft, hard);
            var all = new List<string>(hard);
            all.AddRange(soft);
            reason = string.Join(", ", all.ToArray());
            return hard.Count > 0 ? 2 : soft.Count > 0 ? 1 : 0;
        }

        static void Check(double value, int softLimit, int hardLimit, string fmt, List<string> soft, List<string> hard)
        {
            string text = string.Format(CultureInfo.InvariantCulture, fmt, value);
            if (hardLimit > 0 && value >= hardLimit) hard.Add(text + " ≥ " + hardLimit);
            else if (softLimit > 0 && value >= softLimit) soft.Add(text + " ≥ " + softLimit);
        }

        static bool IsCrash(uint code)
        {
            // NTSTATUS-ошибки (0xC0000005, 0xC0000374, …) и C++/CLR-исключения (0xE…).
            // 0xFFFFFFFF — это чужой TerminateProcess(-1), а не падение.
            return code >= 0xC0000000 && code != 0xFFFFFFFF;
        }

        static string DescribeCode(uint code)
        {
            switch (code)
            {
                case 0xC0000005: return "access violation";
                case 0xC0000374: return "повреждение кучи";
                case 0xC0000409: return "переполнение буфера на стеке";
                case 0xC00000FD: return "переполнение стека";
                case 0xC0000017: return "не хватило памяти";
                case 0xE06D7363: return "необработанное C++-исключение";
                default: return "сбой";
            }
        }

        static Sample TakeSample(TrackedDock t)
        {
            try
            {
                using (Process p = Process.GetProcessById(t.Pid))
                {
                    var s = new Sample();
                    s.Pid = t.Pid;
                    s.PrivateMb = p.PrivateMemorySize64 / 1048576.0;
                    s.Handles = p.HandleCount;
                    s.Threads = p.Threads.Count;
                    s.Uptime = DateTime.Now - p.StartTime;
                    s.Gdi = (int)Native.GetGuiResources(t.Handle, 0);
                    s.User = (int)Native.GetGuiResources(t.Handle, 1);
                    s.GpuMb = GpuDedicatedMb(t.Pid);
                    return s;
                }
            }
            catch (Exception) { return null; }
        }

        static PerformanceCounterCategory gpuCategory;
        static bool gpuErrorLogged;

        // Выделенная видеопамять процесса по всем адаптерам — те же счётчики
        // "GPU Process Memory", что показывает Диспетчер задач. Экземпляры
        // называются pid_<pid>_luid_<...>_phys_<n>. Первое чтение занимает
        // несколько секунд (грузятся имена счётчиков), дальше — миллисекунды.
        static double GpuDedicatedMb(int pid)
        {
            try
            {
                if (gpuCategory == null) gpuCategory = new PerformanceCounterCategory("GPU Process Memory");
                InstanceDataCollection dedicated = gpuCategory.ReadCategory()["Dedicated Usage"];
                if (dedicated == null) return -1;
                string prefix = "pid_" + pid + "_";
                long bytes = 0;
                foreach (InstanceData d in dedicated.Values)
                    if (d.InstanceName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) bytes += d.RawValue;
                return bytes / 1048576.0;
            }
            catch (Exception e)
            {
                if (!gpuErrorLogged) { gpuErrorLogged = true; Log("Не удалось прочитать счётчики видеопамяти: " + e.Message); }
                return -1;
            }
        }

        static string DockProcessName()
        {
            return Path.GetFileNameWithoutExtension(cfg.DockExe);
        }

        static int FindDockPid()
        {
            int found = 0;
            foreach (Process p in Process.GetProcessesByName(DockProcessName()))
            {
                if (found == 0 && p.SessionId == Process.GetCurrentProcess().SessionId) found = p.Id;
                p.Dispose();
            }
            return found;
        }

        static IntPtr FindDockWindow(int pid)
        {
            IntPtr result = IntPtr.Zero;
            var sb = new StringBuilder(64);
            Native.EnumWindows(delegate(IntPtr h, IntPtr l)
            {
                int wpid;
                Native.GetWindowThreadProcessId(h, out wpid);
                if (wpid != pid) return true;
                sb.Length = 0;
                Native.GetClassName(h, sb, sb.Capacity);
                if (sb.ToString() != DockWindowClass) return true;
                result = h;
                return false;
            }, IntPtr.Zero);
            return result;
        }

        static bool IsResponding(int pid)
        {
            IntPtr h = FindDockWindow(pid);
            if (h == IntPtr.Zero) return true; // окна ещё/уже нет — это не зависание
            IntPtr r;
            const uint SMTO_ABORTIFHUNG = 0x0002;
            return Native.SendMessageTimeout(h, 0 /* WM_NULL */, IntPtr.Zero, IntPtr.Zero, SMTO_ABORTIFHUNG, 5000, out r) != IntPtr.Zero;
        }

        static int IdleSeconds()
        {
            var lii = new Native.LASTINPUTINFO();
            lii.cbSize = (uint)Marshal.SizeOf(typeof(Native.LASTINPUTINFO));
            if (!Native.GetLastInputInfo(ref lii)) return 0;
            return (int)(unchecked((uint)Environment.TickCount - lii.dwTime) / 1000);
        }

        // Полноэкранная D3D-игра или режим презентации — не мешаем.
        // QUNS_BUSY (2) не учитываем: при скрытой панели задач им считается любое развёрнутое окно.
        static bool IsFullscreenBusy()
        {
            int state;
            if (Native.SHQueryUserNotificationState(out state) != 0) return false;
            return state == 3 || state == 4;
        }

        // Убивает док (закрываться по WM_CLOSE он не умеет), проверяет, что его
        // настройки не побились, и запускает заново. Возвращает pid нового дока или 0.
        static int RestartDock(string reason, TrackedDock tracked, Sample before = null)
        {
            string exe = tracked != null && !string.IsNullOrEmpty(tracked.Path) ? tracked.Path : cfg.DockExe;
            string dir = Path.GetDirectoryName(exe);
            Log("Перезапуск дока: " + reason + (before != null ? ". До: " + before : "") + ".");

            BackupSettings(dir);
            foreach (Process p in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(exe)))
            {
                try
                {
                    if (p.SessionId != Process.GetCurrentProcess().SessionId) continue;
                    p.Kill();
                    if (!p.WaitForExit(10000)) Log("Процесс " + p.Id + " не завершился за 10 с.");
                }
                catch (Exception e) { Log("Не удалось завершить pid " + p.Id + ": " + e.Message); }
                finally { p.Dispose(); }
            }
            Thread.Sleep(2000); // даём службе MyDock снять Dockmod-хуки
            VerifySettings(dir);
            return LaunchDock(exe);
        }

        static int LaunchDock(string exe)
        {
            for (int attempt = 1; attempt <= 3; attempt++)
            {
                int pid = FindDockPid();
                if (pid == 0)
                {
                    try
                    {
                        var psi = new ProcessStartInfo(exe);
                        psi.WorkingDirectory = Path.GetDirectoryName(exe);
                        psi.UseShellExecute = true;
                        using (Process p = Process.Start(psi)) { }
                    }
                    catch (Exception e) { Log("Не удалось запустить " + exe + ": " + e.Message); }
                }

                for (int i = 0; i < 60; i++)
                {
                    Thread.Sleep(500);
                    pid = FindDockPid();
                    if (pid != 0 && FindDockWindow(pid) != IntPtr.Zero)
                    {
                        Log("Док запущен, pid " + pid + ".");
                        return pid;
                    }
                }
                Log("Попытка " + attempt + ": док не появился за 30 с.");
            }
            Log("Док так и не запустился. Запустите его вручную (или через Steam).");
            return 0;
        }

        static void BackupSettings(string dockDir)
        {
            string backupDir = Path.Combine(baseDir, "backup");
            Directory.CreateDirectory(backupDir);
            foreach (string name in SettingsFiles)
            {
                try
                {
                    var src = new FileInfo(Path.Combine(dockDir, name));
                    if (src.Exists && src.Length > 0) src.CopyTo(Path.Combine(backupDir, name), true);
                }
                catch (Exception e) { Log("Не удалось сохранить копию " + name + ": " + e.Message); }
            }
        }

        // Если док убили посреди записи файла, файл окажется пустым или обрезанным —
        // возвращаем копию, снятую за пару секунд до этого.
        static void VerifySettings(string dockDir)
        {
            foreach (string name in SettingsFiles)
            {
                try
                {
                    var backup = new FileInfo(Path.Combine(baseDir, "backup", name));
                    var cur = new FileInfo(Path.Combine(dockDir, name));
                    if (!backup.Exists) continue;
                    if (!cur.Exists || cur.Length < backup.Length)
                    {
                        backup.CopyTo(cur.FullName, true);
                        Log(name + " оказался " + (cur.Exists ? "обрезан" : "удалён") + " — восстановлен из копии.");
                    }
                }
                catch (Exception e) { Log("Не удалось проверить " + name + ": " + e.Message); }
            }
        }

        static void ReloadConfigIfChanged()
        {
            DateTime stamp = File.Exists(iniPath) ? File.GetLastWriteTimeUtc(iniPath) : DateTime.MinValue;
            if (cfg != null && stamp == iniStamp) return;
            bool reload = cfg != null;
            try { cfg = Config.Load(iniPath); iniStamp = stamp; }
            catch (Exception e) { if (cfg == null) cfg = new Config(); Log("Не удалось прочитать DockDoctor.ini: " + e.Message); return; }
            if (reload) Log("Настройки перечитаны.");
        }

        public static string FormatSpan(TimeSpan t)
        {
            if (t.TotalDays >= 1) return string.Format("{0}д {1}ч {2:00}м", (int)t.TotalDays, t.Hours, t.Minutes);
            return string.Format("{0}ч {1:00}м", (int)t.TotalHours, t.Minutes);
        }

        static void Log(string message)
        {
            lock (logLock)
            {
                try
                {
                    var fi = new FileInfo(logPath);
                    if (fi.Exists && fi.Length > 1024 * 1024)
                    {
                        string old = Path.Combine(baseDir, "DockDoctor.old.log");
                        if (File.Exists(old)) File.Delete(old);
                        fi.MoveTo(old);
                    }
                    File.AppendAllText(logPath,
                        DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) + "  " + message + Environment.NewLine,
                        new UTF8Encoding(true));
                }
                catch (Exception) { }
            }
        }
    }
}
