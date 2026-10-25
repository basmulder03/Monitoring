import { defineConfig } from 'vitepress'

// Served from https://basmulder03.github.io/Plover/. A dead link fails the build, which is the link check of CI.
export default defineConfig({
  title: 'Plover',
  description: 'A small, stateless, Native AOT monitoring system for .NET 10 workloads on Kubernetes and Windows.',
  base: '/Plover/',
  cleanUrls: true,
  head: [['link', { rel: 'icon', type: 'image/svg+xml', href: '/Plover/logo.svg' }]],
  lastUpdated: true,
  srcExclude: ['node_modules/**'],
  // A fenced block marked mermaid becomes the Mermaid component, which draws it in the browser (see theme/Mermaid.vue).
  markdown: {
    config(md) {
      const fence = md.renderer.rules.fence!
      md.renderer.rules.fence = (tokens, index, options, env, self) => {
        const token = tokens[index]
        return token.info.trim() === 'mermaid'
          ? `<Mermaid code="${encodeURIComponent(token.content)}" />`
          : fence(tokens, index, options, env, self)
      }
    },
  },
  // The trend page is fetched from the perf-results branch when the site is deployed, so it is not there while the links are checked.
  ignoreDeadLinks: [/performance-trend/],
  themeConfig: {
    logo: '/logo.svg',
    nav: [
      { text: 'Design', link: '/design' },
      { text: 'Configuration', link: '/configuration' },
      { text: 'Reference', link: '/reference' },
      { text: 'Speed budgets', link: '/performance' },
      { text: 'GitHub', link: 'https://github.com/basmulder03/Plover' },
    ],
    sidebar: [
      {
        text: 'Understand',
        items: [
          { text: 'Start here', link: '/start' },
          { text: 'Overview', link: '/' },
          {
            text: 'Design',
            link: '/design',
            collapsed: true,
            items: [
              { text: 'Purpose and scope', link: '/design#purpose-and-scope' },
              { text: 'Architecture overview', link: '/design#architecture-overview' },
              { text: 'How it works', link: '/design#how-it-works' },
              { text: 'Discovery and liveness', link: '/design#discovery-and-liveness' },
              { text: 'Wire protocol and security', link: '/design#wire-protocol' },
              { text: 'Data model and health', link: '/design#data-model-and-health' },
              { text: 'Tracing and dependency graph', link: '/design#tracing-and-dependency-graph' },
              { text: 'Windows design', link: '/design#windows-design' },
              { text: 'High availability and recovery', link: '/design#high-availability-and-recovery' },
              { text: 'Central configuration', link: '/design#central-configuration' },
              { text: 'Alerting', link: '/design#alerting' },
              { text: 'Self-monitoring', link: '/design#self-monitoring' },
              { text: 'Dashboard UI', link: '/design#dashboard-ui' },
              { text: 'Deployment and packaging', link: '/design#deployment-and-packaging' },
              { text: 'Code structure', link: '/design#code-structure' },
              { text: 'Testing strategy', link: '/design#testing-strategy' },
            ],
          },
          { text: 'The dashboard in motion', link: '/dashboard' },
          { text: 'Glossary', link: '/glossary' },
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
          { text: 'Roadmap', link: '/roadmap' },
          { text: 'Releases', link: '/releases' },
        ],
      },
    ],
    search: { provider: 'local' },
    outline: [2, 3],
    socialLinks: [{ icon: 'github', link: 'https://github.com/basmulder03/Plover' }],
  },
})
