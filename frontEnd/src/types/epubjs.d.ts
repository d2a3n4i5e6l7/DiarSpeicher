declare module "epubjs" {
	export interface NavItem {
		id: string;
		href: string;
		label: string;
		subitems?: NavItem[];
		parent?: string;
	}

	export interface Navigation {
		toc: NavItem[];
		get(target: string): NavItem | undefined;
	}

	export interface RenditionOptions {
		width?: number | string;
		height?: number | string;
		flow?: "paginated" | "scrolled" | "scrolled-doc";
		spread?: "none" | "always" | "auto";
		minSpreadWidth?: number;
		manager?: string;
	}

	export interface Location {
		start: {
			cfi: string;
			href: string;
			index: number;
			percentage?: number;
			displayed?: {
				page: number;
				total: number;
			};
		};
		end: {
			cfi: string;
			href: string;
			index: number;
			percentage?: number;
		};
		atStart?: boolean;
		atEnd?: boolean;
	}

	export interface Rendition {
		display(target?: string | number): Promise<void>;
		next(): Promise<void>;
		prev(): Promise<void>;
		destroy(): void;
		on(event: string, callback: (...args: unknown[]) => void): void;
		off(event: string, callback: (...args: unknown[]) => void): void;
		themes: {
			register(name: string, styles: Record<string, unknown> | string): void;
			select(name: string): void;
			fontSize(size: string): void;
			font(family: string): void;
			override(name: string, value: string, priority?: boolean): void;
		};
		currentLocation(): Location;
	}

	export interface Book {
		loaded: {
			navigation: Promise<Navigation>;
			metadata: Promise<unknown>;
			spine: Promise<unknown>;
		};
		navigation: Navigation;
		renderTo(element: HTMLElement | string, options?: RenditionOptions): Rendition;
		destroy(): void;
		locations: {
			generate(chars?: number): Promise<string[]>;
			cfiFromPercentage(percentage: number): string;
			percentageFromCfi(cfi: string): number;
		};
	}

	function ePub(urlOrData: string | ArrayBuffer, options?: Record<string, unknown>): Book;

	export default ePub;
}
