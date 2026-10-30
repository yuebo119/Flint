import React from 'react';
import { graphql } from 'gatsby';

export default function Post({ data }) {
  const p = data.markdownRemark;
  return (
    <article>
      <h1>{p.frontmatter.title}</h1>
      <div dangerouslySetInnerHTML={{ __html: p.html }} />
    </article>
  );
}

export const query = graphql`
  query ($id: String!) {
    markdownRemark(id: { eq: $id }) {
      frontmatter {
        title
      }
      html
    }
  }
`;
