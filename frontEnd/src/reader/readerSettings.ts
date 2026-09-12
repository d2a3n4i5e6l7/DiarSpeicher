export type ReadingMode = "Paged" | "Double" | "ContinuousVertical";
export type ReadingDirection = "LeftToRight" | "RightToLeft";
export type ImageFit = "width" | "height" | "original";

export interface ReaderSettings {
	mode: ReadingMode;
	direction: ReadingDirection;
	fit: ImageFit;
	background: string;
}

/** El lienzo arranca en negro puro: el brandbook lo exige para cero fatiga visual. */
export const READER_BACKGROUNDS = ["#000000", "#050508", "#11131C", "#F0F2F6"] as const;

export const DEFAULT_READER_SETTINGS: ReaderSettings = {
	mode: "Paged",
	direction: "LeftToRight",
	fit: "height",
	background: "#000000",
};

const KEY = "diarspeicher-reader-settings";

/**
 * Los ajustes son del usuario, no del servidor: viven en `localStorage`. Lo que llega
 * de la biblioteca (`defaultReadingMode`, `defaultReadingDir`) solo se usa la primera
 * vez, cuando aún no hay nada guardado.
 */
export function loadReaderSettings(): ReaderSettings | null {
	try {
		const raw = localStorage.getItem(KEY);
		if (!raw) return null;
		const parsed = JSON.parse(raw) as Partial<ReaderSettings>;
		return { ...DEFAULT_READER_SETTINGS, ...parsed };
	} catch {
		return null;
	}
}

export function saveReaderSettings(settings: ReaderSettings): void {
	try {
		localStorage.setItem(KEY, JSON.stringify(settings));
	} catch {
		/* almacenamiento no disponible: los ajustes duran lo que la sesión */
	}
}

/** Traduce los valores de configuración de la biblioteca a los del lector. */
export function settingsFromLibrary(
	defaultReadingMode: string | undefined,
	defaultReadingDir: string | undefined
): ReaderSettings {
	let mode: ReadingMode = "Paged";
	if (defaultReadingMode === "ContinuousVertical" || defaultReadingMode === "ContinuousHorizontal") {
		mode = "ContinuousVertical";
	}

	return {
		...DEFAULT_READER_SETTINGS,
		mode,
		direction: defaultReadingDir === "RightToLeft" ? "RightToLeft" : "LeftToRight",
	};
}
