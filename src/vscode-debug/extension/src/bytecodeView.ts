import { HostBytecodeMessage } from './debugTypes';

export type BytecodeCodeRow = {
  isCurrent: boolean;
  pc: string;
  op: string;
  operands: string;
};

export type BytecodeViewModel = {
  signature: string;
  title: string;
  location: string;
  metaRows: Array<{ key: string; value: string }>;
  constants: Array<{ index: string; value: string }>;
  code: BytecodeCodeRow[];
  highlighted?: { pc: string; op: string; operands: string };
};

export function buildBytecodeViewModel(message: HostBytecodeMessage): BytecodeViewModel {
  const title = message.title ?? 'Okojo Bytecode';
  const sourcePath = message.sourceLocation?.sourcePath ?? message.sourcePath ?? '';
  const line = message.sourceLocation?.line ?? 0;
  const column = message.sourceLocation?.column ?? 0;
  const parsed = parseBytecodeText(message.text ?? '');
  const location = sourcePath.length > 0
    ? `${sourcePath}${line > 0 ? `:${line}:${column || 1}` : ''}`
    : 'No source location';
  const highlighted = parsed.code.find((entry) => entry.isCurrent);

  return {
    signature: `${title}\n${message.text ?? ''}`,
    title,
    location,
    metaRows: parsed.meta,
    constants: parsed.constants,
    code: parsed.code,
    highlighted: highlighted
      ? { pc: highlighted.pc, op: highlighted.op, operands: highlighted.operands }
      : undefined,
  };
}

