import type { PaletteMode } from "@mui/material";

/**
 * Tokens de marca en dos modos.
 *
 * El modo noche es el original (Iron Blood). El modo dia sale del uniforme:
 * porcelana de fondo, gris acero en bordes y biseles, rojo opaco de la capa y
 * el azul del iris como unico acento frio. Regla de oro: en dia el rojo es
 * tinta, no luz, asi que `--ds-glow-a` cae a 0 y todos los resplandores
 * desaparecen solos sin tocar ni una linea de las paginas.
 *
 * Todo se publica como variables CSS en `:root` para que las 17 paginas que ya
 * consumen `DS.*` cambien de modo sin reescribirlas.
 */
export type BrandTokens = Record<string, string>;

const DARK: BrandTokens = {
	/* Superficies */
	"--ds-bg": "#050508",
	"--ds-bg-deep": "#07080A",
	"--ds-bg-sunken": "#0A0B0E",
	"--ds-bg-card": "#0F1015",
	"--ds-bg-panel": "#11131C",
	"--ds-bg-overlay": "#0D0F14",
	"--ds-bg-surface": "#161821",
	"--ds-bg-surface-hi": "#1E212D",
	"--ds-bg-pill": "#1B1E26",
	"--ds-bg-btn": "#151821",
	"--ds-bg-danger": "#160303",
	"--ds-bg-rgb": "5, 5, 8",

	/* Bordes */
	"--ds-border": "#282C38",
	"--ds-border-soft": "#1C1F28",
	"--ds-border-head": "#232733",
	"--ds-border-hi": "#383E4C",
	"--ds-border-red": "#381010",

	/* Texto */
	"--ds-text-strong": "#FFFFFF",
	"--ds-platinum": "#F0F2F6",
	"--ds-text-2": "#A3ABB8",
	"--ds-muted": "#8E95A5",
	"--ds-subtle": "#636B7C",

	/* Rojo */
	"--ds-red": "#C21818",
	"--ds-red-glow": "#FF2E2E",
	"--ds-red-light": "#FF3E3E",
	"--ds-red-soft": "#FF5C5C",
	"--ds-red-dark": "#660B0B",
	"--ds-red-hover": "#80060A",
	"--ds-red-deep": "#1C0303",
	"--ds-red-deepest": "#1A0202",
	"--ds-red-rgb": "194, 24, 24",

	/* Metal */
	"--ds-metal-hi": "#8F96A3",
	"--ds-metal": "#4B5160",
	"--ds-metal-2": "#2B303E",
	"--ds-metal-3": "#1A1D26",
	"--ds-metal-lo": "#121318",

	/* Estado */
	"--ds-ok": "#22C55E",
	"--ds-ok-light": "#4ADE80",
	"--ds-ok-dark": "#14532D",
	"--ds-ok-bg": "#071A0E",
	"--ds-ok-rgb": "34, 197, 94",
	"--ds-warn": "#F59E0B",
	"--ds-warn-light": "#FBBF24",
	"--ds-warn-dark": "#78350F",
	"--ds-warn-text": "#FDE68A",
	"--ds-warn-bg": "#1C1408",
	"--ds-warn-rgb": "245, 158, 11",
	/* El azul del iris: informacion y seleccion, y solo de dia. De noche esos
	 * dos papeles siguen siendo del gris y del rojo, igual que hasta ahora. */
	"--ds-blue": "#6C9BD2",
	"--ds-info-line": "#8E95A5",
	"--ds-select": "#C21818",
	"--ds-select-rgb": "194, 24, 24",

	/* Gradientes y efectos */
	"--ds-bevel-metal":
		"linear-gradient(145deg, #4B5160 0%, #2B303E 38%, #1A1D26 62%, #121318 100%)",
	"--ds-bevel-metal-hot":
		"linear-gradient(145deg, #8F96A3 0%, #C21818 45%, #660B0B 75%, #1A0202 100%)",
	"--ds-gradient-card": "linear-gradient(145deg, #161922 0%, #0A0B0E 100%)",
	"--ds-gradient-dialog": "linear-gradient(145deg, #0D0F14 0%, #060709 100%)",
	"--ds-gradient-header": "linear-gradient(90deg, #1C0303 0%, #050508 100%)",
	"--ds-gradient":
		"linear-gradient(135deg, #C21818 0%, #1A0202 50%, #050508 100%)",
	"--ds-grid-line": "rgba(194, 24, 24, 0.03)",
	"--ds-shadow-dialog": "0 12px 50px rgba(0, 0, 0, 0.9)",
	/* Alfa global del resplandor rojo: en dia vale 0 y apaga todos los halos. */
	"--ds-glow-a": "0.45",
};

