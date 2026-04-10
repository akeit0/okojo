import fs from "node:fs";
import path from "node:path";
import { createRequire } from "node:module";

const [, , inputHtmlPath, toolRootArg] = process.argv;

if (!inputHtmlPath) {
  process.stderr.write("Usage: node ConvertHtmlToMarkdown.mjs <input-html> [tool-root]\n");
  process.exit(1);
}

const toolRoot = toolRootArg || path.dirname(new URL(import.meta.url).pathname);
const localRequire = createRequire(path.join(toolRoot, "package.json"));

let lib;
try {
  lib = localRequire("turndown");
} catch {
  try {
    lib = localRequire("turndown/lib/turndown");
  } catch {
    lib = null;
  }
}

if (!lib) {
  process.stderr.write("Missing dependency: turndown. Run `cd tools/OkojoEcma262 && npm install`.\n");
  process.exit(2);
}

const TurndownService =
  lib.TurndownService ||
  (typeof lib === "function" ? lib : null) ||
  (lib.default && lib.default.TurndownService) ||
  (lib.default && lib.default.default && lib.default.default.TurndownService);

if (typeof TurndownService !== "function") {
  process.stderr.write("Could not resolve TurndownService from installed package.\n");
  process.exit(3);
}

function normalizeSpecHtml(html) {
  return html
    .replace(/&nbsp;/g, " ")
    .replace(/<dfn\b[^>]*>/g, "<strong>")
    .replace(/<\/dfn>/g, "</strong>")
    .replace(/<var\b[^>]*class="field"[^>]*>/g, "<code>")
    .replace(/<var\b[^>]*>/g, "<code>")
    .replace(/<\/var>/g, "</code>")
    .replace(/<emu-xref\b[^>]*>/g, "")
    .replace(/<\/emu-xref>/g, "")
    .replace(/<emu-eqn\b[^>]*>/g, "<code>")
    .replace(/<\/emu-eqn>/g, "</code>")
    .replace(/<emu-nt\b[^>]*>/g, "<code>")
    .replace(/<\/emu-nt>/g, "</code>")
    .replace(/<emu-val\b[^>]*>/g, "<code>")
    .replace(/<\/emu-val>/g, "</code>")
    .replace(/<emu-(?!clause|intro|note|example|alg|table|figure)[a-z-]+\b[^>]*>/g, "<span>")
    .replace(/<\/emu-(?!clause|intro|note|example|alg|table|figure)[a-z-]+>/g, "</span>")
    .replace(/<emu-(clause|intro|note|example|alg|table|figure)\b[^>]*>/g, "<div>")
    .replace(/<\/emu-(clause|intro|note|example|alg|table|figure)>/g, "</div>");
}

const html = normalizeSpecHtml(fs.readFileSync(inputHtmlPath, "utf8"));
const service = new TurndownService({
  codeBlockStyle: "fenced",
  bulletListMarker: "-",
  headingStyle: "atx"
});

process.stdout.write(service.turndown(html));
