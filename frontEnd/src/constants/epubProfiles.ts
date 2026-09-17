import type { EpubDeviceProfile } from "../api/endpoints";

export const DEFAULT_EPUB_PROFILES: EpubDeviceProfile[] = [
	{
		id: "default-epub-profile",
		name: "CDisplayEx / Smartphone",
		devicePattern: ".*",
		width: 1200,
		height: 1920,
		autoHeight: false,
		fontSize: 40,
		lineHeight: 1.6,
		fontFamily: "Inter",
		marginHorizontal: 48,
		marginVertical: 48,
		theme: "core",
		isDefault: true,
	},
];

export const RESOLUTION_PRESETS = [
	{ label: "Móvil 1080×2400", width: 1080, height: 2400, autoHeight: false },
	{ label: "Móvil 1080×1920", width: 1080, height: 1920, autoHeight: false },
	{ label: "Tablet 1600×2560", width: 1600, height: 2560, autoHeight: false },
	{ label: "Tablet 1200×1920", width: 1200, height: 1920, autoHeight: false },
	{ label: "Desktop 1920×1080", width: 1920, height: 1080, autoHeight: false },
];

/** El libro manda: se usan las fuentes que el propio EPUB trae incrustadas. */
export const BOOK_EMBEDDED_FONT = "__book__";

export const FONT_OPTIONS = [
	{ label: "Fuentes originales del libro", value: BOOK_EMBEDDED_FONT },
	{ label: "Inter (Estándar y Limpia)", value: "Inter" },
	{ label: "Chakra Petch (Táctica Naval)", value: "Chakra Petch" },
	{ label: "Rajdhani (Militar / Condensada)", value: "Rajdhani" },
	{ label: "Orbitron (Sci-Fi Táctico)", value: "Orbitron" },
	{ label: "JetBrains Mono (Técnica / Código)", value: "JetBrains Mono" },
];
