#:package Markdig@1.4.0
#:property ManagePackageVersionsCentrally=false
// Markdig 管道并发安全探针（R7 T8.1）：共享单实例 MarkdownPipeline 被并行
// Parse+ToHtml 时输出是否漂移。结论登记 docs/PERFORMANCE-TASKS-R7.md T8.1
using System.Security.Cryptography;
using System.Text;
using Markdig;

var markdown = """
    # 标题测试 Heading
    
    这是一段**中文加粗**与 *斜体* 以及 `inline code` 的段落。

    - 列表项一
    - 列表项二 with [link](https://example.com)

    > 引用块 Quote

    ```csharp
    var x = 1; // code fence
    ```

    | 列 A | 列 B |
    |-----|------|
    | 1   | 2    |
    """;

// 单线程基线哈希
var pipeline = new MarkdownPipelineBuilder().UseAdvancedExtensions().Build();
var baseline = Hash(Markdig.Markdown.ToHtml(markdown, pipeline));
Console.WriteLine($"baseline: {baseline}");

// 并行 10k 次共享同一管道实例
var mismatches = 0;
Parallel.For(0, 10_000, i =>
{
    var html = Markdig.Markdown.ToHtml(markdown, pipeline);
    if (Hash(html) != baseline)
    {
        Interlocked.Increment(ref mismatches);
    }
});
Console.WriteLine($"parallel shared-pipeline: 10000 runs, mismatches={mismatches}");
Console.WriteLine(mismatches == 0 ? "VERDICT: safe (no divergence under parallel shared pipeline)" : "VERDICT: UNSAFE (pipeline state leak)");

static string Hash(string s) =>
    Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(s)));
