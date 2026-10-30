// Hexo 构建启动器：API 直调（hexo-cli 的本地命令装载在本环境异常，API 等价且少一层壳）
const Hexo = require('hexo');
const h = new Hexo(process.cwd(), { silent: true });
h.env.init = true;
h.init()
  .then(() => h.call('generate', {}))
  .then(() => process.exit(0))
  .catch(e => { console.error('ERR', e.message); process.exit(1); });
