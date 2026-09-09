import {
	Alert,
	Box,
	Button,
	Card,
	CardContent,
	CircularProgress,
	IconButton,
	InputAdornment,
	Stack,
	TextField,
	Typography,
	useTheme,
} from "@mui/material";
import Visibility from "@mui/icons-material/Visibility";
import VisibilityOff from "@mui/icons-material/VisibilityOff";
import LockOutlinedIcon from "@mui/icons-material/LockOutlined";
import StorageIcon from "@mui/icons-material/Storage";
import React, { useState } from "react";
import { useSession } from "../auth/SessionContext";
import { URL_ACCESS } from "../api/client";

export default function LoginPage() {
	const theme = useTheme();
	const { login } = useSession();

	const [username, setUsername] = useState("");
	const [password, setPassword] = useState("");
	const [showPassword, setShowPassword] = useState(false);
	const [loading, setLoading] = useState(false);
	const [errorMessage, setErrorMessage] = useState<string | null>(null);

	const handleSubmit = async (e: React.SyntheticEvent) => {
		e.preventDefault();
		if (!username.trim() || !password) {
			setErrorMessage("Por favor, introduce el usuario y la contraseña.");
			return;
		}

		setErrorMessage(null);
		setLoading(true);

		try {
			await login(username.trim(), password);
		} catch (err: unknown) {
			if (err instanceof Error) {
				setErrorMessage(err.message || "Error al iniciar sesión.");
			} else {
				setErrorMessage("No se pudo iniciar sesión.");
			}
		} finally {
			setLoading(false);
		}
	};

	return (
		<Box
			sx={{
				minHeight: "100vh",
				display: "flex",
				alignItems: "center",
				justifyContent: "center",
				background:
					theme.palette.mode === "dark"
						? "radial-gradient(ellipse at top, #1e1b4b 0%, #0f172a 70%)"
						: "radial-gradient(ellipse at top, #e0e7ff 0%, #f8fafc 70%)",
				p: 2,
			}}
		>
			<Card
				sx={{
					width: "100%",
					maxWidth: 440,
					backdropFilter: "blur(8px)",
					backgroundColor:
						theme.palette.mode === "dark" ? "rgba(30, 41, 59, 0.85)" : "rgba(255, 255, 255, 0.9)",
				}}
			>
				<CardContent sx={{ p: { xs: 3, sm: 4 } }}>
					<Stack spacing={3} sx={{ alignItems: "center", mb: 3 }}>
						<Box
							sx={{
								width: 56,
								height: 56,
								borderRadius: "50%",
								display: "flex",
								alignItems: "center",
								justifyContent: "center",
								background:
									theme.palette.mode === "dark"
										? "linear-gradient(135deg, #6366f1 0%, #4338ca 100%)"
										: "linear-gradient(135deg, #4f46e5 0%, #3730a3 100%)",
								color: "#fff",
								boxShadow: "0 8px 16px -4px rgba(99, 102, 241, 0.4)",
							}}
						>
							<StorageIcon fontSize="medium" />
						</Box>

						<Box sx={{ textAlign: "center" }}>
							<Typography variant="h5" component="h1" sx={{ fontWeight: 700 }}>
								DiarSpeicher
							</Typography>
							<Typography variant="body2" color="text.secondary" sx={{ mt: 0.5 }}>
								Panel de Administración del Sistema
							</Typography>
						</Box>
					</Stack>

					{errorMessage && (
						<Alert severity="error" sx={{ mb: 3 }}>
							{errorMessage}
						</Alert>
					)}

					<form onSubmit={(e) => { void handleSubmit(e); }} noValidate>
						<Stack spacing={2.5}>
							<TextField
								id="login-username"
								label="Usuario"
								variant="outlined"
								fullWidth
								required
								autoFocus
								value={username}
								onChange={(e) => setUsername(e.target.value)}
								disabled={loading}
							/>

							<TextField
								id="login-password"
								label="Contraseña"
								type={showPassword ? "text" : "password"}
								variant="outlined"
								fullWidth
								required
								value={password}
								onChange={(e) => setPassword(e.target.value)}
								disabled={loading}
								slotProps={{
									input: {
										endAdornment: (
											<InputAdornment position="end">
												<IconButton
													aria-label="toggle password visibility"
													onClick={() => setShowPassword(!showPassword)}
													edge="end"
													size="small"
												>
													{showPassword ? <VisibilityOff fontSize="small" /> : <Visibility fontSize="small" />}
												</IconButton>
											</InputAdornment>
										),
									},
								}}
							/>

							<Button
								id="login-submit-btn"
								type="submit"
								variant="contained"
								size="large"
								fullWidth
								disabled={loading}
								startIcon={loading ? <CircularProgress size={20} color="inherit" /> : <LockOutlinedIcon />}
								sx={{ py: 1.2, mt: 1 }}
							>
								{loading ? "Iniciando sesión..." : "Iniciar Sesión"}
							</Button>
						</Stack>
					</form>

					<Box sx={{ mt: 3, pt: 2, borderTop: 1, borderColor: "divider", textAlign: "center" }}>
						<Typography variant="caption" color="text.secondary">
							Conexión Gateway: <code>{URL_ACCESS}</code>
						</Typography>
					</Box>
				</CardContent>
			</Card>
		</Box>
	);
}
