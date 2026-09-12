import { createTheme } from "@mui/material/styles";
import type { Theme, ThemeOptions } from "@mui/material";
import { alertClasses } from "@mui/material/Alert";
import { chipClasses } from "@mui/material/Chip";

function getPalette(): ThemeOptions["palette"] {
	return {
		mode: "dark",
		primary: {
			main: "#C21818",
			light: "#FF2E2E",
			dark: "#660B0B",
			contrastText: "#FFFFFF",
		},
		secondary: {
			main: "#A3ABB8",
			light: "#F0F2F6",
			dark: "#383E4C",
			contrastText: "#FFFFFF",
		},
		error: {
			main: "#FF2E2E",
			light: "#FF5C5C",
			dark: "#80060A",
		},
		warning: {
			main: "#F59E0B",
			light: "#FBBF24",
			dark: "#78350F",
		},
		info: {
			main: "#C21818",
			light: "#FF3E3E",
			dark: "#331010",
		},
		success: {
			main: "#22C55E",
			light: "#4ADE80",
			dark: "#14532D",
		},
		background: {
			default: "#050508",
			paper: "#0F1015",
		},
		text: {
			primary: "#F0F2F6",
			secondary: "#8E95A5",
			disabled: "#636B7C",
		},
		divider: "#1C1F28",
	};
}

const TYPOGRAPHY_OPTIONS: ThemeOptions["typography"] = {
	fontFamily: ["'Inter'", "'Chakra Petch'", "'Roboto'", "sans-serif"].join(","),
	h1: { fontFamily: "'Orbitron', sans-serif", fontWeight: 900, letterSpacing: "2px" },
	h2: { fontFamily: "'Orbitron', sans-serif", fontWeight: 900, letterSpacing: "2px" },
	h3: { fontFamily: "'Orbitron', sans-serif", fontWeight: 700, letterSpacing: "1.5px" },
	h4: { fontFamily: "'Rajdhani', sans-serif", fontWeight: 700, letterSpacing: "1px", textTransform: "uppercase" },
	h5: { fontFamily: "'Rajdhani', sans-serif", fontWeight: 700, letterSpacing: "1px", textTransform: "uppercase" },
	h6: { fontFamily: "'Rajdhani', sans-serif", fontWeight: 700, letterSpacing: "1px", textTransform: "uppercase" },
	subtitle1: { fontFamily: "'Rajdhani', sans-serif", fontWeight: 600, letterSpacing: "0.5px" },
	subtitle2: { fontFamily: "'Rajdhani', sans-serif", fontWeight: 600, letterSpacing: "0.5px" },
	button: {
		fontFamily: "'Rajdhani', sans-serif",
		fontWeight: 700,
		letterSpacing: "1px",
		textTransform: "uppercase",
	},
	caption: {
		fontFamily: "'JetBrains Mono', monospace",
	},
};

/**
 * Rampa metalica del isotipo oficial: es la que da el bisel de los bordes.
 * Va de reflejo (#4B5160) a sombra (#121318) en diagonal a 145deg.
 */
const METAL_BEVEL = "linear-gradient(145deg, #4B5160 0%, #2B303E 38%, #1A1D26 62%, #121318 100%)";
const METAL_BEVEL_HOT = "linear-gradient(145deg, #8F96A3 0%, #C21818 45%, #660B0B 75%, #1A0202 100%)";
const SURFACE_GRADIENT = "linear-gradient(145deg, #161922 0%, #0A0B0E 100%)";

/** Chaflan balistico a 45 grados: esquina superior izquierda e inferior derecha. */
function chamfer(size: number): string {
	return `polygon(${size}px 0%, 100% 0%, 100% calc(100% - ${size}px), calc(100% - ${size}px) 100%, 0% 100%, 0% ${size}px)`;
}

