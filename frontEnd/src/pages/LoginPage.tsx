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
import AdminPanelSettingsOutlinedIcon from "@mui/icons-material/AdminPanelSettingsOutlined";
import React, { useEffect, useState } from "react";
import { useSession } from "../auth/SessionContext";
import { URL_ACCESS } from "../api/client";
import { authApi } from "../api/endpoints";

export default function LoginPage() {
	const theme = useTheme();
	const { login, registerFirstAdmin } = useSession();

	const [username, setUsername] = useState("");
	const [password, setPassword] = useState("");
	const [showPassword, setShowPassword] = useState(false);
	const [loading, setLoading] = useState(false);
	const [requiresFirstAdmin, setRequiresFirstAdmin] = useState(false);
	const [infoMessage, setInfoMessage] = useState<string | null>(null);
	const [errorMessage, setErrorMessage] = useState<string | null>(null);

	useEffect(() => {
		let isMounted = true;
		const checkInitialSetup = async () => {
			try {
				const res = await authApi.login({ username: "__probe__", password: "__probe__" });
				if (isMounted && res?.requires_first_admin) {
					setRequiresFirstAdmin(true);
				}
			} catch {
				// Si ya existen usuarios, el endpoint responde 401 y el estado permanece en login normal
			}
		};
		void checkInitialSetup();
		return () => {
			isMounted = false;
		};
	}, []);

	const handleSubmit = async (e: React.SyntheticEvent) => {
		e.preventDefault();
		if (!username.trim() || !password) {
			setErrorMessage("Por favor, introduce el usuario y la contraseña.");
			return;
		}

		setErrorMessage(null);
		setLoading(true);

		try {
			if (requiresFirstAdmin) {
				await registerFirstAdmin(username.trim(), password);
			} else {
				const res = await login(username.trim(), password);
				if (res?.requires_first_admin) {
					setRequiresFirstAdmin(true);
					setInfoMessage(
						res.message ||
							"El reino DiarSpeicher no tiene usuarios registrados. Registra el primer Administrador."
					);
				}
			}
		} catch (err: unknown) {
			if (err instanceof Error) {
				setErrorMessage(err.message || "Error al procesar la solicitud.");
			} else {
				setErrorMessage("No se pudo completar la operación.");
			}
		} finally {
			setLoading(false);
		}
	};

	let subtitleText = "Panel de Administración del Sistema";
	let buttonText = "Iniciar Sesión";
	let buttonLoadingText = "Iniciando sesión...";
	let usernameLabel = "Usuario";
	let usernameHelperText: string | undefined;
	let submitButtonIcon: React.ReactNode = <LockOutlinedIcon />;

	if (requiresFirstAdmin) {
		subtitleText = "Configuración Inicial: Crear Administrador Principal";
		buttonText = "Registrar Administrador Principal";
		buttonLoadingText = "Registrando administrador...";
		usernameLabel = "Usuario Administrador";
		usernameHelperText = "Este registro único inicial tendrá control total como Administrador.";
		submitButtonIcon = <AdminPanelSettingsOutlinedIcon />;
	}

	if (loading) {
		submitButtonIcon = <CircularProgress size={20} color="inherit" />;
	}

	let visibilityIcon = <Visibility fontSize="small" />;
	if (showPassword) {
		visibilityIcon = <VisibilityOff fontSize="small" />;
	}

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
								{subtitleText}
							</Typography>
						</Box>
					</Stack>

					{requiresFirstAdmin && (
						<Alert
							severity="warning"
							icon={<AdminPanelSettingsOutlinedIcon />}
							sx={{
								mb: 3,
								textAlign: "left",
								border: "1px solid",
								borderColor: "warning.main",
								"& .MuiAlert-message": { width: "100%" },
							}}
						>
							<Typography variant="subtitle2" sx={{ fontWeight: 700, mb: 0.5 }}>
								Inicio inicial detectado
							</Typography>
							<Typography variant="body2">
								No hay usuarios registrados en el sistema. Las credenciales escritas aquí se usarán para el registro inicial del <strong>Administrador</strong> del reino.
							</Typography>
						</Alert>
					)}

					{infoMessage && !requiresFirstAdmin && (
						<Alert severity="info" sx={{ mb: 3 }}>
							{infoMessage}
						</Alert>
					)}

					{errorMessage && (
						<Alert severity="error" sx={{ mb: 3 }}>
							{errorMessage}
						</Alert>
					)}

					<form onSubmit={(e) => { void handleSubmit(e); }} noValidate>
						<Stack spacing={2.5}>
							<TextField
								id="login-username"
								label={usernameLabel}
								helperText={usernameHelperText}
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
													{visibilityIcon}
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
								startIcon={submitButtonIcon}
								sx={{ py: 1.2, mt: 1 }}
							>
								{loading ? buttonLoadingText : buttonText}
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

