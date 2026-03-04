using System.Diagnostics;
using System.Windows;

namespace FlashSpot.App.Infrastructure;

public static class SystemCommandExecutor
{
    private static readonly HashSet<string> _destructiveCommands = ["shutdown", "restart", "signout"];

    public static bool TryExecute(string actionUri, out string? errorMessage)
    {
        errorMessage = null;

        if (!actionUri.StartsWith("cmd://", StringComparison.OrdinalIgnoreCase))
        {
            errorMessage = "Not a system command URI.";
            return false;
        }

        var command = actionUri["cmd://".Length..].ToLowerInvariant();

        if (_destructiveCommands.Contains(command))
        {
            var result = MessageBox.Show(
                $"Are you sure you want to {command}?",
                "FlashSpot",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result != MessageBoxResult.Yes)
            {
                return false;
            }
        }

        try
        {
            switch (command)
            {
                case "lock":
                    RunProcess("rundll32.exe", "user32.dll,LockWorkStation");
                    break;
                case "sleep":
                    RunProcess("rundll32.exe", "powrprof.dll,SetSuspendState 0,1,0");
                    break;
                case "shutdown":
                    RunProcess("shutdown", "/s /t 0");
                    break;
                case "restart":
                    RunProcess("shutdown", "/r /t 0");
                    break;
                case "signout":
                    RunProcess("shutdown", "/l");
                    break;
                case "emptyrecyclebin":
                    RunProcess("cmd.exe", "/c rd /s /q C:\\$Recycle.Bin 2>nul");
                    break;
                case "taskmgr":
                    RunProcess("taskmgr.exe");
                    break;
                case "controlpanel":
                    RunProcess("control.exe");
                    break;
                case "settings":
                    RunShellExecute("ms-settings:");
                    break;
                case "devicemanager":
                    RunProcess("devmgmt.msc");
                    break;
                case "diskcleanup":
                    RunProcess("cleanmgr.exe");
                    break;
                case "screensaver":
                    RunProcess("rundll32.exe", "user32.dll,LockWorkStation");
                    break;
                case "cmd":
                    RunProcess("cmd.exe");
                    break;
                case "powershell":
                    RunProcess("powershell.exe");
                    break;
                case "terminal":
                    RunProcess("wt.exe");
                    break;
                case "explorer":
                    RunProcess("explorer.exe");
                    break;
                case "calc":
                    RunShellExecute("calculator:");
                    break;
                case "notepad":
                    RunProcess("notepad.exe");
                    break;
                case "snippingtool":
                    RunShellExecute("ms-screenclip:");
                    break;
                default:
                    errorMessage = $"Unknown command: {command}";
                    return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            errorMessage = ex.Message;
            return false;
        }
    }

    private static void RunProcess(string fileName, string? arguments = null)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            UseShellExecute = true
        };

        if (arguments is not null)
        {
            startInfo.Arguments = arguments;
        }

        Process.Start(startInfo);
    }

    private static void RunShellExecute(string uri)
    {
        Process.Start(new ProcessStartInfo { FileName = uri, UseShellExecute = true });
    }
}
