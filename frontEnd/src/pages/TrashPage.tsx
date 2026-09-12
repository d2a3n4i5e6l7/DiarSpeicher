import { useCallback, useEffect, useState } from "react";
import { Alert, Box, Button, LinearProgress, Stack, Typography } from "@mui/material";
import RestoreIcon from "@mui/icons-material/Restore";
import FolderIcon from "@mui/icons-material/Folder";
import InsertDriveFileIcon from "@mui/icons-material/InsertDriveFile";
import PageHeader from "../components/PageHeader";
import HudFrame from "../components/HudFrame";
import { trashApi, type TrashEntry } from "../api/endpoints";
import { READABLE_COLUMN } from "../catalog/layout";
import { formatBytes } from "../catalog/bytes";
import { formatDuration } from "../catalog/useScanProgress";
import { DS } from "../theme";

export default function TrashPage() {
	const [entries, setEntries] = useState<TrashEntry[] | null>(null);
	const [error, setError] = useState<string | null>(null);
	const [working, setWorking] = useState<string | null>(null);

	const load = useCallback(async () => {
		try {
			setEntries(await trashApi.list());
		} catch (e) {
			setError(e instanceof Error ? e.message : "No se pudo leer la papelera.");
			setEntries([]);
		}
	}, []);

	// La cuenta atras corre en el servidor; sin repetir la lista, un elemento caducado
	// seguiria ofreciendo un boton de restaurar que ya no funciona.
	const [tick, setTick] = useState(0);

	useEffect(() => {
		const timer = window.setInterval(() => {
			setTick((prev) => prev + 1);
		}, 30000);

		return () => {
			window.clearInterval(timer);
		};
	}, []);

	useEffect(() => {
		let cancelled = false;
		trashApi.list().then(
			(list) => {
				if (!cancelled) setEntries(list);
			},
			(e: unknown) => {
				if (!cancelled) {
					setError(e instanceof Error ? e.message : "No se pudo leer la papelera.");
					setEntries([]);
				}
			},
		);

		return () => {
			cancelled = true;
		};
	}, [tick]);

	return (
		<Box sx={READABLE_COLUMN}>
			<PageHeader
				title="Restaurar"
				subtitle="Lo borrado del disco se guarda una hora antes de irse de verdad. Pasado el plazo, no hay vuelta atrás."
			/>

			{error && (
				<Alert severity="error" sx={{ mb: 3 }} onClose={() => { setError(null); }}>
					{error}
				</Alert>
			)}

			{entries === null && <LinearProgress sx={{ height: 2 }} />}

			{entries?.length === 0 && (
				<Box sx={{ position: "relative", p: 4, textAlign: "center", backgroundImage: DS.gradientCard, border: `1px solid ${DS.border}` }}>
					<HudFrame />
					<Typography sx={{ fontFamily: "'JetBrains Mono', monospace", fontSize: "12px", color: DS.subtle }}>
						La papelera está vacía.
					</Typography>
				</Box>
			)}

			{entries?.map((entry) => (
				<Box
					key={entry.id}
					sx={{
						position: "relative",
						display: "flex",
						alignItems: "center",
						gap: 2,
						p: 2,
						mb: 1.5,
						backgroundImage: DS.gradientCard,
						border: `1px solid ${DS.border}`,
					}}
				>
					<HudFrame />
					{entry.isDirectory ? (
						<FolderIcon sx={{ color: DS.redGlow, flexShrink: 0 }} />
					) : (
						<InsertDriveFileIcon sx={{ color: DS.redGlow, flexShrink: 0 }} />
					)}

					<Box sx={{ flexGrow: 1, minWidth: 0 }}>
						<Typography
							noWrap
							sx={{ fontFamily: "'Rajdhani', sans-serif", fontWeight: 700, fontSize: "15px", color: DS.platinum }}
						>
							{entry.name}
						</Typography>
						<Typography noWrap sx={{ fontFamily: "'JetBrains Mono', monospace", fontSize: "11px", color: DS.subtle }}>
							{entry.originalPath}
						</Typography>
						<Stack direction="row" spacing={2} sx={{ mt: 0.5, flexWrap: "wrap" }}>
							<Typography sx={{ fontFamily: "'JetBrains Mono', monospace", fontSize: "11px", color: DS.muted }}>
								{entry.fileCount} ficheros · {formatBytes(entry.bytes)}
							</Typography>
							<Typography
								sx={{
									fontFamily: "'JetBrains Mono', monospace",
									fontSize: "11px",
									color: entry.expiresInSeconds < 300 ? DS.redGlow : DS.warn,
								}}
							>
								{entry.expiresInSeconds > 0 ? `se borra en ${formatDuration(entry.expiresInSeconds)}` : "caducado"}
							</Typography>
						</Stack>
					</Box>

					<Button
						variant="outlined"
						startIcon={<RestoreIcon />}
						disabled={working === entry.id || entry.expiresInSeconds <= 0}
						onClick={() => {
							setWorking(entry.id);
							trashApi
								.restore(entry.id)
								.then(load)
								.catch((e: unknown) => {
									setError(e instanceof Error ? e.message : "No se pudo restaurar.");
								})
								.finally(() => {
									setWorking(null);
								});
						}}
						sx={{ flexShrink: 0 }}
					>
						Restaurar
					</Button>
				</Box>
			))}
		</Box>
	);
}