function getCssBaselineStyles() {
	return {
		":root": {
			/* Fondos */
			"--bg-dark": "#050508",
			"--bg-card": "#0F1015",
			"--bg-surface": "#161821",
			"--bg-surface-hover": "#1E212D",
			"--bg-sunken": "#0A0B0E",
			/* Acentos */
			"--ds-red": "#C21818",
			"--ds-red-glow": "#FF2E2E",
			"--ds-red-light": "#FF3E3E",
			"--ds-red-dark": "#660B0B",
			"--ds-red-deep": "#1C0303",
			/* Texto */
			"--ds-platinum": "#F0F2F6",
			"--ds-muted": "#8E95A5",
			"--ds-subtle": "#636B7C",
			/* Bordes */
			"--ds-border": "#282C38",
			"--ds-border-red": "#381010",
			"--ds-border-active": "#C21818",
			/* Metal */
			"--ds-metal-hi": "#8F96A3",
			"--ds-metal": "#4B5160",
			"--ds-metal-lo": "#121318",
			/* Gradientes oficiales */
			"--ds-gradient": "linear-gradient(135deg, #C21818 0%, #1A0202 50%, #050508 100%)",
			"--ds-gradient-header": "linear-gradient(90deg, #1C0303 0%, #050508 100%)",
			"--ds-gradient-card": SURFACE_GRADIENT,
			"--ds-bevel-metal": METAL_BEVEL,
			/* Tipografias */
			"--font-display": "'Orbitron', sans-serif",
			"--font-tactical": "'Rajdhani', sans-serif",
			"--font-mono": "'JetBrains Mono', monospace",
			"--font-body": "'Inter', sans-serif",
		},
		body: {
			backgroundColor: "#050508",
			color: "#F0F2F6",
			scrollbarColor: "#282C38 #050508",
			scrollbarWidth: "thin",
			WebkitUserSelect: "none",
			MozUserSelect: "none",
			msUserSelect: "none",
			userSelect: "none",
		},
		'input, textarea, [contenteditable="true"], [contenteditable=""], [role="textbox"]': {
			WebkitUserSelect: "text",
			MozUserSelect: "text",
			msUserSelect: "text",
			userSelect: "text",
		},
		"::-webkit-scrollbar": {
			width: "6px",
			height: "6px",
		},
		"::-webkit-scrollbar-track": {
			background: "#050508",
		},
		"::-webkit-scrollbar-thumb": {
			background: "#282C38",
			borderRadius: "2px",
		},
		"::-webkit-scrollbar-thumb:hover": {
			background: "#C21818",
		},
		".btn-tactical": {
			background: "#151821",
			color: "#F0F2F6",
			border: "1px solid #383E4C",
			fontFamily: "'Rajdhani', sans-serif",
			fontWeight: 700,
			fontSize: "14px",
			textTransform: "uppercase",
			letterSpacing: "1px",
			padding: "6px 14px",
			cursor: "pointer",
			display: "inline-flex",
			alignItems: "center",
			gap: "6px",
			transition: "all 0.2s ease",
			clipPath: chamfer(6),
		},
		".btn-tactical:hover": {
			background: "#C21818",
			borderColor: "#FF2E2E",
			color: "#FFFFFF",
			boxShadow: "0 0 14px rgba(229, 9, 20, 0.5)",
		},

		/* --- Marcadores de esquina HUD: los cuatro cuadrados --- */
		".hud-corner-tl, .hud-corner-tr, .hud-corner-bl, .hud-corner-br": {
			position: "absolute",
			width: "14px",
			height: "14px",
			pointerEvents: "none",
			zIndex: 2,
		},
		".hud-corner-tl": {
			top: 0,
			left: 0,
			borderTop: "2px solid #C21818",
			borderLeft: "2px solid #C21818",
		},
		".hud-corner-tr": {
			top: 0,
			right: 0,
			borderTop: "2px solid #C21818",
			borderRight: "2px solid #C21818",
		},
		".hud-corner-bl": {
			bottom: 0,
			left: 0,
			borderBottom: "2px solid #C21818",
			borderLeft: "2px solid #C21818",
		},
		".hud-corner-br": {
			bottom: 0,
			right: 0,
			borderBottom: "2px solid #C21818",
			borderRight: "2px solid #C21818",
		},

		/* --- Bisel metalico ---
		 * El chaflan se come el borde en la diagonal, asi que el filo metalico
		 * es el propio fondo del elemento y ::before pinta la cara interior
		 * 1px mas adentro. Sin esto la diagonal sale sin filo.
		 */
		".ds-bevel.ds-bevel": {
			position: "relative",
			background: METAL_BEVEL,
			border: "none",
			borderRadius: 0,
			clipPath: chamfer(12),
			transition: "background 0.25s ease, box-shadow 0.25s ease",
		},
		".ds-bevel.ds-bevel::before": {
			content: '""',
			position: "absolute",
			inset: "1px",
			background: SURFACE_GRADIENT,
			clipPath: chamfer(11),
			pointerEvents: "none",
			zIndex: 0,
		},
		'.ds-bevel.ds-bevel > *:not([class*="hud-corner"]):not(.ds-grid-bg):not(.ds-glow-bg)': {
			position: "relative",
			zIndex: 1,
		},
		".ds-bevel.ds-bevel:hover": {
			background: METAL_BEVEL_HOT,
			boxShadow: "0 8px 30px rgba(194, 24, 24, 0.22)",
		},
		/* Variante ya encendida: para paneles activos o dialogos criticos. */
		".ds-bevel-hot.ds-bevel-hot": {
			background: METAL_BEVEL_HOT,
		},
		/* Bisel sin hover, para superficies estaticas (cabeceras, celdas). */
		".ds-bevel-static.ds-bevel-static:hover": {
			background: METAL_BEVEL,
			boxShadow: "none",
		},
		/* Chaflan pelado, sin filo: para chips y pastillas pequenas. */
		".ds-chamfer": {
			clipPath: chamfer(6),
			borderRadius: "0 !important",
		},

		/* --- Rejilla tactica de fondo --- */
		".ds-grid-bg": {
			position: "absolute",
			inset: 0,
			backgroundImage: [
				"linear-gradient(to right, rgba(194, 24, 24, 0.03) 1px, transparent 1px)",
				"linear-gradient(to bottom, rgba(194, 24, 24, 0.03) 1px, transparent 1px)",
			].join(","),
			backgroundSize: "40px 40px",
			pointerEvents: "none",
			zIndex: 0,
		},
		/* Halo carmesi superior del brandbook: quita el negro plano. */
		".ds-glow-bg": {
			position: "absolute",
			inset: 0,
			backgroundImage: "radial-gradient(circle at 50% 0%, rgba(194, 24, 24, 0.08) 0%, transparent 55%)",
			pointerEvents: "none",
			zIndex: 0,
		},

		/* --- Pastilla monoespaciada para IDs, hashes y contadores --- */
		".ds-pill-mono": {
			display: "inline-flex",
			alignItems: "center",
			gap: "4px",
			background: "#1B1E26",
			color: "#FF2E2E",
			border: "1px solid #660B0B",
			fontFamily: "'JetBrains Mono', monospace",
			fontSize: "11px",
			fontWeight: 700,
			letterSpacing: "0.5px",
			padding: "2px 8px",
			clipPath: chamfer(4),
		},
	};
}

