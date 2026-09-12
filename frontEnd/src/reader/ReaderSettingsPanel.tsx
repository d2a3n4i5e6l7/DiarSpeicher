import { Box, Drawer, IconButton, ToggleButton, ToggleButtonGroup, Typography } from "@mui/material";
import CloseIcon from "@mui/icons-material/Close";
import HudFrame from "../components/HudFrame";
import { READER_BACKGROUNDS, type ImageFit, type ReaderSettings, type ReadingDirection, type ReadingMode } from "./readerSettings";
import { DS } from "../theme";

interface Props {
	open: boolean;
	onClose: () => void;
	settings: ReaderSettings;
	onChange: (next: ReaderSettings) => void;
}

const TOGGLE_SX = {
	flex: 1,
	borderRadius: 0,
	border: `1px solid ${DS.border}`,
	color: DS.muted,
	fontFamily: "'Rajdhani', sans-serif",
	fontWeight: 700,
	fontSize: "12px",
	letterSpacing: "1px",
	"&.Mui-selected": {
		backgroundColor: "rgba(194, 24, 24, 0.18)",
		borderColor: DS.red,
		color: "#FFFFFF",
		"&:hover": { backgroundColor: "rgba(194, 24, 24, 0.28)" },
	},
};

function Section({ title, children }: Readonly<{ title: string; children: React.ReactNode }>) {
	return (
		<Box sx={{ mb: 3 }}>
			<Typography
				sx={{
					fontFamily: "'Rajdhani', sans-serif",
					fontSize: "11px",
					fontWeight: 700,
					letterSpacing: "2px",
					textTransform: "uppercase",
					color: DS.redLight,
					mb: 1,
				}}
			>
				{title}
			</Typography>
			{children}
		</Box>
	);
}

export default function ReaderSettingsPanel({ open, onClose, settings, onChange }: Readonly<Props>) {
	return (
		<Drawer
			anchor="right"
			open={open}
			onClose={onClose}
			slotProps={{
				paper: {
					sx: {
						width: 320,
						maxWidth: "90vw",
						backgroundImage: DS.gradientCard,
						borderLeft: `1px solid ${DS.red}`,
						position: "relative",
					},
				},
			}}
		>
			<HudFrame />

			<Box sx={{ p: 2.5 }}>
				<Box sx={{ display: "flex", alignItems: "center", justifyContent: "space-between", mb: 3 }}>
					<Typography
						sx={{
							fontFamily: "'Orbitron', sans-serif",
							fontSize: "15px",
							fontWeight: 900,
							letterSpacing: "1.5px",
							color: "#FFFFFF",
						}}
					>
						AJUSTES
					</Typography>
					<IconButton onClick={onClose} sx={{ color: DS.muted }}>
						<CloseIcon fontSize="small" />
					</IconButton>
				</Box>

				<Section title="Modo de lectura">
					<ToggleButtonGroup
						exclusive
						fullWidth
						value={settings.mode}
						onChange={(_, next: ReadingMode | null) => next && onChange({ ...settings, mode: next })}
					>
						<ToggleButton value="Paged" sx={TOGGLE_SX}>SIMPLE</ToggleButton>
						<ToggleButton value="Double" sx={TOGGLE_SX}>DOBLE</ToggleButton>
						<ToggleButton value="ContinuousVertical" sx={TOGGLE_SX}>TIRA</ToggleButton>
					</ToggleButtonGroup>
				</Section>

				<Section title="Dirección">
					<ToggleButtonGroup
						exclusive
						fullWidth
						value={settings.direction}
						onChange={(_, next: ReadingDirection | null) => next && onChange({ ...settings, direction: next })}
					>
						<ToggleButton value="LeftToRight" sx={TOGGLE_SX}>IZQ → DER</ToggleButton>
						<ToggleButton value="RightToLeft" sx={TOGGLE_SX}>DER → IZQ</ToggleButton>
					</ToggleButtonGroup>
				</Section>

				<Section title="Ajuste de imagen">
					<ToggleButtonGroup
						exclusive
						fullWidth
						value={settings.fit}
						onChange={(_, next: ImageFit | null) => next && onChange({ ...settings, fit: next })}
					>
						<ToggleButton value="width" sx={TOGGLE_SX}>ANCHO</ToggleButton>
						<ToggleButton value="height" sx={TOGGLE_SX}>ALTO</ToggleButton>
						<ToggleButton value="original" sx={TOGGLE_SX}>ORIGINAL</ToggleButton>
					</ToggleButtonGroup>
				</Section>

				<Section title="Fondo del lienzo">
					<Box sx={{ display: "flex", gap: 1 }}>
						{READER_BACKGROUNDS.map((color) => (
							<Box
								key={color}
								component="button"
								type="button"
								aria-label={`Fondo ${color}`}
								onClick={() => onChange({ ...settings, background: color })}
								sx={{
									width: 44,
									height: 34,
									cursor: "pointer",
									backgroundColor: color,
									border: `2px solid ${settings.background === color ? DS.redGlow : DS.border}`,
									clipPath: "polygon(5px 0%, 100% 0%, 100% calc(100% - 5px), calc(100% - 5px) 100%, 0% 100%, 0% 5px)",
								}}
							/>
						))}
					</Box>
				</Section>

				<Typography
					sx={{ fontFamily: "'JetBrains Mono', monospace", fontSize: "10px", color: DS.subtle, lineHeight: 1.7 }}
				>
					ATAJOS<br />
					← → AVANZAR · ESPACIO SIGUIENTE<br />
					F PANTALLA COMPLETA · ESC SALIR
				</Typography>
			</Box>
		</Drawer>
	);
}
