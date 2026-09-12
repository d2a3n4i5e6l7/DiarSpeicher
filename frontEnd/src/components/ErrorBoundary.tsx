import { Box, Button, Typography } from "@mui/material";
import { Component, type ErrorInfo, type ReactNode } from "react";

interface Props {
	children: ReactNode;
}

interface State {
	error: Error | null;
}

/**
 * Sin esto, cualquier excepción durante el render desmonta el árbol entero y el navegador
 * se queda en negro: sin mensaje, sin ruta y sin pista de qué falló. Aquí al menos se ve
 * qué reventó y se puede volver sin reabrir la pestaña.
 */
export default class ErrorBoundary extends Component<Props, State> {
	constructor(props: Props) {
		super(props);
		this.state = { error: null };
	}

	static getDerivedStateFromError(error: Error): State {
		return { error };
	}

	componentDidCatch(error: Error, info: ErrorInfo): void {
		console.error("Fallo de render no capturado:", error, info.componentStack);
	}

	render(): ReactNode {
		const { error } = this.state;
		if (!error) return this.props.children;

		return (
			<Box
				sx={{
					minHeight: "100vh",
					display: "flex",
					flexDirection: "column",
					alignItems: "center",
					justifyContent: "center",
					gap: 2,
					p: 4,
					backgroundColor: "#050508",
					textAlign: "center",
				}}
			>
				<Typography
					sx={{
						fontFamily: "'Orbitron', sans-serif",
						fontSize: "20px",
						fontWeight: 900,
						letterSpacing: "2px",
						color: "#FF2E2E",
					}}
				>
					FALLO DEL NODO
				</Typography>

				<Typography
					sx={{
						fontFamily: "'JetBrains Mono', monospace",
						fontSize: "12px",
						color: "#A3ABB8",
						maxWidth: 700,
						wordBreak: "break-word",
					}}
				>
					{error.message}
				</Typography>

				<Button
					variant="contained"
					onClick={() => {
						window.location.reload();
					}}
					sx={{ backgroundColor: "#C21818", mt: 1 }}
				>
					RECARGAR
				</Button>
			</Box>
		);
	}
}
