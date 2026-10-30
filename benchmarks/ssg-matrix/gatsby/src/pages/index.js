import React from 'react';
import { graphql } from 'gatsby';

export default function Home({ data }) {
  return (
    <div>
      <h1>Home</h1>
      <ul>
        {data.allMarkdownRemark.nodes.map((n, i) => (
          <li key={i}>{n.frontmatter.title}</li>
        ))}
      </ul>
    </div>
  );
}

export const query = graphql`
  {
    allMarkdownRemark {
      nodes {
        frontmatter {
          title
        }
      }
    }
  }
`;
