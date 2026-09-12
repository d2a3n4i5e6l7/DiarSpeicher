import { createTheme } from "@mui/material/styles";
import type { PaletteMode, Theme, ThemeOptions } from "@mui/material";
import { alertClasses } from "@mui/material/Alert";
import { chipClasses } from "@mui/material/Chip";
import { getBrandTokens, PALETTE_LITERALS } from "./tokens";

function getPalette(mode: PaletteMode): ThemeOptions["palette"] {
	const p = mode === "light" ? PALETTE_LITERALS.light : PALETTE_LITERALS.dark;
	return {
		mode,
		primary: { ...p.primary, contrastText: "#FFFFFF" },
		secondary: { ...p.secondary, contrastText: "#FFFFFF" },
		error: { ...p.error },
		warning: { ...p.warning },
		info: { ...p.info },
		success: { ...p.success },
		background: { ...p.background },
		text: { ...p.text },
		divider: p.divider,
	};
}

const TYPOGRAPHY_OPTIONS: ThemeOptions["typography"] = {
	fontFamily: ["'Inter'", "'Chakra Petch'", "'Roboto'", "sans-serif"].join(","),
	h1: {
		fontFamily: "'Orbitron', sans-serif",
		fontWeight: 900,
		letterSpacing: "2px",
	},
	h2: {
		fontFamily: "'Orbitron', sans-serif",
		fontWeight: 900,
		letterSpacing: "2px",
	},
	h3: {
		fontFamily: "'Orbitron', sans-serif",
		fontWeight: 700,
		letterSpacing: "1.5px",
	},
	h4: {
		fontFamily: "'Rajdhani', sans-serif",
		fontWeight: 700,
		letterSpacing: "1px",
		textTransform: "uppercase",
	},
	h5: {
		fontFamily: "'Rajdhani', sans-serif",
		fontWeight: 700,
		letterSpacing: "1px",
		textTransform: "uppercase",
	},
	h6: {
		fontFamily: "'Rajdhani', sans-serif",
		fontWeight: 700,
		letterSpacing: "1px",
		textTransform: "uppercase",
	},
	subtitle1: {
		fontFamily: "'Rajdhani', sans-serif",
		fontWeight: 600,
		letterSpacing: "0.5px",
	},
	subtitle2: {
		fontFamily: "'Rajdhani', sans-serif",
		fontWeight: 600,
		letterSpacing: "0.5px",
	},
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

/** Rampa metalica del isotipo: es la que da el bisel de los bordes. */
const METAL_BEVEL = "var(--ds-bevel-metal)";
const METAL_BEVEL_HOT = "var(--ds-bevel-metal-hot)";
const SURFACE_GRADIENT = "var(--ds-gradient-card)";
/** Halo rojo. En modo dia `--ds-glow-a` vale 0 y esto se apaga solo. */
const RED_GLOW = "0 0 14px rgba(var(--ds-red-rgb), var(--ds-glow-a))";
const RED_GLOW_LIFT =
	"0 8px 30px rgba(var(--ds-red-rgb), calc(var(--ds-glow-a) * 0.5))";

/** Chaflan balistico a 45 grados: esquina superior izquierda e inferior derecha. */
function chamfer(size: number): string {
	return `polygon(${size}px 0%, 100% 0%, 100% calc(100% - ${size}px), calc(100% - ${size}px) 100%, 0% 100%, 0% ${size}px)`;
}

function getCssBaselineStyles(mode: PaletteMode) {
	return {
		":root": {
			...getBrandTokens(mode),
			/* Alias historicos: habia paginas escritas contra estos nombres. */
			"--bg-dark": "var(--ds-bg)",
			"--bg-card": "var(--ds-bg-card)",
			"--bg-surface": "var(--ds-bg-surface)",
			"--bg-sunken": "var(--ds-bg-sunken)",
			"--ds-border-active": "var(--ds-red)",
			/* Tipografias */
			"--font-display": "'Orbitron', sans-serif",
			"--font-tactical": "'Rajdhani', sans-serif",
			"--font-mono": "'JetBrains Mono', monospace",
			"--font-body": "'Inter', sans-serif",
		},
		/* El lector se queda en modo noche pase lo que pase: vuelve a declarar
		 * los tokens oscuros en su propio subarbol, asi que todo lo que cuelgue
		 * de el los resuelve en oscuro aunque `:root` este en dia. */
		".ds-force-dark": {
			...getBrandTokens("dark"),
		},
		body: {
			backgroundColor: "var(--ds-bg)",
			color: "var(--ds-platinum)",
			scrollbarColor: "var(--ds-border) var(--ds-bg)",
			scrollbarWidth: "thin",
			WebkitUserSelect: "none",
			MozUserSelect: "none",
			msUserSelect: "none",
			userSelect: "none",
		},
		'input, textarea, [contenteditable="true"], [contenteditable=""], [role="textbox"]':
			{
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
			background: "var(--ds-bg)",
		},
		"::-webkit-scrollbar-thumb": {
			background: "var(--ds-border)",
			borderRadius: "2px",
		},
		"::-webkit-scrollbar-thumb:hover": {
			background: "var(--ds-red)",
		},
		".btn-tactical": {
			background: "var(--ds-bg-btn)",
			color: "var(--ds-platinum)",
			border: "1px solid var(--ds-border-hi)",
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
			background: "var(--ds-red)",
			borderColor: "var(--ds-red-glow)",
			color: "#FFFFFF",
			boxShadow: RED_GLOW,
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
			borderTop: "2px solid var(--ds-red)",
			borderLeft: "2px solid var(--ds-red)",
		},
		".hud-corner-tr": {
			top: 0,
			right: 0,
			borderTop: "2px solid var(--ds-red)",
			borderRight: "2px solid var(--ds-red)",
		},
		".hud-corner-bl": {
			bottom: 0,
			left: 0,
			borderBottom: "2px solid var(--ds-red)",
			borderLeft: "2px solid var(--ds-red)",
		},
		".hud-corner-br": {
			bottom: 0,
			right: 0,
			borderBottom: "2px solid var(--ds-red)",
			borderRight: "2px solid var(--ds-red)",
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
		'.ds-bevel.ds-bevel > *:not([class*="hud-corner"]):not(.ds-grid-bg):not(.ds-glow-bg)':
			{
				position: "relative",
				zIndex: 1,
			},
		".ds-bevel.ds-bevel:hover": {
			background: METAL_BEVEL_HOT,
			boxShadow: RED_GLOW_LIFT,
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
				"linear-gradient(to right, var(--ds-grid-line) 1px, transparent 1px)",
				"linear-gradient(to bottom, var(--ds-grid-line) 1px, transparent 1px)",
			].join(","),
			backgroundSize: "40px 40px",
			pointerEvents: "none",
			zIndex: 0,
		},
		/* Halo carmesi superior: quita el fondo plano.
		 * Ojo con el corte: terminar en `transparent` deja un circulo visible
		 * porque `transparent` es rgba(0,0,0,0) y el degradado pasa por gris al
		 * interpolar. Se cierra en el mismo rojo con alfa 0 y se reparte en
		 * varias paradas para que el borde no tenga filo.
		 */
		".ds-glow-bg": {
			position: "absolute",
			inset: 0,
			backgroundImage: [
				"radial-gradient(ellipse 130% 80% at 50% -20%,",
				"rgba(var(--ds-red-rgb), 0.10) 0%,",
				"rgba(var(--ds-red-rgb), 0.065) 26%,",
				"rgba(var(--ds-red-rgb), 0.035) 48%,",
				"rgba(var(--ds-red-rgb), 0.014) 68%,",
				"rgba(var(--ds-red-rgb), 0) 100%)",
			].join(" "),
			pointerEvents: "none",
			zIndex: 0,
		},
		/* Firefox no aplica dithering a los degradados, asi que un halo tan tenue
		 * se escalona y el escalon se lee como un circulo. Chrome y Edge lo
		 * disimulan solos. Esta capa de ruido rompe las bandas en los tres.
		 */
		".ds-glow-bg::after": {
			content: '""',
			position: "absolute",
			inset: 0,
			backgroundImage:
				"url(\"data:image/svg+xml,%3Csvg xmlns='http://www.w3.org/2000/svg' width='140' height='140'%3E%3Cfilter id='n'%3E%3CfeTurbulence type='fractalNoise' baseFrequency='0.9' numOctaves='3' stitchTiles='stitch'/%3E%3C/filter%3E%3Crect width='140' height='140' filter='url(%23n)'/%3E%3C/svg%3E\")",
			opacity: 0.035,
			pointerEvents: "none",
		},

		/* Capa de ruido suelta, para halos pintados a mano fuera de .ds-glow-bg. */
		".ds-noise": {
			position: "absolute",
			inset: 0,
			pointerEvents: "none",
			zIndex: 0,
			backgroundImage:
				"url(\"data:image/svg+xml,%3Csvg xmlns='http://www.w3.org/2000/svg' width='140' height='140'%3E%3Cfilter id='n'%3E%3CfeTurbulence type='fractalNoise' baseFrequency='0.9' numOctaves='3' stitchTiles='stitch'/%3E%3C/filter%3E%3Crect width='140' height='140' filter='url(%23n)'/%3E%3C/svg%3E\")",
			opacity: 0.035,
		},

		/* --- Barrido de indexado ---
		 * La linea recorre la tarjeta de arriba abajo mientras el escaner trabaja. El halo
		 * usa la alfa global, asi que de dia se apaga y queda una linea plana: el rojo
		 * diurno es tinta, no luz.
		 */
		".ds-scanline": {
			position: "absolute",
			inset: 0,
			overflow: "hidden",
			pointerEvents: "none",
			zIndex: 3,
		},
		".ds-scanline::after": {
			content: '""',
			position: "absolute",
			left: 0,
			right: 0,
			height: "2px",
			background: "linear-gradient(90deg, transparent 0%, var(--ds-red-glow) 50%, transparent 100%)",
			boxShadow: "0 0 12px rgba(var(--ds-red-rgb), calc(var(--ds-glow-a) * 1.8))",
			animation: "ds-scan 1.9s linear infinite",
		},
		"@keyframes ds-scan": {
			"0%": { top: "-2px", opacity: 0.2 },
			"12%": { opacity: 1 },
			"88%": { opacity: 1 },
			"100%": { top: "100%", opacity: 0.2 },
		},
		/* Un barrido continuo en pantalla es justo lo que molesta a quien pide menos
		 * movimiento: se queda como una linea fija arriba. */
		"@media (prefers-reduced-motion: reduce)": {
			".ds-scanline::after": {
				animation: "none",
				top: 0,
			},
		},

		/* Puntos suspensivos de terminal de fosforo: verde fijo, no el rojo de la marca,
		 * porque no es una alerta sino la maquina pensando. */
		".ds-dots": {
			color: "#33FF66",
			textShadow: "0 0 6px rgba(51, 255, 102, 0.55)",
			letterSpacing: "2px",
		},
		".ds-dots span": {
			opacity: 0.15,
			animation: "ds-blink 1.2s infinite steps(1, end)",
		},
		".ds-dots span:nth-of-type(2)": { animationDelay: "0.4s" },
		".ds-dots span:nth-of-type(3)": { animationDelay: "0.8s" },
		"@keyframes ds-blink": {
			"0%, 33%": { opacity: 1 },
			"34%, 100%": { opacity: 0.15 },
		},

		/* --- Pastilla monoespaciada para IDs, hashes y contadores --- */
		".ds-pill-mono": {
			display: "inline-flex",
			alignItems: "center",
			gap: "4px",
			background: "var(--ds-bg-pill)",
			color: "var(--ds-red-glow)",
			border: "1px solid var(--ds-red-dark)",
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
		backgroundColor: "var(--ds-metal-lo)",
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

function getComponentOverrides(mode: PaletteMode): ThemeOptions["components"] {
	return {
		MuiCssBaseline: {
			styleOverrides: getCssBaselineStyles(mode),
		},
		MuiPaper: {
			defaultProps: { elevation: 0 },
			styleOverrides: {
				root: {
					backgroundImage: SURFACE_GRADIENT,
					backgroundColor: "var(--ds-bg-card)",
					border: "1px solid var(--ds-border)",
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
							boxShadow: RED_GLOW_LIFT,
						},
					},
				},
			},
		},
		MuiDialog: {
			styleOverrides: {
				paper: {
					"&&": {
						...metalBevel(16, "var(--ds-gradient-dialog)"),
						boxShadow: "var(--ds-shadow-dialog)",
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
					color: "var(--ds-text-strong)",
					backgroundColor: "var(--ds-bg-sunken)",
					borderBottom: "1px solid var(--ds-border-head)",
					padding: "16px 24px",
				},
			},
		},
		MuiDialogActions: {
			styleOverrides: {
				root: {
					padding: "16px 24px",
					borderTop: "1px solid var(--ds-border-soft)",
					backgroundColor: "var(--ds-bg-sunken)",
				},
			},
		},
		MuiMenu: {
			styleOverrides: {
				paper: {
					backgroundImage: "none",
					backgroundColor: "var(--ds-bg-overlay)",
					border: "1px solid var(--ds-border)",
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
					backgroundColor: "var(--ds-red)",
					color: "#FFFFFF",
					"&:hover": {
						backgroundColor: "var(--ds-red-hover)",
						boxShadow: RED_GLOW,
					},
				},
				outlined: {
					borderColor: "var(--ds-red)",
					color: "var(--ds-red-glow)",
					"&:hover": {
						borderColor: "var(--ds-red-glow)",
						backgroundColor: "rgba(var(--ds-red-rgb), 0.1)",
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
					backgroundColor: "var(--ds-bg-sunken)",
					borderRadius: "2px",
					"& .MuiOutlinedInput-notchedOutline": {
						borderColor: "var(--ds-border)",
					},
					"&:hover .MuiOutlinedInput-notchedOutline": {
						borderColor: "var(--ds-red)",
					},
					"&.Mui-focused .MuiOutlinedInput-notchedOutline": {
						borderColor: "var(--ds-red-glow)",
						boxShadow: "0 0 8px rgba(var(--ds-red-rgb), var(--ds-glow-a))",
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
					backgroundColor: "rgba(var(--ds-red-rgb), 0.15)",
					color: "var(--ds-red-light)",
					border: "1px solid var(--ds-red-dark)",
				},
				outlined: {
					borderColor: "var(--ds-border)",
					[`&.${chipClasses.colorDefault}`]: {
						color: "var(--ds-text-2)",
					},
				},
			},
		},
		MuiDivider: {
			styleOverrides: {
				root: {
					borderColor: "var(--ds-border-soft)",
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
						backgroundColor: "var(--ds-red-deep)",
						border: "1px solid var(--ds-red-dark)",
						borderLeft: "4px solid var(--ds-red)",
						color: "var(--ds-red-soft)",
					},
					[`&.${alertClasses.colorSuccess}`]: {
						backgroundColor: "var(--ds-ok-bg)",
						border: "1px solid var(--ds-ok-dark)",
						borderLeft: "4px solid var(--ds-ok)",
						color: "var(--ds-ok)",
					},
					[`&.${alertClasses.colorWarning}`]: {
						backgroundColor: "var(--ds-warn-bg)",
						border: "1px solid var(--ds-warn-dark)",
						borderLeft: "4px solid var(--ds-warn)",
						color: "var(--ds-warn-text)",
					},
					[`&.${alertClasses.colorInfo}`]: {
						backgroundColor: "var(--ds-bg-panel)",
						border: "1px solid var(--ds-border)",
						borderLeft: "4px solid var(--ds-info-line)",
						color: "var(--ds-text-2)",
					},
				},
			},
		},
		MuiTableHead: {
			styleOverrides: {
				root: {
					backgroundColor: "var(--ds-bg-sunken)",
					"& .MuiTableCell-head": {
						fontFamily: "'Rajdhani', sans-serif",
						fontWeight: 700,
						letterSpacing: "1.5px",
						color: "var(--ds-muted)",
						textTransform: "uppercase",
						borderBottom: "1px solid var(--ds-border)",
					},
				},
			},
		},
		MuiTableRow: {
			styleOverrides: {
				root: {
					transition: "background-color 0.15s ease",
					"&.MuiTableRow-hover:hover": {
						backgroundColor: "rgba(var(--ds-red-rgb), 0.06)",
					},
				},
			},
		},
		MuiTableCell: {
			styleOverrides: {
				root: {
					borderBottom: "1px solid var(--ds-border-soft)",
				},
			},
		},
		MuiSwitch: {
			styleOverrides: {
				switchBase: {
					"&.Mui-checked": { color: "var(--ds-red-glow)" },
					"&.Mui-checked + .MuiSwitch-track": {
						backgroundColor: "var(--ds-red)",
						opacity: 0.6,
					},
				},
				track: {
					backgroundColor: "var(--ds-border-hi)",
				},
			},
		},
		MuiTooltip: {
			styleOverrides: {
				tooltip: {
					backgroundColor: "var(--ds-bg-overlay)",
					border: "1px solid var(--ds-border)",
					borderRadius: "2px",
					fontFamily: "'Rajdhani', sans-serif",
					fontSize: "12px",
					fontWeight: 600,
					letterSpacing: "0.5px",
					color: "var(--ds-platinum)",
				},
			},
		},
		MuiLinearProgress: {
			styleOverrides: {
				root: {
					backgroundColor: "var(--ds-red-deep)",
					borderRadius: 0,
				},
				bar: {
					backgroundColor: "var(--ds-red-glow)",
				},
			},
		},
		MuiCircularProgress: {
			styleOverrides: {
				root: {
					color: "var(--ds-red)",
				},
			},
		},
	};
}

/**
 * Tokens de marca para los `sx` de las paginas. Son referencias a variables CSS,
 * no colores literales: el valor lo decide `:root` segun el modo activo, asi que
 * una pagina escrita con `DS.*` cambia de dia a noche sin tocarla.
 * No pasar estos valores por `alpha()` ni concatenarlos dentro de `rgb()`.
 */
export const DS = {
	bgDark: "var(--ds-bg)",
	bgDeep: "var(--ds-bg-deep)",
	bgSunken: "var(--ds-bg-sunken)",
	bgCard: "var(--ds-bg-card)",
	bgPanel: "var(--ds-bg-panel)",
	bgOverlay: "var(--ds-bg-overlay)",
	bgSurface: "var(--ds-bg-surface)",
	bgSurfaceHover: "var(--ds-bg-surface-hi)",
	bgPill: "var(--ds-bg-pill)",
	bgBtn: "var(--ds-bg-btn)",
	bgDanger: "var(--ds-bg-danger)",
	red: "var(--ds-red)",
	redGlow: "var(--ds-red-glow)",
	redLight: "var(--ds-red-light)",
	redSoft: "var(--ds-red-soft)",
	redDark: "var(--ds-red-dark)",
	redHover: "var(--ds-red-hover)",
	redDeep: "var(--ds-red-deep)",
	textStrong: "var(--ds-text-strong)",
	platinum: "var(--ds-platinum)",
	text2: "var(--ds-text-2)",
	muted: "var(--ds-muted)",
	subtle: "var(--ds-subtle)",
	border: "var(--ds-border)",
	borderSoft: "var(--ds-border-soft)",
	borderHead: "var(--ds-border-head)",
	borderHi: "var(--ds-border-hi)",
	borderRed: "var(--ds-border-red)",
	metalHi: "var(--ds-metal-hi)",
	metal: "var(--ds-metal)",
	metalLo: "var(--ds-metal-lo)",
	ok: "var(--ds-ok)",
	okLight: "var(--ds-ok-light)",
	okDark: "var(--ds-ok-dark)",
	warn: "var(--ds-warn)",
	warnLight: "var(--ds-warn-light)",
	warnDark: "var(--ds-warn-dark)",
	warnText: "var(--ds-warn-text)",
	blue: "var(--ds-blue)",
	select: "var(--ds-select)",
	gradient: "var(--ds-gradient)",
	gradientHeader: "var(--ds-gradient-header)",
	gradientCard: "var(--ds-gradient-card)",
	bevelMetal: "var(--ds-bevel-metal)",
	bevelMetalHot: "var(--ds-bevel-metal-hot)",
	glowRed: RED_GLOW,
	glowRedLift: RED_GLOW_LIFT,
} as const;

/** El lector no tiene modo dia: la pagina ya es lo mas claro de la pantalla y
 *  rodearla de porcelana la haria flotar. Su marco se queda oscuro siempre. */
export const READER_CHROME = {
	bg: "#000000",
	surface: "#0D0F14",
	border: "#282C38",
	text: "#F0F2F6",
	muted: "#8E95A5",
} as const;

export function buildTheme(mode: PaletteMode): Theme {
	return createTheme({
		palette: getPalette(mode),
		shape: { borderRadius: 4 },
		typography: TYPOGRAPHY_OPTIONS,
		components: getComponentOverrides(mode),
	});
}
