module.exports = {
  siteMetadata: { title: 'SSG Bench' },
  plugins: [
    { resolve: 'gatsby-source-filesystem', options: { path: `${__dirname}/content` } },
    'gatsby-transformer-remark',
  ],
};
