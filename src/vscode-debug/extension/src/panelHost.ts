import * as vscode from 'vscode';

class PanelHost {
  private static readonly panelTitle = 'Okojo Bytecode';
  private panel: vscode.WebviewPanel | undefined;

  ensurePanel(): vscode.WebviewPanel {
    return this.getOrCreatePanel(true);
  }

  private getOrCreatePanel(reveal: boolean): vscode.WebviewPanel {
    if (!this.panel) {
      this.panel = vscode.window.createWebviewPanel(
        'okojoBytecode',
        PanelHost.panelTitle,
        vscode.ViewColumn.Beside,
        { enableFindWidget: true, enableScripts: true, retainContextWhenHidden: true }
      );
      this.panel.onDidDispose(() => {
        this.panel = undefined;
      });
    } else if (reveal) {
      this.panel.reveal(vscode.ViewColumn.Beside);
    }

    return this.panel;
  }

  isOpen(): boolean {
    return this.panel !== undefined;
  }

  disposePanel(): void {
    if (!this.panel) {
      return;
    }

    try {
      this.panel.dispose();
    } catch {
      // ignore
    }

    this.panel = undefined;
  }

  setHtml(html: string): void {
    const panel = this.getOrCreatePanel(false);
    panel.title = PanelHost.panelTitle;
    panel.webview.html = html;
  }

  postMessage(message: unknown): Thenable<boolean> {
    if (!this.panel) {
      return Promise.resolve(false);
    }

    return this.panel.webview.postMessage(message);
  }

  showPlaceholder(): void {
    this.setHtml(`<!DOCTYPE html>
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
    .toolbar {
      display: flex;
      align-items: center;
      gap: 8px;
      margin: 0 0 8px;
    }
    .pill {
      padding: 2px 6px;
      border: 1px solid var(--vscode-editorWidget-border);
      border-radius: 3px;
      background: var(--vscode-editorWidget-background);
      color: var(--vscode-descriptionForeground);
    }
    .grid, .subgrid {
      border-top: 1px solid var(--vscode-editorWidget-border);
      border-bottom: 1px solid var(--vscode-editorWidget-border);
    }
    .row {
      display: grid;
      grid-template-columns: 18px 60px 52px 1fr;
      gap: 8px;
      padding: 2px 6px;
      border-bottom: 1px solid var(--vscode-editorWidget-border);
    }
    .subrow {
      display: grid;
      grid-template-columns: 80px 1fr;
      gap: 8px;
      padding: 2px 6px;
      border-bottom: 1px solid var(--vscode-editorWidget-border);
    }
    .section {
      margin-top: 10px;
    }
    .section-title {
      margin: 0 0 4px;
      font-weight: 700;
    }
    .hint {
      color: var(--vscode-descriptionForeground);
      font-style: italic;
      margin: 6px 0 0;
    }
  </style>
</head>
<body>
  <div class="header">
    <div class="title">Okojo Bytecode</div>
    <div class="meta">No source location</div>
  </div>
  <div class="stopbar">
    <span class="stopbar-label">Current</span>
    <span class="stopbar-op">No highlighted instruction</span>
  </div>
  <div class="toolbar">
    <span>View</span>
    <span class="pill">Current stop + disassembly</span>
  </div>
  <div class="grid">
    <div class="row"><span></span><span>-</span><span></span><span>No bytecode</span></div>
  </div>
  <div class="hint">Start a Okojo debug session to inspect the current paused bytecode.</div>
  <div class="section">
    <div class="section-title">Metadata</div>
    <div class="subgrid">
      <div class="subrow"><span>state</span><span>waiting for debug session</span></div>
    </div>
  </div>
  <div class="section">
    <div class="section-title">Constants</div>
    <div class="subgrid">
      <div class="subrow"><span>n/a</span><span>No constants.</span></div>
    </div>
  </div>
</body>
</html>`);
  }
}

export const panelHost = new PanelHost();
