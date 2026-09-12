import { Alert, Box, Button, Chip, LinearProgress, Stack, Typography } from "@mui/material";
import CloudDownloadIcon from "@mui/icons-material/CloudDownload";
import UploadFileIcon from "@mui/icons-material/UploadFile";
import StorageIcon from "@mui/icons-material/Storage";
import { useCallback, useEffect, useRef, useState } from "react";
import PageHeader from "../components/PageHeader";
import HudFrame from "../components/HudFrame";
import { formatBytes } from "../catalog/mediaHelpers";
import { READABLE_WIDTH } from "../catalog/layout";
import { METADATA_ARCHIVE_EXTENSIONS, metadataApi, type MetadataStatus } from "../api/endpoints";
import { DS } from "../theme";

/** Mientras la ingesta trabaja se sondea; parada, no se molesta al servidor. */
const POLL_BUSY_MS = 1500;

const STATE_LABELS: Record<string, string> = {
	Absent: "SIN VOLCADO",
	Downloading: "DESCARGANDO",
	Decompressing: "DESCOMPRIMIENDO",
	Indexing: "INDEXANDO",
	Ready: "LISTO",
	Failed: "FALLIDO",
};

function stateColor(state: string): string {
	if (state === "Ready") return "#22C55E";
	if (state === "Failed") return DS.redGlow;
	if (state === "Absent") return DS.subtle;
	return "#F59E0B";
}

