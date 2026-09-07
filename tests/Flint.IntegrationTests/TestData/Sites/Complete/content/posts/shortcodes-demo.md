+++
title = "短代码演示"
date = 2024-01-20T12:00:00+08:00
draft = false
tags = ["短代码", "演示", "测试"]
categories = ["技术"]
description = "这篇文章演示了各种短代码的使用"
+++

## 短代码演示

本文演示了 Flint 支持的各种短代码。

### Figure 短代码

{{< figure src="/images/sample.png" title="示例图片" caption="这是图片说明" alt="示例图片" >}}

### Highlight 短代码

{{< highlight csharp "linenos=true,hl_lines=3 5" >}}
using System;

public class Program
{
    public static void Main()
    {
        Console.WriteLine("Hello, World!");
    }
}
{{< /highlight >}}

### Gist 短代码

{{< gist username gist_id >}}

### YouTube 短代码

{{< youtube dQw4w9WgXcQ >}}

### Vimeo 短代码

{{< vimeo 123456789 >}}

### 自定义短代码

{{< notice type="warning" >}}
这是一个警告通知。
{{< /notice >}}

{{< notice type="info" >}}
这是一个信息通知。
{{< /notice >}}

## 结论

短代码演示完成。
