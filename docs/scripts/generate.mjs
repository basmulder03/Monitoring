// Writes the reference pages of the site from the source, so they cannot drift from the code: docs/reference/*.md (not committed).
// Run by `npm run build` and `npm run dev`. Reads only; it needs no packages.
import { readdirSync, readFileSync, writeFileSync, mkdirSync, statSync, existsSync } from 'node:fs'
import { join, relative, basename, dirname } from 'node:path'
import { fileURLToPath } from 'node:url'

const root = join(dirname(fileURLToPath(import.meta.url)), '..', '..')
const out = join(root, 'docs', 'reference')
mkdirSync(out, { recursive: true })
const GITHUB = 'https://github.com/basmulder03/Plover/blob/main/'

const walk = (dir, accept) => readdirSync(dir).flatMap(name => {
  if (['bin', 'obj', 'node_modules', 'dist'].includes(name)) return []
  const path = join(dir, name)
  return statSync(path).isDirectory() ? walk(path, accept) : accept(path) ? [path] : []
})
const read = path => readFileSync(path, 'utf8')
const rel = path => relative(root, path).split('\\').join('/')
const link = path => `[${rel(path)}](${GITHUB}${rel(path)})`

// XML doc comments to Markdown that is safe in a table cell and in Vue.
function plain(xml) {
  return (xml ?? '')
    .replace(/<see langword="(\w+)"\s*\/>/g, '`$1`')
    .replace(/<(?:see|seealso) cref="([^"]+)"\s*\/>/g, (_, c) => '`' + c.replace(/^[A-Z]:/, '').replace(/^.*\./, '') + '`')
    .replace(/<paramref name="(\w+)"\s*\/>/g, '`$1`')
    .replace(/<c>(.*?)<\/c>/gs, '`$1`')
    .replace(/<\/?[a-z]+[^>]*>/g, ' ')
    .replace(/&lt;/g, '<').replace(/&gt;/g, '>').replace(/&amp;/g, '&')
    .replace(/\s+/g, ' ').trim()
    .replace(/\|/g, '\\|').replace(/\{\{/g, '&#123;&#123;')
}
const tag = (doc, name) => doc.match(new RegExp(`<${name}>([\\s\\S]*?)</${name}>`))?.[1]
const paramDocs = doc => Object.fromEntries([...doc.matchAll(/<param name="(\w+)">([\s\S]*?)<\/param>/g)].map(m => [m[1], plain(m[2])]))
const docText = block => block.split('\n').map(l => l.trim().replace(/^\/\/\/ ?/, '')).join('\n')

// The declarations of a C# file, each with its doc comment: records (with their parameters), enums (with their members), classes.
const DECLARATION = /((?:[ \t]*\/\/\/.*\n)+)(?:[ \t]*\[.*\]\n)*[ \t]*(?:public|internal)\s+(?:sealed\s+|abstract\s+|static\s+|partial\s+|readonly\s+)*(record struct|record|enum|class|interface)\s+(\w+)/g

function splitParams(text) {
  const parts = []; let depth = 0; let cur = ''
  for (const ch of text) {
    if ('<([{'.includes(ch)) depth++
    if ('>)]}'.includes(ch)) depth--
    if (ch === ',' && depth === 0) { parts.push(cur); cur = '' } else cur += ch
  }
  if (cur.trim()) parts.push(cur)
  return parts.map(p => p.trim())
}

function declarations(path) {
  const text = read(path)
  return [...text.matchAll(DECLARATION)].map(m => {
    const doc = docText(m[1])
    const after = text.slice(m.index + m[0].length)
    const decl = { kind: m[2], name: m[3], summary: plain(tag(doc, 'summary')), doc, params: [], members: [], after }
    if (m[2].startsWith('record') && after.trimStart().startsWith('(')) {
      let depth = 0; let end = 0; const start = after.indexOf('(')
      for (let i = start; i < after.length; i++) {
        if (after[i] === '(') depth++
        if (after[i] === ')' && --depth === 0) { end = i; break }
      }
      const docs = paramDocs(doc)
      decl.params = splitParams(after.slice(start + 1, end)).map(p => {
        const [type, name] = [p.replace(/=.*$/s, '').trim().replace(/\s+\w+$/, ''), p.replace(/=.*$/s, '').trim().split(/\s+/).pop()]
        return { name, type, doc: docs[name] ?? '', optional: p.includes('=') }
      })
    }
    if (m[2] === 'enum') {
      const body = after.slice(after.indexOf('{') + 1, after.indexOf('}'))
      decl.members = [...body.matchAll(/((?:[ \t]*\/\/\/.*\n)+)[ \t]*(\w+)/g)].map(x => ({ name: x[2], doc: plain(tag(docText(x[1]), 'summary')) }))
    }
    return decl
  })
}

const recordTable = d => d.params.length
  ? `| Field | Type | Meaning |\n| --- | --- | --- |\n${d.params.map(p => `| \`${p.name}\` | \`${p.type.replace(/\|/g, '\\|')}\`${p.optional ? ' (optional)' : ''} | ${p.doc} |`).join('\n')}\n`
  : ''

// ---------- Wire protocol ----------
{
  const dir = join(root, 'src', 'Plover.Protocol')
  const sections = [['Messages', 'Messages'], ['Health', 'Health'], ['Instances', 'Instances'], ['Reports', 'Reports carried in `PullResult`'], ['Framing', 'Framing'], ['Versioning', 'Versioning']]
  let md = `# Wire protocol reference\n\nGenerated from the XML documentation of \`src/Plover.Protocol\`. How the messages are used is described in [the design](../design#wire-protocol).\n\n`
  for (const [folder, title] of sections) {
    const decls = walk(join(dir, folder), p => p.endsWith('.cs')).flatMap(p => declarations(p).map(d => ({ ...d, path: p })))
      .filter(d => d.kind !== 'class' || d.params.length || d.summary)
    if (!decls.length) continue
    md += `## ${title}\n\n`
    for (const d of decls.sort((a, b) => a.name.localeCompare(b.name))) {
      md += `### ${d.name}\n\n${d.summary}\n\n${recordTable(d)}`
      if (d.members.length) md += `| Value | Meaning |\n| --- | --- |\n${d.members.map(m => `| \`${m.name}\` | ${m.doc} |`).join('\n')}\n`
      md += `\n<sub>${link(d.path)}</sub>\n\n`
    }
  }
  writeFileSync(join(out, 'protocol.md'), md)
}

// ---------- HTTP API ----------
{
  const rows = []
  for (const path of walk(join(root, 'src'), p => p.endsWith('.cs'))) {
    const text = read(path)
    for (const m of text.matchAll(/\.Map(Get|Post|Put|Delete|Patch)\(\s*"([^"]+)"/g)) {
      rows.push({ project: rel(path).split('/')[1], feature: rel(path).match(/Features\/(\w+)/)?.[1] ?? '', method: m[1].toUpperCase(), route: m[2], path })
    }
  }
  const byProject = Map.groupBy(rows, r => r.project)
  let md = `# HTTP API reference\n\nEvery route the services map, found in the source (\`MapGet\`, \`MapPost\` and the like). The dashboard is the main client of the controller's routes; \`/api/events\` is a Server-Sent Events stream.\n\n`
  for (const [project, list] of [...byProject].sort()) {
    md += `## ${project}\n\n| Method | Route | Feature | Source |\n| --- | --- | --- | --- |\n`
    for (const r of list.sort((a, b) => a.route.localeCompare(b.route))) md += `| ${r.method} | \`${r.route}\` | ${r.feature} | ${link(r.path)} |\n`
    md += '\n'
  }
  writeFileSync(join(out, 'api.md'), md)
}

