// SpendWiseWorker launcher - a tiny native .exe whose only job is to start
// the real PowerShell-based worker (SpendWise-Worker.ps1) silently, with no
// console flash, and to carry the app's real icon (Explorer/taskbar/shortcut
// all read the icon from THIS exe, not from the .ps1 it launches).
//
// Built with the .NET Framework C# compiler that ships with every Windows
// install (Framework64\v4.0.30319\csc.exe) - no external package needed.

using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows.Forms;

[assembly: AssemblyTitle("SpendWise Worker")]
[assembly: AssemblyProduct("SpendWise")]
[assembly: AssemblyCompany("Hananel Sabag")]
[assembly: AssemblyVersion(Launcher.BuildVersion)]
[assembly: AssemblyFileVersion(Launcher.BuildVersion)]
[assembly: AssemblyInformationalVersion(Launcher.BuildVersion)]

internal static class Launcher
{
    // Replaced at build time by Build-Exe.ps1 with the actual build date.
    public const string BuildVersion = "0.0.0.0";

    [STAThread]
    private static void Main()
    {
        try
        {
            string exeDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            string script = Path.Combine(exeDir, "SpendWise-Worker.ps1");

            if (!File.Exists(script))
            {
                MessageBox.Show(
                    "SpendWise-Worker.ps1 not found next to this exe:\n" + script,
                    "SpendWise Worker", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            var psi = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = "-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File \"" + script + "\"",
                WorkingDirectory = exeDir,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            Process.Start(psi);
        }
        catch (Exception ex)
        {
            MessageBox.Show("Failed to start SpendWise Worker:\n" + ex.Message,
                "SpendWise Worker", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
