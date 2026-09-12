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
					backgroundColor: "var(--ds-warn-bg)",
					border: "1px solid var(--ds-warn-dark)",
					borderLeft: "4px solid var(--ds-warn)",
					borderRadius: "3px",
					color: "var(--ds-warn-text)",
					"& .MuiAlert-message": { width: "100%" },
				}}
			>
				<Typography variant="subtitle2" sx={{ fontWeight: 700, mb: 0.5 }}>
					INICIALIZACIÓN DEL SISTEMA
				</Typography>
				<Typography variant="body2" sx={{ fontSize: "12px", color: "var(--ds-warn-light)" }}>
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
					backgroundColor: "var(--ds-red-deep)",
					border: "1px solid var(--ds-red-dark)",
					borderLeft: "4px solid var(--ds-red)",
					borderRadius: "3px",
					color: "var(--ds-red-soft)",
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
					backgroundColor: "var(--ds-bg-panel)",
					border: "1px solid var(--ds-border)",
					borderLeft: "4px solid var(--ds-red)",
					borderRadius: "3px",
					color: "var(--ds-platinum)",
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
				backgroundColor: "var(--ds-bg)",
				backgroundImage: `
					radial-gradient(ellipse 120% 90% at 50% 5%,
						rgba(var(--ds-red-rgb), 0.12) 0%,
						rgba(var(--ds-red-rgb), 0.078) 26%,
						rgba(var(--ds-red-rgb), 0.042) 48%,
						rgba(var(--ds-red-rgb), 0.017) 68%,
						rgba(var(--ds-red-rgb), 0) 100%),
					linear-gradient(to right, var(--ds-grid-line) 1px, transparent 1px),
					linear-gradient(to bottom, var(--ds-grid-line) 1px, transparent 1px)
				`,
				backgroundSize: "100% 100%, 40px 40px, 40px 40px",
				p: 2,
				position: "relative",
			}}
		>
			<Box className="ds-noise" />
			<Card
				sx={{
					width: "100%",
					maxWidth: 460,
					boxShadow: "var(--ds-shadow-dialog)",
					overflow: "hidden",
				}}
			>
				<HudFrame />

				<Box
					sx={{
						height: 42,
						background: "var(--ds-bg-sunken)",
						borderBottom: "1px solid var(--ds-border-head)",
						display: "flex",
						alignItems: "center",
						justifyContent: "space-between",
						px: 2.5,
					}}
				>
					<Box sx={{ display: "flex", alignItems: "center", gap: 1 }}>
						<Box
							sx={{
								background: "var(--ds-bg-pill)",
								color: "var(--ds-red-glow)",
								border: "1px solid var(--ds-red-dark)",
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
								color: "var(--ds-muted)",
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
							color: "var(--ds-subtle)",
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
									color: "var(--ds-text-strong)",
									lineHeight: 1.1,
								}}
							>
								DIAR<Box component="span" sx={{ color: "var(--ds-red-glow)" }}>SPEICHER</Box>
							</Typography>
							<Typography
								sx={{
									fontFamily: "'Rajdhani', sans-serif",
									fontSize: "12px",
									fontWeight: 700,
									letterSpacing: "3.5px",
									color: "var(--ds-muted)",
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
								background: "var(--ds-bg-panel)",
								border: "1px solid var(--ds-metal-2)",
								borderLeft: "3px solid var(--ds-red)",
								borderRadius: "2px",
							}}
						>
							<Typography
								sx={{
									fontFamily: "'JetBrains Mono', monospace",
									fontSize: "10px",
									color: "var(--ds-red-light)",
									fontWeight: 700,
								}}
							>
								DIARMUND ARCHIVE PROTOCOL
							</Typography>
							<Typography sx={{ color: "var(--ds-subtle)", fontSize: "10px" }}>|</Typography>
							<Typography
								sx={{
									fontFamily: "'Rajdhani', sans-serif",
									fontSize: "11px",
									color: "var(--ds-platinum)",
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
										color: "var(--ds-text-2)",
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
										color: "var(--ds-text-2)",
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
														sx={{ color: "var(--ds-muted)" }}
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
									backgroundColor: "var(--ds-bg-btn)",
									color: "var(--ds-platinum)",
									border: "1px solid var(--ds-border-hi)",
									fontFamily: "'Rajdhani', sans-serif",
									fontWeight: 700,
									fontSize: "15px",
									letterSpacing: "1.5px",
									textTransform: "uppercase",
									clipPath: "polygon(6px 0%, 100% 0%, 100% calc(100% - 6px), calc(100% - 6px) 100%, 0% 100%, 0% 6px)",
									transition: "all 0.2s ease",
									"&:hover": {
										backgroundColor: "var(--ds-red)",
										borderColor: "var(--ds-red-glow)",
										color: "#FFFFFF",
										boxShadow: "0 0 16px rgba(var(--ds-red-rgb), var(--ds-glow-a))",
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
							borderTop: "1px solid var(--ds-border-soft)",
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
								color: "var(--ds-subtle)",
							}}
						>
							GATEWAY: <code>{URL_ACCESS}</code>
						</Typography>
						<Typography
							variant="caption"
							sx={{
								fontFamily: "'JetBrains Mono', monospace",
								fontSize: "10px",
								color: "var(--ds-red-hover)",
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
