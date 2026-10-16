namespace UbisoftAutoLogin.Tray;

/// <summary>Lets a newly opened copy ask this one to exit (see <see cref="Program.HandOverEventName"/>).</summary>
internal static class HandOver
{
    public static RegisteredWaitHandle Listen(ApplicationContext context)
    {
        var ui = SynchronizationContext.Current;
        var ev = new EventWaitHandle(false, EventResetMode.AutoReset, Program.HandOverEventName);
        return ThreadPool.RegisterWaitForSingleObject(ev, (_, _) =>
        {
            Core.AppLog.Info("another copy was opened; handing over");
            if (ui is null) context.ExitThread(); else ui.Post(_ => context.ExitThread(), null);
        }, null, Timeout.Infinite, executeOnlyOnce: true);
    }
}