const DAY: BrandTokens = {
	/* Superficies */
	"--ds-bg": "#ECEEF2",
	"--ds-bg-deep": "#E3E6EC",
	"--ds-bg-sunken": "#DFE3EA",
	"--ds-bg-card": "#F7F8FA",
	"--ds-bg-panel": "#F2F4F7",
	"--ds-bg-overlay": "#FFFFFF",
	"--ds-bg-surface": "#FFFFFF",
	"--ds-bg-surface-hi": "#E7EAF0",
	"--ds-bg-pill": "#E3E6EC",
	"--ds-bg-btn": "#F2F4F7",
	"--ds-bg-danger": "#F7E7E7",
	"--ds-bg-rgb": "236, 238, 242",

	/* Bordes */
	"--ds-border": "#B7BEC9",
	"--ds-border-soft": "#CDD3DC",
	"--ds-border-head": "#C6CCD6",
	"--ds-border-hi": "#9AA3B0",
	"--ds-border-red": "#D8B3B3",

	/* Texto */
	"--ds-text-strong": "#16181D",
	"--ds-platinum": "#16181D",
	"--ds-text-2": "#4A505C",
	"--ds-muted": "#5A6170",
	"--ds-subtle": "#767E8C",

	/* Rojo: tinta, no luz */
	"--ds-red": "#A32020",
	"--ds-red-glow": "#8E1B1B",
	"--ds-red-light": "#B32626",
	"--ds-red-soft": "#8E1B1B",
	"--ds-red-dark": "#D3A9A9",
	"--ds-red-hover": "#7E1717",
	"--ds-red-deep": "#F3E2E2",
	"--ds-red-deepest": "#EAD6D6",
	"--ds-red-rgb": "163, 32, 32",

	/* Metal: la luz cae desde arriba, reflejo blanco primero */
	"--ds-metal-hi": "#FFFFFF",
	"--ds-metal": "#C8CED8",
	"--ds-metal-2": "#B7BEC9",
	"--ds-metal-3": "#AEB6C2",
	"--ds-metal-lo": "#9AA3B0",

	/* Estado */
	"--ds-ok": "#1B7F3E",
	"--ds-ok-light": "#22C55E",
	"--ds-ok-dark": "#BFE3CC",
	"--ds-ok-bg": "#E7F5EC",
	"--ds-ok-rgb": "27, 127, 62",
	"--ds-warn": "#B4740A",
	"--ds-warn-light": "#C98A08",
	"--ds-warn-dark": "#E6D3B0",
	"--ds-warn-text": "#6B4A07",
	"--ds-warn-bg": "#FBF2DE",
	"--ds-warn-rgb": "180, 116, 10",
	"--ds-blue": "#2F5C90",
	"--ds-info-line": "#2F5C90",
	"--ds-select": "#2F5C90",
	"--ds-select-rgb": "47, 92, 144",

	/* Gradientes y efectos */
	"--ds-bevel-metal":
		"linear-gradient(145deg, #FFFFFF 0%, #DFE3EA 38%, #C8CED8 62%, #9AA3B0 100%)",
	"--ds-bevel-metal-hot":
		"linear-gradient(145deg, #FFFFFF 0%, #A32020 45%, #7E1717 75%, #5E1010 100%)",
	"--ds-gradient-card": "linear-gradient(145deg, #FFFFFF 0%, #EDEFF4 100%)",
	"--ds-gradient-dialog": "linear-gradient(145deg, #FFFFFF 0%, #EAEDF2 100%)",
	"--ds-gradient-header": "linear-gradient(90deg, #F3E2E2 0%, #ECEEF2 100%)",
	"--ds-gradient":
		"linear-gradient(135deg, #A32020 0%, #D8BDBD 50%, #ECEEF2 100%)",
	"--ds-grid-line": "rgba(74, 80, 92, 0.05)",
	"--ds-shadow-dialog": "0 12px 40px rgba(22, 24, 29, 0.22)",
	"--ds-glow-a": "0",
};

export function getBrandTokens(mode: PaletteMode): BrandTokens {
	return mode === "light" ? DAY : DARK;
}

/** Valores literales de la paleta MUI: aqui no valen variables CSS porque MUI
 *  calcula sombras y contrastes sobre ellos. */
export const PALETTE_LITERALS = {
	dark: {
		primary: { main: "#C21818", light: "#FF2E2E", dark: "#660B0B" },
		secondary: { main: "#A3ABB8", light: "#F0F2F6", dark: "#383E4C" },
		error: { main: "#FF2E2E", light: "#FF5C5C", dark: "#80060A" },
		warning: { main: "#F59E0B", light: "#FBBF24", dark: "#78350F" },
		info: { main: "#8E95A5", light: "#A3ABB8", dark: "#383E4C" },
		success: { main: "#22C55E", light: "#4ADE80", dark: "#14532D" },
		background: { default: "#050508", paper: "#0F1015" },
		text: { primary: "#F0F2F6", secondary: "#8E95A5", disabled: "#636B7C" },
		divider: "#1C1F28",
	},
	light: {
		primary: { main: "#A32020", light: "#B32626", dark: "#7E1717" },
		secondary: { main: "#4A505C", light: "#9AA3B0", dark: "#2C313B" },
		error: { main: "#A32020", light: "#C24444", dark: "#6E1616" },
		warning: { main: "#B4740A", light: "#C98A08", dark: "#6B4A07" },
		info: { main: "#2F5C90", light: "#6C9BD2", dark: "#1E3F66" },
		success: { main: "#1B7F3E", light: "#22C55E", dark: "#0F4D25" },
		background: { default: "#ECEEF2", paper: "#F7F8FA" },
		text: { primary: "#16181D", secondary: "#5A6170", disabled: "#767E8C" },
		divider: "#CDD3DC",
	},
} as const;