/**
 * Bisel metalico reutilizable. El chaflan recorta el borde en la diagonal, por
 * eso el filo es el fondo del propio elemento y ::before pinta la cara interior
 * 1px mas adentro; con un `border` normal la diagonal saldria sin filo.
 */
function metalBevel(size: number, face: string = SURFACE_GRADIENT) {
	return {
		position: "relative" as const,
		backgroundColor: "#121318",
		backgroundImage: METAL_BEVEL,
		border: "none",
		borderRadius: 0,
		clipPath: chamfer(size),
		"&::before": {
			content: '""',
			position: "absolute" as const,
			inset: "1px",
			backgroundImage: face,
			clipPath: chamfer(size - 1),
			pointerEvents: "none" as const,
			zIndex: 0,
		},
		'& > *:not([class*="hud-corner"]):not(.ds-grid-bg):not(.ds-glow-bg)': {
			position: "relative" as const,
			zIndex: 1,
		},
	};
}

function getComponentOverrides(): ThemeOptions["components"] {
	return {
		MuiCssBaseline: {
			styleOverrides: getCssBaselineStyles(),
		},
		MuiPaper: {
			defaultProps: { elevation: 0 },
			styleOverrides: {
				root: {
					backgroundImage: SURFACE_GRADIENT,
					backgroundColor: "#0F1015",
					border: "1px solid #282C38",
				},
			},
		},
		MuiCard: {
			styleOverrides: {
				root: {
					"&&": {
						...metalBevel(14),
						transition: "background-image 0.25s ease, box-shadow 0.25s ease",
						"&:hover": {
							backgroundImage: METAL_BEVEL_HOT,
							boxShadow: "0 8px 30px rgba(194, 24, 24, 0.22)",
						},
					},
				},
			},
		},
		MuiDialog: {
			styleOverrides: {
				paper: {
					"&&": {
						...metalBevel(16, "linear-gradient(145deg, #0D0F14 0%, #060709 100%)"),
						boxShadow: "0 12px 50px rgba(0, 0, 0, 0.9)",
					},
				},
			},
		},
		MuiDialogTitle: {
			styleOverrides: {
				root: {
					fontFamily: "'Orbitron', sans-serif",
					fontSize: "17px",
					fontWeight: 900,
					letterSpacing: "1.5px",
					textTransform: "uppercase",
					color: "#FFFFFF",
					backgroundColor: "#0A0B0E",
					borderBottom: "1px solid #232733",
					padding: "16px 24px",
				},
			},
		},
		MuiDialogActions: {
			styleOverrides: {
				root: {
					padding: "16px 24px",
					borderTop: "1px solid #1C1F28",
					backgroundColor: "#0A0B0E",
				},
			},
		},
		MuiMenu: {
			styleOverrides: {
				paper: {
					backgroundImage: "none",
					backgroundColor: "#0D0F14",
					border: "1px solid #282C38",
					borderRadius: "2px",
				},
			},
		},
		MuiButton: {
			defaultProps: { disableElevation: true },
			styleOverrides: {
				root: {
					fontFamily: "'Rajdhani', sans-serif",
					fontWeight: 700,
					letterSpacing: "1px",
					borderRadius: 0,
					clipPath: chamfer(6),
					transition: "all 0.2s ease",
				},
				contained: {
					backgroundColor: "#C21818",
					color: "#FFFFFF",
					"&:hover": {
						backgroundColor: "#80060A",
						boxShadow: "0 0 14px rgba(194, 24, 24, 0.45)",
					},
				},
				outlined: {
					borderColor: "#C21818",
					color: "#FF2E2E",
					"&:hover": {
						borderColor: "#FF2E2E",
						backgroundColor: "rgba(194, 24, 24, 0.1)",
					},
				},
			},
		},
		MuiTextField: {
			defaultProps: { size: "small" },
		},
		MuiSelect: {
			defaultProps: { size: "small" },
		},
		MuiOutlinedInput: {
			styleOverrides: {
				root: {
					backgroundColor: "#0A0B0E",
					borderRadius: "2px",
					"& .MuiOutlinedInput-notchedOutline": {
						borderColor: "#282C38",
					},
					"&:hover .MuiOutlinedInput-notchedOutline": {
						borderColor: "#C21818",
					},
					"&.Mui-focused .MuiOutlinedInput-notchedOutline": {
						borderColor: "#FF2E2E",
						boxShadow: "0 0 8px rgba(255, 46, 46, 0.25)",
					},
				},
			},
		},
		MuiChip: {
			styleOverrides: {
				root: {
					fontFamily: "'JetBrains Mono', monospace",
					fontSize: "11px",
					fontWeight: 600,
					borderRadius: 0,
					clipPath: chamfer(4),
				},
				filled: {
					backgroundColor: "rgba(194, 24, 24, 0.15)",
					color: "#FF3E3E",
					border: "1px solid #660B0B",
				},
				outlined: {
					borderColor: "#282C38",
					[`&.${chipClasses.colorDefault}`]: {
						color: "#A3ABB8",
					},
				},
			},
		},
		MuiDivider: {
			styleOverrides: {
				root: {
					borderColor: "#1C1F28",
				},
			},
		},
		MuiAlert: {
			styleOverrides: {
				root: {
					borderRadius: 0,
					clipPath: chamfer(8),
					fontFamily: "'Inter', sans-serif",
				},
				standard: {
					[`&.${alertClasses.colorError}`]: {
						backgroundColor: "#1C0303",
						border: "1px solid #660B0B",
						borderLeft: "4px solid #C21818",
						color: "#FF5C5C",
					},
					[`&.${alertClasses.colorSuccess}`]: {
						backgroundColor: "#071A0E",
						border: "1px solid #14532D",
						borderLeft: "4px solid #22C55E",
						color: "#4ADE80",
					},
					[`&.${alertClasses.colorWarning}`]: {
						backgroundColor: "#1C1408",
						border: "1px solid #78350F",
						borderLeft: "4px solid #F59E0B",
						color: "#FDE68A",
					},
					[`&.${alertClasses.colorInfo}`]: {
						backgroundColor: "#11131C",
						border: "1px solid #282C38",
						borderLeft: "4px solid #8E95A5",
						color: "#A3ABB8",
					},
				},
			},
		},
		MuiTableHead: {
			styleOverrides: {
				root: {
					backgroundColor: "#0A0B0E",
					"& .MuiTableCell-head": {
						fontFamily: "'Rajdhani', sans-serif",
						fontWeight: 700,
						letterSpacing: "1.5px",
						color: "#8E95A5",
						textTransform: "uppercase",
						borderBottom: "1px solid #282C38",
					},
				},
			},
		},
		MuiTableRow: {
			styleOverrides: {
				root: {
					transition: "background-color 0.15s ease",
					"&.MuiTableRow-hover:hover": {
						backgroundColor: "rgba(194, 24, 24, 0.06)",
					},
				},
			},
		},
		MuiTableCell: {
			styleOverrides: {
				root: {
					borderBottom: "1px solid #1C1F28",
				},
			},
		},
		MuiSwitch: {
			styleOverrides: {
				switchBase: {
					"&.Mui-checked": { color: "#FF2E2E" },
					"&.Mui-checked + .MuiSwitch-track": {
						backgroundColor: "#C21818",
						opacity: 0.6,
					},
				},
				track: {
					backgroundColor: "#383E4C",
				},
			},
		},
		MuiTooltip: {
			styleOverrides: {
				tooltip: {
					backgroundColor: "#0D0F14",
					border: "1px solid #282C38",
					borderRadius: "2px",
					fontFamily: "'Rajdhani', sans-serif",
					fontSize: "12px",
					fontWeight: 600,
					letterSpacing: "0.5px",
					color: "#F0F2F6",
				},
			},
		},
		MuiLinearProgress: {
			styleOverrides: {
				root: {
					backgroundColor: "#1C0303",
					borderRadius: 0,
				},
				bar: {
					backgroundColor: "#FF2E2E",
				},
			},
		},
		MuiCircularProgress: {
			styleOverrides: {
				root: {
					color: "#C21818",
				},
			},
		},
	};
}

/** Tokens de marca para usar desde los `sx` de las paginas. */
export const DS = {
	bgDark: "#050508",
	bgSunken: "#0A0B0E",
	bgCard: "#0F1015",
	bgSurface: "#161821",
	bgSurfaceHover: "#1E212D",
	red: "#C21818",
	redGlow: "#FF2E2E",
	redLight: "#FF3E3E",
	redDark: "#660B0B",
	redDeep: "#1C0303",
	platinum: "#F0F2F6",
	muted: "#8E95A5",
	subtle: "#636B7C",
	border: "#282C38",
	borderSoft: "#1C1F28",
	borderRed: "#381010",
	metalHi: "#8F96A3",
	gradientHeader: "linear-gradient(90deg, #1C0303 0%, #050508 100%)",
	gradientCard: SURFACE_GRADIENT,
	bevelMetal: METAL_BEVEL,
	bevelMetalHot: METAL_BEVEL_HOT,
} as const;

export const theme: Theme = createTheme({
	palette: getPalette(),
	shape: {
		borderRadius: 4,
	},
	typography: TYPOGRAPHY_OPTIONS,
	components: getComponentOverrides(),
});

export function buildTheme(): Theme {
	return theme;
}
