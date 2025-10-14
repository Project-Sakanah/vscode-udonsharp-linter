import * as path from 'path';
import * as vscode from 'vscode';

export type RuleSeveritySetting = 'error' | 'warn' | 'info' | 'off';

export interface LinterSettings {
	profile: string;
	ruleOverrides: Record<string, RuleSeveritySetting>;
	unityApiSurface: 'bundled-stubs' | 'custom-stubs' | 'none';
	customStubPath: string | undefined;
	allowRefOut: boolean;
	codeActionsEnabled: boolean;
	telemetry: 'off' | 'minimal';
	policyPackPaths: string[];
}

export class SettingsManager implements vscode.Disposable {
	private readonly emitter = new vscode.EventEmitter<LinterSettings>();
	private readonly disposables: vscode.Disposable[] = [];
	private currentSettings: LinterSettings;

	constructor() {
		this.currentSettings = readSettings();
		this.disposables.push(
			vscode.workspace.onDidChangeConfiguration(event => {
				if (event.affectsConfiguration('udonsharpLinter')) {
					this.refresh();
				}
			})
		);
	}

	public get settings(): LinterSettings {
		return this.currentSettings;
	}

	public readonly onDidChange = this.emitter.event;

	public dispose(): void {
		this.emitter.dispose();
		for (const disposable of this.disposables) {
			disposable.dispose();
		}
	}

	private refresh(): void {
		const next = readSettings();
		if (!settingsAreEqual(this.currentSettings, next)) {
			this.currentSettings = next;
			this.emitter.fire(next);
		}
	}
}

function readSettings(): LinterSettings {
	const configuration = vscode.workspace.getConfiguration('udonsharpLinter');

	const profile = configuration.get<string>('profile', 'latest');
	const ruleOverrides = toRuleOverrides(configuration.get<Record<string, RuleSeveritySetting>>('rules', {}));
	const unityApiSurface = configuration.get<'bundled-stubs' | 'custom-stubs' | 'none'>('unityApiSurface', 'bundled-stubs');
	const allowRefOut = configuration.get<boolean>('allow.refOut', false);
	const codeActionsEnabled = configuration.get<boolean>('codeActions.enable', true);
	const telemetry = configuration.get<'off' | 'minimal'>('telemetry', 'minimal');
	const rawPolicyPaths = configuration.get<string[]>('policyPackPaths', []);
	const customStubPathRaw = configuration.get<string>('customStubPath', '')?.trim() ?? '';
	const customStubPath = unityApiSurface === 'custom-stubs' && customStubPathRaw.length > 0
		? resolveToAbsolute(customStubPathRaw)
		: undefined;

	return {
		profile,
		ruleOverrides,
		unityApiSurface,
		customStubPath,
		allowRefOut,
		codeActionsEnabled,
		telemetry,
		policyPackPaths: rawPolicyPaths
			.map(pathCandidate => resolveToAbsolute(pathCandidate))
			.filter(Boolean) as string[],
	};
}

function toRuleOverrides(overrides: Record<string, RuleSeveritySetting>): Record<string, RuleSeveritySetting> {
	const sanitized: Record<string, RuleSeveritySetting> = {};
	for (const [ruleId, severity] of Object.entries(overrides)) {
		if (!ruleId || typeof severity !== 'string') {
			continue;
		}
		if (severity === 'error' || severity === 'warn' || severity === 'info' || severity === 'off') {
			sanitized[ruleId.toUpperCase()] = severity;
		}
	}
	return sanitized;
}

function settingsAreEqual(left: LinterSettings, right: LinterSettings): boolean {
	return left.profile === right.profile
		&& left.unityApiSurface === right.unityApiSurface
		&& left.customStubPath === right.customStubPath
		&& left.allowRefOut === right.allowRefOut
		&& left.codeActionsEnabled === right.codeActionsEnabled
		&& left.telemetry === right.telemetry
		&& arraysEqual(left.policyPackPaths, right.policyPackPaths)
		&& recordEqual(left.ruleOverrides, right.ruleOverrides);
}

function arraysEqual(left: readonly string[], right: readonly string[]): boolean {
	if (left.length !== right.length) {
		return false;
	}
	for (let index = 0; index < left.length; index += 1) {
		if (left[index] !== right[index]) {
			return false;
		}
	}
	return true;
}

function recordEqual(left: Record<string, string>, right: Record<string, string>): boolean {
	const leftKeys = Object.keys(left);
	const rightKeys = Object.keys(right);
	if (leftKeys.length !== rightKeys.length) {
		return false;
	}
	for (const key of leftKeys) {
		if (left[key] !== right[key]) {
			return false;
		}
	}
	return true;
}

function resolveToAbsolute(candidate: string): string | undefined {
	if (!candidate) {
		return undefined;
	}

	let normalised = candidate;
	if (candidate.startsWith('~')) {
		const home = path.resolve(process.env.HOME ?? process.env.USERPROFILE ?? '');
		normalised = path.join(home, candidate.substring(1));
	}

	if (path.isAbsolute(normalised)) {
		return path.normalize(normalised);
	}

	const workspaceFolder = vscode.workspace.workspaceFolders?.[0]?.uri.fsPath;
	if (!workspaceFolder) {
		return undefined;
	}
	return path.normalize(path.join(workspaceFolder, normalised));
}
