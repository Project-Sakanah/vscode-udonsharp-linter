import * as vscode from 'vscode';
import { CloseAction, ErrorAction, ErrorHandler, LanguageClient } from 'vscode-languageclient/node';
import { SettingsManager } from '../config/settings';
import { createLanguageClient } from './bootstrap';
import { RuleRepository } from './ruleRepository';
import { Requests, ServerStatusPayload } from './messages';
import { StatusBarController } from '../ui/statusBar';

const MAX_RESTART_ATTEMPTS = 3;
const STATUS_REFRESH_INTERVAL = 60_000;

export class LanguageClientController implements vscode.Disposable {
	private client: LanguageClient | undefined;
	private disposed = false;
	private restartAttempts = 0;
	private restartPending = false;
	private statusInterval: NodeJS.Timeout | undefined;
	private readonly settingsListener: vscode.Disposable;

	constructor(
		private readonly context: vscode.ExtensionContext,
		private readonly settings: SettingsManager,
		private readonly ruleRepository: RuleRepository,
		private readonly statusBar: StatusBarController,
	) {
		this.settingsListener = this.settings.onDidChange(() => {
			if (!this.client) {
				return;
			}
			this.client.sendNotification('workspace/didChangeConfiguration', {
				settings: {
					udonsharpLinter: this.settings.settings,
				},
			});
		});
	}

	public async start(): Promise<void> {
		if (this.disposed) {
			return;
		}
		if (this.client) {
			return;
		}

		const errorHandler = this.createErrorHandler();
		const client = await createLanguageClient(
			this.context,
			this.settings,
			this.ruleRepository,
			errorHandler
		);
		this.client = client;

		this.statusInterval = setInterval(() => {
			void this.requestServerStatus();
		}, STATUS_REFRESH_INTERVAL);

		try {
			await client.start();
			this.restartAttempts = 0;
			await this.ruleRepository.initialise();
			await this.requestServerStatus();
		} catch (error) {
			this.clearStatusInterval();
			this.client = undefined;
			throw error;
		}
	}

	public get languageClient(): LanguageClient | undefined {
		return this.client;
	}

	public async stop(): Promise<void> {
		this.clearStatusInterval();
		if (this.client) {
			const current = this.client;
			this.client = undefined;
			try {
				await current.stop(2_000);
			} catch (error) {
				console.error('UdonSharp Linter client stop failure:', error);
			}
		}
	}

	public dispose(): void {
		this.disposed = true;
		this.clearStatusInterval();
		this.settingsListener.dispose();
		void this.stop();
	}

	private createErrorHandler(): ErrorHandler {
		return {
			error: (error: unknown, message: unknown, count: number | undefined) => {
				console.error('UdonSharp Linter server error:', error, message);
				if ((count ?? 0) >= MAX_RESTART_ATTEMPTS) {
					vscode.window.showErrorMessage(
						vscode.l10n.t('The UdonSharp Linter server failed repeatedly. Check the output window for details.')
					).then(undefined, console.error);
					return { action: ErrorAction.Shutdown };
				}
				return { action: ErrorAction.Continue };
			},
			closed: () => {
				console.warn('UdonSharp Linter server connection closed.');
				this.scheduleRestart();
				return { action: CloseAction.DoNotRestart };
			},
		};
	}

	private scheduleRestart(): void {
		if (this.disposed) {
			return;
		}

		if (this.restartPending) {
			return;
		}

		if (this.restartAttempts >= MAX_RESTART_ATTEMPTS) {
			void vscode.window.showErrorMessage(
				vscode.l10n.t('The UdonSharp Linter server cannot be restarted. Reload the extension or restart VSCode.')
			);
			return;
		}

		this.restartPending = true;
		this.restartAttempts += 1;
		const delay = Math.pow(2, this.restartAttempts - 1) * 1_000;
		setTimeout(() => {
			void (async () => {
				this.restartPending = false;

				if (this.client) {
					await this.stop();
				}

				this.client = undefined;

				if (this.disposed) {
					return;
				}

				try {
					await this.start();
				} catch (error) {
					console.error('UdonSharp Linter server restart failed:', error);
					this.scheduleRestart();
				}
			})();
		}, delay);
	}

	private clearStatusInterval(): void {
		if (this.statusInterval) {
			clearInterval(this.statusInterval);
			this.statusInterval = undefined;
		}
	}

	private async requestServerStatus(): Promise<void> {
		if (!this.client) {
			return;
		}
		try {
			const payload = await this.client.sendRequest<ServerStatusPayload>(Requests.serverStatus);
			if (payload) {
				this.statusBar.updateServerStatus(payload);
			}
		} catch (error) {
			// Non-critical; log to output only.
			console.debug('Failed to request UdonSharp Linter server status', error);
		}
	}
}
