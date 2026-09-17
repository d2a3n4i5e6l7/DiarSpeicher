import type { EpubDeviceProfile } from "../api/endpoints";

export const DEFAULT_EPUB_PROFILES: EpubDeviceProfile[] = [
	{
		id: "default-epub-profile",
		name: "CDisplayEx / Smartphone",
		devicePattern: ".*",
		width: 1200,
		height: 1920,
		fontSize: 40,
		lineHeight: 1.6,
		fontFamily: "Inter",
		marginHorizontal: 48,
		marginVertical: 48,
		theme: "core",
		isDefault: true,
	},
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
