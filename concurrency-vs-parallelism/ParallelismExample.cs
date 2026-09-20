// Parallelism در عمل — کار CPU-Bound
//
// Parallelism یعنی اجرای واقعیِ چند کار در آنِ واحد، روی چند هسته‌ی
// پردازنده. جایی معنی داره که کار واقعاً CPU رو مشغول می‌کنه —
// محاسبات سنگین، پردازش تصویر، فشرده‌سازی، و مثل این‌ها.

public class ParallelismExample
{
    // اینجا هر Item یک محاسبه‌ی سنگین ریاضی (CPU-Bound) نیاز داره.
    // Parallel.For این کار رو بین Thread‌های Pool تقسیم می‌کنه تا از
    // همه‌ی هسته‌های CPU استفاده بشه — برخلاف async/await که برای
    // این نوع کار هیچ کمکی نمی‌کنه (چون کار واقعاً "منتظر" چیزی نیست).
    public double[] ComputeHeavyMath(int[] input)
    {
        var results = new double[input.Length];

        Parallel.For(0, input.Length, i =>
        {
            results[i] = HeavyComputation(input[i]);
        });

        return results;
    }

    private double HeavyComputation(int n)
    {
        double result = 0;
        for (int i = 0; i < 5_000_000; i++)
        {
            result += Math.Sqrt(i) * Math.Sin(n);
        }
        return result;
    }

    // نکته‌ی مهم: تعداد Thread هایی که Parallel.For استفاده می‌کنه رو
    // معمولاً نباید دستی مدیریت کرد. اگه لازم شد محدودش کنی
    // (مثلاً برای اینکه CPU رو کامل قبضه نکنه):
    public double[] ComputeHeavyMath_LimitedDegree(int[] input, int maxDegreeOfParallelism)
    {
        var results = new double[input.Length];

        Parallel.For(0, input.Length,
            new ParallelOptions { MaxDegreeOfParallelism = maxDegreeOfParallelism },
            i => { results[i] = HeavyComputation(input[i]); });

        return results;
    }
}
