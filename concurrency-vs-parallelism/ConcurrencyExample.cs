// Concurrency در عمل — کار IO-Bound
//
// Concurrency یعنی مدیریت چند کار که "به‌نظر" هم‌زمان پیش می‌رن.
// بهترین جا برای استفاده از Concurrency، کارهای IO-Bound هستن —
// جایی که Thread منتظر یک منبع خارجی (دیتابیس، شبکه، دیسک) می‌مونه،
// نه اینکه واقعاً CPU رو مشغول کنه.

public class ConcurrencyExample
{
    private readonly HttpClient _http = new();

    // این متد سه تا Request رو "هم‌زمان" می‌فرسته، ولی نه با سه Thread جدا.
    // هر Task منتظر جواب شبکه می‌مونه؛ در این حین Thread آزاده و می‌تونه
    // کار دیگه‌ای انجام بده. این دقیقاً همون چیزیه که async/await براش
    // ساخته شده: Concurrency بدون نیاز به Thread اضافه.
    public async Task<string[]> FetchAllAsync(string[] urls)
    {
        var tasks = urls.Select(url => _http.GetStringAsync(url));
        return await Task.WhenAll(tasks);
    }

    // مقایسه: اگه به‌جای async/await از Thread واقعی استفاده کنیم
    // (که برای IO-Bound کار اشتباهیه)، فقط منابع سیستم رو هدر می‌دیم:
    public string[] FetchAllWithThreads_BadExample(string[] urls)
    {
        var results = new string[urls.Length];
        var threads = new Thread[urls.Length];

        for (int i = 0; i < urls.Length; i++)
        {
            int index = i;
            threads[i] = new Thread(() =>
            {
                // هر Thread اینجا فقط منتظر می‌مونه — CPU بیکاره،
                // ولی حافظه و Context Switch رایگان نیست.
                results[index] = _http.GetStringAsync(urls[index]).GetAwaiter().GetResult();
            });
            threads[i].Start();
        }

        foreach (var t in threads) t.Join();
        return results;
    }
}
