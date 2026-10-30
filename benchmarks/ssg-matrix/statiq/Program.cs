using Statiq.App;
using Statiq.Web;

// Statiq 矩阵臂启动器：走 Statiq.Web 默认管线，站点内容在 input/ 下
return await Bootstrapper
    .Factory
    .CreateWeb(args)
    .RunAsync();
