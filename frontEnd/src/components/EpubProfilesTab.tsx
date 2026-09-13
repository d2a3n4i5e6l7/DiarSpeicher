import {
	Box,
	Card,
	Chip,
	Divider,
	FormControl,
	FormControlLabel,
	IconButton,
	InputLabel,
	MenuItem,
	Select,
	Slider,
	Stack,
	Switch,
	TextField,
	Tooltip,
	Typography,
} from "@mui/material";
import RestartAltIcon from "@mui/icons-material/RestartAlt";
import AutoStoriesIcon from "@mui/icons-material/AutoStories";
import { DS } from "../theme";
import type { EpubDeviceProfile } from "../api/endpoints";
import { DEFAULT_EPUB_PROFILES, FONT_OPTIONS } from "../constants/epubProfiles";

interface EpubProfilesTabProps {
	readonly profiles: readonly EpubDeviceProfile[];
	readonly onChange: (profiles: EpubDeviceProfile[]) => void;
}

function extractNumber(val: number | number[]): number {
	return Array.isArray(val) ? val[0] ?? 0 : val;
}

export default function EpubProfilesTab({ profiles, onChange }: EpubProfilesTabProps) {
	const currentProfile: EpubDeviceProfile =
		profiles.length > 0 ? { ...profiles[0] } : { ...DEFAULT_EPUB_PROFILES[0] };

	const updateProfile = (patch: Partial<EpubDeviceProfile>) => {
		const updated: EpubDeviceProfile = {
			...currentProfile,
			...patch,
			devicePattern: ".*",
			isDefault: true,
		};
		onChange([updated]);
	};

	const handleResetDefaults = () => {
		onChange([...DEFAULT_EPUB_PROFILES]);
	};

	// Colores dinámicos del simulador de pantalla en vivo
	const themeStylesMap: Record<string, { bg: string; text: string; heading: string; accent: string; border: string; muted: string }> = {
		light: { bg: "#FFFFFF", text: "#111111", heading: "#000000", accent: "#880000", border: "#D0D0D0", muted: "#666666" },
		core: { bg: "#050508", text: "#F0F2F6", heading: "#FFFFFF", accent: "#C21818", border: "#282C38", muted: "#8E95A5" },
		oled: { bg: "#000000", text: "#FFFFFF", heading: "#FFFFFF", accent: "#FF2E2E", border: "#1C1C1C", muted: "#757575" },
		sepia: { bg: "#1C1814", text: "#E4D8C8", heading: "#E29D62", accent: "#C27838", border: "#332B24", muted: "#968270" },
	};
	const themeStyles = themeStylesMap[currentProfile.theme] ?? themeStylesMap.core;

	// Escala visual calculada para que la proporción de fuente en el preview simule el dispositivo real
	const previewWidth = 320;
	const scale = previewWidth / Math.max(currentProfile.width, 320);
	const scaledFontSize = Math.max(10, Math.round(currentProfile.fontSize * scale));
	const scaledMarginH = Math.max(8, Math.round(currentProfile.marginHorizontal * scale));
	const scaledMarginV = Math.max(8, Math.round(currentProfile.marginVertical * scale));

	return (
		<Box sx={{ p: { xs: 1, sm: 2 } }}>
			<Box
				sx={{
					display: "grid",
					gridTemplateColumns: { xs: "1fr", lg: "1.15fr 0.85fr" },
					gap: 3,
					alignItems: "start",
				}}
			>
				{/* Columna Izquierda: Configuración Única del Perfil */}
				<Card
					sx={{
						p: { xs: 2, sm: 3 },
						backgroundColor: DS.bgCard,
						border: `1px solid ${DS.border}`,
						borderRadius: 0,
						position: "relative",
					}}
				>
					<Stack spacing={3}>
						{/* Encabezado Principal */}
						<Box sx={{ display: "flex", justifyContent: "space-between", alignItems: "center" }}>
							<Stack direction="row" spacing={1.2} sx={{ alignItems: "center" }}>
								<AutoStoriesIcon sx={{ fontSize: 20, color: DS.red }} />
								<Typography
									sx={{
										fontFamily: "'Rajdhani', sans-serif",
										fontWeight: 700,
										fontSize: "17px",
										letterSpacing: "1px",
										textTransform: "uppercase",
										color: DS.platinum,
									}}
								>
									AJUSTES DE RENDERIZADO: {currentProfile.name}
								</Typography>
							</Stack>

							<Tooltip title="Restablecer valores por defecto">
								<IconButton size="small" onClick={handleResetDefaults} sx={{ color: DS.muted }}>
									<RestartAltIcon fontSize="small" />
								</IconButton>
							</Tooltip>
						</Box>

						{/* Nombre del Perfil */}
						<TextField
							label="Nombre del Perfil"
							size="small"
							fullWidth
							value={currentProfile.name}
							onChange={(e) => updateProfile({ name: e.target.value })}
						/>

						{/* Dimensiones y Scroll Continuo */}
						<Box
							sx={{
								display: "grid",
								gridTemplateColumns: { xs: "1fr", sm: "1fr 1fr 1.2fr" },
								gap: 2,
								alignItems: "center",
							}}
						>
							<TextField
								label="Ancho (px)"
								type="number"
								size="small"
								fullWidth
								value={currentProfile.width}
								onChange={(e) => updateProfile({ width: Math.max(320, Number(e.target.value)) })}
							/>
							<TextField
								label="Alto (px)"
								type="number"
								size="small"
								fullWidth
								value={currentProfile.height}
								onChange={(e) => updateProfile({ height: Math.max(480, Number(e.target.value)) })}
							/>
							<FormControlLabel
								control={
									<Switch
										checked={currentProfile.autoHeight}
										onChange={(e) => updateProfile({ autoHeight: e.target.checked })}
									/>
								}
								label={
									<Box>
										<Typography sx={{ fontSize: "12px", fontWeight: 700 }}>Scroll Continuo</Typography>
										<Typography sx={{ fontSize: "10px", color: DS.muted }}>Tira vertical CDisplayEx</Typography>
									</Box>
								}
							/>
						</Box>

						<Divider sx={{ borderColor: DS.border }} />

						{/* Tipografía y Tema */}
						<Box sx={{ display: "grid", gridTemplateColumns: { xs: "1fr", sm: "1fr 1fr" }, gap: 2 }}>
							<FormControl fullWidth size="small">
								<InputLabel id="font-family-label">Tipografía del Lector</InputLabel>
								<Select
									labelId="font-family-label"
									label="Tipografía del Lector"
									value={currentProfile.fontFamily}
									onChange={(e) => updateProfile({ fontFamily: e.target.value })}
								>
									{FONT_OPTIONS.map((f) => (
										<MenuItem key={f.value} value={f.value} sx={{ fontFamily: f.value }}>
											{f.label}
										</MenuItem>
									))}
								</Select>
							</FormControl>

							<FormControl fullWidth size="small">
								<InputLabel id="theme-label">Tema Cromático</InputLabel>
								<Select
									labelId="theme-label"
									label="Tema Cromático"
									value={currentProfile.theme}
									onChange={(e) => {
										const val = e.target.value;
										if (val === "light" || val === "core" || val === "oled" || val === "sepia") {
											updateProfile({ theme: val });
										}
									}}
								>
									<MenuItem value="light">Blanco Tinta Electrónica (#FFFFFF)</MenuItem>
									<MenuItem value="core">Iron Blood Core (#050508)</MenuItem>
									<MenuItem value="oled">Negro Puro OLED (#000000)</MenuItem>
									<MenuItem value="sepia">Sepia Táctico (#1C1814)</MenuItem>
								</Select>
							</FormControl>
						</Box>

						{/* Sliders: Tamaño de Fuente, Interlineado, Márgenes */}
						<Stack spacing={2.5} sx={{ pt: 1 }}>
							<Box>
								<Stack direction="row" sx={{ justifyContent: "space-between", mb: 0.5 }}>
									<Typography sx={{ fontSize: "12px", color: DS.platinum }}>
										Tamaño de Fuente: {currentProfile.fontSize}px
									</Typography>
									<Typography sx={{ fontSize: "11px", fontFamily: "'JetBrains Mono', monospace", color: DS.redLight }}>
										{currentProfile.fontSize}px
									</Typography>
								</Stack>
								<Slider
									size="small"
									min={18}
									max={64}
									step={1}
									value={currentProfile.fontSize}
									onChange={(_, v) => updateProfile({ fontSize: extractNumber(v) })}
									sx={{ color: DS.red }}
								/>
							</Box>

							<Box>
								<Stack direction="row" sx={{ justifyContent: "space-between", mb: 0.5 }}>
									<Typography sx={{ fontSize: "12px", color: DS.platinum }}>
										Interlineado (Line Height): {currentProfile.lineHeight.toFixed(2)}
									</Typography>
									<Typography sx={{ fontSize: "11px", fontFamily: "'JetBrains Mono', monospace", color: DS.muted }}>
										x{currentProfile.lineHeight.toFixed(2)}
									</Typography>
								</Stack>
								<Slider
									size="small"
									min={1.2}
									max={2.4}
									step={0.05}
									value={currentProfile.lineHeight}
									onChange={(_, v) => updateProfile({ lineHeight: extractNumber(v) })}
									sx={{ color: DS.red }}
								/>
							</Box>

							<Box sx={{ display: "grid", gridTemplateColumns: "1fr 1fr", gap: 3 }}>
								<Box>
									<Stack direction="row" sx={{ justifyContent: "space-between", mb: 0.5 }}>
										<Typography sx={{ fontSize: "12px", color: DS.platinum }}>Margen Horiz.</Typography>
										<Typography sx={{ fontSize: "11px", fontFamily: "'JetBrains Mono', monospace", color: DS.muted }}>
											{currentProfile.marginHorizontal}px
										</Typography>
									</Stack>
									<Slider
										size="small"
										min={16}
										max={160}
										step={4}
										value={currentProfile.marginHorizontal}
										onChange={(_, v) => updateProfile({ marginHorizontal: extractNumber(v) })}
										sx={{ color: DS.red }}
									/>
								</Box>

								<Box>
									<Stack direction="row" sx={{ justifyContent: "space-between", mb: 0.5 }}>
										<Typography sx={{ fontSize: "12px", color: DS.platinum }}>Margen Vert.</Typography>
										<Typography sx={{ fontSize: "11px", fontFamily: "'JetBrains Mono', monospace", color: DS.muted }}>
											{currentProfile.marginVertical}px
										</Typography>
									</Stack>
									<Slider
										size="small"
										min={16}
										max={160}
										step={4}
										value={currentProfile.marginVertical}
										onChange={(_, v) => updateProfile({ marginVertical: extractNumber(v) })}
										sx={{ color: DS.red }}
									/>
								</Box>
							</Box>
						</Stack>
					</Stack>
				</Card>

				{/* Columna Derecha: Vista Previa en Vivo (Simulador de Pantalla E-Ink / OLED) */}
				<Card
					sx={{
						p: { xs: 2, sm: 2.5 },
						backgroundColor: DS.bgCard,
						border: `1px solid ${DS.border}`,
						borderRadius: 0,
						display: "flex",
						flexDirection: "column",
						alignItems: "center",
					}}
				>
					<Box sx={{ width: "100%", display: "flex", justifyContent: "space-between", alignItems: "center", mb: 2 }}>
						<Typography
							sx={{
								fontFamily: "'Rajdhani', sans-serif",
								fontWeight: 700,
								fontSize: "14px",
								letterSpacing: "1px",
								textTransform: "uppercase",
								color: DS.platinum,
							}}
						>
							VISTA PREVIA EN VIVO
						</Typography>
						<Chip
							label={`${String(currentProfile.width)} × ${String(currentProfile.height)} PX`}
							size="small"
							sx={{
								fontFamily: "'JetBrains Mono', monospace",
								fontSize: "10px",
								backgroundColor: DS.bgSunken,
								border: `1px solid ${DS.border}`,
								color: DS.redLight,
							}}
						/>
					</Box>

					{/* Marco del Dispositivo Táctico */}
					<Box
						sx={{
							width: previewWidth,
							maxWidth: "100%",
							height: 480,
							maxHeight: 520,
							backgroundColor: themeStyles.bg,
							border: `2px solid ${themeStyles.border}`,
							boxShadow: `0 0 16px rgba(0,0,0,0.8), inset 0 0 0 1px ${DS.borderRed}`,
							position: "relative",
							display: "flex",
							flexDirection: "column",
							overflow: "hidden",
							clipPath: "polygon(8px 0%, 100% 0%, 100% calc(100% - 8px), calc(100% - 8px) 100%, 0% 100%, 0% 8px)",
							transition: "background-color 0.25s ease, color 0.25s ease",
						}}
					>
						{/* HUD Header del Dispositivo simulado */}
						<Box
							sx={{
								px: 1.5,
								py: 0.8,
								display: "flex",
								justifyContent: "space-between",
								alignItems: "center",
								borderBottom: `1px solid ${themeStyles.border}`,
								backgroundColor: currentProfile.theme === "light" ? "#F5F5F5" : "rgba(0,0,0,0.4)",
							}}
						>
							<Typography
								sx={{
									fontFamily: "'JetBrains Mono', monospace",
									fontSize: "9px",
									color: themeStyles.accent,
									fontWeight: 700,
								}}
							>
								DIARSPEICHER // EPUB STREAM
							</Typography>
							<Typography
								sx={{
									fontFamily: "'JetBrains Mono', monospace",
									fontSize: "9px",
									color: themeStyles.muted,
								}}
							>
								{currentProfile.autoHeight ? "SCROLL CONTINUO" : "PÁG. FIJA"}
							</Typography>
						</Box>

						{/* Contenido Simulado de la Novela con la tipografía y escala configurada */}
						<Box
							sx={{
								flex: 1,
								overflowY: "auto",
								px: `${String(scaledMarginH)}px`,
								py: `${String(scaledMarginV)}px`,
								color: themeStyles.text,
								fontFamily: currentProfile.fontFamily,
								fontSize: `${String(scaledFontSize)}px`,
								lineHeight: currentProfile.lineHeight,
								textAlign: "justify",
								userSelect: "none",
							}}
						>
							<Typography
								component="div"
								sx={{
									fontFamily: currentProfile.fontFamily,
									fontWeight: 700,
									fontSize: `${String(Math.round(scaledFontSize * 1.35))}px`,
									color: themeStyles.heading,
									mb: 1.2,
									letterSpacing: "0.5px",
								}}
							>
								Capítulo 1: El despertar del búnker
							</Typography>

							<Typography
								component="p"
								sx={{
									fontFamily: currentProfile.fontFamily,
									fontSize: `${String(scaledFontSize)}px`,
									lineHeight: currentProfile.lineHeight,
									mb: 1.2,
									color: themeStyles.text,
								}}
							>
								Los indicadores parpadearon en carmesí sobre la consola táctica de DiarSpeicher. El flujo de
								datos se sincronizaba en la memoria sin tocar el disco, listo para alimentar el visor de CDisplayEx.
							</Typography>

							<Typography
								component="p"
								sx={{
									fontFamily: currentProfile.fontFamily,
									fontSize: `${String(scaledFontSize)}px`,
									lineHeight: currentProfile.lineHeight,
									mb: 1.2,
									color: themeStyles.text,
								}}
							>
								La iluminación de emergencia teñía el mamparo de un rojo profundo. Cada párrafo se rasterizaba con
								precisión milimétrica según los parámetros de tu dispositivo.
							</Typography>

							<Typography
								component="p"
								sx={{
									fontFamily: currentProfile.fontFamily,
									fontSize: `${String(scaledFontSize)}px`,
									lineHeight: currentProfile.lineHeight,
									color: themeStyles.text,
								}}
							>
								—"Transmisión iniciada con éxito. Lectura táctica en curso"— confirmó el operador.
							</Typography>
						</Box>

						{/* HUD Footer del Dispositivo simulado */}
						<Box
							sx={{
								px: 1.5,
								py: 0.6,
								display: "flex",
								justifyContent: "space-between",
								alignItems: "center",
								borderTop: `1px solid ${themeStyles.border}`,
								backgroundColor: currentProfile.theme === "light" ? "#F5F5F5" : "rgba(0,0,0,0.4)",
							}}
						>
							<Typography
								sx={{
									fontFamily: "'JetBrains Mono', monospace",
									fontSize: "9px",
									color: themeStyles.muted,
								}}
							>
								{currentProfile.name}
							</Typography>
							<Typography
								sx={{
									fontFamily: "'JetBrains Mono', monospace",
									fontSize: "9px",
									color: themeStyles.accent,
									fontWeight: 700,
								}}
							>
								[ PÁG. 01 / 28 ]
							</Typography>
						</Box>
					</Box>
				</Card>
			</Box>
		</Box>
	);
}
