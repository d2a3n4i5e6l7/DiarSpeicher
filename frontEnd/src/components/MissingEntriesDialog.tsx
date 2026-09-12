import {
	Alert,
	Box,
	Button,
	Dialog,
	DialogActions,
	DialogContent,
	DialogTitle,
	Stack,
	Typography,
} from "@mui/material";
import FolderOffIcon from "@mui/icons-material/FolderOff";
import HudFrame from "./HudFrame";
import { type MissingReport } from "../api/endpoints";
import { DS } from "../theme";

interface Props {
	report: MissingReport;
	working: boolean;
	onClose: () => void;
	onPurge: () => void;
}

/**
 * Lo que quedo en el indice sin nada detras en el disco. Purgarlo no toca un solo fichero:
 * los ficheros ya no estan, que es justo el motivo de que aparezcan aqui.
 */
export default function MissingEntriesDialog({ report, working, onClose, onPurge }: Readonly<Props>) {
	return (
		<Dialog open onClose={onClose} maxWidth="sm" fullWidth>
			<HudFrame />
			<DialogTitle>// Entradas sin carpeta</DialogTitle>

			<DialogContent sx={{ p: 3 }}>
				<Typography sx={{ color: DS.platinum, mb: 2 }}>
					El escaneo encontró {report.series.length} series y {report.totalVolumes} tomos que siguen en el índice
					pero ya no están en el disco. ¿Los quito?
				</Typography>

				<Box sx={{ maxHeight: 240, overflowY: "auto", border: `1px solid ${DS.border}`, mb: 2 }}>
					{report.series.map((serie) => (
						<Stack
							key={serie.id}
							direction="row"
							spacing={1}
							sx={{ alignItems: "center", px: 1.5, py: 0.8, borderBottom: `1px solid ${DS.borderSoft}` }}
						>
							<FolderOffIcon sx={{ fontSize: 15, color: DS.redLight, flexShrink: 0 }} />
							<Box sx={{ flexGrow: 1, minWidth: 0 }}>
								<Typography
									noWrap
									sx={{ fontFamily: "'Rajdhani', sans-serif", fontWeight: 600, fontSize: "13px", color: DS.platinum }}
								>
									{serie.name}
								</Typography>
								<Typography noWrap sx={{ fontFamily: "'JetBrains Mono', monospace", fontSize: "10px", color: DS.subtle }}>
									{serie.path}
								</Typography>
							</Box>
							<Typography sx={{ fontFamily: "'JetBrains Mono', monospace", fontSize: "11px", color: DS.muted }}>
								{serie.volumeCount}
							</Typography>
						</Stack>
					))}

					{report.orphanVolumes > 0 && (
						<Stack direction="row" spacing={1} sx={{ alignItems: "center", px: 1.5, py: 0.8 }}>
							<FolderOffIcon sx={{ fontSize: 15, color: DS.subtle, flexShrink: 0 }} />
							<Typography sx={{ fontSize: "12px", color: DS.muted }}>
								{report.orphanVolumes} tomos sueltos, en series que sí siguen en disco
							</Typography>
						</Stack>
					)}
				</Box>

				<Alert severity="info" sx={{ mb: 1 }}>
					No se borra nada del disco: esos ficheros ya no existen. Solo se limpia el índice.
				</Alert>

				{report.totalReadingSessions > 0 && (
					<Alert severity="warning">
						Se perderá el progreso de lectura de {report.totalReadingSessions}{" "}
						{report.totalReadingSessions === 1 ? "tomo" : "tomos"}. Si los ficheros vuelven al disco, se
						reindexarán como nuevos y empezarán desde la página 1.
					</Alert>
				)}
			</DialogContent>

			<DialogActions>
				<Button onClick={onClose} sx={{ color: DS.muted }}>
					DEJARLOS
				</Button>
				<Button variant="contained" disabled={working} onClick={onPurge}>
					{working ? "LIMPIANDO..." : "QUITAR DEL ÍNDICE"}
				</Button>
			</DialogActions>
		</Dialog>
	);
}
