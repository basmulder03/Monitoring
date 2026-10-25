// Bundles the dashboard into the files the controller embeds: app.js, styles.css and index.html in ../wwwroot.
import { build } from "esbuild";
import { copyFile, mkdir } from "node:fs/promises";
import { fileURLToPath } from "node:url";

const out = new URL("../wwwroot/", import.meta.url);
// A path, not the pathname of the address: on Windows that one reads /D:/..., which is no folder.
const outdir = fileURLToPath(out);
await mkdir(out, { recursive: true });

await build({
  entryPoints: { app: "src/main.tsx" },
  outdir,
  bundle: true,
  minify: true,
  target: "es2022",
  format: "iife",
  jsx: "automatic",
  jsxImportSource: "preact",
  legalComments: "none",
  logLevel: "info",
});

// Not minified: the style sheet is small, and the tests read the design tokens from it.
await build({
  entryPoints: { styles: "src/styles/index.css" },
  outdir,
  bundle: true,
  logLevel: "info",
});

await copyFile(new URL("public/index.html", import.meta.url), new URL("index.html", out));
