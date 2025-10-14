import * as vscode from 'vscode';
import { RuleRepository } from '../lsp/ruleRepository';
import { RuleDescriptor } from '../lsp/messages';

interface RuleQuickPickItem extends vscode.QuickPickItem {
	rule: RuleDescriptor;
}

export async function pickRule(ruleRepository: RuleRepository): Promise<RuleDescriptor | undefined> {
	const rules = ruleRepository.rules;
	if (rules.length === 0) {
		await vscode.window.showWarningMessage(vscode.l10n.t('Rule data has not been loaded yet. Please try again in a moment.'));
		return undefined;
	}

	const quickPick = vscode.window.createQuickPick<RuleQuickPickItem>();
	quickPick.matchOnDescription = true;
	quickPick.matchOnDetail = true;
	quickPick.placeholder = vscode.l10n.t('Type a rule ID or title to filter.');
	quickPick.items = rules.map(rule => ({
		label: `${rule.id} — ${rule.title}`,
		description: rule.category,
		detail: rule.description,
		rule,
	}));

	const disposables: vscode.Disposable[] = [];
	try {
		const selection = await new Promise<RuleQuickPickItem | undefined>(resolve => {
			disposables.push(
				quickPick.onDidAccept(() => resolve(quickPick.selectedItems[0])),
				quickPick.onDidHide(() => resolve(undefined))
			);
			quickPick.show();
		});

		return selection?.rule;
	} finally {
		disposables.forEach(disposable => disposable.dispose());
		quickPick.dispose();
	}
}
