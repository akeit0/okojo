import * as path from 'node:path';
import * as vscode from 'vscode';
import { OkojoDebugSession } from './adapter';
import { buildBytecodeViewModel, renderBytecodeShellHtml } from './bytecodeView';
import { HostBytecodeMessage } from './debugTypes';
import { panelHost } from './panelHost';

const sessions = new Map<string, OkojoDebugSession>();
let bytecodeOwner: string | undefined;

export function activate(context: vscode.ExtensionContext): void {
  const output = vscode.window.createOutputChannel('Okojo');
  context.subscriptions.push(output);
  output.appendLine(`Okojo debugger ${context.extension.packageJSON.version}`);
  context.subscriptions.push(vscode.debug.registerDebugConfigurationProvider('okojo', new ConfigurationProvider()));
  context.subscriptions.push(vscode.debug.registerDebugAdapterDescriptorFactory('okojo', {
    createDebugAdapterDescriptor(session: vscode.DebugSession): vscode.DebugAdapterDescriptor {
      if (!vscode.workspace.isTrusted) throw new Error('Trust the workspace before launching executable code.');
      const adapter = new OkojoDebugSession();
      sessions.set(session.id, adapter);
      adapter.onDidSendMessage(message => {
        if (message.type !== 'event' || vscode.debug.activeDebugSession?.id !== session.id) return;
        if (message.event === 'stopped') {
          if (panelHost.isOpen() || vscode.workspace.getConfiguration('okojo.debugger').get('openOnStop', false)) {
            if (!panelHost.isOpen()) {
              panelHost.showPlaceholder();
              bytecodeOwner = undefined;
            }
            adapter.requestBytecodeDump();
          }
        } else if (message.event === 'okojo/bytecode') {
          if (!panelHost.isOpen()) return;
          if (bytecodeOwner !== session.id) {
            panelHost.setHtml(renderBytecodeShellHtml());
            bytecodeOwner = session.id;
          }
          void panelHost.postMessage({ type: 'bytecode-update', payload: buildBytecodeViewModel(message.body as HostBytecodeMessage) });
        }
      });
      return new vscode.DebugAdapterInlineImplementation(adapter);
    },
  }));
  context.subscriptions.push(vscode.debug.onDidTerminateDebugSession(session => {
    sessions.get(session.id)?.dispose();
    sessions.delete(session.id);
    if (bytecodeOwner === session.id) {
      bytecodeOwner = undefined;
      if (panelHost.isOpen()) panelHost.showPlaceholder();
    }
  }));
  context.subscriptions.push(vscode.commands.registerCommand('okojo.showBytecode', () => {
    if (panelHost.isOpen()) {
      panelHost.disposePanel();
      bytecodeOwner = undefined;
      return;
    }
    panelHost.showPlaceholder();
    bytecodeOwner = undefined;
    const active = vscode.debug.activeDebugSession;
    if (active) sessions.get(active.id)?.requestBytecodeDump();
  }));
  context.subscriptions.push(vscode.commands.registerCommand('okojo.debugOptions', async () => {
    const active = vscode.debug.activeDebugSession;
    const adapter = active ? sessions.get(active.id) : undefined;
    if (!adapter) {
      void vscode.window.showInformationMessage('Start an Okojo debug session to adjust debugger options.');
      return;
    }
    const choice = await vscode.window.showQuickPick([
      { label: `${adapter.getStepGranularity() === 'line' ? '$(check) ' : ''}Line`, value: 'line' as const },
      { label: `${adapter.getStepGranularity() === 'instruction' ? '$(check) ' : ''}Instruction`, value: 'instruction' as const },
    ], { placeHolder: 'Okojo stepping granularity' });
    if (choice) adapter.setStepGranularity(choice.value);
  }));
  context.subscriptions.push({ dispose: deactivate });
}

export function deactivate(): void {
  for (const adapter of sessions.values()) adapter.dispose();
  sessions.clear();
  panelHost.disposePanel();
  bytecodeOwner = undefined;
}

class ConfigurationProvider implements vscode.DebugConfigurationProvider {
  resolveDebugConfiguration(
    folder: vscode.WorkspaceFolder | undefined,
    config: vscode.DebugConfiguration,
  ): vscode.ProviderResult<vscode.DebugConfiguration> {
    if (!vscode.workspace.isTrusted) {
      void vscode.window.showErrorMessage('Okojo debugging is disabled in untrusted workspaces.');
      return null;
    }
    const editor = vscode.window.activeTextEditor?.document;
    const editorPath = editor?.uri.scheme === 'file' ? editor.uri.fsPath : undefined;
    const workspace = folder?.uri.fsPath ?? (editorPath ? vscode.workspace.getWorkspaceFolder(vscode.Uri.file(editorPath))?.uri.fsPath : undefined);
    const settings = vscode.workspace.getConfiguration('okojo.debugger', folder?.uri);
    config.type ??= 'okojo';
    config.name ??= 'Okojo: Launch';
    config.request ??= 'launch';
    if (config.request !== 'launch') {
      void vscode.window.showErrorMessage('Okojo currently supports launch sessions, not attach.');
      return null;
    }
    if (!config.program) {
      if (!editorPath || !/\.(?:[cm]?js)$/i.test(editorPath)) {
        void vscode.window.showErrorMessage('Open a JavaScript file, or set program to the generated JavaScript entry. Use enableSourceMaps to debug TypeScript sources.');
        return null;
      }
      config.program = editorPath;
    }
    config.cwd ??= workspace ?? (editorPath ? path.dirname(editorPath) : undefined);
    for (const [key, fallback] of Object.entries({ checkInterval: 1024, stepGranularity: 'line', traceBreakpoints: false, traceAdapter: false, dotnetPath: 'dotnet' })) {
      config[key] ??= settings.get(key, fallback);
    }
    if (!config.debugServerPath && !config.debugServerProject) {
      const server = settings.get<string>('debugServerPath');
      if (server) config.debugServerPath = server;
      // Otherwise the adapter walks repository ancestors; do not inject a
      // nonexistent project under a sample or the first multi-root workspace.
    }
    config.stopOnEntry ??= false;
    return config;
  }
}
