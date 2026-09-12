import {
	Alert,
	Box,
	Button,
	CircularProgress,
	Dialog,
	DialogActions,
	DialogContent,
	DialogTitle,
	Tab,
	Tabs,
	Typography,
} from "@mui/material";
import UploadFileIcon from "@mui/icons-material/UploadFile";
import RestartAltIcon from "@mui/icons-material/RestartAlt";
import { useEffect, useRef, useState } from "react";
import HudFrame from "./HudFrame";
import { COVER_GRID } from "../catalog/layout";
import { mediaTitle } from "../catalog/mediaHelpers";
import { mediaApi, seriesApi, type MediaItem } from "../api/endpoints";
import { DS } from "../theme";

interface Props {
	open: boolean;
	seriesId: string;
	onClose: () => void;
	onChanged: () => void;
}

interface Loaded {
	key: string;
	volumes: MediaItem[];
}

const ACCEPTED = [".jpg", ".jpeg", ".png", ".webp"];

/**
 * Portada de la serie sin salir a internet: o la de uno de sus tomos, que ya están
 * indexados y en disco, o una imagen propia. Buscar carátulas en la red para algo que ya
 * está dentro del archivo era el camino largo.
 */
export default function SeriesCoverDialog({ open, seriesId, onClose, onChanged }: Readonly<Props>) {
	const [loaded, setLoaded] = useState<Loaded | null>(null);
	const [tab, setTab] = useState(0);
	const [busy, setBusy] = useState(false);
	const [error, setError] = useState<string | null>(null);
	const fileInputRef = useRef<HTMLInputElement | null>(null);

	useEffect(() => {
		if (!open) return;

		let mounted = true;
		seriesApi
			.media(seriesId, 0, 100)
			.then((result) => {
				if (mounted) setLoaded({ key: seriesId, volumes: result.data });
			})
			.catch(() => {
				if (mounted) setLoaded({ key: seriesId, volumes: [] });
			});

		return () => {
			mounted = false;
		};
	}, [open, seriesId]);

	const fresh = loaded?.key === seriesId ? loaded : null;
	const volumes = fresh?.volumes ?? null;

	const run = async (action: () => Promise<unknown>) => {
		setBusy(true);
		setError(null);
		try {
			await action();
			onChanged();
			onClose();
		} catch (err: unknown) {
			setError(err instanceof Error ? err.message : "No se pudo cambiar la portada.");
		} finally {
			setBusy(false);
		}
	};

	const submitFile = (file: File) => {
		const name = file.name.toLowerCase();
		if (!ACCEPTED.some((ext) => name.endsWith(ext))) {
			setError(`"${file.name}" no vale. Se aceptan ${ACCEPTED.join(", ")}.`);
			return;
		}
		void run(() => seriesApi.uploadCover(seriesId, file));
	};

	return (
		<Dialog open={open} onClose={onClose} maxWidth="md" fullWidth>
			<HudFrame />
			<DialogTitle>Portada de la serie</DialogTitle>

			<DialogContent sx={{ p: 3 }}>
				{error && (
					<Alert severity="error" sx={{ mb: 2 }}>
						{error}
					</Alert>
				)}

				<Tabs
					value={tab}
					onChange={(_, next: number) => setTab(next)}
					sx={{
						borderBottom: `1px solid ${DS.border}`,
						mb: 3,
						"& .MuiTab-root": {
							fontFamily: "'Rajdhani', sans-serif",
							fontWeight: 700,
							letterSpacing: "1px",
							color: DS.muted,
							"&.Mui-selected": { color: "#FFFFFF" },
						},
						"& .MuiTabs-indicator": { backgroundColor: DS.red, height: 2 },
					}}
				>
					<Tab label="Desde un tomo" />
					<Tab label="Portada personalizada" />
				</Tabs>

				{tab === 0 && volumes === null && (
					<Box sx={{ display: "flex", justifyContent: "center", py: 6 }}>
						<CircularProgress size={26} thickness={5} />
					</Box>
				)}

				{tab === 0 && volumes !== null && volumes.length === 0 && (
					<Typography sx={{ fontFamily: "'Inter', sans-serif", fontSize: "13px", color: DS.muted, py: 3 }}>
						Esta serie no tiene tomos indexados de los que sacar una portada.
					</Typography>
				)}

				{tab === 0 && volumes !== null && volumes.length > 0 && (
					<Box sx={{ display: "grid", gap: 2, gridTemplateColumns: COVER_GRID }}>
						{volumes.map((volume) => (
							<Box
								key={volume.id}
								component="button"
								type="button"
								disabled={busy}
								onClick={() => {
									void run(() => seriesApi.setCoverFromMedia(seriesId, volume.id));
								}}
								sx={{
									p: 0,
									cursor: busy ? "wait" : "pointer",
									background: "none",
									border: `2px solid ${DS.border}`,
									transition: "border-color 0.2s ease",
									"&:hover": { borderColor: DS.redGlow },
								}}
							>
								<Box
									component="img"
									src={mediaApi.thumbnailUrl(volume.id)}
									alt={mediaTitle(volume)}
									loading="lazy"
									sx={{ width: "100%", aspectRatio: "2 / 3", objectFit: "cover", display: "block" }}
								/>
								<Typography
									sx={{
										fontFamily: "'JetBrains Mono', monospace",
										fontSize: "10px",
										color: DS.muted,
										p: 0.75,
										overflow: "hidden",
										textOverflow: "ellipsis",
										whiteSpace: "nowrap",
									}}
								>
									{mediaTitle(volume)}
								</Typography>
							</Box>
						))}
					</Box>
				)}

				{tab === 1 && (
					<Box
						onClick={() => fileInputRef.current?.click()}
						sx={{
							border: `2px dashed ${DS.border}`,
							backgroundColor: DS.bgSunken,
							p: 6,
							textAlign: "center",
							cursor: busy ? "wait" : "pointer",
							"&:hover": { borderColor: DS.red },
						}}
					>
						<input
							type="file"
							ref={fileInputRef}
							style={{ display: "none" }}
							accept={ACCEPTED.join(",")}
							disabled={busy}
							onChange={(e) => {
								const file = e.target.files?.[0];
								if (file) submitFile(file);
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
							Elige una imagen de tu equipo
						</Typography>
						<Typography sx={{ fontFamily: "'JetBrains Mono', monospace", fontSize: "11px", color: DS.subtle, mt: 0.75 }}>
							{ACCEPTED.join(" · ").toUpperCase()}
						</Typography>
					</Box>
				)}
			</DialogContent>

			<DialogActions sx={{ justifyContent: "space-between" }}>
				<Button
					startIcon={<RestartAltIcon />}
					disabled={busy}
					onClick={() => {
						void run(() => seriesApi.clearCover(seriesId));
					}}
					sx={{ color: DS.muted, "&:hover": { color: DS.redGlow } }}
				>
					VOLVER A LA AUTOMÁTICA
				</Button>
				<Button onClick={onClose} disabled={busy} sx={{ color: DS.muted }}>
					CERRAR
				</Button>
			</DialogActions>
		</Dialog>
	);
}
