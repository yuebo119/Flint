module.exports = function (eleventyConfig) {
  // 语料不含 tag，索引页用的 collections.posts 需要显式定义，否则首页为空
  eleventyConfig.addCollection("posts", (api) => api.getFilteredByGlob("src/posts/*.md"));

  return {
    dir: { input: "src", output: "_site", includes: "_includes" },
  };
};