import { defineConfig } from 'vitepress'
import { withMermaid } from 'vitepress-plugin-mermaid'

// Served from https://basmulder03.github.io/Monitoring/. A dead link fails the build, which is the link check of CI.
export default withMermaid(defineConfig({
  title: 'Monitoring',
  description: 'A small, stateless, Native AOT monitoring system for .NET 10 workloads on Kubernetes and Windows.',
  base: '/Monitoring/',
  cleanUrls: true,
  lastUpdated: true,
  srcExclude: ['node_modules/**'],
  // The trend page is fetched from the perf-results branch when the site is deployed, so it is not there while the links are checked.
  ignoreDeadLinks: [/performance-trend/],
  themeConfig: {
    nav: [
      { text: 'Design', link: '/design' },
      { text: 'Configuration', link: '/configuration' },
      { text: 'Reference', link: '/reference' },
      { text: 'Speed budgets', link: '/performance' },
      { text: 'GitHub', link: 'https://github.com/basmulder03/Monitoring' },
    ],
    sidebar: [
      {
        text: 'Understand',
        items: [
          { text: 'Overview', link: '/' },
          { text: 'Design', link: '/design' },
        ],
      },
      {
        text: 'Run',
        items: [
          { text: 'Configuration', link: '/configuration' },
          { text: 'Windows servers', link: '/windows' },
          { text: 'OpenShift', link: '/openshift' },
        ],
      },
      {
        text: 'Reference (generated from the code)',
        items: [
          { text: 'About the reference', link: '/reference' },
          { text: 'Code structure', link: '/reference/structure' },
          { text: 'Wire protocol', link: '/reference/protocol' },
          { text: 'HTTP API', link: '/reference/api' },
          { text: 'Dashboard components and icons', link: '/reference/ui' },
        ],
      },
      {
        text: 'Build',
        items: [
          { text: 'Testing', link: '/testing' },
          { text: 'Speed budgets', link: '/performance' },
          { text: 'Contributing', link: '/contributing' },
        ],
      },
    ],
    search: { provider: 'local' },
    outline: [2, 3],
    socialLinks: [{ icon: 'github', link: 'https://github.com/basmulder03/Monitoring' }],
  },
}))
