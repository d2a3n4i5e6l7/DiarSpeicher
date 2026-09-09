import { createTheme, type PaletteMode } from "@mui/material";

export function buildTheme(mode: PaletteMode) {
	const isDark = mode === "dark";

	return createTheme({
		palette: {
			mode,
			primary: {
				main: isDark ? "#818cf8" : "#4f46e5",
				light: isDark ? "#a5b4fc" : "#6366f1",
				dark: isDark ? "#4f46e5" : "#4338ca",
				contrastText: "#ffffff",
			},
			secondary: {
				main: isDark ? "#38bdf8" : "#0284c7",
				light: isDark ? "#7dd3fc" : "#38bdf8",
				dark: isDark ? "#0284c7" : "#0369a1",
			},
			background: {
				default: isDark ? "#0f172a" : "#f8fafc",
				paper: isDark ? "#1e293b" : "#ffffff",
			},
			text: {
				primary: isDark ? "#f1f5f9" : "#0f172a",
				secondary: isDark ? "#94a3b8" : "#64748b",
			},
			divider: isDark ? "rgba(255, 255, 255, 0.08)" : "rgba(0, 0, 0, 0.08)",
		},
		typography: {
			fontFamily: '"Inter", -apple-system, BlinkMacSystemFont, "Segoe UI", Roboto, sans-serif',
			h5: {
				fontWeight: 700,
				letterSpacing: "-0.02em",
			},
			h6: {
				fontWeight: 600,
				letterSpacing: "-0.01em",
			},
			button: {
				textTransform: "none",
				fontWeight: 600,
			},
		},
		shape: {
			borderRadius: 10,
		},
		components: {
			MuiButton: {
				styleOverrides: {
					root: {
						borderRadius: 8,
						boxShadow: "none",
						"&:hover": {
							boxShadow: "none",
						},
					},
				},
				variants: [
					{
						props: { variant: "contained", color: "primary" },
						style: {
							background: isDark
								? "linear-gradient(135deg, #6366f1 0%, #4f46e5 100%)"
								: "linear-gradient(135deg, #4f46e5 0%, #4338ca 100%)",
						},
					},
				],
			},
			MuiPaper: {
				styleOverrides: {
					root: {
						backgroundImage: "none",
					},
				},
			},
			MuiCard: {
				styleOverrides: {
					root: {
						borderRadius: 12,
						border: isDark ? "1px solid rgba(255, 255, 255, 0.08)" : "1px solid rgba(0, 0, 0, 0.06)",
						boxShadow: isDark
							? "0 4px 20px -2px rgba(0, 0, 0, 0.5)"
							: "0 4px 20px -2px rgba(0, 0, 0, 0.05)",
					},
				},
			},
			MuiTextField: {
				defaultProps: {
					size: "small",
				},
			},
			MuiOutlinedInput: {
				styleOverrides: {
					root: {
						borderRadius: 8,
					},
				},
			},
		},
	});
}
