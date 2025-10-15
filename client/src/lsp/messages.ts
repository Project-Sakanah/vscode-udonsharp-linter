import { DiagnosticSeverity } from 'vscode-languageclient/node';

export interface RuleDescriptor {
	readonly id: string;
	readonly title: string;
	readonly category: string;
	readonly defaultSeverity: DiagnosticSeverity;
	readonly description: string;
	readonly helpLink: string | null;
	readonly hasCodeFix: boolean;
	readonly profileSeverity?: Record<string, DiagnosticSeverity>;
}

export interface RuleDocumentation {
	readonly id: string;
	readonly locale: string;
	readonly title: string;
	readonly markdown: string;
}

export interface ServerStatusPayload {
	readonly profile: string;
	readonly disabledRuleCount: number;
	readonly totalRuleCount: number;
	readonly serverVersion: string;
}

export namespace Requests {
	export const listRules = 'udonsharp/rules/list';
	export const loadRuleDocumentation = 'udonsharp/rules/documentation';
	export const serverStatus = 'udonsharp/server/status';
	export const serverStatusCompat = 'udonsharp/status';
}
