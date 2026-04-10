import * as path from 'node:path';
import * as vscode from 'vscode';
import { OkojoDebugSession } from './adapter';
import { panelHost } from './panelHost';

let activeDebugSession: OkojoDebugSession | undefined;

export function activate(context: vscode.ExtensionContext) {
  const type = 'okojo';
  const extensionVersion = String(context.extension.packageJSON?.version ?? 'unknown');
  const output = vscode.window.createOutputChannel('Okojo');

  output.appendLine(`[okojo] extension ${extensionVersion} activated`);
  context.subscriptions.push(output);

  context.subscriptions.push(
    vscode.debug.onDidTerminateDebugSession((session) => {
      if (session.type === type) {
        activeDebugSession = undefined;
        if (panelHost.isOpen()) {
          panelHost.showPlaceholder();
        }
      }
    })
  );

  context.subscriptions.push(
    vscode.debug.registerDebugConfigurationProvider(type, new OkojoDebugConfigurationProvider())
  );

  context.subscriptions.push(
    vscode.debug.registerDebugAdapterDescriptorFactory(type, new InlineFactory())
  );

  context.subscriptions.push(
    vscode.commands.registerCommand('okojo.showBytecode', async () => {
      const activeSession = vscode.debug.activeDebugSession;
      if (!activeSession || activeSession.type !== type || !activeDebugSession) {
        if (panelHost.isOpen()) {
          panelHost.disposePanel();
        } else {
          panelHost.showPlaceholder();
        }
        return;
      }

      activeDebugSession.toggleBytecodeView();
    })
  );

  context.subscriptions.push(
    vscode.commands.registerCommand('okojo.debugOptions', async () => {
      const activeSession = vscode.debug.activeDebugSession;
      if (!activeSession || activeSession.type !== type) {
        vscode.window.showInformationMessage('Start a Okojo debug session to adjust debugger options.');
        return;
      }

      if (!activeDebugSession) {
        vscode.window.showInformationMessage('Start a Okojo debug session to adjust debugger options.');
        return;
      }

      await activeDebugSession.showDebugOptionsMenu();
    })
  );
}

export function deactivate() {}

class OkojoDebugConfigurationProvider implements vscode.DebugConfigurationProvider {
  resolveDebugConfiguration(
    _folder: vscode.WorkspaceFolder | undefined,
    config: vscode.DebugConfiguration
  ): vscode.ProviderResult<vscode.DebugConfiguration> {
    const activeEditorPath = vscode.window.activeTextEditor?.document.uri.fsPath;
    const workspaceFolder = vscode.workspace.workspaceFolders?.[0]?.uri.fsPath ?? '';

    if (!config.type) config.type = 'okojo';
    if (!config.name) config.name = 'Okojo: Launch';
    if (!config.request) config.request = 'launch';
    if (!config.program) {
      config.program = activeEditorPath ?? `${workspaceFolder}/test.js`;
    }
    if (!config.cwd) {
      config.cwd = activeEditorPath ? vscode.workspace.getWorkspaceFolder(vscode.Uri.file(activeEditorPath))?.uri.fsPath ?? workspaceFolder : workspaceFolder;
    }
    if (!config.debugServerProject) {
      config.debugServerProject = `${workspaceFolder}/src/Okojo.DebugServer/Okojo.DebugServer.csproj`;
    }
    if (config.checkInterval === undefined) {
      config.checkInterval = vscode.workspace.getConfiguration('okojo.debugger').get<number>('checkInterval', 1024);
    }
    if (config.stepGranularity === undefined) {
      config.stepGranularity = vscode.workspace.getConfiguration('okojo.debugger').get<string>('stepGranularity', 'line');
    }
    if (config.traceBreakpoints === undefined) {
      config.traceBreakpoints = vscode.workspace.getConfiguration('okojo.debugger').get<boolean>('traceBreakpoints', false);
    }
    if (config.traceAdapter === undefined) {
      config.traceAdapter = vscode.workspace.getConfiguration('okojo.debugger').get<boolean>('traceAdapter', false);
    }
    if (config.moduleEntry === undefined) {
      config.moduleEntry = path.extname(String(config.program ?? '')).toLowerCase() === '.mjs';
    }
    if (config.stopOnEntry === undefined) config.stopOnEntry = false;
    return config;
  }
}

class InlineFactory implements vscode.DebugAdapterDescriptorFactory {
  createDebugAdapterDescriptor(
    _session: vscode.DebugSession
  ): vscode.ProviderResult<vscode.DebugAdapterDescriptor> {
    activeDebugSession = new OkojoDebugSession();
    return new vscode.DebugAdapterInlineImplementation(activeDebugSession);
  }
}
