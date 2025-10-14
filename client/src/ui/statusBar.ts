import * as vscode from 'vscode';
import { LinterSettings, SettingsManager } from '../config/settings';
import { ServerStatusPayload } from '../lsp/messages';

const STATUS_COMMAND = 'udonsharpLinter.switchProfile';

export class StatusBarController implements vscode.Disposable {
	private readonly statusItem: vscode.StatusBarItem;
	private readonly disposables: vscode.Disposable[] = [];
	private lastStatus: ServerStatusPayload | undefined;

	constructor(private readonly settings: SettingsManager) {
		this.statusItem = vscode.window.createStatusBarItem(vscode.StatusBarAlignment.Left, 10);
		this.statusItem.command = STATUS_COMMAND;
		this.statusItem.tooltip = vscode.l10n.t('Switch the active UdonSharp Linter profile or review rule status.');

		this.disposables.push(
			settings.onDidChange(updated => this.updateFromSettings(updated))
		);

		this.statusItem.show();
		this.updateFromSettings(settings.settings);
	}

	public updateServerStatus(status: ServerStatusPayload): void {
		this.lastStatus = status;
		this.updateStatusBar(status.profile, status.disabledRuleCount, status.totalRuleCount);
	}

	public dispose(): void {
		this.statusItem.dispose();
		for (const disposable of this.disposables) {
			disposable.dispose();
		}
	}

	private updateFromSettings(currentSettings: LinterSettings): void {
		const disabledCount = Object.values(currentSettings.ruleOverrides)
			.filter(severity => severity === 'off').length;
		const totalCount = this.lastStatus?.totalRuleCount ?? undefined;
		this.updateStatusBar(currentSettings.profile, disabledCount, totalCount);
	}

	private updateStatusBar(profile: string, disabledCount: number, totalCount?: number): void {
		const totalSuffix = typeof totalCount === 'number' ? `/${totalCount}` : '';
		const text = totalSuffix
			? `$(shield) UdonSharp ${profile} · ${disabledCount}${totalSuffix} off`
			: `$(shield) UdonSharp ${profile} · ${disabledCount} off`;
		this.statusItem.text = text;
	}
}