export function renderBytecodeShellHtml(): string {
  return `<!DOCTYPE html>
<html lang="en">
<head>
  <meta charset="UTF-8" />
  <meta name="viewport" content="width=device-width, initial-scale=1.0" />
  <style>
    body {
      margin: 0;
      padding: 8px 10px 16px;
      font-family: var(--vscode-editor-font-family, ui-monospace, monospace);
      font-size: 12px;
      color: var(--vscode-foreground);
      background: var(--vscode-editor-background);
    }
    .header {
      margin: 0 0 8px;
    }
    .title {
      font-size: 12px;
      font-weight: 700;
      margin-bottom: 2px;
      white-space: nowrap;
      overflow: hidden;
      text-overflow: ellipsis;
    }
    .meta {
      font-size: 10px;
      color: var(--vscode-descriptionForeground);
      word-break: break-all;
    }
    .stopbar {
      display: flex;
      flex-wrap: wrap;
      gap: 8px;
      align-items: baseline;
      margin: 0 0 8px;
      padding: 4px 0;
    }
    .stopbar-label {
      font-size: 10px;
      text-transform: uppercase;
      letter-spacing: 0.08em;
      color: var(--vscode-descriptionForeground);
    }
    .stopbar-op {
      font-size: 13px;
      font-weight: 700;
    }
    .stopbar-meta,
    .stopbar-operands {
      color: var(--vscode-descriptionForeground);
    }
    .toolbar {
      display: flex;
      align-items: center;
      gap: 8px;
      margin: 0 0 8px;
      color: var(--vscode-foreground);
    }
    .pill {
      padding: 2px 6px;
      border: 1px solid var(--vscode-editorWidget-border);
      border-radius: 3px;
      background: var(--vscode-editorWidget-background);
      color: var(--vscode-descriptionForeground);
    }
    .grid {
      border-top: 1px solid var(--vscode-editorWidget-border);
      border-bottom: 1px solid var(--vscode-editorWidget-border);
    }
    .row {
      display: grid;
      grid-template-columns: 18px 60px 52px 1fr;
      gap: 8px;
      padding: 2px 6px;
      border-bottom: 1px solid var(--vscode-editorWidget-border);
      align-items: start;
    }
    .row:nth-child(even) {
      background: var(--vscode-editorInlayHint-background, transparent);
    }
    .row.current {
      background: var(--vscode-editor-findMatchHighlightBackground, #fffbcc);
    }
    .row.current .col.op,
    .row.current .col.operands {
      color: var(--vscode-foreground);
    }
    .col.pc,
    .col.line,
    .mark {
      color: var(--vscode-descriptionForeground);
    }
    .mark {
      width: 10px;
      display: inline-block;
      text-align: center;
    }
    .col.op {
      font-weight: 700;
    }
    .col.operands {
      white-space: pre-wrap;
      word-break: break-word;
    }
    .row,
    .subrow,
    .header,
    .stopbar,
    .toolbar {
      min-width: 0;
    }
    .hint {
      color: var(--vscode-descriptionForeground);
      font-style: italic;
      margin: 6px 0 0;
    }
    .section {
      margin-top: 10px;
    }
    .section-title {
      margin: 0 0 4px;
      font-weight: 700;
    }
    .subgrid {
      border-top: 1px solid var(--vscode-editorWidget-border);
    }
    .subrow {
      display: grid;
      grid-template-columns: 80px 1fr;
      gap: 8px;
      padding: 2px 6px;
      border-bottom: 1px solid var(--vscode-editorWidget-border);
    }
    .subrow.meta-row {
      grid-template-columns: 120px 1fr;
    }
    .subrow:nth-child(even) {
      background: var(--vscode-editorInlayHint-background, transparent);
    }
  </style>
</head>
<body>
  <div class="header">
    <div class="title" id="title">Okojo Bytecode</div>
    <div class="meta" id="location">No source location</div>
  </div>
  <div class="stopbar empty" id="stopbar">
    <span class="stopbar-label">Current</span>
    <span class="stopbar-op" id="stopbar-op">No highlighted instruction</span>
    <span class="stopbar-meta" id="stopbar-meta"></span>
    <span class="stopbar-operands" id="stopbar-operands"></span>
  </div>
  <div class="toolbar">
    <span>View</span>
    <span class="pill">Current stop + disassembly</span>
  </div>
  <div class="grid" id="code-rows">
    <div class="row"><span class="mark"></span><span class="col pc">-</span><span class="col line"></span><span class="col op">No bytecode</span></div>
  </div>
  <div class="hint">Current instruction is highlighted and auto-centered after each stop.</div>
  <div class="section">
    <div class="section-title">Metadata</div>
    <div class="subgrid" id="summary"></div>
  </div>
  <div class="section">
    <div class="section-title">Constants</div>
    <div class="subgrid" id="constants">
      <div class="subrow"><span class="col pc">n/a</span><span>No constants.</span></div>
    </div>
  </div>
  <script>
    const escapeHtml = (text) => String(text)
      .replaceAll('&', '&amp;')
      .replaceAll('<', '&lt;')
      .replaceAll('>', '&gt;');

    let currentSignature = null;
    let currentPc = null;

    const updateStopbar = (model) => {
      const stopbar = document.getElementById('stopbar');
      const stopbarOp = document.getElementById('stopbar-op');
      const stopbarMeta = document.getElementById('stopbar-meta');
      const stopbarOperands = document.getElementById('stopbar-operands');
      if (model.highlighted) {
        stopbar.classList.remove('empty');
        stopbarOp.textContent = model.highlighted.op;
        stopbarMeta.textContent = 'pc ' + model.highlighted.pc;
        stopbarOperands.textContent = model.highlighted.operands || '';
      } else {
        stopbar.classList.add('empty');
        stopbarOp.textContent = 'No highlighted instruction';
        stopbarMeta.textContent = '';
        stopbarOperands.textContent = '';
      }
    };

    const updateHeader = (model) => {
      document.getElementById('title').textContent = model.title || 'Okojo Bytecode';
      document.getElementById('location').textContent = model.location || 'No source location';
    };

    const scrollToCurrent = () => {
      const current = document.querySelector('.row.current');
      if (current && current.scrollIntoView) {
        try { current.scrollIntoView({ block: 'center', behavior: 'auto' }); }
        catch { current.scrollIntoView(); }
      }
    };

    const updateCurrentRow = (pc) => {
      if (currentPc !== null) {
        const previous = document.querySelector('.row.current');
        if (previous) {
          previous.classList.remove('current');
          const prevMark = previous.querySelector('.mark');
          if (prevMark) prevMark.textContent = '';
        }
      }

      currentPc = pc ?? null;
      if (currentPc === null) {
        return;
      }

      const next = document.querySelector('.row[data-pc="' + escapeHtml(currentPc) + '"]');
      if (next) {
        next.classList.add('current');
        const nextMark = next.querySelector('.mark');
        if (nextMark) nextMark.textContent = '>';
      }
    };

    const renderFull = (model) => {
      updateHeader(model);
      updateStopbar(model);

      const summary = document.getElementById('summary');
      summary.innerHTML = (model.metaRows || []).map((entry) =>
        '<div class="subrow meta-row"><span class="col pc">' + escapeHtml(entry.key) + '</span><span>' + escapeHtml(entry.value) + '</span></div>'
      ).join('');

      const constants = document.getElementById('constants');
      constants.innerHTML = (model.constants && model.constants.length > 0)
        ? model.constants.map((entry) =>
            '<div class="subrow"><span class="col pc">' + escapeHtml(entry.index) + '</span><span>' + escapeHtml(entry.value) + '</span></div>'
          ).join('')
        : '<div class="subrow"><span class="col pc">n/a</span><span>No constants.</span></div>';

      const codeRows = document.getElementById('code-rows');
      codeRows.innerHTML = (model.code && model.code.length > 0)
        ? model.code.map((entry) =>
            '<div class="row' + (entry.isCurrent ? ' current' : '') + '" data-pc="' + escapeHtml(entry.pc) + '">' +
              '<span class="mark">' + (entry.isCurrent ? '&gt;' : '') + '</span>' +
              '<span class="col pc">' + escapeHtml(entry.pc) + '</span>' +
              '<span class="col line"></span>' +
              '<span><span class="col op">' + escapeHtml(entry.op) + '</span>' + (entry.operands ? ' <span class="col operands">' + escapeHtml(entry.operands) + '</span>' : '') + '</span>' +
            '</div>'
          ).join('')
        : '<div class="row"><span class="mark"></span><span class="col pc">-</span><span class="col line"></span><span class="col op">No bytecode</span></div>';

      currentSignature = model.signature || null;
      currentPc = model.highlighted ? model.highlighted.pc : null;
      scrollToCurrent();
    };

    window.addEventListener('message', (event) => {
      if (event.data && event.data.type === 'bytecode-update') {
        const model = event.data.payload || {};

        if (currentSignature && model.signature === currentSignature) {
          updateStopbar(model);
          updateCurrentRow(model.highlighted ? model.highlighted.pc : null);
          scrollToCurrent();
          return;
        }

        renderFull(model);
      }
    });

    const vscode = typeof acquireVsCodeApi === 'function' ? acquireVsCodeApi() : null;
    if (vscode) {
      vscode.setState({ ready: true });
    }
  </script>
</body>
</html>`;
}

