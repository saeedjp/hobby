// Corner Case ها — وقتی Concurrency و Parallelism رو قاطی می‌کنیم

public class CornerCases
{
    // Corner Case 1: استفاده از Parallel.For برای کار IO-Bound
    //
    // این کد کامپایل می‌شه و کار می‌کنه، ولی اشتباهه: هر Iteration یک
    // Thread واقعی از Thread Pool می‌گیره و همون‌جا منتظر جواب شبکه
    // می‌مونه (چون GetAwaiter().GetResult() به‌صورت Sync اجرا می‌شه).
    // نتیجه: با ۱۰۰۰ تا URL، ۱۰۰۰ تا Thread رزرو می‌شه که اکثرشون
    // فقط منتظرن — Thread Pool Starvation.
    public void FetchUrls_WrongWay(string[] urls)
    {
        var http = new HttpClient();
        Parallel.ForEach(urls, url =>
        {
            var result = http.GetStringAsync(url).GetAwaiter().GetResult(); // Sync-over-Async
        });
    }

    // راه درست همون Concurrency با async/await هست (نه Parallelism):
    public async Task FetchUrls_RightWay(string[] urls)
    {
        var http = new HttpClient();
        var tasks = urls.Select(url => http.GetStringAsync(url));
        await Task.WhenAll(tasks);
    }

    // Corner Case 2: async void در کاری که باید منتظرش بمونیم
    //
    // async void یعنی Exception داخلش قابل Catch کردن نیست و
    // Caller نمی‌تونه بفهمه کی تموم می‌شه. تقریباً همیشه اشتباهه،
    // به‌جز برای Event Handler ها.
    public async void ProcessOrder_Wrong(Order order) // نباید async void باشه
    {
        await SaveOrderAsync(order); // اگه اینجا Exception بخوره، هیچ‌کس نمی‌فهمه
    }

    public async Task ProcessOrder_Right(Order order)
    {
        await SaveOrderAsync(order); // حالا Caller می‌تونه await کنه و Exception رو بگیره
    }

    // Corner Case 3: تعداد هسته‌ی CPU محدوده — Parallelism نامحدود نیست
    //
    // اگه Degree of Parallelism رو کنترل نکنیم و همزمان چند عملیات
    // CPU-Bound سنگین رو موازی اجرا کنیم، ممکنه از تعداد هسته‌های
    // واقعی بیشتر بشه و به‌جای سریع‌تر شدن، فقط Context Switch
    // اضافه ایجاد بشه.
    public void ShowCoreCount()
    {
        Console.WriteLine($"Available CPU cores: {Environment.ProcessorCount}");
        // معمولاً بهتره MaxDegreeOfParallelism رو نزدیک به همین عدد نگه داریم،
        // نه بیشتر.
    }

    private Task SaveOrderAsync(Order order) => Task.CompletedTask;
}

public record Order(Guid Id, decimal Amount);
