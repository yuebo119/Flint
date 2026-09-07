// Flint 静态站点生成器
// 短代码注册表实现

using System.Collections.Concurrent;
using Flint.Core.Abstractions;

namespace Flint.Core.Content.Shortcodes;

/// <summary>
/// 短代码注册表实现
/// 管理所有已注册的短代码处理器
/// </summary>
public sealed class ShortcodeRegistry : IShortcodeRegistry
{
    /// <summary>
    /// 短代码处理器字典（线程安全）
    /// </summary>
    private readonly ConcurrentDictionary<string, IShortcodeProcessor> _processors = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// 创建短代码注册表实例
    /// </summary>
    /// <param name="registerBuiltins">是否注册内置短代码</param>
    public ShortcodeRegistry(bool registerBuiltins = true)
    {
        if (registerBuiltins)
        {
            RegisterBuiltinShortcodes();
        }
    }

    /// <inheritdoc />
    public void Register(IShortcodeProcessor processor)
    {
        ArgumentNullException.ThrowIfNull(processor);

        if (string.IsNullOrWhiteSpace(processor.Name))
        {
            throw new ArgumentException("短代码处理器名称不能为空", nameof(processor));
        }

        _processors[processor.Name] = processor;
    }

    /// <inheritdoc />
    public IShortcodeProcessor? Get(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        return _processors.TryGetValue(name, out var processor) ? processor : null;
    }

    /// <inheritdoc />
    public bool Contains(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        return _processors.ContainsKey(name);
    }

    /// <inheritdoc />
    public IEnumerable<string> GetRegisteredNames()
    {
        return _processors.Keys;
    }

    /// <summary>
    /// 注销短代码处理器
    /// </summary>
    /// <param name="name">短代码名称</param>
    /// <returns>是否成功注销</returns>
    public bool Unregister(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        return _processors.TryRemove(name, out _);
    }

    /// <summary>
    /// 清空所有注册的短代码处理器
    /// </summary>
    public void Clear()
    {
        _processors.Clear();
    }

    /// <summary>
    /// 获取已注册的短代码数量
    /// </summary>
    public int Count => _processors.Count;

    /// <summary>
    /// 注册内置短代码
    /// </summary>
    private void RegisterBuiltinShortcodes()
    {
        // 注册所有内置短代码处理器
        Register(new FigureShortcode());
        Register(new HighlightShortcode());
        Register(new RefShortcode());
        Register(new RelrefShortcode());
        Register(new GistShortcode());
        Register(new YoutubeShortcode());
        Register(new TweetShortcode());
        Register(new VimeoShortcode());
        Register(new InstagramShortcode());
        Register(new ParamShortcode());
    }
}
