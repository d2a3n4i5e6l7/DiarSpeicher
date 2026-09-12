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
} from "@mui/material";
import Visibility from "@mui/icons-material/Visibility";
import VisibilityOff from "@mui/icons-material/VisibilityOff";
import LockOutlinedIcon from "@mui/icons-material/LockOutlined";
import AdminPanelSettingsOutlinedIcon from "@mui/icons-material/AdminPanelSettingsOutlined";
import React, { useEffect, useState } from "react";
import { useSession } from "../auth/SessionContext";
import HudFrame from "../components/HudFrame";
import { URL_ACCESS } from "../api/client";
import { authApi } from "../api/endpoints";
import DiarSpeicherLogo from "../components/DiarSpeicherLogo";

export default function LoginPage() {
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

	let subtitleText = "MANGA & EBOOK SELF-HOSTED ARCHIVE SERVER";
	let buttonText = "ACCEDER AL NODO";
	let buttonLoadingText = "AUTENTICANDO...";
	let usernameLabel = "Identificador / Usuario";
	let usernameHelperText: string | undefined;
	let submitButtonIcon: React.ReactNode = <LockOutlinedIcon fontSize="small" />;

	if (requiresFirstAdmin) {
		subtitleText = "CONFIGURACIÓN INICIAL: REGISTRO DE SYSADMIN";
		buttonText = "CREAR ADMINISTRADOR PRINCIPAL";
		buttonLoadingText = "CONFIGURANDO...";
		usernameLabel = "Usuario Administrador";
		usernameHelperText = "Este registro único inicial tendrá control total de DiarSpeicher.";
		submitButtonIcon = <AdminPanelSettingsOutlinedIcon fontSize="small" />;
	}

	if (loading) {
		submitButtonIcon = <CircularProgress size={18} color="inherit" />;
	}

	let visibilityIcon = <Visibility fontSize="small" />;
	if (showPassword) {
		visibilityIcon = <VisibilityOff fontSize="small" />;
	}

	let alertContent: React.ReactNode = null;
	if (requiresFirstAdmin) {
		alertContent = (
			<Alert
				severity="warning"
				icon={<AdminPanelSettingsOutlinedIcon />}
				sx={{
					mb: 3,
					textAlign: "left",
					backgroundColor: "#1C1408",
					border: "1px solid #78350F",
					borderLeft: "4px solid #F59E0B",
					borderRadius: "3px",
					color: "#FDE68A",
					"& .MuiAlert-message": { width: "100%" },
				}}
			>
				<Typography variant="subtitle2" sx={{ fontWeight: 700, mb: 0.5 }}>
					INICIALIZACIÓN DEL SISTEMA
				</Typography>
				<Typography variant="body2" sx={{ fontSize: "12px", color: "#FBBF24" }}>
					No se detectaron usuarios en el reino. Las credenciales configuradas aquí se asignarán al <strong>Administrador Principal</strong>.
				</Typography>
			</Alert>
		);
	} else if (errorMessage) {
		alertContent = (
			<Alert
				severity="error"
				sx={{
					mb: 3,
					backgroundColor: "#1C0303",
					border: "1px solid #660B0B",
					borderLeft: "4px solid #C21818",
					borderRadius: "3px",
					color: "#FF5C5C",
				}}
			>
				<Typography variant="body2" sx={{ fontSize: "12px" }}>
					{errorMessage}
				</Typography>
			</Alert>
		);
	} else if (infoMessage) {
		alertContent = (
			<Alert
				severity="info"
				sx={{
					mb: 3,
					backgroundColor: "#11131C",
					border: "1px solid #282C38",
					borderLeft: "4px solid #C21818",
					borderRadius: "3px",
					color: "#F0F2F6",
				}}
			>
				<Typography variant="body2" sx={{ fontSize: "12px" }}>
					{infoMessage}
				</Typography>
			</Alert>
		);
	}

	return (
		<Box
			sx={{
				minHeight: "100vh",
				width: "100%",
				display: "flex",
				flexDirection: "column",
				alignItems: "center",
				justifyContent: "center",
				backgroundColor: "#050508",
				backgroundImage: `
					radial-gradient(circle at 50% 15%, rgba(194, 24, 24, 0.12) 0%, transparent 65%),
					linear-gradient(to right, rgba(194, 24, 24, 0.03) 1px, transparent 1px),
					linear-gradient(to bottom, rgba(194, 24, 24, 0.03) 1px, transparent 1px)
				`,
				backgroundSize: "100% 100%, 40px 40px, 40px 40px",
				p: 2,
				position: "relative",
			}}
		>
			<Card
				sx={{
					width: "100%",
					maxWidth: 460,
					boxShadow: "0 12px 50px rgba(0, 0, 0, 0.9)",
					overflow: "hidden",
				}}
			>
				<HudFrame />

				<Box
					sx={{
						height: 42,
						background: "#0A0B0E",
						borderBottom: "1px solid #232733",
						display: "flex",
						alignItems: "center",
						justifyContent: "space-between",
						px: 2.5,
					}}
				>
					<Box sx={{ display: "flex", alignItems: "center", gap: 1 }}>
						<Box
							sx={{
								background: "#1B1E26",
								color: "#FF2E2E",
								border: "1px solid #660B0B",
								fontFamily: "'JetBrains Mono', monospace",
								fontSize: 11,
								fontWeight: 700,
								px: 0.75,
								py: 0.25,
							}}
						>
							SEC-01
						</Box>
						<Typography
							sx={{
								fontFamily: "'Rajdhani', sans-serif",
								fontWeight: 700,
								fontSize: 12,
								letterSpacing: "1.5px",
								color: "#8E95A5",
								textTransform: "uppercase",
							}}
						>
							CONTROL DE ACCESO AL NODO
						</Typography>
					</Box>
					<Typography
						sx={{
							fontFamily: "'JetBrains Mono', monospace",
							fontSize: 10,
							color: "#636B7C",
							letterSpacing: "1px",
						}}
					>
						DS-ARCHIVE-2026
					</Typography>
				</Box>

				<CardContent sx={{ p: { xs: 3, sm: 4.5 } }}>
					<Stack spacing={2} sx={{ alignItems: "center", mb: 3.5, textAlign: "center" }}>
						<DiarSpeicherLogo size={70} showText={false} />

						<Box>
							<Typography
								component="h1"
								sx={{
									fontFamily: "'Orbitron', sans-serif",
									fontSize: "26px",
									fontWeight: 900,
									letterSpacing: "2.5px",
									color: "#FFFFFF",
									lineHeight: 1.1,
								}}
							>
								DIAR<Box component="span" sx={{ color: "#FF2E2E" }}>SPEICHER</Box>
							</Typography>
							<Typography
								sx={{
									fontFamily: "'Rajdhani', sans-serif",
									fontSize: "12px",
									fontWeight: 700,
									letterSpacing: "3.5px",
									color: "#8E95A5",
									textTransform: "uppercase",
									mt: 0.75,
								}}
							>
								{subtitleText}
							</Typography>
						</Box>

						<Box
							sx={{
								display: "inline-flex",
								alignItems: "center",
								gap: 1,
								px: 1.5,
								py: 0.5,
								background: "#11131C",
								border: "1px solid #2B303E",
								borderLeft: "3px solid #C21818",
								borderRadius: "2px",
							}}
						>
							<Typography
								sx={{
									fontFamily: "'JetBrains Mono', monospace",
									fontSize: "10px",
									color: "#FF3E3E",
									fontWeight: 700,
								}}
							>
								DIARMUND ARCHIVE PROTOCOL
							</Typography>
							<Typography sx={{ color: "#636B7C", fontSize: "10px" }}>|</Typography>
							<Typography
								sx={{
									fontFamily: "'Rajdhani', sans-serif",
									fontSize: "11px",
									color: "#D1D5DB",
									letterSpacing: "1px",
									fontWeight: 600,
								}}
							>
								IRON BLOOD CORE
							</Typography>
						</Box>
					</Stack>

					{alertContent}

					<form onSubmit={(e) => { void handleSubmit(e); }} noValidate>
						<Stack spacing={2.5}>
							<Box>
								<Typography
									component="label"
									htmlFor="login-username"
									sx={{
										display: "block",
										fontFamily: "'Rajdhani', sans-serif",
										fontWeight: 700,
										fontSize: "12px",
										letterSpacing: "1px",
										color: "#A3ABB8",
										textTransform: "uppercase",
										mb: 0.75,
									}}
								>
									{usernameLabel}
								</Typography>
								<TextField
									id="login-username"
									variant="outlined"
									fullWidth
									required
									autoFocus
									value={username}
									onChange={(e) => setUsername(e.target.value)}
									disabled={loading}
									helperText={usernameHelperText}
									placeholder="diarmund"
									slotProps={{
										input: {
											sx: {
												fontFamily: "'JetBrains Mono', monospace",
												fontSize: "13px",
											},
										},
									}}
								/>
							</Box>

							<Box>
								<Typography
									component="label"
									htmlFor="login-password"
									sx={{
										display: "block",
										fontFamily: "'Rajdhani', sans-serif",
										fontWeight: 700,
										fontSize: "12px",
										letterSpacing: "1px",
										color: "#A3ABB8",
										textTransform: "uppercase",
										mb: 0.75,
									}}
								>
									Contraseña Maestra
								</Typography>
								<TextField
									id="login-password"
									type={showPassword ? "text" : "password"}
									variant="outlined"
									fullWidth
									required
									value={password}
									onChange={(e) => setPassword(e.target.value)}
									disabled={loading}
									placeholder="••••••••••••"
									slotProps={{
										input: {
											sx: {
												fontFamily: "'JetBrains Mono', monospace",
												fontSize: "13px",
											},
											endAdornment: (
												<InputAdornment position="end">
													<IconButton
														aria-label="toggle password visibility"
														onClick={() => setShowPassword(!showPassword)}
														edge="end"
														size="small"
														sx={{ color: "#8E95A5" }}
													>
														{visibilityIcon}
													</IconButton>
												</InputAdornment>
											),
										},
									}}
								/>
							</Box>

							<Button
								id="login-submit-btn"
								type="submit"
								fullWidth
								disabled={loading}
								startIcon={submitButtonIcon}
								sx={{
									mt: 1.5,
									py: 1.3,
									backgroundColor: "#151821",
									color: "#F0F2F6",
									border: "1px solid #383E4C",
									fontFamily: "'Rajdhani', sans-serif",
									fontWeight: 700,
									fontSize: "15px",
									letterSpacing: "1.5px",
									textTransform: "uppercase",
									clipPath: "polygon(6px 0%, 100% 0%, 100% calc(100% - 6px), calc(100% - 6px) 100%, 0% 100%, 0% 6px)",
									transition: "all 0.2s ease",
									"&:hover": {
										backgroundColor: "#C21818",
										borderColor: "#FF2E2E",
										color: "#FFFFFF",
										boxShadow: "0 0 16px rgba(194, 24, 24, 0.55)",
									},
								}}
							>
								{loading ? buttonLoadingText : buttonText}
							</Button>
						</Stack>
					</form>

					<Box
						sx={{
							mt: 3.5,
							pt: 2,
							borderTop: "1px solid #1C1F28",
							display: "flex",
							alignItems: "center",
							justifyContent: "space-between",
						}}
					>
						<Typography
							variant="caption"
							sx={{
								fontFamily: "'JetBrains Mono', monospace",
								fontSize: "10px",
								color: "#636B7C",
							}}
						>
							GATEWAY: <code>{URL_ACCESS}</code>
						</Typography>
						<Typography
							variant="caption"
							sx={{
								fontFamily: "'JetBrains Mono', monospace",
								fontSize: "10px",
								color: "#80060A",
								fontWeight: 700,
							}}
						>
							[RESTRICTED]
						</Typography>
					</Box>
				</CardContent>
			</Card>
		</Box>
	);
}
