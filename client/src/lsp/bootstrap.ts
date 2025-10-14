import * as fs from 'fs';
import * as path from 'path';
import * as vscode from 'vscode';
import { CloseAction, ErrorAction, ErrorHandler, LanguageClient, LanguageClientOptions, ServerOptions } from 'vscode-languageclient/node';
import type { CancellationToken, MessageSignature } from 'vscode-jsonrpc';
import { SettingsManager } from '../config/settings';
import { RuleRepository } from './ruleRepository';

interface ServerCommand {
	command: string;
	args: string[];
	options: {
		env: NodeJS.ProcessEnv;
	};
}

const SERVER_ENV_PREFIX = 'udonsharpLinter';

export async function createLanguageClient(
	context: vscode.ExtensionContext,
	settingsManager: SettingsManager,
	ruleRepository: RuleRepository,
	errorHandler?: ErrorHandler
): Promise<LanguageClient> {
	const serverCommand = await resolveServerCommand(context);

	const serverOptions: ServerOptions = {
		command: serverCommand.command,
		args: serverCommand.args,
		options: serverCommand.options,
	};

	const clientOptions: LanguageClientOptions = {
		documentSelector: [
			{ language: 'csharp', scheme: 'file' },
			{ language: 'csharp', scheme: 'untitled' },
		],
		initializationOptions: settingsManager.settings,
		synchronize: {
			configurationSection: 'udonsharpLinter',
			fileEvents: vscode.workspace.createFileSystemWatcher('**/*.cs'),
		},
		errorHandler: errorHandler ?? createDefaultErrorHandler(),
	};

	const client = new LanguageClient(
		'UdonSharpLinter',
		'UdonSharp Linter',
		serverOptions,
		clientOptions
	);

	const originalHandleFailedRequest = client.handleFailedRequest.bind(client);
	client.handleFailedRequest = function<R>(
		type: MessageSignature,
		token: CancellationToken | undefined,
		error: unknown,
		defaultValue: R,
		showNotification?: boolean
	): R {
		if (error instanceof vscode.CancellationError) {
			return defaultValue;
		}

		console.error(`UdonSharp Linter request failed (${type.method}):`, error);

		return originalHandleFailedRequest(
			type,
			token,
			error,
			defaultValue,
			showNotification
		);
	};

	client.registerProposedFeatures();
	ruleRepository.attachClient(client);

	return client;
}

function createDefaultErrorHandler(): ErrorHandler {
	return {
		error() {
			return { action: ErrorAction.Continue };
		},
		closed() {
			return { action: CloseAction.DoNotRestart };
		},
	};
}

async function resolveServerCommand(context: vscode.ExtensionContext): Promise<ServerCommand> {
	const override = process.env.UDONSHARP_LINTER_SERVER_PATH;
	if (override && override.length > 0) {
		await ensureExecutable(override);
		return {
			command: override,
			args: ['--lsp'],
			options: {
				env: createServerEnvironment(),
			},
		};
	}

	const executableRelative = path.join('resources', 'server', detectPlatform(), detectExecutableName());
	const executablePath = vscode.Uri.joinPath(context.extensionUri, executableRelative).fsPath;
	await ensureExecutable(executablePath);
	return {
		command: executablePath,
		args: ['--lsp'],
		options: {
			env: createServerEnvironment(),
		},
	};
}

function detectPlatform(): string {
	const arch = process.arch;
	switch (process.platform) {
	case 'win32':
		if (arch === 'arm64') {
			return 'win-arm64';
		}
		return 'win-x64';
	case 'darwin':
		if (arch === 'arm64') {
			return 'osx-arm64';
		}
		return 'osx-x64';
	case 'linux':
	default:
		if (arch === 'arm64') {
			return 'linux-arm64';
		}
		return 'linux-x64';
	}
}

function detectExecutableName(): string {
	return process.platform === 'win32' ? 'udonsharp-lsp.exe' : 'udonsharp-lsp';
}

async function ensureExecutable(executablePath: string): Promise<void> {
	const mode = process.platform === 'win32' ? fs.constants.F_OK : fs.constants.X_OK;
	await fs.promises.access(executablePath, mode);
	if (process.platform !== 'win32') {
		await fs.promises.chmod(executablePath, 0o755);
	}
}

function createServerEnvironment(): NodeJS.ProcessEnv {
	const env: NodeJS.ProcessEnv = { ...process.env };
	env[`${SERVER_ENV_PREFIX.toUpperCase()}_TELEMETRY`] = process.env.UDONSHARP_LINTER_TELEMETRY ?? '1';
	return env;
}
