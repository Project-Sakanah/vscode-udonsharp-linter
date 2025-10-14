import * as vscode from 'vscode';
import { LanguageClient } from 'vscode-languageclient/node';
import { Requests, RuleDescriptor, RuleDocumentation } from './messages';

export class RuleRepository implements vscode.Disposable {
	private client: LanguageClient | undefined;
	private readonly ruleEmitter = new vscode.EventEmitter<RuleDescriptor[]>();
	private readonly documentationCache = new Map<string, RuleDocumentation>();
	private cachedRules: RuleDescriptor[] = [];

	public readonly onDidChangeRules = this.ruleEmitter.event;

	public attachClient(client: LanguageClient): void {
		this.client = client;
		// No push notifications yet; listeners request status explicitly.
	}

	public async initialise(): Promise<void> {
		await this.refresh();
	}

	public async refresh(): Promise<void> {
		if (!this.client) {
			this.cachedRules = [];
			this.ruleEmitter.fire(this.cachedRules);
			return;
		}

		try {
			const descriptors = await this.client.sendRequest<RuleDescriptor[]>(Requests.listRules);
			if (Array.isArray(descriptors)) {
				this.cachedRules = descriptors.sort((left, right) => left.id.localeCompare(right.id, 'en'));
				this.ruleEmitter.fire(this.cachedRules);
			}
		} catch (error) {
			this.cachedRules = [];
			this.ruleEmitter.fire(this.cachedRules);
			await vscode.window.showErrorMessage(vscode.l10n.t('UdonSharp Linter could not load rule metadata. {0}', String(error)));
		}
	}

	public get rules(): readonly RuleDescriptor[] {
		return this.cachedRules;
	}

	public async getDocumentation(ruleId: string, locale: string): Promise<RuleDocumentation | undefined> {
		const cacheKey = `${ruleId}:${locale}`;
		if (this.documentationCache.has(cacheKey)) {
			return this.documentationCache.get(cacheKey);
		}
		if (!this.client) {
			return undefined;
		}
		try {
			const doc = await this.client.sendRequest<RuleDocumentation>(Requests.loadRuleDocumentation, { ruleId, locale });
			if (doc) {
				this.documentationCache.set(cacheKey, doc);
			}
			return doc;
		} catch (error) {
			await vscode.window.showErrorMessage(vscode.l10n.t('UdonSharp Linter could not load documentation. {0}', String(error)));
			return undefined;
		}
	}

	public clearCache(): void {
		this.documentationCache.clear();
	}

	public dispose(): void {
		this.ruleEmitter.dispose();
		this.documentationCache.clear();
	}
}
