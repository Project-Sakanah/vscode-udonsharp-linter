import * as vscode from 'vscode';
import { RuleRepository } from '../lsp/ruleRepository';
import { RuleDocumentation } from '../lsp/messages';

export class RuleDocumentationPanel implements vscode.Disposable {
	private panel: vscode.WebviewPanel | undefined;

	constructor(private readonly ruleRepository: RuleRepository) {}

	public async show(ruleId: string): Promise<void> {
		const locale = vscode.env.language ?? 'en-US';
		const documentation = await this.ruleRepository.getDocumentation(ruleId, locale)
			|| await this.ruleRepository.getDocumentation(ruleId, 'en-US');

		if (!documentation) {
			await vscode.window.showWarningMessage(vscode.l10n.t('Documentation for rule {0} is unavailable.', ruleId));
			return;
		}

		if (!this.panel) {
			this.panel = vscode.window.createWebviewPanel(
				'udonsharpLinter.ruleDocumentation',
				`UdonSharp Rule – ${documentation.id}`,
				vscode.ViewColumn.Beside,
				{
					enableScripts: true,
					localResourceRoots: [],
					retainContextWhenHidden: true,
				}
			);
			this.panel.onDidDispose(() => {
				this.panel = undefined;
			});
		} else {
			this.panel.title = `UdonSharp Rule – ${documentation.id}`;
		}

		const html = await renderMarkdown(documentation);
		this.panel.webview.html = html;
		this.panel.reveal(undefined, true);
	}

	public dispose(): void {
		this.panel?.dispose();
		this.panel = undefined;
	}
}

async function renderMarkdown(documentation: RuleDocumentation): Promise<string> {
	const commandResult = await vscode.commands.executeCommand<string>('markdown.api.render', documentation.markdown);
	const resolvedHtml = typeof commandResult === 'string' ? commandResult : `<pre>${escapeHtml(documentation.markdown)}</pre>`;

	const csp = [
		"default-src 'none'",
		"img-src data:",
		"style-src 'unsafe-inline'",
	].join('; ');

	return `<!DOCTYPE html>
<html lang="${documentation.locale}">
<head>
	<meta charset="UTF-8">
	<meta http-equiv="Content-Security-Policy" content="${csp}">
	<meta name="viewport" content="width=device-width, initial-scale=1.0">
	<title>${escapeHtml(documentation.title)}</title>
	<style>
		body {
			font-family: var(--vscode-font-family);
			color: var(--vscode-editor-foreground);
			background-color: var(--vscode-editor-background);
			padding: 16px;
			line-height: 1.5;
		}
		a {
			color: var(--vscode-textLink-foreground);
		}
		code {
			background-color: var(--vscode-editor-lineHighlightBackground, rgba(125,125,125,0.2));
			padding: 0.1rem 0.2rem;
			border-radius: 4px;
		}
		pre {
			background-color: var(--vscode-editor-lineHighlightBackground, rgba(125,125,125,0.2));
			padding: 0.75rem;
			border-radius: 6px;
			overflow-x: auto;
		}
	</style>
</head>
<body>
${resolvedHtml}
</body>
</html>`;
}

function escapeHtml(value: string): string {
	return value
		.replace(/&/g, '&amp;')
		.replace(/</g, '&lt;')
		.replace(/>/g, '&gt;')
		.replace(/"/g, '&quot;')
		.replace(/'/g, '&#39;');
}
