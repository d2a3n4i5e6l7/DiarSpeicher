import { createTheme } from "@mui/material/styles";
import type { PaletteMode, Theme } from "@mui/material";

export function buildTheme(mode: PaletteMode): Theme {
	return createTheme({
		palette: {
			mode,
			primary: { main: mode === "light" ? "#00695c" : "#4db6ac" },
			secondary: { main: mode === "light" ? "#5d4037" : "#bcaaa4" },
			background: {
				default: mode === "light" ? "#f4f6f8" : "#12161b",
				paper: mode === "light" ? "#ffffff" : "#1b2129",
			},
		},
		shape: { borderRadius: 8 },
		typography: {
			fontFamily: ["Inter", "Roboto", "system-ui", "sans-serif"].join(","),
			h5: { fontWeight: 600 },
			h6: { fontWeight: 600 },
		},
		components: {
			MuiCssBaseline: {
				styleOverrides: {
					body: {
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
				},
			},
			MuiPaper: { defaultProps: { elevation: 0 }, styleOverrides: { root: { backgroundImage: "none" } } },
			MuiButton: { defaultProps: { disableElevation: true } },
			MuiTextField: { defaultProps: { size: "small" } },
			MuiSelect: { defaultProps: { size: "small" } },
		},
	});
}