// ---------- UI: components and icons ----------
{
  const ui = join(root, 'src', 'Plover.Ui', 'ui', 'src')
  const sprite = read(join(ui, 'components', 'Sprite.tsx'))
  const icons = [...sprite.matchAll(/<symbol id="i-([\w-]+)"[^>]*>([\s\S]*?)<\/symbol>/g)].map(m => ({ name: m[1], body: m[2] }))
  const union = read(join(ui, 'components', 'Icon.tsx')).match(/type IconName =([^;]+);/)?.[1].match(/"[\w-]+"/g)?.map(s => s.slice(1, -1)) ?? []
  const missing = union.filter(n => !icons.some(i => i.name === n)).concat(icons.filter(i => !union.includes(i.name)).map(i => i.name))
  if (missing.length) throw new Error(`IconName and the sprite disagree about: ${missing.join(', ')}`)

  const files = walk(ui, p => p.endsWith('.tsx') && !p.endsWith('.test.tsx'))
  const components = []
  for (const path of files) {
    const text = read(path)
    for (const m of text.matchAll(/(?:\/\*\*([\s\S]*?)\*\/\s*)?export function ([A-Z]\w*)\(/g)) {
      let depth = 0; let i = m.index + m[0].length - 1; const start = i
      for (; i < text.length; i++) { if (text[i] === '(') depth++; if (text[i] === ')' && --depth === 0) break }
      let signature = text.slice(start + 1, i).replace(/\s+/g, ' ').trim()
      const named = signature.match(/:\s*(\w+Props)$/)
      if (named) signature = text.match(new RegExp(`interface ${named[1]} \\{([\\s\\S]*?)\\n\\}`))?.[1].replace(/\s+/g, ' ').trim() ?? signature
      components.push({ name: m[2], doc: plain(m[1]?.replace(/^\s*\*\s?/gm, '')), props: signature, path })
    }
  }
  const bySection = Map.groupBy(components, c => rel(c.path).includes('/views/') ? 'Views' : 'Shared components')
  let md = `# Dashboard components and icons\n\nGenerated from \`src/Plover.Ui/ui/src\` (Preact, TypeScript). The dashboard is a static app that the controller serves; it has no component library, so what is below is all of it.\n\n`
  md += `## Icons\n\nThe icons are defined once as an SVG sprite (\`Sprite.tsx\`) and used by reference: \`<Icon name="bell" />\`. There is no font and no network request for them. \`IconName\` lists the valid names, and the build of this page fails when the type and the sprite disagree.\n\n<div class="icon-grid">\n`
  for (const i of icons) md += `<figure><svg viewBox="0 0 24 24" width="32" height="32" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true">${i.body}</svg><figcaption><code>${i.name}</code></figcaption></figure>\n`
  md += `</div>\n\n`
  for (const [section, list] of [...bySection].sort()) {
    md += `## ${section}\n\n`
    for (const c of list.sort((a, b) => a.name.localeCompare(b.name))) {
      md += `### ${c.name}\n\n${c.doc || '_No description yet._'}\n\n\`\`\`ts\n${c.name}(${c.props.replace(/^\{ /, '{ ')})\n\`\`\`\n\n<sub>${link(c.path)}</sub>\n\n`
    }
  }
  writeFileSync(join(out, 'ui.md'), md)
}

// ---------- Code structure ----------
{
  const projects = readdirSync(join(root, 'src')).filter(n => existsSync(join(root, 'src', n, `${n}.csproj`)))
  const edges = projects.flatMap(p => [...read(join(root, 'src', p, `${p}.csproj`)).matchAll(/ProjectReference Include="[^"]*?([\w.]+)\.csproj"/g)].map(m => [p, m[1]]))
  const id = s => s.replace(/\W/g, '_')
  let md = `# Code structure\n\nGenerated from the projects and slices in \`src\`. The rules these follow are enforced by the architecture tests (see [Testing](../testing)).\n\n## Projects and what they reference\n\n\`\`\`mermaid\nflowchart BT\n${projects.map(p => `    ${id(p)}["${p}"]`).join('\n')}\n${edges.map(([a, b]) => `    ${id(a)} --> ${id(b)}`).join('\n')}\n\`\`\`\n\n`
  md += `## Features of each project\n\nEvery feature is a vertical slice: a folder with its own types and one \`Add…\` registration. The text is the summary of that registration.\n\n`
  for (const p of projects) {
    const features = join(root, 'src', p, 'Features')
    if (!existsSync(features)) continue
    const rows = []
    for (const f of readdirSync(features).sort()) {
      const dir = join(features, f)
      if (!statSync(dir).isDirectory()) continue
      const slice = walk(dir, x => /Slice\.cs$/.test(x)).map(read).join('\n')
      const m = slice.match(/((?:[ \t]*\/\/\/.*\n)+)[ \t]*public static \w[\w<>?]* Add\w+\(/)
      rows.push(`| \`${f}\` | ${plain(tag(docText(m?.[1] ?? ''), 'summary'))} |`)
    }
    md += `### ${p}\n\n| Feature | What it adds |\n| --- | --- |\n${rows.join('\n')}\n\n`
  }
  const rules = []
  for (const path of walk(join(root, 'src'), p => p.endsWith('.cs'))) {
    for (const d of declarations(path)) {
      if (d.kind === 'class' && /\bIAlertRule\b/.test(d.after.split('{')[0])) rules.push({ ...d, path })
    }
  }
  if (rules.length) {
    md += `## Alert rules\n\n| Rule | What it alerts on |\n| --- | --- |\n${rules.sort((a, b) => a.name.localeCompare(b.name)).map(r => `| \`${r.name}\` | ${r.summary} |`).join('\n')}\n`
  }
  writeFileSync(join(out, 'structure.md'), md)
}

console.log('reference pages written to', rel(out))
