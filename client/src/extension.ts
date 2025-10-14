import * as vscode from 'vscode';
import { SettingsManager } from './config/settings';
import { RuleRepository } from './lsp/ruleRepository';
import { StatusBarController } from './ui/statusBar';
import { LanguageClientController } from './lsp/clientController';
import { RuleDocumentationPanel } from './ui/ruleDocsPanel';
import { pickRule } from './ui/ruleSearch';
import { showProfilePicker } from './ui/profileQuickPick';

let clientController: LanguageClientController | undefined;

export async function activate(context: vscode.ExtensionContext): Promise<void> {
	const settingsManager = new SettingsManager();
	const ruleRepository = new RuleRepository();
	const statusBar = new StatusBarController(settingsManager);
	const documentationPanel = new RuleDocumentationPanel(ruleRepository);

	context.subscriptions.push(settingsManager, ruleRepository, statusBar, documentationPanel);

	clientController = new LanguageClientController(
		context,
		settingsManager,
		ruleRepository,
		statusBar,
	);
	context.subscriptions.push(clientController);

	try {
		await clientController.start();
	} catch (error) {
		const message = vscode.l10n.t('Failed to start the UdonSharp Linter server. {0}', String(error));
		console.error(message, error);
		await vscode.window.showErrorMessage(message);
	}

	context.subscriptions.push(
		vscode.commands.registerCommand('udonsharpLinter.switchProfile', async () => {
			await showProfilePicker(settingsManager);
		}),
		vscode.commands.registerCommand('udonsharpLinter.searchRules', async () => {
			const selection = await pickRule(ruleRepository);
			if (selection) {
				await documentationPanel.show(selection.id);
			}
		}),
		vscode.commands.registerCommand('udonsharpLinter.openRuleDocs', async (ruleId?: string) => {
			if (!ruleId) {
				const picked = await pickRule(ruleRepository);
				if (!picked) {
					return;
				}
				ruleId = picked.id;
			}
			await documentationPanel.show(ruleId);
		}),
	);

	vscode.window.showInformationMessage(vscode.l10n.t('UdonSharp Linter is active.'));
}

export async function deactivate(): Promise<void> {
	if (clientController) {
		await clientController.stop();
		clientController = undefined;
	}
}
