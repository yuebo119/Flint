const path = require('path');

exports.createPages = async ({ graphql, actions }) => {
  const { createPage } = actions;
  const result = await graphql(`
    {
      allMarkdownRemark {
        nodes {
          id
        }
      }
    }
  `);
  if (result.errors) throw result.errors;
  result.data.allMarkdownRemark.nodes.forEach((node, idx) => {
    createPage({
      path: `/post-${idx}/`,
      component: path.resolve('./src/templates/post.js'),
      context: { id: node.id },
    });
  });
};
