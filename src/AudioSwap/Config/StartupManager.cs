using System.Diagnostics;
using System.Security;
using System.Security.Principal;
using Microsoft.Win32;

namespace AudioSwap.Config;

/// <summary>
/// "Start with Windows", two ways, neither needing admin rights: the per-user Run key
/// (what Task Manager's Startup apps lists) and a sign-in task in Task Scheduler, because
/// Windows can occasionally skip a Run entry at sign-in. Whichever starts AudioSwap second
/// exits straight away, and switching it off in Task Manager is respected by both.
/// </summary>
internal static class StartupManager
{
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    const string ApprovedKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";
    const string ValueName = "AudioSwap";
    const int TaskCreateOrUpdate = 6, TaskLogonInteractiveToken = 3;

    static string ExePath => Environment.ProcessPath!;
    static string Command => $"\"{ExePath}\" --startup";
    static string TaskName => $"AudioSwap ({Environment.UserName})";

    public static bool IsEnabled => !IsTurnedOffInWindows && (RunCommand() is not null || TaskExePath() is not null);

    /// <summary>True when AudioSwap was switched off in Task Manager or Settings &gt; Apps &gt; Startup.</summary>
    public static bool IsTurnedOffInWindows
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(ApprovedKey);
            // The first byte is even when enabled (02, 06) and odd when disabled (03, 07).
            return key?.GetValue(ValueName) is byte[] { Length: > 0 } state && (state[0] & 1) == 1;
        }
    }

    public static void SetEnabled(bool enabled)
    {
        using (var key = Registry.CurrentUser.CreateSubKey(RunKey))
        {
            if (enabled) key.SetValue(ValueName, Command);
            else key.DeleteValue(ValueName, throwOnMissingValue: false);
        }
        if (enabled)
        {
            // Turning it on here also clears a "Disabled" set in Task Manager.
            using var approved = Registry.CurrentUser.OpenSubKey(ApprovedKey, writable: true);
            approved?.DeleteValue(ValueName, throwOnMissingValue: false);
        }

        // The Run entry works on its own, so a Task Scheduler problem isn't fatal.
        try
        {
            if (enabled) RegisterTask();
            else DeleteTask();
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"AudioSwap: updating the sign-in task failed: {ex}");
        }
    }

    /// <summary>
    /// Run at every start: points both entries at this exe if it was moved, and adds the
    /// sign-in task to setups made before it existed.
    /// </summary>
    public static void RefreshPath()
    {
        if (IsEnabled && (RunCommand() != Command || !string.Equals(TaskExePath(), ExePath, StringComparison.OrdinalIgnoreCase)))
            SetEnabled(true);
    }

    static string? RunCommand()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey);
        return key?.GetValue(ValueName) as string;
    }

    static dynamic TaskFolder()
    {
        dynamic service = Activator.CreateInstance(Type.GetTypeFromProgID("Schedule.Service", throwOnError: true)!)!;
        service.Connect();
        return service.GetFolder("\\");
    }

    static string? TaskExePath()
    {
        try
        {
            dynamic task = TaskFolder().GetTask(TaskName);
            return (string)task.Definition.Actions.Item(1).Path;
        }
        catch (Exception)
        {
            return null; // not registered, or Task Scheduler isn't available
        }
    }

    static void RegisterTask()
    {
        var user = SecurityElement.Escape(WindowsIdentity.GetCurrent().Name);
        // No time limit (the default stops a task after 3 days), runs on battery, normal priority.
        var xml = $"""
            <?xml version="1.0" encoding="UTF-16"?>
            <Task version="1.2" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
              <RegistrationInfo>
                <Description>Starts AudioSwap when you sign in. Turn it off with AudioSwap's "Start with Windows" option.</Description>
              </RegistrationInfo>
              <Triggers>
                <LogonTrigger>
                  <UserId>{user}</UserId>
                  <Delay>PT5S</Delay>
                </LogonTrigger>
              </Triggers>
              <Principals>
                <Principal id="Author">
                  <UserId>{user}</UserId>
                  <LogonType>InteractiveToken</LogonType>
                  <RunLevel>LeastPrivilege</RunLevel>
                </Principal>
              </Principals>
              <Settings>
                <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
                <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
                <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
                <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>
                <Priority>5</Priority>
              </Settings>
              <Actions Context="Author">
                <Exec>
                  <Command>{SecurityElement.Escape(ExePath)}</Command>
                  <Arguments>--startup</Arguments>
                </Exec>
              </Actions>
            </Task>
            """;
        TaskFolder().RegisterTask(TaskName, xml, TaskCreateOrUpdate, null, null, TaskLogonInteractiveToken, null);
    }

    static void DeleteTask()
    {
        if (TaskExePath() is not null) TaskFolder().DeleteTask(TaskName, 0);
    }
}
