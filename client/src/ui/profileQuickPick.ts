import * as vscode from 'vscode';
import { SettingsManager } from '../config/settings';

const PROFILE_OPTIONS = [
	{ label: 'latest', description: vscode.l10n.t('Latest UdonSharp/VRChat SDK profile.') },
	{ label: 'legacy_0.x', description: vscode.l10n.t('Legacy compatibility profile for UdonSharp 0.x.') },
	{ label: 'strict_experimental', description: vscode.l10n.t('Strict/experimental profile that promotes all warnings to errors.') },
];

export async function showProfilePicker(settings: SettingsManager): Promise<void> {
	const quickPick = vscode.window.createQuickPick();
	quickPick.matchOnDescription = true;
	quickPick.title = vscode.l10n.t('Select UdonSharp Linter Profile');
	quickPick.placeholder = vscode.l10n.t('Choose the constraint profile to apply.');
	quickPick.items = PROFILE_OPTIONS.map(item => ({
		label: item.label,
		description: item.description,
		picked: item.label === settings.settings.profile,
	}));

	try {
		const selection = await new Promise<vscode.QuickPickItem | undefined>(resolve => {
			const acceptSubscription = quickPick.onDidAccept(() => resolve(quickPick.selectedItems[0]));
			const hideSubscription = quickPick.onDidHide(() => resolve(undefined));
			quickPick.onDidHide(() => {
				acceptSubscription.dispose();
				hideSubscription.dispose();
			});
			quickPick.show();
		});

		if (!selection) {
			return;
		}

		await vscode.workspace.getConfiguration('udonsharpLinter')
			.update('profile', selection.label, vscode.ConfigurationTarget.Workspace);
	} finally {
		quickPick.dispose();
	}
}