function parseBytecodeText(text: string): {
  meta: Array<{ key: string; value: string }>;
  constants: Array<{ index: string; value: string }>;
  code: BytecodeCodeRow[];
} {
  const meta: Array<{ key: string; value: string }> = [];
  const constants: Array<{ index: string; value: string }> = [];
  const code: BytecodeCodeRow[] = [];
  let section: 'meta' | 'constants' | 'code' = 'meta';

  for (const rawLine of text.split(/\r?\n/)) {
    const line = rawLine.trimEnd();
    if (line.length === 0) {
      continue;
    }
    if (line === '.constants') {
      section = 'constants';
      continue;
    }
    if (line === '.code') {
      section = 'code';
      continue;
    }

    if (section === 'meta' && line.startsWith(';')) {
      const body = line.slice(1).trim();
      const colon = body.indexOf(':');
      if (colon > 0) {
        meta.push({
          key: body.slice(0, colon).trim(),
          value: body.slice(colon + 1).trim(),
        });
      }
      continue;
    }

    if (section === 'constants') {
      const match = line.match(/^\s*(\[[^\]]+\])\s+(.*)$/);
      if (match) {
        constants.push({ index: match[1], value: match[2] });
      }
      continue;
    }

    if (section === 'code') {
      const isCurrent = line.startsWith('=> ');
      const body = isCurrent ? line.slice(3) : line;
      const match = body.match(/^\s*(\d+)\s+([A-Za-z0-9_]+)(?:\s+(.*))?$/);
      if (match) {
        code.push({
          isCurrent,
          pc: match[1],
          op: match[2],
          operands: match[3] ?? '',
        });
      }
    }
  }

  return { meta, constants, code };
}