export default function MetadataPage() {
	const [status, setStatus] = useState<MetadataStatus | null>(null);
	const [error, setError] = useState<string | null>(null);
	const [dragging, setDragging] = useState(false);
	const [uploading, setUploading] = useState(false);
	const fileInputRef = useRef<HTMLInputElement | null>(null);

	const refresh = useCallback(async () => {
		const next = await metadataApi.status();
		setStatus(next);
		return next;
	}, []);

	useEffect(() => {
		let mounted = true;
		let timer: number | undefined;

		const tick = async () => {
			try {
				const next = await metadataApi.status();
				if (!mounted) return;
				setStatus(next);
				// Solo se vuelve a preguntar si hay algo en marcha: parado, un sondeo cada
				// segundo y medio es ruido contra el servidor para siempre.
				if (next.busy) {
					timer = window.setTimeout(() => void tick(), POLL_BUSY_MS);
				}
			} catch (err: unknown) {
				if (mounted) setError(err instanceof Error ? err.message : "No se pudo leer el estado del volcado.");
			}
		};

		void tick();
		return () => {
			mounted = false;
			if (timer !== undefined) window.clearTimeout(timer);
		};
	}, []);

	const startDownload = async () => {
		setError(null);
		try {
			await metadataApi.download();
		} catch (err: unknown) {
			setError(err instanceof Error ? err.message : "No se pudo iniciar la descarga.");
		} finally {
			await refresh().catch(() => undefined);
		}
	};

	const submitFile = async (file: File) => {
		setError(null);
		const name = file.name.toLowerCase();
		if (!METADATA_ARCHIVE_EXTENSIONS.some((ext) => name.endsWith(ext))) {
			setError(`"${file.name}" no es un volcado válido. Se aceptan ${METADATA_ARCHIVE_EXTENSIONS.join(", ")}.`);
			return;
		}

		setUploading(true);
		try {
			await metadataApi.import(file);
		} catch (err: unknown) {
			setError(err instanceof Error ? err.message : "No se pudo ingerir el fichero.");
		} finally {
			setUploading(false);
			await refresh().catch(() => undefined);
		}
	};

	const busy = status?.busy === true || uploading;
	const state = status?.state ?? "Absent";
	const percent = status?.percent;

	return (
		<Box>
			<PageHeader
				title="Metadata externa"
				subtitle="Catálogo de MangaBaka para rellenar autores, géneros, estado y portadas de las series."
			/>

			{error && (
				<Alert severity="error" sx={{ mb: 3 }} onClose={() => setError(null)}>
					{error}
				</Alert>
			)}

			<Box
				sx={{
					position: "relative",
					p: 3,
					mb: 3,
					maxWidth: READABLE_WIDTH,
					backgroundImage: DS.gradientCard,
					border: `1px solid ${DS.border}`,
				}}
			>
				<HudFrame />

				<Stack direction="row" spacing={2} sx={{ alignItems: "center", mb: 2 }}>
					<StorageIcon sx={{ color: stateColor(state), fontSize: 28 }} />
					<Box sx={{ flexGrow: 1, minWidth: 0 }}>
						<Stack direction="row" spacing={1.5} sx={{ alignItems: "center", flexWrap: "wrap", gap: 1 }}>
							<Chip
								size="small"
								label={STATE_LABELS[state] ?? state.toUpperCase()}
								sx={{ backgroundColor: "rgba(194, 24, 24, 0.15)", color: stateColor(state), border: `1px solid ${DS.redDark}` }}
							/>
							{status && status.seriesCount > 0 && (
								<Box component="span" className="ds-pill-mono">
									{status.seriesCount.toLocaleString("es-ES")} SERIES
								</Box>
							)}
							{status && status.sizeBytes > 0 && (
								<Box component="span" className="ds-pill-mono">
									{formatBytes(status.sizeBytes)}
								</Box>
							)}
						</Stack>
						{status?.message && (
							<Typography sx={{ fontFamily: "'JetBrains Mono', monospace", fontSize: "11px", color: DS.muted, mt: 1 }}>
								{status.message}
							</Typography>
						)}
					</Box>
				</Stack>

				{busy && (
					<LinearProgress
						variant={percent === null || percent === undefined ? "indeterminate" : "determinate"}
						value={percent ?? 0}
						sx={{ height: 4, mb: 2 }}
					/>
				)}

				<Stack direction={{ xs: "column", sm: "row" }} spacing={1.5}>
					<Button
						variant="contained"
						startIcon={<CloudDownloadIcon />}
						onClick={() => {
							void startDownload();
						}}
						disabled={busy}
						className="btn-tactical"
						sx={{ background: DS.red, borderColor: DS.redGlow, color: "#FFFFFF", px: 3 }}
					>
						DESCARGAR BASE DE DATOS DE MANGA
					</Button>
				</Stack>

				<Typography sx={{ fontFamily: "'Inter', sans-serif", fontSize: "12px", color: DS.subtle, mt: 2, lineHeight: 1.7 }}>
					La descarga son unos 390 MB comprimidos que se quedan en <code>manga_database/</code> como 3,5 GB de
					SQLite. Se hace una vez y se refresca cada pocos meses. Datos de MangaBaka bajo licencia
					CC BY-NC-SA 4.0: uso no comercial con atribución.
				</Typography>
			</Box>

			<Box
				onDragOver={(e) => {
					e.preventDefault();
					setDragging(true);
				}}
				onDragLeave={() => setDragging(false)}
				onDrop={(e) => {
					e.preventDefault();
					setDragging(false);
					const file = e.dataTransfer.files[0];
					if (file) void submitFile(file);
				}}
				onClick={() => fileInputRef.current?.click()}
				sx={{
					maxWidth: READABLE_WIDTH,
					border: `2px dashed ${dragging ? DS.redGlow : DS.border}`,
					backgroundColor: dragging ? "rgba(194, 24, 24, 0.12)" : DS.bgSunken,
					p: 5,
					textAlign: "center",
					cursor: busy ? "not-allowed" : "pointer",
					opacity: busy ? 0.5 : 1,
					transition: "all 0.2s ease",
					"&:hover": busy ? undefined : { borderColor: DS.red },
				}}
			>
				<input
					type="file"
					ref={fileInputRef}
					style={{ display: "none" }}
					accept=".zst,.gz,.tgz"
					disabled={busy}
					onChange={(e) => {
						const file = e.target.files?.[0];
						if (file) void submitFile(file);
						e.target.value = "";
					}}
				/>
				<UploadFileIcon sx={{ fontSize: 40, color: DS.redGlow, mb: 1 }} />
				<Typography
					sx={{
						fontFamily: "'Rajdhani', sans-serif",
						fontSize: "16px",
						fontWeight: 700,
						letterSpacing: "1px",
						textTransform: "uppercase",
						color: DS.platinum,
					}}
				>
					O arrastra aquí un volcado que ya tengas
				</Typography>
				<Typography sx={{ fontFamily: "'JetBrains Mono', monospace", fontSize: "11px", color: DS.subtle, mt: 0.75 }}>
					{METADATA_ARCHIVE_EXTENSIONS.join(" · ").toUpperCase()}
				</Typography>
			</Box>
		</Box>
	);
}
