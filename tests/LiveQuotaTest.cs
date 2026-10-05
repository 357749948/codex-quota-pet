using System;
using System.Threading;
using CodexQuotaPet;

internal static class LiveQuotaTest
{
    private static int Main()
    {
        using (var service = new QuotaService())
        using (var ready = new ManualResetEvent(false))
        {
            service.Changed += delegate(QuotaSnapshot snapshot) { if (snapshot.State == "ready" || snapshot.State == "error" || snapshot.State == "login_required") ready.Set(); };
            service.Start();
            if (!ready.WaitOne(45000) || service.Current.State != "ready") { Console.Error.WriteLine("Live quota read failed: " + service.Current.State); return 1; }
            Console.WriteLine("Live quota read succeeded. This opt-in check uses your current CLI account.");
            foreach (var window in service.Current.Windows) Console.WriteLine(window.Label + ": " + OverlayLogic.Percentage(window.RemainingPercent) + "; reset (UTC): " + window.ResetsAtUtc);
        }
        return 0;
    }
}
