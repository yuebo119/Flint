// Flint 静态站点生成器
// 内部投影对象的标记接口
//
// 背景：Flint 为了对齐 Hugo 的模板 API，把若干**引擎内部结构**包装成 ScriptObject
// 暴露给模板（页面片段 `.Content` 的 `to_html` 形态、词条映射 `.Data.Terms`、
// 分页器、页面 store 等）。它们与"用户数据 map"在 .NET 类型上同为 ScriptObject，
// 模板侧无从区分。
//
// 问题：主题里的"递归遍历 map/slice 做转换"辅助函数（FixIt 的
// camel-case-keys.html 是最典型的例子）会依据 `reflect.IsMap` / `reflect.IsSlice`
// 逐层下钻。内部投影一旦被判成 map/slice，就会把引擎内部成员当数据展开，
// 而内部成员的引用图是**有环的**（store → 页面对象 → 页面片段 → …），
// 递归因此永不收敛，200 层后触发 partial 深度守卫，整页渲染失败。
//
// 修法：给这些投影打标记，`reflect_is_map` / `reflect_is_slice` 一律回答 false，
// `as_list` / `as_pairs` 一律返回空——模板把它们当**不透明标量**处理。
// 注意**不要**标记真正的集合（页面集合 LazyPageList、资源集合 PageResourcesObject），
// 它们在 Hugo 里就是 slice，必须继续可遍历。
namespace Flint.Core.Templates;

/// <summary>
/// 引擎内部投影对象：对模板表现为不透明标量（既不是 Hugo 意义上的 map，也不是 slice）
/// </summary>
internal interface IFlintNonDataObject
{
}

/// <summary>
/// 语言对象（.Site.Language）：字符串化时给出**语言码**（Hugo 的
/// <c>lang.Language.String()</c> 即 Lang）。ScriptObject.ToString 是密封的，
/// 故用此标记接口让 FlintScribanContext.ObjectToString 走语言码分支，
/// 模板 `{{ site.language }}` 写进 HTML 属性时得 "zh-cn" 而非 map 转储
/// </summary>
internal interface ILanguageCode
{
    /// <summary>语言码（如 zh-cn）</summary>
    string LanguageCodeValue { get; }
}
