namespace Bifrost.Core;

public static class RetryHelper
{
    public static async Task<T> RunAsync<T>(
        Func<Task<T>> action,
        int maxAttempts = 3,
        int delayMs     = 2000,
        string? label   = null)
    {
        for (int attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                return await action();
            }
            catch (Exception ex) when (attempt < maxAttempts)
            {
                Logger.Log($"       [WARN] Attempt {attempt}/{maxAttempts} failed{(label != null ? $" ({label})" : "")}: {ex.Message}");
                Logger.Log($"       [WARN] Retrying in {delayMs}ms...");
                await Task.Delay(delayMs);
            }
        }

        // Final attempt — let it throw
        return await action();
    }

    public static T Run<T>(
        Func<T> action,
        int maxAttempts = 3,
        int delayMs     = 2000,
        string? label   = null)
    {
        for (int attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                return action();
            }
            catch (Exception ex) when (attempt < maxAttempts)
            {
                Logger.Log($"       [WARN] Attempt {attempt}/{maxAttempts} failed{(label != null ? $" ({label})" : "")}: {ex.Message}");
                Logger.Log($"       [WARN] Retrying in {delayMs}ms...");
                Thread.Sleep(delayMs);
            }
        }

        return action();
    }

    public static void Run(
        Action action,
        int maxAttempts = 3,
        int delayMs     = 2000,
        string? label   = null)
        => Run<bool>(() => { action(); return true; }, maxAttempts, delayMs, label);
}
